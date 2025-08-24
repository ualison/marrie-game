using Godot;

/// <summary>
/// Scene setup helper - add this to your main scene's root node
/// This ensures GameManager is properly initialized
/// </summary>
public partial class SceneSetup : Node
{
    public override void _Ready()
    {
        // Ensure GameManager exists in the scene
        if (GameManager.Instance == null)
        {
            var gameManager = new GameManager();
            gameManager.Name = "GameManager";
            GetTree().CurrentScene.AddChild(gameManager);
            GD.Print("SceneSetup: Created GameManager automatically");
        }
        
        // Optional: Verify required groups exist
        VerifyRequiredGroups();
    }
    
    private void VerifyRequiredGroups()
    {
        var requiredGroups = new[] { "player", "game_over_ui", "life_bar_ui" };
        
        foreach (var group in requiredGroups)
        {
            var nodes = GetTree().GetNodesInGroup(group);
            if (nodes.Count == 0)
            {
                GD.PrintErr($"SceneSetup: No nodes found in required group '{group}'");
            }
            else
            {
                GD.Print($"SceneSetup: Found {nodes.Count} node(s) in group '{group}'");
            }
        }
    }
}
