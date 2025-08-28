namespace MarrieGame.Core.Interfaces
{
    /// <summary>
    /// Interface for audio management
    /// </summary>
    public interface IAudioManager
    {
        void PlaySound(string soundName);
        void StopSound(string soundName);
        void SetMasterVolume(float volume);
        void RestartBackgroundMusic();
    }
}
