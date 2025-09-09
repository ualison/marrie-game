using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class InfiniteForestManager : Node2D
{
    [ExportGroup("Environment Settings")]
    [Export] public PackedScene EnvironmentChunkPrefab; // Your Environment.tscn prefab
    [Export] public float ChunkWidth = 800.0f; // Width of each environment chunk
    [Export] public float SpawnDistance = 1200.0f; // Distance from camera to spawn new chunks
    [Export] public float DestroyDistance = 1600.0f; // Distance from camera to destroy chunks
    [Export] public int MaxActiveChunks = 7; // Maximum number of chunks to keep active

    [ExportGroup("Camera Reference")]
    [Export] public Camera2D PlayerCamera;

    private Dictionary<int, Node2D> activeChunks = new Dictionary<int, Node2D>();
    private float lastPlayerX = 0.0f;
    private int currentChunkIndex = 0;

    public override void _Ready()
    {
        if (PlayerCamera == null)
        {
            // Try to find the player camera automatically
            PlayerCamera = GetViewport().GetCamera2D();
            if (PlayerCamera == null)
            {
                GD.PrintErr("InfiniteForestManager: No Camera2D found! Please assign PlayerCamera.");
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

        float currentPlayerX = PlayerCamera.GlobalPosition.X;
        
        // Check if player has moved significantly
        if (Mathf.Abs(currentPlayerX - lastPlayerX) > ChunkWidth * 0.1f)
        {
            UpdateChunks(currentPlayerX);
            lastPlayerX = currentPlayerX;
        }
    }

    private void UpdateChunks(float playerX)
    {
        currentChunkIndex = GetChunkIndexFromPosition(playerX);
        
        // Determine which chunks should be active
        HashSet<int> requiredChunks = new HashSet<int>();
        
        // Calculate how many chunks we need on each side based on spawn distance
        int chunksOnEachSide = Mathf.CeilToInt(SpawnDistance / ChunkWidth);
        
        for (int i = -chunksOnEachSide; i <= chunksOnEachSide; i++)
        {
            requiredChunks.Add(currentChunkIndex + i);
        }

        // Spawn missing chunks
        foreach (int chunkIndex in requiredChunks)
        {
            if (!activeChunks.ContainsKey(chunkIndex))
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
            
            float chunkX = chunkIndex * ChunkWidth;
            float distanceFromPlayer = Mathf.Abs(chunkX - playerX);
            
            if (distanceFromPlayer > DestroyDistance || !requiredChunks.Contains(chunkIndex))
            {
                chunksToRemove.Add(chunkIndex);
            }
        }

        // Actually remove the chunks
        foreach (int chunkIndex in chunksToRemove)
        {
            DestroyChunk(chunkIndex);
        }

        // Ensure we don't exceed the maximum number of chunks (safety measure)
        EnforceMaxChunkLimit();
        
        // Debug info
        GD.Print($"Active chunks: {activeChunks.Count}, Player chunk: {currentChunkIndex}");
    }

    private void SpawnChunk(int chunkIndex)
    {
        if (EnvironmentChunkPrefab == null || activeChunks.ContainsKey(chunkIndex))
            return;

        // Instantiate the chunk
        Node2D newChunk = EnvironmentChunkPrefab.Instantiate<Node2D>();
        if (newChunk == null)
        {
            GD.PrintErr($"Failed to instantiate chunk at index {chunkIndex}");
            return;
        }

        // Position the chunk
        float chunkX = chunkIndex * ChunkWidth;
        newChunk.GlobalPosition = new Vector2(chunkX, 0);
        
        // Add chunk name for debugging
        newChunk.Name = $"EnvironmentChunk_{chunkIndex}";
        
        // Add to scene and track it
        AddChild(newChunk);
        activeChunks[chunkIndex] = newChunk;
        
        GD.Print($"Spawned chunk {chunkIndex} at position {chunkX}");
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

        // Find chunks furthest from the player and remove them
        var sortedChunks = activeChunks
            .OrderByDescending(kvp => Mathf.Abs(kvp.Key * ChunkWidth - PlayerCamera.GlobalPosition.X))
            .ToList();

        int chunksToRemove = activeChunks.Count - MaxActiveChunks;
        for (int i = 0; i < chunksToRemove; i++)
        {
            DestroyChunk(sortedChunks[i].Key);
        }
    }

    // Public method to manually trigger chunk updates (useful for debugging)
    public void ForceUpdateChunks()
    {
        if (PlayerCamera != null)
        {
            // ONLY READ camera position, never modify it
            UpdateChunks(PlayerCamera.GlobalPosition.X);
        }
    }

    // Public method to get current chunk information
    public Dictionary<string, object> GetChunkInfo()
    {
        return new Dictionary<string, object>
        {
            ["ActiveChunks"] = activeChunks.Count,
            ["CurrentChunkIndex"] = currentChunkIndex,
            ["PlayerPosition"] = PlayerCamera?.GlobalPosition ?? Vector2.Zero // READ ONLY
        };
    }

    // Clean up when the node is freed
    public override void _ExitTree()
    {
        // Clean up all active chunks
        foreach (var chunk in activeChunks.Values)
        {
            if (chunk != null && IsInstanceValid(chunk))
            {
                chunk.QueueFree();
            }
        }
        activeChunks.Clear();
    }
}