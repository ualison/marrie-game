namespace MarrieGame.Core.Enums
{
    /// <summary>
    /// Defines different replay recording modes for different scenes
    /// </summary>
    public enum ReplayRecordingMode
    {
        /// <summary>
        /// Traditional mode - record until player death, then create replay gem at death location
        /// Used in Forest and similar exploration scenes
        /// </summary>
        DeathMode,
        
        /// <summary>
        /// Puzzle mode - record only player movements continuously, single session until replay gem is pressed
        /// Used in PuzzleDimension and similar puzzle-solving scenes
        /// No death required, replay can be triggered manually via static gems
        /// </summary>
        PuzzleMode
    }
}