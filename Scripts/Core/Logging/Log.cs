using MarrieGame.Core.Logging;

/// <summary>
/// Static facade for easy logging access throughout the game
/// Why: keep call sites concise while allowing the underlying logger to be swapped or configured centrally
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
    
    public static void SetCategoryFilter(LogCategory category, bool enabled)
    {
        if (Logger is MarrieGame.Core.Logging.GameLogger gameLogger)
            gameLogger.SetCategoryFilter(category, enabled);
    }

    // Why: allow call sites to avoid constructing expensive log messages when category is disabled
    public static bool IsCategoryEnabled(LogCategory category)
    {
        if (Logger is MarrieGame.Core.Logging.GameLogger gameLogger)
            return gameLogger.IsCategoryEnabled(category);
        return false;
    }
    
    /// <summary>
    /// Enable minimal logging for production - only warnings, errors, and critical events
    /// Use this to reduce log noise and focus on potential bugs
    /// </summary>
    public static void EnableMinimalLogging()
    {
        SetLogLevel(LogLevel.Warning); // Focus on problems, not routine operations
    // Why: notify that logs were reduced so developers can correlate missing entries
    Info("Minimal logging enabled - prioritizing warnings and errors for bug detection", "Logger");
    }
    
    /// <summary>
    /// Reduce verbose logging to focus on errors and important events
    /// Disables debug and performance categories while keeping error detection active
    /// </summary>
    public static void ReduceVerboseLogging()
    {
        if (Logger is MarrieGame.Core.Logging.GameLogger gameLogger)
        {
            gameLogger.SetCategoryFilter(LogCategory.Debug, false);
            gameLogger.SetCategoryFilter(LogCategory.Performance, false);
            gameLogger.SetCategoryFilter(LogCategory.System, false); // Reduce system initialization noise
            // Keep error categories active for bug detection
            gameLogger.SetCategoryFilter(LogCategory.Error, true);
            gameLogger.SetCategoryFilter(LogCategory.Warning, true);
            // Why: explicit message to mark runtime mode change in logs
            Info("Reduced verbose logging - focusing on warnings and errors", "Logger");
        }
    }
    #endregion
}
