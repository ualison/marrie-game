using MarrieGame.Core.Logging;

/// <summary>
/// Static facade for easy logging access throughout the game
/// Provides simple API while maintaining flexibility of the underlying system
/// </summary>
public static class Log
{
    private static ILogger Logger => MarrieGame.Core.Logging.GameLogger.Instance;
    
    #region Standard Logging
    public static void Debug(string message, string category = "Debug")
    {
        Logger?.LogDebug(message, category);
    }
    
    public static void Info(string message, string category = "Info")
    {
        Logger?.LogInfo(message, category);
    }
    
    public static void Warning(string message, string category = "Warning")
    {
        Logger?.LogWarning(message, category);
    }
    
    public static void Error(string message, string category = "Error")
    {
        Logger?.LogError(message, category);
    }
    
    public static void Critical(string message, string category = "Critical")
    {
        Logger?.LogCritical(message, category);
    }
    #endregion
    
    #region Game-Specific Logging
    public static void Gameplay(string message, string context = "")
    {
        Logger?.LogGameplay(message, context);
    }
    
    public static void System(string message, string system = "")
    {
        Logger?.LogSystem(message, system);
    }
    
    public static void Performance(string message, double? executionTime = null)
    {
        Logger?.LogPerformance(message, executionTime);
    }
    
    public static void Replay(string message, string context = "")
    {
        Logger?.LogReplay(message, context);
    }
    
    public static void UI(string message, string component = "")
    {
        Logger?.LogUI(message, component);
    }
    
    public static void Audio(string message, string context = "")
    {
        Logger?.LogAudio(message, context);
    }
    #endregion
    
    #region Performance Tracking
    public static void StartTimer(string timerName)
    {
        if (Logger is MarrieGame.Core.Logging.GameLogger gameLogger)
            gameLogger.StartPerformanceTimer(timerName);
    }
    
    public static void EndTimer(string timerName, string operation = "")
    {
        if (Logger is MarrieGame.Core.Logging.GameLogger gameLogger)
            gameLogger.EndPerformanceTimer(timerName, operation);
    }
    #endregion
    
    #region Configuration
    public static void SetLogLevel(LogLevel level)
    {
        Logger?.SetLogLevel(level);
    }
    
    public static void EnableDebugMode()
    {
        if (Logger is MarrieGame.Core.Logging.GameLogger gameLogger)
            gameLogger.EnableDebugMode();
    }
    
    public static void EnableReleaseMode()
    {
        if (Logger is MarrieGame.Core.Logging.GameLogger gameLogger)
            gameLogger.EnableReleaseMode();
    }
    #endregion
}
