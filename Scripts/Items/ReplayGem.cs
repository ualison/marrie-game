using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Utils;

// Simplified replay gem - only handles UI interaction and triggers replay
public partial class ReplayGem : Node2D
{
    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Tween _brightnessTween;
    private Label _promptLabel;
    private Tween _floatingTween;
    #endregion

    #region Export Variables
    [Export] public float FloatingDistance { get; set; } = 8.0f; // How far up and down to float
    [Export] public float FloatingDuration { get; set; } = 2.0f; // How long one cycle takes
    [Export] public bool BrightEffectEnabled { get; set; } = true; // Enable pulsing bright effect
    [Export] public float BrightnessFactor { get; set; } = 1.6f; // How bright the pulse gets (1 = normal)
    [Export] public float BrightPulseDuration { get; set; } = 1.0f; // Full pulse cycle duration
    #endregion

    #region State
    private bool _playerNearby = false;
    private bool _canInteract = false; // Prevent immediate auto-triggering
    private Vector2 _originalGemPosition;
    // Tracks whether the gem was visible before a reality change so we can restore it
    private bool _wasVisibleBeforeRealityChange = false;
    #endregion

    #region Initialization
    // UPDATED METHOD: Better initialization with immediate reality check
    public override void _Ready()
    {
        Log.Replay("ReplayGem _Ready - ensure scene PauseMode is set to Process if replay should run while tree is paused", "ReplayGem");
        InitializeComponents();
        ConnectSignals();
        SetupPromptLabel();
        HideInitially();
        EnableInputProcessing();

        // IMPROVED: Immediate reality check and connection
        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            Log.Replay($"RealityManager found during ReplayGem init - Current reality: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;

            // Apply initial reality state immediately
            OnRealityChanged(RealityManager.Instance.CurrentReality);
        }
        else
        {
            Log.Replay("RealityManager not yet available during ReplayGem init", "ReplayGem");
            CallDeferred(nameof(ConnectToRealityManager));
        }

        // Add delay before allowing interaction to prevent auto-triggering
        GetTree().CreateTimer(1.0f).Timeout += () =>
        {
            _canInteract = true;
            Log.Replay("ReplayGem interaction enabled", "ReplayGem");

            // Final reality check after interaction is enabled
            if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
            {
                var currentReality = RealityManager.Instance.CurrentReality;
                if (currentReality == Reality.PastReality)
                {
                    Log.Debug("ReplayGem: Ensuring hidden state in PastReality after interaction enabled", "ReplayGem");
                    ForceHideCompletely();
                }
                else
                {
                    // If we're in CurrentReality and this gem is statically placed, show it
                    CheckAndShowStaticGem();
                }
            }
            else
            {
                // If RealityManager isn't available yet, assume CurrentReality and show static gem
                CheckAndShowStaticGem();
            }
        };

        Log.Replay("ReplayGem ready - hidden until activated", "ReplayGem");
    }

    //UPDATED METHOD: Improved connection to RealityManager
    private void ConnectToRealityManager()
    {
        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            Log.Debug("ReplayGem connecting to RealityManager", "ReplayGem");
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;

            // Apply current reality immediately to ensure correct initial state
            var currentReality = RealityManager.Instance.CurrentReality;
            Log.Debug($"ReplayGem applying initial reality state: {currentReality}", "ReplayGem");
            OnRealityChanged(currentReality);
        }
        else
        {
            // Retry connection after a short delay
            Log.Debug("RealityManager not available, retrying connection in 0.1s", "ReplayGem");
            GetTree().CreateTimer(0.1f).Timeout += ConnectToRealityManager;
        }
    }

    private void CheckAndShowStaticGem()
    {
        // Check if this gem is statically placed in scene (not dynamically created)
        // We can detect this by checking if the gem has a scene file path
        var sceneFilePath = GetTree().CurrentScene?.SceneFilePath;
        bool isStaticGem = !string.IsNullOrEmpty(sceneFilePath);

        if (isStaticGem)
        {
            Log.Replay($"Detected static ReplayGem in scene {sceneFilePath} - showing gem", "ReplayGem");
            ShowGem(GlobalPosition);
        }
    }
    private void InitializeComponents()
    {
        // Get required child nodes with error handling
        _detectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "DetectionArea");
        _gemSprite = NodeHelpers.GetNodeOrNull<Sprite2D>(this, null, "GemSprite");
        _promptLabel = NodeHelpers.GetNodeOrNull<Label>(this, null, "PromptLabel");

        // Store original gem position for floating animation
        if (_gemSprite != null)
            _originalGemPosition = _gemSprite.Position;

        // Ensure default modulate is normal white so brightness tweens start from a known value
        if (_gemSprite != null)
            _gemSprite.Modulate = Colors.White;

        // Create tween for floating animation when needed
        _floatingTween = null;
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
        if (_gemSprite != null)
        {
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Hide();
        }
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
        Log.Replay($"Attempting to start replay - RealityManager availability: {RealityManager.IsInstanceAvailable}", "ReplayGem");

        if (RealityManager.IsInstanceAvailable)
        {
            Log.Replay($"Current reality before replay: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
            RealityManager.Instance.StartReplay();
            Log.Replay($"Reality after StartReplay call: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
        }
        else
        {
            Log.Warning("RealityManager.Instance is null - cannot change reality. This suggests RealityManager hasn't been initialized yet.", "ReplayGem");
            Log.Warning("Check that RealityManager is properly added to the scene tree before ReplayGem activation.", "ReplayGem");
        }

        // Get death record and start replay through orchestrator
        var deathRecord = GetDeathRecord();
        if (deathRecord == null)
        {
            // In PuzzleMode, missing death records are expected initially
            var recordingMode = GameManager.Instance?.GetReplayRecordingMode() ?? ReplayRecordingMode.DeathMode;
            
            if (recordingMode == ReplayRecordingMode.PuzzleMode)
            {
                Log.Debug("No recording data available for PuzzleMode replay", "ReplayGem");
            }
            else
            {
                Log.Warning("No death record available for replay", "ReplayGem");
            }
            
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
        if (_gemSprite != null)
        {
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Hide();
        }
    }
    #endregion

    public override void _ExitTree()
    {
        // Clean up reality manager connection
        if (RealityManager.IsInstanceAvailable)
        {
            RealityManager.Instance.RealityChangedEvent -= OnRealityChanged;
        }
    }

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

    // Handles reality changes: hide when entering PastReality, restore when leaving
    private void OnRealityChanged(Reality newReality)
    {
        Log.Debug($"ReplayGem OnRealityChanged called - newReality={newReality}, currentVisible={Visible}", "ReplayGem");

        if (newReality == Reality.PastReality)
        {
            // Remember if the gem was visible so we can restore later
            _wasVisibleBeforeRealityChange = Visible && (_gemSprite?.Visible ?? false);
            Log.Replay($"Hiding ReplayGem because reality is PastReality (wasVisible={_wasVisibleBeforeRealityChange})", "ReplayGem");

            // IMPROVED: Force hide everything immediately
            ForceHideCompletely();
        }
        else if (newReality == Reality.CurrentReality)
        {
            // If we were visible before the reality switch, restore visuals
            if (_wasVisibleBeforeRealityChange)
            {
                Log.Replay("Restoring ReplayGem after returning from PastReality", "ReplayGem");

                // Restore full visibility
                Show();
                Visible = true;

                if (_gemSprite != null)
                {
                    _gemSprite.Show();
                    _gemSprite.Visible = true;
                    StartFloatingAnimation();
                }

                if (_promptLabel != null)
                {
                    _promptLabel.Hide(); // show only when player nearby
                }

                if (_detectionArea != null)
                {
                    _detectionArea.SetDeferred("monitoring", true);
                }
            }
            _wasVisibleBeforeRealityChange = false;
        }
    }

    // NEW METHOD: Force complete hiding of gem
    private void ForceHideCompletely()
    {
        // Disable all interaction first
        if (_detectionArea != null)
        {
            _detectionArea.SetDeferred("monitoring", false);
            _detectionArea.SetDeferred("monitorable", false);
        }

        // Stop any animations
        StopFloatingAnimation();

        // Hide all visual components
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
            _promptLabel.Visible = false;
        }

        if (_gemSprite != null)
        {
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Hide();
            _gemSprite.Visible = false;
        }

        // Hide the main node
        Hide();
        Visible = false;

        // Clear player nearby state
        _playerNearby = false;

        Log.Debug("ReplayGem completely hidden for PastReality", "ReplayGem");
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
            var playerMana = player != null ? NodeHelpers.GetNodeOrNull<PlayerMana>(player, null, "PlayerMana") : null;
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
        // Don't start if there's no sprite to animate
        if (_gemSprite == null) return;

        // If an existing tween is running, stop and clear it
        if (_floatingTween != null)
        {
            _floatingTween.Kill();
            _floatingTween = null;
        }

        // Create a new tween for the floating animation
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

        // Brightness pulsing effect (separate tween)
        if (BrightEffectEnabled)
        {
            // Ensure any existing brightness tween is stopped
            if (_brightnessTween != null)
            {
                _brightnessTween.Kill();
                _brightnessTween = null;
            }

            _brightnessTween = CreateTween();
            _brightnessTween.SetLoops();

            var normal = Colors.White;
            var bright = new Color(BrightnessFactor, BrightnessFactor, BrightnessFactor, 1.0f);

            // Pulse: normal -> bright -> normal
            _brightnessTween.TweenProperty(_gemSprite, "modulate", bright, BrightPulseDuration / 2.0f);
            _brightnessTween.TweenProperty(_gemSprite, "modulate", normal, BrightPulseDuration / 2.0f);
        }
    }

    private void StopFloatingAnimation()
    {
        if (_floatingTween != null)
        {
            _floatingTween.Kill();
            _floatingTween = null;
        }

        if (_brightnessTween != null)
        {
            _brightnessTween.Kill();
            _brightnessTween = null;
        }

        // Reset gem to original position
        if (_gemSprite != null)
        {
            // Reset visual tint to normal
            _gemSprite.Modulate = Colors.White;
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
        Log.Replay($"Attempting to end replay - RealityManager availability: {RealityManager.IsInstanceAvailable}", "ReplayGem");

        if (RealityManager.IsInstanceAvailable)
        {
            Log.Replay($"Current reality before ending replay: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
            RealityManager.Instance.EndReplay();
            Log.Replay($"Reality after EndReplay call: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
        }
        else
        {
            Log.Warning("RealityManager.Instance is null - cannot reset reality after replay", "ReplayGem");
        }
    }
    #endregion
}
