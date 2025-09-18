/// <summary>
/// PuzzleCrystal.cs - Manages puzzle crystal activation and Player-GhostPlayer collision control
/// 
/// Core Features:
/// 1. Priority-based activation system: Player has priority over GhostPlayer
/// 2. Dynamic Player-GhostPlayer collision management:
///    - Collision ENABLED when GhostPlayer is activating the crystal (layer 3)
///    - Collision DISABLED when Player takes control or crystal deactivates
/// 3. Collision tracking for multiple bodies entering/exiting the crystal
/// 
/// Collision System:
/// - Uses collision layer 3 for Player-GhostPlayer interactions
/// - Only active when GhostPlayer is the controlling/activating entity
/// - Automatically manages collision states based on activation priority
/// 
/// Priority Logic:
/// - Player: Always takes control when entering, disables Player-Ghost collision
/// - GhostPlayer: Can only control when no Player is present, enables Player-Ghost collision
/// - Transfer: Seamless control transfer between Player and GhostPlayer with collision updates
/// </summary>

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

    // Collision management for GhostPlayer-Player interaction
    private bool _isGhostPlayerActivating = false;
    private Node2D _currentGhostPlayer = null;
    private Node2D _currentPlayer = null;

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
                
                // Update collision - Player is not activating crystal, so disable ghost collision
                UpdatePlayerGhostCollision(false, null, body);
            }
            else if (_currentActivatingBody != body)
            {
                // Player taking over from GhostPlayer
                bool wasGhostActivating = IsGhostPlayer(_currentActivatingBody);
                _currentActivatingBody = body;
                GD.Print($"PuzzleCrystal control taken by Player: {body.Name}");
                
                // Update collision - Player took over, so disable ghost collision
                if (wasGhostActivating)
                {
                    UpdatePlayerGhostCollision(false, null, body);
                }
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
                    
                    // Update collision - GhostPlayer is activating, enable collision with Player
                    Node2D currentPlayer = FindCurrentPlayer();
                    UpdatePlayerGhostCollision(true, body, currentPlayer);
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

        // Check if the exiting body is a GhostPlayer that was activating - always disable collision
        bool isExitingGhostPlayer = IsGhostPlayer(body);
        bool wasGhostCurrentlyActivating = isExitingGhostPlayer && (_currentActivatingBody == body);

        // If a GhostPlayer that was activating is exiting, disable collision immediately
        if (wasGhostCurrentlyActivating)
        {
            UpdatePlayerGhostCollision(false, null, null);
            GD.Print($"PuzzleCrystal: GhostPlayer {body.Name} exited - disabled Player collision");
        }

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
                
                // Update collision - Player is now controlling, disable ghost collision
                UpdatePlayerGhostCollision(false, null, remainingPlayer);
            }
            else if (remainingGhost != null)
            {
                // No Player, but GhostPlayer is still colliding - keep active under GhostPlayer control
                _currentActivatingBody = remainingGhost;
                GD.Print($"PuzzleCrystal control transferred to GhostPlayer: {remainingGhost.Name}");
                
                // Update collision - GhostPlayer is now controlling, enable collision
                Node2D currentPlayer = FindCurrentPlayer();
                UpdatePlayerGhostCollision(true, remainingGhost, currentPlayer);
            }
            else if (_collidingBodies.Count == 0)
            {
                // No bodies are colliding anymore - deactivate only if it wasn't pre-active
                // Note: For now, we assume all activation comes from collision
                // If there are other activation mechanisms, this logic would need to be updated
                IsActive = false;
                _currentActivatingBody = null;
                GD.Print($"PuzzleCrystal deactivated - no colliding bodies remaining");
                
                // Update collision - Crystal is deactivating, disable ghost collision (if not already done)
                if (!wasGhostCurrentlyActivating && IsGhostPlayer(body))
                {
                    UpdatePlayerGhostCollision(false, null, null);
                }
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

    #region Collision Management
    /// <summary>
    /// Enables collision between Player and GhostPlayer when GhostPlayer is activating the crystal
    /// </summary>
    private void EnablePlayerGhostCollision()
    {
        if (_currentPlayer is CharacterBody2D player && _currentGhostPlayer is CharacterBody2D ghost)
        {
            // Enable collision on collision layer 3 for Player-GhostPlayer interaction
            ghost.SetCollisionLayerValue(3, true);
            ghost.SetCollisionMaskValue(3, true);
            
            player.SetCollisionLayerValue(3, true);
            player.SetCollisionMaskValue(3, true);
            
            GD.Print($"PuzzleCrystal: Enabled Player-GhostPlayer collision (Ghost: {ghost.Name}, Player: {player.Name})");
        }
    }

    /// <summary>
    /// Disables collision between Player and GhostPlayer when GhostPlayer stops activating the crystal
    /// </summary>
    private void DisablePlayerGhostCollision()
    {
        if (_currentPlayer is CharacterBody2D player && _currentGhostPlayer is CharacterBody2D ghost)
        {
            // Disable collision on collision layer 3 for Player-GhostPlayer interaction
            ghost.SetCollisionLayerValue(3, false);
            ghost.SetCollisionMaskValue(3, false);
            
            player.SetCollisionLayerValue(3, false);
            player.SetCollisionMaskValue(3, false);
            
            GD.Print($"PuzzleCrystal: Disabled Player-GhostPlayer collision (Ghost: {ghost.Name}, Player: {player.Name})");
        }
    }

    /// <summary>
    /// Updates the collision state based on GhostPlayer activation status
    /// </summary>
    private void UpdatePlayerGhostCollision(bool ghostIsActivating, Node2D ghostPlayer, Node2D player)
    {
        bool previousState = _isGhostPlayerActivating;
        _isGhostPlayerActivating = ghostIsActivating;
        _currentGhostPlayer = ghostPlayer;
        _currentPlayer = player;

        if (ghostIsActivating && !previousState)
        {
            // GhostPlayer just started activating - enable collision
            EnablePlayerGhostCollision();
        }
        else if (!ghostIsActivating && previousState)
        {
            // GhostPlayer stopped activating - disable collision
            DisablePlayerGhostCollision();
        }
    }

    /// <summary>
    /// Finds the current Player in the scene (not ghost)
    /// </summary>
    private Node2D FindCurrentPlayer()
    {
        // Try to find player in the scene tree
        var players = GetTree().GetNodesInGroup("player");
        foreach (Node node in players)
        {
            if (node is Node2D playerNode && IsPlayer(playerNode))
            {
                return playerNode;
            }
        }
        return null;
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