namespace MarrieGame.Core.Enums
{
    // Defines the state of a monster affecting visibility, damage, and interaction
    public enum MonsterState
    {

        // Monster is visible and can take/give damage
        // Includes: fully visible, following player, can interact
        Visible,
        
        // Monster is hidden and cannot take/give damage  
        // Includes: fade out, invisible, fade in phases
        // Life bar is hidden, no interactions possible
        Hidden
    }
}
