using Godot;
using MarrieGame.Core.Logging; // Why: bring LogCategory into scope to avoid verbose fully-qualified names

/// Handles scene loading, reloading, and lifecycle management
/// Single Responsibility: Scene Management
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
            if (Log.IsCategoryEnabled(LogCategory.Debug))
                Log.Debug($"Scene reloaded: {node.Name}", "SceneLifecycleManager");
            
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
        if (Log.IsCategoryEnabled(LogCategory.Debug))
            Log.Debug($"Scene reloaded: {GetTree().CurrentScene?.Name}", "SceneLifecycleManager");

        // Notify readiness regardless of logging configuration
        CallDeferred(nameof(NotifySceneReady));
    }
    private void NotifySceneReady()
    {
        // Always fire the event; logging is only observational
        if (Log.IsCategoryEnabled(LogCategory.Debug))
            Log.Debug("Firing SceneReady event", "SceneLifecycleManager");

        SceneReady?.Invoke();

        if (Log.IsCategoryEnabled(LogCategory.Debug))
            Log.Debug("SceneReady event fired", "SceneLifecycleManager");
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
