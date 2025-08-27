using Godot;

// Factory for creating replay-related entities
// Handles instantiation of replay gems, ghost players, and ghost monsters
public static class ReplayEntityFactory
{
    private static PackedScene _replayGemScene;
    private static PackedScene _ghostPlayerScene;
    private static PackedScene _ghostMonsterScene;

    #region Scene Loading
    private static PackedScene GetReplayGemScene()
    {
        if (_replayGemScene == null)
        {
            string path = "res://Prefabs/ReplayGem.tscn";

            try
            {
                if (ResourceLoader.Exists(path))
                {
                    _replayGemScene = GD.Load<PackedScene>(path);
                    if (_replayGemScene != null)
                    {
                        Log.Debug($"ReplayEntityFactory: Loaded ReplayGem scene from {path}", "ReplayEntityFactory");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"ReplayEntityFactory: Failed to load scene from {path}: {ex.Message}", "ReplayEntityFactory");
            }

            if (_replayGemScene == null)
            {
                Log.Error("ReplayEntityFactory: Could not find ReplayGem.tscn in any expected location", "ReplayEntityFactory");
            }
        }
        return _replayGemScene;
    }

    private static PackedScene GetGhostPlayerScene()
    {
        if (_ghostPlayerScene == null)
        {
            try
            {
                if (ResourceLoader.Exists("res://Prefabs/GhostPlayer.tscn"))
                {
                    _ghostPlayerScene = GD.Load<PackedScene>("res://Prefabs/GhostPlayer.tscn");
                    if (_ghostPlayerScene != null)
                    {
#if DEBUG
                        Log.Debug("ReplayEntityFactory: Loaded GhostPlayer scene", "ReplayEntityFactory");
#endif
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"ReplayEntityFactory: Failed to load GhostPlayer scene: {ex.Message}", "ReplayEntityFactory");
            }
        }
        return _ghostPlayerScene;
    }

    private static PackedScene GetGhostMonsterScene()
    {
        if (_ghostMonsterScene == null)
        {
            try
            {
                if (ResourceLoader.Exists("res://Prefabs/GhostMonster.tscn"))
                {
                    _ghostMonsterScene = GD.Load<PackedScene>("res://Prefabs/GhostMonster.tscn");
                    if (_ghostMonsterScene != null)
                    {
#if DEBUG
                        Log.Debug("ReplayEntityFactory: Loaded GhostMonster scene", "ReplayEntityFactory");
#endif
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"ReplayEntityFactory: Failed to load GhostMonster scene: {ex.Message}", "ReplayEntityFactory");
            }
        }
        return _ghostMonsterScene;
    }
    #endregion

    #region Entity Creation
    public static ReplayGem CreateReplayGem(Vector2 position, Node parent)
    {
        var scene = GetReplayGemScene();
        if (scene == null)
        {
            Log.Error("ReplayEntityFactory: CRITICAL - Could not load ReplayGem scene!", "ReplayEntityFactory");
            return null;
        }

        var gem = scene.Instantiate() as ReplayGem;
        if (gem == null)
        {
            Log.Error("ReplayEntityFactory: CRITICAL - ReplayGem scene did not instantiate properly!", "ReplayEntityFactory");
            return null;
        }

        // Set position before adding to tree
        gem.GlobalPosition = position;
#if DEBUG
        Log.Debug($"ReplayEntityFactory: Set gem GlobalPosition to {position}", "ReplayEntityFactory");
#endif

        // Use call_deferred to avoid physics flush conflicts
        parent.CallDeferred(Node.MethodName.AddChild, gem);

        // Show the gem after it's added to the tree
        gem.CallDeferred(nameof(ReplayGem.ShowGem), position);

        // Fire event immediately since we just want to notify
        GameEventSystem.OnReplayGemCreated(position);
#if DEBUG
        Log.Debug($"ReplayEntityFactory: Created ReplayGem at {position} - showing deferred", "ReplayEntityFactory");
#endif

        return gem;
    }

    public static GhostPlayer CreateGhostPlayer(Vector2 position, Node parent)
    {
        var scene = GetGhostPlayerScene();
        GhostPlayer ghost = null;

        if (scene != null)
        {
            ghost = scene.Instantiate() as GhostPlayer;
        }

        // Fallback to programmatic creation
        if (ghost == null)
        {
            ghost = CreateGhostPlayerProgrammatically();
        }

        // ONLY copy sprites if we had to fall back to programmatic creation
        if (ghost != null)
        {
            // Only copy sprites if we created the ghost programmatically (scene loading failed)
            if (scene == null)
            {
                var originalPlayer = parent.GetTree().GetFirstNodeInGroup("player") as Player;
                if (originalPlayer != null)
                {
                    CopyPlayerSprite(originalPlayer, ghost);
                }
            }

            // Add ghost to scene first
            parent.CallDeferred(Node.MethodName.AddChild, ghost);

            // Use a timer to call setup after the ghost is properly added
            var timer = parent.GetTree().CreateTimer(0.01f);
            timer.Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(ghost))
                {
                    ghost._on_ghost_setup_complete(position);
                }
            };
        }

        return ghost;
    }

    public static GhostMonster CreateGhostMonster(Vector2 position, Node parent)
    {
        var scene = GetGhostMonsterScene();
        GhostMonster ghost = null;
        Node nodeToAdd = null;

        if (scene != null)
        {
            // When loading from scene, the structure is:
            // Root (Node2D) -> CharacterBody2D (with GhostMonster script)
            var rootNode = scene.Instantiate();
            var characterBody = rootNode.GetNodeOrNull<CharacterBody2D>("CharacterBody2D");

            if (characterBody is GhostMonster ghostMonster)
            {
                ghost = ghostMonster;
                nodeToAdd = rootNode; // Add the root node to maintain hierarchy
                Log.Debug("ReplayEntityFactory: GhostMonster loaded from scene with proper hierarchy", "ReplayEntityFactory");
            }
            else
            {
                Log.Warning("ReplayEntityFactory: Scene loaded but CharacterBody2D is not a GhostMonster", "ReplayEntityFactory");
                rootNode.QueueFree(); // Clean up failed instantiation
            }
        }

        // Fallback to programmatic creation
        if (ghost == null)
        {
            var ghostContainer = CreateGhostMonsterProgrammatically();
            ghost = ghostContainer; // In programmatic creation, we return the CharacterBody2D directly
            nodeToAdd = ghost; // Add the CharacterBody2D directly for programmatic creation

            // Copy sprites only for programmatic creation
            var originalMonster = parent.GetTree().GetFirstNodeInGroup("enemies") as Monster;
            if (originalMonster != null)
            {
                CopyMonsterSprite(originalMonster, ghost);
            }
        }

        // Add the appropriate node to parent
        if (nodeToAdd != null)
        {
            parent.CallDeferred(Node.MethodName.AddChild, nodeToAdd);

            // Use a timer to call setup after the ghost is properly added
            var timer = parent.GetTree().CreateTimer(0.01f);
            timer.Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(ghost))
                {
                    ghost._on_ghost_setup_complete(position);
                }
            };
        }

        return ghost;
    }
    #endregion

    #region Programmatic Creation Fallbacks
    private static GhostPlayer CreateGhostPlayerProgrammatically()
    {
        var ghost = new GhostPlayer { Name = "GhostPlayer" };

        // Note: Sprite copying is now done in the main CreateGhostPlayer method
        return ghost;
    }

    private static GhostMonster CreateGhostMonsterProgrammatically()
    {
        // Create the GhostMonster directly as CharacterBody2D for programmatic fallback
        // This is simpler than trying to recreate the full .tscn hierarchy programmatically
        var ghost = new GhostMonster { Name = "GhostMonster" };

        // Create collision shape
        var collisionShape = new CollisionShape2D
        {
            Name = "CollisionShape2D",
            Position = new Vector2(10, -20)
        };

        var shape = new RectangleShape2D();
        shape.Size = new Vector2(302, 376);
        collisionShape.Shape = shape;
        ghost.AddChild(collisionShape);

        // Create basic sprite (will be replaced by CopyMonsterSprite if original monster found)
        var sprite = new Sprite2D
        {
            Name = "GhostMonsterSprite",
            Position = new Vector2(12.5f, 9.0f),
            Scale = new Vector2(0.31543f, 0.317708f)
        };
        ghost.AddChild(sprite);

        // Create MonsterLife component (Node, not Node2D, so no Position property)
        var monsterLife = new MonsterLife
        {
            Name = "GhostMonsterLife"
        };
        ghost.AddChild(monsterLife);

        // Note: MonsterLifeBar will be created dynamically in SetupLifeSystem
        // since the programmatic creation doesn't include the complex UI prefab

        Log.Debug("ReplayEntityFactory: Created programmatic GhostMonster as direct CharacterBody2D", "ReplayEntityFactory");

        return ghost;
    }
    private static void CopyPlayerSprite(Player original, GhostPlayer ghost)
    {
        var originalSprite = original.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (originalSprite != null)
        {
            var ghostSprite = new AnimatedSprite2D
            {
                Name = "AnimatedSprite2D",
                SpriteFrames = originalSprite.SpriteFrames,
                Animation = "idle",
                Scale = originalSprite.Scale,
                Position = originalSprite.Position,
                Rotation = originalSprite.Rotation,
                FlipH = originalSprite.FlipH,
                FlipV = originalSprite.FlipV,
                Offset = originalSprite.Offset
            };
            ghost.AddChild(ghostSprite);
        }
    }

    private static void CopyMonsterSprite(Monster original, GhostMonster ghost)
    {
        var sprite2D = original.GetNodeOrNull<Sprite2D>("MonsterSprite");
        var animSprite = original.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");

        if (sprite2D != null)
        {
            var ghostSprite = new Sprite2D
            {
                Name = "MonsterSprite",
                Texture = sprite2D.Texture,
                Scale = sprite2D.Scale,
                Position = sprite2D.Position,
                Rotation = sprite2D.Rotation,
                FlipH = sprite2D.FlipH,
                FlipV = sprite2D.FlipV,
                Offset = sprite2D.Offset
            };
            ghost.AddChild(ghostSprite);
        }
        else if (animSprite != null)
        {
            var ghostSprite = new AnimatedSprite2D
            {
                Name = "AnimatedSprite2D",
                SpriteFrames = animSprite.SpriteFrames,
                Animation = "idle",
                Scale = animSprite.Scale,
                Position = animSprite.Position,
                Rotation = animSprite.Rotation,
                FlipH = animSprite.FlipH,
                FlipV = animSprite.FlipV,
                Offset = animSprite.Offset
            };
            ghost.AddChild(ghostSprite);
        }
    }
    #endregion
}
