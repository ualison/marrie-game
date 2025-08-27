using System.Collections.Generic;
using Godot;

// Manages all enemy behavior and spawning separate from GameManager
public partial class EnemyManager : Node
{
    #region Singleton
    public static EnemyManager Instance { get; private set; }
    #endregion

    #region Enemy Tracking
    private List<Monster> _followerMonsters = new();
    private List<Monster> _regularMonsters = new();
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
        // Debug level - system initialization is routine
        Log.Debug("EnemyManager initialized", "EnemyManager");
    }

    private void DiscoverEnemies()
    {
        // Find all existing enemies in scene
        FindFollowerMonsters();
        FindRegularMonsters();
        
        // Debug level - component discovery is routine
        Log.Debug($"Found {_followerMonsters.Count} follower monsters, {_regularMonsters.Count} regular monsters", "EnemyManager");
    }

    private void FindFollowerMonsters()
    {
        // Find all Monster nodes with follower behavior enabled
        _followerMonsters.Clear();
        var enemies = GetTree().GetNodesInGroup("enemies");
        
        foreach (Node enemy in enemies)
        {
            if (enemy is Monster monster && monster.EnableFollowerBehavior)
            {
                _followerMonsters.Add(monster);
            }
        }
    }

    private void FindRegularMonsters()
    {
        // Find all regular Monster nodes (without follower behavior)
        _regularMonsters.Clear();
        var enemies = GetTree().GetNodesInGroup("enemies");
        
        foreach (Node enemy in enemies)
        {
            if (enemy is Monster monster && !monster.EnableFollowerBehavior)
            {
                _regularMonsters.Add(monster);
            }
        }
    }
    #endregion

    #region Follower Monster Control
    public void HideAllFollowers()
    {
        // Force all follower monsters to hide
        foreach (var follower in _followerMonsters)
        {
            if (IsInstanceValid(follower))
            {
                follower.ForceHide();
            }
        }
        Log.System("All follower monsters hidden", "EnemyManager");
    }

    public void ShowAllFollowers()
    {
        // Force all follower monsters to appear
        foreach (var follower in _followerMonsters)
        {
            if (IsInstanceValid(follower))
            {
                follower.ForceShow();
            }
        }
        Log.System("All follower monsters shown", "EnemyManager");
    }

    public int GetVisibleFollowerCount()
    {
        // Count how many followers are currently visible
        int count = 0;
        foreach (var follower in _followerMonsters)
        {
            if (IsInstanceValid(follower) && follower.IsVisible())
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
        // Spawn a new follower monster at position
        if (followerScene == null)
        {
            followerScene = GD.Load<PackedScene>("res://Prefabs/Monster.tscn");
        }

        if (followerScene == null)
        {
            Log.Error("Could not load Monster scene", "EnemyManager");
            return null;
        }

        var follower = followerScene.Instantiate() as Monster;
        if (follower == null)
        {
            Log.Error("Failed to instantiate Monster", "EnemyManager");
            return null;
        }

        // Enable follower behavior
        follower.EnableFollowerBehavior = true;

        // Add to scene and track
        GetTree().CurrentScene.AddChild(follower);
        follower.GlobalPosition = position;
        _followerMonsters.Add(follower);

        Log.System($"Spawned Monster with follower behavior at {position}", "EnemyManager");
        return follower;
    }
    #endregion

    #region Game Events
    public void OnPlayerDeath()
    {
        // Handle player death - could pause all enemies or trigger special behavior
        HideAllFollowers();
        Log.System("Player died, hiding all followers", "EnemyManager");
    }

    public void OnGameRestart()
    {
        // Reset all enemies to their initial states for new game
        ResetAllEnemies();
        Log.System("Game restarted, resetting all enemies", "EnemyManager");
    }

    public void ResetAllEnemies()
    {
        // Reset all follower monsters to initial state
        foreach (var follower in _followerMonsters)
        {
            if (IsInstanceValid(follower))
            {
                follower.Reset();
            }
        }

        // Reset regular monsters if they have Reset method
        foreach (var monster in _regularMonsters)
        {
            if (IsInstanceValid(monster))
            {
                // Regular monsters don't have Reset method yet, but could be added
                Log.Warning($"Regular monster {monster.Name} - reset not implemented", "EnemyManager");
            }
        }

        Log.System($"Reset {_followerMonsters.Count} follower monsters", "EnemyManager");
    }
    #endregion

    #region Debug
    public void LogEnemyStatus()
    {
        #if DEBUG
        Log.Debug("=== Enemy Manager Status ===", "EnemyManager");
        Log.Debug($"Follower Monsters: {_followerMonsters.Count}", "EnemyManager");
        Log.Debug($"Regular Monsters: {_regularMonsters.Count}", "EnemyManager");
        Log.Debug($"Visible Followers: {GetVisibleFollowerCount()}", "EnemyManager");
        #endif
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    #endregion
}