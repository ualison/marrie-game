using Godot;

/// <summary>
/// Represents a thrown rock projectile that follows a predetermined trajectory
/// </summary>
public partial class ThrownRock : Node2D, IPoolable
{
    #region Exported Properties
    [Export] public float TravelSpeed { get; set; } = 300.0f;
    [Export] public Texture2D RockTexture { get; set; }
    #endregion

    #region Components
    private Sprite2D _sprite;
    private Area2D _collisionArea; // For collision detection with other objects
    #endregion

    // Shared rock texture to reduce repeated resource loads
    private static Texture2D _sharedRockTexture;

    #region Trajectory State
    private Vector2[] _trajectoryPoints;
    private int _currentPointIndex = 0;
    private float _travelProgress = 0.0f;
    private bool _isMoving = false;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        SetupSprite();
        SetupCollisionArea();
        LoadRockTexture();
        Log.System("ThrownRock ready", "ThrownRock");
    }

    private void SetupSprite()
    {
        _sprite = new Sprite2D();
        _sprite.Scale = new Vector2(0.05f, 0.05f); // Small scale for thrown rock
        AddChild(_sprite);
    }

    private void SetupCollisionArea()
    {
        // Create Area2D for collision detection with other objects (like apples)
        _collisionArea = new Area2D();
        var collisionShape = new CollisionShape2D();
        var shape = new CircleShape2D();
        shape.Radius = 10.0f; // Small collision radius for the rock
        collisionShape.Shape = shape;
        
        // Set collision layers - rock should be on layer 8 to be detected by apples
    _collisionArea.CollisionLayer = 8; // Rock collision layer (matches Apple's RockDetectionArea mask)
    _collisionArea.CollisionMask = 1 | 4 | 16;  // Detect ground (1), apples (4), monsters (16) as appropriate
    _collisionArea.Monitoring = true;
    _collisionArea.Monitorable = true;
    _collisionArea.AreaEntered += OnCollisionAreaEntered;
    _collisionArea.BodyEntered += OnCollisionBodyEntered;
        
    AddChild(_collisionArea);
    _collisionArea.AddChild(collisionShape);
        
        // Add to rocks group for easy identification
        AddToGroup("rocks");
    // Ensure movement runs in physics step so physics monitoring/integration works correctly
    SetPhysicsProcess(true);
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System("ThrownRock collision area set up on layer 8", "ThrownRock");
    }

    private void LoadRockTexture()
    {
        if (RockTexture == null)
        {
            if (_sharedRockTexture == null)
                _sharedRockTexture = GD.Load<Texture2D>("res://Art/Rock.png");
            RockTexture = _sharedRockTexture;
        }
        
        if (RockTexture != null && _sprite != null)
        {
            _sprite.Texture = RockTexture;
        }
    }
    #endregion

    #region Movement
    public override void _PhysicsProcess(double delta)
    {
        // Run movement during the physics step so Area2D overlap events are detected reliably
        if (_isMoving && _trajectoryPoints != null && _trajectoryPoints.Length > 1)
        {
            MoveAlongTrajectory((float)delta);
        }
    }

    private void MoveAlongTrajectory(float delta)
    {
        if (_currentPointIndex >= _trajectoryPoints.Length - 1)
        {
            OnTrajectoryComplete();
            return;
        }

        // Calculate movement between current and next point
        Vector2 currentPoint = _trajectoryPoints[_currentPointIndex];
        Vector2 nextPoint = _trajectoryPoints[_currentPointIndex + 1];
        
        float segmentDistance = currentPoint.DistanceTo(nextPoint);
        float moveDistance = TravelSpeed * delta;
        
        _travelProgress += moveDistance / segmentDistance;
        
        if (_travelProgress >= 1.0f)
        {
            // Move to next segment
            _currentPointIndex++;
            _travelProgress = 0.0f;
            
            if (_currentPointIndex < _trajectoryPoints.Length)
            {
                Vector2 newPosition = _trajectoryPoints[_currentPointIndex];
                
                // Rely on Area2D signals for collision detection; simply move the rock
                GlobalPosition = newPosition;
            }
        }
        else
        {
            // Interpolate between current and next point
            Vector2 interpolatedPosition = currentPoint.Lerp(nextPoint, _travelProgress);
            
            // Move without per-point physics checks; Area2D will notify if we hit something
            GlobalPosition = interpolatedPosition;
        }
        
        // Rotate sprite based on movement direction
        if (_currentPointIndex < _trajectoryPoints.Length - 1)
        {
            Vector2 direction = (_trajectoryPoints[_currentPointIndex + 1] - GlobalPosition).Normalized();
            _sprite.Rotation = direction.Angle();
        }
    }

    private void OnRockHitSomething(Vector2 hitPosition)
    {
        _isMoving = false;
        GlobalPosition = hitPosition;
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System("ThrownRock hit something during flight", "ThrownRock");
        
        // Create impact effect and remove rock
        CreateImpactEffect();
    }

    private void OnCollisionAreaEntered(Area2D area)
    {
        // Delegate handling to the same logic used elsewhere (or treat generically)
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System($"ThrownRock collided with area: {area.Name}", "ThrownRock");

        // If this area corresponds to an Apple or has the apple layer, try to trigger fall
        if ((area.CollisionLayer & 4) != 0)
        {
            Node current = area;
            Apple apple = null;
            while (current != null && apple == null)
            {
                apple = current as Apple;
                current = current.GetParent();
            }

            if (apple != null)
            {
                if (apple.CanFallFromRockHit())
                {
                    Log.System($"ThrownRock triggered apple fall for: {apple.Name}", "ThrownRock");
                    apple.MakeAppleFall();
                }
                else
                {
                    Log.System($"ThrownRock hit apple but it cannot fall now: {apple.Name}", "ThrownRock");
                }
            }
        }

        // Stop movement and create impact
        _isMoving = false;
        CreateImpactEffect();
    }

    private void OnCollisionBodyEntered(Node2D body)
    {
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System($"ThrownRock collided with body: {body.Name} ({body.GetType().Name})", "ThrownRock");

        if (body is GhostMonster ghost)
        {
            ghost.TakeRockDamage(1);
        }
        else if (body is Monster monster)
        {
            monster.TakeRockDamage(1);
        }

        _isMoving = false;
        CreateImpactEffect();
    }

    private void OnTrajectoryComplete()
    {
        _isMoving = false;
        Log.System("ThrownRock reached end of trajectory (landed on ground)", "ThrownRock");
        
        // Add some visual effect or impact for ground landing
        CreateImpactEffect();
    }

    private void CreateImpactEffect()
    {
        // Simple visual feedback - could be expanded later
        var tween = CreateTween();
        tween.TweenProperty(_sprite, "modulate", new Color(1, 1, 1, 0), 0.3f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (IsFromPool)
                PoolManager.Release(this);
            else
                QueueFree();
        }));
    }
    #endregion

    // Pooling support
    public bool IsFromPool { get; set; } = false;

    public void ResetForReuse()
    {
        // Reset runtime state
        _isMoving = false;
        _trajectoryPoints = null;
        _currentPointIndex = 0;
        _travelProgress = 0f;
        Visible = true;
        Show();
    }

    public void OnReleasedToPool()
    {
        // Called when returned to pool - cleanup signals if any
        _isMoving = false;
        try { if (_collisionArea != null) { _collisionArea.AreaEntered -= OnCollisionAreaEntered; _collisionArea.BodyEntered -= OnCollisionBodyEntered; } } catch { }
    }

    #region Public API
    /// <summary>
    /// Set the trajectory points for the rock to follow
    /// </summary>
    /// <param name="points">Array of world positions defining the path</param>
    public void SetTrajectory(Vector2[] points)
    {
        if (points == null || points.Length < 2)
        {
            Log.Warning("Invalid trajectory points provided", "ThrownRock");
            QueueFree();
            return;
        }

        _trajectoryPoints = points;
        _currentPointIndex = 0;
        _travelProgress = 0.0f;
        
        // Start at the first point
        GlobalPosition = _trajectoryPoints[0];
        
        // Begin movement
        _isMoving = true;
        
        Log.System($"ThrownRock trajectory set with {points.Length} points", "ThrownRock");
    }

    /// <summary>
    /// Get the current trajectory progress (0-1)
    /// </summary>
    /// <returns>Progress value</returns>
    public float GetTrajectoryProgress()
    {
        if (_trajectoryPoints == null || _trajectoryPoints.Length == 0)
            return 0.0f;
            
        return (float)_currentPointIndex / (_trajectoryPoints.Length - 1);
    }

    /// <summary>
    /// Check if the rock is currently moving
    /// </summary>
    /// <returns>True if moving along trajectory</returns>
    public bool IsMoving() => _isMoving;
    #endregion
}
