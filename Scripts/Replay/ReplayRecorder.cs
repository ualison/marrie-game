using Godot;
using System.Collections.Generic;

// Movement recording system for ghost replay functionality
// Simplified in-memory only recording without save system dependency
public partial class ReplayRecorder : Node
{
	public static ReplayRecorder Instance { get; private set; }

	[Export] public float RecordInterval { get; set; } = 0.1f;

	public bool IsRecording { get; private set; }

	// In-memory frame storage
	private readonly List<ReplayFrame> _playerFrames = new();
	private readonly List<ReplayFrame> _monsterFrames = new();

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

		_recordTimer += (float)delta;

		if (_recordTimer >= RecordInterval)
		{
			CaptureCurrentFrame();
			_recordTimer = 0.0f;
		}
	}

	private void CaptureCurrentFrame()
	{
		// Use total elapsed time since recording started
		float currentTime = Time.GetTicksMsec() / 1000.0f - _startTime;

		// Record player state
		if (_player != null)
		{
			_playerFrames.Add(CreateFrameForCharacter(_player, currentTime));
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
		GD.Print("Recording data cleared");
	}

	private void InitializeRecording()
	{
		if (GetTree() == null)
		{
			CallDeferred(nameof(InitializeRecording));
			return;
		}

		DiscoverCharacters();

		if (_player != null)
		{
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
		IsRecording = true;
		_recordTimer = 0f;
		_startTime = Time.GetTicksMsec() / 1000.0f; // Store start time in seconds
		GD.Print("ReplayRecorder: Started recording");
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
}