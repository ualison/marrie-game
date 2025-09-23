# Catapult Testing Guide

## Quick Test
1. **Open Test Scene**: Use `Scenes/CatapultTest.tscn` for a simple test setup
2. **Or add to existing scene**: Drag `Prefabs/Catapult.tscn` into your scene

## How to Test the Catapult

### Setup
1. **Add Catapult to Scene**: Drag the `Catapult.tscn` prefab into your test scene
2. **Position Catapult**: Place it on ground level where the player can reach it
3. **Ensure Player is Present**: Make sure your scene has a Player character

### Controls
- **C Key**: Hold to push the catapult (mapped to "push_catapult" input action)
- **Arrow Keys/WASD**: Move the player to approach the catapult

### Visual Feedback
- **Green Tint**: Player is in range of the catapult (can interact)
- **Yellow Tint**: Player is actively pushing the catapult
- **Normal Color**: No interaction

### Expected Behavior
1. **Player approaches catapult** → Catapult turns green (detection range)
2. **Player holds C key** → Catapult turns yellow, starts moving in push direction
3. **Wheels rotate** → Based on movement direction and speed
4. **Player releases C** → Catapult stops being pushed, returns to green if still in range
5. **Player moves away** → Catapult returns to normal color
6. **When NOT pressing C** → Player is gently pushed away from catapult collision

### Debugging
Check the Godot console for debug messages:
- "Catapult initialized at position..." - confirms catapult is working
- "Player entered catapult detection area" - confirms player detection
- "Player started/stopped pushing catapult" - confirms input handling

### Troubleshooting

#### No "Catapult initialized" message in console
- **Scene Setup**: Make sure the Catapult.tscn is actually added to the scene
- **Script Assignment**: Verify the Catapult node has the Catapult.cs script attached
- **Build**: Run `dotnet build` to ensure scripts are compiled

#### Player Not Detected (No green tint)
- **Player Group**: Verify player is in "player" group (check Player script _Ready method)
- **Detection Area**: Ensure DetectionArea exists in catapult scene
- **Collision Layers**: Player should be on layer 2, DetectionArea should detect layer 2
- **Distance**: Player must be within ~250 units of catapult center

#### Input Not Working (No yellow tint when pressing C)
- **Input Map**: Verify "push_catapult" action exists in Project Settings > Input Map
- **Key Mapping**: Ensure C key (physical keycode 67) is mapped to "push_catapult"
- **Player in Range**: Must see green tint first before C key will work

#### Catapult Doesn't Move
- **Ground Setup**: Ensure catapult is on a floor (StaticBody2D with collision)
- **RigidBody Setup**: Catapult should be RigidBody2D, not frozen
- **Push Force**: Try increasing PushForce in inspector (default: 300)

#### Wheels Don't Rotate
- **Node Names**: Verify WheelLeft and WheelRight sprites exist
- **Movement**: Wheels only rotate when catapult is actually moving
- **Speed Threshold**: Movement must be > 0.1 units/frame

### Scene Structure
The Catapult.tscn should have:
```
Catapult (RigidBody2D) - with Catapult.cs script
├── CollisionShape2D - for physics collision
├── Skeleton (Sprite2D)
├── WheelLeft (Sprite2D) 
├── WheelRight (Sprite2D)
├── Spoon (Sprite2D)
├── Handle (Sprite2D)
└── DetectionArea (Area2D)
    └── DetectionShape (CollisionShape2D)
```

### Tunable Parameters
In the Catapult inspector, you can adjust:
- **PushForce**: How strong the push is (default: 300)
- **WheelRotationSpeed**: How fast wheels rotate (default: 200)
- **WheelLeftPath/WheelRightPath**: Paths to wheel sprites
- **DetectionAreaPath**: Path to detection area node