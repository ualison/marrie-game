using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Performance-optimized replay recording system with efficient character tracking and data extraction
/// </summary>
public partial class ReplayRecorder : Node
{
    #region Singleton
    public static ReplayRecorder Instance { get; private set; }
    #endregion

    #region Exported Configuration
    [Export] public float RecordInterval { get; set; } = 0.1f;
    [Export] public string PlayerGroupName { get; set; } = "player";
    [Export] public string EnemyGroupName { get; set; } = "enemies";
    [Export] public bool EnableOptimizations { get; set; } = true;
    #endregion

    #region Properties
    public bool IsRecording { get; private set; }
    public bool HasRecordedData => _playerFrames.Count > 0 || _monsterFrames.Count > 0;
    #endregion

    #region Private State
    private bool _isPausedForReplay;
    private float _recordTimer;
    private float _elapsedRecordingTime;

    private readonly List<ReplayFrame> _playerFrames = new();
    private readonly List<ReplayFrame> _monsterFrames = new();

    // Cached references for performance
    private Player _player;
    private Monster _monster;

    // Performance optimization tracking
    private Vector2 _lastPlayerPosition;
    private bool _playerWasMoving = true;
    private int _frameSkipCounter = 0;
    private const float MIN_MOVEMENT_THRESHOLD = 0.5f;
    
    // Discovery retry system
    private SceneTreeTimer _discoveryTimer;
    #endregion

    #region Godot Lifecycle
    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        
        // Add to group for discovery
        AddToGroup("replay_recorder");
        
        // Use CallDeferred for proper initialization order
        CallDeferred(MethodName.InitializeRecording);
    }

    public override void _Process(double delta)
    {
        if (!IsRecording || _isPausedForReplay) 
            return;

        var deltaFloat = (float)delta;
        _recordTimer += deltaFloat;
        _elapsedRecordingTime += deltaFloat;

        if (_recordTimer >= RecordInterval)
        {
            if (ShouldCaptureFrame())
            {
                CaptureCurrentFrame();
            }
            _recordTimer = 0.0f;
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        // Clear cache on exit
        CharacterDataExtractor.ClearCache();
    }
    #endregion

    #region Initialization
    public void InitializeRecording()
    {
        DiscoverCharacters();

        if (_player != null)
        {
            StartRecording();
        }
        else
        {
            // Retry character discovery
            ScheduleDiscoveryRetry();
        }
    }

    private void ScheduleDiscoveryRetry()
    {
        // SceneTreeTimer doesn't have Kill method, just create new one
        _discoveryTimer = GetTree().CreateTimer(0.1f);
        _discoveryTimer.Timeout += InitializeRecording;
    }

    private void DiscoverCharacters()
    {
        // Use more specific discovery with null-conditional operators
        _player = GetTree().GetFirstNodeInGroup(PlayerGroupName) as Player;
        
        // Find Monster with follower behavior enabled
        var enemies = GetTree().GetNodesInGroup(EnemyGroupName);
        _monster = enemies.OfType<Monster>().FirstOrDefault(m => m.EnableFollowerBehavior) ?? 
                   enemies.OfType<Monster>().FirstOrDefault();

        GD.Print($"ReplayRecorder discovered - Player: {_player != null}, " +
                 $"Monster: {_monster != null}, FollowerBehavior: {_monster?.EnableFollowerBehavior}");
    }
    #endregion

    #region Recording Control
    public void StartRecording()
    {
        ClearRecording();
        IsRecording = true;
        _recordTimer = 0f;
        _elapsedRecordingTime = 0f;
        
        // Initialize movement tracking for optimizations
        if (_player != null)
        {
            _lastPlayerPosition = _player.GlobalPosition;
            _playerWasMoving = true;
        }
        
        GD.Print($"ReplayRecorder: Started recording - Player: {_player != null}");
    }

    public void StopRecording()
    {
        IsRecording = false;
        GD.Print($"Recording stopped. Player: {_playerFrames.Count}, Monster: {_monsterFrames.Count} frames");
    }

    public void ClearRecording()
    {
        _playerFrames.Clear();
        _monsterFrames.Clear();
        _recordTimer = 0f;
        _elapsedRecordingTime = 0f;
        _frameSkipCounter = 0;
        CharacterDataExtractor.ClearCache();
    }

    public void PauseRecording()
    {
        _isPausedForReplay = true;
        GD.Print("Recording paused for replay");
    }

    public void ResumeRecording()
    {
        _isPausedForReplay = false;
        GD.Print("Recording resumed");
    }
    #endregion

    #region Frame Capture with Optimizations
    private bool ShouldCaptureFrame()
    {
        if (!EnableOptimizations) return true;

        // Check if player moved significantly
        if (_player != null)
        {
            var currentPos = _player.GlobalPosition;
            var distance = currentPos.DistanceTo(_lastPlayerPosition);
            
            if (distance < MIN_MOVEMENT_THRESHOLD)
            {
                // Skip frames when not moving, but capture every 10th frame for safety
                _frameSkipCounter++;
                if (_frameSkipCounter < 10)
                {
                    return false;
                }
                _frameSkipCounter = 0;
            }
            else
            {
                _lastPlayerPosition = currentPos;
                _frameSkipCounter = 0;
            }
        }

        return true;
    }

    private void CaptureCurrentFrame()
    {
        var currentTime = _elapsedRecordingTime;

        // Record player state using optimized extractor
        if (_player != null)
        {
            var frame = CharacterDataExtractor.ExtractFrame(_player, currentTime);
            _playerFrames.Add(frame);
        }

        // Record monster state
        if (_monster != null)
        {
            var frame = CharacterDataExtractor.ExtractFrame(_monster, currentTime);
            _monsterFrames.Add(frame);
        }
    }
    #endregion

    #region Data Access
    /// <summary>
    /// Returns a copy of recorded player frames
    /// </summary>
    public List<ReplayFrame> GetPlayerFrames() => new(_playerFrames);

    /// <summary>
    /// Returns a copy of recorded monster frames  
    /// </summary>
    public List<ReplayFrame> GetMonsterFrames() => new(_monsterFrames);

    /// <summary>
    /// Forces character rediscovery (useful after scene changes)
    /// </summary>
    public void RefreshCharacterReferences()
    {
        DiscoverCharacters();
        CharacterDataExtractor.ClearCache();
    }

    /// <summary>
    /// Gets recording statistics for debugging
    /// </summary>
    public (int playerFrames, int monsterFrames, float recordingTime, int cacheSize) GetRecordingStats()
    {
        return (_playerFrames.Count, _monsterFrames.Count, _elapsedRecordingTime, CharacterDataExtractor.GetCacheSize());
    }
    #endregion
}