using Godot;
using System.Collections.Generic;

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
        Log.Debug($"GhostMonster ready: {Name}", "GhostMonster");
    }

    public void _on_ghost_setup_complete(Vector2 position)
    {
        // Called after the ghost is added to scene and sprites are ready
        GlobalPosition = position;
        AddToGroup("ghost_monster");
        
        // Refresh sprite discovery since factory may have added sprites after _Ready
        SetupGhostVisuals();
        
        Log.Debug($"Ghost monster setup complete at position {position}", "GhostMonster");
        Log.Debug($"Ghost monster children count: {GetChildCount()}, Active sprite: {_activeSprite?.Name ?? "null"}", "GhostMonster");
        
        // Force visibility for debugging
        Show();
        if (_activeSprite != null)
        {
            _activeSprite.Show();
            Log.Debug($"Ghost monster sprite shown: {_activeSprite.Name}, Visible: {_activeSprite.Visible}, Modulate: {_activeSprite.Modulate}", "GhostMonster");
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
    #endregion

    #region Optimized Frame Processing
    private ReplayFrame? FindFrameAtTimeOptimized(float time)
    {
        // Optimized: Don't search from beginning every time
        // Most of the time we just need the next frame
        if (_currentFrameIndex < _replayFrames.Count && _replayFrames[_currentFrameIndex].Timestamp <= time)
        {
            // Fast forward to correct frame
            while (_currentFrameIndex < _replayFrames.Count - 1 && _replayFrames[_currentFrameIndex + 1].Timestamp <= time)
            {
                _currentFrameIndex++;
            }
            return _replayFrames[_currentFrameIndex];
        }

        return _currentFrameIndex < _replayFrames.Count ? _replayFrames[_currentFrameIndex] : null;
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
            // Log position changes during transitions for debugging
            float distance = _currentPosition.DistanceTo(frame.Position);
            if (distance > 50f) // Log significant position changes
            {
                Log.Debug($"GhostMonster: Large position change during transition - Distance: {distance:F1}, From: {_currentPosition} To: {frame.Position}", "GhostMonster");
            }
            
            HandleStateTransition(newMonsterState, newIsFullyVisible, frame);
        }

        // Update current alpha
        if (Mathf.Abs(_lastAlpha - finalAlpha) > ALPHA_TOLERANCE)
        {
            _activeSprite.Visible = frame.Alpha > 0.0f;
            _activeSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, finalAlpha);
            _lastAlpha = finalAlpha;
        }

        // Update state tracking
        _currentMonsterState = newMonsterState;
        _isCurrentlyFullyVisible = newIsFullyVisible;
    }

    private void HandleStateTransition(string newState, bool newIsFullyVisible, ReplayFrame frame)
    {
        // Log state transitions for debugging
        if (!newIsFullyVisible && _isCurrentlyFullyVisible)
        {
            Log.Debug($"GhostMonster: Starting fade transition - State: {newState} (movement will slow down)", "GhostMonster");
        }
        else if (newIsFullyVisible && !_isCurrentlyFullyVisible)
        {
            Log.Debug($"GhostMonster: Ending fade transition - State: {newState} (resuming normal movement)", "GhostMonster");
        }
    }

    #endregion

    #region Replay Control
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
        }

        // Apply first frame immediately to show correct initial state
        ApplyFrameToGhostOptimized(firstFrame);

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
            QueueFree();
            return;
        }

        var tween = CreateTween();
        tween.TweenProperty(_activeSprite, "modulate:a", 0.0f, 0.5f);
        tween.TweenCallback(Callable.From(() =>
        {
            Log.Debug("Ghost monster faded out and destroyed", "GhostMonster");
            QueueFree();
        }));
    }

    public bool IsReplaying() => _isReplaying;
    #endregion

    #region Visual Setup - CACHED
    private void SetupGhostVisuals()
    {
        DiscoverSpriteComponents();
        CacheAnimationCapability();
        ApplyGhostEffect();
    }

    private void DiscoverSpriteComponents()
    {
        _animatedSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (_animatedSprite != null)
        {
            _activeSprite = _animatedSprite;
            Log.Debug("GhostMonster: Found AnimatedSprite2D", "GhostMonster");
            return;
        }

        _sprite = GetNodeOrNull<Sprite2D>("MonsterSprite")
               ?? GetNodeOrNull<Sprite2D>("GhostMonsterSprite")
               ?? GetNodeOrNull<Sprite2D>("Sprite2D");

        if (_sprite != null)
        {
            _activeSprite = _sprite;
            Log.Debug($"GhostMonster: Found sprite: {_sprite.Name}", "GhostMonster");
        }
        else
        {
            Log.Warning("GhostMonster: No sprite found, creating fallback", "GhostMonster");
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
    #endregion
}