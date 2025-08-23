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
    private Monster _monster;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup singleton pattern with persistence
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            CallDeferred(nameof(InitializeRecording));
        }
        else if (Instance != this)
        {
            // Remove duplicate instances
            QueueFree();
            return;
        }
        else
        {
            // Reinitialize existing instance
            CallDeferred(nameof(InitializeRecording));
        }
    }

    public void InitializeRecording()
    {
        // Ensure tree is available before initializing
        if (GetTree() == null)
        {
            CallDeferred(nameof(InitializeRecording));
            return;
        }

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
        _monster = GetTree().GetFirstNodeInGroup("enemies") as Monster;

        GD.Print($"ReplayRecorder discovered - Player: {_player != null}, Monster: {_monster != null}");
    }
    #endregion

    #region Core Recording Loop
    public override void _Process(double delta)
    {
        // Skip processing if not recording
        if (!IsRecording) return;

        // Check if player is actually moving to avoid recording static frames
        bool playerIsMoving = CheckPlayerMovement();

        // Don't record if paused for replay or player is stationary
        if (_isPausedForReplay || !playerIsMoving) return;

        _recordTimer += (float)delta;

        // Record frame at specified intervals
        if (_recordTimer >= RecordInterval)
        {
            CaptureCurrentFrame();
            _recordTimer = 0.0f;
        }
    }

    private bool CheckPlayerMovement()
    {
        // Determine if player has moved since last check
        if (_player == null) return false;

        Vector2 currentPosition = _player.GlobalPosition;
        bool isMoving = currentPosition.DistanceTo(_lastPlayerPosition) > MIN_MOVEMENT_THRESHOLD;

        if (isMoving)
        {
            // Player is moving - update last position
            _lastPlayerPosition = currentPosition;
            _playerWasMoving = true;
        }
        else if (_playerWasMoving)
        {
            // Player just stopped - record one final frame
            _playerWasMoving = false;
            return true;
        }

        return isMoving;
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

            // Debug: Print every 10th frame to check for gaps
            if (_playerFrames.Count % 10 == 0)
            {
                GD.Print($"Frame {_playerFrames.Count}: Time={currentTime:F2}s, Pos={frame.Position}");
            }
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

        // Initialize movement tracking
        if (_player != null)
        {
            _lastPlayerPosition = _player.GlobalPosition;
            _playerWasMoving = true;
        }

        GD.Print("ReplayRecorder: Started new recording session");
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