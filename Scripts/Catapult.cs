using Godot;
using MarrieGame.Core.Interfaces;

/// <summary>
/// Catapult that can be pushed by the player when pressing C key
/// Features rolling wheels and collision detection
/// 
/// How to use:
/// 1. Place this script on a RigidBody2D node
/// 2. Ensure the catapult has collision shapes
/// 3. Player approaches the catapult (green tint indicates range)
/// 4. Player holds C key to push (yellow tint indicates pushing)
/// 5. Wheels rotate based on movement direction and speed
/// 6. When not pressing C, player will be gently pushed away from catapult
/// </summary>
public partial class Catapult : RigidBody2D, IPausable
{
    #region Exported Properties
    [Export] public float PushForce = 300.0f;
    [Export] public float WheelRotationSpeed = 200.0f;
    [Export] public NodePath WheelLeftPath = "WheelLeft";
    [Export] public NodePath WheelRightPath = "WheelRight";
    [Export] public NodePath DetectionAreaPath = "DetectionArea";
    #endregion

    #region Components
    private Sprite2D _wheelLeft;
    private Sprite2D _wheelRight;
    private CollisionShape2D _collisionShape;
    private Area2D _detectionArea;
    private Player _player;
    private bool _isPaused = false;
    #endregion

    #region State
    private bool _isBeingPushed = false;
    private Vector2 _lastPosition;
    private float _currentSpeed = 0.0f;
    private bool _playerInRange = false;
    private Color _originalModulate = Colors.White;
    #endregion

    public override void _Ready()
    {
        // Add to group for identification
        AddToGroup("catapults");
        
        // Store original color for visual feedback
        _originalModulate = Modulate;
        
        // Initialize components
        InitializeComponents();
        InitializePhysics();
        
        // Store initial position for speed calculation
        _lastPosition = GlobalPosition;
        
        Log.Info($"Catapult initialized at position {GlobalPosition}", "Catapult");
        Log.Debug($"Detection area found: {_detectionArea != null}", "Catapult");
        Log.Debug($"Wheels found - Left: {_wheelLeft != null}, Right: {_wheelRight != null}", "Catapult");
    }

    private void InitializeComponents()
    {
        // Find wheel sprites
        _wheelLeft = GetNodeOrNull<Sprite2D>(WheelLeftPath);
        _wheelRight = GetNodeOrNull<Sprite2D>(WheelRightPath);
        
        if (_wheelLeft == null)
            Log.Warning("Left wheel not found at path: " + WheelLeftPath, "Catapult");
        if (_wheelRight == null)
            Log.Warning("Right wheel not found at path: " + WheelRightPath, "Catapult");
            
        // Find collision shape
        _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        if (_collisionShape == null)
        {
            Log.Warning("No collision shape found on Catapult", "Catapult");
        }
        
        // Find detection area
        _detectionArea = GetNodeOrNull<Area2D>(DetectionAreaPath);
        if (_detectionArea == null)
        {
            Log.Warning("DetectionArea not found at path: " + DetectionAreaPath, "Catapult");
            CreateDetectionArea();
        }
        else
        {
            // Connect signals for existing detection area
            ConnectDetectionAreaSignals();
            // Configure collision for existing detection area
            ConfigureDetectionAreaCollision();
        }
    }

    private void CreateDetectionArea()
    {
        // Create detection area for player proximity
        _detectionArea = new Area2D { Name = "DetectionArea" };
        AddChild(_detectionArea);
        
        // Create collision shape for detection
        var detectionShape = new CollisionShape2D();
        var shape = new RectangleShape2D();
        shape.Size = new Vector2(500, 300); // Larger than catapult for detection
        detectionShape.Shape = shape;
        
        _detectionArea.AddChild(detectionShape);
        
        // Connect signals and configure collision
        ConnectDetectionAreaSignals();
        ConfigureDetectionAreaCollision();
    }

    private void ConnectDetectionAreaSignals()
    {
        if (_detectionArea == null) return;
        
        // Connect signals safely - using new Godot 4 syntax
        _detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
        _detectionArea.BodyExited += OnDetectionAreaBodyExited;
        
        Log.Debug("Connected DetectionArea signals", "Catapult");
    }

    private void ConfigureDetectionAreaCollision()
    {
        if (_detectionArea == null) return;
        
        // Set detection area to layer 10 (unused) and detect player on layer 2
        _detectionArea.SetCollisionLayerValue(10, true);  // Put detection area on layer 10
        _detectionArea.SetCollisionMaskValue(2, true);    // Detect player on layer 2
        
        Log.Debug("Detection area collision configured: layer 10, detecting layer 2", "Catapult");
    }

    private void InitializePhysics()
    {
        // Set up RigidBody2D properties for realistic physics
        GravityScale = 1.0f;
        LinearDamp = 0.5f; // Some damping to prevent infinite sliding
        AngularDamp = 2.0f; // Prevent excessive rotation
        
        // Set collision layers - catapults on layer 3, detect ground (layer 1) and player (layer 2)
        SetCollisionLayerValue(3, true);    // Catapult is on layer 3
        SetCollisionMaskValue(1, true);     // Collide with ground (layer 1) 
        SetCollisionMaskValue(2, true);     // Collide with player (layer 2)
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isPaused) return;
        
        UpdateMovementState();
        HandlePlayerInteraction();
        UpdateWheelRotation((float)delta);
        UpdateVisualFeedback();
    }

    private void UpdateMovementState()
    {
        // Calculate current speed based on position change
        Vector2 currentPosition = GlobalPosition;
        Vector2 movement = currentPosition - _lastPosition;
        _currentSpeed = movement.Length() / (float)GetPhysicsProcessDeltaTime();
        _lastPosition = currentPosition;
    }

    private void HandlePlayerInteraction()
    {
        if (_player == null || !_playerInRange) return;
        
        // Check if player is pressing push button
        bool wantsToPush = Input.IsActionPressed("push_catapult");
        
        // Debug log state changes
        if (wantsToPush && !_isBeingPushed)
        {
            Log.Info("Player started pushing catapult", "Catapult");
        }
        else if (!wantsToPush && _isBeingPushed)
        {
            Log.Info("Player stopped pushing catapult", "Catapult");
        }
        
        _isBeingPushed = wantsToPush;
        
        if (_isBeingPushed)
        {
            ApplyPushForce();
        }
    }

    private void ApplyPushForce()
    {
        if (_player == null) return;
        
        // Calculate push direction based on player position relative to catapult
        Vector2 playerPos = _player.GlobalPosition;
        Vector2 catapultPos = GlobalPosition;
        
        // Determine push direction: if player is to the left, push right, and vice versa
        Vector2 pushDirection;
        if (playerPos.X < catapultPos.X)
        {
            pushDirection = Vector2.Right;
        }
        else
        {
            pushDirection = Vector2.Left;
        }
        
        // Apply the force
        ApplyForce(pushDirection * PushForce);
        
        // Add a small upward component to prevent getting stuck on ground
        ApplyForce(Vector2.Up * PushForce * 0.1f);
    }

    private void UpdateWheelRotation(float delta)
    {
        if (_currentSpeed < 0.1f) return; // Don't rotate wheels if not moving
        
        // Calculate rotation based on movement speed and direction
        float rotationAmount = (_currentSpeed * delta * WheelRotationSpeed) / 100.0f;
        
        // Determine rotation direction based on movement
        Vector2 velocity = LinearVelocity;
        if (velocity.X < 0) // Moving left
            rotationAmount = -rotationAmount;
            
        // Apply rotation to both wheels
        if (_wheelLeft != null)
            _wheelLeft.Rotation += rotationAmount;
        if (_wheelRight != null)
            _wheelRight.Rotation += rotationAmount;
    }

    private void UpdateVisualFeedback()
    {
        // Change color when being pushed
        if (_isBeingPushed)
        {
            // Slight yellow tint when being pushed
            Modulate = Colors.LightYellow;
        }
        else if (_playerInRange)
        {
            // Slight green tint when player is in range
            Modulate = Colors.LightGreen;
        }
        else
        {
            // Normal color
            Modulate = _originalModulate;
        }
    }

    #region Detection Area Events
    private void OnDetectionAreaBodyEntered(Node2D body)
    {
        Log.Debug($"Body entered detection area: {body.Name} (Type: {body.GetType().Name})", "Catapult");
        
        if (body is Player player)
        {
            _player = player;
            _playerInRange = true;
            Log.Info("Player entered catapult detection area", "Catapult");
        }
    }

    private void OnDetectionAreaBodyExited(Node2D body)
    {
        Log.Debug($"Body exited detection area: {body.Name} (Type: {body.GetType().Name})", "Catapult");
        
        if (body is Player)
        {
            _player = null;
            _playerInRange = false;
            _isBeingPushed = false;
            Log.Info("Player left catapult detection area", "Catapult");
        }
    }
    #endregion

    #region Public API
    public bool IsBeingPushed() => _isBeingPushed;
    public float GetCurrentSpeed() => _currentSpeed;
    public bool IsPlayerInRange() => _playerInRange;
    #endregion

    #region IPausable Implementation
    public bool IsPaused => _isPaused;
    
    public void Pause()
    {
        _isPaused = true;
        // Stop all physics when paused
        Freeze = true;
    }
    
    public void Resume()
    {
        _isPaused = false;
        Freeze = false;
    }
    #endregion
}