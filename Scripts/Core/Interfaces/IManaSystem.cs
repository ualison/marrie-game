namespace MarrieGame.Core.Interfaces
{
    /// <summary>
    /// Interface for mana management systems
    /// </summary>
    public interface IManaSystem
    {
        int CurrentResource { get; }
        int MaxResource { get; }
        bool IsEmpty { get; }
        bool UseMana(int amount);
        void RestoreMana(int amount);
        void Reset();
    }
}
