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
    [Export(PropertyHint.Range, "1,200")] private float appleMinDistance = 20.0f; // Minimum distance (px) between apples
    [ExportGroup("Apples")]
    [Export(PropertyHint.Range, "0,1")] private float applePlacementHeight = 0.6f; // Preferred placement height fraction along branch (0..1)
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

        // Build candidate positions from suitable branches
        var candidates = new List<(Vector2 pos, Vector2 normal, float frac, BranchData branch)>();

        foreach (var branch in suitableBranches)
        {
            int len = branch.Points.Length;
            if (len < 2) continue;

            // sample a few points along the outer portion of the branch (near tip)
            int start = Math.Max(1, (int)(len * 0.3f));
            for (int s = 0; s < 3; s++) // up to 3 samples per branch
            {
                float fracJitter = ((float)s - 1) * 0.08f; // -0.08, 0, +0.08
                float frac = Mathf.Clamp(applePlacementHeight + fracJitter + ((float)random.NextDouble() - 0.5f) * 0.06f, 0f, 1f);
                int idx = start + (int)(frac * (len - start - 1));
                idx = Mathf.Clamp(idx, 1, len - 1);

                Vector2 applePosition = branch.Points[idx];

                Vector2 tangent;
                if (idx >= len - 1)
                    tangent = branch.Points[idx] - branch.Points[idx - 1];
                else
                    tangent = branch.Points[idx + 1] - branch.Points[idx];

                if (tangent.Length() <= 0.0001f)
                    tangent = branch.EndDirection;

                Vector2 normal = new Vector2(-tangent.Y, tangent.X).Normalized();
                float baseInset = Mathf.Max(0f, branch.Thickness * 0.4f);
                float jitterInset = (float)(random.NextDouble() * branch.Thickness * 0.2f);
                Vector2 offset = -normal * (baseInset + jitterInset);
                float alongJitter = (float)(random.NextDouble() * (branch.Thickness * 0.1f) - branch.Thickness * 0.05f);
                offset += tangent.Normalized() * alongJitter;

                Vector2 finalPos = applePosition + offset;

                candidates.Add((finalPos, normal, frac, branch));
            }
        }

        int target = Math.Min(appleQuantity, suitableBranches.Count * 2);
        var chosen = new List<(Vector2 pos, Vector2 normal, float frac, BranchData branch)>();

        // Helper: compute min distance from pos to already chosen
        float MinDistToChosen(Vector2 p)
        {
            float md = float.MaxValue;
            foreach (var c in chosen)
            {
                float d = c.pos.DistanceTo(p);
                if (d < md) md = d;
            }
            return chosen.Count == 0 ? float.MaxValue : md;
        }

        // Score candidate prefering closeness to applePlacementHeight and distance from others
        float ScoreCandidate((Vector2 pos, Vector2 normal, float frac, BranchData branch) c)
        {
            float prefScore = 1.0f - Math.Abs(c.frac - applePlacementHeight); // closer fraction higher
            // add minor preference for thicker branches
            float thicknessScore = c.branch.Thickness / (initialThickness + 0.0001f);
            return prefScore * 0.7f + thicknessScore * 0.3f;
        }

        // Greedy selection: prefer pairs when possible for even counts
        while (chosen.Count < target && candidates.Count > 0)
        {
            int need = target - chosen.Count;

            if (need >= 2)
            {
                // try to find best pair (opposite-ish normals and similar frac)
                float bestPairScore = float.MinValue;
                int bestA = -1, bestB = -1;

                for (int a = 0; a < candidates.Count; a++)
                {
                    var ca = candidates[a];
                    for (int b = a + 1; b < candidates.Count; b++)
                    {
                        var cb = candidates[b];
                        // avoid same branch exact same index roughly
                        if (ca.branch == cb.branch && Math.Abs(ca.frac - cb.frac) < 0.02f) continue;

                        float normalDot = ca.normal.Dot(cb.normal);
                        // prefer roughly opposite sides or at least different directions
                        if (normalDot > 0.7f) continue;

                        float pairScore = ScoreCandidate(ca) + ScoreCandidate(cb);
                        // prefer pairs that are well separated
                        float sep = ca.pos.DistanceTo(cb.pos);
                        pairScore += Math.Min(sep / (appleMinDistance + 0.0001f), 2.0f);

                        if (pairScore > bestPairScore)
                        {
                            bestPairScore = pairScore;
                            bestA = a; bestB = b;
                        }
                    }
                }

                if (bestA != -1 && bestB != -1)
                {
                    // ensure they don't violate min distance with already chosen
                    var ca = candidates[bestA];
                    var cb = candidates[bestB];
                    if (MinDistToChosen(ca.pos) >= appleMinDistance && MinDistToChosen(cb.pos) >= appleMinDistance && ca.pos.DistanceTo(cb.pos) >= appleMinDistance)
                    {
                        chosen.Add(ca);
                        chosen.Add(cb);
                        // remove higher index first
                        if (bestA > bestB) { candidates.RemoveAt(bestA); candidates.RemoveAt(bestB); }
                        else { candidates.RemoveAt(bestB); candidates.RemoveAt(bestA); }
                        continue;
                    }
                }
            }

            // otherwise pick single best candidate that respects min distance
            float bestScore = float.MinValue;
            int bestIdx = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                float md = MinDistToChosen(c.pos);
                if (md < appleMinDistance) continue; // too close to chosen

                float score = ScoreCandidate(c) + (md == float.MaxValue ? 2.0f : Math.Min(md / (appleMinDistance + 0.0001f), 2.0f));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx != -1)
            {
                chosen.Add(candidates[bestIdx]);
                candidates.RemoveAt(bestIdx);
            }
            else
            {
                // no more viable candidates
                break;
            }
        }

        // Instantiate chosen apples
        foreach (var c in chosen)
        {
            var apple = applePrefab.Instantiate<Node2D>();
            apple.Position = c.pos;
            apple.Rotation = (float)(random.NextDouble() * 0.4 - 0.2);
            float scale = 0.7f + (c.branch.Thickness / initialThickness) * 0.5f;
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