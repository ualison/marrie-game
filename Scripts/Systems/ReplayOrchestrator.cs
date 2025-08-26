using Godot;
using System.Collections.Generic;

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
    private bool _replayInProgress = false; // Prevent duplicate replays
    #endregion

    #region Lifecycle
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            Log.System("ReplayOrchestrator initialized", "ReplayOrchestrator");
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

        // Track the gem that triggered this replay
        _activeGem = triggeringGem;

        Log.Replay($"=== StartReplay called by {triggeringGem?.Name ?? "unknown"} ===", "ReplayOrchestrator");
        Log.Replay($"Using death record: {deathRecord.DeathId} at {deathRecord.DeathPosition} with {deathRecord.PlayerFrames.Count} player frames, {deathRecord.MonsterFrames.Count} monster frames", "ReplayOrchestrator");

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

    #region Replay Management
    private void CreateAndStartReplay(List<ReplayFrame> playerFrames, List<ReplayFrame> monsterFrames)
    {
        // Pause entities FIRST, then create ghosts
        PauseOriginalEntities();

        var ghostPlayer = CreateGhostPlayer();
        if (ghostPlayer != null)
        {
            Log.Replay("Ghost player created successfully, starting replay setup...", "ReplayOrchestrator");

            StartGhostReplay(ghostPlayer, playerFrames);

            // Add monster ghost if data available
            if (monsterFrames != null && monsterFrames.Count > 0)
            {
                CreateAndStartMonsterGhost(monsterFrames);
            }

            ScheduleReplayCleanup(playerFrames);
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
            Log.Replay($"Ghost monster started with {monsterFrames.Count} frames", "ReplayOrchestrator");
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

        // Calculate proper replay duration: last frame timestamp + buffer time
        float lastFrameTime = frames[frames.Count - 1].Timestamp;
        float replayDuration = lastFrameTime + 3.0f; // Extra time for fade out and safety

        Log.Replay($"Scheduling replay cleanup in {replayDuration:F1}s (last frame at {lastFrameTime:F1}s)", "ReplayOrchestrator");

        _replayCleanupTimer = this.CreateManagedTimer(replayDuration, () =>
        {
            Log.Replay("Cleanup timer triggered - ending replay", "ReplayOrchestrator");
            CleanupAfterReplay();
        });
    }
    #endregion

    #region Game State Management
    private void PauseOriginalEntities()
    {
        // Pause recording FIRST to avoid recording the freeze process
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.PauseRecording();
        }

        // Pause and hide the original player during replay
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            player.PausePlayer();
            player.Hide();
            Log.Replay("Player paused and hidden for replay", "ReplayOrchestrator");
        }

        // Disable all monsters during replay
        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monster in monsters)
        {
            if (monster is Monster follower && follower.EnableFollowerBehavior)
            {
                follower.DisableForReplay();
                Log.Replay($"Monster with follower behavior {monster.Name} disabled for replay", "ReplayOrchestrator");
            }
            else if (monster is Node2D monsterNode2D)
            {
                monsterNode2D.Hide();
                Log.Replay($"Monster {monster.Name} hidden for replay", "ReplayOrchestrator");
            }
        }
    }

    private void ResumeOriginalEntities()
    {
        // Resume and show the original player
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            player.ResumePlayer();
            player.Show();
            Log.Replay("Player resumed and shown after replay", "ReplayOrchestrator");
        }

        // Re-enable all monsters after replay
        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monster in monsters)
        {
            if (monster is Monster follower && follower.EnableFollowerBehavior)
            {
                follower.EnableAfterReplay();
                Log.Replay($"Monster with follower behavior {monster.Name} enabled after replay", "ReplayOrchestrator");
            }
            else if (monster is Node2D monsterNode2D)
            {
                monsterNode2D.Show();
                Log.Replay($"Monster {monster.Name} shown after replay", "ReplayOrchestrator");
            }
        }
    }

    private void RestartRecording()
    {
        if (ReplayRecorder.Instance != null)
        {
            // Double-check that player is properly resumed
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
                ReplayRecorder.Instance.StartRecording();
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
        // Use factory for ghost creation
        return ReplayEntityFactory.CreateGhostPlayer(Vector2.Zero, GetTree().CurrentScene);
    }

    private GhostMonster CreateGhostMonster()
    {
        // Use factory for ghost creation
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
            
            // Add small delay before restarting recording
            this.CreateManagedTimer(0.1f, () =>
            {
                RestartRecording();
                
                // Restore gem visibility after replay
                if (_activeGem != null && IsInstanceValid(_activeGem))
                {
                    _activeGem.ShowGemAfterReplay();
                }
                
                Log.Replay("Recording restarted after ensuring all entities are active", "ReplayOrchestrator");
            });
            
            Log.Replay("Replay completed - all entities restored", "ReplayOrchestrator");
            
            // Mark replay as no longer in progress
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
                _currentGhost.CallDeferred("queue_free");
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
                _currentGhostMonster.CallDeferred("queue_free");
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
        // Clean up any remaining ghost entities
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
                        ghost.CallDeferred("queue_free");
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
                        ghost.CallDeferred("queue_free");
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
