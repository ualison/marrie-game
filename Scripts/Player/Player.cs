using Godot;

// Main character controller - handles movement, damage, and visual feedback
// Refactored for better performance and cleaner separation of concerns
public partial class Player : CharacterBody2D
{
	[Export] public float Speed = 120.0f;
	[Export] public float JumpHeight = 100.0f;

	private float _jumpVelocity;
	private bool _facingRight = true;
	private float _gravity = ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle();
	private bool _isPaused = false;

	// Cached component references for performance
	private Node2D _sprite;
	private PlayerLife _playerLife;
	private Area2D _hurtBox;

	// Visual feedback system
	private Tween _blinkTween;
	private Timer _visualTimer;
	private bool _isBlinking;

	public override void _Ready()
	{
		AddToGroup("player");
		InitializeComponents();
		CalculateJumpVelocity();
		GD.Print($"Player initialized - Health system: {(_playerLife != null ? "✓" : "✗")}");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isPaused || _playerLife?.IsDead == true) return; // Add _isPaused check

		ApplyGravity(delta);
		HandleJumpInput();
		HandleMovementInput();

		MoveAndSlide();

		UpdateVisuals();
	}

	// Handles damage from external sources
	public bool TakeDamage(int damage = 1) => _playerLife?.TakeDamage(damage) ?? false;

	// Heals the player
	public void Heal(int amount = 1) => _playerLife?.Heal(amount);

	// Gets the player's life system for external access
	public PlayerLife GetLifeSystem() => _playerLife;

	// Resets player to initial state
	public void Reset()
	{
		_playerLife?.Reset();
		Velocity = Vector2.Zero;
		StopVisualEffects();
		RestoreNormalAppearance();
	}

	private void InitializeComponents()
	{
		FindSpriteComponent();
		SetupLifeSystem();
		SetupHurtBox();
		SetupVisualTimer();
	}

	private void FindSpriteComponent()
	{
		// Optimized sprite discovery - check common names first
		_sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D")
			?? GetNodeOrNull<Sprite2D>("Sprite2D")
			?? GetNodeOrNull<Node2D>("MarrieSprite")
			?? FindFirstSpriteChild();
	}

	private Node2D FindFirstSpriteChild()
	{
		foreach (Node child in GetChildren())
		{
			if (child is Sprite2D or AnimatedSprite2D)
				return child as Node2D;
		}
		return null;
	}

	private void SetupLifeSystem()
	{
		// Find existing PlayerLife or create one
		_playerLife = GetNodeOrNull<PlayerLife>("PlayerLife") ?? FindPlayerLifeInChildren();

		if (_playerLife == null)
		{
			_playerLife = new PlayerLife { Name = "PlayerLife" };
			AddChild(_playerLife);
			CallDeferred(nameof(AddLifeSystemToGroup));
		}

		// Connect death signal
		if (!_playerLife.IsConnected(PlayerLife.SignalName.PlayerDied, new Callable(this, nameof(OnPlayerDied))))
		{
			_playerLife.PlayerDied += OnPlayerDied;
		}
	}

	private PlayerLife FindPlayerLifeInChildren()
	{
		foreach (Node child in GetChildren())
		{
			if (child is PlayerLife life)
				return life;
		}
		return null;
	}

	private void AddLifeSystemToGroup()
	{
		_playerLife?.AddToGroup("life_system");
	}

	private void SetupHurtBox()
	{
		_hurtBox = GetNodeOrNull<Area2D>("HurtBox");
		if (_hurtBox != null)
		{
			_hurtBox.BodyEntered += OnHurtBoxBodyEntered;
			_hurtBox.AreaEntered += OnHurtBoxAreaEntered;
		}
	}

	private void SetupVisualTimer()
	{
		_visualTimer = new Timer { OneShot = true };
		_visualTimer.Timeout += StopVisualEffects;
		AddChild(_visualTimer);
	}

	private void CalculateJumpVelocity()
	{
		_jumpVelocity = -Mathf.Sqrt(2.0f * _gravity * JumpHeight);
	}

	private void ApplyGravity(double delta)
	{
		if (!IsOnFloor())
		{
			Velocity = new Vector2(Velocity.X, Velocity.Y + _gravity * (float)delta);
		}
	}

	private void HandleJumpInput()
	{
		if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
		{
			Velocity = new Vector2(Velocity.X, _jumpVelocity);
		}
	}

	private void HandleMovementInput()
	{
		float horizontal = Input.GetAxis("ui_left", "ui_right");

		Velocity = horizontal != 0
			? new Vector2(horizontal * Speed, Velocity.Y)
			: new Vector2(Mathf.MoveToward(Velocity.X, 0, Speed), Velocity.Y);
	}

	private void UpdateVisuals()
	{
		float direction = Input.GetAxis("ui_left", "ui_right");
		UpdateSpriteDirection(direction);
		UpdateAnimations(direction);
		UpdateInvulnerabilityVisual();
	}

	private void UpdateSpriteDirection(float direction)
	{
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
		if (_sprite is not AnimatedSprite2D animSprite || animSprite.SpriteFrames == null) return;

		string animation = IsOnFloor()
			? (Mathf.Abs(direction) > 0 ? "walk" : "idle")
			: (Velocity.Y < 0 ? "jump" : "fall");

		if (animSprite.SpriteFrames.HasAnimation(animation))
			animSprite.Play(animation);
	}

	private void UpdateInvulnerabilityVisual()
	{
		if (_playerLife == null) return;

		if (_playerLife.IsInvulnerable && !_isBlinking)
			StartBlinkEffect();
		else if (!_playerLife.IsInvulnerable && _isBlinking)
			StopVisualEffects();
	}

	private void StartBlinkEffect()
	{
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
		_isBlinking = false;
		_visualTimer.Stop();
		_blinkTween?.Kill();
		RestoreNormalAppearance();
	}

	private void RestoreNormalAppearance()
	{
		if (_sprite != null)
			_sprite.Modulate = Colors.White;
	}

	private void OnHurtBoxBodyEntered(Node2D body)
	{
		if (body.IsInGroup("enemy"))
			ProcessDamageFromEnemy(body);
	}

	private void OnHurtBoxAreaEntered(Area2D area)
	{
		if (area.IsInGroup("enemy"))
			ProcessDamageFromEnemy(area);
	}

	private void ProcessDamageFromEnemy(Node2D enemy)
	{
		if (TakeDamage(1))
		{
			ApplyKnockback(enemy);
			GD.Print($"Player took damage! Health: {_playerLife.CurrentLife}");
		}
	}

	private void ApplyKnockback(Node2D source)
	{
		Vector2 direction = (GlobalPosition - source.GlobalPosition).Normalized();
		if (direction.Length() < 0.1f) direction = Vector2.Left;

		Velocity = new Vector2(direction.X * 200.0f, -100.0f);
	}

	private void OnPlayerDied()
	{
		GD.Print("Player died!");
		Velocity = Vector2.Zero;

		// Apply death visual effect
		if (_sprite != null)
			_sprite.Modulate = new Color(1.0f, 0.3f, 0.3f, 0.7f);

		// Play death animation if available
		if (_sprite is AnimatedSprite2D animSprite && animSprite.SpriteFrames?.HasAnimation("death") == true)
			animSprite.Play("death");

		GameManager.Instance?.PlayerDied(GlobalPosition);
	}

	public void PausePlayer()
	{
		_isPaused = true;
		GD.Print("Player paused for replay");
	}

	public void ResumePlayer()
	{
		_isPaused = false;
		GD.Print("Player resumed after replay");
	}

	public bool IsPaused() => _isPaused;
}