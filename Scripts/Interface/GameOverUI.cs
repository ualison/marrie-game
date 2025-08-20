using Godot;

// Game over screen UI controller with smooth transitions and flexible messaging
// Simplified for better performance and cleaner component discovery
public partial class GameOverUI : Control
{
	[Export] public string GameOverMessage { get; set; } = "Game Over!";
	[Export] public string SubMessage { get; set; } = "Temporal interferences consumed you...";

	[Signal] public delegate void RestartRequestedEventHandler();
	[Signal] public delegate void QuitRequestedEventHandler();

	// Cached UI references for performance
	private Label _gameOverLabel;
	private Label _subMessageLabel;
	private Button _restartButton;
	private Button _quitButton;

	public override void _Ready()
	{
		Hide();
		DiscoverUIElements();
		ConnectButtons();
		UpdateDisplayedText();
	}

	// Shows game over screen with optional custom messages and smooth fade-in
	public void ShowGameOver(string customMessage = null, string customSubMessage = null)
	{
		if (customMessage != null) GameOverMessage = customMessage;
		if (customSubMessage != null) SubMessage = customSubMessage;

		UpdateDisplayedText();
		Show();

		// Smooth fade-in transition
		Modulate = new Color(1, 1, 1, 0);
		var tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 1.0f, 0.5f);

		_restartButton?.GrabFocus();
	}

	// Hides the game over screen
	public void HideGameOver() => Hide();

	// Updates both message texts
	public void SetMessages(string mainMessage, string subMessage)
	{
		GameOverMessage = mainMessage;
		SubMessage = subMessage;
		UpdateDisplayedText();
	}

	private void DiscoverUIElements()
	{
		// Optimized UI discovery with fallback search
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

	// Efficient UI element search using breadth-first traversal
	private T FindUIElement<T>(string name) where T : class
	{
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
		if (_restartButton != null)
			_restartButton.Pressed += OnRestartPressed;

		if (_quitButton != null)
			_quitButton.Pressed += OnQuitPressed;
	}

	private void UpdateDisplayedText()
	{
		if (_gameOverLabel != null)
			_gameOverLabel.Text = GameOverMessage;

		if (_subMessageLabel != null)
			_subMessageLabel.Text = SubMessage;
	}

	private void OnRestartPressed()
	{
		// Add safety check before emitting signal
		if (IsInsideTree())
		{
			EmitSignal(SignalName.RestartRequested);
		}
		else
		{
			GD.PrintErr("GameOverUI not in tree - cannot emit RestartRequested signal");
		}
	}
	private void OnQuitPressed() => EmitSignal(SignalName.QuitRequested);



	private void LogDiscoveredElements()
	{
		GD.Print("GameOverUI elements discovered:");
		GD.Print($"- GameOverLabel: {(_gameOverLabel != null ? "✓" : "✗")}");
		GD.Print($"- SubMessageLabel: {(_subMessageLabel != null ? "✓" : "✗")}");
		GD.Print($"- RestartButton: {(_restartButton != null ? "✓" : "✗")}");
		GD.Print($"- QuitButton: {(_quitButton != null ? "✓" : "✗")}");
	}
}