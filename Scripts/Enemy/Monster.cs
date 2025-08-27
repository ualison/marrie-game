using Godot;
using MarrieGame.Core.Enums;

// Unified Monster class supporting both simple and follower behaviors
public partial class Monster : CharacterBody2D
{
    #region Exported Properties
    [Export] public int DamageAmount { get; set; } = 1;
    [Export] public bool InitiallyVisible { get; set; } = true;
    // Note: Reality is now managed globally by RealityManager, not per-monster
    
    // Follower behavior properties
    [Export] public bool EnableFollowerBehavior { get; set; } = false;
    [Export] public float FollowDistance { get; set; } = 150.0f; // Target distance to maintain behind player
    [Export] public float MinSpawnDistance { get; set; } = 200.0f; // Initial spawn distance from player
    [Export] public float MinimumFollowDistance { get; set; } = 80.0f; // Don't move if closer than this to player
    [Export] public float WalkSpeed { get; set; } = 40.0f;
    [Export] public float InitialWaitTime { get; set; } = 7.0f;
    [Export] public float MinVisibleTime { get; set; } = 30.0f;
    [Export] public float MaxVisibleTime { get; set; } = 90.0f;
    [Export] public float MinHiddenTime { get; set; } = 10.0f;
    [Export] public float MaxHiddenTime { get; set; } = 45.0f;
    
    public bool IsDisabledForReplay { get; private set; } = false;
    #endregion

    #region Monster State (used by both simple and follower monsters)
    private MonsterState _currentState = MonsterState.Visible;
    private bool _isTransitioning = false; // Track if currently fading in/out
    private bool _isVisible = true;  // Initialize consistently with _currentState
    private bool _isFullyVisible = true;  // Initialize consistently with _currentState
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
            // Debug level - component discovery success is routine
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

        // Set collision layers/masks to only detect players (layer 1)
        _damageArea.CollisionLayer = 0; // Don't collide with anything as a layer
        _damageArea.CollisionMask = 1; // Only detect objects on layer 1 (players)

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
        
        // Initialize simple monsters based on InitiallyVisible property
        if (InitiallyVisible)
        {
            _currentState = MonsterState.Visible;
            _isVisible = true;
            _isFullyVisible = true;
            Show();
            // Show MonsterLifeBar when visible
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
            _currentState = MonsterState.Hidden;
            _isVisible = false;
            _isFullyVisible = false;
            Hide();
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
        _currentState = MonsterState.Visible;
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
                _currentState = MonsterState.Visible;
                StartVisibleBehavior();
            }
        };
        
        Log.Debug("Monster enabled after replay - will resume normal behavior in 3 seconds", "Monster");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!EnableFollowerBehavior) return;
        if (IsDisabledForReplay) return;
        
        if (_player?.GetLifeSystem()?.IsDead != false) return;

        // Always apply gravity and physics when monster exists
        ApplyGravity(delta);

        if (_currentState == MonsterState.Visible)
        {
            if (_isFullyVisible)
            {
                UpdateMovement();
            }
            else
            {
                Velocity = new Vector2(0, Velocity.Y);
            }
        }
        else // MonsterState.Hidden
        {
            // Don't move horizontally when hidden, but still fall with gravity
            Velocity = new Vector2(0, Velocity.Y);
        }

        MoveAndSlide();
    }

    private void ApplyGravity(double delta)
    {
        if (!IsOnFloor())
            Velocity = new Vector2(Velocity.X, Velocity.Y + _gravity * (float)delta);
    }

    private void UpdateMovement()
    {
        if (_player == null) return;
        
        float distanceToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
        
        // If too close to player, don't move toward them
        if (distanceToPlayer <= MinimumFollowDistance)
        {
            Velocity = new Vector2(0, Velocity.Y);
            return;
        }
        
        // Normal following behavior - maintain FollowDistance behind player
        Vector2 targetPos = _player.GlobalPosition - new Vector2(FollowDistance, 0);
        float distanceToTarget = GlobalPosition.DistanceTo(targetPos);

        if (distanceToTarget > 10.0f)
        {
            Vector2 direction = (targetPos - GlobalPosition).Normalized();
            Velocity = new Vector2(direction.X * WalkSpeed, Velocity.Y);
            UpdateSpriteDirection();
        }
        else
        {
            // Stop moving horizontally but keep current velocity for smoother stopping
            Velocity = new Vector2(0, Velocity.Y);
        }
    }

    private void UpdateSpriteDirection()
    {
        if (_spriteNode == null || _player == null) return;

        bool shouldFaceLeft = _player.GlobalPosition.X > GlobalPosition.X;
        var scale = _spriteNode.Scale;
        
        // Only update scale if direction actually changed to avoid unnecessary updates
        bool currentlyFacingLeft = scale.X < 0;
        if (currentlyFacingLeft != shouldFaceLeft)
        {
            _spriteNode.Scale = new Vector2(shouldFaceLeft ? -Mathf.Abs(scale.X) : Mathf.Abs(scale.X), scale.Y);
        }
    }
    #endregion

    #region State Management
    private void StartInitialWait()
    {
        // Initial wait is now just a visible state with no movement
        _currentState = MonsterState.Visible;
        _isVisible = true;
        _isFullyVisible = true;
        _isTransitioning = false;

        GlobalPosition = _initialPosition;
        Velocity = Vector2.Zero;

        if (_spriteNode != null)
        {
            _spriteNode.Visible = true;
            _spriteNode.Modulate = Colors.White;
        }

        // Show MonsterLifeBar when visible
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Show();
        }

        // Always keep ground collision enabled (layer 0), disable player collision during initial wait
        SetCollisionMask(1u); // Bit 0 = ground collision
        SetCollisionLayer(2u); // Put monster on layer 2 to avoid player collision during wait

        FacePlayer();

        _stateTimer.WaitTime = InitialWaitTime;
        _stateTimer.Start();
    }

    private void StartVisibleBehavior()
    {
        _currentState = MonsterState.Visible;
        _isVisible = true;
        _isFullyVisible = false;
        _isTransitioning = true;

        // Hide life bar before repositioning to prevent showing at old position
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Hide();
        }

        PositionBehindPlayerOnGround();
    // Keep ground collision enabled but stay on layer 2 (avoid player collisions)
    SetCollisionMask(1u);
    SetCollisionLayer(2u);
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

    private void StartHiddenBehavior()
    {
        _currentState = MonsterState.Hidden;
        _isVisible = false;
        _isFullyVisible = false;
        _isTransitioning = true;

        FadeOut();
        PlayAudio(false);

        float hiddenTime = GD.Randf() * (MaxHiddenTime - MinHiddenTime) + MinHiddenTime;
        _stateTimer.WaitTime = hiddenTime;
        _stateTimer.Start();
    }

    private void SwitchState()
    {
        // Check if this is the first state switch from initial visible state
        if (_stateTimer.WaitTime == InitialWaitTime && _currentState == MonsterState.Visible)
        {
            StartFirstTransition();
            return;
        }
        
        if (_currentState == MonsterState.Visible)
        {
            // Don't hide if monster is too close to player - extend visible time instead
            if (_player != null)
            {
                float distanceToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
                if (distanceToPlayer <= MinimumFollowDistance)
                {
                    // Monster is too close to player, extend visible time instead of hiding
                    float extendedVisibleTime = GD.Randf() * (MaxVisibleTime - MinVisibleTime) + MinVisibleTime;
                    _stateTimer.WaitTime = extendedVisibleTime;
                    _stateTimer.Start();
                    Log.Debug($"Monster too close to player ({distanceToPlayer:F1} <= {MinimumFollowDistance}), extending visible time", "Monster");
                    return;
                }
            }
            StartHiddenBehavior();
        }
        else // MonsterState.Hidden
        {
            StartVisibleBehavior();
        }
    }

    private void StartFirstTransition()
    {
        // First transition: go from initial visible state to hidden, then visible again
        StartHiddenBehavior();
        
        // After a short delay, reposition and become visible
        GetTree().CreateTimer(0.5f).Timeout += () =>
        {
            if (_currentState == MonsterState.Hidden)
            {
                StartVisibleBehavior();
            }
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
        // Use MinSpawnDistance for initial positioning, FollowDistance for regular following
        float spawnDistance = (_stateTimer.WaitTime == InitialWaitTime) ? MinSpawnDistance : FollowDistance;
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
        
        // Remove frequent debug logging that can cause performance issues
        // Log.Debug($"Life bar position updated to: {_monsterLifeBar.GlobalPosition} (Monster at: {GlobalPosition})", "Monster");
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
            // Now that fade-in finished, allow player interactions again
            _isFullyVisible = true;
            _isTransitioning = false; // No longer transitioning
            SetCollisionMask(1u);
            SetCollisionLayer(1u);
            
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

        // Keep ground collision but disable player interactions when hidden
        SetCollisionMask(1u); // Keep ground collision (bit 0)
        SetCollisionLayer(2u); // Move to layer 2 to avoid player interactions
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
            _isTransitioning = false; // No longer transitioning
            // Life bar is already hidden, no need to update position when not visible
            Log.Debug("Monster fade out complete - fully hidden", "Monster");
        }));
    }

    public void PlayDamageEffect()
    {
        // Default damage effect - determine color based on current reality
        Reality currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        Color effectColor = currentReality == Reality.CurrentReality ? Colors.Red : Colors.Purple;
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
    public new bool IsVisible() => _isVisible;
    public bool IsFullyVisible() => _isFullyVisible;
    public MonsterState GetState() => _currentState;
    public void ForceHide() 
    { 
        if (EnableFollowerBehavior) 
        { 
            _stateTimer.Stop(); 
            StartHiddenBehavior(); 
        }
        else
        {
            // For simple monsters, directly set to hidden state
            _currentState = MonsterState.Hidden;
            _isVisible = false;
            _isFullyVisible = false;
            Hide();
            if (_monsterLifeBar != null)
            {
                _monsterLifeBar.Hide();
            }
        }
    }
    
    public void ForceShow() 
    { 
        if (EnableFollowerBehavior) 
        { 
            _stateTimer.Stop(); 
            StartVisibleBehavior(); 
        }
        else
        {
            // For simple monsters, directly set to visible state
            _currentState = MonsterState.Visible;
            _isVisible = true;
            _isFullyVisible = true;
            Show();
            if (_monsterLifeBar != null)
            {
                _monsterLifeBar.Show();
            }
        }
    }

    public bool CanTakeDamage() 
    {
        // Use state-based logic consistently for both follower and simple monsters
        bool result = _currentState == MonsterState.Visible && !_isTransitioning;
        return result;
    }

    public MonsterLife GetLifeSystem() => _monsterLife;

    public Reality GetCurrentReality() => RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;

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
        Reality currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        Log.Debug($"TakeRockDamage called - CanTakeDamage: {CanTakeDamage()}, MonsterLife: {_monsterLife != null}, CurrentReality: {currentReality}", "Monster");
        
        if (!CanTakeDamage() || _monsterLife == null) 
        {
            Log.UI($"Rock damage blocked - CanTakeDamage: {CanTakeDamage()}, MonsterLife exists: {_monsterLife != null}", "Monster");
            return false;
        }
        
        bool damaged = false;
        
        switch (currentReality)
        {
            case Reality.CurrentReality:
                // In current reality, rocks damage red hearts only
                Log.Debug($"Attempting physical damage - CurrentRedHearts: {_monsterLife.CurrentRedHearts}", "Monster");
                damaged = _monsterLife.TakePhysicalDamage(damage);
                if (damaged)
                {
            // Info level - combat feedback is important for balance testing
            Log.Info($"Rock hit monster in Current Reality - damaged red hearts", "Monster");
                }
                else
                {
                // Warning level - indicates potential balance issue
                Log.Warning($"Rock hit monster in Current Reality but no red hearts to damage", "Monster");
                }
                break;
                
            case Reality.PastReality:
                // In past reality, rocks damage purple hearts only
                damaged = _monsterLife.TakePurpleHeartDamage(damage);
                if (damaged)
                {
                // Info level - important purple gem reality feedback
                Log.Info($"Rock hit monster in Purple Gem Reality - damaged purple hearts", "Monster");
                    
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
            Color damageColor = currentReality == Reality.CurrentReality ? Colors.Red : Colors.Purple;
            PlayDamageEffect(damageColor);
            Log.Debug($"Monster took {damage} rock damage in {currentReality} reality", "Monster");
        }
        
        return damaged;
    }

    public void SetReality(Reality reality)
    {
        // Reality is now managed globally - this method is deprecated
        Log.Warning($"SetReality called on monster - reality should be managed globally by RealityManager", "Monster");
    }

    public void Reset()
    {
        // Reset logic for both follower and regular monsters
        if (EnableFollowerBehavior)
        {
            _stateTimer?.Stop();
            _currentTween?.Kill();

            GlobalPosition = _initialPosition;
            Velocity = Vector2.Zero;

            _currentState = MonsterState.Visible;
            _isVisible = true;
            _isFullyVisible = true;
            _isTransitioning = false;

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

            // Keep ground collision during reset
            SetCollisionMask(1u); // Ground collision
            SetCollisionLayer(2u); // Layer 2 during reset

            // Reset MonsterLife
            _monsterLife?.Reset();

            PlayAudio(false);
            FacePlayer();

            _stateTimer.WaitTime = InitialWaitTime;
            _stateTimer.Start();

            Log.Debug($"Monster reset to initial position {_initialPosition}", "Monster");
        }
        else
        {
            // Regular monster reset logic
            GlobalPosition = _initialPosition;
            Velocity = Vector2.Zero;
            _currentState = MonsterState.Visible;
            _isVisible = true;
            _isFullyVisible = true;
            _isTransitioning = false;

            if (_spriteNode != null)
            {
                _spriteNode.Visible = true;
                _spriteNode.Modulate = Colors.White;
            }

            if (_monsterLifeBar != null)
            {
                _monsterLifeBar.Show();
            }

            SetCollisionMask(1u);
            SetCollisionLayer(2u);
            _monsterLife?.Reset();
            PlayAudio(false);
            Log.Debug($"Regular monster reset to initial position {_initialPosition}", "Monster");
        }
    }

    private void OnPlayerEntered(Node2D body)
    {
        // Only process player collisions - ignore ground and other objects
        if (body is not Player player)
        {
            return; // Don't log non-player collisions to reduce spam
        }

        Log.Debug($"Monster {Name} collision with Player {body.Name} - IsGhost: {body.IsInGroup("ghost_player") || body.IsInGroup("ghost_monster")}", "Monster");

        if (!player.GetLifeSystem()?.IsDead == true)
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