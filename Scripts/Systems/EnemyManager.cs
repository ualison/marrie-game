using System.Collections.Generic;
using Godot;

// Manages all enemy behavior and spawning separate from GameManager
public partial class EnemyManager : Node
{
    private EventSubscriptionHelper _subscriptionHelper = new();
    // Cached prefabs to avoid repeated GD.Load calls
    private PackedScene _monsterPrefab;
    #region Singleton
    public static EnemyManager Instance { get; private set; }
    #endregion

    #region Enemy Tracking
    private List<Monster> _monsters = new();
    private bool _isPlayerDead = false;
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
    // Cache common prefabs
    _monsterPrefab = GD.Load<PackedScene>("res://Prefabs/Monster.tscn");
        // Connect to global game events
        GameEventSystem.PlayerDied += OnPlayerDeath;
        _subscriptionHelper.AddUnsubscriber(() => GameEventSystem.PlayerDied -= OnPlayerDeath);

    GameEventSystem.GameRestart += OnGameRestart;
    _subscriptionHelper.AddUnsubscriber(() => GameEventSystem.GameRestart -= OnGameRestart);
        // Debug level - system initialization is routine
        Log.Debug("EnemyManager initialized", "EnemyManager");
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
            if (IsInstanceValid(monster))
            {
                monster.Reset();
            }
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