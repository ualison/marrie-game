using Godot;
using MarrieGame.Core.Enums;

// Unified Monster class supporting both simple and follower behaviors
public partial class Monster : CharacterBody2D
{
    #region Exported Properties
    [Export] public int DamageAmount { get; set; } = 1;
    [Export] public bool ShowForTesting { get; set; } = true;
    [Export] public MonsterReality CurrentReality { get; set; } = MonsterReality.CurrentReality;
    
    // Follower behavior properties
    [Export] public bool EnableFollowerBehavior { get; set; } = false;
    [Export] public float FollowDistance { get; set; } = 150.0f;
    [Export] public float MinSpawnDistance { get; set; } = 200.0f;
    [Export] public float MinimumFollowDistance { get; set; } = 80.0f;
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
    private MonsterLife _monsterLife;
    private MonsterLifeBar _monsterLifeBar;
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
        FindMonsterLife();
        FindMonsterLifeBar();
        SetupDamageArea();
    }

    private void InitializeFollowerComponents()
    {
        SetupTimer();
        FindSprite();
        FindMonsterLife();
        FindMonsterLifeBar();
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

    private void FindMonsterLife()
    {
        // Find MonsterLife component
        _monsterLife = GetNodeOrNull<MonsterLife>("MonsterLife");
        
        if (_monsterLife == null)
        {
            Log.Warning("Monster: No MonsterLife component found!", "Monster");
        }
        else
        {
            Log.Debug("Monster: MonsterLife component found and connected", "Monster");
        }
    }

    private void FindMonsterLifeBar()
    {
        // Find MonsterLifeBar component
        _monsterLifeBar = GetNodeOrNull<MonsterLifeBar>("MonsterLifeBar");
        
        if (_monsterLifeBar == null)
        {
            Log.Warning("Monster: No MonsterLifeBar component found!", "Monster");
        }
        else
        {
            Log.Debug("Monster: MonsterLifeBar component found and connected", "Monster");
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
            // Show MonsterLifeBar for testing
            if (_monsterLifeBar != null)
            {
                _monsterLifeBar.Show();
            }
            #if DEBUG
            Log.Debug("Monster: Ready for contact damage", "Monster");
            #endif
        }
        else
        {
            // Hide MonsterLifeBar when monster is hidden
            if (_monsterLifeBar != null)
            {
                _monsterLifeBar.Hide();
            }
        }
    }
    #endregion

    #region Physics and Movement
    public void DisableForReplay()
    {
        if (!EnableFollowerBehavior) return;
        
        IsDisabledForReplay = true;
        SetProcessMode(ProcessModeEnum.Disabled);
        
        // Hide MonsterLifeBar during replay
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Hide();
        }
        
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
        
        // Show MonsterLifeBar when enabling after replay
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Show();
        }
        
        _stateTimer.Stop();
        _currentState = MonsterState.Following;
        _isVisible = true;
        _isFullyVisible = true;
        
        SetCollisionMask(1u);
        SetCollisionLayer(1u);
        
        // Reset MonsterLife to full health
        _monsterLife?.Reset();
        
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

        // Show MonsterLifeBar during initial wait
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Show();
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

        // Hide life bar before repositioning to prevent showing at old position
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Hide();
        }

        PositionBehindPlayerOnGround();
        SetCollisionMask(1u);
        SetCollisionLayer(1u);
        FacePlayer();

        // Ensure life bar position is updated after repositioning
        UpdateLifeBarPosition();

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
        {
            // Check if monster is close enough to player to avoid hiding
            if (_player != null)
            {
                float distanceToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
                if (distanceToPlayer <= MinimumFollowDistance)
                {
                    // Monster is too close to player, continue following instead of hiding
                    float extendedVisibleTime = GD.Randf() * (MaxVisibleTime - MinVisibleTime) + MinVisibleTime;
                    _stateTimer.WaitTime = extendedVisibleTime;
                    _stateTimer.Start();
                    Log.Debug($"Monster too close to player ({distanceToPlayer:F1} <= {MinimumFollowDistance}), extending follow time", "Monster");
                    return;
                }
            }
            StartHiding();
        }
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
            // Hide life bar before repositioning
            if (_monsterLifeBar != null)
            {
                _monsterLifeBar.Hide();
            }

            PositionBehindPlayerOnGround();
            SetCollisionMask(1u);
            SetCollisionLayer(1u);
            FacePlayer();

            // Ensure life bar position is updated after repositioning
            UpdateLifeBarPosition();

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

        // Update MonsterLifeBar position immediately after teleport, even if hidden
        // This prevents the life bar from appearing at the old position when fading back in
        UpdateLifeBarPosition();

        #if DEBUG
        Log.Debug($"Monster positioned on {(_shouldSpawnOnRight ? "right" : "left")} side at distance {safeDistance}", "Monster");
        #endif
    }

    private void UpdateLifeBarPosition()
    {
        if (_monsterLifeBar != null && _monsterLifeBar.FollowMonster)
        {
            // Wait one frame to ensure monster position is fully updated
            CallDeferred(nameof(DoUpdateLifeBarPosition));
        }
    }

    private void DoUpdateLifeBarPosition()
    {
        if (_monsterLifeBar == null || !_monsterLifeBar.FollowMonster) return;

        // Skip position updates when life bar is hidden to avoid unnecessary calculations
        if (!_monsterLifeBar.Visible) return;

        DoUpdateLifeBarPositionForced();
    }

    private void DoUpdateLifeBarPositionForced()
    {
        if (_monsterLifeBar == null || !_monsterLifeBar.FollowMonster) return;

        // Direct position calculation 
        var camera = GetViewport().GetCamera2D();
        if (camera != null)
        {
            Vector2 screenPos = camera.GetScreenCenterPosition() + (GlobalPosition - camera.GlobalPosition) * camera.Zoom;
            _monsterLifeBar.GlobalPosition = screenPos + _monsterLifeBar.OffsetFromMonster;
        }
        else
        {
            _monsterLifeBar.GlobalPosition = GlobalPosition + _monsterLifeBar.OffsetFromMonster;
        }
        
        Log.Debug($"Life bar position updated to: {_monsterLifeBar.GlobalPosition} (Monster at: {GlobalPosition})", "Monster");
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
            
            // Delay showing life bar slightly to ensure position is correct
            GetTree().CreateTimer(0.1f).Timeout += () =>
            {
                if (_monsterLifeBar != null)
                {
                    // Update position right before showing the life bar
                    DoUpdateLifeBarPositionForced(); // Force position update even when hidden
                    _monsterLifeBar.Show();
                }
            };
            
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

        // Hide MonsterLifeBar when fading out
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Hide();
        }

        _currentTween = CreateTween();
        _currentTween.TweenProperty(_spriteNode, "modulate:a", 0.0f, 0.5f);
        _currentTween.TweenCallback(Callable.From(() =>
        {
            _spriteNode.Visible = false;
            // Life bar is already hidden, no need to update position when not visible
            Log.Debug("Monster fade out complete - fully hidden", "Monster");
        }));
    }

    public void PlayDamageEffect()
    {
        // Default damage effect - determine color based on current reality
        Color effectColor = CurrentReality == MonsterReality.CurrentReality ? Colors.Red : Colors.Purple;
        PlayDamageEffect(effectColor);
    }

    public void PlayDamageEffect(Color damageColor)
    {
        var targetSprite = _sprite ?? _spriteNode;
        if (targetSprite == null) return;

        var tween = CreateTween();
        tween.TweenProperty(targetSprite, "modulate", damageColor, 0.2f);
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

    public bool CanTakeDamage() 
    {
        bool result = EnableFollowerBehavior ? (_isFullyVisible && (_currentState == MonsterState.Following || _currentState == MonsterState.InitialWait)) : true;
        Log.Debug($"CanTakeDamage - EnableFollowerBehavior: {EnableFollowerBehavior}, IsFullyVisible: {_isFullyVisible}, CurrentState: {_currentState}, Result: {result}", "Monster");
        return result;
    }

    public MonsterLife GetLifeSystem() => _monsterLife;

    public MonsterReality GetCurrentReality() => CurrentReality;

    public bool TakePhysicalDamage(int damage = 1)
    {
        // Physical damage to red hearts only
        if (!CanTakeDamage() || _monsterLife == null) return false;
        
        bool damaged = _monsterLife.TakePhysicalDamage(damage);
        if (damaged)
        {
            PlayDamageEffect(Colors.Red); // Physical damage = red flash
            Log.Debug($"Monster took {damage} physical damage", "Monster");
        }
        return damaged;
    }

    public bool TakeMagicalDamage(int damage = 1)
    {
        // Magical damage to any hearts
        if (!CanTakeDamage() || _monsterLife == null) return false;
        
        bool damaged = _monsterLife.TakeMagicalDamage(damage);
        if (damaged)
        {
            PlayDamageEffect(Colors.Purple); // Magical damage = purple flash
            Log.Debug($"Monster took {damage} magical damage", "Monster");
        }
        return damaged;
    }

    public bool TakeRockDamage(int damage = 1)
    {
        // Rock damage based on current reality
        Log.Debug($"TakeRockDamage called - CanTakeDamage: {CanTakeDamage()}, MonsterLife: {_monsterLife != null}, CurrentReality: {CurrentReality}", "Monster");
        
        if (!CanTakeDamage() || _monsterLife == null) 
        {
            Log.UI($"Rock damage blocked - CanTakeDamage: {CanTakeDamage()}, MonsterLife exists: {_monsterLife != null}", "Monster");
            return false;
        }
        
        bool damaged = false;
        
        switch (CurrentReality)
        {
            case MonsterReality.CurrentReality:
                // In current reality, rocks damage red hearts only
                Log.Debug($"Attempting physical damage - CurrentRedHearts: {_monsterLife.CurrentRedHearts}", "Monster");
                damaged = _monsterLife.TakePhysicalDamage(damage);
                if (damaged)
                {
                    Log.UI($"Rock hit monster in Current Reality - damaged red hearts", "Monster");
                }
                else
                {
                    Log.UI($"Rock hit monster in Current Reality but no red hearts to damage", "Monster");
                }
                break;
                
            case MonsterReality.PurpleGemReality:
                // In purple gem reality, rocks damage purple hearts only
                damaged = _monsterLife.TakePurpleHeartDamage(damage);
                if (damaged)
                {
                    Log.UI($"Rock hit monster in Purple Gem Reality - damaged purple hearts", "Monster");
                    
                    // Check if all purple hearts are lost (monster should die)
                    if (_monsterLife.CurrentPurpleHearts <= 0)
                    {
                        Log.UI($"Monster died - all purple hearts lost in Purple Gem Reality", "Monster");
                        // The MonsterLife system will handle the death signal
                    }
                }
                else
                {
                    Log.UI($"Rock hit monster in Purple Gem Reality but no purple hearts to damage", "Monster");
                }
                break;
        }
        
        if (damaged)
        {
            // Use appropriate color based on which reality and what type of damage was dealt
            Color damageColor = CurrentReality == MonsterReality.CurrentReality ? Colors.Red : Colors.Purple;
            PlayDamageEffect(damageColor);
            Log.Debug($"Monster took {damage} rock damage in {CurrentReality} reality", "Monster");
        }
        
        return damaged;
    }

    public void SetReality(MonsterReality reality)
    {
        CurrentReality = reality;
        Log.Debug($"Monster reality changed to {reality}", "Monster");
    }

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

        // Show MonsterLifeBar when resetting
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Show();
        }

        SetCollisionMask(0u);
        SetCollisionLayer(0u);

        // Reset MonsterLife
        _monsterLife?.Reset();

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