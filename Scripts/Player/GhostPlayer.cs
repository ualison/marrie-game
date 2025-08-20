using Godot;
using System.Collections.Generic;

// Ghost version of player that replays recorded movements
// Simplified for better performance and cleaner visual management
public partial class GhostPlayer : CharacterBody2D
{
	private List<ReplayFrame> _replayFrames;
	private int _currentFrameIndex;
	private float _replayStartTime;
	private bool _isReplaying;
	
	// Cached sprite references for performance
	private AnimatedSprite2D _animatedSprite;
	private Sprite2D _sprite;
	private Node2D _activeSprite;
	
	public override void _Ready()
	{
		SetupGhostVisuals();
		DisableCollisions();
		Hide();
	}
	
	public override void _Process(double delta)
	{
		if (!_isReplaying || _replayFrames == null) return;
		
		float currentTime = (float)Time.GetUnixTimeFromSystem() - _replayStartTime;
		var targetFrame = FindFrameAtTime(currentTime);
		
		if (targetFrame != null)
		{
			ApplyFrameToGhost(targetFrame);
		}
		
		// Stop replay when complete
		if (_currentFrameIndex >= _replayFrames.Count)
		{
			StopReplay();
		}
	}
	
	// Starts replaying recorded movement frames
	public void StartReplay(List<ReplayFrame> frames)
	{
		if (frames == null || frames.Count == 0)
		{
			GD.Print("GhostPlayer: No frames to replay");
			return;
		}
		
		_replayFrames = frames;
		_currentFrameIndex = 0;
		_replayStartTime = (float)Time.GetUnixTimeFromSystem();
		_isReplaying = true;
		
		Show();
		GD.Print($"GhostPlayer: Started replay with {frames.Count} frames");
	}
	
	// Stops replay and hides ghost
	public void StopReplay()
	{
		_isReplaying = false;
		Hide();
		GD.Print("GhostPlayer: Replay stopped");
	}
	
	// Checks if currently replaying
	public bool IsReplaying() => _isReplaying;
	
	private void SetupGhostVisuals()
	{
		DiscoverSpriteComponents();
		ApplyGhostEffect();
	}
	
	// Finds sprite components with fallback names
	private void DiscoverSpriteComponents()
	{
		// Try animated sprite first
		_animatedSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
		if (_animatedSprite != null)
		{
			_activeSprite = _animatedSprite;
			return;
		}
		
		// Check for various sprite node names
		_sprite = GetNodeOrNull<Sprite2D>("Sprite2D") 
			?? GetNodeOrNull<Sprite2D>("GhostMarrieSprite")
			?? GetNodeOrNull<Sprite2D>("MarrieSprite");
		
		if (_sprite != null)
		{
			_activeSprite = _sprite;
			return;
		}
		
		// Check if custom names are actually AnimatedSprite2D
		_animatedSprite = GetNodeOrNull<AnimatedSprite2D>("GhostMarrieSprite")
			?? GetNodeOrNull<AnimatedSprite2D>("MarrieSprite");
		
		_activeSprite = _animatedSprite;
	}
	
	private void ApplyGhostEffect()
	{
		if (_activeSprite != null)
		{
			// Semi-transparent blue tint for ghost effect
			_activeSprite.Modulate = new Color(0.5f, 0.5f, 1.0f, 0.6f);
			GD.Print($"GhostPlayer: Applied ghost effect to '{_activeSprite.Name}'");
		}
		else
		{
			GD.PrintErr("GhostPlayer: No sprite found for ghost effect");
		}
	}
	
	private void DisableCollisions()
	{
		SetCollisionLayerValue(1, false);
		SetCollisionMaskValue(1, false);
	}
	
	// Efficient frame lookup using linear search with index tracking
	private ReplayFrame FindFrameAtTime(float time)
	{
		// Linear search from current position (efficient for sequential playback)
		for (int i = _currentFrameIndex; i < _replayFrames.Count; i++)
		{
			if (_replayFrames[i].Timestamp >= time)
			{
				_currentFrameIndex = i;
				return _replayFrames[i];
			}
		}
		
		// Return last frame if past end
		return _replayFrames.Count > 0 ? _replayFrames[_replayFrames.Count - 1] : null;
	}
	
	// Applies frame data to ghost's visual state
	private void ApplyFrameToGhost(ReplayFrame frame)
	{
		// Update position and rotation
		GlobalPosition = frame.Position;
		GlobalRotation = frame.Rotation;
		
		// Apply sprite flip
		if (_activeSprite != null)
		{
			var currentScale = _activeSprite.Scale;
			_activeSprite.Scale = new Vector2(
				frame.SpriteFlip ? -Mathf.Abs(currentScale.X) : Mathf.Abs(currentScale.X),
				currentScale.Y
			);
		}
		
		// Play animation if available
		if (_animatedSprite != null && !string.IsNullOrEmpty(frame.Animation))
		{
			if (_animatedSprite.SpriteFrames?.HasAnimation(frame.Animation) == true)
			{
				if (_animatedSprite.Animation != frame.Animation)
					_animatedSprite.Play(frame.Animation);
			}
		}
	}
}
