using Godot;

// Stalker enemy that follows player and disappears randomly to create tension
public partial class FollowerMonster : CharacterBody2D
{
    #region Exported Properties
    [Export] public float FollowDistance = 150.0f;
    [Export] public float MinSpawnDistance = 200.0f; // Minimum distance when appearing
    [Export] public float WalkSpeed = 40.0f;
    [Export] public float InitialWaitTime = 7.0f; // Time before first stalking behavior starts
    [Export] public float MinVisibleTime = 30.0f;
    [Export] public float MaxVisibleTime = 90.0f;
    [Export] public float MinHiddenTime = 10.0f;
    [Export] public float MaxHiddenTime = 45.0f;
    #endregion

    #region State
    public enum MonsterState { Following, Hidden, InitialWait }
    private MonsterState _currentState = MonsterState.InitialWait;
    private bool _isVisible = false;
    private bool _isFullyVisible = false;
    private bool _shouldSpawnOnRight = false;
    private float _gravity;
    private Vector2 _initialPosition; // Store monster's scene position
    #endregion

    #region Components
    private Player _player;
    private Timer _stateTimer;
    private AudioStreamPlayer2D _footstepAudio;
    private Node2D _spriteNode;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        AddToGroup("enemies");
        _gravity = ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle();

        // Store the initial position set in the scene
        _initialPosition = GlobalPosition;

        InitializeComponents();
    }

    private void InitializeComponents()
    {
        SetupTimer();
        FindSprite();
        SetupAudio();
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
        }
        else
        {
            _spriteNode = GetNodeOrNull<Sprite2D>("MonsterSprite");
        }
    }

    private void SetupAudio()
    {
        _footstepAudio = GetNodeOrNull<AudioStreamPlayer2D>("FootstepAudio");
        if (_footstepAudio == null)
        {
            _footstepAudio = new AudioStreamPlayer2D { Name = "FootstepAudio" };
            AddChild(_footstepAudio);
        }
    }

    private void FindPlayerAndStart()
    {
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
    #endregion

    #region Physics
    public override void _PhysicsProcess(double delta)
    {
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
                // During fade-in, stop horizontal movement but allow gravity
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

        // Always face toward player when moving
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
        _isFullyVisible = false;

        GlobalPosition = _initialPosition;
        Velocity = Vector2.Zero;

        if (_spriteNode != null)
        {
            _spriteNode.Visible = true;
        }

        SetCollisionMask(0u);
        SetCollisionLayer(0u);

        FacePlayer();

        _stateTimer.WaitTime = InitialWaitTime;
        _stateTimer.Start();

        GD.Print($"Monster waiting at scene position {_initialPosition} for {InitialWaitTime} seconds");
    }

    private void StartFollowing()
    {
        _currentState = MonsterState.Following;
        _isVisible = true;
        _isFullyVisible = false;

        // Position on random side and start fade immediately
        PositionBehindPlayerOnGround();

        // Enable collision immediately to prevent falling through ground
        SetCollisionMask(1u);
        SetCollisionLayer(1u);

        FacePlayer();
        FadeIn();
        PlayAudio(true);

        float visibleTime = GD.Randf() * (MaxVisibleTime - MinVisibleTime) + MinVisibleTime;
        _stateTimer.WaitTime = visibleTime;
        _stateTimer.Start();

        GD.Print("Monster started following behavior");
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
            StartFollowing();
        else if (_currentState == MonsterState.Following)
            StartHiding();
        else
            StartFollowing();
    }

    private void FacePlayer()
    {
        if (_spriteNode == null || _player == null) return;

        bool shouldFaceLeft = _player.GlobalPosition.X > GlobalPosition.X;
        var scale = _spriteNode.Scale;
        _spriteNode.Scale = new Vector2(shouldFaceLeft ? -Mathf.Abs(scale.X) : Mathf.Abs(scale.X), scale.Y);

        GD.Print($"Monster facing player: {(shouldFaceLeft ? "left (flipped)" : "right (normal)")}");
    }

    private void PositionBehindPlayerOnGround()
    {
        if (_player == null) return;

        Vector2 currentPlayerPos = _player.GlobalPosition;
        float spawnDistance = _currentState == MonsterState.InitialWait ? MinSpawnDistance : FollowDistance;

        // Ensure minimum safe distance to avoid immediate collision
        float safeDistance = Mathf.Max(spawnDistance, 80.0f);

        // Randomly choose left or right side
        _shouldSpawnOnRight = GD.Randf() > 0.5f;
        float xOffset = _shouldSpawnOnRight ? safeDistance : -safeDistance;

        Vector2 targetPosition = new Vector2(currentPlayerPos.X + xOffset, currentPlayerPos.Y);

        GlobalPosition = targetPosition;
        Velocity = Vector2.Zero;

        GD.Print($"Monster positioned on {(_shouldSpawnOnRight ? "right" : "left")} side at distance {safeDistance}");
    }

    private void EnableCollisionAfterDelay()
    {
        // Simple 1-second delay before enabling collision
        GetTree().CreateTimer(1.0f).Timeout += () =>
        {
            if (_isFullyVisible && _currentState == MonsterState.Following)
            {
                SetCollisionMask(1u);
                SetCollisionLayer(1u);
                GD.Print("Monster collision enabled");
            }
        };
    }
    #endregion

    #region Visual and Audio Effects
    private void FadeIn()
    {
        if (_spriteNode == null) return;

        _spriteNode.Visible = true;
        _spriteNode.Modulate = new Color(1, 1, 1, 0);

        var tween = CreateTween();
        tween.TweenProperty(_spriteNode, "modulate:a", 1.0f, 0.5f);
        tween.TweenCallback(Callable.From(() =>
        {
            _isFullyVisible = true;
            GD.Print("Monster fully visible");
        }));
    }

    private void FadeOut()
    {
        if (_spriteNode == null) return;

        // Disable collision immediately when starting to fade out
        SetCollisionMask(0u);
        SetCollisionLayer(0u);
        _isFullyVisible = false;

        var tween = CreateTween();
        tween.TweenProperty(_spriteNode, "modulate:a", 0.0f, 0.5f);
        tween.TweenCallback(Callable.From(() =>
        {
            _spriteNode.Visible = false;
            GD.Print("Monster fully hidden - cannot take damage");
        }));
    }

    public void PlayDamageEffect()
    {
        if (_spriteNode == null) return;

        var tween = CreateTween();
        tween.TweenProperty(_spriteNode, "modulate", Colors.Purple, 0.2f);
        tween.TweenProperty(_spriteNode, "modulate", Colors.White, 0.3f);
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
    public bool IsVisible() => _isVisible;
    public bool IsFullyVisible() => _isFullyVisible;
    public MonsterState GetState() => _currentState;
    public void ForceHide() { _stateTimer.Stop(); StartHiding(); }
    public void ForceShow() { _stateTimer.Stop(); StartFollowing(); }

    // Override to prevent damage during fade effects and initial wait
    public bool CanTakeDamage() => _isFullyVisible && _currentState == MonsterState.Following;

    // Reset monster to initial state for game restart
    public void Reset()
    {
        // Stop any active timers
        _stateTimer?.Stop();

        // Reset to initial position and state
        GlobalPosition = _initialPosition;
        Velocity = Vector2.Zero;

        // Reset state variables
        _currentState = MonsterState.InitialWait;
        _isVisible = true;
        _isFullyVisible = false;

        // Make visible at initial position
        if (_spriteNode != null)
        {
            _spriteNode.Visible = true;
            _spriteNode.Modulate = Colors.White;
        }

        // Disable collision
        SetCollisionMask(0u);
        SetCollisionLayer(0u);

        // Stop audio
        PlayAudio(false);

        // Face player from initial position
        FacePlayer();

        // Restart initial wait
        _stateTimer.WaitTime = InitialWaitTime;
        _stateTimer.Start();

        GD.Print($"Monster reset to initial position {_initialPosition}");
    }
    #endregion
}