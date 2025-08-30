using Godot;

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
}
