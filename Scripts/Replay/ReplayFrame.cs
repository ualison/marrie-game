using Godot;

// Data structure for storing a single frame of replay information
public class ReplayFrame
{
    #region Core Data
    public float Timestamp { get; set; }
    public Vector2 Position { get; set; }
    public float Rotation { get; set; }
    #endregion

    #region Visual Data
    public string Animation { get; set; } = "idle";
    public bool SpriteFlip { get; set; }
    public bool IsVisible { get; set; } = true;
    public float Alpha { get; set; } = 1.0f; // Track actual alpha value for fades
    #endregion

    #region Constructors
    public ReplayFrame()
    {
        // Default constructor for easy initialization
    }

    public ReplayFrame(float timestamp, Vector2 position, float rotation = 0f, string animation = "idle", 
                      bool spriteFlip = false, bool isVisible = true, float alpha = 1.0f)
    {
        // Convenience constructor for quick frame creation
        Timestamp = timestamp;
        Position = position;
        Rotation = rotation;
        Animation = animation ?? "idle";
        SpriteFlip = spriteFlip;
        IsVisible = isVisible;
        Alpha = alpha;
    }
    #endregion

    #region Utility Methods
    public ReplayFrame Clone()
    {
        // Create a deep copy of this frame
        return new ReplayFrame(Timestamp, Position, Rotation, Animation, SpriteFlip, IsVisible, Alpha);
    }

    public override string ToString()
    {
        // Provide readable string representation for debugging
        return $"Frame[{Timestamp:F2}s] Pos:{Position} Rot:{Rotation:F1}° Anim:{Animation} Flip:{SpriteFlip} Visible:{IsVisible} Alpha:{Alpha:F2}";
    }
    #endregion

    #region Validation
    public bool IsValid()
    {
        // Basic validation to ensure frame data is reasonable
        return Timestamp >= 0 && !string.IsNullOrWhiteSpace(Animation) && Alpha >= 0f && Alpha <= 1f;
    }
    #endregion
}