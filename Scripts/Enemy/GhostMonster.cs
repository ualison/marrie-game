using Godot;
using System.Collections.Generic;
using GodotCollections = Godot.Collections;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Utils;
using MarrieGame.Core.Interfaces;

// Ghost version of monster that replays recorded movements and interactions - Performance Optimized
public partial class GhostMonster : CharacterBody2D, IPoolable
{
    #region Constants and Configuration
    private static readonly Color GHOST_TINT = new(0.6f, 0.3f, 0.9f, 0.7f); // Purple gem-like color
    private const float ALPHA_TOLERANCE = 0.01f; // Avoid unnecessary updates
    private const float SMOOTH_SPEED = 10.0f; // Position interpolation speed for smooth movement
    #endregion

    #region State Variables
    private List<ReplayFrame> _replayFrames;
    private int _currentFrameIndex;
    private float _elapsedTime; // FIXED: Use delta-based timing for better sync
    private bool _isReplaying;
    private string _monsterType;
    #endregion

    #region Visual Components
    private AnimatedSprite2D _animatedSprite;
    private Sprite2D _sprite;
    private Node2D _activeSprite;
    // Monster/Ghost life for the ghost so UI and damage work like real Monster
    private MonsterLife _monsterLife;
    private GhostMonsterLife _ghostLife;
    private MonsterLifeBar _monsterLifeBar;
    private Area2D _damageArea;
    #endregion

    #region Performance Cache
    private bool _hasAnimationFrames => _animatedSprite?.SpriteFrames != null;
    private float _lastAlpha = -1f; // Cache to avoid unnecessary updates
    private bool _lastFlipState = false;
    private string _lastAnimation = "";
    private Tween _currentFadeTween; // Track fade transitions

    // State-based behavior (replaces alpha detection)
    private bool _isCurrentlyFullyVisible = true;
    private string _currentMonsterState = "";

    // Movement smoothing
    private Vector2 _currentPosition;
    private Vector2 _targetPosition;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        SetupGhostVisuals();
        DisableCollisions();
        Hide();
    }

    public void _on_ghost_setup_complete(Vector2 position)
    {
        // Called after the ghost is added to scene and sprites are ready
        GlobalPosition = position;
        AddToGroup("ghost_monster");

        // Refresh sprite discovery since factory may have added sprites after _Ready
        SetupGhostVisuals();

        // Force visibility for debugging
        Show();
        if (_activeSprite != null)
        {
            _activeSprite.Show();
        }

    // Setup a GhostMonsterLife (purple-heart only) and LifeBar so ghost displays health in PastReality
    SetupLifeSystem();

        // Setup damage area for player collision detection
        SetupDamageArea();

        // Connect to reality changes so life bar visibility and following behavior update
        // CRITICAL FIX: Connect to reality changes BEFORE checking current reality
        EnsureRealitySubscription();

        // CRITICAL FIX: Apply current reality state immediately after connection
        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            ApplyRealityToLifeBar(RealityManager.Instance.CurrentReality);
        }
    }
    private bool _realityConnected = false;

    private void EnsureRealitySubscription()
    {
        if (_realityConnected) return;

        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            // Subscribe to typed C# event for direct enum delivery
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
            _realityConnected = true;
            Log.Debug("GhostMonster: Successfully connected to RealityManager signals", "GhostMonster");
        }
        else
        {
            Log.Warning("GhostMonster: RealityManager not available for subscription", "GhostMonster");
        }
    }

    // OnRealityChanged(Reality) will be invoked directly via RealityChangedEvent (typed C# event)

    private void OnRealityChanged(Reality newReality)
    {
        // Ignore callbacks on disposed/queued instances without noisy warnings.
        if (!IsInstanceValid(this))
            return;

        ApplyRealityToLifeBar(newReality);
    }

    private void ApplyRealityToLifeBar(Reality reality)
    {
        // Guard against disposed ghosts
        if (!IsInstanceValid(this))
        {
            Log.Warning("GhostMonster: ApplyRealityToLifeBar called on invalid instance, ignoring", "GhostMonster");
            return;
        }

        if (_monsterLifeBar == null)
        {
            Log.Debug("GhostMonster: Life bar is null when applying reality - attempting to (re)initialize life system", "GhostMonster");
            // Try to (re)discover or create the life bar dynamically in case setup failed earlier
            try
            {
                // SetupLifeSystem is safe-guarded itself; call only if this instance is valid
                if (IsInstanceValid(this)) SetupLifeSystem();
            }
            catch (System.Exception ex)
            {
                Log.Warning($"GhostMonster: Exception while attempting SetupLifeSystem: {ex.Message}", "GhostMonster");
            }

            if (_monsterLifeBar == null)
            {
                Log.Warning("GhostMonster: Cannot apply reality - life bar is still null after reinitialization", "GhostMonster");
                return;
            }
        }

        Log.Debug($"GhostMonster: Applying reality {reality} to life bar", "GhostMonster");

        // CORRECTED: GhostMonster life bar should be visible in PastReality, hidden in CurrentReality
        if (reality == Reality.CurrentReality)
        {
            // Hide and disconnect life bar in current reality (when real monster is active)
            try
            {
                _monsterLifeBar.ConnectToLifeSystem(null); // disconnect from any life system
            }
            catch { }
            _monsterLifeBar.Hide();
            _monsterLifeBar.SetFollowMonster(false);
            // Ensure fully invisible
            var mbMod = _monsterLifeBar.Modulate;
            _monsterLifeBar.Modulate = new Color(mbMod.R, mbMod.G, mbMod.B, 0.0f);
            Log.Debug("GhostMonster: Life bar hidden in CurrentReality", "GhostMonster");
        }
        else if (reality == Reality.PastReality)
        {
            // Show and make life bar follow ghost during past reality replay
            _monsterLifeBar.ConnectToLifeSystem(_monsterLife);
            _monsterLifeBar.SetFollowMonster(true);
            _monsterLifeBar.SetMonsterReference(this); // CRITICAL FIX: Ensure it follows this ghost
            _monsterLifeBar.Show();
            // Ensure visible alpha matches ghost
            float targetAlpha = _activeSprite != null ? _activeSprite.Modulate.A : GHOST_TINT.A;
            var mbMod = _monsterLifeBar.Modulate;
            _monsterLifeBar.Modulate = new Color(mbMod.R, mbMod.G, mbMod.B, targetAlpha);
            Log.Debug($"GhostMonster: Life bar shown and following in PastReality with alpha {targetAlpha}", "GhostMonster");
        }
    }
    #endregion

    #region Core Update Loop - OPTIMIZED
    public override void _Process(double delta)
    {
        if (!_isReplaying || _replayFrames == null) return;

        // FIXED: Use delta-based timing for consistent synchronization
        _elapsedTime += (float)delta;
        var targetFrame = FindFrameAtTime(_elapsedTime);

        if (targetFrame.HasValue)
        {
            // Always update target position from replay data
            _targetPosition = targetFrame.Value.Position;
            ApplyFrameToGhostOptimized(targetFrame.Value);

            // FIXED: For immediate response, set position directly for first few frames
            if (_elapsedTime < 0.2f) // First 200ms for immediate response
            {
                _currentPosition = _targetPosition;
                GlobalPosition = _currentPosition;
            }
            else
            {
                // Handle movement based on state (like Monster with follower behavior)
                if (_isCurrentlyFullyVisible)
                {
                    // Normal smooth interpolation when fully visible
                    _currentPosition = _currentPosition.Lerp(_targetPosition, SMOOTH_SPEED * (float)delta);
                }
                else
                {
                    // During fade transitions, handle position updates more carefully
                    var distance = _currentPosition.DistanceTo(_targetPosition);

                    if (distance > 100f) // Large position change (repositioning during fade)
                    {
                        // Teleport during invisible phase (like Monster repositioning)
                        _currentPosition = _targetPosition;
                    }
                    else if (distance > 1.0f)
                    {
                        // Small adjustments - use slower movement during fades
                        _currentPosition = _currentPosition.Lerp(_targetPosition, SMOOTH_SPEED * 0.2f * (float)delta);
                    }
                }
                GlobalPosition = _currentPosition;
            }

            // Life bar positioning is now handled automatically by MonsterLifeBar._Process()
        }
        else if (_currentFrameIndex >= _replayFrames.Count)
        {
            _isReplaying = false;
            Log.Replay("GhostMonster: Replay completed", "GhostMonster");
            return;
        }
        else
        {
            // Continue movement even when no new frame is found
            if (_isCurrentlyFullyVisible)
            {
                _currentPosition = _currentPosition.Lerp(_targetPosition, SMOOTH_SPEED * (float)delta);
            }
            GlobalPosition = _currentPosition;
        }
    }
    #endregion

    #region Frame Processing
    private ReplayFrame? FindFrameAtTime(float time)
    {
        // Handle negative time (should show first frame immediately)
        if (time <= 0f && _replayFrames.Count > 0)
        {
            _currentFrameIndex = 0;
            return _replayFrames[0];
        }

        for (int i = _currentFrameIndex; i < _replayFrames.Count; i++)
        {
            if (_replayFrames[i].Timestamp >= time)
            {
                _currentFrameIndex = i;
                return _replayFrames[i];
            }
        }

        // If no frame found for current time, return the last frame we should have played
        if (_currentFrameIndex > 0 && _currentFrameIndex < _replayFrames.Count)
        {
            return _replayFrames[_currentFrameIndex];
        }

        return null;
    }

    private void ApplyFrameToGhostOptimized(ReplayFrame frame)
    {
        // Only update rotation if it actually changed
        if (Mathf.Abs(GlobalRotation - frame.Rotation) > 0.01f)
        {
            GlobalRotation = frame.Rotation;
        }

        if (_activeSprite != null)
        {
            // Use state-driven behavior instead of alpha detection
            HandleStateBasedBehavior(frame);

            // Only update sprite flip if it changed
            if (_lastFlipState != frame.SpriteFlip)
            {
                var currentScale = _activeSprite.Scale;
                _activeSprite.Scale = new Vector2(
                    frame.SpriteFlip ? -Mathf.Abs(currentScale.X) : Mathf.Abs(currentScale.X),
                    currentScale.Y
                );
                _lastFlipState = frame.SpriteFlip;
            }
        }

        // Only update animation if it changed and we have animation capability
        var animName = ReplayStringRegistry.GetAnimationName(frame.AnimationId);
        if (_hasAnimationFrames && _lastAnimation != animName)
        {
            if (!string.IsNullOrEmpty(animName))
            {
                _animatedSprite?.Play(animName);
                _lastAnimation = animName;
            }
        }
    }

    private void HandleStateBasedBehavior(ReplayFrame frame)
    {
    // Extract state information from frame (use compact ids)
    string newMonsterState = ReplayStringRegistry.GetMonsterStateName(frame.MonsterStateId);
    bool newIsFullyVisible = frame.IsFullyVisible;
        float finalAlpha = frame.Alpha * GHOST_TINT.A;

        // Update current alpha
        if (Mathf.Abs(_lastAlpha - finalAlpha) > ALPHA_TOLERANCE)
        {
            _activeSprite.Visible = frame.Alpha > 0.0f;
            _activeSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, finalAlpha);
            _lastAlpha = finalAlpha;

            // FIXED: Keep life bar alpha in sync with ghost when the life bar is active and following
            SynchronizeLifeBarAlpha(finalAlpha);
        }

        // Update state tracking
        _currentMonsterState = newMonsterState;
        _isCurrentlyFullyVisible = newIsFullyVisible;
    }

    private void SynchronizeLifeBarAlpha(float ghostAlpha)
    {
        if (_monsterLifeBar == null || !_monsterLifeBar.Visible || !_monsterLifeBar.FollowMonster)
            return;

        // Only update if the reality allows life bar visibility
        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        if (currentReality == Reality.PastReality)
        {
            var mbMod = _monsterLifeBar.Modulate;
            _monsterLifeBar.Modulate = new Color(mbMod.R, mbMod.G, mbMod.B, ghostAlpha);
        }
    }

    #endregion

    #region Replay Control
    // GhostMonster.cs StartReplay method with fixes
    public void StartReplay(List<ReplayFrame> frames, string monsterType = "Monster")
    {
        if (frames == null || frames.Count == 0)
        {
            Log.Warning("GhostMonster: No frames to replay", "GhostMonster");
            return;
        }

        _replayFrames = frames;
        _monsterType = monsterType;
        _currentFrameIndex = 0;
        _elapsedTime = 0f; // FIXED: Start from zero with delta-based timing
        _isReplaying = true;

        // Reset performance cache
        _lastAlpha = -1f;
        _lastFlipState = false;
        _lastAnimation = "";

        // Reset state tracking
        _currentFadeTween?.Kill();
        _isCurrentlyFullyVisible = true;
        _currentMonsterState = "";

        // FIXED: Start at first frame position and immediately apply first frame
        var firstFrame = frames[0];
        GlobalPosition = firstFrame.Position;
        _currentPosition = GlobalPosition;
        _targetPosition = GlobalPosition;

        // FIXED: Show the ghost immediately with proper initial state instead of hidden
        Show();
        if (_activeSprite != null)
        {
            _activeSprite.Visible = true;
            // Start with first frame's alpha instead of 0 to eliminate delay
            var initialAlpha = firstFrame.Alpha * GHOST_TINT.A;
            _activeSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, initialAlpha);

            // FIXED: Synchronize life bar with initial alpha
            SynchronizeLifeBarAlpha(initialAlpha);
        }

        // Apply first frame immediately to show correct initial state
        ApplyFrameToGhostOptimized(firstFrame);

        // CRITICAL: Ensure life bar follows this ghost during replay
        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        if (currentReality == Reality.PastReality && _monsterLifeBar != null && _monsterLifeBar.FollowMonster)
        {
            // Ensure the life bar is properly configured to follow this ghost
            _monsterLifeBar.SetMonsterReference(this);
        }

        Log.Replay($"GhostMonster: Started optimized replay with {frames.Count} frames - immediate start at {firstFrame.Position}", "GhostMonster");
    }


    public void StopReplay()
    {
        _isReplaying = false;
        _currentFadeTween?.Kill();
        _isCurrentlyFullyVisible = true;
        _currentMonsterState = "";
        Log.Replay("GhostMonster: Replay stopped", "GhostMonster");
    }

    public void FadeOutAndDestroy()
    {
        if (_activeSprite == null)
        {
            // Clean up life bar before destroying
            CleanupLifeBar();
            QueueFree();
            return;
        }

        var tween = CreateTween();
        tween.TweenProperty(_activeSprite, "modulate:a", 0.0f, 0.5f);
        if (_monsterLifeBar != null)
        {
            tween.TweenProperty(_monsterLifeBar, "modulate:a", 0.0f, 0.5f);
        }
        tween.TweenCallback(Callable.From(() =>
        {
            Log.Debug("Ghost monster faded out and released/destroyed", "GhostMonster");
            CleanupLifeBar();
            if (IsFromPool)
                PoolManager.Release(this);
            else
                QueueFree();
        }));
    }

    public bool IsReplaying() => _isReplaying;
    #endregion

    #region Visual Setup - CACHED - FIXED PATHS
    private void SetupGhostVisuals()
    {
        DiscoverSpriteComponents();
        ApplyGhostEffect();
    }

    private void DiscoverSpriteComponents()
    {
        // Debug: Log the scene structure to understand the hierarchy
        Log.Debug($"GhostMonster: Script attached to: {Name} (Type: {GetType().Name})", "GhostMonster");
        Log.Debug($"GhostMonster: Parent: {GetParent()?.Name} (Type: {GetParent()?.GetType().Name})", "GhostMonster");
        Log.Debug($"GhostMonster: Children count: {GetChildCount()}", "GhostMonster");

        // List all children for debugging
        for (int i = 0; i < GetChildCount(); i++)
        {
            var child = GetChild(i);
            Log.Debug($"GhostMonster: Child {i}: {child.Name} (Type: {child.GetType().Name})", "GhostMonster");
        }

        // FIXED: Since the script is attached to CharacterBody2D, search directly in this node
        // The sprite should be a direct child of this CharacterBody2D

        // Use centralized helper to discover a sprite node (animated or static)
        var found = NodeHelpers.FindSpriteNode(this, null);
        if (found is AnimatedSprite2D as2d)
        {
            _animatedSprite = as2d;
            _activeSprite = _animatedSprite;
            Log.Debug($"GhostMonster: Found AnimatedSprite2D: {_animatedSprite.Name}", "GhostMonster");
            return;
        }

        if (found is Sprite2D s2d)
        {
            _sprite = s2d;
            _activeSprite = _sprite;
            Log.Debug($"GhostMonster: Found Sprite2D: {_sprite.Name}", "GhostMonster");
        }
        else
        {
            Log.Warning("GhostMonster: No sprite found via NodeHelpers, creating fallback", "GhostMonster");
            CreateFallbackVisual();
        }
    }

    private void ApplyGhostEffect()
    {
        if (_activeSprite != null)
        {
            _activeSprite.Modulate = GHOST_TINT;
            _activeSprite.Show();
        }
    }

    private void CreateFallbackVisual()
    {
        var fallbackSprite = new Sprite2D { Name = "FallbackGhostSprite" };

        var imageTexture = new ImageTexture();
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(new Color(1, 0, 0));
        imageTexture.SetImage(image);

        fallbackSprite.Texture = imageTexture;
        fallbackSprite.Modulate = GHOST_TINT;

        AddChild(fallbackSprite);
        _activeSprite = fallbackSprite;
    }

    private void DisableCollisions()
    {
        SetCollisionLayerValue(1, true);
        SetCollisionMaskValue(1, false);

        var damageArea = GetNodeOrNull<Area2D>("DamageArea");
        if (damageArea != null)
        {
            damageArea.SetCollisionLayerValue(1, false);
            damageArea.SetCollisionMaskValue(1, false);
            damageArea.Monitoring = false;
            damageArea.Monitorable = false;
        }

        foreach (Node child in GetChildren())
        {
            if (child is Area2D area)
            {
                area.Monitoring = false;
                area.Monitorable = false;
                area.SetCollisionLayerValue(1, false);
                area.SetCollisionMaskValue(1, false);
            }
        }
    }

    // Setup DamageArea for player collision detection (similar to Monster.cs)
    private void SetupDamageArea()
    {
        _damageArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "DamageArea");
        if (_damageArea == null)
        {
            CreateDamageArea();
        }
        try
        {
            var callable = new Callable(this, nameof(OnPlayerEntered));
            if (!_damageArea.IsConnected("body_entered", callable))
                _damageArea.BodyEntered += OnPlayerEntered;
        }
        catch
        {
            try { _damageArea.BodyEntered += OnPlayerEntered; } catch { }
        }

        // Ensure damage area is on the player detection layer and actively monitoring
        try
        {
            _damageArea.CollisionLayer = 1; // player layer
            _damageArea.CollisionMask = 1;  // detect player
                try { _damageArea.SetDeferred("monitoring", true); } catch { _damageArea.Monitoring = true; }
                try { _damageArea.SetDeferred("monitorable", true); } catch { _damageArea.Monitorable = true; }
            // Immediately check for overlapping players in case the player is
            // already inside the area when the ghost becomes active. Godot
            // doesn't emit body_entered for an area that was disabled while a
            // body was overlapping, so we must manually detect and handle it.
            // Defer overlap handling until monitoring is guaranteed to be active
            CallDeferred(nameof(HandleGhostDamageAreaEnable));
        }
        catch { }
    }

    private void CreateDamageArea()
    {
        _damageArea = new Area2D { Name = "DamageArea" };
        AddChild(_damageArea);

        // Set collision layers/masks to only detect players (layer 1)
        try
        {
            var playerNode = GetTree().GetFirstNodeInGroup("player") as PhysicsBody2D;
            if (playerNode != null)
                _damageArea.CollisionMask = playerNode.CollisionLayer;
            else
                _damageArea.CollisionMask = 2; // default player layer in prefabs
        }
        catch { _damageArea.CollisionMask = 2; }

        _damageArea.CollisionLayer = GetCollisionLayer();

        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = 25.0f };
        _damageArea.AddChild(shape);
    }

    private void OnPlayerEntered(Node2D body)
    {
        // Only process player collisions - ignore ground and other objects
        if (body is not Player player)
        {
            return;
        }
            bool isGhost = body.IsInGroup("ghost_player") || body.IsInGroup("ghost_monster");
            Log.Debug($"GhostMonster {Name} collision with Player {body.Name} - IsGhost: {isGhost}", "GhostMonster");

            // Only proceed if player is alive
            if (player.LifeSystem?.IsDead == true) return;

            Log.Debug($"GhostMonster {Name} attempting damage to player", "GhostMonster");
            if (player.TakeDamage(1)) // Default damage amount for ghost
            {
                PlayDamageEffect(Colors.Purple);
            }
    }

    private void SetupLifeSystem()
    {
        // Debug logging for life system discovery
        Log.Debug($"GhostMonster: Setting up life system on {Name}", "GhostMonster");

        // FIXED: Since the script is attached to CharacterBody2D, search directly in this node
        // The life components should be direct children of this CharacterBody2D

        // Look for the correct MonsterLife node using helper utilities
        // Prefer GhostMonsterLife if available (purple-heart only), fall back to MonsterLife for compatibility
        var ghostLife = NodeHelpers.GetNodeOrNull<GhostMonsterLife>(this, null, "GhostMonsterLife");
        if (ghostLife != null)
        {
            _ghostLife = ghostLife;
            _monsterLife = null;
            Log.Debug($"GhostMonster: Using existing GhostMonsterLife component at: {_ghostLife.GetPath()}", "GhostMonster");
        }
        else
        {
            // Try to find a MonsterLife for backward compatibility
            _monsterLife = NodeHelpers.GetNodeOrNull<MonsterLife>(this, null, "MonsterLife")
                           ?? NodeHelpers.FindChildOfType<MonsterLife>(this);

            if (_monsterLife != null)
            {
                Log.Debug($"GhostMonster: Using existing MonsterLife component at: {_monsterLife.GetPath()}", "GhostMonster");
            }
            else
            {
                // Create a GhostMonsterLife if nothing found
                Log.Debug("GhostMonster: GhostMonsterLife not found in scene, creating independent GhostMonsterLife", "GhostMonster");
                var created = new GhostMonsterLife { Name = "GhostMonsterLife" };
                AddChild(created);
                _ghostLife = created;
                Log.Debug("GhostMonster: Created independent GhostMonsterLife for ghost", "GhostMonster");
            }
        }

        // Look for the correct MonsterLifeBar node
        _monsterLifeBar = NodeHelpers.GetNodeOrNull<MonsterLifeBar>(this, null, "GhostMonsterLifeBar");
        if (_monsterLifeBar == null)
        {
            _monsterLifeBar = NodeHelpers.GetNodeOrNull<MonsterLifeBar>(this, null, "MonsterLifeBar");
        }
        if (_monsterLifeBar == null)
        {
            _monsterLifeBar = NodeHelpers.FindChildOfType<MonsterLifeBar>(this);
        }

        if (_monsterLifeBar != null)
        {
            Log.Debug($"GhostMonster: Found existing GhostMonsterLifeBar at: {_monsterLifeBar.GetPath()}", "GhostMonster");
            // Use the existing life bar from the scene
            _monsterLifeBar.AutoDiscover = false;
            // Connect to either MonsterLife or GhostMonsterLife (ConnectToLifeSystem accepts IHealthSystem)
            if ((object)_monsterLife != null)
                _monsterLifeBar.ConnectToLifeSystem(_monsterLife);
            else if ((object)_ghostLife != null)
                _monsterLifeBar.ConnectToLifeSystem(_ghostLife);
            _monsterLifeBar.SetFollowMonster(true);
            _monsterLifeBar.Hide(); // Start hidden
            _monsterLifeBar.SetRequiredReality(Reality.PastReality);

            // CRITICAL FIX: Set the monster reference to this GhostMonster
            _monsterLifeBar.SetMonsterReference(this);

            Log.Debug("GhostMonster: Successfully configured existing GhostMonsterLifeBar from scene", "GhostMonster");
        }
        else
        {
            Log.Debug("GhostMonster: GhostMonsterLifeBar not found, creating dynamic instance", "GhostMonster");
            // Create new life bar instance with unique name
            var lifeBarScene = GD.Load<PackedScene>("res://Prefabs/UI/MonsterLifeBar.tscn");
            if (lifeBarScene != null)
            {
                var lifeBarInstance = lifeBarScene.Instantiate() as MonsterLifeBar;
                if (lifeBarInstance != null)
                {
                    lifeBarInstance.Name = "DynamicGhostLifeBar"; // Unique name to avoid conflicts
                    // Ensure this ghost is still valid before adding global UI
                    if (IsInstanceValid(this) && GetTree() != null && GetTree().CurrentScene != null)
                    {
                        GetTree().CurrentScene.AddChild(lifeBarInstance);
                        lifeBarInstance.AutoDiscover = false;
                        try { lifeBarInstance.ConnectToLifeSystem((IHealthSystem)_monsterLife ?? _ghostLife); } catch { }
                        lifeBarInstance.SetFollowMonster(true);
                        lifeBarInstance.SetRequiredReality(Reality.PastReality);
                        try { lifeBarInstance.SetMonsterReference(this); } catch { }
                        float initialAlphaForBar = GHOST_TINT.A;
                        var mbModTemp = lifeBarInstance.Modulate;
                        lifeBarInstance.Modulate = new Color(mbModTemp.R, mbModTemp.G, mbModTemp.B, initialAlphaForBar);
                        lifeBarInstance.Hide();
                        _monsterLifeBar = lifeBarInstance;
                        Log.Debug("GhostMonster: Created independent MonsterLifeBar instance", "GhostMonster");
                    }
                    else
                    {
                        // Instance is no longer valid; free the created bar immediately to avoid leaks
                        try { lifeBarInstance.QueueFree(); } catch { }
                    }
                }
            }
            else
            {
                Log.Warning("GhostMonster: Failed to load MonsterLifeBar.tscn", "GhostMonster");
            }
        }
    }

    private void CleanupLifeBar()
    {
        // Clean up life bar - but only if it was dynamically created, not from scene
        if (_monsterLifeBar != null && IsInstanceValid(_monsterLifeBar))
        {
            try
            {
                _monsterLifeBar.ConnectToLifeSystem(null);
                _monsterLifeBar.SetFollowMonster(false);
            }
            catch { }

            // Only queue_free if it was dynamically created (not part of original scene)
            if (_monsterLifeBar.Name == "DynamicGhostLifeBar" && _monsterLifeBar.GetParent() == GetTree().CurrentScene)
            {
                _monsterLifeBar.QueueFree();
            }
            else
            {
                // Just hide scene-based life bar (GhostMonsterLifeBar from .tscn)
                _monsterLifeBar.Hide();
            }
            _monsterLifeBar = null;
        }
    }

    public override void _ExitTree()
    {
        // Ensure cleanup when ghost is removed from scene
        CleanupLifeBar();
        // Unsubscribe from RealityManager events to avoid callbacks to freed instances
        try
        {
            if (_realityConnected && RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
            {
                RealityManager.Instance.RealityChangedEvent -= OnRealityChanged;
            }
        }
        catch { }
        _realityConnected = false;

        base._ExitTree();
    }

    // Pooling support
    public bool IsFromPool { get; set; } = false;

    public void ResetForReuse()
    {
        // Stop any active replay and tweens
        try { StopReplay(); } catch { }
        _isReplaying = false;
        _replayFrames = null;
        _currentFrameIndex = 0;
        _elapsedTime = 0f;

        // Reset visual and state caches
        _isCurrentlyFullyVisible = true;
        _currentMonsterState = "";
        _lastAlpha = -1f;
        _lastFlipState = false;
        _lastAnimation = "";
        _currentFadeTween?.Kill();

        // Reset positions
        _currentPosition = Vector2.Zero;
        _targetPosition = Vector2.Zero;

        // Restore visibility and basic visual defaults
        Show();
        if (_activeSprite != null)
        {
            _activeSprite.Show();
            _activeSprite.Modulate = GHOST_TINT;
            _activeSprite.Scale = Vector2.One;
        }

        if (_animatedSprite != null)
        {
            try { _animatedSprite.Stop(); } catch { }
        }

        // Disable damage area while pooled
        if (_damageArea != null)
        {
            _damageArea.Monitoring = false;
            _damageArea.Monitorable = false;
        }

        // Reset lifebar reference but don't free scene-based bars here
        // CleanupLifeBar will be called when released to pool
    }

    public void OnReleasedToPool()
    {
        // Disconnect runtime signals
        try { if (_damageArea != null) _damageArea.BodyEntered -= OnPlayerEntered; } catch { }

        // Unsubscribe from RealityManager if connected
        try
        {
            if (_realityConnected && RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
            {
                RealityManager.Instance.RealityChangedEvent -= OnRealityChanged;
            }
        }
        catch { }
        _realityConnected = false;

        // Cleanup life bar (will QueueFree dynamic bars and hide scene bars)
        CleanupLifeBar();

        // Remove from groups to avoid stray callbacks while pooled
        try { RemoveFromGroup("ghost_monster"); } catch { }

        // Disable collisions to keep pooled node inert
        try { DisableCollisions(); } catch { }
    }

    // NOTE: Recursive descendant search replaced by NodeHelpers.FindChildOfType and other helpers.


    // Replace the existing TakeRockDamage method with this implementation
    public bool TakeRockDamage(int damage = 1)
    {
        Reality currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        Log.Debug($"GhostMonster.TakeRockDamage called - CurrentReality: {currentReality}", "GhostMonster");

        // Use centralized life pool for consistent damage handling
        var lifePool = EnemyManager.Instance?.GetLifePoolForGhost(this);
        if (lifePool != null)
        {
            bool damaged = lifePool.HandleGhostDamage(damage, currentReality);
            
            if (damaged)
            {
                Color effectColor = currentReality == Reality.CurrentReality ? Colors.Red : Colors.Purple;
                PlayDamageEffect(effectColor);
                Log.Info($"Rock hit GhostMonster in {currentReality} - damage handled by centralized pool", "GhostMonster");
            }
            else
            {
                Log.Warning($"Rock hit GhostMonster in {currentReality} but centralized pool refused damage", "GhostMonster");
            }
            
            return damaged;
        }
        
        // Fallback to local handling if no centralized pool
        if (_monsterLife == null) return false;

        bool localDamaged = false;

        switch (currentReality)
        {
            case Reality.CurrentReality:
                // Ghosts don't take damage in current reality (Monster handles that)
                Log.Warning($"Rock hit GhostMonster in Current Reality but ghosts shouldn't take damage in current reality", "GhostMonster");
                break;

            case Reality.PastReality:
                // Rocks only damage purple hearts in past reality for ghosts
                localDamaged = _monsterLife.TakeRockDamageInPastReality(damage);
                if (localDamaged)
                {
                    PlayDamageEffect(Colors.Purple);
                    Log.Info($"Rock hit GhostMonster in Past Reality - damaged purple hearts locally", "GhostMonster");
                }
                else
                {
                    Log.Warning($"Rock hit GhostMonster in Past Reality but no purple hearts to damage", "GhostMonster");
                }
                break;
        }

        return localDamaged;
    }
    // Add this new method to handle ghost-specific death behavior
    private void OnGhostDeath()
    {
        // Stop any ongoing replay
        StopReplay();

        // Trigger fade out and destruction
        FadeOutAndDestroy();

        // Notify any systems that need to know about ghost death
        Log.UI("GhostMonster has been destroyed by rock damage", "GhostMonster");
    }



    // Also update the existing PlayDamageEffect method to ensure it works properly
    public void PlayDamageEffect(Color damageColor)
    {
        if (_activeSprite == null) return;

        // Kill any existing damage effect tween (simple approach - kill current tween if it exists)
        _currentFadeTween?.Kill();

        // Create new damage effect
        var tween = CreateTween();
        tween.SetParallel(true); // Allow multiple property tweens

        // Flash to damage color then back to ghost tint
        tween.TweenProperty(_activeSprite, "modulate", damageColor, 0.15f);
        tween.TweenProperty(_activeSprite, "modulate", GHOST_TINT, 0.25f).SetDelay(0.15f);

        // Add slight scale effect for impact feedback
        var originalScale = _activeSprite.Scale;
        tween.TweenProperty(_activeSprite, "scale", originalScale * 1.1f, 0.1f);
        tween.TweenProperty(_activeSprite, "scale", originalScale, 0.2f).SetDelay(0.1f);

        // Store reference to current damage tween
        _currentFadeTween = tween;
    }

    private void HandleGhostDamageAreaEnable()
    {
        if (_damageArea == null) return;

        try
        {
            var overlapping = _damageArea.GetOverlappingBodies();
            foreach (var b in overlapping)
            {
                if (b is Node2D body && body.IsInGroup("player"))
                    OnPlayerEntered(body);
            }
        }
        catch { }

        try
        {
            var playerNode = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (playerNode != null)
            {
                float radius = 32.0f;
                try
                {
                    foreach (Node child in _damageArea.GetChildren())
                    {
                        if (child is CollisionShape2D cs && cs.Shape is CircleShape2D circle)
                        {
                            radius = circle.Radius;
                            break;
                        }
                    }
                }
                catch { }

                if (playerNode.GlobalPosition.DistanceTo(GlobalPosition) <= radius)
                    OnPlayerEntered(playerNode);
            }
        }
        catch { }
    }
    // Expose life system as property and keep compatibility method
    public MonsterLife LifeSystem => _monsterLife;
    public MonsterLife GetLifeSystem() => _monsterLife;

   
    // Enhanced TakePhysicalDamage for consistency with centralized system
    public bool TakePhysicalDamage(int damage = 1)
    {
        // Use centralized life pool if available
        var lifePool = EnemyManager.Instance?.GetLifePoolForGhost(this);
        if (lifePool != null)
        {
            var reality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
            bool damaged = lifePool.HandleGhostDamage(damage, reality);
            
            if (damaged)
            {
                PlayDamageEffect(Colors.Red);
                Log.Debug($"GhostMonster took {damage} physical damage via centralized pool", "GhostMonster");
            }
            
            return damaged;
        }
        
        // Fallback to local system
        if (_monsterLife == null) return false;

        Log.Debug($"GhostMonster TakePhysicalDamage - CurrentRedHearts: {_monsterLife.CurrentRedHearts}", "GhostMonster");
        bool localDamaged = _monsterLife.TakePhysicalDamage(damage);

        if (localDamaged)
        {
            PlayDamageEffect(Colors.Red);
            Log.Debug($"GhostMonster took {damage} physical damage locally", "GhostMonster");

            // Check for death
            if (_monsterLife.IsDead)
            {
                OnGhostDeath();
            }
        }

        return localDamaged;
    }

      // Enhanced TakeMagicalDamage for consistency with centralized system
    public bool TakeMagicalDamage(int damage = 1)
    {
        // Use centralized life pool if available
        var lifePool = EnemyManager.Instance?.GetLifePoolForGhost(this);
        if (lifePool != null)
        {
            var reality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
            bool damaged = lifePool.HandleGhostDamage(damage, reality);
            
            if (damaged)
            {
                PlayDamageEffect(Colors.Purple);
                Log.Debug($"GhostMonster took {damage} magical damage via centralized pool", "GhostMonster");
            }
            
            return damaged;
        }
        
        // Fallback to local system
        if (_monsterLife == null) return false;

        Log.Debug($"GhostMonster TakeMagicalDamage - CurrentLife: {_monsterLife.CurrentLife}", "GhostMonster");
        bool localDamaged = _monsterLife.TakeMagicalDamage(damage);

        if (localDamaged)
        {
            PlayDamageEffect(Colors.Purple);
            Log.Debug($"GhostMonster took {damage} magical damage locally", "GhostMonster");

            // Check for death
            if (_monsterLife.IsDead)
            {
                OnGhostDeath();
            }
        }

        return localDamaged;
    }
    #endregion
}