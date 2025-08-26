using Godot;

/// <summary>
/// Simple UI element to display the current rock count
/// </summary>
public partial class RockCountUI : Label
{
    #region State
    private Inventory _inventory;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        SetupLabel();
        // Use CallDeferred to ensure all nodes are ready before finding inventory
        CallDeferred(nameof(FindAndConnectInventory));
    }

    private void FindAndConnectInventory()
    {
        FindInventory();
        
        if (_inventory != null)
        {
            ConnectToInventory();
            Log.UI("Successfully connected to Inventory", "RockCountUI");
        }
        else
        {
            // If inventory not found, try again after a short delay
            Log.Warning("Inventory not found, retrying...", "RockCountUI");
            GetTree().CreateTimer(0.1f).Timeout += () => FindAndConnectInventory();
        }
    }

    private void FindInventory()
    {
        // Try multiple strategies to find the inventory
        _inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;
        
        // If not found by group, try to find via player
        if (_inventory == null)
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node;
            if (player != null)
            {
                _inventory = player.GetNodeOrNull<Inventory>("Inventory");
            }
        }
        
        // If still not found, search more broadly
        if (_inventory == null)
        {
            var allNodes = GetTree().GetNodesInGroup("inventory");
            if (allNodes.Count > 0)
            {
                _inventory = allNodes[0] as Inventory;
            }
        }

        if (_inventory == null)
        {
            Log.Warning("Inventory not found for RockCountUI", "RockCountUI");
        }
    }

    private void ConnectToInventory()
    {
        if (_inventory != null)
        {
            _inventory.RockCountChanged += OnRockCountChanged;
            UpdateDisplay(_inventory.GetRockCount());
        }
    }

    private void SetupLabel()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        
        // Style the label
        AddThemeColorOverride("font_color", Colors.White);
        AddThemeColorOverride("font_shadow_color", Colors.Black);
        AddThemeConstantOverride("shadow_offset_x", 2);
        AddThemeConstantOverride("shadow_offset_y", 2);
    }
    #endregion

    #region Event Handlers
    private void OnRockCountChanged(int newCount)
    {
        UpdateDisplay(newCount);
    }

    private void UpdateDisplay(int rockCount)
    {
        Text = $"{rockCount}";
        
        // Change color based on availability
        if (rockCount > 0)
        {
            AddThemeColorOverride("font_color", Colors.White);
        }
        else
        {
            AddThemeColorOverride("font_color", Colors.Gray);
        }
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        DisconnectFromInventory();
    }

    private void DisconnectFromInventory()
    {
        if (_inventory != null)
        {
            _inventory.RockCountChanged -= OnRockCountChanged;
            _inventory = null;
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Manually connect to a specific inventory instance
    /// </summary>
    /// <param name="inventory">The inventory to connect to</param>
    public void ConnectToInventory(Inventory inventory)
    {
        DisconnectFromInventory();
        _inventory = inventory;
        ConnectToInventory();
    }

    /// <summary>
    /// Force refresh the display from current inventory
    /// </summary>
    public void RefreshDisplay()
    {
        if (_inventory != null)
        {
            UpdateDisplay(_inventory.GetRockCount());
        }
    }
    #endregion
}
