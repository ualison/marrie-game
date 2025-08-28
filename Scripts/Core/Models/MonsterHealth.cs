namespace MarrieGame.Core.Models
{
    /// <summary>
    /// Typed payload for monster health change events.
    /// </summary>
    public readonly record struct MonsterHealth(int Current, int Max, int PurpleHearts);
}
