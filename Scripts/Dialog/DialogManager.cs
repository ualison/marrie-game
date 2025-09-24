using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        
        // Create dialog UI and add it to the canvas layer with deferred call
        _dialogUI = new DialogUI();
        _dialogUI.Name = "DialogUI";
        canvasLayer.CallDeferred("add_child", _dialogUI);
        
        // Connect signals after UI is ready
        CallDeferred(nameof(ConnectSignals));
    }
    
    private void ConnectSignals()
    {
        if (_dialogUI != null)
        {
            _dialogUI.DialogClosed += OnDialogClosed;
            _dialogUI.ChoiceSelected += OnChoiceSelected;
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
        
        ShowDialog(dialog);
    }

    public bool IsDialogActive()
    {
        return _dialogUI != null && _dialogUI.Visible;
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
        switch (choice)
        {
            case "YES":
                HandleYesChoice();
                break;
            case "NO":
                HandleNoChoice();
                break;
            case "correct":
                HandleCorrectAnswer();
                break;
            case "wrong":
                HandleWrongAnswer();
                break;
        }
    }

    private void HandleYesChoice()
    {
        // Move to the question phase
        var questionDialog = _dialogData.Dialogs.FirstOrDefault(d => d.Id == "riddle_question");
        if (questionDialog != null)
        {
            _currentDialog = questionDialog;
            var randomQuestion = questionDialog.Questions[_random.Next(questionDialog.Questions.Count)];
            _dialogUI.ShowQuestion(randomQuestion);
        }
    }

    private void HandleNoChoice()
    {
        // Player doesn't want to continue - close dialog
        _dialogUI.HideDialog();
    }

    private void HandleCorrectAnswer()
    {
        // Player answered correctly - let NPC handle reward spawning
        _dialogUI.HideDialog();
        
        // Make Haze disappear and handle its own crystal spawning
        if (_currentNPC != null && _currentNPC.HasMethod("OnCorrectAnswer"))
        {
            _currentNPC.Call("OnCorrectAnswer");
        }
    }

    private void HandleWrongAnswer()
    {
        // Player answered wrong - Haze disappears
        _dialogUI.HideDialog();
        
        if (_currentNPC != null && _currentNPC.HasMethod("OnWrongAnswer"))
        {
            _currentNPC.Call("OnWrongAnswer");
        }
    }

    private void OnDialogClosed()
    {
        _currentDialog = null;
        _currentNPC = null;
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