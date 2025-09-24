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
    // Rolling recording window in seconds (configurable)
    private float _recordWindowSeconds = 10f;
    // Whether the recorder was paused because we entered past reality (replaying)
    private bool _recorderPausedForReplay = false;
    // Timestamp when the current rolling recording started (ms)
    private long _rollingRecordingStartMs = 0;
    // Timer to manage rolling window updates
    private Timer _rollingWindowTimer;
    // Current rolling window data (temporary storage)
    private DeathRecord _currentRollingRecord;
    #endregion

    #region Death Records
    private readonly Dictionary<string, DeathRecord> _deathRecords = new();
    private string _currentDeathId = "";
    private ReplayRecordingMode _currentRecordingMode = ReplayRecordingMode.DeathMode;
    // Track which death IDs have already been played back so we don't create a
    // new gem for them after a replay has already occurred.
    private readonly HashSet<string> _playedDeathIds = new();
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
            
            // Cleanup timer
            if (_rollingWindowTimer != null)
            {
                _rollingWindowTimer.Stop();
                _rollingWindowTimer.QueueFree();
                _rollingWindowTimer = null;
            }
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
        
        // Important: Log initialization to help debug recording issues
        Log.Info($"ReplaySystemManager initialized - Mode: {_currentRecordingMode}", "ReplaySystemManager");

        // Connect to reality changes to broadcast hide/show to all gems
        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
        }

        // Setup rolling window timer
        SetupRollingWindowTimer();
        
        // Ensure the recorder is in rolling mode: start recording automatically
        // only when in the current reality. This keeps a sliding buffer of the
        // most recent _recordWindowSeconds worth of gameplay.
        // Always start recording to maintain continuous rolling window
        TryStartRollingRecordingIfNeeded(true); // Force start
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

        // Start recording immediately after scene reset
        TryStartRollingRecordingIfNeeded(true);

        Log.Info("ReplaySystemManager reset after scene change", "ReplaySystemManager");
    }
    #endregion

    #region Event Handlers
    private void OnPlayerDied(MarrieGame.Core.Models.PlayerDeath death)
    {
        // In rolling window mode, we always have a recording running
        // Just save the current rolling window as a death record
        if (_recorder == null || !_recorder.HasRecordedData)
        {
            Log.Warning("No replay data available for death (no current recording)", "ReplaySystemManager");
            return;
        }
        
        // Save the current rolling window recording as a death record
        SaveDeathRecord(death.Position);

        // Don't stop the recorder - it will restart automatically for the next rolling window
        // Defensive: ensure no ReplayGem is left at the player's death position.
        CleanupCurrentGem();

        // Important event - player deaths should be tracked for debugging
        Log.Info($"Player death recorded at {death.Position} (Total deaths: {_deathRecords.Count})", "ReplaySystemManager");
    }

    private void OnGameRestart()
    {
        // Clean up current replay visuals and gems
        // If there is an active gem that was used to trigger a replay previously,
        // reuse it instead of creating a new one. Otherwise cleanup existing gem
        // and create a new one if appropriate.
        // We'll decide below whether to create a new gem.
        
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
        
        // Restart rolling recording for new session (always force restart to ensure clean state)
        TryStartRollingRecordingIfNeeded(true);
        
        // Do NOT automatically create a new ReplayGem on game restart.
        // Reuse an existing gem if present; otherwise only notify the player
        // that a replay is available. This avoids duplicate/automatic gem
        // creation and keeps gem visibility controlled by reality changes.
        if (lastDeath != null)
        {
            if (_currentGem != null && IsInstanceValid(_currentGem))
            {
                Log.Replay($"Reusing existing ReplayGem for death {lastDeath.DeathId}", "ReplaySystemManager");
                var spawnPos = PlayerStateManager.Instance != null ? PlayerStateManager.Instance.PlayerSpawnPosition : lastDeath.DeathPosition;
                _currentGem.ShowGem(spawnPos);
            }
            else
            {
                // No existing gem — don't auto-create one here. Let scene logic or
                // player actions create/show gems when appropriate.
                Log.Replay($"Replay available for death {lastDeath.DeathId} but no existing gem found; not auto-creating on restart", "ReplaySystemManager");
            }
        }
        
        Log.Replay($"Game restarted - recorder reset, death records preserved ({_deathRecords.Count} deaths)", "ReplaySystemManager");
    }
    #endregion

    #region Death Record Management
    private void SaveDeathRecord(Vector2 deathPosition)
    {
        _currentDeathId = GenerateDeathId();
        
        DeathRecord recordToSave = null;
        
        // Prefer the current rolling record if available and recent
        if (_currentRollingRecord != null)
        {
            recordToSave = new DeathRecord(_currentDeathId, deathPosition, 0)
            {
                PlayerFrames = _currentRollingRecord.PlayerFrames,
                MonsterFrames = _currentRollingRecord.MonsterFrames
            };
        }
        // Fallback to current recorder data if no rolling record exists
        else if (_recorder != null && _recorder.HasRecordedData)
        {
            recordToSave = new DeathRecord(_currentDeathId, deathPosition, 0)
            {
                PlayerFrames = _recorder.GetPlayerFrames(),
                MonsterFrames = _recorder.GetMonsterFrames()
            };
        }

        if (recordToSave == null)
        {
            Log.Warning("No replay data available to save for death", "ReplaySystemManager");
            return;
        }

        // Clear previous death records (keep only the latest for rolling behavior)
        _deathRecords.Clear();
        _deathRecords[_currentDeathId] = recordToSave;

        Log.Replay($"Death record saved: {recordToSave.TotalFrames} frames at position {deathPosition}", "ReplaySystemManager");
        
        // NOTE: Do not notify the player here (at death) — notifications should appear
        // when the scene/session is (re)started so the player sees replay availability
        // at a predictable time. UI notification moved to OnGameRestart/CreateReplayGem flow.
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

    private void OnRealityChanged(Reality newReality)
    {
        // Broadcast hide/show to all ReplayGem instances in the scene
        var allGems = GetTree().GetNodesInGroup("replay_gem");
        foreach (Node n in allGems)
        {
            if (n is ReplayGem gem && IsInstanceValid(gem))
            {
                try
                {
                    if (newReality == Reality.PastReality)
                    {
                        gem.ForceHideCompletely();
                    }
                    else
                    {
                        // Restore visibility state - ShowGemAfterReplay will ensure proper restore for active gem
                        gem.Show();
                    }
                }
                catch { }
            }
        }

        // Pause or resume the rolling recorder depending on reality
        if (newReality == Reality.PastReality)
        {
            // If player is replaying the past, pause the main rolling recorder
            if (_recorder != null && _recorder.IsRecording && !_recorderPausedForReplay)
            {
                _recorder.StopRecording();
                _recorderPausedForReplay = true;
                Log.Debug("Paused rolling recorder for replay (past reality)", "ReplaySystemManager");
            }
        }
        else
        {
            // Return to current reality: resume rolling recorder and start new window
            if (_recorderPausedForReplay)
            {
                _recorderPausedForReplay = false;
                TryStartRollingRecordingIfNeeded(true); // Force restart after replay
                Log.Debug("Resumed rolling recorder after replay (current reality)", "ReplaySystemManager");
            }
            else
            {
                // Ensure we have a rolling recording active
                TryStartRollingRecordingIfNeeded(true); // Force to ensure it's working
            }
        }
    }

    private bool IsInCurrentReality()
    {
        return !RealityManager.IsInstanceAvailable || RealityManager.Instance.CurrentReality == Reality.CurrentReality;
    }

    private void TryStartRollingRecordingIfNeeded(bool forceRestart = false)
    {
        if (!IsInCurrentReality()) return;
        if (_recorder == null) return;

        if (forceRestart)
        {
            Log.Debug("Force restarting rolling recording", "ReplaySystemManager");
            StopRollingRecording();
            StartRollingRecording();
            return;
        }

        if (!_recorder.IsRecording)
        {
            Log.Debug("Starting rolling recording (recorder not active)", "ReplaySystemManager");
            StartRollingRecording();
        }
        else
        {
            // Already recording: ensure rolling window timer is running
            if (_rollingRecordingStartMs == 0)
            {
                _rollingRecordingStartMs = (long)(Time.GetUnixTimeFromSystem() * 1000);
                Log.Debug("Set rolling recording start timestamp", "ReplaySystemManager");
            }
            
            // Ensure the rolling window timer is running
            if (_rollingWindowTimer != null)
            {
                if (_rollingWindowTimer.IsStopped())
                {
                    _rollingWindowTimer.Start();
                    Log.Debug("Started rolling window timer (was stopped)", "ReplaySystemManager");
                }
            }
            else
            {
                Log.Warning("Rolling window timer is null - recreating", "ReplaySystemManager");
                SetupRollingWindowTimer();
                if (_rollingWindowTimer != null)
                {
                    _rollingWindowTimer.Start();
                }
            }
        }
    }

    private void StartRollingRecording()
    {
        if (_recorder == null) 
        {
            Log.Warning("Cannot start rolling recording - recorder is null", "ReplaySystemManager");
            return;
        }
        
        // Clear previous recording data and start fresh
        _recorder.ClearRecording();
        _recorder.StartRecording();
        _rollingRecordingStartMs = (long)(Time.GetUnixTimeFromSystem() * 1000);
        
        // Ensure rolling window timer is set up and started
        if (_rollingWindowTimer == null)
        {
            Log.Debug("Rolling window timer is null during StartRollingRecording - setting up", "ReplaySystemManager");
            SetupRollingWindowTimer();
        }
        
        if (_rollingWindowTimer != null)
        {
            _rollingWindowTimer.Start();
            Log.Info($"Started rolling recording with {_recordWindowSeconds}s window", "ReplaySystemManager");
        }
        else
        {
            Log.Error("Failed to create rolling window timer", "ReplaySystemManager");
        }
    }

    private void StopRollingRecording()
    {
        if (_recorder == null) return;
        
        if (_recorder.IsRecording)
        {
            _recorder.StopRecording();
            Log.Debug("Stopped rolling recording", "ReplaySystemManager");
        }
        
        _rollingRecordingStartMs = 0;
        
        // Stop the rolling window timer
        if (_rollingWindowTimer != null)
        {
            _rollingWindowTimer.Stop();
            Log.Debug("Stopped rolling window timer", "ReplaySystemManager");
        }
    }

    private void SetupRollingWindowTimer()
    {
        // Clean up existing timer
        if (_rollingWindowTimer != null)
        {
            _rollingWindowTimer.Timeout -= OnRollingWindowComplete; // Disconnect signal
            _rollingWindowTimer.Stop();
            _rollingWindowTimer.QueueFree();
            _rollingWindowTimer = null;
        }

        // Create new timer for rolling window management
        _rollingWindowTimer = new Timer();
        _rollingWindowTimer.WaitTime = _recordWindowSeconds;
        _rollingWindowTimer.Autostart = false;
        _rollingWindowTimer.OneShot = false; // Repeat every _recordWindowSeconds
        _rollingWindowTimer.Timeout += OnRollingWindowComplete;
        AddChild(_rollingWindowTimer);
        
        Log.Debug($"Set up rolling window timer with {_recordWindowSeconds}s interval", "ReplaySystemManager");
    }

    private void OnRollingWindowComplete()
    {
        // Only process if we're in current reality and not paused for replay
        if (!IsInCurrentReality() || _recorderPausedForReplay)
        {
            // If paused for replay, restart the timer to continue when we return
            if (_rollingWindowTimer != null && !_rollingWindowTimer.IsStopped())
            {
                _rollingWindowTimer.Start();
            }
            return;
        }

        if (_recorder != null && _recorder.IsRecording)
        {
            // Always store current recording as the rolling window record, regardless of duration
            _currentRollingRecord = new DeathRecord($"rolling_{Time.GetUnixTimeFromSystem()}", Vector2.Zero, 0)
            {
                PlayerFrames = _recorder.GetPlayerFrames(),
                MonsterFrames = _recorder.GetMonsterFrames()
            };
            
            var stats = _recorder.GetStats();
            Log.Debug($"Rolling window completed: {stats.duration:F1}s recorded ({_currentRollingRecord.TotalFrames} frames)", "ReplaySystemManager");

            // Start new rolling window - this clears old data and starts fresh
            StartRollingRecording();
        }
        else
        {
            // If not recording, start recording to ensure continuous operation
            Log.Debug("Rolling window timer fired but recorder not active - starting recording", "ReplaySystemManager");
            StartRollingRecording();
        }
    }

    /// <summary>
    /// Mark a death id as having been played back so we can avoid recreating
    /// a new gem for the same death later.
    /// </summary>
    public void NotifyReplayPlayed(string deathId)
    {
        if (string.IsNullOrEmpty(deathId)) return;
        _playedDeathIds.Add(deathId);
    }
    #endregion

    #region Public API (for ReplayGem to call)
    public bool HasReplayData()
    {
        // Check death records first (highest priority)
        if (_deathRecords.Count > 0)
        {
            return true;
        }
        
        // Check if we have a current rolling record with data
        if (_currentRollingRecord != null && _currentRollingRecord.TotalFrames > 0)
        {
            return true;
        }
        
        // Check if recorder has current data (fallback)
        if (_recorder != null && _recorder.HasRecordedData)
        {
            return true;
        }
        
        return false;
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

    /// <summary>
    /// Configure the rolling record window length (seconds).
    /// Must be > 0.
    /// </summary>
    public void SetRecordWindowSeconds(float seconds)
    {
        if (seconds <= 0) return;
        _recordWindowSeconds = seconds;
        
        // Update the timer if it exists
        if (_rollingWindowTimer != null)
        {
            _rollingWindowTimer.WaitTime = _recordWindowSeconds;
        }
    }

    public DeathRecord GetLastDeathRecord()
    {
        // Always try death records first (from actual player deaths)
        if (_deathRecords.Count > 0)
        {
            var latestRecord = _deathRecords.Values.FirstOrDefault();
            Log.Debug($"Returning latest death record: {latestRecord?.DeathId}", "ReplaySystemManager");
            return latestRecord;
        }
        
        // If no death records, check if we have a current rolling record available
        if (_currentRollingRecord != null && _currentRollingRecord.TotalFrames > 0)
        {
            Log.Debug($"Returning current rolling record with {_currentRollingRecord.TotalFrames} frames", "ReplaySystemManager");
            return _currentRollingRecord;
        }
        
        // Fallback to current recorder data if recording is active
        if (_recorder != null && _recorder.HasRecordedData)
        {
            try
            {
                var (playerFrames, monsterFrames, duration) = _recorder.GetCurrentRecordingData();
                
                if (playerFrames.Count > 0)
                {
                    // Create a virtual record from current recording data
                    var virtualRecord = new DeathRecord($"current_session_{Time.GetUnixTimeFromSystem()}", Vector2.Zero, 0)
                    {
                        PlayerFrames = playerFrames,
                        MonsterFrames = monsterFrames
                    };

                    Log.Debug($"Created virtual record from current recording with {playerFrames.Count} player frames", "ReplaySystemManager");
                    return virtualRecord;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Failed to get current recording data: {ex.Message}", "ReplaySystemManager");
            }
        }
        
        // No replay data available
        Log.Debug($"No replay data available - DeathRecords: {_deathRecords.Count}, RollingRecord: {(_currentRollingRecord != null ? _currentRollingRecord.TotalFrames : 0)} frames, Recorder: {(_recorder?.HasRecordedData ?? false)}", "ReplaySystemManager");
        return null;
    }
    #endregion
}
