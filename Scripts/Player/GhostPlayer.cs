using Godot;
using System.Collections.Generic;
using MarrieGame.Core.Utils;

// Ghost version of player that replays recorded movements with smooth interpolation
public partial class GhostPlayer : CharacterBody2D, IPoolable
{
    #region Constants and Configuration
    private const float RECORD_INTERVAL = 0.1f; // Match ReplayRecorder interval
    private const float MIN_MOVEMENT = 0.5f; // Minimum distance to filter stationary frames
    private const float SMOOTH_SPEED = 10.0f; // Position interpolation speed
    private static readonly Color GHOST_TINT = new(0.6f, 0.3f, 0.9f, 0.7f); // Purple gem-like color
    #endregion

    #region State Variables
    private List<ReplayFrame> _replayFrames;
    private int _currentFrameIndex;
    private float _elapsedTime; // FIXED: Use delta-based timing for better sync
    private bool _isReplaying;
    #endregion

    #region Visual Components
    private AnimatedSprite2D _animatedSprite;
    private Sprite2D _sprite;
    private Node2D _activeSprite;
    #endregion

    #region Movement Smoothing
    private Vector2 _currentPosition;
    private Vector2 _targetPosition;
    #endregion

    #region Initialization
    public override void _Ready()
    {
    // Setup visual appearance and disable physics interactions
        SetupGhostVisuals();
        DisableCollisions();
        
        // Add to player group so PuzzleCrystals can detect it
        AddToGroup("player");
        AddToGroup("ghost_player");
        
        Hide();

        Log.Replay($"GhostPlayer ready: {Name}, Parent: {GetParent()?.Name}", "GhostPlayer");
    }

    public void _on_ghost_setup_complete(Vector2 position)
    {
        // Called after the ghost is added to scene and sprites are ready
        GlobalPosition = position;
        
        // Add to both player groups for detection by PuzzleCrystals and other systems
        AddToGroup("player");
        AddToGroup("ghost_player");
        
        // Refresh sprite discovery since factory may have added sprites after _Ready
        SetupGhostVisuals();
        
        Log.Replay($"Ghost setup complete at position {position}", "GhostPlayer");
        Log.Replay($"Ghost children count: {GetChildCount()}, Active sprite: {_activeSprite?.Name ?? "null"}", "GhostPlayer");
        
        // Force visibility for debugging
        Show();
        if (_activeSprite != null)
        {
            _activeSprite.Show();
            Log.Replay($"Ghost sprite shown: {_activeSprite.Name}, Visible: {_activeSprite.Visible}, Modulate: {_activeSprite.Modulate}", "GhostPlayer");
        }
    }
    #endregion

    #region Core Update Loop
    public override void _Process(double delta)
    {
        // Skip processing if not replaying or no data available
        if (!_isReplaying || _replayFrames == null) return;

        // FIXED: Use delta-based timing for consistent synchronization
        _elapsedTime += (float)delta;
        var targetFrame = FindFrameAtTime(_elapsedTime);

        if (targetFrame.HasValue)
        {
            // Update target position and apply frame data immediately
            _targetPosition = targetFrame.Value.Position;
            ApplyFrameToGhost(targetFrame.Value);
            
            // FIXED: For immediate response, set position directly for first few frames
            if (_elapsedTime < 0.2f) // First 200ms for immediate response
            {
                _currentPosition = _targetPosition;
                GlobalPosition = _currentPosition;
            }
            else
            {
                // Normal smooth interpolation after initial period
                _currentPosition = _currentPosition.Lerp(_targetPosition, SMOOTH_SPEED * (float)delta);
                GlobalPosition = _currentPosition;
            }
        }
        else if (_currentFrameIndex >= _replayFrames.Count)
        {
            // End replay when all frames are processed
            StopReplay();
            return;
        }
        else
        {
            // Continue smooth movement even when no new frame is found
            _currentPosition = _currentPosition.Lerp(_targetPosition, SMOOTH_SPEED * (float)delta);
            GlobalPosition = _currentPosition;
        }
    }
    #endregion

    #region Replay Control
    public void StartReplay(List<ReplayFrame> frames)
    {
        // Validate input data before starting replay
        if (frames == null || frames.Count == 0)
        {
            Log.Warning("No frames to replay", "GhostPlayer");
            return;
        }

        // FIXED: Use original frames to prevent aggressive filtering that causes delays
        // Only do minimal filtering to preserve immediate start
        var filteredFrames = FilterFramesMinimal(frames);
        if (filteredFrames.Count == 0)
        {
            Log.Warning("No movement frames after filtering", "GhostPlayer");
            return;
        }

        // Initialize replay state
        _replayFrames = filteredFrames;
        _currentFrameIndex = 0;
        _elapsedTime = 0f; // FIXED: Start from zero with delta-based timing
        _isReplaying = true;

        // FIXED: Set initial position and immediately apply first frame to eliminate delay
        var firstFrame = filteredFrames[0];
        GlobalPosition = firstFrame.Position;
        _currentPosition = GlobalPosition;
        _targetPosition = GlobalPosition;
        
        // Apply first frame immediately to show correct initial state
        ApplyFrameToGhost(firstFrame);

        Show();
        Log.Replay($"Started replay with {filteredFrames.Count} filtered frames (was {frames.Count}) - immediate start at {firstFrame.Position}", "GhostPlayer");
        Log.Replay($"Ghost visible after StartReplay: {Visible}, Active sprite: {_activeSprite?.Name ?? "null"}, Sprite visible: {_activeSprite?.Visible ?? false}", "GhostPlayer");
    }

    public void StopReplay()
    {
        // End replay and hide ghost
        _isReplaying = false;
        Hide();
        Log.Replay("Replay stopped", "GhostPlayer");
    }

    public bool IsReplaying() => _isReplaying; // Check if currently replaying
    #endregion

    #region Frame Processing
    private List<ReplayFrame> FilterFramesMinimal(List<ReplayFrame> originalFrames)
    {
        // FIXED: Optimized filtering to preserve immediate replay start with better performance
        // Only remove excessive stationary periods while keeping initial frames
        var filtered = new List<ReplayFrame>();
        
        if (originalFrames.Count == 0) return filtered;
        
        // Always keep first frame for immediate start
        filtered.Add(originalFrames[0]);
        
        Vector2 lastPos = originalFrames[0].Position;
        int stationaryCount = 0;
        const int MAX_STATIONARY_FRAMES = 5; // Only skip after 5 consecutive stationary frames
        const float MIN_MOVEMENT_SQUARED = MIN_MOVEMENT * MIN_MOVEMENT; // Avoid sqrt calculations
        
        for (int i = 1; i < originalFrames.Count; i++)
        {
            var frame = originalFrames[i];
            float distanceSquared = (frame.Position - lastPos).LengthSquared(); // PERFORMANCE: Use squared distance
            
            if (distanceSquared > MIN_MOVEMENT_SQUARED)
            {
                // Movement detected - reset timer and add frame
                stationaryCount = 0;
                filtered.Add(frame);
                lastPos = frame.Position;
            }
            else
            {
                // Stationary frame
                stationaryCount++;
                
                // Keep some stationary frames to preserve timing, skip only excessive ones
                if (stationaryCount <= MAX_STATIONARY_FRAMES || i == originalFrames.Count - 1)
                {
                    filtered.Add(frame);
                }
            }
        }
        
        // Ensure we always have at least the first and last frames
        if (filtered.Count > 0 && originalFrames.Count > 1)
        {
            var lastOriginal = originalFrames[originalFrames.Count - 1];
            var lastFiltered = filtered[filtered.Count - 1];
            if (lastOriginal.Timestamp != lastFiltered.Timestamp)
            {
                filtered.Add(lastOriginal);
            }
        }
        
#if DEBUG
        Log.Debug($"Optimized filtering: {originalFrames.Count} -> {filtered.Count} frames", "GhostPlayer");
#endif
        return filtered;
    }

    private List<ReplayFrame> FilterStatinaryFrames(List<ReplayFrame> originalFrames)
    {
        // DEPRECATED: This method was too aggressive and caused delays
        // Remove frames where player is stationary to compress timeline
        var filtered = new List<ReplayFrame>();
        Vector2 lastPos = Vector2.Zero;
        float timeCompression = 0f;

        for (int i = 0; i < originalFrames.Count; i++)
        {
            var frame = originalFrames[i];

            // Always keep first frame as reference point
            if (i == 0)
            {
                filtered.Add(frame);
                lastPos = frame.Position;
                continue;
            }

            float distance = frame.Position.DistanceTo(lastPos);

            if (distance > MIN_MOVEMENT)
            {
                // Movement detected - add frame with compressed timestamp
                var newFrame = new ReplayFrame(
                    frame.Timestamp - timeCompression,
                    frame.Position,
                    frame.Rotation,
                    frame.AnimationId,
                    frame.SpriteFlip,
                    frame.IsVisible,
                    frame.Alpha
                );
                filtered.Add(newFrame);
                lastPos = frame.Position;
            }
            else
            {
                // No movement - accumulate time compression
                timeCompression += RECORD_INTERVAL;
            }
        }

        return filtered;
    }

    private ReplayFrame? FindFrameAtTime(float time)
    {
        // FIXED: Improved frame finding to ensure immediate playback
        // Handle negative time (should show first frame immediately)
        if (time <= 0f && _replayFrames.Count > 0)
        {
            _currentFrameIndex = 0;
            return _replayFrames[0];
        }
        
        // Find frame that should be playing at current time
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

        // Return null when past all frames to signal end
        return null;
    }

    private void ApplyFrameToGhost(ReplayFrame frame)
    {
        // Apply rotation from frame data
        GlobalRotation = frame.Rotation;

        // DEBUG: Log flip state for debugging
        if (frame.SpriteFlip)
        {
            Log.Debug($"Applying flip to ghost - SpriteFlip: {frame.SpriteFlip}", "GhostPlayer");
        }

        // Apply sprite flip state with enhanced debugging
        if (_activeSprite != null)
        {
            var currentScale = _activeSprite.Scale;
            var newScaleX = frame.SpriteFlip ? -Mathf.Abs(currentScale.X) : Mathf.Abs(currentScale.X);
            
            // Always update scale to ensure consistency
            _activeSprite.Scale = new Vector2(newScaleX, currentScale.Y);
            
            // Also handle FlipH property if it's an AnimatedSprite2D
            if (_animatedSprite != null)
            {
                // Apply FlipH property directly for more accurate reproduction
                _animatedSprite.FlipH = frame.SpriteFlip;
            }

            // FIXED: Apply flip to ALL sprites in GhostMarrieSprite container with proper position mirroring
            var ghostContainer = GetNodeOrNull("GhostMarrieSprite");
            if (ghostContainer != null)
            {
                foreach (Node child in ghostContainer.GetChildren())
                {
                    if (child is Sprite2D sprite)
                    {
                        var spriteScale = sprite.Scale;
                        var spriteNewScaleX = frame.SpriteFlip ? -Mathf.Abs(spriteScale.X) : Mathf.Abs(spriteScale.X);
                        sprite.Scale = new Vector2(spriteNewScaleX, spriteScale.Y);

                        // FIXED: Mirror positions for body parts when flipping
                        ApplyPositionMirroringForBodyPart(sprite, frame.SpriteFlip);
                    }
                    else if (child is AnimatedSprite2D animSprite)
                    {
                        animSprite.FlipH = frame.SpriteFlip;
                        var animScale = animSprite.Scale;
                        var animNewScaleX = frame.SpriteFlip ? -Mathf.Abs(animScale.X) : Mathf.Abs(animScale.X);
                        animSprite.Scale = new Vector2(animNewScaleX, animScale.Y);

                        // FIXED: Mirror positions for body parts when flipping
                        ApplyPositionMirroringForBodyPart(animSprite, frame.SpriteFlip);
                    }
                }
                
                // DEBUG: Log applied flip state
                if (frame.SpriteFlip)
                {
                    Log.Debug($"Applied flip to all {ghostContainer.GetChildCount()} sprites in container", "GhostPlayer");
                }
            }
        }
        else
        {
            // Try to rediscover sprites if they're null - this might happen during initialization
            DiscoverSpriteComponents();
            if (_activeSprite == null)
            {
                Log.Warning("Cannot apply flip - _activeSprite is null even after rediscovery", "GhostPlayer");
                return; // Skip this frame if sprite is still not available
            }
            
            // Retry the flip operation now that we have a sprite
            var currentScale = _activeSprite.Scale;
            var newScaleX = frame.SpriteFlip ? -Mathf.Abs(currentScale.X) : Mathf.Abs(currentScale.X);
            _activeSprite.Scale = new Vector2(newScaleX, currentScale.Y);
            
            if (_animatedSprite != null)
            {
                _animatedSprite.FlipH = frame.SpriteFlip;
            }

            // FIXED: Also apply to container sprites after rediscovery
            var ghostContainer = GetNodeOrNull("GhostMarrieSprite");
            if (ghostContainer != null)
            {
                foreach (Node child in ghostContainer.GetChildren())
                {
                    if (child is Sprite2D sprite)
                    {
                        var spriteScale = sprite.Scale;
                        var spriteNewScaleX = frame.SpriteFlip ? -Mathf.Abs(spriteScale.X) : Mathf.Abs(spriteScale.X);
                        sprite.Scale = new Vector2(spriteNewScaleX, spriteScale.Y);

                        // FIXED: Mirror positions for body parts when flipping
                        ApplyPositionMirroringForBodyPart(sprite, frame.SpriteFlip);
                    }
                    else if (child is AnimatedSprite2D animSprite)
                    {
                        animSprite.FlipH = frame.SpriteFlip;
                        var animScale = animSprite.Scale;
                        var animNewScaleX = frame.SpriteFlip ? -Mathf.Abs(animScale.X) : Mathf.Abs(animScale.X);
                        animSprite.Scale = new Vector2(animNewScaleX, animScale.Y);

                        // FIXED: Mirror positions for body parts when flipping
                        ApplyPositionMirroringForBodyPart(animSprite, frame.SpriteFlip);
                    }
                }
            }
        }

        // Apply visibility state
        if (_activeSprite != null)
        {
            _activeSprite.Visible = frame.IsVisible;
            if (Mathf.Abs(frame.Alpha - _activeSprite.Modulate.A) > 0.01f)
            {
                // FIXED: Maintain purple ghost tint while applying frame alpha
                var newAlpha = frame.Alpha * GHOST_TINT.A;
                
                // Apply to the main active sprite
                _activeSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, newAlpha);
                
                // FIXED: Also apply alpha to all sprites in GhostMarrieSprite container
                var ghostContainer = GetNodeOrNull("GhostMarrieSprite");
                if (ghostContainer != null)
                {
                    foreach (Node child in ghostContainer.GetChildren())
                    {
                        if (child is Sprite2D sprite)
                        {
                            sprite.Visible = frame.IsVisible;
                            sprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, newAlpha);
                        }
                        else if (child is AnimatedSprite2D animSprite)
                        {
                            animSprite.Visible = frame.IsVisible;
                            animSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, newAlpha);
                        }
                    }
                }
            }
        }

        // Play animation if available and valid (use compact id -> name lookup)
        if (_animatedSprite != null)
        {
            var animName = ReplayStringRegistry.GetAnimationName(frame.AnimationId);
            if (!string.IsNullOrEmpty(animName) && _animatedSprite.SpriteFrames?.HasAnimation(animName) == true)
            {
                if (_animatedSprite.Animation != animName)
                {
                    _animatedSprite.Play(animName);
                }
            }
        }
    }
    #endregion

    #region Visual Setup
    private void SetupGhostVisuals()
    {
        // Find sprite components and apply ghost effect
        DiscoverSpriteComponents();
        ApplyGhostEffect();
    }

    private void DiscoverSpriteComponents()
    {
        // FIXED: First try to find AnimatedSprite2D components
        _animatedSprite = NodeHelpers.GetNodeOrNull<AnimatedSprite2D>(this, null, "AnimatedSprite2D");
        if (_animatedSprite != null)
        {
            _activeSprite = _animatedSprite;
            return;
        }

        // FIXED: Look for direct Sprite2D children first
        _sprite = NodeHelpers.GetNodeOrNull<Sprite2D>(this, null, "Sprite2D")
            ?? NodeHelpers.GetNodeOrNull<Sprite2D>(this, null, "MarrieSprite");

        if (_sprite != null)
        {
            _activeSprite = _sprite;
            return;
        }

        // FIXED: Look for GhostMarrieSprite container and find any Sprite2D inside it
        var ghostContainer = GetNodeOrNull("GhostMarrieSprite");
        if (ghostContainer != null)
        {
            // Find any Sprite2D component within the ghost container (MarrieStatic has multiple Sprite2D children)
            foreach (Node child in ghostContainer.GetChildren())
            {
                if (child is Sprite2D sprite2D)
                {
                    _sprite = sprite2D;
                    _activeSprite = _sprite;
                    Log.Replay($"Found sprite in GhostMarrieSprite container: {_sprite.Name}", "GhostPlayer");
                    return;
                }
            }

            // Also check for AnimatedSprite2D in the container
            foreach (Node child in ghostContainer.GetChildren())
            {
                if (child is AnimatedSprite2D animSprite)
                {
                    _animatedSprite = animSprite;
                    _activeSprite = _animatedSprite;
                    Log.Replay($"Found animated sprite in GhostMarrieSprite container: {_animatedSprite.Name}", "GhostPlayer");
                    return;
                }
            }
        }

        // FIXED: As fallback, try to find AnimatedSprite2D with common names
        _animatedSprite = NodeHelpers.GetNodeOrNull<AnimatedSprite2D>(this, null, "GhostMarrieSprite")
            ?? NodeHelpers.GetNodeOrNull<AnimatedSprite2D>(this, null, "MarrieSprite");

        if (_animatedSprite != null)
        {
            _activeSprite = _animatedSprite;
        }
        else
        {
            // Log error and show available children for debugging
            Log.Error("No sprite component found!", "GhostPlayer");
            LogAvailableChildren();
        }
    }

    private void ApplyGhostEffect()
    {
        // FIXED: Apply purple tint to all sprite components, not just the active one
        if (_activeSprite != null)
        {
            // Apply to the main active sprite
            _activeSprite.Modulate = GHOST_TINT;
            _activeSprite.Show();

            // FIXED: If we found a sprite inside GhostMarrieSprite container, 
            // apply the ghost effect to ALL sprites in the container for complete purple tint
            var ghostContainer = GetNodeOrNull("GhostMarrieSprite");
            if (ghostContainer != null)
            {
                foreach (Node child in ghostContainer.GetChildren())
                {
                    if (child is Sprite2D sprite)
                    {
                        sprite.Modulate = GHOST_TINT;
                        sprite.Show();

                        // FIXED: Ensure original positions are set correctly
                        ApplyPositionMirroringForBodyPart(sprite, false);
                    }
                    else if (child is AnimatedSprite2D animSprite)
                    {
                        animSprite.Modulate = GHOST_TINT;
                        animSprite.Show();

                        // FIXED: Ensure original positions are set correctly
                        ApplyPositionMirroringForBodyPart(animSprite, false);
                    }
                }
                Log.Replay($"Applied ghost effect to all sprites in GhostMarrieSprite container", "GhostPlayer");
            }
        }
        else
        {
            // Create fallback visual if no sprite found
            Log.Warning("No sprite found for ghost effect - creating fallback", "GhostPlayer");
            CreateFallbackVisual();
        }
    }

    private void CreateFallbackVisual()
    {
        // Create simple colored rectangle as fallback visual
        var fallbackSprite = new Sprite2D { Name = "FallbackGhostSprite" };

        // Generate blue rectangle texture
        var imageTexture = new ImageTexture();
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 1));
        imageTexture.SetImage(image);

        fallbackSprite.Texture = imageTexture;
        fallbackSprite.Modulate = GHOST_TINT;

        AddChild(fallbackSprite);
        _activeSprite = fallbackSprite;
    }

    private void DisableCollisions()
    {
        // Disable collision with world geometry (layer 1) but keep layer 2 
        // so PuzzleCrystals can still detect the ghost player
        SetCollisionLayerValue(1, false);  // Don't collide with world
        SetCollisionMaskValue(1, false);   // Don't detect world collisions
        
        // Keep collision layer 2 enabled for PuzzleCrystal detection
        // SetCollisionLayerValue(2, false);  // DON'T disable - PuzzleCrystals need this
    }

    // Pooling support
    public bool IsFromPool { get; set; } = false;

    public void ResetForReuse()
    {
        // Stop replay and tweens
        try { StopReplay(); } catch { }

        _isReplaying = false;
        _replayFrames = null;
        _currentFrameIndex = 0;
        _elapsedTime = 0f;

        // Reset visual caches
        if (_activeSprite != null)
        {
            _activeSprite.Show();
            _activeSprite.Modulate = GHOST_TINT;
            _activeSprite.Scale = Vector2.One;
        }
        if (_animatedSprite != null)
        {
            try { _animatedSprite.Stop(); } catch { }
            _animatedSprite.FlipH = false;
        }

        // FIXED: Reset all sprites in GhostMarrieSprite container
        var ghostContainer = GetNodeOrNull("GhostMarrieSprite");
        if (ghostContainer != null)
        {
            foreach (Node child in ghostContainer.GetChildren())
            {
                if (child is Sprite2D sprite)
                {
                    sprite.Show();
                    sprite.Modulate = GHOST_TINT;
                    // FIXED: Reset scale properly without flipping
                    var scale = sprite.Scale;
                    sprite.Scale = new Vector2(Mathf.Abs(scale.X), scale.Y);

                    // FIXED: Reset position to original
                    ApplyPositionMirroringForBodyPart(sprite, false);
                }
                else if (child is AnimatedSprite2D animSprite)
                {
                    try { animSprite.Stop(); } catch { }
                    animSprite.FlipH = false;
                    animSprite.Show();
                    animSprite.Modulate = GHOST_TINT;
                    // FIXED: Reset scale properly without flipping
                    var scale = animSprite.Scale;
                    animSprite.Scale = new Vector2(Mathf.Abs(scale.X), scale.Y);

                    // FIXED: Reset position to original
                    ApplyPositionMirroringForBodyPart(animSprite, false);
                }
            }
        }

        // Reset movement smoothing
        _currentPosition = Vector2.Zero;
        _targetPosition = Vector2.Zero;

        // Visibility defaults
        Show();

        // Disable collisions while pooled, but maintain group membership
        DisableCollisions();
        
        // Ensure it stays in both player groups for consistent detection
        if (!IsInGroup("player")) AddToGroup("player");
        if (!IsInGroup("ghost_player")) AddToGroup("ghost_player");
    }

    public void OnReleasedToPool()
    {
        // Final cleanup: remove any runtime signals or references
        try { RemoveFromGroup("ghost_player"); } catch { }
        try { RemoveFromGroup("player"); } catch { }

        // Hide and detach any heavy resources
        try { Hide(); } catch { }
    }
    #endregion

    #region Debug Utilities
    private void LogAvailableChildren()
    {
        // List all children for debugging sprite discovery issues
#if DEBUG
        Log.Debug("GhostPlayer children:", "GhostPlayer");
        foreach (Node child in GetChildren())
        {
            Log.Debug($"  - {child.Name} ({child.GetType().Name})", "GhostPlayer");
        }
#endif
    }

    // FIXED: Store original positions for proper mirroring
    private readonly Dictionary<string, Vector2> _originalBodyPartPositions = new()
    {
        { "RightHand", new Vector2(37.000004f, 33.000008f) },
        { "RightHandBasket", new Vector2(-49f, 68f) },
        { "LeftFoot", new Vector2(23.000002f, 171f) },
        { "RightFoot", new Vector2(-13f, 172f) },
        { "HairWithoutHead", new Vector2(-7f, 0f) },
        { "Skirt", new Vector2(-2f, 104.00001f) },
        { "Torso", new Vector2(0f, 0f) },
        { "FaceV2", new Vector2(-1.9999998f, -63f) }
    };

    private void ApplyPositionMirroringForBodyPart(Node2D sprite, bool isFlipped)
    {
        // FIXED: Apply proper position mirroring for body parts when flipping
        var spriteName = sprite.Name;
        
        if (_originalBodyPartPositions.TryGetValue(spriteName, out var originalPos))
        {
            if (isFlipped)
            {
                // Mirror the X position around the center (0,0)
                var mirroredX = -originalPos.X;
                sprite.Position = new Vector2(mirroredX, originalPos.Y);
            }
            else
            {
                // Restore original position
                sprite.Position = originalPos;
            }
        }
        // If it's not a known body part, don't change its position
    }
    #endregion
}