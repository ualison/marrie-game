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
        Log.System("Attempting immediate initialization", "UIManager");
        DiscoverUIComponents();
        ConnectUIEvents();
        SubscribeToPlayerEvents();
        
        if (_gameOverUI != null)
        {
            Log.System("Immediate initialization successful", "UIManager");
        }
        else
        {
            Log.System("Immediate initialization failed, waiting for SceneReady", "UIManager");
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
            Log.System("Subscribed to PlayerStateManager.PlayerDied", "UIManager");
        }
        else
        {
            Log.Error("PlayerStateManager.Instance is null!", "UIManager");
        }
    }
    
    private void DiscoverUIComponents()
    {
        _gameOverUI = GetTree().GetFirstNodeInGroup("game_over_ui") as GameOver;
        _lifeBarUI = GetTree().GetFirstNodeInGroup("life_bar_ui") as LifeBar;
        _manaBarUI = GetTree().GetFirstNodeInGroup("mana_bar_ui") as ManaBar;
        
        Log.System($"GameOverUI found: {_gameOverUI != null}, LifeBarUI found: {_lifeBarUI != null}, ManaBarUI found: {_manaBarUI != null}", "UIManager");
        
        // Debug: List all nodes in the game_over_ui group
        var gameOverNodes = GetTree().GetNodesInGroup("game_over_ui");
        Log.System($"Found {gameOverNodes.Count} nodes in 'game_over_ui' group", "UIManager");
        foreach (Node node in gameOverNodes)
        {
            #if DEBUG
            Log.Debug($"  - {node.Name} ({node.GetType().Name})", "UIManager");
            #endif
        }
        
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
                    Log.System($"Found GameOverUI by name search: {name}", "UIManager");
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
                    Log.System("Found GameOverUI at Interface/GameOverScr", "UIManager");
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
                    Log.System("Found LifeBarUI at Interface/LifeBarUI", "UIManager");
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
                    Log.System("Found ManaBarUI at Interface/ManaBarUI", "UIManager");
                }
            }
        }
        
        // Last resort: search all nodes recursively
        if (_gameOverUI == null)
        {
            _gameOverUI = FindNodeByType<GameOver>(GetTree().CurrentScene);
            if (_gameOverUI != null)
            {
                Log.System($"Found GameOverUI by recursive search: {_gameOverUI.Name}", "UIManager");
            }
        }
        
        if (_manaBarUI == null)
        {
            _manaBarUI = FindNodeByType<ManaBar>(GetTree().CurrentScene);
            if (_manaBarUI != null)
            {
                Log.System($"Found ManaBarUI by recursive search: {_manaBarUI.Name}", "UIManager");
            }
        }
        
        // Final status
        Log.System($"Final result - GameOverUI: {_gameOverUI?.Name ?? "null"}, LifeBarUI: {_lifeBarUI?.Name ?? "null"}, ManaBarUI: {_manaBarUI?.Name ?? "null"}", "UIManager");
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
