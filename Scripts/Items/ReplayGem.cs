using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Utils;

// Simplified replay gem - only handles UI interaction and triggers replay
public partial class ReplayGem : Node2D
{
    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Tween _brightnessTween;
    private Label _promptLabel;
    private Tween _floatingTween;
    #endregion

    #region Export Variables
    [Export] public float FloatingDistance { get; set; } = 8.0f; // How far up and down to float
    [Export] public float FloatingDuration { get; set; } = 2.0f; // How long one cycle takes
    [Export] public bool BrightEffectEnabled { get; set; } = true; // Enable pulsing bright effect
    [Export] public float BrightnessFactor { get; set; } = 1.6f; // How bright the pulse gets (1 = normal)
    [Export] public float BrightPulseDuration { get; set; } = 1.0f; // Full pulse cycle duration
    [Export] public float FollowDistance { get; set; } = 96.0f; // Desired distance from the monster
    [Export(PropertyHint.Range, "0.5,10.0,0.1")] public float FollowSmoothing { get; set; } = 4.0f; // Higher = faster follow
    [Export] public float FollowHeightOffset { get; set; } = -24.0f; // Vertical offset while following
    #endregion

    #region State
    private bool _playerNearby = false;
    private bool _canInteract = false; // Prevent immediate auto-triggering
    private Vector2 _originalGemPosition;
    // Tracks whether the gem was visible before a reality change so we can restore it
    private bool _wasVisibleBeforeRealityChange = false;
    private Monster _targetMonster;
    private Vector2 _initialScenePosition;
    private Vector2 _monsterOffsetDirection = Vector2.Up;
    private ReplayGemState _stateBeforeRealityChange = ReplayGemState.Hidden;
    private Vector2 _positionBeforeRealityChange = Vector2.Zero;
    private enum ReplayGemState
    {
        Hidden,
        FollowingMonster,
        ParkedAtLastMonsterPosition
    }

    private ReplayGemState _currentState = ReplayGemState.Hidden;
    private MonsterState _lastKnownMonsterState = MonsterState.Visible;
    private Vector2 _lastFollowPosition = Vector2.Zero;
    private Vector2 _lastMonsterVisiblePosition = Vector2.Zero;
    private double _monsterSearchTimer = 0.0f;
    private const double MonsterSearchIntervalSeconds = 1.0;
    #endregion

    #region Initialization
    // UPDATED METHOD: Better initialization with immediate reality check
    public override void _Ready()
    {
        Log.Replay("ReplayGem _Ready - ensure scene PauseMode is set to Process if replay should run while tree is paused", "ReplayGem");
        InitializeComponents();
        ConnectSignals();
        SetupPromptLabel();
        HideInitially();
        EnableInputProcessing();

        _initialScenePosition = GlobalPosition;
        _monsterSearchTimer = 0.0f;
        TryBindToMonster();

        // IMPROVED: Immediate reality check and connection
        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            Log.Replay($"RealityManager found during ReplayGem init - Current reality: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;

            // Apply initial reality state immediately
            OnRealityChanged(RealityManager.Instance.CurrentReality);
        }
        else
        {
            Log.Replay("RealityManager not yet available during ReplayGem init", "ReplayGem");
            CallDeferred(nameof(ConnectToRealityManager));
        }

        // Add delay before allowing interaction to prevent auto-triggering
        GetTree().CreateTimer(1.0f).Timeout += () =>
        {
            _canInteract = true;
            Log.Replay("ReplayGem interaction enabled", "ReplayGem");

            // Final reality check after interaction is enabled
            if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
            {
                var currentReality = RealityManager.Instance.CurrentReality;
                if (currentReality == Reality.PastReality)
                {
                    Log.Debug("ReplayGem: Ensuring hidden state in PastReality after interaction enabled", "ReplayGem");
                    ForceHideCompletely();
                }
                else
                {
                    // If we're in CurrentReality and this gem is statically placed, show it
                    CheckAndShowStaticGem();
                    
                    // Additional safety check after a short delay to ensure proper monster detection
                    GetTree().CreateTimer(0.5f).Timeout += () => {
                        if (ShouldHideForVisibleMonster() && _currentState != ReplayGemState.Hidden)
                        {
                            Log.Replay("Final safety check: Monster detected as visible after gem shown - hiding gem", "ReplayGem");
                            HideGem();
                        }
                    };
                }
            }
            else
            {
                // If RealityManager isn't available yet, assume CurrentReality and show static gem
                CheckAndShowStaticGem();
                
                // Additional safety check
                GetTree().CreateTimer(0.5f).Timeout += () => {
                    if (ShouldHideForVisibleMonster() && _currentState != ReplayGemState.Hidden)
                    {
                        Log.Replay("Final safety check (no RealityManager): Monster detected as visible - hiding gem", "ReplayGem");
                        HideGem();
                    }
                };
            }
        };

        Log.Replay("ReplayGem ready - hidden until activated", "ReplayGem");

        // Register this gem in a group so managers can find/hide/show them collectively
        AddToGroup("replay_gem");
    }

    //UPDATED METHOD: Improved connection to RealityManager
    private void ConnectToRealityManager()
    {
        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            Log.Debug("ReplayGem connecting to RealityManager", "ReplayGem");
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;

            // Apply current reality immediately to ensure correct initial state
            var currentReality = RealityManager.Instance.CurrentReality;
            Log.Debug($"ReplayGem applying initial reality state: {currentReality}", "ReplayGem");
            OnRealityChanged(currentReality);
        }
        else
        {
            // Retry connection after a short delay
            Log.Debug("RealityManager not available, retrying connection in 0.1s", "ReplayGem");
            GetTree().CreateTimer(0.1f).Timeout += ConnectToRealityManager;
        }
    }

    private void CheckAndShowStaticGem()
    {
        // Check if this gem is statically placed in scene (not dynamically created)
        // We can detect this by checking if the gem has a scene file path
        var sceneFilePath = GetTree().CurrentScene?.SceneFilePath;
        bool isStaticGem = !string.IsNullOrEmpty(sceneFilePath);

        if (isStaticGem)
        {
            Log.Replay($"Detected static ReplayGem in scene {sceneFilePath} - deferring monster visibility check", "ReplayGem");
            
            // Defer the visibility check to ensure all scene nodes are ready
            CallDeferred(nameof(CheckStaticGemVisibilityDeferred));
        }
    }
    
    private void CheckStaticGemVisibilityDeferred()
    {
        Log.Replay("CheckStaticGemVisibilityDeferred: Checking if any monsters are visible", "ReplayGem");
        
        // Force monster binding first
        TryBindToMonster();
        
        // Only show if no monster is currently visible
        if (ShouldHideForVisibleMonster())
        {
            Log.Replay("CheckStaticGemVisibilityDeferred: Monster is visible - keeping ReplayGem hidden", "ReplayGem");
            return;
        }
        
        Log.Replay("CheckStaticGemVisibilityDeferred: No visible monsters found - showing gem", "ReplayGem");
        ShowGem(GlobalPosition);
    }
    private void InitializeComponents()
    {
        // Get required child nodes with error handling
        _detectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "DetectionArea");
        _gemSprite = NodeHelpers.GetNodeOrNull<Sprite2D>(this, null, "GemSprite");
        _promptLabel = NodeHelpers.GetNodeOrNull<Label>(this, null, "PromptLabel");

        // Store original gem position for floating animation
        if (_gemSprite != null)
            _originalGemPosition = _gemSprite.Position;

        // Ensure default modulate is normal white so brightness tweens start from a known value
        if (_gemSprite != null)
            _gemSprite.Modulate = Colors.White;

        // Create tween for floating animation when needed
        _floatingTween = null;
    }

    private void ConnectSignals()
    {
        // Connect detection area signals
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;
    }

    private void SetupPromptLabel()
    {
        // Configure prompt label appearance
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";
    }

    private void HideInitially()
    {
        // Start hidden until shown by GameManager
        _detectionArea.SetDeferred("monitoring", false);
        _promptLabel.Hide();
        if (_gemSprite != null)
        {
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Hide();
        }

        Hide();
        _currentState = ReplayGemState.Hidden;
    }

    private void EnableInputProcessing()
    {
        // Enable input processing for interaction
        SetProcess(true);
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        UpdateMonsterFollowing(delta);

        // Handle interaction input when player is nearby and interaction is enabled
        if (!_playerNearby || !_canInteract) return;

        if (Input.IsActionJustPressed("interact"))
        {
            HandleReplayActivation();
        }
    }

    private void HandleReplayActivation()
    {
        Log.Replay("=== ReplayGem HandleReplayActivation called ===", "ReplayGem");

        // Check if player has enough mana (requires 1 mana)
        var playerMana = GetPlayerManaSystem();
        if (playerMana == null)
        {
            Log.Warning("PlayerMana system not found", "ReplayGem");
            return;
        }

        if (!playerMana.HasEnoughMana(1))
        {
            // Show "Not enough mana" message
            ShowInsufficientManaMessage();
            Log.Replay("Replay activation failed: Insufficient mana", "ReplayGem");
            return;
        }


            // Check for available last death record before consuming mana
            var deathRecord = GetDeathRecord();
            if (deathRecord == null)
            {
                // Show UI message when no death record is available
                Log.Warning("No death record available for replay", "ReplayGem");
                try { UIManager.ShowGameplayMessage("No replay data available yet."); } catch { }
                return;
            }

            // Consume 1 mana for replay
            playerMana.UseMana(1);
            Log.Replay("Consumed 1 mana for replay activation", "ReplayGem");

        // Change reality to PastReality when starting replay
        Log.Replay($"Attempting to start replay - RealityManager availability: {RealityManager.IsInstanceAvailable}", "ReplayGem");

        if (RealityManager.IsInstanceAvailable)
        {
            Log.Replay($"Current reality before replay: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
            RealityManager.Instance.StartReplay();
            Log.Replay($"Reality after StartReplay call: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
        }
        else
        {
            Log.Warning("RealityManager.Instance is null - cannot change reality. This suggests RealityManager hasn't been initialized yet.", "ReplayGem");
            Log.Warning("Check that RealityManager is properly added to the scene tree before ReplayGem activation.", "ReplayGem");
        }

        // At this point, deathRecord is guaranteed to be non-null (checked before consuming mana)
        Log.Replay($"ReplayGem found death record: {deathRecord.DeathId}", "ReplayGem");

        // Hide gem during replay
        HideGemDuringReplay();

        // Start replay through orchestrator, passing this gem instance
        if (ReplayOrchestrator.Instance != null)
        {
            Log.Replay("ReplayGem calling ReplayOrchestrator.StartReplay", "ReplayGem");
            ReplayOrchestrator.Instance.StartReplay(deathRecord, this);
        }
        else
        {
            Log.Error("ReplayOrchestrator.Instance is null", "ReplayGem");
            // Refund mana if replay fails
            playerMana.RestoreMana(1);
        }
    }

    private DeathRecord GetDeathRecord()
    {
        if (ReplaySystemManager.Instance == null)
        {
            Log.Error("ReplaySystemManager.Instance is null - cannot get death record", "ReplayGem");
            return null;
        }

        return ReplaySystemManager.Instance.GetLastDeathRecord();
    }

    private void HideGemDuringReplay()
    {
        // Stop floating animation during replay
        StopFloatingAnimation();

        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }
        if (_gemSprite != null)
        {
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Hide();
        }

        _currentState = ReplayGemState.Hidden;
    }
    #endregion

    #region Monster Following
    private void UpdateMonsterFollowing(double delta)
    {
        if (!FollowMonsterEnabled())
        {
            return;
        }

        if (!IsInCurrentReality())
        {
            return;
        }

        if (_targetMonster == null || !IsInstanceValid(_targetMonster) || _targetMonster.IsQueuedForDeletion())
        {
            UnbindFromMonster();

            _monsterSearchTimer -= delta;
            if (_monsterSearchTimer <= 0.0)
            {
                _monsterSearchTimer = MonsterSearchIntervalSeconds;
                TryBindToMonster();
            }

            // FIXED: Only show gem if no monsters are visible anywhere in the scene
            if (_currentState == ReplayGemState.ParkedAtLastMonsterPosition)
            {
                if (!ShouldHideForVisibleMonster())
                {
                    ShowGem(_initialScenePosition, ReplayGemState.ParkedAtLastMonsterPosition);
                }
                else
                {
                    Log.Replay("UpdateMonsterFollowing: Monster is visible - keeping gem hidden even without bound monster", "ReplayGem");
                    HideGem();
                }
            }
            return;
        }

    MonsterState currentState = _targetMonster.GetState();
    bool monsterVisible = IsMonsterVisibleToPlayer(_targetMonster);

        if (monsterVisible)
        {
            // Monster is visible - HIDE the gem and track monster position
            if (_currentState != ReplayGemState.Hidden)
            {
                HideGem();
            }

            // Keep tracking the monster's position while it's visible
            _lastFollowPosition = CalculateFollowPosition();
            _lastMonsterVisiblePosition = _targetMonster.GlobalPosition;
        }
        else
        {
            // Monster is hidden - SHOW the gem at last known visible position
            if (_lastKnownMonsterState == MonsterState.Visible)
            {
                _lastMonsterVisiblePosition = _targetMonster.GlobalPosition;
            }

            Vector2 showPosition = _lastMonsterVisiblePosition;
            if (Mathf.IsZeroApprox(showPosition.LengthSquared()))
            {
                showPosition = _targetMonster.GlobalPosition;
            }

            if (_currentState != ReplayGemState.ParkedAtLastMonsterPosition)
            {
                // Only show if no other monsters are visible
                if (!ShouldHideForVisibleMonster())
                {
                    ShowGem(showPosition, ReplayGemState.ParkedAtLastMonsterPosition);
                }
                else
                {
                    Log.Replay("UpdateMonsterFollowing: Cannot show gem - other monsters are visible", "ReplayGem");
                    HideGem();
                }
            }
            else
            {
                // Gem is already shown, just update position
                GlobalPosition = showPosition;
            }

            _lastFollowPosition = showPosition;
            _lastKnownMonsterState = currentState;
            return;
        }

        _lastKnownMonsterState = currentState;
    }

    private bool FollowMonsterEnabled()
    {
        // Only follow a monster if one exists; otherwise remain static
        return true;
    }

    private void TryBindToMonster()
    {
        Monster followerCandidate = null;
        Monster fallbackCandidate = null;

        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Monster monster || !IsInstanceValid(monster) || monster.IsQueuedForDeletion())
                continue;

            if (monster.EnableFollowerBehavior)
            {
                followerCandidate = monster;
                break;
            }

            fallbackCandidate ??= monster;
        }

        var chosen = followerCandidate ?? fallbackCandidate;
        if (chosen != null)
        {
            BindToMonster(chosen);
        }
    }

    private void BindToMonster(Monster monster)
    {
        if (_targetMonster == monster)
            return;

        UnbindFromMonster();

        _targetMonster = monster;
        if (_targetMonster == null)
            return;

        try { _targetMonster.TreeExited += OnTargetMonsterTreeExited; } catch { }

        UpdateMonsterOffsetDirection();

        _lastKnownMonsterState = _targetMonster.GetState();
        _lastFollowPosition = CalculateFollowPosition();
        _lastMonsterVisiblePosition = _targetMonster.GlobalPosition;

        if (!IsInCurrentReality())
        {
            HideGem();
        }
        else
        {
            // Check current monster state for immediate visibility decision
            var currentState = _lastKnownMonsterState;
            bool monsterVisible = IsMonsterVisibleToPlayer(_targetMonster);
            
            if (monsterVisible)
            {
                // Monster is visible - hide the gem
                Log.Debug($"BindToMonster: Hiding gem because monster {_targetMonster.Name} is visible", "ReplayGem");
                HideGem();
            }
            else
            {
                // Monster is hidden or inactive - show gem at last visible position
                Log.Debug($"BindToMonster: Showing gem because monster {_targetMonster.Name} is not visible", "ReplayGem");
                ShowGem(_lastMonsterVisiblePosition, ReplayGemState.ParkedAtLastMonsterPosition);
            }
        }

        Log.Debug($"ReplayGem bound to monster {_targetMonster.Name}", "ReplayGem");
    }

    private void UnbindFromMonster()
    {
        if (_targetMonster != null)
        {
            try { _targetMonster.TreeExited -= OnTargetMonsterTreeExited; } catch { }
        }
        _targetMonster = null;
        _lastKnownMonsterState = MonsterState.Visible;
        _lastFollowPosition = GlobalPosition;
        _lastMonsterVisiblePosition = GlobalPosition;
        _currentState = ReplayGemState.Hidden;
    }

    private void OnTargetMonsterTreeExited()
    {
        UnbindFromMonster();
        _monsterSearchTimer = 0.0f;
    }

    private Vector2 CalculateFollowPosition()
    {
        if (_targetMonster == null)
        {
            return GlobalPosition;
        }

        UpdateMonsterOffsetDirection();

        Vector2 basePosition = _targetMonster.GlobalPosition;
        Vector2 lateral = _monsterOffsetDirection * FollowDistance;
        Vector2 vertical = new Vector2(0f, FollowHeightOffset);

        return basePosition + lateral + vertical;
    }

    private void UpdateMonsterOffsetDirection()
    {
        if (_targetMonster == null || !IsInstanceValid(_targetMonster))
        {
            _monsterOffsetDirection = Vector2.Up;
            return;
        }

        Player player = PlayerStateManager.Instance?.GetPlayer();
        if (player != null && IsInstanceValid(player))
        {
            Vector2 diff = player.GlobalPosition - _targetMonster.GlobalPosition;
            if (diff.LengthSquared() > 1f)
            {
                _monsterOffsetDirection = diff.Normalized();
                return;
            }
        }

        Vector2 initialDiff = _initialScenePosition - _targetMonster.GlobalPosition;
        if (initialDiff.LengthSquared() > 1f)
        {
            _monsterOffsetDirection = initialDiff.Normalized();
            return;
        }

        _monsterOffsetDirection = Vector2.Up;
    }

    private bool IsMonsterActive(Monster monster)
    {
        if (monster == null || !IsInstanceValid(monster) || monster.IsQueuedForDeletion())
            return false;

        if (!monster.Visible)
            return false;

        if (!monster.IsCurrentlyVisible)
            return false;

        if (monster.IsDisabledForReplay)
            return false;

        if (monster.LifeSystem?.IsDead == true)
            return false;

        if (EnemyManager.Instance != null)
        {
            if (EnemyManager.Instance.IsMonsterHiddenByPair(monster) || EnemyManager.Instance.IsMonsterScheduledForRespawn(monster))
                return false;
        }

        return true;
    }

    private bool IsMonsterVisibleToPlayer(Monster monster)
    {
        if (monster == null || !IsInstanceValid(monster) || monster.IsQueuedForDeletion())
            return false;

        var state = monster.GetState();
        if (state == MonsterState.Visible)
            return true;

        if (state == MonsterState.Hidden)
            return false;

        // Fall back to broader visibility heuristics for transitional states
        if (!monster.Visible || !monster.IsCurrentlyVisible)
            return false;

        if (monster.IsDisabledForReplay)
            return false;

        if (monster.LifeSystem?.IsDead == true)
            return false;

        if (EnemyManager.Instance != null)
        {
            if (EnemyManager.Instance.IsMonsterHiddenByPair(monster) || EnemyManager.Instance.IsMonsterScheduledForRespawn(monster))
                return false;
        }

        return true;
    }

    private bool IsInCurrentReality()
    {
        return !RealityManager.IsInstanceAvailable || RealityManager.Instance.CurrentReality == Reality.CurrentReality;
    }
    #endregion

    public override void _ExitTree()
    {
        UnbindFromMonster();

        // Clean up reality manager connection
        if (RealityManager.IsInstanceAvailable)
        {
            RealityManager.Instance.RealityChangedEvent -= OnRealityChanged;
        }
    }

    #region Gem Visibility Control
    public void ShowGem(Vector2 position)
    {
        ShowGem(position, ReplayGemState.FollowingMonster);
    }

    private void ShowGem(Vector2 position, ReplayGemState newState)
    {
        // CRITICAL: Always check if we should hide due to visible monster before showing
        if (ShouldHideForVisibleMonster())
        {
            Log.Replay($"ShowGem called but monster is visible - keeping gem hidden at {position}", "ReplayGem");
            _currentState = ReplayGemState.Hidden;
            return;
        }

        GlobalPosition = position;
        _lastFollowPosition = position;
        if (newState == ReplayGemState.ParkedAtLastMonsterPosition)
        {
            _lastMonsterVisiblePosition = position;
        }

        _currentState = newState;

        // Display gem at specified position
        Show();

        if (_gemSprite != null)
        {
            _gemSprite.Show();
            StartFloatingAnimation();
        }

        if (_promptLabel != null)
        {
            _promptLabel.Hide(); // Only show when player nearby
        }

        if (_detectionArea != null)
        {
            _detectionArea.SetDeferred("monitorable", true);
            _detectionArea.SetDeferred("monitoring", true);
        }
        Log.Replay($"ReplayGem shown at {position}, scale={Scale}", "ReplayGem");
    }

    public void HideGem()
    {
        // Hide gem completely and cleanup
        if (_detectionArea != null)
        {
            _detectionArea.SetDeferred("monitoring", false);
            _detectionArea.SetDeferred("monitorable", false);
        }

        // Stop floating animation
        StopFloatingAnimation();

        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }

        if (_gemSprite != null)
        {
            _gemSprite.Hide();
        }

        _playerNearby = false;
        _currentState = ReplayGemState.Hidden;
    }

    // Handles reality changes: hide when entering PastReality, restore when leaving
    private void OnRealityChanged(Reality newReality)
    {
        Log.Debug($"ReplayGem OnRealityChanged called - newReality={newReality}, currentVisible={Visible}", "ReplayGem");

        if (newReality == Reality.PastReality)
        {
            // Remember if the gem was visible so we can restore later
            _wasVisibleBeforeRealityChange = Visible && (_gemSprite?.Visible ?? false);
            _stateBeforeRealityChange = _currentState;
            _positionBeforeRealityChange = GlobalPosition;
            Log.Replay($"Hiding ReplayGem because reality is PastReality (wasVisible={_wasVisibleBeforeRealityChange})", "ReplayGem");

            // IMPROVED: Force hide everything immediately
            ForceHideCompletely();
        }
        else if (newReality == Reality.CurrentReality)
        {
            // If we were visible before the reality switch, restore visuals
            if (_wasVisibleBeforeRealityChange)
            {
                Log.Replay("Restoring ReplayGem after returning from PastReality", "ReplayGem");
                var stateToRestore = _stateBeforeRealityChange == ReplayGemState.Hidden
                    ? ReplayGemState.FollowingMonster
                    : _stateBeforeRealityChange;

                var positionToRestore = stateToRestore == ReplayGemState.ParkedAtLastMonsterPosition
                    ? _lastMonsterVisiblePosition
                    : _positionBeforeRealityChange;

                if (stateToRestore == ReplayGemState.ParkedAtLastMonsterPosition && Mathf.IsZeroApprox(positionToRestore.LengthSquared()))
                {
                    positionToRestore = _positionBeforeRealityChange;
                }

                ShowGem(positionToRestore, stateToRestore);
                EnforceVisibilityForVisibleMonster();

                _stateBeforeRealityChange = ReplayGemState.Hidden;
            }
            _wasVisibleBeforeRealityChange = false;
        }
    }

    // NEW METHOD: Force complete hiding of gem
    public void ForceHideCompletely()
    {
        // Disable all interaction first
        if (_detectionArea != null)
        {
            _detectionArea.SetDeferred("monitoring", false);
            _detectionArea.SetDeferred("monitorable", false);
        }

        // Stop any animations
        StopFloatingAnimation();

        // Hide all visual components
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
            _promptLabel.Visible = false;
        }

        if (_gemSprite != null)
        {
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Hide();
            _gemSprite.Visible = false;
        }

        // Hide the main node
        Hide();
        Visible = false;

        // Clear player nearby state
        _playerNearby = false;
        _currentState = ReplayGemState.Hidden;

        Log.Debug("ReplayGem completely hidden for PastReality", "ReplayGem");
    }
    #endregion

    private void EnforceVisibilityForVisibleMonster()
    {
        if (ShouldHideForVisibleMonster())
        {
            Log.Replay("EnforceVisibilityForVisibleMonster: Hiding gem due to visible monster", "ReplayGem");
            if (_targetMonster != null && IsInstanceValid(_targetMonster))
            {
                _lastMonsterVisiblePosition = _targetMonster.GlobalPosition;
                _lastKnownMonsterState = _targetMonster.GetState();
            }
            HideGem();
        }
    }

    private bool ShouldHideForVisibleMonster()
    {
        // Always check ALL monsters in the scene for visibility
        var enemiesGroup = GetTree().GetNodesInGroup("enemies");
        Log.Replay($"ShouldHideForVisibleMonster: Checking {enemiesGroup.Count} enemies in scene", "ReplayGem");
        
        foreach (Node node in enemiesGroup)
        {
            if (node is Monster monster && IsInstanceValid(monster) && !monster.IsQueuedForDeletion())
            {
                bool isVisible = IsMonsterVisibleToPlayer(monster);
                Log.Replay($"ShouldHideForVisibleMonster: Monster {monster.Name} consideredVisible={isVisible}", "ReplayGem");
                if (isVisible)
                {
                    Log.Replay($"ShouldHideForVisibleMonster: Found visible monster {monster.Name} - gem should hide", "ReplayGem");
                    return true;
                }
            }
        }
        
        Log.Replay("ShouldHideForVisibleMonster: No visible monsters found - gem can show", "ReplayGem");
        return false;
    }

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        // Show prompt when player approaches
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            if (_promptLabel != null)
            {
                _promptLabel.Show();
            }
            Log.UI("Player nearby - showing prompt", "ReplayGem");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        // Hide prompt when player leaves
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            Log.UI("Player left - hiding prompt", "ReplayGem");
        }
    }
    #endregion

    #region Mana System Integration
    private PlayerMana GetPlayerManaSystem()
    {
        // Try multiple strategies to find PlayerMana system
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            var playerMana = player != null ? NodeHelpers.GetNodeOrNull<PlayerMana>(player, null, "PlayerMana") : null;
            if (playerMana != null) return playerMana;
        }

        // Fallback: search by group
        return GetTree().GetFirstNodeInGroup("mana_system") as PlayerMana;
    }

    private void ShowInsufficientManaMessage()
    {
        // Use UIManager to show the message
        UIManager.ShowGameplayMessage("Not enough mana!");
    }
    #endregion

    #region Floating Animation
    private void StartFloatingAnimation()
    {
        // Don't start if there's no sprite to animate
        if (_gemSprite == null) return;

        // If an existing tween is running, stop and clear it
        if (_floatingTween != null)
        {
            _floatingTween.Kill();
            _floatingTween = null;
        }

        // Create a new tween for the floating animation
        _floatingTween = CreateTween();
        _floatingTween.SetLoops(); // Loop infinitely

        // Reset to original position
        _gemSprite.Position = _originalGemPosition;

        // Create floating motion: up -> down -> up (continuous)
        var upPosition = new Vector2(_originalGemPosition.X, _originalGemPosition.Y - FloatingDistance);
        var downPosition = new Vector2(_originalGemPosition.X, _originalGemPosition.Y + FloatingDistance);

        _floatingTween.TweenProperty(_gemSprite, "position", upPosition, FloatingDuration / 2.0f);
        _floatingTween.TweenProperty(_gemSprite, "position", downPosition, FloatingDuration);
        _floatingTween.TweenProperty(_gemSprite, "position", _originalGemPosition, FloatingDuration / 2.0f);

        // Brightness pulsing effect (separate tween)
        if (BrightEffectEnabled)
        {
            // Ensure any existing brightness tween is stopped
            if (_brightnessTween != null)
            {
                _brightnessTween.Kill();
                _brightnessTween = null;
            }

            _brightnessTween = CreateTween();
            _brightnessTween.SetLoops();

            var normal = Colors.White;
            var bright = new Color(BrightnessFactor, BrightnessFactor, BrightnessFactor, 1.0f);

            // Pulse: normal -> bright -> normal
            _brightnessTween.TweenProperty(_gemSprite, "modulate", bright, BrightPulseDuration / 2.0f);
            _brightnessTween.TweenProperty(_gemSprite, "modulate", normal, BrightPulseDuration / 2.0f);
        }
    }

    private void StopFloatingAnimation()
    {
        if (_floatingTween != null)
        {
            _floatingTween.Kill();
            _floatingTween = null;
        }

        if (_brightnessTween != null)
        {
            _brightnessTween.Kill();
            _brightnessTween = null;
        }

        // Reset gem to original position
        if (_gemSprite != null)
        {
            // Reset visual tint to normal
            _gemSprite.Modulate = Colors.White;
            _gemSprite.Position = _originalGemPosition;
        }
    }
    #endregion

    #region Public API (for restoring gem after replay)
    public void ShowGemAfterReplay()
    {
        if (!IsInstanceValid(this))
        {
            return;
        }

        Vector2 targetPosition = GlobalPosition;
        ReplayGemState targetState = ReplayGemState.FollowingMonster;

        if (_targetMonster != null && IsInstanceValid(_targetMonster) && !_targetMonster.IsQueuedForDeletion())
        {
            var monsterState = _targetMonster.GetState();
            bool monsterVisible = monsterState == MonsterState.Visible && IsMonsterActive(_targetMonster);
            if (monsterVisible)
            {
                // Monster is visible - hide the gem and update last known position
                _lastMonsterVisiblePosition = _targetMonster.GlobalPosition;
                HideGem();
                // End replay in RealityManager to return to CurrentReality
                Log.Replay($"Attempting to end replay - RealityManager availability: {RealityManager.IsInstanceAvailable}", "ReplayGem");

                if (RealityManager.IsInstanceAvailable)
                {
                    Log.Replay($"Current reality before ending replay: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
                    RealityManager.Instance.EndReplay();
                    Log.Replay($"Reality after EndReplay call: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
                }
                else
                {
                    Log.Warning("RealityManager.Instance is null - cannot reset reality after replay", "ReplayGem");
                }
                return;
            }
            else
            {
                targetPosition = _lastMonsterVisiblePosition;
                if (Mathf.IsZeroApprox(targetPosition.LengthSquared()))
                {
                    targetPosition = _targetMonster.GlobalPosition;
                }
                targetState = ReplayGemState.ParkedAtLastMonsterPosition;
            }
        }

        ShowGem(targetPosition, targetState);

        // End replay in RealityManager to return to CurrentReality
        Log.Replay($"Attempting to end replay - RealityManager availability: {RealityManager.IsInstanceAvailable}", "ReplayGem");

        if (RealityManager.IsInstanceAvailable)
        {
            Log.Replay($"Current reality before ending replay: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
            RealityManager.Instance.EndReplay();
            Log.Replay($"Reality after EndReplay call: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "ReplayGem");
        }
        else
        {
            Log.Warning("RealityManager.Instance is null - cannot reset reality after replay", "ReplayGem");
        }
    }
    #endregion
}
