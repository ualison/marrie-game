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
    [ExportGroup("Visual")]
    [Export] private bool leavesFalling = false; // Enable/disable automatic leaves falling
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,50")] private float leafFallSpawnRate = 6f; // leaves spawned per second when falling
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,200")] private int maxFallingLeaves = 120; // safety cap for performance
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,4")] private float leafFallGravity = 200f; // pixels/sec^2 downward
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,2")] private float leafWindInfluence = 0.6f; // how much global wind affects falling leaves
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,8")] private Vector2 leafFallLifetimeRange = new Vector2(1.5f, 4.0f); // min/max life in seconds
    [ExportGroup("Visual")]
    [Export] private Vector2 leafInitialVelocity = new Vector2(0f, 20f); // base initial velocity for falling leaves (px/sec)
    [ExportGroup("Visual")]
    [Export] private Vector2 leafVelocityRandomness = new Vector2(20f, 30f); // randomness applied to initial velocity (x +/-, y 0..)
    [ExportGroup("Visual")]
    [Export] private bool despawnOnGroundOnly = true; // if true, leaves are removed only when they touch groundY
    [ExportGroup("Visual")]
    [Export] private float leafGroundY = 400f; // world/local Y coordinate considered 'ground' for leaves
    [ExportGroup("Visual")]
    [Export] private bool fallingLeavesInFrontOfPlayer = true; // if true, move this node in front so leaves render over player
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
    // -- Falling leaves simulation (lightweight, no nodes) --
    private struct FallingLeaf
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Rotation;
        public float AngularVelocity;
        public float Scale;
        public float Age;
        public float Lifetime;
        public float BasePhase; // for fluttering
    }
    private List<FallingLeaf> fallingLeaves = new List<FallingLeaf>();
    private float _leafSpawnAccumulator = 0f;
    // (Gradients removed for performance; branches use opaque trunk color)
    // When true, _Draw should perform the full batch draw; afterwards it will be cleared
    // to avoid re-executing expensive draw code every frame.
    private bool needFullRedraw = true;
    // Ensure we perform at least one full draw after generation.
    private bool _initialDrawDone = false;

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
    // Per-instance key in the shared cache (null if none)
    private string leafTextureCacheKey = null;
    // Renderer node responsible only for drawing falling leaves so Z ordering can be controlled independently
    private FallingLeafRenderer fallingLeafRenderer = null;

    // Shared, per-process cache for leaf textures keyed by (depth, size, pixelsPerUnit)
    private static Dictionary<string, ImageTexture> SharedLeafTextureCache = new Dictionary<string, ImageTexture>();
    private static Dictionary<string, int> SharedLeafTextureRefCount = new Dictionary<string, int>();

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

        // Create a dedicated renderer for falling leaves so we can control their Z independently
        if (fallingLeafRenderer == null)
        {
            fallingLeafRenderer = new FallingLeafRenderer();
            fallingLeafRenderer.ParentTree = this;
            AddChild(fallingLeafRenderer);
        }

        // Ensure Z order matches flag (applies to falling leaves renderer only)
        SetLeavesZOrder();
    }

    // Set this node's Z index so falling leaves render in front/behind player as requested.
    private void SetLeavesZOrder()
    {
        if (fallingLeafRenderer == null) return;

        if (fallingLeavesInFrontOfPlayer)
        {
            // Ensure renderer is a child of this tree so it draws over the tree and can be placed in front
            var currentParent = fallingLeafRenderer.GetParent();
            if (currentParent != this)
            {
                // preserve global position while reparenting
                Vector2 globalPos = fallingLeafRenderer.GlobalPosition;
                currentParent?.RemoveChild(fallingLeafRenderer);
                AddChild(fallingLeafRenderer);
                fallingLeafRenderer.GlobalPosition = globalPos;
            }
            fallingLeafRenderer.ZIndex = 100;
        }
        else
        {
            // Try to place renderer as a sibling of the player so it renders behind the player.
            Node sceneRoot = GetTree().CurrentScene;
            Node2D playerNode = null;
            if (sceneRoot != null)
            {
                // search by name "Player" (common convention) and fallback to first Node2D named "Player"
                playerNode = FindNodeByName(sceneRoot, "Player");
            }

            if (playerNode != null && playerNode.GetParent() != null)
            {
                var desiredParent = playerNode.GetParent();
                var currentParent = fallingLeafRenderer.GetParent();
                if (currentParent != desiredParent)
                {
                    Vector2 globalPos = fallingLeafRenderer.GlobalPosition;
                    currentParent?.RemoveChild(fallingLeafRenderer);
                    desiredParent.AddChild(fallingLeafRenderer);
                    fallingLeafRenderer.GlobalPosition = globalPos;
                }

                // place renderer just behind player using player's ZIndex if available
                int playerZ = 0;
                if (playerNode is Node2D p2d)
                    playerZ = p2d.ZIndex;
                fallingLeafRenderer.ZIndex = playerZ - 1;
            }
            else
            {
                // fallback: keep as child but push behind
                fallingLeafRenderer.ZIndex = -100;
            }
        }
    }

    // Find a Node2D by name in the scene tree (depth-first)
    private Node2D FindNodeByName(Node root, string name)
    {
        if (root == null) return null;
        if (root is Node2D node2d && root.Name == name) return node2d;
        foreach (Node child in root.GetChildren())
        {
            var found = FindNodeByName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    public override void _Draw()
    {
        // If there's nothing to draw, return early.
        if (branches.Count == 0 && optimizedLeafClusters.Count == 0 && fallingLeaves.Count == 0)
            return;

        // Compute global wind offset if enabled (do it per-frame so branches/leaves respond while falling)
        Vector2 windOffset = Vector2.Zero;
        if (EnableWind)
        {
            windOffset = new Vector2(
                Mathf.Sin(_windTime * 2f) * WindStrength,
                Mathf.Sin(_windTime * 1.5f) * WindStrength * 0.5f
            );
        }

        // Draw branches every frame from the cached branch geometry. This ensures the tree remains visible
        // while falling leaves are present (previous implementation only drew branches inside the expensive
        // "needFullRedraw" path which caused the tree to disappear during transient draws).
        if (branches.Count > 0)
        {
            for (int b = 0; b < branches.Count; b++)
            {
                var br = branches[b];
                if (br.Points == null || br.Points.Length < 2) continue;
                Color c = br.Color * new Color(1f, 1f, br.Alpha);

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

        // Draw optimized leaf clusters every frame as well (use cached raster texture when available).
        if (drawLeaves && optimizedLeafClusters.Count > 0 && leafTemplateCached)
        {
            if (!leafTextureCachedRaster)
            {
                CreateLeafTexture();
            }

            Color baseLeafColor = leafColor;
            if (leafTextureCachedRaster && cachedLeafTexture != null)
            {
                Vector2 texSize = cachedLeafTexture.GetSize();
                Vector2 texCenter = texSize * 0.5f;

                for (int i = 0; i < optimizedLeafClusters.Count; i++)
                {
                    var cluster = optimizedLeafClusters[i];
                    DrawSetTransform(cluster.Position, cluster.Rotation, Vector2.One * cluster.Scale);
                    DrawTexture(cachedLeafTexture, -texCenter, baseLeafColor);
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
            }
            else
            {
                for (int i = 0; i < optimizedLeafClusters.Count; i++)
                {
                    var cluster = optimizedLeafClusters[i];
                    float cosRot = Mathf.Cos(cluster.Rotation);
                    float sinRot = Mathf.Sin(cluster.Rotation);

                    for (int f = 0; f < cachedLeafTemplate.BaseFragments.Count; f++)
                    {
                        var templateFrag = cachedLeafTemplate.BaseFragments[f];
                        Vector2 rotatedOffset = new Vector2(
                            templateFrag.Offset.X * cosRot - templateFrag.Offset.Y * sinRot,
                            templateFrag.Offset.X * sinRot + templateFrag.Offset.Y * cosRot
                        ) * cluster.Scale * EffectiveLeafSize;

                        Vector2 finalPos = cluster.Position + rotatedOffset;
                        float finalSize = templateFrag.Size * cluster.Scale * EffectiveLeafSize;
                        Color finalColor = (leafColor * templateFrag.Color);

                        DrawCircle(finalPos, finalSize, finalColor);
                    }
                }
            }
        }

        // Draw lightweight falling leaves (updated each frame). These are transient.
    // falling leaves are drawn by the separate FallingLeafRenderer child so Z ordering can be controlled
    }

        
    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // If we never completed an initial full draw (edge cases where canvas was empty), request one now.
        if (!_initialDrawDone)
        {
            needFullRedraw = true;
            QueueRedraw();
            _initialDrawDone = true; // only force once
        }

        if (EnableWind)
        {
            _windTime += dt;
            // Redraw static tree when wind changes branch positions
            needFullRedraw = true;
            ApplyWindEffect(WindStrength, Vector2.Right);
        }

        // Update falling leaves physics
        if (fallingLeaves.Count > 0)
        {
            // lightweight in-place update and compacting
            int write = 0;
            for (int i = 0; i < fallingLeaves.Count; i++)
            {
                var lf = fallingLeaves[i];

                // gravity
                lf.Velocity += Vector2.Down * leafFallGravity * dt;

                // wind influence - use global wind as a simple oscillation
                if (EnableWind)
                {
                    float windX = Mathf.Sin(_windTime * 2.0f + lf.BasePhase) * WindStrength * leafWindInfluence;
                    lf.Velocity += new Vector2(windX, 0) * dt * 40f;
                }

                // simple aerodynamic drag
                lf.Velocity *= (1f - Mathf.Clamp(0.02f * dt, 0f, 0.2f));

                // integrate
                lf.Position += lf.Velocity * dt;

                // rotation
                lf.Rotation += lf.AngularVelocity * dt;

                // flutter oscillation modifies rotation slightly
                lf.Rotation += Mathf.Sin((float)Time.GetTicksMsec() / 1000.0f + lf.BasePhase) * 0.0025f;

                lf.Age += dt;

                // keep leaf if it hasn't hit the ground (when required) or hasn't expired
                bool keep = true;
                if (despawnOnGroundOnly)
                {
                    // compare local Y (leaf.Position is in local coordinates because we spawn at cluster.Position)
                    if (lf.Position.Y >= leafGroundY)
                        keep = false; // hit ground
                }
                else
                {
                    // default behavior: use lifetime
                    if (lf.Age >= lf.Lifetime)
                        keep = false;
                }

                if (keep && write < maxFallingLeaves)
                {
                    fallingLeaves[write++] = lf;
                }
            }

            if (write < fallingLeaves.Count)
                fallingLeaves.RemoveRange(write, fallingLeaves.Count - write);
        }

        // Spawn new falling leaves when enabled
        if (leavesFalling && drawLeaves && optimizedLeafClusters.Count > 0)
        {
            _leafSpawnAccumulator += leafFallSpawnRate * dt;
            int toSpawn = (int)Math.Floor(_leafSpawnAccumulator);
            if (toSpawn > 0)
            {
                _leafSpawnAccumulator -= toSpawn;
                for (int i = 0; i < toSpawn; i++)
                {
                    if (fallingLeaves.Count >= maxFallingLeaves) break;
                    // pick a random optimized cluster to drop from
                    int idx = random.Next(0, optimizedLeafClusters.Count);
                    var cluster = optimizedLeafClusters[idx];
                    SpawnFallingLeaf(cluster.Position, cluster.Scale);
                }
            }
        }

        // If there are transient falling leaves, ensure the falling-leaf renderer redraws each frame
        if (fallingLeaves.Count > 0)
        {
            if (fallingLeafRenderer != null)
                fallingLeafRenderer.QueueRedraw();
            else
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
        return;
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

        // Clear optimized leaf clusters and branch list
        optimizedLeafClusters.Clear();
        branches.Clear();
    appleInstances.Clear();
    // clear falling leaves when regenerating
    fallingLeaves.Clear();

    // Keep existing leaf template/texture cached across regenerations to avoid expensive rasterization
    // unless explicitly invalidated via InvalidateLeafTemplate().

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

    // Spawn a lightweight FallingLeaf at a position with base scale
    private void SpawnFallingLeaf(Vector2 origin, float baseScale)
    {
        if (random == null) random = new Random(seed);

        FallingLeaf lf = new FallingLeaf();
        lf.Position = origin + new Vector2((float)(random.NextDouble() - 0.5) * EffectiveLeafSize * 2f, (float)(random.NextDouble() - 0.2) * EffectiveLeafSize);
        lf.Scale = baseScale * (0.6f + (float)random.NextDouble() * 0.8f);
    // initial velocity: base plus randomness. X is +/- randomness.X, Y is base Y plus [0..randomness.Y]
    float randX = (float)(random.NextDouble() - 0.5) * 2f * leafVelocityRandomness.X;
    float randY = (float)(random.NextDouble()) * leafVelocityRandomness.Y;
    lf.Velocity = new Vector2(leafInitialVelocity.X + randX, leafInitialVelocity.Y + randY);
        lf.Rotation = (float)(random.NextDouble() * Mathf.Pi * 2f);
        lf.AngularVelocity = (float)(random.NextDouble() - 0.5) * 2f;
        lf.Age = 0f;
        lf.Lifetime = Mathf.Lerp(leafFallLifetimeRange.X, leafFallLifetimeRange.Y, (float)random.NextDouble());
        lf.BasePhase = (float)random.NextDouble() * Mathf.Pi * 2f;

        fallingLeaves.Add(lf);
    }

    // Public controls for leaves falling
    public void StartLeavesFalling()
    {
        leavesFalling = true;
        _leafSpawnAccumulator = 0f;
    SetLeavesZOrder();
    }

    public void StopLeavesFalling()
    {
        leavesFalling = false;
    SetLeavesZOrder();
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
            ReleaseSharedLeafTexture();
            cachedLeafTexture = null;
        }

        Log.System("Leaf template invalidated - will regenerate on next tree creation", "OrganicFractalTree");
    }

    private void ReleaseSharedLeafTexture()
    {
        if (string.IsNullOrEmpty(leafTextureCacheKey))
            return;

        var key = leafTextureCacheKey;
        if (!SharedLeafTextureRefCount.TryGetValue(key, out var rc))
            rc = 0;

        rc = Math.Max(0, rc - 1);
        if (rc <= 0)
        {
            if (SharedLeafTextureCache.TryGetValue(key, out var tex))
            {
                try { tex.Dispose(); } catch { }
            }
            SharedLeafTextureCache.Remove(key);
            SharedLeafTextureRefCount.Remove(key);
        }
        else
        {
            SharedLeafTextureRefCount[key] = rc;
        }

        leafTextureCacheKey = null;
    }

    public override void _ExitTree()
    {
        ReleaseSharedLeafTexture();
        base._ExitTree();
    }

    // Create a rasterized ImageTexture from the cached leaf template.
    // This produces a small texture with white fragments (alpha) that will be tinted
    // at draw time by multiplying with leafColor and cluster.Tint.
    private void CreateLeafTexture()
    {
        if (!leafTemplateCached || cachedLeafTemplate == null)
            return;
        // Parameters for rasterization (lowered to reduce CPU/time spent rasterizing)
        int pixelsPerUnit = 10; // controls resolution of the rasterized template
        float radiusUnits = Math.Max(0.5f, cachedLeafTemplate.TemplateRadius * EffectiveLeafSize);
        int texRadiusPx = (int)Math.Ceiling(radiusUnits * pixelsPerUnit) + 2;
        int width = texRadiusPx * 2;
        int height = width;

        // Build a cache key from key parameters so textures with same params are shared
        string key = $"d{leafSnowflakeDepth}_s{EffectiveLeafSize:F2}_ppu{pixelsPerUnit}";

        // If cached in shared cache, use it and increase refcount
        if (SharedLeafTextureCache.TryGetValue(key, out var sharedTex))
        {
            cachedLeafTexture = sharedTex;
            leafTextureCachedRaster = true;
            leafTextureCacheKey = key;
            // increment refcount
            SharedLeafTextureRefCount[key] = SharedLeafTextureRefCount.TryGetValue(key, out var rc) ? rc + 1 : 1;
            return;
        }

        // Create image via static factory
        var img = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        // Fast clear to transparent
        img.Fill(new Color(0, 0, 0, 0));

        Vector2 center = new Vector2(width / 2f, height / 2f);

        // Helper to set pixel to white with alpha=1 (opaque) - assumes img.Lock() is active
        void SetOpaquePixel(int px, int py)
        {
            if ((uint)px >= (uint)width || (uint)py >= (uint)height) return;
            img.SetPixel(px, py, new Color(1f, 1f, 1f, 1f));
        }

        // Generate Koch snowflake polygon points (centered)
        List<Vector2> GenerateKochSnowflake(Vector2 c, float radius, int depth)
        {
            // Start with an equilateral triangle
            var points = new List<Vector2>();
            
            // Create initial triangle vertices (pointing up)
            for (int i = 0; i < 3; i++)
            {
                float angle = Mathf.Pi * 2f * i / 3f - Mathf.Pi / 2f; // -90 degrees offset to point up
                points.Add(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            
            // Apply Koch transformation for the specified depth
            for (int d = 0; d < depth; d++)
            {
                points = ApplyKochIteration(points);
            }
            
            return points;
        }

        // Helper function to apply one iteration of Koch snowflake transformation
        List<Vector2> ApplyKochIteration(List<Vector2> inputPoints)
        {
            var newPoints = new List<Vector2>();
            
            for (int i = 0; i < inputPoints.Count; i++)
            {
                Vector2 a = inputPoints[i];
                Vector2 b = inputPoints[(i + 1) % inputPoints.Count];
                
                // Calculate the four points for this segment
                Vector2 segment = b - a;
                float segmentLength = segment.Length();
                Vector2 direction = segment.Normalized();
                
                // Point 1: Start of segment
                Vector2 p1 = a;
                
                // Point 2: One third along the segment
                Vector2 p2 = a + direction * (segmentLength / 3f);
                
                // Point 4: Two thirds along the segment
                Vector2 p4 = a + direction * (segmentLength * 2f / 3f);
                
                // Point 3: The peak of the equilateral triangle
                Vector2 midpoint = (p2 + p4) * 0.5f;
                Vector2 perpendicular = new Vector2(-direction.Y, direction.X); // Perpendicular to segment
                float triangleHeight = (segmentLength / 3f) * Mathf.Sqrt(3f) / 2f; // Height of equilateral triangle
                Vector2 p3 = midpoint + perpendicular * triangleHeight;
                
                // Add the points (p4 will be added as p1 of the next iteration, so we skip it here)
                newPoints.Add(p1);
                newPoints.Add(p2);
                newPoints.Add(p3);
                newPoints.Add(p4);
            }
            
            return newPoints;
        }

        // Helper method: Bresenham-like line rasterizer with per-point thickness.
        // img is locked and SetOpaquePixel is safe to call.
        void DrawLineOnImage(Vector2 start, Vector2 end, float thickness)
        {
            int x0 = (int)Math.Round(start.X);
            int y0 = (int)Math.Round(start.Y);
            int x1 = (int)Math.Round(end.X);
            int y1 = (int)Math.Round(end.Y);

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            int radius = Math.Max(1, (int)Math.Ceiling(thickness / 2f));
            int r2 = radius * radius;

            while (true)
            {
                // draw a filled circle (small) around (x0,y0) as thickness
                int minx = Math.Max(0, x0 - radius);
                int maxx = Math.Min(width - 1, x0 + radius);
                int miny = Math.Max(0, y0 - radius);
                int maxy = Math.Min(height - 1, y0 + radius);
                for (int yy = miny; yy <= maxy; yy++)
                {
                    int dyc = yy - y0;
                    int dyc2 = dyc * dyc;
                    for (int xx = minx; xx <= maxx; xx++)
                    {
                        int dxc = xx - x0;
                        if (dxc * dxc + dyc2 <= r2)
                            SetOpaquePixel(xx, yy);
                    }
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        // Rasterize a symmetric snowflake centered at `center` with crisp/solid alpha (no soft fade).
        float radiusPixels = cachedLeafTemplate.TemplateRadius * EffectiveLeafSize * pixelsPerUnit;
        float armWidth = Math.Max(1f, pixelsPerUnit * 0.12f * EffectiveLeafSize); // thickness in pixels

        var snowPts = GenerateKochSnowflake(center, radiusPixels, leafSnowflakeDepth);

        // Rasterize the Koch snowflake outline
        for (int i = 0, sc = snowPts.Count; i < sc; i++)
        {
            Vector2 a = snowPts[i];
            Vector2 b = snowPts[(i + 1) % sc];
            DrawLineOnImage(a, b, armWidth);
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

    // Create texture from image
    // img.Unlock(); // Removed the unlock call as it's not available in the Godot C# API

        if (cachedLeafTexture != null)
        {
            cachedLeafTexture.Dispose();
            cachedLeafTexture = null;
        }
        cachedLeafTexture = ImageTexture.CreateFromImage(img);
        leafTextureCachedRaster = true;
        leafTextureCacheKey = key;

        // Store in shared cache and set refcount = 1
        SharedLeafTextureCache[key] = cachedLeafTexture;
        SharedLeafTextureRefCount[key] = 1;

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

    // Public accessors for the falling-leaf renderer to safely obtain required data
    public (Vector2 Position, float Rotation, float Scale)[] GetFallingLeafInfos()
    {
        var arr = new (Vector2, float, float)[fallingLeaves.Count];
        for (int i = 0; i < fallingLeaves.Count; i++)
        {
            var lf = fallingLeaves[i];
            arr[i] = (lf.Position, lf.Rotation, lf.Scale);
        }
        return arr;
    }

    public ImageTexture GetCachedLeafTexture() => cachedLeafTexture;
    public bool IsLeafTextureCachedRaster() => leafTextureCachedRaster;
    public Color GetLeafColor() => leafColor;
    public bool IsLeafTemplateCached() => leafTemplateCached;
    public float GetEffectiveLeafSizePublic() => EffectiveLeafSize;

}