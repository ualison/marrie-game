using Godot;
using System;
using System.Collections.Generic;

public partial class OrganicFractalTree : Node2D
{
    // === GENERATION SETTINGS ===
    [ExportGroup("Generation")]
    [Export] private int seed = 12345; // Random seed for tree generation - same seed = same tree
    [ExportGroup("Generation")]
    [Export] private bool randomizeOnLoad = true; // Use random seed each time instead of fixed seed
    [ExportGroup("Generation")]
    [Export] private int maxDepth = 10; // Maximum branch generations - higher = more detail but slower
    
    // === TRUNK AND BRANCH DIMENSIONS ===
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export] private float initialLength = 120.0f; // Main trunk length in pixels
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export(PropertyHint.Range, "0.5,0.9")] private float lengthReduction = 0.72f; // How much each branch shrinks (0.72 = 72% of parent)
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export(PropertyHint.Range, "0.1,20.0")] private float initialThickness = 12.0f; // Starting trunk thickness
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export(PropertyHint.Range, "0.4,0.9")] private float thicknessReduction = 0.68f; // How much branch thickness shrinks each generation
    
    // === BRANCHING BEHAVIOR ===
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "10,90")] private float branchAngle = 28.0f; // Base angle between branches in degrees
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,45")] private float angleVariation = 18.0f; // Random variation in branch angles
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,0.8")] private float curvature = 0.15f; // How much branches curve as they grow
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "2,6")] private int minBranches = 2; // Minimum branches per split
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "2,6")] private int maxBranches = 4; // Maximum branches per split
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,1")] private float asymmetry = 0.4f; // How uneven branch angles can be
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,1")] private float branchProbability = 0.85f; // Chance a branch will split (creates more natural gaps)
    
    // === TRUNK SHAPE ===
    [ExportGroup("Trunk Shape")]
    [Export(PropertyHint.Range, "0,1")] private float trunkTaper = 0.3f; // How much the trunk narrows as it goes up
    [ExportGroup("Trunk Shape")]
    [Export(PropertyHint.Range, "0,30")] private float trunkCurve = 8.0f; // How much the main trunk can curve
    
    // === VISUAL APPEARANCE ===
    [ExportGroup("Visual")]
    [Export] private Color trunkColor = new Color(0.4f, 0.3f, 0.2f); // Main trunk/branch color (brown)
    [ExportGroup("Visual")]
    [Export] private Color leafColor = new Color(0.2f, 0.6f, 0.3f); // Leaf color (green)
    [ExportGroup("Visual")]
    [Export] private bool drawLeaves = true; // Whether to draw leaves at branch tips
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "1,15")] private float leafSize = 4.0f; // Size of individual leaves
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "1,20")] private int leafDensity = 8; // How many leaf clusters per branch tip
    [ExportGroup("Visual")]
    [Export] private bool useGradient = true; // Apply color gradient from trunk to branches
    
    // === APPLE SETTINGS ===
    [ExportGroup("Apples")]
    [Export] private bool hasApples = false; // Whether to place apples on the tree
    [ExportGroup("Apples")]
    [Export(PropertyHint.Range, "1,50")] private int appleQuantity = 15; // Number of apples to place
    [ExportGroup("Apples")]
    [Export] private PackedScene applePrefab; // Apple scene to instantiate
    
    private Random random;
    // Precomputed leaf clusters (so _Draw() does minimal work)
    private List<LeafCluster> leafClusters = new List<LeafCluster>();
    private List<BranchData> branches = new List<BranchData>();
    private List<Node2D> appleInstances = new List<Node2D>();
    // Cache gradients per depth to avoid allocating many Gradient objects
    private Dictionary<int, Gradient> gradientCache = new Dictionary<int, Gradient>();
    
    // Store branch data for apple placement and visual effects
    private class BranchData
    {
        public Line2D Line { get; set; }
        public int Depth { get; set; }
        public Vector2[] Points { get; set; }
        public float Thickness { get; set; }
        public Vector2 EndDirection { get; set; } // Direction the branch is growing
    }

    // Helper to detect whether the current scene contains a node of a given type T
    private bool SceneContainsNodeOfType<T>(Node root) where T : Node
    {
        if (root == null) return false;
        if (root is T) return true;
        foreach (Node child in root.GetChildren())
        {
            if (SceneContainsNodeOfType<T>(child)) return true;
        }
        return false;
    }

    // Small types to hold precomputed leaf drawing data so _Draw() is cheap
    private class LeafFragment
    {
        public Vector2 Offset;
        public float Size;
        public Color Color;
    }

    private class LeafCluster
    {
        public Vector2 Position;
        public List<LeafFragment> Fragments = new List<LeafFragment>();
    }
    
    public override void _Ready()
    {
        // Set up random seed
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
        // Draw precomputed leaf clusters. This avoids doing random math per-frame.
        if (drawLeaves && leafClusters.Count > 0)
        {
            for (int i = 0; i < leafClusters.Count; i++)
            {
                var cluster = leafClusters[i];

                // Draw all fragments precomputed for this cluster
                for (int f = 0; f < cluster.Fragments.Count; f++)
                {
                    var frag = cluster.Fragments[f];
                    DrawCircle(cluster.Position + frag.Offset, frag.Size, frag.Color);
                }
            }
        }
    }

    // Create and store a leaf cluster with precomputed fragments for fast drawing.
    private void AddLeafCluster(Vector2 position)
    {
        if (random == null)
            random = new Random(seed);

        var cluster = new LeafCluster();
        cluster.Position = position;

        // Main leaf fragment
        cluster.Fragments.Add(new LeafFragment
        {
            Offset = Vector2.Zero,
            Size = leafSize,
            Color = leafColor
        });

        int actualLeafDensity = Math.Min(leafDensity, 6);

        // Add smaller fragments around the main cluster
        for (int j = 0; j < actualLeafDensity; j++)
        {
            float angle = (float)(random.NextDouble() * (Mathf.Pi * 2f));
            float distance = (leafSize * 0.5f) + (float)random.NextDouble() * (leafSize * 0.5f);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            float smallLeafSize = leafSize * (0.3f + (float)random.NextDouble() * 0.3f);

            float colorVariation = 0.8f + (float)random.NextDouble() * 0.4f;
            float alpha = 0.5f + (float)random.NextDouble() * 0.4f;
            Color leafVariation = leafColor * new Color(colorVariation, colorVariation, colorVariation, alpha);

            cluster.Fragments.Add(new LeafFragment
            {
                Offset = offset,
                Size = smallLeafSize,
                Color = leafVariation
            });
        }

        leafClusters.Add(cluster);
    }
    
    private void GenerateTree()
    {
        GD.Print("Generating tree...");
        
        // Clear existing tree
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
        leafClusters.Clear();
        branches.Clear();
        appleInstances.Clear();
        gradientCache.Clear();
        
        // Initialize random generator
        random = new Random(seed);
        
    // Start growing from the base
        Vector2 startPos = Vector2.Zero;
        Vector2 startDir = Vector2.Up; // Points upward
        
        // Create the tree structure
        GrowBranch(startPos, startDir, initialLength, initialThickness, 0, 0, true);
        
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
        
    GD.Print($"Tree generated with {branches.Count} branches and {leafClusters.Count} leaf clusters");
        if (hasApples)
        {
            GD.Print($"Placed {appleInstances.Count} apples");
        }
    }
    
    private void GrowBranch(Vector2 startPos, Vector2 direction, float length, float thickness, int depth, float parentAngle, bool isTrunk = false)
    {
        // Stop growing if we've reached limits
        if (depth > maxDepth || length < 3.0f || thickness < 0.8f)
        {
            // Add multiple leaf positions at branch tips for fuller canopy
            if (drawLeaves)
            {
                // Create a few small clusters around the tip to simulate fullness. We precompute
                // the leaf fragments here so _Draw() doesn't run heavy random math every frame.
                for (int i = 0; i < 3; i++)
                {
                    Vector2 leafOffset = new Vector2(
                        (float)(random.NextDouble() - 0.5) * leafSize * 2,
                        (float)(random.NextDouble() - 0.5) * leafSize * 2
                    );
                    AddLeafCluster(startPos + leafOffset);
                }
            }
            return;
        }
        
        // Create branch visual
        Line2D branch = new Line2D();
        AddChild(branch);
        
        // Configure branch appearance
        branch.Width = thickness;
        branch.JointMode = Line2D.LineJointMode.Round;
        branch.Antialiased = true;
        
        // Generate organic curved path
        var points = new List<Vector2>();
        Vector2 currentPos = startPos;
        Vector2 currentDir = direction.Normalized();
        
        // More segments for smoother curves
        int segments = Math.Max(4, (int)(length / 8));
        float segmentLength = length / segments;
        
        points.Add(currentPos);
        
        // Create curved path with trunk tapering
        for (int i = 0; i < segments; i++)
        {
            float progress = (float)i / segments;
            
            // Apply trunk curve for main trunk
            if (isTrunk && trunkCurve > 0)
            {
                float trunkCurveAmount = Mathf.DegToRad(trunkCurve) * (float)(random.NextDouble() - 0.5) * 0.3f;
                currentDir = currentDir.Rotated(trunkCurveAmount);
            }
            
            // Add natural curvature
            float curveAmount = (float)(random.NextDouble() - 0.5) * curvature;
            currentDir = currentDir.Rotated(curveAmount);
            currentDir = currentDir.Normalized();
            
            // Apply trunk tapering
            if (isTrunk && trunkTaper > 0)
            {
                float taperFactor = 1.0f - (progress * trunkTaper);
                branch.Width = thickness * taperFactor;
            }
            
            // Move to next point
            currentPos += currentDir * segmentLength;
            points.Add(currentPos);
        }
        
        // Apply points to branch
        branch.Points = points.ToArray();
        
        // Set branch color with gradient. Cache gradients per depth to reduce allocations.
        if (useGradient)
        {
            Gradient gradient;
            int key = depth;
            if (!gradientCache.TryGetValue(key, out gradient))
            {
                gradient = new Gradient();
                float depthFactor = 1.0f - (depth / (float)maxDepth) * 0.4f;
                Color baseColor = trunkColor;

                // Gradually shift to green for smaller branches
                if (depth > maxDepth * 0.6f)
                {
                    float greenBlend = (depth - maxDepth * 0.6f) / (maxDepth * 0.4f);
                    baseColor = trunkColor.Lerp(leafColor * 0.7f, greenBlend * 0.5f);
                }

                gradient.SetColor(0, baseColor * new Color(depthFactor, depthFactor, depthFactor));
                gradient.SetColor(1, baseColor * new Color(depthFactor * 0.8f, depthFactor * 0.8f, depthFactor * 0.8f));
                gradientCache[key] = gradient;
            }
            branch.Gradient = gradient;
        }
        else
        {
            branch.DefaultColor = trunkColor;
        }
        
        // Store branch data
        var branchData = new BranchData
        {
            Line = branch,
            Depth = depth,
            Points = points.ToArray(),
            Thickness = thickness,
            EndDirection = currentDir
        };
        branches.Add(branchData);
        
        Vector2 endPos = currentPos;
        
        // Check if this branch should split (adds natural variation)
        if (random.NextDouble() > branchProbability)
        {
            // This branch doesn't split - just add a leaf cluster at the tip
            if (drawLeaves)
            {
                AddLeafCluster(endPos);
            }
            return;
        }
        
        // Determine number of child branches
        int branchCount = random.Next(minBranches, maxBranches + 1);
        
        // Reduce branching at higher depths for performance and realism
        if (depth > maxDepth * 0.7f)
        {
            branchCount = Math.Min(2, branchCount);
        }
        
        // Increase branching chance for main trunk to create fuller tree
        if (isTrunk)
        {
            branchCount = Math.Max(branchCount, 3);
        }
        
        // Create child branches
        for (int i = 0; i < branchCount; i++)
        {
            // Calculate branch angles with more natural distribution
            float baseAngle = Mathf.DegToRad(branchAngle);
            float variation = Mathf.DegToRad(angleVariation) * (float)(random.NextDouble() - 0.5) * 2;
            float asymmetryFactor = (float)(random.NextDouble() - 0.5) * asymmetry;
            
            float angle;
            if (branchCount == 2)
            {
                // Binary branching
                angle = (i == 0 ? -baseAngle : baseAngle) + variation + asymmetryFactor;
            }
            else
            {
                // Multiple branches - better distribution
                float angleRange = baseAngle * 1.8f; // Slightly wider spread
                angle = -angleRange/2 + (angleRange / (branchCount - 1)) * i + variation;
            }
            
            // Add slight influence from parent angle for natural flow
            angle += parentAngle * 0.15f;
            
            // Calculate new branch properties with more variation
            Vector2 newDir = currentDir.Rotated(angle);
            float lengthVariation = 0.7f + (float)random.NextDouble() * 0.5f; // More length variation
            float newLength = length * lengthReduction * lengthVariation;
            float newThickness = thickness * thicknessReduction;
            
            // Add slight random delay to create more organic growth
            if (random.NextDouble() > 0.1f) // 90% chance to grow
            {
                GrowBranch(endPos, newDir, newLength, newThickness, depth + 1, angle, false);
            }
        }
    }
    
    // Place apples on suitable branches
    private void PlaceApples()
    {
        // If the scene already contains an AppleTree manager that handles apple spawning,
        // don't procedurally place apples here to avoid duplicate apples.
        if (GetTree().CurrentScene != null && SceneContainsNodeOfType<AppleTree>(GetTree().CurrentScene))
        {
            GD.Print("OrganicFractalTree: AppleTree manager found in scene; skipping procedural apple placement.");
            return;
        }

        // Find branches suitable for apples (not too thin, not the trunk, not too deep)
        var suitableBranches = new List<BranchData>();
        foreach (var branch in branches)
        {
            if (branch.Depth > 1 && branch.Depth < maxDepth - 2 && branch.Thickness > 3.0f)
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
        for (int i = 0; i < appleQuantity && i < suitableBranches.Count * 2; i++)
        {
            var selectedBranch = suitableBranches[random.Next(suitableBranches.Count)];

            // Prefer outer portion of the branch for placement
            int minIndex = Math.Max(1, selectedBranch.Points.Length / 3);
            int pointIndex = random.Next(minIndex, selectedBranch.Points.Length);
            Vector2 applePosition = selectedBranch.Points[pointIndex];

            // Compute local tangent and normal so we offset apples toward the branch surface
            Vector2 tangent;
            if (pointIndex >= selectedBranch.Points.Length - 1)
                tangent = selectedBranch.Points[pointIndex] - selectedBranch.Points[pointIndex - 1];
            else
                tangent = selectedBranch.Points[pointIndex + 1] - selectedBranch.Points[pointIndex];

            if (tangent.Length() <= 0.0001f)
                tangent = selectedBranch.EndDirection;

            Vector2 normal = new Vector2(-tangent.Y, tangent.X).Normalized();

            // Push the apple toward the branch (inward) so it sits on/against the branch instead of floating away.
            float baseInset = Mathf.Max(0f, selectedBranch.Thickness * 0.4f);
            float jitter = (float)(random.NextDouble() * selectedBranch.Thickness * 0.2f);
            Vector2 offset = -normal * (baseInset + jitter);

            // Small along-tangent jitter so apples don't all overlap exactly on the line
            float alongJitter = (float)(random.NextDouble() * (selectedBranch.Thickness * 0.1f) - selectedBranch.Thickness * 0.05f);
            offset += tangent.Normalized() * alongJitter;

            // Create apple instance
            var apple = applePrefab.Instantiate<Node2D>();
            apple.Position = applePosition + offset;
            apple.Rotation = (float)(random.NextDouble() * 0.4 - 0.2);

            // Scale based on branch thickness
            float scale = 0.7f + (selectedBranch.Thickness / initialThickness) * 0.5f;
            apple.Scale = Vector2.One * scale;

            AddChild(apple);
            appleInstances.Add(apple);
        }
    }
    
    // === PUBLIC METHODS FOR RUNTIME CONTROL ===
    
    // Regenerate the tree with current settings
    public void Regenerate()
    {
        GenerateTree();
    }
    
    // Change seed and regenerate
    public void SetSeedAndRegenerate(int newSeed)
    {
        seed = newSeed;
        GenerateTree();
    }
    
    // Use random seed and regenerate
    public void RandomizeAndRegenerate()
    {
        seed = (int)GD.RandRange(0, 999999);
        GenerateTree();
    }
    
    // Animate tree growth (call this to watch it grow)
    public async void AnimateGrowth()
    {
        // Hide all branches initially
        foreach (var branchData in branches)
        {
            branchData.Line.Modulate = new Color(1, 1, 1, 0);
        }
        
        // Gradually show branches by depth
        for (int depth = 0; depth <= maxDepth; depth++)
        {
            foreach (var branchData in branches)
            {
                if (branchData.Depth == depth)
                {
                    var tween = CreateTween();
                    tween.TweenProperty(branchData.Line, "modulate:a", 1.0f, 0.3f);
                }
            }
            await ToSignal(GetTree().CreateTimer(0.2f), SceneTreeTimer.SignalName.Timeout);
        }
        
        // Finally show leaves
        QueueRedraw();
    }
}