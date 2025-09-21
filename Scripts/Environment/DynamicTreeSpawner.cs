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
    
    [ExportGroup("Apple Settings")]
    [Export] public PackedScene ApplePrefab;
    [Export] public int MinApplesPerTree = 1;
    [Export] public int MaxApplesPerTree = 4;
    // Constrained region for apple placement within the tree crown
    [Export] public float ApplePlacementRadius = 50.0f;    // Base radius of region where apples can be placed (reduced for tighter placement)
    [Export] public float ApplePlacementOffsetY = -120.0f; // Y offset from tree base to center of apple region (moved up into crown)
    [Export] public float AppleMinSpacing = 20.0f;         // Minimum distance between apples
    [Export] public float BranchAttachmentRadius = 8.0f;   // How close apple must be to a branch pixel (tighter requirement)
    [Export] public int MaxApplePlacementAttempts = 100;   // Max attempts to find valid apple position (increased for better coverage)

    [ExportGroup("Apple Allowed Region")]
    // The allowed region targets the dense branching area of the tree crown
    [Export] public float AppleArcStartDeg = 200.0f;       // Start angle (targeting upper-left crown region)
    [Export] public float AppleArcEndDeg = 340.0f;         // End angle (targeting upper-right crown region)
    [Export] public float AppleInnerRadiusRatio = 0.3f;    // 0..1, inner radius as fraction of ApplePlacementRadius (closer to center)
    [Export] public float AppleOuterRadiusRatio = 0.8f;    // 0..1, outer radius as fraction of ApplePlacementRadius (not at the very edge)
    [Export] public float AppleEllipseYScale = 0.8f;       // Vertical squash of placement ellipse (less squashed for crown area)
    
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
    
    // Structure to hold apple position data
    private struct ApplePosition
    {
        public Vector2 LocalPosition;
        public bool IsValid;
        
        public ApplePosition(Vector2 pos, bool valid)
        {
            LocalPosition = pos;
            IsValid = valid;
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
        
        // Load fractal textures (FractalsExports is expected to always contain textures)
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
        // Ensure targetCount never exceeds the allocated array capacity for this chunk
        int capacity = chunk.Positions != null ? chunk.Positions.Length : MaxTreesPerChunk;
        int minCount = Mathf.Clamp(MinTreesPerChunk, 0, capacity);
        int maxCount = Mathf.Clamp(MaxTreesPerChunk, minCount, capacity);
        
        // Safe RandiRange call
        int targetCount = maxCount > minCount ? _rng.RandiRange(minCount, maxCount) : minCount;
        int posCount = 0;
        int maxAttempts = targetCount * 10;
        
        // Get constraints from neighboring chunks
        var constraints = GetNeighborConstraints(chunkIndex);
        
        for (int attempt = 0; attempt < maxAttempts && posCount < targetCount; attempt++)
        {
            float x = _rng.RandfRange(MinTreeSpacing, ChunkWidth - MinTreeSpacing);
            
            if (IsPositionValid(x, chunk.Positions, posCount, constraints))
            {
                // Guard against possible race where posCount reaches capacity
                if (posCount >= capacity)
                    break;

                chunk.Positions[posCount] = x;
                // Ensure ZLayers array exists and has enough capacity
                if (chunk.ZLayers == null || chunk.ZLayers.Length < capacity)
                    chunk.ZLayers = new int[capacity];

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
        // Always create fractal sprites. The project assumes FractalsExports contains textures.
        var fractal = CreateFractalSprite();
        if (fractal != null)
            return fractal;

        // Last-resort minimal placeholder if something unexpected happens
        var root = new Node2D { Name = "PlaceholderTreeFallback" };
        var rect = new ColorRect
        {
            Size = new Vector2(20, 100),
            Color = Colors.SaddleBrown,
            Position = new Vector2(-10, -100)
        };
        root.AddChild(rect);
        return root;
    }
    
    private Node2D CreateFractalSprite()
    {
        if (!_hasFractalTextures) return null;
        
        try
        {
            var root = new Node2D { Name = "FractalTree" };
            var sprite = new Sprite2D { Name = "Sprite2D" };
            
            // Pick random fractal texture with proper bounds checking
            if (_fractalTextures.Count == 0)
            {
                GD.PrintErr("No fractal textures available");
                return null;
            }
            
            int maxIdx = _fractalTextures.Count - 1;
            int textureIdx = maxIdx > 0 ? _rng.RandiRange(0, maxIdx) : 0;
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

            // Spawn apples as children of the fractal root (positioned relative to the tree)
            TrySpawnApplesInCrown(root, sprite, texture);
            return root;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to create fractal sprite: {ex.Message}");
            return null;
        }
    }

    private void TrySpawnApplesInCrown(Node2D treeRoot, Sprite2D treeSprite, Texture2D texture)
    {
        if (ApplePrefab == null || texture == null) 
        {
            GD.Print("Warning: ApplePrefab or texture is null");
            return;
        }

        // Determine how many apples to spawn
        int minApples = Mathf.Max(0, MinApplesPerTree);
        int maxApples = Mathf.Max(minApples, MaxApplesPerTree);
        
        int targetAppleCount = maxApples > minApples ? 
            _rng.RandiRange(minApples, maxApples) : minApples;
        
        if (targetAppleCount <= 0) 
        {
            GD.Print("Warning: targetAppleCount is 0");
            return;
        }

        // Get texture data for branch detection
        var image = texture.GetImage();
        if (image == null) 
        {
            GD.Print("Warning: Could not get image from texture");
            return;
        }

        var placedApples = new List<Vector2>();
        int successfullyPlaced = 0;

        // Elliptical arc band placement approach (restricted to the underside of the crown)
        // Position the apple placement center relative to the tree sprite center (crown area)
        Vector2 circleCenter = new Vector2(0f, treeSprite.Position.Y + ApplePlacementOffsetY);
        
        for (int attempt = 0; attempt < MaxApplePlacementAttempts && successfullyPlaced < targetAppleCount; attempt++)
        {
            // Generate random position within the allowed elliptical arc band
            float angle = RandomAngleInAllowedArc();
            // Clamp ratios first, then scale to world units to avoid min>max errors
            float innerRatio = Mathf.Clamp(AppleInnerRadiusRatio, 0f, 1f);
            float outerRatio = Mathf.Clamp(AppleOuterRadiusRatio, innerRatio, 1f);
            float rMin = innerRatio * ApplePlacementRadius;
            float rMax = outerRatio * ApplePlacementRadius;
            float distance = _rng.RandfRange(rMin, rMax);

            Vector2 candidatePos = circleCenter + new Vector2(
                Mathf.Cos(angle) * distance,
                Mathf.Sin(angle) * distance * AppleEllipseYScale // Elliptical flattening
            );

            // Check spacing with existing apples
            bool hasGoodSpacing = true;
            foreach (var existingPos in placedApples)
            {
                if (candidatePos.DistanceTo(existingPos) < AppleMinSpacing)
                {
                    hasGoodSpacing = false;
                    break;
                }
            }
            if (!hasGoodSpacing) continue;

            // Check if position is near branch pixels (simplified)
            if (IsNearBranchSimple(image, candidatePos, treeSprite, texture))
            {
                var inst = ApplePrefab.Instantiate() as Node2D;
                if (inst != null)
                {
                    inst.Position = candidatePos;
                    treeRoot.AddChild(inst);
                    placedApples.Add(candidatePos);
                    successfullyPlaced++;
                }
            }
        }

        // Always log apple placement results
        GD.Print($"Tree: Placed {successfullyPlaced}/{targetAppleCount} apples");
    }

    // Returns a random angle (in radians) inside the configured [AppleArcStartDeg, AppleArcEndDeg] range.
    // Angles follow unit circle with 0° at +X, increasing clockwise; since Y grows downward in screen space,
    // 90° points down. Defaults (20°..160°) target the underside of the crown, avoiding the top region.
    private float RandomAngleInAllowedArc()
    {
        float start = NormalizeDeg(AppleArcStartDeg);
        float end = NormalizeDeg(AppleArcEndDeg);

        // If the arc wraps past 360°, split it into two ranges proportionally
        if (start <= end)
        {
            return Mathf.DegToRad(_rng.RandfRange(start, end));
        }
        else
        {
            float rangeA = 360f - start;
            float rangeB = end;
            float total = rangeA + rangeB;
            if (total <= 0.0001f)
                return Mathf.DegToRad(start); // degenerate case
            bool pickA = _rng.Randf() < (rangeA / total);
            float deg = pickA ? _rng.RandfRange(start, 360f) : _rng.RandfRange(0f, end);
            return Mathf.DegToRad(deg);
        }
    }

    private float NormalizeDeg(float deg)
    {
        float d = deg % 360f;
        if (d < 0f) d += 360f;
        return d;
    }

    private bool IsNearBranchSimple(Image image, Vector2 localPos, Sprite2D sprite, Texture2D texture)
    {
        // Convert local position to texture coordinates
        // Account for sprite position offset and scaling
        Vector2 spriteRelative = localPos - sprite.Position;
        float texW = texture.GetWidth();
        float texH = texture.GetHeight();
        
        // Account for sprite scaling and centering
        // Since sprite is centered, we need to account for this in our coordinate transformation
        float texX = (spriteRelative.X / sprite.Scale.X) + (texW * 0.5f);
        float texY = (spriteRelative.Y / sprite.Scale.Y) + (texH * 0.5f);
        
        int centerX = Mathf.RoundToInt(Mathf.Clamp(texX, 0, texW - 1));
        int centerY = Mathf.RoundToInt(Mathf.Clamp(texY, 0, texH - 1));
        
        // Check for non-transparent pixels (branches) within attachment radius
        // Scale the search radius based on sprite scale for consistency
        int searchRadius = Mathf.RoundToInt(BranchAttachmentRadius / Mathf.Max(sprite.Scale.X, sprite.Scale.Y));
        searchRadius = Mathf.Max(1, searchRadius); // Ensure minimum search radius
        
        // First, check if the center position itself is on a branch
        Color centerPixel = image.GetPixel(centerX, centerY);
        bool foundBranch = centerPixel.A > 0.2f; // Higher threshold for more selective placement
        
        // If center isn't on a branch, check nearby pixels
        if (!foundBranch)
        {
            int nearbyFound = 0;
            int totalChecked = 0;
            
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    int pixelX = centerX + dx;
                    int pixelY = centerY + dy;
                    
                    // Check bounds
                    if (pixelX < 0 || pixelX >= texW || pixelY < 0 || pixelY >= texH) continue;
                    
                    // Check distance
                    if (dx * dx + dy * dy > searchRadius * searchRadius) continue;
                    
                    totalChecked++;
                    
                    // Check if pixel is part of tree (non-transparent)
                    Color pixel = image.GetPixel(pixelX, pixelY);
                    if (pixel.A > 0.2f) // Higher threshold for branch detection
                    {
                        nearbyFound++;
                    }
                }
            }
            
            // Require a minimum density of branch pixels in the area
            float branchDensity = totalChecked > 0 ? (float)nearbyFound / totalChecked : 0f;
            foundBranch = branchDensity > 0.3f; // At least 30% of nearby pixels should be branches
        }
        
        return foundBranch;
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
        GD.Print($"Apple placement: Radius={ApplePlacementRadius}, OffsetY={ApplePlacementOffsetY}, MinSpacing={AppleMinSpacing}");
        GD.Print($"Apple allowed arc: {AppleArcStartDeg}°..{AppleArcEndDeg}°, band={AppleInnerRadiusRatio:P0}-{AppleOuterRadiusRatio:P0}, yScale={AppleEllipseYScale}");
    }
    
    public void SetNaturalSpacing(bool enabled)
    {
        UseNaturalSpacing = enabled;
        GD.Print($"Natural spacing: {(enabled ? "enabled" : "disabled")}");
    }
}