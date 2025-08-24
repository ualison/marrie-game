using Godot;
using System.Collections.Generic;
using System.Linq;

// Replay gem that appears at death location and allows ghost replay functionality
public partial class ReplayGem : Node2D
{
    #region Configuration
    [Export] public PackedScene GhostPlayerScene { get; set; } // Assign in inspector if available
    [Export] public PackedScene GhostMonsterScene { get; set; } // Assign in inspector if available
    #endregion

    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Label _promptLabel;
    #endregion

    #region State
    private bool _playerNearby = false;
    private GhostPlayer _currentGhost;
    private GhostMonster _currentGhostMonster;

    private List<Node2D> _hiddenMonsters = new(); // Track which monsters we hid
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup components and hide gem initially
        InitializeComponents();
        ConnectSignals();
        SetupPromptLabel();
        HideInitially();
        EnableInputProcessing();

        Log.Replay("ReplayGem ready - hidden until activated", "ReplayGem");
    }

    private void InitializeComponents()
    {
        // Get required child nodes with error handling
        _detectionArea = GetNode<Area2D>("DetectionArea");
        _gemSprite = GetNode<Sprite2D>("GemSprite");
        _promptLabel = GetNode<Label>("PromptLabel");
    }

    private void ConnectSignals()
    {
        // Connect detection area signals
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;
    }

    private void SetupPromptLabel()
    {
        // Configure prompt label appearance
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";
    }

    private void HideInitially()
    {
        // Start hidden until shown by GameManager
        _detectionArea.SetDeferred("monitoring", false);
        _promptLabel.Hide();
        _gemSprite.Hide();
    }

    private void EnableInputProcessing()
    {
        // Enable input processing for interaction
        SetProcess(true);
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        // Handle interaction input when player is nearby
        if (!_playerNearby) return;

        if (Input.IsActionJustPressed("interact"))
        {
            HandleReplayActivation();
        }
    }

    private void HandleReplayActivation()
    {
        // Stop recording before starting replay to prevent interference
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.StopRecording();
        }

        StartReplay();
    }
    #endregion

    #region Gem Visibility Control
    public void ShowGem(Vector2 position)
    {
        // Display gem at specified position
        GlobalPosition = position;
        Show();

        if (_gemSprite != null)
        {
            _gemSprite.Show();
        }

        if (_promptLabel != null)
        {
            _promptLabel.Hide(); // Only show when player nearby
        }

        _detectionArea.SetDeferred("monitoring", true);
        Log.Replay($"ReplayGem shown at {position}, scale={Scale}", "ReplayGem");
    }

    public void HideGem()
    {
        // Hide gem completely and cleanup
        _detectionArea.SetDeferred("monitoring", false);

        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }

        if (_gemSprite != null)
        {
            _gemSprite.Hide();
        }

        _playerNearby = false;
        CleanupGhost();
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        // Show prompt when player approaches
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            if (_promptLabel != null)
            {
                _promptLabel.Show();
            }
            Log.UI("Player nearby - showing prompt", "ReplayGem");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        // Hide prompt when player leaves
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            Log.UI("Player left - hiding prompt", "ReplayGem");
        }
    }
    #endregion

    #region Replay System - ORIGINAL LOGIC PRESERVED
    private void StartReplay()
    {
        // Initialize replay with data validation - FIXED: Ensure recording is always resumed
        Log.Replay("=== StartReplay called ===", "ReplayGem");

        // Pause recording immediately to prevent interference
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.PauseRecording();
        }

        var deathRecord = GetDeathRecord();
        if (deathRecord == null) 
        {
            // Resume recording if death record is null
            if (ReplayRecorder.Instance != null)
            {
                ReplayRecorder.Instance.ResumeRecording();
            }
            return;
        }

        var playerFrames = deathRecord.PlayerFrames;
        if (!ValidateFrameData(playerFrames)) 
        {
            // Recording is already resumed in ValidateFrameData for empty frames
            return;
        }

        // Clean up existing ghosts and start new replay - ORIGINAL FLOW
        CleanupGhost();
        GetTree().CreateTimer(0.1f).Timeout += () => CreateAndStartGhost(playerFrames, deathRecord.MonsterFrames);
    }

    private DeathRecord GetDeathRecord()
    {
        // Retrieve death record from ReplaySystemManager
        if (ReplaySystemManager.Instance == null)
        {
            Log.Error("ReplaySystemManager.Instance is null - cannot get death record", "ReplayGem");
            return null;
        }

        return ReplaySystemManager.Instance.GetLastDeathRecord();
    }

    private bool ValidateFrameData(System.Collections.Generic.List<ReplayFrame> playerFrames)
    {
        // Validate frame data before starting replay - FIXED: Handle empty frames gracefully
        Log.Replay($"Player frames count: {playerFrames.Count}", "ReplayGem");

        if (playerFrames.Count == 0)
        {
            Log.Warning("No player frames to replay", "ReplayGem");
            // Resume recording immediately if no frames to replay
            if (ReplayRecorder.Instance != null)
            {
                ReplayRecorder.Instance.ResumeRecording();
            }
            return false;
        }

        return true;
    }

    private void CreateAndStartGhost(System.Collections.Generic.List<ReplayFrame> playerFrames,
                                    System.Collections.Generic.List<ReplayFrame> monsterFrames)
    {
        // Create and start ghost - ORIGINAL METHOD NAME AND FLOW
        var ghostPlayer = CreateGhostPlayer();

        if (ghostPlayer != null)
        {
            Log.Replay("Starting ghost replay...", "ReplayGem");

            PauseAndHideOriginalEntities();
            StartGhostReplay(ghostPlayer, playerFrames);
            HideGemDuringReplay();

            // NEW: Add monster ghost if data available
            if (monsterFrames != null && monsterFrames.Count > 0)
            {
                CreateAndStartMonsterGhost(monsterFrames);
            }

            ScheduleReplayCleanup(playerFrames);
        }
        else
        {
            Log.Error("FAILED: Could not create ghost player", "ReplayGem");
        }
    }

    private void CreateAndStartMonsterGhost(System.Collections.Generic.List<ReplayFrame> monsterFrames)
    {
        // NEW: Create monster ghost separately to avoid disrupting player flow
        var ghostMonster = CreateGhostMonster();
        if (ghostMonster != null)
        {
            ghostMonster.StartReplay(monsterFrames, "Monster");
            _currentGhostMonster = ghostMonster;
            Log.Replay($"Ghost monster started with {monsterFrames.Count} frames", "ReplayGem");
        }
    }

    private void PauseAndHideOriginalEntities()
    {
        // Pause and hide the original player during replay
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            player.PausePlayer();
            player.Hide();
            Log.Replay("Player paused and hidden for replay", "ReplayGem");
        }

        // Disable all monsters during replay
        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monster in monsters)
        {
            if (monster is Monster follower && follower.EnableFollowerBehavior)
            {
                follower.DisableForReplay();
                Log.Replay($"Monster with follower behavior {monster.Name} disabled for replay", "ReplayGem");
            }
            else if (monster is Node2D monsterNode2D)
            {
                monsterNode2D.Hide();
                Log.Replay($"Monster {monster.Name} hidden for replay", "ReplayGem");
            }
        }
    }

    private void StartGhostReplay(GhostPlayer ghostPlayer, System.Collections.Generic.List<ReplayFrame> playerFrames)
    {
        // Start the ghost replay and track reference - ORIGINAL METHOD
        ghostPlayer.StartReplay(playerFrames);
        _currentGhost = ghostPlayer;
    }

    private void HideGemDuringReplay()
    {
        // Hide UI elements during replay - ORIGINAL METHOD
        if (_promptLabel != null)
        {
            _promptLabel.Hide();
        }
        _gemSprite?.Hide();
    }

    private void ScheduleReplayCleanup(System.Collections.Generic.List<ReplayFrame> frames)
    {
        // Schedule cleanup when replay completes - ORIGINAL METHOD
        if (frames.Count == 0) return;

        float replayDuration = frames[frames.Count - 1].Timestamp + 2.0f; // Extra time for fade out

        GetTree().CreateTimer(replayDuration).Timeout += () =>
        {
            CleanupAfterReplay();
        };
    }

    private void CleanupAfterReplay()
    {
        // Restore game state after replay ends
        FadeOutGhosts();

        // Wait for fade out to complete before restoring entities
        GetTree().CreateTimer(0.6f).Timeout += () =>
        {
            CleanupGhost();
            ResumeAndShowOriginalEntities();
            RestartRecording();
            ShowGemAfterReplay();
            Log.Replay("Replay completed - all entities restored and recording restarted", "ReplayGem");
        };
    }

    private void FadeOutGhosts()
    {
        // Fade out ghosts before cleanup
        if (_currentGhostMonster != null && IsInstanceValid(_currentGhostMonster))
        {
            _currentGhostMonster.FadeOutAndDestroy();
        }

        if (_currentGhost != null && IsInstanceValid(_currentGhost))
        {
            _currentGhost.StopReplay(); // GhostPlayer hides immediately
        }
    }

    private void ResumeAndShowOriginalEntities()
    {
        // Resume and show the original player
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null)
        {
            player.ResumePlayer();
            player.Show();
            Log.Replay("Player resumed and shown after replay", "ReplayGem");
        }

        // Re-enable all monsters after replay
        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monster in monsters)
        {
            if (monster is Monster follower && follower.EnableFollowerBehavior)
            {
                follower.EnableAfterReplay();
                Log.Replay($"Monster with follower behavior {monster.Name} enabled after replay", "ReplayGem");
            }
            else if (monster is Node2D monsterNode2D)
            {
                monsterNode2D.Show();
                Log.Replay($"Monster {monster.Name} shown after replay", "ReplayGem");
            }
        }
    }

    private void RestartRecording()
    {
        // Restart recording after replay - FIXED: Ensure recording is fully restarted
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.ResumeRecording();
            ReplayRecorder.Instance.RefreshCharacterReferences();
            ReplayRecorder.Instance.InitializeRecording();
        }
    }

    private void ShowGemAfterReplay()
    {
        // Show gem again after replay if still valid - ORIGINAL METHOD
        if (_gemSprite != null && IsInstanceValid(this))
        {
            _gemSprite.Show();
        }
    }
    #endregion

    #region Ghost Creation - ORIGINAL METHODS UNTOUCHED
    private GhostPlayer CreateGhostPlayer()
    {
        // Create ghost player using available methods - ORIGINAL METHOD
        GhostPlayer ghost = null;

        // Try scene instantiation first if available
        if (GhostPlayerScene != null)
        {
            ghost = CreateGhostFromScene();
        }

        // Fallback to programmatic creation
        if (ghost == null)
        {
            ghost = CreateGhostProgrammatically();
        }

        return ghost;
    }

    private GhostPlayer CreateGhostFromScene()
    {
        // Create ghost from scene file - ORIGINAL METHOD
        var ghostNode = GhostPlayerScene.Instantiate();
        var ghost = ghostNode as GhostPlayer;

        if (ghost != null)
        {
            GetTree().CurrentScene.AddChild(ghost);
            ghost.AddToGroup("ghost_player");
            Log.Debug("Created ghost from scene file", "ReplayGem");
        }
        else
        {
            ghostNode?.QueueFree();
        }

        return ghost;
    }

    private GhostPlayer CreateGhostProgrammatically()
    {
        // Create ghost programmatically by copying original player structure - ORIGINAL METHOD
        var originalPlayer = GetTree().GetFirstNodeInGroup("player") as Player;
        if (originalPlayer == null)
        {
            Log.Error("Cannot create ghost - no original player found", "ReplayGem");
            return null;
        }

        var ghost = new GhostPlayer { Name = "GhostPlayer" };

        CopySpriteStructure(originalPlayer, ghost);
        AddGhostToScene(ghost);

        Log.Debug("Created ghost programmatically", "ReplayGem");
        return ghost;
    }

    private void CopySpriteStructure(Node originalPlayer, GhostPlayer ghost)
    {
        // Copy sprite structure from original player to ghost - ORIGINAL METHOD
        var originalAnimSprite = originalPlayer.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D")
                                ?? originalPlayer.GetNodeOrNull<AnimatedSprite2D>("MarrieSprite");

        if (originalAnimSprite != null)
        {
            CreateGhostAnimatedSprite(originalAnimSprite, ghost);
        }
        else
        {
            CreateGhostStaticSprite(originalPlayer, ghost);
        }

        // IMPORTANT: Only copy visual components, no collision or interaction components
        Log.Debug("Ghost player created with visual components only - no collision/damage", "ReplayGem");
    }

    private void CreateGhostAnimatedSprite(AnimatedSprite2D originalAnimSprite, GhostPlayer ghost)
    {
        // Create animated sprite for ghost - ORIGINAL METHOD
        var ghostSprite = new AnimatedSprite2D
        {
            Name = originalAnimSprite.Name,
            SpriteFrames = originalAnimSprite.SpriteFrames,
            Animation = "idle"
        };

        ghost.AddChild(ghostSprite);
        Log.Debug("Copied AnimatedSprite2D structure to ghost", "ReplayGem");
    }

    private void CreateGhostStaticSprite(Node originalPlayer, GhostPlayer ghost)
    {
        // Create static sprite for ghost as fallback - ORIGINAL METHOD
        var originalSprite2D = originalPlayer.GetNodeOrNull<Sprite2D>("Sprite2D")
                            ?? originalPlayer.GetNodeOrNull<Sprite2D>("MarrieSprite");

        if (originalSprite2D != null)
        {
            var ghostSprite = new Sprite2D
            {
                Name = originalSprite2D.Name,
                Texture = originalSprite2D.Texture
            };

            ghost.AddChild(ghostSprite);
            Log.Debug("Copied Sprite2D structure to ghost", "ReplayGem");
        }
    }

    private void AddGhostToScene(GhostPlayer ghost)
    {
        // Add ghost to scene and group - ORIGINAL METHOD
        GetTree().CurrentScene.AddChild(ghost);
        ghost.AddToGroup("ghost_player");
    }
    #endregion

    #region NEW Monster Ghost Creation
    private GhostMonster CreateGhostMonster()
    {
        // Create ghost monster using available methods
        GhostMonster ghost = null;

        // Try scene instantiation first if available
        if (GhostMonsterScene != null)
        {
            ghost = CreateGhostMonsterFromScene();
        }

        // Fallback to programmatic creation
        if (ghost == null)
        {
            ghost = CreateGhostMonsterProgrammatically();
        }

        return ghost;
    }

    private GhostMonster CreateGhostMonsterFromScene()
    {
        // Create ghost monster from scene file
        var ghostNode = GhostMonsterScene.Instantiate();
        var ghost = ghostNode as GhostMonster;

        if (ghost != null)
        {
            GetTree().CurrentScene.AddChild(ghost);
            ghost.AddToGroup("ghost_monster");
            Log.Debug("Created ghost monster from scene file", "ReplayGem");
        }
        else
        {
            ghostNode?.QueueFree();
        }

        return ghost;
    }

    private GhostMonster CreateGhostMonsterProgrammatically()
    {
        // Find the active monster (Monster with follower behavior first, then regular Monster)
        var followerMonster = GetTree().GetNodesInGroup("enemies")
            .OfType<Monster>()
            .FirstOrDefault(m => m.EnableFollowerBehavior);
        var regularMonster = GetTree().GetNodesInGroup("enemies")
            .OfType<Monster>()
            .FirstOrDefault(m => !m.EnableFollowerBehavior);

        Node2D originalMonster = followerMonster ?? regularMonster;

        if (originalMonster == null)
        {
            Log.Warning("Cannot create ghost monster - no original monster found", "ReplayGem");
            return null;
        }

        var ghostMonster = new GhostMonster { Name = "GhostMonster" };

        CopyMonsterSpriteStructure(originalMonster, ghostMonster);
        AddGhostMonsterToScene(ghostMonster);

        Log.Debug($"Created ghost monster programmatically from {originalMonster.GetType().Name}", "ReplayGem");
        return ghostMonster;
    }

    private void CopyMonsterSpriteStructure(Node originalMonster, GhostMonster ghostMonster)
    {
        // For Monster with follower behavior, the sprite is either AnimatedSprite2D or found by the FindSprite() method
        var originalAnimSprite = originalMonster.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");

        if (originalAnimSprite != null)
        {
            var ghostSprite = new AnimatedSprite2D
            {
                Name = "AnimatedSprite2D", // Keep same name
                SpriteFrames = originalAnimSprite.SpriteFrames,
                Animation = "idle"
            };
            ghostMonster.AddChild(ghostSprite);
            Log.Debug("Copied Monster AnimatedSprite2D structure", "ReplayGem");
            return;
        }

        // Try MonsterSprite (for Monster class)
        var originalSprite = originalMonster.GetNodeOrNull<Sprite2D>("MonsterSprite");
        if (originalSprite != null)
        {
            var ghostSprite = new Sprite2D
            {
                Name = "MonsterSprite", // Keep same name  
                Texture = originalSprite.Texture
            };
            ghostMonster.AddChild(ghostSprite);
            Log.Debug("Copied Monster MonsterSprite structure", "ReplayGem");
            return;
        }

        Log.Error("Could not find monster sprite - will use fallback red square", "ReplayGem");
    }

    private void AddGhostMonsterToScene(GhostMonster ghostMonster)
    {
        // Add ghost monster to scene and group
        GetTree().CurrentScene.AddChild(ghostMonster);
        ghostMonster.AddToGroup("ghost_monster");
    }
    #endregion

    #region Cleanup - ORIGINAL METHOD PRESERVED + Monster Addition
    private void CleanupGhost()
    {
        // Clean up current ghost and ensure game state is restored - ORIGINAL METHOD
        CleanupCurrentGhost();
        CleanupCurrentGhostMonster();
        CleanupAllGhosts();
        EnsureEntitiesResumed();
        EnsureRecordingResumed();
    }

    private void CleanupCurrentGhost()
    {
        // Clean up tracked ghost reference - ORIGINAL METHOD
        if (_currentGhost != null && IsInstanceValid(_currentGhost))
        {
            _currentGhost.StopReplay();
            _currentGhost.QueueFree();
            _currentGhost = null;
            Log.Debug("Current ghost cleaned up", "ReplayGem");
        }
    }

    private void CleanupCurrentGhostMonster()
    {
        // Clean up tracked monster ghost reference
        if (_currentGhostMonster != null && IsInstanceValid(_currentGhostMonster))
        {
            _currentGhostMonster.StopReplay();
            _currentGhostMonster.QueueFree();
            _currentGhostMonster = null;
            Log.Debug("Current ghost monster cleaned up", "ReplayGem");
        }
    }

    private void CleanupAllGhosts()
    {
        // Clean up any other ghost players that might exist - ORIGINAL METHOD EXPANDED
        var allGhosts = GetTree().GetNodesInGroup("ghost_player");
        foreach (Node ghost in allGhosts)
        {
            if (IsInstanceValid(ghost))
            {
                ghost.QueueFree();
            }
        }

        // Added monster cleanup
        var allGhostMonsters = GetTree().GetNodesInGroup("ghost_monster");
        foreach (Node ghost in allGhostMonsters)
        {
            if (IsInstanceValid(ghost))
            {
                ghost.QueueFree();
            }
        }
    }

    private void EnsureEntitiesResumed()
    {
        // Ensure player is resumed and shown if cleanup is called directly
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player?.IsPaused() == true)
        {
            player.ResumePlayer();
            player.Show();
        }

        // Ensure only the monsters we hid are restored
        foreach (var monster in _hiddenMonsters)
        {
            if (IsInstanceValid(monster))
            {
                monster.Show();
            }
        }
        _hiddenMonsters.Clear();
    }

    private void EnsureRecordingResumed()
    {
        // Ensure recording is resumed if cleanup is called directly - FIXED: Force restart if needed
        if (ReplayRecorder.Instance != null)
        {
            ReplayRecorder.Instance.ResumeRecording();
            
            // If recording is not actually running, force restart
            if (!ReplayRecorder.Instance.IsRecording)
            {
                Log.Warning("Recording was not active, forcing restart", "ReplayGem");
                ReplayRecorder.Instance.RefreshCharacterReferences();
                ReplayRecorder.Instance.InitializeRecording();
            }
        }
    }

    public override void _ExitTree()
    {
        // Clean up when gem is removed from scene - ORIGINAL METHOD
        CleanupGhost();
    }
    #endregion
}