using Godot;
using System;
using System.Collections.Generic;

public partial class OrganicFractalTree : Node2D
{
    [Export] private int seed = 12345;
    [Export] private bool randomizeOnLoad = true;
    [Export] private int maxDepth = 12;
    [Export] private float initialLength = 150.0f;
    [Export] private float lengthReduction = 0.75f;
    [Export(PropertyHint.Range, "0.1,3.0")] private float initialThickness = 8.0f;
    [Export(PropertyHint.Range, "0.1,1.0")] private float thicknessReduction = 0.7f;
    [Export(PropertyHint.Range, "10,90")] private float branchAngle = 35.0f;
    [Export(PropertyHint.Range, "0,30")] private float angleVariation = 15.0f;
    [Export(PropertyHint.Range, "0,0.5")] private float curvature = 0.1f;
    [Export(PropertyHint.Range, "2,5")] private int minBranches = 2;
    [Export(PropertyHint.Range, "2,5")] private int maxBranches = 3;
    [Export(PropertyHint.Range, "0,1")] private float asymmetry = 0.3f;
    [Export] private Color trunkColor = new Color(0.9f, 0.9f, 0.9f);
    [Export] private Color leafColor = new Color(0.95f, 0.95f, 0.95f);
    [Export] private bool drawLeaves = true;
    [Export(PropertyHint.Range, "1,10")] private float leafSize = 3.0f;
    
    [ExportGroup("Apples")]
    [Export] private bool hasApples = false;
    [Export(PropertyHint.Range, "1,50")] private int appleQuantity = 10;
    [Export] private PackedScene applePrefab;
    
    private Random random;
    private List<Vector2> leafPositions = new List<Vector2>();
    private List<BranchData> branches = new List<BranchData>();
    private List<Node2D> appleInstances = new List<Node2D>();
    
    // Store branch data for apple placement
    private class BranchData
    {
        public Line2D Line { get; set; }
        public int Depth { get; set; }
        public Vector2[] Points { get; set; }
        public float Thickness { get; set; }
    }
    
    public override void _Ready()
    {
        // Randomize seed on load if enabled
        if (randomizeOnLoad)
        {
            seed = (int)GD.RandRange(0, 999999);
            GD.Print($"OrganicFractalTree: Using random seed: {seed}");
        }
        else
        {
            GD.Print($"OrganicFractalTree: Using fixed seed: {seed}");
        }
        
        GenerateTree();
    }
    
    public override void _Draw()
    {
        // Draw leaves using custom drawing
        if (drawLeaves && leafPositions.Count > 0)
        {
            foreach (var leafPos in leafPositions)
            {
                DrawCircle(leafPos, leafSize, leafColor);
                
                // Draw multiple smaller circles around for a more organic look
                for (int i = 0; i < 3; i++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2);
                    float distance = leafSize * 0.5f;
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    DrawCircle(leafPos + offset, leafSize * 0.7f, leafColor * new Color(1, 1, 1, 0.8f));
                }
            }
        }
    }
    
    private void GenerateTree()
    {
        GD.Print("Generating tree...");
        
        // Clear any existing children and lists
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
        leafPositions.Clear();
        branches.Clear();
        appleInstances.Clear();
        
        // Initialize random generator
        random = new Random(seed);
        
        // Start growing from the base
        Vector2 startPos = Vector2.Zero;
        Vector2 startDir = Vector2.Up; // Points upward (negative Y in Godot)
        
        // Create the tree structure
        GrowBranch(startPos, startDir, initialLength, initialThickness, 0, 0);
        
        // Place apples if enabled
        if (hasApples && applePrefab != null && branches.Count > 0)
        {
            PlaceApples();
        }
        else if (hasApples && applePrefab == null)
        {
            GD.PrintErr("OrganicFractalTree: Apple prefab not set!");
        }
        
        // Request redraw for leaves
        QueueRedraw();
        
        GD.Print($"Tree generated with {branches.Count} branches and {leafPositions.Count} leaf positions");
        if (hasApples)
        {
            GD.Print($"Placed {appleInstances.Count} apples");
        }
    }
    
    private void GrowBranch(Vector2 startPos, Vector2 direction, float length, float thickness, int depth, float parentAngle)
    {
        if (depth > maxDepth || length < 2.0f || thickness < 0.5f)
        {
            // Add leaf position at branch tips
            if (drawLeaves)
            {
                leafPositions.Add(startPos);
            }
            return;
        }
        
        // Create a new Line2D for this branch
        Line2D branch = new Line2D();
        AddChild(branch);
        
        // Configure branch appearance
        branch.Width = thickness;
        branch.DefaultColor = trunkColor;
        branch.JointMode = Line2D.LineJointMode.Round;
        // branch.CapMode = Line2D.LineCapMode.Round; // Not available in older Godot versions
        branch.Antialiased = true;
        
        // Generate branch points with organic curves
        var points = new List<Vector2>();
        Vector2 currentPos = startPos;
        Vector2 currentDir = direction.Normalized();
        int segments = Math.Max(3, (int)(length / 10));
        float segmentLength = length / segments;
        
        points.Add(currentPos);
        
        // Create curved path
        for (int i = 0; i < segments; i++)
        {
            // Add slight random curve to direction
            float curveAmount = (float)(random.NextDouble() - 0.5) * curvature;
            currentDir = currentDir.Rotated(curveAmount);
            currentDir = currentDir.Normalized();
            
            // Move to next point
            currentPos += currentDir * segmentLength;
            points.Add(currentPos);
        }
        
        // Apply points to the branch
        branch.Points = points.ToArray();
        
        // Store branch data for apple placement
        var branchData = new BranchData
        {
            Line = branch,
            Depth = depth,
            Points = points.ToArray(),
            Thickness = thickness
        };
        branches.Add(branchData);
        
        // Add subtle gradient effect
        var gradient = new Gradient();
        float depthFactor = 1.0f - (depth / (float)maxDepth) * 0.3f;
        gradient.SetColor(0, trunkColor * new Color(depthFactor, depthFactor, depthFactor));
        gradient.SetColor(1, trunkColor * new Color(depthFactor * 0.9f, depthFactor * 0.9f, depthFactor * 0.9f));
        branch.Gradient = gradient;
        
        // Get end position for child branches
        Vector2 endPos = currentPos;
        
        // Determine number of child branches
        int branchCount = random.Next(minBranches, maxBranches + 1);
        
        // Reduce branches at higher depths for performance and realism
        if (depth > maxDepth * 0.7f)
        {
            branchCount = Math.Min(2, branchCount);
        }
        
        // Create child branches
        for (int i = 0; i < branchCount; i++)
        {
            // Calculate branch angle with organic variation
            float baseAngle = Mathf.DegToRad(branchAngle);
            float variation = Mathf.DegToRad(angleVariation) * (float)(random.NextDouble() - 0.5) * 2;
            float asymmetryFactor = (float)(random.NextDouble() - 0.5) * asymmetry;
            
            float angle;
            if (branchCount == 2)
            {
                // Binary branching - symmetric with variation
                angle = (i == 0 ? -baseAngle : baseAngle) + variation + asymmetryFactor;
            }
            else
            {
                // Multiple branches - distribute around
                float angleStep = baseAngle * 2 / (branchCount - 1);
                angle = -baseAngle + angleStep * i + variation;
            }
            
            // Add parent angle influence for natural flow
            angle += parentAngle * 0.2f;
            
            // Calculate new branch properties
            Vector2 newDir = currentDir.Rotated(angle);
            float lengthVariation = 0.8f + (float)random.NextDouble() * 0.4f;
            float newLength = length * lengthReduction * lengthVariation;
            float newThickness = thickness * thicknessReduction;
            
            // Recursively grow child branch
            GrowBranch(endPos, newDir, newLength, newThickness, depth + 1, angle);
        }
    }
    
    // Place apples on branches
    private void PlaceApples()
    {
        // Filter suitable branches (not too deep, not the trunk)
        var suitableBranches = new List<BranchData>();
        foreach (var branch in branches)
        {
            // Skip trunk (depth 0) and very thin branches
            if (branch.Depth > 0 && branch.Depth < maxDepth - 1 && branch.Thickness > 2.0f)
            {
                suitableBranches.Add(branch);
            }
        }
        
        if (suitableBranches.Count == 0)
        {
            GD.PrintErr("No suitable branches for apples!");
            return;
        }
        
        // Place apples
        for (int i = 0; i < appleQuantity; i++)
        {
            // Select a random suitable branch
            var selectedBranch = suitableBranches[random.Next(suitableBranches.Count)];
            
            // Select a random point along the branch (avoid the very base)
            int pointIndex = random.Next(1, selectedBranch.Points.Length);
            Vector2 applePosition = selectedBranch.Points[pointIndex];
            
            // Add slight offset from branch center
            float offsetAngle = (float)(random.NextDouble() * Math.PI * 2);
            float offsetDistance = selectedBranch.Thickness * 0.5f + 5.0f; // Slightly outside branch
            Vector2 offset = new Vector2(
                Mathf.Cos(offsetAngle) * offsetDistance,
                Mathf.Sin(offsetAngle) * offsetDistance
            );
            
            // Instantiate apple
            var apple = applePrefab.Instantiate<Node2D>();
            apple.Position = applePosition + offset;
            
            // Random slight rotation for variety
            apple.Rotation = (float)(random.NextDouble() * 0.3 - 0.15);
            
            // Optionally scale based on branch thickness
            float scale = 0.8f + (selectedBranch.Thickness / initialThickness) * 0.4f;
            apple.Scale = Vector2.One * scale;
            
            AddChild(apple);
            appleInstances.Add(apple);
        }
    }
    
    // Public method to regenerate the tree at runtime
    public void Regenerate()
    {
        GenerateTree();
    }
    
    // Method to change seed and regenerate
    public void SetSeedAndRegenerate(int newSeed)
    {
        seed = newSeed;
        GenerateTree();
    }
    
    // Method to randomize and regenerate
    public void RandomizeAndRegenerate()
    {
        seed = (int)GD.RandRange(0, 999999);
        GenerateTree();
    }
}