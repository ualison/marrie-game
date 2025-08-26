using Godot;
using Core.Enums;

/// <summary>
/// Apple item with different state cycles for tree vs fallen apples:
/// 
/// Tree Cycle: RedLife → GrayKnockable → PurpleMana → RedLife (repeats)
/// Fallen Cycle: RedLife → GrayNotCollectible → PurpleMana → RedLife (repeats)
/// 
/// States:
/// - RedLife: Red color, Life phase (collectible when fallen, restores health)
/// - GrayKnockable: Gray color, Knockable phase (tree only, can be hit by rocks to fall)
/// - PurpleMana: Purple color, Mana phase (collectible when fallen, restores mana)
/// - GrayNotCollectible: Dark gray color, Non-collectible phase (fallen only, not collectible)
/// 
/// Only RedLife and PurpleMana phases are collectible, and only when apple is on the ground
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
    private RigidBody2D _rigidBody;
    private Area2D _rockDetectionArea;
    private Area2D _groundDetectionArea;
    #endregion

    #region State
    private bool _playerNearby = false;
    private AppleState _currentState = AppleState.RedLife; // Current apple state
    private ApplePosition _currentPosition = ApplePosition.OnTree; // Current apple position
    private Timer _transitionTimer;
    private Tween _fadeTween;
    private Vector2 _originalScale; // Store original scale for transitions
    private bool _isFalling = false; // Track if apple is falling
    private bool _hasLanded = false; // Track if apple has landed on ground
    #endregion

    #region Initialization
    public override void _Ready()
    {
        InitializeComponents();
        SetupRigidBody();
        ConnectSignals();
        SetupPromptLabel();
        SetupTransitionTimer();
        SetInitialState();
        EnableInputProcessing();

        Log.System("Apple item ready", "Apple");
    }

    private void InitializeComponents()
    {
        _detectionArea = GetNode<Area2D>("RigidBody2D/DetectionArea");
        _rigidBody = GetNode<RigidBody2D>("RigidBody2D");
        _appleSprite = GetNode<Sprite2D>("RigidBody2D/AppleSprite");
        _promptLabel = GetNode<Label>("RigidBody2D/PromptLabel");
        _rockDetectionArea = GetNode<Area2D>("RigidBody2D/RockDetectionArea");
        _groundDetectionArea = GetNode<Area2D>("RigidBody2D/GroundDetectionArea");
    }

    private void SetupRigidBody()
    {
        // Connect rock hit detection - the Area2D is already set up in the scene
        _rockDetectionArea.AreaEntered += OnProjectileImpact;
        
        // Connect to ground collision detection via dedicated ground detection area
        _groundDetectionArea.BodyEntered += OnGroundCollision;
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
        // GrayKnockable phase lasts longer (transition phase)
        return (_currentState == AppleState.GrayKnockable) 
            ? NeutralPhaseTime 
            : TransitionTime;
    }

    private void SetInitialState()
    {
        _currentState = AppleState.RedLife;
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
        // Only allow consumption during RedLife and PurpleMana phases
        if (_currentState != AppleState.RedLife && _currentState != AppleState.PurpleMana)
        {
            return;
        }
        
        // Only allow pickup if apple is on ground (prevent tree cheating)
        if (_currentPosition != ApplePosition.OnGround)
        {
            Log.UI("Cannot pickup apple from tree - must be knocked down first", "Apple");
            return;
        }

        if (_currentState == AppleState.RedLife)
        {
            ConsumeRedApple(); // Red phase gives life
        }
        else if (_currentState == AppleState.PurpleMana)
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
        // Different cycles based on apple position
        if (_currentPosition == ApplePosition.OnGround)
        {
            // Ground apple cycle: RedLife → GrayNotCollectible → PurpleMana → RedLife
            _currentState = _currentState switch
            {
                AppleState.RedLife => AppleState.GrayNotCollectible,
                AppleState.GrayNotCollectible => AppleState.PurpleMana,
                AppleState.PurpleMana => AppleState.RedLife,
                AppleState.GrayKnockable => AppleState.GrayNotCollectible, // Transition from tree to ground
                _ => AppleState.RedLife
            };
        }
        else // ApplePosition.OnTree
        {
            // Tree apple cycle: RedLife → GrayKnockable → PurpleMana → RedLife
            _currentState = _currentState switch
            {
                AppleState.RedLife => AppleState.GrayKnockable,
                AppleState.GrayKnockable => AppleState.PurpleMana,
                AppleState.PurpleMana => AppleState.RedLife,
                _ => AppleState.RedLife
            };
        }
        
        // Update texture based on current state
        Texture2D newTexture = _currentState switch
        {
            AppleState.RedLife => RedAppleTexture,
            AppleState.GrayKnockable => NeutralAppleTexture,
            AppleState.PurpleMana => PurpleAppleTexture,
            AppleState.GrayNotCollectible => NeutralAppleTexture,
            _ => RedAppleTexture
        };

        if (newTexture != null)
        {
            _appleSprite.Texture = newTexture;
            
            // Apply visual effects for different states
            ApplyStateVisualEffects();
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
            case AppleState.RedLife:
                // Normal red apple appearance
                break;
            case AppleState.GrayKnockable:
                // Gray appearance - slightly desaturated and knockable by rocks
                _appleSprite.Modulate = new Color(0.7f, 0.7f, 0.7f, 1.0f);
                break;
            case AppleState.PurpleMana:
                // Normal purple apple appearance
                break;
            case AppleState.GrayNotCollectible:
                // Darker gray appearance for fallen apples (not collectible)
                _appleSprite.Modulate = new Color(0.5f, 0.5f, 0.5f, 1.0f);
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
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            _promptLabel.Hide();
        }
    }

    private void OnProjectileImpact(Area2D area)
    {
        // Generic projectile impact detection - any Area2D on layer 8 is considered a projectile
        if (area.CollisionLayer == 8)
        {
            // Apple should fall ONLY when hit in GrayKnockable state
            if (_currentState == AppleState.GrayKnockable && !_isFalling)
            {
                Log.System("Apple hit by rock - triggering fall", "Apple");
                MakeAppleFall();
            }
        }
    }

    private void OnGroundCollision(Node2D body)
    {
        // Check if apple hit the ground (StaticBody2D)
        if (body is StaticBody2D && _isFalling && !_hasLanded)
        {
            Log.System("Apple landed on ground", "Apple");
            // Use CallDeferred to avoid physics state change during collision detection
            CallDeferred(nameof(OnGroundLanding));
        }
    }

    public void MakeAppleFall()
    {
        if (_isFalling) 
        {
            return;
        }
        
        _isFalling = true;
        
        // Use call_deferred to change physics properties safely during collision detection
        CallDeferred(nameof(DeferredMakeAppleFall));
    }
    
    private void DeferredMakeAppleFall()
    {
        // Ensure the RigidBody2D can move and respond to physics
        _rigidBody.Freeze = false; // Unfreeze to allow physics
        _rigidBody.GravityScale = 1.0f; // Enable gravity
        
        // Change position to OnGround and state to GrayNotCollectible when apple falls
        _currentPosition = ApplePosition.OnGround;
        _currentState = AppleState.GrayNotCollectible;
        ApplyStateVisualEffects();
        
        // Add some impulse for more dramatic effect
        Vector2 randomImpulse = new Vector2(
            (float)GD.RandRange(-50, 50), 
            (float)GD.RandRange(-20, 0)
        );
        _rigidBody.ApplyImpulse(randomImpulse);
    }

    public void OnGroundLanding()
    {
        _hasLanded = true;
        
        // Disable RigidBody2D physics
        _rigidBody.GravityScale = 0.0f;
        _rigidBody.LinearVelocity = Vector2.Zero;
        _rigidBody.AngularVelocity = 0.0f;
        _rigidBody.Freeze = true;
        
        // Disable physical collision with player (keep only detection area)
        var physicsCollision = _rigidBody.GetNodeOrNull<CollisionShape2D>("PhysicsCollision");
        if (physicsCollision != null)
        {
            physicsCollision.Disabled = true;
        }
    }

    private void UpdatePromptDisplay()
    {
        if (!_playerNearby)
        {
            _promptLabel.Hide();
            return;
        }

        // Only show prompts for red and purple states AND only when apple is on ground
        bool isCollectibleState = _currentState == AppleState.RedLife || _currentState == AppleState.PurpleMana;
        bool isOnGround = _currentPosition == ApplePosition.OnGround;
        
        if (isCollectibleState && isOnGround)
        {
            string promptText = _currentState switch
            {
                AppleState.RedLife => "Press E (Life)",
                AppleState.PurpleMana => "Press E (Mana)",
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
        if (_transitionTimer != null && (_currentState == AppleState.RedLife || _currentState == AppleState.PurpleMana))
        {
            _transitionTimer.WaitTime = time;
        }
    }

    public void SetNeutralPhaseTime(float time)
    {
        NeutralPhaseTime = time;
        if (_transitionTimer != null && (_currentState == AppleState.GrayKnockable))
        {
            _transitionTimer.WaitTime = time;
        }
    }

    public bool IsCollectible() => _currentState == AppleState.RedLife || _currentState == AppleState.PurpleMana;
    
    public AppleState GetCurrentState() => _currentState;
    
    public ApplePosition GetCurrentPosition() => _currentPosition;
    
    public bool IsInLifePhase() => _currentState == AppleState.RedLife;
    
    public bool IsInManaPhase() => _currentState == AppleState.PurpleMana;
    
    public bool IsOnTree() => _currentPosition == ApplePosition.OnTree;
    
    public bool IsOnGround() => _currentPosition == ApplePosition.OnGround;
    
    public bool CanFallFromRockHit() => _currentState == AppleState.GrayKnockable && _currentPosition == ApplePosition.OnTree && !_isFalling;
    
    public bool IsFalling() => _isFalling;
    #endregion
}
