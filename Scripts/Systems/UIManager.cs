using Godot;
using System.Linq;

/// <summary>
/// Manages UI lifecycle and interactions
/// Single Responsibility: UI Management
/// </summary>
public partial class UIManager : Node
{
    public static UIManager Instance { get; private set; }
    
    #region UI Components
    private GameOver _gameOverUI;
    private LifeBar _lifeBarUI;
    private ManaBar _manaBarUI;
    private GameMessage _gameMessageUI;
    #endregion
    
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            
            // Listen to scene changes to rediscover UI components
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
        // Only log when initialization completely fails
        DiscoverUIComponents();
        ConnectUIEvents();
        SubscribeToPlayerEvents();
        
        if (_gameOverUI == null)
        {
            Log.Warning("UIManager initialization incomplete - GameOverUI not found", "UIManager");
        }
    }
    
    private void OnSceneReady()
    {
        DiscoverUIComponents();
        ConnectUIEvents();
        SubscribeToPlayerEvents();
    }
    
    private void SubscribeToPlayerEvents()
    {
        // Subscribe to player state events after all managers are initialized
        if (PlayerStateManager.Instance != null)
        {
            PlayerStateManager.PlayerDied += OnPlayerDied;
            // Debug level - connection successful
            Log.Debug("Subscribed to PlayerStateManager.PlayerDied", "UIManager");
        }
        else
        {
            // Critical error - game won't function properly
            Log.Error("PlayerStateManager.Instance is null!", "UIManager");
        }
    }
    
    private void DiscoverUIComponents()
    {
        _gameOverUI = GetTree().GetFirstNodeInGroup("game_over_ui") as GameOver;
        _lifeBarUI = GetTree().GetFirstNodeInGroup("life_bar_ui") as LifeBar;
        _manaBarUI = GetTree().GetFirstNodeInGroup("mana_bar_ui") as ManaBar;
        _gameMessageUI = GetTree().GetFirstNodeInGroup("game_message_ui") as GameMessage;
        
        // Only log critical missing components
        if (_gameOverUI == null)
            Log.Warning("GameOverUI not found in groups, searching by name...", "UIManager");
        
        // Debug: List all nodes in the game_over_ui group only when debugging
        var gameOverNodes = GetTree().GetNodesInGroup("game_over_ui");
        if (gameOverNodes.Count == 0 && _gameOverUI == null)
        {
            Log.Warning("No nodes found in 'game_over_ui' group", "UIManager");
        }
        
        #if DEBUG
        foreach (Node node in gameOverNodes)
        {
            Log.Debug($"  - {node.Name} ({node.GetType().Name})", "UIManager");
        }
        #endif
        
        // If not found by group, try to find by common names
        if (_gameOverUI == null)
        {
            var possibleNames = new[] { "GameOverScr", "GameOverUI", "GameOver", "UI/GameOverScr", "Interface/GameOverScr" };
            
            foreach (var name in possibleNames)
            {
                var node = GetTree().CurrentScene.FindChild(name, true, false);
                if (node is GameOver gameOverUI)
                {
                    _gameOverUI = gameOverUI;
                    // Debug level - fallback search succeeded
                    Log.Debug($"Found GameOverUI by name search: {name}", "UIManager");
                    break;
                }
            }
        }
        
        // Try direct path based on your scene structure
        if (_gameOverUI == null)
        {
            var interfaceNode = GetTree().CurrentScene.FindChild("Interface", false, false);
            if (interfaceNode != null)
            {
                var gameOverNode = interfaceNode.FindChild("GameOverScr", false, false);
                if (gameOverNode is GameOver gameOverUI)
                {
                    _gameOverUI = gameOverUI;
                    // Debug level - fallback path search succeeded
                    Log.Debug("Found GameOverUI at Interface/GameOverScr", "UIManager");
                }
            }
        }
        
        // Similar search for LifeBarUI
        if (_lifeBarUI == null)
        {
            var interfaceNode = GetTree().CurrentScene.FindChild("Interface", false, false);
            if (interfaceNode != null)
            {
                var lifeBarNode = interfaceNode.FindChild("LifeBarUI", false, false);
                if (lifeBarNode is LifeBar lifeBarUI)
                {
                    _lifeBarUI = lifeBarUI;
                    Log.Debug("Found LifeBarUI at Interface/LifeBarUI", "UIManager");
                }
            }
        }
        
        // Similar search for ManaBarUI
        if (_manaBarUI == null)
        {
            var interfaceNode = GetTree().CurrentScene.FindChild("Interface", false, false);
            if (interfaceNode != null)
            {
                var manaBarNode = interfaceNode.FindChild("ManaBarUI", false, false);
                if (manaBarNode is ManaBar manaBarUI)
                {
                    _manaBarUI = manaBarUI;
                    Log.Debug("Found ManaBarUI at Interface/ManaBarUI", "UIManager");
                }
            }
        }
        
        // Last resort: search all nodes recursively
        if (_gameOverUI == null)
        {
            _gameOverUI = FindNodeByType<GameOver>(GetTree().CurrentScene);
            if (_gameOverUI != null)
            {
                Log.Debug($"Found GameOverUI by recursive search: {_gameOverUI.Name}", "UIManager");
            }
        }
        
        if (_manaBarUI == null)
        {
            _manaBarUI = FindNodeByType<ManaBar>(GetTree().CurrentScene);
            if (_manaBarUI != null)
            {
                Log.Debug($"Found ManaBarUI by recursive search: {_manaBarUI.Name}", "UIManager");
            }
        }
        
        if (_gameMessageUI == null)
        {
            _gameMessageUI = FindNodeByType<GameMessage>(GetTree().CurrentScene);
            if (_gameMessageUI != null)
            {
                Log.Debug($"Found GameMessageUI by recursive search: {_gameMessageUI.Name}", "UIManager");
            }
        }
        
        // Final status - debug level for initialization tracking
        Log.Debug($"Final result - GameOverUI: {_gameOverUI?.Name ?? "null"}, LifeBarUI: {_lifeBarUI?.Name ?? "null"}, ManaBarUI: {_manaBarUI?.Name ?? "null"}, GameMessageUI: {_gameMessageUI?.Name ?? "null"}", "UIManager");
    }
    
    private T FindNodeByType<T>(Node startNode) where T : class
    {
        if (startNode is T result)
            return result;
            
        foreach (Node child in startNode.GetChildren())
        {
            var found = FindNodeByType<T>(child);
            if (found != null)
                return found;
        }
        
        return null;
    }
    
    private void ConnectUIEvents()
    {
        if (_gameOverUI != null)
        {
            // Disconnect first to avoid duplicates
            if (_gameOverUI.IsConnected(GameOver.SignalName.RestartRequested, new Callable(this, nameof(OnRestartRequested))))
            {
                _gameOverUI.RestartRequested -= OnRestartRequested;
            }
            _gameOverUI.RestartRequested += OnRestartRequested;
        }
    }
    
    private void OnPlayerDied(Vector2 deathPosition, int remainingHealth)
    {
        Log.UI($"Received player death event at {deathPosition}", "UIManager");
        
        // Wait 1 second before showing game over UI
        GetTree().CreateTimer(1.0f).Timeout += () => ShowGameOverUIWithFade();
    }
    
    private void ShowGameOverUIWithFade()
    {
        if (_gameOverUI != null)
        {
            Log.UI("Showing GameOverUI with fade transition", "UIManager");
            
            // Start invisible and fade in
            _gameOverUI.Modulate = new Color(1, 1, 1, 0);
            _gameOverUI.Show();
            
            // Create fade-in tween
            var tween = CreateTween();
            tween.TweenProperty(_gameOverUI, "modulate:a", 1.0f, 0.5f);
            tween.TweenCallback(Callable.From(() => Log.UI("Fade-in complete", "UIManager")));
        }
        else
        {
            Log.Error("Cannot show game over - GameOverUI not found!", "UIManager");
        }
    }
    
    private void ShowGameOverUI()
    {
        // Legacy method - keeping for compatibility
        ShowGameOverUIWithFade();
    }
    
    private void HideGameOverUI()
    {
        if (_gameOverUI != null)
        {
            // Fade out before hiding
            var tween = CreateTween();
            tween.TweenProperty(_gameOverUI, "modulate:a", 0.0f, 0.3f);
            tween.TweenCallback(Callable.From(() => {
                _gameOverUI.Hide();
                _gameOverUI.Modulate = new Color(1, 1, 1, 1); // Reset for next time
                Log.UI("GameOverUI hidden with fade-out", "UIManager");
            }));
        }
    }
    
    private void OnRestartRequested()
    {
        Log.UI("Restart requested via GameOverUI", "UIManager");
        HideGameOverUI();
        
        // Notify game restart
        GameEventSystem.OnGameRestart();
    }
    
    public GameOver GetGameOverUI() => _gameOverUI;
    public LifeBar GetLifeBarUI() => _lifeBarUI;
    public ManaBar GetManaBarUI() => _manaBarUI;
    public GameMessage GetGameMessageUI() => _gameMessageUI;
    
    #region Public Message API
    /// <summary>
    /// Show a gameplay message to the player
    /// </summary>
    /// <param name="message">The message to display</param>
    public static void ShowGameplayMessage(string message)
    {
        if (Instance?._gameMessageUI != null)
        {
            Instance._gameMessageUI.ShowMessage(message);
            Log.UI($"Showing gameplay message: {message}", "UIManager");
        }
        else
        {
            Log.Warning($"Cannot show message '{message}' - GameMessageUI not found", "UIManager");
        }
    }
    
    /// <summary>
    /// Hide the current gameplay message if one is showing
    /// </summary>
    public static void HideGameplayMessage()
    {
        if (Instance?._gameMessageUI != null)
        {
            Instance._gameMessageUI.HideMessage();
        }
    }
    
    /// <summary>
    /// Check if a gameplay message is currently being displayed
    /// </summary>
    /// <returns>True if a message is showing</returns>
    public static bool IsShowingGameplayMessage()
    {
        return Instance?._gameMessageUI?.IsShowingMessage() ?? false;
    }
    #endregion
    
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            SceneLifecycleManager.SceneReady -= OnSceneReady;
            if (PlayerStateManager.Instance != null)
                PlayerStateManager.PlayerDied -= OnPlayerDied;
            Instance = null;
        }
    }
}
