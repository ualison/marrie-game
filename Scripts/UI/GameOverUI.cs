using Godot;

// Game over screen UI controller with smooth transitions and flexible messaging
public partial class GameOverUI : Control
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
        // Find UI components with optimized search and fallback
        _gameOverLabel = GetNodeOrNull<Label>("VBoxContainer/GameOverLabel")
            ?? FindUIElement<Label>("GameOverLabel");

        _subMessageLabel = GetNodeOrNull<Label>("VBoxContainer/SubMessageLabel")
            ?? FindUIElement<Label>("SubMessageLabel");

        _restartButton = GetNodeOrNull<Button>("VBoxContainer/ButtonContainer/RestartButton")
            ?? FindUIElement<Button>("RestartButton");

        _quitButton = GetNodeOrNull<Button>("VBoxContainer/ButtonContainer/QuitButton")
            ?? FindUIElement<Button>("QuitButton");

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
        // Show with smooth fade-in transition
        Show();
        Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 1.0f, 0.5f);
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
            GD.PrintErr("GameOverUI not in tree - cannot emit RestartRequested signal");
        }
    }

    private void OnQuitPressed()
    {
        // Handle quit button press
        EmitSignal(SignalName.QuitRequested);
    }
    #endregion

    #region Debug Utilities
    private void LogDiscoveredElements()
    {
        // Log discovered UI elements for debugging
        GD.Print("GameOverUI elements discovered:");
        GD.Print($"- GameOverLabel: {(_gameOverLabel != null ? "✓" : "✗")}");
        GD.Print($"- SubMessageLabel: {(_subMessageLabel != null ? "✓" : "✗")}");
        GD.Print($"- RestartButton: {(_restartButton != null ? "✓" : "✗")}");
        GD.Print($"- QuitButton: {(_quitButton != null ? "✓" : "✗")}");
    }
    #endregion
}
