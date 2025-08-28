using Godot;

/// <summary>
/// Rock item that can be collected by the player when static, or thrown as a projectile
/// - Static mode: Collectible item that adds to player's rock inventory
/// - Thrown mode: Projectile that follows trajectory and can hit objects like apples
/// </summary>
public partial class Rock : Node2D
{
    #region Exported Properties
    [Export] public Texture2D RockTexture { get; set; }
    [Export] public int RockValue { get; set; } = 1; // Number of rocks this item gives when collected
    [Export] public float TravelSpeed { get; set; } = 300.0f; // Speed when thrown
    #endregion

    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _rockSprite;
    private Label _promptLabel;
    private Area2D _collisionArea; // For collision detection when thrown
    #endregion

    #region State
    private bool _playerNearby = false;
    private bool _isThrown = false; // Track if this rock is in thrown mode
    
    // Trajectory state (for thrown mode)
    private Vector2[] _trajectoryPoints;
    private int _currentPointIndex = 0;
    private float _travelProgress = 0.0f;
    private bool _isMoving = false;
    #endregion

    #region Rock Modes
    public enum RockMode
    {
        Static,    // Collectible item
        Thrown     // Projectile
    }
    
    private RockMode _currentMode = RockMode.Static;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        InitializeComponents();
        
        if (_currentMode == RockMode.Static)
        {
            SetupStaticMode();
        }
        else
        {
            SetupThrownMode();
        }

        Log.System($"Rock ready in {_currentMode} mode", "Rock");
    }

    private void InitializeComponents()
    {
        // Try to get nodes from scene file, but handle programmatic creation gracefully
        _detectionArea = GetNodeOrNull<Area2D>("DetectionArea");
        _rockSprite = GetNodeOrNull<Sprite2D>("RockSprite");
        _promptLabel = GetNodeOrNull<Label>("PromptLabel");
        
        // Try to get the thrown collision area if it exists in the scene
        _collisionArea = GetNodeOrNull<Area2D>("ThrownCollisionArea");
        
        // If this is programmatic creation (no scene nodes), create sprite
        if (_rockSprite == null)
        {
            _rockSprite = new Sprite2D();
            _rockSprite.Scale = new Vector2(0.05f, 0.05f); // Small scale for thrown rock
            AddChild(_rockSprite);
            
            // Set thrown mode by default for programmatic creation
            _currentMode = RockMode.Thrown;
        }
        
        // Load rock texture if not set
        if (RockTexture == null)
        {
            RockTexture = GD.Load<Texture2D>("res://Art/Rock.png");
        }
        
        if (RockTexture != null && _rockSprite != null)
        {
            _rockSprite.Texture = RockTexture;
        }
    }

    private void SetupStaticMode()
    {
        // Only setup static mode if we have the required scene nodes
        if (_detectionArea != null)
        {
            ConnectSignals();
        }
        
        if (_promptLabel != null)
        {
            SetupPromptLabel();
        }
        
        EnableInputProcessing();
    }

    private void SetupThrownMode()
    {
        // Hide prompt label for thrown rocks
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }
        
        SetupCollisionArea();
        
        // Adjust scale for thrown rocks (smaller)
        if (_rockSprite != null)
        {
            _rockSprite.Scale = new Vector2(0.05f, 0.05f);
        }
        
        // Add to rocks group for collision detection
        AddToGroup("rocks");
        
        EnableInputProcessing();
        
        Log.System($"Rock setup in thrown mode - CollisionArea: {_collisionArea != null}", "Rock");
    }

    private void SetupCollisionArea()
    {
        // Use existing collision area from scene if available
        if (_collisionArea == null)
        {
            // This shouldn't happen since we're using the scene file now
            Log.Warning("ThrownCollisionArea not found in scene", "Rock");
            return;
        }
        
        // Ensure proper layer setup for thrown rocks
        _collisionArea.CollisionLayer = 8;  // Rock is on layer 8
        _collisionArea.CollisionMask = 1 | 4 | 16;   // Rock detects bodies/monsters on layer 1 and apples on layer 4

        // Connect to area entered signal so rock disappears when hitting apple or monster
        _collisionArea.AreaEntered += OnRockHitArea;
        _collisionArea.BodyEntered += OnRockHitBody;
        
        Log.System($"Rock collision area set up - Layer: {_collisionArea.CollisionLayer}, Mask: {_collisionArea.CollisionMask}", "Rock");
    }

    private void OnRockHitArea(Area2D area)
    {
        // Rock hit something with an Area2D (like an apple or monster's damage area)
        Log.System($"Rock's area hit another area: {area.Name} on layer {area.CollisionLayer}, mask {area.CollisionMask}", "Rock");
        
        string areaName = area.Name.ToString();
        
        // Check if this is an apple's rock detection area
        if (area.CollisionLayer == 4)
        {
            Log.System("Rock confirmed hit apple detection area", "Rock");
            // Stop movement and disappear when hitting apple area
            _isMoving = false;
            CreateImpactEffect();
        }
        else if (areaName.Contains("HurtBox") || areaName.Contains("DamageArea"))
        {
            // This is likely a monster's area - find the parent monster and deal damage
            Log.System($"Rock hit monster area: {area.Name} - looking for parent monster", "Rock");
            
            // Try to find a GhostMonster first, then a Monster in the parents
            Node current = area;
            GhostMonster ghost = null;
            Monster monster = null;

            while (current != null && ghost == null && monster == null)
            {
                ghost = current as GhostMonster;
                monster = current as Monster;
                current = current.GetParent();
            }

            if (ghost != null)
            {
                Log.System($"Found parent ghost monster: {ghost.Name} - dealing rock damage", "Rock");
                bool damaged = ghost.TakeRockDamage(1);
                if (damaged)
                {
                    Log.Info("Rock successfully damaged ghost monster via area detection!", "Rock");
                }
                else
                {
                    Log.UI("Rock hit ghost monster but no damage dealt (invulnerable or no appropriate hearts)", "Rock");
                }
                _isMoving = false;
                CreateImpactEffect();
            }
            else if (monster != null)
            {
                Log.System($"Found parent monster: {monster.Name} - dealing rock damage", "Rock");
                bool damaged = monster.TakeRockDamage(1);
                if (damaged)
                {
                    Log.Info("Rock successfully damaged monster via area detection!", "Rock");
                }
                else
                {
                    Log.UI("Rock hit monster but no damage dealt (invulnerable or no appropriate hearts)", "Rock");
                }
                _isMoving = false;
                CreateImpactEffect();
            }
            else
            {
                Log.System($"No parent monster found for area: {area.Name} - continuing trajectory", "Rock");
            }
        }
        else
        {
            Log.System($"Rock hit unknown area: {area.Name} - stopping", "Rock");
            // Stop movement and disappear for other unknown areas
            _isMoving = false;
            CreateImpactEffect();
        }
    }

    private void OnRockHitBody(Node2D body)
    {
        // Rock hit a CharacterBody2D (like a monster)
        Log.System($"Rock Area2D hit body: {body.Name} ({body.GetType().Name}) at position {GlobalPosition}", "Rock");
        
        // Check if this is a ghost monster first, then a real Monster
        if (body is GhostMonster ghost)
        {
            Log.System("Rock hit ghost monster via Area2D - dealing rock damage based on reality", "Rock");
            bool damaged = ghost.TakeRockDamage(1);
            if (damaged)
            {
                Log.UI("Rock successfully damaged ghost monster!", "Rock");
            }
            else
            {
                Log.UI("Rock hit ghost monster but no damage dealt (invulnerable or no appropriate hearts)", "Rock");
            }
        }
        else if (body is Monster monster)
        {
            Log.System("Rock hit monster via Area2D - dealing rock damage based on reality", "Rock");
            bool damaged = monster.TakeRockDamage(1);
            if (damaged)
            {
                Log.UI("Rock successfully damaged monster!", "Rock");
            }
            else
            {
                Log.UI("Rock hit monster but no damage dealt (invulnerable or no appropriate hearts)", "Rock");
            }
        }
        
        // Stop movement and disappear when hitting any body
        _isMoving = false;
        CreateImpactEffect();
    }

    private void ConnectSignals()
    {
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;
    }

    private void SetupPromptLabel()
    {
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";
        _promptLabel.Hide();
    }

    private void EnableInputProcessing()
    {
        SetProcess(true);
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        if (_currentMode == RockMode.Static && !_playerNearby) return;
        
        if (_currentMode == RockMode.Static && Input.IsActionJustPressed("interact"))
        {
            HandleRockCollection();
        }
        else if (_currentMode == RockMode.Thrown && _isMoving && _trajectoryPoints != null && _trajectoryPoints.Length > 1)
        {
            MoveAlongTrajectory((float)delta);
        }
    }

    private void HandleRockCollection()
    {
        // Debug level - routine rock collection
        Log.Debug($"Collecting rock - adding {RockValue} rocks to inventory", "Rock");
        
        // Find inventory and add rocks
        var inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;
        
        if (inventory != null)
        {
            inventory.AddRocks(RockValue);
            // Debug level - routine inventory updates
            Log.Debug($"Rocks added to inventory. Total rocks: {inventory.GetRockCount()}", "Rock");
        }
        else
        {
            Log.Warning("Inventory not found", "Rock");
        }

        // Remove rock after collection
        DestroyRock();
    }

    private void DestroyRock()
    {
        Log.UI("Rock collected and destroyed", "Rock");
        QueueFree();
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
            _rockSprite.Rotation = direction.Angle();
        }
    }

    private void OnRockHitSomething(Vector2 hitPosition)
    {
        _isMoving = false;
        GlobalPosition = hitPosition;
        
        Log.System("Rock hit ground during flight", "Rock");
        
        // Create impact effect and remove rock
        CreateImpactEffect();
    }

    private bool CheckCollisionAtPosition(Vector2 worldPoint)
    {
        // Use PhysicsServer2D to check for ground/static objects but exclude monsters
        var spaceState = GetWorld2D().DirectSpaceState;
        
        // Check for ground/static objects (layer 1) but exclude monsters
        var query = new PhysicsPointQueryParameters2D();
        query.Position = worldPoint;
        query.CollisionMask = 1; // Check layer 1 (ground and monsters)
        query.Exclude = new Godot.Collections.Array<Rid>();
        
        var result = spaceState.IntersectPoint(query);
        
        // If we found collisions, check if any are NOT monsters
        foreach (var collision in result)
        {
            var collider = collision["collider"].AsGodotObject();
            
            // If the collider is not a monster, it's ground/static - collision detected
            if (collider is not Monster)
            {
                Log.System($"Rock trajectory collision with ground/static object: {collider?.GetType().Name ?? "unknown"}", "Rock");
                return true;
            }
            else
            {
                Log.System($"Rock trajectory passed through monster: {((Monster)collider).Name} - letting Area2D handle interaction", "Rock");
            }
        }
        
        // No ground/static collisions found (only monsters if any)
        return false;
    }

    private void OnTrajectoryComplete()
    {
        _isMoving = false;
        Log.System("Rock reached end of trajectory (landed on ground)", "Rock");
        
        // Add some visual effect or impact for ground landing
        CreateImpactEffect();
    }

    private void CreateImpactEffect()
    {
        // Simple visual feedback - could be expanded later
        var tween = CreateTween();
        tween.TweenProperty(_rockSprite, "modulate", new Color(1, 1, 1, 0), 0.3f);
        tween.TweenCallback(Callable.From(() => QueueFree()));
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            if (_promptLabel != null)
            {
                _promptLabel.Show();
            }
            Log.UI("Player nearby rock - showing prompt", "Rock");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            Log.UI("Player left rock - hiding prompt", "Rock");
        }
    }
    #endregion

    #region Public API
    // Static mode API
    public bool IsCollectible() => _currentMode == RockMode.Static;
    
    public int GetRockValue() => RockValue;
    
    // Thrown mode API
    /// <summary>
    /// Convert this rock to thrown mode and set its trajectory
    /// </summary>
    /// <param name="points">Array of world positions defining the path</param>
    public void SetTrajectory(Vector2[] points)
    {
        if (points == null || points.Length < 2)
        {
            Log.Warning("Invalid trajectory points provided", "Rock");
            QueueFree();
            return;
        }

        _currentMode = RockMode.Thrown;
        _isThrown = true;
        _trajectoryPoints = points;
        _currentPointIndex = 0;
        _travelProgress = 0.0f;
        
        // Hide static mode UI elements
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }
        
        // Adjust sprite scale for thrown mode
        if (_rockSprite != null)
        {
            _rockSprite.Scale = new Vector2(0.05f, 0.05f);
        }
        
        // Always setup collision area for thrown mode
        SetupCollisionArea();
        
        // Start at the first point
        GlobalPosition = _trajectoryPoints[0];
        
        // Begin movement
        _isMoving = true;
        
        Log.System($"Rock trajectory set with {points.Length} points", "Rock");
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
    
    /// <summary>
    /// Get the current mode of the rock
    /// </summary>
    /// <returns>Current rock mode</returns>
    public RockMode GetMode() => _currentMode;
    #endregion
}
