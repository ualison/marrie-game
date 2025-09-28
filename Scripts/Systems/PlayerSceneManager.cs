using Godot;
using MarrieGame.Core.Logging;

/// <summary>
/// Manages global player and Puff instantiation across scenes
/// Single Responsibility: Global Character Management
/// 
/// SOLID Compliance:
/// - Single Responsibility: Only manages global character instantiation
/// - Open/Closed: Extensible through events, no need to modify existing code
/// - Dependency Inversion: Depends on abstractions (events) not implementations
/// </summary>
public partial class PlayerSceneManager : Node
{
    public static PlayerSceneManager Instance { get; private set; }

    #region Exported Properties
    [Export] public PackedScene PlayerPrefab { get; set; }
    [Export] public PackedScene PuffPrefab { get; set; }
    #endregion

    #region Game State
    public bool IsPuffUnlocked { get; private set; } = false;
    private Vector2? _crystalTeleportPosition = null;
    #endregion

    #region Component References
    private Node2D _currentPlayer;
    private Node2D _currentPuff;
    #endregion

    #region Events
    public static event System.Action<Node2D> PlayerInstantiated;
    public static event System.Action<Node2D> PuffInstantiated;
    public static event System.Action PuffUnlocked;
    #endregion

    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;

            // Load default prefabs if not set
            LoadDefaultPrefabs();

            // Listen to scene changes to instantiate characters
            SceneLifecycleManager.SceneReady += OnSceneReady;

            // Subscribe to events for Puff unlock
            SubscribeToEvents();

            Log.System("PlayerSceneManager initialized", "PlayerSceneManager");

            // Initialize for current scene
            CallDeferred(nameof(InitializeCurrentScene));
        }
        else
        {
            QueueFree();
        }
    }

    private void LoadDefaultPrefabs()
    {
        if (PlayerPrefab == null)
        {
            PlayerPrefab = GD.Load<PackedScene>("res://Prefabs/Player.tscn");
            Log.Debug("Loaded default Player prefab", "PlayerSceneManager");
        }

        if (PuffPrefab == null)
        {
            PuffPrefab = GD.Load<PackedScene>("res://Prefabs/Puff.tscn");
            Log.Debug("Loaded default Puff prefab", "PlayerSceneManager");
        }

        if (PlayerPrefab == null)
        {
            Log.Error("Failed to load Player prefab!", "PlayerSceneManager");
        }

        if (PuffPrefab == null)
        {
            Log.Error("Failed to load Puff prefab!", "PlayerSceneManager");
        }
    }

    private void SubscribeToEvents()
    {
        // Listen for game events that might trigger Puff unlock
        GameEventSystem.GameRestart += OnGameRestart;
        
        Log.Debug("Event subscriptions active", "PlayerSceneManager");
    }

    private void InitializeCurrentScene()
    {
        OnSceneReady();
    }

    private void OnSceneReady()
    {
        Log.Debug("Scene ready - checking for character instantiation", "PlayerSceneManager");

        // Always instantiate player
        InstantiatePlayerIfNeeded();

        // Only instantiate Puff if it's unlocked
        if (IsPuffUnlocked)
        {
            InstantiatePuffIfNeeded();
        }
    }

    private void OnGameRestart()
    {
        Log.Debug("Game restart - resetting character state", "PlayerSceneManager");
        
        // Note: We don't reset IsPuffUnlocked on restart - it persists across game sessions
        // This way once Puff is unlocked, it stays unlocked
        
        // Clean up existing character references
        _currentPlayer = null;
        _currentPuff = null;
    }

    #region Player Management
    private void InstantiatePlayerIfNeeded()
    {
        // Check if player already exists in the scene
        var existingPlayer = GetTree().GetFirstNodeInGroup("player");
        if (existingPlayer != null)
        {
            _currentPlayer = existingPlayer as Node2D;
            Log.Debug($"Player already exists in scene: {existingPlayer.Name}", "PlayerSceneManager");
            
            // Still notify other systems about the existing player
            PlayerInstantiated?.Invoke(_currentPlayer);
            GameEventSystem.OnPlayerSpawned(_currentPlayer);
            return;
        }

        // Find PlayerPosition node
        var playerPosition = FindPlayerPosition();
        if (playerPosition == null)
        {
            Log.Warning("No PlayerPosition node found in current scene", "PlayerSceneManager");
            return;
        }

        // Instantiate player
        if (PlayerPrefab == null)
        {
            Log.Error("Cannot instantiate player - PlayerPrefab is null", "PlayerSceneManager");
            return;
        }

        var playerInstance = PlayerPrefab.Instantiate();
        if (playerInstance == null)
        {
            Log.Error("Failed to instantiate Player prefab", "PlayerSceneManager");
            return;
        }

        // Add to scene
        var sceneRoot = GetTree().CurrentScene;
        if (sceneRoot == null)
        {
            Log.Error("Cannot instantiate player - no current scene", "PlayerSceneManager");
            playerInstance.QueueFree();
            return;
        }

        // Set position and add to scene
        if (playerInstance is Node2D playerNode)
        {
            sceneRoot.AddChild(playerInstance);
            
            // Use crystal teleport position if available, otherwise use PlayerPosition
            Vector2 spawnPosition = _crystalTeleportPosition ?? playerPosition.GlobalPosition;
            playerNode.GlobalPosition = spawnPosition;
            _currentPlayer = playerNode;

            Log.System($"Player instantiated at position {playerNode.GlobalPosition}", "PlayerSceneManager");

            // Clear crystal teleport position after use
            if (_crystalTeleportPosition.HasValue)
            {
                Log.Debug("Used crystal teleport position for player spawn", "PlayerSceneManager");
                _crystalTeleportPosition = null;
            }

            // Notify other systems
            PlayerInstantiated?.Invoke(playerNode);
            GameEventSystem.OnPlayerSpawned(playerNode);
        }
        else
        {
            Log.Error("Player prefab is not Node2D", "PlayerSceneManager");
            sceneRoot.AddChild(playerInstance);
            _currentPlayer = playerInstance as Node2D;
        }
    }

    private Node2D FindPlayerPosition()
    {
        // Search for PlayerPosition node in the scene
        var sceneRoot = GetTree().CurrentScene;
        if (sceneRoot == null) return null;

        return FindNodeByName(sceneRoot, "PlayerPosition") as Node2D;
    }
    #endregion

    #region Puff Management
    private void InstantiatePuffIfNeeded()
    {
        // Check if Puff already exists in the scene
        var existingPuff = GetTree().GetFirstNodeInGroup("puff");
        if (existingPuff != null)
        {
            _currentPuff = existingPuff as Node2D;
            Log.Debug($"Puff already exists in scene: {existingPuff.Name}", "PlayerSceneManager");
            
            // Still notify other systems about the existing Puff
            PuffInstantiated?.Invoke(_currentPuff);
            GameEventSystem.OnPuffSpawned(_currentPuff);
            return;
        }

        // Find PuffPosition node
        var puffPosition = FindPuffPosition();
        if (puffPosition == null)
        {
            Log.Debug("No PuffPosition node found in current scene", "PlayerSceneManager");
            return;
        }

        // Instantiate Puff
        if (PuffPrefab == null)
        {
            Log.Error("Cannot instantiate Puff - PuffPrefab is null", "PlayerSceneManager");
            return;
        }

        var puffInstance = PuffPrefab.Instantiate();
        if (puffInstance == null)
        {
            Log.Error("Failed to instantiate Puff prefab", "PlayerSceneManager");
            return;
        }

        // Add to scene
        var sceneRoot = GetTree().CurrentScene;
        if (sceneRoot == null)
        {
            Log.Error("Cannot instantiate Puff - no current scene", "PlayerSceneManager");
            puffInstance.QueueFree();
            return;
        }

        // Set position and add to scene
        if (puffInstance is Node2D puffNode)
        {
            sceneRoot.AddChild(puffInstance);
            
            // Use crystal teleport position if available, otherwise use PuffPosition
            Vector2 spawnPosition = _crystalTeleportPosition ?? puffPosition.GlobalPosition;
            puffNode.GlobalPosition = spawnPosition;
            _currentPuff = puffNode;

            Log.System($"Puff instantiated at position {puffNode.GlobalPosition}", "PlayerSceneManager");

            // Clear crystal teleport position after use (only if not already cleared by player spawn)
            if (_crystalTeleportPosition.HasValue)
            {
                Log.Debug("Used crystal teleport position for Puff spawn", "PlayerSceneManager");
                _crystalTeleportPosition = null;
            }

            // Notify other systems
            PuffInstantiated?.Invoke(puffNode);
            GameEventSystem.OnPuffSpawned(puffNode);
        }
        else
        {
            Log.Error("Puff prefab is not Node2D", "PlayerSceneManager");
            sceneRoot.AddChild(puffInstance);
            _currentPuff = puffInstance as Node2D;
        }
    }

    private Node2D FindPuffPosition()
    {
        // Search for PuffPosition node in the scene
        var sceneRoot = GetTree().CurrentScene;
        if (sceneRoot == null) return null;

        return FindNodeByName(sceneRoot, "PuffPosition") as Node2D;
    }

    /// <summary>
    /// Call this when Puff is unlocked (e.g., when YellowCage is opened)
    /// </summary>
    public void UnlockPuff()
    {
        if (IsPuffUnlocked)
        {
            Log.Debug("Puff already unlocked", "PlayerSceneManager");
            return;
        }

        IsPuffUnlocked = true;
        Log.System("Puff unlocked! Will spawn in future scenes", "PlayerSceneManager");

        // Notify other systems
        PuffUnlocked?.Invoke();
        GameEventSystem.OnPuffUnlocked();

        // Try to instantiate Puff in current scene if PuffPosition exists
        InstantiatePuffIfNeeded();
    }

    /// <summary>
    /// Get current Puff unlock status
    /// </summary>
    public bool GetPuffUnlockStatus()
    {
        return IsPuffUnlocked;
    }

    /// <summary>
    /// Force set Puff unlock status (for debugging or save system)
    /// </summary>
    public void SetPuffUnlockStatus(bool unlocked)
    {
        bool wasUnlocked = IsPuffUnlocked;
        IsPuffUnlocked = unlocked;

        if (!wasUnlocked && unlocked)
        {
            Log.System("Puff unlock status set to true", "PlayerSceneManager");
            PuffUnlocked?.Invoke();
            GameEventSystem.OnPuffUnlocked();
            InstantiatePuffIfNeeded();
        }
        else if (wasUnlocked && !unlocked)
        {
            Log.System("Puff unlock status set to false", "PlayerSceneManager");
            // Remove current Puff if it exists
            if (_currentPuff != null && IsInstanceValid(_currentPuff))
            {
                _currentPuff.QueueFree();
                _currentPuff = null;
            }
        }
    }

    /// <summary>
    /// Reset Puff unlock status to false (for testing or game restart)
    /// </summary>
    public void ResetPuffUnlock()
    {
        SetPuffUnlockStatus(false);
        Log.System("Puff unlock status reset", "PlayerSceneManager");
    }

    /// <summary>
    /// Set crystal teleport position for next player/Puff spawn
    /// </summary>
    public void SetCrystalTeleportPosition(Vector2 position)
    {
        _crystalTeleportPosition = position;
        Log.Debug($"Crystal teleport position set to {position}", "PlayerSceneManager");
    }

    /// <summary>
    /// Clear crystal teleport position
    /// </summary>
    public void ClearCrystalTeleportPosition()
    {
        _crystalTeleportPosition = null;
        Log.Debug("Crystal teleport position cleared", "PlayerSceneManager");
    }
    #endregion

    #region Utility Methods
    private Node FindNodeByName(Node root, string nodeName)
    {
        if (root == null) return null;

        if (root.Name == nodeName)
            return root;

        foreach (Node child in root.GetChildren())
        {
            var found = FindNodeByName(child, nodeName);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Get current player instance
    /// </summary>
    public Node2D GetCurrentPlayer()
    {
        return _currentPlayer;
    }

    /// <summary>
    /// Get current Puff instance
    /// </summary>
    public Node2D GetCurrentPuff()
    {
        return _currentPuff;
    }
    #endregion

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            // Cleanup subscriptions
            SceneLifecycleManager.SceneReady -= OnSceneReady;
            GameEventSystem.GameRestart -= OnGameRestart;

            Instance = null;
            Log.System("Cleaned up", "PlayerSceneManager");
        }
    }
}