using Godot;

// Player health display with flexible heart representation and automatic PlayerLife connection
public partial class LifeBarUI : Control
{
    #region Configuration
    [Export] public Texture2D FullHeartTexture { get; set; }
    [Export] public Texture2D EmptyHeartTexture { get; set; }
    #endregion

    #region Components
    private HBoxContainer _heartsContainer;
    private PlayerLife _lifeSystem;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup hearts container and find life system
        InitializeContainer();
        CallDeferred(nameof(FindLifeSystem));
    }

    private void InitializeContainer()
    {
        // Ensure hearts container exists in scene structure
        _heartsContainer = GetNode<HBoxContainer>("HeartsContainer");

        if (_heartsContainer == null)
        {
            GD.PrintErr("LifeBarUI: HBoxContainer 'HeartsContainer' not found!");
            GD.PrintErr("Create an HBoxContainer child named 'HeartsContainer'");
        }
    }

    private void FindLifeSystem()
    {
        // Automatically discover and connect to player's PlayerLife
        _lifeSystem = DiscoverPlayerLife();

        if (_lifeSystem != null)
        {
            ConnectToLifeSystem();
            GD.Print("LifeBarUI: Connected to PlayerLife!");
        }
        else
        {
            LogLifeSystemError();
        }
    }

    private PlayerLife DiscoverPlayerLife()
    {
        // Try multiple strategies to find PlayerLife component
        return FindLifeSystemViaPlayer()
            ?? FindLifeSystemViaPath()
            ?? FindLifeSystemViaGroup();
    }

    private PlayerLife FindLifeSystemViaPlayer()
    {
        // Strategy 1: Group-based search (most reliable)
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        return player?.GetNodeOrNull<PlayerLife>("PlayerLife");
    }

    private PlayerLife FindLifeSystemViaPath()
    {
        // Strategy 2: Safe node path search with type checking
        var playerNode = GetNodeOrNull("../../Characters/Player");
        if (playerNode is Player playerDirect)
            return playerDirect.GetNodeOrNull<PlayerLife>("PlayerLife");
        
        return null;
    }

    private PlayerLife FindLifeSystemViaGroup()
    {
        // Strategy 3: Search by group membership
        return GetTree().GetFirstNodeInGroup("life_system") as PlayerLife;
    }

    private void ConnectToLifeSystem()
    {
        // Connect to health change events for real-time updates
        _lifeSystem.LifeChanged += OnLifeChanged;
        OnLifeChanged(_lifeSystem.CurrentLife, _lifeSystem.MaxLife);
    }

    private void LogLifeSystemError()
    {
        // Log error with helpful debugging information
        GD.PrintErr("LifeBarUI: PlayerLife not found!");
        GD.PrintErr("Make sure Player node has the Player.cs script attached!");
    }
    #endregion

    #region Heart Display Management
    private void OnLifeChanged(int currentLife, int maxLife)
    {
        // Respond to health changes from PlayerLife
        UpdateHearts(currentLife, maxLife);
    }

    private void UpdateHearts(int currentLife, int maxLife)
    {
        // Rebuild heart display based on current health state
        if (_heartsContainer == null) return;

        ClearExistingHearts();
        CreateNewHearts(currentLife, maxLife);
    }

    private void ClearExistingHearts()
    {
        // Clear existing hearts to prevent accumulation
        foreach (Node child in _heartsContainer.GetChildren())
            child.QueueFree();
    }

    private void CreateNewHearts(int currentLife, int maxLife)
    {
        // Choose display mode based on available resources
        if (HasHeartTextures())
            CreateTextureHearts(currentLife, maxLife);
        else
            CreateTextHearts(currentLife, maxLife);
    }

    private bool HasHeartTextures()
    {
        // Check if both heart textures are available
        return FullHeartTexture != null && EmptyHeartTexture != null;
    }

    private void CreateTextureHearts(int currentLife, int maxLife)
    {
        // Create hearts using texture images for polished visual appearance
        for (int i = 0; i < maxLife; i++)
        {
            var heart = CreateTextureHeart(i, currentLife);
            _heartsContainer.AddChild(heart);
        }
    }

    private TextureRect CreateTextureHeart(int index, int currentLife)
    {
        // Create individual texture heart
        var heart = new TextureRect
        {
            Texture = index < currentLife ? FullHeartTexture : EmptyHeartTexture,
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(32, 32)
        };

        return heart;
    }

    private void CreateTextHearts(int currentLife, int maxLife)
    {
        // Fallback display using Unicode heart characters
        for (int i = 0; i < maxLife; i++)
        {
            var heartLabel = CreateTextHeart(i, currentLife);
            _heartsContainer.AddChild(heartLabel);
        }
    }

    private Label CreateTextHeart(int index, int currentLife)
    {
        // Create individual text heart with appropriate styling
        var heartLabel = new Label();

        if (index < currentLife)
        {
            // Filled heart for remaining life
            heartLabel.Text = "♥";
            heartLabel.AddThemeColorOverride("font_color", Colors.Red);
        }
        else
        {
            // Empty heart for lost life
            heartLabel.Text = "♡";
            heartLabel.AddThemeColorOverride("font_color", Colors.Gray);
        }

        heartLabel.AddThemeFontSizeOverride("font_size", 24);
        return heartLabel;
    }
    #endregion

    #region Public API
    public void SetHeartTextures(Texture2D fullHeart, Texture2D emptyHeart)
    {
        // Allow external assignment of heart textures
        FullHeartTexture = fullHeart;
        EmptyHeartTexture = emptyHeart;

        // Refresh display if already connected to life system
        if (_lifeSystem != null)
            UpdateHearts(_lifeSystem.CurrentLife, _lifeSystem.MaxLife);
    }

    public void ConnectToLifeSystem(PlayerLife lifeSystem)
    {
        // Manually connect to specific PlayerLife instance
        DisconnectFromCurrentLifeSystem();
        EstablishNewConnection(lifeSystem);
    }

    private void DisconnectFromCurrentLifeSystem()
    {
        // Clean up previous connection to prevent duplicate events
        if (_lifeSystem != null)
            _lifeSystem.LifeChanged -= OnLifeChanged;
    }

    private void EstablishNewConnection(PlayerLife lifeSystem)
    {
        // Establish new connection and update display
        _lifeSystem = lifeSystem;

        if (_lifeSystem != null)
        {
            _lifeSystem.LifeChanged += OnLifeChanged;
            OnLifeChanged(_lifeSystem.CurrentLife, _lifeSystem.MaxLife);
        }
    }
    #endregion
}
