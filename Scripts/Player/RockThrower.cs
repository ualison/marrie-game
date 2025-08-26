using Godot;
using System.Collections.Generic;

/// <summary>
/// Handles rock throwing mechanics with trajectory preview
/// Allows player to draw an arc trajectory and throw rocks along that path
/// </summary>
public partial class RockThrower : Node2D
{
    #region Exported Properties
    [Export] public PackedScene RockProjectileScene { get; set; }
    [Export] public float MaxThrowDistance { get; set; } = 500.0f;
    [Export] public float MinThrowDistance { get; set; } = 100.0f;
    [Export] public float TrajectoryHeight { get; set; } = 50.0f;
    [Export] public int TrajectoryPoints { get; set; } = 30;
    [Export] public Color TrajectoryColor { get; set; } = Colors.Purple;
    [Export] public float TrajectoryWidth { get; set; } = 5.0f;
    [Export] public float LineOpacity { get; set; } = 0.8f; // Line opacity (0.0 = transparent, 1.0 = opaque)
    [Export] public uint CollisionMask { get; set; } = 1; // Which collision layers to check against
    [Export] public bool ExtendToGround { get; set; } = true; // Whether to extend trajectory to ground if no obstacles hit
    [Export] public float RockVelocity { get; set; } = 300.0f; // Speed at which the rock moves along its trajectory
    #endregion

    #region State
    private bool _isDrawingTrajectory = false;
    private Vector2 _startPosition;
    private Vector2 _currentMousePosition;
    private Line2D _trajectoryLine;
    private Inventory _inventory;
    private Camera2D _camera;
    private Vector2[] _currentGlobalTrajectory; // Store current trajectory in global coordinates
    #endregion

    #region Initialization
    public override void _Ready()
    {
        SetupTrajectoryLine();
        FindComponents();
        Log.System("RockThrower ready", "RockThrower");
    }

    private void SetupTrajectoryLine()
    {
        _trajectoryLine = new Line2D();
        _trajectoryLine.DefaultColor = new Color(TrajectoryColor.R, TrajectoryColor.G, TrajectoryColor.B, LineOpacity);
        _trajectoryLine.Width = TrajectoryWidth;
        _trajectoryLine.ZIndex = 100; // Draw on top
        AddChild(_trajectoryLine);
    }

    private void FindComponents()
    {
        // Find inventory component
        _inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;
        if (_inventory == null)
        {
            Log.Warning("Inventory not found for RockThrower", "RockThrower");
        }

        // Find camera for screen-to-world conversion
        _camera = GetViewport().GetCamera2D();
    }
    #endregion

    #region Input Processing
    public override void _Input(InputEvent @event)
    {
        // Only process if player has rocks
        if (_inventory == null || !_inventory.HasRocks(1))
        {
            if (_isDrawingTrajectory)
            {
                CancelTrajectory();
            }
            return;
        }

        HandleMouseInput(@event);
    }

    private void HandleMouseInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            HandleMouseButton(mouseButton);
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isDrawingTrajectory)
        {
            HandleMouseMotion(mouseMotion);
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        if (mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed && !_isDrawingTrajectory)
            {
                StartTrajectoryDrawing(mouseButton.Position);
            }
            else if (!mouseButton.Pressed && _isDrawingTrajectory)
            {
                ThrowRock();
                EndTrajectoryDrawing();
            }
        }
        else if (mouseButton.ButtonIndex == MouseButton.Right && _isDrawingTrajectory)
        {
            // Right click cancels trajectory
            CancelTrajectory();
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        _currentMousePosition = mouseMotion.Position;
        UpdateTrajectory();
    }
    #endregion

    #region Trajectory System
    private void StartTrajectoryDrawing(Vector2 screenPosition)
    {
        _isDrawingTrajectory = true;
        _startPosition = GlobalPosition; // Start from RockThrower's position, not mouse position
        _currentMousePosition = screenPosition;
        
        _trajectoryLine.Show();
    }

    private void UpdateTrajectory()
    {
        if (!_isDrawingTrajectory) return;

        Vector2 currentWorldPos = GetGlobalMousePosition();
        Vector2 direction = (currentWorldPos - GlobalPosition); // Direction from RockThrower to mouse
        
        // Ensure minimum distance for meaningful trajectory
        if (direction.Length() < 50.0f)
        {
            _trajectoryLine.ClearPoints();
            return;
        }
        
        // Calculate throw distance based on mouse position
        float throwDistance = CalculateThrowDistance(direction);
        
        // Generate trajectory points starting from RockThrower position
        _currentGlobalTrajectory = GenerateTrajectoryPoints(GlobalPosition, direction.Normalized(), throwDistance);
        
        // Update the line - convert global points to local coordinates for display
        _trajectoryLine.ClearPoints();
        foreach (var globalPoint in _currentGlobalTrajectory)
        {
            Vector2 localPoint = ToLocal(globalPoint);
            _trajectoryLine.AddPoint(localPoint);
        }
    }

    private float CalculateThrowDistance(Vector2 direction)
    {
        // Base distance on the full distance to mouse position
        float mouseDistance = direction.Length();
        
        // Map mouse distance to throw distance with reasonable scaling
        float normalizedDistance = Mathf.Clamp(mouseDistance / 300.0f, 0.0f, 1.0f);
        
        return Mathf.Lerp(MinThrowDistance, MaxThrowDistance, normalizedDistance);
    }

   private Vector2[] GenerateTrajectoryPoints(Vector2 start, Vector2 direction, float distance)
{
    var points = new List<Vector2>();

    // Normalize direction and set initial speed
    Vector2 dir = direction.Normalized();
    float speed = distance * 2.0f; // scale throw strength (tweak as needed)

    // Split speed into components
    Vector2 velocity = dir * speed;

    // Arc height influence (adds upward boost)
    velocity.Y -= TrajectoryHeight;

    // Physics simulation step
    float timeStep = 0.05f; // smaller = smoother curve
    int maxSteps = 200;

    Vector2 currentPos = start;

    for (int i = 0; i < maxSteps; i++)
    {
        // Save point
        points.Add(currentPos);

        // Advance physics
        currentPos += velocity * timeStep;
        velocity.Y += 9.8f * 30f * timeStep; // gravity factor (tweak)

        // Collision check
        if (CheckCollisionAtPoint(currentPos))
            break;
    }

    // Fallback if no points
    if (points.Count == 0)
        points.Add(start);

    return points.ToArray();
}


    private Vector2 FindGroundCollision(Vector2 startPoint, Vector2 direction)
    {
        // Cast downward from the last trajectory point to find ground
        var spaceState = GetWorld2D().DirectSpaceState;
        
        var query = new PhysicsRayQueryParameters2D();
        query.From = startPoint;
        query.To = startPoint + Vector2.Down * 1000; // Cast far down to find ground
        query.CollisionMask = CollisionMask;
        query.Exclude = new Godot.Collections.Array<Rid>();
        
        var result = spaceState.IntersectRay(query);
        
        if (result.Count > 0)
        {
            return result["position"].AsVector2();
        }
        
        // If no ground found, return the original point
        return startPoint;
    }

    private bool CheckCollisionAtPoint(Vector2 worldPoint)
    {
        // Use PhysicsServer2D to check for collisions at this point
        var spaceState = GetWorld2D().DirectSpaceState;
        
        // Create a small collision query at this point
        var query = new PhysicsPointQueryParameters2D();
        query.Position = worldPoint;
        query.CollisionMask = CollisionMask; // Use configurable collision mask
        query.Exclude = new Godot.Collections.Array<Rid>(); // Don't exclude anything
        
        // Perform the collision check
        var result = spaceState.IntersectPoint(query);
        
        return result.Count > 0;
    }

    private void ThrowRock()
    {
        if (_inventory == null || !_inventory.HasRocks(1))
        {
            Log.UI("No rocks available to throw", "RockThrower");
            return;
        }

        // Remove rock from inventory
        if (!_inventory.RemoveRocks(1))
        {
            Log.Warning("Failed to remove rock from inventory", "RockThrower");
            return;
        }

        // Create and launch projectile
        CreateRockProjectile();
        
        Log.UI($"Rock thrown! Remaining rocks: {_inventory.GetRockCount()}", "RockThrower");
    }

    private void CreateRockProjectile()
    {
        // Load and instantiate rock from scene file
        var rockScene = GD.Load<PackedScene>("res://Prefabs/Rock.tscn");
        var rock = rockScene.Instantiate() as Rock;
        
        if (rock == null)
        {
            Log.Warning("Failed to instantiate rock from scene", "RockThrower");
            return;
        }
        
        rock.TravelSpeed = RockVelocity; // Set the rock movement speed
        GetTree().CurrentScene.AddChild(rock);
        
        // Use the stored global trajectory points
        if (_currentGlobalTrajectory != null && _currentGlobalTrajectory.Length > 0)
        {
            rock.SetTrajectory(_currentGlobalTrajectory);
            Log.System($"Rock projectile created with {_currentGlobalTrajectory.Length} trajectory points and velocity {RockVelocity}", "RockThrower");
            _currentGlobalTrajectory = null; // Clear trajectory after successful use
        }
        else
        {
            Log.Warning("No trajectory data available for rock projectile", "RockThrower");
            rock.QueueFree();
        }
    }

    private void EndTrajectoryDrawing()
    {
        _isDrawingTrajectory = false;
        _trajectoryLine.Hide();
        _trajectoryLine.ClearPoints();
        // Don't clear trajectory here - let CreateRockProjectile clear it after use
    }

    private void CancelTrajectory()
    {
        _isDrawingTrajectory = false;
        _trajectoryLine.Hide();
        _trajectoryLine.ClearPoints();
        _currentGlobalTrajectory = null; // Clear stored trajectory
    }
    #endregion

    #region Public API
    public bool IsDrawingTrajectory() => _isDrawingTrajectory;
    
    public int GetAvailableRocks() => _inventory?.GetRockCount() ?? 0;
    #endregion
}
