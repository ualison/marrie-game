using Godot;
using System.Collections.Generic;

public partial class FractalBush : Node2D
{
    [Export] public int MaxDepth { get; set; } = 9;
    [Export] public float InitialLength { get; set; } = 120f;
    [Export] public float LengthDecay { get; set; } = 0.75f;
    [Export] public float MinAngle { get; set; } = 8f;
    [Export] public float MaxAngle { get; set; } = 38f;
    [Export] public float AngleVariation { get; set; } = 8f;
    [Export] public float WidthDecay { get; set; } = 0.72f;
    [Export] public float InitialWidth { get; set; } = 14f;
    [Export] public int BranchCount { get; set; } = 4;
    [Export] public int TrunkCount { get; set; } = 3;
    [Export] public Color BranchColor { get; set; } = Colors.SaddleBrown;
    [Export] public bool EnableWind { get; set; } = false;
    [Export] public float WindStrength { get; set; } = 0.5f;
    
    // Pre-allocated collections for performance
    private List<Vector2[]> _branchSegments;
    private List<float> _branchWidths;
    private FastNoiseLite _noise;
    private float _windTime = 0f;
    
    // Structure to hold branch data for efficient rendering
    private struct Branch
    {
        public Vector2 Start;
        public Vector2 End;
        public float Width;
        public int Depth;
        
        public Branch(Vector2 start, Vector2 end, float width, int depth)
        {
            Start = start;
            End = end;
            Width = width;
            Depth = depth;
        }
    }
    
    private List<Branch> _branches;
    
    public override void _Ready()
    {
        // Initialize noise for organic variation
        _noise = new FastNoiseLite();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _noise.Seed = (int)(GD.Randf() * 10000);
        _noise.Frequency = 0.1f;
        
        // Pre-allocate collections
        _branches = new List<Branch>(1024); // Pre-allocate for typical branch count
        _branchSegments = new List<Vector2[]>();
        _branchWidths = new List<float>();
        
        GenerateBush();
    }
    
    public override void _Process(double delta)
    {
        if (EnableWind)
        {
            _windTime += (float)delta;
            QueueRedraw(); // Only redraw if wind is enabled
        }
    }
    
    public void GenerateBush()
    {
        _branches.Clear();
        // Create multiple trunks clustered near the center to form the thick base
        Vector2 basePos = new Vector2(0, 0);
        for (int t = 0; t < TrunkCount; t++)
        {
            // small horizontal offsets so trunks are close but distinct
            float offsetX = (t - (TrunkCount - 1) / 2.0f) * (InitialWidth * 0.6f) + GD.Randf() * 4f - 2f;
            Vector2 startPos = basePos + new Vector2(offsetX, 0);
            Vector2 direction = Vector2.Up.Rotated((GD.Randf() - 0.5f) * 0.06f); // slight tilt
            GenerateBranch(startPos, direction, InitialLength * (0.9f + GD.Randf() * 0.2f), InitialWidth * (0.8f + GD.Randf() * 0.4f), 0, 0f);
        }
        
        QueueRedraw();
    }
    
    private void GenerateBranch(Vector2 start, Vector2 direction, float length, float width, int depth, float parentAngle)
    {
        if (depth >= MaxDepth || length < 5f || width < 0.5f)
            return;
        
        // Add organic noise to the direction
        float noiseValue = _noise.GetNoise2D(start.X * 0.01f, start.Y * 0.01f + depth * 0.1f);
        float angleOffset = noiseValue * Mathf.DegToRad(AngleVariation);
        
        // Calculate end position
        Vector2 noisyDirection = direction.Rotated(angleOffset);
        Vector2 end = start + noisyDirection * length;
        
    // Add the branch to our collection (store depth for later drawing tweaks)
    _branches.Add(new Branch(start, end, width, depth));
        
        // Generate child branches
        if (depth < MaxDepth - 1)
        {
            int actualBranchCount = Mathf.Max(1, BranchCount - depth / 2); // Fewer branches at higher depths
            
            for (int i = 0; i < actualBranchCount; i++)
            {
                // Calculate branch position (not always at the end, sometimes partway)
                float branchPosition = GD.Randf() * 0.4f + 0.6f; // Between 60% and 100% of branch length
                Vector2 branchStart = start.Lerp(end, branchPosition);
                
                // Calculate branch angle with variation
                float baseAngle = Mathf.Lerp(MinAngle, MaxAngle, GD.Randf());
                float angleVariation = (GD.Randf() - 0.5f) * AngleVariation;
                float finalAngle = Mathf.DegToRad(baseAngle + angleVariation);
                
                // Alternate sides and add some randomness
                if (i % 2 == 0)
                    finalAngle = -finalAngle;
                
                // Add some upward bias to make it look more natural
                finalAngle += (GD.Randf() - 0.5f) * 0.2f;
                
                Vector2 branchDirection = noisyDirection.Rotated(finalAngle);
                
                // Calculate new length and width
                float newLength = length * LengthDecay * (0.8f + GD.Randf() * 0.4f); // 80-120% variation
                float newWidth = width * WidthDecay;
                
                GenerateBranch(branchStart, branchDirection, newLength, newWidth, depth + 1, finalAngle);
            }
        }
    }
    
    public override void _Draw()
    {
        // Calculate wind effect if enabled
        Vector2 windOffset = Vector2.Zero;
        if (EnableWind)
        {
            windOffset = new Vector2(
                Mathf.Sin(_windTime * 2f) * WindStrength,
                Mathf.Sin(_windTime * 1.5f) * WindStrength * 0.5f
            );
        }
        
        // Draw all branches as filled quads for a solid silhouette
        for (int i = 0; i < _branches.Count; i++)
        {
            Branch branch = _branches[i];

            // Apply wind effect more strongly to branch tips
            float windMultiplier = branch.Start.DistanceTo(Vector2.Zero) / InitialLength;
            Vector2 windAdjustedStart = branch.Start + windOffset * windMultiplier;
            Vector2 windAdjustedEnd = branch.End + windOffset * windMultiplier;

            // Taper width slightly by depth
            float widthStart = branch.Width;
            float widthEnd = Mathf.Max(0.8f, branch.Width * 0.35f);
            if (branch.Depth > 2)
            {
                widthStart *= Mathf.Clamp(1f - branch.Depth * 0.06f, 0.4f, 1f);
            }

            // Compute perpendicular vector to create a quad
            Vector2 dir = (windAdjustedEnd - windAdjustedStart).Normalized();
            Vector2 perp = new Vector2(-dir.Y, dir.X);

            Vector2 a = windAdjustedStart + perp * (widthStart * 0.5f);
            Vector2 b = windAdjustedStart - perp * (widthStart * 0.5f);
            Vector2 c = windAdjustedEnd - perp * (widthEnd * 0.5f);
            Vector2 d = windAdjustedEnd + perp * (widthEnd * 0.5f);

            // Draw main branch polygon
            Vector2[] poly = new Vector2[] { a, b, c, d };
            DrawPolygon(poly, new Color[] { BranchColor });

            // Slightly darker inner layer for perceived depth on thicker branches
            if (branch.Width > 6f)
            {
                Color inner = BranchColor.Darkened(0.12f);
                Vector2 insetA = windAdjustedStart + perp * (widthStart * 0.36f);
                Vector2 insetB = windAdjustedStart - perp * (widthStart * 0.36f);
                Vector2 insetC = windAdjustedEnd - perp * (widthEnd * 0.36f);
                Vector2 insetD = windAdjustedEnd + perp * (widthEnd * 0.36f);
                Vector2[] innerPoly = new Vector2[] { insetA, insetB, insetC, insetD };
                DrawPolygon(innerPoly, new Color[] { inner });
            }
        }
    }
    
    // Public method to regenerate with new parameters
    public void RegenerateWithRandomSeed()
    {
        _noise.Seed = (int)(GD.Randf() * 10000);
        GenerateBush();
    }
    
    // Method to animate growth
    public void AnimateGrowth()
    {
        var tween = CreateTween();
        float originalLength = InitialLength;
        InitialLength = 0f;
        GenerateBush();
        
        tween.TweenMethod(Callable.From<float>(SetGrowthProgress), 0f, originalLength, 2f);
        tween.TweenCallback(Callable.From(() => InitialLength = originalLength));
    }
    
    private void SetGrowthProgress(float progress)
    {
        InitialLength = progress;
        GenerateBush();
    }
}