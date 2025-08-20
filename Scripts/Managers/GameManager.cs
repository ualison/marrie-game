using System.Collections.Generic;
using Godot;

// Central game coordinator - manages player lifecycle, death tracking, and replay system
// Persists across scene reloads by attaching to the tree root
public partial class GameManager : Node
{
    #region Singleton and State
    public static GameManager Instance { get; private set; }

    public Vector2 PlayerSpawnPosition { get; set; } = Vector2.Zero;
    public Vector2 LastDeathPosition { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool HasReplayData { get; private set; }
    // Use instance variables instead of static
    private readonly Dictionary<string, DeathRecord> _deathRecords = new();
    private string _currentDeathId;

    private PackedScene _replayGemScene;
    #endregion

    #region Cached References
    private Player _player;
    private PlayerLife _playerLife;
    private GameOverUI _gameOverUI;
    private ReplayGem _replayGem;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;

            // CRITICAL: Ensure GameManager is attached to root and persists
            CallDeferred(nameof(EnsurePersistentAtRoot));

            GD.Print("GameManager AutoLoad initialized");
        }
        else if (Instance != this)
        {
            // If another instance exists, free this one
            QueueFree();
            return;
        }

        // Connect to scene changes with null check
        var currentTree = GetTree();
        if (currentTree != null)
        {
            currentTree.NodeAdded += OnNodeAdded;
        }

        CallDeferred(nameof(Initialize));
    }


    private void OnNodeAdded(Node node)
    {
        // Reinitialize when new scene loads
        if (node == GetTree().CurrentScene)
        {
            CallDeferred(nameof(HandleSceneReload));
        }
    }

    private void HandleSceneReload()
    {
        GD.Print($"=== Scene reloaded: {GetTree().CurrentScene?.Name} ===");

        // Reinitialize for new scene
        Initialize();

        // Create gem if we have replay data
        if (HasReplayData && LastDeathPosition != Vector2.Zero)
        {
            GD.Print("Creating ReplayGem after scene reload");
            CreateAndShowReplayGem(LastDeathPosition);
        }
    }


    private void Initialize()
    {
        DiscoverComponents();
        ConnectSystems();
        CachePlayerSpawn();
        GD.Print("GameManager initialized");
    }
    #endregion

    #region Death System
    // Called when player dies - records death data but doesn't create gem immediately
    // Gem will appear only after scene reload
    public void PlayerDied(Vector2 deathPosition)
    {
        if (IsGameOver) return;

        LastDeathPosition = deathPosition;
        IsGameOver = true;
        HasReplayData = true;

        string sceneName = GetTree().CurrentScene?.Name ?? "Unknown";
        int deathCount = _deathRecords.Count + 1;
        _currentDeathId = $"{sceneName}Death{deathCount}";

        GD.Print($"GameManager: Player died at {deathPosition} - recording as {_currentDeathId}");

        var deathRecord = new DeathRecord(_currentDeathId, deathPosition);
        if (ReplayRecorder.Instance != null)
        {
            deathRecord.PlayerFrames = ReplayRecorder.Instance.GetPlayerFrames();
            deathRecord.MonsterFrames = ReplayRecorder.Instance.GetMonsterFrames();
            ReplayRecorder.Instance.StopRecording();
            // Clear old recording data for next session
            ReplayRecorder.Instance.ClearRecording();
        }

        _deathRecords[_currentDeathId] = deathRecord;
        GD.Print($"Stored death record: {_currentDeathId} with {deathRecord.PlayerFrames.Count} player frames");

        GetTree().CreateTimer(2.0f).Timeout += ShowGameOverScreen;
    }
    public DeathRecord GetLastDeathRecord()
    {
        GD.Print($"GetLastDeathRecord - Current ID: '{_currentDeathId}'");
        GD.Print($"Death records count: {_deathRecords.Count}");

        foreach (var kvp in _deathRecords)
        {
            GD.Print($"  Record: {kvp.Key} with {kvp.Value.PlayerFrames.Count} frames");
        }

        if (!string.IsNullOrEmpty(_currentDeathId) && _deathRecords.ContainsKey(_currentDeathId))
        {
            GD.Print($"Found record: {_currentDeathId}");
            return _deathRecords[_currentDeathId];
        }

        GD.Print("No death record found");
        return null;
    }

    private void OnPlayerHealthDepleted()
    {
        if (IsGameOver) return;

        Vector2 deathPosition = _player?.GlobalPosition ?? Vector2.Zero;
        PlayerDied(deathPosition);
    }
    #endregion

    #region Scene Management
    // Restarts the game and reloads scene - gem will appear if replay data exists
    public void RestartGame()
    {
        // Add comprehensive null checks
        var tree = GetTree();
        if (tree == null)
        {
            GD.PrintErr("GetTree() is null in RestartGame - GameManager may have been freed");
            return;
        }

        if (tree.CurrentScene == null)
        {
            GD.PrintErr("CurrentScene is null - cannot reload scene");
            return;
        }

        // Store replay state before reload
        bool shouldShowGem = HasReplayData && LastDeathPosition != Vector2.Zero;
        Vector2 gemPosition = LastDeathPosition;

        GD.Print($"🔄 Restart: shouldShowGem={shouldShowGem}, position={gemPosition}");

        // Clear game over state but KEEP replay data
        IsGameOver = false;

        // Store the timer reference to prevent disposal issues
        if (shouldShowGem)
        {
            var timer = tree.CreateTimer(0.5f); // Increased delay
            timer.Timeout += () =>
            {
                // Triple safety check before creating gem
                if (IsInstanceValid(this) && GetTree() != null && GameManager.Instance == this)
                {
                    CreateAndShowReplayGem(gemPosition);
                }
            };
        }

        // Use CallDeferred to ensure we're not in the middle of signal processing
        CallDeferred(nameof(DoSceneReload));
    }

    private void DoSceneReload()
    {
        // Additional safety check since this is called deferred
        if (!IsInstanceValid(this))
        {
            GD.PrintErr("GameManager disposed before DoSceneReload could execute");
            return;
        }

        var tree = GetTree();
        if (tree?.CurrentScene != null)
        {
            tree.ReloadCurrentScene();
        }
        else
        {
            GD.PrintErr("Cannot reload scene - tree or CurrentScene is null");
        }
    }
    public void QuitGame() => GetTree().Quit();
    #endregion

    #region Replay System
    // Clears replay tracking for new game/run
    // Clears replay tracking for new game/run
    public void ResetReplayData()
    {
        HasReplayData = false;
        LastDeathPosition = Vector2.Zero;

        if (_replayGem != null)
        {
            _replayGem.QueueFree();
            _replayGem = null;
        }
    }

    private PackedScene GetReplayGemScene()
    {
        if (_replayGemScene == null)
        {
            _replayGemScene = GD.Load<PackedScene>("res://Prefabs//ReplayGem.tscn");
            if (_replayGemScene == null)
            {
                GD.PrintErr("Failed to load ReplayGem.tscn - check the file path");
            }
            else
            {
                GD.Print("ReplayGem.tscn loaded successfully");
            }
        }
        return _replayGemScene;
    }

    // Called when new scene is loaded - creates replay gem if replay data exists
    private async void OnRootChildEntered(Node node)
    {
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
                GD.Print("🎮 Creating ReplayGem after scene reload (not immediate death)");
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

    // Creates and positions the replay gem at death location
    // Creates and positions the replay gem at death location
    private void CreateAndShowReplayGem(Vector2 position)
    {
        // CRITICAL: Add disposal and validity checks at the start
        if (!IsInstanceValid(this))
        {
            GD.PrintErr("GameManager disposed - cannot create ReplayGem");
            return;
        }

        var tree = GetTree();
        if (tree == null)
        {
            GD.PrintErr("GetTree() returned null - cannot create ReplayGem");
            return;
        }

        GD.Print($"CreateAndShowReplayGem called with position: {position}");

        var replayGemScene = GetReplayGemScene();
        if (replayGemScene == null)
        {
            GD.PrintErr("ReplayGemScene could not be loaded!");
            return;
        }

        // Remove existing gem
        if (_replayGem != null && IsInstanceValid(_replayGem))
        {
            _replayGem.QueueFree();
            _replayGem = null;
        }

        // Instantiate the gem
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

        // Add to scene tree FIRST (this will trigger _Ready)
        var parent = tree.CurrentScene;
        if (parent == null)
        {
            GD.PrintErr("CurrentScene is null; cannot add ReplayGem. Will retry on next frame.");
            // Don't retry if we're disposed - just fail gracefully
            if (IsInstanceValid(this))
            {
                tree.CreateTimer(0.016f).Timeout += () =>
                {
                    if (IsInstanceValid(this)) CreateAndShowReplayGem(position);
                };
            }
            return;
        }

        parent.AddChild(_replayGem);
        GD.Print($"ReplayGem added to scene tree under '{parent.Name}'.");

        // Show the gem AFTER its _Ready executes
        _replayGem.CallDeferred(nameof(_replayGem.ShowGem), position);

        GD.Print($"GameManager: ReplayGem created and will be shown at {position}");
    }
    #endregion

    #region Component Discovery and Connection
    private void DiscoverComponents()
    {
        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        _playerLife = _player?.GetLifeSystem();
        _gameOverUI = FindNodeByType<GameOverUI>(GetTree().CurrentScene);

        LogComponentStatus();
    }

    private void ConnectSystems()
    {
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
        if (_player != null)
            PlayerSpawnPosition = _player.GlobalPosition;
    }
    #endregion

    #region UI System
    private void ShowGameOverScreen()
    {
        _gameOverUI?.ShowGameOver("Game Over!", "Try again!");
    }
    #endregion

    #region Utility Methods
    private void EnsurePersistentAtRoot()
    {
        var tree = GetTree();
        if (tree?.Root != null && GetParent() != tree.Root)
        {
            CallDeferred(nameof(ReparentToRoot));
        }
    }

    private void ReparentToRoot()
    {
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

    // Simple breadth-first search to find node of specific type
    private T FindNodeByType<T>(Node root) where T : class
    {
        if (root is T result) return result;

        var queue = new System.Collections.Generic.Queue<Node>();
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

    public void LogComponentStatus()
    {
        GD.Print("=== GameManager State ===");
        GD.Print($"IsGameOver: {IsGameOver}");
        GD.Print($"HasReplayData: {HasReplayData}");
        GD.Print($"LastDeathPosition: {LastDeathPosition}");
        GD.Print($"ReplayGem exists: {_replayGem != null && IsInstanceValid(_replayGem)}");
    }

    // Add this method before the #region Cleanup
    private bool IsValidAndInTree()
    {
        return IsInstanceValid(this) && IsInsideTree() && GetTree() != null;
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            GetTree().Root.ChildEnteredTree -= OnRootChildEntered;
            Instance = null;
        }
    }

    // Add this method before the #endregion Cleanup
    private bool IsValidForOperations()
    {
        return IsInsideTree() && GetTree() != null && GetTree().CurrentScene != null;
    }
    #endregion
}