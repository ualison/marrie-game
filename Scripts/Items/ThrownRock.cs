using Godot;

/// <summary>
/// Represents a thrown rock projectile that follows a predetermined trajectory
/// </summary>
public partial class ThrownRock : Node2D
{
    #region Exported Properties
    [Export] public float TravelSpeed { get; set; } = 300.0f;
    [Export] public Texture2D RockTexture { get; set; }
    #endregion

    #region Components
    private Sprite2D _sprite;
    #endregion

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
        LoadRockTexture();
        Log.System("ThrownRock ready", "ThrownRock");
    }

    private void SetupSprite()
    {
        _sprite = new Sprite2D();
        _sprite.Scale = new Vector2(0.05f, 0.05f); // Small scale for thrown rock
        AddChild(_sprite);
    }

    private void LoadRockTexture()
    {
        if (RockTexture == null)
        {
            // Load rock texture from resources
            RockTexture = GD.Load<Texture2D>("res://Art/Rock.png");
        }
        
        if (RockTexture != null && _sprite != null)
        {
            _sprite.Texture = RockTexture;
        }
    }
    #endregion

    #region Movement
    public override void _Process(double delta)
    {
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
                
                // Check for collision at the new position
                if (CheckCollisionAtPosition(newPosition))
                {
                    OnRockHitSomething(newPosition);
                    return;
                }
                
                GlobalPosition = newPosition;
            }
        }
        else
        {
            // Interpolate between current and next point
            Vector2 interpolatedPosition = currentPoint.Lerp(nextPoint, _travelProgress);
            
            // Check for collision at interpolated position
            if (CheckCollisionAtPosition(interpolatedPosition))
            {
                OnRockHitSomething(interpolatedPosition);
                return;
            }
            
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
        
        Log.System("ThrownRock hit something during flight", "ThrownRock");
        
        // Create impact effect and remove rock
        CreateImpactEffect();
    }

    private bool CheckCollisionAtPosition(Vector2 worldPoint)
    {
        // Use PhysicsServer2D to check for collisions at this point
        var spaceState = GetWorld2D().DirectSpaceState;
        
        // Create a small collision query at this point
        var query = new PhysicsPointQueryParameters2D();
        query.Position = worldPoint;
        query.CollisionMask = 1; // Check collision layer 1 (typically ground/static objects)
        query.Exclude = new Godot.Collections.Array<Rid>(); // Don't exclude anything
        
        // Perform the collision check
        var result = spaceState.IntersectPoint(query);
        
        return result.Count > 0;
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
        tween.TweenCallback(Callable.From(() => QueueFree()));
    }
    #endregion

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
