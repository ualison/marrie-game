using Godot;
using System;
using System.Collections.Generic;

public partial class OrganicFractalTree : Node2D
{
    // === GENERATION SETTINGS ===
    [ExportGroup("Generation")]
    [Export] private int seed = 12345;
    [ExportGroup("Generation")]
    [Export] private bool randomizeOnLoad = true;
    [ExportGroup("Generation")]
    [Export] private int maxDepth = 10;

    // === TRUNK AND BRANCH DIMENSIONS ===
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export] private float initialLength = 120.0f;
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export(PropertyHint.Range, "0.5,0.9")] private float lengthReduction = 0.72f;
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export(PropertyHint.Range, "0.1,20.0")] private float initialThickness = 12.0f;
    [ExportGroup("Trunk & Branch Dimensions")]
    [Export(PropertyHint.Range, "0.4,0.9")] private float thicknessReduction = 0.68f;

    // === BRANCHING BEHAVIOR ===
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "10,90")] private float branchAngle = 28.0f;
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,45")] private float angleVariation = 18.0f;
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,0.8")] private float curvature = 0.15f;
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "2,6")] private int minBranches = 2;
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "2,6")] private int maxBranches = 4;
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,1")] private float asymmetry = 0.4f;
    [ExportGroup("Branching")]
    [Export(PropertyHint.Range, "0,1")] private float branchProbability = 0.55f;

    // === TRUNK SHAPE ===
    [ExportGroup("Trunk Shape")]
    [Export(PropertyHint.Range, "0,1")] private float trunkTaper = 0.3f;
    [ExportGroup("Trunk Shape")]
    [Export(PropertyHint.Range, "0,30")] private float trunkCurve = 8.0f;

    // === VISUAL APPEARANCE ===
    [ExportGroup("Visual")]
    [Export] private Color trunkColor = new Color(0.4f, 0.3f, 0.2f);
    [ExportGroup("Visual")]
    [Export] private Color leafColor = new Color(0.2f, 0.6f, 0.3f);
    [ExportGroup("Visual")]
    [Export] private bool drawLeaves = true;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "1,15")] private float leafSize = 4.0f;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0.1,2.0")] private float leafScale = 0.6f;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "1,20")] private int leafDensity = 8;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,6")] private int leafSnowflakeDepth = 2;
    [ExportGroup("Visual")]
    [Export] private bool leavesFalling = false;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,50")] private float leafFallSpawnRate = 6f;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,200")] private int maxFallingLeaves = 120;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,4")] private float leafFallGravity = 200f;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,2")] private float leafWindInfluence = 0.6f;
    [ExportGroup("Visual")]
    [Export(PropertyHint.Range, "0,8")] private Vector2 leafFallLifetimeRange = new Vector2(1.5f, 4.0f);
    [ExportGroup("Visual")]
    [Export] private Vector2 leafInitialVelocity = new Vector2(0f, 20f);
    [ExportGroup("Visual")]
    [Export] private Vector2 leafVelocityRandomness = new Vector2(20f, 30f);
    [ExportGroup("Visual")]
    [Export] private bool despawnOnGroundOnly = true;
    [ExportGroup("Visual")]
    [Export] private float leafGroundY = 400f;
    [ExportGroup("Visual")]
    [Export] private bool fallingLeavesInFrontOfPlayer = true;

    // === WIND / DYNAMICS ===
    [ExportGroup("Wind")]
    [Export] private bool EnableWind = false;
    [ExportGroup("Wind")]
    [Export(PropertyHint.Range, "0,2")] private float WindStrength = 0.5f;
    private float _windTime = 0f;

    // === APPLE SETTINGS ===
    [ExportGroup("Apples")]
    [Export] private bool hasApples = false;
    [ExportGroup("Apples")]
    [Export(PropertyHint.Range, "1,50")] private int appleQuantity = 15;
    [ExportGroup("Apples")]
    [Export(PropertyHint.Range, "1,200")] private float appleMinDistance = 20.0f;
    [ExportGroup("Apples")]
    [Export(PropertyHint.Range, "0,1")] private float applePlacementHeight = 0.6f;
    [ExportGroup("Apples")]
    [Export] private PackedScene applePrefab;

    private Random random;
    private List<BranchData> branches = new List<BranchData>();
    private List<Node2D> appleInstances = new List<Node2D>();
    
    // OPTIMIZATION: Use arrays instead of List for falling leaves (better cache locality)
    private FallingLeaf[] fallingLeaves = new FallingLeaf[0];
    private int fallingLeafCount = 0;
    private float _leafSpawnAccumulator = 0f;
    
    // OPTIMIZATION: Cache wind calculations
    private Vector2 cachedWindOffset = Vector2.Zero;
    private bool windNeedsUpdate = true;
    
    private bool needFullRedraw = true;
    private bool _initialDrawDone = false;

    // === OPTIMIZED LEAF SYSTEM ===
    private LeafTemplate cachedLeafTemplate;
    private bool leafTemplateCached = false;
    // OPTIMIZATION: Use arrays for better performance
    private OptimizedLeafCluster[] optimizedLeafClusters = new OptimizedLeafCluster[0];
    private int leafClusterCount = 0;

    private float EffectiveLeafSize => leafSize * leafScale;

    private struct FallingLeaf
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Rotation;
        public float AngularVelocity;
        public float Scale;
        public float Age;
        public float Lifetime;
        public float BasePhase;
    }

    private class BranchData
    {
        public int Depth { get; set; }
        public Vector2[] Points { get; set; }
        public float Thickness { get; set; }
        public Vector2 EndDirection { get; set; }
        public Color Color { get; set; } = new Color(0, 0, 0, 1);
        public float Alpha { get; set; } = 1.0f;
        // OPTIMIZATION: Pre-calculate wind multipliers
        public float[] WindMultipliers { get; set; }
    }

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

    private class LeafFragment
    {
        public Vector2 Offset;
        public float Size;
        public Color Color;
    }

    private class LeafTemplate
    {
        public List<LeafFragment> BaseFragments { get; set; } = new List<LeafFragment>();
        public float TemplateRadius { get; set; }
    }

    // OPTIMIZATION: Changed to struct for better memory layout
    private struct OptimizedLeafCluster
    {
        public Vector2 Position;
        public Vector2 OriginalPosition;
        public float Scale;
        public float Rotation;
    }

    private ImageTexture cachedLeafTexture = null;
    private bool leafTextureCachedRaster = false;
    private string leafTextureCacheKey = null;
    private FallingLeafRenderer fallingLeafRenderer = null;

    private static Dictionary<string, ImageTexture> SharedLeafTextureCache = new Dictionary<string, ImageTexture>();
    private static Dictionary<string, int> SharedLeafTextureRefCount = new Dictionary<string, int>();

    public override void _Ready()
    {
        if (randomizeOnLoad)
        {
            seed = (int)GD.RandRange(0, 999999);
            Log.System($"Using random seed: {seed}", "OrganicFractalTree");
        }
        else
        {
            Log.System($"Using fixed seed: {seed}", "OrganicFractalTree");
        }

        // OPTIMIZATION: Pre-allocate falling leaves array
        fallingLeaves = new FallingLeaf[maxFallingLeaves];
        fallingLeafCount = 0;

        GenerateTree();

        if (fallingLeafRenderer == null)
        {
            fallingLeafRenderer = new FallingLeafRenderer();
            fallingLeafRenderer.ParentTree = this;
            AddChild(fallingLeafRenderer);
        }

        SetLeavesZOrder();
    }

    private void SetLeavesZOrder()
    {
        if (fallingLeafRenderer == null) return;

        if (fallingLeavesInFrontOfPlayer)
        {
            var currentParent = fallingLeafRenderer.GetParent();
            if (currentParent != this)
            {
                Vector2 globalPos = fallingLeafRenderer.GlobalPosition;
                currentParent?.RemoveChild(fallingLeafRenderer);
                AddChild(fallingLeafRenderer);
                fallingLeafRenderer.GlobalPosition = globalPos;
            }
            fallingLeafRenderer.ZIndex = 100;
        }
        else
        {
            Node sceneRoot = GetTree().CurrentScene;
            Node2D playerNode = null;
            if (sceneRoot != null)
            {
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

                int playerZ = 0;
                if (playerNode is Node2D p2d)
                    playerZ = p2d.ZIndex;
                fallingLeafRenderer.ZIndex = playerZ - 1;
            }
            else
            {
                fallingLeafRenderer.ZIndex = -100;
            }
        }
    }

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
        if (branches.Count == 0 && leafClusterCount == 0 && fallingLeafCount == 0)
            return;

        // OPTIMIZATION: Use cached wind offset
        Vector2 windOffset = cachedWindOffset;

        // Draw branches
        if (branches.Count > 0)
        {
            for (int b = 0; b < branches.Count; b++)
            {
                var br = branches[b];
                if (br.Points == null || br.Points.Length < 2) continue;
                
                Color c = br.Color;
                if (br.Alpha < 1f)
                    c *= new Color(1f, 1f, br.Alpha);

                int segCount = br.Points.Length - 1;
                bool thinBranch = br.Thickness < 1.2f;

                if (thinBranch)
                {
                    // OPTIMIZATION: Draw thin branches as single polyline when possible
                    if (EnableWind && windOffset != Vector2.Zero)
                    {
                        for (int i = 0; i < segCount; i++)
                        {
                            Vector2 p0 = br.Points[i] + windOffset * br.WindMultipliers[i];
                            Vector2 p1 = br.Points[i + 1] + windOffset * br.WindMultipliers[i + 1];
                            DrawLine(p0, p1, c, br.Thickness);
                        }
                    }
                    else
                    {
                        // No wind - draw directly
                        for (int i = 0; i < segCount; i++)
                        {
                            DrawLine(br.Points[i], br.Points[i + 1], c, br.Thickness);
                        }
                    }
                }
                else
                {
                    // Draw thicker branches with tapering
                    for (int i = 0; i < segCount; i++)
                    {
                        float frac = (float)i / segCount;
                        float segThickness = Mathf.Lerp(br.Thickness, br.Thickness * 0.35f, frac);

                        Vector2 p0, p1;
                        if (EnableWind && windOffset != Vector2.Zero)
                        {
                            p0 = br.Points[i] + windOffset * br.WindMultipliers[i];
                            p1 = br.Points[i + 1] + windOffset * br.WindMultipliers[i + 1];
                        }
                        else
                        {
                            p0 = br.Points[i];
                            p1 = br.Points[i + 1];
                        }

                        DrawLine(p0, p1, c, segThickness);
                    }

                    // OPTIMIZATION: Only draw caps for visible branches
                    if (br.Thickness > 2f)
                    {
                        for (int i = 0; i < br.Points.Length; i += 2) // Skip every other cap
                        {
                            float fracP = (float)i / Math.Max(1, br.Points.Length - 1);
                            float capRadius = Mathf.Lerp(br.Thickness, br.Thickness * 0.35f, fracP) * 0.5f;
                            if (capRadius > 0.8f)
                            {
                                Vector2 capPos = EnableWind && windOffset != Vector2.Zero 
                                    ? br.Points[i] + windOffset * br.WindMultipliers[i]
                                    : br.Points[i];
                                DrawCircle(capPos, capRadius, c);
                            }
                        }
                    }
                }
            }
        }

        // Draw leaf clusters
        if (drawLeaves && leafClusterCount > 0 && leafTemplateCached)
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

                for (int i = 0; i < leafClusterCount; i++)
                {
                    var cluster = optimizedLeafClusters[i];
                    DrawSetTransform(cluster.Position, cluster.Rotation, Vector2.One * cluster.Scale);
                    DrawTexture(cachedLeafTexture, -texCenter, baseLeafColor);
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
            }
            else
            {
                // Fallback to simple circles
                float eff = EffectiveLeafSize;
                for (int i = 0; i < leafClusterCount; i++)
                {
                    var cluster = optimizedLeafClusters[i];
                    DrawCircle(cluster.Position, eff * 0.7f * cluster.Scale, baseLeafColor);
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (!_initialDrawDone)
        {
            needFullRedraw = true;
            QueueRedraw();
            _initialDrawDone = true;
        }

        // OPTIMIZATION: Only update wind when needed
        if (EnableWind)
        {
            _windTime += dt;
            windNeedsUpdate = true;
            
            if (Mathf.Abs(WindStrength) > 0.01f)
            {
                // Update cached wind offset
                cachedWindOffset = new Vector2(
                    Mathf.Sin(_windTime * 2f) * WindStrength,
                    Mathf.Sin(_windTime * 1.5f) * WindStrength * 0.5f
                );
                ApplyWindEffect(WindStrength, Vector2.Right);
            }
            else
            {
                cachedWindOffset = Vector2.Zero;
            }
            
            needFullRedraw = true;
        }

        // OPTIMIZATION: Batch update falling leaves
        if (fallingLeafCount > 0)
        {
            UpdateFallingLeavesOptimized(dt);
        }

        // Spawn new falling leaves
        if (leavesFalling && drawLeaves && leafClusterCount > 0)
        {
            _leafSpawnAccumulator += leafFallSpawnRate * dt;
            int toSpawn = (int)_leafSpawnAccumulator;
            if (toSpawn > 0)
            {
                _leafSpawnAccumulator -= toSpawn;
                for (int i = 0; i < toSpawn && fallingLeafCount < maxFallingLeaves; i++)
                {
                    int idx = random.Next(0, leafClusterCount);
                    var cluster = optimizedLeafClusters[idx];
                    SpawnFallingLeaf(cluster.Position, cluster.Scale);
                }
            }
        }

        if (fallingLeafCount > 0)
        {
            if (fallingLeafRenderer != null)
                fallingLeafRenderer.QueueRedraw();
            else
                QueueRedraw();
        }
    }

    // OPTIMIZATION: Batch update falling leaves with minimal allocations
    private void UpdateFallingLeavesOptimized(float dt)
    {
        int write = 0;
        float windX = 0f;
        
        if (EnableWind)
        {
            windX = Mathf.Sin(_windTime * 2.0f) * WindStrength * leafWindInfluence * dt * 40f;
        }
        
        float gravityDelta = leafFallGravity * dt;
        float dragFactor = 1f - Mathf.Clamp(0.02f * dt, 0f, 0.2f);
        float flutterTime = (float)Time.GetTicksMsec() / 1000.0f;
        
        for (int i = 0; i < fallingLeafCount; i++)
        {
            var lf = fallingLeaves[i];
            
            // Physics update
            lf.Velocity.Y += gravityDelta;
            
            if (EnableWind)
            {
                lf.Velocity.X += windX * Mathf.Sin(flutterTime + lf.BasePhase);
            }
            
            lf.Velocity *= dragFactor;
            lf.Position += lf.Velocity * dt;
            lf.Rotation += lf.AngularVelocity * dt + Mathf.Sin(flutterTime + lf.BasePhase) * 0.0025f;
            lf.Age += dt;
            
            // Check if leaf should be kept
            bool keep = true;
            if (despawnOnGroundOnly)
            {
                keep = lf.Position.Y < leafGroundY;
            }
            else
            {
                keep = lf.Age < lf.Lifetime;
            }
            
            if (keep)
            {
                fallingLeaves[write++] = lf;
            }
        }
        
        fallingLeafCount = write;
    }

    private void CreateLeafTemplate()
    {
        if (leafTemplateCached) return;

        cachedLeafTemplate = new LeafTemplate();
        cachedLeafTemplate.BaseFragments.Add(new LeafFragment
        {
            Offset = Vector2.Zero,
            Size = 1.0f,
            Color = new Color(1f, 1f, 1f, 1f)
        });
        cachedLeafTemplate.TemplateRadius = 1.0f;
        leafTemplateCached = true;
    }

    private void AddOptimizedLeafCluster(Vector2 position)
    {
        if (!leafTemplateCached)
            CreateLeafTemplate();

        if (random == null)
            random = new Random(seed);

        // OPTIMIZATION: Ensure array capacity
        if (leafClusterCount >= optimizedLeafClusters.Length)
        {
            int newSize = Math.Max(16, optimizedLeafClusters.Length * 2);
            Array.Resize(ref optimizedLeafClusters, newSize);
        }

        optimizedLeafClusters[leafClusterCount++] = new OptimizedLeafCluster
        {
            Position = position,
            OriginalPosition = position,
            Scale = 0.8f + (float)random.NextDouble() * 0.4f,
            Rotation = (float)(random.NextDouble() * Mathf.Pi * 2)
        };
    }

    private void GenerateTree()
    {
        Log.System("Generating tree...", "OrganicFractalTree");

        foreach (Node child in GetChildren())
        {
            if (child != fallingLeafRenderer) // Keep the renderer
                child.QueueFree();
        }

        // Reset collections
        leafClusterCount = 0;
        branches.Clear();
        appleInstances.Clear();
        fallingLeafCount = 0;

        random = new Random(seed);

        CreateLeafTemplate();
        CreateLeafTexture();

        Vector2 startPos = Vector2.Zero;
        Vector2 startDir = Vector2.Up;

        GrowBranch(startPos, startDir, initialLength, initialThickness, 0, 0, true);

        if (hasApples && applePrefab != null && branches.Count > 0)
        {
            PlaceApples();
        }
        else if (hasApples && applePrefab == null)
        {
            Log.Error("Apple prefab not set!", "OrganicFractalTree");
        }

        needFullRedraw = true;
        QueueRedraw();

        Log.Info($"Tree generated with {branches.Count} branches and {leafClusterCount} leaf clusters", "OrganicFractalTree");
        if (hasApples)
        {
            Log.Info($"Placed {appleInstances.Count} apples", "OrganicFractalTree");
        }
    }

    private void SpawnFallingLeaf(Vector2 origin, float baseScale)
    {
        if (random == null) random = new Random(seed);
        if (fallingLeafCount >= maxFallingLeaves) return;

        FallingLeaf lf = new FallingLeaf();
        lf.Position = origin + new Vector2((float)(random.NextDouble() - 0.5) * EffectiveLeafSize * 2f, (float)(random.NextDouble() - 0.2) * EffectiveLeafSize);
        lf.Scale = baseScale * (0.6f + (float)random.NextDouble() * 0.8f);
        
        float randX = (float)(random.NextDouble() - 0.5) * 2f * leafVelocityRandomness.X;
        float randY = (float)random.NextDouble() * leafVelocityRandomness.Y;
        lf.Velocity = new Vector2(leafInitialVelocity.X + randX, leafInitialVelocity.Y + randY);
        lf.Rotation = (float)(random.NextDouble() * Mathf.Pi * 2f);
        lf.AngularVelocity = (float)(random.NextDouble() - 0.5) * 2f;
        lf.Age = 0f;
        lf.Lifetime = Mathf.Lerp(leafFallLifetimeRange.X, leafFallLifetimeRange.Y, (float)random.NextDouble());
        lf.BasePhase = (float)random.NextDouble() * Mathf.Pi * 2f;

        fallingLeaves[fallingLeafCount++] = lf;
    }

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
        if (depth > maxDepth || length < 3.0f || thickness < 0.8f)
        {
            if (drawLeaves)
            {
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

        var points = new List<Vector2>();
        Vector2 currentPos = startPos;
        Vector2 currentDir = direction.Normalized();

        int segments = Math.Clamp((int)(length / 16), 2, 8);
        float segmentLength = length / segments;

        points.Add(currentPos);

        for (int i = 0; i < segments; i++)
        {
            float progress = (float)i / segments;

            if (isTrunk && trunkCurve > 0)
            {
                float trunkCurveAmount = Mathf.DegToRad(trunkCurve) * (float)(random.NextDouble() - 0.5) * 0.3f;
                currentDir = currentDir.Rotated(trunkCurveAmount);
            }

            float curveAmount = (float)(random.NextDouble() - 0.5) * curvature;
            currentDir = currentDir.Rotated(curveAmount);
            currentDir = currentDir.Normalized();

            currentPos += currentDir * segmentLength;
            points.Add(currentPos);
        }

        float depthFactor = 1.0f - (depth / (float)maxDepth) * 0.4f;
        Color branchColor = trunkColor * new Color(depthFactor, depthFactor, depthFactor, 1.0f);

        // OPTIMIZATION: Pre-calculate wind multipliers
        var windMults = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            float distance = points[i].DistanceTo(Vector2.Zero);
            windMults[i] = Mathf.Clamp(distance / (initialLength + 0.0001f), 0f, 1.5f) * (i / (float)points.Count);
        }

        var branchData = new BranchData
        {
            Depth = depth,
            Points = points.ToArray(),
            Thickness = thickness,
            EndDirection = currentDir,
            Color = branchColor,
            Alpha = 1.0f,
            WindMultipliers = windMults
        };
        branches.Add(branchData);

        Vector2 endPos = currentPos;

        if (random.NextDouble() > branchProbability)
        {
            if (drawLeaves)
            {
                AddOptimizedLeafCluster(endPos);
            }
            return;
        }

        int branchCount = random.Next(minBranches, maxBranches + 1);

        if (depth > maxDepth * 0.7f)
        {
            branchCount = Math.Min(2, branchCount);
        }

        if (isTrunk)
        {
            branchCount = Math.Max(branchCount, 3);
        }

        for (int i = 0; i < branchCount; i++)
        {
            float baseAngle = Mathf.DegToRad(branchAngle);
            float variation = Mathf.DegToRad(angleVariation) * (float)(random.NextDouble() - 0.5) * 2;
            float asymmetryFactor = (float)(random.NextDouble() - 0.5) * asymmetry;

            float angle;
            if (branchCount == 2)
            {
                angle = (i == 0 ? -baseAngle : baseAngle) + variation + asymmetryFactor;
            }
            else
            {
                float angleRange = baseAngle * 1.8f;
                angle = -angleRange / 2 + (angleRange / (branchCount - 1)) * i + variation;
            }

            angle += parentAngle * 0.15f;

            Vector2 newDir = currentDir.Rotated(angle);
            float lengthVariation = 0.7f + (float)random.NextDouble() * 0.5f;
            float newLength = length * lengthReduction * lengthVariation;
            float newThickness = thickness * thicknessReduction;

            if (random.NextDouble() > 0.1f)
            {
                GrowBranch(endPos, newDir, newLength, newThickness, depth + 1, angle, false);
            }
        }
    }

    private void PlaceApples()
    {
        if (GetTree().CurrentScene != null && SceneContainsNodeOfType<AppleTree>(GetTree().CurrentScene))
        {
            Log.System("AppleTree manager found in scene; skipping procedural apple placement.", "OrganicFractalTree");
            return;
        }

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

        var candidates = new List<(Vector2 pos, Vector2 normal, float frac, BranchData branch)>();

        foreach (var branch in suitableBranches)
        {
            int len = branch.Points.Length;
            if (len < 2) continue;

            int start = Math.Max(1, (int)(len * 0.3f));
            for (int s = 0; s < 3; s++)
            {
                float fracJitter = ((float)s - 1) * 0.08f;
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

        float ScoreCandidate((Vector2 pos, Vector2 normal, float frac, BranchData branch) c)
        {
            float prefScore = 1.0f - Math.Abs(c.frac - applePlacementHeight);
            float thicknessScore = c.branch.Thickness / (initialThickness + 0.0001f);
            return prefScore * 0.7f + thicknessScore * 0.3f;
        }

        while (chosen.Count < target && candidates.Count > 0)
        {
            int need = target - chosen.Count;

            if (need >= 2)
            {
                float bestPairScore = float.MinValue;
                int bestA = -1, bestB = -1;

                for (int a = 0; a < candidates.Count; a++)
                {
                    var ca = candidates[a];
                    for (int b = a + 1; b < candidates.Count; b++)
                    {
                        var cb = candidates[b];
                        if (ca.branch == cb.branch && Math.Abs(ca.frac - cb.frac) < 0.02f) continue;

                        float normalDot = ca.normal.Dot(cb.normal);
                        if (normalDot > 0.7f) continue;

                        float pairScore = ScoreCandidate(ca) + ScoreCandidate(cb);
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
                    var ca = candidates[bestA];
                    var cb = candidates[bestB];
                    if (MinDistToChosen(ca.pos) >= appleMinDistance && MinDistToChosen(cb.pos) >= appleMinDistance && ca.pos.DistanceTo(cb.pos) >= appleMinDistance)
                    {
                        chosen.Add(ca);
                        chosen.Add(cb);
                        if (bestA > bestB) { candidates.RemoveAt(bestA); candidates.RemoveAt(bestB); }
                        else { candidates.RemoveAt(bestB); candidates.RemoveAt(bestA); }
                        continue;
                    }
                }
            }

            float bestScore = float.MinValue;
            int bestIdx = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                float md = MinDistToChosen(c.pos);
                if (md < appleMinDistance) continue;

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
                break;
            }
        }

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

    public void Regenerate()
    {
        GenerateTree();
    }

    public void SetSeedAndRegenerate(int newSeed)
    {
        seed = newSeed;
        GenerateTree();
    }

    public void RandomizeAndRegenerate()
    {
        seed = (int)GD.RandRange(0, 999999);
        GenerateTree();
    }

    public async void AnimateGrowth()
    {
        needFullRedraw = true;

        foreach (var branchData in branches)
        {
            branchData.Alpha = 0f;
        }

        for (int depth = 0; depth <= maxDepth; depth++)
        {
            foreach (var branchData in branches)
            {
                if (branchData.Depth == depth)
                {
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

        needFullRedraw = true;
        QueueRedraw();
    }

    public void InvalidateLeafTemplate()
    {
        leafTemplateCached = false;
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

    private void CreateLeafTexture()
    {
        if (!leafTemplateCached || cachedLeafTemplate == null)
            return;
        
        int pixelsPerUnit = 10;
        float radiusUnits = Math.Max(0.5f, cachedLeafTemplate.TemplateRadius * EffectiveLeafSize);
        int texRadiusPx = (int)Math.Ceiling(radiusUnits * pixelsPerUnit) + 2;
        int width = texRadiusPx * 2;
        int height = width;

        string key = $"d{leafSnowflakeDepth}_s{EffectiveLeafSize:F2}_ppu{pixelsPerUnit}";

        if (SharedLeafTextureCache.TryGetValue(key, out var sharedTex))
        {
            cachedLeafTexture = sharedTex;
            leafTextureCachedRaster = true;
            leafTextureCacheKey = key;
            SharedLeafTextureRefCount[key] = SharedLeafTextureRefCount.TryGetValue(key, out var rc) ? rc + 1 : 1;
            return;
        }

        var img = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        Vector2 center = new Vector2(width / 2f, height / 2f);

        void SetOpaquePixel(int px, int py)
        {
            if ((uint)px >= (uint)width || (uint)py >= (uint)height) return;
            img.SetPixel(px, py, new Color(1f, 1f, 1f, 1f));
        }

        List<Vector2> GenerateKochSnowflake(Vector2 c, float radius, int depth)
        {
            var points = new List<Vector2>();
            
            for (int i = 0; i < 3; i++)
            {
                float angle = Mathf.Pi * 2f * i / 3f - Mathf.Pi / 2f;
                points.Add(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            
            for (int d = 0; d < depth; d++)
            {
                points = ApplyKochIteration(points);
            }
            
            return points;
        }

        List<Vector2> ApplyKochIteration(List<Vector2> inputPoints)
        {
            var newPoints = new List<Vector2>();
            
            for (int i = 0; i < inputPoints.Count; i++)
            {
                Vector2 a = inputPoints[i];
                Vector2 b = inputPoints[(i + 1) % inputPoints.Count];
                
                Vector2 segment = b - a;
                float segmentLength = segment.Length();
                Vector2 direction = segment.Normalized();
                
                Vector2 p1 = a;
                Vector2 p2 = a + direction * (segmentLength / 3f);
                Vector2 p4 = a + direction * (segmentLength * 2f / 3f);
                
                Vector2 midpoint = (p2 + p4) * 0.5f;
                Vector2 perpendicular = new Vector2(-direction.Y, direction.X);
                float triangleHeight = (segmentLength / 3f) * Mathf.Sqrt(3f) / 2f;
                Vector2 p3 = midpoint + perpendicular * triangleHeight;
                
                newPoints.Add(p1);
                newPoints.Add(p2);
                newPoints.Add(p3);
                newPoints.Add(p4);
            }
            
            return newPoints;
        }

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

        float radiusPixels = cachedLeafTemplate.TemplateRadius * EffectiveLeafSize * pixelsPerUnit;
        float armWidth = Math.Max(1f, pixelsPerUnit * 0.12f * EffectiveLeafSize);

        var snowPts = GenerateKochSnowflake(center, radiusPixels, leafSnowflakeDepth);

        for (int i = 0, sc = snowPts.Count; i < sc; i++)
        {
            Vector2 a = snowPts[i];
            Vector2 b = snowPts[(i + 1) % sc];
            DrawLineOnImage(a, b, armWidth);
        }

        int centerRadius = Math.Max(1, (int)(pixelsPerUnit * 0.6f * EffectiveLeafSize));
        for (int y = -centerRadius; y <= centerRadius; y++)
        {
            for (int x = -centerRadius; x <= centerRadius; x++)
            {
                if (x * x + y * y <= centerRadius * centerRadius)
                    SetOpaquePixel((int)center.X + x, (int)center.Y + y);
            }
        }

        if (cachedLeafTexture != null)
        {
            cachedLeafTexture.Dispose();
            cachedLeafTexture = null;
        }
        cachedLeafTexture = ImageTexture.CreateFromImage(img);
        leafTextureCachedRaster = true;
        leafTextureCacheKey = key;

        SharedLeafTextureCache[key] = cachedLeafTexture;
        SharedLeafTextureRefCount[key] = 1;

        Log.System($"Rasterized leaf texture created ({width}x{height})", "OrganicFractalTree");
    }

    public Dictionary<string, object> GetPerformanceStats()
    {
        return new Dictionary<string, object>
        {
            {"BranchCount", branches.Count},
            {"LeafClusterCount", leafClusterCount},
            {"LeafTemplateFragments", leafTemplateCached ? cachedLeafTemplate.BaseFragments.Count : 0},
            {"AppleCount", appleInstances.Count},
            {"MemoryEstimate", GetEstimatedMemoryUsage()}
        };
    }

    private long GetEstimatedMemoryUsage()
    {
        long total = 0;

        total += branches.Count * (sizeof(int) + sizeof(float) * 3 + 16 * 4 + 16);
        foreach (var branch in branches)
        {
            if (branch.Points != null)
                total += branch.Points.Length * 8;
        }

        total += leafClusterCount * (8 + sizeof(float) * 2 + 16);

        if (leafTemplateCached)
        {
            total += cachedLeafTemplate.BaseFragments.Count * (8 + sizeof(float) + 16);
        }

        return total;
    }

    public void ForceRedraw()
    {
        needFullRedraw = true;
        QueueRedraw();
    }

    public Vector2[] GetLeafClusterPositions()
    {
        var positions = new Vector2[leafClusterCount];
        for (int i = 0; i < leafClusterCount; i++)
        {
            positions[i] = optimizedLeafClusters[i].Position;
        }
        return positions;
    }

    public void ApplyWindEffect(float windStrength, Vector2 windDirection)
    {
        if (!leafTemplateCached || leafClusterCount == 0)
            return;

        windDirection = windDirection.Normalized();
        Vector2 perp = new Vector2(-windDirection.Y, windDirection.X);
        float time = (float)Time.GetTicksMsec() / 1000.0f;

        float baseLeafDisplacement = 6.0f;

        for (int i = 0; i < leafClusterCount; i++)
        {
            var cluster = optimizedLeafClusters[i];

            Vector2 origin = cluster.OriginalPosition != Vector2.Zero ? cluster.OriginalPosition : cluster.Position;

            float along = Mathf.Sin(time * 2.0f + origin.X * 0.01f) * windStrength;
            float across = Mathf.Sin(time * 1.5f + origin.Y * 0.01f) * windStrength * 0.5f;

            Vector2 displacement = windDirection * (along * baseLeafDisplacement * cluster.Scale)
                                 + perp * (across * baseLeafDisplacement * 0.5f * cluster.Scale);

            float windEffect = Mathf.Sin(time * 2.0f + origin.X * 0.01f) * windStrength;
            cluster.Rotation += windEffect * 0.1f;

            float scaleVariation = 1.0f + Mathf.Sin(time * 1.5f + origin.Y * 0.01f) * windStrength * 0.05f;
            cluster.Scale = Mathf.Clamp(cluster.Scale * scaleVariation, 0.5f, 1.5f);

            cluster.Position = origin + displacement;
            optimizedLeafClusters[i] = cluster; // Write back struct
        }

        needFullRedraw = true;
        QueueRedraw();
    }

    public (Vector2 Position, float Rotation, float Scale)[] GetFallingLeafInfos()
    {
        var arr = new (Vector2, float, float)[fallingLeafCount];
        for (int i = 0; i < fallingLeafCount; i++)
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