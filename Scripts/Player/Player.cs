using Godot;

// Main character controller handling movement, damage, and visual feedback
public partial class Player : CharacterBody2D
{
    #region Exported Properties
    [Export] public float Speed = 120.0f;
    [Export] public float JumpHeight = 100.0f;
    #endregion

    #region Movement State
    private float _jumpVelocity;
    private bool _facingRight = true;
    private float _gravity;
    private bool _isPaused = false;
    #endregion

    #region Components
    private Node2D _sprite;
    private PlayerLife _playerLife;
    private Area2D _hurtBox;
    #endregion

    #region Visual Effects
    private Tween _blinkTween;
    private Timer _visualTimer;
    private bool _isBlinking;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Add to group for easy discovery and initialize all systems
        AddToGroup("player");
        InitializePhysics();
        InitializeComponents();
        GD.Print($"Player initialized - Health system: {(_playerLife != null ? "✓" : "✗")}");
    }

    private void InitializePhysics()
    {
        // Cache gravity and calculate jump velocity based on desired height
        _gravity = ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle();
        _jumpVelocity = -Mathf.Sqrt(2.0f * _gravity * JumpHeight);
    }

    private void InitializeComponents()
    {
        // Setup all required components in order
        FindSpriteComponent();
        SetupLifeSystem();
        SetupHurtBox();
        SetupVisualTimer();
    }
    #endregion

    #region Core Game Loop
    public override void _PhysicsProcess(double delta)
    {
        // Skip physics when paused or dead
        if (_isPaused || _playerLife?.IsDead == true) return;

        ProcessMovement(delta);
        UpdateVisuals();
    }

    private void ProcessMovement(double delta)
    {
        // Apply gravity, handle input, and move character
        ApplyGravity(delta);
        HandleJumpInput();
        HandleMovementInput();
        MoveAndSlide();
    }
    #endregion

    #region Physics and Movement
    private void ApplyGravity(double delta)
    {
        // Apply gravity when not grounded
        if (!IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, Velocity.Y + _gravity * (float)delta);
        }
    }

    private void HandleJumpInput()
    {
        // Jump when accept pressed and on ground
        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, _jumpVelocity);
        }
    }

    private void HandleMovementInput()
    {
        // Get horizontal input and apply movement or friction
        float horizontal = Input.GetAxis("ui_left", "ui_right");

        Velocity = horizontal != 0
            ? new Vector2(horizontal * Speed, Velocity.Y)
            : new Vector2(Mathf.MoveToward(Velocity.X, 0, Speed), Velocity.Y);
    }
    #endregion

    #region Visual Updates
    private void UpdateVisuals()
    {
        // Update sprite direction, animations, and special effects
        float direction = Input.GetAxis("ui_left", "ui_right");
        UpdateSpriteDirection(direction);
        UpdateAnimations(direction);
        UpdateInvulnerabilityVisual();
    }

    private void UpdateSpriteDirection(float direction)
    {
        // Flip sprite based on movement direction
        if (_sprite == null || direction == 0) return;

        bool shouldFaceRight = direction > 0;
        if (shouldFaceRight != _facingRight)
        {
            _facingRight = shouldFaceRight;
            var scale = _sprite.Scale;
            _sprite.Scale = new Vector2(_facingRight ? Mathf.Abs(scale.X) : -Mathf.Abs(scale.X), scale.Y);
        }
    }

    private void UpdateAnimations(float direction)
    {
        // Play appropriate animation based on state
        if (_sprite is not AnimatedSprite2D animSprite || animSprite.SpriteFrames == null) return;

        string animation = DetermineAnimation(direction);
        if (animSprite.SpriteFrames.HasAnimation(animation))
            animSprite.Play(animation);
    }

    private string DetermineAnimation(float direction)
    {
        // Choose animation based on movement state
        if (!IsOnFloor())
            return Velocity.Y < 0 ? "jump" : "fall";
        
        return Mathf.Abs(direction) > 0 ? "walk" : "idle";
    }

    private void UpdateInvulnerabilityVisual()
    {
        // Start or stop blinking effect based on invulnerability state
        if (_playerLife == null) return;

        if (_playerLife.IsInvulnerable && !_isBlinking)
            StartBlinkEffect();
        else if (!_playerLife.IsInvulnerable && _isBlinking)
            StopVisualEffects();
    }
    #endregion

    #region Component Setup
    private void FindSpriteComponent()
    {
        // Find sprite component with optimized search order
        _sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D")
            ?? GetNodeOrNull<Sprite2D>("Sprite2D")
            ?? GetNodeOrNull<Node2D>("MarrieSprite")
            ?? FindFirstSpriteChild();
    }

    private Node2D FindFirstSpriteChild()
    {
        // Fallback search through all children
        foreach (Node child in GetChildren())
        {
            if (child is Sprite2D or AnimatedSprite2D)
                return child as Node2D;
        }
        return null;
    }

    private void SetupLifeSystem()
    {
        // Find or create PlayerLife component
        _playerLife = GetNodeOrNull<PlayerLife>("PlayerLife") ?? FindPlayerLifeInChildren();

        if (_playerLife == null)
        {
            _playerLife = new PlayerLife { Name = "PlayerLife" };
            AddChild(_playerLife);
            CallDeferred(nameof(AddLifeSystemToGroup));
        }

        ConnectLifeSystemSignals();
    }

    private PlayerLife FindPlayerLifeInChildren()
    {
        // Search for existing PlayerLife in children
        foreach (Node child in GetChildren())
        {
            if (child is PlayerLife life)
                return life;
        }
        return null;
    }

    private void ConnectLifeSystemSignals()
    {
        // Connect death signal to avoid duplicate connections
        if (!_playerLife.IsConnected(PlayerLife.SignalName.PlayerDied, new Callable(this, nameof(OnPlayerDied))))
        {
            _playerLife.PlayerDied += OnPlayerDied;
        }
    }

    private void AddLifeSystemToGroup()
    {
        // Add PlayerLife to group for external discovery
        _playerLife?.AddToGroup("life_system");
    }

    private void SetupHurtBox()
    {
        // Connect hurt box signals for damage detection
        _hurtBox = GetNodeOrNull<Area2D>("HurtBox");
        if (_hurtBox != null)
        {
            _hurtBox.BodyEntered += OnHurtBoxBodyEntered;
            _hurtBox.AreaEntered += OnHurtBoxAreaEntered;
        }
    }

    private void SetupVisualTimer()
    {
        // Timer for visual effect duration control
        _visualTimer = new Timer { OneShot = true };
        _visualTimer.Timeout += StopVisualEffects;
        AddChild(_visualTimer);
    }
    #endregion

    #region Visual Effects
    private void StartBlinkEffect()
    {
        // Create blinking effect during invulnerability
        if (_sprite == null) return;

        _isBlinking = true;
        _blinkTween?.Kill();

        _blinkTween = CreateTween();
        _blinkTween.SetLoops();
        _blinkTween.TweenProperty(_sprite, "modulate:a", 0.3f, 0.15f);
        _blinkTween.TweenProperty(_sprite, "modulate:a", 1.0f, 0.15f);

        _visualTimer.WaitTime = 2.0f;
        _visualTimer.Start();
    }

    private void StopVisualEffects()
    {
        // Stop all visual effects and restore normal appearance
        _isBlinking = false;
        _visualTimer.Stop();
        _blinkTween?.Kill();
        RestoreNormalAppearance();
    }

    private void RestoreNormalAppearance()
    {
        // Reset sprite to normal color
        if (_sprite != null)
            _sprite.Modulate = Colors.White;
    }
    #endregion

    #region Damage System
    private void OnHurtBoxBodyEntered(Node2D body)
    {
        // Handle collision with enemy bodies
        if (body.IsInGroup("enemy"))
            ProcessDamageFromEnemy(body);
    }

    private void OnHurtBoxAreaEntered(Area2D area)
    {
        // Handle collision with enemy areas
        if (area.IsInGroup("enemy"))
            ProcessDamageFromEnemy(area);
    }

    private void ProcessDamageFromEnemy(Node2D enemy)
    {
        // Apply damage and knockback if successful
        if (TakeDamage(1))
        {
            ApplyKnockback(enemy);
            GD.Print($"Player took damage! Health: {_playerLife.CurrentLife}");
        }
    }

    private void ApplyKnockback(Node2D source)
    {
        // Calculate knockback direction and apply velocity
        Vector2 direction = (GlobalPosition - source.GlobalPosition).Normalized();
        if (direction.Length() < 0.1f) direction = Vector2.Left; // Fallback direction

        Velocity = new Vector2(direction.X * 200.0f, -100.0f);
    }

    private void OnPlayerDied()
    {
        // Handle player death - stop movement and apply visual effects
        GD.Print("Player died!");
        Velocity = Vector2.Zero;

        ApplyDeathVisuals();
        GameManager.Instance?.PlayerDied(GlobalPosition);
    }

    private void ApplyDeathVisuals()
    {
        // Apply death tint and animation
        if (_sprite != null)
            _sprite.Modulate = new Color(1.0f, 0.3f, 0.3f, 0.7f);

        if (_sprite is AnimatedSprite2D animSprite && animSprite.SpriteFrames?.HasAnimation("death") == true)
            animSprite.Play("death");
    }
    #endregion

    #region Public API
    public bool TakeDamage(int damage = 1) => _playerLife?.TakeDamage(damage) ?? false;
    public void Heal(int amount = 1) => _playerLife?.Heal(amount);
    public PlayerLife GetLifeSystem() => _playerLife; // Get life system for external access

    public void Reset()
    {
        // Reset player to initial state
        _playerLife?.Reset();
        Velocity = Vector2.Zero;
        StopVisualEffects();
        RestoreNormalAppearance();
    }

    public void PausePlayer()
    {
        // Pause player movement for replay mode
        _isPaused = true;
        GD.Print("Player paused for replay");
    }

    public void ResumePlayer()
    {
        // Resume player movement after replay
        _isPaused = false;
        GD.Print("Player resumed after replay");
    }

    public bool IsPaused() => _isPaused; // Check if player is paused
    #endregion
}