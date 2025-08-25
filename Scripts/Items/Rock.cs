using Godot;

/// <summary>
/// Rock item that can be collected by the player and stored in inventory
/// Simple collectible item that adds to the player's rock count
/// </summary>
public partial class Rock : Node2D
{
    #region Exported Properties
    [Export] public Texture2D RockTexture { get; set; }
    [Export] public int RockValue { get; set; } = 1; // Number of rocks this item gives
    #endregion

    #region UI Components
    private Area2D _detectionArea;
    private Sprite2D _rockSprite;
    private Label _promptLabel;
    #endregion

    #region State
    private bool _playerNearby = false;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        InitializeComponents();
        ConnectSignals();
        SetupPromptLabel();
        EnableInputProcessing();

        Log.System("Rock item ready", "Rock");
    }

    private void InitializeComponents()
    {
        _detectionArea = GetNode<Area2D>("DetectionArea");
        _rockSprite = GetNode<Sprite2D>("RockSprite");
        _promptLabel = GetNode<Label>("PromptLabel");
    }

    private void ConnectSignals()
    {
        _detectionArea.BodyEntered += OnPlayerEntered;
        _detectionArea.BodyExited += OnPlayerExited;
    }

    private void SetupPromptLabel()
    {
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _promptLabel.Text = "Press E";
        _promptLabel.Hide();
    }

    private void EnableInputProcessing()
    {
        SetProcess(true);
    }
    #endregion

    #region Input Processing
    public override void _Process(double delta)
    {
        if (!_playerNearby) return;

        if (Input.IsActionJustPressed("interact"))
        {
            HandleRockCollection();
        }
    }

    private void HandleRockCollection()
    {
        Log.UI($"Collecting rock - adding {RockValue} rocks to inventory", "Rock");
        
        // Find inventory and add rocks
        var inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;
        
        if (inventory != null)
        {
            inventory.AddRocks(RockValue);
            Log.UI($"Rocks added to inventory. Total rocks: {inventory.GetRockCount()}", "Rock");
        }
        else
        {
            Log.Warning("Inventory not found", "Rock");
        }

        // Remove rock after collection
        DestroyRock();
    }

    private void DestroyRock()
    {
        Log.UI("Rock collected and destroyed", "Rock");
        QueueFree();
    }
    #endregion

    #region Player Detection
    private void OnPlayerEntered(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            _promptLabel.Show();
            Log.UI("Player nearby rock - showing prompt", "Rock");
        }
    }

    private void OnPlayerExited(Node2D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            _promptLabel.Hide();
            Log.UI("Player left rock - hiding prompt", "Rock");
        }
    }
    #endregion

    #region Public API
    public bool IsCollectible() => true;
    
    public int GetRockValue() => RockValue;
    #endregion
}
