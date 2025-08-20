using Godot;

// Data structure for storing a single frame of replay information
// Simple class with properties for easy assignment and access
public class ReplayFrame
{
	public float Timestamp { get; set; }
	public Vector2 Position { get; set; }
	public float Rotation { get; set; }
	public string Animation { get; set; } = "idle";
	public bool SpriteFlip { get; set; }
}