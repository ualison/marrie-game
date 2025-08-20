using Godot;
using System.Collections.Generic;

// Ghost version of player that replays recorded movements
// Enhanced with better error handling and visual feedback
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

		// Add debug info
		GD.Print($"GhostPlayer ready: {Name}, Parent: {GetParent()?.Name}");
	}

	public override void _Process(double delta)
	{
		if (!_isReplaying || _replayFrames == null) return;

		_replayStartTime += (float)delta;
		var targetFrame = FindFrameAtTime(_replayStartTime);

		if (targetFrame != null)
		{
			ApplyFrameToGhost(targetFrame);
		}
		else
		{
			// No more frames - stop replay
			StopReplay();
		}
	}

	public void StartReplay(List<ReplayFrame> frames)
	{
		if (frames == null || frames.Count == 0)
		{
			GD.Print("GhostPlayer: No frames to replay");
			return;
		}

		_replayFrames = frames;
		_currentFrameIndex = 0;
		_replayStartTime = 0f; // Start from 0
		_isReplaying = true;

		// Set starting position to first frame
		if (frames.Count > 0)
		{
			GlobalPosition = frames[0].Position;
		}

		Show();
		GD.Print($"GhostPlayer: Started replay with {frames.Count} frames");
		GD.Print($"GhostPlayer: First frame position: {frames[0].Position}");
		GD.Print($"GhostPlayer: Ghost position: {GlobalPosition}");
		GD.Print($"GhostPlayer: Ghost visible: {Visible}");
	}

	private ReplayFrame FindFrameAtTime(float time)
	{
		// Find the frame that should be playing at this time
		for (int i = _currentFrameIndex; i < _replayFrames.Count; i++)
		{
			if (_replayFrames[i].Timestamp >= time)
			{
				_currentFrameIndex = i;
				return _replayFrames[i];
			}
		}

		// If we've gone past all frames, return null to signal end
		return null;
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
			GD.Print($"GhostPlayer: Found AnimatedSprite2D: {_animatedSprite.Name}");
			return;
		}

		// Check for various sprite node names
		_sprite = GetNodeOrNull<Sprite2D>("Sprite2D")
			?? GetNodeOrNull<Sprite2D>("GhostMarrieSprite")
			?? GetNodeOrNull<Sprite2D>("MarrieSprite");

		if (_sprite != null)
		{
			_activeSprite = _sprite;
			GD.Print($"GhostPlayer: Found Sprite2D: {_sprite.Name}");
			return;
		}

		// Check if custom names are actually AnimatedSprite2D
		_animatedSprite = GetNodeOrNull<AnimatedSprite2D>("GhostMarrieSprite")
			?? GetNodeOrNull<AnimatedSprite2D>("MarrieSprite");

		if (_animatedSprite != null)
		{
			_activeSprite = _animatedSprite;
			GD.Print($"GhostPlayer: Found custom AnimatedSprite2D: {_animatedSprite.Name}");
		}
		else
		{
			GD.PrintErr("GhostPlayer: No sprite component found!");

			// List all children for debugging
			GD.Print("GhostPlayer children:");
			foreach (Node child in GetChildren())
			{
				GD.Print($"  - {child.Name} ({child.GetType().Name})");
			}
		}
	}

	private void ApplyGhostEffect()
	{
		if (_activeSprite != null)
		{
			// Semi-transparent blue tint for ghost effect
			_activeSprite.Modulate = new Color(0.5f, 0.5f, 1.0f, 0.7f);
			_activeSprite.Show();
			GD.Print($"GhostPlayer: Applied ghost effect to '{_activeSprite.Name}'");
		}
		else
		{
			GD.PrintErr("GhostPlayer: No sprite found for ghost effect - creating fallback");
			CreateFallbackVisual();
		}
	}

	// Creates a simple visual fallback if no sprite is found
	private void CreateFallbackVisual()
	{
		var fallbackSprite = new Sprite2D();
		fallbackSprite.Name = "FallbackGhostSprite";

		// Create a simple colored rectangle texture
		var imageTexture = new ImageTexture();
		var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
		image.Fill(new Color(0, 0, 1)); // Blue color
		imageTexture.SetImage(image);

		fallbackSprite.Texture = imageTexture;
		fallbackSprite.Modulate = new Color(0.5f, 0.5f, 1.0f, 0.7f);

		AddChild(fallbackSprite);
		_activeSprite = fallbackSprite;

		GD.Print("GhostPlayer: Created fallback visual");
	}

	private void DisableCollisions()
	{
		SetCollisionLayerValue(1, false);
		SetCollisionMaskValue(1, false);
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