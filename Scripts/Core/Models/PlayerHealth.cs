namespace MarrieGame.Core.Models
{
    /// <summary>
    /// Typed payload for player health change events.
    /// </summary>
    public readonly record struct PlayerHealth(int Current, int Max);
}
