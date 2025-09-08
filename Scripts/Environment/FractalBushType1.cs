using Godot;
using System;
using System.Collections.Generic;

public partial class FractalBushType1 : Node2D
{
    [Export] public float InitialLength = 100.0f;
    [Export] public float LengthReduction = 0.7f;
    [Export] public float AngleVariation = 25.0f;
    [Export] public int MaxDepth = 8;
    [Export] public int BranchCount = 2;
    [Export] public float MinLength = 5.0f;
    [Export] public Color BranchColor = Colors.SaddleBrown;
    [Export] public float LineWidth = 3.0f;
    [Export] public bool EnableWind = false;
    [Export] public float WindStrength = 0.5f;

    // Performance: pre-generate branches and draw them in _Draw
    private struct Branch
    {
        public Vector2 Start;
        public Vector2 End;
        public float Width;

        public Branch(Vector2 s, Vector2 e, float w)
        {
            Start = s; End = e; Width = w;
        }
    }

    private List<Branch> _branches;
    private FastNoiseLite _noise;
    private float _windTime = 0f;
    private Random random = new Random();
    
    public override void _Ready()
    {
    // Initialize noise used for subtle organic variation (wind/noise)
    _noise = new FastNoiseLite();
    _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
    _noise.Seed = (int)(GD.Randf() * 10000);
    _noise.Frequency = 0.1f;

    _branches = new List<Branch>(1024);
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
    
    public override void _Draw()
    {
        // Draw pre-generated branches with optional wind offset
        Vector2 windOffset = Vector2.Zero;
        if (EnableWind)
        {
            windOffset = new Vector2(
                Mathf.Sin(_windTime * 2f) * WindStrength,
                Mathf.Sin(_windTime * 1.5f) * WindStrength * 0.5f
            );
        }

        for (int i = 0; i < _branches.Count; i++)
        {
            var b = _branches[i];

            // Apply wind more strongly to tips (further from origin)
            float windMultiplier = b.Start.DistanceTo(Vector2.Zero) / InitialLength;
            Vector2 s = b.Start + windOffset * windMultiplier;
            Vector2 e = b.End + windOffset * windMultiplier;

            DrawLine(s, e, BranchColor, b.Width);
            if (b.Width > 2f)
                DrawCircle(e, b.Width * 0.5f, BranchColor);
        }
    }

    private void GenerateBush()
    {
        _branches.Clear();

        Vector2 startPos = Vector2.Zero;
        Vector2 direction = Vector2.Up;
        // Start recursion that fills _branches
        GenerateBranch(startPos, direction, InitialLength, LineWidth, 0);
        QueueRedraw();
    }

    private void GenerateBranch(Vector2 startPos, Vector2 direction, float length, float width, int depth)
    {
        // Stop if we've reached max depth or minimum length
        if (depth >= MaxDepth || length < MinLength || width < 0.5f)
            return;

        // Root behavior: spawn several base branches to form a bushy base
        if (depth == 0)
        {
            int baseCount = Math.Max(BranchCount * 3, 5);
            for (int i = 0; i < baseCount; i++)
            {
                float baseAngle = (i - (baseCount - 1) / 2.0f) * (AngleVariation * 0.5f / (baseCount - 1));
                float angleOffset = baseAngle + (float)(random.NextDouble() - 0.5) * (AngleVariation * 0.25f);
                float angleRad = Mathf.DegToRad(angleOffset);
                Vector2 newDirection = direction.Rotated(angleRad);

                if (newDirection.Y > 0f)
                    continue;

                float lengthMultiplier = 0.5f + (float)(random.NextDouble() * 0.6f);
                float newLength = length * lengthMultiplier;
                float newWidth = width * 0.9f;

                GenerateBranch(startPos, newDirection, newLength, newWidth, depth + 1);
            }
            return;
        }

        // Add subtle noise-based angle variation for organic look
        float noiseValue = _noise.GetNoise2D(startPos.X * 0.01f, startPos.Y * 0.01f + depth * 0.1f);
        float angleNoise = noiseValue * Mathf.DegToRad(AngleVariation * 0.5f);

        Vector2 noisyDir = direction.Rotated(angleNoise);
        Vector2 endPos = startPos + noisyDir * length;

        // Store this segment for drawing
        _branches.Add(new Branch(startPos, endPos, width));

        // Generate child branches
        if (depth < MaxDepth - 1)
        {
            int actualBranchCount = Mathf.Max(1, BranchCount - depth / 2);
            for (int i = 0; i < actualBranchCount; i++)
            {
                float branchPosition = (float)random.NextDouble() * 0.4f + 0.6f; // 60-100%
                Vector2 branchStart = startPos.Lerp(endPos, branchPosition);

                float baseAngle = (BranchCount > 1)
                    ? (i - (actualBranchCount - 1) / 2.0f) * (AngleVariation / Math.Max(1, actualBranchCount - 1))
                    : 0f;
                float angleOffset = baseAngle + (float)(random.NextDouble() - 0.5) * (AngleVariation * 0.3f);

                if (depth <= 1)
                    angleOffset = Mathf.Clamp(angleOffset, -AngleVariation * 0.6f, AngleVariation * 0.6f);

                float angleRad = Mathf.DegToRad(angleOffset);
                Vector2 childDir = noisyDir.Rotated(angleRad);

                float lengthMultiplier = LengthReduction + (float)(random.NextDouble() - 0.5) * 0.2f;
                float childLength = length * lengthMultiplier;
                float childWidth = width * 0.8f;

                GenerateBranch(branchStart, childDir, childLength, childWidth, depth + 1);
            }
        }
    }
    
    // Method to regenerate the bush with new random seed
    public void RegenerateBush()
    {
        random = new Random();
        if (_noise != null)
            _noise.Seed = (int)(GD.Randf() * 10000);
        GenerateBush();
    }
    
    // Public helper to regenerate with a new random seed (exposed)
    public void RegenerateWithRandomSeed()
    {
        if (_noise != null)
            _noise.Seed = (int)(GD.Randf() * 10000);
        GenerateBush();
    }
}