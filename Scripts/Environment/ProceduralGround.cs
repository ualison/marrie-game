using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ProceduralGround : Node2D
{
    [Export] public float GroundWidth { get; set; } = 1303.0f;
    [Export] public float GroundHeight { get; set; } = 118.0f;
    [Export] public int GrassDensity { get; set; } = 300;
    [Export] public float GrassHeightMin { get; set; } = 30.0f;
    [Export] public float GrassHeightMax { get; set; } = 120.0f;
    [Export] public float GrassWidthMin { get; set; } = 2.0f;
    [Export] public float GrassWidthMax { get; set; } = 8.0f;
    [Export] public float GrassAngleVariance { get; set; } = 45.0f;
    [Export] public float GrassCurvature { get; set; } = 0.3f;
    [Export] public float WindStrength { get; set; } = 0.0f;
    [Export] public float GrassSharpness { get; set; } = 0.8f;
    [Export] public int GrassSegments { get; set; } = 6;
    [Export] public Color GroundColor { get; set; } = new Color(0.2f, 0.4f, 0.1f);
    [Export] public Color GrassColorBase { get; set; } = new Color(0.1f, 0.2f, 0.05f);
    [Export] public Color GrassColorMid { get; set; } = new Color(0.15f, 0.4f, 0.1f);
    [Export] public Color GrassColorTip { get; set; } = new Color(0.2f, 0.6f, 0.15f);
    [Export] public int SeedValue { get; set; } = 0;
    [Export] public int SummitCount { get; set; } = 128;
    
    // New parameters for better ground-grass connection
    [Export] public float GroundBlendDepth { get; set; } = 8.0f; // How deep grass blends into ground
    [Export] public float GroundTextureDensity { get; set; } = 0.3f; // Small ground texture variations
    [Export] public bool UseAdaptiveOccluder { get; set; } = true; // Adapt occluder to grass positions
    [Export] public float RedrawFPS { get; set; } = 30.0f; // Max redraws per second when wind is active

    private FastNoiseLite _noise;
    private FastNoiseLite _groundTextureNoise; // For fine ground details
    private List<GrassBlade> _grassBlades = new List<GrassBlade>();
    private List<Vector2> _groundContour = new List<Vector2>(); // Cache ground contour
    private Random _random;
    private double _redrawAccumulator = 0.0;

    public override void _Ready()
    {
        SetupNoise();
        GenerateGroundContour();
        GenerateGrassBlades();
    }

    private void SetupNoise()
    {
        _noise = new FastNoiseLite();
        _groundTextureNoise = new FastNoiseLite();
        
        if (SeedValue > 0)
        {
            _noise.Seed = SeedValue;
            _groundTextureNoise.Seed = SeedValue + 1000;
            _random = new Random(SeedValue);
        }
        else
        {
            var seed = GD.Randi();
            _noise.Seed = (int)seed;
            _groundTextureNoise.Seed = (int)seed + 1000;
            _random = new Random((int)seed);
        }
        
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _noise.Frequency = 0.05f;
        
        // Finer noise for ground texture details
        _groundTextureNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _groundTextureNoise.Frequency = 0.2f;
    }

    private void GenerateGroundContour()
    {
        _groundContour.Clear();
        
        int sampleCount;
        if (SummitCount > 0)
            sampleCount = Mathf.Clamp(SummitCount, 8, 1024);
        else
            sampleCount = Mathf.Clamp((int)(GroundWidth / 4.0f), 64, 512); // Higher resolution
            
        float startX = -GroundWidth / 2.0f;
        float step = GroundWidth / (sampleCount - 1);

        for (int i = 0; i < sampleCount; i++)
        {
            float x = startX + i * step;
            float y = GetGroundHeightAtX(x);
            _groundContour.Add(new Vector2(x, y));
        }
    }

    private void GenerateGrassBlades()
    {
        _grassBlades.Clear();

        for (int i = 0; i < GrassDensity; i++)
        {
            float clusterX = (float)_random.NextDouble() * GroundWidth - GroundWidth / 2;
            float clusterSpread = 15.0f;
            float x = clusterX + ((float)_random.NextDouble() - 0.5f) * clusterSpread;
            x = Mathf.Clamp(x, -GroundWidth / 2 + 10, GroundWidth / 2 - 10);
            
            float groundY = GetGroundHeightAtX(x);
            Vector2 basePos = new Vector2(x, groundY);

            GrassBlade blade = CreateOrganicGrassBlade(basePos);
            _grassBlades.Add(blade);
        }

        _grassBlades.Sort((a, b) => a.BasePosition.X.CompareTo(b.BasePosition.X));
        QueueRedraw();
    }

    private float GetGroundHeightAtX(float x)
    {
        float noiseValue = _noise.GetNoise1D(x * 0.01f);
        float amplitude = GroundHeight * 0.5f;
        
        // Add fine texture detail
        float textureNoise = _groundTextureNoise.GetNoise1D(x * 0.1f) * GroundTextureDensity;
        
        return -Mathf.Abs(noiseValue) * amplitude + textureNoise;
    }

    private GrassBlade CreateOrganicGrassBlade(Vector2 basePos)
    {
        float heightVariation = 0.8f + (float)_random.NextDouble() * 0.4f;
        float height = Mathf.Lerp(GrassHeightMin, GrassHeightMax, (float)_random.NextDouble()) * heightVariation;
        
        float widthVariation = 0.7f + (float)_random.NextDouble() * 0.6f;
        float baseWidth = Mathf.Lerp(GrassWidthMin, GrassWidthMax, (float)_random.NextDouble()) * widthVariation;
        
        float leanAngle = ((float)_random.NextDouble() - 0.5f) * GrassAngleVariance * (0.8f + (float)_random.NextDouble() * 0.4f);
        
        int segments = GrassSegments + _random.Next(-2, 3);
        segments = Math.Max(3, segments);
        
        List<Vector2> leftSide = new List<Vector2>();
        List<Vector2> rightSide = new List<Vector2>();
        
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float currentHeight = t * height;
            
            float curveVariation = 0.5f + (float)_random.NextDouble() * 1.0f;
            float curve = Mathf.Sin(t * Mathf.Pi) * GrassCurvature * height * 0.1f * curveVariation;
            float lean = Mathf.Sin(t * Mathf.Pi * 0.5f) * Mathf.DegToRad(leanAngle) * height * 0.3f;
            
            Vector2 centerPoint = new Vector2(
                basePos.X + curve + lean,
                basePos.Y - currentHeight
            );
            
            float widthFactor = Mathf.Pow(1.0f - t, 1.2f);
            float sharpnessVariation = GrassSharpness + ((float)_random.NextDouble() - 0.5f) * 0.3f;
            sharpnessVariation = Mathf.Clamp(sharpnessVariation, 0.3f, 1.0f);
            
            if (t > sharpnessVariation)
            {
                widthFactor *= Mathf.Pow((1.0f - t) / (1.0f - sharpnessVariation), 2.0f);
            }
            float currentWidth = baseWidth * widthFactor;
            
            float noiseVariation = _noise.GetNoise2D(basePos.X * 0.1f, currentHeight * 0.05f) * 0.4f;
            currentWidth *= (1.0f + noiseVariation);
            
            float jitterStrength = 0.1f + (float)_random.NextDouble() * 0.3f;
            float jitterX = ((float)_random.NextDouble() - 0.5f) * currentWidth * jitterStrength;
            
            Vector2 leftPoint = new Vector2(centerPoint.X - currentWidth * 0.5f + jitterX, centerPoint.Y);
            Vector2 rightPoint = new Vector2(centerPoint.X + currentWidth * 0.5f + jitterX, centerPoint.Y);
            
            leftSide.Add(leftPoint);
            rightSide.Add(rightPoint);
        }
        
        List<Vector2> bladePoints = new List<Vector2>();
        
        // Adaptive underground depth based on ground blend setting
        float undergroundDepth = Mathf.Max(baseWidth * 1.5f, GroundBlendDepth) + (float)_random.NextDouble() * GroundBlendDepth * 0.5f;
        
        // Create base points that curve slightly underground for natural emergence
        Vector2 leftBase = new Vector2(
            leftSide[0].X + ((float)_random.NextDouble() - 0.5f) * baseWidth * 0.3f, 
            leftSide[0].Y + undergroundDepth
        );
        Vector2 rightBase = new Vector2(
            rightSide[0].X + ((float)_random.NextDouble() - 0.5f) * baseWidth * 0.3f, 
            rightSide[0].Y + undergroundDepth
        );
        
        bladePoints.Add(leftBase);
        bladePoints.AddRange(leftSide);
        
        for (int i = rightSide.Count - 1; i >= 0; i--)
        {
            bladePoints.Add(rightSide[i]);
        }
        
        bladePoints.Add(rightBase);
        
        float colorVariation = (float)_random.NextDouble();
        float hueShift = ((float)_random.NextDouble() - 0.5f) * 0.1f;
        Color bladeColor = GrassColorBase.Lerp(GrassColorMid, colorVariation);
        bladeColor.H += hueShift;
        
        var blade = new GrassBlade
        {
            BasePosition = basePos,
            Points = bladePoints,
            Height = height,
            Color = bladeColor,
            LeanAngle = leanAngle,
            BaseWidth = baseWidth,
            UndergroundDepth = undergroundDepth
        };

    // Cache arrays for faster drawing
    blade.CachedPoints = blade.Points.ToArray();
        // Precompute per-vertex colors based on height so we don't recalc every frame
    var cachedColors = new Color[blade.CachedPoints.Length];
        for (int i = 0; i < blade.CachedPoints.Length; i++)
        {
            float heightFactor = Mathf.Clamp((blade.BasePosition.Y - blade.CachedPoints[i].Y) / blade.Height, -0.2f, 1.0f);
            Color pointColor;
            if (heightFactor < 0)
                pointColor = GroundColor.Lerp(blade.Color, 0.7f + heightFactor * 1.5f);
            else if (heightFactor < 0.5f)
                pointColor = blade.Color.Lerp(GrassColorMid, heightFactor * 2.0f);
            else
                pointColor = GrassColorMid.Lerp(GrassColorTip, (heightFactor - 0.5f) * 2.0f);

            cachedColors[i] = pointColor;
        }
    blade.CachedColors = cachedColors;

        return blade;
    }

    public override void _Draw()
    {
        // Draw back ground polygon
        DrawGroundBase();
        
        // Draw grass blades
        foreach (var blade in _grassBlades)
        {
            DrawGrassBlade(blade);
        }
        
        // Draw adaptive front occluder
        DrawAdaptiveGroundOccluder();
    }

    private void DrawGroundBase()
    {
        List<Vector2> polyPts = new List<Vector2>();
        List<Color> polyColors = new List<Color>();

        // Top contour with enhanced texture variation
        foreach (var point in _groundContour)
        {
            polyPts.Add(point);
            
            // Add texture-based color variation
            float textureVariation = _groundTextureNoise.GetNoise2D(point.X * 0.05f, point.Y * 0.05f);
            float shade = 1.0f - Mathf.Clamp(Mathf.Abs(point.Y) / (GroundHeight * 0.6f), 0.0f, 0.6f);
            shade += textureVariation * 0.1f;
            
            Color groundShade = new Color(
                GroundColor.R * (0.85f + 0.15f * shade), 
                GroundColor.G * (0.85f + 0.15f * shade), 
                GroundColor.B * (0.85f + 0.15f * shade), 
                GroundColor.A
            );
            polyColors.Add(groundShade);
        }

        // Bottom vertices
        float startX = -GroundWidth / 2.0f;
        polyPts.Add(new Vector2(startX + GroundWidth, GroundHeight));
        polyColors.Add(GroundColor * 0.8f);
        polyPts.Add(new Vector2(startX, GroundHeight));
        polyColors.Add(GroundColor * 0.8f);

        DrawPolygon(polyPts.ToArray(), polyColors.ToArray());
    }

    private void DrawAdaptiveGroundOccluder()
    {
        if (!UseAdaptiveOccluder)
        {
            DrawSimpleGroundOccluder();
            return;
        }

        // Create a more sophisticated occluder that considers grass blade positions
        List<Vector2> occluderPoints = new List<Vector2>();
        
        // Get grass blade base positions for adaptive depth calculation
        var grassBasePositions = _grassBlades
            .Where(b => b.BasePosition.X >= -GroundWidth / 2 && b.BasePosition.X <= GroundWidth / 2)
            .OrderBy(b => b.BasePosition.X)
            .ToList();

        // Build occluder with variable depth based on local grass density and size
        for (int i = 0; i < _groundContour.Count; i++)
        {
            Vector2 groundPoint = _groundContour[i];
            
            // Find nearby grass blades to determine appropriate cover depth
            float localMaxDepth = GroundBlendDepth;
            var nearbyBlades = grassBasePositions
                .Where(blade => Mathf.Abs(blade.BasePosition.X - groundPoint.X) < 20.0f)
                .ToList();
                
            if (nearbyBlades.Any())
            {
                float avgBaseWidth = nearbyBlades.Average(b => b.BaseWidth);
                float maxUndergroundDepth = nearbyBlades.Max(b => b.UndergroundDepth);
                localMaxDepth = Mathf.Max(GroundBlendDepth, Mathf.Min(maxUndergroundDepth * 0.8f, avgBaseWidth * 2.0f));
            }
            
            // Add some variation to the depth
            float depthVariation = _groundTextureNoise.GetNoise1D(groundPoint.X * 0.08f) * localMaxDepth * 0.3f;
            float actualDepth = localMaxDepth + depthVariation;
            
            occluderPoints.Add(new Vector2(groundPoint.X, groundPoint.Y + actualDepth));
        }

        // Add the top contour in reverse to close the polygon
        for (int i = _groundContour.Count - 1; i >= 0; i--)
        {
            occluderPoints.Add(_groundContour[i]);
        }

        // Create color gradient for the occluder band
        Color[] occluderColors = new Color[occluderPoints.Count];
        for (int i = 0; i < occluderColors.Length; i++)
        {
            if (i < _groundContour.Count)
            {
                // Bottom edge - darker
                occluderColors[i] = GroundColor * 0.9f;
            }
            else
            {
                // Top edge - lighter, matches ground surface
                occluderColors[i] = GroundColor * 0.98f;
            }
        }

        DrawPolygon(occluderPoints.ToArray(), occluderColors);
    }

    private void DrawSimpleGroundOccluder()
    {
        float coverDepth = Mathf.Min(20.0f, GroundHeight * 0.25f);
        List<Vector2> occluder = new List<Vector2>();

        // Simple uniform depth occluder
        foreach (var point in _groundContour)
        {
            occluder.Add(new Vector2(point.X, point.Y + coverDepth));
        }

        for (int i = _groundContour.Count - 1; i >= 0; i--)
        {
            occluder.Add(_groundContour[i]);
        }

        Color occluderColor = GroundColor * 0.95f;
        Color[] occluderColors = new Color[occluder.Count];
        for (int i = 0; i < occluderColors.Length; i++) 
            occluderColors[i] = occluderColor;

        DrawPolygon(occluder.ToArray(), occluderColors);
    }

    private void DrawGrassBlade(GrassBlade blade)
    {
    if (blade.CachedPoints == null || blade.CachedColors == null || blade.CachedPoints.Length < 3) return;

    // If no wind, draw cached arrays directly (fast)
    if (Mathf.IsEqualApprox(WindStrength, 0.0f))
        {
            DrawPolygon(blade.CachedPoints, blade.CachedColors);
            // Optional outline
            if (blade.Height > 60.0f)
            {
                Color outlineColor = blade.Color * 0.7f;
                for (int i = 0; i < blade.CachedPoints.Length; i++)
                {
                    int nextI = (i + 1) % blade.CachedPoints.Length;
                    DrawLine(blade.CachedPoints[i], blade.CachedPoints[nextI], outlineColor, 0.5f);
                }
            }
            return;
        }

        // Throttle redraw frequency for wind to reduce CPU draw cost
        // Wind X offsets change only X, so we can reuse cached colors
        double time = Time.GetUnixTimeFromSystem();
        float windOffset = Mathf.Sin((float)time * 2.0f + blade.BasePosition.X * 0.01f) * WindStrength;

        Vector2[] tempPoints = new Vector2[blade.CachedPoints.Length];
        for (int i = 0; i < blade.CachedPoints.Length; i++)
        {
            Vector2 p = blade.CachedPoints[i];
            float heightFactor = (blade.BasePosition.Y - p.Y) / blade.Height;
            if (heightFactor > 0)
                p.X += windOffset * heightFactor * heightFactor;
            tempPoints[i] = p;
        }

        DrawPolygon(tempPoints, blade.CachedColors);

        if (blade.Height > 60.0f)
        {
            Color outlineColor = blade.Color * 0.7f;
            for (int i = 0; i < tempPoints.Length; i++)
            {
                int nextI = (i + 1) % tempPoints.Length;
                DrawLine(tempPoints[i], tempPoints[nextI], outlineColor, 0.5f);
            }
        }
        
    // (outline already drawn above for tempPoints)
    }

    public override void _Process(double delta)
    {
        if (WindStrength > 0)
        {
            _redrawAccumulator += delta;
            double interval = 1.0 / Mathf.Max(1.0f, RedrawFPS);
            if (_redrawAccumulator >= interval)
            {
                QueueRedraw();
                _redrawAccumulator = 0.0;
            }
        }
    }

    public void RegenerateGrass()
    {
        GenerateGroundContour();
        GenerateGrassBlades();
    }

    public void SetWindStrength(float strength)
    {
        WindStrength = strength;
    }

    public void SetGrassDensity(int density)
    {
        GrassDensity = density;
        GenerateGrassBlades();
    }
}

public class GrassBlade
{
    public Vector2 BasePosition { get; set; }
    public List<Vector2> Points { get; set; } = new List<Vector2>();
    public float Height { get; set; }
    public Color Color { get; set; }
    public float LeanAngle { get; set; }
    public float BaseWidth { get; set; }
    public float UndergroundDepth { get; set; }
    // Cached arrays for drawing performance
    public Vector2[] CachedPoints { get; set; }
    public Color[] CachedColors { get; set; }
}