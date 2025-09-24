using Godot;
using MarrieGame.Core.Interfaces;
using System;
using System.Collections.Generic;

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
// Note: damping should be configured in the scene/prefab. Do not override damping at runtime.
	[Export] public NodePath WheelLeftPath = "RigidBody2D/Sprites/WheelLeft";
	[Export] public NodePath WheelRightPath = "RigidBody2D/Sprites/WheelRight";
	[Export] public NodePath DetectionAreaPath = "RigidBody2D/DetectionArea";
	[Export] public NodePath RigidBodyPath = "RigidBody2D";
	[Export] public NodePath SpritesRootPath = "RigidBody2D/Sprites";
	[Export] public NodePath SpoonPivotPath = "RigidBody2D/Sprites/SpoonPivot";
	[Export] public NodePath HandleSpritePath = "RigidBody2D/Sprites/Handle";
	[Export] public float SpoonTargetAngleDegrees { get; set; } = 45.0f;
	[Export] public float SpoonRotationDuration { get; set; } = 0.2f;
	[Export] public float HandleTargetAngleDegrees { get; set; } = 45.0f;
	[Export] public float HandleRotationDuration { get; set; } = 0.2f;

	// Trajectory preview settings (shown when player first presses E)
	[Export] public float LaunchSpeed { get; set; } = 400.0f;
	[Export] public int TrajectorySamples { get; set; } = 50;
	[Export] public float TrajectorySampleDt { get; set; } = 0.05f;
	[Export] public float TrajectoryWidth { get; set; } = 4.0f;
	[Export] public float TrajectoryGravityScale { get; set; } = 1.0f; // Adjust gravity for trajectory arc
	[Export] public float LaunchAngleDegrees { get; set; } = 65.0f; // Launch angle in degrees (steeper for catapult)
	[Export] public Color TrajectoryColor { get; set; } = Colors.Green;
	#endregion

	#region Components
	private Sprite2D _wheelLeft;
	private Sprite2D _wheelRight;
	private Node2D _spoonPivot;
	private object _spoonTween;
	private Sprite2D _handleSprite;
	private object _handleTween;
	private Timer _puffPollTimer;
	private Node2D _puffNodeRefForPoll;
	private Vector2 _puffTargetPosForPoll = Vector2.Zero;
	private float _puffPollElapsed = 0f;
	private bool _puffIsStocked = false;
	private bool _spoonHasRotated = false;

	[Export] public float PuffPollInterval { get; set; } = 0.05f;
	[Export] public float PuffPollTimeout { get; set; } = 1.0f;
	[Export] public float PuffPositionTolerance { get; set; } = 6.0f;
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

	// Trajectory preview node (created on demand)
	private Line2D _trajectoryPreview;
	#endregion

	#region State
	private bool _isBeingPushed = false;
	private Vector2 _lastPosition;
	private float _currentSpeed = 0.0f;
	private bool _playerInRange = false;
	private Color _originalModulate = Colors.White;
	// Track whether collisions with the player are currently enabled to avoid
	// logging the same debug message every frame.
	private bool _playerCollisionEnabled = false;
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
		
		// Create puff poll timer (used to wait until Puff actually reaches the stock position)
		_puffPollTimer = new Timer();
		_puffPollTimer.WaitTime = PuffPollInterval;
		_puffPollTimer.OneShot = false;
		_puffPollTimer.Timeout += OnPuffPollTimeout;
		AddChild(_puffPollTimer);
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

		// Cache spoon pivot (used for visual rotation when puff is stocked/launched)
		_spoonPivot = _rigidBody.GetNodeOrNull<Node2D>(SpoonPivotPath) ?? _rigidBody.GetNodeOrNull<Node2D>("Sprites/SpoonPivot");
		if (_spoonPivot != null)
		{
			// Ensure pivot starts at zero rotation
			try { _spoonPivot.RotationDegrees = 0f; } catch {}
		}

		// Cache handle sprite (used for visual rotation when catapult is operated)
		_handleSprite = _rigidBody.GetNodeOrNull<Sprite2D>(HandleSpritePath) ?? _rigidBody.GetNodeOrNull<Sprite2D>("Sprites/Handle");
		if (_handleSprite != null)
		{
			// Ensure handle starts at zero rotation
			try { _handleSprite.RotationDegrees = 0f; } catch {}
			Log.Debug("Handle sprite found and initialized", "Catapult");
		}
		else
		{
			Log.Warning("Handle sprite not found - handle rotation will not work", "Catapult");
		}
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
		// Do not override linear/angular damping here; respect authoring-time values
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
		// Check if player is pressing push button (check this regardless of range)
		bool wantsToPush = Input.IsActionPressed("push_catapult");
		
		// If player is not in range or null, they can't push
		if (_player == null || !_playerInRange)
		{
			// Debug log state changes - player left range while pushing
			if (_isBeingPushed)
			{
				Log.Info("Player stopped pushing catapult (left range)", "Catapult");
			}
			
			// Reset pushing state and ensure collisions remain disabled when player is not in range
			_isBeingPushed = false;
			DisablePlayerCollision();
			// Also handle handle-area prompt/logic when player is not in primary detection area
			HandleHandleAreaInput();
			return;
		}
		
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
			// Command Puff to move to PuffPosition or rotate spoon when already stocked
			var puffNodes = GetTree().GetNodesInGroup("puff");
			if (puffNodes.Count > 0)
			{
				var puffNode = puffNodes[0] as Node2D;
				var puffAI = puffNode as MarrieGame.AI.PuffAI;
				var puffPosNode = _rigidBody.GetNodeOrNull<Node2D>("Sprites/SpoonPivot/Spoon/SpoonIsolated/PuffPosition") ?? GetNodeOrNull<Node2D>("Sprites/PuffPosition");
				if (puffAI != null && puffPosNode != null)
				{
					// If Puff is already stocked (or catapult detected arrival) and stock timer is active,
					// treat this press as the 'launch' press that rotates the spoon.
					bool puffStateIsStocked = false;
					try { puffStateIsStocked = puffAI.GetCurrentState() == MarrieGame.AI.PuffAI.PuffState.StuckInCatapult; } catch {}
					bool stockTimerActive = false;
					try { stockTimerActive = _stockTimer != null && !_stockTimer.IsStopped(); } catch {}

					if ((_puffIsStocked || puffStateIsStocked) && stockTimerActive)
					{
						// Only allow rotating the spoon once per stocked event unless you want repeated rotation
						if (!_spoonHasRotated)
						{
							TryRotateSpoonTo(SpoonTargetAngleDegrees, SpoonRotationDuration);
							TryRotateHandleTo(HandleTargetAngleDegrees, HandleRotationDuration);
							LaunchPuff(puffNode as Node2D, puffAI);
							_spoonHasRotated = true;
							Log.Debug("Spoon and handle rotated, Puff launched by player", "Catapult");
						}
						else
						{
							Log.Debug("Player pressed E but spoon already rotated this stock event", "Catapult");
						}
					}
					else
					{
						// Initial press: command Puff to move to PuffPosition
						// Do not request Puff to stock if it's currently launching
						bool puffIsLaunching = false;
						try { puffIsLaunching = puffAI.GetCurrentState() == MarrieGame.AI.PuffAI.PuffState.Launching; } catch {}
						if (puffIsLaunching)
						{
							Log.Debug("Ignored SetStockPosition because Puff is launching", "Catapult");
						}
						else
						{
							puffAI.SetStockPosition(puffPosNode.GlobalPosition);
							Log.Debug("Sent SetStockPosition to Puff (typed call)", "Catapult");
							// Start/restart stock timer
							try { _stockTimer.WaitTime = StockTimeoutSeconds; } catch {}
							try { _stockTimer.Start(); } catch {}
							// Start polling Puff position; when Puff arrives we'll mark it stocked
							StartPuffPositionPoll(puffNode as Node2D, puffPosNode.GlobalPosition);
						}
					}
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

	private void OnPuffPollTimeout()
	{
		// Increment elapsed
		_puffPollElapsed += PuffPollInterval;
		if (_puffNodeRefForPoll == null)
		{
			StopPuffPositionPoll();
			return;
		}
		// If puff node no longer valid, stop
		if (!IsInstanceValid(_puffNodeRefForPoll))
		{
			StopPuffPositionPoll();
			return;
		}
		// Check distance
		float dist = (_puffNodeRefForPoll.GlobalPosition - _puffTargetPosForPoll).Length();
		if (dist <= PuffPositionTolerance)
		{
			// Puff reached target; mark stocked and stop polling. Rotation will occur
			// only when the player presses E a second time during the stock window.
			_puffIsStocked = true;
			Log.Debug("Puff reached stock position and marked as stocked", "Catapult");
			StopPuffPositionPoll();
			return;
		}
		// Timeout exceeded
		if (_puffPollElapsed >= PuffPollTimeout)
		{
			Log.Debug("Puff did not reach stock position within timeout", "Catapult");
			StopPuffPositionPoll();
		}
	}

	private void StopPuffPositionPoll()
	{
		try { if (_puffPollTimer != null) _puffPollTimer.Stop(); } catch {}
		_puffNodeRefForPoll = null;
		_puffPollElapsed = 0f;
		// If Puff reached stock position (stocked), keep the trajectory visible
		// while the stock timer runs so player sees the launch arc. Otherwise hide it.
		try {
			if (!_puffIsStocked)
			{
				HideTrajectory();
			}
		} catch {}
	}

	private void StartPuffPositionPoll(Node2D puffNode, Vector2 targetPosition)
	{
		_puffNodeRefForPoll = puffNode;
		_puffTargetPosForPoll = targetPosition;
		_puffPollElapsed = 0f;
 		// Reset stocked/rotation flags for the new stock attempt
 		_puffIsStocked = false;
 		_spoonHasRotated = false;
		try { if (_puffPollTimer != null) _puffPollTimer.Start(); } catch {}

		// Show predicted arc for where Puff will be launched
		try { ShowTrajectory(); } catch {}
	}

	/// <summary>
	/// Build and show a Line2D that visualizes a parabolic projectile arc
	/// starting from the Spoon pivot/world puff position using physics-based trajectory calculation.
	/// </summary>
	private void ShowTrajectory()
	{
		// If already present, remove first
		if (_trajectoryPreview != null && IsInstanceValid(_trajectoryPreview))
		{
			_trajectoryPreview.QueueFree();
			_trajectoryPreview = null;
		}

		// Create Line2D
		_trajectoryPreview = new Line2D();
		_trajectoryPreview.Width = TrajectoryWidth;
		_trajectoryPreview.DefaultColor = Colors.Green;
		_trajectoryPreview.ZIndex = 1000;

		// Determine start position: use PuffPosition node if available, otherwise spoon pivot
		Vector2 startPos = Vector2.Zero;
		var puffPosNode = _rigidBody?.GetNodeOrNull<Node2D>("Sprites/SpoonPivot/Spoon/SpoonIsolated/PuffPosition") ?? _rigidBody?.GetNodeOrNull<Node2D>("Sprites/PuffPosition");
		if (puffPosNode != null) startPos = puffPosNode.GlobalPosition;
		else if (_spoonPivot != null) startPos = _spoonPivot.GlobalPosition;
		else startPos = GlobalPosition;

		// Determine the effective gravity that the Puff will experience so the preview
		// matches the actual launch. Prefer the Puff instance's physics gravity when
		// available (RigidBody2D.GravityScale or exported gravity_scale), otherwise
		// fall back to project default multiplied by TrajectoryGravityScale.
		float defaultGravity = ProjectSettings.HasSetting("physics/2d/default_gravity") ? (float)ProjectSettings.GetSetting("physics/2d/default_gravity") : 980f;
		float gravity = defaultGravity * TrajectoryGravityScale;

		try
		{
			var puffNodes = GetTree().GetNodesInGroup("puff");
			if (puffNodes.Count > 0)
			{
				var maybeRigid = puffNodes[0] as RigidBody2D;
				if (maybeRigid != null)
				{
					gravity = defaultGravity * maybeRigid.GravityScale;
				}
			}
		}
		catch { /* keep default gravity if anything goes wrong */ }

		// Use the exported launch angle for catapult trajectory
		float angleRad = Mathf.DegToRad(LaunchAngleDegrees);
		
		// Determine launch direction based on player position or default to right
		float direction = 1.0f; // Default to right
		if (_player != null)
		{
			// Launch away from player (if player is on left, launch right, and vice versa)
			if (_player.GlobalPosition.X < startPos.X)
				direction = 1.0f; // Launch right
			else
				direction = -1.0f; // Launch left
		}
		
		// Calculate initial velocity components for catapult-like launch
		float initialSpeedX = LaunchSpeed * Mathf.Cos(angleRad) * direction;
		float initialSpeedY = -LaunchSpeed * Mathf.Sin(angleRad); // Negative for upward launch in Godot coordinates

		// Calculate trajectory points using proper projectile motion physics
		var points = new List<Vector2>();
		points.Add(startPos); // Start point
		
		float timeStep = 0.02f; // Smaller time step for smoother curve
		float maxTime = 3.0f; // Maximum simulation time
		
		for (float t = timeStep; t <= maxTime; t += timeStep)
		{
			// Standard projectile motion equations
			// x = x0 + vx0 * t
			// y = y0 + vy0 * t + (1/2) * g * t^2
			float x = startPos.X + initialSpeedX * t;
			float y = startPos.Y + initialSpeedY * t + 0.5f * gravity * t * t;
			
			Vector2 pos = new Vector2(x, y);
			points.Add(pos);
			
			// Stop if projectile goes significantly below start position or gets too far away
			if (y > startPos.Y + 600f || Mathf.Abs(x - startPos.X) > 1000f)
			{
				break;
			}
		}

		// Convert to array for Line2D
		_trajectoryPreview.Points = points.ToArray();
		_trajectoryPreview.DefaultColor = TrajectoryColor;
		// Add to the scene root so it renders in world space
		GetTree().Root.AddChild(_trajectoryPreview);
	}

	private void HideTrajectory()
	{
		try
		{
			if (_trajectoryPreview != null && IsInstanceValid(_trajectoryPreview))
			{
				_trajectoryPreview.QueueFree();
			}
		}
		catch {}
		_trajectoryPreview = null;
	}

	/// <summary>
	/// Launch Puff following the trajectory physics when the spoon rotates
	/// </summary>
	private void LaunchPuff(Node2D puffNode, MarrieGame.AI.PuffAI puffAI)
	{
		if (puffNode == null || puffAI == null)
		{
			Log.Warning("Cannot launch Puff - missing node or AI reference", "Catapult");
			return;
		}

		// Get launch position - use the same position as trajectory calculation
		Vector2 launchPos = puffNode.GlobalPosition;
		
		// Calculate launch velocity using same physics as trajectory
		float angleRad = Mathf.DegToRad(LaunchAngleDegrees);
		
		// Determine launch direction based on player position or default to right
		// Use the same logic as trajectory calculation for consistency
		float direction = 1.0f; // Default to right
		if (_player != null)
		{
			// Launch away from player (if player is on left, launch right, and vice versa)
			if (_player.GlobalPosition.X < launchPos.X)
				direction = 1.0f; // Launch right
			else
				direction = -1.0f; // Launch left
		}
		
		// Calculate initial velocity components matching the trajectory exactly
		float initialSpeedX = LaunchSpeed * Mathf.Cos(angleRad) * direction;
		float initialSpeedY = -LaunchSpeed * Mathf.Sin(angleRad); // Negative for upward launch in Godot coordinates
		
		Vector2 launchVelocity = new Vector2(initialSpeedX, initialSpeedY);
		
		Log.Info($"Launching Puff from {launchPos} with velocity {launchVelocity} (angle: {LaunchAngleDegrees}°, direction: {direction})", "Catapult");

		// Apply the launch velocity to Puff FIRST before changing AI state
		if (puffNode is CharacterBody2D characterBody)
		{
			// For CharacterBody2D, set the velocity directly
			characterBody.Velocity = launchVelocity;
			Log.Info($"Applied velocity to CharacterBody2D: {launchVelocity}", "Catapult");
			
			// Keep reapplying the velocity for a short time to ensure it sticks
			var maintainTimer = GetTree().CreateTimer(0.5f);
			float reapplyInterval = 0.02f;
			int reapplyCount = 0;
			var reapplyTimer = new Timer();
			reapplyTimer.WaitTime = reapplyInterval;
			reapplyTimer.OneShot = false;
			reapplyTimer.Timeout += () => {
				reapplyCount++;
				if (IsInstanceValid(characterBody) && reapplyCount < 25) // Reapply for 0.5 seconds
				{
					// Only reapply if velocity is being overridden (getting too small)
					if (characterBody.Velocity.Length() < launchVelocity.Length() * 0.8f)
					{
						characterBody.Velocity = launchVelocity;
						Log.Debug($"Reapplied velocity to CharacterBody2D: {characterBody.Velocity}", "Catapult");
					}
				}
				else
				{
					reapplyTimer.QueueFree();
				}
			};
			AddChild(reapplyTimer);
			reapplyTimer.Start();
		}
		else if (puffNode is RigidBody2D rigidBody)
		{
			// For RigidBody2D, apply impulse
			rigidBody.LinearVelocity = launchVelocity;
			Log.Info($"Applied velocity to RigidBody2D: {launchVelocity}", "Catapult");
		}
		else
		{
			Log.Warning($"Puff node type {puffNode.GetType()} not supported for launching", "Catapult");
		}

		// Clear Puff from catapult stock state AFTER applying velocity and with delay
		// This prevents the AI from immediately overriding the physics
		var delayTimer = GetTree().CreateTimer(0.1f);
		delayTimer.Timeout += () => {
			puffAI.StartLaunching();
			Log.Debug("Puff state cleared after launch delay", "Catapult");
		};
		
		// Reset catapult state
		_puffIsStocked = false;
		_spoonHasRotated = false;

		// Rotate spoon and handle back to neutral after a short delay so the catapult
		// is visually ready for the next stock/launch sequence.
		var resetVisualsTimer = GetTree().CreateTimer(0.25f);
		resetVisualsTimer.Timeout += () => {
			TryRotateSpoonBack(SpoonRotationDuration);
			TryRotateHandleBack(HandleRotationDuration);
		};
		
		// Hide trajectory after a short delay to show the launch path briefly
		var hideTimer = GetTree().CreateTimer(0.5f);
		hideTimer.Timeout += HideTrajectory;
		
		// Stop stock timer
		try { _stockTimer?.Stop(); } catch {}
		
		Log.Info("Puff successfully launched from catapult following trajectory", "Catapult");
	}
	private void EnablePlayerCollision()
	{
		if (_rigidBody == null || _player == null) return;

		if (_player is PhysicsBody2D phys)
		{
			// Only change state and log when collisions are currently disabled
			if (!_playerCollisionEnabled)
			{
				_rigidBody.RemoveCollisionExceptionWith(phys);
				_playerCollisionEnabled = true;
				Log.Debug("Enabled collisions with player", "Catapult");
			}
		}
	}

	private void DisablePlayerCollision()
	{
		if (_rigidBody == null || _player == null) return;

		if (_player is PhysicsBody2D phys)
		{
			// Only change state and log when collisions are currently enabled
			if (_playerCollisionEnabled)
			{
				_rigidBody.AddCollisionExceptionWith(phys);
				_playerCollisionEnabled = false;
				Log.Debug("Disabled collisions with player", "Catapult");
			}
		}
	}

	private void ApplyPushForce()
	{
		if (_player == null || _rigidBody == null) return;
		
		// Get the player's velocity to determine push direction and strength
		Vector2 playerVelocity = _player.Velocity;
		float playerSpeed = playerVelocity.Length();
		// Read horizontal input once (used both for speed estimation and direction)
		float inputAxis = Input.GetAxis("ui_left", "ui_right");

		// If player's physics velocity is very small (e.g. they're stuck against the
		// catapult because of collision resolution), fall back to the player's input
		// axis so holding C + walking still pushes the catapult.
		if (playerSpeed < 10.0f)
		{
			if (Mathf.Abs(inputAxis) > 0.05f)
			{
				// Estimate player speed from their input and configured Speed value
				float estimatedSpeed = Mathf.Abs(inputAxis) * (_player?.Speed ?? 120.0f);
				playerSpeed = estimatedSpeed;
				Log.Debug($"Player velocity low; using input-based estimated speed {playerSpeed:F1} for pushing", "Catapult");
			}
			else
			{
				// No meaningful input and no velocity — still apply a base push while
				// the player is holding the push action so the catapult moves until
				// the player releases C.
				playerSpeed = 0.0f;
				Log.Debug("Player holding push with no movement input; applying base push", "Catapult");
			}
		}
		// Determine push direction: prefer the player's horizontal input direction so
		// holding left/right consistently pushes that way. If there's no horizontal
		// input, fall back to a position-based direction (player -> catapult).
		Vector2 pushDirection;
		if (Mathf.Abs(inputAxis) > 0.05f)
		{
			pushDirection = new Vector2(Mathf.Sign(inputAxis), 0f);
		}
		else
		{
			Vector2 playerPos = _player.GlobalPosition;
			Vector2 catapultPos = _rigidBody.GlobalPosition;
			pushDirection = (catapultPos - playerPos).Normalized();
		}
		
		// Calculate force magnitude based on player's speed and the configured PushForce
		float forceMagnitude = Mathf.Clamp(
			PushForce + (playerSpeed * 2.0f), // Scale with player speed
			50.0f,  // Minimum force
			PushForce * 2.0f  // Maximum force (double the configured value)
		);
		
		// Apply the force as a continuous force (not impulse) since this is called every frame while pushing
		Vector2 forceVector = pushDirection * forceMagnitude;
		_rigidBody.ApplyCentralForce(forceVector);
		
		// Add a small upward component to prevent getting stuck on ground
		_rigidBody.ApplyCentralForce(Vector2.Up * forceMagnitude * 0.1f);
		
		Log.Debug($"Applied push force {forceVector.Length():F1} to catapult (player speed: {playerSpeed:F1})", "Catapult");
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

	/// <summary>
	/// Try to rotate the SpoonPivot to a target angle (degrees) over duration seconds.
	/// Uses the scene tree tween API and cancels any previous spoon tween.
	/// </summary>
	private void TryRotateSpoonTo(float targetDegrees, float duration)
	{
		if (_spoonPivot == null) return;
		// Cancel existing tween if any
		try
		{
			if (_spoonTween is Tween existingTween)
			{
				existingTween.Kill();
			}
		}
		catch {}
		// Create new tween
		try
		{
			var tween = GetTree().CreateTween();
			tween.TweenProperty(_spoonPivot, "rotation_degrees", targetDegrees, duration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			_spoonTween = tween;
		}
		catch (System.Exception ex)
		{
			Log.Warning($"Failed rotating spoon pivot: {ex.Message}", "Catapult");
		}
	}

	private void TryRotateSpoonBack(float duration)
	{
		TryRotateSpoonTo(0f, duration);
		// Also rotate handle back to neutral when spoon returns to neutral
		TryRotateHandleBack(HandleRotationDuration);
	}

	/// <summary>
	/// Try to rotate the Handle to a target angle (degrees) over duration seconds.
	/// Uses the scene tree tween API and cancels any previous handle tween.
	/// </summary>
	private void TryRotateHandleTo(float targetDegrees, float duration)
	{
		if (_handleSprite == null) return;
		// Cancel existing tween if any
		try
		{
			if (_handleTween is Tween existingTween)
			{
				existingTween.Kill();
			}
		}
		catch {}
		// Create new tween
		try
		{
			var tween = GetTree().CreateTween();
			tween.TweenProperty(_handleSprite, "rotation_degrees", targetDegrees, duration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			_handleTween = tween;
			Log.Debug($"Started handle rotation to {targetDegrees} degrees over {duration} seconds", "Catapult");
		}
		catch (System.Exception ex)
		{
			Log.Warning($"Failed rotating handle sprite: {ex.Message}", "Catapult");
		}
	}

	private void TryRotateHandleBack(float duration)
	{
		TryRotateHandleTo(0f, duration);
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
		// Stop any poll and rotate spoon pivot back to neutral when releasing
		StopPuffPositionPoll();
		// Ensure preview arc is hidden when releasing
		try { HideTrajectory(); } catch {}
		// Reset stocked/rotation flags so subsequent stock attempts behave correctly
		_puffIsStocked = false;
		_spoonHasRotated = false;
		TryRotateSpoonBack(SpoonRotationDuration);
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
