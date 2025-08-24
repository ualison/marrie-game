# Marrie Game - Logging System Documentation

## Overview

This project implements a comprehensive logging system designed specifically for game development using the **Observer Pattern** with **Strategy Pattern** for different log outputs. The system provides structured, categorized logging that helps with debugging, performance monitoring, and understanding game flow.

## Architecture

### Core Components

1. **ILogger Interface** - Defines the contract for logging operations
2. **GameLogger** - Main implementation with Godot integration  
3. **Log Static Facade** - Simplified API for easy access throughout the codebase

### Design Patterns Used

- **Singleton Pattern**: GameLogger instance for global access
- **Facade Pattern**: Log static class for simplified API
- **Strategy Pattern**: Different log levels and categories
- **Observer Pattern**: Event-driven logging system

## Usage Examples

### Basic Logging
```csharp
// Standard log levels
Log.Debug("Detailed debugging information");
Log.Info("General information about game state");
Log.Warning("Something unexpected but recoverable");
Log.Error("Error occurred but game can continue");
Log.Critical("Serious error that may crash the game");
```

### Game-Specific Logging
```csharp
// Gameplay events
Log.Gameplay("Player collected gem", "ItemCollection");
Log.Gameplay("Enemy spawned at position", "EnemySystem");

// System operations
Log.System("Manager initialized", "GameManager");
Log.System("Scene transition started", "SceneManager");

// Performance tracking
Log.Performance("Frame rendering took 16.7ms");
Log.StartTimer("LoadLevel");
// ... operations ...
Log.EndTimer("LoadLevel", "Level loading");

// Replay system
Log.Replay("Recording started", "ReplayRecorder");
Log.Replay("Playback initiated", "ReplayPlayer");

// UI operations
Log.UI("Menu opened", "MainMenu");
Log.UI("Button clicked", "OptionsPanel");

// Audio operations
Log.Audio("Music started", "BackgroundMusic");
Log.Audio("Sound effect played", "PlayerActions");
```

### Performance Monitoring
```csharp
// Manual timing
Log.StartTimer("ComplexOperation");
// ... perform operation ...
Log.EndTimer("ComplexOperation", "Processing player input");

// Automatic timing with context
Log.Performance("AI pathfinding completed", 12.5); // 12.5ms
```

## Log Categories

| Category | Purpose | When to Use |
|----------|---------|-------------|
| **Debug** | Detailed debugging info | Development only, verbose details |
| **Info** | General information | Important state changes |
| **Warning** | Unexpected but recoverable | Missing optional resources |
| **Error** | Errors that don't crash | Failed operations with fallbacks |
| **Critical** | Serious errors | Potential crashes, data corruption |
| **Gameplay** | Game mechanics | Player actions, game state changes |
| **System** | System operations | Manager initialization, scene changes |
| **Performance** | Performance metrics | Timing, frame rates, memory usage |
| **Replay** | Replay system | Recording, playback operations |
| **UI** | User interface | Menu interactions, HUD updates |
| **Audio** | Audio system | Music, sound effects, audio state |

## Best Practices

### 1. Use Appropriate Log Levels
```csharp
// ✅ Good - Use Debug for detailed info
Log.Debug($"Processing {enemies.Count} enemies this frame", "EnemySystem");

// ✅ Good - Use Info for important state changes
Log.Info("Game started", "GameManager");

// ❌ Bad - Don't use Error for normal operations
Log.Error("Player moved to new position"); // This is normal!
```

### 2. Provide Context
```csharp
// ✅ Good - Includes context and values
Log.Gameplay($"Player health changed: {oldHealth} -> {newHealth}", "HealthSystem");

// ❌ Bad - Vague and unhelpful
Log.Info("Something changed");
```

### 3. Use Categories Consistently
```csharp
// ✅ Good - Consistent category usage
Log.System("PlayerManager initialized", "PlayerManager");
Log.System("EnemyManager initialized", "EnemyManager");

// ❌ Bad - Inconsistent categories
Log.Info("PlayerManager ready");
Log.Debug("EnemyManager setup complete");
```

### 4. Performance Logging
```csharp
// ✅ Good - Time expensive operations
Log.StartTimer("LoadLevel");
LoadLevelAssets();
Log.EndTimer("LoadLevel", "Asset loading");

// ✅ Good - Log performance metrics
if (frameTime > 33.3) // More than 30 FPS
{
    Log.Performance($"Frame took {frameTime}ms - potential lag", frameTime);
}
```

### 5. Error Handling
```csharp
// ✅ Good - Log errors with context
try
{
    LoadPlayerSave();
}
catch (System.Exception ex)
{
    Log.Error($"Failed to load player save: {ex.Message}", "SaveSystem");
    // Handle gracefully
}
```

## Configuration

### Debug vs Release Builds
```csharp
// Automatically configured based on build type
#if DEBUG
    Log.EnableDebugMode(); // Shows all logs
#else
    Log.EnableReleaseMode(); // Hides debug logs
#endif
```

### Manual Configuration
```csharp
// Change log level at runtime
Log.SetLogLevel(LogLevel.Warning); // Only warnings and above

// Enable specific categories
if (GameLogger.Instance != null)
{
    GameLogger.Instance.SetCategoryFilter(LogCategory.Performance, true);
    GameLogger.Instance.SetCategoryFilter(LogCategory.Debug, false);
}
```

## Integration with Existing Code

### Replacing GD.Print Calls
```csharp
// Old code
GD.Print("GameManager: Game started");

// New code
Log.System("Game started", "GameManager");
```

### Manager Classes Pattern
```csharp
public partial class MyManager : Node
{
    public override void _Ready()
    {
        Log.System("MyManager initialized", "MyManager");
        
        Log.StartTimer("ManagerInit");
        InitializeComponents();
        Log.EndTimer("ManagerInit", "Component initialization");
    }
    
    private void OnPlayerAction()
    {
        Log.Gameplay("Player performed action", "MyManager");
    }
    
    public override void _ExitTree()
    {
        Log.System("MyManager cleaned up", "MyManager");
    }
}
```

## Output Format

Logs are formatted as:
```
[HH:mm:ss.fff] [LEVEL] [Category] Message
```

Example:
```
[14:23:15.123] [INFO] [Gameplay] Player collected gem
[14:23:15.124] [DEBUG] [Performance] Frame rendering - Execution Time: 16.67ms
[14:23:15.125] [WARNING] [System] Optional component not found
```

## Performance Considerations

1. **Conditional Compilation**: Debug logs are removed in release builds
2. **Level Filtering**: Only logs at or above the current level are processed
3. **Category Filtering**: Individual categories can be disabled
4. **String Interpolation**: Use string interpolation for better performance

```csharp
// ✅ Good - String interpolation
Log.Debug($"Processing {count} items");

// ❌ Slower - String concatenation
Log.Debug("Processing " + count + " items");
```

## Testing and Debugging

### Debug Methods
```csharp
// Log system status for debugging
if (GameLogger.Instance != null)
{
    GameLogger.Instance.LogStatus();
}

// Log manager status
PlayerStateManager.Instance?.LogPlayerStatus();
ReplaySystemManager.Instance?.LogReplaySystemStatus();
```

### Common Debugging Scenarios

1. **Component Discovery Issues**:
   ```csharp
   Log.Debug($"Found {components.Count} components", "ComponentSystem");
   foreach (var component in components)
   {
       Log.Debug($"  - {component.Name} ({component.GetType().Name})", "ComponentSystem");
   }
   ```

2. **Event Flow Tracking**:
   ```csharp
   Log.Gameplay("Player died event fired", "EventSystem");
   Log.System("Death event received", "PlayerManager");
   Log.Replay("Recording death data", "ReplaySystem");
   ```

3. **Performance Bottlenecks**:
   ```csharp
   Log.StartTimer("ExpensiveOperation");
   // ... operation ...
   Log.EndTimer("ExpensiveOperation", "Critical path timing");
   ```

## Migration Guide

1. **Add using statement** to files:
   ```csharp
   // Add this to files that will use logging
   using MarrieGame.Core.Logging; // Only needed for advanced features
   // Log static class is globally available
   ```

2. **Replace existing logging**:
   ```csharp
   // Replace this:
   GD.Print("Manager initialized");
   
   // With this:
   Log.System("Manager initialized", "ManagerName");
   ```

3. **Add performance tracking**:
   ```csharp
   // Add timing to expensive operations
   Log.StartTimer("LoadAssets");
   LoadGameAssets();
   Log.EndTimer("LoadAssets", "Asset loading");
   ```

4. **Categorize logs appropriately**:
   ```csharp
   // Use specific categories for better organization
   Log.Gameplay("Player action", "PlayerController");
   Log.UI("Menu opened", "MainMenu");
   Log.Audio("Music started", "AudioManager");
   ```

This logging system provides a robust foundation for debugging and monitoring your game while maintaining clean, organized code that follows best practices for game development.
