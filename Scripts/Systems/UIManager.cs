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
    private GameOverUI _gameOverUI;
    private LifeBarUI _lifeBarUI;
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
        GD.Print("UIManager: Attempting immediate initialization");
        DiscoverUIComponents();
        ConnectUIEvents();
        SubscribeToPlayerEvents();
        
        if (_gameOverUI != null)
        {
            GD.Print("UIManager: Immediate initialization successful");
        }
        else
        {
            GD.Print("UIManager: Immediate initialization failed, waiting for SceneReady");
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
            GD.Print("UIManager: Subscribed to PlayerStateManager.PlayerDied");
        }
        else
        {
            GD.PrintErr("UIManager: PlayerStateManager.Instance is null!");
        }
    }
    
    private void DiscoverUIComponents()
    {
        _gameOverUI = GetTree().GetFirstNodeInGroup("game_over_ui") as GameOverUI;
        _lifeBarUI = GetTree().GetFirstNodeInGroup("life_bar_ui") as LifeBarUI;
        
        GD.Print($"UIManager: GameOverUI found: {_gameOverUI != null}, LifeBarUI found: {_lifeBarUI != null}");
        
        // Debug: List all nodes in the game_over_ui group
        var gameOverNodes = GetTree().GetNodesInGroup("game_over_ui");
        GD.Print($"UIManager: Found {gameOverNodes.Count} nodes in 'game_over_ui' group");
        foreach (Node node in gameOverNodes)
        {
            GD.Print($"  - {node.Name} ({node.GetType().Name})");
        }
        
        // If not found by group, try to find by common names
        if (_gameOverUI == null)
        {
            var possibleNames = new[] { "GameOverScr", "GameOverUI", "GameOver", "UI/GameOverScr", "Interface/GameOverScr" };
            
            foreach (var name in possibleNames)
            {
                var node = GetTree().CurrentScene.FindChild(name, true, false);
                if (node is GameOverUI gameOverUI)
                {
                    _gameOverUI = gameOverUI;
                    GD.Print($"UIManager: Found GameOverUI by name search: {name}");
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
                if (gameOverNode is GameOverUI gameOverUI)
                {
                    _gameOverUI = gameOverUI;
                    GD.Print($"UIManager: Found GameOverUI at Interface/GameOverScr");
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
                if (lifeBarNode is LifeBarUI lifeBarUI)
                {
                    _lifeBarUI = lifeBarUI;
                    GD.Print($"UIManager: Found LifeBarUI at Interface/LifeBarUI");
                }
            }
        }
        
        // Last resort: search all nodes recursively
        if (_gameOverUI == null)
        {
            _gameOverUI = FindNodeByType<GameOverUI>(GetTree().CurrentScene);
            if (_gameOverUI != null)
            {
                GD.Print($"UIManager: Found GameOverUI by recursive search: {_gameOverUI.Name}");
            }
        }
        
        // Final status
        GD.Print($"UIManager: Final result - GameOverUI: {_gameOverUI?.Name ?? "null"}, LifeBarUI: {_lifeBarUI?.Name ?? "null"}");
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
            if (_gameOverUI.IsConnected(GameOverUI.SignalName.RestartRequested, new Callable(this, nameof(OnRestartRequested))))
            {
                _gameOverUI.RestartRequested -= OnRestartRequested;
            }
            _gameOverUI.RestartRequested += OnRestartRequested;
        }
    }
    
    private void OnPlayerDied(Vector2 deathPosition, int remainingHealth)
    {
        GD.Print($"UIManager: Received player death event at {deathPosition}");
        
        // Wait 1 second before showing game over UI
        GetTree().CreateTimer(1.0f).Timeout += () => ShowGameOverUIWithFade();
    }
    
    private void ShowGameOverUIWithFade()
    {
        if (_gameOverUI != null)
        {
            GD.Print("UIManager: Showing GameOverUI with fade transition");
            
            // Start invisible and fade in
            _gameOverUI.Modulate = new Color(1, 1, 1, 0);
            _gameOverUI.Show();
            
            // Create fade-in tween
            var tween = CreateTween();
            tween.TweenProperty(_gameOverUI, "modulate:a", 1.0f, 0.5f);
            tween.TweenCallback(Callable.From(() => GD.Print("UIManager: Fade-in complete")));
        }
        else
        {
            GD.PrintErr("UIManager: Cannot show game over - GameOverUI not found!");
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
                GD.Print("UIManager: GameOverUI hidden with fade-out");
            }));
        }
    }
    
    private void OnRestartRequested()
    {
        GD.Print("UIManager: Restart requested via GameOverUI");
        HideGameOverUI();
        
        // Notify game restart
        GameEvents.OnGameRestart();
    }
    
    public GameOverUI GetGameOverUI() => _gameOverUI;
    public LifeBarUI GetLifeBarUI() => _lifeBarUI;
    
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
