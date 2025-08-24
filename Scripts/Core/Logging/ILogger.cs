using System;

namespace MarrieGame.Core.Logging
{
    /// <summary>
    /// Interface for logging system following Dependency Inversion Principle
    /// Allows different logging implementations and testing
    /// </summary>
    public interface ILogger
    {
        void LogDebug(string message, string category = "Debug");
        void LogInfo(string message, string category = "Info");
        void LogWarning(string message, string category = "Warning");
        void LogError(string message, string category = "Error");
        void LogCritical(string message, string category = "Critical");
        
        // Game-specific logging (reduced scope)
        void LogGameplay(string message, string context = "");
        void LogSystem(string message, string system = "");
        void LogPerformance(string message, double? executionTime = null);
        
        // Feature-specific (only critical events)
        void LogReplay(string message, string replayContext = "");
        void LogUI(string message, string uiComponent = "");
        void LogAudio(string message, string audioContext = "");
        
        void SetLogLevel(LogLevel level);
        bool IsEnabled(LogLevel level);
    }
    
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4,
        Off = 5
    }
    
    public enum LogCategory
    {
        // Standard levels
        Debug,
        Info,
        Warning,
        Error,
        Critical,
        
        // Game-specific categories
        Gameplay,
        System,
        Performance,
        
        // Feature-specific (only log important events)
        Replay,
        UI,
        Audio,
        
        // Component-specific (reduced logging)
        Player,
        Enemy,
        Scene
    }
}
