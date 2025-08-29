using System;
using System.Collections.Generic;
using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Models;

public partial class EnemyManager : Node
{
    [Export] public float DeathHideTime { get; set; } = 180f;
    
    public static EnemyManager Instance { get; private set; }
    
    // Simple collections
    private readonly List<Monster> _monsters = new();
    private readonly List<GhostMonster> _ghosts = new();
    private readonly Dictionary<string, EnemyPair> _pairs = new();
    private readonly Dictionary<string, CentralizedLifePool> _lifePools = new();
    
    private bool _isPlayerDead = false;
    
    // Debug UI for aggregated life pools
    [Export] public bool DebugShowAggregatedPools { get; set; } = false;
    [Export] public Vector2 DebugUIPosition { get; set; } = new Vector2(10, 10);
    [Export] public string DebugToggleHotkey { get; set; } = "F8";
    private CanvasLayer _debugLayer;
    private Panel _debugPanel;
    private VBoxContainer _debugContainer;
    private readonly Dictionary<string, Label> _debugLabels = new();
    private readonly Dictionary<string, Action> _debugSubscriptions = new();

    public override void _Ready()
    {
        if (Instance != null)
        {
            QueueFree();
            return;
        }
        
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        
        // Subscribe to events
        GameEventSystem.PlayerDied += OnPlayerDied;
        GameEventSystem.GameRestart += OnGameRestart;
        
        if (RealityManager.Instance != null)
        {
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
        }
        
        // Initialize after tree is ready
        CallDeferred(nameof(Initialize));
    }
    
    private void Initialize()
    {
        FindAllEntities();
        CreatePairs();
        SetupCentralizedLifeManagement();
    }
    
    #region Entity Discovery
    
    private void FindAllEntities()
    {
        _monsters.Clear();
        _ghosts.Clear();
        
        // Find monsters
        var enemies = GetTree().GetNodesInGroup("enemies");
        foreach (Node enemy in enemies)
        {
            if (enemy is Monster monster && IsInstanceValid(monster))
            {
                _monsters.Add(monster);
            }
        }
        
        // Find ghosts
        var ghostNodes = GetTree().GetNodesInGroup("ghost_monster");
        foreach (var ghost in ghostNodes)
        {
            if (ghost is GhostMonster ghostMonster && IsInstanceValid(ghostMonster))
            {
                _ghosts.Add(ghostMonster);
            }
        }
        
        Log.Debug($"Found {_monsters.Count} monsters and {_ghosts.Count} ghosts", "EnemyManager");
    }
    
    #endregion
    
    #region Pairing System
    
    private void CreatePairs()
    {
        _pairs.Clear();
        var usedGhosts = new HashSet<GhostMonster>();
        
        foreach (var monster in _monsters)
        {
            if (!IsInstanceValid(monster)) continue;
            
            // Find closest ghost
            GhostMonster closestGhost = null;
            float closestDistance = float.MaxValue;
            
            foreach (var ghost in _ghosts)
            {
                if (!IsInstanceValid(ghost) || usedGhosts.Contains(ghost)) continue;
                
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
            }
            
            // Create pair with unique ID
            string pairId = $"{monster.Name}_{monster.GetInstanceId()}";
            var pair = new EnemyPair
            {
                Monster = monster,
                Ghost = closestGhost,
                PairId = pairId
            };
            
            _pairs[pairId] = pair;
            Log.Debug($"Paired: {monster.Name} <-> {closestGhost?.Name ?? "None"}", "EnemyManager");
        }
    }
    
    #endregion
    
    #region Centralized Life Management - NEW APPROACH
    
    private void SetupCentralizedLifeManagement()
    {
        _lifePools.Clear();
        
        foreach (var pair in _pairs.Values)
        {
            if (pair == null) continue;
            
            // Create centralized life pool that manages both entities
            var lifePool = new CentralizedLifePool(pair, this);
            _lifePools[pair.PairId] = lifePool;
            
            // Initialize the pool from existing life systems
            lifePool.Initialize();
            
            // Debug UI hook
            if (DebugShowAggregatedPools)
            {
                EnsureDebugUICreated();
                AttachDebugToLifePool(pair.PairId, lifePool);
            }
        }
        
        Log.Debug($"Created {_lifePools.Count} centralized life pools", "EnemyManager");
    }
    
    #endregion
    
    #region Pair Death and Visibility Control
    
    public void HandlePairDeath(EnemyPair pair)
    {
        if (pair == null || pair.IsHidden) return;
        
        Log.System($"EnemyManager handling pair death: {pair.Monster?.Name}, scheduling respawn in {DeathHideTime} seconds", "EnemyManager");
        
        // Pair.IsHidden should already be set by CentralizedLifePool.CheckForPairDeath()
        // Entities should already be hidden by CheckForPairDeath()
        // Just ensure they're properly hidden and schedule respawn
        
        if (IsInstanceValid(pair.Monster) && pair.Monster.IsCurrentlyVisible)
        {
            pair.Monster.ForceHide();
            Log.Debug("EnemyManager: Force hiding monster that was still visible", "EnemyManager");
        }
        
        if (IsInstanceValid(pair.Ghost))
        {
            pair.Ghost.FadeOutAndDestroy();
            Log.Debug("EnemyManager: Destroying ghost", "EnemyManager");
        }
        
        // Schedule respawn
        var timer = GetTree().CreateTimer(DeathHideTime);
        timer.Timeout += () => RespawnPair(pair);
    }
    
    private void HidePairEntities(EnemyPair pair)
    {
        if (IsInstanceValid(pair.Monster))
        {
            pair.Monster.ForceHide();
        }
        
        if (IsInstanceValid(pair.Ghost))
        {
            pair.Ghost.FadeOutAndDestroy();
        }
    }
    
    private void RespawnPair(EnemyPair pair)
    {
        if (pair == null) return;
        
        Log.System($"Respawning pair {pair.PairId}", "EnemyManager");
        
        // Reset centralized life pool to full
        if (_lifePools.TryGetValue(pair.PairId, out var lifePool))
        {
            lifePool.ResetToFull();
        }
        
        // Show entities based on current reality
        pair.IsHidden = false;
        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        
        if (IsInstanceValid(pair.Monster))
        {
            if (currentReality == Reality.CurrentReality)
            {
                pair.Monster.ForceShow();
                pair.Monster.Reset();
            }
            else
            {
                pair.Monster.ForceHide(); // Hide in past reality
            }
        }
        
        // Ghost will be handled by replay system if in past reality
    }
    
    #endregion
    
    #region Reality Management
    
    private void OnRealityChanged(Reality newReality)
    {
        Log.System($"Reality changed to {newReality}, updating entity visibility", "EnemyManager");
        
        foreach (var pair in _pairs.Values)
        {
            if (pair.IsHidden) continue; // Don't show dead pairs
            
            UpdatePairVisibilityForReality(pair, newReality);
        }
    }
    
    private void UpdatePairVisibilityForReality(EnemyPair pair, Reality reality)
    {
        if (reality == Reality.CurrentReality)
        {
            // Current Reality: Show Monster, hide Ghost
            if (IsInstanceValid(pair.Monster))
            {
                pair.Monster.ForceShow();
            }
            
            if (IsInstanceValid(pair.Ghost))
            {
                pair.Ghost.FadeOutAndDestroy();
            }
        }
        else if (reality == Reality.PastReality)
        {
            // Past Reality: Hide Monster, Ghost will be shown by replay system
            if (IsInstanceValid(pair.Monster))
            {
                pair.Monster.ForceHide();
            }
            
            // Ghost visibility is handled by replay system
        }
    }
    
    #endregion
    
    #region Public API
    
    public void HideAllFollowers()
    {
        foreach (var monster in _monsters)
        {
            if (IsInstanceValid(monster))
            {
                monster.ForceHide();
            }
        }
        Log.System("All monsters hidden", "EnemyManager");
    }
    
    public void ShowAllFollowers()
    {
        foreach (var pair in _pairs.Values)
        {
            if (pair.IsHidden) continue;
            
            if (IsInstanceValid(pair.Monster))
            {
                pair.Monster.ForceShow();
            }
        }
        Log.System("All non-hidden monsters shown", "EnemyManager");
    }
    
    public int GetVisibleFollowerCount()
    {
        int count = 0;
        foreach (var monster in _monsters)
        {
            if (IsInstanceValid(monster) && monster.IsCurrentlyVisible)
            {
                count++;
            }
        }
        return count;
    }
    
    public bool IsMonsterHiddenByPair(Monster monster)
    {
        if (monster == null) return false;
        
        // Find the pair containing this monster
        foreach (var pair in _pairs.Values)
        {
            if (pair.Monster == monster)
            {
                return pair.IsHidden;
            }
        }
        
        return false;
    }
    
    public void ResetAllEnemies()
    {
        foreach (var monster in _monsters)
        {
            if (IsInstanceValid(monster) && !IsMonsterHiddenByPair(monster))
            {
                monster.Reset();
            }
        }
        
        // Reset all life pools to full
        foreach (var lifePool in _lifePools.Values)
        {
            lifePool.ResetToFull();
        }
        
        Log.System($"Reset {_monsters.Count} enemies", "EnemyManager");
    }

    // Called by factories or external code when the set of ghost nodes in the scene changes.
    public void NotifyGhostsChanged()
    {
        Log.System("NotifyGhostsChanged: refreshing enemy/ghost pairings", "EnemyManager");

        // Capture monsters that were previously hidden so we can preserve that state
        var previouslyHiddenMonsterIds = new HashSet<ulong>();
        foreach (var oldPair in _pairs.Values)
        {
            if (oldPair == null) continue;
            if (oldPair.IsHidden && IsInstanceValid(oldPair.Monster))
            {
                previouslyHiddenMonsterIds.Add(oldPair.Monster.GetInstanceId());
            }

            // Clean up old life pools
            if (_lifePools.TryGetValue(oldPair.PairId, out var oldPool))
            {
                oldPool.Cleanup();
                _lifePools.Remove(oldPair.PairId);
            }
        }

        // Rebuild entity lists and pairings
        FindAllEntities();
        CreatePairs();
        SetupCentralizedLifeManagement();

        // Re-apply hidden state for pairs that were hidden before the update
        foreach (var pair in _pairs.Values)
        {
            if (pair == null) continue;

            if (IsInstanceValid(pair.Monster) && previouslyHiddenMonsterIds.Contains(pair.Monster.GetInstanceId()))
            {
                pair.IsHidden = true;
                HidePairEntities(pair);
            }
        }
    }
    
    /// <summary>
    /// Get the centralized life pool for a specific monster
    /// </summary>
    public CentralizedLifePool GetLifePoolForMonster(Monster monster)
    {
        if (monster == null) return null;
        
        foreach (var pair in _pairs.Values)
        {
            if (pair.Monster == monster && _lifePools.TryGetValue(pair.PairId, out var pool))
            {
                return pool;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Get the centralized life pool for a specific ghost
    /// </summary>
    public CentralizedLifePool GetLifePoolForGhost(GhostMonster ghost)
    {
        if (ghost == null) return null;
        
        foreach (var pair in _pairs.Values)
        {
            if (pair.Ghost == ghost && _lifePools.TryGetValue(pair.PairId, out var pool))
            {
                return pool;
            }
        }
        
        return null;
    }
    
    #endregion
    
    #region Game Events
    
    private void OnPlayerDied(PlayerDeath death)
    {
        if (_isPlayerDead) return;
        _isPlayerDead = true;
        
        HideAllFollowers();
        Log.System("Player died, hiding all followers", "EnemyManager");
    }
    
    private void OnGameRestart()
    {
        _isPlayerDead = false;
        
        // Reset all life pools and show entities
        foreach (var lifePool in _lifePools.Values)
        {
            lifePool.ResetToFull();
        }
        
        foreach (var pair in _pairs.Values)
        {
            pair.IsHidden = false;
            if (IsInstanceValid(pair.Monster))
            {
                pair.Monster.Reset();
                pair.Monster.ForceShow();
            }
        }
        
        Log.System("Game restarted, all enemies reset", "EnemyManager");
    }
    
    #endregion
    
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            // Clean up all life pools
            foreach (var pool in _lifePools.Values)
            {
                pool.Cleanup();
            }
            _lifePools.Clear();
            
            Instance = null;
        }
    }

    #region Debug UI Helpers
    private void EnsureDebugUICreated()
    {
        if (_debugLayer != null) return;

        _debugLayer = new CanvasLayer { Name = "EnemyManagerDebugLayer" };
        AddChild(_debugLayer);

        _debugPanel = new Panel { Name = "EnemyManagerDebugPanel" };
        _debugPanel.Modulate = new Color(0f, 0f, 0f, 0.6f);
        _debugPanel.AnchorLeft = 0f;
        _debugPanel.AnchorTop = 0f;
        _debugPanel.AnchorRight = 0f;
        _debugPanel.AnchorBottom = 0f;
        _debugLayer.AddChild(_debugPanel);

        _debugContainer = new VBoxContainer { Name = "EnemyManagerDebugContainer" };
        _debugPanel.AddChild(_debugContainer);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        base._UnhandledInput(@event);

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            try
            {
                var keyName = keyEvent.Keycode.ToString();
                if (string.Equals(keyName, DebugToggleHotkey, StringComparison.OrdinalIgnoreCase))
                {
                    ToggleDebugUI();
                }
            }
            catch { }
        }
    }

    private void ToggleDebugUI()
    {
        DebugShowAggregatedPools = !DebugShowAggregatedPools;
        if (DebugShowAggregatedPools)
        {
            EnsureDebugUICreated();
            // Attach existing life pools
            foreach (var kv in _lifePools)
            {
                AttachDebugToLifePool(kv.Key, kv.Value);
            }
        }
        else
        {
            // Detach all
            var keys = new List<string>(_debugLabels.Keys);
            foreach (var k in keys) DetachDebugFromLifePool(k);
        }
    }

    private void DestroyDebugUI()
    {
        try
        {
            foreach (var lbl in _debugLabels.Values) lbl.QueueFree();
            _debugLabels.Clear();
            foreach (var sub in _debugSubscriptions.Values) { } // no-op
            _debugSubscriptions.Clear();

            if (_debugLayer != null) _debugLayer.QueueFree();
            _debugLayer = null;
            _debugPanel = null;
            _debugContainer = null;
        }
        catch { }
    }

    private void AttachDebugToLifePool(string pairId, CentralizedLifePool lifePool)
    {
        if (_debugContainer == null) return;
        if (_debugLabels.ContainsKey(pairId)) return;

        var label = new Label { Text = $"{pairId}: R={lifePool.RedHearts} P={lifePool.PurpleHearts}" };
        _debugContainer.AddChild(label);
        label.Modulate = Colors.White;
        _debugLabels[pairId] = label;

        Action updater = () => UpdateDebugLabel(pairId, lifePool);
        lifePool.OnLifeChanged += updater;
        _debugSubscriptions[pairId] = updater;
    }

    private void DetachDebugFromLifePool(string pairId)
    {
        if (!_debugLabels.ContainsKey(pairId)) return;
        try
        {
            if (_lifePools.TryGetValue(pairId, out var lifePool))
            {
                if (_debugSubscriptions.TryGetValue(pairId, out var updater))
                {
                    lifePool.OnLifeChanged -= updater;
                }
            }
        }
        catch { }

        try { _debugLabels[pairId].QueueFree(); } catch { }
        _debugLabels.Remove(pairId);
        _debugSubscriptions.Remove(pairId);

        // If no labels left, destroy the whole debug layer
        if (_debugLabels.Count == 0)
        {
            DestroyDebugUI();
        }
    }

    private void UpdateDebugLabel(string pairId, CentralizedLifePool lifePool)
    {
        if (!_debugLabels.TryGetValue(pairId, out var label)) return;
        try
        {
            label.Text = $"{pairId}: R={lifePool.RedHearts}/{lifePool.MaxRed} P={lifePool.PurpleHearts}/{lifePool.MaxPurple} [{lifePool.GetStatus()}]";
        }
        catch { }
    }
    #endregion
}

/// <summary>
/// Centralized life pool that manages the combined health of a Monster/Ghost pair.
/// This replaces the problematic SharedLife system with cleaner separation of concerns.
/// </summary>
public class CentralizedLifePool
{
    public int RedHearts { get; private set; }
    public int PurpleHearts { get; private set; }
    public int MaxRed { get; private set; } = 3;
    public int MaxPurple { get; private set; } = 3;
    
    private readonly EnemyPair _pair;
    private readonly EnemyManager _manager;
    private bool _syncInProgress = false;
    
    public CentralizedLifePool(EnemyPair pair, EnemyManager manager)
    {
        _pair = pair;
        _manager = manager;
    }
    
    public event Action OnLifeChanged;
    
    public void Initialize()
    {
        // Initialize from Monster's life system (primary source)
        if (IsInstanceValid(_pair.Monster))
        {
            var monsterLife = _pair.Monster.GetLifeSystem();
            if (monsterLife != null)
            {
                MaxRed = monsterLife.RedHearts;
                MaxPurple = monsterLife.PurpleHearts;
                RedHearts = monsterLife.CurrentRedHearts;
                PurpleHearts = monsterLife.CurrentPurpleHearts;
                
                // Subscribe to monster life events
                monsterLife.LifeChanged += OnMonsterLifeChanged;
            }
        }
        
        // Initialize Ghost's life system to match (but keep separate)
        if (IsInstanceValid(_pair.Ghost))
        {
            var ghostLife = _pair.Ghost.LifeSystem;
            if (ghostLife != null)
            {
                // Set ghost to have same max capacity as monster
                // But start with separate current values
                ghostLife.MaxLife = MaxRed + MaxPurple;
                ghostLife.PurpleHearts = MaxPurple;
                ghostLife.Reset(); // This will set appropriate starting values
                
                // Subscribe to ghost life events
                ghostLife.LifeChanged += OnGhostLifeChanged;
            }
        }
        
        Log.Debug($"Initialized life pool: R={RedHearts}/{MaxRed} P={PurpleHearts}/{MaxPurple}", "CentralizedLifePool");
        OnLifeChanged?.Invoke();
    }
    
    /// <summary>
    /// Handle damage to the Monster (red hearts only in current reality)
    /// </summary>
    public bool HandleMonsterDamage(int damage, Reality reality)
    {
        if (_syncInProgress) return false;
        
        if (reality == Reality.CurrentReality)
        {
            // Monster takes red heart damage in current reality
            int actualDamage = Math.Min(damage, RedHearts);
            if (actualDamage <= 0) return false;
            
            RedHearts -= actualDamage;
            SyncToEntities();
            CheckForPairDeath();
            OnLifeChanged?.Invoke();
            
            Log.Debug($"Monster took {actualDamage} red heart damage. Pool: R={RedHearts} P={PurpleHearts}", "CentralizedLifePool");
            return true;
        }
        
        return false; // Monster doesn't take damage in past reality
    }
    
    /// <summary>
    /// Handle damage to the Ghost (purple hearts only in past reality)
    /// </summary>
    public bool HandleGhostDamage(int damage, Reality reality)
    {
        if (_syncInProgress) return false;
        
        if (reality == Reality.PastReality)
        {
            // Ghost takes purple heart damage in past reality
            int actualDamage = Math.Min(damage, PurpleHearts);
            if (actualDamage <= 0) return false;
            
            PurpleHearts -= actualDamage;
            SyncToEntities();
            CheckForPairDeath();
            OnLifeChanged?.Invoke();
            
            Log.Debug($"Ghost took {actualDamage} purple heart damage. Pool: R={RedHearts} P={PurpleHearts}", "CentralizedLifePool");
            return true;
        }
        
        return false; // Ghost doesn't take damage in current reality
    }
    
    private void OnMonsterLifeChanged(int currentLife, int maxLife, int purpleHearts)
    {
        if (_syncInProgress) return;
        
        // Monster life changed independently - update our pool to reflect current state
        int monsterRed = Math.Max(0, currentLife - purpleHearts);
        int monsterPurple = Math.Max(0, purpleHearts);
        
        // Update our centralized values
        RedHearts = monsterRed;
        PurpleHearts = monsterPurple;
        
        // Sync to ghost to keep them in sync
        SyncToGhost();
        CheckForPairDeath();
        OnLifeChanged?.Invoke();
    }
    
    private void OnGhostLifeChanged(int currentLife, int maxLife, int purpleHearts)
    {
        if (_syncInProgress) return;
        
        // Ghost life changed independently - update our pool to reflect current state
        int ghostRed = Math.Max(0, currentLife - purpleHearts);
        int ghostPurple = Math.Max(0, purpleHearts);
        
        // Update our centralized values
        RedHearts = ghostRed;
        PurpleHearts = ghostPurple;
        
        // Sync to monster to keep them in sync
        SyncToMonster();
        CheckForPairDeath();
        OnLifeChanged?.Invoke();
    }
    
    private void SyncToEntities()
    {
        SyncToMonster();
        SyncToGhost();
    }
    
    private void SyncToMonster()
    {
        if (!IsInstanceValid(_pair.Monster)) return;
        
        var monsterLife = _pair.Monster.GetLifeSystem();
        if (monsterLife == null) return;
        
        _syncInProgress = true;
        try
        {
            // Monster gets the current pool values
            monsterLife.SetHearts(RedHearts, PurpleHearts);
        }
        finally
        {
            _syncInProgress = false;
        }
    }
    
    private void SyncToGhost()
    {
        if (!IsInstanceValid(_pair.Ghost)) return;
        
        var ghostLife = _pair.Ghost.LifeSystem;
        if (ghostLife == null) return;
        
        _syncInProgress = true;
        try
        {
            // Ghost gets the current pool values
            ghostLife.SetHearts(RedHearts, PurpleHearts);
        }
        finally
        {
            _syncInProgress = false;
        }
    }
    
    private void CheckForPairDeath()
    {
        // Pair dies when BOTH red and purple hearts are depleted
        bool isDead = RedHearts <= 0 && PurpleHearts <= 0;
        
        if (isDead && !_pair.IsHidden)
        {
            Log.System($"Pair death detected: R={RedHearts} P={PurpleHearts} - triggering immediate hide", "CentralizedLifePool");
            
            // Immediately hide entities when death is detected
            _pair.IsHidden = true;
            
            // Hide the monster immediately
            if (IsInstanceValid(_pair.Monster))
            {
                _pair.Monster.ForceHide();
                Log.Debug("Monster hidden due to pair death", "CentralizedLifePool");
            }
            
            // Hide the ghost immediately
            if (IsInstanceValid(_pair.Ghost))
            {
                _pair.Ghost.FadeOutAndDestroy();
                Log.Debug("Ghost destroyed due to pair death", "CentralizedLifePool");
            }
            
            // Schedule respawn through EnemyManager
            _manager?.HandlePairDeath(_pair);
        }
    }
    
    public void ResetToFull()
    {
        RedHearts = MaxRed;
        PurpleHearts = MaxPurple;
        SyncToEntities();
        OnLifeChanged?.Invoke();
        
        Log.Debug($"Life pool reset to full: R={RedHearts}/{MaxRed} P={PurpleHearts}/{MaxPurple}", "CentralizedLifePool");
    }
    
    public void Cleanup()
    {
        try
        {
            if (IsInstanceValid(_pair.Monster))
            {
                var monsterLife = _pair.Monster.GetLifeSystem();
                if (monsterLife != null)
                {
                    monsterLife.LifeChanged -= OnMonsterLifeChanged;
                }
            }
            
            if (IsInstanceValid(_pair.Ghost))
            {
                var ghostLife = _pair.Ghost.LifeSystem;
                if (ghostLife != null)
                {
                    ghostLife.LifeChanged -= OnGhostLifeChanged;
                }
            }
        }
        catch { }
    }
    
    public string GetStatus()
    {
        if (RedHearts <= 0 && PurpleHearts <= 0) return "DEAD";
        if (_pair.IsHidden) return "HIDDEN";
        return "ALIVE";
    }
    
    private static bool IsInstanceValid(GodotObject obj)
    {
        return GodotObject.IsInstanceValid(obj);
    }
}