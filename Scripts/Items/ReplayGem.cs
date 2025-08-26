using Godot;

// Simplified replay gem - only handles UI interaction and triggers replay
public partial class ReplayGem : Node2D
{
    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Label _promptLabel;
    #endregion

    #region State
    private bool _playerNearby = false;
    private bool _canInteract = false; // Prevent immediate auto-triggering
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

        // Add delay before allowing interaction to prevent auto-triggering
        GetTree().CreateTimer(1.0f).Timeout += () => {
            _canInteract = true;
            Log.Replay("ReplayGem interaction enabled", "ReplayGem");
        };

        Log.Replay("ReplayGem ready - hidden until activated", "ReplayGem");
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
        // Handle interaction input when player is nearby and interaction is enabled
        if (!_playerNearby || !_canInteract) return;

        if (Input.IsActionJustPressed("interact"))
        {
            HandleReplayActivation();
        }
    }

    private void HandleReplayActivation()
    {
        Log.Replay("=== ReplayGem HandleReplayActivation called ===", "ReplayGem");
        
        // Get death record and start replay through orchestrator
        var deathRecord = GetDeathRecord();
        if (deathRecord == null) 
        {
            Log.Warning("No death record available for replay", "ReplayGem");
            return;
        }

        Log.Replay($"ReplayGem found death record: {deathRecord.DeathId}", "ReplayGem");

        // Hide gem during replay
        HideGemDuringReplay();

        // Start replay through orchestrator, passing this gem instance
        if (ReplayOrchestrator.Instance != null)
        {
            Log.Replay("ReplayGem calling ReplayOrchestrator.StartReplay", "ReplayGem");
            ReplayOrchestrator.Instance.StartReplay(deathRecord, this);
        }
        else
        {
            Log.Error("ReplayOrchestrator.Instance is null", "ReplayGem");
        }
    }

    private DeathRecord GetDeathRecord()
    {
        if (ReplaySystemManager.Instance == null)
        {
            Log.Error("ReplaySystemManager.Instance is null - cannot get death record", "ReplayGem");
            return null;
        }

        return ReplaySystemManager.Instance.GetLastDeathRecord();
    }

    private void HideGemDuringReplay()
    {
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }
        _gemSprite?.Hide();
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
        Log.Replay($"ReplayGem shown at {position}, scale={Scale}", "ReplayGem");
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
            Log.UI("Player nearby - showing prompt", "ReplayGem");
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
            Log.UI("Player left - hiding prompt", "ReplayGem");
        }
    }
    #endregion

    #region Public API (for restoring gem after replay)
    public void ShowGemAfterReplay()
    {
        // Show gem again after replay if still valid
        if (_gemSprite != null && IsInstanceValid(this))
        {
            _gemSprite.Show();
        }
    }
    #endregion
}
