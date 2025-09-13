using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class DynamicTreeSpawner : Node2D
{
    [ExportGroup("Tree Distribution")]
    [Export] public int MinTreesPerChunk = 4;
    [Export] public int MaxTreesPerChunk = 8;
    [Export] public float MinTreeSpacing = 80.0f;
    [Export] public float MaxTreeSpacing = 150.0f;
    [Export] public float TreeSpacingVariation = 30.0f;
    [Export] public float ChunkWidth = 800.0f;
    [Export] public float TreeYPosition = 295.0f;
    [Export] public float TreeYVariation = 10.0f;
    
    [ExportGroup("Bush Distribution")]
    [Export] public float MinBushSpacing = 60.0f; // Bushes can be closer together
    [Export] public float MaxBushSpacing = 120.0f;
    [Export] public float BushToTreeMinDistance = 40.0f; // Minimum distance from bush to tree
    [Export] public float BushYOffset = 15.0f; // Bushes sit slightly higher
    
    [ExportGroup("Tree Prefabs")]
    [Export] public PackedScene[] TreePrefabs;
    [Export] public PackedScene BushPrefab;
    
    [ExportGroup("Tree Variety")]
    [Export] public float BackgroundTreeProbability = 0.3f;
    [Export] public float BushProbability = 0.15f;
    [Export] public Color BackgroundTreeColor = new Color(0.54f, 0.54f, 0.54f, 1.0f);
    [Export] public Color ForegroundTreeColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
    
    [ExportGroup("Layer Depth Control")]
    [Export] public int BackgroundZIndex = -3; // Far background trees
    [Export] public int MidBackgroundZIndex = -2; // Mid background trees
    [Export] public int BehindPlayerZIndex = -1; // Just behind player
    [Export] public int PlayerZIndex = 0; // Player layer (reference)
    [Export] public int ForegroundZIndex = 1; // In front of player
    [Export] public float DepthVariationIntensity = 0.4f; // How much Y affects depth perception
    [Export] public float LayerTransitionSmoothness = 0.7f; // Probability distribution smoothness
    
    [ExportGroup("Apple Settings")]
    [Export] public int TreesPerApple = 5;
    [Export] public PackedScene AppleBearingTreePrefab;
    
    [ExportGroup("Chunk Spacing")]
    [Export] public float ChunkGapMin = -50.0f;
    [Export] public float ChunkGapMax = 50.0f;
    
    // Performance optimization: Object pooling
    private readonly Queue<Node2D> _treePool = new Queue<Node2D>();
    private readonly Queue<Node2D> _bushPool = new Queue<Node2D>();
    private const int POOL_PREALLOC_SIZE = 20;
    
    // Cached data structures for performance
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private readonly Dictionary<int, CachedChunkData> _chunkCache = new Dictionary<int, CachedChunkData>();
    private readonly List<float> _tempPositionList = new List<float>(16); // Reusable list
    private readonly List<float> _tempCandidatesList = new List<float>(32); // Reusable list
    
    // Layer containers
    private Node2D _backLayerContainer;
    private Node2D _frontLayerContainer;
    private int _totalSpawnedTrees = 0;
    
    // Cached data structure for chunk information
    private struct CachedChunkData
    {
        public List<SpawnedObjectInfo> SpawnedObjects;
        public float ChunkOffset;
        
        public CachedChunkData(float offset)
        {
            SpawnedObjects = new List<SpawnedObjectInfo>();
            ChunkOffset = offset;
        }
    }
    
    // Information about spawned objects for collision detection
    private struct SpawnedObjectInfo
    {
        public float XPosition;
        public bool IsBush;
        public int ZLayer;
        
        public SpawnedObjectInfo(float x, bool bush, int z)
        {
            XPosition = x;
            IsBush = bush;
            ZLayer = z;
        }
    }
    
    public override void _Ready()
    {
        InitializeContainers();
        PreallocateObjectPool();
    }
    
    private void InitializeContainers()
    {
        _backLayerContainer = new Node2D
        {
            Name = "OnlyBehindPlayerLayerTrees",
            ZIndex = BehindPlayerZIndex
        };
        AddChild(_backLayerContainer);
        
        _frontLayerContainer = new Node2D
        {
            Name = "FrontOrBehindPlayerLayerTrees"
        };
        AddChild(_frontLayerContainer);
    }
    
    private void PreallocateObjectPool()
    {
        // Pre-create objects for pooling to reduce instantiation overhead
        for (int i = 0; i < POOL_PREALLOC_SIZE; i++)
        {
            if (TreePrefabs?.Length > 0)
            {
                var tree = TreePrefabs[0].Instantiate<Node2D>();
                tree.Visible = false;
                _treePool.Enqueue(tree);
            }
            
            if (BushPrefab != null)
            {
                var bush = BushPrefab.Instantiate<Node2D>();
                bush.Visible = false;
                _bushPool.Enqueue(bush);
            }
        }
    }
    
    public void InitializeChunk(int chunkIndex, ulong seed)
    {
        ClearTrees();
        _rng.Seed = seed;
        
        // Generate chunk offset
        float offset = _rng.RandfRange(ChunkGapMin, ChunkGapMax);
        
        // Initialize cached chunk data
        if (!_chunkCache.ContainsKey(chunkIndex))
        {
            _chunkCache[chunkIndex] = new CachedChunkData(offset);
        }
        
        // Generate and spawn objects
        var positions = GenerateOptimizedPositions(chunkIndex);
        SpawnObjectsAtPositions(positions, chunkIndex);
    }
    
    private List<(float x, bool isBush, int zLayer)> GenerateOptimizedPositions(int chunkIndex)
    {
        var results = new List<(float, bool, int)>();
        _tempPositionList.Clear();
        
        // Get boundary constraints from adjacent chunks
        var constraints = GetAdjacentConstraints(chunkIndex);
        
        int targetCount = _rng.RandiRange(MinTreesPerChunk, MaxTreesPerChunk);
        
        // Use optimized Poisson disk sampling
        var treePositions = GenerateOptimizedPoissonPositions(targetCount, constraints);
        
        foreach (float pos in treePositions)
        {
            bool isBush = _rng.Randf() < BushProbability;
            int zLayer = CalculateNaturalZLayer(pos);
            
            results.Add((pos, isBush, zLayer));
            
            // Cache this position
            _chunkCache[chunkIndex].SpawnedObjects.Add(new SpawnedObjectInfo(pos, isBush, zLayer));
        }
        
        return results;
    }
    
    private int CalculateNaturalZLayer(float xPosition)
    {
        // Create natural depth distribution based on position and randomness
        float normalizedX = xPosition / ChunkWidth; // 0 to 1
        float depthSeed = _rng.Randf();
        
        // Create a more natural distribution with multiple depth layers
        float depthFactor = (normalizedX * 0.3f + depthSeed * 0.7f + 
                           Mathf.Sin(normalizedX * Mathf.Pi) * DepthVariationIntensity);
        
        // Smooth probability distribution for layer assignment
        if (depthFactor < 0.15f * LayerTransitionSmoothness)
            return BackgroundZIndex;
        else if (depthFactor < 0.35f * LayerTransitionSmoothness)
            return MidBackgroundZIndex;
        else if (depthFactor < 0.65f)
            return BehindPlayerZIndex;
        else if (depthFactor < 0.85f)
            return PlayerZIndex;
        else
            return ForegroundZIndex;
    }
    
    private List<float> GetAdjacentConstraints(int chunkIndex)
    {
        var constraints = new List<float>();
        
        // Check left chunk
        if (_chunkCache.ContainsKey(chunkIndex - 1))
        {
            var leftChunk = _chunkCache[chunkIndex - 1];
            foreach (var obj in leftChunk.SpawnedObjects)
            {
                if (obj.XPosition > ChunkWidth - GetMaxSpacing(obj.IsBush) * 2)
                {
                    constraints.Add(obj.XPosition - (ChunkWidth + leftChunk.ChunkOffset));
                }
            }
        }
        
        // Check right chunk
        if (_chunkCache.ContainsKey(chunkIndex + 1))
        {
            var rightChunk = _chunkCache[chunkIndex + 1];
            foreach (var obj in rightChunk.SpawnedObjects)
            {
                if (obj.XPosition < GetMaxSpacing(obj.IsBush) * 2)
                {
                    constraints.Add(obj.XPosition + (ChunkWidth + rightChunk.ChunkOffset));
                }
            }
        }
        
        return constraints;
    }
    
    private float GetMinSpacing(bool isBush) => isBush ? MinBushSpacing : MinTreeSpacing;
    private float GetMaxSpacing(bool isBush) => isBush ? MaxBushSpacing : MaxTreeSpacing;
    
    private List<float> GenerateOptimizedPoissonPositions(int targetCount, List<float> constraints)
    {
        _tempPositionList.Clear();
        _tempCandidatesList.Clear();
        
        // Optimized grid-based approach for better performance
        float cellSize = MinTreeSpacing;
        int gridWidth = Mathf.CeilToInt(ChunkWidth / cellSize);
        bool[] occupiedCells = new bool[gridWidth];
        
        // Mark constrained areas
        foreach (float constraint in constraints)
        {
            int cellIndex = Mathf.FloorToInt(constraint / cellSize);
            if (cellIndex >= 0 && cellIndex < gridWidth)
            {
                occupiedCells[cellIndex] = true;
                // Mark adjacent cells too
                if (cellIndex > 0) occupiedCells[cellIndex - 1] = true;
                if (cellIndex < gridWidth - 1) occupiedCells[cellIndex + 1] = true;
            }
        }
        
        int attempts = 0;
        while (_tempPositionList.Count < targetCount && attempts < targetCount * 10)
        {
            attempts++;
            
            float candidateX = _rng.RandfRange(MinTreeSpacing, ChunkWidth - MinTreeSpacing);
            int cellIndex = Mathf.FloorToInt(candidateX / cellSize);
            
            if (cellIndex >= 0 && cellIndex < gridWidth && !occupiedCells[cellIndex])
            {
                if (IsPositionValidOptimized(candidateX, _tempPositionList, constraints))
                {
                    _tempPositionList.Add(candidateX);
                    occupiedCells[cellIndex] = true;
                    
                    // Mark adjacent cells
                    if (cellIndex > 0) occupiedCells[cellIndex - 1] = true;
                    if (cellIndex < gridWidth - 1) occupiedCells[cellIndex + 1] = true;
                }
            }
        }
        
        return new List<float>(_tempPositionList);
    }
    
    private bool IsPositionValidOptimized(float x, List<float> existingPositions, List<float> constraints)
    {
        // Optimized distance checking with early termination
        float minSpacing = MinTreeSpacing;
        
        for (int i = 0; i < existingPositions.Count; i++)
        {
            if (Mathf.Abs(x - existingPositions[i]) < minSpacing)
                return false;
        }
        
        for (int i = 0; i < constraints.Count; i++)
        {
            if (Mathf.Abs(x - constraints[i]) < minSpacing)
                return false;
        }
        
        return x >= minSpacing * 0.5f && x <= ChunkWidth - minSpacing * 0.5f;
    }
    
    private void SpawnObjectsAtPositions(List<(float x, bool isBush, int zLayer)> positions, int chunkIndex)
    {
        foreach (var (x, isBush, zLayer) in positions)
        {
            // Check if this should be an apple tree (only for trees, not bushes)
            bool shouldBeAppleTree = false;
            if (!isBush)
            {
                _totalSpawnedTrees++;
                shouldBeAppleTree = TreesPerApple > 0 && _totalSpawnedTrees % TreesPerApple == 0;
            }
            
            Node2D objectNode = GetPooledObject(isBush, shouldBeAppleTree);
            
            if (objectNode == null)
                continue;
            
            // Apple trees get special Z-layer treatment: always in front of other trees
            int finalZLayer = zLayer;
            if (shouldBeAppleTree)
            {
                // Apple trees should be in front of other trees but can be behind/front of player
                if (zLayer < PlayerZIndex)
                    finalZLayer = BehindPlayerZIndex; // Behind player but in front of other background trees
                else
                    finalZLayer = ForegroundZIndex; // In front of player and other trees
            }
            
            ConfigureSpawnedObject(objectNode, x, isBush, finalZLayer, shouldBeAppleTree);
        }
    }
    
    private Node2D GetPooledObject(bool isBush, bool shouldBeAppleTree = false)
    {
        Node2D objectNode = null;
        
        if (isBush && BushPrefab != null)
        {
            if (_bushPool.Count > 0)
            {
                objectNode = _bushPool.Dequeue();
                objectNode.Visible = true;
            }
            else
            {
                objectNode = BushPrefab.Instantiate<Node2D>();
            }
        }
        else if (shouldBeAppleTree && AppleBearingTreePrefab != null)
        {
            // Always use the special apple tree prefab when available
            objectNode = AppleBearingTreePrefab.Instantiate<Node2D>();
        }
        else if (TreePrefabs?.Length > 0)
        {
            if (_treePool.Count > 0)
            {
                objectNode = _treePool.Dequeue();
                objectNode.Visible = true;
            }
            else
            {
                int treeIndex = _rng.RandiRange(0, TreePrefabs.Length - 1);
                objectNode = TreePrefabs[treeIndex].Instantiate<Node2D>();
            }
        }
        
        return objectNode;
    }
    
    private void ConfigureSpawnedObject(Node2D objectNode, float x, bool isBush, int zLayer, bool isAppleTree = false)
    {
        // Position with appropriate Y offset
        float yOffset = isBush ? BushYOffset : 0f;
        float yVariation = _rng.RandfRange(-TreeYVariation, TreeYVariation);
        objectNode.Position = new Vector2(x, TreeYPosition + yOffset + yVariation);
        
        // Set Z-layer for natural depth
        objectNode.ZIndex = zLayer;
        
        // Determine which container to use and apply coloring
        bool isBackground = zLayer < PlayerZIndex;
        Node2D targetContainer = isBackground ? _backLayerContainer : _frontLayerContainer;
        
        targetContainer.AddChild(objectNode);
        
        // Apply appropriate coloring (apple trees keep their natural colors)
        if (isAppleTree)
        {
            // Apple trees keep their default/natural colors - don't override
        }
        else if (isBackground)
        {
            ApplyColor(objectNode, BackgroundTreeColor);
        }
        else
        {
            ApplyColor(objectNode, ForegroundTreeColor);
        }
        
        // Apply apple properties if this is an apple tree
        if (isAppleTree)
        {
            ApplyAppleLogic(objectNode);
        }
        
        // Apply object-specific configuration
        ConfigureObjectParameters(objectNode, isBush, isAppleTree);
        
        // Apply wind and randomization
        ApplyEnvironmentalEffects(objectNode);
    }
    
    private void ApplyColor(Node2D objectNode, Color color)
    {
        if (objectNode.HasMethod("SetTreeColor"))
        {
            objectNode.Call("SetTreeColor", color);
        }
        else if (objectNode is CanvasItem canvasItem)
        {
            canvasItem.Modulate = color;
        }
    }
    
    private void ApplyAppleLogic(Node2D treeNode)
    {
        // Set apple properties using multiple methods for compatibility
        if (treeNode.HasMethod("SetHasApples"))
        {
            treeNode.Call("SetHasApples", true);
        }
        
        // Try setting the hasApples property
        try
        {
            treeNode.Set("hasApples", true);
        }
        catch
        {
            // Ignore if property doesn't exist
        }
        
        // Try setting apple quantity if the tree supports it
        if (treeNode.HasMethod("SetAppleQuantity"))
        {
            int appleCount = _rng.RandiRange(3, 8); // More apples for special trees
            treeNode.Call("SetAppleQuantity", appleCount);
        }
        else
        {
            try
            {
                treeNode.Set("appleQuantity", _rng.RandiRange(3, 8));
            }
            catch
            {
                // Ignore if property doesn't exist
            }
        }
        
        // Try enabling apple visibility if the tree supports it
        try
        {
            treeNode.Set("showApples", true);
        }
        catch
        {
            // Ignore if property doesn't exist
        }
    }
    
    private void ConfigureObjectParameters(Node2D objectNode, bool isBush, bool isAppleTree = false)
    {
        if (isBush)
        {
            // Configure bush parameters
            if (objectNode.HasMethod("SetBranchCount"))
            {
                objectNode.Set("BranchCount", _rng.RandiRange(4, 6));
                objectNode.Set("InitialLength", _rng.RandfRange(50f, 80f));
            }
        }
        else
        {
            // Configure tree parameters
            if (objectNode.HasMethod("SetMaxDepth"))
            {
                objectNode.Set("maxDepth", _rng.RandiRange(5, 7));
                objectNode.Set("initialLength", _rng.RandfRange(180f, 220f));
                objectNode.Set("branchAngle", _rng.RandfRange(15f, 22f));
                objectNode.Set("angleVariation", _rng.RandfRange(5f, 10f));
                objectNode.Set("maxBranches", _rng.RandiRange(2, 3));
                objectNode.Set("leafDensity", _rng.RandiRange(1, 3));
            }
            
            // Apple trees get special configuration
            if (isAppleTree)
            {
                // Make apple trees slightly more prominent
                if (objectNode.HasMethod("SetMaxDepth"))
                {
                    objectNode.Set("maxDepth", _rng.RandiRange(6, 8)); // Slightly taller
                    objectNode.Set("initialLength", _rng.RandfRange(200f, 240f)); // Slightly bigger
                    objectNode.Set("leafDensity", _rng.RandiRange(2, 4)); // More leaves
                }
            }
        }
    }
    
    private void ApplyEnvironmentalEffects(Node2D objectNode)
    {
        // Randomize appearance
        if (objectNode.HasMethod("RandomizeTree"))
        {
            objectNode.Call("RandomizeTree", _rng.Randi());
        }
        
        // Apply wind variation
        if (objectNode.HasMethod("SetWindStrength"))
        {
            float windStrength = _rng.RandfRange(0.15f, 0.25f);
            objectNode.Call("SetWindStrength", windStrength);
        }
    }
    
    private void ClearTrees()
    {
        // Return objects to pool instead of destroying them
        ReturnChildrenToPool(_frontLayerContainer, false);
        ReturnChildrenToPool(_backLayerContainer, false);
    }
    
    private void ReturnChildrenToPool(Node2D container, bool isBushContainer)
    {
        foreach (Node child in container.GetChildren())
        {
            if (child is Node2D node2D)
            {
                container.RemoveChild(node2D);
                node2D.Visible = false;
                
                // Determine if it's a bush based on type or container
                bool isBush = (BushPrefab != null && 
                              node2D.GetType().Name == System.IO.Path.GetFileNameWithoutExtension(BushPrefab.ResourcePath));
                
                if (isBush && _bushPool.Count < POOL_PREALLOC_SIZE * 2)
                {
                    _bushPool.Enqueue(node2D);
                }
                else if (!isBush && _treePool.Count < POOL_PREALLOC_SIZE * 2)
                {
                    _treePool.Enqueue(node2D);
                }
                else
                {
                    child.QueueFree();
                }
            }
            else
            {
                child.QueueFree();
            }
        }
    }
    
    public void ResetChunk()
    {
        ClearTrees();
    }
    
    public void CleanupOldPositions(int currentChunkIndex, int keepRadius = 3)
    {
        var keysToRemove = _chunkCache.Keys.Where(index => 
            Mathf.Abs(index - currentChunkIndex) > keepRadius).ToList();
        
        foreach (int key in keysToRemove)
        {
            _chunkCache.Remove(key);
        }
    }
}