using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Apple tree that manages apple spawning and persistence
/// Can spawn 0-8 apples at random positions on the tree
/// Tracks current apple count for persistence across reloads
/// </summary>
public partial class AppleTree : Node2D
{
    #region Exported Properties
    [Export] public int MaxApples { get; set; } = 8; // Maximum apples this tree can have (0-8)
    [Export] public PackedScene AppleScene { get; set; } // Apple.tscn prefab
    #endregion

    #region Tree State
    private int _currentAppleCount = 0; // Current number of apples on tree
    private List<Node2D> _applePositions = new(); // All available apple positions
    private List<Node2D> _activeApplePositions = new(); // Currently used positions
    private List<Apple> _spawnedApples = new(); // References to spawned apples
    private static Dictionary<string, int> _treeAppleCounts = new(); // Persistent storage
    #endregion

    #region Constants
    private const int TOTAL_APPLE_POSITIONS = 8;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        ValidateMaxApples();
        DiscoverApplePositions();
        LoadTreeState();
        
        // Defer apple spawning to avoid busy setup conflicts
        CallDeferred(nameof(SpawnApples));
        
        // Only log if configuration issues exist
        if (_applePositions.Count < TOTAL_APPLE_POSITIONS)
            Log.Warning($"AppleTree missing positions: {_applePositions.Count}/{TOTAL_APPLE_POSITIONS} found", "AppleTree");
    }

    private void ValidateMaxApples()
    {
        // Ensure MaxApples is within valid range
        int originalMax = MaxApples;
        MaxApples = Mathf.Clamp(MaxApples, 0, TOTAL_APPLE_POSITIONS);
        
        // Only warn if the value was actually clamped
        if (MaxApples != originalMax)
        {
            Log.Warning($"MaxApples clamped from {originalMax} to {MaxApples} (valid range: 0-{TOTAL_APPLE_POSITIONS})", "AppleTree");
        }
    }

    private void DiscoverApplePositions()
    {
        // Find all ApplePosition nodes - they are now direct children of Root
        _applePositions.Clear();
        int missingPositions = 0;
        
        for (int i = 1; i <= TOTAL_APPLE_POSITIONS; i++)
        {
            var positionNode = GetNodeOrNull<Node2D>($"ApplePostion{i}");
            if (positionNode != null)
            {
                _applePositions.Add(positionNode);
            }
            else
            {
                missingPositions++;
                // Only warn for individual missing positions in debug mode
                #if DEBUG
                Log.Debug($"ApplePostion{i} not found in tree", "AppleTree");
                #endif
            }
        }

        // Warn once about total missing positions if any are missing
        if (missingPositions > 0)
        {
            Log.Warning($"{missingPositions} apple positions missing (found {_applePositions.Count}/{TOTAL_APPLE_POSITIONS})", "AppleTree");
        }
    }

    private void LoadTreeState()
    {
        // Load persistent tree state using scene path as key
        string treeKey = GetTreeKey();
        
        if (_treeAppleCounts.ContainsKey(treeKey))
        {
            _currentAppleCount = _treeAppleCounts[treeKey];
            // Debug level - state loading is routine
            Log.Debug($"Loaded tree state: {_currentAppleCount} apples", "AppleTree");
        }
        else
        {
            // First time - use MaxApples as starting count
            _currentAppleCount = MaxApples;
            SaveTreeState();
            // Debug level - new tree initialization is routine
            Log.Debug($"New tree initialized with {_currentAppleCount} apples", "AppleTree");
        }

        // Ensure current count doesn't exceed MaxApples
        _currentAppleCount = Mathf.Min(_currentAppleCount, MaxApples);
    }

    private string GetTreeKey()
    {
        // Generate unique key for this tree based on scene path and position
        var scenePath = GetTree().CurrentScene?.SceneFilePath ?? "unknown";
        var position = GlobalPosition;
        return $"{scenePath}:{position.X},{position.Y}";
    }
    #endregion

    #region Apple Management
    private void SpawnApples()
    {
        if (AppleScene == null)
        {
            Log.Error("AppleScene not assigned! Cannot spawn apples.", "AppleTree");
            return;
        }

        if (_currentAppleCount <= 0)
        {
            // Debug level - empty trees are expected
            Log.Debug("No apples to spawn", "AppleTree");
            return;
        }

        // Clear any existing apples
        ClearExistingApples();

        // Select random positions for apples
        SelectRandomApplePositions();

        // Spawn apples at selected positions
        SpawnApplesAtPositions();

        // Debug level - routine spawning operation
        Log.Debug($"Spawned {_spawnedApples.Count} apples on tree", "AppleTree");
    }

    private void ClearExistingApples()
    {
        // Remove any previously spawned apples
        foreach (var apple in _spawnedApples)
        {
            if (IsInstanceValid(apple))
            {
                apple.QueueFree();
            }
        }
        _spawnedApples.Clear();
        _activeApplePositions.Clear();
    }

    private void SelectRandomApplePositions()
    {
        // Randomly select positions for current apple count
        var availablePositions = new List<Node2D>(_applePositions);
        _activeApplePositions.Clear();

        for (int i = 0; i < _currentAppleCount && availablePositions.Count > 0; i++)
        {
            int randomIndex = GD.RandRange(0, availablePositions.Count - 1);
            _activeApplePositions.Add(availablePositions[randomIndex]);
            availablePositions.RemoveAt(randomIndex);
        }

        Log.Debug($"Selected {_activeApplePositions.Count} random positions", "AppleTree");
    }

    private void SpawnApplesAtPositions()
    {
        // Instantiate apples at selected positions
        foreach (var position in _activeApplePositions)
        {
            var appleInstance = AppleScene.Instantiate<Apple>();
            if (appleInstance == null)
            {
                Log.Error("Failed to instantiate apple from AppleScene", "AppleTree");
                continue;
            }

            // Add apple to scene using call_deferred to avoid setup conflicts
            GetTree().CurrentScene.CallDeferred("add_child", appleInstance);
            
            // Set position after adding to scene (deferred)
            CallDeferred(nameof(SetApplePosition), appleInstance, position.GlobalPosition);

            // Connect to apple's consumption event
            ConnectAppleEvents(appleInstance);

            _spawnedApples.Add(appleInstance);
        }
    }

    private void SetApplePosition(Apple apple, Vector2 globalPosition)
    {
        // Helper method to set apple position after it's been added to scene
        if (IsInstanceValid(apple))
        {
            apple.GlobalPosition = globalPosition;
        }
    }

    private void ConnectAppleEvents(Apple apple)
    {
        // Connect to apple tree_exiting to detect when apple is consumed
        apple.TreeExiting += OnAppleConsumed;
    }

    private void OnAppleConsumed()
    {
        // Called when an apple is consumed/destroyed
        _currentAppleCount = Mathf.Max(0, _currentAppleCount - 1);
        SaveTreeState();
        
        // Important gameplay event - player consumed an apple
        Log.Info($"Apple consumed! Tree now has {_currentAppleCount} apples", "AppleTree");
    }

    private void SaveTreeState()
    {
        // Save current apple count for persistence
        string treeKey = GetTreeKey();
        _treeAppleCounts[treeKey] = _currentAppleCount;
        
        // Debug level - routine state saving
        Log.Debug($"Tree state saved: {_currentAppleCount} apples", "AppleTree");
    }
    #endregion

    #region Public API
    public void RefreshApples()
    {
        // Manually refresh apple spawning (for debugging/testing)
        SpawnApples();
    }

    public void SetAppleCount(int count)
    {
        // Manually set apple count and respawn
        _currentAppleCount = Mathf.Clamp(count, 0, MaxApples);
        SaveTreeState();
        SpawnApples();
        
        // Debug level - manual apple count changes are for development/testing
        Log.Debug($"Apple count manually set to {_currentAppleCount}", "AppleTree");
    }

    public int GetCurrentAppleCount() => _currentAppleCount;
    
    public int GetMaxApples() => MaxApples;

    public void ResetTreeState()
    {
        // Reset tree to full capacity (for debugging)
        _currentAppleCount = MaxApples;
        SaveTreeState();
        SpawnApples();
        
        // Debug level - reset operations are typically for development/testing
        Log.Debug($"Tree state reset to {_currentAppleCount} apples", "AppleTree");
    }

    public static void ClearAllTreeStates()
    {
        // Clear all persistent tree data (for new game)
        _treeAppleCounts.Clear();
        // Debug level - typically called during development or testing
        Log.Debug("All tree states cleared", "AppleTree");
    }
    #endregion

    #region Debug Information
    public override void _ExitTree()
    {
        // Cleanup when tree is removed from scene
        // Debug level - cleanup operations are routine
        Log.Debug($"AppleTree removed with {_currentAppleCount} apples remaining", "AppleTree");
    }

    public void LogTreeStatus()
    {
        // Debug method to log current tree status
        Log.Debug($"=== AppleTree Status ===", "AppleTree");
        Log.Debug($"Max Apples: {MaxApples}", "AppleTree");
        Log.Debug($"Current Count: {_currentAppleCount}", "AppleTree");
        Log.Debug($"Spawned Apples: {_spawnedApples.Count}", "AppleTree");
        Log.Debug($"Available Positions: {_applePositions.Count}", "AppleTree");
        Log.Debug($"Tree Key: {GetTreeKey()}", "AppleTree");
    }
    #endregion
}
