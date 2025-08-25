using Godot;

/// <summary>
/// Apple item that cycles through four states: Red → Neutral → Purple → Gray → Red
/// - Red: Life phase (collectible, restores life)
/// - Neutral: Gray transition phase (not collectible, no prompt)
/// - Purple: Mana phase (collectible, restores mana)  
/// - Gray: Dark gray phase (not collectible, no prompt)
/// Only Red and Purple phases are collectible
/// </summary>
public partial class Apple : Node2D
{
    #region Exported Properties
    [Export] public float TransitionTime { get; set; } = 3.0f; // Time between color transitions
    [Export] public float NeutralPhaseTime { get; set; } = 2.5f; // Time spent in neutral/collectible phases
    [Export] public int LifeRestoreAmount { get; set; } = 1; // Life restored by red apple
    [Export] public int ManaRestoreAmount { get; set; } = 2; // Mana restored by purple apple
    [Export] public Texture2D RedAppleTexture { get; set; }
    [Export] public Texture2D PurpleAppleTexture { get; set; }
    [Export] public Texture2D NeutralAppleTexture { get; set; }
    #endregion

    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _appleSprite;
    private Label _promptLabel;
    #endregion

    #region State
    private bool _playerNearby = false;
    private AppleState _currentState = AppleState.Red; // Current apple state
    private Timer _transitionTimer;
    private Tween _fadeTween;
    private Vector2 _originalScale; // Store original scale for transitions
    #endregion

    #region Apple State Enum
    public enum AppleState
    {
        Red,        // Life phase (collectible)
        Neutral,    // Gray transition phase (not collectible)
        Purple,     // Mana phase (collectible)
        Gray        // Gray phase (not collectible)
    }
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
        _transitionTimer.WaitTime = GetCurrentPhaseTime();
        _transitionTimer.OneShot = false;
        _transitionTimer.Timeout += OnTransitionTimeout;
        AddChild(_transitionTimer);
        _transitionTimer.Start();
    }

    private float GetCurrentPhaseTime()
    {
        // Neutral and Gray phases last longer (transition phases)
        return (_currentState == AppleState.Neutral || _currentState == AppleState.Gray) 
            ? NeutralPhaseTime 
            : TransitionTime;
    }

    private void SetInitialState()
    {
        _currentState = AppleState.Red;
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
        // Only allow consumption during Red and Purple phases
        if (_currentState != AppleState.Red && _currentState != AppleState.Purple)
        {
            Log.UI("Apple is not collectible in current state", "Apple");
            return;
        }

        if (_currentState == AppleState.Red)
        {
            ConsumeRedApple(); // Red phase gives life
        }
        else if (_currentState == AppleState.Purple)
        {
            ConsumePurpleApple(); // Purple phase gives mana
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
        
        // Phase 4: Update prompt display after transition completes
        _fadeTween.TweenCallback(Callable.From(UpdatePromptDisplay));
    }

    private void SwitchTexture()
    {
        // Cycle through states: Red → Neutral → Purple → Gray → Red
        _currentState = _currentState switch
        {
            AppleState.Red => AppleState.Neutral,
            AppleState.Neutral => AppleState.Purple,
            AppleState.Purple => AppleState.Gray,
            AppleState.Gray => AppleState.Red,
            _ => AppleState.Red
        };
        
        // Update texture based on current state
        Texture2D newTexture = _currentState switch
        {
            AppleState.Red => RedAppleTexture,
            AppleState.Neutral => NeutralAppleTexture,
            AppleState.Purple => PurpleAppleTexture,
            AppleState.Gray => NeutralAppleTexture, // Use neutral texture for gray phase
            _ => RedAppleTexture
        };

        if (newTexture != null)
        {
            _appleSprite.Texture = newTexture;
            
            // Apply visual effects for different states
            ApplyStateVisualEffects();
            
            Log.System($"Apple switched to {_currentState} state", "Apple");
        }

        // Update timer for the new phase
        _transitionTimer.WaitTime = GetCurrentPhaseTime();
    }

    private void ApplyStateVisualEffects()
    {
        // Reset modulation first
        _appleSprite.Modulate = Colors.White;
        
        switch (_currentState)
        {
            case AppleState.Red:
                // Normal red apple appearance
                break;
            case AppleState.Neutral:
                // Gray/neutral appearance - slightly desaturated
                _appleSprite.Modulate = new Color(0.8f, 0.8f, 0.8f, 1.0f);
                break;
            case AppleState.Purple:
                // Normal purple apple appearance
                break;
            case AppleState.Gray:
                // Darker gray appearance to distinguish from neutral
                _appleSprite.Modulate = new Color(0.6f, 0.6f, 0.6f, 1.0f);
                break;
        }
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            UpdatePromptDisplay();
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

    private void UpdatePromptDisplay()
    {
        if (!_playerNearby)
        {
            _promptLabel.Hide();
            return;
        }

        // Show prompts for red and purple states (collectible phases)
        if (_currentState == AppleState.Red || _currentState == AppleState.Purple)
        {
            string promptText = _currentState switch
            {
                AppleState.Red => "Press E (Life)",
                AppleState.Purple => "Press E (Mana)",
                _ => "Press E"
            };
            _promptLabel.Text = promptText;
            _promptLabel.Show();
        }
        else
        {
            _promptLabel.Hide();
        }
    }
    #endregion

    #region Public API
    public void SetTransitionTime(float time)
    {
        TransitionTime = time;
        if (_transitionTimer != null && (_currentState == AppleState.Red || _currentState == AppleState.Purple))
        {
            _transitionTimer.WaitTime = time;
        }
    }

    public void SetNeutralPhaseTime(float time)
    {
        NeutralPhaseTime = time;
        if (_transitionTimer != null && (_currentState == AppleState.Neutral || _currentState == AppleState.Gray))
        {
            _transitionTimer.WaitTime = time;
        }
    }

    public bool IsCollectible() => _currentState == AppleState.Red || _currentState == AppleState.Purple;
    
    public AppleState GetCurrentState() => _currentState;
    
    public bool IsInLifePhase() => _currentState == AppleState.Red || _currentState == AppleState.Neutral;
    
    public bool IsInManaPhase() => _currentState == AppleState.Purple || _currentState == AppleState.Gray;
    #endregion
}
