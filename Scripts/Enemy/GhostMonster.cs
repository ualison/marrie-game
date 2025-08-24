using Godot;
using System.Collections.Generic;

// Ghost version of monster that replays recorded movements and interactions - Performance Optimized
public partial class GhostMonster : CharacterBody2D
{
    #region Constants and Configuration
    private static readonly Color GHOST_TINT = new(1.0f, 0.5f, 0.5f, 0.7f);
    private const float ALPHA_TOLERANCE = 0.01f; // Avoid unnecessary updates
    #endregion

    #region State Variables
    private List<ReplayFrame> _replayFrames;
    private int _currentFrameIndex;
    private float _replayStartTime;
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
    #endregion

    #region Initialization
    public override void _Ready()
    {
        SetupGhostVisuals();
        DisableCollisions();
        Hide();
        GD.Print($"GhostMonster ready: {Name}");
    }
    #endregion

    #region Core Update Loop - OPTIMIZED
    public override void _Process(double delta)
    {
        if (!_isReplaying || _replayFrames == null) return;

        float elapsedTime = Time.GetTicksMsec() / 1000.0f - _replayStartTime;
        var targetFrame = FindFrameAtTime(elapsedTime); // Use same method as GhostPlayer

        if (targetFrame != null)
        {
            GlobalPosition = targetFrame.Position;
            ApplyFrameToGhostOptimized(targetFrame);
        }
        else if (_currentFrameIndex >= _replayFrames.Count)
        {
            _isReplaying = false;
            GD.Print("GhostMonster: Replay completed");
            return;
        }
    }
    // Use the same frame finder as GhostPlayer
    private ReplayFrame FindFrameAtTime(float time)
    {
        for (int i = _currentFrameIndex; i < _replayFrames.Count; i++)
        {
            if (_replayFrames[i].Timestamp >= time)
            {
                _currentFrameIndex = i;
                return _replayFrames[i];
            }
        }
        return null;
    }
    #endregion

    #region Optimized Frame Processing
    private ReplayFrame FindFrameAtTimeOptimized(float time)
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
            // Only update alpha if it changed significantly
            float finalAlpha = frame.Alpha * GHOST_TINT.A;
            if (Mathf.Abs(_lastAlpha - finalAlpha) > ALPHA_TOLERANCE)
            {
                _activeSprite.Visible = frame.Alpha > 0.0f;
                _activeSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, finalAlpha);
                _lastAlpha = finalAlpha;
            }

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
    #endregion

    #region Replay Control
    public void StartReplay(List<ReplayFrame> frames, string monsterType = "Monster")
    {
        if (frames == null || frames.Count == 0)
        {
            GD.Print("GhostMonster: No frames to replay");
            return;
        }

        _replayFrames = frames;
        _monsterType = monsterType;
        _currentFrameIndex = 0;
        _replayStartTime = Time.GetTicksMsec() / 1000.0f;
        _isReplaying = true;

        // Reset performance cache
        _lastAlpha = -1f;
        _lastFlipState = false;
        _lastAnimation = "";
        _lastPosition = Vector2.Zero;

        // Start at first frame position
        GlobalPosition = frames[0].Position;
        _lastPosition = GlobalPosition;

        // Show the CharacterBody2D but start sprite hidden
        Show();
        if (_activeSprite != null)
        {
            _activeSprite.Visible = false;
            _activeSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, 0);
        }

        GD.Print($"GhostMonster: Started optimized replay with {frames.Count} frames");
    }

    public void StopReplay()
    {
        _isReplaying = false;
        GD.Print("GhostMonster: Replay stopped");
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
            GD.Print("Ghost monster faded out and destroyed");
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
            GD.Print("GhostMonster: Found AnimatedSprite2D");
            return;
        }

        _sprite = GetNodeOrNull<Sprite2D>("MonsterSprite")
               ?? GetNodeOrNull<Sprite2D>("GhostMonsterSprite")
               ?? GetNodeOrNull<Sprite2D>("Sprite2D");

        if (_sprite != null)
        {
            _activeSprite = _sprite;
            GD.Print($"GhostMonster: Found sprite: {_sprite.Name}");
        }
        else
        {
            GD.Print("GhostMonster: No sprite found, creating fallback");
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