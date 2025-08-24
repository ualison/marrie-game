using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Simplified replay system manager
/// Focused on essential replay functionality without over-engineering
/// </summary>
public partial class ReplaySystemManager : Node
{
    #region Singleton
    public static ReplaySystemManager Instance { get; private set; }
    #endregion

    #region Components
    private ReplayRecorder _recorder;
    private ReplayGem _currentGem;
    #endregion

    #region Death Records
    private readonly Dictionary<string, DeathRecord> _deathRecords = new();
    private string _currentDeathId = "";
    #endregion

    #region Lifecycle
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            
            // Connect to events
            PlayerStateManager.PlayerDied += OnPlayerDied;
            GameEventSystem.GameRestart += OnGameRestart;
            
            // Initialize
            CallDeferred(nameof(Initialize));
        }
        else
        {
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        // Cleanup events
        if (Instance == this)
        {
            Instance = null;
            PlayerStateManager.PlayerDied -= OnPlayerDied;
            GameEventSystem.GameRestart -= OnGameRestart;
        }
    }
    #endregion

    #region Initialization (Simplified)
    private void Initialize()
    {
        // Find or create recorder
        _recorder = ReplayRecorder.Instance;
        
        if (_recorder == null)
        {
            // Create recorder if it doesn't exist
            _recorder = new ReplayRecorder { Name = "ReplayRecorder" };
            GetTree().CurrentScene.AddChild(_recorder);
            Log.System("Created ReplayRecorder", "ReplaySystemManager");
        }
        
        Log.System("ReplaySystemManager initialized", "ReplaySystemManager");
    }
    #endregion

    #region Event Handlers
    private void OnPlayerDied(Vector2 deathPosition, int health)
    {
        // Check if we have current recording data
        bool hasCurrentRecording = _recorder?.HasRecordedData ?? false;
        
        if (!hasCurrentRecording)
        {
            Log.Warning("No replay data available for death (no current recording)", "ReplaySystemManager");
            return;
        }

        // Always stop current recording and save death record
        _recorder.StopRecording();
        SaveDeathRecord(deathPosition);
        
        Log.Replay($"Player death recorded at {deathPosition} (Total deaths: {_deathRecords.Count})", "ReplaySystemManager");
    }

    private void OnGameRestart()
    {
        // Clean up current replay visuals and gems
        CleanupCurrentReplay();
        
        Log.Debug($"OnGameRestart - Before: Current death ID: '{_currentDeathId}', Total deaths: {_deathRecords.Count}", "ReplaySystemManager");
        
        // Find the most recent death record by timestamp
        DeathRecord lastDeath = null;
        if (_deathRecords.Count > 0)
        {
            lastDeath = _deathRecords.Values
                .OrderByDescending(record => record.DeathTime)
                .FirstOrDefault();
        }
        
        // Set current death ID to the most recent death for ReplayGem usage
        if (lastDeath != null)
        {
            _currentDeathId = lastDeath.DeathId;
            Log.Debug($"Set current death ID to: {_currentDeathId} (DeathTime: {lastDeath.DeathTime})", "ReplaySystemManager");
        }
        else
        {
            _currentDeathId = "";
        }
        
        // Restart recording for new session
        if (_recorder != null)
        {
            _recorder.RefreshCharacterCache();
            _recorder.ClearRecording(); // Clear current recording data
            _recorder.StartRecording(); // Start fresh recording
        }
        
        // Create ReplayGem at beginning of new session if we have previous deaths
        if (lastDeath != null)
        {
            Log.Debug($"Creating ReplayGem at death position: {lastDeath.DeathPosition}", "ReplaySystemManager");
            CreateReplayGem(lastDeath.DeathPosition);
            Log.Replay($"ReplayGem created at game start from previous death at {lastDeath.DeathPosition}", "ReplaySystemManager");
        }
        
        Log.Replay($"Game restarted - recorder reset, death records preserved ({_deathRecords.Count} deaths)", "ReplaySystemManager");
    }
    #endregion

    #region Death Record Management
    private void SaveDeathRecord(Vector2 deathPosition)
    {
        _currentDeathId = GenerateDeathId();
        var record = new DeathRecord(_currentDeathId, deathPosition, _deathRecords.Count)
        {
            PlayerFrames = _recorder.GetPlayerFrames(),
            MonsterFrames = _recorder.GetMonsterFrames()
        };

        _deathRecords[_currentDeathId] = record;

        var stats = _recorder.GetStats();
        Log.Debug($"SaveDeathRecord - New death ID: {_currentDeathId} at {deathPosition}, DeathTime: {record.DeathTime}, DeathSequence: {record.DeathSequence}, Total deaths: {_deathRecords.Count}", "ReplaySystemManager");
        Log.Replay($"Death record saved: {record.TotalFrames} frames, {stats.duration:F1}s duration at position {deathPosition}", "ReplaySystemManager");
    }    private string GenerateDeathId()
    {
        return $"death_{Time.GetUnixTimeFromSystem()}_{_deathRecords.Count}";
    }
    #endregion

    #region Replay Gem Management
    private void CreateReplayGem(Vector2 position)
    {
        // Remove existing gem
        CleanupCurrentGem();
        
        // Defer creation to avoid physics flush conflicts
        CallDeferred(nameof(CreateReplayGemDeferred), position);
    }
    
    private void CreateReplayGemDeferred(Vector2 position)
    {
        // Create new gem using factory
        _currentGem = ReplayEntityFactory.CreateReplayGem(position, GetTree().CurrentScene);
        
        if (_currentGem != null)
        {
            Log.Replay($"Replay gem created at {position}", "ReplaySystemManager");
        }
        else
        {
            Log.Error("Failed to create replay gem", "ReplaySystemManager");
        }
    }

    private void CleanupCurrentGem()
    {
        if (_currentGem != null)
        {
            if (IsInstanceValid(_currentGem))
            {
                _currentGem.QueueFree();
            }
            _currentGem = null;
        }
    }
    #endregion

    #region Replay Playback
    private void StartReplay(DeathRecord record)
    {
        Log.Replay($"Starting replay: {record}", "ReplaySystemManager");
        
        // Pause current recording
        if (_recorder != null)
        {
            _recorder.PauseRecording();
        }
        
        // Pause player
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        player?.PausePlayer();
        
        // Create and start ghost replays
        if (record.IsPlayerDataAvailable())
        {
            CreateGhostPlayer(record);
        }
        
        if (record.IsMonsterDataAvailable())
        {
            CreateGhostMonster(record);
        }
        
        // Set timer to end replay
        var replayDuration = record.RecordingDuration + 1f; // Add buffer
        GetTree().CreateTimer(replayDuration).Timeout += EndReplay;
    }

    private void CreateGhostPlayer(DeathRecord record)
    {
        var ghost = ReplayEntityFactory.CreateGhostPlayer(record.DeathPosition, GetTree().CurrentScene);
        if (ghost != null)
        {
            ghost.StartReplay(record.PlayerFrames);
            Log.Replay("Ghost player started", "ReplaySystemManager");
        }
    }

    private void CreateGhostMonster(DeathRecord record)
    {
        var ghost = ReplayEntityFactory.CreateGhostMonster(record.DeathPosition, GetTree().CurrentScene);
        if (ghost != null)
        {
            ghost.StartReplay(record.MonsterFrames);
            Log.Replay("Ghost monster started", "ReplaySystemManager");
        }
    }

    private void EndReplay()
    {
        // Resume player
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        player?.ResumePlayer();
        
        // Resume recording
        if (_recorder != null)
        {
            _recorder.ResumeRecording();
        }
        
        // Clean up ghosts (they should auto-cleanup when replay ends)
        var ghosts = GetTree().GetNodesInGroup("ghost_player");
        ghosts.AddRange(GetTree().GetNodesInGroup("ghost_monster"));
        
        foreach (Node ghost in ghosts)
        {
            if (IsInstanceValid(ghost))
            {
                ghost.QueueFree();
            }
        }
        
        Log.Replay("Replay ended", "ReplaySystemManager");
    }

    private void CleanupCurrentReplay()
    {
        CleanupCurrentGem();
        
        // Stop any ongoing replay
        var ghosts = GetTree().GetNodesInGroup("ghost_player");
        ghosts.AddRange(GetTree().GetNodesInGroup("ghost_monster"));
        
        foreach (Node ghost in ghosts)
        {
            if (IsInstanceValid(ghost))
            {
                ghost.QueueFree();
            }
        }
    }
    #endregion

    #region Public API (for ReplayGem to call)
    public bool HasReplayData()
    {
        // Only return true if we have previous death records to replay
        // Current recording data alone doesn't constitute "replayable" data until after death
        return _deathRecords.Count > 0;
    }

    public (int deaths, int totalFrames) GetReplayStats()
    {
        int totalFrames = 0;
        foreach (var record in _deathRecords.Values)
        {
            totalFrames += record.TotalFrames;
        }
        
        return (_deathRecords.Count, totalFrames);
    }

    public DeathRecord GetLastDeathRecord()
    {
        Log.Debug($"GetLastDeathRecord called - Current death ID: '{_currentDeathId}', Total deaths: {_deathRecords.Count}", "ReplaySystemManager");
        
        // Debug: List all death records
        foreach (var kvp in _deathRecords)
        {
            Log.Debug($"  Death record: {kvp.Key} at {kvp.Value.DeathPosition}, DeathTime: {kvp.Value.DeathTime}, Sequence: {kvp.Value.DeathSequence}", "ReplaySystemManager");
        }
        
        // FIXED: Use death sequence to break timestamp ties, ensuring latest death is selected
        if (_deathRecords.Count > 0)
        {
            var latestRecord = _deathRecords.Values
                .OrderByDescending(record => record.DeathTime)
                .ThenByDescending(record => record.DeathSequence) // Break ties with sequence number
                .FirstOrDefault();
            
            if (latestRecord != null)
            {
                Log.Debug($"Using latest death record: {latestRecord.DeathId} at {latestRecord.DeathPosition} (sequence: {latestRecord.DeathSequence} from {_deathRecords.Count} total deaths)", "ReplaySystemManager");
                return latestRecord;
            }
        }
        
        Log.Warning("No death records available", "ReplaySystemManager");
        return null;
    }

    public void StartReplayFromGem(DeathRecord record)
    {
        if (record == null)
        {
            Log.Warning("Cannot start replay - no death record provided", "ReplaySystemManager");
            return;
        }

        StartReplay(record);
    }
    #endregion
}
