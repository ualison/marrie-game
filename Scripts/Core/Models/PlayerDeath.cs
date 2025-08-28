using Godot;

namespace MarrieGame.Core.Models
{
    /// <summary>
    /// Typed payload for player death events to avoid multi-arg delegates.
    /// </summary>
    public readonly record struct PlayerDeath(Vector2 Position, int RemainingHealth);
}
