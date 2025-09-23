namespace Core.Enums
{
    /// <summary>
    /// Defines the different states an apple can be in during its lifecycle.
    /// 
    /// State cycles differ based on apple position:
    /// Tree Cycle: RedLife → GrayKnockable → PurpleMana → YellowEnergy → RedLife (repeats)
    /// Ground Cycle: RedLife → GrayNotCollectible → PurpleMana → YellowEnergy → RedLife (repeats)
    /// 
    /// States:
    /// - RedLife: Red color, Life phase (collectible when on ground, restores health)
    /// - GrayKnockable: Gray color, Knockable phase (tree only, can be hit by rocks to fall)
    /// - PurpleMana: Purple color, Mana phase (collectible when on ground, restores mana)
    /// - GrayNotCollectible: Dark gray color, Non-collectible phase (ground only, not collectible)
    /// - YellowEnergy: Yellow color, Energy phase (collectible by Puff in both tree and ground positions, restores Puff energy)
    /// 
    /// Only RedLife and PurpleMana phases are collectible by player when apple is on the ground
    /// YellowEnergy phase is collectible by Puff regardless of position (tree or ground)
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
        GrayNotCollectible,
        
        /// <summary>
        /// Yellow color - Energy phase (collectible by Puff in both positions, restores Puff energy)
        /// </summary>
        YellowEnergy
    }
}
