using Godot;

/// <summary>
/// Handles scene loading, reloading, and lifecycle management
/// Single Responsibility: Scene Management
/// </summary>
public partial class SceneLifecycleManager : Node
{
    public static SceneLifecycleManager Instance { get; private set; }
    
    // Events for other systems to listen to
    public static event System.Action<Node> SceneChanged;
    public static event System.Action SceneReady;
    
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            ConnectToSceneEvents();
        }
        else
        {
            QueueFree();
        }
    }
    
    private void ConnectToSceneEvents()
    {
        var currentTree = GetTree();
        if (currentTree != null)
        {
            currentTree.NodeAdded += OnNodeAdded;
            currentTree.Root.ChildEnteredTree += OnRootChildEntered;
        }
    }
    
    private void OnNodeAdded(Node node)
    {
        if (node == GetTree().CurrentScene)
        {
            CallDeferred(nameof(HandleSceneReload));
        }
    }
    
    private async void OnRootChildEntered(Node node)
    {
        if (node == GetTree().CurrentScene)
        {
            GD.Print($"=== Scene reloaded: {node.Name} ===");
            
            // Wait for scene to be fully ready
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            
            // Notify other systems
            SceneChanged?.Invoke(node);
            CallDeferred(nameof(NotifySceneReady));
        }
    }
    
    private void HandleSceneReload()
    {
        GD.Print($"=== Scene reloaded: {GetTree().CurrentScene?.Name} ===");
        CallDeferred(nameof(NotifySceneReady));
    }
    
    private void NotifySceneReady()
    {
        GD.Print("SceneLifecycleManager: Firing SceneReady event");
        SceneReady?.Invoke();
        GD.Print("SceneLifecycleManager: SceneReady event fired");
    }
    
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            var tree = GetTree();
            if (tree != null)
            {
                tree.NodeAdded -= OnNodeAdded;
                tree.Root.ChildEnteredTree -= OnRootChildEntered;
            }
            Instance = null;
        }
    }
}
