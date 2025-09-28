using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Models;

public partial class EnemyManager : Node
{
    #region Configuration
    [Export] public float GlobalHideTime { get; set; } = 40f; // Ghost death -> both respawn
    [Export] public float LocalHideTime { get; set; } = 10f;  // Monster depleted -> monster respawn
    [Export] public float MinSpawnDistance { get; set; } = 300.0f; // Minimum distance from player spawn for monsters

    public static EnemyManager Instance { get; private set; }
    #endregion

    #region Core Data
    private readonly List<EnemyPair> _pairs = new();
    private readonly Dictionary<Monster, EnemyPair> _monsterLookup = new();
    private readonly Dictionary<GhostMonster, EnemyPair> _ghostLookup = new();
    private readonly Dictionary<EnemyPair, Timer> _respawnTimers = new();
    private readonly Dictionary<EnemyPair, RespawnType> _respawnTypes = new();

    private bool _isPlayerDead = false;
    #endregion

    #region Respawn Types
    private enum RespawnType
    {
        MonsterOnly,    // Only monster respawns (local timer)
        FullPair        // Both monster and ghost respawn (global timer)
    }
    #endregion

    #region Initialization
    public override void _Ready()
    {
        if (Instance != null)
        {
            QueueFree();
            return;
        }

        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        GameEventSystem.PlayerDied += OnPlayerDied;
        GameEventSystem.GameRestart += OnGameRestart;

        if (RealityManager.Instance != null)
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;

        CallDeferred(nameof(Initialize));
    }

    private void Initialize()
    {
        DiscoverPairs();
        ConnectLifeEvents();
    }
    #endregion

    #region Entity Discovery
    private void DiscoverPairs()
    {
        ClearAll();

        var monsters = GetTree().GetNodesInGroup("enemies").OfType<Monster>().Where(IsInstanceValid).ToList();
        var ghosts = GetTree().GetNodesInGroup("ghost_monster").OfType<GhostMonster>().Where(IsInstanceValid).ToList();

        // Pair by proximity
        var usedGhosts = new HashSet<GhostMonster>();
        foreach (var monster in monsters)
        {
            var closestGhost = ghosts
                .Where(g => !usedGhosts.Contains(g))
                .OrderBy(g => monster.GlobalPosition.DistanceTo(g.GlobalPosition))
                .FirstOrDefault();

            var pair = new EnemyPair
            {
                Monster = monster,
                Ghost = closestGhost,
                PairId = closestGhost != null ? $"{monster.Name}_{closestGhost.Name}" : $"{monster.Name}_solo",
                IsHidden = false
            };

            _pairs.Add(pair);
            _monsterLookup[monster] = pair;

            if (closestGhost != null)
            {
                _ghostLookup[closestGhost] = pair;
                usedGhosts.Add(closestGhost);
            }
        }

        Log.Debug($"Created {_pairs.Count} enemy pairs", "EnemyManager");
    }

    private void ConnectLifeEvents()
    {
        foreach (var pair in _pairs)
        {
            if (IsInstanceValid(pair.Monster?.LifeSystem))
            {
                pair.Monster.LifeSystem.LifeChanged += (current, max, purple) => OnMonsterLifeChanged(pair.Monster, current);
                pair.Monster.LifeSystem.MonsterDied += () => OnMonsterDied(pair.Monster);
            }

            if (IsInstanceValid(pair.Ghost?.LifeSystem))
            {
                pair.Ghost.LifeSystem.MonsterDied += () => OnGhostDied(pair.Ghost);
            }
        }
    }
    #endregion

    #region Life Events
    private void OnMonsterLifeChanged(Monster monster, int currentLife)
    {
        if (currentLife <= 0)
            HandleMonsterDepleted(monster);
    }

    private void OnMonsterDied(Monster monster) => HandleMonsterDepleted(monster);
    private void OnGhostDied(GhostMonster ghost) => HandleGhostDeath(ghost);
    #endregion

    #region Death Handling - FIXED
    private void HandleMonsterDepleted(Monster monster)
    {
        var pair = GetPair(monster);
        if (pair == null) return;

        // Check if pair already has a global timer (ghost death overrides monster depletion)
        if (_respawnTypes.TryGetValue(pair, out var existingType) && existingType == RespawnType.FullPair)
        {
            Log.Debug("Monster depleted but pair already scheduled for global respawn - ignoring local timer", "EnemyManager");
            return;
        }

        Log.Warning($"Monster depleted - hiding for {LocalHideTime}s (local timer)", "EnemyManager");

        HideMonster(pair.Monster);
        pair.IsHidden = true;
        ScheduleRespawn(pair, LocalHideTime, RespawnType.MonsterOnly);
    }

    public void HandleGhostDeath(GhostMonster ghost)
    {
        var pair = GetPair(ghost);
        if (pair == null)
        {
            // Unpaired ghost - just destroy it and check for auto-return
            if (IsInstanceValid(ghost))
                ghost.FadeOutAndDestroy();

            CheckForAutoReturn();
            return;
        }

        HandleGhostDeath(pair); // This will also call CheckForAutoReturn
    }

    public void HandleGhostDeath(EnemyPair pair)
    {
        if (pair == null) return;

        Log.Warning($"Ghost died - damage sent to global ghost life system", "EnemyManager");

        // NO MORE GLOBAL TIMER - Ghost monsters are now handled by global life system
        CancelRespawn(pair);

        // Disable the ghost immediately
        if (IsInstanceValid(pair.Ghost))
        {
            try
            {
                pair.Ghost.FadeOutAndDisable();
            }
            catch (Exception ex)
            {
                Log.Error($"EnemyManager: FadeOutAndDisable failed for {pair.Ghost?.Name}: {ex.Message}", "EnemyManager");
                try { pair.Ghost.StopReplay(); } catch { }
                try { pair.Ghost.Hide(); } catch { }
            }
        }

        // Monster in current reality keeps its local hide behavior
        // No global hiding - only local hide time when monster depleted
        pair.IsHidden = false; // Reset pair hidden state since we don't use global timer anymore

        // NEW: Check if we should auto-return to CurrentReality
        CheckForAutoReturn();
    }

    // NEW METHOD: Check if all ghosts are dead and trigger auto-return
    private void CheckForAutoReturn()
    {
        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        if (currentReality != Reality.PastReality)
        {
            return; // Only auto-return when in PastReality
        }

        // Count active (visible and alive) ghosts
        int activeGhosts = 0;
        foreach (var pair in _pairs)
        {
            if (IsInstanceValid(pair.Ghost) && pair.Ghost.LifeSystem != null && !pair.Ghost.LifeSystem.IsDead)
            {
                activeGhosts++;
            }
        }

        Log.Debug($"CheckForAutoReturn: Found {activeGhosts} active ghosts in PastReality", "EnemyManager");

        if (activeGhosts == 0)
        {
            Log.Replay("All ghosts eliminated - triggering auto-return to CurrentReality", "EnemyManager");

            // INCREASED DELAY: Give more time for death effects and ghost cleanup
            GetTree().CreateTimer(1.5f).Timeout += () =>
            {
                if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
                {
                    var stillInPast = RealityManager.Instance.CurrentReality == Reality.PastReality;
                    if (stillInPast)
                    {
                        Log.Replay("EnemyManager executing auto-return to CurrentReality after 1.5s delay", "EnemyManager");
                        RealityManager.Instance.EndReplay();
                    }
                    else
                    {
                        Log.Debug("Auto-return cancelled - already returned to CurrentReality", "EnemyManager");
                    }
                }
            };
        }
    }
    #endregion

    #region Respawn System - SIMPLIFIED
    private void ScheduleRespawn(EnemyPair pair, float seconds, RespawnType type)
    {
        // Always cancel existing timer first
        CancelRespawn(pair);
        var timer = new Timer { OneShot = true, WaitTime = seconds };
        // Ensure respawn timers continue to run even when the SceneTree is paused
        // (e.g. during player-death pause). This prevents "slow" or frozen countdowns.
        timer.ProcessMode = ProcessModeEnum.Always;
        AddChild(timer);
        _respawnTimers[pair] = timer;
        _respawnTypes[pair] = type;

        timer.Timeout += () =>
        {
            if (type == RespawnType.MonsterOnly)
                RespawnMonster(pair);
            else
                RespawnPair(pair);

            timer.QueueFree();
            _respawnTimers.Remove(pair);
            _respawnTypes.Remove(pair);
        };

        timer.Start();

        Log.Debug($"Scheduled {type} respawn for pair {pair.PairId} in {seconds}s", "EnemyManager");
    }

    private void CancelRespawn(EnemyPair pair)
    {
        if (_respawnTimers.TryGetValue(pair, out var timer))
        {
            Log.Debug($"Cancelled existing respawn timer for pair {pair.PairId}", "EnemyManager");
            timer.Stop();
            timer.QueueFree();
            _respawnTimers.Remove(pair);
            _respawnTypes.Remove(pair);
        }
    }

    private void RespawnMonster(EnemyPair pair)
    {
        if (!IsInstanceValid(pair.Monster)) return;

        Log.System($"Respawning monster {pair.Monster.Name} after local depletion", "EnemyManager");

        pair.Monster.LifeSystem?.Reset();

        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        pair.Monster.ForceEnableAfterRespawn();

        if (currentReality == Reality.CurrentReality)
        {
            pair.Monster.ForceShow();

            // Notify ReplayRecorder that monster is now visible again
            if (ReplayRecorder.Instance != null)
            {
                ReplayRecorder.Instance.OnMonsterVisibilityChanged(pair.Monster, true);
            }
        }

        pair.IsHidden = false;
    }

    private void RespawnPair(EnemyPair pair)
    {
        Log.System($"Respawning full pair {pair.PairId} after global timeout", "EnemyManager");

        // Respawn monster
        if (IsInstanceValid(pair.Monster))
        {
            pair.Monster.LifeSystem?.Reset();
            pair.Monster.ForceEnableAfterRespawn();

            var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
            if (currentReality == Reality.CurrentReality)
            {
                pair.Monster.ForceShow();

                // Ensure monster is actually visible after respawn (bug fix)
                CallDeferred(nameof(VerifyMonsterVisibility), pair.Monster);

                // Notify ReplayRecorder that monster is now visible again
                if (ReplayRecorder.Instance != null)
                {
                    ReplayRecorder.Instance.OnMonsterVisibilityChanged(pair.Monster, true);
                }
            }
        }

        // Reset ghost life (ghost will be recreated by replay system when needed)
        if (IsInstanceValid(pair.Ghost))
        {
            pair.Ghost.LifeSystem?.Reset();
        }

        pair.IsHidden = false;
    }

    private void VerifyMonsterVisibility(Monster monster)
    {
        if (!IsInstanceValid(monster)) return;

        // Double-check visibility state and force if needed
        if (!monster.Visible)
        {
            Log.Warning($"Monster {monster.Name} not visible after respawn - forcing visibility", "EnemyManager");
            monster.Show();
            monster.Visible = true;
        }
    }
    #endregion

    #region Reality Management
    private void OnRealityChanged(Reality newReality)
    {
        Log.System($"Reality changed to {newReality}", "EnemyManager");

        // Check if global ghost life is defeated
        bool isGlobalGhostLifeDefeated = GlobalGhostLifeManager.Instance?.IsDefeated ?? false;

        foreach (var pair in _pairs)
        {
            if (!IsInstanceValid(pair.Monster)) continue;

            if (newReality == Reality.CurrentReality)
            {
                // Only show monster if global ghost life is NOT defeated
                if (!isGlobalGhostLifeDefeated && pair.Monster.LifeSystem?.CurrentRedHearts > 0 && !pair.IsHidden)
                {
                    pair.Monster.ForceShow();
                }
                else if (isGlobalGhostLifeDefeated)
                {
                    // Keep monster hidden if global ghost life is defeated
                    pair.Monster.ForceHide();
                    Log.Debug($"Monster {pair.Monster.Name} kept hidden - global ghost life defeated", "EnemyManager");
                }

                if (IsInstanceValid(pair.Ghost))
                    pair.Ghost.FadeOutAndDestroy();
            }
            else if (newReality == Reality.PastReality)
            {
                pair.Monster.ForceHide();
                // Ghost visibility handled by replay system
            }
        }
    }
    #endregion

    #region Public API - SIMPLIFIED
    public EnemyPair GetPair(Monster monster) => _monsterLookup.GetValueOrDefault(monster);
    public EnemyPair GetPair(GhostMonster ghost) => _ghostLookup.GetValueOrDefault(ghost);

    public int GetMonsterRedHearts(Monster monster) => monster?.LifeSystem?.CurrentRedHearts ?? 0;

    public int GetGhostPurpleHearts(GhostMonster ghost)
    {
        if (!IsInstanceValid(ghost)) return 0;

        try
        {
            var ghostType = ghost.GetType();

            // Try to get _ghostLife first (GhostMonsterLife - purple hearts only)
            var ghostLifeField = ghostType.GetField("_ghostLife", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (ghostLifeField != null)
            {
                var ghostLife = ghostLifeField.GetValue(ghost) as Node;
                if (ghostLife != null && IsInstanceValid(ghostLife))
                {
                    var currentLifeProp = ghostLife.GetType().GetProperty("CurrentLife");
                    if (currentLifeProp != null)
                        return (int)currentLifeProp.GetValue(ghostLife);
                }
            }

            // Fallback to _monsterLife for backward compatibility
            var monsterLifeField = ghostType.GetField("_monsterLife", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (monsterLifeField != null)
            {
                var monsterLife = monsterLifeField.GetValue(ghost) as Node;
                if (monsterLife != null && IsInstanceValid(monsterLife))
                {
                    var purpleHeartsProp = monsterLife.GetType().GetProperty("CurrentPurpleHearts");
                    if (purpleHeartsProp != null)
                        return (int)purpleHeartsProp.GetValue(monsterLife);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to get purple hearts for ghost {ghost.Name}: {ex.Message}", "EnemyManager");
        }

        return 0;
    }

    public float GetRespawnTimeLeft(EnemyPair pair)
    {
        if (pair == null) return -1f;
        if (_respawnTimers.TryGetValue(pair, out var timer))
        {
            try
            {
                return (float)timer.TimeLeft;
            }
            catch
            {
                return -1f;
            }
        }
        return -1f;
    }

    public string GetRespawnType(EnemyPair pair)
    {
        if (pair == null) return "None";
        return _respawnTypes.TryGetValue(pair, out var type) ? type.ToString() : "None";
    }

    public bool IsMonsterHiddenByPair(Monster monster) => GetPair(monster)?.IsHidden ?? false;

    /// Returns true if the specified monster is part of a pair that currently has a respawn timer scheduled.
    /// This is used by other systems (like ReplayOrchestrator) to avoid showing monsters that should remain hidden
    /// during a global respawn timeout even when reality returns to current.
    public bool IsMonsterScheduledForRespawn(Monster monster)
    {
        var pair = GetPair(monster);
        if (pair == null) return false;
        return _respawnTimers.ContainsKey(pair);
    }

    public void ResetAllEnemies()
    {
        CancelAllRespawns();

        foreach (var pair in _pairs)
        {
            if (IsInstanceValid(pair.Monster))
                pair.Monster.Reset();

            if (IsInstanceValid(pair.Ghost))
                pair.Ghost.LifeSystem?.Reset();

            pair.IsHidden = false;
        }

        Log.System("All enemies reset", "EnemyManager");
    }

    /// <summary>
    /// Repositions monsters to maintain minimum distance from player spawn position
    /// Uses the exported MinSpawnDistance property if no distance is specified
    /// </summary>
    public void RepositionMonstersForPlayerSpawn(Vector2 playerSpawnPosition, float minDistance = -1.0f)
    {
        // Use exported value if no specific distance provided
        float effectiveDistance = minDistance > 0 ? minDistance : MinSpawnDistance;
        
        foreach (var pair in _pairs)
        {
            if (!IsInstanceValid(pair.Monster)) continue;

            Vector2 monsterPos = pair.Monster.GlobalPosition;
            float distance = playerSpawnPosition.DistanceTo(monsterPos);

            if (distance < effectiveDistance)
            {
                // Calculate direction away from player spawn
                Vector2 direction = (monsterPos - playerSpawnPosition).Normalized();
                if (direction.Length() < 0.1f) // If too close, use random direction
                {
                    direction = new Vector2((float)(GD.Randf() - 0.5f), 0).Normalized();
                }

                // Position monster at minimum distance
                Vector2 newPosition = playerSpawnPosition + (direction * effectiveDistance);
                pair.Monster.GlobalPosition = newPosition;

                Log.Debug($"Repositioned monster from {monsterPos} to {newPosition} (distance: {distance} -> {effectiveDistance})", "EnemyManager");
            }
        }
    }

    public void NotifyGhostsChanged()
    {
        Log.System("Refreshing enemy-ghost pairs", "EnemyManager");
        CancelAllRespawns();
        DiscoverPairs();
        ConnectLifeEvents();
    }

    public List<EnemyPair> GetAllPairsSnapshot() => new List<EnemyPair>(_pairs);

    // IMPROVED: These properties now better handle the case where global timer overrides local
    public float GlobalRespawnTimeLeft
    {
        get
        {
            // If any pair has a FullPair timer, return the smallest time left (next global event)
            float min = float.PositiveInfinity;
            bool hasGlobalTimer = false;

            foreach (var kvp in _respawnTimers)
            {
                if (_respawnTypes.TryGetValue(kvp.Key, out var t) && t == RespawnType.FullPair)
                {
                    hasGlobalTimer = true;
                    try { min = Math.Min(min, (float)kvp.Value.TimeLeft); } catch { }
                }
            }

            return hasGlobalTimer && !float.IsPositiveInfinity(min) ? min : -1f;
        }
    }

    public float LocalRespawnTimeLeft
    {
        get
        {
            // IMPORTANT: If there's any global timer active, local timers are effectively overridden
            // So we should return -1 to indicate no active local timer
            if (GlobalRespawnTimeLeft > 0)
                return -1f;

            // If any pair has a MonsterOnly timer, return the smallest time left (next local event)
            float min = float.PositiveInfinity;
            bool hasLocalTimer = false;

            foreach (var kvp in _respawnTimers)
            {
                if (_respawnTypes.TryGetValue(kvp.Key, out var t) && t == RespawnType.MonsterOnly)
                {
                    hasLocalTimer = true;
                    try { min = Math.Min(min, (float)kvp.Value.TimeLeft); } catch { }
                }
            }

            return hasLocalTimer && !float.IsPositiveInfinity(min) ? min : -1f;
        }
    }
    #endregion

    #region Game Events
    private void OnPlayerDied(PlayerDeath death)
    {
        if (_isPlayerDead) return;
        _isPlayerDead = true;

        foreach (var pair in _pairs)
        {
            if (IsInstanceValid(pair.Monster))
                pair.Monster.ForceHide();
        }

        Log.System("Player died, hiding all monsters", "EnemyManager");
    }

    private void OnGameRestart()
    {
        _isPlayerDead = false;
        ResetAllEnemies();

        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        if (currentReality == Reality.CurrentReality)
        {
            foreach (var pair in _pairs)
            {
                if (IsInstanceValid(pair.Monster))
                    pair.Monster.ForceShow();
            }
        }

        Log.System("Game restarted", "EnemyManager");
    }

    /// <summary>
    /// Called when the global ghost life system is defeated
    /// Destroys all ghost monsters permanently until game restart
    /// </summary>
    public void OnGlobalGhostLifeDefeated()
    {
        Log.System("Global Ghost Life defeated - destroying all ghost monsters and hiding all monsters permanently", "EnemyManager");
        
        foreach (var pair in _pairs)
        {
            if (IsInstanceValid(pair.Ghost))
            {
                // Stop any ongoing replays
                try { pair.Ghost.StopReplay(); } catch { }
                
                // Permanently remove the ghost
                try { pair.Ghost.QueueFree(); } catch { }
                pair.Ghost = null;
            }
            
            // Also hide the monster in current reality since the source ghost life is defeated
            if (IsInstanceValid(pair.Monster))
            {
                HideMonster(pair.Monster);
                Log.Debug($"Monster {pair.Monster.Name} hidden permanently - global ghost life defeated", "EnemyManager");
            }
            
            // Cancel any respawn timers since ghosts are permanently defeated
            CancelRespawn(pair);
            
            // Keep pair hidden state as true since monsters should stay hidden
            pair.IsHidden = true;
        }
        
        // Clear ghost lookup since all ghosts are destroyed
        _ghostLookup.Clear();
        
        Log.System("All ghost monsters permanently destroyed", "EnemyManager");
    }
    #endregion

    #region Utility
    private void HideMonster(Monster monster)
    {
        if (IsInstanceValid(monster))
        {
            monster.ForceHide();

            // Notify ReplayRecorder that monster is now hidden
            if (ReplayRecorder.Instance != null)
            {
                ReplayRecorder.Instance.OnMonsterVisibilityChanged(monster, false);
            }
        }
    }

    private void CancelAllRespawns()
    {
        foreach (var timer in _respawnTimers.Values)
        {
            timer.Stop();
            timer.QueueFree();
        }
        _respawnTimers.Clear();
        _respawnTypes.Clear();
    }

    private void ClearAll()
    {
        CancelAllRespawns();
        _pairs.Clear();
        _monsterLookup.Clear();
        _ghostLookup.Clear();
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            ClearAll();
            Instance = null;
        }
    }
    #endregion
}