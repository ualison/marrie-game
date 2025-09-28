using Godot;
using MarrieGame.Core.Logging;

/// <summary>
/// YellowCage that responds to player collision by applying force.
/// Uses physics simulation with RigidBody2D for natural swinging behavior.
/// </summary>
public partial class YellowCage : RigidBody2D
{
    #region Exported Properties
    [Export] public float ForceMultiplier = 300.0f; // How much force to apply when player collides
    [Export] public float MinimumForce = 100.0f;    // Minimum force to apply
    [Export] public float MaximumForce = 500.0f;    // Maximum force to apply
    [Export] public bool DetachFromChainOnUnlock = true; // Whether cage should detach and fall when unlocked
    #endregion

    #region Private Variables
    private Area2D _detectionArea;
    private CollisionShape2D _detectionCollision;
    private bool _playerInside = false;
    private Player _lastCollidingPlayer;
    private Label _promptLabel;
    private bool _isUnlocked = false;
    private PinJoint2D _chainConnection;
    private bool _isDetachedFromChain = false;
    private Sprite2D _puffPrisoner;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        InitializeComponents();
        SetupCollisionDetection();
        
        Log.Debug($"YellowCage ready: {Name}", "YellowCage");
    }

    private void InitializeComponents()
    {
        // Find the detection area that should be added in the scene
        _detectionArea = GetNodeOrNull<Area2D>("DetectionArea");
        
        // Find the prompt label
        _promptLabel = GetNodeOrNull<Label>("PromptLabel");
        if (_promptLabel != null)
        {
            _promptLabel.Hide(); // Start hidden
            Log.Debug("YellowCage: PromptLabel found and hidden", "YellowCage");
        }
        else
        {
            Log.Warning("YellowCage: PromptLabel not found in scene", "YellowCage");
        }
        
        // Find the PuffPrisoner sprite
        _puffPrisoner = GetNodeOrNull<Sprite2D>("PuffPresioner");
        if (_puffPrisoner != null)
        {
            Log.Debug("YellowCage: PuffPresioner sprite found", "YellowCage");
        }
        else
        {
            Log.Warning("YellowCage: PuffPresioner sprite not found in scene", "YellowCage");
        }
        
        // Find the chain connection (PinJoint2D that connects this cage to the chain)
        var parentNode = GetParent();
        if (parentNode != null)
        {
            // Look for PinJoint2D that has this cage as node_b
            foreach (Node child in parentNode.GetChildren())
            {
                if (child is PinJoint2D joint)
                {
                    // Get the actual node that NodeB points to
                    var nodeBPath = joint.NodeB;
                    if (!nodeBPath.IsEmpty)
                    {
                        var nodeB = joint.GetNodeOrNull(nodeBPath);
                        if (nodeB == this)
                        {
                            _chainConnection = joint;
                            Log.Debug($"YellowCage: Chain connection found: {joint.Name}", "YellowCage");
                            break;
                        }
                    }
                }
            }
        }
        
        if (_chainConnection == null)
        {
            Log.Warning("YellowCage: Chain connection (PinJoint2D) not found", "YellowCage");
        }
        
        if (_detectionArea != null)
        {
            _detectionCollision = _detectionArea.GetNodeOrNull<CollisionShape2D>("DetectionCollision");
        }
        else
        {
            Log.Warning("YellowCage: DetectionArea not found in scene", "YellowCage");
        }
    }

    private void SetupCollisionDetection()
    {
        if (_detectionArea != null)
        {
            // Connect signals for player detection
            _detectionArea.BodyEntered += OnPlayerEntered;
            _detectionArea.BodyExited += OnPlayerExited;
            
            // Set collision layers appropriately
            // Detect player layer (layer 2 based on other scripts in the project)
            _detectionArea.CollisionMask = 2;
            _detectionArea.CollisionLayer = 0; // Don't interfere with other collision detection
            
            Log.Debug("YellowCage: Collision detection setup complete", "YellowCage");
        }
    }
    #endregion

    #region Collision Handling
    private void OnPlayerEntered(Node2D body)
    {
        // Only process player collisions
        if (body is not Player player)
        {
            return;
        }

        Log.Debug($"YellowCage: Player entered cage area: {body.Name}", "YellowCage");
        
        _playerInside = true;
        _lastCollidingPlayer = player;
        
        // Show prompt label when player enters and cage is not unlocked
        if (_promptLabel != null && !_isUnlocked)
        {
            _promptLabel.Show();
            Log.Debug("YellowCage: PromptLabel shown", "YellowCage");
        }
        
        // Apply force based on player's velocity and position
        ApplyForceFromPlayer(player);
    }

    private void OnPlayerExited(Node2D body)
    {
        if (body is not Player player)
        {
            return;
        }

        Log.Debug($"YellowCage: Player exited cage area: {body.Name}", "YellowCage");
        
        _playerInside = false;
        _lastCollidingPlayer = null;
        
        // Hide prompt label when player exits
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
            Log.Debug("YellowCage: PromptLabel hidden", "YellowCage");
        }
    }

    private void ApplyForceFromPlayer(Player player)
    {
        if (player == null || Freeze) return;

        // Calculate force direction based on player's position relative to cage
        Vector2 forceDirection = (GlobalPosition - player.GlobalPosition).Normalized();
        
        // If player is moving, factor in their velocity
        Vector2 playerVelocity = player.Velocity;
        float playerSpeed = playerVelocity.Length();
        
        // Calculate force magnitude based on player speed
        float forceMagnitude = Mathf.Clamp(
            ForceMultiplier + (playerSpeed * 0.5f),
            MinimumForce,
            MaximumForce
        );
        
        // Apply the force as an impulse
        Vector2 forceVector = forceDirection * forceMagnitude;
        
        // Apply impulse to the cage
        ApplyImpulse(forceVector);
        
        Log.Debug($"YellowCage: Applied force {forceVector} to cage from player collision", "YellowCage");
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        // Only process input if player is nearby and cage is not unlocked
        if (_playerInside && !_isUnlocked && Input.IsActionJustPressed("interact"))
        {
            Log.Debug("YellowCage: E key pressed while player nearby - attempting to unlock cage", "YellowCage");
            HandleCageUnlock();
        }
    }
    #endregion

    #region Physics Process
    public override void _PhysicsProcess(double delta)
    {
        // If player is still inside and moving, continuously apply small forces
        if (_playerInside && _lastCollidingPlayer != null)
        {
            // Apply continuous smaller force while player remains in contact
            ApplyContinuousForce(delta);
        }
    }

    private void ApplyContinuousForce(double delta)
    {
        if (_lastCollidingPlayer == null || Freeze) return;

        Vector2 playerVelocity = _lastCollidingPlayer.Velocity;
        
        // Only apply continuous force if player is moving significantly
        if (playerVelocity.Length() > 50.0f)
        {
            Vector2 forceDirection = (GlobalPosition - _lastCollidingPlayer.GlobalPosition).Normalized();
            float continuousForce = ForceMultiplier * 0.1f; // Much smaller continuous force
            
            Vector2 forceVector = forceDirection * continuousForce * (float)delta;
            ApplyCentralForce(forceVector);
        }
    }
    #endregion

    #region Chain Detachment System
    /// <summary>
    /// Detaches the cage from the chain, allowing it to fall under gravity
    /// </summary>
    public void DetachFromChain()
    {
        if (_isDetachedFromChain)
        {
            Log.Debug("YellowCage: Already detached from chain", "YellowCage");
            return;
        }

        // Safety check - only detach if the flag allows it
        if (!DetachFromChainOnUnlock)
        {
            Log.Warning("YellowCage: DetachFromChain() called but DetachFromChainOnUnlock is false", "YellowCage");
            return;
        }

        Log.Debug("YellowCage: Detaching from chain", "YellowCage");

        // Remove the chain connection
        if (_chainConnection != null)
        {
            Log.Debug($"YellowCage: Removing chain connection: {_chainConnection.Name}", "YellowCage");
            _chainConnection.QueueFree();
            Log.Debug("YellowCage: Chain connection removed", "YellowCage");
        }
        else
        {
            Log.Warning("YellowCage: No chain connection found to remove", "YellowCage");
        }

        // Enable gravity and physics if not already enabled
        GravityScale = 1.0f; // Ensure gravity affects the cage
        Freeze = false; // Make sure physics are enabled
        
        // Add a small random horizontal impulse for more natural falling
        Vector2 randomImpulse = new Vector2(
            (float)(GD.Randf() - 0.5f) * 50.0f, // Small random horizontal force
            0
        );
        ApplyImpulse(randomImpulse);

        _isDetachedFromChain = true;
        Log.Debug("YellowCage: Successfully detached from chain and falling", "YellowCage");
    }

    /// <summary>
    /// Checks if the cage is currently detached from the chain
    /// </summary>
    /// <returns>True if cage is detached and falling</returns>
    public bool IsDetachedFromChain()
    {
        return _isDetachedFromChain;
    }

    /// <summary>
    /// Checks if cage detachment is enabled
    /// </summary>
    /// <returns>True if DetachFromChainOnUnlock flag is enabled</returns>
    public bool IsDetachmentEnabled()
    {
        return DetachFromChainOnUnlock;
    }

    /// <summary>
    /// Can be called to manually trigger chain detachment (useful for other game systems)
    /// </summary>
    public void TriggerChainDetachment()
    {
        if (!_isUnlocked && DetachFromChainOnUnlock)
        {
            Log.Debug("YellowCage: Chain detachment triggered externally", "YellowCage");
            DetachFromChain();
        }
        else if (!DetachFromChainOnUnlock)
        {
            Log.Debug("YellowCage: Chain detachment triggered externally but DetachFromChainOnUnlock is false", "YellowCage");
        }
        else if (_isUnlocked)
        {
            Log.Debug("YellowCage: Chain detachment triggered externally but cage is already unlocked", "YellowCage");
        }
    }

    /// <summary>
    /// Forces chain detachment regardless of current settings or unlock state.
    /// This is used by the puzzle system when all crystals are active.
    /// The cage will still require a key to fade out and release the Puff.
    /// </summary>
    public void ForceChainDetachment()
    {
        if (_isDetachedFromChain)
        {
            Log.Debug("YellowCage: Force detachment called but cage is already detached", "YellowCage");
            return;
        }

        Log.Debug("YellowCage: Force detachment called by puzzle system", "YellowCage");

        // Remove the chain connection
        if (_chainConnection != null)
        {
            Log.Debug($"YellowCage: Force removing chain connection: {_chainConnection.Name}", "YellowCage");
            _chainConnection.QueueFree();
            Log.Debug("YellowCage: Chain connection force removed", "YellowCage");
        }
        else
        {
            Log.Warning("YellowCage: No chain connection found to force remove", "YellowCage");
        }

        // Enable gravity and physics if not already enabled
        GravityScale = 1.0f; // Ensure gravity affects the cage
        Freeze = false; // Make sure physics are enabled
        
        // Add a small random horizontal impulse for more natural falling
        Vector2 randomImpulse = new Vector2(
            (float)(GD.Randf() - 0.5f) * 50.0f, // Small random horizontal force
            0
        );
        ApplyImpulse(randomImpulse);

        _isDetachedFromChain = true;
        Log.Debug("YellowCage: Successfully force detached from chain and falling (puzzle system)", "YellowCage");
    }
    #endregion

    #region Cage Unlock System
    private void HandleCageUnlock()
    {
        // Find the inventory system
        var inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;
        
        if (inventory == null)
        {
            Log.Warning("YellowCage: Inventory not found", "YellowCage");
            return;
        }
        
        // Check if player has at least one key
        if (!inventory.HasKeys())
        {
            Log.Debug("YellowCage: Player doesn't have any keys", "YellowCage");
            return;
        }
        
        // Remove one key from inventory
        if (inventory.RemoveKey())
        {
            Log.Debug($"YellowCage: Key used. Remaining keys: {inventory.GetKeyCount()}", "YellowCage");
            
            // Mark cage as unlocked and start the unlock sequence
            _isUnlocked = true;
            
            // Hide prompt label immediately
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            
            // Start fade out and spawn process
            StartUnlockSequence();
        }
        else
        {
            Log.Warning("YellowCage: Failed to remove key from inventory", "YellowCage");
        }
    }

    private void StartUnlockSequence()
    {
        Log.Debug("YellowCage: Starting unlock sequence", "YellowCage");
        Log.Debug($"YellowCage: DetachFromChainOnUnlock = {DetachFromChainOnUnlock}", "YellowCage");
        
        // Only detach from chain if the flag is explicitly enabled
        if (DetachFromChainOnUnlock)
        {
            Log.Debug("YellowCage: Detachment enabled - calling DetachFromChain()", "YellowCage");
            DetachFromChain();
        }
        else
        {
            // Keep cage attached to chain, just disable physics to prevent swinging during fade
            Log.Debug("YellowCage: Detachment disabled - cage will fade out while remaining attached", "YellowCage");
            SetCagePhysicsEnabled(false);
        }
        
        // Start fade out tween
        FadeCageOut();
    }

    private void FadeCageOut()
    {
        Log.Debug("YellowCage: Starting fade out animation", "YellowCage");
        
        // Create a tween for smooth fade out
        var tween = CreateTween();
        tween.SetParallel(true); // Allow multiple properties to animate simultaneously
        
        // Fade out the entire cage by reducing modulate alpha
        tween.TweenProperty(this, "modulate", new Color(1, 1, 1, 0), 1.0f);
        
        // When fade completes, spawn Puff and destroy cage
        tween.TweenCallback(Callable.From(SpawnPuffAndDestroy)).SetDelay(1.0f);
    }

    private void SpawnPuffAndDestroy()
    {
        Log.Debug("YellowCage: Unlocking Puff globally and destroying cage", "YellowCage");
        
        // Notify PlayerSceneManager that Puff is now unlocked
        if (PlayerSceneManager.Instance != null)
        {
            PlayerSceneManager.Instance.UnlockPuff();
            Log.System("YellowCage: Puff unlocked globally via PlayerSceneManager", "YellowCage");
        }
        else
        {
            Log.Warning("YellowCage: PlayerSceneManager not found - falling back to local Puff spawn", "YellowCage");
            
            // Fallback to local spawn if PlayerSceneManager is not available
            SpawnLocalPuff();
        }
        
        // Destroy the cage
        QueueFree();
        Log.Debug("YellowCage: Cage destroyed", "YellowCage");
    }

    /// <summary>
    /// Fallback method to spawn Puff locally if PlayerSceneManager is not available
    /// </summary>
    private void SpawnLocalPuff()
    {
        // Load the Puff prefab
        var puffScene = GD.Load<PackedScene>("res://Prefabs/Puff.tscn");
        if (puffScene == null)
        {
            Log.Error("YellowCage: Failed to load Puff.tscn prefab", "YellowCage");
            return;
        }
        
        // Instantiate Puff
        var puffInstance = puffScene.Instantiate();
        if (puffInstance == null)
        {
            Log.Error("YellowCage: Failed to instantiate Puff prefab", "YellowCage");
            return;
        }
        
        // Get the parent node to add Puff to the same level as the cage
        var parent = GetParent();
        if (parent == null)
        {
            Log.Error("YellowCage: No parent found to spawn Puff", "YellowCage");
            puffInstance.QueueFree();
            return;
        }
        
        // Set Puff position to PuffPrisoner sprite position (or fallback to cage position)
        if (puffInstance is Node2D puffNode)
        {
            parent.AddChild(puffInstance);
            
            // Use PuffPrisoner sprite position if available, otherwise fallback to cage position
            if (_puffPrisoner != null)
            {
                puffNode.GlobalPosition = _puffPrisoner.GlobalPosition;
                Log.Debug($"YellowCage: Puff spawned locally at PuffPresioner position {puffNode.GlobalPosition}", "YellowCage");
            }
            else
            {
                puffNode.GlobalPosition = GlobalPosition;
                Log.Debug($"YellowCage: Puff spawned locally at cage position (PuffPresioner not found) {puffNode.GlobalPosition}", "YellowCage");
            }
        }
        else
        {
            Log.Warning("YellowCage: Puff prefab is not Node2D, adding anyway", "YellowCage");
            parent.AddChild(puffInstance);
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Enable/disable cage physics
    /// </summary>
    public void SetCagePhysicsEnabled(bool enabled)
    {
        // If cage is detached, don't freeze it (let it fall)
        if (_isDetachedFromChain && !enabled)
        {
            Log.Debug("YellowCage: Physics cannot be disabled while detached from chain", "YellowCage");
            return;
        }
        
        Freeze = !enabled;
        
        if (enabled)
        {
            Log.Debug("YellowCage: Physics enabled", "YellowCage");
        }
        else
        {
            Log.Debug("YellowCage: Physics disabled", "YellowCage");
        }
    }

    /// <summary>
    /// Apply a custom force to the cage
    /// </summary>
    public void ApplyCustomForce(Vector2 force)
    {
        if (!Freeze)
        {
            ApplyImpulse(force);
            Log.Debug($"YellowCage: Applied custom force {force}", "YellowCage");
        }
    }
    #endregion
}