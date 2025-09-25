using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MarrieGame.Scripts.Dialog;

/// <summary>
/// Dialog UI Manager that handles the visual presentation of dialogs
/// Based on the brown-themed UI shown in the image
/// Now works with pre-built scene nodes instead of runtime creation
/// </summary>
public partial class DialogUI : Control
{
    #region Signals
    [Signal] public delegate void DialogClosedEventHandler();
    [Signal] public delegate void ChoiceSelectedEventHandler(string choice);
    #endregion

    #region UI Components - Scene References
    [Export] private Panel _backgroundPanel;
    [Export] private Label _phraseLabel;
    [Export] private HBoxContainer _choicesContainer;
    [Export] private Button _yesButton;
    [Export] private Button _noButton;
    
    // Question UI elements
    [Export] private Panel _questionPanel;
    [Export] private Label _questionLabel;
    [Export] private Label _timerLabel;
    [Export] private HBoxContainer _answersContainer;
    [Export] private Button _answer1Button;
    [Export] private Button _answer2Button;
    [Export] private Button _answer3Button;
    
    // Message UI elements
    [Export] private Panel _messagePanel;
    [Export] private Label _messageLabel;
    #endregion

    #region State
    private DialogQuestion _currentQuestion;
    private List<DialogChoice> _shuffledChoices;
    private bool _isShowingQuestion = false;
    
    // Timer and fade system
    [Export] private float _questionTimeLimit = 30.0f; // Default 30 seconds
    [Export] private float _messageDisplayTime = 3.0f; // Default message display time
    private Timer _questionTimer;
    private float _timeRemaining;
    private Tween _fadeTween;
    
    // Public property to access message display time
    public float MessageDisplayTime => _messageDisplayTime;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Get references to pre-built scene nodes
        GetNodeReferences();
        SetupStyling();
        ConnectSignals();
        Hide(); // Start hidden
    }
    
    private void GetNodeReferences()
    {
        // Get references to all pre-built nodes from the scene
        _backgroundPanel = GetNode<Panel>("BackgroundPanel");
        _phraseLabel = GetNode<Label>("BackgroundPanel/PhraseLabel");
        _choicesContainer = GetNode<HBoxContainer>("BackgroundPanel/ButtonContainer"); // Updated path
        _yesButton = GetNode<Button>("BackgroundPanel/ButtonContainer/YesButton");
        _noButton = GetNode<Button>("BackgroundPanel/ButtonContainer/NoButton");
        
        // Question panel nodes
        _questionPanel = GetNode<Panel>("QuestionPanel");
        _questionLabel = GetNode<Label>("QuestionPanel/QuestionLabel");
        _timerLabel = GetNode<Label>("QuestionPanel/TimerLabel");
        _answersContainer = GetNode<HBoxContainer>("QuestionPanel/AnswersContainer");
        _answer1Button = GetNode<Button>("QuestionPanel/AnswersContainer/Answer1Button");
        _answer2Button = GetNode<Button>("QuestionPanel/AnswersContainer/Answer2Button");
        _answer3Button = GetNode<Button>("QuestionPanel/AnswersContainer/Answer3Button");
        
        // Message panel nodes
        _messagePanel = GetNode<Panel>("MessagePanel");
        _messageLabel = GetNode<Label>("MessagePanel/MessageLabel");
    }

    private void SetupStyling()
    {
        // Button styling is now handled in the scene file using texture resources
        // DialogButtonUnactive.png for normal state, DialogButtonActive.png for hover/pressed states
        
        // Setup timer
        _questionTimer = new Timer();
        _questionTimer.WaitTime = 0.1f; // Update every 100ms for smooth countdown
        _questionTimer.Timeout += UpdateTimer;
        AddChild(_questionTimer);
        
        // Initialize panels as invisible for fade effects
        _backgroundPanel.Modulate = new Color(1, 1, 1, 0);
        _questionPanel.Modulate = new Color(1, 1, 1, 0);
        _messagePanel.Modulate = new Color(1, 1, 1, 0);
    }

    private void ConnectSignals()
    {
        _yesButton.Pressed += OnYesPressed;
        _noButton.Pressed += OnNoPressed;
        _answer1Button.Pressed += () => OnAnswerPressed(0);
        _answer2Button.Pressed += () => OnAnswerPressed(1);
        _answer3Button.Pressed += () => OnAnswerPressed(2);
    }
    #endregion

    #region Public API
    public void ShowInitialDialog(string text)
    {
        GD.Print($"ShowInitialDialog called with text: {text}");
        _phraseLabel.Text = text;
        _isShowingQuestion = false;
        Show();
        
        // Stop any running timer first
        StopTimer();
        
        // If question panel is currently visible, fade it out first, then show background
        if (_questionPanel.Visible)
        {
            GD.Print("Question panel is visible, fading it out first");
            FadeOutPanel(_questionPanel, () => {
                _questionPanel.Hide();
                _backgroundPanel.Show();
                GD.Print("Showing background panel with exit message");
                FadeInPanel(_backgroundPanel);
            });
        }
        else
        {
            // Question panel not visible, just show background panel
            GD.Print("Question panel not visible, directly showing background panel");
            _questionPanel.Hide();
            _backgroundPanel.Show();
            FadeInPanel(_backgroundPanel);
        }
    }

    public void ShowQuestion(DialogQuestion question)
    {
        _currentQuestion = question;
        
        // Shuffle the choices and remember the correct answer position
        _shuffledChoices = ShuffleChoices(question.Choices);
        
        _questionLabel.Text = question.Text;
        _answer1Button.Text = _shuffledChoices[0].Text;
        _answer2Button.Text = _shuffledChoices[1].Text;
        _answer3Button.Text = _shuffledChoices[2].Text;
        
        // Start timer
        _timeRemaining = _questionTimeLimit;
        UpdateTimerDisplay();
        _questionTimer.Start();
        
        _isShowingQuestion = true;
        
        // Fade out background panel, then fade in question panel
        FadeOutPanel(_backgroundPanel, () => {
            _backgroundPanel.Hide();
            _questionPanel.Show();
            FadeInPanel(_questionPanel);
        });
    }

    public void ShowQuestion(DialogQuestion question, float timeLimit)
    {
        _questionTimeLimit = timeLimit;
        ShowQuestion(question);
    }

    public void ShowMessage(string text, float displayTime = -1.0f)
    {
        // Use the exported debug parameter if displayTime is not specified
        if (displayTime < 0)
            displayTime = _messageDisplayTime;
            
        GD.Print($"ShowMessage called with text: {text}, displayTime: {displayTime}");
        _messageLabel.Text = text;
        _isShowingQuestion = false;
        Show();
        
        // Stop any running timer first
        StopTimer();
        
        // Hide other panels and show message panel
        if (_questionPanel.Visible)
        {
            GD.Print("Question panel is visible, fading it out first");
            FadeOutPanel(_questionPanel, () => {
                _questionPanel.Hide();
                _backgroundPanel.Hide();
                _messagePanel.Show();
                GD.Print("Showing message panel");
                FadeInPanel(_messagePanel);
            });
        }
        else if (_backgroundPanel.Visible)
        {
            GD.Print("Background panel is visible, fading it out first");
            FadeOutPanel(_backgroundPanel, () => {
                _backgroundPanel.Hide();
                _questionPanel.Hide();
                _messagePanel.Show();
                GD.Print("Showing message panel");
                FadeInPanel(_messagePanel);
            });
        }
        else
        {
            // No other panels visible, just show message panel
            GD.Print("No other panels visible, directly showing message panel");
            _backgroundPanel.Hide();
            _questionPanel.Hide();
            _messagePanel.Show();
            FadeInPanel(_messagePanel);
        }
        
        // Auto-hide after specified time
        if (displayTime > 0)
        {
            var timer = GetTree().CreateTimer(displayTime);
            timer.Timeout += () => {
                GD.Print($"Message display time ({displayTime}s) expired - hiding message");
                HideDialog();
                // Emit dialog closed signal to properly clean up state
                EmitSignal(SignalName.DialogClosed);
            };
        }
    }

    public void ShowMessageWithoutAutoHide(string text)
    {
        GD.Print($"ShowMessageWithoutAutoHide called with text: {text}");
        _messageLabel.Text = text;
        _isShowingQuestion = false;
        Show();
        
        // Stop any running timer first
        StopTimer();
        
        // Hide other panels and show message panel
        if (_questionPanel.Visible)
        {
            GD.Print("Question panel is visible, fading it out first");
            FadeOutPanel(_questionPanel, () => {
                _questionPanel.Hide();
                _backgroundPanel.Hide();
                _messagePanel.Show();
                GD.Print("Showing message panel");
                FadeInPanel(_messagePanel);
            });
        }
        else if (_backgroundPanel.Visible)
        {
            GD.Print("Background panel is visible, fading it out first");
            FadeOutPanel(_backgroundPanel, () => {
                _backgroundPanel.Hide();
                _questionPanel.Hide();
                _messagePanel.Show();
                GD.Print("Showing message panel");
                FadeInPanel(_messagePanel);
            });
        }
        else
        {
            // No other panels visible, just show message panel
            GD.Print("No other panels visible, directly showing message panel");
            _backgroundPanel.Hide();
            _questionPanel.Hide();
            _messagePanel.Show();
            FadeInPanel(_messagePanel);
        }
    }

    public void HideDialog()
    {
        StopTimer();
        
        // Fade out current visible panel
        if (_messagePanel.Visible)
        {
            FadeOutPanel(_messagePanel, () => {
                CompleteHideDialog();
            });
        }
        else if (_isShowingQuestion && _questionPanel.Visible)
        {
            FadeOutPanel(_questionPanel, () => {
                CompleteHideDialog();
            });
        }
        else if (_backgroundPanel.Visible)
        {
            FadeOutPanel(_backgroundPanel, () => {
                CompleteHideDialog();
            });
        }
        else
        {
            CompleteHideDialog();
        }
    }
    
    private void CompleteHideDialog()
    {
        Hide();
        _backgroundPanel.Hide();
        _questionPanel.Hide();
        _messagePanel.Hide();
        _isShowingQuestion = false;
    }
    #endregion

    #region Event Handlers
    private void OnYesPressed()
    {
        EmitSignal(SignalName.ChoiceSelected, "YES");
    }

    private void OnNoPressed()
    {
        EmitSignal(SignalName.ChoiceSelected, "NO");
        EmitSignal(SignalName.DialogClosed);
    }

    private void OnAnswerPressed(int answerIndex)
    {
        // Stop the timer when an answer is selected
        StopTimer();
        
        if (_shuffledChoices != null && answerIndex < _shuffledChoices.Count)
        {
            var selectedChoice = _shuffledChoices[answerIndex];
            // Send the actual choice text instead of "correct"/"wrong"
            EmitSignal(SignalName.ChoiceSelected, selectedChoice.Text);
        }
    }
    #endregion

    #region Helper Methods
    private List<DialogChoice> ShuffleChoices(List<DialogChoice> choices)
    {
        var shuffled = new List<DialogChoice>(choices);
        var random = new System.Random();
        
        // Fisher-Yates shuffle
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        
        return shuffled;
    }
    
    #region Fade Transitions
    private void FadeInPanel(Panel panel)
    {
        if (_fadeTween != null)
        {
            _fadeTween.Kill();
        }
        
        _fadeTween = CreateTween();
        panel.Modulate = new Color(1, 1, 1, 0);
        _fadeTween.TweenProperty(panel, "modulate:a", 1.0f, 0.3f);
    }
    
    private void FadeOutPanel(Panel panel, System.Action onComplete = null)
    {
        if (_fadeTween != null)
        {
            _fadeTween.Kill();
        }
        
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(panel, "modulate:a", 0.0f, 0.3f);
        
        if (onComplete != null)
        {
            _fadeTween.TweenCallback(Callable.From(onComplete));
        }
    }
    #endregion
    
    #region Timer System
    private void UpdateTimer()
    {
        _timeRemaining -= 0.1f;
        
        if (_timeRemaining <= 0)
        {
            _timeRemaining = 0;
            StopTimer();
            OnTimerExpired();
        }
        
        UpdateTimerDisplay();
    }
    
    private void UpdateTimerDisplay()
    {
        int seconds = Mathf.CeilToInt(_timeRemaining);
        _timerLabel.Text = $"Time: {seconds}s";
        
        // Change color as time runs out
        if (_timeRemaining <= 10)
        {
            _timerLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f, 1)); // Red
        }
        else if (_timeRemaining <= 20)
        {
            _timerLabel.AddThemeColorOverride("font_color", new Color(1, 0.7f, 0.3f, 1)); // Orange
        }
        else
        {
            _timerLabel.AddThemeColorOverride("font_color", new Color(1, 0.7f, 0.3f, 1)); // Default orange
        }
    }
    
    private void StopTimer()
    {
        if (_questionTimer != null)
        {
            _questionTimer.Stop();
        }
    }
    
    private void OnTimerExpired()
    {
        // Auto-select first answer when time runs out
        OnAnswerPressed(0);
        GD.Print("Question timer expired - auto-selecting first answer");
    }
    #endregion
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        StopTimer();
        if (_fadeTween != null)
        {
            _fadeTween.Kill();
        }
    }
    #endregion
}