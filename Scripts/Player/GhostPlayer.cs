using Godot;
using System.Collections.Generic;

// Ghost version of player that replays recorded movements with smooth interpolation
public partial class GhostPlayer : CharacterBody2D
{
    #region Constants and Configuration
    private const float RECORD_INTERVAL = 0.1f; // Match ReplayRecorder interval
    private const float MIN_MOVEMENT = 0.5f; // Minimum distance to filter stationary frames
    private const float SMOOTH_SPEED = 10.0f; // Position interpolation speed
    private static readonly Color GHOST_TINT = new(0.5f, 0.5f, 1.0f, 0.7f); // Semi-transparent blue
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
        Hide();

        Log.Replay($"GhostPlayer ready: {Name}, Parent: {GetParent()?.Name}", "GhostPlayer");
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
                    frame.Animation,
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
        }
        else
        {
            Log.Warning("Cannot apply flip - _activeSprite is null", "GhostPlayer");
        }

        // Apply visibility state
        if (_activeSprite != null)
        {
            _activeSprite.Visible = frame.IsVisible;
            if (Mathf.Abs(frame.Alpha - _activeSprite.Modulate.A) > 0.01f)
            {
                var currentColor = _activeSprite.Modulate;
                _activeSprite.Modulate = new Color(currentColor.R, currentColor.G, currentColor.B, frame.Alpha * GHOST_TINT.A);
            }
        }

        // Play animation if available and valid
        if (_animatedSprite != null && !string.IsNullOrEmpty(frame.Animation))
        {
            if (_animatedSprite.SpriteFrames?.HasAnimation(frame.Animation) == true)
            {
                if (_animatedSprite.Animation != frame.Animation)
                {
                    _animatedSprite.Play(frame.Animation);
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
        // Try animated sprite first (most common)
        _animatedSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (_animatedSprite != null)
        {
            _activeSprite = _animatedSprite;
            return;
        }

        // Check for various sprite node names as fallback
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D")
            ?? GetNodeOrNull<Sprite2D>("GhostMarrieSprite")
            ?? GetNodeOrNull<Sprite2D>("MarrieSprite");

        if (_sprite != null)
        {
            _activeSprite = _sprite;
            return;
        }

        // Check if custom names are actually AnimatedSprite2D
        _animatedSprite = GetNodeOrNull<AnimatedSprite2D>("GhostMarrieSprite")
            ?? GetNodeOrNull<AnimatedSprite2D>("MarrieSprite");

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
        // Apply semi-transparent blue tint for ghost appearance
        if (_activeSprite != null)
        {
            _activeSprite.Modulate = GHOST_TINT;
            _activeSprite.Show();
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
        // Prevent ghost from interfering with physics
        SetCollisionLayerValue(1, false);
        SetCollisionMaskValue(1, false);
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
    #endregion
}