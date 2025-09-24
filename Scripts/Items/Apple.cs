using Godot;
using Core.Enums;
using MarrieGame.Core.Utils;
using System.Linq;

/// <summary>
/// Apple item with different state cycles that change randomly:
/// 
/// Tree States: RedLife, GrayKnockable, PurpleMana, YellowEnergy (selected randomly)
/// Fallen States: RedLife, GrayNotCollectible, PurpleMana, YellowEnergy (selected randomly)
/// 
/// States:
/// - RedLife: Red color, Life phase (collectible when fallen, restores health)
/// - GrayKnockable: Gray color, Knockable phase (tree only, can be hit by rocks to fall)
/// - PurpleMana: Purple color, Mana phase (collectible when fallen, restores mana)
/// - GrayNotCollectible: Dark gray color, Non-collectible phase (fallen only, not collectible)
/// - YellowEnergy: Yellow color, Energy phase (collectible by Puff in both positions, restores Puff energy)
/// 
/// RedLife and PurpleMana phases are collectible by player, only when apple is on the ground
/// YellowEnergy phase is collectible by Puff regardless of position (tree or ground)
/// Transition times are randomly selected within configurable ranges
/// </summary>
public partial class Apple : Node2D
{
    #region Exported Properties
    [Export] public float MinTransitionTime { get; set; } = 2.0f; // Minimum time between color transitions
    [Export] public float MaxTransitionTime { get; set; } = 5.0f; // Maximum time between color transitions
    [Export] public float MinNeutralPhaseTime { get; set; } = 1.5f; // Minimum time spent in neutral/collectible phases
    [Export] public float MaxNeutralPhaseTime { get; set; } = 4.0f; // Maximum time spent in neutral/collectible phases
    [Export] public int LifeRestoreAmount { get; set; } = 1; // Life restored by red apple
    [Export] public int ManaRestoreAmount { get; set; } = 2; // Mana restored by purple apple
    [Export] public int EnergyRestoreAmount { get; set; } = 1; // Energy restored to Puff by yellow apple
    [Export] public Texture2D RedAppleTexture { get; set; }
    [Export] public Texture2D PurpleAppleTexture { get; set; }
    [Export] public Texture2D NeutralAppleTexture { get; set; }
    [Export] public Texture2D ApplePuffTexture { get; set; }
    #endregion

    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _appleSprite;
    private Label _promptLabel;
    private RigidBody2D _rigidBody;
    private Area2D _rockDetectionArea;
    private Area2D _groundDetectionArea;
    private Area2D _puffDetectionArea;
    #endregion

    #region State
    private bool _playerNearby = false;
    private AppleState _currentState = AppleState.RedLife; // Current apple state
    private ApplePosition _currentPosition = ApplePosition.OnTree; // Current apple position
    private Timer _transitionTimer;
    private Tween _fadeTween;
    private Vector2 _originalScale; // Store original scale for transitions
    private bool _isFalling = false; // Track if apple is falling
    private bool _hasLanded = false; // Track if apple has landed on ground
    // When Puff picks up an apple from the tree (special behaviour), track it here
    private bool _isAttachedToPuff = false;
    private Node2D _carryingPuff = null;
    // Keep reference to original parent so we can reparent the apple back when dropped
    private Node _originalParent = null;
    // Used to defer attaching to Puff (avoid removing nodes during physics callbacks)
    private Node2D _pendingAttachPuff = null;
    // Used to defer detaching from Puff when Puff touches ground
    private Node2D _pendingDetachPuff = null;
    
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Add to apple group for easy discovery
        AddToGroup("apple");
        
        // Initialize the apple system
        InitializeComponents();
        SetupRigidBody();
        ConnectSignals();
        SetupPromptLabel();
        SetupTransitionTimer();
        SetInitialState();
        EnableInputProcessing();
    }

    private void InitializeComponents()
    {
    _detectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "RigidBody2D/DetectionArea");
    _rigidBody = NodeHelpers.GetNodeOrNull<RigidBody2D>(this, null, "RigidBody2D");
    _appleSprite = NodeHelpers.GetNodeOrNull<Sprite2D>(this, null, "RigidBody2D/AppleSprite");
    _promptLabel = NodeHelpers.GetNodeOrNull<Label>(this, null, "RigidBody2D/PromptLabel");
    _rockDetectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "RigidBody2D/RockDetectionArea");
    _groundDetectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "RigidBody2D/GroundDetectionArea");
    _puffDetectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "RigidBody2D/PuffDetectionArea");
    }

    private void SetupRigidBody()
    {
        // Connect rock hit detection - the Area2D is already set up in the scene
        if (_rockDetectionArea != null)
        {
            // Ensure the detection area is monitoring so AreaEntered fires reliably
            _rockDetectionArea.Monitoring = true;
            _rockDetectionArea.Monitorable = true;
            _rockDetectionArea.AreaEntered += OnProjectileImpact;
        }
        
        // Connect to ground collision detection via dedicated ground detection area
        if (_groundDetectionArea != null)
            _groundDetectionArea.BodyEntered += OnGroundCollision;
            
        // Connect Puff collision detection for energy restoration
        if (_puffDetectionArea != null)
        {
            _puffDetectionArea.Monitoring = true;
            _puffDetectionArea.Monitorable = true;
            _puffDetectionArea.BodyEntered += OnPuffCollision;
        }
        else
        {
            Log.Warning("PuffDetectionArea not found in scene - Puff collision won't work!", "Apple");
        }
    }

    private void ConnectSignals()
    {
        if (_detectionArea != null)
        {
            _detectionArea.BodyEntered += OnPlayerEntered;
            _detectionArea.BodyExited += OnPlayerExited;
        }
    }

    private void SetupPromptLabel()
    {
        if (_promptLabel != null)
        {
            _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _promptLabel.Text = "Press E";
            _promptLabel.Hide();
        }
    }

    private void SetupTransitionTimer()
    {
        _transitionTimer = new Timer();
        _transitionTimer.WaitTime = GetCurrentPhaseTime();
        _transitionTimer.OneShot = false;
        if (_transitionTimer != null)
        {
            _transitionTimer.Timeout += OnTransitionTimeout;
            AddChild(_transitionTimer);
            _transitionTimer.Start();
        }
    }

    private float GetCurrentPhaseTime()
    {
        // Get random time based on current state
        if (_currentState == AppleState.GrayKnockable || _currentState == AppleState.GrayNotCollectible)
        {
            // Neutral phases use neutral phase time range
            return (float)GD.RandRange(MinNeutralPhaseTime, MaxNeutralPhaseTime);
        }
        else
        {
            // Collectible phases use transition time range
            return (float)GD.RandRange(MinTransitionTime, MaxTransitionTime);
        }
    }

    private void SetInitialState()
    {
        // Randomly select initial state based on apple position
        if (_currentPosition == ApplePosition.OnTree)
        {
            // Tree apples can start with any tree state
            AppleState[] treeStates = { AppleState.RedLife, AppleState.GrayKnockable, AppleState.PurpleMana, AppleState.YellowEnergy };
            int randomIndex = GD.RandRange(0, treeStates.Length - 1);
            _currentState = treeStates[randomIndex];
        }
        else
        {
            // Ground apples can start with any ground state
            AppleState[] groundStates = { AppleState.RedLife, AppleState.GrayNotCollectible, AppleState.PurpleMana, AppleState.YellowEnergy };
            int randomIndex = GD.RandRange(0, groundStates.Length - 1);
            _currentState = groundStates[randomIndex];
        }

        // Set the appropriate texture based on the random initial state
        Texture2D initialTexture = _currentState switch
        {
            AppleState.RedLife => RedAppleTexture,
            AppleState.GrayKnockable => NeutralAppleTexture,
            AppleState.PurpleMana => PurpleAppleTexture,
            AppleState.GrayNotCollectible => NeutralAppleTexture,
            AppleState.YellowEnergy => ApplePuffTexture,
            _ => RedAppleTexture
        };

        if (initialTexture != null && _appleSprite != null)
        {
            _appleSprite.Texture = initialTexture;
        }
        
        // Apply initial state visual effects
        ApplyStateVisualEffects();
        
        // Store the original scale for smooth transitions
        if (_appleSprite != null)
        {
            _originalScale = _appleSprite.Scale;
        }
    }

    private void EnableInputProcessing()
    {
        SetProcess(true);
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        if (!_playerNearby) return;

        if (Input.IsActionJustPressed("interact"))
        {
            HandleAppleConsumption();
        }
    }

    private void HandleAppleConsumption()
    {
        // Only allow consumption during RedLife and PurpleMana phases
        if (_currentState != AppleState.RedLife && _currentState != AppleState.PurpleMana)
        {
            return;
        }
        
        // Only allow pickup if apple is on ground (prevent tree cheating)
        if (_currentPosition != ApplePosition.OnGround)
        {
            Log.UI("Cannot pickup apple from tree - must be knocked down first", "Apple");
            return;
        }

        if (_currentState == AppleState.RedLife)
        {
            ConsumeRedApple(); // Red phase gives life
        }
        else if (_currentState == AppleState.PurpleMana)
        {
            ConsumePurpleApple(); // Purple phase gives mana
        }

        // Remove apple after consumption
        DestroyApple();
    }

    private void ConsumeRedApple()
    {
        // Find player and restore life
    var player = GetTree().GetFirstNodeInGroup("player") as Node;
    var playerLife = player != null ? NodeHelpers.GetNodeOrNull<PlayerLife>(player, null, "PlayerLife") : null;
        
        if (playerLife != null)
        {
            playerLife.Heal(LifeRestoreAmount);
        }
        else
        {
            Log.Warning("PlayerLife not found", "Apple");
        }
    }

    private void ConsumePurpleApple()
    {
        // Find player and restore mana
    var player = GetTree().GetFirstNodeInGroup("player") as Node;
    var playerMana = player != null ? NodeHelpers.GetNodeOrNull<PlayerMana>(player, null, "PlayerMana") : null;
        
        if (playerMana != null)
        {
            playerMana.RestoreMana(ManaRestoreAmount);
        }
        else
        {
            Log.Warning("PlayerMana not found", "Apple");
        }
    }

    private void RestoreEnergyToPuff()
    {
        // Find Puff and restore energy
        var puff = GetTree().GetFirstNodeInGroup("puff") as Node;
        var puffEnergyBar = puff != null ? NodeHelpers.GetNodeOrNull<MarrieGame.UI.PuffEnergyBar>(puff, null, "PuffEnergyBar") : null;
        
        if (puffEnergyBar != null)
        {
            puffEnergyBar.GainEnergy(EnergyRestoreAmount);
        }
        else
        {
            // Try alternative ways to find energy bar in scene tree
            string[] possibleGroups = { "puff_energy_bar", "puff_energy_bar_ui" };
            MarrieGame.UI.PuffEnergyBar foundEnergyBar = null;
            
            foreach (string groupName in possibleGroups)
            {
                var energyBars = GetTree().GetNodesInGroup(groupName);
                
                if (energyBars.Count > 0)
                {
                    foundEnergyBar = energyBars[0] as MarrieGame.UI.PuffEnergyBar;
                    if (foundEnergyBar != null)
                    {
                        break;
                    }
                }
            }
            
            if (foundEnergyBar != null)
            {
                foundEnergyBar.GainEnergy(EnergyRestoreAmount);
            }
            else
            {
                Log.Warning("PuffEnergyBar not found via any group search", "Apple");
            }
        }

        // Remove apple after energy restoration
        DestroyApple();
    }

    private void DestroyApple()
    {
        QueueFree();
    }
    #endregion

    #region Color Transition
    private void OnTransitionTimeout()
    {
        TransitionColor();
    }

    private void TransitionColor()
    {
        if (_fadeTween != null && _fadeTween.IsValid())
        {
            _fadeTween.Kill();
        }

        _fadeTween = CreateTween();
        
        // Create a smoother transition with subtle scale effect and partial fade
        // Phase 1: Scale down slightly and fade to 50% opacity
        _fadeTween.TweenProperty(_appleSprite, "scale", _originalScale * 0.85f, 0.2f);
        _fadeTween.TweenProperty(_appleSprite, "modulate:a", 0.5f, 0.2f);
        
        // Phase 2: Switch texture at the lowest point
        _fadeTween.TweenCallback(Callable.From(SwitchTexture));
        
        // Phase 3: Scale back to original size and full opacity
        _fadeTween.TweenProperty(_appleSprite, "scale", _originalScale, 0.2f);
        _fadeTween.TweenProperty(_appleSprite, "modulate:a", 1.0f, 0.2f);
        
        // Phase 4: Update prompt display after transition completes
        _fadeTween.TweenCallback(Callable.From(UpdatePromptDisplay));
    }

    private void SwitchTexture()
    {
        // Random state selection based on apple position
        if (_currentPosition == ApplePosition.OnGround)
        {
            // Ground apple: choose randomly from RedLife, GrayNotCollectible, PurpleMana, YellowEnergy
            AppleState[] groundStates = { AppleState.RedLife, AppleState.GrayNotCollectible, AppleState.PurpleMana, AppleState.YellowEnergy };
            
            // If transitioning from tree to ground, start with GrayNotCollectible
            if (_currentState == AppleState.GrayKnockable)
            {
                _currentState = AppleState.GrayNotCollectible;
            }
            else
            {
                // Get available states (excluding current state to ensure change)
                var availableStates = groundStates.Where(state => state != _currentState).ToArray();
                if (availableStates.Length > 0)
                {
                    int randomIndex = GD.RandRange(0, availableStates.Length - 1);
                    _currentState = availableStates[randomIndex];
                }
            }
        }
        else // ApplePosition.OnTree
        {
            // Tree apple: choose randomly from RedLife, GrayKnockable, PurpleMana, YellowEnergy
            AppleState[] treeStates = { AppleState.RedLife, AppleState.GrayKnockable, AppleState.PurpleMana, AppleState.YellowEnergy };
            
            // Get available states (excluding current state to ensure change)
            var availableStates = treeStates.Where(state => state != _currentState).ToArray();
            if (availableStates.Length > 0)
            {
                int randomIndex = GD.RandRange(0, availableStates.Length - 1);
                _currentState = availableStates[randomIndex];
            }
        }
        
        // Update texture based on current state
        Texture2D newTexture = _currentState switch
        {
            AppleState.RedLife => RedAppleTexture,
            AppleState.GrayKnockable => NeutralAppleTexture,
            AppleState.PurpleMana => PurpleAppleTexture,
            AppleState.GrayNotCollectible => NeutralAppleTexture,
            AppleState.YellowEnergy => ApplePuffTexture,
            _ => RedAppleTexture
        };

        if (newTexture != null)
        {
            _appleSprite.Texture = newTexture;
            
            // Apply visual effects for different states
            ApplyStateVisualEffects();
        }

        // Update timer for the new phase with random timing
        _transitionTimer.WaitTime = GetCurrentPhaseTime();
        _transitionTimer.Start(); // Restart the timer
    }

    private void ApplyStateVisualEffects()
    {
        // Reset modulation first
        _appleSprite.Modulate = Colors.White;
        
        switch (_currentState)
        {
            case AppleState.RedLife:
                // Normal red apple appearance
                break;
            case AppleState.GrayKnockable:
                // Gray appearance - slightly desaturated and knockable by rocks
                _appleSprite.Modulate = new Color(0.7f, 0.7f, 0.7f, 1.0f);
                break;
            case AppleState.PurpleMana:
                // Normal purple apple appearance
                break;
            case AppleState.GrayNotCollectible:
                // Darker gray appearance for fallen apples (not collectible)
                _appleSprite.Modulate = new Color(0.5f, 0.5f, 0.5f, 1.0f);
                break;
            case AppleState.YellowEnergy:
                // Bright yellow appearance with slight glow effect for Puff energy
                _appleSprite.Modulate = new Color(1.2f, 1.2f, 0.8f, 1.0f);
                break;
        }
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            UpdatePromptDisplay();
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            _promptLabel.Hide();
        }
    }

    private void OnProjectileImpact(Area2D area)
    {
        // Generic projectile impact detection - any Area2D on layer 8 is considered a projectile
    // Use bitmask check in case multiple layers are set on the entering area
    if ((area.CollisionLayer & 8) != 0)
        {
            // Apple should fall ONLY when hit in GrayKnockable state
            if (_currentState == AppleState.GrayKnockable && !_isFalling)
            {
                MakeAppleFall();
            }
        }
    }

    private void OnGroundCollision(Node2D body)
    {
        // If apple is attached to Puff and Puff touches ground, detach and make the apple fall to ground
        if (body is StaticBody2D && _isAttachedToPuff)
        {
            // Defer the detach to avoid changing the scene tree during physics callbacks
            _pendingDetachPuff = _carryingPuff;
            CallDeferred(nameof(DeferredDetachFromPuff));
            return;
        }

        // Check if apple hit the ground (StaticBody2D) in a normal falling case
        if (body is StaticBody2D && _isFalling && !_hasLanded)
        {
            // Use CallDeferred to avoid physics state change during collision detection
            CallDeferred(nameof(OnGroundLanding));
        }
    }

    private void OnPuffCollision(Node2D body)
    {
        // If the colliding body is Puff, handle Puff-specific interactions
        if (body.IsInGroup("puff"))
        {
            // If apple is already attached to Puff, ignore
            if (_isAttachedToPuff)
                return;

            // Puff should not consume apples that are still on a tree
            if (_currentPosition == ApplePosition.OnTree)
            {
                // If the apple is GrayKnockable and on the tree, Puff should attach and carry it
                if (_currentState == AppleState.GrayKnockable)
                {
                    // Store original parent/index so we can reparent back later
                    _originalParent = GetParent();
                    // original parent stored for later reparenting
                    // note: we avoid storing child index to keep compatibility across engines
                    // and rely on adding back to the parent normally
                    // _originalIndex intentionally omitted

                    // Reparent to the Puff's attachment node so it moves with Puff
                    _carryingPuff = body;
                    // Defer the actual reparenting to avoid modifying the scene tree during physics callbacks
                    _carryingPuff = body;
                    _pendingAttachPuff = _carryingPuff;
                    CallDeferred(nameof(DeferredAttachToPuff));

                    // Reset local position so it appears attached to Puff
                    Position = Vector2.Zero;

                    // Disable physics while attached
                    if (_rigidBody != null)
                    {
                        // Freeze physics while attached to Puff
                        _rigidBody.Freeze = true;
                        _rigidBody.GravityScale = 0.0f;
                        _rigidBody.LinearVelocity = Vector2.Zero;
                    }
                }

                // Do not allow Puff to consume apples that are still on the tree
                return;
            }

            // If apple is on ground and in YellowEnergy state it is collectible by Puff
            if (_currentPosition == ApplePosition.OnGround && _currentState == AppleState.YellowEnergy)
            {
                RestoreEnergyToPuff();
            }

            return;
        }

        // Non-Puff bodies are ignored here
    }

    public void MakeAppleFall()
    {
        if (_isFalling) 
        {
            return;
        }
        
        _isFalling = true;
        
        // Use call_deferred to change physics properties safely during collision detection
        CallDeferred(nameof(DeferredMakeAppleFall));
    }

    private void DeferredAttachToPuff()
    {
        if (_pendingAttachPuff == null)
            return;

        // Remove from original parent safely (this is now deferred, outside physics callback)
        if (_originalParent != null)
        {
            try { _originalParent.RemoveChild(this); } catch { }
        }

        // Attach to Puff's attach node if present
        Node2D attachNode = _pendingAttachPuff.GetNodeOrNull<Node2D>("PuffAppleAttached");
        if (attachNode != null)
        {
            attachNode.AddChild(this);
            Position = Vector2.Zero;
        }
        else
        {
            _pendingAttachPuff.AddChild(this);
            Position = Vector2.Zero;
        }

        // Disable physics while attached
        if (_rigidBody != null)
        {
            _rigidBody.Freeze = true;
            _rigidBody.GravityScale = 0.0f;
            _rigidBody.LinearVelocity = Vector2.Zero;
        }

        _carryingPuff = _pendingAttachPuff;
        _isAttachedToPuff = true;
        _pendingAttachPuff = null;
    }

    private void DeferredDetachFromPuff()
    {
        if (_pendingDetachPuff == null)
            return;

        var puffNode = _pendingDetachPuff;

        // Record the apple's current global position before reparenting so it can be restored
        Vector2 detachGlobalPos = GlobalPosition;

        // If parent is the puff or its attach node, remove the apple safely
        // Try to get the SceneTree and target root BEFORE removing the node from its parent.
        var tree = GetTree();
        if (tree == null)
        {
            // SceneTree not available - clear state and abort but keep node safe
            Log.Warning("SceneTree missing during DeferredDetachFromPuff; clearing attachment state without reparent", "Apple");
            _isAttachedToPuff = false;
            _carryingPuff = null;
            _originalParent = null;
            _pendingDetachPuff = null;
            return;
        }

        // Determine a sensible root to reparent to (prefer current scene)
        Node root = tree.GetCurrentScene();
        if (root == null)
            root = tree.Root;

        // If parent is the puff or its attach node, remove the apple safely
        try { if (GetParent() != null) GetParent().RemoveChild(this); } catch { }

        // Reparent to the determined root if not already parented there.
        try
        {
            if (GetParent() == null && root != null)
            {
                root.AddChild(this);
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning($"Failed to reparent apple during DeferredDetachFromPuff: {ex.Message}", "Apple");
        }

        // Restore the apple to the position it had while attached so it drops exactly where Puff was
        GlobalPosition = detachGlobalPos;

        // Clear attachment state and original parent reference
        _isAttachedToPuff = false;
        _carryingPuff = null;
        _originalParent = null;
        _pendingDetachPuff = null;

        // Trigger normal fall behaviour so the apple becomes a ground apple
        MakeAppleFall();
    }
    
    private void DeferredMakeAppleFall()
    {
        // Ensure the RigidBody2D can move and respond to physics
        _rigidBody.Freeze = false; // Unfreeze to allow physics
        _rigidBody.GravityScale = 1.0f; // Enable gravity
        
        // Change position to OnGround and state to GrayNotCollectible when apple falls
        _currentPosition = ApplePosition.OnGround;
        _currentState = AppleState.GrayNotCollectible;
        ApplyStateVisualEffects();
        
        // Add some impulse for more dramatic effect
        Vector2 randomImpulse = new Vector2(
            (float)GD.RandRange(-50, 50), 
            (float)GD.RandRange(-20, 0)
        );
        _rigidBody.ApplyImpulse(randomImpulse);
    }

    public void OnGroundLanding()
    {
        _hasLanded = true;
        
        // Disable RigidBody2D physics
        _rigidBody.GravityScale = 0.0f;
        _rigidBody.LinearVelocity = Vector2.Zero;
        _rigidBody.AngularVelocity = 0.0f;
        _rigidBody.Freeze = true;
        
        // Disable physical collision with player (keep only detection area)
    var physicsCollision = NodeHelpers.GetNodeOrNull<CollisionShape2D>(_rigidBody, null, "PhysicsCollision");
        if (physicsCollision != null)
        {
            physicsCollision.Disabled = true;
        }
    }

    private void UpdatePromptDisplay()
    {
        if (!_playerNearby)
        {
            _promptLabel.Hide();
            return;
        }

        // Only show prompts for red and purple states AND only when apple is on ground
        bool isCollectibleState = _currentState == AppleState.RedLife || _currentState == AppleState.PurpleMana;
        bool isOnGround = _currentPosition == ApplePosition.OnGround;
        
        if (isCollectibleState && isOnGround)
        {
            string promptText = _currentState switch
            {
                AppleState.RedLife => "Press E (Life)",
                AppleState.PurpleMana => "Press E (Mana)",
                _ => "Press E"
            };
            _promptLabel.Text = promptText;
            _promptLabel.Show();
        }
        else
        {
            _promptLabel.Hide();
        }
    }
    #endregion

    #region Public API
    public void SetTransitionTimeRange(float minTime, float maxTime)
    {
        MinTransitionTime = minTime;
        MaxTransitionTime = maxTime;
        // Restart timer with new random time if currently in a transition phase
        if (_transitionTimer != null && (_currentState == AppleState.RedLife || _currentState == AppleState.PurpleMana))
        {
            _transitionTimer.WaitTime = GetCurrentPhaseTime();
        }
    }

    public void SetNeutralPhaseTimeRange(float minTime, float maxTime)
    {
        MinNeutralPhaseTime = minTime;
        MaxNeutralPhaseTime = maxTime;
        // Restart timer with new random time if currently in a neutral phase
        if (_transitionTimer != null && (_currentState == AppleState.GrayKnockable || _currentState == AppleState.GrayNotCollectible))
        {
            _transitionTimer.WaitTime = GetCurrentPhaseTime();
        }
    }

    public bool IsCollectible() => _currentState == AppleState.RedLife || _currentState == AppleState.PurpleMana;
    
    public bool IsCollectibleByPuff() => _currentState == AppleState.YellowEnergy;
    
    public AppleState GetCurrentState() => _currentState;
    
    public ApplePosition GetCurrentPosition() => _currentPosition;
    
    public bool IsInLifePhase() => _currentState == AppleState.RedLife;
    
    public bool IsInManaPhase() => _currentState == AppleState.PurpleMana;
    
    public bool IsInEnergyPhase() => _currentState == AppleState.YellowEnergy;
    
    public bool IsOnTree() => _currentPosition == ApplePosition.OnTree;
    
    public bool IsOnGround() => _currentPosition == ApplePosition.OnGround;
    
    public bool CanFallFromRockHit() => _currentState == AppleState.GrayKnockable && _currentPosition == ApplePosition.OnTree && !_isFalling;
    
    public bool IsFalling() => _isFalling;
    #endregion
}
