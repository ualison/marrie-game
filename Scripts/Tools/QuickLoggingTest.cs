using Godot;

/// <summary>
/// Quick logging test - attach this to any node in your scene to see the new logging in action
/// This will demonstrate all the different log levels and categories
/// </summary>
public partial class QuickLoggingTest : Node
{
    public override void _Ready()
    {
        // Wait a moment for the logging system to initialize
        GetTree().CreateTimer(0.5f).Timeout += RunTest;
    }
    
    private void RunTest()
    {
        // Test if logging system is available
        if (MarrieGame.Core.Logging.GameLogger.Instance == null)
        {
            GD.Print("Creating GameLogger for test...");
            var logger = new MarrieGame.Core.Logging.GameLogger();
            GetTree().Root.AddChild(logger);
        }
        
        GD.Print("=== NEW LOGGING SYSTEM TEST ===");
        
        // Test all log levels
        Log.Debug("Debug message - only shows in debug builds", "Test");
        Log.Info("Info message - general information", "Test");
        Log.Warning("Warning message - something unexpected", "Test");
        Log.Error("Error message - something went wrong", "Test");
        
        // Test game-specific categories
        Log.Gameplay("Player jumped over obstacle", "Test");
        Log.System("Component initialized successfully", "Test");
        Log.Audio("Background music started", "Test");
        Log.UI("Menu button pressed", "Test");
        Log.Replay("Recording frame captured", "Test");
        
        // Test performance timing
        Log.StartTimer("TestOperation");
        
        // Simulate some work
        for (int i = 0; i < 100; i++)
        {
            float dummy = Mathf.Sin(i) * Mathf.Cos(i);
        }
        
        Log.EndTimer("TestOperation", "Quick computation test");
        
        // Direct performance logging
        Log.Performance("Test frame rendered", 16.7);
        
        GD.Print("=== TEST COMPLETE - Check output above for new formatted logs ===");
        
        // Clean up
        QueueFree();
    }
}
