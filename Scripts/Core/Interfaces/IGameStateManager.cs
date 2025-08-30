namespace MarrieGame.Core.Interfaces
{
    /// <summary>
    /// Interface for game state management
    /// </summary>
    public interface IGameStateManager
    {
        bool IsGameOver { get; }
        void RestartGame();
        void QuitGame();
    }
}
