using Godot;
using System.Collections.Generic;

// Movement recording system for ghost replay functionality
// Simplified in-memory only recording without save system dependency
public partial class ReplayRecorder : Node
{
	public static ReplayRecorder Instance { get; private set; }

	[Export] public float RecordInterval { get; set; } = 0.1f;

	private float _pauseStartTime; // Add this line

	public bool IsRecording { get; private set; }
	private bool _isPausedForReplay = false;

	// In-memory frame storage
	private readonly List<ReplayFrame> _playerFrames = new();
	private readonly List<ReplayFrame> _monsterFrames = new();

private Vector2 _lastPlayerPosition = Vector2.Zero;
private bool _playerWasMoving = true;

// In-memory frame storage

	private float _recordTimer;
	private float _startTime;

	// Cached component references
	private Player _player;
	private Monster _monster;

	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			ProcessMode = ProcessModeEnum.Always;
			// Don't reparent - just mark as persistent
			CallDeferred(nameof(InitializeRecording));
		}
		else
		{
			if (Instance != this)
			{
				QueueFree();
				return; 
			}
			CallDeferred(nameof(InitializeRecording));
		}
	}
	public override void _Process(double delta)
	{
		if (!IsRecording) return;

		// Check if player is actually moving
		bool playerIsMoving = CheckPlayerMovement();

		// Don't record if paused for replay OR if player is frozen
		if (_isPausedForReplay || !playerIsMoving) return;

		_recordTimer += (float)delta;

		if (_recordTimer >= RecordInterval)
		{
			CaptureCurrentFrame();
			_recordTimer = 0.0f;
		}
	}
private bool CheckPlayerMovement()
{
    if (_player == null) return false;

    Vector2 currentPosition = _player.GlobalPosition;
    bool isMoving = currentPosition.DistanceTo(_lastPlayerPosition) > 0.1f;
    
    // Update last position if player is moving
    if (isMoving)
    {
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
    // Use total elapsed time since recording started
    float currentTime = Time.GetTicksMsec() / 1000.0f - _startTime;

    // Record player state
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

    // Record monster state
    if (_monster != null)
    {
        _monsterFrames.Add(CreateFrameForCharacter(_monster, currentTime));
    }
}
	// Stops recording and logs final frame count
	public void StopRecording()
	{
		IsRecording = false;
		GD.Print($"Recording stopped. Player: {_playerFrames.Count}, Monster: {_monsterFrames.Count} frames");
	}

	// Returns copy of player frames for replay
	public List<ReplayFrame> GetPlayerFrames() => new(_playerFrames);

	// Returns copy of monster frames for replay
	public List<ReplayFrame> GetMonsterFrames() => new(_monsterFrames);

	// Checks if any data has been recorded
	public bool HasRecordedData() => _playerFrames.Count > 0 || _monsterFrames.Count > 0;

	// Clears all recorded data for new session
	public void ClearRecording()
	{
		_playerFrames.Clear();
		_monsterFrames.Clear();
		_recordTimer = 0f;
		GD.Print("Recording data cleared - ready for new session");
	}
	public void InitializeRecording()
	{
		if (GetTree() == null)
		{
			CallDeferred(nameof(InitializeRecording));
			return;
		}

		DiscoverCharacters();

		if (_player != null)
		{
			// Always start fresh recording for new session
			StartRecording();
		}
	}

	private void DiscoverCharacters()
	{
		_player = GetTree().GetFirstNodeInGroup("player") as Player;
		_monster = GetTree().GetFirstNodeInGroup("monsters") as Monster;

		GD.Print($"ReplayRecorder discovered - Player: {_player != null}, Monster: {_monster != null}");
	}

private void StartRecording()
{
    // Clear any previous recording data first
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
	// Creates replay frame from character's current state
	private ReplayFrame CreateFrameForCharacter(Node2D character, float timestamp)
	{
		return new ReplayFrame
		{
			Timestamp = timestamp,
			Position = character.GlobalPosition,
			Rotation = character.GlobalRotation,
			Animation = ExtractCurrentAnimation(character),
			SpriteFlip = ExtractSpriteFlip(character)
		};
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	// Extracts current animation from AnimatedSprite2D
	private string ExtractCurrentAnimation(Node2D character)
	{
		var animSprite = character.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
		return animSprite?.IsPlaying() == true ? animSprite.Animation : "idle";
	}

	// Extracts sprite flip state from any sprite component
	private bool ExtractSpriteFlip(Node2D character)
	{
		// Check common sprite node names
		var sprite = character.GetNodeOrNull<Node2D>("AnimatedSprite2D")
			?? character.GetNodeOrNull<Node2D>("Sprite2D")
			?? character.GetNodeOrNull<Node2D>("MarrieSprite")
			?? character.GetNodeOrNull<Node2D>("MonsterSprite");

		return sprite?.Scale.X < 0;
	}

public void PauseRecording()
{
    _isPausedForReplay = true;
    
    // Store the exact time when we paused to resume timeline correctly
    _pauseStartTime = Time.GetTicksMsec() / 1000.0f;
    
    GD.Print("Recording paused for replay");
}

public void ResumeRecording()
{
    if (_isPausedForReplay)
    {
        // Calculate how long we were paused and adjust start time
        float pauseDuration = (Time.GetTicksMsec() / 1000.0f) - _pauseStartTime;
        _startTime += pauseDuration; // Shift start time to maintain timeline continuity
        
        _isPausedForReplay = false;
        GD.Print($"Recording resumed after {pauseDuration:F2}s pause");
    }
}
}