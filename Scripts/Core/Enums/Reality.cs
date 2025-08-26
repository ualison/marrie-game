namespace MarrieGame.Core.Enums
{
    /// <summary>
    /// Defines the reality state, affecting which type of damage entities take from rocks
    /// </summary>
    public enum Reality
    {
        /// <summary>
        /// Current reality - rock damage affects red hearts only
        /// </summary>
        CurrentReality,
        
        /// <summary>
        /// Past reality - rock damage affects purple hearts only
        /// Monster dies when all purple hearts are lost
        /// </summary>
        PastReality
    }
}
