using Godot;
using System.Linq;
using MarrieGame.Core.Logging;
using MarrieGame.Core.Enums;

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
    public string CurrentSceneName { get; private set; } = "";
    public bool IsPuzzleDimensionScene => CurrentSceneName.Contains("PuzzleDimension");
    public bool IsForestScene => CurrentSceneName.Contains("Forest");
    #endregion

    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;

            // Detect current scene
            DetectCurrentScene();

            // Initialize logging system first
            InitializeLoggingSystem();

            // Initialize all managers in correct order
            InitializeManagers();

            // Subscribe to events for coordination
            SubscribeToEvents();

            Log.System($"Game orchestration active - Scene: {CurrentSceneName}", "GameManager");
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
        // Debug level - system initialization is routine
        Log.Debug("Logging system initialized", "GameManager");

        // Configure logging for production performance
#if !DEBUG
            Log.ReduceVerboseLogging();
#endif
    }

    private void InitializeManagers()
    {
        Log.StartTimer("ManagerInitialization");

        // Ensure all managers exist as children - order matters!
        EnsureManagerExists<SceneLifecycleManager>();  // First - needed for scene events
        EnsureManagerExists<RealityManager>();         // Early - needed for reality-based game logic
        EnsureManagerExists<GlobalGhostLifeManager>(); // Global ghost life system
        EnsureManagerExists<PlayerStateManager>();     // Now managed by GameManager
        EnsureManagerExists<EnemyManager>();           // Add EnemyManager for consistency
        EnsureManagerExists<UIManager>();
        EnsureManagerExists<DialogManager>();          // Dialog system for NPCs
        EnsureManagerExists<ReplaySystemManager>();

        Log.EndTimer("ManagerInitialization", "All managers initialized");
    }

    private void EnsureManagerExists<T>() where T : Node, new()
    {
        if (GetChildren().OfType<T>().FirstOrDefault() == null)
        {
            var manager = new T();
            AddChild(manager);
            // Debug level - routine component creation
            Log.Debug($"Created {typeof(T).Name}", "GameManager");

            // Special logging for RealityManager to verify it's working
            if (typeof(T) == typeof(RealityManager))
            {
                // Use CallDeferred to ensure RealityManager's _Ready has been called
                CallDeferred(nameof(VerifyRealityManagerInitialization));
            }
        }
        else
        {
            Log.System($"{typeof(T).Name} already exists", "GameManager");
        }
    }

    private void VerifyRealityManagerInitialization()
    {
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System($"Verifying RealityManager initialization - IsInstanceAvailable: {RealityManager.IsInstanceAvailable}", "GameManager");
        if (RealityManager.IsInstanceAvailable)
        {
            if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
                Log.System($"RealityManager successfully initialized with reality: {RealityManager.Instance.GetCurrentRealityWithLogging()}", "GameManager");
        }
        else
        {
            Log.Error("RealityManager failed to initialize properly!", "GameManager");
        }
    }

    private void SubscribeToEvents()
    {
        // Listen to key events for game flow coordination
        GameEventSystem.GameRestart += OnGameRestart;
        
        // Listen to scene changes to update scene state
        SceneLifecycleManager.SceneReady += OnSceneChanged;

        Log.System("Event subscriptions active", "GameManager");
    }

    private void OnSceneChanged()
    {
        // Update current scene information
        DetectCurrentScene();
        
        // Notify replay recorder about potential recording mode change
        if (ReplayRecorder.Instance != null)
        {
            var newMode = GetReplayRecordingMode();
            ReplayRecorder.Instance.ForceSetRecordingMode(newMode);
            Log.System($"Updated ReplayRecorder mode to: {newMode} for scene: {CurrentSceneName}", "GameManager");
        }
    }

    private void DetectCurrentScene()
    {
        var currentScene = GetTree().CurrentScene;
        if (currentScene != null)
        {
            CurrentSceneName = currentScene.Name;
            
            // Also try to get the scene file name if Name is not descriptive enough
            var sceneFilePath = currentScene.SceneFilePath;
            if (!string.IsNullOrEmpty(sceneFilePath))
            {
                var sceneName = sceneFilePath.GetFile().GetBaseName(); // Gets filename without extension
                if (!string.IsNullOrEmpty(sceneName))
                {
                    CurrentSceneName = sceneName;
                }
            }
            
            Log.System($"Detected scene: {CurrentSceneName} (IsPuzzleDimension: {IsPuzzleDimensionScene}, IsForest: {IsForestScene})", "GameManager");
        }
        else
        {
            CurrentSceneName = "Unknown";
            Log.Warning("Could not detect current scene", "GameManager");
        }
    }

    private void OnPlayerDied(MarrieGame.Core.Models.PlayerDeath death)
    {
        // Important game event - always log player deaths for analysis
        Log.Error($"Coordinating player death response at {death.Position} (Health: {death.RemainingHealth})", "GameManager");

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

    GameEventSystem.OnGameStateChanged(MarrieGame.Core.Enums.GameState.Paused);
        Log.Gameplay("Game paused", "GameManager");
    }

    public void ResumeGame()
    {
        if (!IsPaused) return;

        IsPaused = false;
        GetTree().Paused = false;

    GameEventSystem.OnGameStateChanged(MarrieGame.Core.Enums.GameState.Active);
        Log.Gameplay("Game resumed", "GameManager");
    }

    public void RestartGame()
    {
        Log.Gameplay("Restart requested", "GameManager");

        // Reset reality state when restarting
        if (RealityManager.IsInstanceAvailable)
        {
            RealityManager.Instance.Reset();
            Log.System("Reality state reset for game restart", "GameManager");
        }

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

    /// <summary>
    /// Gets the appropriate replay recording mode for the current scene
    /// </summary>
    public ReplayRecordingMode GetReplayRecordingMode()
    {
        if (IsPuzzleDimensionScene)
        {
            return ReplayRecordingMode.PuzzleMode; // Player only, single session until gem press
        }
        else if (IsForestScene)
        {
            return ReplayRecordingMode.DeathMode; // Current behavior, record until death
        }
        else
        {
            // Default fallback behavior
            return ReplayRecordingMode.DeathMode;
        }
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
            SceneLifecycleManager.SceneReady -= OnSceneChanged;

            // Clear any remaining static subscriptions to avoid memory leaks
            GameEventSystem.ClearAllSubscriptions();

            Instance = null;
            Log.System("Cleaned up", "GameManager");
        }
    }
}
