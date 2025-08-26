using Godot;

// Unified Monster class supporting both simple and follower behaviors
public partial class Monster : CharacterBody2D
{
    #region Exported Properties
    [Export] public int DamageAmount { get; set; } = 1;
    [Export] public bool ShowForTesting { get; set; } = true;
    
    // Follower behavior properties
    [Export] public bool EnableFollowerBehavior { get; set; } = false;
    [Export] public float FollowDistance { get; set; } = 150.0f;
    [Export] public float MinSpawnDistance { get; set; } = 200.0f;
    [Export] public float WalkSpeed { get; set; } = 40.0f;
    [Export] public float InitialWaitTime { get; set; } = 7.0f;
    [Export] public float MinVisibleTime { get; set; } = 30.0f;
    [Export] public float MaxVisibleTime { get; set; } = 90.0f;
    [Export] public float MinHiddenTime { get; set; } = 10.0f;
    [Export] public float MaxHiddenTime { get; set; } = 45.0f;
    
    public bool IsDisabledForReplay { get; private set; } = false;
    #endregion

    #region Follower State (only used when EnableFollowerBehavior = true)
    public enum MonsterState { Following, Hidden, InitialWait }
    private MonsterState _currentState = MonsterState.InitialWait;
    private bool _isVisible = false;
    private bool _isFullyVisible = false;
    private bool _shouldSpawnOnRight = false;
    private float _gravity;
    private Vector2 _initialPosition;
    #endregion

    #region Components
    private Player _player;
    private Timer _stateTimer;
    private AudioStreamPlayer2D _footstepAudio;
    private Sprite2D _sprite;
    private Node2D _spriteNode;
    private Area2D _damageArea;
    private Tween _currentTween;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        AddToGroup("enemies");
        
        if (EnableFollowerBehavior)
        {
            _gravity = ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle();
            _initialPosition = GlobalPosition;
            InitializeFollowerComponents();
        }
        else
        {
            InitializeSimpleComponents();
        }
        
        SetInitialVisibility();
    }

    private void InitializeSimpleComponents()
    {
        FindSprite();
        SetupDamageArea();
    }

    private void InitializeFollowerComponents()
    {
        SetupTimer();
        FindSprite();
        SetupAudio();
        SetupDamageArea();
        CallDeferred(nameof(FindPlayerAndStart));
    }

    private void SetupTimer()
    {
        _stateTimer = new Timer { OneShot = true };
        _stateTimer.Timeout += SwitchState;
        AddChild(_stateTimer);
    }

    private void FindSprite()
    {
        // Try AnimatedSprite2D first, then Sprite2D
        var animatedSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animatedSprite != null)
        {
            _spriteNode = animatedSprite;
            _sprite = null; // Clear sprite reference when using animated sprite
        }
        else
        {
            _sprite = GetNodeOrNull<Sprite2D>("MonsterSprite");
            _spriteNode = _sprite;
        }

        if (_spriteNode == null)
        {
            Log.Error("Monster: No sprite component found!", "Monster");
        }
    }

    private void SetupAudio()
    {
        if (!EnableFollowerBehavior) return;
        
        _footstepAudio = GetNodeOrNull<AudioStreamPlayer2D>("FootstepAudio");
        if (_footstepAudio == null)
        {
            _footstepAudio = new AudioStreamPlayer2D { Name = "FootstepAudio" };
            AddChild(_footstepAudio);
        }
    }

    private void SetupDamageArea()
    {
        _damageArea = GetNodeOrNull<Area2D>("DamageArea");

        if (_damageArea == null)
        {
            CreateDamageArea();
        }

        _damageArea.BodyEntered += OnPlayerEntered;
    }

    private void CreateDamageArea()
    {
        _damageArea = new Area2D { Name = "DamageArea" };
        AddChild(_damageArea);

        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = 25.0f };
        _damageArea.AddChild(shape);
    }

    private void FindPlayerAndStart()
    {
        if (!EnableFollowerBehavior) return;
        
        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (_player != null)
        {
            StartInitialWait();
        }
        else
        {
            CallDeferred(nameof(FindPlayerAndStart));
        }
    }

    private void SetInitialVisibility()
    {
        if (EnableFollowerBehavior)
        {
            // Follower behavior will control visibility
            return;
        }
        
        if (ShowForTesting)
        {
            Show();
            #if DEBUG
            Log.Debug("Monster: Ready for contact damage", "Monster");
            #endif
        }
        else
        {
        }
    }
    #endregion

    #region Physics and Movement
    public void DisableForReplay()
    {
        if (!EnableFollowerBehavior) return;
        
        IsDisabledForReplay = true;
        SetProcessMode(ProcessModeEnum.Disabled);
        Hide();
    }

    public void EnableAfterReplay()
    {
        if (!EnableFollowerBehavior) return;
        
        IsDisabledForReplay = false;
        SetProcessMode(ProcessModeEnum.Always);
        
        if (_spriteNode != null)
        {
            _spriteNode.Visible = true;
            _spriteNode.Modulate = Colors.White;
            Show();
        }
        
        _stateTimer.Stop();
        _currentState = MonsterState.Following;
        _isVisible = true;
        _isFullyVisible = true;
        
        SetCollisionMask(1u);
        SetCollisionLayer(1u);
        
        GetTree().CreateTimer(3.0f).Timeout += () =>
        {
            if (!IsDisabledForReplay)
            {
                StartFollowing();
            }
        };
        
        Log.Debug("Monster enabled after replay - will resume normal behavior in 3 seconds", "Monster");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!EnableFollowerBehavior) return;
        if (IsDisabledForReplay) return;
        
        if (_player?.GetLifeSystem()?.IsDead != false) return;

        if (_currentState == MonsterState.Following)
        {
            ApplyGravity(delta);

            if (_isFullyVisible)
            {
                UpdateMovement();
            }
            else
            {
                Velocity = new Vector2(0, Velocity.Y);
            }

            MoveAndSlide();
        }
    }

    private void ApplyGravity(double delta)
    {
        if (!IsOnFloor())
            Velocity = new Vector2(Velocity.X, Velocity.Y + _gravity * (float)delta);
    }

    private void UpdateMovement()
    {
        Vector2 targetPos = _player.GlobalPosition - new Vector2(FollowDistance, 0);
        float distance = GlobalPosition.DistanceTo(targetPos);

        if (distance > 10.0f)
        {
            Vector2 direction = (targetPos - GlobalPosition).Normalized();
            Velocity = new Vector2(direction.X * WalkSpeed, Velocity.Y);
            UpdateSpriteDirection();
        }
        else
        {
            Velocity = new Vector2(0, Velocity.Y);
        }
    }

    private void UpdateSpriteDirection()
    {
        if (_spriteNode == null || _player == null) return;

        bool shouldFaceLeft = _player.GlobalPosition.X > GlobalPosition.X;
        var scale = _spriteNode.Scale;
        _spriteNode.Scale = new Vector2(shouldFaceLeft ? -Mathf.Abs(scale.X) : Mathf.Abs(scale.X), scale.Y);
    }
    #endregion

    #region State Management
    private void StartInitialWait()
    {
        _currentState = MonsterState.InitialWait;
        _isVisible = true;
        _isFullyVisible = true;

        GlobalPosition = _initialPosition;
        Velocity = Vector2.Zero;

        if (_spriteNode != null)
        {
            _spriteNode.Visible = true;
            _spriteNode.Modulate = Colors.White;
        }

        SetCollisionMask(0u);
        SetCollisionLayer(0u);

        FacePlayer();

        _stateTimer.WaitTime = InitialWaitTime;
        _stateTimer.Start();
    }

    private void StartFollowing()
    {
        _currentState = MonsterState.Following;
        _isVisible = true;
        _isFullyVisible = false;

        PositionBehindPlayerOnGround();
        SetCollisionMask(1u);
        SetCollisionLayer(1u);
        FacePlayer();

        if (_spriteNode != null)
        {
            _spriteNode.Visible = true;
            _spriteNode.Modulate = new Color(1, 1, 1, 0);
        }

        CallDeferred(nameof(DelayedFadeIn));

        PlayAudio(true);

        float visibleTime = GD.Randf() * (MaxVisibleTime - MinVisibleTime) + MinVisibleTime;
        _stateTimer.WaitTime = visibleTime;
        _stateTimer.Start();
    }

    private void DelayedFadeIn()
    {
        FadeIn();
    }

    private void StartHiding()
    {
        _currentState = MonsterState.Hidden;
        _isVisible = false;
        _isFullyVisible = false;

        FadeOut();
        PlayAudio(false);

        float hiddenTime = GD.Randf() * (MaxHiddenTime - MinHiddenTime) + MinHiddenTime;
        _stateTimer.WaitTime = hiddenTime;
        _stateTimer.Start();
    }

    private void SwitchState()
    {
        if (_currentState == MonsterState.InitialWait)
            StartFirstTransition();
        else if (_currentState == MonsterState.Following)
            StartHiding();
        else
            StartFollowing();
    }

    private void StartFirstTransition()
    {
        _currentState = MonsterState.Following;
        _isVisible = false;
        _isFullyVisible = false;

        FadeOut();
        PlayAudio(false);

        GetTree().CreateTimer(0.5f).Timeout += () =>
        {
            PositionBehindPlayerOnGround();
            SetCollisionMask(1u);
            SetCollisionLayer(1u);
            FacePlayer();

            if (_spriteNode != null)
            {
                _spriteNode.Visible = true;
                _spriteNode.Modulate = new Color(1, 1, 1, 0);
            }

            CallDeferred(nameof(DelayedFadeIn));
            PlayAudio(true);

            float visibleTime = GD.Randf() * (MaxVisibleTime - MinVisibleTime) + MinVisibleTime;
            _stateTimer.WaitTime = visibleTime;
            _stateTimer.Start();
        };
    }

    private void FacePlayer()
    {
        if (_spriteNode == null || _player == null) return;

        bool shouldFaceLeft = _player.GlobalPosition.X > GlobalPosition.X;
        var scale = _spriteNode.Scale;
        _spriteNode.Scale = new Vector2(shouldFaceLeft ? -Mathf.Abs(scale.X) : Mathf.Abs(scale.X), scale.Y);
    }

    private void PositionBehindPlayerOnGround()
    {
        if (_player == null) return;

        Vector2 currentPlayerPos = _player.GlobalPosition;
        float spawnDistance = _currentState == MonsterState.InitialWait ? MinSpawnDistance : FollowDistance;
        float safeDistance = Mathf.Max(spawnDistance, 80.0f);

        _shouldSpawnOnRight = GD.Randf() > 0.5f;
        float xOffset = _shouldSpawnOnRight ? safeDistance : -safeDistance;

        Vector2 targetPosition = new Vector2(currentPlayerPos.X + xOffset, currentPlayerPos.Y);

        GlobalPosition = targetPosition;
        Velocity = Vector2.Zero;

        #if DEBUG
        Log.Debug($"Monster positioned on {(_shouldSpawnOnRight ? "right" : "left")} side at distance {safeDistance}", "Monster");
        #endif
    }
    #endregion

    #region Visual and Audio Effects
    private void FadeIn()
    {
        if (_spriteNode == null) return;

        _currentTween?.Kill();

        _spriteNode.Visible = true;
        _spriteNode.Modulate = new Color(1, 1, 1, 0);

        _currentTween = CreateTween();
        _currentTween.TweenProperty(_spriteNode, "modulate:a", 1.0f, 0.5f);
        _currentTween.TweenCallback(Callable.From(() =>
        {
            _isFullyVisible = true;
            Log.Debug("Monster fade in complete - fully visible", "Monster");
        }));
    }

    private void FadeOut()
    {
        if (_spriteNode == null) return;

        _currentTween?.Kill();

        SetCollisionMask(0u);
        SetCollisionLayer(0u);
        _isFullyVisible = false;

        _currentTween = CreateTween();
        _currentTween.TweenProperty(_spriteNode, "modulate:a", 0.0f, 0.5f);
        _currentTween.TweenCallback(Callable.From(() =>
        {
            _spriteNode.Visible = false;
            Log.Debug("Monster fade out complete - fully hidden", "Monster");
        }));
    }

    public void PlayDamageEffect()
    {
        var targetSprite = _sprite ?? _spriteNode;
        if (targetSprite == null) return;

        var tween = CreateTween();
        tween.TweenProperty(targetSprite, "modulate", Colors.Purple, 0.2f);
        tween.TweenProperty(targetSprite, "modulate", Colors.White, 0.3f);
    }

    private void PlayAudio(bool play)
    {
        if (_footstepAudio == null) return;

        if (play && !_footstepAudio.Playing)
            _footstepAudio.Play();
        else if (!play)
            _footstepAudio.Stop();
    }
    #endregion

    #region Public API
    public new bool IsVisible() => EnableFollowerBehavior ? _isVisible : Visible;
    public bool IsFullyVisible() => EnableFollowerBehavior ? _isFullyVisible : true;
    public MonsterState GetState() => _currentState;
    public void ForceHide() { if (EnableFollowerBehavior) { _stateTimer.Stop(); StartHiding(); } }
    public void ForceShow() { if (EnableFollowerBehavior) { _stateTimer.Stop(); StartFollowing(); } }

    public bool CanTakeDamage() => EnableFollowerBehavior ? (_isFullyVisible && _currentState == MonsterState.Following) : true;

    public void Reset()
    {
        if (!EnableFollowerBehavior) return;
        
        _stateTimer?.Stop();
        _currentTween?.Kill();

        GlobalPosition = _initialPosition;
        Velocity = Vector2.Zero;

        _currentState = MonsterState.InitialWait;
        _isVisible = true;
        _isFullyVisible = true;

        if (_spriteNode != null)
        {
            _spriteNode.Visible = true;
            _spriteNode.Modulate = Colors.White;
        }

        SetCollisionMask(0u);
        SetCollisionLayer(0u);

        PlayAudio(false);
        FacePlayer();

        _stateTimer.WaitTime = InitialWaitTime;
        _stateTimer.Start();

        Log.Debug($"Monster reset to initial position {_initialPosition}", "Monster");
    }

    private void OnPlayerEntered(Node2D body)
    {
        Log.Debug($"Monster {Name} collision with {body.Name} - IsGhost: {body.IsInGroup("ghost_player") || body.IsInGroup("ghost_monster")}", "Monster");

        if (body is Player player && !player.GetLifeSystem()?.IsDead == true)
        {
            // Check if we can damage based on behavior mode
            if (EnableFollowerBehavior && !CanTakeDamage()) return;
            
            Log.Debug($"Monster {Name} attempting damage to player", "Monster");
            if (player.TakeDamage(DamageAmount))
            {
                PlayDamageEffect();
            }
        }
    }
    #endregion
}