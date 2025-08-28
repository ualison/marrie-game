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
    public readonly string Animation;
    public readonly bool SpriteFlip;
    public readonly bool IsVisible;
    public readonly float Alpha;
    #endregion

    #region Monster State Data
    public readonly string MonsterState; // "InitialWait", "Following", "Hidden"
    public readonly bool IsFullyVisible; // Controls movement during transitions
    #endregion

    #region Constructor
    public ReplayFrame(float timestamp, Vector2 position, float rotation = 0f, 
                      string animation = "idle", bool spriteFlip = false, 
                      bool isVisible = true, float alpha = 1.0f,
                      string monsterState = "", bool isFullyVisible = true)
    {
        Timestamp = timestamp;
        Position = position;
        Rotation = rotation;
        Animation = animation ?? "idle";
        SpriteFlip = spriteFlip;
        IsVisible = isVisible;
        Alpha = alpha;
        MonsterState = monsterState ?? "";
        IsFullyVisible = isFullyVisible;
    }
    #endregion

    #region Utility Methods
    public override string ToString()
    {
        var stateInfo = !string.IsNullOrEmpty(MonsterState) ? $" State:{MonsterState} FullyVisible:{IsFullyVisible}" : "";
        return $"Frame[{Timestamp:F2}s] Pos:{Position} Rot:{Rotation:F1}° Anim:{Animation} Flip:{SpriteFlip} Visible:{IsVisible} Alpha:{Alpha:F2}{stateInfo}";
    }

    public bool IsValid()
    {
        return Timestamp >= 0 && !string.IsNullOrWhiteSpace(Animation) && Alpha is >= 0f and <= 1f;
    }
    #endregion
}