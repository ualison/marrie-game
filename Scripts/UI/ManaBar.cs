using Godot;

// Player mana display with flexible heart representation and automatic PlayerMana connection
public partial class ManaBar : Control
{
    #region Configuration
    [Export] public Texture2D FullManaTexture { get; set; }
    [Export] public Texture2D EmptyManaTexture { get; set; }
    #endregion

    #region Components
    private HBoxContainer _manaContainer;
    private PlayerMana _manaSystem;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup mana container and find mana system
        InitializeContainer();
        CallDeferred(nameof(FindManaSystem));
    }

    private void InitializeContainer()
    {
        // Ensure mana container exists in scene structure
        _manaContainer = GetNode<HBoxContainer>("ManaContainer");

        if (_manaContainer == null)
        {
            Log.Error("HBoxContainer 'ManaContainer' not found!", "ManaBarUI");
            Log.Error("Create an HBoxContainer child named 'ManaContainer'", "ManaBarUI");
        }
    }

    private void FindManaSystem()
    {
        // Automatically discover and connect to player's PlayerMana
        _manaSystem = DiscoverPlayerMana();

        if (_manaSystem != null)
        {
            ConnectToManaSystem();
            Log.UI("Connected to PlayerMana!", "ManaBarUI");
        }
        else
        {
            LogManaSystemError();
        }
    }

    private PlayerMana DiscoverPlayerMana()
    {
        // Try multiple strategies to find PlayerMana component
        return FindManaSystemViaPlayer()
            ?? FindManaSystemViaPath()
            ?? FindManaSystemViaGroup();
    }

    private PlayerMana FindManaSystemViaPlayer()
    {
        // Strategy 1: Group-based search (most reliable)
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        return player?.GetNodeOrNull<PlayerMana>("PlayerMana");
    }

    private PlayerMana FindManaSystemViaPath()
    {
        // Strategy 2: Safe node path search with type checking
        var playerNode = GetNodeOrNull("../../Characters/Player");
        if (playerNode is Player playerDirect)
            return playerDirect.GetNodeOrNull<PlayerMana>("PlayerMana");
        
        return null;
    }

    private PlayerMana FindManaSystemViaGroup()
    {
        // Strategy 3: Search by group membership
        return GetTree().GetFirstNodeInGroup("mana_system") as PlayerMana;
    }

    private void ConnectToManaSystem()
    {
        // Connect to mana change events for real-time updates
        _manaSystem.ManaChanged += OnManaChanged;
        OnManaChanged(_manaSystem.CurrentMana, _manaSystem.MaxMana);
    }

    private void LogManaSystemError()
    {
        // Log error with helpful debugging information
        Log.Error("PlayerMana not found!", "ManaBarUI");
        Log.Error("Make sure Player node has the PlayerMana.cs script attached!", "ManaBarUI");
    }
    #endregion

    #region Mana Display Management
    private void OnManaChanged(int currentMana, int maxMana)
    {
        // Respond to mana changes from PlayerMana
        UpdateManaDisplay(currentMana, maxMana);
    }

    private void UpdateManaDisplay(int currentMana, int maxMana)
    {
        // Rebuild mana display based on current mana state
        if (_manaContainer == null) return;

        ClearExistingMana();
        CreateNewMana(currentMana, maxMana);
    }

    private void ClearExistingMana()
    {
        // Clear existing mana display to prevent accumulation
        foreach (Node child in _manaContainer.GetChildren())
            child.QueueFree();
    }

    private void CreateNewMana(int currentMana, int maxMana)
    {
        // Choose display mode based on available resources
        if (HasManaTextures())
            CreateTextureManaPieces(currentMana, maxMana);
        else
            CreateTextManaLabels(currentMana, maxMana);
    }

    private bool HasManaTextures()
    {
        // Check if both mana textures are available
        return FullManaTexture != null && EmptyManaTexture != null;
    }

    private void CreateTextureManaPieces(int currentMana, int maxMana)
    {
        // Create mana display using texture images for polished visual appearance
        for (int i = 0; i < maxMana; i++)
        {
            var mana = CreateSingleTextureMana(i, currentMana);
            _manaContainer.AddChild(mana);
        }
    }

    private TextureRect CreateSingleTextureMana(int index, int currentMana)
    {
        // Create individual texture mana
        var mana = new TextureRect
        {
            Texture = index < currentMana ? FullManaTexture : EmptyManaTexture,
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(32, 32)
        };

        return mana;
    }

    private void CreateTextManaLabels(int currentMana, int maxMana)
    {
        // Fallback display using Unicode mana characters
        for (int i = 0; i < maxMana; i++)
        {
            var manaLabel = CreateSingleTextMana(i, currentMana);
            _manaContainer.AddChild(manaLabel);
        }
    }

    private Label CreateSingleTextMana(int index, int currentMana)
    {
        // Create individual text mana with appropriate styling
        var manaLabel = new Label();

        if (index < currentMana)
        {
            // Filled mana for available mana
            manaLabel.Text = "★";
            manaLabel.AddThemeColorOverride("font_color", Colors.Blue);
        }
        else
        {
            // Empty mana for depleted mana
            manaLabel.Text = "☆";
            manaLabel.AddThemeColorOverride("font_color", Colors.Gray);
        }

        manaLabel.AddThemeFontSizeOverride("font_size", 24);
        return manaLabel;
    }
    #endregion

    #region Public API
    public void SetManaTextures(Texture2D fullMana, Texture2D emptyMana)
    {
        // Allow external assignment of mana textures
        FullManaTexture = fullMana;
        EmptyManaTexture = emptyMana;

        // Refresh display if already connected to mana system
        if (_manaSystem != null)
            UpdateManaDisplay(_manaSystem.CurrentMana, _manaSystem.MaxMana);
    }

    public void ConnectToManaSystem(PlayerMana manaSystem)
    {
        // Manually connect to specific PlayerMana instance
        DisconnectFromCurrentManaSystem();
        EstablishNewConnection(manaSystem);
    }

    private void DisconnectFromCurrentManaSystem()
    {
        // Clean up previous connection to prevent duplicate events
        if (_manaSystem != null)
            _manaSystem.ManaChanged -= OnManaChanged;
    }

    private void EstablishNewConnection(PlayerMana manaSystem)
    {
        // Establish new connection and update display
        _manaSystem = manaSystem;

        if (_manaSystem != null)
        {
            _manaSystem.ManaChanged += OnManaChanged;
            OnManaChanged(_manaSystem.CurrentMana, _manaSystem.MaxMana);
        }
    }
    #endregion
}
