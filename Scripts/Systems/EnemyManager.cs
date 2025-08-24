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
        GD.Print("EnemyManager initialized");
    }

    private void DiscoverEnemies()
    {
        // Find all existing enemies in scene
        FindFollowerMonsters();
        FindRegularMonsters();
        
        GD.Print($"EnemyManager: Found {_followerMonsters.Count} follower monsters, {_regularMonsters.Count} regular monsters");
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
        GD.Print("All follower monsters hidden");
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
        GD.Print("All follower monsters shown");
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
            GD.PrintErr("EnemyManager: Could not load Monster scene");
            return null;
        }

        var follower = followerScene.Instantiate() as Monster;
        if (follower == null)
        {
            GD.PrintErr("EnemyManager: Failed to instantiate Monster");
            return null;
        }

        // Enable follower behavior
        follower.EnableFollowerBehavior = true;

        // Add to scene and track
        GetTree().CurrentScene.AddChild(follower);
        follower.GlobalPosition = position;
        _followerMonsters.Add(follower);

        GD.Print($"EnemyManager: Spawned Monster with follower behavior at {position}");
        return follower;
    }
    #endregion

    #region Game Events
    public void OnPlayerDeath()
    {
        // Handle player death - could pause all enemies or trigger special behavior
        HideAllFollowers();
        GD.Print("EnemyManager: Player died, hiding all followers");
    }

    public void OnGameRestart()
    {
        // Reset all enemies to their initial states for new game
        ResetAllEnemies();
        GD.Print("EnemyManager: Game restarted, resetting all enemies");
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
                GD.Print($"Regular monster {monster.Name} - reset not implemented");
            }
        }

        GD.Print($"Reset {_followerMonsters.Count} follower monsters");
    }
    #endregion

    #region Debug
    public void LogEnemyStatus()
    {
        GD.Print("=== Enemy Manager Status ===");
        GD.Print($"Follower Monsters: {_followerMonsters.Count}");
        GD.Print($"Regular Monsters: {_regularMonsters.Count}");
        GD.Print($"Visible Followers: {GetVisibleFollowerCount()}");
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