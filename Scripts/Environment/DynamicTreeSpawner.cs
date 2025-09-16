using Godot;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public partial class DynamicTreeSpawner : Node2D
{
    [ExportGroup("Tree Distribution")]
    [Export] public int MinTreesPerChunk = 4;
    [Export] public int MaxTreesPerChunk = 8;
    [Export] public float MinTreeSpacing = 80.0f;
    [Export] public float ChunkWidth = 800.0f;
    [Export] public float TreeYPosition = 295.0f;
    [Export] public float TreeYVariation = 10.0f;
    
    [ExportGroup("Layer Depth Control")]
    [Export] public int BackgroundZIndex = -3;
    [Export] public int MidBackgroundZIndex = -2;
    [Export] public int BehindPlayerZIndex = -1;
    [Export] public int PlayerZIndex = 0;
    [Export] public int ForegroundZIndex = 1;
    [Export] public float DepthVariationIntensity = 0.4f;
    [Export] public float LayerTransitionSmoothness = 0.7f;
    
    [ExportGroup("Natural Chunk Spacing")]
    [Export] public float ChunkOverlapMin = -80.0f;
    [Export] public float ChunkOverlapMax = 120.0f;
    [Export] public float ChunkTransitionSmoothness = 0.8f;
    [Export] public bool UseNaturalSpacing = true;
    
    [ExportGroup("Fractal Settings")]
    [Export] public float FractalTreeProbability = 0.7f;
    [Export] public float FractalTreeWidth = 200.0f;
    [Export] public float FractalTreeHeight = 300.0f;
    [Export] public float FractalVerticalOffset = 0.0f;
    
    // Optimized data structures
    private readonly Dictionary<int, ChunkData> _chunks = new Dictionary<int, ChunkData>();
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private readonly List<Texture2D> _fractalTextures = new List<Texture2D>();
    
    // Cached containers
    private Node2D _backContainer;
    private Node2D _frontContainer;
    
    // Pre-calculated values
    private float _invChunkWidth;
    private float _depthIntensityPi;
    private bool _hasFractalTextures;
    
    // Simplified chunk data
    private struct ChunkData
    {
        public float[] Positions;
        public int[] ZLayers;
        public float Offset;
        public int Count;
        public float NaturalTransition;
        
        public ChunkData(int capacity)
        {
            Positions = new float[capacity];
            ZLayers = new int[capacity];
            Offset = 0f;
            Count = 0;
            NaturalTransition = 0f;
        }
    }
    
    public override void _Ready()
    {
        Initialize();
    }
    
    private void Initialize()
    {
        // Pre-calculate commonly used values
        _invChunkWidth = 1f / ChunkWidth;
        _depthIntensityPi = DepthVariationIntensity * Mathf.Pi;
        
        // Load fractal textures
        LoadFractalTextures();
        
        // Create containers
        _backContainer = new Node2D { Name = "BackTrees", ZIndex = BehindPlayerZIndex };
        _frontContainer = new Node2D { Name = "FrontTrees" };
        AddChild(_backContainer);
        AddChild(_frontContainer);
        
        GD.Print($"Initialized with {_fractalTextures.Count} fractal textures");
    }
    
    private void LoadFractalTextures()
    {
        try
        {
            var dir = DirAccess.Open("res://FractalsExports");
            if (dir == null)
            {
                GD.Print("FractalsExports directory not found");
                return;
            }
            
            dir.ListDirBegin();
            int loadedCount = 0;
            
            while (true)
            {
                var file = dir.GetNext();
                if (string.IsNullOrEmpty(file)) break;
                
                if (file == "." || file == ".." || dir.CurrentIsDir()) continue;
                
                var ext = System.IO.Path.GetExtension(file)?.ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                
                var path = $"res://FractalsExports/{file}";
                var texture = ResourceLoader.Load<Texture2D>(path);
                if (texture != null)
                {
                    _fractalTextures.Add(texture);
                    loadedCount++;
                }
            }
            
            dir.ListDirEnd();
            _hasFractalTextures = _fractalTextures.Count > 0;
            
            if (_hasFractalTextures)
            {
                GD.Print($"Loaded {loadedCount} fractal textures");
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Error loading fractal textures: {ex.Message}");
        }
    }
    
    public void InitializeChunk(int chunkIndex, ulong seed)
    {
        ClearTrees();
        _rng.Seed = seed;
        
        // Get or create chunk data
        if (!_chunks.TryGetValue(chunkIndex, out var chunk))
        {
            chunk = new ChunkData(MaxTreesPerChunk);
            _chunks[chunkIndex] = chunk;
        }
        
        // Calculate natural spacing
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
        
        // Generate and spawn trees
        int treeCount = GenerateTreePositions(chunkIndex, chunk);
        SpawnTrees(chunk, treeCount);
        
        _chunks[chunkIndex] = chunk;
    }
    
    private float CalculateNaturalChunkOffset(int chunkIndex)
    {
        float baseNoise = Mathf.Sin(chunkIndex * 0.7f) * Mathf.Cos(chunkIndex * 0.3f);
        float detailNoise = Mathf.Sin(chunkIndex * 2.1f) * 0.3f;
        float combinedNoise = (baseNoise + detailNoise) * 0.5f;
        
        float naturalBias = -20.0f;
        return Mathf.Lerp(ChunkOverlapMin, ChunkOverlapMax, (combinedNoise + 1f) * 0.5f) + naturalBias;
    }
    
    private float CalculateChunkTransition(int chunkIndex)
    {
        return Mathf.Sin(chunkIndex * 0.5f + Mathf.Pi * 0.25f) * ChunkTransitionSmoothness;
    }
    
    private int GenerateTreePositions(int chunkIndex, ChunkData chunk)
    {
        int targetCount = _rng.RandiRange(MinTreesPerChunk, MaxTreesPerChunk);
        int posCount = 0;
        int maxAttempts = targetCount * 10;
        
        // Get constraints from neighboring chunks
        var constraints = GetNeighborConstraints(chunkIndex);
        
        for (int attempt = 0; attempt < maxAttempts && posCount < targetCount; attempt++)
        {
            float x = _rng.RandfRange(MinTreeSpacing, ChunkWidth - MinTreeSpacing);
            
            if (IsPositionValid(x, chunk.Positions, posCount, constraints))
            {
                chunk.Positions[posCount] = x;
                chunk.ZLayers[posCount] = CalculateZLayer(x);
                posCount++;
            }
        }
        
        chunk.Count = posCount;
        return posCount;
    }
    
    private List<float> GetNeighborConstraints(int chunkIndex)
    {
        var constraints = new List<float>();
        
        // Left chunk
        if (_chunks.TryGetValue(chunkIndex - 1, out var leftChunk))
        {
            for (int i = 0; i < leftChunk.Count; i++)
            {
                if (leftChunk.Positions[i] > ChunkWidth - MinTreeSpacing * 2)
                {
                    constraints.Add(leftChunk.Positions[i] - (ChunkWidth + leftChunk.Offset));
                }
            }
        }
        
        // Right chunk
        if (_chunks.TryGetValue(chunkIndex + 1, out var rightChunk))
        {
            for (int i = 0; i < rightChunk.Count; i++)
            {
                if (rightChunk.Positions[i] < MinTreeSpacing * 2)
                {
                    constraints.Add(rightChunk.Positions[i] + (ChunkWidth + rightChunk.Offset));
                }
            }
        }
        
        return constraints;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsPositionValid(float x, float[] positions, int count, List<float> constraints)
    {
        // Check against existing positions in chunk
        for (int i = 0; i < count; i++)
        {
            if (Mathf.Abs(x - positions[i]) < MinTreeSpacing)
                return false;
        }
        
        // Check against neighboring chunk constraints
        foreach (float constraint in constraints)
        {
            if (Mathf.Abs(x - constraint) < MinTreeSpacing)
                return false;
        }
        
        return true;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateZLayer(float x)
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
    
    private void SpawnTrees(ChunkData chunk, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = chunk.Positions[i];
            int zLayer = chunk.ZLayers[i];
            
            Node2D tree = CreateTree();
            if (tree == null) continue;
            
            ConfigureTree(tree, x, zLayer);
        }
    }
    
    private Node2D CreateTree()
    {
        if (_hasFractalTextures && _rng.Randf() < FractalTreeProbability)
        {
            return CreateFractalSprite();
        }
        
        // Fallback: create simple colored rectangle as placeholder
        return CreatePlaceholderTree();
    }
    
    private Node2D CreateFractalSprite()
    {
        if (!_hasFractalTextures) return null;
        
        try
        {
            var root = new Node2D { Name = "FractalTree" };
            var sprite = new Sprite2D { Name = "Sprite2D" };
            
            // Pick random fractal texture
            int textureIdx = _rng.RandiRange(0, _fractalTextures.Count - 1);
            var texture = _fractalTextures[textureIdx];
            sprite.Texture = texture;
            
            // Configure size and position
            if (texture != null)
            {
                float texW = texture.GetWidth();
                float texH = texture.GetHeight();
                float randVar = _rng.RandfRange(0.9f, 1.1f);
                
                if (FractalTreeWidth > 0f && FractalTreeHeight > 0f)
                {
                    float scaleX = (FractalTreeWidth / texW) * randVar;
                    float scaleY = (FractalTreeHeight / texH) * randVar;
                    sprite.Scale = new Vector2(scaleX, scaleY);
                }
                else
                {
                    float scale = _rng.RandfRange(0.8f, 1.2f);
                    sprite.Scale = new Vector2(scale, scale);
                }
                
                // Position sprite so root node marks bottom-center of tree
                sprite.Centered = true;
                float baseOffset = -(texH * sprite.Scale.Y * 0.5f) + FractalVerticalOffset;
                sprite.Position = new Vector2(0f, baseOffset);
            }
            
            root.AddChild(sprite);
            return root;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to create fractal sprite: {ex.Message}");
            return null;
        }
    }
    
    private Node2D CreatePlaceholderTree()
    {
        var root = new Node2D { Name = "PlaceholderTree" };
        var rect = new ColorRect 
        { 
            Size = new Vector2(20, 100),
            Color = Colors.SaddleBrown,
            Position = new Vector2(-10, -100)
        };
        root.AddChild(rect);
        return root;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConfigureTree(Node2D tree, float x, int zLayer)
    {
        // Position with variation
        float yVar = _rng.RandfRange(-TreeYVariation, TreeYVariation);
        tree.Position = new Vector2(x, TreeYPosition + yVar);
        tree.ZIndex = zLayer;
        tree.Visible = true;
        
        // Add to appropriate container
        var container = zLayer < PlayerZIndex ? _backContainer : _frontContainer;
        container.AddChild(tree);
    }
    
    private void ClearTrees()
    {
        ClearContainer(_backContainer);
        ClearContainer(_frontContainer);
    }
    
    private void ClearContainer(Node2D container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
    
    public void CleanupOldChunks(int currentChunkIndex, int keepRadius = 3)
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
    
    public void ResetChunk() => ClearTrees();
    
    // Debug methods
    public void PrintDebugInfo()
    {
        GD.Print($"=== Tree Spawner Debug Info ===");
        GD.Print($"Fractal textures: {_fractalTextures.Count}");
        GD.Print($"Fractal probability: {FractalTreeProbability}");
        GD.Print($"Natural spacing: {UseNaturalSpacing}");
        GD.Print($"Chunks loaded: {_chunks.Count}");
    }
    
    public void SetNaturalSpacing(bool enabled)
    {
        UseNaturalSpacing = enabled;
        GD.Print($"Natural spacing: {(enabled ? "enabled" : "disabled")}");
    }
}