using Godot;
using System.Text.Json;
using MarrieGame.Scripts.Dialog;

/// <summary>
/// Simple test script to verify dialog system functionality
/// Run this in Godot editor to test JSON parsing
/// </summary>
public partial class DialogSystemTest : Node
{
    public override void _Ready()
    {
        TestJsonParsing();
        TestDialogDataStructure();
    }

    private void TestJsonParsing()
    {
        GD.Print("=== Testing Dialog JSON Parsing ===");
        
        string filePath = "res://Dialogs/HazeQuestions.json";
        
        if (!FileAccess.FileExists(filePath))
        {
            GD.PrintErr($"❌ Dialog file not found: {filePath}");
            return;
        }

        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"❌ Failed to open dialog file: {filePath}");
            return;
        }

        string jsonText = file.GetAsText();
        
        try
        {
            var dialogData = JsonSerializer.Deserialize<DialogData>(jsonText);
            
            GD.Print($"✅ JSON parsed successfully!");
            GD.Print($"✅ Found {dialogData.Dialogs.Count} dialog entries");
            GD.Print($"✅ Found {dialogData.Actions.Count} action definitions");
            
            // Test specific dialog entries
            var guardianEncounter = dialogData.Dialogs.Find(d => d.Id == "guardian_encounter");
            if (guardianEncounter != null)
            {
                GD.Print($"✅ Guardian encounter dialog found: '{guardianEncounter.Text}'");
                GD.Print($"✅ Has {guardianEncounter.Choices.Count} choices");
            }
            
            var riddleQuestion = dialogData.Dialogs.Find(d => d.Id == "riddle_question");
            if (riddleQuestion != null)
            {
                GD.Print($"✅ Riddle question dialog found with {riddleQuestion.Questions.Count} questions");
                foreach (var question in riddleQuestion.Questions)
                {
                    var correctAnswers = question.Choices.FindAll(c => c.IsCorrect);
                    GD.Print($"   Question: '{question.Text}' ({correctAnswers.Count} correct answers)");
                }
            }
            
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"❌ Failed to parse dialog data: {ex.Message}");
        }
    }

    private void TestDialogDataStructure()
    {
        GD.Print("\n=== Testing Dialog Data Structure ===");
        
        // Test creating dialog data structure programmatically
        var testChoice = new DialogChoice
        {
            Text = "Test Answer",
            IsCorrect = true,
            Action = "test_action"
        };
        
        var testQuestion = new DialogQuestion
        {
            Text = "Test Question?",
            Choices = new() { testChoice }
        };
        
        var testDialog = new DialogEntry
        {
            Id = "test_dialog",
            Text = "Test Dialog Text",
            Questions = new() { testQuestion }
        };
        
        GD.Print($"✅ Dialog structure created: {testDialog.Id}");
        GD.Print($"✅ Question: {testQuestion.Text}");
        GD.Print($"✅ Choice: {testChoice.Text} (Correct: {testChoice.IsCorrect})");
    }
}