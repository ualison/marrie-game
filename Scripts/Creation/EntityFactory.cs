using Godot;

/// <summary>
/// Entity creation services for dynamic game objects
/// Handles instantiation of replay gems and ghost entities
/// </summary>
public static class EntityFactory
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
                if (ResourceLoader.Exists(path))
                {
                    _replayGemScene = GD.Load<PackedScene>(path);
                    Log.Debug($"EntityFactory: Loaded ReplayGem scene from {path}", "EntityFactory");
                    break;
                }
            }
            
            if (_replayGemScene == null)
            {
                Log.Error("EntityFactory: Could not find ReplayGem.tscn in any expected location", "EntityFactory");
            }
        }
        return _replayGemScene;
    }
    
    private static PackedScene GetGhostPlayerScene()
    {
        if (_ghostPlayerScene == null)
        {
            _ghostPlayerScene = GD.Load<PackedScene>("res://Prefabs/GhostPlayer.tscn");
        }
        return _ghostPlayerScene;
    }
    
    private static PackedScene GetGhostMonsterScene()
    {
        if (_ghostMonsterScene == null)
        {
            _ghostMonsterScene = GD.Load<PackedScene>("res://Prefabs/GhostMonster.tscn");
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
            Log.Error("EntityFactory: CRITICAL - Could not load ReplayGem scene!", "EntityFactory");
            return null;
        }
        
        var gem = scene.Instantiate() as ReplayGem;
        if (gem == null)
        {
            Log.Error("EntityFactory: CRITICAL - ReplayGem scene did not instantiate properly!", "EntityFactory");
            return null;
        }
        
        parent.AddChild(gem);
        gem.GlobalPosition = position;
        GameEvents.OnReplayGemCreated(position);
        Log.Debug($"EntityFactory: Created ReplayGem at {position}", "EntityFactory");
        
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
            parent.AddChild(ghost);
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
            parent.AddChild(ghost);
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
