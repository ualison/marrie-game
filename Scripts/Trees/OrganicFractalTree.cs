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
    [Export(PropertyHint.Range, "0,1")] private float branchProbability = 0.55f; // Chance a branch will split (creates more natural gaps)

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
    private List<BranchData> branches = new List<BranchData>();
    private List<Node2D> appleInstances = new List<Node2D>();
    // Cache gradients per depth to avoid allocating many Gradient objects
    private Dictionary<int, Gradient> gradientCache = new Dictionary<int, Gradient>();
    // When true, _Draw should perform the full batch draw; afterwards it will be cleared
    // to avoid re-executing expensive draw code every frame.
    private bool needFullRedraw = true;

    // === OPTIMIZED LEAF SYSTEM ===
    private LeafTemplate cachedLeafTemplate;
    private bool leafTemplateCached = false;
    private List<OptimizedLeafCluster> optimizedLeafClusters = new List<OptimizedLeafCluster>();

    // Store branch data for apple placement and visual effects
    private class BranchData
    {
        public int Depth { get; set; }
        public Vector2[] Points { get; set; }
        public float Thickness { get; set; }
        public Vector2 EndDirection { get; set; } // Direction the branch is growing
        public Color Color { get; set; } = new Color(0, 0, 0, 1);
        public float Alpha { get; set; } = 1.0f; // Used for simple growth animation
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

    // Cached leaf template system
    private class LeafFragment
    {
        public Vector2 Offset;
        public float Size;
        public Color Color;
    }

    private class LeafTemplate
    {
        public List<LeafFragment> BaseFragments { get; set; } = new List<LeafFragment>();
        public float TemplateRadius { get; set; } // Used for scaling bounds
    }

    // Optimized leaf cluster that references the cached template
    private class OptimizedLeafCluster
    {
        public Vector2 Position;
        public float Scale = 1.0f;
        public float Rotation = 0.0f;
        public Color Tint = new Color(1f, 1f, 1f, 1f);
    }

    public override void _Ready()
    {
        // Set up random seed
        if (randomizeOnLoad)
        {
            seed = (int)GD.RandRange(0, 999999);
            Log.System($"Using random seed: {seed}", "OrganicFractalTree");
        }
        else
        {
            Log.System($"Using fixed seed: {seed}", "OrganicFractalTree");
        }

        GenerateTree();
    }

    public override void _Draw()
    {
        // Avoid redoing the heavy batch draw if nothing changed since last draw.
        if (!needFullRedraw)
            return;

        // Batch-draw branches to reduce node count and draw calls.
        if (branches.Count > 0)
        {
            for (int b = 0; b < branches.Count; b++)
            {
                var br = branches[b];
                if (br.Points == null || br.Points.Length < 2) continue;
                Color c = br.Color * new Color(1f, 1f, 1f, br.Alpha);

                for (int i = 0; i < br.Points.Length - 1; i++)
                {
                    DrawLine(br.Points[i], br.Points[i + 1], c, br.Thickness);
                }
            }
        }

        // Draw optimized leaf clusters using cached template
        if (drawLeaves && optimizedLeafClusters.Count > 0 && leafTemplateCached)
        {
            Color baseLeafColor = leafColor;

            for (int i = 0; i < optimizedLeafClusters.Count; i++)
            {
                var cluster = optimizedLeafClusters[i];
                float cosRot = Mathf.Cos(cluster.Rotation);
                float sinRot = Mathf.Sin(cluster.Rotation);

                // Draw all fragments from the cached template
                for (int f = 0; f < cachedLeafTemplate.BaseFragments.Count; f++)
                {
                    var templateFrag = cachedLeafTemplate.BaseFragments[f];

                    // Transform the template offset by rotation and scale
                    Vector2 rotatedOffset = new Vector2(
                        templateFrag.Offset.X * cosRot - templateFrag.Offset.Y * sinRot,
                        templateFrag.Offset.X * sinRot + templateFrag.Offset.Y * cosRot
                    ) * cluster.Scale * leafSize;

                    Vector2 finalPos = cluster.Position + rotatedOffset;
                    float finalSize = templateFrag.Size * cluster.Scale * leafSize;
                    Color finalColor = (baseLeafColor * cluster.Tint * templateFrag.Color);

                    DrawCircle(finalPos, finalSize, finalColor);
                }
            }
        }

        // We've completed the full draw; avoid doing it again until tree changes or we re-enable (e.g. during animation).
        needFullRedraw = false;
    }

    // Method to create and cache the leaf template (called once per tree generation)
    private void CreateLeafTemplate()
    {
        if (leafTemplateCached) return;

        if (random == null)
            random = new Random(seed);

        cachedLeafTemplate = new LeafTemplate();

        // Create main leaf fragment at center
        cachedLeafTemplate.BaseFragments.Add(new LeafFragment
        {
            Offset = Vector2.Zero,
            Size = 1.0f, // Normalized size - will be scaled later
            Color = new Color(1f, 1f, 1f, 1f) // Use white for tinting
        });

        int actualLeafDensity = Math.Min(leafDensity, 8); // Slightly higher cap for template
        float maxRadius = 0f;

        // Add smaller fragments around the main cluster
        for (int j = 0; j < actualLeafDensity; j++)
        {
            float angle = (float)(random.NextDouble() * (Mathf.Pi * 2f));
            float distance = 0.5f + (float)random.NextDouble() * 0.5f; // Normalized distance
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            maxRadius = Math.Max(maxRadius, distance + 0.3f); // Account for fragment size

            float smallLeafSize = 0.3f + (float)random.NextDouble() * 0.3f; // Normalized size
            float alpha = 0.5f + (float)random.NextDouble() * 0.4f;

            cachedLeafTemplate.BaseFragments.Add(new LeafFragment
            {
                Offset = offset,
                Size = smallLeafSize,
                Color = new Color(1f, 1f, 1f, alpha) // White with varied alpha for tinting
            });
        }

        cachedLeafTemplate.TemplateRadius = maxRadius;
        leafTemplateCached = true;

        Log.System($"Leaf template cached with {cachedLeafTemplate.BaseFragments.Count} fragments", "OrganicFractalTree");
    }

    // Optimized method to add leaf clusters using the cached template
    private void AddOptimizedLeafCluster(Vector2 position)
    {
        if (!leafTemplateCached)
            CreateLeafTemplate();

        if (random == null)
            random = new Random(seed);

        var cluster = new OptimizedLeafCluster
        {
            Position = position,
            Scale = 0.8f + (float)random.NextDouble() * 0.4f, // Vary scale slightly
            Rotation = (float)(random.NextDouble() * Mathf.Pi * 2), // Random rotation
            Tint = new Color(
                0.8f + (float)random.NextDouble() * 0.4f, // Slight color variation
                0.9f + (float)random.NextDouble() * 0.2f,
                0.7f + (float)random.NextDouble() * 0.3f,
                0.9f + (float)random.NextDouble() * 0.1f
            )
        };

        optimizedLeafClusters.Add(cluster);
    }

    private void GenerateTree()
    {
        Log.System("Generating tree...", "OrganicFractalTree");

        // Clear existing tree
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        // Clear optimized leaf clusters instead of old ones
        optimizedLeafClusters.Clear();
        branches.Clear();
        appleInstances.Clear();
        gradientCache.Clear();

        // Reset leaf template cache if needed
        leafTemplateCached = false;

        // Initialize random generator
        random = new Random(seed);

        // Create the leaf template first
        CreateLeafTemplate();

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
            Log.Error("Apple prefab not set!", "OrganicFractalTree");
        }

        // Request redraw for leaves
        needFullRedraw = true;
        QueueRedraw();

        Log.Info($"Tree generated with {branches.Count} branches and {optimizedLeafClusters.Count} leaf clusters", "OrganicFractalTree");
        if (hasApples)
        {
            Log.Info($"Placed {appleInstances.Count} apples", "OrganicFractalTree");
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
                // Create a few small clusters around the tip using optimized system
                for (int i = 0; i < 1; i++)
                {
                    Vector2 leafOffset = new Vector2(
                        (float)(random.NextDouble() - 0.5) * leafSize * 2,
                        (float)(random.NextDouble() - 0.5) * leafSize * 2
                    );
                    AddOptimizedLeafCluster(startPos + leafOffset);
                }
            }
            return;
        }

        // Generate organic curved path
        var points = new List<Vector2>();
        Vector2 currentPos = startPos;
        Vector2 currentDir = direction.Normalized();

        // More segments for smoother curves
        int segments = Math.Max(3, (int)(length / 12));
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

            // Move to next point
            currentPos += currentDir * segmentLength;
            points.Add(currentPos);
        }

        // Compute a single representative color for the branch
        float depthFactor = 1.0f - (depth / (float)maxDepth) * 0.4f;
        Color baseColor = trunkColor;
        if (depth > maxDepth * 0.6f)
        {
            float greenBlend = (depth - maxDepth * 0.6f) / (maxDepth * 0.4f);
            baseColor = trunkColor.Lerp(leafColor * 0.7f, greenBlend * 0.5f);
        }
        Color branchColor = baseColor * new Color(depthFactor, depthFactor, depthFactor, 1.0f);

        // Store branch data
        var branchData = new BranchData
        {
            Depth = depth,
            Points = points.ToArray(),
            Thickness = thickness,
            EndDirection = currentDir,
            Color = branchColor,
            Alpha = 1.0f
        };
        branches.Add(branchData);

        Vector2 endPos = currentPos;

        // Check if this branch should split (adds natural variation)
        if (random.NextDouble() > branchProbability)
        {
            // This branch doesn't split - just add a leaf cluster at the tip
            if (drawLeaves)
            {
                AddOptimizedLeafCluster(endPos);
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
                angle = -angleRange / 2 + (angleRange / (branchCount - 1)) * i + variation;
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
            Log.System("AppleTree manager found in scene; skipping procedural apple placement.", "OrganicFractalTree");
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
            Log.Error("No suitable branches for apples!", "OrganicFractalTree");
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
        // Ensure we perform full draw while animating
        needFullRedraw = true;

        // Hide all branches initially by setting alpha to zero
        foreach (var branchData in branches)
        {
            branchData.Alpha = 0f;
        }

        // Gradually show branches by depth (animate alpha)
        for (int depth = 0; depth <= maxDepth; depth++)
        {
            foreach (var branchData in branches)
            {
                if (branchData.Depth == depth)
                {
                    // simple linear tween via timer loop to avoid creating per-branch Tweens
                    float elapsed = 0f;
                    float duration = 0.3f;
                    while (elapsed < duration)
                    {
                        await ToSignal(GetTree().CreateTimer(0.03f), SceneTreeTimer.SignalName.Timeout);
                        elapsed += 0.03f;
                        branchData.Alpha = Mathf.Clamp(elapsed / duration, 0f, 1f);
                        needFullRedraw = true;
                        QueueRedraw();
                    }
                    branchData.Alpha = 1f;
                }
            }
            await ToSignal(GetTree().CreateTimer(0.2f), SceneTreeTimer.SignalName.Timeout);
        }

        // Finally show leaves
        needFullRedraw = true;
        QueueRedraw();
    }

    // === ADDITIONAL OPTIMIZATION METHODS ===

    // Method to invalidate leaf template (call if leafDensity or leafSize changes significantly)
    public void InvalidateLeafTemplate()
    {
        leafTemplateCached = false;
        Log.System("Leaf template invalidated - will regenerate on next tree creation", "OrganicFractalTree");
    }

    // Method to get performance statistics
    public Dictionary<string, object> GetPerformanceStats()
    {
        return new Dictionary<string, object>
        {
            {"BranchCount", branches.Count},
            {"LeafClusterCount", optimizedLeafClusters.Count},
            {"LeafTemplateFragments", leafTemplateCached ? cachedLeafTemplate.BaseFragments.Count : 0},
            {"AppleCount", appleInstances.Count},
            {"MemoryEstimate", GetEstimatedMemoryUsage()}
        };
    }

    // Estimate memory usage of the tree (in bytes)
    private long GetEstimatedMemoryUsage()
    {
        long total = 0;

        // Branch data
        total += branches.Count * (sizeof(int) + sizeof(float) * 3 + 16 * 4 + 16); // Rough estimate per branch
        foreach (var branch in branches)
        {
            if (branch.Points != null)
                total += branch.Points.Length * 8; // Vector2 = 8 bytes
        }

        // Optimized leaf clusters (much smaller than old system)
        total += optimizedLeafClusters.Count * (8 + sizeof(float) * 2 + 16); // Position + scale + rotation + tint

        // Leaf template (shared)
        if (leafTemplateCached)
        {
            total += cachedLeafTemplate.BaseFragments.Count * (8 + sizeof(float) + 16); // Offset + size + color
        }

        return total;
    }

    // Method to force a redraw (useful for debugging or external changes)
    public void ForceRedraw()
    {
        needFullRedraw = true;
        QueueRedraw();
    }

    // Method to get leaf cluster positions (useful for effects like wind animation)
    public Vector2[] GetLeafClusterPositions()
    {
        var positions = new Vector2[optimizedLeafClusters.Count];
        for (int i = 0; i < optimizedLeafClusters.Count; i++)
        {
            positions[i] = optimizedLeafClusters[i].Position;
        }
        return positions;
    }

    // Method to apply wind effect to leaves (modifies rotation and scale slightly)
    public void ApplyWindEffect(float windStrength, Vector2 windDirection)
    {
        if (!leafTemplateCached || optimizedLeafClusters.Count == 0)
            return;

        windDirection = windDirection.Normalized();
        float time = (float)Time.GetTicksMsec() / 1000.0f;

        for (int i = 0; i < optimizedLeafClusters.Count; i++)
        {
            var cluster = optimizedLeafClusters[i];

            // Apply wind-based rotation oscillation
            float windEffect = Mathf.Sin(time * 2.0f + cluster.Position.X * 0.01f) * windStrength;
            cluster.Rotation += windEffect * 0.1f;

            // Apply slight scale variation based on wind
            float scaleVariation = 1.0f + Mathf.Sin(time * 1.5f + cluster.Position.Y * 0.01f) * windStrength * 0.05f;
            cluster.Scale = Mathf.Clamp(cluster.Scale * scaleVariation, 0.5f, 1.5f);
        }

        needFullRedraw = true;
        QueueRedraw();
    }

    // Method to change leaf colors (e.g., for seasonal effects)
    public void SetSeasonalLeafColors(Color newLeafColor, float variation = 0.2f)
    {
        leafColor = newLeafColor;

        // Update existing leaf cluster tints
        if (random == null) random = new Random(seed);

        for (int i = 0; i < optimizedLeafClusters.Count; i++)
        {
            var cluster = optimizedLeafClusters[i];
            cluster.Tint = new Color(
                1.0f - (float)random.NextDouble() * variation,
                1.0f - (float)random.NextDouble() * variation,
                1.0f - (float)random.NextDouble() * variation,
                0.9f + (float)random.NextDouble() * 0.1f
            );
        }

        needFullRedraw = true;
        QueueRedraw();
    }

    // Method to animate leaf appearance (useful for spring growth)
    public async void AnimateLeafGrowth(float duration = 2.0f)
    {
        if (optimizedLeafClusters.Count == 0)
            return;

        // Store original scales
        var originalScales = new float[optimizedLeafClusters.Count];
        for (int i = 0; i < optimizedLeafClusters.Count; i++)
        {
            originalScales[i] = optimizedLeafClusters[i].Scale;
            optimizedLeafClusters[i].Scale = 0f; // Start with no leaves
        }

        needFullRedraw = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            await ToSignal(GetTree().CreateTimer(0.03f), SceneTreeTimer.SignalName.Timeout);
            elapsed += 0.03f;
            float progress = elapsed / duration;

            // Grow leaves with a nice easing curve
            float easedProgress = 1f - Mathf.Pow(1f - progress, 3f); // Ease-out cubic

            for (int i = 0; i < optimizedLeafClusters.Count; i++)
            {
                optimizedLeafClusters[i].Scale = originalScales[i] * easedProgress;
            }

            needFullRedraw = true;
            QueueRedraw();
        }

        // Ensure final state
        for (int i = 0; i < optimizedLeafClusters.Count; i++)
        {
            optimizedLeafClusters[i].Scale = originalScales[i];
        }

        needFullRedraw = true;
        QueueRedraw();
    }
}