using Godot;

// Replay gem that appears at death location and allows ghost replay
// Works with DetectionArea that has CollisionShape2D as child
public partial class ReplayGem : Node2D
{
    private Area2D _detectionArea;
    private Sprite2D _gemSprite;
    private Label _promptLabel;
    private CollisionShape2D _collisionShape;
    private bool _playerNearby = false;

    public override void _Ready()
    {
        // Get required child nodes
        _detectionArea = GetNode<Area2D>("DetectionArea");
        _gemSprite = GetNode<Sprite2D>("GemSprite");
        _promptLabel = GetNode<Label>("PromptLabel");

        // Get CollisionShape2D from DetectionArea (it's already there)
        _collisionShape = _detectionArea.GetNode<CollisionShape2D>("CollisionShape2D");

        // Create a circle shape if none exists
        if (_collisionShape.Shape == null)
        {
            _collisionShape.Shape = new CircleShape2D { Radius = 50.0f };
        }

        // Connect signals
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;

        // Setup prompt label
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";

        // Ensure the gem starts hidden
        _detectionArea.SetDeferred("monitoring", false);
        _promptLabel.Hide();
        _gemSprite.Hide();

        GD.Print("ReplayGem ready - hidden until activated");
    }

    public override void _Input(InputEvent @event)
    {
        if (!_playerNearby) return;

        if (@event.IsActionPressed("ui_accept"))
        {
            StartReplay();
        }
    }

    // Called by GameManager to show gem at death position
    public void ShowGem(Vector2 position)
    {
        GlobalPosition = position;
        Show();

        if (_gemSprite != null)
        {
            _gemSprite.Show();
        }

        if (_promptLabel != null)
        {
            _promptLabel.Hide(); // only show when player nearby
        }

        _detectionArea.SetDeferred("monitoring", true);
        GD.Print($"ReplayGem shown at {position}, scale={Scale}");
    }

    // Hides gem completely
    public void HideGem()
    {
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
    }

    private void OnPlayerEntered(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            if (_promptLabel != null)
            {
                _promptLabel.Show();
            }
            GD.Print("Player nearby - showing prompt");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            GD.Print("Player left - hiding prompt");
        }
    }

    private void StartReplay()
    {
        if (ReplayRecorder.Instance?.HasRecordedData() != true)
        {
            GD.Print("No replay data available");
            return;
        }

        var playerFrames = ReplayRecorder.Instance.GetPlayerFrames();
        if (playerFrames.Count == 0)
        {
            GD.Print("No player frames to replay");
            return;
        }

        // Find ghost player and start replay
        var ghostPlayer = GetTree().GetFirstNodeInGroup("ghost_player") as GhostPlayer;
        if (ghostPlayer != null)
        {
            ghostPlayer.StartReplay(playerFrames);
            if (_promptLabel != null)
            {
                _promptLabel.Hide();
            }
            GD.Print("Started ghost replay");
        }
        else
        {
            GD.Print("No ghost player found for replay");
        }
    }
}