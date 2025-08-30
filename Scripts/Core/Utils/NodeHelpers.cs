using System;
using Godot;

namespace MarrieGame.Core.Utils
{
    /// <summary>
    /// Lightweight helper utilities for common node lookup/creation patterns.
    /// Aim: reduce duplicated GetNodeOrNull logic and make fallbacks consistent.
    /// </summary>
    public static class NodeHelpers
    {
        // Try a configured NodePath first, then a named child, then return null.
        public static T GetNodeOrNull<T>(Node owner, NodePath configuredPath, string childName) where T : Node
        {
            if (owner == null) return null;

            if (configuredPath != null && configuredPath.ToString() != "")
            {
                var n = owner.GetNodeOrNull<T>(configuredPath);
                if (n != null) return n;
            }

            if (!string.IsNullOrEmpty(childName))
            {
                var byName = owner.GetNodeOrNull<T>(childName);
                if (byName != null) return byName;
            }

            return null;
        }

        // Find a child of the requested type anywhere among direct children.
        public static T FindChildOfType<T>(Node owner) where T : Node
        {
            if (owner == null) return null;
            foreach (Node child in owner.GetChildren())
            {
                if (child is T t) return t;
            }
            return null;
        }

        // Get or create child: try configured path or named child; otherwise create via factory (if provided).
        public static T GetOrCreateChild<T>(Node owner, NodePath configuredPath, string childName, Func<T> factory = null) where T : Node
        {
            var existing = GetNodeOrNull<T>(owner, configuredPath, childName);
            if (existing != null) return existing;

            existing = FindChildOfType<T>(owner);
            if (existing != null) return existing;

            if (factory != null)
            {
                var created = factory();
                if (!string.IsNullOrEmpty(childName)) created.Name = childName;
                owner.AddChild(created);
                return created;
            }

            return null;
        }

        // Specialized convenience method for sprites: prefer configured path, then common names, then first sprite child.
        public static Node2D FindSpriteNode(Node owner, NodePath configuredPath)
        {
            if (owner == null) return null;

            if (configuredPath != null && configuredPath.ToString() != "")
            {
                var n = owner.GetNodeOrNull<Node2D>(configuredPath);
                if (n != null) return n;
            }

            var animated = owner.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
            if (animated != null) return animated;

            var sprite = owner.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null) return sprite;

            var named = owner.GetNodeOrNull<Node2D>("MarrieSprite") ?? owner.GetNodeOrNull<Node2D>("MonsterSprite");
            if (named != null) return named;

            // Fallback: find first child that is a Sprite2D or AnimatedSprite2D
            foreach (Node child in owner.GetChildren())
            {
                if (child is AnimatedSprite2D || child is Sprite2D)
                    return child as Node2D;
            }

            return null;
        }
    }
}
