using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class DynamicTreeSpawner : Node2D
{
    [ExportGroup("Tree Distribution")]
    [Export] public int MinTreesPerChunk = 4;
    [Export] public int MaxTreesPerChunk = 8;
    [Export] public float MinTreeSpacing = 80.0f; // Minimum distance between trees
    [Export] public float MaxTreeSpacing = 150.0f; // Maximum distance between trees
    [Export] public float TreeSpacingVariation = 30.0f; // Random variation in spacing
    [Export] public float ChunkWidth = 800.0f;
    [Export] public float TreeYPosition = 295.0f; // Default Y position for trees
    [Export] public float TreeYVariation = 10.0f; // Slight Y variation for depth
    
    [ExportGroup("Tree Prefabs")]
    [Export] public PackedScene[] TreePrefabs; // Array of different tree types
    [Export] public PackedScene BushPrefab; // Bush prefab
    
    [ExportGroup("Tree Variety")]
    [Export] public float BackgroundTreeProbability = 0.3f; // Chance for background trees
    [Export] public float BushProbability = 0.15f; // Chance to spawn bush instead of tree
    [Export] public Color BackgroundTreeColor = new Color(0.54f, 0.54f, 0.54f, 1.0f);
    [Export] public Color ForegroundTreeColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
    
    private RandomNumberGenerator rng = new RandomNumberGenerator();
    private Dictionary<int, List<float>> chunkTreePositions = new Dictionary<int, List<float>>();
    private Node2D frontLayerContainer;
    private Node2D backLayerContainer;
    
    public override void _Ready()
    {
        // Create layer containers
        backLayerContainer = new Node2D();
        backLayerContainer.Name = "OnlyBehindPlayerLayerTrees";
        backLayerContainer.ZIndex = -1;
        AddChild(backLayerContainer);
        
        frontLayerContainer = new Node2D();
        frontLayerContainer.Name = "FrontOrBehindPlayerLayerTrees";
        AddChild(frontLayerContainer);
    }
    
    public void InitializeChunk(int chunkIndex, ulong seed)
    {
        // Clear existing trees
        ClearTrees();
        
        // Initialize RNG with seed for consistency
        rng.Seed = seed;
        
        // Generate tree positions for this chunk
        List<float> treePositions = GenerateTreePositions(chunkIndex);
        
        // Spawn trees at generated positions
        foreach (float xPos in treePositions)
        {
            SpawnTreeAtPosition(xPos, chunkIndex);
        }
    }
    
    private List<float> GenerateTreePositions(int chunkIndex)
    {
        List<float> positions = new List<float>();
        
        // Get adjacent chunk positions to avoid clustering at borders
        List<float> adjacentTreePositions = new List<float>();
        
        // Check left chunk
        if (chunkTreePositions.ContainsKey(chunkIndex - 1))
        {
            adjacentTreePositions.AddRange(
                chunkTreePositions[chunkIndex - 1]
                    .Where(x => x > ChunkWidth - MinTreeSpacing * 2)
                    .Select(x => x - ChunkWidth)
            );
        }
        
        // Check right chunk
        if (chunkTreePositions.ContainsKey(chunkIndex + 1))
        {
            adjacentTreePositions.AddRange(
                chunkTreePositions[chunkIndex + 1]
                    .Where(x => x < MinTreeSpacing * 2)
                    .Select(x => x + ChunkWidth)
            );
        }
        
        // Determine number of trees for this chunk
        int treeCount = rng.RandiRange(MinTreesPerChunk, MaxTreesPerChunk);
        
        // Use Poisson Disk Sampling for better distribution
        positions = GeneratePoissonDiskPositions(treeCount, adjacentTreePositions);
        
        // Store positions for adjacent chunk checking
        chunkTreePositions[chunkIndex] = new List<float>(positions);
        
        return positions;
    }
    
    private List<float> GeneratePoissonDiskPositions(int targetCount, List<float> avoidPositions)
    {
        List<float> positions = new List<float>();
        List<float> candidates = new List<float>();
        
        // Start with random position or from edges
        float startX = rng.RandfRange(MinTreeSpacing, ChunkWidth - MinTreeSpacing);
        
        // Ensure starting position is valid
        if (IsPositionValid(startX, positions, avoidPositions))
        {
            positions.Add(startX);
        }
        
        // Generate candidates around existing positions
        int attempts = 0;
        while (positions.Count < targetCount && attempts < 100)
        {
            attempts++;
            
            if (positions.Count == 0)
            {
                // If no valid start position, try random positions
                float randomX = rng.RandfRange(MinTreeSpacing, ChunkWidth - MinTreeSpacing);
                if (IsPositionValid(randomX, positions, avoidPositions))
                {
                    positions.Add(randomX);
                }
            }
            else
            {
                // Generate candidates around existing positions
                foreach (float pos in positions)
                {
                    for (int i = 0; i < 5; i++) // Try 5 candidates per position
                    {
                        float distance = rng.RandfRange(MinTreeSpacing, MaxTreeSpacing);
                        float variation = rng.RandfRange(-TreeSpacingVariation, TreeSpacingVariation);
                        float candidateX = pos + (rng.Randf() > 0.5f ? 1 : -1) * (distance + variation);
                        
                        // Keep within chunk bounds
                        candidateX = Mathf.Clamp(candidateX, 0, ChunkWidth);
                        
                        if (IsPositionValid(candidateX, positions, avoidPositions))
                        {
                            candidates.Add(candidateX);
                        }
                    }
                }
                
                // Add best candidate (furthest from existing trees)
                if (candidates.Count > 0)
                {
                    float bestCandidate = candidates[0];
                    float bestDistance = 0;
                    
                    foreach (float candidate in candidates)
                    {
                        float minDist = GetMinDistance(candidate, positions, avoidPositions);
                        if (minDist > bestDistance)
                        {
                            bestDistance = minDist;
                            bestCandidate = candidate;
                        }
                    }
                    
                    positions.Add(bestCandidate);
                    candidates.Clear();
                }
            }
        }
        
        return positions;
    }
    
    private bool IsPositionValid(float x, List<float> existingPositions, List<float> avoidPositions)
    {
        // Check distance from existing positions
        foreach (float pos in existingPositions)
        {
            if (Mathf.Abs(x - pos) < MinTreeSpacing)
                return false;
        }
        
        // Check distance from positions to avoid (adjacent chunks)
        foreach (float pos in avoidPositions)
        {
            if (Mathf.Abs(x - pos) < MinTreeSpacing)
                return false;
        }
        
        // Check chunk boundaries
        if (x < MinTreeSpacing * 0.5f || x > ChunkWidth - MinTreeSpacing * 0.5f)
            return false;
        
        return true;
    }
    
    private float GetMinDistance(float x, List<float> existingPositions, List<float> avoidPositions)
    {
        float minDist = float.MaxValue;
        
        foreach (float pos in existingPositions)
        {
            minDist = Mathf.Min(minDist, Mathf.Abs(x - pos));
        }
        
        foreach (float pos in avoidPositions)
        {
            minDist = Mathf.Min(minDist, Mathf.Abs(x - pos));
        }
        
        return minDist;
    }
    
    private void SpawnTreeAtPosition(float xPos, int chunkIndex)
    {
        Node2D treeNode = null;
        
        // Decide what to spawn
        float spawnRoll = rng.Randf();
        
        if (spawnRoll < BushProbability && BushPrefab != null)
        {
            // Spawn bush
            treeNode = BushPrefab.Instantiate<Node2D>();
        }
        else if (TreePrefabs != null && TreePrefabs.Length > 0)
        {
            // Spawn tree
            int treeIndex = rng.RandiRange(0, TreePrefabs.Length - 1);
            treeNode = TreePrefabs[treeIndex].Instantiate<Node2D>();
        }
        
        if (treeNode == null)
            return;
        
        // Determine if background or foreground
        bool isBackground = rng.Randf() < BackgroundTreeProbability;
        
        // Set position with slight Y variation
        float yVariation = rng.RandfRange(-TreeYVariation, TreeYVariation);
        treeNode.Position = new Vector2(xPos, TreeYPosition + yVariation);
        
        // Apply color and layer
        if (isBackground)
        {
            backLayerContainer.AddChild(treeNode);
            treeNode.ZIndex = -1;
            
            if (treeNode.HasMethod("SetTreeColor"))
            {
                treeNode.Call("SetTreeColor", BackgroundTreeColor);
            }
            else if (treeNode is CanvasItem canvasItem)
            {
                canvasItem.Modulate = BackgroundTreeColor;
            }
        }
        else
        {
            frontLayerContainer.AddChild(treeNode);
            
            // Randomly assign z-index for depth variety
            treeNode.ZIndex = rng.Randf() > 0.7f ? -1 : 0;
            
            if (treeNode.HasMethod("SetTreeColor"))
            {
                treeNode.Call("SetTreeColor", ForegroundTreeColor);
            }
        }
        
        // Initialize tree with variation
        if (treeNode.HasMethod("RandomizeTree"))
        {
            treeNode.Call("RandomizeTree", rng.Randi());
        }
        
        // Apply wind variation
        if (treeNode.HasMethod("SetWindStrength"))
        {
            float windStrength = rng.RandfRange(0.15f, 0.25f);
            treeNode.Call("SetWindStrength", windStrength);
        }
        
        // Set tree-specific parameters
        ConfigureTreeParameters(treeNode);
    }
    
    private void ConfigureTreeParameters(Node2D treeNode)
    {
        // Configure tree-specific parameters based on type
        string nodeName = treeNode.GetClass();
        
        // For OrganicFractalTree
        if (treeNode.HasMethod("SetMaxDepth"))
        {
            treeNode.Set("maxDepth", rng.RandiRange(5, 7));
            treeNode.Set("initialLength", rng.RandfRange(180f, 220f));
            treeNode.Set("branchAngle", rng.RandfRange(15f, 22f));
            treeNode.Set("angleVariation", rng.RandfRange(5f, 10f));
            treeNode.Set("maxBranches", rng.RandiRange(2, 3));
            treeNode.Set("leafDensity", rng.RandiRange(1, 3));
            
            // Randomly add apples to some trees
            if (rng.Randf() > 0.6f)
            {
                treeNode.Set("hasApples", true);
                treeNode.Set("appleQuantity", rng.RandiRange(3, 6));
            }
        }
        
        // For FractalBush types
        if (treeNode.HasMethod("SetBranchCount"))
        {
            treeNode.Set("BranchCount", rng.RandiRange(4, 6));
            treeNode.Set("InitialLength", rng.RandfRange(50f, 80f));
        }
    }
    
    private void ClearTrees()
    {
        // Clear all existing trees
        foreach (Node child in frontLayerContainer.GetChildren())
        {
            child.QueueFree();
        }
        
        foreach (Node child in backLayerContainer.GetChildren())
        {
            child.QueueFree();
        }
    }
    
    public void ResetChunk()
    {
        ClearTrees();
    }
    
    // Clean up stored positions for memory management
    public void CleanupOldPositions(int currentChunkIndex, int keepRadius = 3)
    {
        List<int> toRemove = new List<int>();
        
        foreach (int index in chunkTreePositions.Keys)
        {
            if (Mathf.Abs(index - currentChunkIndex) > keepRadius)
            {
                toRemove.Add(index);
            }
        }
        
        foreach (int index in toRemove)
        {
            chunkTreePositions.Remove(index);
        }
    }
}