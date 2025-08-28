using Godot;

/// <summary>
/// Immutable data structure for storing replay frame information with better performance
/// </summary>
public readonly struct ReplayFrame
{
    #region Core Data
    public readonly float Timestamp;
    public readonly Vector2 Position;
    public readonly float Rotation;
    #endregion

    #region Visual Data
    // Store compact ids for animation and monster state to avoid per-frame string allocations
    public readonly ushort AnimationId;
    public readonly bool SpriteFlip;
    public readonly bool IsVisible;
    public readonly float Alpha;
    #endregion

    #region Monster State Data
    public readonly ushort MonsterStateId; // compact id for monster state
    public readonly bool IsFullyVisible; // Controls movement during transitions
    #endregion

    #region Constructor
    public ReplayFrame(float timestamp, Vector2 position, float rotation = 0f,
                      ushort animationId = 0, bool spriteFlip = false,
                      bool isVisible = true, float alpha = 1.0f,
                      ushort monsterStateId = 0, bool isFullyVisible = true)
    {
        Timestamp = timestamp;
        Position = position;
        Rotation = rotation;
        AnimationId = animationId;
        SpriteFlip = spriteFlip;
        IsVisible = isVisible;
        Alpha = alpha;
        MonsterStateId = monsterStateId;
        IsFullyVisible = isFullyVisible;
    }
    #endregion

    #region Utility Methods
    public override string ToString()
    {
    var animName = ReplayStringRegistry.GetAnimationName(AnimationId);
    var stateName = ReplayStringRegistry.GetMonsterStateName(MonsterStateId);
    var stateInfo = !string.IsNullOrEmpty(stateName) ? $" State:{stateName} FullyVisible:{IsFullyVisible}" : "";
    return $"Frame[{Timestamp:F2}s] Pos:{Position} Rot:{Rotation:F1}° Anim:{animName} Flip:{SpriteFlip} Visible:{IsVisible} Alpha:{Alpha:F2}{stateInfo}";
    }

    public bool IsValid()
    {
    // AnimationId can be zero for idle/no-animation
    return Timestamp >= 0 && Alpha is >= 0f and <= 1f;
    }
    #endregion
}