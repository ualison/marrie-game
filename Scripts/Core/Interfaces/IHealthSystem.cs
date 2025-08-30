namespace MarrieGame.Core.Interfaces
{
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
}
