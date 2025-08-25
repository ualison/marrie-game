using Godot;

/// <summary>
/// Apple item that transitions between red (life) and purple (mana) states
/// Red apple restores 1 life, purple apple restores 2 mana
/// </summary>
public partial class Apple : Node2D
{
    #region Exported Properties
    [Export] public float TransitionTime { get; set; } = 3.0f; // Time between color transitions
    [Export] public int LifeRestoreAmount { get; set; } = 1; // Life restored by red apple
    [Export] public int ManaRestoreAmount { get; set; } = 2; // Mana restored by purple apple
    [Export] public Texture2D RedAppleTexture { get; set; }
    [Export] public Texture2D PurpleAppleTexture { get; set; }
    #endregion

    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _appleSprite;
    private Label _promptLabel;
    #endregion

    #region State
    private bool _playerNearby = false;
    private bool _isRed = true; // Start as red apple (life)
    private Timer _transitionTimer;
    private Tween _fadeTween;
    private Vector2 _originalScale; // Store original scale for transitions
    #endregion

    #region Initialization
    public override void _Ready()
    {
        InitializeComponents();
        ConnectSignals();
        SetupPromptLabel();
        SetupTransitionTimer();
        SetInitialState();
        EnableInputProcessing();

        Log.System("Apple item ready", "Apple");
    }

    private void InitializeComponents()
    {
        _detectionArea = GetNode<Area2D>("DetectionArea");
        _appleSprite = GetNode<Sprite2D>("AppleSprite");
        _promptLabel = GetNode<Label>("PromptLabel");
    }

    private void ConnectSignals()
    {
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;
    }

    private void SetupPromptLabel()
    {
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";
        _promptLabel.Hide();
    }

    private void SetupTransitionTimer()
    {
        _transitionTimer = new Timer();
        _transitionTimer.WaitTime = TransitionTime;
        _transitionTimer.OneShot = false;
        _transitionTimer.Timeout += OnTransitionTimeout;
        AddChild(_transitionTimer);
        _transitionTimer.Start();
    }

    private void SetInitialState()
    {
        _isRed = true;
        if (RedAppleTexture != null)
        {
            _appleSprite.Texture = RedAppleTexture;
        }
        
        // Store the original scale for smooth transitions
        _originalScale = _appleSprite.Scale;
    }

    private void EnableInputProcessing()
    {
        SetProcess(true);
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        if (!_playerNearby) return;

        if (Input.IsActionJustPressed("interact"))
        {
            HandleAppleConsumption();
        }
    }

    private void HandleAppleConsumption()
    {
        if (_isRed)
        {
            ConsumeRedApple();
        }
        else
        {
            ConsumePurpleApple();
        }

        // Remove apple after consumption
        DestroyApple();
    }

    private void ConsumeRedApple()
    {
        Log.UI($"Consumed red apple - restoring {LifeRestoreAmount} life", "Apple");
        
        // Find player and restore life
        var player = GetTree().GetFirstNodeInGroup("player") as Node;
        var playerLife = player?.GetNodeOrNull<PlayerLife>("PlayerLife");
        
        if (playerLife != null)
        {
            playerLife.Heal(LifeRestoreAmount);
        }
        else
        {
            Log.Warning("PlayerLife not found", "Apple");
        }
    }

    private void ConsumePurpleApple()
    {
        Log.UI($"Consumed purple apple - restoring {ManaRestoreAmount} mana", "Apple");
        
        // Find player and restore mana
        var player = GetTree().GetFirstNodeInGroup("player") as Node;
        var playerMana = player?.GetNodeOrNull<PlayerMana>("PlayerMana");
        
        if (playerMana != null)
        {
            playerMana.RestoreMana(ManaRestoreAmount);
        }
        else
        {
            Log.Warning("PlayerMana not found", "Apple");
        }
    }

    private void DestroyApple()
    {
        Log.UI("Apple consumed and destroyed", "Apple");
        QueueFree();
    }
    #endregion

    #region Color Transition
    private void OnTransitionTimeout()
    {
        TransitionColor();
    }

    private void TransitionColor()
    {
        if (_fadeTween != null && _fadeTween.IsValid())
        {
            _fadeTween.Kill();
        }

        _fadeTween = CreateTween();
        
        // Create a smoother transition with subtle scale effect and partial fade
        // Phase 1: Scale down slightly and fade to 50% opacity
        _fadeTween.TweenProperty(_appleSprite, "scale", _originalScale * 0.85f, 0.2f);
        _fadeTween.TweenProperty(_appleSprite, "modulate:a", 0.5f, 0.2f);
        
        // Phase 2: Switch texture at the lowest point
        _fadeTween.TweenCallback(Callable.From(SwitchTexture));
        
        // Phase 3: Scale back to original size and full opacity
        _fadeTween.TweenProperty(_appleSprite, "scale", _originalScale, 0.2f);
        _fadeTween.TweenProperty(_appleSprite, "modulate:a", 1.0f, 0.2f);
    }

    private void SwitchTexture()
    {
        _isRed = !_isRed;
        
        if (_isRed && RedAppleTexture != null)
        {
            _appleSprite.Texture = RedAppleTexture;
            Log.System("Apple switched to red (life)", "Apple");
        }
        else if (!_isRed && PurpleAppleTexture != null)
        {
            _appleSprite.Texture = PurpleAppleTexture;
            Log.System("Apple switched to purple (mana)", "Apple");
        }
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            _promptLabel.Show();
            Log.UI("Player nearby apple - showing prompt", "Apple");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            _promptLabel.Hide();
            Log.UI("Player left apple - hiding prompt", "Apple");
        }
    }
    #endregion

    #region Public API
    public void SetTransitionTime(float time)
    {
        TransitionTime = time;
        if (_transitionTimer != null)
        {
            _transitionTimer.WaitTime = time;
        }
    }

    public bool IsRedApple() => _isRed;
    #endregion
}
