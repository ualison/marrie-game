using Godot;
using MarrieGame.Core.Interfaces;

/// <summary>
/// Catapult that can be pushed by the player when pressing C key
/// Features rolling wheels and collision detection
/// 
/// How to use:
/// 1. Place this script on a Node2D root that has a RigidBody2D child
/// 2. Ensure the catapult has collision shapes
/// 3. Player approaches the catapult (green tint indicates range)
/// 4. Player holds C key to push (yellow tint indicates pushing)
/// 5. Wheels rotate based on movement direction and speed
/// 6. When not pressing C, player will be gently pushed away from catapult
/// </summary>
public partial class Catapult : Node2D, IPausable
{
	#region Exported Properties
	[Export] public float PushForce = 300.0f;
	[Export] public float WheelRotationSpeed = 200.0f;
	// Gravity scale applied to the RigidBody2D (0 disables gravity, 1 = project default multiplier)
	[Export] public float BodyGravityScale = 1.0f;
	// Damping settings (exposed so instances can override). Use Godot's defaults by default.
	[Export] public float BodyLinearDamp = 0.5f;
	[Export] public float BodyAngularDamp = 2.0f;
	[Export] public NodePath WheelLeftPath = "RigidBody2D/Sprites/WheelLeft";
	[Export] public NodePath WheelRightPath = "RigidBody2D/Sprites/WheelRight";
	[Export] public NodePath DetectionAreaPath = "RigidBody2D/DetectionArea";
	[Export] public NodePath RigidBodyPath = "RigidBody2D";
	[Export] public NodePath SpritesRootPath = "RigidBody2D/Sprites";
	#endregion

	#region Components
	private Sprite2D _wheelLeft;
	private Sprite2D _wheelRight;
	private CollisionShape2D _collisionShape;
	private Area2D _detectionArea;
	private Area2D _handleArea;
	private RigidBody2D _rigidBody;
	private Node2D _spritesRoot;
	private Player _player;
	private CanvasItem _promptC1;
	private CanvasItem _promptC2;
	private Timer _stockTimer;
	[Export] public float StockTimeoutSeconds { get; set; } = 10.0f;
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
		// Ensure physics inherits the instance scale by baking it into the body
		BakeInstanceScaleIntoBody();
		InitializePhysics();

		// Ensure player collisions are ignored by default. Collisions will only be
		// enabled when the player is both in the detection area and pressing the
		// push action. This prevents accidental blocking when the player walks near
		// or under the catapult.
		if (_rigidBody != null)
		{
			var nodes = GetTree().GetNodesInGroup("player");
			foreach (var obj in nodes)
			{
				// Only add collision exceptions for physics bodies
				if (obj is PhysicsBody2D physBody)
				{
					// Add collision exception so player and catapult do not collide by default
					_rigidBody.AddCollisionExceptionWith(physBody);
				}
			}
		}
		
		// Diagnostics: log effective transforms and physics settings
		if (_rigidBody != null)
		{
			Log.Debug($"Scale - Root:{Scale}, RigidBody.Local:{_rigidBody.Scale}, RigidBody.Global:{_rigidBody.GlobalScale}", "Catapult");
			Log.Debug($"Physics - GravityScale:{_rigidBody.GravityScale}, LinearDamp:{_rigidBody.LinearDamp}, AngularDamp:{_rigidBody.AngularDamp}", "Catapult");
		}

		// Store initial position for speed calculation
		if (_rigidBody != null)
			_lastPosition = _rigidBody.GlobalPosition;
		else
			_lastPosition = GlobalPosition;
		
		Log.Info($"Catapult initialized at position {GlobalPosition}", "Catapult");
		Log.Debug($"Detection area found: {_detectionArea != null}", "Catapult");
		Log.Debug($"Wheels found - Left: {_wheelLeft != null}, Right: {_wheelRight != null}", "Catapult");

		// Cache prompt labels (they are direct children in the tscn). Use CanvasItem
		// so we can handle both Control-based Label (UI) and world-space Label2D
		// if present. This avoids hard dependency on a Label2D type that may not
		// exist in the runtime being used.
		// Try to find prompts under the RigidBody2D (so they move with physics) first,
		// then fall back to the scene root. Support both CanvasItem and Control types.
		if (_rigidBody != null)
		{
			_promptC1 = _rigidBody.GetNodeOrNull<CanvasItem>("PromptLabel-C1") ?? _rigidBody.GetNodeOrNull<Control>("PromptLabel-C1");
			_promptC2 = _rigidBody.GetNodeOrNull<CanvasItem>("PromptLabel-C2") ?? _rigidBody.GetNodeOrNull<Control>("PromptLabel-C2");
		}
		else
		{
			_promptC1 = GetNodeOrNull<CanvasItem>("PromptLabel-C1") ?? GetNodeOrNull<Control>("PromptLabel-C1");
			_promptC2 = GetNodeOrNull<CanvasItem>("PromptLabel-C2") ?? GetNodeOrNull<Control>("PromptLabel-C2");
		}
		if (_promptC1 != null) _promptC1.Visible = false;
		if (_promptC2 != null) _promptC2.Visible = false;

		// Ensure the small E prompt is hidden initially (it will be shown when player enters HandleArea)
		var promptEInit = _rigidBody.GetNodeOrNull<CanvasItem>("PromptLabel-E") ?? GetNodeOrNull<CanvasItem>("PromptLabel-E");
		if (promptEInit != null) promptEInit.Visible = false;

		// Find handle area (used for commanding Puff to be stocked)
		_handleArea = _rigidBody.GetNodeOrNull<Area2D>("Sprites/HandleArea");
		if (_handleArea != null)
		{
			// Configure handle area so it detects the player (player uses collision layer 2)
			try { _handleArea.SetCollisionLayerValue(11, true); } catch {}
			try { _handleArea.SetCollisionMaskValue(2, true); } catch {}
			try { _handleArea.Monitoring = true; } catch {}
			try { _handleArea.Monitorable = true; } catch {}
			_handleArea.BodyEntered += OnHandleAreaBodyEntered;
			_handleArea.BodyExited += OnHandleAreaBodyExited;
			Log.Debug("Connected HandleArea signals", "Catapult");
		}

		// Create a timer for holding Puff in the catapult (stock timeout)
		_stockTimer = new Timer();
		_stockTimer.WaitTime = StockTimeoutSeconds;
		_stockTimer.OneShot = true;
		_stockTimer.Timeout += () => ReleasePuffFromHandle();
		AddChild(_stockTimer);
	}

	private void InitializeComponents()
	{
		// Find the RigidBody2D child
		_rigidBody = GetNodeOrNull<RigidBody2D>(RigidBodyPath);
		if (_rigidBody == null)
		{
			Log.Error("RigidBody2D not found at path: " + RigidBodyPath, "Catapult");
			return;
		}

		// Find the sprites root (visuals container)
		_spritesRoot = GetNodeOrNull<Node2D>(SpritesRootPath);
		if (_spritesRoot == null)
		{
			Log.Warning("Sprites root not found at path: " + SpritesRootPath, "Catapult");
		}
		
		// Find wheel sprites
		_wheelLeft = GetNodeOrNull<Sprite2D>(WheelLeftPath);
		_wheelRight = GetNodeOrNull<Sprite2D>(WheelRightPath);
		
		if (_wheelLeft == null)
			Log.Warning("Left wheel not found at path: " + WheelLeftPath, "Catapult");
		if (_wheelRight == null)
			Log.Warning("Right wheel not found at path: " + WheelRightPath, "Catapult");
			
		// Find collision shape
		_collisionShape = _rigidBody.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (_collisionShape == null)
		{
			Log.Warning("No collision shape found on RigidBody2D", "Catapult");
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

	// Intentionally removed: scaling should apply uniformly to entire prefab (including physics)

	private void BakeInstanceScaleIntoBody()
	{
		if (_rigidBody == null) return;

		var rootScale = Scale;
		// Respect scene-instance transforms: do NOT modify the root Scale at runtime.
		// Previous behavior baked the instance's root scale into the RigidBody and then
		// reset the root Scale to Vector2.One, which caused scene-placed instances to
		// lose their configured scale. To avoid overriding authoring-time transforms,
		// leave the transforms as-is and let Godot handle scale inheritance for both
		// visuals and physics.
		if (!Mathf.IsEqualApprox(rootScale.X, 1f) || !Mathf.IsEqualApprox(rootScale.Y, 1f))
		{
			Log.Info($"Instance has non-default scale {rootScale}; leaving scale untouched to respect scene-instance transforms", "Catapult");
		}
	}

	private void CreateDetectionArea()
	{
		// Create detection area for player proximity
		_detectionArea = new Area2D { Name = "DetectionArea" };
		_rigidBody.AddChild(_detectionArea);
		
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
		if (_rigidBody == null) return;
		
		// Set up RigidBody2D properties for realistic physics
		_rigidBody.GravityScale = BodyGravityScale;
		// Allow scene/prefab to control damping via exports for consistency across instances
		_rigidBody.LinearDamp = BodyLinearDamp;
		_rigidBody.AngularDamp = BodyAngularDamp;
		// Do NOT override collision layers/masks here; respect prefab/scene instance settings
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
		// Calculate current speed based on RigidBody2D position change
		if (_rigidBody == null) return;
		
		Vector2 currentPosition = _rigidBody.GlobalPosition;
		Vector2 movement = currentPosition - _lastPosition;
		_currentSpeed = movement.Length() / (float)GetPhysicsProcessDeltaTime();
		_lastPosition = currentPosition;
	}

	private void HandlePlayerInteraction()
	{
		if (_player == null || !_playerInRange)
		{
			// Ensure collisions remain disabled when player is not in range
			DisablePlayerCollision();
			// Also handle handle-area prompt/logic when player is not in primary detection area
			HandleHandleAreaInput();
			return;
		}
		
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
		
		// Toggle collision only while player is in range and pressing push
		_isBeingPushed = wantsToPush;
		if (_isBeingPushed)
		{
			EnablePlayerCollision();
			ApplyPushForce();
		}
		else
		{
			// Player in range but not pressing push - keep collisions disabled
			DisablePlayerCollision();
		}

		// Always check handle-area input even when in detection range
		HandleHandleAreaInput();
	}

	private void HandleHandleAreaInput()
	{
		if (!_playerInHandleArea || _playerInHandleRef == null) return;

		bool isJustPressedE = Input.IsActionJustPressed("interact") || Input.IsActionJustPressed("ui_accept");

		if (isJustPressedE)
		{
			// Command Puff to move to PuffPosition
			var puffNodes = GetTree().GetNodesInGroup("puff");
			if (puffNodes.Count > 0)
			{
				var puffNode = puffNodes[0] as Node2D;
				var puffAI = puffNode as MarrieGame.AI.PuffAI;
				var puffPosNode = _rigidBody.GetNodeOrNull<Node2D>("Sprites/PuffPosition") ?? GetNodeOrNull<Node2D>("Sprites/PuffPosition");
				if (puffAI != null && puffPosNode != null)
				{
					puffAI.SetStockPosition(puffPosNode.GlobalPosition);
					Log.Debug("Sent SetStockPosition to Puff (typed call)", "Catapult");
					// Start/restart stock timer
					try { _stockTimer.WaitTime = StockTimeoutSeconds; } catch {}
					try { _stockTimer.Start(); } catch {}
				}
				else
				{
					if (puffAI == null) Log.Warning("No PuffAI instance found in 'puff' group", "Catapult");
					if (puffPosNode == null) Log.Warning("PuffPosition node not found under catapult sprites", "Catapult");
				}
			}
		}
		// Note: do not clear Puff every frame when E is not held.
		// Puff will be released when the player leaves the handle area or when the stock timer expires.
	}

	private void EnablePlayerCollision()
	{
		if (_rigidBody == null || _player == null) return;

		if (_player is PhysicsBody2D phys)
		{
			// Remove exception so physics will consider collisions
			_rigidBody.RemoveCollisionExceptionWith(phys);
			Log.Debug("Enabled collisions with player", "Catapult");
		}
	}

	private void DisablePlayerCollision()
	{
		if (_rigidBody == null || _player == null) return;

		if (_player is PhysicsBody2D phys)
		{
			// Ensure catapult and player do not collide
			_rigidBody.AddCollisionExceptionWith(phys);
			Log.Debug("Disabled collisions with player", "Catapult");
		}
	}

	private void ApplyPushForce()
	{
		if (_player == null || _rigidBody == null) return;
		
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
		_rigidBody.ApplyForce(pushDirection * PushForce);
		
		// Add a small upward component to prevent getting stuck on ground
		_rigidBody.ApplyForce(Vector2.Up * PushForce * 0.1f);
	}

	private void UpdateWheelRotation(float delta)
	{
		if (_currentSpeed < 0.1f || _rigidBody == null) return; // Don't rotate wheels if not moving
		
		// Calculate rotation based on movement speed and direction
		float rotationAmount = (_currentSpeed * delta * WheelRotationSpeed) / 100.0f;
		
		// Determine rotation direction based on movement
		Vector2 velocity = _rigidBody.LinearVelocity;
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
			// Hide prompts while pushing
			if (_promptC1 != null) _promptC1.Visible = false;
			if (_promptC2 != null) _promptC2.Visible = false;
		}
		else if (_playerInRange)
		{
			// Slight green tint when player is in range
			Modulate = Colors.LightGreen;
			// Show prompts when player is in range and not pushing
			if (_promptC1 != null) _promptC1.Visible = true;
			if (_promptC2 != null) _promptC2.Visible = true;
		}
		else
		{
			// Normal color
			Modulate = _originalModulate;
			// Hide prompts when player not in range
			if (_promptC1 != null) _promptC1.Visible = false;
			if (_promptC2 != null) _promptC2.Visible = false;
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
			// Keep collisions disabled until the player actively pushes
			DisablePlayerCollision();
			Log.Info("Player entered catapult detection area", "Catapult");
		}
	}

	private void OnDetectionAreaBodyExited(Node2D body)
	{
		Log.Debug($"Body exited detection area: {body.Name} (Type: {body.GetType().Name})", "Catapult");
		
		if (body is Player)
		{
			// Make sure to disable any collision and reset state when player leaves
			_player = null;
			_playerInRange = false;
			_isBeingPushed = false;
			DisablePlayerCollision();
			Log.Info("Player left catapult detection area", "Catapult");
		}
	}

	#region Handle Area Events
	private bool _playerInHandleArea = false;
	private Player _playerInHandleRef = null;

	private void OnHandleAreaBodyEntered(Node2D body)
	{
		if (body is Player player)
		{
			_playerInHandleArea = true;
			_playerInHandleRef = player;
			// Show the small E prompt (PromptLabel-E is a sibling on RigidBody2D)
			var promptE = _rigidBody.GetNodeOrNull<CanvasItem>("PromptLabel-E") ?? GetNodeOrNull<CanvasItem>("PromptLabel-E");
			if (promptE != null) promptE.Visible = true;
			Log.Info("Player entered handle area", "Catapult");
		}
	}

	private void OnHandleAreaBodyExited(Node2D body)
	{
		if (body is Player player)
		{
			_playerInHandleArea = false;
			_playerInHandleRef = null;
			var promptE = _rigidBody.GetNodeOrNull<CanvasItem>("PromptLabel-E") ?? GetNodeOrNull<CanvasItem>("PromptLabel-E");
			if (promptE != null) promptE.Visible = false;
			// Ensure Puff is released if player leaves while holding
			ReleasePuffFromHandle();
			Log.Info("Player left handle area", "Catapult");
		}
	}

	private void ReleasePuffFromHandle()
	{
		// Stop/reset the stock timer if it exists
		if (_stockTimer != null)
		{
			try { _stockTimer.Stop(); } catch {}
		}

		// Find Puff instance in scene and clear stock
		var puffNodes = GetTree().GetNodesInGroup("puff");
		if (puffNodes.Count > 0)
		{
			var puffNode = puffNodes[0] as MarrieGame.AI.PuffAI;
			if (puffNode != null)
			{
				puffNode.ClearStockPosition();
				Log.Debug("Sent ClearStockPosition to Puff (typed call)", "Catapult");
			}
			else
			{
				Log.Warning("No PuffAI instance found in 'puff' group when releasing", "Catapult");
			}
		}
	}

	#endregion
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
		if (_rigidBody != null)
			_rigidBody.Freeze = true;
	}
	
	public void Resume()
	{
		_isPaused = false;
		if (_rigidBody != null)
			_rigidBody.Freeze = false;
	}
	#endregion
}
