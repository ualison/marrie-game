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
    private float _replayStartTime;
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

        // Calculate elapsed time since replay started
        float elapsedTime = Time.GetTicksMsec() / 1000.0f - _replayStartTime;
        var targetFrame = FindFrameAtTime(elapsedTime);

        if (targetFrame.HasValue)
        {
            // Update target position and apply frame data
            _targetPosition = targetFrame.Value.Position;
            ApplyFrameToGhost(targetFrame.Value);
        }
        else if (_currentFrameIndex >= _replayFrames.Count)
        {
            // End replay when all frames are processed
            StopReplay();
            return;
        }

        // Smoothly interpolate to target position for fluid movement
        _currentPosition = _currentPosition.Lerp(_targetPosition, SMOOTH_SPEED * (float)delta);
        GlobalPosition = _currentPosition;
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

        // Remove stationary sequences to eliminate pause periods
        var filteredFrames = FilterStatinaryFrames(frames);
        if (filteredFrames.Count == 0)
        {
            Log.Warning("No movement frames after filtering", "GhostPlayer");
            return;
        }

        // Initialize replay state
        _replayFrames = filteredFrames;
        _currentFrameIndex = 0;
        _replayStartTime = Time.GetTicksMsec() / 1000.0f;
        _isReplaying = true;

        // Set initial position and make ghost visible
        GlobalPosition = filteredFrames[0].Position;
        _currentPosition = GlobalPosition;
        _targetPosition = GlobalPosition;

        Show();
        Log.Replay($"Started replay with {filteredFrames.Count} filtered frames (was {frames.Count})", "GhostPlayer");
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
    private List<ReplayFrame> FilterStatinaryFrames(List<ReplayFrame> originalFrames)
    {
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
        // Find frame that should be playing at current time
        for (int i = _currentFrameIndex; i < _replayFrames.Count; i++)
        {
            if (_replayFrames[i].Timestamp >= time)
            {
                _currentFrameIndex = i;
                return _replayFrames[i];
            }
        }

        // Return null when past all frames to signal end
        return null;
    }

    private void ApplyFrameToGhost(ReplayFrame frame)
    {
        // Apply rotation from frame data
        GlobalRotation = frame.Rotation;

        // Apply sprite flip state
        if (_activeSprite != null)
        {
            var currentScale = _activeSprite.Scale;
            _activeSprite.Scale = new Vector2(
                frame.SpriteFlip ? -Mathf.Abs(currentScale.X) : Mathf.Abs(currentScale.X),
                currentScale.Y
            );
        }

        // Play animation if available and valid
        if (_animatedSprite != null && !string.IsNullOrEmpty(frame.Animation))
        {
            if (_animatedSprite.SpriteFrames?.HasAnimation(frame.Animation) == true)
            {
                if (_animatedSprite.Animation != frame.Animation)
                    _animatedSprite.Play(frame.Animation);
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
            Log.Debug($"Found AnimatedSprite2D: {_animatedSprite.Name}", "GhostPlayer");
            return;
        }

        // Check for various sprite node names as fallback
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D")
            ?? GetNodeOrNull<Sprite2D>("GhostMarrieSprite")
            ?? GetNodeOrNull<Sprite2D>("MarrieSprite");

        if (_sprite != null)
        {
            _activeSprite = _sprite;
            Log.Debug($"Found Sprite2D: {_sprite.Name}", "GhostPlayer");
            return;
        }

        // Check if custom names are actually AnimatedSprite2D
        _animatedSprite = GetNodeOrNull<AnimatedSprite2D>("GhostMarrieSprite")
            ?? GetNodeOrNull<AnimatedSprite2D>("MarrieSprite");

        if (_animatedSprite != null)
        {
            _activeSprite = _animatedSprite;
            Log.Debug($"Found custom AnimatedSprite2D: {_animatedSprite.Name}", "GhostPlayer");
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
            Log.Debug($"Applied ghost effect to '{_activeSprite.Name}'", "GhostPlayer");
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

        Log.Debug("Created fallback visual", "GhostPlayer");
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
        Log.Debug("GhostPlayer children:", "GhostPlayer");
        foreach (Node child in GetChildren())
        {
            Log.Debug($"  - {child.Name} ({child.GetType().Name})", "GhostPlayer");
        }
    }
    #endregion
}