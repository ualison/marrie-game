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
    [Export(PropertyHint.Range, "0.1,2.0")] private float leafScale = 0.6f; // Multiplier to scale leaves smaller/larger in editor
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "1,20")] private int leafDensity = 8; // How many leaf clusters per branch tip
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,6")] private int leafSnowflakeDepth = 2; // Fractal depth for Koch snowflake leaf template
    // === WIND / DYNAMICS ===
    [ExportGroup("Wind")]
    [Export] private bool EnableWind = false;
    [ExportGroup("Wind")]
    [Export(PropertyHint.Range, "0,2")] private float WindStrength = 0.5f;
    // Internal time accumulator for wind animation
    private float _windTime = 0f;

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
    // (Gradients removed for performance; branches use opaque trunk color)
    // When true, _Draw should perform the full batch draw; afterwards it will be cleared
    // to avoid re-executing expensive draw code every frame.
    private bool needFullRedraw = true;

    // === OPTIMIZED LEAF SYSTEM ===
    private LeafTemplate cachedLeafTemplate;
    private bool leafTemplateCached = false;
    private List<OptimizedLeafCluster> optimizedLeafClusters = new List<OptimizedLeafCluster>();

    // Effective leaf size combining base leafSize and editor multiplier
    private float EffectiveLeafSize => leafSize * leafScale;

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
    // store the base position so wind offsets can be applied without accumulation
    public Vector2 OriginalPosition;
        public float Scale = 1.0f;
        public float Rotation = 0.0f;
    // Tint removed: leaves use the single opaque `leafColor` now.
    }

    // Cached rendered texture for a single leaf cluster (rasterized once and reused)
    private ImageTexture cachedLeafTexture = null;
    private bool leafTextureCachedRaster = false;

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

        // Compute global wind offset if enabled
        Vector2 windOffset = Vector2.Zero;
        if (EnableWind)
        {
            windOffset = new Vector2(
                Mathf.Sin(_windTime * 2f) * WindStrength,
                Mathf.Sin(_windTime * 1.5f) * WindStrength * 0.5f
            );
        }

        // Batch-draw branches to reduce node count and draw calls.
        if (branches.Count > 0)
        {
            for (int b = 0; b < branches.Count; b++)
            {
                var br = branches[b];
                if (br.Points == null || br.Points.Length < 2) continue;
                Color c = br.Color * new Color(1f, 1f, br.Alpha);

                // Compute a simple wind multiplier so outer branches/tips move more.
                // Use distance from origin (trunk base) relative to initialLength.
                float branchDistance = br.Points[br.Points.Length - 1].DistanceTo(Vector2.Zero);
                float windMultiplier = Mathf.Clamp(branchDistance / (initialLength + 0.0001f), 0f, 1.5f);

                for (int i = 0; i < br.Points.Length - 1; i++)
                {
                    Vector2 p0 = br.Points[i] + windOffset * windMultiplier * (i / (float)br.Points.Length);
                    Vector2 p1 = br.Points[i + 1] + windOffset * windMultiplier * ((i + 1) / (float)br.Points.Length);
                    DrawLine(p0, p1, c, br.Thickness);
                }
            }
        }

        // Draw optimized leaf clusters using cached template. Prefer a rasterized texture
        // to avoid many DrawCircle calls. Fall back to fragment drawing if raster texture
        // isn't available.
        if (drawLeaves && optimizedLeafClusters.Count > 0 && leafTemplateCached)
        {
            // Ensure raster texture exists for the current template
            if (!leafTextureCachedRaster)
            {
                CreateLeafTexture();
            }

            Color baseLeafColor = leafColor;
            if (leafTextureCachedRaster && cachedLeafTexture != null)
            {
                // Use DrawSetTransform to place, rotate and scale the cached texture per cluster
                Vector2 texSize = cachedLeafTexture.GetSize();
                Vector2 texCenter = texSize * 0.5f;

                for (int i = 0; i < optimizedLeafClusters.Count; i++)
                {
                    var cluster = optimizedLeafClusters[i];

                    // Set transform so the subsequent draw is positioned/rotated/scaled
                    DrawSetTransform(cluster.Position, cluster.Rotation, Vector2.One * cluster.Scale);

                    // Draw texture centered at origin (transform handles world position)
                    // Use only base leafColor (opaque) — no per-cluster tinting.
                    DrawTexture(cachedLeafTexture, -texCenter, baseLeafColor);

                    // Reset transform for next draw
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
            }
            else
            {
                // Fallback: draw using the template fragments (older method)

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
                        ) * cluster.Scale * EffectiveLeafSize;

                        Vector2 finalPos = cluster.Position + rotatedOffset;
                        float finalSize = templateFrag.Size * cluster.Scale * EffectiveLeafSize;
                        // Use only base leafColor (opaque) combined with fragment color.
                        Color finalColor = (leafColor * templateFrag.Color);

                        DrawCircle(finalPos, finalSize, finalColor);
                    }
                }
            }
        }

        // We've completed the full draw; avoid doing it again until tree changes or we re-enable (e.g. during animation).
        needFullRedraw = false;
    }

    public override void _Process(double delta)
    {
        if (EnableWind)
        {
            _windTime += (float)delta;
            // Redraw each frame while wind is active
            needFullRedraw = true;
            // Apply wind to leaves as well using a rightward bias by default
            ApplyWindEffect(WindStrength, Vector2.Right);
            QueueRedraw();
        }
    }

    // Method to create and cache the leaf template (called once per tree generation)
    private void CreateLeafTemplate()
    {
        // Switch to a deterministic snowflake template for high-fidelity leaves.
        // The rasterizer will draw a crisp, symmetric snowflake into the texture.
        if (leafTemplateCached) return;

        cachedLeafTemplate = new LeafTemplate();

        // Keep a single central fragment for compatibility with the fallback renderer.
        cachedLeafTemplate.BaseFragments.Add(new LeafFragment
        {
            Offset = Vector2.Zero,
            Size = 1.0f,
            Color = new Color(1f, 1f, 1f, 1f)
        });

        // Use a unit radius as the normalized template radius; rasterizer will scale by EffectiveLeafSize.
        cachedLeafTemplate.TemplateRadius = 1.0f;
        leafTemplateCached = true;

        Log.System($"Snowflake leaf template cached", "OrganicFractalTree");
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
            OriginalPosition = position,
            Scale = 0.8f + (float)random.NextDouble() * 0.4f, // Vary scale slightly
            Rotation = (float)(random.NextDouble() * Mathf.Pi * 2), // Random rotation
            // No per-cluster tint: leaves use the global `leafColor`.
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

        // Reset leaf template cache if needed
        leafTemplateCached = false;

        // Initialize random generator
        random = new Random(seed);

        // Create the leaf template first
        CreateLeafTemplate();
    // Rasterize cached leaf texture now to avoid doing it inside _Draw
    CreateLeafTexture();

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
                        (float)(random.NextDouble() - 0.5) * EffectiveLeafSize * 2,
                        (float)(random.NextDouble() - 0.5) * EffectiveLeafSize * 2
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
    // Use opaque trunk color and darken slightly with depth for performance (no gradients)
    Color branchColor = trunkColor * new Color(depthFactor, depthFactor, depthFactor, 1.0f);

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
        // Also invalidate the rasterized texture cache
        leafTextureCachedRaster = false;
        if (cachedLeafTexture != null)
        {
            cachedLeafTexture.Dispose();
            cachedLeafTexture = null;
        }

        Log.System("Leaf template invalidated - will regenerate on next tree creation", "OrganicFractalTree");
    }

    // Create a rasterized ImageTexture from the cached leaf template.
    // This produces a small texture with white fragments (alpha) that will be tinted
    // at draw time by multiplying with leafColor and cluster.Tint.
    private void CreateLeafTexture()
    {
    if (!leafTemplateCached || cachedLeafTemplate == null)
            return;

        // Parameters for rasterization
        int pixelsPerUnit = 14; // controls resolution of the rasterized template
    float radiusUnits = Math.Max(0.5f, cachedLeafTemplate.TemplateRadius * EffectiveLeafSize);
    int texRadiusPx = (int)Math.Ceiling(radiusUnits * pixelsPerUnit) + 2;
        int width = texRadiusPx * 2;
        int height = width;

    // Create image via static factory (Godot C# Image.Create returns an Image)
    var img = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);

        // Clear to transparent
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                img.SetPixel(x, y, new Color(0, 0, 0, 0));
            }
        }

        Vector2 center = new Vector2(width / 2f, height / 2f);

        // Rasterize a symmetric snowflake centered at `center` with crisp/solid alpha (no soft fade).
        // We'll draw multiple radial arms and small branch details for fidelity.

        // Koch snowflake rasterization
        float radiusPixels = cachedLeafTemplate.TemplateRadius * EffectiveLeafSize * pixelsPerUnit;
        float armWidth = Math.Max(1f, pixelsPerUnit * 0.12f * EffectiveLeafSize); // thickness in pixels

        // Helper to set pixel to white with alpha=1 (opaque)
        void SetOpaquePixel(int px, int py)
        {
            if (px < 0 || px >= width || py < 0 || py >= height) return;
            img.SetPixel(px, py, new Color(1f, 1f, 1f, 1f));
        }

        // Generate Koch snowflake polygon points (centered)
        List<Vector2> GenerateKochSnowflake(Vector2 c, float radius, int depth)
        {
            // Start from an equilateral triangle
            var pts = new List<Vector2>();
            for (int i = 0; i < 3; i++)
            {
                float ang = Mathf.Pi * 2f * (i / 3.0f) - Mathf.Pi / 2f; // point upward
                pts.Add(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
            }

            // Local function to subdivide one segment
            List<Vector2> Subdivide(List<Vector2> poly)
            {
                var next = new List<Vector2>();
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[(i + 1) % poly.Count];
                    Vector2 v = b - a;
                    Vector2 p1 = a + v * (1.0f / 3.0f);
                    Vector2 p2 = a + v * (2.0f / 3.0f);

                    // peak point (rotate by -60 degrees to go outward)
                    float ang = -Mathf.Pi / 3.0f; // -60 degrees
                    Vector2 dir = p2 - p1;
                    Vector2 peak = p1 + new Vector2(
                        dir.X * Mathf.Cos(ang) - dir.Y * Mathf.Sin(ang),
                        dir.X * Mathf.Sin(ang) + dir.Y * Mathf.Cos(ang)
                    );

                    next.Add(a);
                    next.Add(p1);
                    next.Add(peak);
                    next.Add(p2);
                }
                return next;
            }

            var current = pts;
            for (int d = 0; d < Math.Max(0, depth); d++)
            {
                current = Subdivide(current);
            }

            return current;
        }

        var snowPts = GenerateKochSnowflake(center, radiusPixels, leafSnowflakeDepth);

        // Rasterize by stamping along each segment with thickness to approximate a filled shape
        for (int i = 0; i < snowPts.Count; i++)
        {
            Vector2 a = snowPts[i];
            Vector2 b = snowPts[(i + 1) % snowPts.Count];
            Vector2 seg = b - a;
            float segLen = seg.Length();
            if (segLen <= 0.001f) continue;
            Vector2 dir = seg / segLen;

            // step along segment
            float step = 0.6f; // pixel step
            for (float t = 0; t <= segLen; t += step)
            {
                Vector2 pos = a + dir * t;
                int cx = (int)Mathf.Floor(pos.X);
                int cy = (int)Mathf.Floor(pos.Y);

                int r = (int)Math.Ceiling(armWidth);
                for (int oy = -r; oy <= r; oy++)
                {
                    for (int ox = -r; ox <= r; ox++)
                    {
                        float ddx = (ox + 0.5f);
                        float ddy = (oy + 0.5f);
                        if (ddx * ddx + ddy * ddy <= (armWidth + 0.0f) * (armWidth + 0.0f))
                        {
                            SetOpaquePixel(cx + ox, cy + oy);
                        }
                    }
                }
            }
        }

        // Optionally draw a small central hexagon/star to add fidelity
        int centerRadius = Math.Max(1, (int)(pixelsPerUnit * 0.6f * EffectiveLeafSize));
        for (int y = -centerRadius; y <= centerRadius; y++)
        {
            for (int x = -centerRadius; x <= centerRadius; x++)
            {
                if (x * x + y * y <= centerRadius * centerRadius)
                    SetOpaquePixel((int)center.X + x, (int)center.Y + y);
            }
        }

        // No explicit lock/unlock needed in this binding

        // Create texture
        if (cachedLeafTexture != null)
        {
            cachedLeafTexture.Dispose();
            cachedLeafTexture = null;
        }

        cachedLeafTexture = ImageTexture.CreateFromImage(img);
        leafTextureCachedRaster = true;

        Log.System($"Rasterized leaf texture created ({width}x{height})", "OrganicFractalTree");
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
        Vector2 perp = new Vector2(-windDirection.Y, windDirection.X);
        float time = (float)Time.GetTicksMsec() / 1000.0f;

        // base magnitude in pixels for leaf displacement (tunable)
        float baseLeafDisplacement = 6.0f;

        for (int i = 0; i < optimizedLeafClusters.Count; i++)
        {
            var cluster = optimizedLeafClusters[i];

            // Use OriginalPosition for deterministic phase offsets
            Vector2 origin = cluster.OriginalPosition != Vector2.Zero ? cluster.OriginalPosition : cluster.Position;

            // Positional offset along wind direction (primary) and perpendicular (secondary)
            float along = Mathf.Sin(time * 2.0f + origin.X * 0.01f) * windStrength;
            float across = Mathf.Sin(time * 1.5f + origin.Y * 0.01f) * windStrength * 0.5f;

            Vector2 displacement = windDirection * (along * baseLeafDisplacement * cluster.Scale)
                                 + perp * (across * baseLeafDisplacement * 0.5f * cluster.Scale);

            // Apply rotation and scale jitter as before (use origin for deterministic behavior)
            float windEffect = Mathf.Sin(time * 2.0f + origin.X * 0.01f) * windStrength;
            cluster.Rotation += windEffect * 0.1f;

            float scaleVariation = 1.0f + Mathf.Sin(time * 1.5f + origin.Y * 0.01f) * windStrength * 0.05f;
            cluster.Scale = Mathf.Clamp(cluster.Scale * scaleVariation, 0.5f, 1.5f);

            // Update world position (don't accumulate; always base on OriginalPosition)
            cluster.Position = origin + displacement;
        }

        needFullRedraw = true;
        QueueRedraw();
    }

    // NOTE: Seasonal per-cluster leaf coloring and AnimateLeafGrowth removed.
}