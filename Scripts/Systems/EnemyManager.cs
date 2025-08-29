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
    private readonly Dictionary<string, SharedLife> _sharedLives = new();
    
    private bool _isPlayerDead = false;

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
        SetupLifeSynchronization();
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
    
    #region Life Synchronization
    
    private void SetupLifeSynchronization()
    {
        _sharedLives.Clear();
        
        foreach (var pair in _pairs.Values)
        {
            if (pair == null) continue;
            
            // Create shared life for this pair
            var sharedLife = new SharedLife(pair);
            _sharedLives[pair.PairId] = sharedLife;
            
            // Subscribe to life events
            SubscribeToLifeEvents(pair, sharedLife);
        }
    }
    
    private void SubscribeToLifeEvents(EnemyPair pair, SharedLife sharedLife)
    {
        // Subscribe to monster life events
        if (IsInstanceValid(pair.Monster))
        {
            var monsterLife = pair.Monster.GetLifeSystem();
            if (monsterLife != null)
            {
                monsterLife.LifeChanged += sharedLife.OnMonsterLifeChanged;
                monsterLife.MonsterDied += () => HandlePairDeath(pair);
                
                // Initialize shared life from monster
                sharedLife.SetLife(monsterLife.CurrentRedHearts, monsterLife.CurrentPurpleHearts);
                sharedLife.SetMaxLife(monsterLife.RedHearts, monsterLife.PurpleHearts);
            }
        }
        
        // Subscribe to ghost life events  
        if (IsInstanceValid(pair.Ghost))
        {
            var ghostLife = pair.Ghost.LifeSystem;
            if (ghostLife != null)
            {
                ghostLife.LifeChanged += sharedLife.OnGhostLifeChanged;
                ghostLife.MonsterDied += () => HandlePairDeath(pair);
                
                // Initialize shared life from ghost if monster didn't have life system
                if (sharedLife.RedHearts == 0 && sharedLife.PurpleHearts == 0)
                {
                    sharedLife.SetLife(ghostLife.CurrentRedHearts, ghostLife.CurrentPurpleHearts);
                    sharedLife.SetMaxLife(ghostLife.RedHearts, ghostLife.PurpleHearts);
                }
            }
        }
    }
    
    private void UnsubscribeFromLifeEvents(EnemyPair pair)
    {
        if (!_sharedLives.TryGetValue(pair.PairId, out var sharedLife)) return;
        
        // Unsubscribe monster
        if (IsInstanceValid(pair.Monster))
        {
            var monsterLife = pair.Monster.GetLifeSystem();
            if (monsterLife != null)
            {
                monsterLife.LifeChanged -= sharedLife.OnMonsterLifeChanged;
            }
        }
        
        // Unsubscribe ghost
        if (IsInstanceValid(pair.Ghost))
        {
            var ghostLife = pair.Ghost.LifeSystem;
            if (ghostLife != null)
            {
                ghostLife.LifeChanged -= sharedLife.OnGhostLifeChanged;
            }
        }
    }
    
    #endregion
    
    #region Death and Respawn
    
    public void HandlePairDeath(EnemyPair pair)
    {
        if (pair == null || pair.IsHidden) return;
        
        Log.System($"Pair died: {pair.Monster?.Name}, hiding for {DeathHideTime} seconds", "EnemyManager");
        
        // Hide entities
        pair.IsHidden = true;
        if (IsInstanceValid(pair.Monster))
        {
            pair.Monster.ForceHide();
        }
        
        if (IsInstanceValid(pair.Ghost))
        {
            pair.Ghost.FadeOutAndDestroy();
        }
        
        // Unsubscribe to avoid events while hidden
        UnsubscribeFromLifeEvents(pair);
        
        // Schedule respawn
        var timer = GetTree().CreateTimer(DeathHideTime);
        timer.Timeout += () => RespawnPair(pair);
    }
    
    private void RespawnPair(EnemyPair pair)
    {
        if (pair == null) return;
        
        Log.System($"Respawning pair {pair.PairId}", "EnemyManager");
        
        // Reset shared life to full
        if (_sharedLives.TryGetValue(pair.PairId, out var sharedLife))
        {
            sharedLife.ResetToFull();
        }
        
        // Show entities
        pair.IsHidden = false;
        if (IsInstanceValid(pair.Monster))
        {
            pair.Monster.ForceShow();
            pair.Monster.Reset();
        }
        
        // Re-subscribe to life events
        if (_sharedLives.ContainsKey(pair.PairId))
        {
            SubscribeToLifeEvents(pair, _sharedLives[pair.PairId]);
        }
    }
    
    #endregion
    
    #region Reality Management
    
    private void OnRealityChanged(Reality newReality)
    {
        Log.System($"Reality changed to {newReality}, refreshing entities", "EnemyManager");
        
        // Unsubscribe all current events
        foreach (var pair in _pairs.Values)
        {
            UnsubscribeFromLifeEvents(pair);
        }
        
        // Re-discover entities
        FindAllEntities();
        CreatePairs();
        SetupLifeSynchronization();
        
        // Ensure hidden pairs stay hidden
        foreach (var pair in _pairs.Values)
        {
            if (pair.IsHidden)
            {
                if (IsInstanceValid(pair.Monster)) pair.Monster.ForceHide();
                if (IsInstanceValid(pair.Ghost)) pair.Ghost.FadeOutAndDestroy();
            }
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
        
        // Reset all shared lives to full
        foreach (var sharedLife in _sharedLives.Values)
        {
            sharedLife.ResetToFull();
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

            // Unsubscribe existing events before we rebuild
            UnsubscribeFromLifeEvents(oldPair);
        }

        // Rebuild entity lists and pairings
        FindAllEntities();
        CreatePairs();
        SetupLifeSynchronization();

        // Re-apply hidden state for pairs that were hidden before the update
        foreach (var pair in _pairs.Values)
        {
            if (pair == null || pair.IsHidden) continue;

            if (IsInstanceValid(pair.Monster) && previouslyHiddenMonsterIds.Contains(pair.Monster.GetInstanceId()))
            {
                pair.IsHidden = true;
                pair.Monster.ForceHide();

                if (IsInstanceValid(pair.Ghost))
                {
                    try { pair.Ghost.FadeOutAndDestroy(); } catch { }
                }

                // Ensure we don't receive life events while hidden
                UnsubscribeFromLifeEvents(pair);
            }
        }
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
        
        // Reset all shared lives and show entities
        foreach (var sharedLife in _sharedLives.Values)
        {
            sharedLife.ResetToFull();
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
            // Clean up all subscriptions
            foreach (var pair in _pairs.Values)
            {
                UnsubscribeFromLifeEvents(pair);
            }
            
            Instance = null;
        }
    }
}

// Fixed shared life class that matches MonsterLife event signature
public class SharedLife
{
    public int RedHearts { get; private set; }
    public int PurpleHearts { get; private set; }
    public int MaxRed { get; private set; } = 3;
    public int MaxPurple { get; private set; } = 3;
    
    private readonly EnemyPair _pair;
    private bool _syncInProgress = false;
    
    public SharedLife(EnemyPair pair)
    {
        _pair = pair;
    }
    
    public void SetLife(int red, int purple)
    {
        RedHearts = Math.Max(0, red);
        PurpleHearts = Math.Max(0, purple);
        
        SyncToEntities();
        CheckForDeath();
    }
    
    public void SetMaxLife(int maxRed, int maxPurple)
    {
        MaxRed = Math.Max(1, maxRed);
        MaxPurple = Math.Max(1, maxPurple);
    }
    
    // Fixed: Match MonsterLife.LifeChanged signature (currentLife, maxLife, purpleHearts)
    public void OnMonsterLifeChanged(int currentLife, int maxLife, int purpleHearts)
    {
        if (_syncInProgress) return;
        
        // Calculate red hearts from total life minus purple hearts
        int redHearts = Math.Max(0, currentLife - purpleHearts);
        
        RedHearts = redHearts;
        PurpleHearts = purpleHearts;
        
        SyncToGhost();
        CheckForDeath();
    }
    
    // Fixed: Match MonsterLife.LifeChanged signature for ghost
    public void OnGhostLifeChanged(int currentLife, int maxLife, int purpleHearts)
    {
        if (_syncInProgress) return;
        
        // Calculate red hearts from total life minus purple hearts
        int redHearts = Math.Max(0, currentLife - purpleHearts);
        
        RedHearts = redHearts;
        PurpleHearts = purpleHearts;
        
        SyncToMonster();
        CheckForDeath();
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
            ghostLife.SetHearts(RedHearts, PurpleHearts);
        }
        finally
        {
            _syncInProgress = false;
        }
    }
    
    private void CheckForDeath()
    {
        bool isDead = RedHearts <= 0 && PurpleHearts <= 0;
        
        if (isDead && !_pair.IsHidden)
        {
            EnemyManager.Instance?.HandlePairDeath(_pair);
        }
    }
    
    public void ResetToFull()
    {
        SetLife(MaxRed, MaxPurple);
    }
    
    private static bool IsInstanceValid(GodotObject obj)
    {
        return GodotObject.IsInstanceValid(obj);
    }
}