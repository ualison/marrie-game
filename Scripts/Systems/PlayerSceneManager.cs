using Godot;
using System;

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
    private Vector2? _pendingPuffTeleportPosition = null;
    #endregion

    #region Component References
    private Node2D _currentPlayer;
    private Node2D _currentPuff;
    #endregion

    #region Events
    public static event Action<Node2D> PlayerInstantiated;
    public static event Action<Node2D> PuffInstantiated;
    public static event Action PuffUnlocked;
    public static event Action<Vector2> CrystalTeleportSpawn;
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
        var playerPosition = FindPlayerPosition();

        // Check if player already exists in the scene
        var existingPlayer = GetTree().GetFirstNodeInGroup("player");
        if (existingPlayer != null)
        {
            var existingPlayerNode = existingPlayer as Node2D;
            _currentPlayer = existingPlayerNode;

            if (existingPlayerNode != null)
            {
                bool positioned = PositionPlayer(existingPlayerNode, playerPosition);
                if (positioned)
                {
                    Log.Debug($"Player already exists - repositioned to {existingPlayerNode.GlobalPosition}", "PlayerSceneManager");
                }
                else
                {
                    Log.Debug($"Player already exists in scene: {existingPlayer.Name}", "PlayerSceneManager");
                }
            }
            else
            {
                Log.Warning("Player found in group 'player' is not a Node2D", "PlayerSceneManager");
            }

            // Still notify other systems about the existing player
            PlayerInstantiated?.Invoke(existingPlayerNode ?? existingPlayer as Node2D);
            GameEventSystem.OnPlayerSpawned(existingPlayerNode ?? existingPlayer as Node2D);
            return;
        }

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
            
            bool positioned = PositionPlayer(playerNode, playerPosition);
            if (positioned)
            {
                Log.System($"Player instantiated at position {playerNode.GlobalPosition}", "PlayerSceneManager");
            }
            else
            {
                Log.System("Player instantiated without explicit spawn position - using prefab default", "PlayerSceneManager");
            }
            _currentPlayer = playerNode;

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
        var puffPosition = FindPuffPosition();

        // Check if Puff already exists in the scene
        var existingPuff = GetTree().GetFirstNodeInGroup("puff");
        if (existingPuff != null)
        {
            var existingPuffNode = existingPuff as Node2D;
            _currentPuff = existingPuffNode;

            if (existingPuffNode != null)
            {
                bool positioned = PositionPuff(existingPuffNode, puffPosition);
                if (positioned)
                {
                    Log.Debug($"Puff already exists - repositioned to {existingPuffNode.GlobalPosition}", "PlayerSceneManager");
                }
                else
                {
                    Log.Debug($"Puff already exists in scene: {existingPuff.Name}", "PlayerSceneManager");
                }
            }
            else
            {
                Log.Warning("Puff found in group 'puff' is not a Node2D", "PlayerSceneManager");
            }

            // Still notify other systems about the existing Puff
            PuffInstantiated?.Invoke(existingPuffNode ?? existingPuff as Node2D);
            GameEventSystem.OnPuffSpawned(existingPuffNode ?? existingPuff as Node2D);
            return;
        }

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
            
            bool positioned = PositionPuff(puffNode, puffPosition);
            if (positioned)
            {
                Log.System($"Puff instantiated at position {puffNode.GlobalPosition}", "PlayerSceneManager");
            }
            else
            {
                Log.System("Puff instantiated without explicit spawn position - using prefab default", "PlayerSceneManager");
            }
            _currentPuff = puffNode;

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
        _pendingPuffTeleportPosition = null;
        Log.Debug($"Crystal teleport position set to {position}", "PlayerSceneManager");
    }

    /// <summary>
    /// Clear crystal teleport position
    /// </summary>
    public void ClearCrystalTeleportPosition()
    {
        _crystalTeleportPosition = null;
        _pendingPuffTeleportPosition = null;
        Log.Debug("Crystal teleport position cleared", "PlayerSceneManager");
    }
    #endregion

    #region Utility Methods
    private static readonly Vector2 PuffTeleportOffset = new Vector2(50, 0);

    private bool PositionPlayer(Node2D playerNode, Node2D spawnMarker)
    {
        if (playerNode == null)
            return false;

        if (_crystalTeleportPosition.HasValue)
        {
            var teleportPos = _crystalTeleportPosition.Value;
            playerNode.GlobalPosition = teleportPos;
            _pendingPuffTeleportPosition = teleportPos;

            Log.Debug("Used crystal teleport position for player spawn", "PlayerSceneManager");
            _crystalTeleportPosition = null;

            // Notify other systems about crystal teleport spawn
            CrystalTeleportSpawn?.Invoke(teleportPos);
            return true;
        }

        if (spawnMarker != null)
        {
            playerNode.GlobalPosition = spawnMarker.GlobalPosition;
            return true;
        }

        return false;
    }

    private bool PositionPuff(Node2D puffNode, Node2D spawnMarker)
    {
        if (puffNode == null)
            return false;

        if (_pendingPuffTeleportPosition.HasValue)
        {
            puffNode.GlobalPosition = _pendingPuffTeleportPosition.Value + PuffTeleportOffset;
            Log.Debug("Used player's teleport position for Puff spawn", "PlayerSceneManager");
            _pendingPuffTeleportPosition = null;
            return true;
        }

        if (_crystalTeleportPosition.HasValue)
        {
            puffNode.GlobalPosition = _crystalTeleportPosition.Value + PuffTeleportOffset;
            Log.Debug("Used crystal teleport position for Puff spawn", "PlayerSceneManager");
            _crystalTeleportPosition = null;
            return true;
        }

        if (spawnMarker != null)
        {
            puffNode.GlobalPosition = spawnMarker.GlobalPosition;
            return true;
        }

        return false;
    }

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