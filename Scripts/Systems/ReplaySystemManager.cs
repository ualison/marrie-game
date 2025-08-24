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
        }
        else
        {
            QueueFree();
        }
    }
    
    private void OnSceneReady()
    {
        DiscoverReplayComponents();
        InitializeReplaySystem();
        
        // Also try to discover components immediately if they weren't found
        if (_replayRecorder == null)
        {
            // Retry discovery after a short delay
            GetTree().CreateTimer(0.1f).Timeout += () => {
                if (_replayRecorder == null)
                {
                    DiscoverReplayComponents();
                    GD.Print($"ReplaySystemManager: Retry discovery - ReplayRecorder: {_replayRecorder != null}");
                }
            };
        }
    }
    
    private void DiscoverReplayComponents()
    {
        // Try to get ReplayRecorder instance
        _replayRecorder = ReplayRecorder.Instance;
        
        // If instance is null, try to find it in the scene tree
        if (_replayRecorder == null)
        {
            GD.Print("ReplaySystemManager: ReplayRecorder.Instance is null, searching scene tree...");
            var recorderNodes = GetTree().GetNodesInGroup("replay_recorder");
            if (recorderNodes.Count > 0)
            {
                _replayRecorder = recorderNodes[0] as ReplayRecorder;
                GD.Print($"ReplaySystemManager: Found ReplayRecorder in scene tree: {_replayRecorder != null}");
            }
            
            // Last resort: search all nodes
            if (_replayRecorder == null)
            {
                var allNodes = GetTree().GetNodesInGroup("replay");
                foreach (Node node in allNodes)
                {
                    if (node is ReplayRecorder recorder)
                    {
                        _replayRecorder = recorder;
                        GD.Print("ReplaySystemManager: Found ReplayRecorder via comprehensive search");
                        break;
                    }
                }
            }
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
                GD.Print($"ReplaySystemManager: Checking path: {path}");
                if (ResourceLoader.Exists(path))
                {
                    ReplayGemScene = GD.Load<PackedScene>(path);
                    if (ReplayGemScene != null)
                    {
                        GD.Print($"ReplaySystemManager: Successfully loaded ReplayGem scene from {path}");
                        break;
                    }
                    else
                    {
                        GD.PrintErr($"ReplaySystemManager: Path exists but failed to load as PackedScene: {path}");
                    }
                }
                else
                {
                    GD.Print($"ReplaySystemManager: Path does not exist: {path}");
                }
            }
            
            if (ReplayGemScene == null)
            {
                GD.PrintErr("ReplaySystemManager: CRITICAL - Could not find ReplayGem.tscn in any expected location");
                GD.Print("ReplaySystemManager: Checked paths: " + string.Join(", ", possiblePaths));
            }
        }
        
        GD.Print($"ReplaySystemManager: ReplayRecorder found: {_replayRecorder != null}, ReplayGem scene: {ReplayGemScene != null}");
    }
    
    private void InitializeReplaySystem()
    {
        if (_replayRecorder != null)
        {
            _replayRecorder.StartRecording();
            GD.Print("ReplaySystemManager: Started recording");
        }
    }
    
    private void OnPlayerDied(Vector2 deathPosition, int remainingHealth)
    {
        GD.Print($"ReplaySystemManager: Player died - about to capture frames BEFORE stopping recording");
        
        // Ensure we have a valid recorder reference
        if (_replayRecorder == null)
        {
            GD.Print("ReplaySystemManager: _replayRecorder is null, attempting immediate discovery...");
            DiscoverReplayComponents();
        }
        
        // Check frame counts before capturing
        if (_replayRecorder != null)
        {
            var stats = _replayRecorder.GetRecordingStats();
            GD.Print($"ReplaySystemManager: Recording stats before capture - Player: {stats.playerFrames}, Monster: {stats.monsterFrames}");
        }
        else
        {
            GD.PrintErr("ReplaySystemManager: Still no ReplayRecorder found - cannot capture frames!");
        }
        
        // IMPORTANT: Store death record BEFORE stopping recording to capture frames
        StoreDeathRecord(deathPosition);
        
        // Now stop recording after we've captured the frames
        StopRecording();
        
        // DON'T spawn replay gem immediately - only after restart
        GD.Print($"ReplaySystemManager: Player died at {deathPosition}, gem will appear after restart");
    }
    
    private void StoreDeathRecord(Vector2 deathPosition)
    {
        // Create and store death record
        _currentDeathId = System.Guid.NewGuid().ToString();
        var deathRecord = new DeathRecord(_currentDeathId, deathPosition);
        
        // Get replay frames from recorder
        if (_replayRecorder != null)
        {
            GD.Print("ReplaySystemManager: About to get frames from recorder...");
            var playerFrames = _replayRecorder.GetPlayerFrames();
            var monsterFrames = _replayRecorder.GetMonsterFrames();
            
            GD.Print($"ReplaySystemManager: Retrieved {playerFrames?.Count ?? 0} player frames and {monsterFrames?.Count ?? 0} monster frames");
            
            deathRecord.PlayerFrames = playerFrames ?? new System.Collections.Generic.List<ReplayFrame>();
            deathRecord.MonsterFrames = monsterFrames ?? new System.Collections.Generic.List<ReplayFrame>();
            
            GD.Print($"ReplaySystemManager: Assigned frames to death record - Player: {deathRecord.PlayerFrames.Count}, Monster: {deathRecord.MonsterFrames.Count}");
        }
        else
        {
            GD.PrintErr("ReplaySystemManager: _replayRecorder is null!");
            deathRecord.PlayerFrames = new System.Collections.Generic.List<ReplayFrame>();
            deathRecord.MonsterFrames = new System.Collections.Generic.List<ReplayFrame>();
        }
        
        _deathRecords[_currentDeathId] = deathRecord;
        GD.Print($"ReplaySystemManager: Stored death record {_currentDeathId}");
    }
    
    public DeathRecord GetLastDeathRecord()
    {
        // Retrieve most recent death record for replay
        if (!string.IsNullOrEmpty(_currentDeathId) && _deathRecords.ContainsKey(_currentDeathId))
        {
            var record = _deathRecords[_currentDeathId];
            GD.Print($"ReplaySystemManager: Found death record {_currentDeathId} with {record.PlayerFrames?.Count ?? 0} player frames and {record.MonsterFrames?.Count ?? 0} monster frames");
            return record;
        }
        
        // Fallback: try to get the most recent death record if currentDeathId is lost
        if (_deathRecords.Count > 0)
        {
            var lastRecord = _deathRecords.Values.LastOrDefault();
            if (lastRecord != null)
            {
                GD.Print($"ReplaySystemManager: Using fallback - found death record with {lastRecord.PlayerFrames?.Count ?? 0} player frames");
                return lastRecord;
            }
        }
        
        GD.PrintErr($"ReplaySystemManager: No death record found - _currentDeathId: '{_currentDeathId}', total records: {_deathRecords.Count}");
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
                GD.Print($"ReplaySystemManager: Spawned replay gem at {position}");
            }
            else
            {
                GD.PrintErr("ReplaySystemManager: Failed to create replay gem from scene!");
            }
        }
        else
        {
            GD.PrintErr("ReplaySystemManager: ReplayGem scene could not be loaded - this should not happen!");
        }
    }
    
    private void RemoveCurrentReplayGem()
    {
        if (_currentReplayGem != null && IsInstanceValid(_currentReplayGem))
        {
            _currentReplayGem.QueueFree();
            _currentReplayGem = null;
            GD.Print("ReplaySystemManager: Removed previous replay gem");
        }
    }
    
    private void StopRecording()
    {
        if (_replayRecorder != null)
        {
            _replayRecorder.StopRecording();
            GD.Print("ReplaySystemManager: Stopped recording");
        }
    }
    
    private void OnGameRestart()
    {
        GD.Print("ReplaySystemManager: Game restarting");
        
        // Store the last death position for respawning gem after restart
        Vector2 lastDeathPosition = Vector2.Zero;
        if (!string.IsNullOrEmpty(_currentDeathId) && _deathRecords.ContainsKey(_currentDeathId))
        {
            lastDeathPosition = _deathRecords[_currentDeathId].DeathPosition;
            GD.Print($"ReplaySystemManager: Storing last death position: {lastDeathPosition}");
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
                GD.Print($"ReplaySystemManager: Respawned replay gem at {lastDeathPosition} after restart");
            };
        }
        
        // Restart recording
        if (_replayRecorder != null)
        {
            _replayRecorder.StartRecording();
            GD.Print("ReplaySystemManager: Restarted recording for new game");
        }
    }
    
    public void StartReplay()
    {
        // Note: Replay is actually initiated through ReplayGem interaction
        // The ReplayRecorder provides frame data, but doesn't control playback
        if (_replayRecorder != null && _replayRecorder.HasRecordedData)
        {
            GD.Print("ReplaySystemManager: Replay data available");
            RemoveCurrentReplayGem();
        }
        else
        {
            GD.PrintErr("ReplaySystemManager: Cannot start replay - no recorded data!");
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
