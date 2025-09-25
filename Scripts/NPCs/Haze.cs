using Godot;

/// <summary>
/// Haze NPC that can be interacted with to start dialogs
/// Shows a prompt when player is nearby and handles dialog interactions
/// Spawns a PuzzleCrystal when player answers correctly
/// </summary>
public partial class Haze : Node2D
{
    #region Components
    private Area2D _detectionArea;
    private Label _promptLabel;
    private Sprite2D _hazeSprite;
    private Sprite2D _crystalSprite;
    
    [Export] private PackedScene _sceneSwitcherCrystalScene;
    #endregion

    #region State
    private bool _playerNearby = false;
    private bool _hasDisappeared = false;
    private bool _crystalSpawned = false;
    
    // Crystal animation state
    private Tween _crystalFloatingTween;
    private Tween _crystalBrightnessTween;
    private Vector2 _originalCrystalPosition;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        GD.Print("Haze: _Ready called");
        InitializeComponents();
        SetProcess(true);
    }

    private void InitializeComponents()
    {
        // Get existing sprites
        _hazeSprite = GetNodeOrNull<Sprite2D>("HazeSprite");
        _crystalSprite = GetNodeOrNull<Sprite2D>("CrystalSprite");
        
        // Store original crystal position and start animations
        if (_crystalSprite != null)
        {
            _originalCrystalPosition = _crystalSprite.Position;
            _crystalSprite.Modulate = Colors.White;
            StartCrystalAnimations();
        }
        
        // Load PuzzleCrystal scene if not set in editor
        if (_sceneSwitcherCrystalScene == null)
        {
            _sceneSwitcherCrystalScene = GD.Load<PackedScene>("res://Prefabs/SceneSwitcherCrystal.tscn");
        }
        
        // Get existing prompt label from prefab
        _promptLabel = GetNodeOrNull<Label>("PromptLabel");
        
        // Get the detection area from the scene
        _detectionArea = GetNodeOrNull<Area2D>("DetectionArea");
        if (_detectionArea == null)
        {
            GD.PrintErr("DetectionArea not found in Haze scene!");
        }
        else
        {
            GD.Print("DetectionArea found successfully");
        }
        
        // Style the existing prompt label
        if (_promptLabel != null)
        {
            _promptLabel.AddThemeColorOverride("font_color", Colors.White);
            _promptLabel.AddThemeColorOverride("font_shadow_color", Colors.Black);
            _promptLabel.AddThemeFontSizeOverride("font_size", 14);
            _promptLabel.Hide(); // Make sure it starts hidden
        }
        
        // Connect signals and setup after everything is created
        ConnectSignals();
        SetupPromptLabel();
        
        GD.Print("Haze initialized successfully");
    }

    private void ConnectSignals()
    {
        if (_detectionArea != null)
        {
            // Try C# event subscription first
            try
            {
                _detectionArea.BodyEntered += OnPlayerEntered;
                _detectionArea.BodyExited += OnPlayerExited;
                GD.Print("Haze: Signals connected via C# events");
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"Haze: C# event subscription failed: {e.Message}");
            }

            // Fallback: use Godot Connect to ensure signal arrives
            try
            {
                _detectionArea.Connect("body_entered", new Callable(this, nameof(OnPlayerEntered)));
                _detectionArea.Connect("body_exited", new Callable(this, nameof(OnPlayerExited)));
                GD.Print("Haze: Signals connected via Connect() fallback");
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"Haze: Connect() fallback failed: {e.Message}");
            }

            // Print detection area properties for debugging
            GD.Print($"Haze: DetectionArea.monitoring={_detectionArea.Monitoring}, monitorable={_detectionArea.Monitorable}, collision_layer={_detectionArea.CollisionLayer}, collision_mask={_detectionArea.CollisionMask}");
        }
        else
        {
            GD.PrintErr("Haze: Cannot connect signals - DetectionArea is null!");
        }
    }

    private void SetupPromptLabel()
    {
        if (_promptLabel != null)
        {
            _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _promptLabel.Hide();
            GD.Print("Haze: Prompt label setup complete");
        }
        else
        {
            GD.PrintErr("Haze: PromptLabel not found!");
        }
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        if (_hasDisappeared)
            return;

        if (_playerNearby)
        {
            if (Input.IsActionJustPressed("interact"))
            {
                GD.Print("Haze: Interact key pressed, starting dialog");
                StartDialog();
            }
        }
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        GD.Print($"Haze: Body entered - {body.Name} (Groups: {string.Join(", ", body.GetGroups())})");
        
        // Try multiple ways to detect the player
        bool isPlayer = body.IsInGroup("player") || 
                       body.Name.ToString().ToLower().Contains("player") ||
                       body.GetType().Name.ToLower().Contains("player");
        
        if (isPlayer && !_hasDisappeared)
        {
            _playerNearby = true;
            if (_promptLabel != null)
            {
                _promptLabel.Show();
                GD.Print("Player nearby Haze - showing prompt");
            }
            else
            {
                GD.Print("Player nearby but prompt label is null!");
            }
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        GD.Print($"Haze: Body exited - {body.Name}");
        
        // Try multiple ways to detect the player
        bool isPlayer = body.IsInGroup("player") || 
                       body.Name.ToString().ToLower().Contains("player") ||
                       body.GetType().Name.ToLower().Contains("player");
        
        if (isPlayer)
        {
            _playerNearby = false;
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            
            // Close any active dialog when player moves away
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogActive())
            {
                DialogManager.Instance.CloseDialog();
                GD.Print("Player left Haze - closing active dialog");
            }
            
            GD.Print("Player left Haze - hiding prompt");
        }
    }
    #endregion

    #region Dialog System
    private void StartDialog()
    {
        GD.Print("Haze: Attempting to start dialog...");
        
        if (DialogManager.Instance == null)
        {
            GD.PrintErr("Haze: DialogManager.Instance is null!");
            return;
        }
        
        if (!DialogManager.Instance.IsDialogActive())
        {
            _promptLabel?.Hide();
            DialogManager.Instance.StartDialog("haze_encounter", this);
            GD.Print("Haze: Dialog started successfully");
        }
        else
        {
            GD.Print("Haze: Dialog already active, not starting new one");
        }
    }

    // Called by DialogManager when player answers correctly
    public void OnCorrectAnswer()
    {
        GD.Print("Haze: Player answered correctly! Disappearing and spawning PuzzleCrystal...");
        DisappearWithAnimationAndSpawnCrystal();
    }

    // Called by DialogManager when player answers incorrectly
    public void OnWrongAnswer()
    {
        GD.Print("Haze: Player answered wrong! Disappearing...");
        DisappearWithAnimation();
    }
    #endregion

    #region Visual Effects
    private void StartCrystalAnimations()
    {
        if (_crystalSprite == null) return;
        
        StartCrystalFloatingAnimation();
        StartCrystalBrightnessAnimation();
    }
    
    private void StartCrystalFloatingAnimation()
    {
        if (_crystalSprite == null) return;
        
        // Stop existing tween
        if (_crystalFloatingTween != null)
        {
            _crystalFloatingTween.Kill();
            _crystalFloatingTween = null;
        }
        
        // Create new floating tween
        _crystalFloatingTween = CreateTween();
        _crystalFloatingTween.SetLoops();
        
        // Reset to original position
        _crystalSprite.Position = _originalCrystalPosition;
        
        // Floating motion: up -> down -> back to original
        var floatingDistance = 5.0f;
        var floatingDuration = 2.0f;
        
        var upPosition = new Vector2(_originalCrystalPosition.X, _originalCrystalPosition.Y - floatingDistance);
        var downPosition = new Vector2(_originalCrystalPosition.X, _originalCrystalPosition.Y + floatingDistance);
        
        _crystalFloatingTween.TweenProperty(_crystalSprite, "position", upPosition, floatingDuration / 2.0f);
        _crystalFloatingTween.TweenProperty(_crystalSprite, "position", downPosition, floatingDuration);
        _crystalFloatingTween.TweenProperty(_crystalSprite, "position", _originalCrystalPosition, floatingDuration / 2.0f);
    }
    
    private void StartCrystalBrightnessAnimation()
    {
        if (_crystalSprite == null) return;
        
        // Stop existing tween
        if (_crystalBrightnessTween != null)
        {
            _crystalBrightnessTween.Kill();
            _crystalBrightnessTween = null;
        }
        
        // Create brightness pulsing tween
        _crystalBrightnessTween = CreateTween();
        _crystalBrightnessTween.SetLoops();
        
        var normal = Colors.White;
        var bright = new Color(1.6f, 1.6f, 1.6f, 1.0f);
        var pulseDuration = 1.5f;
        
        // Pulse: normal -> bright -> normal
        _crystalBrightnessTween.TweenProperty(_crystalSprite, "modulate", bright, pulseDuration / 2.0f);
        _crystalBrightnessTween.TweenProperty(_crystalSprite, "modulate", normal, pulseDuration / 2.0f);
    }
    
    private void StopCrystalAnimations()
    {
        if (_crystalFloatingTween != null)
        {
            _crystalFloatingTween.Kill();
            _crystalFloatingTween = null;
        }
        
        if (_crystalBrightnessTween != null)
        {
            _crystalBrightnessTween.Kill();
            _crystalBrightnessTween = null;
        }
        
        // Reset crystal to original state
        if (_crystalSprite != null)
        {
            _crystalSprite.Modulate = Colors.White;
            _crystalSprite.Position = _originalCrystalPosition;
        }
    }

    private void DisappearWithAnimation()
    {
        _hasDisappeared = true;
        _promptLabel.Hide();
        
        // Stop crystal animations before fading out
        StopCrystalAnimations();
        
        // Fade out animation
        var tween = CreateTween();
        tween.SetParallel(true);
        
        if (_hazeSprite != null)
        {
            tween.TweenProperty(_hazeSprite, "modulate:a", 0.0f, 1.0f);
        }
        
        if (_crystalSprite != null)
        {
            tween.TweenProperty(_crystalSprite, "modulate:a", 0.0f, 1.0f);
        }
        
        tween.TweenCallback(Callable.From(CompleteDisappearance)).SetDelay(1.0f);
    }

    private void DisappearWithAnimationAndSpawnCrystal()
    {
        _hasDisappeared = true;
        _promptLabel.Hide();
        
        // Stop crystal animations before fading out
        StopCrystalAnimations();
        
        // Fade out animation
        var tween = CreateTween();
        tween.SetParallel(true);
        
        if (_hazeSprite != null)
        {
            tween.TweenProperty(_hazeSprite, "modulate:a", 0.0f, 1.0f);
        }
        
        if (_crystalSprite != null)
        {
            tween.TweenProperty(_crystalSprite, "modulate:a", 0.0f, 1.0f);
        }
        
        // Spawn PuzzleCrystal after fade out completes
        tween.TweenCallback(Callable.From(SpawnPuzzleCrystal)).SetDelay(1.0f);
        tween.TweenCallback(Callable.From(CompleteDisappearance)).SetDelay(1.2f);
    }

    private void SpawnPuzzleCrystal()
    {
        // Prevent duplicate spawning
        if (_crystalSpawned)
        {
            GD.Print("Crystal already spawned, skipping duplicate spawn");
            return;
        }

        if (_sceneSwitcherCrystalScene != null)
        {
            // Instantiate the PuzzleCrystal
            var puzzleCrystal = _sceneSwitcherCrystalScene.Instantiate() as Node2D;
            if (puzzleCrystal != null)
            {
                // Add to parent (same level as Haze)
                GetParent().AddChild(puzzleCrystal);
                
                // Position at Haze's location
                puzzleCrystal.GlobalPosition = GlobalPosition;
                
                // Start with crystal invisible
                puzzleCrystal.Modulate = new Color(1, 1, 1, 0);
                
                // Fade in animation
                var fadeTween = puzzleCrystal.CreateTween();
                fadeTween.TweenProperty(puzzleCrystal, "modulate:a", 1.0f, 0.8f);
                
                // Mark as spawned to prevent duplicates
                _crystalSpawned = true;
                
                GD.Print("PuzzleCrystal spawned and fading in at Haze location");
            }
            else
            {
                GD.PrintErr("Failed to instantiate PuzzleCrystal");
            }
        }
        else
        {
            GD.PrintErr("PuzzleCrystal scene not loaded");
        }
    }

    private void CompleteDisappearance()
    {
        // Disable collision and hide completely
        if (_detectionArea != null)
        {
            _detectionArea.SetDeferred("monitoring", false);
        }
        
        Hide();
        GD.Print("Haze has completely disappeared");
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        if (_detectionArea != null)
        {
            try
            {
                _detectionArea.BodyEntered -= OnPlayerEntered;
                _detectionArea.BodyExited -= OnPlayerExited;
            }
            catch { }

            // Attempt to disconnect Godot-style signals if connected
            try
            {
                _detectionArea.Disconnect("body_entered", new Callable(this, nameof(OnPlayerEntered)));
            }
            catch { }
            try
            {
                _detectionArea.Disconnect("body_exited", new Callable(this, nameof(OnPlayerExited)));
            }
            catch { }
        }
        
        // Clean up crystal animation tweens
        StopCrystalAnimations();
    }
    #endregion
}