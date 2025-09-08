using Godot;
using System;
using System.Collections.Generic;

public partial class OrganicFractalBush : Node2D
{
    [Export] public float TrunkLength = 150.0f;
    [Export] public float BranchingFactor = 0.75f;
    [Export] public float AngleSpread = 35.0f;
    [Export] public float AngleAsymmetry = 15.0f;
    [Export] public int MaxDepth = 12;
    [Export] public float MinBranchLength = 3.0f;
    [Export] public float BranchThickness = 8.0f;
    [Export] public float ThicknessFalloff = 0.8f;
    [Export] public Color TrunkColor = new Color(0.4f, 0.3f, 0.2f);
    [Export] public Color TipColor = new Color(0.8f, 0.9f, 0.7f);
    [Export] public float NoiseScale = 0.1f;
    [Export] public float GravityBend = 0.15f;
    [Export] public int Seed = 12345;
    
    private struct Branch
    {
        public Vector2 start;
        public Vector2 end;
        public float thickness;
        public Color color;
        public int depth;
    }
    
    private List<Branch> branches = new List<Branch>();
    private Random random;
    private FastNoiseLite noise;
    
    public override void _Ready()
    {
        random = new Random(Seed);
        noise = new FastNoiseLite();
        noise.Seed = Seed;
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        noise.Frequency = NoiseScale;
        
        GenerateBush();
    }
    
    private void GenerateBush()
    {
        branches.Clear();
        
        Vector2 startPos = Vector2.Zero;
        Vector2 initialDirection = Vector2.Up;
        
        GenerateBranch(startPos, initialDirection, TrunkLength, BranchThickness, 0);
    }
    
    private void GenerateBranch(Vector2 startPos, Vector2 direction, float length, float thickness, int depth)
    {
        if (depth >= MaxDepth || length < MinBranchLength || thickness < 0.5f)
            return;
        
        // Add slight curve using noise for organic feel
        float noiseValue = noise.GetNoise2D(startPos.X * 0.01f, startPos.Y * 0.01f + depth * 0.5f);
        float curvature = noiseValue * 0.3f;
        
        // Apply gravity bend (branches tend to droop slightly)
        float gravityEffect = GravityBend * (1.0f - (float)depth / MaxDepth);
        Vector2 bendDirection = direction.Rotated(curvature + gravityEffect);
        
        Vector2 endPos = startPos + bendDirection * length;
        
        // Calculate color interpolation
        float colorT = Mathf.Clamp((float)depth / (MaxDepth - 2), 0.0f, 1.0f);
        Color branchColor = TrunkColor.Lerp(TipColor, colorT);
        
        // Store branch for drawing
        branches.Add(new Branch
        {
            start = startPos,
            end = endPos,
            thickness = thickness,
            color = branchColor,
            depth = depth
        });
        
        // Determine branching pattern based on depth
        int branchCount = GetBranchCount(depth);
        
        if (branchCount > 0)
        {
            for (int i = 0; i < branchCount; i++)
            {
                // Create asymmetric branching angles
                float baseAngle = 0;
                if (branchCount == 2)
                {
                    baseAngle = (i == 0) ? -AngleSpread : AngleSpread;
                    // Add asymmetry
                    if (i == 0)
                        baseAngle -= AngleAsymmetry * (float)random.NextDouble();
                    else
                        baseAngle += AngleAsymmetry * (float)random.NextDouble();
                }
                else if (branchCount == 3)
                {
                    baseAngle = (i - 1) * AngleSpread * 0.8f;
                    baseAngle += (float)(random.NextDouble() - 0.5) * AngleAsymmetry;
                }
                
                // Add natural variation
                float angleVariation = (float)(random.NextDouble() - 0.5) * 15.0f;
                float finalAngle = baseAngle + angleVariation;
                
                Vector2 newDirection = bendDirection.Rotated(Mathf.DegToRad(finalAngle));
                
                // Variable length reduction with some randomness
                float lengthReduction = BranchingFactor + (float)(random.NextDouble() - 0.5) * 0.2f;
                lengthReduction = Mathf.Clamp(lengthReduction, 0.5f, 0.9f);
                
                float newLength = length * lengthReduction;
                float newThickness = thickness * ThicknessFalloff;
                
                GenerateBranch(endPos, newDirection, newLength, newThickness, depth + 1);
            }
        }
    }
    
    private int GetBranchCount(int depth)
    {
        // Organic branching pattern
        if (depth == 0) return 2; // Trunk splits into 2
        if (depth < 3) return 2;  // Primary branches
        if (depth < 6) return (random.NextDouble() < 0.7) ? 2 : 3; // Secondary branches, mostly 2, sometimes 3
        if (depth < 9) return (random.NextDouble() < 0.6) ? 2 : 1; // Tertiary branches, mix of 1 and 2
        return (random.NextDouble() < 0.3) ? 2 : 1; // Fine branches, mostly single
    }
    
    public override void _Draw()
    {
        // Sort branches by depth for proper layering (draw thick branches first)
        branches.Sort((a, b) => a.depth.CompareTo(b.depth));
        
        // Draw all branches
        foreach (var branch in branches)
        {
            // Use anti-aliased lines for smooth appearance
            DrawLine(branch.start, branch.end, branch.color, branch.thickness, true);
            
            // Add subtle tapered effect by drawing a slightly thinner line on top
            if (branch.thickness > 2.0f)
            {
                Color innerColor = branch.color.Lerp(Colors.White, 0.1f);
                DrawLine(branch.start, branch.end, innerColor, branch.thickness * 0.7f, true);
            }
        }
    }
    
    public void RegenerateBush()
    {
        Seed = (int)(Time.GetUnixTimeFromSystem() % int.MaxValue);
        random = new Random(Seed);
        noise.Seed = Seed;
        GenerateBush();
        QueueRedraw();
    }
    
    public void RegenerateWithSeed(int newSeed)
    {
        Seed = newSeed;
        random = new Random(Seed);
        noise.Seed = Seed;
        GenerateBush();
        QueueRedraw();
    }
}