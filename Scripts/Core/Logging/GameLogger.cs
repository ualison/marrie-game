using Godot;
using System;
using System.Collections.Generic;

namespace MarrieGame.Core.Logging
{
    /// <summary>
    /// Main logging implementation using Strategy Pattern for output methods
    /// Singleton pattern for global access while maintaining testability through interface
    /// </summary>
    public partial class GameLogger : Node, ILogger
    {
        public static GameLogger Instance { get; private set; }
        
        private LogLevel _currentLogLevel = LogLevel.Debug;
        private readonly Dictionary<LogCategory, bool> _categoryFilters = new();
        
        // Performance tracking
        private readonly Dictionary<string, DateTime> _performanceTimers = new();
        
        public override void _Ready()
        {
            if (Instance == null)
            {
                Instance = this;
                InitializeLogger();
            }
            else
            {
                QueueFree();
            }
        }
        
        private void InitializeLogger()
        {
            // Set default log level based on build type
            #if DEBUG
                _currentLogLevel = LogLevel.Debug;
            #else
                _currentLogLevel = LogLevel.Info;
            #endif
            
            // Initialize category filters (all enabled by default)
            foreach (LogCategory category in Enum.GetValues<LogCategory>())
            {
                _categoryFilters[category] = true;
            }
            
            LogInfo("GameLogger initialized", "Logger");
        }
        
        #region Core Logging Methods
        public void LogDebug(string message, string category = "Debug")
        {
            Log(LogLevel.Debug, message, category, LogCategory.Debug);
        }
        
        public void LogInfo(string message, string category = "Info")
        {
            Log(LogLevel.Info, message, category, LogCategory.Info);
        }
        
        public void LogWarning(string message, string category = "Warning")
        {
            Log(LogLevel.Warning, message, category, LogCategory.Warning);
        }
        
        public void LogError(string message, string category = "Error")
        {
            Log(LogLevel.Error, message, category, LogCategory.Error);
        }
        
        public void LogCritical(string message, string category = "Critical")
        {
            Log(LogLevel.Critical, message, category, LogCategory.Critical);
        }
        #endregion
        
        #region Specialized Game Logging Methods
        public void LogGameplay(string message, string context = "")
        {
            var fullMessage = string.IsNullOrEmpty(context) ? message : $"[{context}] {message}";
            Log(LogLevel.Info, fullMessage, "Gameplay", LogCategory.Gameplay);
        }
        
        public void LogSystem(string message, string system = "")
        {
            var fullMessage = string.IsNullOrEmpty(system) ? message : $"[{system}] {message}";
            Log(LogLevel.Info, fullMessage, "System", LogCategory.System);
        }
        
        public void LogPerformance(string message, double? executionTime = null)
        {
            var fullMessage = executionTime.HasValue 
                ? $"{message} - Execution Time: {executionTime.Value:F2}ms"
                : message;
            Log(LogLevel.Debug, fullMessage, "Performance", LogCategory.Performance);
        }
        
        public void LogReplay(string message, string replayContext = "")
        {
            var fullMessage = string.IsNullOrEmpty(replayContext) ? message : $"[{replayContext}] {message}";
            Log(LogLevel.Info, fullMessage, "Replay", LogCategory.Replay);
        }
        
        public void LogUI(string message, string uiComponent = "")
        {
            var fullMessage = string.IsNullOrEmpty(uiComponent) ? message : $"[{uiComponent}] {message}";
            Log(LogLevel.Info, fullMessage, "UI", LogCategory.UI);
        }
        
        public void LogAudio(string message, string audioContext = "")
        {
            var fullMessage = string.IsNullOrEmpty(audioContext) ? message : $"[{audioContext}] {message}";
            Log(LogLevel.Info, fullMessage, "Audio", LogCategory.Audio);
        }
        #endregion
        
        #region Performance Tracking
        public void StartPerformanceTimer(string timerName)
        {
            _performanceTimers[timerName] = DateTime.Now;
        }
        
        public void EndPerformanceTimer(string timerName, string operation = "")
        {
            if (_performanceTimers.TryGetValue(timerName, out DateTime startTime))
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var message = string.IsNullOrEmpty(operation) ? timerName : $"{operation} ({timerName})";
                LogPerformance(message, elapsed);
                _performanceTimers.Remove(timerName);
            }
            else
            {
                LogWarning($"Performance timer '{timerName}' was not started", "Performance");
            }
        }
        #endregion
        
        #region Configuration
        public void SetLogLevel(LogLevel level)
        {
            _currentLogLevel = level;
            LogInfo($"Log level changed to: {level}", "Logger");
        }
        
        public bool IsEnabled(LogLevel level)
        {
            return level >= _currentLogLevel;
        }
        
        public void SetCategoryFilter(LogCategory category, bool enabled)
        {
            _categoryFilters[category] = enabled;
            LogInfo($"Category filter '{category}' set to: {enabled}", "Logger");
        }
        
        public void EnableDebugMode()
        {
            SetLogLevel(LogLevel.Debug);
            LogInfo("Debug mode enabled - All logs will be shown", "Logger");
        }
        
        public void EnableReleaseMode()
        {
            SetLogLevel(LogLevel.Info);
            LogInfo("Release mode enabled - Debug logs hidden", "Logger");
        }
        #endregion
        
        #region Core Implementation
        private void Log(LogLevel level, string message, string category, LogCategory logCategory)
        {
            // Check if this log level and category should be processed
            if (!IsEnabled(level) || !_categoryFilters.GetValueOrDefault(logCategory, true))
                return;
            
            // Format the message with timestamp and category
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var levelStr = level.ToString().ToUpper();
            var formattedMessage = $"[{timestamp}] [{levelStr}] [{category}] {message}";
            
            // Output using appropriate Godot method based on level
            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    GD.Print(formattedMessage);
                    break;
                case LogLevel.Warning:
                    GD.PrintRich($"[color=yellow]{formattedMessage}[/color]");
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    GD.PrintErr(formattedMessage);
                    break;
            }
        }
        #endregion
        
        #region Cleanup
        public override void _ExitTree()
        {
            if (Instance == this)
            {
                LogInfo("GameLogger shutting down", "Logger");
                Instance = null;
            }
        }
        #endregion
    }
}
