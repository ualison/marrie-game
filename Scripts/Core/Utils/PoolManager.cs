using System.Collections.Generic;
using Godot;

/// <summary>
/// Simple pooling manager for frequently-created nodes (rocks, ghosts, etc.)
/// Pools are keyed by a string key (normally the PackedScene resource path).
/// Acquire returns either a pooled instance or a fresh instantiate from the provided prefab.
/// Release will deactivate the node and store it back into the pool.
/// </summary>
public static class PoolManager
{
    private static readonly Dictionary<string, Stack<Node>> _pools = new();

    public static Node Acquire(PackedScene prefab, string key = null)
    {
        if (prefab == null) return null;
        key ??= prefab.ResourcePath ?? prefab.GetType().Name;

        if (_pools.TryGetValue(key, out var stack) && stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is IPoolable poolable)
            {
                poolable.IsFromPool = true;
                poolable.ResetForReuse();
            }
            if (node is CanvasItem ci) ci.Show();
            return node;
        }

        var inst = prefab.Instantiate();
        if (inst is IPoolable p)
        {
            p.IsFromPool = false; // fresh instance
        }
        return inst;
    }

    public static void Release(Node node, string key = null)
    {
        if (node == null) return;

        // Try to disable node and store into pool
        // If node implements IPoolable, call OnReleasedToPool
        if (node is IPoolable poolable)
        {
            try
            {
                poolable.OnReleasedToPool();
            }
            catch { }
        }

        // Remove from parent and hide (if possible)
        if (node.GetParent() != null)
            node.GetParent().RemoveChild(node);
        if (node is CanvasItem ci2) ci2.Hide();

    // Use provided key or fallback to node's type name
    key ??= node.GetType().Name;

        if (!_pools.TryGetValue(key, out var stack))
        {
            stack = new Stack<Node>();
            _pools[key] = stack;
        }
        stack.Push(node);
    }
}
