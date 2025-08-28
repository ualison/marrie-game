using Godot;
using MarrieGame.Core.Enums;

// Monster health display with red and purple hearts that follows the monster
// Hearts display differently based on reality - showing which hearts are vulnerable to damage
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

    // Reality-specific configuration
    [Export] public Reality RequiredReality { get; set; } = Reality.CurrentReality;
    #endregion

    #region Components
    private HBoxContainer _heartsContainer;
    private MonsterLife _lifeSystem;
    private Node2D _monster;
    private Camera2D _cachedCamera;
    private Vector2 _lastMonsterPos = new Vector2(float.NaN, float.NaN);
    private Vector2 _lastCameraPos = new Vector2(float.NaN, float.NaN);
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup hearts container and find life system
        InitializeContainer();
        CallDeferred(nameof(FindLifeSystem));

    // Cache the camera if available to avoid repeated GetViewport().GetCamera2D() calls
    _cachedCamera = GetViewport().GetCamera2D();

        // Connect to reality changes
        if (RealityManager.IsInstanceAvailable)
        {
            RealityManager.Instance.RealityChanged += OnRealityChanged;
        }
        else
        {
            // If RealityManager isn't ready yet, try to connect later
            CallDeferred(nameof(ConnectToRealityManager));
        }
    }

    private void ConnectToRealityManager()
    {
        if (RealityManager.IsInstanceAvailable)
        {
            RealityManager.Instance.RealityChanged += OnRealityChanged;
        }
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
        // Strategy 1: Check parent node for MonsterLife or GhostMonsterLife
        var parent = GetParent();

        if (parent != null)
        {
            // First, try to find the monster node directly
            _monster = parent as Node2D;

            // Check for regular Monster
            if (parent is Monster monster)
            {
                return monster.GetNodeOrNull<MonsterLife>("MonsterLife");
            }

            // Check for GhostMonster by looking for GhostMonsterLife node
            var ghostLife = parent.GetNodeOrNull<MonsterLife>("GhostMonsterLife");
            if (ghostLife != null)
            {
                Log.Debug($"Found GhostMonsterLife in parent: {parent.Name}", "MonsterLifeBarUI");
                return ghostLife;
            }

            // Check for regular MonsterLife as fallback
            var regularLife = parent.GetNodeOrNull<MonsterLife>("MonsterLife");
            if (regularLife != null)
            {
                return regularLife;
            }
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

    #region Reality Management
    private void OnRealityChanged(Reality newReality)
    {
        Log.Debug($"Reality changed to {newReality}, required reality: {RequiredReality}", "MonsterLifeBarUI");

        // Show/hide based on reality match
        bool shouldBeVisible = (newReality == RequiredReality);
        Visible = shouldBeVisible;

        if (shouldBeVisible)
        {
            Log.Debug($"Showing life bar for {RequiredReality} reality", "MonsterLifeBarUI");

            // Refresh heart display to show correct vulnerability based on new reality
            if (_lifeSystem != null)
            {
                UpdateHearts(_lifeSystem.CurrentLife, _lifeSystem.MaxLife, _lifeSystem.PurpleHearts);
            }
        }
        else
        {
            Log.Debug($"Hiding life bar (wrong reality)", "MonsterLifeBarUI");
        }
    }

    private bool ShouldFollowMonster()
    {
        // Only follow if reality matches and following is enabled
        if (!FollowMonster) return false;

        if (RealityManager.IsInstanceAvailable)
        {
            return RealityManager.Instance.CurrentReality == RequiredReality;
        }

        // Fallback: if RealityManager isn't available, use default behavior
        return true;
    }
    #endregion

    #region Position Updates
    public override void _Process(double delta)
    {
        // Follow the monster if enabled, monster exists, and reality is correct
        if (!ShouldFollowMonster() || _monster == null || !IsInstanceValid(_monster))
            return;

        // Only update when the monster or camera has actually moved to avoid unnecessary math/work
        Vector2 monsterPos = _monster.GlobalPosition;
        Vector2 cameraPos = _cachedCamera != null ? _cachedCamera.GlobalPosition : new Vector2(float.NaN, float.NaN);

        bool monsterMoved = float.IsNaN(_lastMonsterPos.X) || monsterPos != _lastMonsterPos;
        bool cameraMoved = float.IsNaN(_lastCameraPos.X) || cameraPos != _lastCameraPos;

        if (monsterMoved || cameraMoved)
        {
            UpdatePosition();
            _lastMonsterPos = monsterPos;
            _lastCameraPos = cameraPos;
        }
    }

    private void UpdatePosition()
    {
        // Position the UI above the monster. Use cached camera when available to reduce per-frame lookups.
        var camera = _cachedCamera ?? GetViewport().GetCamera2D();
        if (camera != null)
        {
            // Convert monster's world position to screen position (camera conversion kept minimal)
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
        Reality currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;

        // Use MonsterLife's corrected property calculations
        int currentRedHearts = _lifeSystem?.CurrentRedHearts ?? 0;
        int currentPurpleHearts = _lifeSystem?.CurrentPurpleHearts ?? 0;
        int maxRedHearts = maxLife - purpleHearts;

        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.Debug))
        {
            Log.Debug($"Monster Hearts - Current: {currentLife}, Max: {maxLife}, Purple: {purpleHearts}, Reality: {currentReality}", "MonsterLifeBarUI");
            Log.Debug($"Calculated - Red hearts: {currentRedHearts}/{maxRedHearts}, Purple hearts: {currentPurpleHearts}/{purpleHearts}", "MonsterLifeBarUI");
        }

        // REALITY-SPECIFIC DISPLAY: Show only the relevant heart type
        if (currentReality == Reality.CurrentReality)
        {
            // Current Reality: Show ONLY red hearts (vulnerable hearts)
            for (int i = 0; i < maxRedHearts; i++)
            {
                bool isFull = i < currentRedHearts;
                var heart = CreateTextureHeart(isFull, true, true); // Red, vulnerable
                _heartsContainer.AddChild(heart);
            }
        }
        else // PastReality
        {
            // Past Reality: Show ONLY purple hearts (vulnerable hearts)
            for (int i = 0; i < purpleHearts; i++)
            {
                bool isFull = i < currentPurpleHearts;
                var heart = CreateTextureHeart(isFull, false, true); // Purple, vulnerable
                _heartsContainer.AddChild(heart);
            }
        }
    }

    private void CreateRedTextureHearts(int maxCount, int currentCount, bool vulnerable)
    {
        for (int i = 0; i < maxCount; i++)
        {
            bool isFull = i < currentCount;
            var heart = CreateTextureHeart(isFull, true, vulnerable);
            _heartsContainer.AddChild(heart);
        }
    }

    private void CreatePurpleTextureHearts(int maxCount, int currentCount, bool vulnerable)
    {
        for (int i = 0; i < maxCount; i++)
        {
            bool isFull = i < currentCount;
            var heart = CreateTextureHeart(isFull, false, vulnerable);
            _heartsContainer.AddChild(heart);
        }
    }

    private TextureRect CreateTextureHeart(bool isFull, bool isRed, bool vulnerable)
    {
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
            CustomMinimumSize = new Vector2(32, 32)
        };

        // Since we only show relevant hearts, always display at full brightness
        heart.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);

        return heart;
    }
    private void CreateTextHearts(int currentLife, int maxLife, int purpleHearts)
    {
        Reality currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;

        int currentRedHearts = _lifeSystem?.CurrentRedHearts ?? 0;
        int currentPurpleHearts = _lifeSystem?.CurrentPurpleHearts ?? 0;
        int maxRedHearts = maxLife - purpleHearts;

        // REALITY-SPECIFIC DISPLAY: Show only the relevant heart type
        if (currentReality == Reality.CurrentReality)
        {
            // Current Reality: Show ONLY red hearts
            for (int i = 0; i < maxRedHearts; i++)
            {
                bool isFull = i < currentRedHearts;
                var heartLabel = CreateTextHeart(isFull, true, true); // Red, vulnerable
                _heartsContainer.AddChild(heartLabel);
            }
        }
        else // PastReality
        {
            // Past Reality: Show ONLY purple hearts
            for (int i = 0; i < purpleHearts; i++)
            {
                bool isFull = i < currentPurpleHearts;
                var heartLabel = CreateTextHeart(isFull, false, true); // Purple, vulnerable
                _heartsContainer.AddChild(heartLabel);
            }
        }
    }


    private void CreateRedTextHearts(int maxCount, int currentCount, bool vulnerable)
    {
        for (int i = 0; i < maxCount; i++)
        {
            bool isFull = i < currentCount;
            var heartLabel = CreateTextHeart(isFull, true, vulnerable);
            _heartsContainer.AddChild(heartLabel);
        }
    }

    private void CreatePurpleTextHearts(int maxCount, int currentCount, bool vulnerable)
    {
        for (int i = 0; i < maxCount; i++)
        {
            bool isFull = i < currentCount;
            var heartLabel = CreateTextHeart(isFull, false, vulnerable);
            _heartsContainer.AddChild(heartLabel);
        }
    }

    private Label CreateTextHeart(bool isFull, bool isRed, bool vulnerable)
    {
        var heartLabel = new Label();

        if (isFull)
        {
            heartLabel.Text = "♥";
            Color baseColor = isRed ? Colors.Red : Colors.Purple;
            heartLabel.AddThemeColorOverride("font_color", baseColor);
        }
        else
        {
            heartLabel.Text = "♡";
            Color baseColor = isRed ? Colors.DarkRed : Colors.DarkMagenta;
            heartLabel.AddThemeColorOverride("font_color", baseColor);
        }

        heartLabel.AddThemeFontSizeOverride("font_size", 20);
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
            // Defer finding the monster node so caller code can reparent _lifeSystem
            // before we attempt to resolve its parent. This prevents stale references
            // when callers move the MonsterLife in the scene tree (see GhostMonster.ReconnectLifeBar).
            CallDeferred(nameof(FindMonsterNode));
        }
    }

    // Allow external callers to force Monster discovery after scene-tree changes
    public void RefreshMonsterNode()
    {
        FindMonsterNode();
    }

    public void SetFollowMonster(bool follow)
    {
        FollowMonster = follow;
    }

    public void SetOffset(Vector2 offset)
    {
        OffsetFromMonster = offset;
    }

    public void SetRequiredReality(Reality reality)
    {
        RequiredReality = reality;

        // Update visibility immediately if RealityManager is available
        if (RealityManager.IsInstanceAvailable)
        {
            OnRealityChanged(RealityManager.Instance.CurrentReality);
        }
    }

    public void SetMonsterReference(Node2D monster)
    {
        _monster = monster;
        Log.Debug($"Monster reference manually set to: {monster?.Name} at {monster?.GlobalPosition}", "MonsterLifeBarUI");
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        // Clean up reality manager connection
        if (RealityManager.IsInstanceAvailable)
        {
            RealityManager.Instance.RealityChanged -= OnRealityChanged;
        }
        // Clean up life system connection
        DisconnectFromCurrentLifeSystem();
    }
    #endregion
}