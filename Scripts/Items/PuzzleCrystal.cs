using Godot;

public partial class PuzzleCrystal : Node2D
{
    #region Export Variables for Animation
    [Export] public float FloatingDistance { get; set; } = 8.0f; // How far up and down to float
    [Export] public float FloatingDuration { get; set; } = 2.0f; // How long one cycle takes
    [Export] public bool BrightEffectEnabled { get; set; } = true; // Enable pulsing bright effect
    [Export] public float BrightnessFactor { get; set; } = 1.6f; // How bright the pulse gets (1 = normal)
    [Export] public float BrightPulseDuration { get; set; } = 1.0f; // Full pulse cycle duration
    #endregion

    #region State Variables
    // Global variable flag to track if the crystal is active
    public bool IsActive { get; private set; } = false;

    // Node references
    private Area2D area2D;
    private Sprite2D _gemSprite;
    private Tween _floatingTween;
    private Tween _brightnessTween;
    private Vector2 _originalGemPosition;
    #endregion

    public override void _Ready()
    {
        // Get reference to the Area2D node
        area2D = GetNode<Area2D>("Area2D");
        
        // Get reference to the sprite for animations
        _gemSprite = GetNode<Sprite2D>("GemSprite");
        
        // Store original gem position for floating animation
        if (_gemSprite != null)
        {
            _originalGemPosition = _gemSprite.Position;
            // Ensure default modulate is normal white so brightness tweens start from a known value
            _gemSprite.Modulate = Colors.White;
        }
        
        // Connect the area signals for detecting when bodies enter and exit
        area2D.BodyEntered += OnBodyEntered;
        area2D.BodyExited += OnBodyExited;
        
        // Start the floating animation and bright effect
        StartFloatingAnimation();
    }

    public override void _ExitTree()
    {
        // Clean up tweens when the node is removed from the scene
        StopFloatingAnimation();
    }

    private void OnBodyEntered(Node2D body)
    {
        // Check if the body is Player or GhostPlayer (by name or type)
        string bodyName = body.Name.ToString().ToLower();
        if (bodyName.Contains("player") || bodyName.Contains("characterbody2d") || 
            body.GetGroups().Contains("player") || body is CharacterBody2D)
        {
            IsActive = true;
            GD.Print($"PuzzleCrystal activated by: {body.Name} (Type: {body.GetType()})");
        }
    }

    private void OnBodyExited(Node2D body)
    {
        // Check if the body is Player or GhostPlayer (by name or type)
        string bodyName = body.Name.ToString().ToLower();
        if (bodyName.Contains("player") || bodyName.Contains("characterbody2d") || 
            body.GetGroups().Contains("player") || body is CharacterBody2D)
        {
            IsActive = false;
            GD.Print($"PuzzleCrystal deactivated by: {body.Name} (Type: {body.GetType()})");
        }
    }

    #region Floating Animation
    private void StartFloatingAnimation()
    {
        // Don't start if there's no sprite to animate
        if (_gemSprite == null) return;

        // If an existing tween is running, stop and clear it
        if (_floatingTween != null)
        {
            _floatingTween.Kill();
            _floatingTween = null;
        }

        // Create a new tween for the floating animation
        _floatingTween = CreateTween();
        _floatingTween.SetLoops(); // Loop infinitely

        // Reset to original position
        _gemSprite.Position = _originalGemPosition;

        // Create floating motion: up -> down -> up (continuous)
        var upPosition = new Vector2(_originalGemPosition.X, _originalGemPosition.Y - FloatingDistance);
        var downPosition = new Vector2(_originalGemPosition.X, _originalGemPosition.Y + FloatingDistance);

        _floatingTween.TweenProperty(_gemSprite, "position", upPosition, FloatingDuration / 2.0f);
        _floatingTween.TweenProperty(_gemSprite, "position", downPosition, FloatingDuration);
        _floatingTween.TweenProperty(_gemSprite, "position", _originalGemPosition, FloatingDuration / 2.0f);

        // Brightness pulsing effect (separate tween)
        if (BrightEffectEnabled)
        {
            StartBrightnessEffect();
        }
    }

    private void StartBrightnessEffect()
    {
        // Don't start if there's no sprite to animate
        if (_gemSprite == null) return;

        // Ensure any existing brightness tween is stopped
        if (_brightnessTween != null)
        {
            _brightnessTween.Kill();
            _brightnessTween = null;
        }

        _brightnessTween = CreateTween();
        _brightnessTween.SetLoops();

        var normal = Colors.White;
        var bright = new Color(BrightnessFactor, BrightnessFactor, BrightnessFactor, 1.0f);

        // Pulse: normal -> bright -> normal
        _brightnessTween.TweenProperty(_gemSprite, "modulate", bright, BrightPulseDuration / 2.0f);
        _brightnessTween.TweenProperty(_gemSprite, "modulate", normal, BrightPulseDuration / 2.0f);
    }

    private void StopFloatingAnimation()
    {
        if (_floatingTween != null)
        {
            _floatingTween.Kill();
            _floatingTween = null;
        }

        if (_brightnessTween != null)
        {
            _brightnessTween.Kill();
            _brightnessTween = null;
        }

        // Reset gem to original position
        if (_gemSprite != null)
        {
            // Reset visual tint to normal
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Position = _originalGemPosition;
        }
    }
    #endregion
}