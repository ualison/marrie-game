using Godot;
using System;
using MarrieGame.Core.Enums;

/// <summary>
/// Central event system for game-wide communication
/// Provides type-safe messaging between game systems and components
/// </summary>
public static class GameEventSystem
{
    // Player Events
    public static event Action<MarrieGame.Core.Models.PlayerDeath> PlayerDied;
    public static event Action<MarrieGame.Core.Models.PlayerHealth> PlayerHealthChanged;
    public static event Action<int, int> PlayerManaChanged;
    public static event Action PlayerManaRestored;
    public static event Action PlayerManaDepleted;
    
    // Monster Events
    public static event Action<MarrieGame.Core.Models.MonsterHealth> MonsterHealthChanged;
    
    // Game State Events
    public static event Action<GameState> GameStateChanged;
    public static event Action GameRestart;
    public static event Action GameRestarted;
    
    // System Events
    public static event Action<Vector2> ReplayGemCreated;
    
    #region Player Event Triggers
    public static void OnPlayerDied(MarrieGame.Core.Models.PlayerDeath payload)
    {
        PlayerDied?.Invoke(payload);
    }
    
    public static void OnPlayerHealthChanged(int currentHealth, int maxHealth)
    {
        var payload = new MarrieGame.Core.Models.PlayerHealth(currentHealth, maxHealth);
        PlayerHealthChanged?.Invoke(payload);
    }
    
    public static void OnPlayerManaChanged(int currentMana, int maxMana)
    {
        PlayerManaChanged?.Invoke(currentMana, maxMana);
    }
    
    public static void OnPlayerManaRestored()
    {
        PlayerManaRestored?.Invoke();
    }
    
    public static void OnPlayerManaDepleted()
    {
        PlayerManaDepleted?.Invoke();
    }
    #endregion
    
    #region Monster Event Triggers
    public static void OnMonsterHealthChanged(int currentHealth, int maxHealth, int purpleHearts)
    {
        var payload = new MarrieGame.Core.Models.MonsterHealth(currentHealth, maxHealth, purpleHearts);
        MonsterHealthChanged?.Invoke(payload);
    }
    #endregion
    
    #region Game State Event Triggers
    public static void OnGameStateChanged(GameState newState)
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
        PlayerManaChanged = null;
        PlayerManaRestored = null;
        PlayerManaDepleted = null;
        MonsterHealthChanged = null;
        GameStateChanged = null;
        GameRestart = null;
        GameRestarted = null;
        ReplayGemCreated = null;
    }
    #endregion
}
