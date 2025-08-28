namespace MarrieGame.Core.Interfaces
{
    /// <summary>
    /// Interface for entities that support pausing/resuming
    /// </summary>
    public interface IPausable
    {
        bool IsPaused { get; }
        void Pause();
        void Resume();
    }
}
