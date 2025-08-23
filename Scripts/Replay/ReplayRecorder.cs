using Godot;
using System.Collections.Generic;

// Movement recording system for ghost replay functionality with pause/resume support
public partial class ReplayRecorder : Node
{
    #region Singleton
    public static ReplayRecorder Instance { get; private set; }
    #endregion

    #region Configuration
    [Export] public float RecordInterval { get; set; } = 0.1f;
    private const float MIN_MOVEMENT_THRESHOLD = 0.1f; // Minimum distance to consider movement
    #endregion

    #region State Management
    public bool IsRecording { get; private set; }
    private bool _isPausedForReplay = false;
    private float _pauseStartTime;
    private float _recordTimer;
    private float _startTime;
    #endregion

    #region Movement Tracking
    private Vector2 _lastPlayerPosition = Vector2.Zero;
    private bool _playerWasMoving = true;
    #endregion

    #region Frame Storage
    private readonly List<ReplayFrame> _playerFrames = new();
    private readonly List<ReplayFrame> _monsterFrames = new();
    #endregion

    #region Character References
    private Player _player;
    private Node2D _monster; // Can be Monster or FollowerMonster
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Simple setup for scene node - no singleton needed
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        
        GD.Print($"ReplayRecorder _Ready: {Name}, Parent: {GetParent()?.Name}");
        
        // Initialize immediately
        DiscoverCharacters();
        
        // Start recording if player is available
        if (_player != null)
        {
            StartRecording();
        }
        else
        {
            // Retry in next frame if player not found yet
            GetTree().CreateTimer(0.1f).Timeout += () => {
                if (_player == null) {
                    DiscoverCharacters();
                    if (_player != null) {
                        StartRecording();
                    }
                }
            };
        }
    }

    public void InitializeRecording()
    {
        // Simple initialization without complex deferred calling
        DiscoverCharacters();

        // Start recording if player is available
        if (_player != null)
        {
            StartRecording();
        }
    }

    private void DiscoverCharacters()
    {
        // Find player and monster characters in the scene
        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        
        // Try FollowerMonster first, then regular Monster
        _monster = GetTree().GetFirstNodeInGroup("enemies") as FollowerMonster;
        if (_monster == null)
        {
            _monster = GetTree().GetFirstNodeInGroup("enemies") as Monster;
        }

        GD.Print($"ReplayRecorder discovered - Player: {_player != null}, Monster: {_monster != null} ({_monster?.GetType().Name})");
    }
    #endregion

    #region Core Recording Loop
    public override void _Process(double delta)
    {
        // Always print this to verify _Process is being called
        if (_playerFrames.Count < 5) // Only first 5 times to avoid spam
        {
            GD.Print($"_Process called: IsRecording={IsRecording}, Paused={_isPausedForReplay}, Timer={_recordTimer:F2}");
        }

        // Skip processing if not recording
        if (!IsRecording) 
        {
            return;
        }

        // Don't record if paused for replay
        if (_isPausedForReplay) return;

        _recordTimer += (float)delta;

        // Record frame at specified intervals
        if (_recordTimer >= RecordInterval)
        {
            GD.Print($"RECORDING FRAME! Timer={_recordTimer:F2}");
            CaptureCurrentFrame();
            _recordTimer = 0.0f;
        }
    }

    private void CaptureCurrentFrame()
    {
        // Use total elapsed time since recording started for consistent timeline
        float currentTime = Time.GetTicksMsec() / 1000.0f - _startTime;

        // Record player state if available
        if (_player != null)
        {
            var frame = CreateFrameForCharacter(_player, currentTime);
            _playerFrames.Add(frame);

            // Debug: Print every frame to check recording
            GD.Print($"Frame {_playerFrames.Count}: Time={currentTime:F2}s, Pos={frame.Position}");
        }

        // Record monster state if available
        if (_monster != null)
        {
            _monsterFrames.Add(CreateFrameForCharacter(_monster, currentTime));
        }
    }
    #endregion

    #region Recording Control
    public void StartRecording()
    {
        // Clear previous data and start fresh recording session
        ClearRecording();

        IsRecording = true;
        _recordTimer = 0f;
        _startTime = Time.GetTicksMsec() / 1000.0f;

        GD.Print($"ReplayRecorder: Started new recording session - IsRecording={IsRecording}, Player exists: {_player != null}");
        GD.Print($"Process mode: {ProcessMode}, Tree exists: {GetTree() != null}");
        GD.Print($"Singleton check: Instance == this: {Instance == this}, Instance exists: {Instance != null}");
        GD.Print($"Node name: {Name}, Parent: {GetParent()?.Name}, IsInsideTree: {IsInsideTree()}");
    }

    public void StopRecording()
    {
        // End recording and log final frame count
        IsRecording = false;
        GD.Print($"Recording stopped. Player: {_playerFrames.Count}, Monster: {_monsterFrames.Count} frames");
    }

    public void ClearRecording()
    {
        // Clear all recorded data for new session
        _playerFrames.Clear();
        _monsterFrames.Clear();
        _recordTimer = 0f;
        GD.Print("Recording data cleared - ready for new session");
    }

    public void PauseRecording()
    {
        // Pause recording for replay mode
        _isPausedForReplay = true;
        _pauseStartTime = Time.GetTicksMsec() / 1000.0f;
        GD.Print("Recording paused for replay");
    }

    public void ResumeRecording()
    {
        // Resume recording and adjust timeline to maintain continuity
        if (_isPausedForReplay)
        {
            // Calculate pause duration and adjust start time
            float pauseDuration = (Time.GetTicksMsec() / 1000.0f) - _pauseStartTime;
            _startTime += pauseDuration; // Shift start time to maintain timeline continuity

            _isPausedForReplay = false;
            GD.Print($"Recording resumed after {pauseDuration:F2}s pause");
        }
    }
    #endregion

    #region Data Access
    public List<ReplayFrame> GetPlayerFrames() => new(_playerFrames); // Return copy of player frames
    public List<ReplayFrame> GetMonsterFrames() => new(_monsterFrames); // Return copy of monster frames
    public bool HasRecordedData() => _playerFrames.Count > 0 || _monsterFrames.Count > 0; // Check if data exists
    #endregion

    #region Frame Creation
    private ReplayFrame CreateFrameForCharacter(Node2D character, float timestamp)
    {
        // Create replay frame from character's current state
        return new ReplayFrame
        {
            Timestamp = timestamp,
            Position = character.GlobalPosition,
            Rotation = character.GlobalRotation,
            Animation = ExtractCurrentAnimation(character),
            SpriteFlip = ExtractSpriteFlip(character)
        };
    }

    private string ExtractCurrentAnimation(Node2D character)
    {
        // Get current animation from AnimatedSprite2D if available
        var animSprite = character.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        return animSprite?.IsPlaying() == true ? animSprite.Animation : "idle";
    }

    private bool ExtractSpriteFlip(Node2D character)
    {
        // Check sprite flip state from any sprite component
        var sprite = character.GetNodeOrNull<Node2D>("AnimatedSprite2D")
            ?? character.GetNodeOrNull<Node2D>("Sprite2D")
            ?? character.GetNodeOrNull<Node2D>("MarrieSprite")
            ?? character.GetNodeOrNull<Node2D>("MonsterSprite");

        return sprite?.Scale.X < 0;
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        // Clear singleton reference when exiting
        if (Instance == this)
        {
            Instance = null;
        }
    }
    #endregion
}