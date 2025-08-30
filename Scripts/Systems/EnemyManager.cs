using System;
using System.Collections.Generic;
using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Models;
using MarrieGame.Systems.LifePools;

public partial class EnemyManager : Node
{
    #region Fields & Config
    [Export] public float DeathHideTime { get; set; } = 180f; // Ghost death -> respawn time for both
    [Export] public float MonsterHideTime { get; set; } = 30f; // Monster red depletion -> monster respawn time

    public static EnemyManager Instance { get; private set; }

    // Simple entity tracking
    private readonly Dictionary<Monster, GhostMonster> _monsterToGhost = new();
    private readonly Dictionary<GhostMonster, Monster> _ghostToMonster = new();
    // Pair & life-pool tracking
    private readonly Dictionary<Monster, EnemyPair> _monsterToPair = new();
    private readonly Dictionary<GhostMonster, EnemyPair> _ghostToPair = new();
    private readonly Dictionary<Monster, SeparateLifePool> _monsterLifePools = new();
    private readonly Dictionary<GhostMonster, SeparateLifePool> _ghostLifePools = new();
    
    // Track individual lifes
    private readonly Dictionary<Monster, int> _monsterRedHearts = new();
    private readonly Dictionary<GhostMonster, int> _ghostPurpleHearts = new();
    
    // Timer management
    private readonly Dictionary<Monster, Timer> _monsterRespawnTimers = new();
    private readonly Dictionary<Monster, Timer> _pairRespawnTimers = new();

    private bool _isPlayerDead = false;
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
        _monsterToGhost.Clear();
        _ghostToMonster.Clear();
        // Clear existing pair/pool mappings
        foreach (var pool in _monsterLifePools.Values)
            pool?.Cleanup();
        _monsterToPair.Clear();
        _ghostToPair.Clear();
        _monsterLifePools.Clear();
        _ghostLifePools.Clear();

        var monsters = new List<Monster>();
        var ghosts = new List<GhostMonster>();

        // Find all monsters
        foreach (Node enemy in GetTree().GetNodesInGroup("enemies"))
        {
            if (enemy is Monster monster && IsInstanceValid(monster))
            {
                monsters.Add(monster);
            }
        }

        // Find all ghosts
        foreach (Node ghost in GetTree().GetNodesInGroup("ghost_monster"))
        {
            if (ghost is GhostMonster ghostMonster && IsInstanceValid(ghostMonster))
            {
                ghosts.Add(ghostMonster);
            }
        }

        // Simple pairing by proximity
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
                _monsterToGhost[monster] = closestGhost;
                _ghostToMonster[closestGhost] = monster;
                usedGhosts.Add(closestGhost);

                // Create and register an EnemyPair and SeparateLifePool for coordinated life handling
                var pair = new EnemyPair { Monster = monster, Ghost = closestGhost, PairId = $"{monster.Name}_{closestGhost.Name}", IsHidden = false };
                var pool = new SeparateLifePool(pair, this);
                pool.Initialize();

                _monsterToPair[monster] = pair;
                _ghostToPair[closestGhost] = pair;
                _monsterLifePools[monster] = pool;
                _ghostLifePools[closestGhost] = pool;

                Log.Debug($"Paired: {monster.Name} <-> {closestGhost.Name}", "EnemyManager");
            }
        }

        Log.Debug($"Created {_monsterToGhost.Count} monster-ghost pairs", "EnemyManager");
    }
    #endregion

    #region Life System Management
    private void InitializeLifeSystems()
    {
        _monsterRedHearts.Clear();
        _ghostPurpleHearts.Clear();

        // Initialize monster red hearts
        foreach (var monster in _monsterToGhost.Keys)
        {
            var life = monster.LifeSystem;
            if (life != null)
            {
                _monsterRedHearts[monster] = life.CurrentRedHearts;
                
                // Connect to life changes
                life.LifeChanged += (current, max, purple) => OnMonsterLifeChanged(monster, current);
                life.MonsterDied += () => OnMonsterDied(monster);
            }
        }

        // Initialize ghost purple hearts
        foreach (var ghost in _ghostToMonster.Keys)
        {
            var life = ghost.LifeSystem;
            if (life != null)
            {
                _ghostPurpleHearts[ghost] = life.CurrentPurpleHearts;
                
                // Connect to life changes
                life.LifeChanged += (current, max, purple) => OnGhostLifeChanged(ghost, purple);
                life.MonsterDied += () => OnGhostDied(ghost);
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
        if (!_ghostPurpleHearts.ContainsKey(ghost)) return;
        
        _ghostPurpleHearts[ghost] = purpleHearts;
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
    private void HandleGhostDeath(GhostMonster ghost)
    {
        if (!_ghostToMonster.TryGetValue(ghost, out var monster)) return;

        Log.System($"Ghost died - hiding both entities for {DeathHideTime}s", "EnemyManager");

        // Cancel any monster-only respawn
        CancelMonsterRespawn(monster);

        // Hide both immediately
        if (IsInstanceValid(monster))
        {
            monster.ForceHide();
        }

        if (IsInstanceValid(ghost))
        {
            ghost.FadeOutAndDestroy();
        }

        // Schedule pair respawn
        SchedulePairRespawn(monster, ghost, DeathHideTime);
    }

    private void HandleMonsterRedDepleted(Monster monster)
    {
        Log.System($"Monster red hearts depleted - hiding for {MonsterHideTime}s", "EnemyManager");

        // Hide only the monster
        if (IsInstanceValid(monster))
        {
            monster.ForceHide();
        }

        // Schedule monster-only respawn
        ScheduleMonsterRespawn(monster, MonsterHideTime);
    }

    private void SchedulePairRespawn(Monster monster, GhostMonster ghost, float seconds)
    {
        CancelPairRespawn(monster);

        var timer = new Timer { OneShot = true, WaitTime = seconds };
        AddChild(timer);
        _pairRespawnTimers[monster] = timer;

        timer.Timeout += () =>
        {
            RespawnPair(monster, ghost);
            timer.QueueFree();
            _pairRespawnTimers.Remove(monster);
        };

        timer.Start();
    }

    private void ScheduleMonsterRespawn(Monster monster, float seconds)
    {
        CancelMonsterRespawn(monster);

        var timer = new Timer { OneShot = true, WaitTime = seconds };
        AddChild(timer);
        _monsterRespawnTimers[monster] = timer;

        timer.Timeout += () =>
        {
            RespawnMonsterOnly(monster);
            timer.QueueFree();
            _monsterRespawnTimers.Remove(monster);
        };

        timer.Start();
    }

    private void CancelPairRespawn(Monster monster)
    {
        if (_pairRespawnTimers.TryGetValue(monster, out var timer))
        {
            timer.Stop();
            timer.QueueFree();
            _pairRespawnTimers.Remove(monster);
        }
    }

    private void CancelMonsterRespawn(Monster monster)
    {
        if (_monsterRespawnTimers.TryGetValue(monster, out var timer))
        {
            timer.Stop();
            timer.QueueFree();
            _monsterRespawnTimers.Remove(monster);
        }
    }

    private void RespawnPair(Monster monster, GhostMonster ghost)
    {
        Log.System("Respawning monster-ghost pair", "EnemyManager");

        // Reset both to full health
        if (IsInstanceValid(monster))
        {
            monster.LifeSystem?.Reset();
            _monsterRedHearts[monster] = monster.LifeSystem?.CurrentRedHearts ?? 0;
            
            var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
            if (currentReality == Reality.CurrentReality)
            {
                monster.ForceShow();
            }
        }

        // Ghost will be recreated by replay system in past reality
        if (IsInstanceValid(ghost))
        {
            ghost.LifeSystem?.Reset();
            _ghostPurpleHearts[ghost] = ghost.LifeSystem?.CurrentPurpleHearts ?? 0;
        }
    }

    private void RespawnMonsterOnly(Monster monster)
    {
        Log.System("Respawning monster after red heart depletion", "EnemyManager");

        if (IsInstanceValid(monster))
        {
            // Reset only red hearts
            var life = monster.LifeSystem;
            if (life != null)
            {
                life.SetHearts(life.MaxLife, 0); // Full red hearts, no purple
                _monsterRedHearts[monster] = life.CurrentRedHearts;
            }

            monster.ForceShow();
        }
    }
    #endregion

    #region Reality Management
    private void OnRealityChanged(Reality newReality)
    {
        Log.System($"Reality changed to {newReality}", "EnemyManager");

        foreach (var kvp in _monsterToGhost)
        {
            var monster = kvp.Key;
            var ghost = kvp.Value;

            if (!IsInstanceValid(monster)) continue;

            if (newReality == Reality.CurrentReality)
            {
                // Show monster if it has red hearts
                if (_monsterRedHearts.GetValueOrDefault(monster, 0) > 0)
                {
                    monster.ForceShow();
                }

                // Hide ghost
                if (IsInstanceValid(ghost))
                {
                    ghost.FadeOutAndDestroy();
                }
            }
            else if (newReality == Reality.PastReality)
            {
                // Hide monster
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
        return _ghostPurpleHearts.GetValueOrDefault(ghost, 0);
    }

    public bool IsMonsterPaired(Monster monster)
    {
        return _monsterToGhost.ContainsKey(monster);
    }

    public bool IsGhostPaired(GhostMonster ghost)
    {
        return _ghostToMonster.ContainsKey(ghost);
    }

    public void ResetAllEnemies()
    {
        CancelAllTimers();

        foreach (var monster in _monsterToGhost.Keys)
        {
            if (IsInstanceValid(monster))
            {
                monster.Reset();
                _monsterRedHearts[monster] = monster.LifeSystem?.CurrentRedHearts ?? 0;
            }
        }

        foreach (var ghost in _ghostToMonster.Keys)
        {
            if (IsInstanceValid(ghost))
            {
                ghost.LifeSystem?.Reset();
                _ghostPurpleHearts[ghost] = ghost.LifeSystem?.CurrentPurpleHearts ?? 0;
            }
        }

        // Reset pair hidden flags and life pools
        foreach (var pair in _monsterToPair.Values)
        {
            if (pair != null)
            {
                pair.IsHidden = false;
            }
        }

        foreach (var pool in _monsterLifePools.Values)
        {
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
        foreach (var pool in _monsterLifePools.Values)
        {
            pool?.Cleanup();
        }
        _monsterLifePools.Clear();
        _ghostLifePools.Clear();
        _monsterToPair.Clear();
        _ghostToPair.Clear();
    }
    #endregion

    #region LifePool & Pair API
    public SeparateLifePool GetLifePoolForMonster(Monster monster)
    {
        if (monster == null) return null;
        _monsterLifePools.TryGetValue(monster, out var pool);
        return pool;
    }

    public SeparateLifePool GetLifePoolForGhost(GhostMonster ghost)
    {
        if (ghost == null) return null;
        _ghostLifePools.TryGetValue(ghost, out var pool);
        return pool;
    }

    public bool IsMonsterHiddenByPair(Monster monster)
    {
        if (monster == null) return false;
        if (!_monsterToPair.TryGetValue(monster, out var pair)) return false;
        return pair.IsHidden;
    }

    // Adapter for SeparateLifePool -> call when monster red depleted via pool
    public void HandleMonsterRedDepleted(EnemyPair pair)
    {
        if (pair == null) return;

        if (!_monsterToGhost.TryGetValue(pair.Monster, out var ghost))
        {
            // fallback: try to use pair.Ghost
            ghost = pair.Ghost;
        }

        // Mark pair hidden
        pair.IsHidden = true;

        // Hide only the monster
        if (IsInstanceValid(pair.Monster))
        {
            pair.Monster.ForceHide();
        }

        // Schedule monster-only respawn (use existing scheduling)
        ScheduleMonsterRespawn(pair.Monster, MonsterHideTime);
    }

    // Adapter for SeparateLifePool -> call when ghost dies via pool
    public void HandleGhostDeath(EnemyPair pair)
    {
        if (pair == null) return;

        var monster = pair.Monster;
        var ghost = pair.Ghost;

        Log.System($"Ghost died - hiding both entities for {DeathHideTime}s", "EnemyManager");

        // Cancel any monster-only respawn
        CancelMonsterRespawn(monster);

        // Hide both immediately
        if (IsInstanceValid(monster))
        {
            monster.ForceHide();
        }

        if (IsInstanceValid(ghost))
        {
            ghost.FadeOutAndDestroy();
        }

        // Mark pair hidden
        pair.IsHidden = true;

        // Schedule pair respawn
        SchedulePairRespawn(monster, ghost, DeathHideTime);
    }
    #endregion

    #region Game Events
    private void OnPlayerDied(PlayerDeath death)
    {
        if (_isPlayerDead) return;
        _isPlayerDead = true;

        foreach (var monster in _monsterToGhost.Keys)
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
            foreach (var monster in _monsterToGhost.Keys)
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