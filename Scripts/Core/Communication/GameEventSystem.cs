using Godot;
using System;

/// <summary>
/// Central event system for game-wide communication
/// Provides type-safe messaging between game systems and components
/// </summary>
public static class GameEventSystem
{
    // Player Events
    public static event Action<Vector2, int> PlayerDied;
    public static event Action<int, int> PlayerHealthChanged;
    
    // Game State Events
    public static event Action<string> GameStateChanged;
    public static event Action GameRestart;
    public static event Action GameRestarted;
    
    // System Events
    public static event Action<Vector2> ReplayGemCreated;
    
    #region Player Event Triggers
    public static void OnPlayerDied(Vector2 position, int remainingHealth)
    {
        PlayerDied?.Invoke(position, remainingHealth);
    }
    
    public static void OnPlayerHealthChanged(int currentHealth, int maxHealth)
    {
        PlayerHealthChanged?.Invoke(currentHealth, maxHealth);
    }
    #endregion
    
    #region Game State Event Triggers
    public static void OnGameStateChanged(string newState)
    {
        GameStateChanged?.Invoke(newState);
    }
    
    public static void OnGameRestart()
    {
        GameRestart?.Invoke();
    }
    
    public static void OnGameRestarted()
    {
        GameRestarted?.Invoke();
    }
    #endregion
    
    #region System Event Triggers
    public static void OnReplayGemCreated(Vector2 position)
    {
        ReplayGemCreated?.Invoke(position);
    }
    #endregion
    
    #region Cleanup
    public static void ClearAllSubscriptions()
    {
        PlayerDied = null;
        PlayerHealthChanged = null;
        GameStateChanged = null;
        GameRestart = null;
        GameRestarted = null;
        ReplayGemCreated = null;
    }
    #endregion
}
