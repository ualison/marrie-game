using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MarrieGame.Scripts.Dialog;

/// <summary>
/// Dialog UI Manager that handles the visual presentation of dialogs
/// Based on the brown-themed UI shown in the image
/// </summary>
public partial class DialogUI : Control
{
    #region Signals
    [Signal] public delegate void DialogClosedEventHandler();
    [Signal] public delegate void ChoiceSelectedEventHandler(string choice);
    #endregion

    #region UI Components
    private Panel _backgroundPanel;
    private Label _phraseLabel;
    private VBoxContainer _choicesContainer;
    private Button _yesButton;
    private Button _noButton;
    
    // Question UI elements
    private Panel _questionPanel;
    private Label _questionLabel;
    private HBoxContainer _answersContainer;
    private Button _answer1Button;
    private Button _answer2Button;
    private Button _answer3Button;
    #endregion

    #region State
    private DialogQuestion _currentQuestion;
    private List<DialogChoice> _shuffledChoices;
    private bool _isShowingQuestion = false;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Use CallDeferred to avoid node setup conflicts during complex UI creation
        CallDeferred(nameof(InitializeUI));
        Hide(); // Start hidden
    }
    
    private void InitializeUI()
    {
        InitializeComponents();
        SetupStyling();
        ConnectSignals();
    }

    private void InitializeComponents()
    {
        // Set this control to fill the screen
        AnchorLeft = 0.0f;
        AnchorTop = 0.0f;
        AnchorRight = 1.0f;
        AnchorBottom = 1.0f;
        OffsetLeft = 0;
        OffsetTop = 0;
        OffsetRight = 0;
        OffsetBottom = 0;
        
        // Create main background panel (centered)
        _backgroundPanel = new Panel();
        _backgroundPanel.Size = new Vector2(600, 200);
        // Center the panel
        _backgroundPanel.AnchorLeft = 0.5f;
        _backgroundPanel.AnchorTop = 0.3f;
        _backgroundPanel.AnchorRight = 0.5f;
        _backgroundPanel.AnchorBottom = 0.3f;
        _backgroundPanel.OffsetLeft = -300; // Half of width
        _backgroundPanel.OffsetTop = -100;  // Half of height
        _backgroundPanel.OffsetRight = 300;
        _backgroundPanel.OffsetBottom = 100;
        AddChild(_backgroundPanel);

        // Create phrase label
        _phraseLabel = new Label();
        _phraseLabel.Text = "SAY A PHRASE\nASK IF WANT CONTINUE";
        _phraseLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _phraseLabel.VerticalAlignment = VerticalAlignment.Center;
        _phraseLabel.AnchorLeft = 0.0f;
        _phraseLabel.AnchorTop = 0.0f;
        _phraseLabel.AnchorRight = 1.0f;
        _phraseLabel.AnchorBottom = 0.7f;
        _phraseLabel.OffsetLeft = 10;
        _phraseLabel.OffsetTop = 10;
        _phraseLabel.OffsetRight = -10;
        _phraseLabel.OffsetBottom = 0;
        _backgroundPanel.AddChild(_phraseLabel);

        // Create choices container
        _choicesContainer = new VBoxContainer();
        _choicesContainer.AnchorLeft = 0.0f;
        _choicesContainer.AnchorTop = 0.7f;
        _choicesContainer.AnchorRight = 1.0f;
        _choicesContainer.AnchorBottom = 1.0f;
        _choicesContainer.OffsetLeft = 10;
        _choicesContainer.OffsetTop = 0;
        _choicesContainer.OffsetRight = -10;
        _choicesContainer.OffsetBottom = -10;
        _backgroundPanel.AddChild(_choicesContainer);

        // Create YES/NO buttons
        var buttonContainer = new HBoxContainer();
        buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
        buttonContainer.AddThemeConstantOverride("separation", 50);
        _choicesContainer.AddChild(buttonContainer);

        _yesButton = new Button();
        _yesButton.Text = "YES";
        _yesButton.CustomMinimumSize = new Vector2(100, 40);
        buttonContainer.AddChild(_yesButton);

        _noButton = new Button();
        _noButton.Text = "NO";
        _noButton.CustomMinimumSize = new Vector2(100, 40);
        buttonContainer.AddChild(_noButton);

        // Create question panel (initially hidden)
        CreateQuestionPanel();
    }

    private void CreateQuestionPanel()
    {
        _questionPanel = new Panel();
        _questionPanel.Size = new Vector2(700, 300);
        // Center the question panel
        _questionPanel.AnchorLeft = 0.5f;
        _questionPanel.AnchorTop = 0.4f;
        _questionPanel.AnchorRight = 0.5f;
        _questionPanel.AnchorBottom = 0.4f;
        _questionPanel.OffsetLeft = -350; // Half of width
        _questionPanel.OffsetTop = -150;  // Half of height
        _questionPanel.OffsetRight = 350;
        _questionPanel.OffsetBottom = 150;
        AddChild(_questionPanel);

        // Question label
        _questionLabel = new Label();
        _questionLabel.Text = "QUESTION/ENIGMA";
        _questionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _questionLabel.VerticalAlignment = VerticalAlignment.Center;
        _questionLabel.AnchorLeft = 0.0f;
        _questionLabel.AnchorTop = 0.0f;
        _questionLabel.AnchorRight = 1.0f;
        _questionLabel.AnchorBottom = 0.4f;
        _questionLabel.OffsetLeft = 10;
        _questionLabel.OffsetTop = 10;
        _questionLabel.OffsetRight = -10;
        _questionLabel.OffsetBottom = 0;
        _questionPanel.AddChild(_questionLabel);

        // Answers container
        _answersContainer = new HBoxContainer();
        _answersContainer.AnchorLeft = 0.0f;
        _answersContainer.AnchorTop = 0.4f;
        _answersContainer.AnchorRight = 1.0f;
        _answersContainer.AnchorBottom = 1.0f;
        _answersContainer.OffsetLeft = 10;
        _answersContainer.OffsetTop = 10;
        _answersContainer.OffsetRight = -10;
        _answersContainer.OffsetBottom = -10;
        _answersContainer.Alignment = BoxContainer.AlignmentMode.Center;
        _answersContainer.AddThemeConstantOverride("separation", 20);
        _questionPanel.AddChild(_answersContainer);

        // Answer buttons
        _answer1Button = CreateAnswerButton("ANSWER 1");
        _answer2Button = CreateAnswerButton("ANSWER 2");
        _answer3Button = CreateAnswerButton("ANSWER 3");

        _answersContainer.AddChild(_answer1Button);
        _answersContainer.AddChild(_answer2Button);
        _answersContainer.AddChild(_answer3Button);

        _questionPanel.Hide();
    }

    private Button CreateAnswerButton(string text)
    {
        var button = new Button();
        button.Text = text;
        button.CustomMinimumSize = new Vector2(180, 80);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return button;
    }

    private void SetupStyling()
    {
        // Apply brown theme styling similar to the image
        var brownColor = new Color(0.6f, 0.4f, 0.2f, 0.9f); // Brown background
        var textColor = new Color(1.0f, 0.9f, 0.8f, 1.0f); // Light cream text

        // Style main panel
        StylePanel(_backgroundPanel, brownColor);
        StylePanel(_questionPanel, brownColor);

        // Style labels
        StyleLabel(_phraseLabel, textColor, 24);
        StyleLabel(_questionLabel, textColor, 20);

        // Style buttons
        StyleButton(_yesButton, brownColor, textColor);
        StyleButton(_noButton, brownColor, textColor);
        StyleButton(_answer1Button, brownColor, textColor);
        StyleButton(_answer2Button, brownColor, textColor);
        StyleButton(_answer3Button, brownColor, textColor);
    }

    private void StylePanel(Panel panel, Color backgroundColor)
    {
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = backgroundColor;
        styleBox.BorderWidthTop = 3;
        styleBox.BorderWidthBottom = 3;
        styleBox.BorderWidthLeft = 3;
        styleBox.BorderWidthRight = 3;
        styleBox.BorderColor = new Color(0.4f, 0.2f, 0.1f, 1.0f);
        panel.AddThemeStyleboxOverride("panel", styleBox);
    }

    private void StyleLabel(Label label, Color textColor, int fontSize)
    {
        label.AddThemeColorOverride("font_color", textColor);
        label.AddThemeFontSizeOverride("font_size", fontSize);
    }

    private void StyleButton(Button button, Color backgroundColor, Color textColor)
    {
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = backgroundColor;
        styleBox.BorderWidthTop = 2;
        styleBox.BorderWidthBottom = 2;
        styleBox.BorderWidthLeft = 2;
        styleBox.BorderWidthRight = 2;
        styleBox.BorderColor = new Color(0.4f, 0.2f, 0.1f, 1.0f);
        
        button.AddThemeStyleboxOverride("normal", styleBox);
        button.AddThemeColorOverride("font_color", textColor);
        button.AddThemeFontSizeOverride("font_size", 16);
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
        _phraseLabel.Text = text;
        _backgroundPanel.Show();
        _questionPanel.Hide();
        _isShowingQuestion = false;
        Show();
    }

    public void ShowQuestion(DialogQuestion question)
    {
        _currentQuestion = question;
        _backgroundPanel.Hide();
        
        // Shuffle the choices and remember the correct answer position
        _shuffledChoices = ShuffleChoices(question.Choices);
        
        _questionLabel.Text = question.Text;
        _answer1Button.Text = _shuffledChoices[0].Text;
        _answer2Button.Text = _shuffledChoices[1].Text;
        _answer3Button.Text = _shuffledChoices[2].Text;
        
        _questionPanel.Show();
        _isShowingQuestion = true;
        Show();
    }

    public void HideDialog()
    {
        Hide();
        _backgroundPanel.Hide();
        _questionPanel.Hide();
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
        if (_shuffledChoices != null && answerIndex < _shuffledChoices.Count)
        {
            var selectedChoice = _shuffledChoices[answerIndex];
            EmitSignal(SignalName.ChoiceSelected, selectedChoice.IsCorrect ? "correct" : "wrong");
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
    #endregion
}