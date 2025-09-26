using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class InfiniteForestManager : Node2D
{
    [ExportGroup("Environment Settings")]
    [Export] public PackedScene EnvironmentChunkPrefab;
    [Export] public float ChunkWidth = 800.0f;
    [Export] public float SpawnDistance = 1200.0f;
    [Export] public float DestroyDistance = 1600.0f;
    [Export] public int MaxActiveChunks = 7;

    [ExportGroup("Camera Reference")]
    [Export] public Camera2D PlayerCamera;

    private Dictionary<int, Node2D> activeChunks = new Dictionary<int, Node2D>();
    private HashSet<int> processingChunks = new HashSet<int>(); // Tracks all chunks being processed
    private Dictionary<int, float> chunkDistances = new Dictionary<int, float>(); // Cache distances
    private float lastPlayerX = 0.0f;
    private int currentChunkIndex = 0;
    private int lastChunkIndex = int.MinValue;
    
    // Ultra-optimized update system
    private float updateCooldown = 0.0f;
    private const float UPDATE_COOLDOWN_TIME = 0.02f; // Even faster updates for smoother experience
    private const float MOVEMENT_THRESHOLD = 25.0f; // Smaller threshold for immediate response
    
    // Pre-calculated values for performance
    private float chunkWidthSq;
    private float spawnDistanceSq;
    private float destroyDistanceSq;
    private int chunksOnEachSide;
    
    // Predictive spawning
    private Queue<int> chunksToSpawn = new Queue<int>();
    private Queue<int> chunksToDestroy = new Queue<int>();
    private const int MAX_SPAWNS_PER_FRAME = 1;
    private const int MAX_DESTROYS_PER_FRAME = 2;

    public override void _Ready()
    {
        if (PlayerCamera == null)
        {
            PlayerCamera = GetViewport().GetCamera2D();
            if (PlayerCamera == null)
            {
                GD.PrintErr("InfiniteForestManager: No Camera2D found!");
                return;
            }
        }

        if (EnvironmentChunkPrefab == null)
        {
            GD.PrintErr("InfiniteForestManager: EnvironmentChunkPrefab not assigned!");
            return;
        }

        // Pre-calculate squared distances for performance
        chunkWidthSq = ChunkWidth * ChunkWidth;
        spawnDistanceSq = SpawnDistance * SpawnDistance;
        destroyDistanceSq = DestroyDistance * DestroyDistance;
        chunksOnEachSide = Mathf.CeilToInt(SpawnDistance / ChunkWidth);
        
        // Optimize distances for smoother experience
        SpawnDistance = Mathf.Max(SpawnDistance, ChunkWidth * 2.0f); // Ensure minimum coverage
        DestroyDistance = SpawnDistance * 1.5f; // Reasonable margin for destruction

        // Initialize with the starting chunk
        SpawnChunk(0);
        lastPlayerX = PlayerCamera.GlobalPosition.X;
        currentChunkIndex = GetChunkIndexFromPosition(lastPlayerX);
        lastChunkIndex = currentChunkIndex;
    }

    public override void _Process(double delta)
    {
        if (PlayerCamera == null) return;

        // Process queued chunk operations first (spread across frames)
        ProcessChunkQueues();

        // Update cooldown
        if (updateCooldown > 0)
        {
            updateCooldown -= (float)delta;
            return;
        }

        float currentPlayerX = PlayerCamera.GlobalPosition.X;
        int newChunkIndex = GetChunkIndexFromPosition(currentPlayerX);
        
        // Always update when chunk changes, more lenient for movement
        float deltaX = currentPlayerX - lastPlayerX;
        bool chunkChanged = newChunkIndex != lastChunkIndex;
        bool significantMovement = deltaX * deltaX > MOVEMENT_THRESHOLD * MOVEMENT_THRESHOLD;
        
        if (chunkChanged || significantMovement)
        {
            UpdateChunks(currentPlayerX, newChunkIndex);
            lastPlayerX = currentPlayerX;
            lastChunkIndex = newChunkIndex;
            updateCooldown = chunkChanged ? 0.01f : UPDATE_COOLDOWN_TIME; // Faster updates on chunk change
        }
    }

    private void ProcessChunkQueues()
    {
        // Process spawning queue
        int spawnsProcessed = 0;
        while (chunksToSpawn.Count > 0 && spawnsProcessed < MAX_SPAWNS_PER_FRAME)
        {
            int chunkIndex = chunksToSpawn.Dequeue();
            if (!activeChunks.ContainsKey(chunkIndex) && !processingChunks.Contains(chunkIndex))
            {
                SpawnChunkImmediate(chunkIndex);
                spawnsProcessed++;
            }
        }

        // Process destruction queue
        int destroysProcessed = 0;
        while (chunksToDestroy.Count > 0 && destroysProcessed < MAX_DESTROYS_PER_FRAME)
        {
            int chunkIndex = chunksToDestroy.Dequeue();
            if (activeChunks.ContainsKey(chunkIndex))
            {
                DestroyChunkImmediate(chunkIndex);
                destroysProcessed++;
            }
        }
    }

    private void UpdateChunks(float playerX, int newChunkIndex = -1)
    {
        if (newChunkIndex == -1)
            currentChunkIndex = GetChunkIndexFromPosition(playerX);
        else
            currentChunkIndex = newChunkIndex;
        
        // Pre-allocate collections to avoid garbage collection
        HashSet<int> requiredChunks = new HashSet<int>(MaxActiveChunks + 2);
        
        // Build required chunks list more efficiently
        for (int i = -chunksOnEachSide; i <= chunksOnEachSide; i++)
        {
            requiredChunks.Add(currentChunkIndex + i);
        }

        // Update distance cache and spawn/remove chunks in one pass
        List<int> chunksToRemove = null; // Lazy initialization
        
        foreach (var kvp in activeChunks)
        {
            int chunkIndex = kvp.Key;
            Node2D chunk = kvp.Value;
            
            // Skip if chunk is invalid
            if (chunk == null || !IsInstanceValid(chunk))
            {
                if (chunksToRemove == null) chunksToRemove = new List<int>();
                chunksToRemove.Add(chunkIndex);
                continue;
            }
            
            float chunkX = chunkIndex * ChunkWidth;
            float deltaX = chunkX - playerX;
            float distanceFromPlayer = deltaX < 0 ? -deltaX : deltaX; // Faster than Mathf.Abs
            
            // Cache the distance
            chunkDistances[chunkIndex] = distanceFromPlayer;
            
            if (distanceFromPlayer > DestroyDistance || !requiredChunks.Contains(chunkIndex))
            {
                if (chunksToRemove == null) chunksToRemove = new List<int>();
                chunksToRemove.Add(chunkIndex);
            }
            else
            {
                // Apply LOD less frequently to reduce overhead
                if (currentChunkIndex % 3 == 0) // Only every 3rd update
                {
                    ApplyLODToChunk(chunk, distanceFromPlayer);
                }
            }
        }

        // Queue missing chunks for spawning (spread across frames)
        foreach (int chunkIndex in requiredChunks)
        {
            if (!activeChunks.ContainsKey(chunkIndex) && !processingChunks.Contains(chunkIndex))
            {
                // Prioritize chunks closer to player
                float chunkX = chunkIndex * ChunkWidth;
                float distanceFromPlayer = FastAbs(chunkX - playerX);
                
                if (distanceFromPlayer < SpawnDistance * 0.7f)
                {
                    // Immediate spawn for close chunks
                    SpawnChunkImmediate(chunkIndex);
                }
                else if (!chunksToSpawn.Contains(chunkIndex))
                {
                    // Queue for later spawning
                    chunksToSpawn.Enqueue(chunkIndex);
                }
            }
        }

        // Queue chunks for removal (spread across frames)
        if (chunksToRemove != null)
        {
            foreach (int chunkIndex in chunksToRemove)
            {
                if (!chunksToDestroy.Contains(chunkIndex))
                {
                    chunksToDestroy.Enqueue(chunkIndex);
                }
            }
        }

        // Ensure we don't exceed the maximum number of chunks (optimized)
        EnforceMaxChunkLimit(playerX);

        // Predictive chunking - spawn chunks in movement direction
        PredictiveChunkSpawning(playerX);

        // Periodically clean up distance cache (every ~10 updates)
        if (currentChunkIndex % 10 == 0)
        {
            CleanupDistanceCache();
        }
    }

    private void PredictiveChunkSpawning(float playerX)
    {
        // Determine movement direction
        float movementDirection = playerX - lastPlayerX;
        if (FastAbs(movementDirection) < 10.0f) return; // Not moving enough to predict
        
        // Pre-spawn chunks in movement direction
        int directionMultiplier = movementDirection > 0 ? 1 : -1;
        int predictiveChunkIndex = currentChunkIndex + (directionMultiplier * (chunksOnEachSide + 1));
        
        // Only spawn if not already active and not too far
        if (!activeChunks.ContainsKey(predictiveChunkIndex) && 
            !processingChunks.Contains(predictiveChunkIndex) &&
            !chunksToSpawn.Contains(predictiveChunkIndex))
        {
            float predictiveChunkX = predictiveChunkIndex * ChunkWidth;
            float predictiveDistance = FastAbs(predictiveChunkX - playerX);
            
            if (predictiveDistance < SpawnDistance * 1.2f) // Slightly beyond normal spawn distance
            {
                chunksToSpawn.Enqueue(predictiveChunkIndex);
            }
        }
    }

    private void SpawnChunkImmediate(int chunkIndex)
    {
        // Comprehensive duplicate prevention
        if (EnvironmentChunkPrefab == null || 
            activeChunks.ContainsKey(chunkIndex) || 
            processingChunks.Contains(chunkIndex))
        {
            return;
        }

        // Mark as being processed immediately
        processingChunks.Add(chunkIndex);

        // Calculate position
        float chunkX = chunkIndex * ChunkWidth;

        // Skip position check for performance - trust the system

        // Instantiate the chunk
        Node2D newChunk = EnvironmentChunkPrefab.Instantiate<Node2D>();
        if (newChunk == null)
        {
            GD.PrintErr($"Failed to instantiate chunk at index {chunkIndex}");
            processingChunks.Remove(chunkIndex);
            return;
        }

        // Add to scene and set position immediately
        AddChild(newChunk);
        newChunk.GlobalPosition = new Vector2(chunkX, 0);

        // NO fade-in for immediate spawning - full visibility immediately
        newChunk.Modulate = Colors.White;

        // Pre-calculate seed for consistency
        ulong seed = (ulong)(chunkIndex * 12345 + 6789);

        // Initialize chunk if it has the appropriate methods
        if (newChunk.HasMethod("InitializeChunk"))
        {
            newChunk.Call("InitializeChunk", chunkIndex, seed);
        }
        else if (newChunk.HasMethod("ResetChunk"))
        {
            newChunk.Call("ResetChunk");
        }

        // Initialize dynamic tree spawner if present
        var treeSpawner = newChunk.GetNodeOrNull<Node2D>("DynamicTreeSpawner");
        if (treeSpawner != null && treeSpawner.HasMethod("InitializeChunk"))
        {
            treeSpawner.Call("InitializeChunk", chunkIndex, seed);
        }

        // Register the chunk and cache its distance
        activeChunks[chunkIndex] = newChunk;
        float playerX = PlayerCamera?.GlobalPosition.X ?? 0;
        chunkDistances[chunkIndex] = FastAbs(chunkX - playerX);
        
        processingChunks.Remove(chunkIndex);
    }

    private void SpawnChunk(int chunkIndex)
    {
        // This is now just a wrapper for compatibility
        SpawnChunkImmediate(chunkIndex);
    }







    private void ApplyLODToChunk(Node2D chunk, float distance)
    {
        if (chunk == null || !IsInstanceValid(chunk))
            return;

        // Pre-calculate LOD thresholds for performance
        float freezeThreshold = SpawnDistance * 0.9f;
        float unfreezeThreshold = SpawnDistance * 0.5f;

        // Apply distance-based optimizations
        if (chunk.HasMethod("OptimizeForDistance"))
        {
            chunk.Call("OptimizeForDistance", distance);
        }
        
        // Batch LOD operations to reduce method calls
        if (distance > freezeThreshold)
        {
            if (chunk.HasMethod("FreezeChunk"))
            {
                chunk.Call("FreezeChunk");
            }
        }
        else if (distance < unfreezeThreshold)
        {
            if (chunk.HasMethod("UnfreezeChunk"))
            {
                chunk.Call("UnfreezeChunk");
            }
        }
    }

    private void DestroyChunkImmediate(int chunkIndex)
    {
        if (!activeChunks.ContainsKey(chunkIndex))
            return;

        Node2D chunk = activeChunks[chunkIndex];
        if (chunk != null && IsInstanceValid(chunk))
        {
            // Immediate destruction - no fade for performance
            chunk.QueueFree();
        }
        
        // Clean up immediately to prevent issues
        activeChunks.Remove(chunkIndex);
        processingChunks.Remove(chunkIndex);
        chunkDistances.Remove(chunkIndex);
    }

    private void DestroyChunk(int chunkIndex)
    {
        // This is now just a wrapper for compatibility
        DestroyChunkImmediate(chunkIndex);
    }

    private void FadeOutAndDestroy(Node2D chunk)
    {
        if (chunk == null || !IsInstanceValid(chunk))
            return;

        // Quick fade out to reduce pop-out effect
        Tween tween = CreateTween();
        tween.TweenProperty(chunk, "modulate:a", 0.0f, 0.15f);
        tween.TweenCallback(Callable.From(() => {
            if (chunk != null && IsInstanceValid(chunk))
            {
                chunk.QueueFree();
            }
        }));
    }

    private int GetChunkIndexFromPosition(float positionX)
    {
        return Mathf.FloorToInt(positionX / ChunkWidth);
    }

    private void EnforceMaxChunkLimit(float playerX)
    {
        if (activeChunks.Count <= MaxActiveChunks)
            return;

        // Use cached distances instead of expensive LINQ operations
        int chunksToRemove = activeChunks.Count - MaxActiveChunks;
        List<(int index, float distance)> chunksByDistance = new List<(int, float)>(activeChunks.Count);
        
        foreach (var kvp in activeChunks)
        {
            if (kvp.Value != null && IsInstanceValid(kvp.Value))
            {
                float distance = chunkDistances.ContainsKey(kvp.Key) 
                    ? chunkDistances[kvp.Key] 
                    : FastAbs(kvp.Key * ChunkWidth - playerX);
                chunksByDistance.Add((kvp.Key, distance));
            }
        }
        
        // Sort by distance (furthest first) - more efficient than LINQ
        chunksByDistance.Sort((a, b) => b.distance.CompareTo(a.distance));
        
        // Remove the furthest chunks
        for (int i = 0; i < chunksToRemove && i < chunksByDistance.Count; i++)
        {
            DestroyChunk(chunksByDistance[i].index);
        }
    }
    
    // Faster absolute value calculation
    private static float FastAbs(float value)
    {
        return value < 0 ? -value : value;
    }

    // Public method to manually trigger chunk updates
    public void ForceUpdateChunks()
    {
        if (PlayerCamera != null)
        {
            updateCooldown = 0; // Reset cooldown to allow immediate update
            float playerX = PlayerCamera.GlobalPosition.X;
            int newChunkIndex = GetChunkIndexFromPosition(playerX);
            UpdateChunks(playerX, newChunkIndex);
            lastPlayerX = playerX;
            lastChunkIndex = newChunkIndex;
        }
    }

    // Public method to get current chunk information
    public Dictionary<string, object> GetChunkInfo()
    {
        return new Dictionary<string, object>
        {
            ["ActiveChunks"] = activeChunks.Count,
            ["ProcessingChunks"] = processingChunks.Count,
            ["CurrentChunkIndex"] = currentChunkIndex,
            ["PlayerPosition"] = PlayerCamera?.GlobalPosition ?? Vector2.Zero
        };
    }

    // Clean up when the node is freed
    public override void _ExitTree()
    {
        foreach (var chunk in activeChunks.Values)
        {
            if (chunk != null && IsInstanceValid(chunk))
            {
                chunk.QueueFree();
            }
        }
        activeChunks.Clear();
        processingChunks.Clear();
        chunkDistances.Clear();
        chunksToSpawn.Clear();
        chunksToDestroy.Clear();
    }

    // Periodically clean up orphaned distance cache entries
    private void CleanupDistanceCache()
    {
        if (chunkDistances.Count <= activeChunks.Count * 2) return;

        List<int> keysToRemove = new List<int>();
        foreach (int chunkIndex in chunkDistances.Keys)
        {
            if (!activeChunks.ContainsKey(chunkIndex))
            {
                keysToRemove.Add(chunkIndex);
            }
        }

        foreach (int key in keysToRemove)
        {
            chunkDistances.Remove(key);
        }
    }
}