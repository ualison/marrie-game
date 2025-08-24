using Godot;

/// <summary>
/// Simple factory for the few entities you actually create dynamically
/// No overengineering - just centralized creation logic
/// </summary>
public static class EntityCreator
{
    private static PackedScene _replayGemScene;
    private static PackedScene _ghostPlayerScene;
    private static PackedScene _ghostMonsterScene;
    
    // Lazy load scenes
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
                    GD.Print($"EntityCreator: Loaded ReplayGem scene from {path}");
                    break;
                }
            }
            
            if (_replayGemScene == null)
            {
                GD.PrintErr("EntityCreator: Could not find ReplayGem.tscn in any expected location");
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
    
    public static ReplayGem CreateReplayGem(Vector2 position, Node parent)
    {
        var scene = GetReplayGemScene();
        if (scene == null)
        {
            GD.PrintErr("EntityCreator: CRITICAL - Could not load ReplayGem scene from any path!");
            return null;
        }
        
        var gem = scene.Instantiate() as ReplayGem;
        if (gem == null)
        {
            GD.PrintErr("EntityCreator: CRITICAL - ReplayGem scene did not instantiate as ReplayGem class!");
            return null;
        }
        
        parent.AddChild(gem);
        gem.GlobalPosition = position;
        GameEvents.OnReplayGemCreated(position);
        GD.Print($"EntityCreator: Successfully created ReplayGem from PackedScene at {position}");
        
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
}
