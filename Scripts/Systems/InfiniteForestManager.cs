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
    private float lastPlayerX = 0.0f;
    private int currentChunkIndex = 0;
    
    // Prevent too frequent updates
    private float updateCooldown = 0.0f;
    private const float UPDATE_COOLDOWN_TIME = 0.1f; // 100ms between updates

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

        // Initialize with the starting chunk
        SpawnChunk(0);
        lastPlayerX = PlayerCamera.GlobalPosition.X;
    }

    public override void _Process(double delta)
    {
        if (PlayerCamera == null) return;

        // Update cooldown
        if (updateCooldown > 0)
        {
            updateCooldown -= (float)delta;
            return;
        }

        float currentPlayerX = PlayerCamera.GlobalPosition.X;
        
        // Check if player has moved significantly (reduced threshold for responsiveness)
        if (Mathf.Abs(currentPlayerX - lastPlayerX) > ChunkWidth * 0.1f)
        {
            UpdateChunks(currentPlayerX);
            lastPlayerX = currentPlayerX;
            updateCooldown = UPDATE_COOLDOWN_TIME; // Set cooldown
        }
    }

    private void UpdateChunks(float playerX)
    {
        currentChunkIndex = GetChunkIndexFromPosition(playerX);
        
        // Determine which chunks should be active
        HashSet<int> requiredChunks = new HashSet<int>();
        
        // Calculate how many chunks we need on each side
        int chunksOnEachSide = Mathf.CeilToInt(SpawnDistance / ChunkWidth);
        
        for (int i = -chunksOnEachSide; i <= chunksOnEachSide; i++)
        {
            requiredChunks.Add(currentChunkIndex + i);
        }

        // Spawn missing chunks (with duplicate prevention)
        foreach (int chunkIndex in requiredChunks)
        {
            // Check if chunk exists or is being processed
            if (!activeChunks.ContainsKey(chunkIndex) && !processingChunks.Contains(chunkIndex))
            {
                SpawnChunk(chunkIndex);
            }
        }

        // Remove chunks that are too far away
        List<int> chunksToRemove = new List<int>();
        foreach (var kvp in activeChunks)
        {
            int chunkIndex = kvp.Key;
            Node2D chunk = kvp.Value;
            
            // Skip if chunk is invalid
            if (chunk == null || !IsInstanceValid(chunk))
            {
                chunksToRemove.Add(chunkIndex);
                continue;
            }
            
            float chunkX = chunkIndex * ChunkWidth;
            float distanceFromPlayer = Mathf.Abs(chunkX - playerX);
            
            if (distanceFromPlayer > DestroyDistance || !requiredChunks.Contains(chunkIndex))
            {
                chunksToRemove.Add(chunkIndex);
            }
            else
            {
                // Apply LOD based on distance
                ApplyLODToChunk(chunk, distanceFromPlayer);
            }
        }

        // Actually remove the chunks
        foreach (int chunkIndex in chunksToRemove)
        {
            DestroyChunk(chunkIndex);
        }

        // Ensure we don't exceed the maximum number of chunks
        EnforceMaxChunkLimit();
    }

    private void SpawnChunk(int chunkIndex)
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

        // Check if a chunk already exists at this position
        if (IsPositionOccupied(chunkX))
        {
            GD.PrintErr($"Position {chunkX} already occupied, skipping chunk {chunkIndex}");
            processingChunks.Remove(chunkIndex);
            return;
        }

        // Instantiate the chunk
        Node2D newChunk = EnvironmentChunkPrefab.Instantiate<Node2D>();
        if (newChunk == null)
        {
            GD.PrintErr($"Failed to instantiate chunk at index {chunkIndex}");
            processingChunks.Remove(chunkIndex);
            return;
        }

        // Set name for debugging
        newChunk.Name = $"EnvironmentChunk_{chunkIndex}";

        // Add to scene and set position
        AddChild(newChunk);
        newChunk.GlobalPosition = new Vector2(chunkX, 0);

        // Initialize chunk if it has the appropriate methods
        if (newChunk.HasMethod("InitializeChunk"))
        {
            // Use chunk index for consistent random seed
            ulong seed = (ulong)(chunkIndex * 12345 + 6789);
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
            ulong seed = (ulong)(chunkIndex * 12345 + 6789);
            treeSpawner.Call("InitializeChunk", chunkIndex, seed);
        }

        // Register the chunk
        activeChunks[chunkIndex] = newChunk;
        processingChunks.Remove(chunkIndex);

        GD.Print($"Spawned chunk {chunkIndex} at position {chunkX}");
    }

    private bool IsPositionOccupied(float xPosition)
    {
        // Check if any active chunk is already at this position
        // Use a small tolerance to account for floating point precision
        const float tolerance = 1.0f;
        
        foreach (var kvp in activeChunks)
        {
            if (kvp.Value != null && IsInstanceValid(kvp.Value))
            {
                float existingX = kvp.Value.GlobalPosition.X;
                if (Mathf.Abs(existingX - xPosition) < tolerance)
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    private void ApplyLODToChunk(Node2D chunk, float distance)
    {
        if (chunk == null || !IsInstanceValid(chunk))
            return;

        // Apply distance-based optimizations
        if (chunk.HasMethod("OptimizeForDistance"))
        {
            chunk.Call("OptimizeForDistance", distance);
        }
        
        // Freeze very distant chunks
        if (distance > SpawnDistance * 0.9f)
        {
            if (chunk.HasMethod("FreezeChunk"))
            {
                chunk.Call("FreezeChunk");
            }
        }
        else if (distance < SpawnDistance * 0.5f)
        {
            if (chunk.HasMethod("UnfreezeChunk"))
            {
                chunk.Call("UnfreezeChunk");
            }
        }
    }

    private void DestroyChunk(int chunkIndex)
    {
        if (!activeChunks.ContainsKey(chunkIndex))
            return;

        Node2D chunk = activeChunks[chunkIndex];
        if (chunk != null && IsInstanceValid(chunk))
        {
            chunk.QueueFree();
        }
        
        activeChunks.Remove(chunkIndex);
        processingChunks.Remove(chunkIndex); // Clean up processing set too
        
        GD.Print($"Destroyed chunk {chunkIndex}");
    }

    private int GetChunkIndexFromPosition(float positionX)
    {
        return Mathf.FloorToInt(positionX / ChunkWidth);
    }

    private void EnforceMaxChunkLimit()
    {
        if (activeChunks.Count <= MaxActiveChunks)
            return;

        // Find chunks furthest from the player
        var sortedChunks = activeChunks
            .Where(kvp => kvp.Value != null && IsInstanceValid(kvp.Value))
            .OrderByDescending(kvp => Mathf.Abs(kvp.Key * ChunkWidth - PlayerCamera.GlobalPosition.X))
            .ToList();

        int chunksToRemove = activeChunks.Count - MaxActiveChunks;
        for (int i = 0; i < chunksToRemove && i < sortedChunks.Count; i++)
        {
            DestroyChunk(sortedChunks[i].Key);
        }
    }

    // Public method to manually trigger chunk updates
    public void ForceUpdateChunks()
    {
        if (PlayerCamera != null)
        {
            updateCooldown = 0; // Reset cooldown to allow immediate update
            UpdateChunks(PlayerCamera.GlobalPosition.X);
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
    }
}