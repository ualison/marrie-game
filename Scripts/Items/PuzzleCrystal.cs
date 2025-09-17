using Godot;
using System.Collections.Generic;

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

    // Track which bodies are currently colliding to manage priority
    private readonly HashSet<Node2D> _collidingBodies = new();
    private Node2D _currentActivatingBody = null; // The body that is currently activating the crystal

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
        if (!IsPlayerOrGhost(body))
            return;

        // Add the body to our collision tracking set
        _collidingBodies.Add(body);

        // Determine if this is a Player (higher priority) or GhostPlayer
        bool isPlayer = IsPlayer(body);
        bool isGhostPlayer = IsGhostPlayer(body);

        GD.Print($"PuzzleCrystal collision entered by: {body.Name} (Type: {body.GetType()}) - IsPlayer: {isPlayer}, IsGhostPlayer: {isGhostPlayer}");

        // Priority logic:
        // 1. If Player is already activating, GhostPlayer cannot change state
        // 2. If already active, don't deactivate (both Player and GhostPlayer)
        
        if (isPlayer)
        {
            // Player has highest priority - always activate
            if (!IsActive)
            {
                IsActive = true;
                _currentActivatingBody = body;
                GD.Print($"PuzzleCrystal activated by Player: {body.Name}");
            }
            else if (_currentActivatingBody != body)
            {
                // Player taking over from GhostPlayer
                _currentActivatingBody = body;
                GD.Print($"PuzzleCrystal control taken by Player: {body.Name}");
            }
        }
        else if (isGhostPlayer)
        {
            // GhostPlayer can only activate if no Player is currently controlling
            if (_currentActivatingBody == null || (!IsPlayer(_currentActivatingBody) && !IsActive))
            {
                if (!IsActive)
                {
                    IsActive = true;
                    _currentActivatingBody = body;
                    GD.Print($"PuzzleCrystal activated by GhostPlayer: {body.Name}");
                }
            }
            else
            {
                GD.Print($"GhostPlayer {body.Name} cannot change PuzzleCrystal state - Player has priority");
            }
        }
    }

    private void OnBodyExited(Node2D body)
    {
        // Check if the body is Player or GhostPlayer (by name or type)
        if (!IsPlayerOrGhost(body))
            return;

        // Remove the body from our collision tracking set
        _collidingBodies.Remove(body);

        GD.Print($"PuzzleCrystal collision exited by: {body.Name} (Type: {body.GetType()})");

        // Only deactivate if this body was the one currently activating the crystal
        // and no other bodies are still colliding AND the crystal was activated by collision
        // (not pre-activated by some other means)
        if (_currentActivatingBody == body)
        {
            // Check if any Player is still colliding (highest priority)
            Node2D remainingPlayer = null;
            Node2D remainingGhost = null;

            foreach (var collidingBody in _collidingBodies)
            {
                if (IsPlayer(collidingBody))
                {
                    remainingPlayer = collidingBody;
                    break; // Player has highest priority
                }
                else if (IsGhostPlayer(collidingBody))
                {
                    remainingGhost = collidingBody;
                }
            }

            if (remainingPlayer != null)
            {
                // Another Player is still colliding - transfer control but keep active
                _currentActivatingBody = remainingPlayer;
                GD.Print($"PuzzleCrystal control transferred to Player: {remainingPlayer.Name}");
            }
            else if (remainingGhost != null)
            {
                // No Player, but GhostPlayer is still colliding - keep active under GhostPlayer control
                _currentActivatingBody = remainingGhost;
                GD.Print($"PuzzleCrystal control transferred to GhostPlayer: {remainingGhost.Name}");
            }
            else if (_collidingBodies.Count == 0)
            {
                // No bodies are colliding anymore - deactivate only if it wasn't pre-active
                // Note: For now, we assume all activation comes from collision
                // If there are other activation mechanisms, this logic would need to be updated
                IsActive = false;
                _currentActivatingBody = null;
                GD.Print($"PuzzleCrystal deactivated - no colliding bodies remaining");
            }
            else
            {
                // Bodies are still colliding but crystal should remain active
                GD.Print($"PuzzleCrystal remains active - {_collidingBodies.Count} bodies still colliding");
            }
        }
        else
        {
            GD.Print($"Body {body.Name} exited but was not the activating body - no state change");
        }
    }

    #region Helper Methods
    private bool IsPlayerOrGhost(Node2D body)
    {
        if (body == null) return false;
        
        string bodyName = body.Name.ToString().ToLower();
        return bodyName.Contains("player") || 
               bodyName.Contains("characterbody2d") || 
               body.GetGroups().Contains("player") || 
               body is CharacterBody2D;
    }

    private bool IsPlayer(Node2D body)
    {
        if (body == null) return false;
        
        // Player is in "player" group but NOT in "ghost_player" group
        return body.GetGroups().Contains("player") && 
               !body.GetGroups().Contains("ghost_player");
    }

    private bool IsGhostPlayer(Node2D body)
    {
        if (body == null) return false;
        
        // GhostPlayer is in both "player" and "ghost_player" groups
        return body.GetGroups().Contains("ghost_player");
    }
    #endregion

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