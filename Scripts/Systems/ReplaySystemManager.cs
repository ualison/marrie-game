using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages replay system coordination and replay gem spawning
/// Single Responsibility: Replay System Management
/// </summary>
public partial class ReplaySystemManager : Node
{
    public static ReplaySystemManager Instance { get; private set; }
    
    #region Replay Components
    private ReplayRecorder _replayRecorder;
    #endregion
    
    #region Replay Gem Management
    [Export] public PackedScene ReplayGemScene;
    private ReplayGem _currentReplayGem;
    #endregion
    
    #region Death Records
    private Dictionary<string, DeathRecord> _deathRecords = new();
    private string _currentDeathId = "";
    #endregion
    
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            
            // Listen to scene changes and player events
            SceneLifecycleManager.SceneReady += OnSceneReady;
            PlayerStateManager.PlayerDied += OnPlayerDied;
            
            // Listen to game events
            GameEvents.GameRestart += OnGameRestart;
            
            // Also try immediate discovery as a fallback
            CallDeferred(nameof(InitializeDirectly));
        }
        else
        {
            QueueFree();
        }
    }
    
    private void InitializeDirectly()
    {
        // Fallback initialization in case SceneReady doesn't fire
        if (_replayRecorder == null)
        {
            Log.System("Attempting direct initialization as fallback", "ReplaySystemManager");
            DiscoverReplayComponents();
            InitializeReplaySystem();
        }
    }
    
    private void OnSceneReady()
    {
        DiscoverReplayComponents();
        InitializeReplaySystem();
        
        // Also try to discover components immediately if they weren't found
        if (_replayRecorder == null)
        {
            // Retry discovery after a short delay with multiple attempts
            RetryDiscovery(1);
        }
    }
    
    private void RetryDiscovery(int attemptNumber)
    {
        const int maxAttempts = 5;
        const float retryDelay = 0.2f;
        
        if (attemptNumber > maxAttempts)
        {
            Log.Error($"Failed to discover ReplayRecorder after {maxAttempts} attempts!", "ReplaySystemManager");
            return;
        }
        
        GetTree().CreateTimer(retryDelay * attemptNumber).Timeout += () => {
            if (_replayRecorder == null)
            {
                DiscoverReplayComponents();
                Log.System($"Retry discovery attempt {attemptNumber} - ReplayRecorder: {_replayRecorder != null}", "ReplaySystemManager");
                
                if (_replayRecorder == null)
                {
                    RetryDiscovery(attemptNumber + 1);
                }
                else
                {
                    Log.System($"Successfully discovered ReplayRecorder on attempt {attemptNumber}", "ReplaySystemManager");
                    InitializeReplaySystem();
                }
            }
        };
    }
    
    private void DiscoverReplayComponents()
    {
        // Try to get ReplayRecorder instance
        _replayRecorder = ReplayRecorder.Instance;
        
        // If instance is null, try to find it in the scene tree
        if (_replayRecorder == null)
        {
            Log.System("ReplayRecorder.Instance is null, searching scene tree...", "ReplaySystemManager");
            
            // First try the specific group
            var recorderNodes = GetTree().GetNodesInGroup("replay_recorder");
            if (recorderNodes.Count > 0)
            {
                _replayRecorder = recorderNodes[0] as ReplayRecorder;
                Log.System($"Found ReplayRecorder in replay_recorder group: {_replayRecorder != null}", "ReplaySystemManager");
            }
            
            // If still not found, try broader search
            if (_replayRecorder == null)
            {
                var allNodes = GetTree().GetNodesInGroup("replay");
                foreach (Node node in allNodes)
                {
                    if (node is ReplayRecorder recorder)
                    {
                        _replayRecorder = recorder;
                        Log.System("Found ReplayRecorder via replay group search", "ReplaySystemManager");
                        break;
                    }
                }
            }
            
            // Last resort: search all nodes by type
            if (_replayRecorder == null)
            {
                _replayRecorder = GetTree().GetFirstNodeInGroup("replay_recorder") as ReplayRecorder;
                if (_replayRecorder == null)
                {
                    // Manual search through scene tree
                    _replayRecorder = FindReplayRecorderInTree(GetTree().CurrentScene);
                }
                if (_replayRecorder != null)
                {
                    Log.System("Found ReplayRecorder via comprehensive tree search", "ReplaySystemManager");
                }
            }
        }
        
        // Validate the found recorder
        if (_replayRecorder != null && !IsInstanceValid(_replayRecorder))
        {
            Log.Error("Found ReplayRecorder but it's not valid!", "ReplaySystemManager");
            _replayRecorder = null;
        }
        
        // Load replay gem scene if not set - try multiple paths
        if (ReplayGemScene == null)
        {
            var possiblePaths = new[]
            {
                "res://Prefabs/ReplayGem.tscn",
                "res://prefabs/ReplayGem.tscn", 
                "res://Scenes/ReplayGem.tscn",
                "res://scenes/ReplayGem.tscn"
            };
            
            foreach (var path in possiblePaths)
            {
                if (ResourceLoader.Exists(path))
                {
                    ReplayGemScene = GD.Load<PackedScene>(path);
                    if (ReplayGemScene != null)
                    {
                        Log.System($"Successfully loaded ReplayGem scene from {path}", "ReplaySystemManager");
                        break;
                    }
                    else
                    {
                        Log.Error($"Path exists but failed to load as PackedScene: {path}", "ReplaySystemManager");
                    }
                }
            }
            
            if (ReplayGemScene == null)
            {
                Log.Critical("Could not find ReplayGem.tscn in any expected location", "ReplaySystemManager");
                Log.Debug("Checked paths: " + string.Join(", ", possiblePaths), "ReplaySystemManager");
            }
        }
        
        Log.System($"Discovery complete - ReplayRecorder: {_replayRecorder != null}, ReplayGem scene: {ReplayGemScene != null}", "ReplaySystemManager");
    }
    
    private ReplayRecorder FindReplayRecorderInTree(Node node)
    {
        if (node is ReplayRecorder recorder)
            return recorder;
            
        foreach (Node child in node.GetChildren())
        {
            var result = FindReplayRecorderInTree(child);
            if (result != null)
                return result;
        }
        
        return null;
    }
    
    private void InitializeReplaySystem()
    {
        if (_replayRecorder != null)
        {
            _replayRecorder.StartRecording();
            Log.System("Started recording", "ReplaySystemManager");
        }
    }
    
    private void OnPlayerDied(Vector2 deathPosition, int remainingHealth)
    {
        // Use new logging system with fallback
        Log.Replay($"Player died - capturing replay data at {deathPosition} (Health: {remainingHealth})", "ReplaySystemManager");
        
        // Ensure we have a valid recorder reference with more aggressive discovery
        if (_replayRecorder == null)
        {
            Log.Error("ReplayRecorder is null, attempting immediate discovery", "ReplaySystemManager");
            
            DiscoverReplayComponents();
            
            // If still null after discovery, try more aggressive searching
            if (_replayRecorder == null)
            {
                // Force a more thorough search
                var allRecorders = new System.Collections.Generic.List<ReplayRecorder>();
                FindAllNodesOfType(GetTree().CurrentScene, typeof(ReplayRecorder), allRecorders);
                
                if (allRecorders.Count > 0)
                {
                    _replayRecorder = allRecorders[0];
                    Log.System("Found ReplayRecorder through aggressive search", "ReplaySystemManager");
                }
            }
        }
        
        // Check frame counts before capturing
        if (_replayRecorder != null)
        {
            try
            {
                var stats = _replayRecorder.GetRecordingStats();
                Log.Replay($"Recording stats before capture - Player: {stats.playerFrames}, Monster: {stats.monsterFrames}", "ReplaySystemManager");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error getting recording stats: {ex.Message}", "ReplaySystemManager");
            }
        }
        else
        {
            Log.Error("Still no ReplayRecorder found - cannot capture frames!", "ReplaySystemManager");
        }
        
        // IMPORTANT: Store death record BEFORE stopping recording to capture frames
        StoreDeathRecord(deathPosition);
        
        // Now stop recording after we've captured the frames
        StopRecording();
        
        // DON'T spawn replay gem immediately - only after restart
        Log.Replay($"Player died at {deathPosition}, gem will appear after restart", "ReplaySystemManager");
    }
    
    private void FindAllNodesOfType(Node node, System.Type type, System.Collections.Generic.List<ReplayRecorder> results)
    {
        if (node.GetType() == type || node.GetType().IsSubclassOf(type))
        {
            results.Add(node as ReplayRecorder);
        }
        
        foreach (Node child in node.GetChildren())
        {
            FindAllNodesOfType(child, type, results);
        }
    }
    
    private void StoreDeathRecord(Vector2 deathPosition)
    {
        // Create and store death record
        _currentDeathId = System.Guid.NewGuid().ToString();
        var deathRecord = new DeathRecord(_currentDeathId, deathPosition);
        
        // Get replay frames from recorder
        if (_replayRecorder != null)
        {
            Log.Replay("About to get frames from recorder...", "ReplaySystemManager");
            var playerFrames = _replayRecorder.GetPlayerFrames();
            var monsterFrames = _replayRecorder.GetMonsterFrames();
            
            Log.Replay($"Retrieved {playerFrames?.Count ?? 0} player frames and {monsterFrames?.Count ?? 0} monster frames", "ReplaySystemManager");
            
            deathRecord.PlayerFrames = playerFrames ?? new System.Collections.Generic.List<ReplayFrame>();
            deathRecord.MonsterFrames = monsterFrames ?? new System.Collections.Generic.List<ReplayFrame>();
            
            Log.Replay($"Assigned frames to death record - Player: {deathRecord.PlayerFrames.Count}, Monster: {deathRecord.MonsterFrames.Count}", "ReplaySystemManager");
        }
        else
        {
            Log.Error("_replayRecorder is null!", "ReplaySystemManager");
            deathRecord.PlayerFrames = new System.Collections.Generic.List<ReplayFrame>();
            deathRecord.MonsterFrames = new System.Collections.Generic.List<ReplayFrame>();
        }
        
        _deathRecords[_currentDeathId] = deathRecord;
        Log.Replay($"Stored death record {_currentDeathId}", "ReplaySystemManager");
    }
    
    public DeathRecord GetLastDeathRecord()
    {
        // Retrieve most recent death record for replay
        if (!string.IsNullOrEmpty(_currentDeathId) && _deathRecords.ContainsKey(_currentDeathId))
        {
            var record = _deathRecords[_currentDeathId];
            Log.Replay($"Found death record {_currentDeathId} with {record.PlayerFrames?.Count ?? 0} player frames and {record.MonsterFrames?.Count ?? 0} monster frames", "ReplaySystemManager");
            return record;
        }
        
        // Fallback: try to get the most recent death record if currentDeathId is lost
        if (_deathRecords.Count > 0)
        {
            var lastRecord = _deathRecords.Values.LastOrDefault();
            if (lastRecord != null)
            {
                Log.Replay($"Using fallback - found death record with {lastRecord.PlayerFrames?.Count ?? 0} player frames", "ReplaySystemManager");
                return lastRecord;
            }
        }
        
        Log.Error($"No death record found - _currentDeathId: '{_currentDeathId}', total records: {_deathRecords.Count}", "ReplaySystemManager");
        return null;
    }
    
    private void SpawnReplayGem(Vector2 position)
    {
        // Remove existing gem if any
        RemoveCurrentReplayGem();
        
        // Always try to load the ReplayGem scene if not already loaded
        if (ReplayGemScene == null)
        {
            DiscoverReplayComponents(); // This will try to load the scene
        }
        
        if (ReplayGemScene != null)
        {
            // Create new gem using EntityFactory
            _currentReplayGem = EntityFactory.CreateReplayGem(position, GetTree().CurrentScene);
            
            if (_currentReplayGem != null)
            {
                // Ensure the gem is visible after creation
                _currentReplayGem.ShowGem(position);
                Log.System($"Spawned replay gem at {position}", "ReplaySystemManager");
            }
            else
            {
                Log.Error("Failed to create replay gem from scene!", "ReplaySystemManager");
            }
        }
        else
        {
            Log.Error("ReplayGem scene could not be loaded - this should not happen!", "ReplaySystemManager");
        }
    }
    
    private void RemoveCurrentReplayGem()
    {
        if (_currentReplayGem != null && IsInstanceValid(_currentReplayGem))
        {
            _currentReplayGem.QueueFree();
            _currentReplayGem = null;
            Log.System("Removed previous replay gem", "ReplaySystemManager");
        }
    }
    
    private void StopRecording()
    {
        if (_replayRecorder != null)
        {
            _replayRecorder.StopRecording();
            Log.System("Stopped recording", "ReplaySystemManager");
        }
    }
    
    private void OnGameRestart()
    {
        Log.System("Game restarting", "ReplaySystemManager");
        
        // Store the last death position for respawning gem after restart
        Vector2 lastDeathPosition = Vector2.Zero;
        if (!string.IsNullOrEmpty(_currentDeathId) && _deathRecords.ContainsKey(_currentDeathId))
        {
            lastDeathPosition = _deathRecords[_currentDeathId].DeathPosition;
            Log.System($"Storing last death position: {lastDeathPosition}", "ReplaySystemManager");
        }
        
        // Remove current replay gem
        RemoveCurrentReplayGem();
        
        // DON'T clear _currentDeathId - we need it for replay functionality!
        // The death record should remain accessible for replay
        
        // Respawn the replay gem at the death position if we have one
        if (lastDeathPosition != Vector2.Zero)
        {
            // Wait a bit for the scene to settle after restart
            GetTree().CreateTimer(0.5f).Timeout += () => {
                SpawnReplayGem(lastDeathPosition);
                Log.System($"Respawned replay gem at {lastDeathPosition} after restart", "ReplaySystemManager");
            };
        }
        
        // Restart recording
        if (_replayRecorder != null)
        {
            _replayRecorder.StartRecording();
            Log.System("Restarted recording for new game", "ReplaySystemManager");
        }
    }
    
    public void StartReplay()
    {
        // Note: Replay is actually initiated through ReplayGem interaction
        // The ReplayRecorder provides frame data, but doesn't control playback
        if (_replayRecorder != null && _replayRecorder.HasRecordedData)
        {
            Log.System("Replay data available", "ReplaySystemManager");
            RemoveCurrentReplayGem();
        }
        else
        {
            Log.Error("Cannot start replay - no recorded data!", "ReplaySystemManager");
        }
    }
    
    public ReplayRecorder GetReplayRecorder() => _replayRecorder;
    public ReplayGem GetCurrentReplayGem() => _currentReplayGem;
    
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            SceneLifecycleManager.SceneReady -= OnSceneReady;
            PlayerStateManager.PlayerDied -= OnPlayerDied;
            GameEvents.GameRestart -= OnGameRestart;
            Instance = null;
        }
    }
}
