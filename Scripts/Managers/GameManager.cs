using System.Collections.Generic;
using Godot;

// Central game coordinator managing player lifecycle, death tracking, and replay system
public partial class GameManager : Node
{
    #region Singleton
    public static GameManager Instance { get; private set; }
    #endregion

    #region Game State
    public Vector2 PlayerSpawnPosition { get; set; } = Vector2.Zero;
    public Vector2 LastDeathPosition { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool HasReplayData { get; private set; }
    #endregion

    #region Death Tracking
    private readonly Dictionary<string, DeathRecord> _deathRecords = new();
    private string _currentDeathId;
    #endregion

    #region Cached References
    private Player _player;
    private PlayerLife _playerLife;
    private GameOverUI _gameOverUI;
    private ReplayGem _replayGem;
    private PackedScene _replayGemScene;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Ensure singleton pattern and persistence across scenes
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            CallDeferred(nameof(EnsurePersistentAtRoot));
            GD.Print("GameManager AutoLoad initialized");
        }
        else if (Instance != this)
        {
            QueueFree();
            return;
        }

        ConnectToSceneEvents();
        CallDeferred(nameof(Initialize));
    }

    private void ConnectToSceneEvents()
    {
        // Listen for scene changes to reinitialize components
        var currentTree = GetTree();
        if (currentTree != null)
        {
            currentTree.NodeAdded += OnNodeAdded;
            currentTree.Root.ChildEnteredTree += OnRootChildEntered;
        }
    }

    private void Initialize()
    {
        // Setup all game systems
        DiscoverComponents();
        ConnectSystems();
        CachePlayerSpawn();

        // Initialize EnemyManager integration
        ConnectEnemyManager();

        GD.Print("GameManager initialized");
    }

    private void ConnectEnemyManager()
    {
        // Connect to EnemyManager if it exists
        if (EnemyManager.Instance != null)
        {
            GD.Print("GameManager: Connected to EnemyManager");
        }
        else
        {
            GD.Print("GameManager: EnemyManager not found - enemies will use default behavior");
        }
    }



    private void EnsurePersistentAtRoot()
    {
        // Move to root to persist across scene reloads
        var tree = GetTree();
        if (tree?.Root != null && GetParent() != tree.Root)
        {
            CallDeferred(nameof(ReparentToRoot));
        }
    }

    private void ReparentToRoot()
    {
        // Reparent to tree root for persistence
        var tree = GetTree();
        if (tree?.Root != null && IsInstanceValid(this))
        {
            var currentParent = GetParent();
            if (currentParent != tree.Root)
            {
                currentParent?.RemoveChild(this);
                tree.Root.AddChild(this);
                GD.Print("GameManager reparented to Root for persistence across scene reloads.");
            }
        }
    }
    #endregion

    #region Scene Management
    private void OnNodeAdded(Node node)
    {
        // Reinitialize when new scene loads
        if (node == GetTree().CurrentScene)
        {
            CallDeferred(nameof(HandleSceneReload));
        }
    }

    private async void OnRootChildEntered(Node node)
    {
        // Handle scene reload and gem creation when new scene is added to root
        if (node == GetTree().CurrentScene)
        {
            GD.Print($"=== Scene reloaded: {node.Name} ===");
            GD.Print($"HasReplayData: {HasReplayData}, LastDeathPosition: {LastDeathPosition}");

            // Wait for scene to be fully ready
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // Only spawn gem if we have replay data from a previous death
            if (HasReplayData && LastDeathPosition != Vector2.Zero)
            {
                GD.Print("Creating ReplayGem after scene reload (not immediate death)");
                CreateAndShowReplayGem(LastDeathPosition);
            }
            else
            {
                GD.Print("No replay data available for gem creation");
            }

            // Re-initialize components for the new scene
            CallDeferred(nameof(Initialize));
        }
    }

    private void HandleSceneReload()
    {
        // Handle scene reload and recreate replay gem if needed
        GD.Print($"=== Scene reloaded: {GetTree().CurrentScene?.Name} ===");

        Initialize();

        // Create gem if we have replay data from previous death
        if (HasReplayData && LastDeathPosition != Vector2.Zero)
        {
            GD.Print("Creating ReplayGem after scene reload");
            CreateAndShowReplayGem(LastDeathPosition);
        }
    }

    public void RestartGame()
    {
        // Store replay state
        bool shouldShowGem = HasReplayData && LastDeathPosition != Vector2.Zero;
        Vector2 gemPosition = LastDeathPosition;

        // Reset game state
        IsGameOver = false;
        ClearRecorderForNewSession();

        // Debug: Log positions before reset
        GD.Print($"=== GAME RESTART DEBUG ===");
        GD.Print($"PlayerSpawnPosition: {PlayerSpawnPosition}");
        GD.Print($"Player current position: {_player?.GlobalPosition}");

        // Reset player to spawn position and state
        if (_player != null)
        {
            GD.Print($"Resetting player from {_player.GlobalPosition} to {PlayerSpawnPosition}");
            _player.GlobalPosition = PlayerSpawnPosition;
            _player.Reset(); // Use existing Reset() method
            GD.Print($"Player position after reset: {_player.GlobalPosition}");
        }
        else
        {
            GD.PrintErr("Player is null - cannot reset position!");
            // Try to find player again
            _player = GetTree().GetFirstNodeInGroup("player") as Player;
            if (_player != null)
            {
                GD.Print("Found player, resetting position");
                _player.GlobalPosition = PlayerSpawnPosition;
                _player.Reset();
            }
        }

        // Notify EnemyManager of game restart
        EnemyManager.Instance?.OnGameRestart();

        // Hide game over UI
        _gameOverUI?.HideGameOver();

        // Create replay gem if needed
        if (shouldShowGem)
        {
            CreateAndShowReplayGem(gemPosition);
        }

        // Start new recording session
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.StartRecording();
        }

        // Restart background audio
        RestartBackgroundAudio();

        GD.Print("Game restarted without scene reload");
    }

    private void RestartBackgroundAudio()
    {
        // Use AudioManager singleton if available
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.RestartBackgroundMusic();
            return;
        }

        // Fallback: find AudioManager by group or path
        var audioManager = GetTree().GetFirstNodeInGroup("background_music") as AudioStreamPlayer2D;
        if (audioManager == null)
        {
            audioManager = GetTree().CurrentScene?.GetNode<AudioStreamPlayer2D>("Systems/AudioManager");
        }

        if (audioManager != null)
        {
            audioManager.Stop();
            audioManager.Play();
            GD.Print("AudioManager restarted from beginning");
        }
        else
        {
            GD.PrintErr("AudioManager not found - check node setup");
        }
    }

    private AudioStreamPlayer2D FindAudioPlayer2D(Node node)
    {
        // Recursively find AudioStreamPlayer2D
        if (node is AudioStreamPlayer2D audio) return audio;

        foreach (Node child in node.GetChildren())
        {
            var result = FindAudioPlayer2D(child);
            if (result != null) return result;
        }
        return null;
    }

    private bool IsValidTree(SceneTree tree)
    {
        // Validate tree and current scene availability
        if (tree?.CurrentScene == null)
        {
            GD.PrintErr("Cannot reload scene - tree or CurrentScene is null");
            return false;
        }
        return true;
    }

    private void ClearRecorderForNewSession()
    {
        // Clear recorder for new session
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.ClearRecording();
        }
    }

    private void ScheduleGemCreation(SceneTree tree, Vector2 gemPosition)
    {
        // Schedule gem creation with safety checks
        var timer = tree.CreateTimer(0.5f);
        timer.Timeout += () =>
        {
            if (IsInstanceValid(this) && GetTree() != null && GameManager.Instance == this)
            {
                CreateAndShowReplayGem(gemPosition);
            }
        };
    }

    // Update QuitGame to be more direct:
    public void QuitGame()
    {
        GetTree()?.Quit();
    }

    #endregion

    #region Death System
   public void PlayerDied(Vector2 deathPosition)
{
    // Handle player death and record replay data
    if (IsGameOver) return;

    // Don't allow death during replay
    if (ReplayRecorder.Instance != null && ReplayRecorder.Instance.IsRecording == false)
    {
        GD.Print("Ignoring player death during replay");
        return;
    }

    GD.Print($"GameManager: Player died at {deathPosition}");
    
    // Set game state
    IsGameOver = true;
    LastDeathPosition = deathPosition;
    HasReplayData = true;
    
    // Record death data for replay
    RecordDeathData(deathPosition);
    
    // Show game over screen
    ShowGameOverScreen();
    
    GD.Print("GameManager: Game over sequence complete");
}

    private void RecordDeathData(Vector2 deathPosition)
    {
        // Create and store death record with replay data
        string sceneName = GetTree().CurrentScene?.Name ?? "Unknown";
        int deathCount = _deathRecords.Count + 1;
        _currentDeathId = $"{sceneName}Death{deathCount}";

        GD.Print($"GameManager: Player died at {deathPosition} - recording as {_currentDeathId}");

        var deathRecord = new DeathRecord(_currentDeathId, deathPosition);

        if (ReplayRecorder.Instance != null)
        {
            // Capture current session frames before stopping
            deathRecord.PlayerFrames = ReplayRecorder.Instance.GetPlayerFrames();
            deathRecord.MonsterFrames = ReplayRecorder.Instance.GetMonsterFrames();
            ReplayRecorder.Instance.StopRecording();

            GD.Print($"Captured {deathRecord.PlayerFrames.Count} frames for this death");
        }

        _deathRecords[_currentDeathId] = deathRecord;
        GD.Print($"Stored death record: {_currentDeathId} with {deathRecord.PlayerFrames.Count} player frames");
    }

    public DeathRecord GetLastDeathRecord()
    {
        // Retrieve most recent death record for replay
        GD.Print($"GetLastDeathRecord - Current ID: '{_currentDeathId}'");
        GD.Print($"Death records count: {_deathRecords.Count}");

        LogDeathRecords();

        if (!string.IsNullOrEmpty(_currentDeathId) && _deathRecords.ContainsKey(_currentDeathId))
        {
            GD.Print($"Found record: {_currentDeathId}");
            return _deathRecords[_currentDeathId];
        }

        GD.Print("No death record found");
        return null;
    }

    private void LogDeathRecords()
    {
        // Debug logging for death records
        foreach (var kvp in _deathRecords)
        {
            GD.Print($"  Record: {kvp.Key} with {kvp.Value.PlayerFrames.Count} frames");
        }
    }
    private void OnPlayerHealthDepleted()
    {
        // Handle health depletion event
        if (IsGameOver) return;

        GD.Print($"OnPlayerHealthDepleted called - Recording: {ReplayRecorder.Instance?.IsRecording}, GameOver: {IsGameOver}");

        Vector2 deathPosition = _player?.GlobalPosition ?? Vector2.Zero;
        PlayerDied(deathPosition);
    }
    #endregion

    #region Replay System
    public void ResetReplayData()
    {
        // Clear replay tracking for new game/run
        HasReplayData = false;
        LastDeathPosition = Vector2.Zero;

        if (_replayGem != null && IsInstanceValid(_replayGem))
        {
            _replayGem.QueueFree();
            _replayGem = null;
        }
    }

    private void CreateAndShowReplayGem(Vector2 position)
    {
        // Create and position replay gem at death location
        if (!IsInstanceValid(this) || GetTree() == null)
        {
            GD.PrintErr("GameManager disposed - cannot create ReplayGem");
            return;
        }

        GD.Print($"CreateAndShowReplayGem called with position: {position}");

        var replayGemScene = GetReplayGemScene();
        if (replayGemScene == null) return;

        CleanupExistingGem();
        InstantiateAndSetupGem(replayGemScene, position);
    }

    private PackedScene GetReplayGemScene()
    {
        // Load replay gem scene if not cached
        if (_replayGemScene == null)
        {
            _replayGemScene = GD.Load<PackedScene>("res://Prefabs//ReplayGem.tscn");
            if (_replayGemScene == null)
            {
                GD.PrintErr("Failed to load ReplayGem.tscn - check the file path");
                return null;
            }
            GD.Print("ReplayGem.tscn loaded successfully");
        }
        return _replayGemScene;
    }

    private void CleanupExistingGem()
    {
        // Remove existing gem before creating new one
        if (_replayGem != null && IsInstanceValid(_replayGem))
        {
            _replayGem.QueueFree();
            _replayGem = null;
        }
    }

    private void InstantiateAndSetupGem(PackedScene replayGemScene, Vector2 position)
    {
        // Create and setup new replay gem
        var gemNode = replayGemScene.Instantiate();
        if (gemNode == null)
        {
            GD.PrintErr("Failed to instantiate ReplayGemScene!");
            return;
        }

        _replayGem = gemNode as ReplayGem;
        if (_replayGem == null)
        {
            GD.PrintErr($"Scene instantiated but is not ReplayGem type. Got: {gemNode.GetType()}");
            gemNode.QueueFree();
            return;
        }

        AddGemToScene(position);
    }

    private void AddGemToScene(Vector2 position)
    {
        // Add gem to scene tree and show at position
        var parent = GetTree().CurrentScene;
        if (parent == null)
        {
            GD.PrintErr("CurrentScene is null; cannot add ReplayGem. Will retry on next frame.");
            RetryGemCreation(position);
            return;
        }

        parent.AddChild(_replayGem);
        GD.Print($"ReplayGem added to scene tree under '{parent.Name}'.");

        // Show gem after _Ready executes
        _replayGem.CallDeferred(nameof(_replayGem.ShowGem), position);
        GD.Print($"GameManager: ReplayGem created and will be shown at {position}");
    }

    private void RetryGemCreation(Vector2 position)
    {
        // Retry gem creation on next frame if failed
        if (IsInstanceValid(this))
        {
            GetTree().CreateTimer(0.016f).Timeout += () =>
            {
                if (IsInstanceValid(this)) CreateAndShowReplayGem(position);
            };
        }
    }
    #endregion

    #region Component Discovery
    private void DiscoverComponents()
    {
        // Find required components in current scene
        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        _playerLife = _player?.GetLifeSystem();
        _gameOverUI = FindNodeByType<GameOverUI>(GetTree().CurrentScene);

        LogComponentStatus();
    }

    private void ConnectSystems()
    {
        // Connect signals between systems
        if (_playerLife != null)
        {
            _playerLife.PlayerDied += OnPlayerHealthDepleted;
        }

        if (_gameOverUI != null)
        {
            _gameOverUI.RestartRequested += RestartGame;
            _gameOverUI.QuitRequested += QuitGame;
            _gameOverUI.HideGameOver();
        }
    }

    private void CachePlayerSpawn()
    {
        // Cache player spawn position for respawning
        if (_player != null)
            PlayerSpawnPosition = _player.GlobalPosition;
    }

    private T FindNodeByType<T>(Node root) where T : class
    {
        // Find node of specific type using breadth-first search
        if (root is T result) return result;

        var queue = new Queue<Node>();
        if (root != null) queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T found) return found;

            foreach (Node child in current.GetChildren())
                queue.Enqueue(child);
        }

        return null;
    }
    #endregion

    #region UI System
    private void ShowGameOverScreen()
    {
        // Display game over UI
        _gameOverUI?.ShowGameOver("Game Over!", "Try again!");
    }
    #endregion

    #region Debug and Utilities
    public void LogComponentStatus()
    {
        // Log current state for debugging
        GD.Print("=== GameManager State ===");
        GD.Print($"IsGameOver: {IsGameOver}");
        GD.Print($"HasReplayData: {HasReplayData}");
        GD.Print($"LastDeathPosition: {LastDeathPosition}");
        GD.Print($"ReplayGem exists: {_replayGem != null && IsInstanceValid(_replayGem)}");
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        // Clean up singleton reference and connections
        if (Instance == this)
        {
            GetTree().Root.ChildEnteredTree -= OnRootChildEntered;
            Instance = null;
        }
    }
    #endregion
}