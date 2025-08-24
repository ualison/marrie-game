# Logging Integration Examples

This file shows how to integrate the logging system into your existing files with minimal changes.

## GameManager Integration (Already Applied)

The GameManager has been updated with the logging system. Here's what was changed:

```csharp
// Added using statement
using MarrieGame.Core.Logging;

// Added logger initialization in _Ready()
private void InitializeLoggingSystem()
{
    if (GameLogger.Instance == null)
    {
        var logger = new GameLogger();
        GetTree().Root.AddChild(logger);
        Log.System("Logging system initialized", "GameManager");
    }
}

// Replaced GD.Print calls with appropriate Log methods
Log.System("Game orchestration active", "GameManager");
Log.Gameplay("Game paused", "GameManager");
```

## How to Update Your Existing Files

### 1. AudioManager.cs - Simple Find & Replace

Replace these lines:
```csharp
// Old:
GD.Print("AudioManager initialized");
// New:
Log.System("AudioManager initialized", "AudioManager");

// Old:
GD.Print("Background music started");
// New:
Log.Audio("Background music started", "BackgroundMusic");

// Old:
GD.PrintErr("No background music assigned to AudioManager");
// New:
Log.Error("No background music assigned to AudioManager", "AudioManager");
```

### 2. PlayerStateManager.cs - Gameplay Logging

Replace these lines:
```csharp
// Old:
GD.Print($"PlayerStateManager: Player found: {_player != null}");
// New:
Log.System($"Player found: {_player != null}", "PlayerStateManager");

// Old:
GD.Print("PlayerStateManager: Player health depleted");
// New:
Log.Gameplay("Player health depleted", "PlayerStateManager");
```

### 3. ReplaySystemManager.cs - Replay Logging

Replace these lines:
```csharp
// Old:
GD.Print("ReplaySystemManager: Started recording");
// New:
Log.Replay("Started recording", "ReplaySystemManager");

// Old:
GD.Print($"ReplaySystemManager: Player died at {deathPosition}");
// New:
Log.Replay($"Player died at {deathPosition}", "ReplaySystemManager");
```

### 4. UIManager.cs - UI Logging

Replace these lines:
```csharp
// Old:
GD.Print("UIManager: Showing GameOverUI");
// New:
Log.UI("Showing GameOverUI", "UIManager");

// Old:
GD.PrintErr("UIManager: Cannot show game over - GameOverUI not found!");
// New:
Log.Error("Cannot show game over - GameOverUI not found!", "UIManager");
```

## Performance Timing Examples

Add performance tracking to expensive operations:

```csharp
// In any manager initialization:
Log.StartTimer("ComponentInit");
InitializeComponents();
Log.EndTimer("ComponentInit", "Component initialization");

// In asset loading:
Log.StartTimer("LoadAssets");
LoadGameAssets();
Log.EndTimer("LoadAssets", "Asset loading");
```

## Debug Methods

Add debug status methods to your managers:

```csharp
public void LogManagerStatus()
{
    Log.Debug("=== MyManager Status ===", "MyManager");
    Log.Debug($"Component Count: {components.Count}", "MyManager");
    Log.Debug($"Is Initialized: {isInitialized}", "MyManager");
}
```

## Error Handling Pattern

Wrap risky operations with proper logging:

```csharp
try
{
    RiskyOperation();
}
catch (System.Exception ex)
{
    Log.Error($"Operation failed: {ex.Message}", "MyManager");
    HandleGracefully();
}
```

## Quick Migration Steps

1. **Start the logging system** (already done in GameManager)
2. **Replace GD.Print calls** with appropriate Log.* methods
3. **Add performance timers** to expensive operations
4. **Use proper error logging** instead of GD.PrintErr
5. **Add debug status methods** for troubleshooting

## Key Benefits

- **Categorized logs** make debugging easier
- **Performance tracking** helps identify bottlenecks
- **Build-aware logging** removes debug output in release
- **Structured format** with timestamps and categories
- **Zero performance overhead** in release builds
