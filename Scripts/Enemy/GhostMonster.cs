using Godot;
using System.Collections.Generic;
using GodotCollections = Godot.Collections;
using MarrieGame.Core.Enums;

// Ghost version of monster that replays recorded movements and interactions - Performance Optimized
public partial class GhostMonster : CharacterBody2D
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
    // Monster life for the ghost so UI and damage work like real Monster
    private MonsterLife _monsterLife;
    private MonsterLifeBar _monsterLifeBar;
    #endregion

    #region Performance Cache
    private bool _hasAnimationFrames = false;
    private float _lastAlpha = -1f; // Cache to avoid unnecessary updates
    private bool _lastFlipState = false;
    private string _lastAnimation = "";
    private Vector2 _lastPosition = Vector2.Zero;
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

        // Setup a MonsterLife and LifeBar so ghost displays health like a real monster
        SetupLifeSystem();

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
            // Connect using Godot signals to receive the raw emitted value
            RealityManager.Instance.Connect("RealityChanged", new Callable(this, nameof(OnRealityChangedSignal)));
            _realityConnected = true;
            Log.Debug("GhostMonster: Successfully connected to RealityManager signals", "GhostMonster");
        }
        else
        {
            Log.Warning("GhostMonster: RealityManager not available for subscription", "GhostMonster");
        }
    }

    // Wrapper for the Godot signal which emits an int; convert it to the Reality enum and forward
    private void OnRealityChangedSignal(object rawValue)
    {
        try
        {
            int intVal = -1;

            // Godot may send signal args as an object array or Godot.Collections.Array
            if (rawValue is object[] objArr && objArr.Length > 0)
            {
                intVal = System.Convert.ToInt32(objArr[0]);
            }
            else if (rawValue is GodotCollections.Array gdArr && gdArr.Count > 0)
            {
                intVal = System.Convert.ToInt32(gdArr[0]);
            }
            else
            {
                intVal = System.Convert.ToInt32(rawValue);
            }

            var reality = (MarrieGame.Core.Enums.Reality)intVal;
            OnRealityChanged(reality);
        }
        catch (System.Exception ex)
        {
            Log.Warning($"GhostMonster: Received RealityChanged signal with unexpected value - {ex.Message}", "GhostMonster");
        }
    }

    private void OnRealityChanged(Reality newReality)
    {
        Log.Debug($"GhostMonster: Reality changed to {newReality}", "GhostMonster");
        ApplyRealityToLifeBar(newReality);
    }

    private void ApplyRealityToLifeBar(Reality reality)
    {
        if (_monsterLifeBar == null)
        {
            Log.Warning("GhostMonster: Cannot apply reality - life bar is null", "GhostMonster");
            return;
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
        if (_hasAnimationFrames && _lastAnimation != frame.Animation)
        {
            if (!string.IsNullOrEmpty(frame.Animation))
            {
                _animatedSprite?.Play(frame.Animation);
                _lastAnimation = frame.Animation;
            }
        }
    }

    private void HandleStateBasedBehavior(ReplayFrame frame)
    {
        // Extract state information from frame
        string newMonsterState = frame.MonsterState;
        bool newIsFullyVisible = frame.IsFullyVisible;
        float finalAlpha = frame.Alpha * GHOST_TINT.A;

        // Detect state transitions
        bool stateChanged = newMonsterState != _currentMonsterState;
        bool visibilityChanged = newIsFullyVisible != _isCurrentlyFullyVisible;

        if (stateChanged || visibilityChanged)
        {
            HandleStateTransition(newMonsterState, newIsFullyVisible, frame);
        }

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

    private void HandleStateTransition(string newState, bool newIsFullyVisible, ReplayFrame frame)
    {
        // State transition handling without excessive logging
        // Logic for handling transitions can be added here if needed
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
        _lastPosition = Vector2.Zero;

        // Reset state tracking
        _currentFadeTween?.Kill();
        _isCurrentlyFullyVisible = true;
        _currentMonsterState = "";

        // FIXED: Start at first frame position and immediately apply first frame
        var firstFrame = frames[0];
        GlobalPosition = firstFrame.Position;
        _currentPosition = GlobalPosition;
        _targetPosition = GlobalPosition;
        _lastPosition = GlobalPosition;

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
            Log.Debug("Ghost monster faded out and destroyed", "GhostMonster");
            CleanupLifeBar();
            QueueFree();
        }));
    }

    public bool IsReplaying() => _isReplaying;
    #endregion

    #region Visual Setup - CACHED - FIXED PATHS
    private void SetupGhostVisuals()
    {
        DiscoverSpriteComponents();
        CacheAnimationCapability();
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

        // Try to find AnimatedSprite2D first (for animated monsters)
        _animatedSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (_animatedSprite == null)
        {
            _animatedSprite = GetNodeOrNull<AnimatedSprite2D>("GhostMonsterSprite");
        }
        if (_animatedSprite == null)
        {
            _animatedSprite = FindDescendantOfType<AnimatedSprite2D>(this);
        }

        if (_animatedSprite != null)
        {
            _activeSprite = _animatedSprite;
            Log.Debug($"GhostMonster: Found AnimatedSprite2D: {_animatedSprite.Name}", "GhostMonster");
            return;
        }

        // Try to find Sprite2D (this should match "GhostMonsterSprite" from the .tscn)
        _sprite = GetNodeOrNull<Sprite2D>("GhostMonsterSprite");
        if (_sprite == null)
        {
            _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        }
        if (_sprite == null)
        {
            _sprite = GetNodeOrNull<Sprite2D>("MonsterSprite"); // fallback for other monster types
        }
        if (_sprite == null)
        {
            _sprite = FindDescendantOfType<Sprite2D>(this);
        }

        if (_sprite != null)
        {
            _activeSprite = _sprite;
            Log.Debug($"GhostMonster: Found Sprite2D: {_sprite.Name}", "GhostMonster");
        }
        else
        {
            Log.Warning("GhostMonster: No sprite found, creating fallback", "GhostMonster");
            Log.Warning($"GhostMonster: Searched paths: GhostMonsterSprite, AnimatedSprite2D, Sprite2D, MonsterSprite, recursive search", "GhostMonster");
            CreateFallbackVisual();
        }
    }

    private void CacheAnimationCapability()
    {
        // Cache whether we have animation capability to avoid repeated checks
        _hasAnimationFrames = _animatedSprite != null && _animatedSprite.SpriteFrames != null;
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
        SetCollisionLayerValue(1, false);
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

    private void SetupLifeSystem()
    {
        // Debug logging for life system discovery
        Log.Debug($"GhostMonster: Setting up life system on {Name}", "GhostMonster");

        // FIXED: Since the script is attached to CharacterBody2D, search directly in this node
        // The life components should be direct children of this CharacterBody2D

        // Look for the correct MonsterLife node
        _monsterLife = GetNodeOrNull<MonsterLife>("GhostMonsterLife");
        if (_monsterLife == null)
        {
            _monsterLife = GetNodeOrNull<MonsterLife>("MonsterLife");
        }
        if (_monsterLife == null)
        {
            _monsterLife = FindDescendantOfType<MonsterLife>(this);
        }

        if (_monsterLife == null)
        {
            Log.Debug("GhostMonster: GhostMonsterLife not found in scene, creating independent MonsterLife", "GhostMonster");
            // Create independent MonsterLife for ghost - completely separate from Monster
            _monsterLife = new MonsterLife { Name = "GhostMonsterLife" };
            AddChild(_monsterLife);
            Log.Debug("GhostMonster: Created independent MonsterLife for ghost", "GhostMonster");
        }
        else
        {
            Log.Debug($"GhostMonster: Using existing GhostMonsterLife component at: {_monsterLife.GetPath()}", "GhostMonster");
        }

        // Look for the correct MonsterLifeBar node
        _monsterLifeBar = GetNodeOrNull<MonsterLifeBar>("GhostMonsterLifeBar");
        if (_monsterLifeBar == null)
        {
            _monsterLifeBar = GetNodeOrNull<MonsterLifeBar>("MonsterLifeBar");
        }
        if (_monsterLifeBar == null)
        {
            _monsterLifeBar = FindDescendantOfType<MonsterLifeBar>(this);
        }

        if (_monsterLifeBar != null)
        {
            Log.Debug($"GhostMonster: Found existing GhostMonsterLifeBar at: {_monsterLifeBar.GetPath()}", "GhostMonster");
            // Use the existing life bar from the scene
            _monsterLifeBar.AutoDiscover = false;
            _monsterLifeBar.ConnectToLifeSystem(_monsterLife);
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
                    GetTree().CurrentScene.AddChild(lifeBarInstance);
                    lifeBarInstance.AutoDiscover = false;
                    lifeBarInstance.ConnectToLifeSystem(_monsterLife);
                    lifeBarInstance.SetFollowMonster(true);
                    lifeBarInstance.SetRequiredReality(Reality.PastReality);
                    lifeBarInstance.SetMonsterReference(this);
                    float initialAlphaForBar = GHOST_TINT.A;
                    var mbModTemp = lifeBarInstance.Modulate;
                    lifeBarInstance.Modulate = new Color(mbModTemp.R, mbModTemp.G, mbModTemp.B, initialAlphaForBar);
                    lifeBarInstance.Hide();
                    _monsterLifeBar = lifeBarInstance;
                    Log.Debug("GhostMonster: Created independent MonsterLifeBar instance", "GhostMonster");
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
        base._ExitTree();
    }

    // FIXED: Helper method for recursive node search - now properly implemented
    private T FindDescendantOfType<T>(Node parent) where T : Node
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is T found)
                return found;

            var descendant = FindDescendantOfType<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }

    // Allow external systems (like rocks) to apply rock damage to ghost
    public bool TakeRockDamage(int damage = 1)
    {
        // Only purple heart damage should be consumed when in PastReality
        var currentReality = RealityManager.Instance?.CurrentReality ?? MarrieGame.Core.Enums.Reality.CurrentReality;
        if (currentReality == MarrieGame.Core.Enums.Reality.PastReality)
        {
            bool damaged = _monsterLife?.TakePurpleHeartDamage(damage) ?? false;
            if (damaged) PlayDamageEffect(Colors.Purple);
            return damaged;
        }

        // In current reality, treat like physical damage (red hearts)
        bool damagedCurrent = _monsterLife?.TakePhysicalDamage(damage) ?? false;
        if (damagedCurrent) PlayDamageEffect(Colors.Red);
        return damagedCurrent;
    }

    public void PlayDamageEffect(Color damageColor)
    {
        if (_activeSprite != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_activeSprite, "modulate", damageColor, 0.15f);
            tween.TweenProperty(_activeSprite, "modulate", GHOST_TINT, 0.25f);
        }
    }

    // Compatibility API to match Monster's public interface
    public MonsterLife GetLifeSystem() => _monsterLife;

    public bool TakePhysicalDamage(int damage = 1)
    {
        if (_monsterLife == null) return false;
        bool damaged = _monsterLife.TakePhysicalDamage(damage);
        if (damaged) PlayDamageEffect(Colors.Red);
        return damaged;
    }

    public bool TakeMagicalDamage(int damage = 1)
    {
        if (_monsterLife == null) return false;
        bool damaged = _monsterLife.TakeMagicalDamage(damage);
        if (damaged) PlayDamageEffect(Colors.Purple);
        return damaged;
    }

    public void Reset()
    {
        _monsterLife?.Reset();
    }
    #endregion
}