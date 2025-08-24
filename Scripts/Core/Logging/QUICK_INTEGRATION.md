# Quick Integration Guide

## Step 1: Initialize the Logging System

Add this to your `GameManager._Ready()` method before initializing other managers:

```csharp
private void InitializeLoggingSystem()
{
    // Create and add the logger to the scene tree if it doesn't exist
    if (GameLogger.Instance == null)
    {
        var logger = new GameLogger();
        GetTree().Root.AddChild(logger);
        Log.System("Logging system initialized", "GameManager");
    }
}
```

## Step 2: Replace Existing GD.Print Calls

### Quick Find & Replace Examples:

**AudioManager.cs**:
```csharp
// Replace:
GD.Print("AudioManager initialized");
// With:
Log.System("AudioManager initialized", "AudioManager");

// Replace:
GD.Print("Background music started");
// With:
Log.Audio("Background music started", "BackgroundMusic");
```

**PlayerStateManager.cs**:
```csharp
// Replace:
GD.Print($"PlayerStateManager: Player found: {_player != null}");
// With:
Log.System($"Player found: {_player != null}", "PlayerStateManager");

// Replace:
GD.Print("PlayerStateManager: Player health depleted");
// With:
Log.Gameplay("Player health depleted", "PlayerStateManager");
```

**ReplaySystemManager.cs**:
```csharp
// Replace:
GD.Print("ReplaySystemManager: Started recording");
// With:
Log.Replay("Started recording", "ReplaySystemManager");

// Replace:
GD.Print($"ReplaySystemManager: Player died at {deathPosition}");
// With:
Log.Replay($"Player died at {deathPosition}", "ReplaySystemManager");
```

## Step 3: Add Performance Tracking

Add timing to expensive operations:

```csharp
// Before:
InitializeManagers();

// After:
Log.StartTimer("ManagerInitialization");
InitializeManagers();
Log.EndTimer("ManagerInitialization", "All managers initialized");
```

## Step 4: Add the Global Using (Optional)

If you want to use advanced features, add this to files that need it:
```csharp
using MarrieGame.Core.Logging;
```

## Step 5: Update Error Handling

```csharp
// Replace:
GD.PrintErr("ReplaySystemManager: Cannot start replay - no recorded data!");
// With:
Log.Error("Cannot start replay - no recorded data!", "ReplaySystemManager");
```

## Quick Reference

| Old Code | New Code | Category |
|----------|----------|----------|
| `GD.Print("Manager initialized")` | `Log.System("Manager initialized", "ManagerName")` | System |
| `GD.Print("Player action")` | `Log.Gameplay("Player action", "Context")` | Gameplay |
| `GD.Print("Music started")` | `Log.Audio("Music started", "BackgroundMusic")` | Audio |
| `GD.Print("UI opened")` | `Log.UI("UI opened", "MenuName")` | UI |
| `GD.Print("Recording data")` | `Log.Replay("Recording data", "ReplaySystem")` | Replay |
| `GD.PrintErr("Error occurred")` | `Log.Error("Error occurred", "SystemName")` | Error |

## Log Level Usage

- **Debug**: `Log.Debug("Detailed info", "Component")` - Development only
- **Info**: `Log.Info("Important info", "Component")` - General information
- **Warning**: `Log.Warning("Unexpected", "Component")` - Recoverable issues
- **Error**: `Log.Error("Failed operation", "Component")` - Errors with fallbacks
- **Critical**: `Log.Critical("Serious error", "Component")` - Potential crashes

## Performance Monitoring

```csharp
// Time operations
Log.StartTimer("OperationName");
// ... do work ...
Log.EndTimer("OperationName", "Operation description");

// Or report performance directly
Log.Performance("Operation completed in 15.2ms", 15.2);
```
