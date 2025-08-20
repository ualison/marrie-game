using Godot;

// Replay gem that appears at death location and allows ghost replay
// Enhanced to dynamically create ghost player when needed
public partial class ReplayGem : Node2D
{
    [Export] public PackedScene GhostPlayerScene { get; set; } // Assign in inspector

    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Label _promptLabel;

    private bool _playerNearby = false;
    private GhostPlayer _currentGhost;

    public override void _Ready()
    {
        // Get required child nodes
        _detectionArea = GetNode<Area2D>("DetectionArea");
        _gemSprite = GetNode<Sprite2D>("GemSprite");
        _promptLabel = GetNode<Label>("PromptLabel");

        // Connect signals
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;

        // Setup prompt label
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";

        // Ensure the gem starts hidden
        _detectionArea.SetDeferred("monitoring", false);
        _promptLabel.Hide();
        _gemSprite.Hide();

        // CRITICAL: Enable input processing
        SetProcess(true);

        GD.Print("ReplayGem ready - hidden until activated");
    }

    public override void _Process(double delta)
    {
        if (!_playerNearby) return;

        if (Input.IsActionJustPressed("interact"))
        {
            StartReplay();
        }
    }

    // Called by GameManager to show gem at death position
    public void ShowGem(Vector2 position)
    {
        GlobalPosition = position;
        Show();

        if (_gemSprite != null)
        {
            _gemSprite.Show();
        }

        if (_promptLabel != null)
        {
            _promptLabel.Hide(); // only show when player nearby
        }

        _detectionArea.SetDeferred("monitoring", true);
        GD.Print($"ReplayGem shown at {position}, scale={Scale}");
    }

    // Hides gem completely
    public void HideGem()
    {
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

        // Clean up any existing ghost
        CleanupGhost();
    }

    private void OnPlayerEntered(Node2D body)
    {
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

    private void StartReplay()
{
    GD.Print("=== StartReplay called ===");
    
    GD.Print($"GameManager.Instance is null: {GameManager.Instance == null}");
    
    if (GameManager.Instance == null)
    {
        GD.Print("GameManager.Instance is null - cannot get death record");
        return;
    }
    
    GD.Print("Calling GetLastDeathRecord...");
    var deathRecord = GameManager.Instance.GetLastDeathRecord();
    
    if (deathRecord == null)
    {
        GD.Print("No death record available");
        return;
    }

    var playerFrames = deathRecord.PlayerFrames;
    GD.Print($"Using stored death record: {deathRecord.DeathId}");
    GD.Print($"Player frames count: {playerFrames.Count}");

    if (playerFrames.Count == 0)
    {
        GD.Print("No player frames to replay - EXITING");
        return;
    }

    // Print first few frames for debugging
    for (int i = 0; i < Mathf.Min(3, playerFrames.Count); i++)
    {
        var frame = playerFrames[i];
        GD.Print($"Frame {i}: Time={frame.Timestamp}, Pos={frame.Position}");
    }

    // Clean up any existing ghost first
    CleanupGhost();

    // Try to find existing ghost player first
    var ghostPlayer = GetTree().GetFirstNodeInGroup("ghost_player") as GhostPlayer;
    GD.Print($"Found existing ghost: {ghostPlayer != null}");

    // If no ghost exists, create one dynamically
    if (ghostPlayer == null)
    {
        GD.Print("Creating new ghost...");
        ghostPlayer = CreateGhostPlayer();
        GD.Print($"Ghost created successfully: {ghostPlayer != null}");
    }

    if (ghostPlayer != null)
    {
        GD.Print("Starting ghost replay...");
        ghostPlayer.StartReplay(playerFrames);
        _currentGhost = ghostPlayer;

        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }

        // Hide gem during replay (optional)
        _gemSprite?.Hide();

        GD.Print("Ghost replay started successfully");

        // Schedule cleanup after replay completes
        ScheduleReplayCleanup(playerFrames);
    }
    else
    {
        GD.Print("FAILED: Ghost player is null!");
    }
}

    private GhostPlayer CreateGhostPlayer()
    {
        GhostPlayer ghost = null;

        // Method 1: Try to instantiate from scene if assigned
        if (GhostPlayerScene != null)
        {
            var ghostNode = GhostPlayerScene.Instantiate();
            ghost = ghostNode as GhostPlayer;

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
        }

        // Method 2: Create programmatically if scene method failed
        if (ghost == null)
        {
            ghost = CreateGhostProgrammatically();
        }

        return ghost;
    }

    private GhostPlayer CreateGhostProgrammatically()
    {
        // Find the original player to copy structure from
        var originalPlayer = GetTree().GetFirstNodeInGroup("player") as Player;
        if (originalPlayer == null)
        {
            GD.PrintErr("Cannot create ghost - no original player found");
            return null;
        }

        // Create ghost player instance
        var ghost = new GhostPlayer();
        ghost.Name = "GhostPlayer";

        // Copy sprite structure from original player
        CopySpriteStructure(originalPlayer, ghost);

        // Add to scene and group
        GetTree().CurrentScene.AddChild(ghost);
        ghost.AddToGroup("ghost_player");

        GD.Print("Created ghost programmatically");
        return ghost;
    }

    private void CopySpriteStructure(Node originalPlayer, GhostPlayer ghost)
    {
        // Find the main sprite in original player
        var originalSprite = originalPlayer.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D")
                          ?? originalPlayer.GetNodeOrNull<AnimatedSprite2D>("MarrieSprite");

        if (originalSprite is AnimatedSprite2D originalAnimSprite)
        {
            // Create matching animated sprite for ghost
            var ghostSprite = new AnimatedSprite2D();
            ghostSprite.Name = originalAnimSprite.Name;
            ghostSprite.SpriteFrames = originalAnimSprite.SpriteFrames;
            ghostSprite.Animation = "idle";

            ghost.AddChild(ghostSprite);
            GD.Print("Copied AnimatedSprite2D structure to ghost");
        }
        else
        {
            // Fallback: try regular Sprite2D
            var originalSprite2D = originalPlayer.GetNodeOrNull<Sprite2D>("Sprite2D")
                               ?? originalPlayer.GetNodeOrNull<Sprite2D>("MarrieSprite");

            if (originalSprite2D != null)
            {
                var ghostSprite = new Sprite2D();
                ghostSprite.Name = originalSprite2D.Name;
                ghostSprite.Texture = originalSprite2D.Texture;

                ghost.AddChild(ghostSprite);
                GD.Print("Copied Sprite2D structure to ghost");
            }
        }
    }

    private void ScheduleReplayCleanup(System.Collections.Generic.List<ReplayFrame> frames)
    {
        if (frames.Count == 0) return;

        // Calculate total replay duration
        float replayDuration = frames[frames.Count - 1].Timestamp + 1.0f; // Add 1 second buffer

        // Schedule cleanup
        GetTree().CreateTimer(replayDuration).Timeout += () =>
        {
            CleanupGhost();
            _gemSprite?.Show(); // Show gem again after replay
            GD.Print("Replay completed - ghost cleaned up");
        };
    }

    private void CleanupGhost()
    {
        if (_currentGhost != null && IsInstanceValid(_currentGhost))
        {
            _currentGhost.StopReplay();
            _currentGhost.QueueFree();
            _currentGhost = null;
            GD.Print("Ghost player cleaned up");
        }
    }

    public override void _ExitTree()
    {
        CleanupGhost();
    }
}