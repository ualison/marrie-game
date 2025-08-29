using System.Collections.Generic;
using Godot;
using MarrieGame.Core.Enums;

// Manages all enemy behavior and spawning separate from GameManager
public partial class EnemyManager : Node
{
    private EventSubscriptionHelper _subscriptionHelper = new();
    // Cached prefabs to avoid repeated GD.Load calls
    private PackedScene _monsterPrefab;
    [Export] public float PairedHideTime { get; set; } = 5.0f; // seconds to hide paired monster/ghost
    #region Singleton
    public static EnemyManager Instance { get; private set; }
    #endregion

    // Helper to query whether a monster is currently hidden by any pairing hide action
    public bool IsMonsterHiddenByPair(Monster monster)
    {
        if (monster == null) return false;
        foreach (var pair in _pairs)
        {
            if (pair == null) continue;
            if (!pair.IsHiddenForPair) continue;
            if (IsInstanceValid(pair.Monster) && pair.Monster == monster) return true;
        }
        return false;
    }

    // Allow external systems to notify manager that ghosts changed (e.g., created/destroyed)
    public void NotifyGhostsChanged()
    {
        CallDeferred(nameof(DiscoverGhosts));
    }

    #region Enemy Tracking
    private List<Monster> _monsters = new();
    private bool _isPlayerDead = false;
    // Track discovered ghosts separately
    private List<GhostMonster> _ghosts = new();

    // Pairing state - map a monster (or ghost) to paired depletion flags and pending timer
    private class PairState
    {
        public Monster Monster;
        public GhostMonster Ghost;
        public bool RedDepleted = false;     // red hearts depleted (vulnerable in CurrentReality)
        public bool PurpleDepleted = false;  // purple hearts depleted (vulnerable in PastReality)
        public bool IsHiddenForPair = false;
    }

    private readonly List<PairState> _pairs = new();
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup singleton
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
        }
        else
        {
            QueueFree();
            return;
        }

        CallDeferred(nameof(DiscoverEnemies));
        CallDeferred(nameof(DiscoverGhosts));
        // Cache common prefabs
        _monsterPrefab = GD.Load<PackedScene>("res://Prefabs/Monster.tscn");
        // Connect to global game events
        GameEventSystem.PlayerDied += OnPlayerDeath;
        _subscriptionHelper.AddUnsubscriber(() => GameEventSystem.PlayerDied -= OnPlayerDeath);

        GameEventSystem.GameRestart += OnGameRestart;
        _subscriptionHelper.AddUnsubscriber(() => GameEventSystem.GameRestart -= OnGameRestart);
        // Listen for reality changes so hidden pairs remain hidden when other systems re-enable entities
        if (RealityManager.Instance != null)
        {
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
            _subscriptionHelper.AddUnsubscriber(() => RealityManager.Instance.RealityChangedEvent -= OnRealityChanged);
        }
        // Debug level - system initialization is routine
        Log.Debug("EnemyManager initialized", "EnemyManager");
    }

    private void DiscoverGhosts()
    {
        _ghosts.Clear();
        var ghosts = GetTree().GetNodesInGroup("ghost_monster");
        foreach (var node in ghosts)
        {
            if (node is GhostMonster gm)
            {
                _ghosts.Add(gm);
            }
        }

        // Attempt to pair monsters and ghosts by nearest name or proximity heuristic
        BuildPairs();
    }

    private void BuildPairs()
    {
        _pairs.Clear();

        // Simple heuristic: pair by proximity (closest ghost to each monster)
        foreach (var m in _monsters)
        {
            if (!IsInstanceValid(m)) continue;
            GhostMonster best = null;
            float bestDist = float.MaxValue;
            foreach (var g in _ghosts)
            {
                if (!IsInstanceValid(g)) continue;
                float d = m.GlobalPosition.DistanceTo(g.GlobalPosition);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }

            var pair = new PairState { Monster = m, Ghost = best };
            _pairs.Add(pair);

            // Subscribe to their life change signals safely
            TrySubscribeLife(m, pair);
            if (best != null)
            {
                TrySubscribeLife(best, pair);
            }
        }
    }

    private void TrySubscribeLife(Monster monster, PairState pair)
    {
        if (monster == null) return;
        var life = monster.GetLifeSystem();
        if (life == null) return;

        // Connect to LifeChanged and MonsterDied signals
        try
        {
            Log.Debug($"EnemyManager: Subscribing to Monster life for {monster.Name}", "EnemyManager");
            life.LifeChanged += (int cur, int max, int purple) => OnLifeChangedPair(monster, pair, cur, max, purple);
            life.MonsterDied += () => OnMonsterDiedPair(pair);
        }
        catch (System.Exception ex) { Log.Warning($"EnemyManager: Failed subscribing to monster life: {ex.Message}", "EnemyManager"); }
    }

    private void TrySubscribeLife(GhostMonster ghost, PairState pair)
    {
        if (ghost == null) return;
        var life = ghost.LifeSystem;
        if (life == null) return;
        try
        {
            Log.Debug($"EnemyManager: Subscribing to Ghost life for {ghost.Name}", "EnemyManager");
            life.LifeChanged += (int cur, int max, int purple) => OnLifeChangedPair(ghost, pair, cur, max, purple);
            life.MonsterDied += () => OnGhostDiedPair(pair);
        }
        catch (System.Exception ex) { Log.Warning($"EnemyManager: Failed subscribing to ghost life: {ex.Message}", "EnemyManager"); }
    }

    // Life changed from either side - re-evaluate using both Monster and Ghost life systems
    private void OnLifeChangedPair(Monster origin, PairState pair, int currentLife, int maxLife, int purpleHearts)
    {
        if (pair == null) return;
        EvaluatePairDepletion(pair);
    }

    private void OnLifeChangedPair(GhostMonster origin, PairState pair, int currentLife, int maxLife, int purpleHearts)
    {
        if (pair == null) return;
        EvaluatePairDepletion(pair);
    }

    private void EvaluatePairDepletion(PairState pair)
    {
        if (pair == null || pair.IsHiddenForPair) return;
        // Use safe locals to avoid accessing disposed Godot objects
        string monsterName = IsInstanceValid(pair.Monster) ? pair.Monster.Name : "<invalid_monster>";
        string ghostName = IsInstanceValid(pair.Ghost) ? pair.Ghost.Name : "<invalid_ghost>";
        Log.Debug($"EnemyManager: Evaluating pair depletion - Monster:{monsterName} Ghost:{ghostName}", "EnemyManager");

        // Compute red hearts from the real Monster's life system (safe access)
        int red = 0;
        if (IsInstanceValid(pair.Monster))
        {
            var mLife = pair.Monster.GetLifeSystem();
            red = mLife?.CurrentRedHearts ?? 0;
        }

        // Compute purple hearts from the GhostMonster's life system if available (safe)
        int purple = 0;
        if (IsInstanceValid(pair.Ghost))
        {
            var gLife = pair.Ghost.LifeSystem;
            purple = gLife?.CurrentPurpleHearts ?? 0;
        }
        else if (IsInstanceValid(pair.Monster))
        {
            // Fallback: if no valid ghost, use monster purple hearts
            var mLife = pair.Monster.GetLifeSystem();
            purple = mLife?.CurrentPurpleHearts ?? 0;
        }

        // Update flags to reflect current state (set when depleted, clear when regained)
        bool redNowDepleted = red <= 0;
        bool purpleNowDepleted = purple <= 0;

        if (redNowDepleted && !pair.RedDepleted)
        {
            pair.RedDepleted = true;
            Log.Debug($"EnemyManager: Red depleted for {monsterName}", "EnemyManager");
        }
        else if (!redNowDepleted && pair.RedDepleted)
        {
            pair.RedDepleted = false;
            Log.Debug($"EnemyManager: Red restored for {monsterName}", "EnemyManager");
        }

        if (purpleNowDepleted && !pair.PurpleDepleted)
        {
            pair.PurpleDepleted = true;
            Log.Debug($"EnemyManager: Purple depleted for pair Monster:{monsterName} Ghost:{ghostName}", "EnemyManager");
        }
        else if (!purpleNowDepleted && pair.PurpleDepleted)
        {
            pair.PurpleDepleted = false;
            Log.Debug($"EnemyManager: Purple restored for pair Monster:{monsterName} Ghost:{ghostName}", "EnemyManager");
        }

        // Only trigger the paired hide when both hearts are currently depleted at the same time
        if (redNowDepleted && purpleNowDepleted && !pair.IsHiddenForPair)
        {
            // Extra safety: ensure we still have a valid monster to hide
            if (IsInstanceValid(pair.Monster))
            {
                HidePairAndScheduleRespawn(pair);
            }
        }
    }

    private void OnMonsterDiedPair(PairState pair)
    {
        if (pair == null) return;
        // Monster died - treat as both depleted
        pair.RedDepleted = true;
        pair.PurpleDepleted = true;
        HidePairAndScheduleRespawn(pair);
    }

    private void OnGhostDiedPair(PairState pair)
    {
        if (pair == null) return;
        pair.RedDepleted = true;
        pair.PurpleDepleted = true;
        HidePairAndScheduleRespawn(pair);
    }

    private void HidePairAndScheduleRespawn(PairState pair)
    {
        if (pair.IsHiddenForPair) return;
        pair.IsHiddenForPair = true;

        // Hide monster and ghost safely
        if (IsInstanceValid(pair.Monster))
        {
            pair.Monster.ForceHide();
        }
        if (IsInstanceValid(pair.Ghost))
        {
            pair.Ghost.FadeOutAndDestroy();
        }

        Log.System($"Pair hidden for {PairedHideTime}s - Monster:{pair.Monster?.Name} Ghost:{pair.Ghost?.Name}", "EnemyManager");

        // Schedule respawn / re-show after PairedHideTime
        GetTree().CreateTimer(PairedHideTime).Timeout += () =>
        {
            // Restore flags and show/reset
            pair.RedDepleted = false;
            pair.PurpleDepleted = false;
            pair.IsHiddenForPair = false;

            if (IsInstanceValid(pair.Monster))
            {
                pair.Monster.Reset();
                pair.Monster.ForceShow();
            }

            // Ghosts are transient; we recreate a ghost only if needed during replay orchestration.
            Log.System($"Pair restored - Monster:{pair.Monster?.Name}", "EnemyManager");
        };
    }

    private void OnRealityChanged(Reality newReality)
    {
        // Ensure any pairs currently hidden remain hidden when reality toggles or replay systems re-enable nodes.
        foreach (var pair in _pairs)
        {
            if (pair == null) continue;
            if (!pair.IsHiddenForPair) continue;
            if (IsInstanceValid(pair.Monster))
            {
                // Re-apply hide to prevent external systems from showing the monster while it's part of a hidden pair
                pair.Monster.ForceHide();
            }
        }
    }

    private void DiscoverEnemies()
    {
        // Find all existing enemies in scene
        FindAllMonsters();
        // Debug level - component discovery is routine
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.Debug)) Log.Debug($"Found {_monsters.Count} monsters", "EnemyManager");
    }

    private void FindAllMonsters()
    {
        // Find all Monster nodes
        _monsters.Clear();
        var enemies = GetTree().GetNodesInGroup("enemies");
        foreach (Node enemy in enemies)
        {
            if (enemy is Monster monster)
            {
                _monsters.Add(monster);
            }
        }
    }

    private void FindRegularMonsters()
    {
        // Method removed: all monsters are tracked in _monsters
    }
    #endregion

    #region Follower Monster Control
    public void HideAllFollowers()
    {
        // Hide all monsters - prune invalid entries first to avoid wasted iterations
        _monsters.RemoveAll(m => !IsInstanceValid(m));
        foreach (var monster in _monsters)
        {
            monster.ForceHide();
        }
        Log.System("All monsters hidden", "EnemyManager");
    }

    public void ShowAllFollowers()
    {
        // Show all monsters - prune invalid entries first
        _monsters.RemoveAll(m => !IsInstanceValid(m));
        foreach (var monster in _monsters)
        {
            // If this monster is currently hidden as part of a paired hide, do not show it here
            if (IsMonsterHiddenByPair(monster)) continue;
            monster.ForceShow();
        }
        Log.System("All monsters shown", "EnemyManager");
    }

    public int GetVisibleFollowerCount()
    {
        // Count how many monsters are currently visible
        _monsters.RemoveAll(m => !IsInstanceValid(m));
        int count = 0;
        foreach (var monster in _monsters)
        {
            if (monster.IsCurrentlyVisible)
            {
                count++;
            }
        }
        return count;
    }
    #endregion

    #region Enemy Spawning
    public Monster SpawnFollowerMonster(Vector2 position, PackedScene followerScene = null)
    {
        // Spawn a new monster at position
        if (followerScene == null)
        {
            followerScene = _monsterPrefab;
        }

        if (followerScene == null)
        {
            Log.Error("Could not load Monster scene", "EnemyManager");
            return null;
        }

        var key = followerScene.ResourcePath;
        var node = PoolManager.Acquire(followerScene, key);
        var monster = node as Monster;
        if (monster == null)
        {
            Log.Error("Failed to obtain Monster instance from pool or prefab", "EnemyManager");
            return null;
        }

        // Add to scene and track
        GetTree().CurrentScene.AddChild(monster);
        monster.GlobalPosition = position;
        if (!_monsters.Contains(monster)) _monsters.Add(monster);

        Log.System($"Spawned Monster at {position}", "EnemyManager");
        return monster;
    }
    #endregion

    #region Game Events
    public void OnPlayerDeath(MarrieGame.Core.Models.PlayerDeath death)
    {
        if (_isPlayerDead) return;
        _isPlayerDead = true;
        // Handle player death - could pause all enemies or trigger special behavior
        HideAllFollowers();
        Log.System($"Player died at {death.Position} (health: {death.RemainingHealth}), hiding all followers", "EnemyManager");
    }

    public void OnGameRestart()
    {
        _isPlayerDead = false;
        // Reset all enemies to their initial states for new game
        ResetAllEnemies();
        Log.System("Game restarted, resetting all enemies", "EnemyManager");
    }

    public void ResetAllEnemies()
    {
        // Reset all monsters to initial state
        foreach (var monster in _monsters)
        {
            if (!IsInstanceValid(monster)) continue;
            // Skip resetting monsters that are currently hidden due to paired depletion
            if (IsMonsterHiddenByPair(monster)) continue;
            monster.Reset();
        }
        Log.System($"Reset {_monsters.Count} monsters", "EnemyManager");
    }
    #endregion

    #region Debug
    public void LogEnemyStatus()
    {
#if DEBUG
        Log.Debug("=== Enemy Manager Status ===", "EnemyManager");
        Log.Debug($"Monsters: {_monsters.Count}", "EnemyManager");
        Log.Debug($"Visible Monsters: {GetVisibleFollowerCount()}", "EnemyManager");
#endif
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            // Centralized unsubscribe
            _subscriptionHelper.UnsubscribeAll();
            Instance = null;
        }
    }
    #endregion
}