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

    private RandomNumberGenerator rng = new RandomNumberGenerator();
    private Vector2[] linePoints;
    private Color[] lineColors;
    private float[] lineWidths;
    private int lineCount = 0;
    private int globalBranchCount = 0; // chosen once per tree to avoid per-node variation
    private Vector2[] leafPositions;
    private Color[] leafColors;
    private float[] leafSizes;
    private int leafCount = 0;

    public override void _Ready()
    {
        rng.Randomize();
        
        // Pre-allocate arrays for better performance
        int maxBranches = EstimateMaxBranches(MaxDepth);
        linePoints = new Vector2[maxBranches * 2]; // Each line needs 2 points
        lineColors = new Color[maxBranches];
        lineWidths = new float[maxBranches];
        
        int maxLeaves = maxBranches / 4; // Estimate leaves
        leafPositions = new Vector2[maxLeaves];
        leafColors = new Color[maxLeaves];
        leafSizes = new float[maxLeaves];
        
        GenerateTree();
    }

    private void ValidateBranchBounds()
    {
        // Ensure sensible exported values
        if (MinBranches < 1) MinBranches = 1;
        if (MaxBranches < MinBranches) MaxBranches = MinBranches;
        // Also clamp to a small practical range to avoid explosion
        MinBranches = Mathf.Clamp(MinBranches, 1, 6);
        MaxBranches = Mathf.Clamp(MaxBranches, MinBranches, 8);
    }



    private int EstimateMaxBranches(int depth)
    {
        if (depth <= 0) return 1;
        return (int)Mathf.Pow(2.5f, depth); // Estimate based on average branch count
    }
    
    private void GenerateTree()
    {
        // Ensure branch bounds are valid before generating
        ValidateBranchBounds();

        lineCount = 0;
        leafCount = 0;
        // Choose a single branch count for the whole tree so min/max don't vary per node
        if (MinBranches == MaxBranches)
            globalBranchCount = MinBranches;
        else
            globalBranchCount = rng.RandiRange(MinBranches, MaxBranches);
        
    // Start from the Node2D's local origin so the node's position in the scene
    // controls where the tree is placed. Previously this was hard-coded which
    // prevented moving the tree by placing the node in the editor.
    Vector2 startPos = Vector2.Zero;
    Vector2 direction = Vector2.Up;
        
        GenerateBranch(startPos, direction, InitialLength, MaxDepth, InitialThickness);
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

   private void GenerateBranch(Vector2 startPos, Vector2 direction, float length, int depth, float thickness)
{
    // More strict validation to prevent tiny/invalid branches
    if (depth <= 0 || thickness < 0.5f || length < 5f || lineCount >= lineColors.Length) return;

    Vector2 endPos = startPos + direction * length;

    // Validate that we have a meaningful branch (not zero length)
    if (startPos.DistanceTo(endPos) < 1f) return;

    // Color interpolation based on depth
    float depthRatio = (MaxDepth - depth) / (float)MaxDepth;
    Color branchColor = TrunkColor.Lerp(LeafColor, depthRatio);

    // Store the line data - ensure we have space
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

    // Generate leaves at the end branches
    if (depth <= 3)
    {
        GenerateLeaves(endPos, thickness, branchColor);
    }

    // Generate child branches with balanced symmetry
    if (depth > 1 && lineCount < lineColors.Length - globalBranchCount) // Reserve space for child branches
    {
        // Determine branch count using the globalBranchCount (consistent across the tree)
        int branchCount = globalBranchCount;

        // Prefer fewer branches near the trunk for symmetry if MinBranches<=2
        if (depth > MaxDepth / 2 && MinBranches <= 2)
        {
            branchCount = Mathf.Clamp(2, MinBranches, MaxBranches);
        }
        
        // Base branch position (fraction along the parent branch)
        float baseBranchPosition = rng.RandfRange(0.7f, 0.85f);

        float baseAngle = direction.Angle();
        float mainAngleSpread = Mathf.DegToRad(rng.RandfRange(MinAngle, MaxAngle));
        
        // Calculate new length and thickness ONCE to ensure consistency
        float baseLengthReduction = LengthReduction + rng.RandfRange(-0.05f, 0.05f);
        float newLength = length * baseLengthReduction;
        float newThickness = thickness * ThicknessReduction;
        
        // Only generate branches if they will be meaningful
        if (newLength >= 5f && newThickness >= 0.5f)
        {
            if (branchCount == 1)
            {
                // Single branch - straight continuation with slight variation
                float angle = baseAngle + rng.RandfRange(-Mathf.DegToRad(AngleVariation * 0.5f), Mathf.DegToRad(AngleVariation * 0.5f));
                Vector2 newDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 branchStart = startPos.Lerp(endPos, baseBranchPosition);
                
                GenerateBranch(branchStart, newDirection, newLength, depth - 1, newThickness);
            }
            else if (branchCount == 2)
            {
                // Two-branch fork: compute slightly different start points for each child
                float asymmetry = Mathf.DegToRad(rng.RandfRange(-AngleVariation * 0.3f, AngleVariation * 0.3f));

                float leftAngle = baseAngle - mainAngleSpread + asymmetry;
                float rightAngle = baseAngle + mainAngleSpread - asymmetry;

                Vector2 leftDirection = new Vector2(Mathf.Cos(leftAngle), Mathf.Sin(leftAngle));
                Vector2 rightDirection = new Vector2(Mathf.Cos(rightAngle), Mathf.Sin(rightAngle));

                // Slightly vary the branch start positions for each child so they don't all originate from the exact same point
                float leftPosOffset = rng.RandfRange(-0.06f, 0.04f);
                float rightPosOffset = rng.RandfRange(-0.04f, 0.06f);

                Vector2 leftStart = startPos.Lerp(endPos, Mathf.Clamp(baseBranchPosition + leftPosOffset, 0.4f, 0.95f));
                Vector2 rightStart = startPos.Lerp(endPos, Mathf.Clamp(baseBranchPosition + rightPosOffset, 0.4f, 0.95f));

                // Add a tiny perpendicular offset scaled by thickness so joins look less mechanical
                Vector2 perp = new Vector2(-direction.Y, direction.X);
                leftStart += perp * rng.RandfRange(-0.6f, 0.6f) * (thickness * 0.2f);
                rightStart += perp * rng.RandfRange(-0.6f, 0.6f) * (thickness * 0.2f);

                // Generate left and right branches
                GenerateBranch(leftStart, leftDirection, newLength, depth - 1, newThickness);
                GenerateBranch(rightStart, rightDirection, newLength, depth - 1, newThickness);
            }
            else if (branchCount == 3)
            {
                // Three-branch fork: center and two sides
                float centerAngle = baseAngle + rng.RandfRange(-Mathf.DegToRad(5f), Mathf.DegToRad(5f));
                float leftAngle = baseAngle - mainAngleSpread;
                float rightAngle = baseAngle + mainAngleSpread;

                Vector2 centerDirection = new Vector2(Mathf.Cos(centerAngle), Mathf.Sin(centerAngle));
                Vector2 leftDirection = new Vector2(Mathf.Cos(leftAngle), Mathf.Sin(leftAngle));
                Vector2 rightDirection = new Vector2(Mathf.Cos(rightAngle), Mathf.Sin(rightAngle));

                float centerLength = newLength;
                float sideLength = newLength * 0.9f; // Side branches slightly shorter
                float sideThickness = newThickness * 0.9f;

                // Only generate if side branches are still meaningful
                if (sideLength >= 5f && sideThickness >= 0.5f)
                {
                    // Spread start positions a bit: center slightly closer to tip, sides slightly lower
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

                    // Generate center and side branches
                    GenerateBranch(centerStart, centerDirection, centerLength, depth - 1, newThickness);
                    GenerateBranch(leftStart, leftDirection, sideLength, depth - 1, sideThickness);
                    GenerateBranch(rightStart, rightDirection, sideLength, depth - 1, sideThickness);
                }
            }
            else
            {
                // General case for 4+ branches - distribute them in a circle
                float totalAngleSpread = mainAngleSpread * 2f; // Total spread angle
                float angleStep = totalAngleSpread / (branchCount - 1); // Angle between branches
                
                for (int i = 0; i < branchCount; i++)
                {
                    // Calculate angle for this branch
                    float branchAngle = baseAngle - mainAngleSpread + (i * angleStep);
                    
                    // Add some randomness
                    branchAngle += rng.RandfRange(-Mathf.DegToRad(AngleVariation * 0.2f), Mathf.DegToRad(AngleVariation * 0.2f));
                    
                    Vector2 branchDirection = new Vector2(Mathf.Cos(branchAngle), Mathf.Sin(branchAngle));
                    
                    // Vary the branch start positions slightly
                    float posOffset = rng.RandfRange(-0.05f, 0.05f);
                    Vector2 branchStart = startPos.Lerp(endPos, Mathf.Clamp(baseBranchPosition + posOffset, 0.4f, 0.95f));
                    
                    // Add perpendicular offset for more natural look
                    Vector2 perp = new Vector2(-direction.Y, direction.X);
                    branchStart += perp * rng.RandfRange(-0.5f, 0.5f) * (thickness * 0.15f);
                    
                    // Slightly vary length and thickness for outer branches
                    float branchLength = newLength;
                    float branchThickness = newThickness;
                    
                    // Make outer branches slightly smaller
                    if (i == 0 || i == branchCount - 1)
                    {
                        branchLength *= 0.9f;
                        branchThickness *= 0.9f;
                    }
                    
                    // Generate the branch
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
        // Generate small circular "leaves" or "flowers"
        float leafSize = Mathf.Max(size * 0.5f, 2f);
        int leafCountToAdd = rng.RandiRange(3, 8);
        
        for (int i = 0; i < leafCountToAdd && leafCount < leafPositions.Length; i++)
        {
            float angle = (i / (float)leafCountToAdd) * Mathf.Pi * 2f;
            float distance = rng.RandfRange(leafSize * 0.5f, leafSize * 1.5f);
            Vector2 leafPos = position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            
            // Create a more vibrant leaf color
            Color leafColor = LeafColor;
            leafColor.A = rng.RandfRange(0.6f, 1f);
            
            leafPositions[leafCount] = leafPos;
            leafColors[leafCount] = leafColor;
            leafSizes[leafCount] = rng.RandfRange(leafSize * 0.3f, leafSize * 0.8f);
            leafCount++;
        }
    }

    // Public methods to regenerate the tree
    public void RegenerateTree()
    {
        rng.Randomize();
        GenerateTree();
        QueueRedraw();
    }
}