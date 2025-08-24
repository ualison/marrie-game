using Godot;

/// <summary>
/// Factory for creating replay-related entities
/// Handles instantiation of replay gems, ghost players, and ghost monsters
/// </summary>
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
            string[] possiblePaths = {
                "res://Prefabs/ReplayGem.tscn",
                "res://prefabs/ReplayGem.tscn", 
                "res://Scenes/ReplayGem.tscn",
                "res://scenes/ReplayGem.tscn"
            };
            
            foreach (string path in possiblePaths)
            {
                try
                {
                    if (ResourceLoader.Exists(path))
                    {
                        _replayGemScene = GD.Load<PackedScene>(path);
                        if (_replayGemScene != null)
                        {
#if DEBUG
                            Log.Debug($"ReplayEntityFactory: Loaded ReplayGem scene from {path}", "ReplayEntityFactory");
#endif
                            break;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error($"ReplayEntityFactory: Failed to load scene from {path}: {ex.Message}", "ReplayEntityFactory");
                }
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
            ghost = CreateGhostPlayerProgrammatically(parent);
        }
        
        if (ghost != null)
        {
            parent.CallDeferred(Node.MethodName.AddChild, ghost);
            ghost.GlobalPosition = position;
            ghost.AddToGroup("ghost_player");
        }
        
        return ghost;
    }
    
    public static GhostMonster CreateGhostMonster(Vector2 position, Node parent)
    {
        var scene = GetGhostMonsterScene();
        GhostMonster ghost = null;
        
        if (scene != null)
        {
            ghost = scene.Instantiate() as GhostMonster;
        }
        
        // Fallback to programmatic creation
        if (ghost == null)
        {
            ghost = CreateGhostMonsterProgrammatically(parent);
        }
        
        if (ghost != null)
        {
            parent.CallDeferred(Node.MethodName.AddChild, ghost);
            ghost.GlobalPosition = position;
            ghost.AddToGroup("ghost_monster");
        }
        
        return ghost;
    }
    #endregion
    
    #region Programmatic Creation Fallbacks
    private static GhostPlayer CreateGhostPlayerProgrammatically(Node parent)
    {
        var ghost = new GhostPlayer { Name = "GhostPlayer" };
        
        // Copy sprite from original player
        var originalPlayer = parent.GetTree().GetFirstNodeInGroup("player") as Player;
        if (originalPlayer != null)
        {
            CopyPlayerSprite(originalPlayer, ghost);
        }
        
        return ghost;
    }
    
    private static GhostMonster CreateGhostMonsterProgrammatically(Node parent)
    {
        var ghost = new GhostMonster { Name = "GhostMonster" };
        
        // Copy sprite from original monster
        var originalMonster = parent.GetTree().GetFirstNodeInGroup("enemies") as Monster;
        if (originalMonster != null)
        {
            CopyMonsterSprite(originalMonster, ghost);
        }
        
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
                Animation = "idle"
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
                Texture = sprite2D.Texture
            };
            ghost.AddChild(ghostSprite);
        }
        else if (animSprite != null)
        {
            var ghostSprite = new AnimatedSprite2D
            {
                Name = "AnimatedSprite2D",
                SpriteFrames = animSprite.SpriteFrames,
                Animation = "idle"
            };
            ghost.AddChild(ghostSprite);
        }
    }
    #endregion
}
