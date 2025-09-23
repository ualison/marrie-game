using Godot;
using MarrieGame.Core.Logging;
using MarrieGame.Core.Interfaces;
using MarrieGame.Core.Enums;
using System;

namespace MarrieGame.AI
{
    /// <summary>
    /// AI controller for Puff companion character
    /// Features following behavior, attack on command, jumping navigation, and energy system
    /// </summary>
    public partial class PuffAI : CharacterBody2D
    {
        #region Enums
        public enum PuffState
        {
            Following,
            Attack,
            Jump,
            Idle,
            FaceLeft,
            FaceRight
        }
        #endregion

        #region Exported Properties
        [Export] public float FollowSpeed { get; set; } = 80.0f;
        [Export] public float FollowDistance { get; set; } = 120.0f; // Distance to stay behind player
        [Export] public float MinFollowDistance { get; set; } = 60.0f; // Minimum distance from player
        [Export] public float AttackSpeed { get; set; } = 150.0f;
        [Export] public float AttackRange { get; set; } = 100.0f;
        [Export] public float AerialAttackRangeMultiplier { get; set; } = 0.8f; // Multiplier for attack range while jumping
        [Export] public float AttackCommandRangeMultiplier { get; set; } = 6.0f; // How far Q-search can look relative to AttackRange
        [Export] public float AttackGiveUpRangeMultiplier { get; set; } = 4.0f; // Abort attack if target distance > AttackRange * this value
        [Export] public float JumpHeight { get; set; } = 80.0f;
        [Export] public int MaxEnergy { get; set; } = 100;
        [Export] public int StartingEnergy { get; set; } = 100;
        [Export] public int AttackDamage { get; set; } = 1;
        [Export] public float AttackCooldown { get; set; } = 1.5f;
        [Export] public float MonsterAvoidanceDistance { get; set; } = 150.0f; // Distance to avoid monsters
        [Export] public float MonsterAvoidanceForce { get; set; } = 200.0f; // How strong the avoidance is
    [Export] public float HitboxForwardOffset { get; set; } = 22.0f; // How far ahead to place HurtBox/Detection when facing
        #endregion

        #region State Management
        private PuffState _currentState = PuffState.Following;
        private PuffState _previousState = PuffState.Following;
        private Timer _stateTimer;
        private Timer _attackCooldownTimer;
        #endregion

        #region Components
        private Node2D _player;
        private Sprite2D _puffSprite;
        private RigidBody2D _rigidBody;
        private CollisionShape2D _collisionShape;
        private Area2D _monsterDetectionArea;
        private Area2D _hurtBox;
        #endregion

        #region Energy System
        public int CurrentEnergy { get; private set; }
        public int MaxEnergyLimit => MaxEnergy;
        #endregion

        #region Physics
        private float _gravity;
        private float _jumpVelocity;
        private bool _facingRight = true;
        private Node2D _currentTarget;
        private bool _canAttack = true;
        private bool _initialized = false;
        private bool _isFollowingEnabled = true; // New: tracks R key toggle state
    private float _lastDeltaToTargetX = float.NaN; // Track horizontal cross-over during attacks
        #endregion

        #region Signals
        [Signal] public delegate void PuffEnergyChangedEventHandler(int currentEnergy, int maxEnergy);
        #endregion

        #region Initialization
        public override void _Ready()
        {
            AddToGroup("puff");
            InitializePhysics();
            InitializeComponents();
            SetupTimers();
            InitializeEnergy();
            
            // Use CallDeferred to ensure everything is ready before starting following
            CallDeferred(MethodName.StartFollowing);

            // Ensure hitboxes start on the correct side
            CallDeferred(nameof(UpdateHitboxesForFacing));
        }

        private void StartFollowing()
        {
            // Ensure Puff starts following the player after initialization is complete
            if (_player != null)
            {
                ReturnToAppropriateState();
                _initialized = true;
                Log.System("PuffAI initialized and started with appropriate state", "PuffAI");
            }
            else
            {
                Log.Warning("PuffAI initialized but player not found - will retry during physics process", "PuffAI");
            }
        }

        private void InitializePhysics()
        {
            _gravity = ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle();
            _jumpVelocity = -Mathf.Sqrt(2.0f * _gravity * JumpHeight);
        }

        private void InitializeComponents()
        {
            // Get player reference
            _player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            
            // Get sprite - it's now a direct child of Puff again
            _puffSprite = GetNodeOrNull<Sprite2D>("PuffSprite");
            
            // Remove references to rigid body since we removed it
            _rigidBody = null;
            _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            
            // Get detection areas
            _monsterDetectionArea = GetNodeOrNull<Area2D>("MonsterDetectionArea");
            // Connect signals on MonsterDetectionArea (use only this area)
            if (_monsterDetectionArea != null)
            {
                try { _monsterDetectionArea.Monitoring = true; } catch {}
                try { _monsterDetectionArea.Monitorable = true; } catch {}
                _monsterDetectionArea.BodyEntered += OnMonsterDetectionBodyEntered;
                _monsterDetectionArea.AreaEntered += OnMonsterDetectionAreaEntered;
            }

            if (_player == null)
                Log.Warning("PuffAI: Player not found in group 'player'", "PuffAI");
            else
                Log.System($"PuffAI: Player found at {_player.GlobalPosition}", "PuffAI");
                
            if (_puffSprite == null)
                Log.Warning("PuffAI: PuffSprite not found as direct child", "PuffAI");
        }

        private void SetupTimers()
        {
            // State timer for various state durations
            _stateTimer = new Timer();
            _stateTimer.WaitTime = 1.0f;
            _stateTimer.OneShot = true;
            AddChild(_stateTimer);

            // Attack cooldown timer
            _attackCooldownTimer = new Timer();
            _attackCooldownTimer.WaitTime = AttackCooldown;
            _attackCooldownTimer.OneShot = true;
            _attackCooldownTimer.Timeout += OnAttackCooldownFinished;
            AddChild(_attackCooldownTimer);
        }

        private void InitializeEnergy()
        {
            CurrentEnergy = StartingEnergy;
            EmitSignal(SignalName.PuffEnergyChanged, CurrentEnergy, MaxEnergyLimit);
        }
        #endregion

        #region Physics Process
        public override void _PhysicsProcess(double delta)
        {
            // CRITICAL: Ensure Puff never disappears - force visibility every frame
            EnsurePuffVisibility();
            
            // Fallback: try to find player if not found during initialization
            if (_player == null)
            {
                _player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (_player != null)
                {
                    Log.System("PuffAI: Player found on retry - starting with appropriate state", "PuffAI");
                    ReturnToAppropriateState();
                    _initialized = true;
                }
            }
            
            if (_player == null || !_initialized) return;

            // Safety check: prevent Puff from going too far from player
            if (_player != null)
            {
                float distanceFromPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
                if (distanceFromPlayer > 1000.0f) // If Puff is more than 1000 units away
                {
                    Log.Warning($"PuffAI: Too far from player ({distanceFromPlayer:F1}), teleporting back", "PuffAI");
                    // Use the same logic as in following behavior to determine safe position
                    bool playerFacingRight = GetPlayerFacingDirection();
                    Vector2 safePosition = _player.GlobalPosition - (playerFacingRight ? Vector2.Right : Vector2.Left) * FollowDistance;
                    GlobalPosition = safePosition;
                    Velocity = Vector2.Zero;
                    ReturnToAppropriateState();
                }
            }

            HandleInput();
            UpdateState(delta);
            ApplyGravity(delta);
            MoveAndSlide();
            UpdateSpriteDirection();
        }

        /// <summary>
        /// Critical safety method to ensure Puff never disappears
        /// </summary>
        private void EnsurePuffVisibility()
        {
            // Ensure Puff node itself is always visible and active
            if (!base.Visible)
            {
                Log.Warning("PuffAI: CRITICAL - Puff was invisible, forcing visible!", "PuffAI");
                base.Visible = true;
            }

            // Ensure Puff sprite is always visible (except when properly dead)
            if (_puffSprite != null)
            {
                if (!_puffSprite.Visible)
                {
                    Log.Warning("PuffAI: CRITICAL - PuffSprite was invisible, forcing visible!", "PuffAI");
                    _puffSprite.Visible = true;
                }

                // Prevent complete transparency
                if (_puffSprite.Modulate.A < 0.1f)
                {
                    Log.Warning("PuffAI: CRITICAL - PuffSprite was nearly transparent, restoring opacity!", "PuffAI");
                    _puffSprite.Modulate = new Color(_puffSprite.Modulate.R, _puffSprite.Modulate.G, _puffSprite.Modulate.B, 1.0f);
                }
            }

            // Ensure collision shape is active
            if (_collisionShape != null && _collisionShape.Disabled)
            {
                Log.Warning("PuffAI: CRITICAL - Collision shape was disabled, re-enabling!", "PuffAI");
                _collisionShape.Disabled = false;
            }

            // Validate position is not NaN or infinite
            if (!GlobalPosition.IsFinite() || GlobalPosition.Length() > 50000)
            {
                Log.Warning($"PuffAI: CRITICAL - Invalid position detected ({GlobalPosition}), teleporting to player!", "PuffAI");
                if (_player != null && _player.GlobalPosition.IsFinite())
                {
                    GlobalPosition = _player.GlobalPosition + Vector2.Right * 50; // Safe offset
                    Velocity = Vector2.Zero;
                }
                else
                {
                    GlobalPosition = Vector2.Zero; // Fallback to origin
                    Velocity = Vector2.Zero;
                }
            }
        }

        /// <summary>
        /// New method to safely destroy Puff (use this instead of QueueFree)
        /// </summary>
        public void SafeDestroy()
        {
            Log.System("PuffAI: Safely destroying Puff", "PuffAI");
            base.QueueFree();
        }

        /// <summary>
        /// Override visibility setter to ensure Puff never becomes invisible
        /// </summary>
        public new bool Visible
        {
            get => base.Visible;
            set
            {
                if (!value)
                {
                    Log.Warning("PuffAI: CRITICAL - Something tried to make Puff invisible! Ignoring.", "PuffAI");
                    return; // Don't allow invisible
                }
                base.Visible = value;
            }
        }

        private void ApplyGravity(double delta)
        {
            if (!IsOnFloor())
                Velocity = new Vector2(Velocity.X, Velocity.Y + _gravity * (float)delta);
        }
        #endregion

        #region Input Handling
        private void HandleInput()
        {
            // Handle R key toggle between following and idle
            if (Input.IsActionJustPressed("puff_toggle"))
            {
                _isFollowingEnabled = !_isFollowingEnabled;
                
                if (_isFollowingEnabled)
                {
                    Log.System("PuffAI: R pressed - Following enabled, switching to Following state", "PuffAI");
                    ChangeState(PuffState.Following);
                }
                else
                {
                    Log.System("PuffAI: R pressed - Following disabled, switching to Idle state", "PuffAI");
                    ChangeState(PuffState.Idle);
                    Velocity = Vector2.Zero; // Stop movement immediately
                }
            }

            // Handle Q key attack - works from any state if monster is visible
            if (Input.IsActionJustPressed("puff_attack") && _canAttack)
            {
                Log.System("PuffAI: Q key pressed - attempting to find and attack monster", "PuffAI");
                var nearestMonster = FindNearestMonster();
                if (nearestMonster != null)
                {
                    // Set the target and initiate attack with proper facing
                    _currentTarget = nearestMonster;
                    InitiateAttackWithFacing();
                    
                    // Enable following so we return to Following after attack (if we were in Idle)
                    if (!_isFollowingEnabled)
                    {
                        _isFollowingEnabled = true;
                        Log.System("PuffAI: Attack initiated from Idle - enabling following for post-attack state", "PuffAI");
                    }
                }
                else
                {
                    Log.Debug("PuffAI: Player commanded attack but no visible monsters in range", "PuffAI");
                }
            }
            else if (Input.IsActionJustPressed("puff_attack") && !_canAttack)
            {
                Log.Warning("PuffAI: *** Q pressed but attack on cooldown ***", "PuffAI");
            }
        }

        private void InitiateAttackWithFacing()
        {
            if (_currentTarget == null || !IsInstanceValid(_currentTarget))
            {
                Log.Warning("PuffAI: InitiateAttackWithFacing called with invalid target", "PuffAI");
                return;
            }

            Log.System($"PuffAI: Player commanded attack - engaging monster {_currentTarget.Name}", "PuffAI");

            // Check if Puff needs to face the target first
            float targetDirection = _currentTarget.GlobalPosition.X - GlobalPosition.X;
            bool targetIsOnRight = targetDirection > 0;
            
            // If Puff is not facing the target, use a brief facing state first
            if (targetIsOnRight != _facingRight)
            {
                Log.Debug($"PuffAI: Need to face target - switching to {(targetIsOnRight ? "FaceRight" : "FaceLeft")} state", "PuffAI");
                ChangeState(targetIsOnRight ? PuffState.FaceRight : PuffState.FaceLeft);
            }
            else
            {
                // Already facing the correct direction, go straight to attack
                Log.Debug("PuffAI: Already facing target - going directly to Attack state", "PuffAI");
                ChangeState(PuffState.Attack);
            }

            // Initialize crossover tracker
            _lastDeltaToTargetX = _currentTarget.GlobalPosition.X - GlobalPosition.X;
        }
        #endregion

        #region State Machine
        private void ChangeState(PuffState newState)
        {
            if (_currentState == newState) return;

            ExitCurrentState();
            _previousState = _currentState;
            _currentState = newState;
            EnterNewState();

            Log.Debug($"PuffAI: State changed from {_previousState} to {_currentState}", "PuffAI");
        }

        private void ExitCurrentState()
        {
            switch (_currentState)
            {
                case PuffState.Following:
                    // Nothing special to do when exiting following
                    break;
                case PuffState.Attack:
                    _currentTarget = null;
                    break;
                case PuffState.Jump:
                    // Jump state naturally ends when landing
                    break;
                case PuffState.FaceLeft:
                case PuffState.FaceRight:
                    // Facing states are transitional - no special cleanup needed
                    break;
            }
        }

        private void EnterNewState()
        {
            switch (_currentState)
            {
                case PuffState.Following:
                    // Reset to following mode
                    break;
                case PuffState.Attack:
                    // Don't start cooldown here - wait until we actually attack
                    Log.Debug("PuffAI: Entered Attack state, beginning pursuit", "PuffAI");
                    // Initialize crossover tracker on enter
                    if (_currentTarget != null && IsInstanceValid(_currentTarget))
                        _lastDeltaToTargetX = _currentTarget.GlobalPosition.X - GlobalPosition.X;
                    break;
                case PuffState.Jump:
                    PerformJump();
                    break;
                case PuffState.Idle:
                    Velocity = Vector2.Zero;
                    break;
                case PuffState.FaceLeft:
                    // Facing states are brief - just used for orientation
                    break;
                case PuffState.FaceRight:
                    // Facing states are brief - just used for orientation
                    break;
            }
        }

        private void UpdateState(double delta)
        {
            // If following is disabled, force Idle state (except during attacks)
            if (!_isFollowingEnabled && _currentState == PuffState.Following)
            {
                ChangeState(PuffState.Idle);
                return;
            }

            switch (_currentState)
            {
                case PuffState.Following:
                    UpdateFollowingBehavior();
                    break;
                case PuffState.Attack:
                    UpdateAttackBehavior();
                    break;
                case PuffState.Jump:
                    UpdateJumpBehavior();
                    break;
                case PuffState.Idle:
                    UpdateIdleBehavior();
                    break;
                case PuffState.FaceLeft:
                    UpdateFacingBehavior();
                    break;
                case PuffState.FaceRight:
                    UpdateFacingBehavior();
                    break;
            }
        }
        #endregion

        #region Following Behavior
        private void UpdateFollowingBehavior()
        {
            if (_player == null) 
            {
                Log.Warning("PuffAI: Cannot follow - player is null", "PuffAI");
                return;
            }

            float distanceToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
            
            // If too close to player, don't move toward them
            if (distanceToPlayer <= MinFollowDistance)
            {
                // But still check for monster avoidance even when close to player (only in Following state)
                Vector2 avoidanceVector = CalculateMonsterAvoidance();
                if (avoidanceVector != Vector2.Zero)
                {
                    Velocity = new Vector2(avoidanceVector.X, Velocity.Y);
                }
                else
                {
                    Velocity = new Vector2(0, Velocity.Y);
                }
                return;
            }

            // Calculate target position (behind player based on player's facing direction)
            bool playerFacingRight = GetPlayerFacingDirection();
            Vector2 targetPos = _player.GlobalPosition - (playerFacingRight ? Vector2.Right : Vector2.Left) * FollowDistance;
            float distanceToTarget = GlobalPosition.DistanceTo(targetPos);

            // Calculate base movement direction toward player/target
            Vector2 moveDirection = Vector2.Zero;
            if (distanceToTarget > 20.0f)
            {
                moveDirection = (targetPos - GlobalPosition).Normalized() * FollowSpeed;
            }

            // Apply monster avoidance - but only in Following state (not during attacks)
            Vector2 avoidanceForce = CalculateMonsterAvoidance();
            if (avoidanceForce != Vector2.Zero)
            {
                // Blend avoidance with following, but avoidance has higher priority
                Vector2 finalDirection = (avoidanceForce + moveDirection * 0.3f).Normalized();
                Velocity = new Vector2(finalDirection.X * FollowSpeed, Velocity.Y);
                Log.Debug("PuffAI: Avoiding monsters while trying to follow player", "PuffAI");
            }
            else if (moveDirection != Vector2.Zero)
            {
                // No monsters nearby, normal following behavior
                Velocity = new Vector2(moveDirection.X, Velocity.Y);

                // Check if we need to jump over obstacles
                if (IsObstacleInFront() && IsOnFloor())
                {
                    ChangeState(PuffState.Jump);
                }
            }
            else
            {
                // Close enough to target position and no threats
                Velocity = new Vector2(0, Velocity.Y);
            }
        }

        private bool GetPlayerFacingDirection()
        {
            // Try to determine player's facing direction by checking sprite scale
            var playerSprite = _player.GetNodeOrNull<Node2D>("AnimatedSprite2D") 
                            ?? _player.GetNodeOrNull<Node2D>("Sprite2D") 
                            ?? _player.GetNodeOrNull<Node2D>("MarrieSprite");
            
            if (playerSprite != null)
            {
                return playerSprite.Scale.X > 0; // True if facing right, false if facing left
            }
            
            // Fallback: use movement direction
            if (_player.HasMethod("get_velocity"))
            {
                var velocity = (Vector2)_player.Call("get_velocity");
                if (Mathf.Abs(velocity.X) > 0.1f)
                    return velocity.X > 0;
            }
            
            // Default to right if we can't determine
            return true;
        }

        private bool IsObstacleInFront()
        {
            // Simple raycast to detect obstacles
            var spaceState = GetWorld2D().DirectSpaceState;
            Vector2 rayStart = GlobalPosition;
            Vector2 rayEnd = rayStart + (Vector2.Right * (_facingRight ? 30 : -30));
            
            var query = PhysicsRayQueryParameters2D.Create(rayStart, rayEnd);
            query.CollideWithAreas = false;
            query.CollideWithBodies = true;
            
            var result = spaceState.IntersectRay(query);
            return result != null && result.Count > 0;
        }

       private Vector2 CalculateMonsterAvoidance()
{
    // Only calculate avoidance in Following state - ignore in other states
    if (_currentState != PuffState.Following)
        return Vector2.Zero;

    Vector2 avoidanceForce = Vector2.Zero;
    var monsters = GetTree().GetNodesInGroup("enemies");
    bool foundNearbyMonster = false;

    foreach (Node2D monster in monsters)
    {
        // Robust validation to prevent crashes
        if (!IsInstanceValid(monster) || monster == null) continue;

        // Additional safety check - ensure monster has a valid GlobalPosition
        Vector2 monsterPos;
        try
        {
            monsterPos = monster.GlobalPosition;
        }
        catch
        {
            Log.Warning($"PuffAI: Monster {monster.Name} has invalid position, skipping avoidance", "PuffAI");
            continue;
        }

        float distanceToMonster = GlobalPosition.DistanceTo(monsterPos);
        
        // Skip monsters that are too far away
        if (distanceToMonster >= MonsterAvoidanceDistance) 
            continue;

        // Skip hidden/inactive monsters for avoidance calculation
        if (monster.HasMethod("GetCurrentState"))
        {
            try
            {
                var monsterState = monster.Call("GetCurrentState");
                if (monsterState.AsInt32() == (int)MonsterState.Hidden)
                {
                    // Don't avoid hidden monsters
                    continue;
                }
            }
            catch
            {
                // If we can't get state, assume monster is active
            }
        }

        foundNearbyMonster = true;
        
        // **FIX: Handle monsters at same position (teleported on top of Puff)**
        Vector2 awayFromMonster;
        if (distanceToMonster <= 0.1f)
        {
            // Monster is at same position - pick a random direction to move away
            // Use a consistent but pseudo-random direction based on monster's name hash
            var random = new Random(monster.Name.GetHashCode());
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            awayFromMonster = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Log.Debug($"PuffAI: Monster {monster.Name} at same position - moving in random direction", "PuffAI");
        }
        else
        {
            // Normal case - move away from monster
            awayFromMonster = (GlobalPosition - monsterPos).Normalized();
        }
        
        // Stronger force when monster is closer (inverse square relationship)
        // For monsters at same position, use maximum force
        float forceStrength;
        if (distanceToMonster <= 0.1f)
        {
            forceStrength = MonsterAvoidanceForce; // Maximum force for same-position monsters
        }
        else
        {
            forceStrength = MonsterAvoidanceForce / (distanceToMonster * distanceToMonster);
            forceStrength = Mathf.Min(forceStrength, MonsterAvoidanceForce); // Cap the maximum force
        }
        
        avoidanceForce += awayFromMonster * forceStrength;
    }

    // If there are nearby monsters, also add force toward player for safety
    if (foundNearbyMonster && _player != null)
    {
        try
        {
            Vector2 towardPlayer = (_player.GlobalPosition - GlobalPosition).Normalized();
            avoidanceForce += towardPlayer * 0.5f; // Moderate force toward player
            
            Log.Debug("PuffAI: Monsters detected, seeking safety near player", "PuffAI");
        }
        catch
        {
            Log.Warning("PuffAI: Error calculating direction to player during avoidance", "PuffAI");
        }
    }

    // Normalize the final avoidance force
    if (avoidanceForce.Length() > 0)
    {
        return avoidanceForce.Normalized();
    }

    return Vector2.Zero;
}
        #endregion

        #region Attack Behavior  
       private void UpdateAttackBehavior()
{
    if (_currentTarget == null || !IsInstanceValid(_currentTarget))
    {
        Log.Debug("PuffAI: Attack target lost, returning to appropriate state", "PuffAI");
        ReturnToAppropriateState();
        return;
    }

    // Comprehensive target validation - check if target is still attackable
    bool targetIsValid = true;
    
    // Check if target monster is in Hidden state - abort attack immediately
    if (_currentTarget.HasMethod("GetCurrentState"))
    {
        var targetState = _currentTarget.Call("GetCurrentState");
        if (targetState.AsInt32() == (int)MonsterState.Hidden)
        {
            Log.System($"PuffAI: Target {_currentTarget.Name} became Hidden - aborting attack", "PuffAI");
            targetIsValid = false;
        }
    }
    
    // Check if target is dead
    if (targetIsValid && _currentTarget.HasMethod("GetLifeSystem"))
    {
        var lifeSystemVariant = _currentTarget.Call("GetLifeSystem");
        if (lifeSystemVariant.VariantType != Variant.Type.Nil)
        {
            var lifeSystem = lifeSystemVariant.AsGodotObject() as Node;
            if (lifeSystem != null && lifeSystem.HasMethod("get_IsDead"))
            {
                bool isDead = (bool)lifeSystem.Call("get_IsDead");
                if (isDead)
                {
                    Log.System($"PuffAI: Target {_currentTarget.Name} died - aborting attack", "PuffAI");
                    targetIsValid = false;
                }
            }
        }
    }
    
    // Check if target can take damage
    if (targetIsValid && _currentTarget.HasMethod("CanTakeDamage"))
    {
        bool canTakeDamage = (bool)_currentTarget.Call("CanTakeDamage");
        if (!canTakeDamage)
        {
            Log.System($"PuffAI: Target {_currentTarget.Name} can no longer take damage - aborting attack", "PuffAI");
            targetIsValid = false;
        }
    }
    
    // If target is no longer valid, return to appropriate state
    if (!targetIsValid)
    {
        ReturnToAppropriateState();
        return;
    }

    // Additional safety check before accessing GlobalPosition
    if (_currentTarget == null || !IsInstanceValid(_currentTarget))
    {
        Log.Debug("PuffAI: Target became invalid during validation", "PuffAI");
        ReturnToAppropriateState();
        return;
    }

    // **FIX: Update facing direction every frame during attack to handle teleporting monsters**
    float targetDirection = _currentTarget.GlobalPosition.X - GlobalPosition.X;
    bool shouldFaceTarget = targetDirection > 0;
    if (shouldFaceTarget != _facingRight)
    {
        _facingRight = shouldFaceTarget;
        if (_puffSprite != null)
        {
            var scale = _puffSprite.Scale;
            _puffSprite.Scale = new Vector2(_facingRight ? Mathf.Abs(scale.X) : -Mathf.Abs(scale.X), scale.Y);
            Log.Debug($"PuffAI: Reorienting to face target - now facing {(_facingRight ? "Right" : "Left")}", "PuffAI");
        }
    }

            float distanceToTarget = GlobalPosition.DistanceTo(_currentTarget.GlobalPosition);
            float deltaX = _currentTarget.GlobalPosition.X - GlobalPosition.X;
            float deltaY = _currentTarget.GlobalPosition.Y - GlobalPosition.Y;

            // Cross-over detection: if Puff crosses horizontally past the target during a leap,
            // trigger an instant hit within a small window. This helps right-side jumps that overshoot.
            if (!float.IsNaN(_lastDeltaToTargetX) && Mathf.Sign(deltaX) != Mathf.Sign(_lastDeltaToTargetX))
            {
                if (Mathf.Abs(deltaX) <= 35.0f && Mathf.Abs(deltaY) <= 60.0f)
                {
                    Log.Debug($"PuffAI: Cross-over detected, landing melee on {_currentTarget.Name} (dx={Mathf.Abs(deltaX):F1}, dy={Mathf.Abs(deltaY):F1})", "PuffAI");
                    AttackTarget(_currentTarget);
                    ReturnToAppropriateState();
                    return;
                }
            }
    
    // If target is too far away, give up and return to appropriate state
    if (distanceToTarget > AttackRange * AttackGiveUpRangeMultiplier)
    {
        Log.Debug($"PuffAI: Target too far away ({distanceToTarget:F1}), giving up attack", "PuffAI");
        ReturnToAppropriateState();
        return;
    }
    
    // Safety check: if we're too far from player while attacking, abort
    if (_player != null)
    {
        float distanceFromPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
        if (distanceFromPlayer > 500.0f)
        {
            Log.Warning($"PuffAI: Too far from player during attack ({distanceFromPlayer:F1}), aborting", "PuffAI");
            ReturnToAppropriateState();
            return;
        }
    }
    
    // Final safety check before attack logic
    if (_currentTarget == null || !IsInstanceValid(_currentTarget))
    {
        Log.Debug("PuffAI: Target became invalid before attack", "PuffAI");
        ReturnToAppropriateState();
        return;
    }
    
    // If close enough to attack
    if (distanceToTarget <= AttackRange)
    {
        // If we're on the ground and close to target, jump to attack
        if (IsOnFloor())
        {
            Log.Debug($"PuffAI: Close to target ({distanceToTarget:F1}), jumping to attack (stay in Attack state)", "PuffAI");
            // Execute a jump impulse while remaining in Attack state so aerial attack logic can trigger
            if (_currentTarget != null && IsInstanceValid(_currentTarget))
            {
                Vector2 direction = (_currentTarget.GlobalPosition - GlobalPosition).Normalized();
                Velocity = new Vector2(direction.X * AttackSpeed, _jumpVelocity);
            }
            
            // Update crossover tracker at end of processing
            _lastDeltaToTargetX = deltaX;
        }
        else
        {
            // We're in the air near target - attack when we get close enough
            // Made attack range more generous while jumping
            if (distanceToTarget <= AttackRange * AerialAttackRangeMultiplier) 
            {
                Log.Debug($"PuffAI: Attempting aerial attack on {_currentTarget.Name}", "PuffAI");
                AttackTarget(_currentTarget);
                Log.System($"PuffAI: Completed aerial attack on {_currentTarget.Name}, returning to appropriate state", "PuffAI");
                ReturnToAppropriateState();
            }
            else
            {
                Log.Debug($"PuffAI: In air but too far for attack: {distanceToTarget:F1} > {AttackRange * AerialAttackRangeMultiplier:F1}", "PuffAI");
                // Continue moving toward target while in air
                if (_currentTarget != null && IsInstanceValid(_currentTarget))
                {
                    Vector2 direction = (_currentTarget.GlobalPosition - GlobalPosition).Normalized();
                    Velocity = new Vector2(direction.X * AttackSpeed * 0.7f, Velocity.Y); // Slightly slower air movement
                }
            }
        }
    }
    else
    {
        // Chase toward target (ignore monster avoidance while attacking)
        if (_currentTarget != null && IsInstanceValid(_currentTarget))
        {
            Vector2 direction = (_currentTarget.GlobalPosition - GlobalPosition).Normalized();
            Velocity = new Vector2(direction.X * AttackSpeed, Velocity.Y);
            
            // Check if we need to jump over obstacles while chasing (keep Attack state)
            if (IsOnFloor())
            {
                if (IsObstacleInFront())
                {
                    Log.Debug("PuffAI: Obstacle detected while chasing - jumping (stay in Attack state)", "PuffAI");
                    Velocity = new Vector2(direction.X * AttackSpeed, _jumpVelocity);
                }
                else
                {
                    // If target is within a moderate range or at a different elevation, perform a pre-attack leap
                    float verticalGap = Mathf.Abs(_currentTarget.GlobalPosition.Y - GlobalPosition.Y);
                    if (distanceToTarget <= AttackRange * 2.5f || verticalGap > 16f)
                    {
                        Log.Debug($"PuffAI: Performing pre-attack leap (dist={distanceToTarget:F1}, vgap={verticalGap:F1})", "PuffAI");
                        Velocity = new Vector2(direction.X * AttackSpeed, _jumpVelocity);
                    }
                }
            }
            
            Log.Debug($"PuffAI: Chasing target, distance: {distanceToTarget:F1}", "PuffAI");
        }
        else
        {
            Log.Debug("PuffAI: Target became invalid during chase", "PuffAI");
            ReturnToAppropriateState();
        }
    }
}

        private void AttackTarget(Node2D target)
        {
            // Final validation before attacking
            if (target == null || !IsInstanceValid(target))
            {
                Log.Warning("PuffAI: AttackTarget called with invalid target", "PuffAI");
                StartAttackCooldown();
                return;
            }
            
            Log.System($"PuffAI: Attacking {target.Name}", "PuffAI");
            
            // Simple direct attack - Puff only attacks monsters by jumping at them
            if (target is Monster monster)
            {
                // Final check: make sure monster is still in a valid state to be attacked
                if (monster.HasMethod("GetCurrentState"))
                {
                    var monsterState = monster.Call("GetCurrentState");
                    if (monsterState.AsInt32() == (int)MonsterState.Hidden)
                    {
                        Log.Debug($"PuffAI: Monster {target.Name} is hidden - cannot attack", "PuffAI");
                        StartAttackCooldown();
                        return;
                    }
                }
                
                // Check if monster is dead
                if (monster.HasMethod("GetLifeSystem"))
                {
                    var lifeSystemVariant = monster.Call("GetLifeSystem");
                    if (lifeSystemVariant.VariantType != Variant.Type.Nil)
                    {
                        var lifeSystem = lifeSystemVariant.AsGodotObject() as Node;
                        if (lifeSystem != null && lifeSystem.HasMethod("get_IsDead"))
                        {
                            bool isDead = (bool)lifeSystem.Call("get_IsDead");
                            if (isDead)
                            {
                                Log.Debug($"PuffAI: Monster {target.Name} is dead - cannot attack", "PuffAI");
                                StartAttackCooldown();
                                return;
                            }
                        }
                    }
                }
                
                // Check if monster can take damage first
                if (!monster.CanTakeDamage())
                {
                    Log.Debug($"PuffAI: Monster {target.Name} cannot take damage (invulnerable, hidden, or no red hearts)", "PuffAI");
                    StartAttackCooldown();
                    return;
                }
                
                // Deal physical damage directly
                bool damageDealt = monster.TakePhysicalDamage(AttackDamage);
                
                if (damageDealt)
                {
                    Log.System($"PuffAI: Successfully damaged monster {target.Name}", "PuffAI");
                }
                else
                {
                    Log.Warning($"PuffAI: Failed to damage monster {target.Name}", "PuffAI");
                }
            }
            else if (target is GhostMonster ghostMonster)
            {
                // Handle ghost monsters
                bool damageDealt = ghostMonster.TakePhysicalDamage(AttackDamage);
                
                if (damageDealt)
                {
                    Log.System($"PuffAI: Successfully damaged ghost monster {target.Name}", "PuffAI");
                }
                else
                {
                    Log.Warning($"PuffAI: Failed to damage ghost monster {target.Name}", "PuffAI");
                }
            }
            else
            {
                // Fallback for targets not strongly typed as Monster/GhostMonster
                // Try a dynamic approach using method names
                try
                {
                    bool canTake = true;
                    if (target.HasMethod("CanTakeDamage"))
                    {
                        var res = target.Call("CanTakeDamage");
                        if (res.VariantType == Variant.Type.Bool)
                            canTake = res.AsBool();
                    }

                    if (!canTake)
                    {
                        Log.Debug($"PuffAI: Target {target.Name} reported cannot take damage", "PuffAI");
                        StartAttackCooldown();
                        return;
                    }

                    bool dealt = false;
                    if (target.HasMethod("TakePhysicalDamage"))
                    {
                        var dmgRes = target.Call("TakePhysicalDamage", AttackDamage);
                        if (dmgRes.VariantType == Variant.Type.Bool)
                            dealt = dmgRes.AsBool();
                        else
                            dealt = true; // assume success when method exists but returns non-bool
                    }
                    else if (target.HasMethod("TakeDamage"))
                    {
                        var dmgRes = target.Call("TakeDamage", AttackDamage);
                        if (dmgRes.VariantType == Variant.Type.Bool)
                            dealt = dmgRes.AsBool();
                        else
                            dealt = true;
                    }
                    else if (target.HasMethod("GetLifeSystem"))
                    {
                        var lifeVariant = target.Call("GetLifeSystem");
                        if (lifeVariant.VariantType != Variant.Type.Nil)
                        {
                            var lifeNode = lifeVariant.AsGodotObject() as Node;
                            if (lifeNode != null && lifeNode.HasMethod("TakePhysicalDamage"))
                            {
                                var dmgRes = lifeNode.Call("TakePhysicalDamage", AttackDamage);
                                if (dmgRes.VariantType == Variant.Type.Bool)
                                    dealt = dmgRes.AsBool();
                                else
                                    dealt = true;
                            }
                        }
                    }

                    if (dealt)
                    {
                        Log.System($"PuffAI: Successfully damaged target {target.Name} via fallback", "PuffAI");
                    }
                    else
                    {
                        Log.Warning($"PuffAI: Fallback damage failed for target {target.Name}", "PuffAI");
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error($"PuffAI: Fallback attack failed on {target.Name}: {ex.Message}", "PuffAI");
                }
            }

            StartAttackCooldown();
        }

        private Node2D FindNearestMonster()
        {
            var monsters = GetTree().GetNodesInGroup("enemies");
            Node2D nearest = null;
            float nearestDistance = float.MaxValue;

            Log.Debug($"PuffAI: Searching for monsters - found {monsters.Count} in 'enemies' group", "PuffAI");

            foreach (Node2D monster in monsters)
            {
                if (!IsInstanceValid(monster)) continue;
                
                // Check if monster is dead or has no life system
                if (monster.HasMethod("GetLifeSystem"))
                {
                    var lifeSystemVariant = monster.Call("GetLifeSystem");
                    if (lifeSystemVariant.VariantType == Variant.Type.Nil)
                    {
                        Log.Debug($"PuffAI: Skipping monster {monster.Name} - no life system", "PuffAI");
                        continue;
                    }
                    
                    // Try to check if the monster is dead
                    if (monster.HasMethod("GetLifeSystem"))
                    {
                        var lifeSystem = monster.Call("GetLifeSystem").AsGodotObject() as Node;
                        if (lifeSystem != null && lifeSystem.HasMethod("get_IsDead"))
                        {
                            bool isDead = (bool)lifeSystem.Call("get_IsDead");
                            if (isDead)
                            {
                                Log.Debug($"PuffAI: Skipping dead monster {monster.Name}", "PuffAI");
                                continue;
                            }
                        }
                    }
                }
                
                // Check if monster is in visible state before considering it as a target
                bool isVisible = true;
                if (monster.HasMethod("GetCurrentState"))
                {
                    var monsterState = monster.Call("GetCurrentState");
                    if (monsterState.AsInt32() == (int)MonsterState.Hidden)
                    {
                        isVisible = false;
                        Log.Debug($"PuffAI: Skipping hidden monster {monster.Name}", "PuffAI");
                    }
                }
                else
                {
                    // If monster doesn't have state method, assume it's visible but warn
                    Log.Debug($"PuffAI: Monster {monster.Name} has no state method - assuming visible", "PuffAI");
                }
                
                if (!isVisible) continue;
                
                // Additional check: make sure monster can actually take damage before considering it
                if (monster.HasMethod("CanTakeDamage"))
                {
                    bool canTakeDamage = (bool)monster.Call("CanTakeDamage");
                    if (!canTakeDamage)
                    {
                        Log.Debug($"PuffAI: Skipping monster {monster.Name} - cannot take damage", "PuffAI");
                        continue;
                    }
                }
                
                float distance = GlobalPosition.DistanceTo(monster.GlobalPosition);
                // Extended search range for attack command - configurable multiplier of base attack range
                if (distance < nearestDistance && distance <= AttackRange * AttackCommandRangeMultiplier) 
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
            }

            if (nearest != null)
            {
                Log.System($"PuffAI: Found nearest attackable monster {nearest.Name} at distance {nearestDistance:F1}", "PuffAI");
            }
            else
            {
                Log.Debug("PuffAI: No attackable monsters found within extended range", "PuffAI");
            }

            return nearest;
        }

        private void StartAttackCooldown()
        {
            _canAttack = false;
            _attackCooldownTimer.Start();
        }

        private void OnAttackCooldownFinished()
        {
            _canAttack = true;
        }
        #endregion

        #region Jump Behavior
        private void UpdateJumpBehavior()
        {
            // Jump state ends when we land on the ground
            if (IsOnFloor())
            {
                ReturnToAppropriateState();
            }
        }

        private void PerformJump()
        {
            if (IsOnFloor())
            {
                Velocity = new Vector2(Velocity.X, _jumpVelocity);
                Log.Debug("PuffAI: Jumping", "PuffAI");
            }
        }
        #endregion

        #region Idle Behavior
        private void UpdateIdleBehavior()
        {
            // Stay completely still when in idle state
            Velocity = new Vector2(0, Velocity.Y); // Keep Y velocity for gravity
            
            // Still collect apples and take damage if needed, but don't move
            // The only way out of idle is through the R key toggle
        }

        private void UpdateFacingBehavior()
        {
            // Facing states are brief transitional states
            // Stay still while turning to face the correct direction
            Velocity = new Vector2(0, Velocity.Y); // Keep Y velocity for gravity
            
            // The facing direction will be handled by UpdateSpriteDirection()
            // After one frame, transition to the appropriate action state
            
            // If we have a target, go to Attack state; otherwise return to appropriate state
            if (_currentTarget != null && IsInstanceValid(_currentTarget))
            {
                Log.Debug("PuffAI: Facing complete - transitioning to Attack state", "PuffAI");
                ChangeState(PuffState.Attack);
            }
            else
            {
                ReturnToAppropriateState();
            }
        }
        #endregion

        #region Energy System
        public bool UseEnergy(int amount = 1)
        {
            if (CurrentEnergy < amount) return false;

            CurrentEnergy = Mathf.Max(0, CurrentEnergy - amount);
            EmitSignal(SignalName.PuffEnergyChanged, CurrentEnergy, MaxEnergyLimit);
            
            Log.System($"PuffAI: Used {amount} energy, current: {CurrentEnergy}/{MaxEnergyLimit}", "PuffAI");
            return true;
        }

        public void GainEnergy(int amount = 1)
        {
            CurrentEnergy = Mathf.Min(MaxEnergyLimit, CurrentEnergy + amount);
            EmitSignal(SignalName.PuffEnergyChanged, CurrentEnergy, MaxEnergyLimit);
            
            Log.System($"PuffAI: Gained {amount} energy, current: {CurrentEnergy}/{MaxEnergyLimit}", "PuffAI");
        }

        public void ConsumeBlueApple()
        {
            GainEnergy(10);
            Log.System("PuffAI: Consumed blue apple for energy", "PuffAI");
        }

        public void Reset()
        {
            CurrentEnergy = StartingEnergy;
            _currentTarget = null;
            _canAttack = true;
            ReturnToAppropriateState();
            
            if (_puffSprite != null)
            {
                _puffSprite.Modulate = Colors.White;
            }
            
            EmitSignal(SignalName.PuffEnergyChanged, CurrentEnergy, MaxEnergyLimit);
            Log.System("PuffAI: Reset to initial state", "PuffAI");
        }
        #endregion

        #region Signal Handlers
        private void OnMonsterDetectionBodyEntered(Node2D body)
        {
            if (body == null) return;
            if (!body.IsInGroup("enemies")) return;

            // Only attack through detection area when we're actively attacking
            if (_currentState == PuffState.Attack)
            {
                if (_currentTarget != null && IsInstanceValid(_currentTarget) && body == _currentTarget)
                {
                    Log.System($"PuffAI: Collision-based attack on {body.Name} (MonsterDetectionArea body)", "PuffAI");
                    AttackTarget(body);
                    ReturnToAppropriateState();
                }
            }
        }

        private void OnMonsterDetectionAreaEntered(Area2D area)
        {
            if (area == null) return;
            var parent = area.GetParent();
            if (parent == null) return;
            if (!parent.IsInGroup("enemies")) return;

            if (_currentState == PuffState.Attack)
            {
                if (_currentTarget != null && IsInstanceValid(_currentTarget) && parent == _currentTarget)
                {
                    Log.System($"PuffAI: Collision-based attack on {parent.Name} (MonsterDetectionArea area)", "PuffAI");
                    AttackTarget(parent as Node2D);
                    ReturnToAppropriateState();
                }
            }
        }

        private void OnHurtBoxBodyEntered(Node2D body)
        {
            // Deprecated: Using MonsterDetectionArea for enemy collisions now.
            if (body == null) return;
            if (body.IsInGroup("apple")) CheckAndConsumeApple(body);
        }

        private void OnHurtBoxAreaEntered(Area2D area)
        {
            // Deprecated: Using MonsterDetectionArea for enemy collisions now.
            if (area == null) return;
            var parent = area.GetParent();
            if (parent != null && parent.IsInGroup("apple")) CheckAndConsumeApple(parent);
        }

        private void CheckAndConsumeApple(Node apple)
        {
            // Check if it's a red apple and collectible
            if (apple.HasMethod("IsInLifePhase") && apple.HasMethod("IsCollectible"))
            {
                bool isLifePhase = (bool)apple.Call("IsInLifePhase");
                bool isCollectible = (bool)apple.Call("IsCollectible");
                
                if (isLifePhase && isCollectible)
                {
                    ConsumeBlueApple(); // All apples now give energy instead of health
                    apple.Call("DestroyApple");
                    Log.System("PuffAI: Consumed apple for energy", "PuffAI");
                }
            }
        }
        #endregion

        #region Visual Updates
        private void UpdateSpriteDirection()
        {
            if (_puffSprite == null) return;

            bool shouldFaceRight = DetermineFacingDirection();
            if (shouldFaceRight != _facingRight)
            {
                _facingRight = shouldFaceRight;
                var scale = _puffSprite.Scale;
                _puffSprite.Scale = new Vector2(_facingRight ? Mathf.Abs(scale.X) : -Mathf.Abs(scale.X), scale.Y);
                Log.Debug($"PuffAI: Facing direction changed to {(_facingRight ? "Right" : "Left")}", "PuffAI");

                // Keep the HurtBox and detection in front of Puff
                UpdateHitboxesForFacing();
            }
        }

        private void UpdateHitboxesForFacing()
        {
            // Use the sprite's actual visual orientation to position hitboxes.
            // Some textures face left by default, so relying on _facingRight can place
            // the hitboxes on the wrong side. Prefer sprite.Scale.X sign when available.
            float sign = 0f;
            if (_puffSprite != null && _puffSprite.Scale.X != 0)
                sign = Mathf.Sign(_puffSprite.Scale.X);
            else
                sign = _facingRight ? 1f : -1f;

            float x = HitboxForwardOffset * sign;

            try
            {
                if (_hurtBox != null)
                {
                    var p = _hurtBox.Position;
                    _hurtBox.Position = new Vector2(x, p.Y);
                }
            }
            catch { }

            try
            {
                if (_monsterDetectionArea != null)
                {
                    var p = _monsterDetectionArea.Position;
                    _monsterDetectionArea.Position = new Vector2(x, p.Y);
                }
            }
            catch { }
        }

        private bool DetermineFacingDirection()
        {
            // Priority 1: If in Attack state and have a target, face the target
            if (_currentState == PuffState.Attack && _currentTarget != null && IsInstanceValid(_currentTarget))
            {
                float targetDirection = _currentTarget.GlobalPosition.X - GlobalPosition.X;
                return targetDirection > 0; // True if target is to the right
            }

            // Priority 2: If in FaceLeft or FaceRight state, use that direction
            if (_currentState == PuffState.FaceLeft)
                return false;
            if (_currentState == PuffState.FaceRight)
                return true;

            // Priority 3: Use velocity direction (existing behavior)
            if (Mathf.Abs(Velocity.X) > 0.1f)
                return Velocity.X > 0;

            // Priority 4: Keep current facing direction if no movement
            return _facingRight;
        }
        
        /// <summary>
        /// Returns to the appropriate state based on whether following is enabled
        /// </summary>
        private void ReturnToAppropriateState()
        {
            if (_isFollowingEnabled)
            {
                ChangeState(PuffState.Following);
            }
            else
            {
                ChangeState(PuffState.Idle);
            }
        }
        #endregion

        #region Public API
        public PuffState GetCurrentState() => _currentState;
        public bool CanAttack() => _canAttack;
        public Node2D GetCurrentTarget() => _currentTarget;
        
        public void ForceState(PuffState state)
        {
            ChangeState(state);
        }
        #endregion
    }
}