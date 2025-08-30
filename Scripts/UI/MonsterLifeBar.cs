using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Utils;
using MarrieGame.Core.Interfaces;

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
    // Editor-exposed NodePaths to reduce fragile string lookups
    [Export] public NodePath HeartsContainerPath;
    [Export] public NodePath MonsterLifePath;
    [Export] public NodePath MonsterNodePath;

    private HBoxContainer _heartsContainer;
    // Generic interface reference so both MonsterLife and GhostMonsterLife can be used
    private IHealthSystem _lifeSystem;
    // Typed references for property access and event wiring
    private MonsterLife _monsterLife;
    private GhostMonsterLife _ghostLife;
    private Node2D _monster;
    private EventSubscriptionHelper _subscription_helper = new();
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
                RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
                _subscription_helper.AddUnsubscriber(() => RealityManager.Instance.RealityChangedEvent -= OnRealityChanged);
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
                RealityManager.Instance.RealityChangedEvent += OnRealityChanged;
                _subscription_helper.AddUnsubscriber(() => RealityManager.Instance.RealityChangedEvent -= OnRealityChanged);
        }
    }

    private void InitializeContainer()
    {
        // Prefer an editor-assigned NodePath when present
        if (HeartsContainerPath != null && HeartsContainerPath.ToString() != "")
        {
            _heartsContainer = NodeHelpers.GetNodeOrNull<HBoxContainer>(this, HeartsContainerPath, "HeartsContainer");
            if (_heartsContainer != null) return;
        }

        // Fallback: ensure hearts container exists in scene structure
        _heartsContainer = NodeHelpers.GetNodeOrNull<HBoxContainer>(this, null, "HeartsContainer");

        if (_heartsContainer == null)
        {
            Log.Error("HBoxContainer 'HeartsContainer' not found!", "MonsterLifeBarUI");
            Log.Error("Create an HBoxContainer child named 'HeartsContainer'", "MonsterLifeBarUI");
        }
    }

    private void FindLifeSystem()
    {
        // Automatically discover and connect to monster's MonsterLife/GhostMonsterLife unless auto-discovery is disabled
        if (!AutoDiscover)
            return;

        var discovered = DiscoverMonsterLife();
        if (discovered != null)
        {
            EstablishLifeConnection(discovered);
            FindMonsterNode();
            Log.Debug("Connected to life system!", "MonsterLifeBarUI");
        }
        else
        {
            LogLifeSystemError();
        }
    }

    private IHealthSystem DiscoverMonsterLife()
    {
        // Try multiple strategies to find MonsterLife or GhostMonsterLife component
        return FindLifeSystemViaParent()
            ?? FindLifeSystemViaGroup();
    }

    private IHealthSystem FindLifeSystemViaParent()
    {
        var parent = GetParent();
        if (parent == null) return null;

        _monster = parent as Node2D;

        // If the parent is a Monster, prefer its MonsterLife child
        if (parent is Monster monster)
        {
            var ml = NodeHelpers.GetNodeOrNull<MonsterLife>(monster, null, "MonsterLife");
            if (ml != null) return ml;
        }

        // Prefer a GhostMonsterLife if present
        var ghostLife = NodeHelpers.GetNodeOrNull<GhostMonsterLife>(parent, null, "GhostMonsterLife");
        if (ghostLife != null)
        {
            Log.Debug($"Found GhostMonsterLife in parent: {parent.Name}", "MonsterLifeBarUI");
            return ghostLife;
        }

        // Fallback to MonsterLife on parent
        var regularLife = NodeHelpers.GetNodeOrNull<MonsterLife>(parent, null, "MonsterLife");
        if (regularLife != null) return regularLife;

        // Check parent's parent for Monster
        if (parent.GetParent() is Monster monsterGrandparent)
        {
            _monster = monsterGrandparent;
            return NodeHelpers.GetNodeOrNull<MonsterLife>(monsterGrandparent, null, "MonsterLife");
        }

        return null;
    }

    private IHealthSystem FindLifeSystemViaGroup()
    {
        var monsters = GetTree().GetNodesInGroup("enemies");
        foreach (Node monsterNode in monsters)
        {
            if (monsterNode is Monster monster)
            {
                var monsterLife = NodeHelpers.GetNodeOrNull<MonsterLife>(monster, null, "MonsterLife");
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
        if (_monster != null) return;
        if (_monsterLife != null)
            _monster = _monsterLife.GetParent() as Node2D;
        else if (_ghostLife != null)
            _monster = _ghostLife.GetParent() as Node2D;
    }

    private void EstablishLifeConnection(IHealthSystem lifeSystem)
    {
        // Disconnect previous
        DisconnectFromCurrentLifeSystem();

        _lifeSystem = lifeSystem;
        _monsterLife = lifeSystem as MonsterLife;
        _ghostLife = lifeSystem as GhostMonsterLife;

        if (_monsterLife != null)
        {
            _monsterLife.LifeChanged += OnLifeChanged;
            int purple = ResolveMaxPurple();
            OnLifeChanged(_monsterLife.CurrentLife, _monsterLife.MaxLife, purple);
            return;
        }

        if (_ghostLife != null)
        {
            _ghostLife.LifeChanged += OnLifeChanged;
            OnLifeChanged(_ghostLife.CurrentLife, _ghostLife.MaxPurple, _ghostLife.CurrentPurpleHearts);
            return;
        }
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
                int current = _monsterLife != null ? _monsterLife.CurrentLife : (_ghostLife != null ? _ghostLife.CurrentLife : _lifeSystem.CurrentHealth);
                int max = _monsterLife != null ? _monsterLife.MaxLife : (_ghostLife != null ? _ghostLife.MaxPurple : _lifeSystem.MaxHealth);
                int purple = ResolveMaxPurple();
                UpdateHearts(current, max, purple);
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
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.System))
            Log.System($"[Debug] MonsterLifeBar.OnLifeChanged called - Current: {currentLife}, Max: {maxLife}, Purple: {purpleHearts}", "MonsterLifeBarUI");

        UpdateHearts(currentLife, maxLife, purpleHearts);

        // Force a redraw to ensure visual update (helpful under some engine redraw timing cases)
        if (_heartsContainer != null)
        {
            if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.Debug))
                Log.Debug($"MonsterLifeBar: Hearts container child count after update: {_heartsContainer.GetChildCount()}", "MonsterLifeBarUI");
            _heartsContainer.QueueRedraw();
        }
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

    // Use typed properties when available
    int currentRedHearts = _monsterLife != null ? _monsterLife.CurrentRedHearts : 0;
    int currentPurpleHearts = _monsterLife != null ? _monsterLife.CurrentPurpleHearts : (_ghostLife != null ? _ghostLife.CurrentPurpleHearts : 0);
    int maxRedHearts = (_monsterLife != null ? _monsterLife.MaxLife : maxLife) - purpleHearts;

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

    private void CreateRedTextureHearts(int maxCount, int currentCount, int purpleCount, bool vulnerable)
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

    int currentRedHearts = _monsterLife != null ? _monsterLife.CurrentRedHearts : 0;
    int currentPurpleHearts = _monsterLife != null ? _monsterLife.CurrentPurpleHearts : (_ghostLife != null ? _ghostLife.CurrentPurpleHearts : 0);
    int maxRedHearts = (_monsterLife != null ? _monsterLife.MaxLife : maxLife) - purpleHearts;

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


    private void CreateRedTextHearts(int maxCount, int currentCount, int purpleCount, bool vulnerable)
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
        {
            int current = _monsterLife != null ? _monsterLife.CurrentLife : (_ghostLife != null ? _ghostLife.CurrentLife : _lifeSystem.CurrentHealth);
            int max = _monsterLife != null ? _monsterLife.MaxLife : (_ghostLife != null ? _ghostLife.MaxPurple : _lifeSystem.MaxHealth);
            int purple = ResolveMaxPurple();
            UpdateHearts(current, max, purple);
        }
    }

    public void ConnectToLifeSystem(IHealthSystem lifeSystem)
    {
        // Manually connect to specific life system (MonsterLife or GhostMonsterLife)
        DisconnectFromCurrentLifeSystem();
        EstablishLifeConnection(lifeSystem);
    }

    private void DisconnectFromCurrentLifeSystem()
    {
        // Clean up previous connection to prevent duplicate events
        if (_monsterLife != null)
        {
            try { _monsterLife.LifeChanged -= OnLifeChanged; } catch { }
            _monsterLife = null;
        }
        if (_ghostLife != null)
        {
            try { _ghostLife.LifeChanged -= OnLifeChanged; } catch { }
            _ghostLife = null;
        }
        _lifeSystem = null;
    }
    private void EstablishNewConnection(MonsterLife lifeSystem)
    {
        // Backwards-compatible helper - wire a MonsterLife typed instance
        DisconnectFromCurrentLifeSystem();
        _lifeSystem = lifeSystem;
        _monsterLife = lifeSystem;
        if (_monsterLife != null)
        {
            _monsterLife.LifeChanged += OnLifeChanged;
            int purple = ResolveMaxPurple();
            OnLifeChanged(_monsterLife.CurrentLife, _monsterLife.MaxLife, purple);
            CallDeferred(nameof(FindMonsterNode));
        }
    }

    private int ResolveMaxPurple()
    {
        // If we have a ghost life typed instance, use it
        if (_ghostLife != null) return _ghostLife.MaxPurple;

        // If we are attached to a monster, try querying EnemyManager's pool for its ghost purple count
        if (_monster != null && EnemyManager.Instance != null)
        {
            var monster = _monster as Monster;
            if (monster != null)
            {
                var pool = EnemyManager.Instance.GetLifePoolForMonster(monster);
                if (pool != null) return pool.GhostPurpleHearts;
            }
        }

        return 0;
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
    // Centralized cleanup of subscriptions registered via helper
    _subscription_helper.UnsubscribeAll();

    // Clean up life system connection
    DisconnectFromCurrentLifeSystem();
    }
    #endregion
}