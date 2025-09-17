using Godot;

public partial class PuzzleCrystal : Node2D
{
    // Global variable flag to track if the crystal is active
    public bool IsActive { get; private set; } = false;

    // Reference to the Area2D node
    private Area2D area2D;

    public override void _Ready()
    {
        // Get reference to the Area2D node
        area2D = GetNode<Area2D>("Area2D");
        
        // Connect the area signals for detecting when bodies enter and exit
        area2D.BodyEntered += OnBodyEntered;
        area2D.BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        // Check if the body is Player or GhostPlayer
        if (body.Name == "Player" || body.Name == "GhostPlayer")
        {
            IsActive = true;
            GD.Print($"PuzzleCrystal activated by: {body.Name}");
        }
    }

    private void OnBodyExited(Node2D body)
    {
        // Check if the body is Player or GhostPlayer
        if (body.Name == "Player" || body.Name == "GhostPlayer")
        {
            IsActive = false;
            GD.Print($"PuzzleCrystal deactivated by: {body.Name}");
        }
    }
}