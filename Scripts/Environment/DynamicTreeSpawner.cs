using Godot;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
    [Export] public float MinBushSpacing = 60.0f;
    [Export] public float MaxBushSpacing = 120.0f;
    [Export] public float BushToTreeMinDistance = 40.0f;
    [Export] public float BushYOffset = 15.0f;
    
    [ExportGroup("Tree Prefabs")]
    [Export] public PackedScene[] TreePrefabs;
    [Export] public PackedScene BushPrefab;
    
    [ExportGroup("Tree Variety")]
    [Export] public float BackgroundTreeProbability = 0.3f;
    [Export] public float BushProbability = 0.15f;
    [Export] public Color BackgroundTreeColor = new Color(0.54f, 0.54f, 0.54f, 1.0f);
    [Export] public Color ForegroundTreeColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
    
    [ExportGroup("Layer Depth Control")]
    [Export] public int BackgroundZIndex = -3;
    [Export] public int MidBackgroundZIndex = -2;
    [Export] public int BehindPlayerZIndex = -1;
    [Export] public int PlayerZIndex = 0;
    [Export] public int ForegroundZIndex = 1;
    [Export] public float DepthVariationIntensity = 0.4f;
    [Export] public float LayerTransitionSmoothness = 0.7f;
    
    [ExportGroup("Apple Settings")]
    [Export] public int TreesPerApple = 5;
    [Export] public PackedScene AppleBearingTreePrefab;
    
    [ExportGroup("Natural Chunk Spacing")]
    [Export] public float ChunkOverlapMin = -80.0f;
    [Export] public float ChunkOverlapMax = 120.0f;
    [Export] public float ChunkTransitionSmoothness = 0.8f;
    [Export] public bool UseNaturalSpacing = true;
    
    // Robust object pools with validation
    private readonly Queue<PooledObject> _treePool = new Queue<PooledObject>(32);
    private readonly Queue<PooledObject> _bushPool = new Queue<PooledObject>(16);
    private readonly Queue<PooledObject> _appleTreePool = new Queue<PooledObject>(8);
    
    // Pre-allocated collections to avoid GC
    private readonly float[] _positionBuffer = new float[32];
    private readonly float[] _constraintBuffer = new float[16];
    private readonly bool[] _gridBuffer = new bool[64]; // Reusable grid
    private readonly (float x, bool isBush, int z, bool isApple)[] _spawnBuffer = new (float, bool, int, bool)[32];
    
    // Cache-friendly data structures
    private readonly Dictionary<int, ChunkData> _chunks = new Dictionary<int, ChunkData>(16);
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    
    // Cached components and frequently accessed data
    private Node2D _backContainer;
    private Node2D _frontContainer;
    private int _treeCount;
    private int _treePrefabCount;
    private bool _hasBushPrefab;
    private bool _hasApplePrefab;
    
    // Precomputed values for performance
    private float _invChunkWidth;
    private float _minSpacingSquared;
    private float _layerSmoothnessRecip;
    private float _depthIntensityPi;
    
    // Safe object wrapper for pooling
    private class PooledObject
    {
        public Node2D Object { get; private set; }
        public bool IsValid { get; private set; }
        public int UseCount { get; private set; }
        
        public PooledObject(Node2D obj)
        {
            Object = obj;
            IsValid = true;
            UseCount = 0;
        }
        
        public bool TryUse()
        {
            if (!IsValid || Object == null || Object.IsQueuedForDeletion())
            {
                IsValid = false;
                return false;
            }
            
            try
            {
                // Test object validity with a safe operation
                var _ = Object.Position;
                UseCount++;
                return true;
            }
            catch
            {
                IsValid = false;
                return false;
            }
        }
        
        public void ReturnToPool()
        {
            if (IsValid && Object != null && !Object.IsQueuedForDeletion())
            {
                try
                {
                    Object.Visible = false;
                    if (Object.GetParent() != null)
                        Object.GetParent().RemoveChild(Object);
                }
                catch
                {
                    IsValid = false;
                }
            }
        }
        
        public void Dispose()
        {
            if (Object != null && !Object.IsQueuedForDeletion())
            {
                Object.QueueFree();
            }
            IsValid = false;
            Object = null;
        }
    }
    
    // Compact chunk data structure
    private struct ChunkData
    {
        public float[] Positions;
        public byte[] Types; // 0=tree, 1=bush, 2=apple
        public float Offset;
        public int Count;
        public float NaturalTransition; // For smoother chunk blending
        
        public ChunkData(int capacity)
        {
            Positions = new float[capacity];
            Types = new byte[capacity];
            Offset = 0f;
            Count = 0;
            NaturalTransition = 0f;
        }
    }
    
    public override void _Ready()
    {
        InitializeOptimized();
    }
    
    private void InitializeOptimized()
    {
        // Cache frequently used values
        _invChunkWidth = 1f / ChunkWidth;
        _minSpacingSquared = MinTreeSpacing * MinTreeSpacing;
        _layerSmoothnessRecip = 1f / LayerTransitionSmoothness;
        _depthIntensityPi = DepthVariationIntensity * Mathf.Pi;
        _treePrefabCount = TreePrefabs?.Length ?? 0;
        _hasBushPrefab = BushPrefab != null;
        _hasApplePrefab = AppleBearingTreePrefab != null;
        
        // Initialize containers
        _backContainer = new Node2D { Name = "BackTrees", ZIndex = BehindPlayerZIndex };
        _frontContainer = new Node2D { Name = "FrontTrees" };
        AddChild(_backContainer);
        AddChild(_frontContainer);
        
        // Warm up object pools safely
        WarmupPoolsSafely();
    }
    
    private void WarmupPoolsSafely()
    {
        // Pre-instantiate common objects with validation
        if (_treePrefabCount > 0)
        {
            for (int i = 0; i < 16; i++)
            {
                try
                {
                    var tree = TreePrefabs[0].Instantiate<Node2D>();
                    var pooledObj = new PooledObject(tree);
                    if (pooledObj.TryUse())
                    {
                        pooledObj.ReturnToPool();
                        _treePool.Enqueue(pooledObj);
                    }
                    else
                    {
                        tree?.QueueFree();
                    }
                }
                catch (System.Exception ex)
                {
                    GD.PrintErr($"Failed to warm up tree pool: {ex.Message}");
                    break;
                }
            }
        }
        
        if (_hasBushPrefab)
        {
            for (int i = 0; i < 8; i++)
            {
                try
                {
                    var bush = BushPrefab.Instantiate<Node2D>();
                    var pooledObj = new PooledObject(bush);
                    if (pooledObj.TryUse())
                    {
                        pooledObj.ReturnToPool();
                        _bushPool.Enqueue(pooledObj);
                    }
                    else
                    {
                        bush?.QueueFree();
                    }
                }
                catch (System.Exception ex)
                {
                    GD.PrintErr($"Failed to warm up bush pool: {ex.Message}");
                    break;
                }
            }
        }
        
        if (_hasApplePrefab)
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var appleTree = AppleBearingTreePrefab.Instantiate<Node2D>();
                    var pooledObj = new PooledObject(appleTree);
                    if (pooledObj.TryUse())
                    {
                        pooledObj.ReturnToPool();
                        _appleTreePool.Enqueue(pooledObj);
                    }
                    else
                    {
                        appleTree?.QueueFree();
                    }
                }
                catch (System.Exception ex)
                {
                    GD.PrintErr($"Failed to warm up apple tree pool: {ex.Message}");
                    break;
                }
            }
        }
    }
    
    public void InitializeChunk(int chunkIndex, ulong seed)
    {
        ClearTrees();
        _rng.Seed = seed;
        
        // Get or create chunk data
        if (!_chunks.TryGetValue(chunkIndex, out var chunk))
        {
            chunk = new ChunkData(MaxTreesPerChunk + 4);
            _chunks[chunkIndex] = chunk;
        }
        
        // Calculate natural chunk transitions
        if (UseNaturalSpacing)
        {
            chunk.Offset = CalculateNaturalChunkOffset(chunkIndex);
            chunk.NaturalTransition = CalculateChunkTransition(chunkIndex);
        }
        else
        {
            chunk.Offset = _rng.RandfRange(ChunkOverlapMin, ChunkOverlapMax);
            chunk.NaturalTransition = 0f;
        }
        
        // Generate positions using optimized algorithm
        int spawnCount = GeneratePositionsOptimized(chunkIndex, chunk);
        
        // Spawn objects in batch
        SpawnObjectsBatch(spawnCount, chunkIndex);
    }
    
    private float CalculateNaturalChunkOffset(int chunkIndex)
    {
        // Create natural variation based on chunk position using Perlin-like noise
        float baseNoise = Mathf.Sin(chunkIndex * 0.7f) * Mathf.Cos(chunkIndex * 0.3f);
        float detailNoise = Mathf.Sin(chunkIndex * 2.1f) * 0.3f;
        float combinedNoise = (baseNoise + detailNoise) * 0.5f;
        
        // Map noise to overlap range with bias toward slight overlap for natural look
        float naturalBias = -20.0f; // Slight overlap preference
        return Mathf.Lerp(ChunkOverlapMin, ChunkOverlapMax, (combinedNoise + 1f) * 0.5f) + naturalBias;
    }
    
    private float CalculateChunkTransition(int chunkIndex)
    {
        // Smooth transition factor for blending chunk boundaries
        return Mathf.Sin(chunkIndex * 0.5f + Mathf.Pi * 0.25f) * ChunkTransitionSmoothness;
    }
    
    private int GeneratePositionsOptimized(int chunkIndex, ChunkData chunk)
    {
        // Reset buffers
        int constraintCount = GetConstraintsOptimized(chunkIndex);
        int targetCount = _rng.RandiRange(MinTreesPerChunk, MaxTreesPerChunk);
        
        // Use spatial hashing for ultra-fast collision detection
        var gridSize = (int)(ChunkWidth / MinTreeSpacing) + 1;
        if (gridSize > _gridBuffer.Length)
            gridSize = _gridBuffer.Length;
        
        System.Array.Clear(_gridBuffer, 0, gridSize);
        
        // Mark constraint cells with natural transition zones
        for (int i = 0; i < constraintCount; i++)
        {
            int cell = FastFloatToInt(_constraintBuffer[i] / MinTreeSpacing);
            if (cell >= 0 && cell < gridSize)
            {
                _gridBuffer[cell] = true;
                // Vary exclusion zone based on natural transition
                float transitionFactor = 1f + chunk.NaturalTransition * 0.3f;
                int exclusionRadius = (int)(transitionFactor + 0.5f);
                
                for (int offset = -exclusionRadius; offset <= exclusionRadius; offset++)
                {
                    int targetCell = cell + offset;
                    if (targetCell >= 0 && targetCell < gridSize)
                        _gridBuffer[targetCell] = true;
                }
            }
        }
        
        // Generate positions with optimized collision detection
        int posCount = 0;
        int attempts = 0;
        int maxAttempts = targetCount * 8;
        
        while (posCount < targetCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Add natural spacing variation
            float spacingVariation = UseNaturalSpacing ? 
                _rng.RandfRange(-TreeSpacingVariation, TreeSpacingVariation) * chunk.NaturalTransition : 0f;
            float effectiveMinSpacing = MinTreeSpacing + spacingVariation;
            float effectiveMaxSpacing = ChunkWidth - effectiveMinSpacing;
            
            float x = _rng.RandfRange(effectiveMinSpacing, effectiveMaxSpacing);
            int cell = FastFloatToInt(x / MinTreeSpacing);
            
            if (cell >= 0 && cell < gridSize && !_gridBuffer[cell] && 
                IsPositionValidFast(x, posCount))
            {
                _positionBuffer[posCount] = x;
                bool isBush = _rng.Randf() < BushProbability;
                int zLayer = CalculateZLayerFast(x);
                bool isApple = false;
                
                if (!isBush && TreesPerApple > 0)
                {
                    _treeCount++;
                    isApple = _treeCount % TreesPerApple == 0;
                    if (isApple)
                    {
                        GD.Print($"Apple tree #{_treeCount} at {x}");
                        zLayer = zLayer < PlayerZIndex ? BehindPlayerZIndex : ForegroundZIndex;
                    }
                }
                
                _spawnBuffer[posCount] = (x, isBush, zLayer, isApple);
                
                // Update chunk data
                chunk.Positions[posCount] = x;
                chunk.Types[posCount] = (byte)(isApple ? 2 : (isBush ? 1 : 0));
                
                _gridBuffer[cell] = true;
                if (cell > 0) _gridBuffer[cell - 1] = true;
                if (cell < gridSize - 1) _gridBuffer[cell + 1] = true;
                
                posCount++;
            }
        }
        
        chunk.Count = posCount;
        return posCount;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FastFloatToInt(float value) => (int)value;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsPositionValidFast(float x, int posCount)
    {
        float minDist = MinTreeSpacing;
        for (int i = 0; i < posCount; i++)
        {
            if (Mathf.Abs(x - _positionBuffer[i]) < minDist)
                return false;
        }
        return true;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateZLayerFast(float x)
    {
        float normalizedX = x * _invChunkWidth;
        float depthFactor = normalizedX * 0.3f + _rng.Randf() * 0.7f + 
                           Mathf.Sin(normalizedX * _depthIntensityPi);
        
        float smoothness = LayerTransitionSmoothness;
        if (depthFactor < 0.15f * smoothness) return BackgroundZIndex;
        if (depthFactor < 0.35f * smoothness) return MidBackgroundZIndex;
        if (depthFactor < 0.65f) return BehindPlayerZIndex;
        if (depthFactor < 0.85f) return PlayerZIndex;
        return ForegroundZIndex;
    }
    
    private int GetConstraintsOptimized(int chunkIndex)
    {
        int count = 0;
        
        // Left chunk constraints with natural spacing
        if (_chunks.TryGetValue(chunkIndex - 1, out var leftChunk))
        {
            float threshold = ChunkWidth - MaxTreeSpacing * 2;
            if (UseNaturalSpacing)
                threshold += leftChunk.NaturalTransition * 50f; // Adjust threshold based on transition
                
            for (int i = 0; i < leftChunk.Count; i++)
            {
                if (leftChunk.Positions[i] > threshold && count < _constraintBuffer.Length - 1)
                {
                    _constraintBuffer[count++] = leftChunk.Positions[i] - (ChunkWidth + leftChunk.Offset);
                }
            }
        }
        
        // Right chunk constraints with natural spacing
        if (_chunks.TryGetValue(chunkIndex + 1, out var rightChunk))
        {
            float threshold = MaxTreeSpacing * 2;
            if (UseNaturalSpacing)
                threshold -= rightChunk.NaturalTransition * 50f; // Adjust threshold based on transition
                
            for (int i = 0; i < rightChunk.Count; i++)
            {
                if (rightChunk.Positions[i] < threshold && count < _constraintBuffer.Length - 1)
                {
                    _constraintBuffer[count++] = rightChunk.Positions[i] + (ChunkWidth + rightChunk.Offset);
                }
            }
        }
        
        return count;
    }
    
    private void SpawnObjectsBatch(int count, int chunkIndex)
    {
        for (int i = 0; i < count; i++)
        {
            var (x, isBush, zLayer, isApple) = _spawnBuffer[i];
            
            Node2D obj = GetPooledObjectSafe(isBush, isApple);
            if (obj == null) continue;
            
            ConfigureObjectFast(obj, x, isBush, zLayer, isApple);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Node2D GetPooledObjectSafe(bool isBush, bool isApple)
    {
        if (isBush && _hasBushPrefab)
        {
            while (_bushPool.Count > 0)
            {
                var pooledObj = _bushPool.Dequeue();
                if (pooledObj.TryUse())
                {
                    return pooledObj.Object;
                }
                else
                {
                    pooledObj.Dispose();
                }
            }
            // Fallback to new instance
            try
            {
                return BushPrefab.Instantiate<Node2D>();
            }
            catch
            {
                return null;
            }
        }
        
        if (isApple && _hasApplePrefab)
        {
            while (_appleTreePool.Count > 0)
            {
                var pooledObj = _appleTreePool.Dequeue();
                if (pooledObj.TryUse())
                {
                    return pooledObj.Object;
                }
                else
                {
                    pooledObj.Dispose();
                }
            }
            // Fallback to new instance
            try
            {
                return AppleBearingTreePrefab.Instantiate<Node2D>();
            }
            catch
            {
                return null;
            }
        }
        
        if (_treePrefabCount > 0)
        {
            while (_treePool.Count > 0)
            {
                var pooledObj = _treePool.Dequeue();
                if (pooledObj.TryUse())
                {
                    return pooledObj.Object;
                }
                else
                {
                    pooledObj.Dispose();
                }
            }
            // Fallback to new instance
            try
            {
                int idx = _rng.RandiRange(0, _treePrefabCount - 1);
                return TreePrefabs[idx].Instantiate<Node2D>();
            }
            catch
            {
                return null;
            }
        }
        
        return null;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConfigureObjectFast(Node2D obj, float x, bool isBush, int zLayer, bool isApple)
    {
        // Position
        float yOffset = isBush ? BushYOffset : 0f;
        float yVar = _rng.RandfRange(-TreeYVariation, TreeYVariation);
        obj.Position = new Vector2(x, TreeYPosition + yOffset + yVar);
        obj.ZIndex = zLayer;
        obj.Visible = true;
        
        // Container
        var container = zLayer < PlayerZIndex ? _backContainer : _frontContainer;
        container.AddChild(obj);
        
        // Color (skip for apple trees to preserve natural colors)
        if (!isApple)
        {
            var color = zLayer < PlayerZIndex ? BackgroundTreeColor : ForegroundTreeColor;
            ApplyColorSafe(obj, color);
        }
        
        // Properties
        if (isApple) ApplyApplePropertiesSafe(obj);
        ConfigureParametersSafe(obj, isBush, isApple);
        ApplyEffectsSafe(obj);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyColorSafe(Node2D obj, Color color)
    {
        try
        {
            if (obj.HasMethod("SetTreeColor"))
                obj.Call("SetTreeColor", color);
            else if (obj is CanvasItem ci)
                ci.Modulate = color;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to apply color: {ex.Message}");
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyApplePropertiesSafe(Node2D obj)
    {
        try
        {
            // Use fastest method available
            if (obj.HasMethod("SetHasApples"))
                obj.Call("SetHasApples", true);
            else
                obj.Set("hasApples", true);
            
            int appleCount = _rng.RandiRange(3, 8);
            if (obj.HasMethod("SetAppleQuantity"))
                obj.Call("SetAppleQuantity", appleCount);
            else
            {
                obj.Set("appleQuantity", appleCount);
                obj.Set("showApples", true);
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to apply apple properties: {ex.Message}");
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConfigureParametersSafe(Node2D obj, bool isBush, bool isApple)
    {
        try
        {
            if (isBush)
            {
                if (obj.HasMethod("SetBranchCount"))
                {
                    obj.Set("BranchCount", _rng.RandiRange(4, 6));
                    obj.Set("InitialLength", _rng.RandfRange(50f, 80f));
                }
            }
            else if (obj.HasMethod("SetMaxDepth"))
            {
                if (isApple)
                {
                    // Enhanced apple tree parameters
                    obj.Set("maxDepth", _rng.RandiRange(6, 8));
                    obj.Set("initialLength", _rng.RandfRange(200f, 240f));
                    obj.Set("leafDensity", _rng.RandiRange(2, 4));
                }
                else
                {
                    // Regular tree parameters
                    obj.Set("maxDepth", _rng.RandiRange(5, 7));
                    obj.Set("initialLength", _rng.RandfRange(180f, 220f));
                    obj.Set("leafDensity", _rng.RandiRange(1, 3));
                }
                
                obj.Set("branchAngle", _rng.RandfRange(15f, 22f));
                obj.Set("angleVariation", _rng.RandfRange(5f, 10f));
                obj.Set("maxBranches", _rng.RandiRange(2, 3));
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to configure parameters: {ex.Message}");
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyEffectsSafe(Node2D obj)
    {
        try
        {
            if (obj.HasMethod("RandomizeTree"))
                obj.Call("RandomizeTree", _rng.Randi());
            
            if (obj.HasMethod("SetWindStrength"))
                obj.Call("SetWindStrength", _rng.RandfRange(0.15f, 0.25f));
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to apply effects: {ex.Message}");
        }
    }
    
    private void ClearTrees()
    {
        ReturnToPoolSafe(_frontContainer);
        ReturnToPoolSafe(_backContainer);
    }
    
    private void ReturnToPoolSafe(Node2D container)
    {
        var children = container.GetChildren();
        foreach (Node child in children)
        {
            if (child is Node2D obj)
            {
                container.RemoveChild(obj);
                
                // Determine object type and return to appropriate pool safely
                try
                {
                    var pooledObj = new PooledObject(obj);
                    if (pooledObj.TryUse())
                    {
                        pooledObj.ReturnToPool();
                        
                        if (_hasBushPrefab && IsBushType(obj) && _bushPool.Count < 16)
                            _bushPool.Enqueue(pooledObj);
                        else if (_hasApplePrefab && IsAppleTreeType(obj) && _appleTreePool.Count < 8)
                            _appleTreePool.Enqueue(pooledObj);
                        else if (_treePool.Count < 32)
                            _treePool.Enqueue(pooledObj);
                        else
                            pooledObj.Dispose();
                    }
                    else
                    {
                        pooledObj.Dispose();
                    }
                }
                catch
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
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsBushType(Node2D obj)
    {
        try
        {
            return _hasBushPrefab && 
                obj.GetType().Name == System.IO.Path.GetFileNameWithoutExtension(BushPrefab.ResourcePath);
        }
        catch
        {
            return false;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsAppleTreeType(Node2D obj)
    {
        try
        {
            return _hasApplePrefab && 
                obj.GetType().Name == System.IO.Path.GetFileNameWithoutExtension(AppleBearingTreePrefab.ResourcePath);
        }
        catch
        {
            return false;
        }
    }
    
    public void ResetChunk() => ClearTrees();
    
    public void CleanupOldPositions(int currentChunkIndex, int keepRadius = 3)
    {
        var toRemove = new List<int>();
        foreach (var kvp in _chunks)
        {
            if (Mathf.Abs(kvp.Key - currentChunkIndex) > keepRadius)
                toRemove.Add(kvp.Key);
        }
        
        foreach (int key in toRemove)
            _chunks.Remove(key);
    }
    
    // Enhanced cleanup method to prevent disposed object issues
    public void ClearAllPools()
    {
        // Clear all pools and dispose invalid objects safely
        while (_treePool.Count > 0)
        {
            var pooledObj = _treePool.Dequeue();
            pooledObj.Dispose();
        }
        
        while (_bushPool.Count > 0)
        {
            var pooledObj = _bushPool.Dequeue();
            pooledObj.Dispose();
        }
        
        while (_appleTreePool.Count > 0)
        {
            var pooledObj = _appleTreePool.Dequeue();
            pooledObj.Dispose();
        }
    }
    
    // Debug methods
    public int GetTotalTreeCount() => _treeCount;
    public void ResetTreeCount() { _treeCount = 0; GD.Print("Tree counter reset"); }
    public void PrintAppleTreeDebugInfo()
    {
        GD.Print($"Trees: {_treeCount}, Per apple: {TreesPerApple}, Next in: {TreesPerApple - (_treeCount % TreesPerApple)}");
    }
    
    // Natural spacing validation methods
    public float GetChunkNaturalOffset(int chunkIndex)
    {
        if (_chunks.TryGetValue(chunkIndex, out var chunk))
            return chunk.Offset;
        return 0f;
    }
    
    public void SetNaturalSpacing(bool enabled)
    {
        UseNaturalSpacing = enabled;
        GD.Print($"Natural spacing: {(enabled ? "enabled" : "disabled")}");
    }
}