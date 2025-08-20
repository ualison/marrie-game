using Godot;

// Replay gem that appears at death location and allows ghost replay functionality
public partial class ReplayGem : Node2D
{
    #region Configuration
    [Export] public PackedScene GhostPlayerScene { get; set; } // Assign in inspector if available
    #endregion

    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Label _promptLabel;
    #endregion

    #region State
    private bool _playerNearby = false;
    private GhostPlayer _currentGhost;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup components and hide gem initially
        InitializeComponents();
        ConnectSignals();
        SetupPromptLabel();
        HideInitially();
        EnableInputProcessing();

        GD.Print("ReplayGem ready - hidden until activated");
    }

    private void InitializeComponents()
    {
        // Get required child nodes with error handling
        _detectionArea = GetNode<Area2D>("DetectionArea");
        _gemSprite = GetNode<Sprite2D>("GemSprite");
        _promptLabel = GetNode<Label>("PromptLabel");
    }

    private void ConnectSignals()
    {
        // Connect detection area signals
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;
    }

    private void SetupPromptLabel()
    {
        // Configure prompt label appearance
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";
    }

    private void HideInitially()
    {
        // Start hidden until shown by GameManager
        _detectionArea.SetDeferred("monitoring", false);
        _promptLabel.Hide();
        _gemSprite.Hide();
    }

    private void EnableInputProcessing()
    {
        // Enable input processing for interaction
        SetProcess(true);
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        // Handle interaction input when player is nearby
        if (!_playerNearby) return;

        if (Input.IsActionJustPressed("interact"))
        {
            HandleReplayActivation();
        }
    }

    private void HandleReplayActivation()
    {
        // Stop recording before starting replay to prevent interference
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.StopRecording();
        }

        StartReplay();
    }
    #endregion

    #region Gem Visibility Control
    public void ShowGem(Vector2 position)
    {
        // Display gem at specified position
        GlobalPosition = position;
        Show();

        if (_gemSprite != null)
        {
            _gemSprite.Show();
        }

        if (_promptLabel != null)
        {
            _promptLabel.Hide(); // Only show when player nearby
        }

        _detectionArea.SetDeferred("monitoring", true);
        GD.Print($"ReplayGem shown at {position}, scale={Scale}");
    }

    public void HideGem()
    {
        // Hide gem completely and cleanup
        _detectionArea.SetDeferred("monitoring", false);
        
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }
        
        if (_gemSprite != null)
        {
            _gemSprite.Hide();
        }
        
        _playerNearby = false;
        CleanupGhost();
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        // Show prompt when player approaches
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            if (_promptLabel != null)
            {
                _promptLabel.Show();
            }
            GD.Print("Player nearby - showing prompt");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        // Hide prompt when player leaves
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            GD.Print("Player left - hiding prompt");
        }
    }
    #endregion

    #region Replay System
    private void StartReplay()
    {
        // Initialize replay with data validation
        GD.Print("=== StartReplay called ===");

        // Pause recording immediately to prevent interference
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.PauseRecording();
        }

        var deathRecord = GetDeathRecord();
        if (deathRecord == null) return;

        var playerFrames = deathRecord.PlayerFrames;
        if (!ValidateFrameData(playerFrames)) return;

        // Clean up existing ghosts and start new replay
        CleanupGhost();
        GetTree().CreateTimer(0.1f).Timeout += () => CreateAndStartGhost(playerFrames);
    }

    private DeathRecord GetDeathRecord()
    {
        // Retrieve death record from GameManager
        if (GameManager.Instance == null)
        {
            GD.Print("GameManager.Instance is null - cannot get death record");
            return null;
        }

        return GameManager.Instance.GetLastDeathRecord();
    }

    private bool ValidateFrameData(System.Collections.Generic.List<ReplayFrame> playerFrames)
    {
        // Validate frame data before starting replay
        GD.Print($"Player frames count: {playerFrames.Count}");

        if (playerFrames.Count == 0)
        {
            GD.Print("No player frames to replay");
            return false;
        }

        return true;
    }

    private void CreateAndStartGhost(System.Collections.Generic.List<ReplayFrame> playerFrames)
    {
        // Create ghost player and start replay
        var ghostPlayer = CreateGhostPlayer();

        if (ghostPlayer != null)
        {
            GD.Print("Starting ghost replay...");
            
            PauseOriginalPlayer();
            StartGhostReplay(ghostPlayer, playerFrames);
            HideGemDuringReplay();
            ScheduleReplayCleanup(playerFrames);
        }
        else
        {
            GD.Print("FAILED: Could not create ghost player");
        }
    }

    private void PauseOriginalPlayer()
    {
        // Pause the original player during replay
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            player.PausePlayer();
        }
    }

    private void StartGhostReplay(GhostPlayer ghostPlayer, System.Collections.Generic.List<ReplayFrame> playerFrames)
    {
        // Start the ghost replay and track reference
        ghostPlayer.StartReplay(playerFrames);
        _currentGhost = ghostPlayer;
    }

    private void HideGemDuringReplay()
    {
        // Hide UI elements during replay
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }
        _gemSprite?.Hide();
    }

    private void ScheduleReplayCleanup(System.Collections.Generic.List<ReplayFrame> frames)
    {
        // Schedule cleanup when replay completes
        if (frames.Count == 0) return;

        float replayDuration = frames[frames.Count - 1].Timestamp + 1.0f;

        GetTree().CreateTimer(replayDuration).Timeout += () =>
        {
            CleanupAfterReplay();
        };
    }

    private void CleanupAfterReplay()
    {
        // Restore game state after replay ends
        CleanupGhost();
        ResumeOriginalPlayer();
        RestartRecording();
        ShowGemAfterReplay();

        GD.Print("Replay completed - recording restarted");
    }

    private void ResumeOriginalPlayer()
    {
        // Resume the original player
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            player.ResumePlayer();
        }
    }

    private void RestartRecording()
    {
        // Restart recording after replay
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.InitializeRecording();
        }
    }

    private void ShowGemAfterReplay()
    {
        // Show gem again after replay if still valid
        if (_gemSprite != null && IsInstanceValid(this))
        {
            _gemSprite.Show();
        }
    }
    #endregion

    #region Ghost Creation
    private GhostPlayer CreateGhostPlayer()
    {
        // Create ghost player using available methods
        GhostPlayer ghost = null;

        // Try scene instantiation first if available
        if (GhostPlayerScene != null)
        {
            ghost = CreateGhostFromScene();
        }

        // Fallback to programmatic creation
        if (ghost == null)
        {
            ghost = CreateGhostProgrammatically();
        }

        return ghost;
    }

    private GhostPlayer CreateGhostFromScene()
    {
        // Create ghost from scene file
        var ghostNode = GhostPlayerScene.Instantiate();
        var ghost = ghostNode as GhostPlayer;

        if (ghost != null)
        {
            GetTree().CurrentScene.AddChild(ghost);
            ghost.AddToGroup("ghost_player");
            GD.Print("Created ghost from scene file");
        }
        else
        {
            ghostNode?.QueueFree();
        }

        return ghost;
    }

    private GhostPlayer CreateGhostProgrammatically()
    {
        // Create ghost programmatically by copying original player structure
        var originalPlayer = GetTree().GetFirstNodeInGroup("player") as Player;
        if (originalPlayer == null)
        {
            GD.PrintErr("Cannot create ghost - no original player found");
            return null;
        }

        var ghost = new GhostPlayer { Name = "GhostPlayer" };
        
        CopySpriteStructure(originalPlayer, ghost);
        AddGhostToScene(ghost);

        GD.Print("Created ghost programmatically");
        return ghost;
    }

    private void CopySpriteStructure(Node originalPlayer, GhostPlayer ghost)
    {
        // Copy sprite structure from original player to ghost
        var originalAnimSprite = originalPlayer.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D")
                                ?? originalPlayer.GetNodeOrNull<AnimatedSprite2D>("MarrieSprite");

        if (originalAnimSprite != null)
        {
            CreateGhostAnimatedSprite(originalAnimSprite, ghost);
        }
        else
        {
            CreateGhostStaticSprite(originalPlayer, ghost);
        }
    }

    private void CreateGhostAnimatedSprite(AnimatedSprite2D originalAnimSprite, GhostPlayer ghost)
    {
        // Create animated sprite for ghost
        var ghostSprite = new AnimatedSprite2D
        {
            Name = originalAnimSprite.Name,
            SpriteFrames = originalAnimSprite.SpriteFrames,
            Animation = "idle"
        };

        ghost.AddChild(ghostSprite);
        GD.Print("Copied AnimatedSprite2D structure to ghost");
    }

    private void CreateGhostStaticSprite(Node originalPlayer, GhostPlayer ghost)
    {
        // Create static sprite for ghost as fallback
        var originalSprite2D = originalPlayer.GetNodeOrNull<Sprite2D>("Sprite2D")
                            ?? originalPlayer.GetNodeOrNull<Sprite2D>("MarrieSprite");

        if (originalSprite2D != null)
        {
            var ghostSprite = new Sprite2D
            {
                Name = originalSprite2D.Name,
                Texture = originalSprite2D.Texture
            };

            ghost.AddChild(ghostSprite);
            GD.Print("Copied Sprite2D structure to ghost");
        }
    }

    private void AddGhostToScene(GhostPlayer ghost)
    {
        // Add ghost to scene and group
        GetTree().CurrentScene.AddChild(ghost);
        ghost.AddToGroup("ghost_player");
    }
    #endregion

    #region Cleanup
    private void CleanupGhost()
    {
        // Clean up current ghost and ensure game state is restored
        CleanupCurrentGhost();
        CleanupAllGhosts();
        EnsurePlayerResumed();
        EnsureRecordingResumed();
    }

    private void CleanupCurrentGhost()
    {
        // Clean up tracked ghost reference
        if (_currentGhost != null && IsInstanceValid(_currentGhost))
        {
            _currentGhost.StopReplay();
            _currentGhost.QueueFree();
            _currentGhost = null;
            GD.Print("Current ghost cleaned up");
        }
    }

    private void CleanupAllGhosts()
    {
        // Clean up any other ghost players that might exist
        var allGhosts = GetTree().GetNodesInGroup("ghost_player");
        foreach (Node ghost in allGhosts)
        {
            if (IsInstanceValid(ghost))
            {
                ghost.QueueFree();
            }
        }
    }

    private void EnsurePlayerResumed()
    {
        // Ensure player is resumed if cleanup is called directly
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player?.IsPaused() == true)
        {
            player.ResumePlayer();
        }
    }

    private void EnsureRecordingResumed()
    {
        // Ensure recording is resumed if cleanup is called directly
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.ResumeRecording();
        }
    }

    public override void _ExitTree()
    {
        // Clean up when gem is removed from scene
        CleanupGhost();
    }
    #endregion
}