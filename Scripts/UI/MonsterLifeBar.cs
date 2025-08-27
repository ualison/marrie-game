using Godot;

// Monster health display with red and purple hearts that follows the monster
public partial class MonsterLifeBar : Control
{
    #region Configuration
    [Export] public Texture2D FullRedHeartTexture { get; set; }
    [Export] public Texture2D EmptyRedHeartTexture { get; set; }
    [Export] public Texture2D FullPurpleHeartTexture { get; set; }
    [Export] public Texture2D EmptyPurpleHeartTexture { get; set; }
    [Export] public Vector2 OffsetFromMonster { get; set; } = new Vector2(0, -50);
    [Export] public bool FollowMonster { get; set; } = true;
    // When false, the life bar will not try to auto-discover a MonsterLife in the scene.
    // Useful when a caller (like GhostMonster) instantiates the bar and wants to
    // explicitly connect it to a specific MonsterLife instance.
    [Export] public bool AutoDiscover { get; set; } = true;
    #endregion

    #region Components
    private HBoxContainer _heartsContainer;
    private MonsterLife _lifeSystem;
    private Node2D _monster;
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
        _heartsContainer = GetNodeOrNull<HBoxContainer>("HeartsContainer");

        if (_heartsContainer == null)
        {
            Log.Error("HBoxContainer 'HeartsContainer' not found!", "MonsterLifeBarUI");
            Log.Error("Create an HBoxContainer child named 'HeartsContainer'", "MonsterLifeBarUI");
        }
    }

    private void FindLifeSystem()
    {
        // Automatically discover and connect to monster's MonsterLife unless auto-discovery is disabled
        if (!AutoDiscover)
        {
            // Auto-discovery disabled by caller; defer until an explicit ConnectToLifeSystem is invoked.
            return;
        }

        _lifeSystem = DiscoverMonsterLife();

        if (_lifeSystem != null)
        {
            ConnectToLifeSystem();
            FindMonsterNode();
            // Debug level - successful connection is routine
            Log.Debug("Connected to MonsterLife!", "MonsterLifeBarUI");
        }
        else
        {
            LogLifeSystemError();
        }
    }

    private MonsterLife DiscoverMonsterLife()
    {
        // Try multiple strategies to find MonsterLife component
        return FindLifeSystemViaParent()
            ?? FindLifeSystemViaGroup();
    }

    private MonsterLife FindLifeSystemViaParent()
    {
        // Strategy 1: Check parent node for MonsterLife
        var parent = GetParent();
        if (parent is Monster monster)
        {
            _monster = monster;
            return monster.GetNodeOrNull<MonsterLife>("MonsterLife");
        }
        
        // Strategy 2: Check parent's parent
        if (parent?.GetParent() is Monster monsterGrandparent)
        {
            _monster = monsterGrandparent;
            return monsterGrandparent.GetNodeOrNull<MonsterLife>("MonsterLife");
        }
        
        return null;
    }

    private MonsterLife FindLifeSystemViaGroup()
    {
        // Strategy 3: Search by group membership
        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monsterNode in monsters)
        {
            if (monsterNode is Monster monster)
            {
                var monsterLife = monster.GetNodeOrNull<MonsterLife>("MonsterLife");
                if (monsterLife != null)
                {
                    _monster = monster;
                    return monsterLife;
                }
            }
        }
        return null;
    }

    private void FindMonsterNode()
    {
        // Try to find the monster node if not already found
        if (_monster == null && _lifeSystem != null)
        {
            _monster = _lifeSystem.GetParent() as Node2D;
        }
    }

    private void ConnectToLifeSystem()
    {
        // Connect to health change events for real-time updates
        _lifeSystem.LifeChanged += OnLifeChanged;
        OnLifeChanged(_lifeSystem.CurrentLife, _lifeSystem.MaxLife, _lifeSystem.PurpleHearts);
    }

    private void LogLifeSystemError()
    {
        // Log error with helpful debugging information
        Log.Error("MonsterLife not found!", "MonsterLifeBarUI");
        Log.Error("Make sure Monster node has the MonsterLife.cs script attached!", "MonsterLifeBarUI");
    }
    #endregion

    #region Position Updates
    public override void _Process(double delta)
    {
        // Follow the monster if enabled and monster exists
        if (FollowMonster && _monster != null && IsInstanceValid(_monster))
        {
            UpdatePosition();
            
            // Don't automatically control visibility - let the Monster class handle it
            // The Monster class calls Show() and Hide() explicitly based on its state
        }
    }

    private void UpdatePosition()
    {
        // Position the UI above the monster
        var camera = GetViewport().GetCamera2D();
        if (camera != null)
        {
            // Convert monster's world position to screen position
            Vector2 screenPos = camera.GetScreenCenterPosition() + (_monster.GlobalPosition - camera.GlobalPosition) * camera.Zoom;
            GlobalPosition = screenPos + OffsetFromMonster;
        }
        else
        {
            // Fallback: direct positioning (works if UI is in world space)
            GlobalPosition = _monster.GlobalPosition + OffsetFromMonster;
        }
    }
    #endregion

    #region Heart Display Management
    private void OnLifeChanged(int currentLife, int maxLife, int purpleHearts)
    {
        // Respond to health changes from MonsterLife
        UpdateHearts(currentLife, maxLife, purpleHearts);
    }

    private void UpdateHearts(int currentLife, int maxLife, int purpleHearts)
    {
        // Rebuild heart display based on current health state
        if (_heartsContainer == null) return;

        ClearExistingHearts();
        CreateNewHearts(currentLife, maxLife, purpleHearts);
    }

    private void ClearExistingHearts()
    {
        // Clear existing hearts to prevent accumulation
        foreach (Node child in _heartsContainer.GetChildren())
            child.QueueFree();
    }

    private void CreateNewHearts(int currentLife, int maxLife, int purpleHearts)
    {
        // Choose display mode based on available resources
        if (HasHeartTextures())
            CreateTextureHearts(currentLife, maxLife, purpleHearts);
        else
            CreateTextHearts(currentLife, maxLife, purpleHearts);
    }

    private bool HasHeartTextures()
    {
        // Check if all required heart textures are available
        return FullRedHeartTexture != null && EmptyRedHeartTexture != null &&
               FullPurpleHeartTexture != null && EmptyPurpleHeartTexture != null;
    }

    private void CreateTextureHearts(int currentLife, int maxLife, int purpleHearts)
    {
        // Create hearts using texture images for polished visual appearance
        // Purple hearts replace the last N hearts in the display, not add to them
        int redHearts = maxLife - purpleHearts;
        
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.Debug))
        {
            Log.Debug($"Monster Hearts - Current: {currentLife}, Max: {maxLife}, Purple: {purpleHearts}", "MonsterLifeBarUI");
            Log.Debug($"Calculated - Red slots: {redHearts}, Purple slots: {purpleHearts}", "MonsterLifeBarUI");
        }
        
        // Create purple hearts first (leftmost positions 0 to purpleHearts-1)
        for (int i = 0; i < purpleHearts; i++)
        {
            bool isFull = currentLife > i; // Purple hearts are filled first (highest priority)
            var heart = CreateTextureHeart(isFull, false); // false for purple
            _heartsContainer.AddChild(heart);
        }
        
        // Create red hearts after purple hearts (positions purpleHearts to maxLife-1)
        for (int i = 0; i < redHearts; i++)
        {
            bool isFull = currentLife > (purpleHearts + i); // Red hearts are filled after purple hearts
            var heart = CreateTextureHeart(isFull, true); // true for red
            _heartsContainer.AddChild(heart);
        }
    }

    private TextureRect CreateTextureHeart(bool isFull, bool isRed)
    {
        // Create individual texture heart
        Texture2D texture;
        
        if (isRed)
            texture = isFull ? FullRedHeartTexture : EmptyRedHeartTexture;
        else
            texture = isFull ? FullPurpleHeartTexture : EmptyPurpleHeartTexture;

        var heart = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(32, 32) // Slightly smaller than player hearts
        };

        return heart;
    }

    private void CreateTextHearts(int currentLife, int maxLife, int purpleHearts)
    {
        // Fallback display using Unicode heart characters
        // Purple hearts replace the last N hearts in the display, not add to them
        int redHearts = maxLife - purpleHearts;
        
        // Create purple hearts first (leftmost positions 0 to purpleHearts-1)
        for (int i = 0; i < purpleHearts; i++)
        {
            bool isFull = currentLife > i; // Purple hearts are filled first (highest priority)
            var heartLabel = CreateTextHeart(isFull, false); // false for purple
            _heartsContainer.AddChild(heartLabel);
        }
        
        // Create red hearts after purple hearts (positions purpleHearts to maxLife-1)
        for (int i = 0; i < redHearts; i++)
        {
            bool isFull = currentLife > (purpleHearts + i); // Red hearts are filled after purple hearts
            var heartLabel = CreateTextHeart(isFull, true); // true for red
            _heartsContainer.AddChild(heartLabel);
        }
    }

    private Label CreateTextHeart(bool isFull, bool isRed)
    {
        // Create individual text heart with appropriate styling
        var heartLabel = new Label();

        if (isFull)
        {
            // Filled heart
            heartLabel.Text = "♥";
            heartLabel.AddThemeColorOverride("font_color", isRed ? Colors.Red : Colors.Purple);
        }
        else
        {
            // Empty heart
            heartLabel.Text = "♡";
            heartLabel.AddThemeColorOverride("font_color", isRed ? Colors.DarkRed : Colors.DarkMagenta);
        }

        heartLabel.AddThemeFontSizeOverride("font_size", 20); // Slightly smaller than player
        return heartLabel;
    }
    #endregion

    #region Public API
    public void SetHeartTextures(Texture2D fullRed, Texture2D emptyRed, Texture2D fullPurple, Texture2D emptyPurple)
    {
        // Allow external assignment of heart textures
        FullRedHeartTexture = fullRed;
        EmptyRedHeartTexture = emptyRed;
        FullPurpleHeartTexture = fullPurple;
        EmptyPurpleHeartTexture = emptyPurple;

        // Refresh display if already connected to life system
        if (_lifeSystem != null)
            UpdateHearts(_lifeSystem.CurrentLife, _lifeSystem.MaxLife, _lifeSystem.PurpleHearts);
    }

    public void ConnectToLifeSystem(MonsterLife lifeSystem)
    {
        // Manually connect to specific MonsterLife instance
        DisconnectFromCurrentLifeSystem();
        EstablishNewConnection(lifeSystem);
    }

    private void DisconnectFromCurrentLifeSystem()
    {
        // Clean up previous connection to prevent duplicate events
        if (_lifeSystem != null)
            _lifeSystem.LifeChanged -= OnLifeChanged;
    }

    private void EstablishNewConnection(MonsterLife lifeSystem)
    {
        // Establish new connection and update display
        _lifeSystem = lifeSystem;

        if (_lifeSystem != null)
        {
            _lifeSystem.LifeChanged += OnLifeChanged;
            OnLifeChanged(_lifeSystem.CurrentLife, _lifeSystem.MaxLife, _lifeSystem.PurpleHearts);
            FindMonsterNode();
        }
    }

    public void SetFollowMonster(bool follow)
    {
        FollowMonster = follow;
    }

    public void SetOffset(Vector2 offset)
    {
        OffsetFromMonster = offset;
    }
    #endregion
}
