using Godot;
using System.Collections.Generic;
using MarrieGame.Core.Enums;

/// <summary>
/// Handles replay orchestration - coordinates game state changes and ghost management during replay
/// Extracted from ReplayGem to separate concerns
/// </summary>
public partial class ReplayOrchestrator : Node
{
    #region Singleton
    public static ReplayOrchestrator Instance { get; private set; }
    #endregion

    #region State
    private GhostPlayer _currentGhost;
    private GhostMonster _currentGhostMonster;
    private ManagedTimer _replayCleanupTimer;
    private ReplayGem _activeGem; // Track the gem that triggered replay
    private bool _replayInProgress = false;
    #endregion

    #region Lifecycle
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            Log.Debug("ReplayOrchestrator initialized", "ReplayOrchestrator");
            // Subscribe to reality changes so orchestrator can react to external auto-returns
            if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
            {
                RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
            }
            else
            {
                // Defer connection if RealityManager isn't ready yet
                CallDeferred(nameof(ConnectToRealityManager));
            }
        }
        else
        {
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
            _replayCleanupTimer?.Cancel();
            // Unsubscribe from reality changes
            try
            {
                if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
                    RealityManager.Instance.RealityChangedEvent -= OnRealityChanged;
            }
            catch { }
        }
    }
    #endregion

    #region Public API
    public void StartReplay(DeathRecord deathRecord, ReplayGem triggeringGem = null)
    {
        if (deathRecord == null)
        {
            Log.Warning("Cannot start replay - no death record provided", "ReplayOrchestrator");
            return;
        }

        if (_replayInProgress)
        {
            Log.Warning("Replay already in progress - ignoring duplicate call", "ReplayOrchestrator");
            return;
        }

        _replayInProgress = true;

        _activeGem = triggeringGem;

        // Inform system manager that this death has been played so it can avoid
        // creating duplicate gems for the same death in future restarts.
        try
        {
            if (ReplaySystemManager.Instance != null && deathRecord != null)
            {
                ReplaySystemManager.Instance.NotifyReplayPlayed(deathRecord.DeathId);
            }
        }
        catch { }

        Log.Info($"=== StartReplay called by {triggeringGem?.Name ?? "unknown"} ===", "ReplayOrchestrator");
        Log.Debug($"Using death record: {deathRecord.DeathId} at {deathRecord.DeathPosition} with {deathRecord.PlayerFrames.Count} player frames, {deathRecord.MonsterFrames.Count} monster frames", "ReplayOrchestrator");

        var playerFrames = deathRecord.PlayerFrames;
        if (playerFrames.Count == 0)
        {
            Log.Warning("No player frames to replay", "ReplayOrchestrator");
            _replayInProgress = false;
            return;
        }

        // Clean up existing ghosts and start new replay
        CleanupGhosts();
        CreateAndStartReplay(playerFrames, deathRecord.MonsterFrames);
    }

    public void StopReplay()
    {
        _replayInProgress = false;
        CleanupAfterReplay();
    }
    #endregion

    #region Reality Integration
    private void ConnectToRealityManager()
    {
        if (RealityManager.IsInstanceAvailable && RealityManager.Instance != null)
        {
            RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
            Log.Debug("ReplayOrchestrator connected to RealityManager", "ReplayOrchestrator");
        }
        else
        {
            // Try again shortly if not ready
            GetTree().CreateTimer(0.1f).Timeout += ConnectToRealityManager;
        }
    }

    private void OnRealityChanged(Reality newReality)
    {
        // If reality returned to CurrentReality while a replay is in progress, ensure ghosts are removed
        if (newReality == Reality.CurrentReality)
        {
            Log.Debug("Reality changed to CurrentReality - ensuring replay cleanup", "ReplayOrchestrator");
            // Immediately stop and cleanup any replay artifacts
            _replayInProgress = false;
            // Cancel any scheduled cleanup timers
            _replayCleanupTimer?.Cancel();
            CleanupAfterReplay();
        }
    }
    #endregion

    #region Replay Management
    private void CreateAndStartReplay(List<ReplayFrame> playerFrames, List<ReplayFrame> monsterFrames)
    {
        PauseOriginalEntities();

        var ghostPlayer = CreateGhostPlayer();
        if (ghostPlayer != null)
        {
            Log.Debug("Ghost player created successfully, starting replay setup...", "ReplayOrchestrator");

                // If we have monster frames, compress the timeline to remove gaps where the monster was hidden
                List<ReplayFrame> workingPlayerFrames = new List<ReplayFrame>(playerFrames);
                List<ReplayFrame> workingMonsterFrames = monsterFrames != null ? new List<ReplayFrame>(monsterFrames) : null;

                if (workingMonsterFrames != null && workingMonsterFrames.Count > 0)
                {
                    CompressTimelineToMonsterPresence(workingPlayerFrames, workingMonsterFrames, 0.35f);
                }

                // Trim trailing stationary frames to avoid long frozen periods at the end of a replay
                var trimmedPlayerFrames = TrimTrailingStationaryFrames(workingPlayerFrames, 0.5f, 3);

                StartGhostReplay(ghostPlayer, trimmedPlayerFrames);

                if (workingMonsterFrames != null && workingMonsterFrames.Count > 0)
                {
                    var trimmedMonsterFrames = TrimTrailingStationaryFrames(workingMonsterFrames, 0.5f, 2);
                    CreateAndStartMonsterGhost(trimmedMonsterFrames);
                }

                ScheduleReplayCleanup(trimmedPlayerFrames);
        }
        else
        {
            Log.Error("FAILED: Could not create ghost player", "ReplayOrchestrator");
        }
    }

    private void CreateAndStartMonsterGhost(List<ReplayFrame> monsterFrames)
    {
        var ghostMonster = CreateGhostMonster();
        if (ghostMonster != null)
        {
            ghostMonster.StartReplay(monsterFrames, "Monster");
            _currentGhostMonster = ghostMonster;
            Log.Debug($"Ghost monster started with {monsterFrames.Count} frames", "ReplayOrchestrator");
            
            // If the monster's recorded data ends before the overall replay ends,
            // schedule an automatic fade/destroy so the ghost doesn't remain frozen
            // on its last frame until the entire replay completes.
            try
            {
                if (monsterFrames != null && monsterFrames.Count > 0)
                {
                    float lastMonsterFrameTime = monsterFrames[monsterFrames.Count - 1].Timestamp;
                    // Small buffer to allow final frame to be visible briefly
                    float autoFadeDelay = lastMonsterFrameTime + 0.15f;

                    // Schedule fade/destroy at the appropriate time relative to replay start
                    this.CreateManagedTimer(autoFadeDelay, () =>
                    {
                        try
                        {
                            if (IsInstanceValid(ghostMonster))
                            {
                                Log.Debug($"Auto-fading ghost monster after last recorded frame at {lastMonsterFrameTime:F2}s", "ReplayOrchestrator");
                                ghostMonster.FadeOutAndDestroy();
                            }
                        }
                        catch { }
                    });
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Failed to schedule auto-fade for ghost monster: {ex.Message}", "ReplayOrchestrator");
            }
        }
    }

    private void StartGhostReplay(GhostPlayer ghostPlayer, List<ReplayFrame> playerFrames)
    {
        ghostPlayer.StartReplay(playerFrames);
        _currentGhost = ghostPlayer;
    }

    private void ScheduleReplayCleanup(List<ReplayFrame> frames)
    {
        if (frames.Count == 0) return;

        // Cancel any existing cleanup timer
        _replayCleanupTimer?.Cancel();

        // Calculate replay duration: last frame timestamp + buffer time
        float lastFrameTime = frames[frames.Count - 1].Timestamp;
        float replayDuration = lastFrameTime + 3.0f;

        Log.Debug($"Scheduling replay cleanup in {replayDuration:F1}s (last frame at {lastFrameTime:F1}s)", "ReplayOrchestrator");

        _replayCleanupTimer = this.CreateManagedTimer(replayDuration, () =>
        {
            Log.Debug("Cleanup timer triggered - ending replay", "ReplayOrchestrator");
            CleanupAfterReplay();
        });
    }

    // Helper: Trim trailing stationary frames from a frame sequence.
    // Keeps a small tail buffer (maxTrailing) after the last movement to preserve timing.
    private List<ReplayFrame> TrimTrailingStationaryFrames(List<ReplayFrame> original, float minMovement = 0.5f, int maxTrailing = 3)
    {
        if (original == null || original.Count <= 2) return new List<ReplayFrame>(original ?? new List<ReplayFrame>());

        var minMovSq = minMovement * minMovement;
        int lastMovingIndex = -1;

        for (int i = 1; i < original.Count; i++)
        {
            var prev = original[i - 1];
            var cur = original[i];
            if ((cur.Position - prev.Position).LengthSquared() > minMovSq)
            {
                lastMovingIndex = i;
            }
        }

        // If no movement detected, keep only the first frame to avoid long freeze
        if (lastMovingIndex == -1)
        {
            return new List<ReplayFrame> { original[0] };
        }

        // Include up to maxTrailing frames after the last movement to preserve a short tail
        int cutoff = System.Math.Min(original.Count - 1, lastMovingIndex + maxTrailing);
        var result = new List<ReplayFrame>(cutoff + 1);
        for (int i = 0; i <= cutoff; i++) result.Add(original[i]);
        return result;
    }

    // Compress both player and monster timelines to remove gaps where the monster had no frames
    // This effectively skips periods when the monster was hidden in CurrentReality.
    // Adjusts timestamps in-place on the provided lists.
    private void CompressTimelineToMonsterPresence(List<ReplayFrame> playerFrames, List<ReplayFrame> monsterFrames, float gapThreshold = 0.4f)
    {
        if (playerFrames == null || monsterFrames == null || monsterFrames.Count == 0) return;

        // Find intervals in monsterFrames where gap between consecutive frames exceeds gapThreshold.
        var gaps = new List<(float start, float end)>();
        for (int i = 1; i < monsterFrames.Count; i++)
        {
            float prevT = monsterFrames[i - 1].Timestamp;
            float curT = monsterFrames[i].Timestamp;
            float delta = curT - prevT;
            if (delta > gapThreshold)
            {
                // Record gap start = prevT, end = curT
                gaps.Add((prevT, curT));
            }
        }

        if (gaps.Count == 0) return;

        // We will subtract cumulative gap durations from subsequent frames to compress timeline
        float cumulativeShift = 0f;
        int gapIndex = 0;

        // Build a list of (gapStart, gapDuration) in chronological order
        var gapDurations = new List<(float at, float duration)>();
        foreach (var g in gaps)
        {
            gapDurations.Add((g.start, g.end - g.start));
        }

        // Apply shifts to monster frames first (in-place replacement)
        for (int i = 0; i < monsterFrames.Count; i++)
        {
            while (gapIndex < gapDurations.Count && monsterFrames[i].Timestamp >= gapDurations[gapIndex].at + cumulativeShift)
            {
                cumulativeShift += gapDurations[gapIndex].duration;
                gapIndex++;
            }

            if (cumulativeShift > 0f)
            {
                var f = monsterFrames[i];
                monsterFrames[i] = new ReplayFrame(f.Timestamp - cumulativeShift, f.Position, f.Rotation, f.AnimationId, f.SpriteFlip, f.IsVisible, f.Alpha, f.MonsterStateId, f.IsFullyVisible);
            }
        }

        // Reset for player frames
        cumulativeShift = 0f;
        gapIndex = 0;

        for (int i = 0; i < playerFrames.Count; i++)
        {
            while (gapIndex < gapDurations.Count && playerFrames[i].Timestamp >= gapDurations[gapIndex].at + cumulativeShift)
            {
                cumulativeShift += gapDurations[gapIndex].duration;
                gapIndex++;
            }

            if (cumulativeShift > 0f)
            {
                var f = playerFrames[i];
                playerFrames[i] = new ReplayFrame(f.Timestamp - cumulativeShift, f.Position, f.Rotation, f.AnimationId, f.SpriteFlip, f.IsVisible, f.Alpha, f.MonsterStateId, f.IsFullyVisible);
            }
        }
    }
    #endregion

    #region Game State Management
    private void PauseOriginalEntities()
    {
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.PauseRecording();
        }

        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            // Keep the real player active during PastReality replay so the player can still move and interact.
            // The ghost player will replay recorded frames independently.
            Log.Debug("Player left active during replay (ghost will replay past frames)", "ReplayOrchestrator");
        }

        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monster in monsters)
        {
            if (monster is Monster follower && follower.EnableFollowerBehavior)
            {
                follower.DisableForReplay();
                Log.Debug($"Monster with follower behavior {monster.Name} disabled for replay", "ReplayOrchestrator");
            }
            else if (monster is Node2D monsterNode2D)
            {
                monsterNode2D.Hide();
                Log.Debug($"Monster {monster.Name} hidden for replay", "ReplayOrchestrator");
            }
        }
    }

    private void ResumeOriginalEntities()
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            // Only resume the player if it was paused by an external system.
            if (player.IsPaused())
            {
                player.ResumePlayer();
                Log.Debug("Player resumed after replay", "ReplayOrchestrator");
            }
            else
            {
                Log.Debug("Player remained active during replay; no resume needed", "ReplayOrchestrator");
            }
        }

        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monster in monsters)
        {
            if (monster is Monster follower && follower.EnableFollowerBehavior)
            {
                follower.EnableAfterReplay();
                Log.Debug($"Monster with follower behavior {monster.Name} enabled after replay", "ReplayOrchestrator");
            }
            else if (monster is Node2D monsterNode2D)
            {
                // Don't show simple monsters that are hidden as part of a paired hide or scheduled respawn
                var m = monster as Monster;
                if (m != null && EnemyManager.Instance != null)
                {
                    if (EnemyManager.Instance.IsMonsterHiddenByPair(m) || EnemyManager.Instance.IsMonsterScheduledForRespawn(m))
                    {
                        Log.Debug($"Skipping show for {monster.Name} after replay because it's hidden or scheduled for respawn", "ReplayOrchestrator");
                        continue;
                    }
                }

                monsterNode2D.Show();
                Log.Debug($"Monster {monster.Name} shown after replay", "ReplayOrchestrator");
            }
        }
    }

    private void RestartRecording()
    {
        // In PuzzleMode, notify ReplaySystemManager to restart recording fresh
        if (ReplaySystemManager.Instance != null)
        {
            ReplaySystemManager.Instance.RestartRecordingAfterPuzzleReplay();
        }

        if (ReplayRecorder.Instance != null)
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Player;
            if (player != null && player.IsPaused())
            {
                Log.Warning("Player still paused - forcing resume before restarting recording", "ReplayOrchestrator");
                player.ResumePlayer();
                player.Show();
            }

            if (!ReplayRecorder.Instance.IsRecording)
            {
                ReplayRecorder.Instance.RefreshCharacterCache();
                ReplayRecorder.Instance.StartRecording(true);
            }
            else if (ReplayRecorder.Instance.IsPausedForReplay)
            {
                ReplayRecorder.Instance.ResumeRecording();
            }
        }
    }
    #endregion

    #region Ghost Creation
    private GhostPlayer CreateGhostPlayer()
    {
        return ReplayEntityFactory.CreateGhostPlayer(Vector2.Zero, GetTree().CurrentScene);
    }

    private GhostMonster CreateGhostMonster()
    {
        return ReplayEntityFactory.CreateGhostMonster(Vector2.Zero, GetTree().CurrentScene);
    }
    #endregion

    #region Cleanup
    private void CleanupAfterReplay()
    {
        FadeOutGhosts();

        // Wait for fade out to complete before restoring entities
        this.CreateManagedTimer(0.6f, () =>
        {
            CleanupGhosts();
            ResumeOriginalEntities();
            
            this.CreateManagedTimer(0.1f, () =>
            {
                RestartRecording();
                
                if (_activeGem != null && IsInstanceValid(_activeGem))
                {
                    _activeGem.ShowGemAfterReplay();
                }
                
                Log.Debug("Recording restarted after ensuring all entities are active", "ReplayOrchestrator");
            });
            
            Log.Debug("Replay completed - all entities restored", "ReplayOrchestrator");
            
            _replayInProgress = false;
        });
    }

    private void FadeOutGhosts()
    {
        if (_currentGhostMonster != null && IsInstanceValid(_currentGhostMonster))
        {
            _currentGhostMonster.FadeOutAndDestroy();
        }

        if (_currentGhost != null && IsInstanceValid(_currentGhost))
        {
            _currentGhost.StopReplay();
        }
    }

    private void CleanupGhosts()
    {
        CleanupCurrentGhost();
        CleanupCurrentGhostMonster();
        CleanupAllGhosts();
    }

    private void CleanupCurrentGhost()
    {
        if (_currentGhost != null && IsInstanceValid(_currentGhost))
        {
            _currentGhost.StopReplay();
            
            if (_currentGhost.GetParent() != null)
            {
                _currentGhost.GetParent().RemoveChild(_currentGhost);
                if (_currentGhost is IPoolable)
                {
                    // Use PoolManager to release pooled ghost when possible
                    PoolManager.Release(_currentGhost, _currentGhost.GetType().Name);
                }
                else
                {
                    _currentGhost.CallDeferred("queue_free");
                }
            }
            else
            {
                _currentGhost.QueueFree();
            }
            _currentGhost = null;
        }
    }

    private void CleanupCurrentGhostMonster()
    {
        if (_currentGhostMonster != null && IsInstanceValid(_currentGhostMonster))
        {
            _currentGhostMonster.StopReplay();
            
            if (_currentGhostMonster.GetParent() != null)
            {
                _currentGhostMonster.GetParent().RemoveChild(_currentGhostMonster);
                if (_currentGhostMonster is IPoolable)
                {
                    PoolManager.Release(_currentGhostMonster, _currentGhostMonster.GetType().Name);
                }
                else
                {
                    _currentGhostMonster.CallDeferred("queue_free");
                }
            }
            else
            {
                _currentGhostMonster.QueueFree();
            }
            _currentGhostMonster = null;
        }
    }

    private void CleanupAllGhosts()
    {
        var allGhosts = GetTree().GetNodesInGroup("ghost_player");
        foreach (Node ghost in allGhosts)
        {
            if (IsInstanceValid(ghost))
            {
                try
                {
                    if (ghost.GetParent() != null)
                    {
                        ghost.GetParent().RemoveChild(ghost);
                        if (ghost is IPoolable)
                        {
                            PoolManager.Release(ghost, ghost.GetType().Name);
                        }
                        else
                        {
                            ghost.CallDeferred("queue_free");
                        }
                    }
                    else
                    {
                        ghost.QueueFree();
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"Error cleaning up ghost player: {ex.Message}", "ReplayOrchestrator");
                    ghost.QueueFree();
                }
            }
        }

        var allGhostMonsters = GetTree().GetNodesInGroup("ghost_monster");
        foreach (Node ghost in allGhostMonsters)
        {
            if (IsInstanceValid(ghost))
            {
                try
                {
                    if (ghost.GetParent() != null)
                    {
                        ghost.GetParent().RemoveChild(ghost);
                        if (ghost is IPoolable)
                        {
                            PoolManager.Release(ghost, ghost.GetType().Name);
                        }
                        else
                        {
                            ghost.CallDeferred("queue_free");
                        }
                    }
                    else
                    {
                        ghost.QueueFree();
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"Error cleaning up ghost monster: {ex.Message}", "ReplayOrchestrator");
                    ghost.QueueFree();
                }
            }
        }
    }
    #endregion
}
