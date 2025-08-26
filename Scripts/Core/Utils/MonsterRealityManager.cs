using Godot;
using MarrieGame.Core.Enums;

/// <summary>
/// Utility class for managing monster realities in the game
/// Provides methods to change monster realities globally or individually
/// </summary>
public static class MonsterRealityManager
{
    /// <summary>
    /// Changes the reality of all monsters in the scene
    /// </summary>
    /// <param name="newReality">The new reality to apply to all monsters</param>
    /// <param name="sceneTree">The scene tree to search for monsters</param>
    public static void ChangeAllMonstersReality(MonsterReality newReality, SceneTree sceneTree)
    {
        var monsters = sceneTree.GetNodesInGroup("enemies");
        int monstersChanged = 0;
        
        foreach (Node node in monsters)
        {
            if (node is Monster monster)
            {
                monster.SetReality(newReality);
                monstersChanged++;
            }
        }
        
        Log.System($"Changed reality to {newReality} for {monstersChanged} monsters", "MonsterRealityManager");
    }
    
    /// <summary>
    /// Changes the reality of a specific monster
    /// </summary>
    /// <param name="monster">The monster to change</param>
    /// <param name="newReality">The new reality to apply</param>
    public static void ChangeMonsterReality(Monster monster, MonsterReality newReality)
    {
        if (monster != null)
        {
            monster.SetReality(newReality);
            Log.System($"Changed monster {monster.Name} reality to {newReality}", "MonsterRealityManager");
        }
    }
    
    /// <summary>
    /// Gets the current reality of a monster
    /// </summary>
    /// <param name="monster">The monster to check</param>
    /// <returns>The monster's current reality</returns>
    public static MonsterReality GetMonsterReality(Monster monster)
    {
        return monster?.GetCurrentReality() ?? MonsterReality.CurrentReality;
    }
    
    /// <summary>
    /// Toggles between CurrentReality and PurpleGemReality for all monsters
    /// </summary>
    /// <param name="sceneTree">The scene tree to search for monsters</param>
    public static void ToggleAllMonstersReality(SceneTree sceneTree)
    {
        var monsters = sceneTree.GetNodesInGroup("enemies");
        
        foreach (Node node in monsters)
        {
            if (node is Monster monster)
            {
                var currentReality = monster.GetCurrentReality();
                var newReality = currentReality == MonsterReality.CurrentReality 
                    ? MonsterReality.PurpleGemReality 
                    : MonsterReality.CurrentReality;
                    
                monster.SetReality(newReality);
            }
        }
        
        Log.System("Toggled reality for all monsters", "MonsterRealityManager");
    }
}
