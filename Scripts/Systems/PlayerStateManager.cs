using Godot;

/// <summary>
/// Manages player state, death, and respawn logic
/// Single Responsibility: Player State Management
/// </summary>
public partial class PlayerStateManager : Node
{
    public static PlayerStateManager Instance { get; private set; }
    
    #region Player State
    public Vector2 PlayerSpawnPosition { get; set; } = Vector2.Zero;
    public bool IsPlayerDead { get; private set; }
    #endregion
    
    #region Component References
    private Player _player;
    private PlayerLife _playerLife;
    #endregion
    
    #region Events
    public static event System.Action<Vector2, int> PlayerDied;
    public static event System.Action PlayerRespawned;
    #endregion
    
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            
            // Listen to scene changes to rediscover components
            SceneLifecycleManager.SceneReady += OnSceneReady;
            
            // Also try immediate initialization as fallback
            CallDeferred(nameof(TryImmediateInitialization));
        }
        else
        {
            QueueFree();
        }
    }
    
    private void TryImmediateInitialization()
    {
        Log.System("Attempting immediate initialization", "PlayerStateManager");
        DiscoverPlayerComponents();
        ConnectPlayerSystems();
        CachePlayerSpawn();
        
        if (_player != null && _playerLife != null)
        {
            Log.System("Immediate initialization successful", "PlayerStateManager");
        }
        else
        {
            Log.System("Immediate initialization failed, waiting for SceneReady", "PlayerStateManager");
        }
    }
    
    private void OnSceneReady()
    {
        DiscoverPlayerComponents();
        ConnectPlayerSystems();
        CachePlayerSpawn();
    }
    
    private void DiscoverPlayerComponents()
    {
        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        _playerLife = _player?.GetLifeSystem();
        
        Log.System($"Player found: {_player != null}, Life system: {_playerLife != null}", "PlayerStateManager");
        
        #if DEBUG
        if (_player != null)
        {
            Log.Debug($"Player node name: {_player.Name}", "PlayerStateManager");
        }
        
        if (_playerLife != null)
        {
            Log.Debug($"PlayerLife node name: {_playerLife.Name}", "PlayerStateManager");
        }
        #endif
    }
    
    private void ConnectPlayerSystems()
    {
        if (_playerLife != null)
        {
            // Disconnect first to avoid duplicates
            if (_playerLife.IsConnected(PlayerLife.SignalName.PlayerDied, new Callable(this, nameof(OnPlayerHealthDepleted))))
            {
                _playerLife.PlayerDied -= OnPlayerHealthDepleted;
#if DEBUG
                Log.Debug("Disconnected existing PlayerDied signal", "PlayerStateManager");
#endif
            }
            _playerLife.PlayerDied += OnPlayerHealthDepleted;
            Log.System("Connected to PlayerLife.PlayerDied signal", "PlayerStateManager");
        }
        else
        {
            Log.Error("Cannot connect to PlayerLife - it's null!", "PlayerStateManager");
        }
    }
    
    private void CachePlayerSpawn()
    {
        if (_player != null)
        {
            PlayerSpawnPosition = _player.GlobalPosition;
            Log.System($"Cached player spawn position: {PlayerSpawnPosition}", "PlayerStateManager");
        }
    }
    
    private void OnPlayerHealthDepleted()
    {
        if (IsPlayerDead) return;
        
        Log.Gameplay("Player health depleted", "PlayerStateManager");
        
        Vector2 deathPosition = _player?.GlobalPosition ?? Vector2.Zero;
        HandlePlayerDeath(deathPosition);
    }
    
    public void HandlePlayerDeath(Vector2 deathPosition)
    {
        if (IsPlayerDead) return;
        
        // Debug logging for replay state
        bool recorderExists = ReplayRecorder.Instance != null;
        bool isPausedForReplay = recorderExists && ReplayRecorder.Instance.IsPausedForReplay;
        bool isRecording = recorderExists && ReplayRecorder.Instance.IsRecording;
        
#if DEBUG
        Log.Debug($"Death handler - Recorder exists: {recorderExists}, IsPausedForReplay: {isPausedForReplay}, IsRecording: {isRecording}", "PlayerStateManager");
#endif
        
        // Don't allow death during replay - FIXED: Check for replay pause specifically
        if (ReplayRecorder.Instance != null && ReplayRecorder.Instance.IsPausedForReplay)
        {
            Log.Replay("Ignoring player death during replay", "PlayerStateManager");
            return;
        }
        
        Log.Gameplay($"Player died at {deathPosition}", "PlayerStateManager");
        
        IsPlayerDead = true;
        
        // Notify other systems
        Log.System("Firing PlayerDied event", "PlayerStateManager");
        PlayerDied?.Invoke(deathPosition, _playerLife?.CurrentHealth ?? 0);
        GameEventSystem.OnPlayerDied(deathPosition, _playerLife?.CurrentHealth ?? 0);
    }
    
    public void RespawnPlayer()
    {
        if (_player == null)
        {
            // Try to find player again
            _player = GetTree().GetFirstNodeInGroup("player") as Player;
        }
        
        if (_player != null)
        {
            Log.Gameplay($"Respawning player from {_player.GlobalPosition} to {PlayerSpawnPosition}", "PlayerStateManager");
            _player.GlobalPosition = PlayerSpawnPosition;
            _player.Reset();
            Log.Gameplay($"Player respawned at: {_player.GlobalPosition}", "PlayerStateManager");
        }
        else
        {
            Log.Error("Cannot respawn - player not found!", "PlayerStateManager");
        }
        
        IsPlayerDead = false;
        
        // Notify other systems
        PlayerRespawned?.Invoke();
    }
    
    public Player GetPlayer() => _player;
    public PlayerLife GetPlayerLife() => _playerLife;
    
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            SceneLifecycleManager.SceneReady -= OnSceneReady;
            Instance = null;
        }
    }
}
