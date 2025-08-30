using System.Collections.Generic;

/// <summary>
/// Global registry to map frequently-used replay strings (animation names, monster states)
/// to compact ushort ids to avoid per-frame string allocations.
/// This registry assigns ids on-demand and is safe for runtime use.
/// </summary>
public static class ReplayStringRegistry
{
    private static readonly Dictionary<string, ushort> _toId = new();
    private static readonly Dictionary<ushort, string> _toString = new();
    private static ushort _nextId = 1; // reserve 0 as "none/idle"

    public static ushort GetOrRegisterAnimation(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        if (_toId.TryGetValue(name, out var id)) return id;
        id = _nextId++;
        _toId[name] = id;
        _toString[id] = name;
        return id;
    }

    public static string GetAnimationName(ushort id)
    {
        if (id == 0) return string.Empty;
        return _toString.TryGetValue(id, out var s) ? s : string.Empty;
    }

    public static ushort GetOrRegisterMonsterState(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        if (_toId.TryGetValue(name, out var id)) return id;
        id = _nextId++;
        _toId[name] = id;
        _toString[id] = name;
        return id;
    }

    public static string GetMonsterStateName(ushort id)
    {
        if (id == 0) return string.Empty;
        return _toString.TryGetValue(id, out var s) ? s : string.Empty;
    }
}
