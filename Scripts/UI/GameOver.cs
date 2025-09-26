using Godot;
using MarrieGame.Core.Utils;

// Game over screen UI controller with smooth transitions and flexible messaging
public partial class GameOver : Control
{
    #region Configuration
    [Export] public string GameOverMessage { get; set; } = "Game Over!";
    [Export] public string SubMessage { get; set; } = "Temporal interferences consumed you...";
    #endregion

    #region Signals
    [Signal] public delegate void RestartRequestedEventHandler();
    [Signal] public delegate void QuitRequestedEventHandler();
    #endregion

    #region UI Components
    [Export] public NodePath GameOverLabelPath;
    [Export] public NodePath SubMessageLabelPath;
    [Export] public NodePath RestartButtonPath;
    [Export] public NodePath QuitButtonPath;

    private Label _gameOverLabel;
    private Label _subMessageLabel;
    private Button _restartButton;
    private Button _quitButton;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup UI components and hide initially
        Hide();
        DiscoverUIElements();
        ConnectButtons();
        UpdateDisplayedText();
    }

    private void DiscoverUIElements()
    {
        // Prefer editor-assigned NodePaths when available
            if (GameOverLabelPath != null && GameOverLabelPath.ToString() != "")
                _gameOverLabel = MarrieGame.Core.Utils.NodeHelpers.GetNodeOrNull<Label>(this, GameOverLabelPath, "CenterContainer/GameOverLabel");
            _gameOverLabel ??= MarrieGame.Core.Utils.NodeHelpers.GetNodeOrNull<Label>(this, null, "CenterContainer/GameOverLabel") ?? FindUIElement<Label>("GameOverLabel");

        if (SubMessageLabelPath != null && SubMessageLabelPath.ToString() != "")
            _subMessageLabel = GetNodeOrNull<Label>(SubMessageLabelPath);
        _subMessageLabel ??= GetNodeOrNull<Label>("CenterContainer/SubMessageLabel") ?? FindUIElement<Label>("SubMessageLabel");

        if (RestartButtonPath != null && RestartButtonPath.ToString() != "")
            _restartButton = GetNodeOrNull<Button>(RestartButtonPath);
        _restartButton ??= MarrieGame.Core.Utils.NodeHelpers.GetNodeOrNull<Button>(this, null, "CenterContainer/ButtonContainer/RestartButton") ?? FindUIElement<Button>("RestartButton");

        if (QuitButtonPath != null && QuitButtonPath.ToString() != "")
            _quitButton = GetNodeOrNull<Button>(QuitButtonPath);
        _quitButton ??= MarrieGame.Core.Utils.NodeHelpers.GetNodeOrNull<Button>(this, null, "CenterContainer/ButtonContainer/QuitButton") ?? FindUIElement<Button>("QuitButton");

        LogDiscoveredElements();
    }

    private T FindUIElement<T>(string name) where T : class
    {
        // Breadth-first search for UI elements when direct paths fail
        var queue = new System.Collections.Generic.Queue<Node>();
        queue.Enqueue(this);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Name == name && current is T element)
                return element;

            foreach (Node child in current.GetChildren())
                queue.Enqueue(child);
        }

        return null;
    }

    private void ConnectButtons()
    {
        // Connect button press events
        if (_restartButton != null)
            _restartButton.Pressed += OnRestartPressed;

        if (_quitButton != null)
            _quitButton.Pressed += OnQuitPressed;
    }

    private void UpdateDisplayedText()
    {
        // Update text content in UI labels
        if (_gameOverLabel != null)
            _gameOverLabel.Text = GameOverMessage;

        if (_subMessageLabel != null)
            _subMessageLabel.Text = SubMessage;
    }
    #endregion

    #region Display Control
    public void ShowGameOver(string customMessage = null, string customSubMessage = null)
    {
        // Display game over screen with optional custom messages
        UpdateMessages(customMessage, customSubMessage);
        UpdateDisplayedText();
        ShowWithFadeIn();
        FocusRestartButton();
    }

    private void UpdateMessages(string customMessage, string customSubMessage)
    {
        // Update messages if custom ones provided
        if (customMessage != null) GameOverMessage = customMessage;
        if (customSubMessage != null) SubMessage = customSubMessage;
    }

    private void ShowWithFadeIn()
    {
        // Ensure this UI fits the screen before showing
        EnsureFitsScreen();
        
        // Show with smooth fade-in transition
        Show();
        Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 1.0f, 0.5f);
        
        Log.Debug("GameOver UI shown with fade-in transition", "GameOverUI");
    }

    private void FocusRestartButton()
    {
        // Set focus to restart button for keyboard navigation
        _restartButton?.GrabFocus();
    }

    public void HideGameOver()
    {
        // Hide the game over screen
        Hide();
    }

    public void SetMessages(string mainMessage, string subMessage)
    {
        // Update both message texts and refresh display
        GameOverMessage = mainMessage;
        SubMessage = subMessage;
        UpdateDisplayedText();
    }
    #endregion



    #region Event Handlers
    private void OnRestartPressed()
    {
        // Handle restart button press with safety checks
        if (IsInsideTree())
        {
            EmitSignal(SignalName.RestartRequested);
        }
        else
        {
            Log.Error("GameOverUI not in tree - cannot emit RestartRequested signal", "GameOverUI");
        }
    }

    private void OnQuitPressed()
    {
        // Handle quit button press
        EmitSignal(SignalName.QuitRequested);
    }

    // Signal handler methods that match the .tscn connections
    private void OnRestartRequested()
    {
        OnRestartPressed();
    }

    private void OnQuitRequested()
    {
        OnQuitPressed();
    }
    #endregion

    private void EnsureFitsScreen()
    {
        // Since this GameOver UI is in the Overlays container managed by CanvasCameraFollower,
        // the Overlays container stays fixed to screen coordinates (Vector2.Zero).
        // We just need to ensure this Control fills the viewport and stays on top.
        var viewport = GetViewport();
        if (viewport != null)
        {
            var viewportSize = viewport.GetVisibleRect().Size;
            
            // Set anchors to fill the entire screen viewport
            SetAnchorsPreset(Control.LayoutPreset.FullRect);
            
            // Since we're in the Overlays container that's fixed to screen origin,
            // we should keep our position at zero relative to that container
            Position = Vector2.Zero;
            Size = viewportSize;
            
            // Make sure it's rendered on top of other overlay elements
            ZIndex = 100;
            
            Log.Debug($"GameOver UI configured for viewport: {viewportSize}, positioned at origin within Overlays container", "GameOverUI");
        }
    }

    #region Debug Utilities
    private void LogDiscoveredElements()
    {
        // Log discovered UI elements for debugging
        #if DEBUG
        Log.Debug("GameOverUI elements discovered:", "GameOverUI");
        Log.Debug($"- GameOverLabel: {(_gameOverLabel != null ? "✓" : "✗")}", "GameOverUI");
        Log.Debug($"- SubMessageLabel: {(_subMessageLabel != null ? "✓" : "✗")}", "GameOverUI");
        Log.Debug($"- RestartButton: {(_restartButton != null ? "✓" : "✗")}", "GameOverUI");
        Log.Debug($"- QuitButton: {(_quitButton != null ? "✓" : "✗")}", "GameOverUI");
        #endif
    }

    /// <summary>
    /// Debug method to manually test GameOver UI positioning
    /// Call this from the debug console or via code to test UI appearance
    /// </summary>
    public void TestShow()
    {
        Log.Debug("TestShow() called - forcing GameOver UI to appear", "GameOverUI");
        ShowGameOver("DEBUG: Test Game Over", "This is a test of the GameOver UI positioning");
    }
    #endregion
}
