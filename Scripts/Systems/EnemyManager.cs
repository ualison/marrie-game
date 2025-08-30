using System;
using System.Collections.Generic;
using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Models;
using MarrieGame.Systems.LifePools;

public partial class EnemyManager : Node
{
    #region Fields & Config
    [Export] public float GlobalMonsterHideTime { get; set; } = 80f; // Ghost death -> respawn time for both
    [Export] public float LocalMonsterHideTime { get; set; } = 15f; // Monster red depletion -> monster respawn time

    public static EnemyManager Instance { get; private set; }

    // Pair-centric model: each pair represents a Monster <-> Ghost relationship
    private readonly List<EnemyPair> _pairs = new();
    private readonly Dictionary<Monster, EnemyPair> _monsterToPair = new();
    private readonly Dictionary<GhostMonster, EnemyPair> _ghostToPair = new();
    private readonly Dictionary<EnemyPair, SeparateLifePool> _pairLifePools = new();

    // Track individual lives for quick lookup
    private readonly Dictionary<Monster, int> _monsterRedHearts = new();
    // Ghost purple hearts are stored in the SeparateLifePool (GhostLifePool). No local store needed.

    // Timer management keyed by pair for clarity
    private readonly Dictionary<EnemyPair, Timer> _monsterRespawnTimers = new();
    private readonly Dictionary<EnemyPair, Timer> _pairRespawnTimers = new();

    private bool _isPlayerDead = false;
    #endregion

    // Small DTO so external UI code can read active respawn timers
    public class RespawnTimerInfo
    {
        public string PairId { get; set; }
        public float SecondsLeft { get; set; }
        public string TimerType { get; set; }
    }

    #region Helpers
    private EnemyPair GetPairForMonster(Monster monster)
    {
        if (monster == null) return null;
        _monsterToPair.TryGetValue(monster, out var pair);
        return pair;
    }

    private EnemyPair GetPairForGhost(GhostMonster ghost)
    {
        if (ghost == null) return null;
        _ghostToPair.TryGetValue(ghost, out var pair);
        return pair;
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
        {
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
        }

        CallDeferred(nameof(Initialize));
    }

    private void Initialize()
    {
    DiscoverAndPairEntities();
    InitializeLifeSystems();
    }
    #endregion

    #region Entity Discovery and Pairing
    private void DiscoverAndPairEntities()
    {
        // Cleanup existing
        foreach (var pool in _pairLifePools.Values)
            pool?.Cleanup();

        _pairs.Clear();
        _monsterToPair.Clear();
        _ghostToPair.Clear();
        _pairLifePools.Clear();

        var monsters = new List<Monster>();
        var ghosts = new List<GhostMonster>();

        // Find all monsters
        foreach (Node enemy in GetTree().GetNodesInGroup("enemies"))
        {
            if (enemy is Monster monster && IsInstanceValid(monster))
                monsters.Add(monster);
        }

        // Find all ghosts
        foreach (Node ghost in GetTree().GetNodesInGroup("ghost_monster"))
        {
            if (ghost is GhostMonster ghostMonster && IsInstanceValid(ghostMonster))
                ghosts.Add(ghostMonster);
        }

        // Simple pairing by proximity, but store as pair-centric data
        var usedGhosts = new HashSet<GhostMonster>();
        foreach (var monster in monsters)
        {
            GhostMonster closestGhost = null;
            float closestDistance = float.MaxValue;

            foreach (var ghost in ghosts)
            {
                if (usedGhosts.Contains(ghost)) continue;

                float distance = monster.GlobalPosition.DistanceTo(ghost.GlobalPosition);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestGhost = ghost;
                }
            }

            if (closestGhost != null)
            {
                usedGhosts.Add(closestGhost);

                var pair = new EnemyPair
                {
                    Monster = monster,
                    Ghost = closestGhost,
                    PairId = $"{monster.Name}_{closestGhost.Name}",
                    IsHidden = false
                };

                var pool = new SeparateLifePool(pair, this);
                pool.Initialize();

                _pairs.Add(pair);
                _monsterToPair[monster] = pair;
                _ghostToPair[closestGhost] = pair;
                _pairLifePools[pair] = pool;

                Log.Debug($"Paired: {monster.Name} <-> {closestGhost.Name}", "EnemyManager");
            }
        }

        // Create entries for monsters that didn't find a ghost so they are still tracked
        foreach (var monster in monsters)
        {
            if (_monsterToPair.ContainsKey(monster)) continue;

            var pair = new EnemyPair
            {
                Monster = monster,
                Ghost = null,
                PairId = $"{monster.Name}_unpaired",
                IsHidden = false
            };

            var pool = new SeparateLifePool(pair, this);
            pool.Initialize();

            _pairs.Add(pair);
            _monsterToPair[monster] = pair;
            _pairLifePools[pair] = pool;

            Log.Debug($"Created unpaired tracking for: {monster.Name}", "EnemyManager");
        }

        Log.Debug($"Created {_pairs.Count} monster-ghost pairs", "EnemyManager");
    }
    #endregion

    #region Life System Management
    private void InitializeLifeSystems()
    {
        _monsterRedHearts.Clear();
    // No local ghost store to clear; pools hold authoritative ghost purple heart counts.
        // Initialize heart tracking and connect events per pair
        foreach (var pair in _pairs)
        {
            var monster = pair.Monster;
            var ghost = pair.Ghost;

            if (IsInstanceValid(monster))
            {
                var life = monster.LifeSystem;
                if (life != null)
                {
                    _monsterRedHearts[monster] = life.CurrentRedHearts;
                    life.LifeChanged += (current, max, purple) => OnMonsterLifeChanged(monster, current);
                    life.MonsterDied += () => OnMonsterDied(monster);
                }
            }

            if (IsInstanceValid(ghost))
            {
                var life = ghost.LifeSystem;
                if (life != null)
                {
                    // Pools will subscribe and track ghost purple counts; keep local monitoring minimal
                    life.LifeChanged += (current, max, purple) => OnGhostLifeChanged(ghost, purple);
                    life.MonsterDied += () => OnGhostDied(ghost);
                }
            }
        }
    }

    private void OnMonsterLifeChanged(Monster monster, int currentLife)
    {
        if (!_monsterRedHearts.ContainsKey(monster)) return;
        
        _monsterRedHearts[monster] = currentLife;
        
        // If red hearts depleted, hide monster temporarily
        if (currentLife <= 0)
        {
            HandleMonsterRedDepleted(monster);
        }
    }

    private void OnGhostLifeChanged(GhostMonster ghost, int purpleHearts)
    {
    // Ghost life changes are proxied through pools when needed; no local state update required.
    // Keep this hook for compatibility and potential debug usage.
    Log.Debug($"EnemyManager.OnGhostLifeChanged: ghost={ghost.Name} purple={purpleHearts}", "EnemyManager");
    }

    private void OnMonsterDied(Monster monster)
    {
        // Monster death is handled by red heart depletion
        HandleMonsterRedDepleted(monster);
    }

    private void OnGhostDied(GhostMonster ghost)
    {
        // Ghost death triggers pair hiding
        HandleGhostDeath(ghost);
    }
    #endregion

    #region Death and Respawn Handling
    private void HandleMonsterRedDepleted(Monster monster)
    {
        var pair = GetPairForMonster(monster);
        if (pair == null)
        {
            Log.Warning($"Monster red hearts depleted (unpaired) - hiding for {LocalMonsterHideTime}s", "EnemyManager");
            if (IsInstanceValid(monster)) monster.ForceHide();
            ScheduleMonsterRespawnForPairless(monster, LocalMonsterHideTime);
            return;
        }

    Log.Warning($"Monster red hearts depleted - hiding for {LocalMonsterHideTime}s", "EnemyManager");
        pair.IsHidden = true;

        if (IsInstanceValid(monster)) monster.ForceHide();
    ScheduleMonsterRespawn(pair, LocalMonsterHideTime);
    }

    private void SchedulePairRespawn(EnemyPair pair, float seconds)
    {
        if (pair == null) return;
        CancelPairRespawn(pair.Monster);

        Log.Debug($"Scheduling pair respawn for {pair.PairId} in {seconds} seconds", "EnemyManager");

        var timer = new Timer { OneShot = true, WaitTime = seconds };
        AddChild(timer);
        _pairRespawnTimers[pair] = timer;

        timer.Timeout += () =>
        {
            Log.Debug($"Pair respawn timer fired for {pair.PairId}", "EnemyManager");
            RespawnPair(pair);
            timer.QueueFree();
            _pairRespawnTimers.Remove(pair);
        };

        timer.Start();
    }

    private void ScheduleMonsterRespawn(EnemyPair pair, float seconds)
    {
        if (pair == null) return;
        CancelMonsterRespawn(pair.Monster);

        var timer = new Timer { OneShot = true, WaitTime = seconds };
        AddChild(timer);
        _monsterRespawnTimers[pair] = timer;

        timer.Timeout += () =>
        {
            Log.Debug($"Monster respawn timer fired for pair {pair.PairId}", "EnemyManager");
            RespawnMonsterOnly(pair.Monster);
            timer.QueueFree();
            _monsterRespawnTimers.Remove(pair);
        };

        timer.Start();
    }

    // For unpaired monsters
    private void ScheduleMonsterRespawnForPairless(Monster monster, float seconds)
    {
        CancelMonsterRespawn(monster);

        var timer = new Timer { OneShot = true, WaitTime = seconds };
        AddChild(timer);
        // Use a short-lived anonymous mapping keyed by a new EnemyPair placeholder is not necessary; track by monster in local scope
        timer.Timeout += () =>
        {
            RespawnMonsterOnly(monster);
            timer.QueueFree();
        };

        timer.Start();
    }

    private void CancelPairRespawn(Monster monster)
    {
        var pair = GetPairForMonster(monster);
        if (pair == null) return;

        if (_pairRespawnTimers.TryGetValue(pair, out var timer))
        {
            timer.Stop();
            timer.QueueFree();
            _pairRespawnTimers.Remove(pair);
        }
    }

    private void CancelMonsterRespawn(Monster monster)
    {
        var pair = GetPairForMonster(monster);
        if (pair != null)
        {
            if (_monsterRespawnTimers.TryGetValue(pair, out var timer))
            {
                timer.Stop();
                timer.QueueFree();
                _monsterRespawnTimers.Remove(pair);
            }
        }
        else
        {
            // Nothing to cancel for pairless monsters in current mapping
        }
    }

    private void RespawnPair(EnemyPair pair)
    {
        if (pair == null) return;

        Log.System("Respawning monster-ghost pair", "EnemyManager");

    Log.Debug($"RespawnPair invoked for {pair.PairId}", "EnemyManager");

        var monster = pair.Monster;
        var ghost = pair.Ghost;

        if (IsInstanceValid(monster))
        {
            // Reset monster life and update internal tracking
            monster.LifeSystem?.Reset();
            var postResetRed = monster.LifeSystem?.CurrentRedHearts ?? 0;
            _monsterRedHearts[monster] = postResetRed;

            var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
            Log.Debug($"RespawnPair: pair={pair.PairId} postResetRed={postResetRed} currentReality={currentReality}", "EnemyManager");

            // Only force-show when we're in CurrentReality
            if (currentReality == Reality.CurrentReality)
            {
                Log.Debug($"RespawnPair: calling ForceShow() on monster {monster.Name}", "EnemyManager");
                monster.ForceShow();
            }
            else
            {
                Log.Debug($"RespawnPair: not showing monster {monster.Name} because currentReality={currentReality}", "EnemyManager");
            }
        }

        if (IsInstanceValid(ghost))
        {
            ghost.LifeSystem?.Reset();
            // rely on life pools for current ghost purple hearts
        }

        pair.IsHidden = false;
    }

    private void RespawnMonsterOnly(Monster monster)
    {
        Log.System("Respawning monster after red heart depletion", "EnemyManager");
        if (!IsInstanceValid(monster)) return;

        // If this monster is part of a pair with a SeparateLifePool, update the pool
        var pair = GetPairForMonster(monster);
        if (pair != null && _pairLifePools.TryGetValue(pair, out var pool))
        {
            Log.Debug($"Resetting pool red hearts for pair {pair.PairId}", "EnemyManager");
            pool.ResetMonsterRedToFull();
            _monsterRedHearts[monster] = pool.MonsterRedHearts;
        }
        else
        {
            var life = monster.LifeSystem;
            if (life != null)
            {
                Log.Debug($"Before RespawnMonsterOnly (pairless): monster={monster.Name} currentRed={life.CurrentRedHearts} max={life.MaxLife}", "EnemyManager");
                life.SetHearts(life.MaxLife, 0); // Full red hearts, no purple
                Log.Debug($"After SetHearts (pairless): monster={monster.Name} currentRed={life.CurrentRedHearts}", "EnemyManager");
                _monsterRedHearts[monster] = life.CurrentRedHearts;
            }
        }

        Log.Debug($"Calling ForceShow on monster {monster.Name}", "EnemyManager");
        monster.ForceShow();
    }
    #endregion

    #region Reality Management
    private void OnRealityChanged(Reality newReality)
    {
        Log.System($"Reality changed to {newReality}", "EnemyManager");
        foreach (var pair in _pairs)
        {
            var monster = pair.Monster;
            var ghost = pair.Ghost;

            if (!IsInstanceValid(monster)) continue;

            if (newReality == Reality.CurrentReality)
            {
                if (_monsterRedHearts.GetValueOrDefault(monster, 0) > 0)
                    monster.ForceShow();

                if (IsInstanceValid(ghost))
                    ghost.FadeOutAndDestroy();
            }
            else if (newReality == Reality.PastReality)
            {
                monster.ForceHide();
                // Ghost visibility handled by replay system
            }
        }
    }
    #endregion

    #region Public API
    public int GetMonsterRedHearts(Monster monster)
    {
        return _monsterRedHearts.GetValueOrDefault(monster, 0);
    }

    public int GetGhostPurpleHearts(GhostMonster ghost)
    {
        var pool = GetLifePoolForGhost(ghost);
        return pool?.GhostPurpleHearts ?? 0;
    }

    public bool IsMonsterPaired(Monster monster)
    {
    return _monsterToPair.ContainsKey(monster);
    }

    public bool IsGhostPaired(GhostMonster ghost)
    {
    return _ghostToPair.ContainsKey(ghost);
    }

    // Returns a snapshot of active respawn timers for debug UI
    public List<RespawnTimerInfo> GetActiveRespawnTimers()
    {
        var result = new List<RespawnTimerInfo>();

        foreach (var kv in _pairRespawnTimers)
        {
            var pair = kv.Key;
            var timer = kv.Value;
            if (timer == null || pair == null) continue;
            // Godot Timer provides TimeLeft property
            float left = 0f;
            try { left = (float)timer.TimeLeft; } catch { left = Mathf.Max(0f, (float)timer.WaitTime); }

            result.Add(new RespawnTimerInfo
            {
                PairId = pair.PairId,
                SecondsLeft = left,
                TimerType = "Pair"
            });
        }

        foreach (var kv in _monsterRespawnTimers)
        {
            var pair = kv.Key;
            var timer = kv.Value;
            if (timer == null || pair == null) continue;
            float left = 0f;
            try { left = (float)timer.TimeLeft; } catch { left = Mathf.Max(0f, (float)timer.WaitTime); }

            result.Add(new RespawnTimerInfo
            {
                PairId = pair.PairId,
                SecondsLeft = left,
                TimerType = "Monster"
            });
        }

        return result;
    }

    public void ResetAllEnemies()
    {
        CancelAllTimers();
        foreach (var pair in _pairs)
        {
            var monster = pair.Monster;
            var ghost = pair.Ghost;

            if (IsInstanceValid(monster))
            {
                monster.Reset();
                _monsterRedHearts[monster] = monster.LifeSystem?.CurrentRedHearts ?? 0;
            }

                if (IsInstanceValid(ghost))
                {
                    ghost.LifeSystem?.Reset();
                }

            pair.IsHidden = false;

            if (_pairLifePools.TryGetValue(pair, out var pool))
                pool?.ResetBothToFull();
        }

        Log.System("All enemies reset", "EnemyManager");
    }

    public void NotifyGhostsChanged()
    {
        Log.System("Refreshing enemy-ghost pairs", "EnemyManager");
    CancelAllTimers();
    DiscoverAndPairEntities();
    InitializeLifeSystems();
    }

    private void CancelAllTimers()
    {
        foreach (var timer in _pairRespawnTimers.Values)
        {
            timer.Stop();
            timer.QueueFree();
        }
        _pairRespawnTimers.Clear();

        foreach (var timer in _monsterRespawnTimers.Values)
        {
            timer.Stop();
            timer.QueueFree();
        }
        _monsterRespawnTimers.Clear();

        // Cleanup life pools
        foreach (var pool in _pairLifePools.Values)
        {
            pool?.Cleanup();
        }
        _pairLifePools.Clear();
        _monsterToPair.Clear();
        _ghostToPair.Clear();
        _pairs.Clear();
    }
    #endregion

    #region LifePool & Pair API
    public SeparateLifePool GetLifePoolForMonster(Monster monster)
    {
        if (monster == null) return null;
    var pair = GetPairForMonster(monster);
    if (pair == null) return null;
    _pairLifePools.TryGetValue(pair, out var pool);
    return pool;
    }

    public SeparateLifePool GetLifePoolForGhost(GhostMonster ghost)
    {
        if (ghost == null) return null;
    var pair = GetPairForGhost(ghost);
    if (pair == null) return null;
    _pairLifePools.TryGetValue(pair, out var pool);
    return pool;
    }

    public bool IsMonsterHiddenByPair(Monster monster)
    {
        if (monster == null) return false;
    var pair = GetPairForMonster(monster);
    return pair != null && pair.IsHidden;
    }

    // Adapter for SeparateLifePool -> call when monster red depleted via pool
    public void HandleMonsterRedDepleted(EnemyPair pair)
    {
        if (pair == null) return;
    pair.IsHidden = true;

    if (IsInstanceValid(pair.Monster)) pair.Monster.ForceHide();

    // Schedule monster-only respawn using pair keyed timer
    ScheduleMonsterRespawn(pair, LocalMonsterHideTime);
    }

    // Adapter for SeparateLifePool -> call when ghost dies via pool
    public void HandleGhostDeath(EnemyPair pair)
    {
        if (pair == null) return;
    var monster = pair.Monster;
    var ghost = pair.Ghost;

    Log.System($"Ghost died - hiding both entities for {GlobalMonsterHideTime}s", "EnemyManager");

    CancelMonsterRespawn(monster);

    if (IsInstanceValid(monster)) monster.ForceHide();
    if (IsInstanceValid(ghost)) ghost.FadeOutAndDestroy();

    pair.IsHidden = true;
    SchedulePairRespawn(pair, GlobalMonsterHideTime);
    }

    // Overload: called by GhostMonster instances when they die locally (unpaired or no pool)
    public void HandleGhostDeath(GhostMonster ghost)
    {
        if (ghost == null) return;
        var pair = GetPairForGhost(ghost);
        if (pair == null)
        {
            Log.System($"Ghost died (unpaired) - destroying ghost instance", "EnemyManager");
            if (IsInstanceValid(ghost)) ghost.FadeOutAndDestroy();
            return;
        }

        // Reuse existing pair-based logic
        HandleGhostDeath(pair);
    }
    #endregion

    #region Game Events
    private void OnPlayerDied(PlayerDeath death)
    {
        if (_isPlayerDead) return;
        _isPlayerDead = true;

        foreach (var monster in _monsterToPair.Keys)
        {
            if (IsInstanceValid(monster))
            {
                monster.ForceHide();
            }
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
            foreach (var monster in _monsterToPair.Keys)
            {
                if (IsInstanceValid(monster))
                {
                    monster.ForceShow();
                }
            }
        }

        Log.System("Game restarted", "EnemyManager");
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            CancelAllTimers();
            Instance = null;
        }
    }
    #endregion
}