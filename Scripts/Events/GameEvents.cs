using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Simple event bus for decoupling game systems
/// Much lighter than complex observer pattern - just what you need
/// </summary>
public static class GameEvents
{
    // Simple event definitions
    public static event Action<Vector2, int> PlayerDied;
    public static event Action<int, int> PlayerHealthChanged;
    public static event Action<string> GameStateChanged;
    public static event Action GameRestarted;
    public static event Action GameRestart;
    public static event Action<Vector2> ReplayGemCreated;
    
    // Fire events
    public static void OnPlayerDied(Vector2 position, int remainingHealth)
    {
        PlayerDied?.Invoke(position, remainingHealth);
    }
    
    public static void OnPlayerHealthChanged(int currentHealth, int maxHealth)
    {
        PlayerHealthChanged?.Invoke(currentHealth, maxHealth);
    }
    
    public static void OnGameStateChanged(string newState)
    {
        GameStateChanged?.Invoke(newState);
    }
    
    public static void OnGameRestarted()
    {
        GameRestarted?.Invoke();
    }
    
    public static void OnGameRestart()
    {
        GameRestart?.Invoke();
    }
    
    public static void OnReplayGemCreated(Vector2 position)
    {
        ReplayGemCreated?.Invoke(position);
    }
    
    // Clear all subscriptions (useful for scene changes)
    public static void ClearAllSubscriptions()
    {
        PlayerDied = null;
        PlayerHealthChanged = null;
        GameStateChanged = null;
        GameRestarted = null;
        GameRestart = null;
        ReplayGemCreated = null;
    }
}
