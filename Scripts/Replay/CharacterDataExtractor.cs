using Godot;
using System.Collections.Generic;

/// <summary>
/// High-performance character data extraction with caching and optimized lookups
/// </summary>
public static class CharacterDataExtractor
{
    #region Cache Storage
    private static readonly Dictionary<Node2D, CachedCharacterData> _characterCache = new();
    private static readonly Dictionary<Node2D, ulong> _lastModifiedTimes = new();
    #endregion

    #region Cached Data Structure
    private struct CachedCharacterData
    {
        public AnimatedSprite2D AnimatedSprite;
        public Sprite2D StaticSprite;
        public Node2D VisualNode;
        public bool HasAnimation;
    }
    #endregion

    #region Main Extraction Method
    public static ReplayFrame ExtractFrame(Node2D character, float timestamp)
    {
        var cachedData = GetCachedCharacterData(character);
        var position = character.GlobalPosition;
        var rotation = character.GlobalRotation;
        var animation = GetCurrentAnimation(cachedData);
        var spriteFlip = GetSpriteFlip(cachedData);
        var (isVisible, alpha) = GetVisibilityData(cachedData);
        var (monsterState, isFullyVisible) = GetMonsterStateData(character);

        return new ReplayFrame(timestamp, position, rotation, animation, spriteFlip, isVisible, alpha, monsterState, isFullyVisible);
    }
    #endregion

    #region Cache Management
    private static CachedCharacterData GetCachedCharacterData(Node2D character)
    {
        // Check if data needs refresh based on modification time
        var currentModTime = character.GetModifyTime();
        if (_characterCache.TryGetValue(character, out var cached) && 
            _lastModifiedTimes.TryGetValue(character, out var lastModTime) &&
            lastModTime == currentModTime)
        {
            return cached;
        }

        // Refresh cache if data is stale or missing
        var newData = DiscoverCharacterComponents(character);
        _characterCache[character] = newData;
        _lastModifiedTimes[character] = currentModTime;
        
        return newData;
    }

    private static CachedCharacterData DiscoverCharacterComponents(Node2D character)
    {
        var data = new CachedCharacterData();

        // Try AnimatedSprite2D first (most common for characters)
        data.AnimatedSprite = character.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (data.AnimatedSprite == null)
        {
            // Search descendants with common names
            data.AnimatedSprite = FindAnimatedSpriteInChildren(character);
        }

        // Try static Sprite2D as fallback
        if (data.AnimatedSprite == null)
        {
            data.StaticSprite = character.GetNodeOrNull<Sprite2D>("Sprite2D") ??
                               character.GetNodeOrNull<Sprite2D>("MonsterSprite") ??
                               FindStaticSpriteInChildren(character);
        }

        // Set primary visual node and flags
        data.VisualNode = (Node2D)data.AnimatedSprite ?? data.StaticSprite;
        data.HasAnimation = data.AnimatedSprite != null;

        return data;
    }

    private static AnimatedSprite2D FindAnimatedSpriteInChildren(Node2D character)
    {
        // Optimized search for AnimatedSprite2D with common names
        string[] commonNames = { "AnimatedSprite2D", "MarrieSprite", "PlayerSprite", "EnemySprite" };
        
        foreach (var name in commonNames)
        {
            var sprite = character.GetNodeOrNull<AnimatedSprite2D>(name);
            if (sprite != null) return sprite;
        }

        // Recursive search as last resort
        return character.FindChild("AnimatedSprite2D", recursive: true) as AnimatedSprite2D;
    }

    private static Sprite2D FindStaticSpriteInChildren(Node2D character)
    {
        // Optimized search for Sprite2D with common names
        string[] commonNames = { "Sprite2D", "MarrieSprite", "PlayerSprite", "MonsterSprite" };
        
        foreach (var name in commonNames)
        {
            var sprite = character.GetNodeOrNull<Sprite2D>(name);
            if (sprite != null) return sprite;
        }

        // Recursive search as last resort
        return character.FindChild("Sprite2D", recursive: true) as Sprite2D;
    }
    #endregion

    #region Data Extraction Methods
    private static string GetCurrentAnimation(CachedCharacterData data)
    {
        if (data.HasAnimation && data.AnimatedSprite?.Animation != null)
        {
            return data.AnimatedSprite.Animation.ToString();
        }
        return "idle";
    }

    private static bool GetSpriteFlip(CachedCharacterData data)
    {
        if (data.HasAnimation)
        {
            return data.AnimatedSprite?.FlipH ?? false;
        }
        
        // For static sprites, check scale.X for flip
        if (data.StaticSprite != null)
        {
            return data.StaticSprite.Scale.X < 0;
        }
        
        return false;
    }

    private static (bool isVisible, float alpha) GetVisibilityData(CachedCharacterData data)
    {
        if (data.VisualNode == null) return (true, 1.0f);

        var modulate = data.VisualNode.Modulate;
        var isVisible = data.VisualNode.Visible && modulate.A > 0f;
        
        return (isVisible, modulate.A);
    }

    private static (string monsterState, bool isFullyVisible) GetMonsterStateData(Node2D character)
    {
        // Extract monster state data if character is a Monster with follower behavior
        if (character is Monster monster && monster.EnableFollowerBehavior)
        {
            return (monster.GetState().ToString(), monster.IsFullyVisible());
        }
        
        // For non-follower Monster characters, return default values
        return ("", true);
    }
    #endregion

    #region Cache Maintenance
    public static void ClearCache()
    {
        _characterCache.Clear();
        _lastModifiedTimes.Clear();
    }

    public static void RemoveFromCache(Node2D character)
    {
        _characterCache.Remove(character);
        _lastModifiedTimes.Remove(character);
    }

    public static int GetCacheSize()
    {
        return _characterCache.Count;
    }
    #endregion

    #region Extension Method for Modification Time
    private static ulong GetModifyTime(this Node node)
    {
        // Simple heuristic: use frame count as modification time
        // In production, you might track actual node modifications
        return Engine.GetProcessFrames();
    }
    #endregion
}
