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
        FindInventory();
        SetupLabel();
        
        if (_inventory != null)
        {
            _inventory.RockCountChanged += OnRockCountChanged;
            UpdateDisplay(_inventory.GetRockCount());
        }
    }

    private void FindInventory()
    {
        _inventory = GetTree().GetFirstNodeInGroup("inventory") as Inventory;
        if (_inventory == null)
        {
            Log.Warning("Inventory not found for RockCountUI", "RockCountUI");
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
        AddThemeStyleboxOverride("normal", new StyleBoxFlat());
    }
    #endregion

    #region Event Handlers
    private void OnRockCountChanged(int newCount)
    {
        UpdateDisplay(newCount);
    }

    private void UpdateDisplay(int rockCount)
    {
        Text = $"Rocks: {rockCount}";
        
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
        if (_inventory != null)
        {
            _inventory.RockCountChanged -= OnRockCountChanged;
        }
    }
    #endregion
}
