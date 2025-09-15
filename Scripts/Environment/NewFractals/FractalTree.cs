using Godot;

public partial class FractalTree : Node2D
{
    [Export] public int MaxDepth { get; set; } = 10;
    [Export] public float InitialLength { get; set; } = 120f;
    [Export] public float LengthReduction { get; set; } = 0.72f;
    [Export] public float AngleVariation { get; set; } = 15f;
    [Export] public float MinAngle { get; set; } = 20f;
    [Export] public float MaxAngle { get; set; } = 35f;
    [Export] public int MinBranches { get; set; } = 2;
    [Export] public int MaxBranches { get; set; } = 3;
    [Export] public float ThicknessReduction { get; set; } = 0.8f;
    [Export] public float InitialThickness { get; set; } = 8f;
    [Export] public Color TrunkColor { get; set; } = new Color(0.4f, 0.3f, 0.5f);
    [Export] public Color LeafColor { get; set; } = new Color(0.7f, 0.3f, 0.9f);
    
    // Export settings
    [ExportGroup("Export Settings")]
    [Export] public bool AutoSaveAfterGeneration { get; set; } = false;
    [Export] public int ExportWidth { get; set; } = 2048;
    [Export] public int ExportHeight { get; set; } = 2048;
    [Export] public string ExportFolderName { get; set; } = "fractal_exports";

    private RandomNumberGenerator rng = new RandomNumberGenerator();
    private Vector2[] linePoints;
    private Color[] lineColors;
    private float[] lineWidths;
    private int lineCount = 0;
    private int globalBranchCount = 0;
    private Vector2[] leafPositions;
    private Color[] leafColors;
    private float[] leafSizes;
    private int leafCount = 0;

    public override void _Ready()
    {
        rng.Randomize();
        
        // Pre-allocate arrays for better performance
        int maxBranches = EstimateMaxBranches(MaxDepth);
        linePoints = new Vector2[maxBranches * 2];
        lineColors = new Color[maxBranches];
        lineWidths = new float[maxBranches];
        
        int maxLeaves = maxBranches / 4;
        leafPositions = new Vector2[maxLeaves];
        leafColors = new Color[maxLeaves];
        leafSizes = new float[maxLeaves];
        
        GenerateTree();
    }

    private void ValidateBranchBounds()
    {
        if (MinBranches < 1) MinBranches = 1;
        if (MaxBranches < MinBranches) MaxBranches = MinBranches;
        MinBranches = Mathf.Clamp(MinBranches, 1, 6);
        MaxBranches = Mathf.Clamp(MaxBranches, MinBranches, 8);
    }

    private int EstimateMaxBranches(int depth)
    {
        if (depth <= 0) return 1;
        return (int)Mathf.Pow(2.5f, depth);
    }
    
    private void GenerateTree()
    {
        ValidateBranchBounds();

        lineCount = 0;
        leafCount = 0;
        
        if (MinBranches == MaxBranches)
            globalBranchCount = MinBranches;
        else
            globalBranchCount = rng.RandiRange(MinBranches, MaxBranches);
        
        Vector2 startPos = Vector2.Zero;
        Vector2 direction = Vector2.Up;
        
        GenerateBranch(startPos, direction, InitialLength, MaxDepth, InitialThickness);
        
        // Auto-save if enabled
        if (AutoSaveAfterGeneration)
        {
            CallDeferred(nameof(SaveTreeAsPNGSimple));
        }
    }

    public override void _Draw()
    {
        // Draw all lines in batch
        for (int i = 0; i < lineCount; i++)
        {
            int pointIndex = i * 2;
            DrawLine(linePoints[pointIndex], linePoints[pointIndex + 1], lineColors[i], lineWidths[i]);
        }
        
        // Draw all leaves in batch
        for (int i = 0; i < leafCount; i++)
        {
            DrawCircle(leafPositions[i], leafSizes[i], leafColors[i]);
        }
    }

    // Calculate the bounding box of the entire tree
    private Rect2 GetTreeBounds()
    {
        if (lineCount == 0) return new Rect2(0, 0, 100, 100);
        
        Vector2 min = linePoints[0];
        Vector2 max = linePoints[0];
        
        // Check all line points
        for (int i = 0; i < lineCount * 2; i++)
        {
            Vector2 point = linePoints[i];
            min.X = Mathf.Min(min.X, point.X);
            min.Y = Mathf.Min(min.Y, point.Y);
            max.X = Mathf.Max(max.X, point.X);
            max.Y = Mathf.Max(max.Y, point.Y);
        }
        
        // Check all leaf positions (with their radius)
        for (int i = 0; i < leafCount; i++)
        {
            Vector2 leafPos = leafPositions[i];
            float leafSize = leafSizes[i];
            min.X = Mathf.Min(min.X, leafPos.X - leafSize);
            min.Y = Mathf.Min(min.Y, leafPos.Y - leafSize);
            max.X = Mathf.Max(max.X, leafPos.X + leafSize);
            max.Y = Mathf.Max(max.Y, leafPos.Y + leafSize);
        }
        
        // Add some padding
        float padding = InitialThickness * 2;
        min -= Vector2.One * padding;
        max += Vector2.One * padding;
        
        return new Rect2(min, max - min);
    }

    // Remove the problematic async method - use SaveTreeAsPNGSimple() instead
    /*
    public async void SaveTreeAsPNG()
    {
        // This method has been removed due to complexity issues
        // Use SaveTreeAsPNGSimple() instead
    }
    */
    
    // Simpler method that renders to an ImageTexture at high resolution
    public void SaveTreeAsPNGSimple()
    {
        // Create export folder if it doesn't exist
        string exportPath = $"res://{ExportFolderName}/";
        if (!DirAccess.DirExistsAbsolute(exportPath))
        {
            DirAccess.MakeDirRecursiveAbsolute(exportPath);
        }
        
        // Create high-resolution image directly
        Image image = Image.Create(ExportWidth, ExportHeight, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0)); // Transparent background
        
        // Calculate tree bounds and scaling
        Rect2 treeBounds = GetTreeBounds();
        float scaleX = ExportWidth / treeBounds.Size.X;
        float scaleY = ExportHeight / treeBounds.Size.Y;
        float scale = Mathf.Min(scaleX, scaleY) * 0.9f;
        
        Vector2 offset = new Vector2(ExportWidth / 2f, ExportHeight / 2f) - treeBounds.GetCenter() * scale;
        
        // Draw lines directly to image
        for (int i = 0; i < lineCount; i++)
        {
            int pointIndex = i * 2;
            Vector2 start = linePoints[pointIndex] * scale + offset;
            Vector2 end = linePoints[pointIndex + 1] * scale + offset;
            
            DrawLineOnImage(image, start, end, lineColors[i], lineWidths[i] * scale);
        }
        
        // Draw leaves directly to image
        for (int i = 0; i < leafCount; i++)
        {
            Vector2 leafPos = leafPositions[i] * scale + offset;
            float leafSize = leafSizes[i] * scale;
            
            DrawCircleOnImage(image, leafPos, leafSize, leafColors[i]);
        }
        
        // Generate filename and save
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filename = $"fractal_tree_{timestamp}.png";
        string fullPath = $"{exportPath}{filename}";
        
        Error result = image.SavePng(fullPath);
        
        if (result == Error.Ok)
        {
            GD.Print($"Tree saved successfully to: {fullPath}");
        }
        else
        {
            GD.PrintErr($"Failed to save tree image: {result}");
        }
    }
    
    private void DrawLineOnImage(Image image, Vector2 start, Vector2 end, Color color, float width)
    {
        // Simple line drawing using Bresenham's algorithm with thickness
        int x0 = (int)start.X;
        int y0 = (int)start.Y;
        int x1 = (int)end.X;
        int y1 = (int)end.Y;
        
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        
        int thickness = Mathf.Max(1, (int)width);
        
        while (true)
        {
            // Draw thick point
            for (int i = -thickness/2; i <= thickness/2; i++)
            {
                for (int j = -thickness/2; j <= thickness/2; j++)
                {
                    int px = x0 + i;
                    int py = y0 + j;
                    if (px >= 0 && px < ExportWidth && py >= 0 && py < ExportHeight)
                    {
                        image.SetPixel(px, py, color);
                    }
                }
            }
            
            if (x0 == x1 && y0 == y1) break;
            
            int e2 = 2 * err;
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
    
    private void DrawCircleOnImage(Image image, Vector2 center, float radius, Color color)
    {
        int centerX = (int)center.X;
        int centerY = (int)center.Y;
        int r = (int)radius;
        
        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y <= r * r)
                {
                    int px = centerX + x;
                    int py = centerY + y;
                    if (px >= 0 && px < ExportWidth && py >= 0 && py < ExportHeight)
                    {
                        image.SetPixel(px, py, color);
                    }
                }
            }
        }
    }
    
    private void CaptureAndSave()
    {
        // This method is no longer needed - using direct image rendering instead
    }

    // Method to manually save tree (can be called from UI or input)
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == Key.S && keyEvent.CtrlPressed)
            {
                SaveTreeAsPNGSimple();
            }
        }
    }

    private void GenerateBranch(Vector2 startPos, Vector2 direction, float length, int depth, float thickness)
    {
        if (depth <= 0 || thickness < 0.5f || length < 5f || lineCount >= lineColors.Length) return;

        Vector2 endPos = startPos + direction * length;

        if (startPos.DistanceTo(endPos) < 1f) return;

        float depthRatio = (MaxDepth - depth) / (float)MaxDepth;
        Color branchColor = TrunkColor.Lerp(LeafColor, depthRatio);

        if (lineCount < lineColors.Length)
        {
            int pointIndex = lineCount * 2;
            if (pointIndex + 1 < linePoints.Length)
            {
                linePoints[pointIndex] = startPos;
                linePoints[pointIndex + 1] = endPos;
                lineColors[lineCount] = branchColor;
                lineWidths[lineCount] = thickness;
                lineCount++;
            }
        }

        if (depth <= 3)
        {
            GenerateLeaves(endPos, thickness, branchColor);
        }

        if (depth > 1 && lineCount < lineColors.Length - globalBranchCount)
        {
            int branchCount = globalBranchCount;

            if (depth > MaxDepth / 2 && MinBranches <= 2)
            {
                branchCount = Mathf.Clamp(2, MinBranches, MaxBranches);
            }
            
            float baseBranchPosition = rng.RandfRange(0.7f, 0.85f);
            float baseAngle = direction.Angle();
            float mainAngleSpread = Mathf.DegToRad(rng.RandfRange(MinAngle, MaxAngle));
            
            float baseLengthReduction = LengthReduction + rng.RandfRange(-0.05f, 0.05f);
            float newLength = length * baseLengthReduction;
            float newThickness = thickness * ThicknessReduction;
            
            if (newLength >= 5f && newThickness >= 0.5f)
            {
                if (branchCount == 1)
                {
                    float angle = baseAngle + rng.RandfRange(-Mathf.DegToRad(AngleVariation * 0.5f), Mathf.DegToRad(AngleVariation * 0.5f));
                    Vector2 newDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 branchStart = startPos.Lerp(endPos, baseBranchPosition);
                    
                    GenerateBranch(branchStart, newDirection, newLength, depth - 1, newThickness);
                }
                else if (branchCount == 2)
                {
                    float asymmetry = Mathf.DegToRad(rng.RandfRange(-AngleVariation * 0.3f, AngleVariation * 0.3f));

                    float leftAngle = baseAngle - mainAngleSpread + asymmetry;
                    float rightAngle = baseAngle + mainAngleSpread - asymmetry;

                    Vector2 leftDirection = new Vector2(Mathf.Cos(leftAngle), Mathf.Sin(leftAngle));
                    Vector2 rightDirection = new Vector2(Mathf.Cos(rightAngle), Mathf.Sin(rightAngle));

                    float leftPosOffset = rng.RandfRange(-0.06f, 0.04f);
                    float rightPosOffset = rng.RandfRange(-0.04f, 0.06f);

                    Vector2 leftStart = startPos.Lerp(endPos, Mathf.Clamp(baseBranchPosition + leftPosOffset, 0.4f, 0.95f));
                    Vector2 rightStart = startPos.Lerp(endPos, Mathf.Clamp(baseBranchPosition + rightPosOffset, 0.4f, 0.95f));

                    Vector2 perp = new Vector2(-direction.Y, direction.X);
                    leftStart += perp * rng.RandfRange(-0.6f, 0.6f) * (thickness * 0.2f);
                    rightStart += perp * rng.RandfRange(-0.6f, 0.6f) * (thickness * 0.2f);

                    GenerateBranch(leftStart, leftDirection, newLength, depth - 1, newThickness);
                    GenerateBranch(rightStart, rightDirection, newLength, depth - 1, newThickness);
                }
                else if (branchCount == 3)
                {
                    float centerAngle = baseAngle + rng.RandfRange(-Mathf.DegToRad(5f), Mathf.DegToRad(5f));
                    float leftAngle = baseAngle - mainAngleSpread;
                    float rightAngle = baseAngle + mainAngleSpread;

                    Vector2 centerDirection = new Vector2(Mathf.Cos(centerAngle), Mathf.Sin(centerAngle));
                    Vector2 leftDirection = new Vector2(Mathf.Cos(leftAngle), Mathf.Sin(leftAngle));
                    Vector2 rightDirection = new Vector2(Mathf.Cos(rightAngle), Mathf.Sin(rightAngle));

                    float centerLength = newLength;
                    float sideLength = newLength * 0.9f;
                    float sideThickness = newThickness * 0.9f;

                    if (sideLength >= 5f && sideThickness >= 0.5f)
                    {
                        float centerPos = Mathf.Clamp(baseBranchPosition + rng.RandfRange(-0.03f, 0.03f), 0.5f, 0.95f);
                        float leftPos = Mathf.Clamp(baseBranchPosition + rng.RandfRange(-0.08f, -0.02f), 0.4f, 0.9f);
                        float rightPos = Mathf.Clamp(baseBranchPosition + rng.RandfRange(0.02f, 0.08f), 0.45f, 0.95f);

                        Vector2 centerStart = startPos.Lerp(endPos, centerPos);
                        Vector2 leftStart = startPos.Lerp(endPos, leftPos);
                        Vector2 rightStart = startPos.Lerp(endPos, rightPos);

                        Vector2 perp = new Vector2(-direction.Y, direction.X);
                        centerStart += perp * rng.RandfRange(-0.4f, 0.4f) * (thickness * 0.18f);
                        leftStart += perp * rng.RandfRange(-0.5f, 0.5f) * (thickness * 0.18f);
                        rightStart += perp * rng.RandfRange(-0.5f, 0.5f) * (thickness * 0.18f);

                        GenerateBranch(centerStart, centerDirection, centerLength, depth - 1, newThickness);
                        GenerateBranch(leftStart, leftDirection, sideLength, depth - 1, sideThickness);
                        GenerateBranch(rightStart, rightDirection, sideLength, depth - 1, sideThickness);
                    }
                }
                else
                {
                    float totalAngleSpread = mainAngleSpread * 2f;
                    float angleStep = totalAngleSpread / (branchCount - 1);
                    
                    for (int i = 0; i < branchCount; i++)
                    {
                        float branchAngle = baseAngle - mainAngleSpread + (i * angleStep);
                        branchAngle += rng.RandfRange(-Mathf.DegToRad(AngleVariation * 0.2f), Mathf.DegToRad(AngleVariation * 0.2f));
                        
                        Vector2 branchDirection = new Vector2(Mathf.Cos(branchAngle), Mathf.Sin(branchAngle));
                        
                        float posOffset = rng.RandfRange(-0.05f, 0.05f);
                        Vector2 branchStart = startPos.Lerp(endPos, Mathf.Clamp(baseBranchPosition + posOffset, 0.4f, 0.95f));
                        
                        Vector2 perp = new Vector2(-direction.Y, direction.X);
                        branchStart += perp * rng.RandfRange(-0.5f, 0.5f) * (thickness * 0.15f);
                        
                        float branchLength = newLength;
                        float branchThickness = newThickness;
                        
                        if (i == 0 || i == branchCount - 1)
                        {
                            branchLength *= 0.9f;
                            branchThickness *= 0.9f;
                        }
                        
                        if (branchLength >= 5f && branchThickness >= 0.5f)
                        {
                            GenerateBranch(branchStart, branchDirection, branchLength, depth - 1, branchThickness);
                        }
                    }
                }
            }
        }
    }

    private void GenerateLeaves(Vector2 position, float size, Color color)
    {
        float leafSize = Mathf.Max(size * 0.5f, 2f);
        int leafCountToAdd = rng.RandiRange(3, 8);
        
        for (int i = 0; i < leafCountToAdd && leafCount < leafPositions.Length; i++)
        {
            float angle = (i / (float)leafCountToAdd) * Mathf.Pi * 2f;
            float distance = rng.RandfRange(leafSize * 0.5f, leafSize * 1.5f);
            Vector2 leafPos = position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            
            Color leafColor = LeafColor;
            leafColor.A = rng.RandfRange(0.6f, 1f);
            
            leafPositions[leafCount] = leafPos;
            leafColors[leafCount] = leafColor;
            leafSizes[leafCount] = rng.RandfRange(leafSize * 0.3f, leafSize * 0.8f);
            leafCount++;
        }
    }

    public void RegenerateTree()
    {
        rng.Randomize();
        GenerateTree();
        QueueRedraw();
    }
}