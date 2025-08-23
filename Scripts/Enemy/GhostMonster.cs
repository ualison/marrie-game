using Godot;
using System.Collections.Generic;

// Ghost version of monster that replays recorded movements and interactions
public partial class GhostMonster : CharacterBody2D
{
    #region Constants and Configuration
    private const float SMOOTH_SPEED = 10.0f;
    private static readonly Color GHOST_TINT = new(1.0f, 0.5f, 0.5f, 0.7f); // Semi-transparent red
    #endregion

    #region State Variables
    private List<ReplayFrame> _replayFrames;
    private int _currentFrameIndex;
    private float _replayStartTime;
    private bool _isReplaying;
    private string _monsterType; // "FollowerMonster" or "Monster"
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
        SetupGhostVisuals();
        DisableCollisions();
        Hide();
        GD.Print($"GhostMonster ready: {Name}");
    }
    #endregion

    #region Core Update Loop
    public override void _Process(double delta)
    {
        if (!_isReplaying || _replayFrames == null) return;

        float elapsedTime = Time.GetTicksMsec() / 1000.0f - _replayStartTime;
        var targetFrame = FindFrameAtTime(elapsedTime);

        if (targetFrame != null)
        {
            _targetPosition = targetFrame.Position;
            ApplyFrameToGhost(targetFrame);
        }
        else if (_currentFrameIndex >= _replayFrames.Count)
        {
            _isReplaying = false;
            GD.Print($"GhostMonster: Replay completed at frame {_currentFrameIndex}/{_replayFrames.Count}");
            GD.Print($"Ghost monster sprite visible: {_activeSprite?.Visible}");
            return;
        }

        _currentPosition = _currentPosition.Lerp(_targetPosition, SMOOTH_SPEED * (float)delta);
        GlobalPosition = _currentPosition;
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

        GlobalPosition = frames[0].Position;
        _currentPosition = GlobalPosition;
        _targetPosition = GlobalPosition;

        // Start with fade in effect like original monster
        FadeIn();
        GD.Print($"GhostMonster: Started replay with {frames.Count} frames ({monsterType})");
    }

    public void StopReplay()
    {
        _isReplaying = false;
        GD.Print("GhostMonster: Replay stopped");
    }

    public void FadeOutAndDestroy()
    {
        // Called by ReplayGem for cleanup with proper timing
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

    #region Frame Processing
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

    private void ApplyFrameToGhost(ReplayFrame frame)
    {
        GlobalRotation = frame.Rotation;

        if (_activeSprite != null)
        {
            var currentScale = _activeSprite.Scale;
            _activeSprite.Scale = new Vector2(
                frame.SpriteFlip ? -Mathf.Abs(currentScale.X) : Mathf.Abs(currentScale.X),
                currentScale.Y
            );
        }

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
        DiscoverSpriteComponents();
        ApplyGhostEffect();
    }


    private void DiscoverSpriteComponents()
    {
        // Try animated sprite first
        _animatedSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (_animatedSprite != null)
        {
            _activeSprite = _animatedSprite;
            GD.Print("GhostMonster: Found AnimatedSprite2D");
            return;
        }

        // Try static sprites - match the names from original monsters
        _sprite = GetNodeOrNull<Sprite2D>("MonsterSprite")  // This is what FollowerMonster uses
               ?? GetNodeOrNull<Sprite2D>("GhostMonsterSprite")  // Fallback
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
        image.Fill(new Color(1, 0, 0)); // Red for monster
        imageTexture.SetImage(image);

        fallbackSprite.Texture = imageTexture;
        fallbackSprite.Modulate = GHOST_TINT;

        AddChild(fallbackSprite);
        _activeSprite = fallbackSprite;
    }

    private void DisableCollisions()
    {
        // Disable physics collision
        SetCollisionLayerValue(1, false);
        SetCollisionMaskValue(1, false);

        // Disable any damage areas that might exist
        var damageArea = GetNodeOrNull<Area2D>("DamageArea");
        if (damageArea != null)
        {
            damageArea.SetCollisionLayerValue(1, false);
            damageArea.SetCollisionMaskValue(1, false);
            damageArea.Monitoring = false;
            damageArea.Monitorable = false;
            GD.Print("Ghost monster damage area disabled");
        }

        // Disable any other Area2D nodes that might cause interactions
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

    private void FadeIn()
    {
        if (_activeSprite == null) return;

        // Make sure the ghost monster is shown and visible
        Show(); // Add this line - ensures the CharacterBody2D itself is visible
        _activeSprite.Visible = true;
        _activeSprite.Modulate = new Color(GHOST_TINT.R, GHOST_TINT.G, GHOST_TINT.B, 0);

        var tween = CreateTween();
        tween.TweenProperty(_activeSprite, "modulate:a", GHOST_TINT.A, 0.5f);
        tween.TweenCallback(Callable.From(() =>
        {
            GD.Print($"Ghost monster fully visible at {GlobalPosition}");
        }));
    }
    #endregion
}