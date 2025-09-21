using Godot;
using MarrieGame.Core.Logging;
using MarrieGame.Core.Interfaces;

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
                ChangeState(PuffState.Following);
                _initialized = true;
                Log.System("PuffAI initialized and started following", "PuffAI");
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
                    Log.System("PuffAI: Player found on retry - starting to follow", "PuffAI");
                    ChangeState(PuffState.Following);
                    _initialized = true;
                }
            }
            
            if (IsDead || _player == null || !_initialized) return;

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
            // Only attack if player explicitly commands it with Q key
            if (Input.IsActionJustPressed("puff_attack") && _canAttack && _currentState == PuffState.Following)
            {
                var nearestMonster = FindNearestMonster();
                if (nearestMonster != null)
                {
                    Log.System("PuffAI: Player commanded attack - engaging monster", "PuffAI");
                    ChangeState(PuffState.Attack);
                    _currentTarget = nearestMonster;
                }
                else
                {
                    Log.Debug("PuffAI: Player commanded attack but no monsters in range", "PuffAI");
                }
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
                    StartAttackCooldown();
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
                    // Stay idle
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
                // But still check for monster avoidance even when close to player
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

            // Apply monster avoidance - this takes priority over following
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
            var monsters = GetTree().GetNodesInGroup("enemy");
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
                ChangeState(PuffState.Following);
                return;
            }

            float distanceToTarget = GlobalPosition.DistanceTo(_currentTarget.GlobalPosition);
            
            if (distanceToTarget <= AttackRange)
            {
                // Attack the target
                AttackTarget(_currentTarget);
                ChangeState(PuffState.Following);
            }
            else
            {
                // Move toward target
                Vector2 direction = (_currentTarget.GlobalPosition - GlobalPosition).Normalized();
                Velocity = new Vector2(direction.X * AttackSpeed, Velocity.Y);
            }
        }

        private void AttackTarget(Node2D target)
        {
            Log.System($"PuffAI: Attacking {target.Name}", "PuffAI");
            
            // Try to damage the target
            if (target.HasMethod("TakeDamage"))
            {
                target.Call("TakeDamage", AttackDamage);
            }
            else if (target.IsInGroup("enemy"))
            {
                // Try to find health system on enemy
                Node healthSystem = null;
                foreach (Node child in target.GetChildren())
                {
                    if (child.HasMethod("TakeDamage"))
                    {
                        healthSystem = child;
                        break;
                    }
                }
                
                if (healthSystem != null)
                {
                    healthSystem.Call("TakeDamage", AttackDamage);
                }
            }

            StartAttackCooldown();
        }

        private Node2D FindNearestMonster()
        {
            var monsters = GetTree().GetNodesInGroup("enemy");
            Node2D nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Node2D monster in monsters)
            {
                if (!IsInstanceValid(monster)) continue;
                
                float distance = GlobalPosition.DistanceTo(monster.GlobalPosition);
                if (distance < nearestDistance && distance <= AttackRange * 2) // Extended search range
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
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
                ChangeState(PuffState.Following);
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
            ChangeState(PuffState.Following);
            
            if (_puffSprite != null)
            {
                _puffSprite.Modulate = Colors.White;
            }
            
            EmitSignal(SignalName.PuffHealthChanged, CurrentHealth, MaxHealth);
            Log.System("PuffAI: Reset to initial state", "PuffAI");
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
            // Only take damage from enemies if Puff failed to avoid them (shouldn't happen often now)
            else if (body.IsInGroup("enemy"))
            {
                TakeDamage(1);
                Log.Debug($"PuffAI: Failed to avoid enemy {body.Name} - took damage", "PuffAI");
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
                // Handle enemy damage only if avoidance failed
                else if (parent.IsInGroup("enemy"))
                {
                    TakeDamage(1);
                    Log.Debug($"PuffAI: Failed to avoid enemy area {area.Name} - took damage", "PuffAI");
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