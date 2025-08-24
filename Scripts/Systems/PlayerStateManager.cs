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
        GD.Print("PlayerStateManager: Attempting immediate initialization");
        DiscoverPlayerComponents();
        ConnectPlayerSystems();
        CachePlayerSpawn();
        
        if (_player != null && _playerLife != null)
        {
            GD.Print("PlayerStateManager: Immediate initialization successful");
        }
        else
        {
            GD.Print("PlayerStateManager: Immediate initialization failed, waiting for SceneReady");
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
        
        GD.Print($"PlayerStateManager: Player found: {_player != null}, Life system: {_playerLife != null}");
        
        if (_player != null)
        {
            GD.Print($"PlayerStateManager: Player node name: {_player.Name}");
        }
        
        if (_playerLife != null)
        {
            GD.Print($"PlayerStateManager: PlayerLife node name: {_playerLife.Name}");
        }
    }
    
    private void ConnectPlayerSystems()
    {
        if (_playerLife != null)
        {
            // Disconnect first to avoid duplicates
            if (_playerLife.IsConnected(PlayerLife.SignalName.PlayerDied, new Callable(this, nameof(OnPlayerHealthDepleted))))
            {
                _playerLife.PlayerDied -= OnPlayerHealthDepleted;
                GD.Print("PlayerStateManager: Disconnected existing PlayerDied signal");
            }
            _playerLife.PlayerDied += OnPlayerHealthDepleted;
            GD.Print("PlayerStateManager: Connected to PlayerLife.PlayerDied signal");
        }
        else
        {
            GD.PrintErr("PlayerStateManager: Cannot connect to PlayerLife - it's null!");
        }
    }
    
    private void CachePlayerSpawn()
    {
        if (_player != null)
        {
            PlayerSpawnPosition = _player.GlobalPosition;
            GD.Print($"Cached player spawn position: {PlayerSpawnPosition}");
        }
    }
    
    private void OnPlayerHealthDepleted()
    {
        if (IsPlayerDead) return;
        
        GD.Print($"PlayerStateManager: Player health depleted");
        
        Vector2 deathPosition = _player?.GlobalPosition ?? Vector2.Zero;
        HandlePlayerDeath(deathPosition);
    }
    
    public void HandlePlayerDeath(Vector2 deathPosition)
    {
        if (IsPlayerDead) return;
        
        // Don't allow death during replay
        if (ReplayRecorder.Instance != null && ReplayRecorder.Instance.IsRecording == false)
        {
            GD.Print("Ignoring player death during replay");
            return;
        }
        
        GD.Print($"PlayerStateManager: Player died at {deathPosition}");
        
        IsPlayerDead = true;
        
        // Notify other systems
        GD.Print("PlayerStateManager: Firing PlayerDied event");
        PlayerDied?.Invoke(deathPosition, _playerLife?.CurrentHealth ?? 0);
        GameEvents.OnPlayerDied(deathPosition, _playerLife?.CurrentHealth ?? 0);
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
            GD.Print($"Respawning player from {_player.GlobalPosition} to {PlayerSpawnPosition}");
            _player.GlobalPosition = PlayerSpawnPosition;
            _player.Reset();
            GD.Print($"Player respawned at: {_player.GlobalPosition}");
        }
        else
        {
            GD.PrintErr("Cannot respawn - player not found!");
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
