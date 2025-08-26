namespace Core.Enums
{
    /// <summary>
    /// Defines the physical position of an apple in the game world
    /// </summary>
    public enum ApplePosition
    {
        /// <summary>
        /// Apple is attached to a tree and has not fallen
        /// </summary>
        OnTree,
        
        /// <summary>
        /// Apple has fallen and is resting on the ground
        /// </summary>
        OnGround
    }
}
