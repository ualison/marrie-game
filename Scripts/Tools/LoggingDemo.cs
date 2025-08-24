using Godot;
using MarrieGame.Core.Logging;

/// <summary>
/// Simple test script to demonstrate the logging system
/// Attach this to any node to see logging in action
/// </summary>
public partial class LoggingDemo : Node
{
    public override void _Ready()
    {
        // Initialize the logging system if not already done
        if (MarrieGame.Core.Logging.GameLogger.Instance == null)
        {
            var logger = new MarrieGame.Core.Logging.GameLogger();
            GetTree().Root.AddChild(logger);
        }
        
        // Wait a bit then run the demo
        GetTree().CreateTimer(1.0f).Timeout += RunLoggingDemo;
    }
    
    private void RunLoggingDemo()
    {
        Log.Info("=== Logging System Demo ===", "LoggingDemo");
        
        // Demonstrate different log levels
        Log.Debug("This is debug information - only shows in debug builds", "LoggingDemo");
        Log.Info("This is general information", "LoggingDemo");
        Log.Warning("This is a warning message", "LoggingDemo");
        Log.Error("This is an error message", "LoggingDemo");
        
        // Demonstrate game-specific logging
        Log.Gameplay("Player performed action", "LoggingDemo");
        Log.System("System component initialized", "LoggingDemo");
        Log.Audio("Sound effect played", "LoggingDemo");
        Log.UI("Menu button clicked", "LoggingDemo");
        Log.Replay("Replay event occurred", "LoggingDemo");
        
        // Demonstrate performance timing
        Log.StartTimer("DemoOperation");
        
        // Simulate some work
        for (int i = 0; i < 1000; i++)
        {
            var dummy = Mathf.Sin(i) * Mathf.Cos(i);
        }
        
        Log.EndTimer("DemoOperation", "Demo computation");
        
        // Direct performance logging
        Log.Performance("Frame rendered successfully", 16.7);
        
        Log.Info("=== Demo Complete ===", "LoggingDemo");
        
        // Clean up
        QueueFree();
    }
}
