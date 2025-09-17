using Godot;
using MarrieGame.Core.Enums;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Simplified replay system manager
/// Focused on essential replay functionality without over-engineering
/// </summary>
public partial class ReplaySystemManager : Node
{
    private EventSubscriptionHelper _subscriptionHelper = new();
    #region Singleton
    public static ReplaySystemManager Instance { get; private set; }
    #endregion

    #region Components
    private ReplayRecorder _recorder;
    private ReplayGem _currentGem;
    private ReplayOrchestrator _orchestrator;
    #endregion

    #region Death Records
    private readonly Dictionary<string, DeathRecord> _deathRecords = new();
    private string _currentDeathId = "";
    private ReplayRecordingMode _currentRecordingMode = ReplayRecordingMode.DeathMode;
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
            _subscriptionHelper.AddUnsubscriber(() => PlayerStateManager.PlayerDied -= OnPlayerDied);

            GameEventSystem.GameRestart += OnGameRestart;
            _subscriptionHelper.AddUnsubscriber(() => GameEventSystem.GameRestart -= OnGameRestart);
            
            // Initialize for current scene
            CallDeferred(nameof(Initialize));

            // Listen for scene changes to reinitialize recorder/orchestrator and reset mode
            SceneLifecycleManager.SceneReady += OnSceneReady;
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
            // Centralized cleanup
            _subscriptionHelper.UnsubscribeAll();
            SceneLifecycleManager.SceneReady -= OnSceneReady;
        }
    }
    #endregion

    #region Initialization (Simplified)
    private void DetectRecordingMode()
    {
        if (GameManager.Instance != null)
        {
            _currentRecordingMode = GameManager.Instance.GetReplayRecordingMode();
        }
        else
        {
            _currentRecordingMode = ReplayRecordingMode.DeathMode; // Default fallback
        }
    }

    private void Initialize()
    {
        // Detect current recording mode
        DetectRecordingMode();

        // Find or create recorder
        _recorder = ReplayRecorder.Instance;
        
        if (_recorder == null)
        {
            // Create recorder if it doesn't exist
            _recorder = new ReplayRecorder { Name = "ReplayRecorder" };
            GetTree().CurrentScene.AddChild(_recorder);
            // Debug level - component creation is routine
            Log.Debug("Created ReplayRecorder", "ReplaySystemManager");
        }

        // Find or create orchestrator
        _orchestrator = ReplayOrchestrator.Instance;
        
        if (_orchestrator == null)
        {
            // Create orchestrator if it doesn't exist
            _orchestrator = new ReplayOrchestrator { Name = "ReplayOrchestrator" };
            GetTree().CurrentScene.AddChild(_orchestrator);
            // Debug level - component creation is routine
            Log.Debug("Created ReplayOrchestrator", "ReplaySystemManager");
        }
        
        // Debug level - system initialization is routine
        Log.Debug($"ReplaySystemManager initialized - Mode: {_currentRecordingMode}", "ReplaySystemManager");
    }
    #endregion

    #region Scene Lifecycle Integration
    private void OnSceneReady()
    {
        // Ensure reality is reset when entering a new scene
        if (RealityManager.IsInstanceAvailable)
        {
            RealityManager.Instance.Reset();
        }

        // Recreate or discover components under the new current scene
        Initialize();

        // Clear any old death records bound to previous scene context
        _deathRecords.Clear();
        _currentDeathId = string.Empty;

        // Reset recorder for the new scene and start fresh recording with correct mode
        if (_recorder != null && IsInstanceValid(_recorder))
        {
            // Recreate the recorder to force mode re-detection based on the new scene
            _recorder.QueueFree();
            _recorder = null;
        }

        if (_recorder == null)
        {
            _recorder = new ReplayRecorder { Name = "ReplayRecorder" };
            GetTree().CurrentScene.AddChild(_recorder);
        }

        Log.Debug("ReplaySystemManager reset after scene change", "ReplaySystemManager");
    }
    #endregion

    #region Event Handlers
    private void OnPlayerDied(MarrieGame.Core.Models.PlayerDeath death)
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
    SaveDeathRecord(death.Position);

    // Important event - player deaths should be tracked for debugging
    Log.Info($"Player death recorded at {death.Position} (Total deaths: {_deathRecords.Count})", "ReplaySystemManager");
    }

    private void OnGameRestart()
    {
        // Clean up current replay visuals and gems
        CleanupCurrentGem();
        
        // Stop any ongoing replays through orchestrator
        if (_orchestrator != null)
        {
            _orchestrator.StopReplay();
        }
        
        // Use consistent method to get the most recent death record
        DeathRecord lastDeath = GetLastDeathRecord();
        
        // Set current death ID to the most recent death for ReplayGem usage
        if (lastDeath != null)
        {
            _currentDeathId = lastDeath.DeathId;
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
        
        // Always create gem on game restart if we have previous deaths
        // This ensures gem appears only on next reload, not during GameOver UI
        if (lastDeath != null)
        {
            // Clean up any existing gem first (should not exist, but safety check)
            CleanupCurrentGem();
            
            CreateReplayGem(lastDeath.DeathPosition);
            Log.Replay($"ReplayGem created on game restart at LAST death position {lastDeath.DeathPosition}", "ReplaySystemManager");
        }
        
        Log.Replay($"Game restarted - recorder reset, death records preserved ({_deathRecords.Count} deaths)", "ReplaySystemManager");
    }
    #endregion

    #region Death Record Management
    private void SaveDeathRecord(Vector2 deathPosition)
    {
        _currentDeathId = GenerateDeathId();
        // FIXED: Use _deathRecords.Count as the next sequence (0, 1, 2, etc.)
        var record = new DeathRecord(_currentDeathId, deathPosition, _deathRecords.Count)
        {
            PlayerFrames = _recorder.GetPlayerFrames(),
            MonsterFrames = _recorder.GetMonsterFrames()
        };

        _deathRecords[_currentDeathId] = record;

        var stats = _recorder.GetStats();
        Log.Replay($"Death record saved: {record.TotalFrames} frames, {stats.duration:F1}s duration at position {deathPosition}", "ReplaySystemManager");
    }

    private string GenerateDeathId()
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

    #region Public API (for ReplayGem to call)
    public bool HasReplayData()
    {
        // In PuzzleMode, check if we have current recording data even without death records
        if (_currentRecordingMode == ReplayRecordingMode.PuzzleMode)
        {
            return (_recorder?.HasRecordedData ?? false) || _deathRecords.Count > 0;
        }
        
        // In DeathMode, only return true if we have previous death records to replay
        return _deathRecords.Count > 0;
    }

    /// <summary>
    /// Restarts recording after a replay in PuzzleMode
    /// </summary>
    public void RestartRecordingAfterPuzzleReplay()
    {
        if (_currentRecordingMode == ReplayRecordingMode.PuzzleMode && _recorder != null)
        {
            Log.Debug("Restarting recording after PuzzleMode replay", "ReplaySystemManager");
            _recorder.ClearRecording();
            _recorder.StartRecording();
        }
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
        // In PuzzleMode, create a virtual death record from current recording data if no real deaths exist
        if (_currentRecordingMode == ReplayRecordingMode.PuzzleMode && _deathRecords.Count == 0)
        {
            if (_recorder != null && _recorder.HasRecordedData)
            {
                var (playerFrames, monsterFrames, duration) = _recorder.GetCurrentRecordingData();
                
                if (playerFrames.Count > 0)
                {
                    // Create a virtual death record for puzzle mode
                    var virtualRecord = new DeathRecord("puzzle_session", Vector2.Zero, 0)
                    {
                        PlayerFrames = playerFrames,
                        MonsterFrames = monsterFrames
                    };
                    
                    Log.Debug($"Created virtual death record for PuzzleMode with {playerFrames.Count} player frames", "ReplaySystemManager");
                    return virtualRecord;
                }
            }
            
            // In puzzle mode, it's normal to have no death records initially
            Log.Debug("No recording data available for PuzzleMode replay", "ReplaySystemManager");
            return null;
        }

        // Use death sequence to break timestamp ties, ensuring latest death is selected
        if (_deathRecords.Count > 0)
        {
            var latestRecord = _deathRecords.Values
                .OrderByDescending(record => record.DeathTime)
                .ThenByDescending(record => record.DeathSequence) // Break ties with sequence number
                .FirstOrDefault();
            
            return latestRecord;
        }
        
        // Only show warning in DeathMode, as PuzzleMode is expected to not have death records initially
        if (_currentRecordingMode == ReplayRecordingMode.DeathMode)
        {
            Log.Warning("No death records available", "ReplaySystemManager");
        }
        return null;
    }
    #endregion
}
