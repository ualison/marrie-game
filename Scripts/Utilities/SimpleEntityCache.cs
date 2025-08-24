using Godot;
using System.Collections.Generic;

/// <summary>
/// Simple entity cache for frequently created/destroyed objects
/// Much simpler than full object pooling - just what you actually need
/// </summary>
public partial class SimpleEntityCache : Node
{
    public static SimpleEntityCache Instance { get; private set; }
    
    // Just cache the few things you actually reuse
    private ReplayGem _cachedReplayGem;
    private GhostPlayer _cachedGhostPlayer;
    private GhostMonster _cachedGhostMonster;
    
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
        }
        else
        {
            QueueFree();
        }
    }
    
    public ReplayGem GetReplayGem(Vector2 position)
    {
        if (_cachedReplayGem == null || !IsInstanceValid(_cachedReplayGem))
        {
            var scene = GD.Load<PackedScene>("res://Prefabs/ReplayGem.tscn");
            _cachedReplayGem = scene?.Instantiate() as ReplayGem;
        }
        
        if (_cachedReplayGem != null && _cachedReplayGem.GetParent() == null)
        {
            GetTree().CurrentScene?.AddChild(_cachedReplayGem);
        }
        
        return _cachedReplayGem;
    }
    
    public void ReturnReplayGem(ReplayGem gem)
    {
        if (gem != null && IsInstanceValid(gem))
        {
            gem.GetParent()?.RemoveChild(gem);
            gem.Hide();
        }
    }
    
    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}
