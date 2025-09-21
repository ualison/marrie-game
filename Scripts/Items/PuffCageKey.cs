using Godot;
using MarrieGame.Core.Logging;

/// <summary>
/// PuffCageKey item that behaves similar to rocks in the puzzle system.
/// Initially invisible and becomes visible when puzzle crystals are active.
/// Uses physics simulation with RigidBody2D for natural falling behavior.
/// Can be picked up by the player when nearby.
/// </summary>
public partial class PuffCageKey : Node2D
{
    #region Private Variables
    private RigidBody2D _rigidBody;
    private Sprite2D _keySprite;
    private CollisionShape2D _collisionShape;
    private Area2D _detectionArea;
    private Label _promptLabel;
    private bool _playerNearby = false;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        InitializeComponents();
        SetupPickupDetection();
        Log.System("PuffCageKey ready", "PuffCageKey");
    }

    private void InitializeComponents()
    {
        // Get components from the scene
        _rigidBody = GetNode<RigidBody2D>("RigidBody2D");
        _keySprite = GetNode<Sprite2D>("RigidBody2D/KeySprite");
        _collisionShape = GetNode<CollisionShape2D>("RigidBody2D/CollisionShape2D");
        // Support both scene layouts: DetectionArea as a sibling of RigidBody2D or as its child
        _detectionArea = GetNodeOrNull<Area2D>("RigidBody2D/DetectionArea")
            ?? GetNodeOrNull<Area2D>("DetectionArea");
        // Support both layouts for PromptLabel too
        _promptLabel = GetNodeOrNull<Label>("RigidBody2D/PromptLabel")
            ?? GetNodeOrNull<Label>("PromptLabel");

        if (_rigidBody == null)
        {
            Log.Error("PuffCageKey: RigidBody2D not found in scene", "PuffCageKey");
        }

        if (_keySprite == null)
        {
            Log.Error("PuffCageKey: KeySprite not found in scene", "PuffCageKey");
        }

        if (_collisionShape == null)
        {
            Log.Error("PuffCageKey: CollisionShape2D not found in scene", "PuffCageKey");
        }

        if (_detectionArea == null)
        {
            Log.Error("PuffCageKey: DetectionArea not found in scene", "PuffCageKey");
        }

        if (_promptLabel == null)
        {
            Log.Error("PuffCageKey: PromptLabel not found in scene", "PuffCageKey");
        }
    }

    private void SetupPickupDetection()
    {
        if (_detectionArea != null)
        {
            // Ensure monitoring is enabled
            _detectionArea.Monitoring = true;
            _detectionArea.Monitorable = false;
            
            // Connect area detection signals
            _detectionArea.BodyEntered += OnPlayerEntered;
            _detectionArea.BodyExited += OnPlayerExited;
            _detectionArea.AreaEntered += OnAreaEntered;
            _detectionArea.AreaExited += OnAreaExited;
            
            Log.Debug($"PuffCageKey: DetectionArea setup complete. Monitoring: {_detectionArea.Monitoring}, CollisionMask: {_detectionArea.CollisionMask}", "PuffCageKey");
        }

        if (_promptLabel != null)
        {
            // Setup prompt label appearance
            _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _promptLabel.Text = "Press E";
            _promptLabel.Hide(); // Initially hidden
            Log.Debug("PuffCageKey: PromptLabel setup complete and hidden", "PuffCageKey");
        }

        // Enable input processing for 'E' key detection
        SetProcess(true);
        Log.Debug("PuffCageKey: Input processing enabled", "PuffCageKey");
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        // Only process input if player is nearby
        if (_playerNearby && Input.IsActionJustPressed("interact"))
        {
            Log.Debug("PuffCageKey: E key pressed while player nearby - collecting key", "PuffCageKey");
            HandleKeyCollection();
        }
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        Log.Debug($"PuffCageKey: Body entered detection area: {body.Name}, Groups: {string.Join(",", body.GetGroups())}", "PuffCageKey");
        
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            if (_promptLabel != null)
            {
                _promptLabel.Show();
                Log.Debug("PuffCageKey: PromptLabel shown", "PuffCageKey");
            }
            Log.UI("Player nearby key - showing pickup prompt", "PuffCageKey");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        Log.Debug($"PuffCageKey: Body exited detection area: {body.Name}", "PuffCageKey");
        
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
                Log.Debug("PuffCageKey: PromptLabel hidden", "PuffCageKey");
            }
            Log.UI("Player left key - hiding pickup prompt", "PuffCageKey");
        }
    }

    // Handle when player's Area2D (like HurtBox) enters
    private void OnAreaEntered(Area2D area)
    {
        try
        {
            var parent = area.GetParent();
            if (parent != null && parent.IsInGroup("player"))
            {
                _playerNearby = true;
                _promptLabel?.Show();
                Log.Debug("PuffCageKey: Player area entered - PromptLabel shown", "PuffCageKey");
            }
        }
        catch { }
    }

    private void OnAreaExited(Area2D area)
    {
        try
        {
            var parent = area.GetParent();
            if (parent != null && parent.IsInGroup("player"))
            {
                _playerNearby = false;
                _promptLabel?.Hide();
                Log.Debug("PuffCageKey: Player area exited - PromptLabel hidden", "PuffCageKey");
            }
        }
        catch { }
    }
    #endregion

    #region Key Collection
    private void HandleKeyCollection()
    {
        Log.Debug("Collecting PuffCageKey - adding to inventory", "PuffCageKey");

        // Find inventory and add key
        var inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;

        if (inventory != null)
        {
            if (inventory.AddKey())
            {
                Log.Debug($"Key added to inventory. Total keys: {inventory.GetKeyCount()}", "PuffCageKey");
                DestroyKey();
            }
            else
            {
                Log.Warning("Failed to add key to inventory", "PuffCageKey");
            }
        }
        else
        {
            Log.Warning("Inventory not found", "PuffCageKey");
        }
    }

    private void DestroyKey()
    {
        Log.UI("PuffCageKey collected and destroyed", "PuffCageKey");
        QueueFree();
    }
    #endregion

    #region Physics Management
    /// <summary>
    /// Re-enable physics for the key (useful when key becomes visible after being hidden)
    /// Similar to Rock.ReEnablePhysics()
    /// </summary>
    public void ReEnablePhysics()
    {
        if (_rigidBody != null)
        {
            // Reset sleeping state and ensure physics updates
            _rigidBody.SetSleeping(false);
            _rigidBody.LinearVelocity = Vector2.Zero; // Reset any existing velocity
            _rigidBody.AngularVelocity = 0.0f; // Reset rotation
            
            // Enable physics if it was disabled
            _rigidBody.Freeze = false;
            
            // Add a tiny downward impulse to kickstart physics
            CallDeferred(nameof(ApplyGravityImpulse));
            
            Log.System("PuffCageKey physics re-enabled", "PuffCageKey");
        }
    }

    private void ApplyGravityImpulse()
    {
        if (_rigidBody != null && !_rigidBody.Freeze)
        {
            // Apply a small downward impulse to ensure gravity kicks in
            _rigidBody.ApplyImpulse(new Vector2(0, 1.0f));
        }
    }

    /// <summary>
    /// Disable physics for the key (useful when hiding the key)
    /// </summary>
    public void DisablePhysics()
    {
        if (_rigidBody != null)
        {
            _rigidBody.Freeze = true;
            _rigidBody.LinearVelocity = Vector2.Zero;
            _rigidBody.AngularVelocity = 0.0f;
            Log.System("PuffCageKey physics disabled", "PuffCageKey");
        }
    }

    /// <summary>
    /// Debug method to log the current physics state of the key
    /// </summary>
    public void LogPhysicsState()
    {
        if (_rigidBody != null)
        {
            Log.System($"PuffCageKey Physics State - Frozen: {_rigidBody.Freeze}, Gravity: {_rigidBody.GravityScale}, Layer: {_rigidBody.CollisionLayer}, Mask: {_rigidBody.CollisionMask}, Sleeping: {_rigidBody.Sleeping}, Position: {GlobalPosition}", "PuffCageKey");
        }
    }
    #endregion
}