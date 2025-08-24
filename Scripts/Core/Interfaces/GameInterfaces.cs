using Godot;

/// <summary>
/// Core game interfaces for system contracts
/// Provides clear contracts for different system responsibilities
/// </summary>

namespace MarrieGame.Core.Interfaces
{
    /// <summary>
    /// Interface for characters that can be recorded for replay
    /// </summary>
    public interface IReplayableCharacter
    {
        Vector2 GlobalPosition { get; }
        float GlobalRotation { get; }
        bool IsVisible { get; }
        string GetCurrentAnimation();
        bool GetSpriteFlip();
        float GetAlpha();
    }

    /// <summary>
    /// Interface for entities that support pausing/resuming
    /// </summary>
    public interface IPausable
    {
        bool IsPaused { get; }
        void Pause();
        void Resume();
    }

    /// <summary>
    /// Interface for entities that can be reset to initial state
    /// </summary>
    public interface IResettable
    {
        void Reset();
    }

    /// <summary>
    /// Interface for health management systems
    /// </summary>
    public interface IHealthSystem
    {
        int CurrentHealth { get; }
        int MaxHealth { get; }
        bool IsDead { get; }
        bool TakeDamage(int amount);
        void Heal(int amount);
        void Reset();
    }

    /// <summary>
    /// Interface for game state management
    /// </summary>
    public interface IGameStateManager
    {
        bool IsGameOver { get; }
        void RestartGame();
        void QuitGame();
    }

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
