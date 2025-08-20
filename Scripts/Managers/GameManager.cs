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

    [Export] public PackedScene ReplayGemScene { get; set; }
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
            EnsurePersistentAtRoot();
            GetTree().Root.ChildEnteredTree += OnRootChildEntered;
            CallDeferred(nameof(Initialize));
        }
        else
        {
            if (Instance != this)
            {
                QueueFree();
                return;
            }
            CallDeferred(nameof(Initialize));
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

        GD.Print($"GameManager: Player died at {deathPosition} - recording for future replay");

        ReplayRecorder.Instance?.StopRecording();

        // Show game over screen after death animation
        GetTree().CreateTimer(2.0f).Timeout += ShowGameOverScreen;
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
        // Store replay state before reload
        bool shouldShowGem = HasReplayData && LastDeathPosition != Vector2.Zero;
        Vector2 gemPosition = LastDeathPosition;

        GD.Print($"🔄 Restart: shouldShowGem={shouldShowGem}, position={gemPosition}");
        GD.Print($"Current HasReplayData: {HasReplayData}");

        // Clear game over state but KEEP replay data
        IsGameOver = false;

        // Reload scene - this will trigger OnRootChildEntered which creates the gem
        GetTree().ReloadCurrentScene();

        // Backup: Force gem creation if the automatic system fails
        if (shouldShowGem)
        {
            GetTree().CreateTimer(0.3f).Timeout += () =>
            {
                if (_replayGem == null || !IsInstanceValid(_replayGem))
                {
                    GD.Print("🔧 Backup: Force creating gem after restart");
                    CreateAndShowReplayGem(gemPosition);
                }
            };
        }
    }

    public void QuitGame() => GetTree().Quit();
    #endregion

    #region Replay System
    // Clears replay tracking for new game/run
    public void ResetReplayData()
    {
        HasReplayData = false;
        LastDeathPosition = Vector2.Zero;
        ReplayRecorder.Instance?.ClearRecording();

        if (_replayGem != null)
        {
            _replayGem.QueueFree();
            _replayGem = null;
        }
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
    private void CreateAndShowReplayGem(Vector2 position)
    {
        GD.Print($"CreateAndShowReplayGem called with position: {position}");

        if (ReplayGemScene == null)
        {
            GD.PrintErr("ReplayGemScene is null! Please assign ReplayGem.tscn in the Inspector");
            return;
        }

        // Remove existing gem
        if (_replayGem != null && IsInstanceValid(_replayGem))
        {
            _replayGem.QueueFree();
            _replayGem = null;
        }

        // Instantiate the gem
        var gemNode = ReplayGemScene.Instantiate();
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
        var parent = GetTree().CurrentScene;
        if (parent == null)
        {
            GD.PrintErr("CurrentScene is null; cannot add ReplayGem. Will retry on next frame.");
            GetTree().CreateTimer(0.016f).Timeout += () => CreateAndShowReplayGem(position);
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
        var root = GetTree().Root;
        if (GetParent() != root)
        {
            CallDeferred(nameof(ReparentToRoot));
        }
    }

    private void ReparentToRoot()
    {
        var root = GetTree().Root;
        if (GetParent() != root)
        {
            Reparent(root);
            GD.Print("GameManager reparented to Root for persistence across scene reloads.");
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
        GD.Print($"ReplayGemScene assigned: {ReplayGemScene != null}");
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
    #endregion
}