# Marrie Game

A sophisticated 2D platformer built with **Godot 4.4** and **C#**, featuring an innovative death replay system and advanced enemy AI. The game follows SOLID principles and modular architecture for maintainable, extensible code.

## 🎮 Core Features

### **Death Replay System**
- **Record & Replay**: Every player movement and enemy behavior is recorded in real-time
- **Ghost Playback**: After death, interact with purple replay gems to watch your previous attempt as a ghost
- **Visual Feedback**: Original sprites become semi-transparent during replay
- **Smart Optimization**: Frame skipping for performance without losing important data

### **Advanced Enemy AI**
- **Follower Behavior**: Monsters spawn at safe distances and follow the player intelligently
- **State-Based AI**: Enemies cycle through states (Following, Hidden, InitialWait)
- **Dynamic Positioning**: Smart spawn logic keeps enemies challenging but fair
- **Visual Effects**: Smooth fade in/out transitions with alpha blending

### **Player Systems**
- **Health Management**: Visual heart-based health system with damage feedback
- **Mana System**: Energy management for special abilities
- **Responsive Controls**: Smooth movement with A/D keys and space for jumping
- **Visual Feedback**: Damage blinking, knockback effects, and directional sprites

### **UI & Polish**
- **Game Over Screen**: Clean restart interface
- **Real-time Health Display**: Heart-based UI that updates dynamically
- **Audio Integration**: Positional audio for footsteps and ambient sounds
- **Scene Management**: Seamless transitions between game states

## 🏗️ Architecture Overview

The game follows a **Manager Pattern** with **Event-Driven Architecture**, ensuring loose coupling and high maintainability.

### **Core Systems Architecture**

```
GameManager (Autoload)
├── SceneLifecycleManager
├── PlayerStateManager  
├── UIManager
├── ReplaySystemManager
└── EnemyManager
```

### **System Flow**

1. **Game Initialization**: GameManager orchestrates all manager initialization
2. **Event-Driven Communication**: Systems communicate through GameEventSystem
3. **Player Actions**: Input → Movement → Recording → UI Updates
4. **Enemy Behavior**: AI State Machine → Position Updates → Recording
5. **Death Handling**: Death Event → Replay Recording → Gem Creation → UI Transition
6. **Replay System**: Gem Interaction → Ghost Spawning → Frame Playback → Cleanup

## 📁 Project Structure

### **Scripts/Systems/** - Core Game Managers
- **`GameManager.cs`** - Main game orchestrator and singleton coordinator
- **`PlayerStateManager.cs`** - Handles player lifecycle and state management
- **`ReplaySystemManager.cs`** - Manages replay recording and death record storage
- **`UIManager.cs`** - Controls all user interface elements and transitions
- **`EnemyManager.cs`** - Coordinates enemy spawning and behavior
- **`SceneLifecycleManager.cs`** - Manages scene transitions and lifecycle events

### **Scripts/Player/** - Character Systems
- **`Player.cs`** - Main character controller with movement, damage, and visual systems
- **`PlayerLife.cs`** - Health management with invulnerability and death handling
- **`PlayerMana.cs`** - Mana/energy system for special abilities
- **`GhostPlayer.cs`** - Replay ghost that mimics recorded player movements

### **Scripts/Enemy/** - AI Systems
- **`Monster.cs`** - Unified enemy class supporting both simple and follower behaviors
  - Simple mode: Basic collision damage
  - Follower mode: Advanced AI with state machine, positioning, and visibility
- **`GhostMonster.cs`** - Replay ghost for enemy movements

### **Scripts/Replay/** - Replay System
- **`ReplayRecorder.cs`** - Records player and enemy movements with frame optimization
- **`ReplayFrame.cs`** - Data structure for storing position, animation, and state data
- **`DeathRecord.cs`** - Encapsulates complete death event with replay data
- **`ReplayOrchestrator.cs`** - Coordinates replay playback and ghost spawning

### **Scripts/Items/** - Interactive Objects
- **`ReplayGem.cs`** - Interactive gem that triggers replay playback
- **`Apple.cs`** - Consumable items for health/mana restoration

### **Scripts/Core/** - Foundation Systems
- **`Communication/GameEventSystem.cs`** - Centralized event messaging system
- **`Logging/`** - Comprehensive logging system with categories and performance tracking
- **`Interfaces/GameInterfaces.cs`** - Common interfaces (IPausable, IResettable)
- **`Factories/ReplayEntityFactory.cs`** - Factory pattern for creating replay entities
- **`Utils/`** - Utility classes and helper functions

### **Scripts/UI/** - User Interface
- **`GameOver.cs`** - Death screen with restart functionality
- **`LifeHearts.cs`** - Visual health display system
- **`ManaBar.cs`** - Mana visualization component

## 🔧 Key Classes Explained

### **GameManager** - The Orchestrator
**Responsibility**: Coordinates all game systems without handling specific logic
- Initializes managers in correct order
- Handles pause/resume game state
- Routes events between systems
- Manages global input (ESC for pause)

### **Player** - The Character Controller
**Responsibility**: Character movement, animations, and damage handling
- Physics-based movement with gravity and jumping
- Sprite direction management and animation control
- Damage system with visual feedback (blinking, knockback)
- Integration with health and mana systems
- Pausable for replay mode

### **ReplaySystemManager** - The Memory Keeper
**Responsibility**: Records, stores, and manages replay data
- Coordinates between ReplayRecorder and ReplayOrchestrator
- Manages death records with unique IDs and timestamps
- Creates replay gems at death positions
- Handles cleanup and state management

### **Monster** - The Intelligent Enemy
**Responsibility**: Dual-mode enemy with simple or advanced AI
- **Simple Mode**: Basic collision damage
- **Follower Mode**: Advanced features:
  - State machine: Following → Hidden → Following cycle
  - Smart positioning behind player at safe distances
  - Fade in/out effects with alpha blending
  - Audio feedback and visual direction updates
  - Replay-aware (can be disabled during playback)

### **ReplayRecorder** - The Performance Monitor
**Responsibility**: Efficient recording of game state
- Frame optimization with movement detection
- Caches character references for performance
- Records position, rotation, animation, and visual state
- Smart skipping to reduce memory usage without losing data

### **GameEventSystem** - The Communication Hub
**Responsibility**: Type-safe messaging between systems
- Decouples systems through event-driven architecture
- Provides strongly-typed events for different game systems
- Centralizes all inter-system communication
- Easy to extend with new event types

## 🎯 Design Patterns Used

### **Singleton Pattern**
- GameManager, PlayerStateManager, ReplaySystemManager
- Ensures single point of truth for each system

### **Observer Pattern**  
- GameEventSystem for loose coupling
- Event-driven communication between systems

### **State Machine Pattern**
- Monster AI states (Following, Hidden, InitialWait)
- Clear state transitions and behaviors

### **Factory Pattern**
- ReplayEntityFactory for creating replay ghosts
- Centralized creation logic

### **Manager Pattern**
- System-specific managers handle related functionality
- Clear separation of concerns

## 🚀 Getting Started

### **Requirements**
- Godot 4.4 with C# support
- .NET SDK for C# compilation

### **Controls**
- **A/D or Arrow Keys**: Move left/right
- **Space**: Jump
- **E**: Interact with replay gems
- **ESC**: Pause/Resume game

### **How to Play**
1. Navigate the forest environment as Marrie
2. Avoid or interact with monsters (depending on their behavior mode)
3. Collect apples to restore health and mana
4. When you die, a purple replay gem appears at your death location
5. After respawning, interact with the gem to watch your previous attempt
6. Use the replay to learn from mistakes and improve your strategy

## 🔮 Technical Highlights

### **Performance Optimizations**
- Frame skipping in replay recording (only records when significant movement occurs)
- Cached character references to avoid repeated node lookups
- Conditional debug logging for production builds
- Smart cleanup of replay data and visual effects

### **Modular Design**
- Each system can be extended independently
- Interface-based design for easy testing and mocking
- Clear API boundaries between systems
- Event-driven architecture prevents tight coupling

### **Visual Polish**
- Smooth sprite direction changes with scale manipulation
- Alpha blending for fade effects and ghost visibility
- Damage feedback with color tinting and knockback
- Responsive UI that reacts to game state changes

## 🔄 Future Enhancements

The modular architecture makes it easy to add:
- Multiple replay gems (storing multiple death attempts)
- Advanced enemy behaviors (new monster types)
- Power-ups and special abilities
- Level progression system
- Save/load functionality
- Multiplayer replay sharing

---

*Built with passion for clean code, smooth gameplay, and innovative mechanics.*