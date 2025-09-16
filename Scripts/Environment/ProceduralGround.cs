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
    [Export] public float GrassSharpness { get; set; } = 0.8f;
    [Export] public int GrassSegments { get; set; } = 6;
    [Export] public Color GrassColorBase { get; set; } = new Color(0.1f, 0.2f, 0.05f);
    [Export] public Color GrassColorMid { get; set; } = new Color(0.15f, 0.4f, 0.1f);
    [Export] public Color GrassColorTip { get; set; } = new Color(0.2f, 0.6f, 0.15f);
    [Export] public int SeedValue { get; set; } = 0;
    [Export] public int SummitCount { get; set; } = 128;
    
    // Simplified parameters - removed ground-related and wind-related properties
    [Export] public float GroundBlendDepth { get; set; } = 8.0f; // How deep grass blends into ground

    private FastNoiseLite _noise;
    private List<GrassBlade> _grassBlades = new List<GrassBlade>();
    private List<Vector2> _groundContour = new List<Vector2>(); // Cache ground contour
    private Random _random;

    public override void _Ready()
    {
        SetupNoise();
        GenerateGroundContour();
        GenerateGrassBlades();
    }

    private void SetupNoise()
    {
        _noise = new FastNoiseLite();
        
        if (SeedValue > 0)
        {
            _noise.Seed = SeedValue;
            _random = new Random(SeedValue);
        }
        else
        {
            var seed = GD.Randi();
            _noise.Seed = (int)seed;
            _random = new Random((int)seed);
        }
        
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _noise.Frequency = 0.05f;
    }

    private void GenerateGroundContour()
    {
        _groundContour.Clear();
        
        int sampleCount;
        if (SummitCount > 0)
            sampleCount = Mathf.Clamp(SummitCount, 8, 1024);
        else
            sampleCount = Mathf.Clamp((int)(GroundWidth / 4.0f), 64, 512);
            
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

        // Create natural clusters with varying density
        int numClusters = Mathf.Max(8, GrassDensity / 25);
        int bladesPerCluster = GrassDensity / numClusters;
        
        for (int cluster = 0; cluster < numClusters; cluster++)
        {
            // Random cluster center
            float clusterCenterX = (float)_random.NextDouble() * GroundWidth - GroundWidth / 2;
            
            // Vary cluster density - some sparse, some dense
            float densityVariation = 0.3f + (float)_random.NextDouble() * 1.4f; // 0.3x to 1.7x density
            int actualBladesInCluster = (int)(bladesPerCluster * densityVariation);
            
            // Vary cluster spread - some tight, some loose
            float clusterSpread = 8.0f + (float)_random.NextDouble() * 35.0f; // 8-43 units
            
            for (int blade = 0; blade < actualBladesInCluster; blade++)
            {
                // Use gaussian-like distribution for more natural clustering
                float u1 = (float)_random.NextDouble();
                float u2 = (float)_random.NextDouble();
                float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.Pi * u2);
                
                float x = clusterCenterX + randStdNormal * clusterSpread * 0.4f;
                x = Mathf.Clamp(x, -GroundWidth / 2 + 5, GroundWidth / 2 - 5);
                
                float groundY = GetGroundHeightAtX(x);
                
                // Add small ground variation for more natural positioning
                float groundVariation = ((float)_random.NextDouble() - 0.5f) * 3.0f;
                Vector2 basePos = new Vector2(x, groundY + groundVariation);

                GrassBlade grassblade = CreateOrganicGrassBlade(basePos);
                _grassBlades.Add(grassblade);
            }
        }

        // Add some scattered individual blades between clusters
        int scatteredBlades = GrassDensity / 8;
        for (int i = 0; i < scatteredBlades; i++)
        {
            float x = (float)_random.NextDouble() * GroundWidth - GroundWidth / 2;
            float groundY = GetGroundHeightAtX(x);
            Vector2 basePos = new Vector2(x, groundY);

            GrassBlade blade = CreateOrganicGrassBlade(basePos);
            _grassBlades.Add(blade);
        }

        _grassBlades.Sort((a, b) => a.BasePosition.X.CompareTo(b.BasePosition.X));
        // Fill any noticeable gaps between blades to make grass contiguous
        FillGapsBetweenBlades();
        QueueRedraw();
    }

    // Insert filler blades between blades to ensure contiguous coverage
    private void FillGapsBetweenBlades()
    {
        if (_grassBlades.Count < 2) return;

        // Compute an average effective base width to determine expected spacing
        float avgBaseWidth = 0f;
        foreach (var b in _grassBlades) avgBaseWidth += b.BaseWidth;
        avgBaseWidth /= _grassBlades.Count;
    // Aim for at most 60% of avgBaseWidth gap between bases
    float targetGap = Mathf.Max(avgBaseWidth * 0.6f, 6.0f);

        var filled = new List<GrassBlade>();
        for (int i = 0; i < _grassBlades.Count - 1; i++)
        {
            var current = _grassBlades[i];
            var next = _grassBlades[i + 1];
            filled.Add(current);

            float gap = next.BasePosition.X - current.BasePosition.X;
            if (gap > targetGap)
            {
                // Insert enough fillers so resulting spacing <= targetGap
                int desiredSlots = Mathf.CeilToInt(gap / targetGap);
                int count = Math.Max(1, desiredSlots - 1);
                // Cap fillers to avoid excessive growth
                count = Math.Min(count, 12);

                for (int k = 1; k <= count; k++)
                {
                    // Distribute evenly
                    float t = (float)k / (count + 1);
                    float fx = Mathf.Lerp(current.BasePosition.X, next.BasePosition.X, t);

                    // Interpolate ground y and derive base pos
                    float fy = Mathf.Lerp(current.BasePosition.Y, next.BasePosition.Y, t);
                    Vector2 basePos = new Vector2(fx, fy);

                    // Filler blades should be slightly shorter and thinner for subtlety
                    GrassBlade filler = CreateOrganicGrassBlade(basePos);
                    filler.Height *= 0.7f;
                    filler.BaseWidth *= 0.7f;

                    // Recompute cached arrays for the filler
                    filler.CachedPoints = filler.Points.ToArray();
                    var colors = new Color[filler.CachedPoints.Length];
                    for (int c = 0; c < colors.Length; c++)
                    {
                        float hf = Mathf.Clamp((filler.BasePosition.Y - filler.CachedPoints[c].Y) / filler.Height, -0.2f, 1.0f);
                        // Make tip colors less saturated/green: mix toward a slightly desaturated tip
                        Color baseCol = filler.Color;
                        Color tipDesat = GrassColorTip.Lerp(new Color(0.45f, 0.4f, 0.35f), 0.6f);
                        float tipBlend = Mathf.Clamp((hf - 0.6f) * 2.5f, 0f, 1f);
                        Color finalCol = baseCol.Lerp(GrassColorMid, Mathf.Clamp(hf * 1.25f, 0f, 1f));
                        finalCol = finalCol.Lerp(tipDesat, tipBlend);
                        // Add subtle noise
                        finalCol *= (1.0f + _noise.GetNoise2D(filler.BasePosition.X * 0.1f, filler.CachedPoints[c].Y * 0.05f) * 0.08f);
                        colors[c] = finalCol;
                    }
                    filler.CachedColors = colors;

                    filled.Add(filler);
                }
            }
        }

        // Add last blade
        filled.Add(_grassBlades[_grassBlades.Count - 1]);

        // Replace the list and keep sorted
        _grassBlades = filled.OrderBy(b => b.BasePosition.X).ToList();
    }

    private float GetGroundHeightAtX(float x)
    {
        float noiseValue = _noise.GetNoise1D(x * 0.01f);
        float amplitude = GroundHeight * 0.5f;
        
        return -Mathf.Abs(noiseValue) * amplitude;
    }

    private GrassBlade CreateOrganicGrassBlade(Vector2 basePos)
    {
        // Much more varied height distribution - favor shorter grass with occasional tall blades
        float heightRoll = (float)_random.NextDouble();
        float height;
        
        if (heightRoll < 0.6f) // 60% shorter grass
        {
            height = Mathf.Lerp(GrassHeightMin, GrassHeightMin + (GrassHeightMax - GrassHeightMin) * 0.4f, (float)_random.NextDouble());
        }
        else if (heightRoll < 0.85f) // 25% medium grass
        {
            height = Mathf.Lerp(GrassHeightMin + (GrassHeightMax - GrassHeightMin) * 0.3f, GrassHeightMin + (GrassHeightMax - GrassHeightMin) * 0.7f, (float)_random.NextDouble());
        }
        else // 15% tall grass
        {
            height = Mathf.Lerp(GrassHeightMin + (GrassHeightMax - GrassHeightMin) * 0.6f, GrassHeightMax, (float)_random.NextDouble());
        }
        
        // Add additional height variation
        float heightVariation = 0.7f + (float)_random.NextDouble() * 0.6f;
        height *= heightVariation;
        
        // More varied width - thinner grass is more common
        float widthRoll = (float)_random.NextDouble();
        float baseWidth;
        if (widthRoll < 0.7f) // 70% thinner grass
        {
            baseWidth = Mathf.Lerp(GrassWidthMin, GrassWidthMin + (GrassWidthMax - GrassWidthMin) * 0.5f, (float)_random.NextDouble());
        }
        else // 30% thicker grass
        {
            baseWidth = Mathf.Lerp(GrassWidthMin + (GrassWidthMax - GrassWidthMin) * 0.4f, GrassWidthMax, (float)_random.NextDouble());
        }
        
        // More natural lean - most grass leans slightly, some significantly
        float leanIntensity = Mathf.Pow((float)_random.NextDouble(), 1.5f); // Bias toward smaller values
        float leanAngle = ((float)_random.NextDouble() - 0.5f) * GrassAngleVariance * leanIntensity;
        
        // Vary segments based on height - taller grass has more segments
        int baseSegments = height < 40 ? 4 : (height < 80 ? 6 : 8);
        int segments = baseSegments + _random.Next(-2, 3);
        segments = Math.Max(3, segments);
        
        List<Vector2> leftSide = new List<Vector2>();
        List<Vector2> rightSide = new List<Vector2>();
        
        // Add natural bend and twist
        float bendDirection = ((float)_random.NextDouble() - 0.5f) * 2.0f;
        float twistAmount = ((float)_random.NextDouble() - 0.5f) * 0.3f;
        
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float currentHeight = t * height;
            
            // More organic curvature with multiple sine waves
            float primaryCurve = Mathf.Sin(t * Mathf.Pi) * GrassCurvature * height * 0.1f;
            float secondaryCurve = Mathf.Sin(t * Mathf.Pi * 2.5f) * GrassCurvature * height * 0.03f;
            float curve = (primaryCurve + secondaryCurve) * bendDirection;
            
            // Natural lean that increases with height
            float lean = Mathf.Sin(t * Mathf.Pi * 0.5f) * Mathf.DegToRad(leanAngle) * height * 0.3f;
            
            // Add subtle twist
            float twist = twistAmount * t * height * 0.02f;
            
            Vector2 centerPoint = new Vector2(
                basePos.X + curve + lean + twist,
                basePos.Y - currentHeight
            );
            
            // More natural width tapering - not just linear
            float widthFactor = Mathf.Pow(1.0f - t, 0.8f + (float)_random.NextDouble() * 0.8f);
            
            // Sharp tip variation
            float sharpnessVariation = GrassSharpness + ((float)_random.NextDouble() - 0.5f) * 0.4f;
            sharpnessVariation = Mathf.Clamp(sharpnessVariation, 0.2f, 1.0f);
            
            if (t > sharpnessVariation)
            {
                float tipSharpness = 1.5f + (float)_random.NextDouble() * 1.0f;
                widthFactor *= Mathf.Pow((1.0f - t) / (1.0f - sharpnessVariation), tipSharpness);
            }
            
            float currentWidth = baseWidth * widthFactor;
            
            // Add noise variation that changes along the blade
            float noiseVariation = _noise.GetNoise2D(basePos.X * 0.15f, currentHeight * 0.08f) * 0.3f;
            float edgeNoise = _noise.GetNoise2D(basePos.X * 0.3f, currentHeight * 0.1f) * 0.15f;
            currentWidth *= (1.0f + noiseVariation);
            
            // Natural edge irregularity
            float leftEdgeVar = ((float)_random.NextDouble() - 0.5f) * currentWidth * 0.1f + edgeNoise;
            float rightEdgeVar = ((float)_random.NextDouble() - 0.5f) * currentWidth * 0.1f - edgeNoise;
            
            Vector2 leftPoint = new Vector2(centerPoint.X - currentWidth * 0.5f + leftEdgeVar, centerPoint.Y);
            Vector2 rightPoint = new Vector2(centerPoint.X + currentWidth * 0.5f + rightEdgeVar, centerPoint.Y);
            
            leftSide.Add(leftPoint);
            rightSide.Add(rightPoint);
        }
        
        List<Vector2> bladePoints = new List<Vector2>();
        
        // Variable underground depth based on blade size
        float undergroundDepth = Mathf.Max(baseWidth * 0.8f + height * 0.05f, 2.0f);
        undergroundDepth += (float)_random.NextDouble() * GroundBlendDepth * 0.2f; // Reduced variation
        
        // Ensure proper polygon construction - create a clean closed shape
        Vector2 leftBase = new Vector2(
            leftSide[0].X + ((float)_random.NextDouble() - 0.5f) * baseWidth * 0.2f, // Reduced from 0.4f
            leftSide[0].Y + undergroundDepth
        );
        Vector2 rightBase = new Vector2(
            rightSide[0].X + ((float)_random.NextDouble() - 0.5f) * baseWidth * 0.2f, // Reduced from 0.4f
            rightSide[0].Y + undergroundDepth
        );
        
        // Build polygon in proper winding order
        bladePoints.Add(leftBase);
        bladePoints.AddRange(leftSide);
        
        // Add right side in reverse order to maintain proper winding
        for (int i = rightSide.Count - 1; i >= 0; i--)
        {
            bladePoints.Add(rightSide[i]);
        }
        bladePoints.Add(rightBase);
        
        // Validate and clean the polygon
        bladePoints = CleanPolygonPoints(bladePoints);
        
        // More natural color variation
        float colorVariation = (float)_random.NextDouble();
        float hueShift = ((float)_random.NextDouble() - 0.5f) * 0.15f;
        float saturationShift = ((float)_random.NextDouble() - 0.5f) * 0.2f;
        
        Color bladeColor = GrassColorBase.Lerp(GrassColorMid, colorVariation);
        bladeColor.H = Mathf.Clamp(bladeColor.H + hueShift, 0.0f, 1.0f);
        bladeColor.S = Mathf.Clamp(bladeColor.S + saturationShift, 0.1f, 1.0f);
        
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
        // Precompute per-vertex colors based on height with more variation
        var cachedColors = new Color[blade.CachedPoints.Length];
        for (int i = 0; i < blade.CachedPoints.Length; i++)
        {
            float heightFactor = Mathf.Clamp((blade.BasePosition.Y - blade.CachedPoints[i].Y) / blade.Height, -0.2f, 1.0f);
            Color pointColor;
            
            // More natural color transitions
            if (heightFactor < 0)
            {
                pointColor = blade.Color * 0.8f; // Darker base
            }
            else if (heightFactor < 0.3f)
            {
                pointColor = blade.Color.Lerp(GrassColorMid, heightFactor * 3.33f);
            }
            else if (heightFactor < 0.8f)
            {
                pointColor = GrassColorMid.Lerp(GrassColorTip, (heightFactor - 0.3f) * 2.0f);
            }
            else
            {
                // Lighter tips with some variation
                Color tipColor = GrassColorTip.Lerp(new Color(0.3f, 0.8f, 0.25f), (heightFactor - 0.8f) * 5.0f);
                pointColor = tipColor;
            }
            
            // Add subtle color noise
            float colorNoise = _noise.GetNoise2D(blade.BasePosition.X * 0.1f, blade.CachedPoints[i].Y * 0.05f) * 0.1f;
            pointColor = pointColor * (1.0f + colorNoise);

            cachedColors[i] = pointColor;
        }
        blade.CachedColors = cachedColors;

        return blade;
    }

    private List<Vector2> CleanPolygonPoints(List<Vector2> points)
    {
        if (points.Count < 3) return points;
        
        List<Vector2> cleaned = new List<Vector2>();
        float minDistance = 0.5f; // Minimum distance between consecutive points
        
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 current = points[i];
            
            // Check for valid coordinates
            if (float.IsNaN(current.X) || float.IsNaN(current.Y) || 
                float.IsInfinity(current.X) || float.IsInfinity(current.Y))
                continue;
                
            // Check distance from previous point
            if (cleaned.Count == 0 || cleaned[cleaned.Count - 1].DistanceTo(current) >= minDistance)
            {
                cleaned.Add(current);
            }
        }
        
        // Ensure we have enough points for a valid polygon
        if (cleaned.Count < 3)
        {
            // Fallback: create a simple triangle
            Vector2 center = points[points.Count / 2];
            cleaned.Clear();
            cleaned.Add(new Vector2(center.X - 1, center.Y + 2));
            cleaned.Add(new Vector2(center.X, center.Y - 10));
            cleaned.Add(new Vector2(center.X + 1, center.Y + 2));
        }
        
        return cleaned;
    }

    public override void _Draw()
    {
        // Only draw grass blades - no ground base or occluder
        foreach (var blade in _grassBlades)
        {
            DrawGrassBlade(blade);
        }
    }

    private void DrawGrassBlade(GrassBlade blade)
    {
        if (blade.CachedPoints == null || blade.CachedColors == null || blade.CachedPoints.Length < 3) return;

        // Validate polygon before drawing
        if (!IsValidPolygon(blade.CachedPoints)) return;

        // Draw the grass blade polygon with cached colors (no wind effects)
        DrawPolygon(blade.CachedPoints, blade.CachedColors);
        
        // Optional outline for larger blades
        if (blade.Height > 60.0f)
        {
            Color outlineColor = blade.Color * 0.7f;
            for (int i = 0; i < blade.CachedPoints.Length; i++)
            {
                int nextI = (i + 1) % blade.CachedPoints.Length;
                DrawLine(blade.CachedPoints[i], blade.CachedPoints[nextI], outlineColor, 0.5f);
            }
        }
    }

    private bool IsValidPolygon(Vector2[] points)
    {
        if (points.Length < 3) return false;
        
        // Check for NaN or infinite values
        foreach (var point in points)
        {
            if (float.IsNaN(point.X) || float.IsNaN(point.Y) || 
                float.IsInfinity(point.X) || float.IsInfinity(point.Y))
                return false;
        }
        
        // Check for degenerate triangles (all points collinear or too close)
        float minDistance = 0.1f;
        for (int i = 0; i < points.Length; i++)
        {
            int nextI = (i + 1) % points.Length;
            if (points[i].DistanceTo(points[nextI]) < minDistance)
                return false;
        }
        
        // Check for basic self-intersection by ensuring polygon has reasonable area
        float area = CalculatePolygonArea(points);
        if (Mathf.Abs(area) <= 1.0f) return false; // Minimum area threshold

        // Ensure polygon is simple (no self-intersections)
        if (!IsSimplePolygon(points))
            return false;

        return true;
    }

    // Check polygon for self-intersections using O(n^2) segment tests.
    private bool IsSimplePolygon(Vector2[] pts)
    {
        int n = pts.Length;
        if (n < 3) return false;

        float eps = 1e-4f;

        for (int i = 0; i < n; i++)
        {
            Vector2 a1 = pts[i];
            Vector2 a2 = pts[(i + 1) % n];

            for (int j = i + 1; j < n; j++)
            {
                // Skip adjacent segments (they share a vertex)
                if (j == i || j == (i + 1) % n) continue;
                if ((i == 0) && (j == n - 1)) continue; // skip first and last adjacency

                Vector2 b1 = pts[j];
                Vector2 b2 = pts[(j + 1) % n];

                if (SegmentsIntersect(a1, a2, b1, b2, eps))
                    return false;
            }
        }

        return true;
    }

    private bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, float eps)
    {
        // Bounding box quick reject
        if (Mathf.Max(p1.X, p2.X) + eps < Mathf.Min(q1.X, q2.X)) return false;
        if (Mathf.Max(q1.X, q2.X) + eps < Mathf.Min(p1.X, p2.X)) return false;
        if (Mathf.Max(p1.Y, p2.Y) + eps < Mathf.Min(q1.Y, q2.Y)) return false;
        if (Mathf.Max(q1.Y, q2.Y) + eps < Mathf.Min(p1.Y, p2.Y)) return false;

        float o1 = Orientation(p1, p2, q1);
        float o2 = Orientation(p1, p2, q2);
        float o3 = Orientation(q1, q2, p1);
        float o4 = Orientation(q1, q2, p2);

        if (o1 * o2 < 0 && o3 * o4 < 0) return true;

        // Collinear/overlap checks
        if (Mathf.Abs(o1) < eps && OnSegment(p1, q1, p2)) return true;
        if (Mathf.Abs(o2) < eps && OnSegment(p1, q2, p2)) return true;
        if (Mathf.Abs(o3) < eps && OnSegment(q1, p1, q2)) return true;
        if (Mathf.Abs(o4) < eps && OnSegment(q1, p2, q2)) return true;

        return false;
    }

    private float Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        // Cross product (b - a) x (c - a)
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private bool OnSegment(Vector2 a, Vector2 b, Vector2 c)
    {
        return b.X <= Mathf.Max(a.X, c.X) + 1e-4f && b.X + 1e-4f >= Mathf.Min(a.X, c.X) &&
               b.Y <= Mathf.Max(a.Y, c.Y) + 1e-4f && b.Y + 1e-4f >= Mathf.Min(a.Y, c.Y);
    }

    private float CalculatePolygonArea(Vector2[] points)
    {
        float area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            int j = (i + 1) % points.Length;
            area += points[i].X * points[j].Y;
            area -= points[j].X * points[i].Y;
        }
        return area * 0.5f;
    }

    // Removed _Process method since there's no wind animation

    public void RegenerateGrass()
    {
        GenerateGroundContour();
        GenerateGrassBlades();
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