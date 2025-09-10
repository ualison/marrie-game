using Godot;
using System;
using System.Collections.Generic;

public partial class EnvironmentChunk : Node2D
{
    private class SavedState
    {
        public Vector2 Position;
        public Vector2 Scale;
        public float Rotation;
        public int ZIndex;
        public bool Visible;
        public Color Modulate;
    }

    // Performance optimizations
    private Dictionary<string, SavedState> initialStates = new Dictionary<string, SavedState>();
    private bool isInitialized = false;
    private bool isLODMode = false;
    private float updateFrequency = 0.016f;
    private float lastUpdateTime = 0f;

    // Cache frequently accessed nodes
    private List<Node2D> cachedTreeNodes = new List<Node2D>();
    private List<Node2D> cachedEffectNodes = new List<Node2D>();

    public override void _Ready()
    {
        CacheInitialState();
        CacheImportantNodes();
        isInitialized = true;
    }

    private void CacheInitialState()
    {
        initialStates.Clear();

        // Use iterative approach instead of recursive for better performance
        var nodesToProcess = new Stack<Node>();
        nodesToProcess.Push(this);

        while (nodesToProcess.Count > 0)
        {
            var node = nodesToProcess.Pop();

            if (node is Node2D nd)
            {
                var pathNode = this.GetPathTo(nd);
                var path = pathNode.ToString();
                if (string.IsNullOrEmpty(path))
                    path = ".";

                if (!initialStates.ContainsKey(path))
                {
                    initialStates[path] = new SavedState
                    {
                        Position = nd.Position,
                        Scale = nd.Scale,
                        Rotation = nd.Rotation,
                        ZIndex = nd.ZIndex,
                        Visible = nd.Visible,
                        Modulate = (nd as CanvasItem)?.Modulate ?? Colors.White
                    };
                }
            }

            // Add children to processing stack
            foreach (Node child in node.GetChildren())
            {
                nodesToProcess.Push(child);
            }
        }
    }

    private void CacheImportantNodes()
    {
        cachedTreeNodes.Clear();
        cachedEffectNodes.Clear();

        // Cache nodes that need frequent access
        var nodesToProcess = new Stack<Node>();
        nodesToProcess.Push(this);

        while (nodesToProcess.Count > 0)
        {
            var node = nodesToProcess.Pop();

            if (node is Node2D nd && node != this)
            {
                string nodeName = node.Name.ToString().ToLower();

                if (nodeName.Contains("tree") || nodeName.Contains("bush"))
                {
                    cachedTreeNodes.Add(nd);
                }
                else if (nodeName.Contains("leaf") || nodeName.Contains("wind") ||
                         nodeName.Contains("particle"))
                {
                    cachedEffectNodes.Add(nd);
                }
            }

            foreach (Node child in node.GetChildren())
            {
                nodesToProcess.Push(child);
            }
        }
    }

    // Optimized reset with batched operations
    public void ResetChunk()
    {
        if (!isInitialized) return;

        // Batch restore all states
        foreach (var kvp in initialStates)
        {
            var path = kvp.Key;
            var state = kvp.Value;
            var node = GetNodeOrNull<Node>(new NodePath(path));

            if (node == this) continue; // Skip self

            if (node is Node2D nd)
            {
                // Batch all property assignments
                nd.Position = state.Position;
                nd.Scale = state.Scale;
                nd.Rotation = state.Rotation;
                nd.ZIndex = state.ZIndex;
                nd.Visible = state.Visible;

                if (nd is CanvasItem ci)
                    ci.Modulate = state.Modulate;

                // Optimized method calls
                if (nd.HasMethod("ResetChunk"))
                    nd.Call("ResetChunk");
                else if (nd.HasMethod("Reset"))
                    nd.Call("Reset");
            }
        }

        this.Visible = true;
        isLODMode = false;
    }

    public void InitializeChunk(int chunkIndex, ulong seed)
    {
        if (!isInitialized) return;

        ResetChunk();

        // Batch initialize child nodes
        var nodesToProcess = new Stack<Node>();
        nodesToProcess.Push(this);

        while (nodesToProcess.Count > 0)
        {
            var node = nodesToProcess.Pop();

            if (node == this)
            {
                // Add children to processing
                foreach (Node child in node.GetChildren())
                    nodesToProcess.Push(child);
                continue;
            }

            // Optimized method calls
            if (node.HasMethod("InitializeChunk"))
            {
                node.Call("InitializeChunk", chunkIndex, (long)seed);
            }
            else if (node.HasMethod("Initialize"))
            {
                node.Call("Initialize", chunkIndex, (long)seed);
            }

            // Continue traversal
            foreach (Node child in node.GetChildren())
                nodesToProcess.Push(child);
        }
    }

    // LOD System for performance
    public void SetLODMode(bool enabled)
    {
        if (isLODMode == enabled) return;

        isLODMode = enabled;

        // Efficiently toggle effects based on LOD
        foreach (var effectNode in cachedEffectNodes)
        {
            if (effectNode == null || !IsInstanceValid(effectNode)) continue;

            if (enabled)
            {
                // Disable expensive effects
                if (effectNode.HasMethod("SetEnabled"))
                    effectNode.Call("SetEnabled", false);
                else
                    effectNode.Visible = false;
            }
            else
            {
                // Re-enable effects
                if (effectNode.HasMethod("SetEnabled"))
                    effectNode.Call("SetEnabled", true);
                else
                    effectNode.Visible = true;
            }
        }

        // Adjust tree complexity
        foreach (var treeNode in cachedTreeNodes)
        {
            if (treeNode == null || !IsInstanceValid(treeNode)) continue;

            if (treeNode.HasMethod("SetLODMode"))
            {
                treeNode.Call("SetLODMode", enabled);
            }
        }
    }

    public void SetUpdateFrequency(float frequency)
    {
        updateFrequency = frequency;

        // Propagate to child nodes
        foreach (var treeNode in cachedTreeNodes)
        {
            if (treeNode?.HasMethod("SetUpdateFrequency") == true)
            {
                treeNode.Call("SetUpdateFrequency", frequency);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!isInitialized) return;

        float currentTime = (float)Time.GetUnixTimeFromSystem();

        // Throttle updates based on frequency
        if (currentTime - lastUpdateTime < updateFrequency)
            return;

        lastUpdateTime = currentTime;

        // Minimal processing in LOD mode
        if (isLODMode)
        {
            ProcessLODMode((float)delta);
        }
        else
        {
            ProcessFullMode((float)delta);
        }
    }

    private void ProcessLODMode(float delta)
    {
        // Very minimal updates for distant chunks
        // Only update critical systems that must continue running

        // Update only 1 tree per frame to spread load
        if (cachedTreeNodes.Count == 0)
            return;

        // Use a stable, non-negative index derived from system time.
        // Time.GetUnixTimeFromSystem() can be large and casting or using
        // the remainder directly may produce negative values. Normalize
        // via Math.Abs on a long and then modulo by the valid count.
        long unix = (long)Math.Floor(Time.GetUnixTimeFromSystem());
        int treeIndex = (int)((Math.Abs(unix * 2L) % cachedTreeNodes.Count));
        var tree = cachedTreeNodes[treeIndex];

        if (tree?.HasMethod("ProcessLOD") == true)
        {
            tree.Call("ProcessLOD", delta);
        }
    }

    private void ProcessFullMode(float delta)
    {
        // Normal processing for nearby chunks
        // Stagger updates across frames to reduce load spikes

    long unix = (long)Math.Floor(Time.GetUnixTimeFromSystem());
    int frameOffset = (int)((Math.Abs(unix * 60L) % 3));

        switch (frameOffset)
        {
            case 0:
                UpdateTrees(delta);
                break;
            case 1:
                UpdateEffects(delta);
                break;
            case 2:
                UpdateOtherElements(delta);
                break;
        }
    }

    private void UpdateTrees(float delta)
    {
        // Update trees with load balancing
        if (cachedTreeNodes.Count == 0)
            return;

        int maxTreesPerFrame = Mathf.Max(1, cachedTreeNodes.Count / 3);

        long unix = (long)Math.Floor(Time.GetUnixTimeFromSystem());
        int startIndex = (int)(Math.Abs(unix * 10L) % cachedTreeNodes.Count);

        for (int i = 0; i < maxTreesPerFrame && i < cachedTreeNodes.Count; i++)
        {
            int index = (startIndex + i) % cachedTreeNodes.Count;
            var tree = cachedTreeNodes[index];

            if (tree?.HasMethod("ProcessUpdate") == true)
            {
                tree.Call("ProcessUpdate", delta);
            }
        }
    }

    private void UpdateEffects(float delta)
    {
        // Update particle effects and animations
        foreach (var effectNode in cachedEffectNodes)
        {
            if (effectNode?.HasMethod("ProcessEffects") == true)
            {
                effectNode.Call("ProcessEffects", delta);
            }
        }
    }

    private void UpdateOtherElements(float delta)
    {
        // Update any remaining elements that need processing
        // This can be extended for other chunk-specific logic
    }

    // Public methods for external optimization
    public void OptimizeForDistance(float distance)
    {
        // Automatically adjust LOD based on distance
        bool shouldUseLOD = distance > 800f; // Threshold distance
        SetLODMode(shouldUseLOD);

        // Adjust update frequency based on distance
        float frequency = Mathf.Lerp(0.016f, 0.5f, distance / 1500f);
        SetUpdateFrequency(frequency);
    }

    public void FreezeChunk()
    {
        // Completely stop all updates for very distant chunks
        SetProcessMode(ProcessModeEnum.Disabled);

        foreach (var tree in cachedTreeNodes)
        {
            if (tree?.HasMethod("Freeze") == true)
            {
                tree.Call("Freeze");
            }
        }
    }

    public void UnfreezeChunk()
    {
        // Resume processing
        SetProcessMode(ProcessModeEnum.Inherit);

        foreach (var tree in cachedTreeNodes)
        {
            if (tree?.HasMethod("Unfreeze") == true)
            {
                tree.Call("Unfreeze");
            }
        }
    }

    // Performance monitoring
    public Dictionary<string, int> GetPerformanceStats()
    {
        return new Dictionary<string, int>
        {
            ["CachedTrees"] = cachedTreeNodes.Count,
            ["CachedEffects"] = cachedEffectNodes.Count,
            ["InitialStates"] = initialStates.Count,
            ["IsLOD"] = isLODMode ? 1 : 0,
            ["UpdateFrequency"] = (int)(updateFrequency * 1000) // in milliseconds
        };
    }

    public override void _ExitTree()
    {
        // Clean up cached references
        cachedTreeNodes.Clear();
        cachedEffectNodes.Clear();
        initialStates.Clear();

        base._ExitTree();
    }
}