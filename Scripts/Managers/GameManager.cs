using Godot;
using System.Linq;

/// <summary>
/// Main game orchestrator following Single Responsibility Principle
/// Responsibility: Game Flow Coordination
/// 
/// SOLID Compliance:
/// - Single Responsibility: Only orchestrates between other managers
/// - Open/Closed: Extensible through events, no need to modify existing code
/// - Liskov Substitution: Implements IGameStateManager correctly
/// - Interface Segregation: Uses focused interfaces
/// - Dependency Inversion: Depends on abstractions (events) not implementations
/// </summary>
public partial class GameManager : Node, IGameStateManager
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
            
            // Initialize all managers in correct order
            InitializeManagers();
            
            // Subscribe to events for coordination
            SubscribeToEvents();
            
            GD.Print("GameManager: Initialized - Game orchestration active");
        }
        else
        {
            QueueFree();
        }
    }
    
    private void InitializeManagers()
    {
        // Ensure all managers exist as children
        EnsureManagerExists<SceneLifecycleManager>();
        EnsureManagerExists<PlayerStateManager>();
        EnsureManagerExists<UIManager>();
        EnsureManagerExists<ReplaySystemManager>();
        
        // Performance Monitor is a static class - just enable it
        PerformanceMonitor.Enable();
        
        GD.Print("GameManager: All managers initialized");
    }
    
    private void EnsureManagerExists<T>() where T : Node, new()
    {
        if (GetChildren().OfType<T>().FirstOrDefault() == null)
        {
            var manager = new T();
            AddChild(manager);
            GD.Print($"GameManager: Created {typeof(T).Name}");
        }
    }
    
    private void SubscribeToEvents()
    {
        // Listen to key events for game flow coordination
        GameEvents.GameRestart += OnGameRestart;
        
        GD.Print("GameManager: Event subscriptions active");
    }
    
    private void OnPlayerDied(Vector2 deathPosition, int remainingHealth)
    {
        GD.Print($"GameManager: Coordinating player death response at {deathPosition}");
        
        // Pause the game
        PauseGame();
        
        // All specific handling is done by specialized managers
        // GameManager just coordinates the overall flow
    }
    
    private void OnGameRestart()
    {
        GD.Print("GameManager: Coordinating game restart");
        
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
        
        GD.Print("GameManager: Game restart coordination complete");
    }
    
    #region IGameStateManager Implementation
    public bool IsGameOver => !IsGameActive;
    
    public void PauseGame()
    {
        if (IsPaused) return;
        
        IsPaused = true;
        GetTree().Paused = true;
        
        GameEvents.OnGameStateChanged("Paused");
        GD.Print("GameManager: Game paused");
    }
    
    public void ResumeGame()
    {
        if (!IsPaused) return;
        
        IsPaused = false;
        GetTree().Paused = false;
        
        GameEvents.OnGameStateChanged("Active");
        GD.Print("GameManager: Game resumed");
    }
    
    public void RestartGame()
    {
        GD.Print("GameManager: Restart requested");
        GameEvents.OnGameRestart();
    }
    
    public void QuitGame()
    {
        GD.Print("GameManager: Quit requested");
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
            GameEvents.GameRestart -= OnGameRestart;
            
            Instance = null;
            GD.Print("GameManager: Cleaned up");
        }
    }
}