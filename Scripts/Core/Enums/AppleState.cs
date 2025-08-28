namespace Core.Enums
{
    /// <summary>
    /// Defines the different states an apple can be in during its lifecycle.
    /// 
    /// State cycles differ based on apple position:
    /// Tree Cycle: RedLife → GrayKnockable → PurpleMana → RedLife (repeats)
    /// Ground Cycle: RedLife → GrayNotCollectible → PurpleMana → RedLife (repeats)
    /// 
    /// States:
    /// - RedLife: Red color, Life phase (collectible when on ground, restores health)
    /// - GrayKnockable: Gray color, Knockable phase (tree only, can be hit by rocks to fall)
    /// - PurpleMana: Purple color, Mana phase (collectible when on ground, restores mana)
    /// - GrayNotCollectible: Dark gray color, Non-collectible phase (ground only, not collectible)
    /// 
    /// Only RedLife and PurpleMana phases are collectible, and only when apple is on the ground
    /// </summary>
    public enum AppleState
    {
        // Red color - Life phase (collectible when on ground, restores health)
        RedLife,
        
        // Gray color - Knockable phase (tree only, can be hit by rocks to fall)
        GrayKnockable,
        
        /// Purple color - Mana phase (collectible when on ground, restores mana)
        /// </summary>
        PurpleMana,
        
        /// <summary>
        /// Dark gray color - Non-collectible phase (ground only, not collectible after being knocked down)
        /// </summary>
        GrayNotCollectible
    }
}
