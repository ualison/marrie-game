namespace MarrieGame.Core.Models
{
    /// <summary>
    /// Typed payload for player mana change events.
    /// </summary>
    public readonly record struct PlayerMana(int Current, int Max);
}
