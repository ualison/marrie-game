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
    [Export] public NodePath AnimatedSpritePath;
    [Export] public NodePath SpritePath;
    [Export] public NodePath MonsterLifePath;
    [Export] public NodePath MonsterLifeBarPath;
    [Export] public NodePath FootstepAudioPath;
    [Export] public NodePath DamageAreaPath;

    private Player _player;
    private MonsterLife _monsterLife;
    private MonsterLifeBar _monsterLifeBar;
    private Timer _stateTimer;
    private AudioStreamPlayer2D _footstepAudio;
    private Sprite2D _sprite;
    private Node2D _spriteNode;
    private Area2D _damageArea;
    private Tween _currentTween;
    private Camera2D _cachedCameraForLifeBar;
    private Vector2 _lastLifeBarMonsterPos = new Vector2(float.NaN, float.NaN);
    private Vector2 _lastLifeBarCameraPos = new Vector2(float.NaN, float.NaN);
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
        // Ensure we respond to global reality changes so the monster's life bar
        // is only visible in CurrentReality and hidden during PastReality.
        CallDeferred(nameof(EnsureRealitySubscription));

        // Configure the MonsterLifeBar for CurrentReality
        var lifeBar = GetNodeOrNull<MonsterLifeBar>("MonsterLifeBar");
        if (lifeBar != null)
        {
            lifeBar.SetRequiredReality(Reality.CurrentReality);
            lifeBar.SetMonsterReference(this);
            Log.Debug($"Monster life bar configured for CurrentReality at position: {GlobalPosition}", "Monster");
        }
        else
        {
            Log.Warning("MonsterLifeBar not found in Monster scene", "Monster");
        }

    // Cache camera for life bar updates to avoid repeated GetViewport() calls
    _cachedCameraForLifeBar = GetViewport().GetCamera2D();
    }

    private bool _realityConnected = false;

    private void EnsureRealitySubscription()
    {
        if (_realityConnected) return;

        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
            _realityConnected = true;
            // Immediately apply current reality state to life bar
            ApplyRealityToLifeBar(RealityManager.Instance.CurrentReality);
        }
        else
        {
            // Try again shortly after (deferred) if RealityManager isn't ready yet
            CallDeferred(nameof(EnsureRealitySubscription));
        }
    }

    // Note: OnRealityChanged(Reality) will be invoked directly via RealityChangedEvent (typed C# event)

    private void OnRealityChanged(Reality newReality)
    {
        ApplyRealityToLifeBar(newReality);
    }

    private void ApplyRealityToLifeBar(Reality reality)
    {
        if (_monsterLifeBar == null) return;

        // CORRECTED: Monster life bar should be visible in CurrentReality, hidden in PastReality
        if (reality == Reality.CurrentReality)
        {
            // Show and ensure life bar follows the monster in current reality
            _monsterLifeBar.ConnectToLifeSystem(_monsterLife);
            _monsterLifeBar.SetFollowMonster(true);
            _monsterLifeBar.Show();
            // Ensure fully visible
            var mbMod = _monsterLifeBar.Modulate;
            _monsterLifeBar.Modulate = new Color(mbMod.R, mbMod.G, mbMod.B, 1.0f);

            // Force immediate position update when enabling following
            CallDeferred(nameof(UpdateLifeBarPosition));
        }
        else if (reality == Reality.PastReality)
        {
            // FIXED: Hide and disconnect the life bar during past reality to avoid conflict with ghost
            try { _monsterLifeBar.ConnectToLifeSystem(null); } catch { }
            _monsterLifeBar.Hide();
            _monsterLifeBar.SetFollowMonster(false);
            var mbMod = _monsterLifeBar.Modulate;
            _monsterLifeBar.Modulate = new Color(mbMod.R, mbMod.G, mbMod.B, 0.0f);
        }
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
        // Prefer NodePath-configured nodes when provided
        if (AnimatedSpritePath != null && AnimatedSpritePath.ToString() != "")
        {
            var animatedSprite = GetNodeOrNull<AnimatedSprite2D>(AnimatedSpritePath);
            if (animatedSprite != null)
            {
                _spriteNode = animatedSprite;
                _sprite = null;
                return;
            }
        }

        if (SpritePath != null && SpritePath.ToString() != "")
        {
            _sprite = GetNodeOrNull<Sprite2D>(SpritePath);
            _spriteNode = _sprite;
            if (_spriteNode != null) return;
        }

        // Fallback: Try AnimatedSprite2D first, then Sprite2D
        var animatedSpriteFallback = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animatedSpriteFallback != null)
        {
            _spriteNode = animatedSpriteFallback;
            _sprite = null;
        }
        else
        {
            _sprite = GetNodeOrNull<Sprite2D>("MonsterSprite");
            _spriteNode = _sprite;
        }

        if (_spriteNode == null)
            Log.Error("Monster: No sprite component found!", "Monster");
    }

    private void FindMonsterLife()
    {
        // Prefer configured path
        if (MonsterLifePath != null && MonsterLifePath.ToString() != "")
            _monsterLife = GetNodeOrNull<MonsterLife>(MonsterLifePath);

        _monsterLife ??= GetNodeOrNull<MonsterLife>("MonsterLife");

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
        // CRITICAL: Only use the Monster's own life bar, never search globally
        // Prefer configured NodePath when available
        if (MonsterLifeBarPath != null && MonsterLifeBarPath.ToString() != "")
            _monsterLifeBar = GetNodeOrNull<MonsterLifeBar>(MonsterLifeBarPath);

        if (_monsterLifeBar == null)
        {
            // Try direct child lookup with common names
            foreach (Node child in GetChildren())
            {
                if (child is MonsterLifeBar lifeBar)
                {
                    _monsterLifeBar = lifeBar;
                    break;
                }
            }
        }

        if (_monsterLifeBar == null)
        {
            Log.Warning("Monster: No MonsterLifeBar component found as direct child!", "Monster");
        }
        else
        {
            // Ensure this life bar is dedicated to this monster
            _monsterLifeBar.AutoDiscover = false;
            _monsterLifeBar.ConnectToLifeSystem(_monsterLife);
            _monsterLifeBar.SetFollowMonster(true);
            _monsterLifeBar.SetRequiredReality(Reality.CurrentReality);
            Log.Debug("Monster: MonsterLifeBar component found and connected", "Monster");
        }
    }

    private void SetupAudio()
    {
        if (!EnableFollowerBehavior) return;

        if (FootstepAudioPath != null && FootstepAudioPath.ToString() != "")
            _footstepAudio = GetNodeOrNull<AudioStreamPlayer2D>(FootstepAudioPath);

        _footstepAudio ??= GetNodeOrNull<AudioStreamPlayer2D>("FootstepAudio");
        if (_footstepAudio == null)
        {
            _footstepAudio = new AudioStreamPlayer2D { Name = "FootstepAudio" };
            AddChild(_footstepAudio);
        }
    }

    private void SetupDamageArea()
    {
        if (DamageAreaPath != null && DamageAreaPath.ToString() != "")
            _damageArea = GetNodeOrNull<Area2D>(DamageAreaPath);

        _damageArea ??= GetNodeOrNull<Area2D>("DamageArea");

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

    if (_player?.LifeSystem?.IsDead != false) return;

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

    // Movement methods moved to Monster.Movement.cs for readability
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

        // Direct position calculation using the same logic as MonsterLifeBar.cs
        var camera = _cachedCameraForLifeBar ?? GetViewport().GetCamera2D();
        Vector2 monsterPos = GlobalPosition;
        Vector2 cameraPos = camera != null ? camera.GlobalPosition : new Vector2(float.NaN, float.NaN);

        // Skip update if nothing moved
        bool monsterMoved = float.IsNaN(_lastLifeBarMonsterPos.X) || monsterPos != _lastLifeBarMonsterPos;
        bool cameraMoved = float.IsNaN(_lastLifeBarCameraPos.X) || cameraPos != _lastLifeBarCameraPos;
        if (!monsterMoved && !cameraMoved) return;

        Vector2 targetPosition;
        if (camera != null)
        {
            Vector2 screenPos = camera.GetScreenCenterPosition() + (monsterPos - camera.GlobalPosition) * camera.Zoom;
            targetPosition = screenPos + _monsterLifeBar.OffsetFromMonster;
        }
        else
        {
            targetPosition = monsterPos + _monsterLifeBar.OffsetFromMonster;
        }

        _monsterLifeBar.GlobalPosition = targetPosition;
        _lastLifeBarMonsterPos = monsterPos;
        _lastLifeBarCameraPos = cameraPos;
    }
    #endregion

    // Visual and audio helper methods moved to Monster.Visuals.cs for readability

    #region Public API
    // Use a descriptive property to avoid confusion with Node.Visible / Godot API
    public bool IsCurrentlyVisible => _isVisible;
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

    // Backwards-compatible getter
    public MonsterLife GetLifeSystem() => _monsterLife;
    // Idiomatic property access
    public MonsterLife LifeSystem => _monsterLife;

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
        Reality currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        Log.Debug($"TakeRockDamage called - CanTakeDamage: {CanTakeDamage()}, MonsterLife: {_monsterLife != null}, CurrentReality: {currentReality}", "Monster");

        // For debugging first-hit issues, log pre-state of life and invulnerability
        if (_monsterLife != null)
        {
            Log.System($"[Debug] PreDamage - CurrentLife: {_monsterLife.CurrentLife}, Red:{_monsterLife.CurrentRedHearts}, Purple:{_monsterLife.CurrentPurpleHearts}, IsInvulnerable: {_monsterLife.IsInvulnerable}", "Monster");
        }

        if (!CanTakeDamage() || _monsterLife == null) return false;

        bool damaged = false;

        switch (currentReality)
        {
            case Reality.CurrentReality:
                // Rocks only damage red hearts in current reality
                damaged = _monsterLife.TakeRockDamageInCurrentReality(damage);
                if (damaged)
                {
                    PlayDamageEffect(Colors.Red);
                    Log.Info($"Rock hit Monster in Current Reality - damaged red hearts", "Monster");
                }
                else
                {
                    Log.Warning($"Rock hit Monster in Current Reality but no red hearts to damage - Red:{_monsterLife.CurrentRedHearts}, Purple:{_monsterLife.CurrentPurpleHearts}, IsInvulnerable:{_monsterLife.IsInvulnerable}", "Monster");
                }
                break;

            case Reality.PastReality:
                // Rocks only damage purple hearts in past reality
                damaged = _monsterLife.TakeRockDamageInPastReality(damage);
                if (damaged)
                {
                    PlayDamageEffect(Colors.Purple);
                    Log.Info($"Rock hit Monster in Past Reality - damaged purple hearts", "Monster");
                }
                else
                {
                    Log.Warning($"Rock hit Monster in Past Reality but no purple hearts to damage - Red:{_monsterLife.CurrentRedHearts}, Purple:{_monsterLife.CurrentPurpleHearts}, IsInvulnerable:{_monsterLife.IsInvulnerable}", "Monster");
                }
                break;
        }

        // Log post-damage state for debugging
        if (_monsterLife != null)
        {
            Log.System($"[Debug] PostDamage - CurrentLife: {_monsterLife.CurrentLife}, Red:{_monsterLife.CurrentRedHearts}, Purple:{_monsterLife.CurrentPurpleHearts}, IsInvulnerable: {_monsterLife.IsInvulnerable}, Damaged: {damaged}", "Monster");
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

        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.Debug))
        {
            Log.Debug($"Monster {Name} collision with Player {body.Name} - IsGhost: {body.IsInGroup("ghost_player") || body.IsInGroup("ghost_monster")}", "Monster");
        }

    if (!player.LifeSystem?.IsDead == true)
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