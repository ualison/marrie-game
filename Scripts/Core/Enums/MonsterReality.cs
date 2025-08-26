namespace MarrieGame.Core.Enums
{
    /// <summary>
    /// Defines the reality state of monsters, affecting which type of damage they take from rocks
    /// </summary>
    public enum MonsterReality
    {
        /// <summary>
        /// Current reality - rock damage affects red hearts only
        /// </summary>
        CurrentReality,
        
        /// <summary>
        /// Purple gem reality - rock damage affects purple hearts only
        /// Monster dies when all purple hearts are lost
        /// </summary>
        PurpleGemReality
    }
}
