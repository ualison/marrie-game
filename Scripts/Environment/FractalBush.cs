using Godot;
using System;

public partial class FractalBush : Node2D
{
    [Export] public float InitialLength = 100.0f;
    [Export] public float LengthReduction = 0.7f;
    [Export] public float AngleVariation = 25.0f;
    [Export] public int MaxDepth = 8;
    [Export] public int BranchCount = 2;
    [Export] public float MinLength = 5.0f;
    [Export] public Color StartColor = Colors.SaddleBrown;
    [Export] public Color EndColor = Colors.Green;
    [Export] public float LineWidth = 3.0f;
    [Export] public bool AnimateGrowth = true;
    [Export] public float AnimationSpeed = 2.0f;
    
    private float currentAnimationTime = 0.0f;
    private Random random = new Random();
    
    public override void _Ready()
    {
        // The bush will draw from the Node2D's position in the scene
        // No need to override the position here
    }
    
    public override void _Process(double delta)
    {
        if (AnimateGrowth)
        {
            currentAnimationTime += (float)(delta * AnimationSpeed);
            QueueRedraw(); // Trigger _Draw to be called
        }
    }
    
    public override void _Draw()
    {
        // Start drawing from the base
        Vector2 startPos = Vector2.Zero;
        Vector2 direction = Vector2.Up; // Start growing upward
        
        DrawBranch(startPos, direction, InitialLength, 0);
    }
    
    private void DrawBranch(Vector2 startPos, Vector2 direction, float length, int depth)
    {
        // Stop if we've reached max depth or minimum length
        if (depth >= MaxDepth || length < MinLength)
            return;
            
        // Calculate animation factor for growth effect
        float animationFactor = 1.0f;
        if (AnimateGrowth)
        {
            float branchTime = currentAnimationTime - (depth * 0.3f);
            animationFactor = Mathf.Clamp(branchTime, 0.0f, 1.0f);
            if (animationFactor <= 0.0f)
                return;
        }
        
        // Calculate end position
        Vector2 endPos = startPos + direction * length * animationFactor;
        
        // Calculate color based on depth (from brown trunk to green leaves)
        float colorLerp = (float)depth / MaxDepth;
        Color branchColor = StartColor.Lerp(EndColor, colorLerp);
        
        // Calculate line width (thicker at base, thinner at tips)
        float currentWidth = LineWidth * (1.0f - (float)depth / MaxDepth * 0.8f);
        
        // Draw the current branch
        DrawLine(startPos, endPos, branchColor, currentWidth);
        
        // Only continue if animation allows
        if (AnimateGrowth && animationFactor < 1.0f)
            return;
        
        // Create child branches
        for (int i = 0; i < BranchCount; i++)
        {
            // Calculate angle variation for each branch
            float angleOffset = 0;
            if (BranchCount > 1)
            {
                // Spread branches evenly with some randomness
                float baseAngle = (i - (BranchCount - 1) / 2.0f) * (AngleVariation / (BranchCount - 1));
                angleOffset = baseAngle + (float)(random.NextDouble() - 0.5) * (AngleVariation * 0.3f);
            }
            else
            {
                // Single branch with random variation
                angleOffset = (float)(random.NextDouble() - 0.5) * AngleVariation;
            }
            
            // Convert angle to radians and rotate direction
            float angleRad = Mathf.DegToRad(angleOffset);
            Vector2 newDirection = direction.Rotated(angleRad);
            
            // Add some randomness to length reduction
            float lengthMultiplier = LengthReduction + (float)(random.NextDouble() - 0.5) * 0.2f;
            float newLength = length * lengthMultiplier;
            
            // Recursively draw child branch
            DrawBranch(endPos, newDirection, newLength, depth + 1);
        }
    }
    
    // Method to regenerate the bush with new random seed
    public void RegenerateBush()
    {
        random = new Random();
        currentAnimationTime = 0.0f;
        QueueRedraw();
    }
    
    // Method to restart animation
    public void RestartAnimation()
    {
        currentAnimationTime = 0.0f;
    }
}