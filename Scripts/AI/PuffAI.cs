using Godot;
using MarrieGame.Core.Logging;
using MarrieGame.Core.Interfaces;
using MarrieGame.Core.Enums;

namespace MarrieGame.AI
{
    /// <summary>
    /// AI controller for Puff companion character
    /// Features following behavior, attack on command, jumping navigation, and health system
    /// </summary>
    public partial class PuffAI : CharacterBody2D, IHealthSystem
    {
        #region Enums
        public enum PuffState
        {
            Following,
            Attack,
            Jump,
            Idle
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
        [Export] public int MaxLife { get; set; } = 3;
        [Export] public int StartingLife { get; set; } = 3;
        [Export] public int AttackDamage { get; set; } = 1;
        [Export] public float AttackCooldown { get; set; } = 1.5f;
        [Export] public float InvulnerabilityTime { get; set; } = 2.0f;
        [Export] public float MonsterAvoidanceDistance { get; set; } = 150.0f; // Distance to avoid monsters
        [Export] public float MonsterAvoidanceForce { get; set; } = 200.0f; // How strong the avoidance is
        #endregion

        #region State Management
        private PuffState _currentState = PuffState.Following;
        private PuffState _previousState = PuffState.Following;
        private Timer _stateTimer;
        private Timer _attackCooldownTimer;
        private Timer _invulnerabilityTimer;
        #endregion

        #region Components
        private Node2D _player;
        private Sprite2D _puffSprite;
        private RigidBody2D _rigidBody;
        private CollisionShape2D _collisionShape;
        private Area2D _monsterDetectionArea;
        private Area2D _hurtBox;
        #endregion

        #region Health System
        public int CurrentHealth { get; private set; }
        public int MaxHealth => MaxLife;
        public bool IsDead => CurrentHealth <= 0;
        public bool IsInvulnerable { get; private set; }
        #endregion

        #region Physics
        private float _gravity;
        private float _jumpVelocity;
        private bool _facingRight = true;
        private Node2D _currentTarget;
        private bool _canAttack = true;
        private bool _initialized = false;
        private bool _isFollowingEnabled = true; // New: tracks R key toggle state
        #endregion

        #region Signals
        [Signal] public delegate void PuffDiedEventHandler();
        [Signal] public delegate void PuffHealthChangedEventHandler(int currentHealth, int maxHealth);
        #endregion

        #region Initialization
        public override void _Ready()
        {
            AddToGroup("puff");
            InitializePhysics();
            InitializeComponents();
            SetupTimers();
            InitializeHealth();
            
            // Use CallDeferred to ensure everything is ready before starting following
            CallDeferred(MethodName.StartFollowing);
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
            _hurtBox = GetNodeOrNull<Area2D>("HurtBox");
            
            // Connect signals for combined hurt box (handles both enemies and apples)
            if (_hurtBox != null)
            {
                _hurtBox.BodyEntered += OnHurtBoxBodyEntered;
                _hurtBox.AreaEntered += OnHurtBoxAreaEntered;
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

            // Invulnerability timer
            _invulnerabilityTimer = new Timer();
            _invulnerabilityTimer.WaitTime = InvulnerabilityTime;
            _invulnerabilityTimer.OneShot = true;
            _invulnerabilityTimer.Timeout += EndInvulnerability;
            AddChild(_invulnerabilityTimer);
        }

        private void InitializeHealth()
        {
            CurrentHealth = StartingLife;
            EmitSignal(SignalName.PuffHealthChanged, CurrentHealth, MaxHealth);
        }
        #endregion

        #region Physics Process
        public override void _PhysicsProcess(double delta)
        {
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
            
            if (IsDead || _player == null || !_initialized) return;

            // Safety check: prevent Puff from going too far from player
            if (_player != null)
            {
                float distanceFromPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
                if (distanceFromPlayer > 1000.0f) // If Puff is more than 1000 units away
                {
                    Log.Warning($"PuffAI: Too far from player ({distanceFromPlayer:F1}), teleporting back", "PuffAI");
                    GlobalPosition = _player.GlobalPosition + Vector2.Left * FollowDistance;
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
                    // Always enter Attack state if visible monster is found, regardless of current state
                    Log.System($"PuffAI: Player commanded attack - engaging monster {nearestMonster.Name}", "PuffAI");
                    ChangeState(PuffState.Attack);
                    _currentTarget = nearestMonster;
                    
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
                    break;
                case PuffState.Jump:
                    PerformJump();
                    break;
                case PuffState.Idle:
                    Velocity = Vector2.Zero;
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
            Vector2 avoidanceForce = Vector2.Zero;
            var monsters = GetTree().GetNodesInGroup("enemies"); // Fixed: was "enemy", should be "enemies"
            bool foundNearbyMonster = false;

            foreach (Node2D monster in monsters)
            {
                if (!IsInstanceValid(monster)) continue;

                float distanceToMonster = GlobalPosition.DistanceTo(monster.GlobalPosition);
                
                // If monster is within avoidance distance, calculate repulsion force
                if (distanceToMonster < MonsterAvoidanceDistance && distanceToMonster > 0)
                {
                    foundNearbyMonster = true;
                    
                    // Calculate direction away from monster
                    Vector2 awayFromMonster = (GlobalPosition - monster.GlobalPosition).Normalized();
                    
                    // Stronger force when monster is closer (inverse square relationship)
                    float forceStrength = MonsterAvoidanceForce / (distanceToMonster * distanceToMonster);
                    forceStrength = Mathf.Min(forceStrength, MonsterAvoidanceForce); // Cap the maximum force
                    
                    avoidanceForce += awayFromMonster * forceStrength;
                }
            }

            // If there are nearby monsters, also add force toward player for safety
            if (foundNearbyMonster && _player != null)
            {
                Vector2 towardPlayer = (_player.GlobalPosition - GlobalPosition).Normalized();
                avoidanceForce += towardPlayer * 0.5f; // Moderate force toward player
                
                Log.Debug("PuffAI: Monsters detected, seeking safety near player", "PuffAI");
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

            float distanceToTarget = GlobalPosition.DistanceTo(_currentTarget.GlobalPosition);
            
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
        #endregion

        #region Health System
        public bool TakeDamage(int damage = 1)
        {
            if (IsDead || IsInvulnerable) return false;

            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            StartInvulnerability();
            
            EmitSignal(SignalName.PuffHealthChanged, CurrentHealth, MaxHealth);
            
            if (IsDead)
            {
                HandleDeath();
            }

            Log.System($"PuffAI: Took {damage} damage, health: {CurrentHealth}/{MaxHealth}", "PuffAI");
            return true;
        }

        public void Heal(int amount = 1)
        {
            if (IsDead) return;

            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
            EmitSignal(SignalName.PuffHealthChanged, CurrentHealth, MaxHealth);
            
            Log.System($"PuffAI: Healed {amount}, health: {CurrentHealth}/{MaxHealth}", "PuffAI");
        }

        private void StartInvulnerability()
        {
            IsInvulnerable = true;
            _invulnerabilityTimer.Start();
            
            // Visual feedback for invulnerability
            if (_puffSprite != null)
            {
                var tween = CreateTween();
                tween.TweenProperty(_puffSprite, "modulate:a", 0.5f, 0.1f);
                tween.TweenProperty(_puffSprite, "modulate:a", 1.0f, 0.1f);
                tween.SetLoops((int)(InvulnerabilityTime * 5)); // Blink during invulnerability
            }
        }

        private void EndInvulnerability()
        {
            IsInvulnerable = false;
            if (_puffSprite != null)
            {
                _puffSprite.Modulate = Colors.White; // Restore normal appearance
            }
        }

        private void HandleDeath()
        {
            EmitSignal(SignalName.PuffDied);
            ChangeState(PuffState.Idle);
            Log.System("PuffAI: Died", "PuffAI");
            
            // Optional: Hide sprite or play death animation
            if (_puffSprite != null)
            {
                _puffSprite.Modulate = new Color(1, 1, 1, 0.5f); // Semi-transparent
            }
        }

        public void ConsumeRedApple()
        {
            Heal(1);
            Log.System("PuffAI: Consumed red apple", "PuffAI");
        }

        public void Reset()
        {
            CurrentHealth = StartingLife;
            IsInvulnerable = false;
            _currentTarget = null;
            _canAttack = true;
            ReturnToAppropriateState();
            
            if (_puffSprite != null)
            {
                _puffSprite.Modulate = Colors.White;
            }
            
            EmitSignal(SignalName.PuffHealthChanged, CurrentHealth, MaxHealth);
            Log.System("PuffAI: Reset to initial state", "PuffAI");
        }
        #endregion

        #region Collision Logic
        /// <summary>
        /// Determines if Puff should take damage from monsters based on current state
        /// </summary>
        private bool ShouldTakeDamageFromMonster()
        {
            // Following and Idle: never take damage from monsters
            if (_currentState == PuffState.Following || _currentState == PuffState.Idle)
                return false;

            // Attack: vulnerable
            if (_currentState == PuffState.Attack)
                return true;

            // Jump: only vulnerable if the jump originated from an attack
            // (i.e., previous state was Attack). Navigation jumps (from Following/Idle)
            // should not make Puff vulnerable.
            if (_currentState == PuffState.Jump)
                return _previousState == PuffState.Attack;

            return false;
        }
        #endregion

        #region Signal Handlers
        private void OnHurtBoxBodyEntered(Node2D body)
        {
            // Handle apple collection - Puff should collect apples
            if (body.IsInGroup("apple"))
            {
                CheckAndConsumeApple(body);
            }
            // Only take damage from enemies in specific states (Jump state only)
            else if (body.IsInGroup("enemies"))
            {
                if (ShouldTakeDamageFromMonster())
                {
                    TakeDamage(1);
                    Log.Debug($"PuffAI: Hit by enemy {body.Name} while in {_currentState} state - took damage", "PuffAI");
                }
                else
                {
                    Log.Debug($"PuffAI: Hit by enemy {body.Name} while in {_currentState} state - ignoring damage", "PuffAI");
                }
            }
        }

        private void OnHurtBoxAreaEntered(Area2D area)
        {
            var parent = area.GetParent();
            if (parent != null)
            {
                // Handle apple collection first
                if (parent.IsInGroup("apple"))
                {
                    CheckAndConsumeApple(parent);
                }
                // Handle enemy damage only in specific states (Jump state only)
                else if (parent.IsInGroup("enemies"))
                {
                    if (ShouldTakeDamageFromMonster())
                    {
                        TakeDamage(1);
                        Log.Debug($"PuffAI: Hit by enemy area {area.Name} while in {_currentState} state - took damage", "PuffAI");
                    }
                    else
                    {
                        Log.Debug($"PuffAI: Hit by enemy area {area.Name} while in {_currentState} state - ignoring damage", "PuffAI");
                    }
                }
            }
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
                    ConsumeRedApple();
                    apple.Call("DestroyApple");
                    Log.System("PuffAI: Consumed red apple", "PuffAI");
                }
            }
        }
        #endregion

        #region Visual Updates
        private void UpdateSpriteDirection()
        {
            if (_puffSprite == null) return;

            bool shouldFaceRight = Velocity.X > 0;
            if (shouldFaceRight != _facingRight)
            {
                _facingRight = shouldFaceRight;
                var scale = _puffSprite.Scale;
                _puffSprite.Scale = new Vector2(_facingRight ? Mathf.Abs(scale.X) : -Mathf.Abs(scale.X), scale.Y);
            }
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