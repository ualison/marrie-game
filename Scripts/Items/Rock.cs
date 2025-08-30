using Godot;
using MarrieGame.Core.Utils;

/// <summary>
/// Rock item that can be collected by the player when static, or thrown as a projectile
/// - Static mode: Collectible item that adds to player's rock inventory
/// - Thrown mode: Projectile that follows trajectory and can hit objects like apples
/// </summary>
public partial class Rock : Node2D, IPoolable
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

    // Pooling support
    public bool IsFromPool { get; set; } = false;

    public void ResetForReuse()
    {
        // Reset runtime state
        _isThrown = false;
        _playerNearby = false;
        _trajectoryPoints = null;
        _currentPointIndex = 0;
        _travelProgress = 0f;
        Visible = true;
        Show();
    // Reattach signals and ensure group membership for reused instances
    try { if (_detectionArea != null) { _detectionArea.BodyEntered -= OnPlayerEntered; _detectionArea.BodyExited -= OnPlayerExited; _detectionArea.BodyEntered += OnPlayerEntered; _detectionArea.BodyExited += OnPlayerExited; } } catch { }
    try { if (_collisionArea != null) { _collisionArea.AreaEntered -= OnRockHitArea; _collisionArea.BodyEntered -= OnRockHitBody; _collisionArea.AreaEntered += OnRockHitArea; _collisionArea.BodyEntered += OnRockHitBody; } } catch { }
    try { if (!IsInGroup("rocks")) AddToGroup("rocks"); } catch { }
    }

    public void OnReleasedToPool()
    {
        // Called when returned to pool - stop any tweens or signals
        _isMoving = false;
    try { if (_detectionArea != null) _detectionArea.BodyEntered -= OnPlayerEntered; } catch { }
    try { if (_detectionArea != null) _detectionArea.BodyExited -= OnPlayerExited; } catch { }
    try { if (_collisionArea != null) _collisionArea.AreaEntered -= OnRockHitArea; } catch { }
    try { if (_collisionArea != null) _collisionArea.BodyEntered -= OnRockHitBody; } catch { }
    }

    // Shared rock texture to reduce repeated resource loads
    private static Texture2D _sharedRockTexture;

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
    _detectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "DetectionArea");
    _rockSprite = NodeHelpers.GetNodeOrNull<Sprite2D>(this, null, "RockSprite");
    _promptLabel = NodeHelpers.GetNodeOrNull<Label>(this, null, "PromptLabel");
        
    // Try to get the thrown collision area if it exists in the scene
    _collisionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "ThrownCollisionArea");
        
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
            if (_sharedRockTexture == null)
                _sharedRockTexture = GD.Load<Texture2D>("res://Art/Rock.png");
            RockTexture = _sharedRockTexture;
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

    // Ensure movement runs in the physics step so Area2D overlaps are detected reliably
    SetPhysicsProcess(true);

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
    _collisionArea.Monitoring = true;
    _collisionArea.Monitorable = true;
    _collisionArea.AreaEntered += OnRockHitArea;
    _collisionArea.BodyEntered += OnRockHitBody;
        
        Log.System($"Rock collision area set up - Layer: {_collisionArea.CollisionLayer}, Mask: {_collisionArea.CollisionMask}", "Rock");
    }

    private void OnRockHitArea(Area2D area)
    {
        // Rock hit something with an Area2D (like an apple or monster's damage area)
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System($"Rock's area hit another area: {area.Name} on layer {area.CollisionLayer}, mask {area.CollisionMask}", "Rock");
        
        string areaName = area.Name.ToString();
        
        // Check if this is an apple's rock detection area (use bitmask)
        if ((area.CollisionLayer & 4) != 0)
        {
            // Try to find the Apple parent node and trigger fall if possible
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
                    Log.System($"Rock triggered apple fall for: {apple.Name}", "Rock");
                    apple.MakeAppleFall();
                }
                else
                {
                    Log.System($"Rock hit apple but it cannot fall now: {apple.Name}", "Rock");
                }
            }

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
                if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
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
                if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
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
                if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
                    Log.System($"No parent monster found for area: {area.Name} - continuing trajectory", "Rock");
            }
        }
        else
        {
            if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
                Log.System($"Rock hit unknown area: {area.Name} - stopping", "Rock");
            // Stop movement and disappear for other unknown areas
            _isMoving = false;
            CreateImpactEffect();
        }
    }

    private void OnRockHitBody(Node2D body)
    {
        // Rock hit a CharacterBody2D (like a monster)
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System($"Rock Area2D hit body: {body.Name} ({body.GetType().Name}) at position {GlobalPosition}", "Rock");
        
        // Check if this is a ghost monster first, then a real Monster
        if (body is GhostMonster ghost)
        {
            if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
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
            if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
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
        // Thrown-mode movement runs in _PhysicsProcess
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_currentMode == RockMode.Thrown && _isMoving && _trajectoryPoints != null && _trajectoryPoints.Length > 1)
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
            _rockSprite.Rotation = direction.Angle();
        }
    }

    private void OnRockHitSomething(Vector2 hitPosition)
    {
    // Deprecated: collisions are handled by Area2D signals (OnRockHitArea/OnRockHitBody).
    // Kept for compatibility in case external callers invoke it.
    _isMoving = false;
    GlobalPosition = hitPosition;
    Log.System("Rock hit during flight (handled programmatically)", "Rock");
    CreateImpactEffect();
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
        tween.TweenCallback(Callable.From(() =>
        {
            if (IsFromPool)
                PoolManager.Release(this);
            else
                QueueFree();
        }));
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
