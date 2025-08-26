using Godot;

// Simplified replay gem - only handles UI interaction and triggers replay
public partial class ReplayGem : Node2D
{
    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Label _promptLabel;
    private Tween _floatingTween;
    #endregion

    #region Export Variables
    [Export] public float FloatingDistance { get; set; } = 8.0f; // How far up and down to float
    [Export] public float FloatingDuration { get; set; } = 2.0f; // How long one cycle takes
    #endregion

    #region State
    private bool _playerNearby = false;
    private bool _canInteract = false; // Prevent immediate auto-triggering
    private Vector2 _originalGemPosition;
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
        
        // Store original gem position for floating animation
        _originalGemPosition = _gemSprite.Position;
        
        // Create tween for floating animation
        _floatingTween = CreateTween();
        _floatingTween.SetLoops(); // Loop infinitely
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
        
        // Check if player has enough mana (requires 1 mana)
        var playerMana = GetPlayerManaSystem();
        if (playerMana == null)
        {
            Log.Warning("PlayerMana system not found", "ReplayGem");
            return;
        }

        if (!playerMana.HasEnoughMana(1))
        {
            // Show "Not enough mana" message
            ShowInsufficientManaMessage();
            Log.Replay("Replay activation failed: Insufficient mana", "ReplayGem");
            return;
        }

        // Consume 1 mana for replay
        if (!playerMana.UseMana(1))
        {
            Log.Warning("Failed to consume mana for replay", "ReplayGem");
            return;
        }

        Log.Replay("Consumed 1 mana for replay activation", "ReplayGem");
        
        // Change reality to PastReality when starting replay
        if (RealityManager.Instance != null)
        {
            RealityManager.Instance.StartReplay();
            Log.Replay("Reality changed to PastReality for replay", "ReplayGem");
        }
        else
        {
            Log.Warning("RealityManager.Instance is null - cannot change reality", "ReplayGem");
        }
        
        // Get death record and start replay through orchestrator
        var deathRecord = GetDeathRecord();
        if (deathRecord == null) 
        {
            Log.Warning("No death record available for replay", "ReplayGem");
            // Refund mana if replay fails
            playerMana.RestoreMana(1);
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
            // Refund mana if replay fails
            playerMana.RestoreMana(1);
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
        // Stop floating animation during replay
        StopFloatingAnimation();
        
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
            StartFloatingAnimation();
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
        
        // Stop floating animation
        StopFloatingAnimation();

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

    #region Mana System Integration
    private PlayerMana GetPlayerManaSystem()
    {
        // Try multiple strategies to find PlayerMana system
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            var playerMana = player.GetNodeOrNull<PlayerMana>("PlayerMana");
            if (playerMana != null) return playerMana;
        }

        // Fallback: search by group
        return GetTree().GetFirstNodeInGroup("mana_system") as PlayerMana;
    }

    private void ShowInsufficientManaMessage()
    {
        // Use UIManager to show the message
        UIManager.ShowGameplayMessage("Not enough mana!");
    }
    #endregion

    #region Floating Animation
    private void StartFloatingAnimation()
    {
        if (_floatingTween == null || _gemSprite == null) return;
        
        // Stop any existing animation
        _floatingTween.Kill();
        _floatingTween = CreateTween();
        _floatingTween.SetLoops(); // Loop infinitely
        
        // Reset to original position
        _gemSprite.Position = _originalGemPosition;
        
        // Create floating motion: up -> down -> up (continuous)
        var upPosition = new Vector2(_originalGemPosition.X, _originalGemPosition.Y - FloatingDistance);
        var downPosition = new Vector2(_originalGemPosition.X, _originalGemPosition.Y + FloatingDistance);
        
        _floatingTween.TweenProperty(_gemSprite, "position", upPosition, FloatingDuration / 2.0f);
        _floatingTween.TweenProperty(_gemSprite, "position", downPosition, FloatingDuration);
        _floatingTween.TweenProperty(_gemSprite, "position", _originalGemPosition, FloatingDuration / 2.0f);
    }
    
    private void StopFloatingAnimation()
    {
        if (_floatingTween != null)
        {
            _floatingTween.Kill();
        }
        
        // Reset gem to original position
        if (_gemSprite != null)
        {
            _gemSprite.Position = _originalGemPosition;
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
            StartFloatingAnimation();
        }
        
        // End replay in RealityManager to return to CurrentReality
        if (RealityManager.Instance != null)
        {
            RealityManager.Instance.EndReplay();
            Log.Replay("Reality changed back to CurrentReality after replay", "ReplayGem");
        }
        else
        {
            Log.Warning("RealityManager.Instance is null - cannot reset reality after replay", "ReplayGem");
        }
    }
    #endregion
}
