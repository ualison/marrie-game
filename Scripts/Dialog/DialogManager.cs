using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System;
using MarrieGame.Scripts.Dialog;

/// <summary>
/// Dialog Manager that handles dialog interactions between NPCs and UI
/// </summary>
public partial class DialogManager : Node
{
    #region Singleton
    public static DialogManager Instance { get; private set; }
    #endregion

    #region Components
    private DialogUI _dialogUI;
    private DialogData _dialogData;
    #endregion

    #region State
    private DialogEntry _currentDialog;
    private Node2D _currentNPC;
    private System.Random _random = new System.Random();
    private bool _playerAgreedToQuestions = false; // Track if player said YES but hasn't answered yet
    private DialogEntry _initialDialog; // Store the initial dialog to access textWhenGoOut
    private bool _isHandlingExit = false; // Prevent multiple exit handling
    #endregion

    #region Initialization
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            // Use CallDeferred to avoid node setup conflicts
            CallDeferred(nameof(SetupDialogUI));
            LoadDialogData();
        }
        else
        {
            QueueFree();
        }
    }

    private void SetupDialogUI()
    {
        // Create a CanvasLayer for the dialog UI to ensure it renders on top
        var canvasLayer = new CanvasLayer();
        canvasLayer.Name = "DialogCanvasLayer";
        canvasLayer.Layer = 100; // High layer to render on top
        GetTree().Root.CallDeferred("add_child", canvasLayer);
        
        // Load and instantiate the DialogUI prefab scene
        var dialogUIScene = GD.Load<PackedScene>("res://Prefabs/DialogUI.tscn");
        _dialogUI = dialogUIScene.Instantiate<DialogUI>();
        _dialogUI.Name = "DialogUI";
        canvasLayer.CallDeferred("add_child", _dialogUI);
        
        // Connect signals after UI is ready
        CallDeferred(nameof(ConnectSignals));
    }
    
    private void ConnectSignals()
    {
        if (_dialogUI != null)
        {
            _dialogUI.DialogClosed += OnDialogUICloseRequested;
            _dialogUI.ChoiceSelected += OnChoiceSelected;
        }
    }
    
    private void OnDialogUICloseRequested()
    {
        // This is called when the UI requests to close (like when message timer expires)
        GD.Print("DialogUI requested close - cleaning up state");
        
        // Only clean up state if we're not in the middle of handling a correct answer
        // (CompleteCorrectAnswer will handle its own cleanup)
        if (_currentDialog == null || _currentDialog.Id != "correct_passage")
        {
            _currentDialog = null;
            _currentNPC = null;
            _initialDialog = null;
            _playerAgreedToQuestions = false;
            _isHandlingExit = false;
            GD.Print("State cleaned up by OnDialogUICloseRequested");
        }
        else
        {
            GD.Print("Skipping state cleanup - correct answer in progress");
        }
    }

    private void LoadDialogData()
    {
        string filePath = "res://Dialogs/HazeQuestions.json";
        
        if (!FileAccess.FileExists(filePath))
        {
            GD.PrintErr($"Dialog file not found: {filePath}");
            return;
        }

        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"Failed to open dialog file: {filePath}");
            return;
        }

        string jsonText = file.GetAsText();
        
        try
        {
            _dialogData = JsonSerializer.Deserialize<DialogData>(jsonText);
            GD.Print($"Loaded {_dialogData.Dialogs.Count} dialog entries");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to parse dialog data: {ex.Message}");
        }
    }
    #endregion

    #region Public API
    public void StartDialog(string dialogId, Node2D npc)
    {
        if (_dialogData == null)
        {
            GD.PrintErr("Dialog data not loaded");
            return;
        }

        if (_dialogUI == null)
        {
            GD.PrintErr("Dialog UI not initialized yet");
            return;
        }

        var dialog = _dialogData.Dialogs.FirstOrDefault(d => d.Id == dialogId);
        if (dialog == null)
        {
            GD.PrintErr($"Dialog not found: {dialogId}");
            return;
        }

        _currentDialog = dialog;
        _currentNPC = npc;
        _initialDialog = dialog; // Store initial dialog for textWhenGoOut access
        _playerAgreedToQuestions = false; // Reset state
        
        ShowDialog(dialog);
        GD.Print($"Started dialog: {dialogId}");
    }

    public bool IsDialogActive()
    {
        return _dialogUI != null && _dialogUI.Visible;
    }

    public void CloseDialog()
    {
        if (_dialogUI != null && _dialogUI.Visible)
        {
            GD.Print($"CloseDialog called - _playerAgreedToQuestions: {_playerAgreedToQuestions}, _initialDialog != null: {_initialDialog != null}, _isHandlingExit: {_isHandlingExit}");
            
            // Check if player agreed to questions but is leaving without answering
            if (_playerAgreedToQuestions && _initialDialog != null && !_isHandlingExit)
            {
                GD.Print("Detected player exit without answering - calling HandlePlayerExitWithoutAnswering");
                HandlePlayerExitWithoutAnswering();
                return;
            }
            
            _dialogUI.HideDialog();
            OnDialogClosed();
            GD.Print("Dialog closed by external request");
        }
        else
        {
            GD.Print("CloseDialog called but dialog UI is null or not visible");
        }
    }
    #endregion

    #region Dialog Flow
    private void ShowDialog(DialogEntry dialog)
    {
        if (dialog.AskContinue)
        {
            _dialogUI.ShowInitialDialog(dialog.Text);
        }
        else if (dialog.Questions != null && dialog.Questions.Count > 0)
        {
            // Pick a random question
            var randomQuestion = dialog.Questions[_random.Next(dialog.Questions.Count)];
            _dialogUI.ShowQuestion(randomQuestion);
        }
    }

    private void OnChoiceSelected(string choice)
    {
        GD.Print($"Choice selected: {choice}");
        
        // Handle standard responses
        switch (choice.ToUpper())
        {
            case "YES":
                HandleYesChoice();
                break;
            case "NO":
                HandleNoChoice();
                break;
            case "CONTINUE":
                // Handle continue button for messages like correct_passage
                CompleteCorrectAnswer();
                break;
            default:
                // Handle custom choices from the JSON (including answer choices)
                HandleCustomChoice(choice);
                break;
        }
    }
    
    private void HandleCustomChoice(string choiceText)
    {
        if (_currentDialog == null) return;
        
        // Find the choice in current dialog
        DialogChoice selectedChoice = null;
        
        // Check if we're in a question phase
        if (_currentDialog.Questions != null && _currentDialog.Questions.Count > 0)
        {
            // Find the choice in the current question
            foreach (var question in _currentDialog.Questions)
            {
                selectedChoice = question.Choices.FirstOrDefault(c => c.Text == choiceText);
                if (selectedChoice != null) break;
            }
        }
        else
        {
            // Find the choice in the main dialog
            selectedChoice = _currentDialog.Choices.FirstOrDefault(c => c.Text == choiceText);
        }
        
        if (selectedChoice != null)
        {
            ProcessChoice(selectedChoice);
        }
        else
        {
            GD.PrintErr($"Choice not found: {choiceText}");
        }
    }
    
    private void ProcessChoice(DialogChoice choice)
    {
        GD.Print($"Processing choice: {choice.Text}, IsCorrect: {choice.IsCorrect}, Action: {choice.Action}");
        
        // If this is an answer to a question, reset the agreed flag since player has now answered
        if (_playerAgreedToQuestions && (choice.IsCorrect || choice.Action == "wrong_answer"))
        {
            _playerAgreedToQuestions = false;
        }
        
        if (choice.IsCorrect)
        {
            HandleCorrectAnswer();
        }
        else if (choice.Action == "wrong_answer")
        {
            HandleWrongAnswer();
        }
        else if (choice.Action == "cancel")
        {
            HandleNoChoice();
        }
        else if (!string.IsNullOrEmpty(choice.NextDialog))
        {
            // Navigate to next dialog
            var nextDialog = _dialogData.Dialogs.FirstOrDefault(d => d.Id == choice.NextDialog);
            if (nextDialog != null)
            {
                _currentDialog = nextDialog;
                ShowDialog(nextDialog);
            }
        }
    }

    private void HandleYesChoice()
    {
        // Player said yes to the riddles - check if they have Purple Apples
        var inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;
        
        if (inventory == null || !inventory.HasPurpleApples())
        {
            // Player doesn't have Purple Apples - show rejection message
            var rejectionMessage = _initialDialog?.TextWhenPlayerDoesNotHavePurpleApples ?? 
                                 "You do not have what I need. Come back when you do.";
            
            GD.Print($"Player doesn't have Purple Apples - showing rejection message: {rejectionMessage}");
            _dialogUI.ShowMessage(rejectionMessage);
            
            // Don't close dialog immediately, let the message timer handle it
            return;
        }
        
        // Player has Purple Apples - consume one and proceed to questions
        if (inventory.RemovePurpleApples(1))
        {
            GD.Print("Purple Apple consumed - proceeding to questions");
            
            // Player agreed to answer questions
            _playerAgreedToQuestions = true;
            GD.Print("HandleYesChoice called - set _playerAgreedToQuestions = true");
            
            // Move to the question phase - look for "dark_questions" dialog
            var questionDialog = _dialogData.Dialogs.FirstOrDefault(d => d.Id == "dark_questions");
            if (questionDialog != null)
            {
                _currentDialog = questionDialog;
                if (questionDialog.Questions != null && questionDialog.Questions.Count > 0)
                {
                    var randomQuestion = questionDialog.Questions[_random.Next(questionDialog.Questions.Count)];
                    _dialogUI.ShowQuestion(randomQuestion);
                    GD.Print($"Showing random question: {randomQuestion.Text.Substring(0, Math.Min(50, randomQuestion.Text.Length))}...");
                }
            }
            else
            {
                GD.PrintErr("dark_questions dialog not found");
            }
        }
        else
        {
            GD.PrintErr("Failed to remove Purple Apple from inventory!");
        }
    }

    private void HandleNoChoice()
    {
        // Player doesn't want to continue - close dialog
        _dialogUI.HideDialog();
    }

    private void HandleCorrectAnswer()
    {
        // Check if there's a "correct_passage" dialog to show first
        var correctPassageDialog = _dialogData.Dialogs.FirstOrDefault(d => d.Id == "correct_passage");
        if (correctPassageDialog != null)
        {
            // Show the correct passage message without auto-hide
            _currentDialog = correctPassageDialog;
            _dialogUI.ShowMessageWithoutAutoHide(correctPassageDialog.Text);
            
            // Wait for the message display time, then complete the correct answer
            var timer = GetTree().CreateTimer(_dialogUI.MessageDisplayTime);
            timer.Timeout += CompleteCorrectAnswer;
        }
        else
        {
            // No passage dialog, handle immediately
            CompleteCorrectAnswer();
        }
    }
    
    private void CompleteCorrectAnswer()
    {
        GD.Print("CompleteCorrectAnswer called");
        
        // Store the current NPC reference before clearing state
        var npc = _currentNPC;
        
        // Reset flags since interaction is complete
        _playerAgreedToQuestions = false;
        
        // Hide the dialog UI
        _dialogUI.HideDialog();
        
        // Clean up state
        _currentDialog = null;
        _currentNPC = null;
        _initialDialog = null;
        _isHandlingExit = false;
        
        // Now call the NPC's OnCorrectAnswer method with the stored reference
        if (npc != null && npc.HasMethod("OnCorrectAnswer"))
        {
            GD.Print("Calling OnCorrectAnswer on NPC");
            npc.Call("OnCorrectAnswer");
        }
        else
        {
            GD.PrintErr("Failed to call OnCorrectAnswer - NPC is null or doesn't have the method");
        }
        
        GD.Print("Correct answer processed - Haze should disappear and spawn crystal");
    }

    private void HandleWrongAnswer()
    {
        // Player answered wrong - Haze disappears
        _playerAgreedToQuestions = false; // Reset flag since interaction is complete
        _dialogUI.HideDialog();
        
        if (_currentNPC != null && _currentNPC.HasMethod("OnWrongAnswer"))
        {
            _currentNPC.Call("OnWrongAnswer");
        }
    }

    private void HandlePlayerExitWithoutAnswering()
    {
        // Prevent multiple calls
        if (_isHandlingExit) return;
        _isHandlingExit = true;
        
        // Player said YES but left without answering - show textWhenGoOut message and make Haze disappear
        GD.Print("HandlePlayerExitWithoutAnswering called - showing exit message");
        
        string exitMessage = "You left without answering..."; // Default message
        
        // Get textWhenGoOut from the initial dialog
        if (_initialDialog != null && !string.IsNullOrEmpty(_initialDialog.TextWhenGoOut))
        {
            exitMessage = _initialDialog.TextWhenGoOut;
            GD.Print($"Using textWhenGoOut: {exitMessage}");
        }
        else
        {
            GD.Print("Using default exit message");
        }
        
        // Show the exit message using the MessagePanel with configurable display time
        float messageDisplayTime = 4.0f; // 4 seconds display time
        GD.Print($"Showing exit message: {exitMessage} for {messageDisplayTime} seconds");
        _dialogUI.ShowMessage(exitMessage, messageDisplayTime);
        
        // Make Haze disappear immediately, don't wait for the message timer
        GD.Print("Making Haze disappear immediately after showing exit message");
        if (_currentNPC != null && _currentNPC.HasMethod("OnPlayerExitWithoutAnswering"))
        {
            _currentNPC.Call("OnPlayerExitWithoutAnswering");
        }
        else if (_currentNPC != null && _currentNPC.HasMethod("OnWrongAnswer"))
        {
            // Fallback to wrong answer method if the specific method doesn't exist
            _currentNPC.Call("OnWrongAnswer");
        }
        
        // Clean up dialog state immediately
        OnDialogClosed();
        
        _playerAgreedToQuestions = false; // Reset flag
    }

    private void OnDialogClosed()
    {
        GD.Print("OnDialogClosed called - cleaning up dialog state");
        _currentDialog = null;
        _currentNPC = null;
        _initialDialog = null;
        _playerAgreedToQuestions = false;
        _isHandlingExit = false; // Reset the exit handling flag
    }
    #endregion

    #region Reward System
    private void SpawnPuzzleCrystal()
    {
        // Load the PuzzleCrystal scene
        var puzzleCrystalScene = GD.Load<PackedScene>("res://Prefabs/PuzzleCrystal.tscn");
        if (puzzleCrystalScene == null)
        {
            GD.PrintErr("PuzzleCrystal scene not found");
            return;
        }

        var puzzleCrystal = puzzleCrystalScene.Instantiate<Node2D>();
        
        // Position it at the NPC's location
        if (_currentNPC != null)
        {
            puzzleCrystal.GlobalPosition = _currentNPC.GlobalPosition + new Vector2(0, -50);
        }
        
        // Add to the current scene
        GetTree().CurrentScene.AddChild(puzzleCrystal);
        
        GD.Print("Puzzle crystal spawned as reward!");
    }
    #endregion

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}