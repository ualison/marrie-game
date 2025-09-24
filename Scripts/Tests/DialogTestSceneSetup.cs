using Godot;

/// <summary>
/// Simple scene setup script for testing the dialog system
/// This ensures everything initializes properly
/// </summary>
public partial class DialogTestSceneSetup : Node2D
{
    public override void _Ready()
    {
        GD.Print("=== Dialog Test Scene Setup ===");
        
        // Wait a bit for all managers to initialize
        CallDeferred(nameof(CheckSystemStatus));
    }
    
    private void CheckSystemStatus()
    {
        // Check if DialogManager is available
        if (DialogManager.Instance != null)
        {
            GD.Print("✅ DialogManager initialized successfully");
            
            // Check if dialog is ready
            var isActive = DialogManager.Instance.IsDialogActive();
            GD.Print($"✅ Dialog system ready (Active: {isActive})");
        }
        else
        {
            GD.PrintErr("❌ DialogManager not found - check GameManager initialization");
        }
        
        // Check for Haze in scene
        var haze = GetTree().GetFirstNodeInGroup("haze");
        if (haze != null)
        {
            GD.Print("✅ Haze NPC found in scene");
        }
        else
        {
            GD.Print("ℹ️ No Haze NPC found in scene (this is normal if not using DialogTestScene)");
        }
        
        // Check for player
        var player = GetTree().GetFirstNodeInGroup("player");
        if (player != null)
        {
            GD.Print("✅ Player found in scene");
        }
        else
        {
            GD.Print("⚠️ No player found in scene");
        }
        
        GD.Print("=== System Check Complete ===");
        GD.Print("Instructions: Approach Haze and press E to test the dialog system!");
    }
    
    public override void _Input(InputEvent @event)
    {
        // Debug shortcut to test dialog directly
        if (@event.IsActionPressed("ui_accept") && Input.IsKeyPressed(Key.Ctrl))
        {
            GD.Print("Debug: Testing dialog system directly...");
            if (DialogManager.Instance != null)
            {
                var haze = GetTree().GetFirstNodeInGroup("haze");
                if (haze is Node2D hazeNode)
                {
                    DialogManager.Instance.StartDialog("guardian_encounter", hazeNode);
                }
            }
        }
    }
}