using Godot;
using System.Linq;
using MarrieGame.Core.Logging;

/// <summary>
/// Main game orchestrator following Single Responsibility Principle
/// Responsibility: Game Flow Coordination
/// 
/// SOLID Compliance:
/// - Single Responsibility: Only orchestrates between other managers
/// - Open/Closed: Extensible through events, no need to modify existing code
/// - Interface Segregation: Uses focused interfaces
/// - Dependency Inversion: Depends on abstractions (events) not implementations
/// </summary>
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }
    
    #region Game State
    public bool IsGameActive { get; private set; } = true;
    public bool IsPaused { get; private set; }
    #endregion
    
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            
            // Initialize logging system first
            InitializeLoggingSystem();
            
            // Initialize all managers in correct order
            InitializeManagers();
            
            // Subscribe to events for coordination
            SubscribeToEvents();
            
            Log.System("Game orchestration active", "GameManager");
        }
        else
        {
            QueueFree();
        }
    }
    
    private void InitializeLoggingSystem()
    {
        // Create and add the logger to the scene tree if it doesn't exist
        if (MarrieGame.Core.Logging.GameLogger.Instance == null)
        {
            var logger = new MarrieGame.Core.Logging.GameLogger();
            // Use call_deferred to avoid timing issues during scene setup
            GetTree().Root.CallDeferred("add_child", logger);
            CallDeferred(nameof(LogSystemInitialized));
        }
    }
    
    private void LogSystemInitialized()
    {
        Log.System("Logging system initialized", "GameManager");
    }
    
    private void InitializeManagers()
    {
        Log.StartTimer("ManagerInitialization");
        
        // Ensure all managers exist as children - order matters!
        EnsureManagerExists<SceneLifecycleManager>();  // First - needed for scene events
        EnsureManagerExists<PlayerStateManager>();
        EnsureManagerExists<UIManager>();
        EnsureManagerExists<ReplaySystemManager>();
        
        Log.EndTimer("ManagerInitialization", "All managers initialized");
    }
    
    private void EnsureManagerExists<T>() where T : Node, new()
    {
        if (GetChildren().OfType<T>().FirstOrDefault() == null)
        {
            var manager = new T();
            AddChild(manager);
            Log.System($"Created {typeof(T).Name}", "GameManager");
        }
    }
    
    private void SubscribeToEvents()
    {
        // Listen to key events for game flow coordination
        GameEventSystem.GameRestart += OnGameRestart;
        
        Log.System("Event subscriptions active", "GameManager");
    }
    
    private void OnPlayerDied(Vector2 deathPosition, int remainingHealth)
    {
        Log.Gameplay($"Coordinating player death response at {deathPosition} (Health: {remainingHealth})", "GameManager");
        
        // Pause the game
        PauseGame();
        
        // All specific handling is done by specialized managers
        // GameManager just coordinates the overall flow
    }
    
    private void OnGameRestart()
    {
        Log.Gameplay("Coordinating game restart", "GameManager");
        
        // Reset game state
        IsGameActive = true;
        ResumeGame();
        
        // Reset player
        PlayerStateManager.Instance?.RespawnPlayer();
        
        // Reset enemies
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.ResetAllEnemies();
        }
        
        Log.Gameplay("Game restart coordination complete", "GameManager");
    }
    
    #region Game State Management
    public bool IsGameOver => !IsGameActive;
    
    public void PauseGame()
    {
        if (IsPaused) return;
        
        IsPaused = true;
        GetTree().Paused = true;
        
        GameEventSystem.OnGameStateChanged("Paused");
        Log.Gameplay("Game paused", "GameManager");
    }
    
    public void ResumeGame()
    {
        if (!IsPaused) return;
        
        IsPaused = false;
        GetTree().Paused = false;
        
        GameEventSystem.OnGameStateChanged("Active");
        Log.Gameplay("Game resumed", "GameManager");
    }
    
    public void RestartGame()
    {
        Log.Gameplay("Restart requested", "GameManager");
        GameEventSystem.OnGameRestart();
    }
    
    public void QuitGame()
    {
        Log.System("Quit requested", "GameManager");
        GetTree().Quit();
    }
    
    public bool GetGameState()
    {
        return IsGameActive && !IsPaused;
    }
    #endregion
    
    #region Input Handling
    public override void _Input(InputEvent @event)
    {
        // Handle global game inputs (like pause)
        if (@event.IsActionPressed("ui_cancel")) // ESC key
        {
            if (IsPaused)
                ResumeGame();
            else
                PauseGame();
        }
    }
    #endregion
    
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            // Cleanup subscriptions
            GameEventSystem.GameRestart -= OnGameRestart;
            
            Instance = null;
            Log.System("Cleaned up", "GameManager");
        }
    }
}
