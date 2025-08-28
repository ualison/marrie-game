using Godot;

/// <summary>
/// Inventory system for managing player items
/// Currently tracks rock quantity, will be expanded later
/// </summary>
public partial class Inventory : Node
{
    #region Properties
    [Export] public int MaxRocks { get; set; } = 999; // Maximum rocks the player can carry
    #endregion

    #region State
    private int _rockCount = 10;
    #endregion

    #region Signals
    [Signal]
    public delegate void RockCountChangedEventHandler(int newCount);
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Add this inventory to the inventory group for easy access
        AddToGroup("inventory");
        
        // Only log critical inventory issues
        if (MaxRocks <= 0)
            Log.Error("Inventory system has invalid max capacity", "Inventory");
    }
    #endregion

    #region Rock Management
    /// <summary>
    /// Adds rocks to the inventory
    /// </summary>
    /// <param name="amount">Number of rocks to add</param>
    /// <returns>True if rocks were added successfully</returns>
    public bool AddRocks(int amount)
    {
        if (amount <= 0)
        {
            Log.Warning($"Invalid rock amount: {amount}", "Inventory");
            return false;
        }

        int newTotal = _rockCount + amount;
        
        // Check if we exceed max capacity
        if (newTotal > MaxRocks)
        {
            // Add only what we can fit
            int canAdd = MaxRocks - _rockCount;
            if (canAdd <= 0)
            {
            // Warning level - player tried to add rocks when full (potential UX issue)
            Log.Warning("Inventory full - cannot add more rocks", "Inventory");
                return false;
            }
            
            _rockCount = MaxRocks;
            // Debug level - routine inventory operations
            Log.Debug($"Added {canAdd} rocks (inventory full). Total: {_rockCount}", "Inventory");
        }
        else
        {
            _rockCount = newTotal;
            // Debug level - routine inventory operations
            Log.Debug($"Added {amount} rocks. Total: {_rockCount}", "Inventory");
        }

        // Emit signal for UI updates
        EmitSignal(SignalName.RockCountChanged, _rockCount);
        return true;
    }

    /// <summary>
    /// Removes rocks from the inventory
    /// </summary>
    /// <param name="amount">Number of rocks to remove</param>
    /// <returns>True if rocks were removed successfully</returns>
    public bool RemoveRocks(int amount)
    {
        if (amount <= 0)
        {
            Log.Warning($"Invalid rock amount: {amount}", "Inventory");
            return false;
        }

        if (_rockCount < amount)
        {
            Log.UI($"Not enough rocks - have {_rockCount}, need {amount}", "Inventory");
            return false;
        }

        _rockCount -= amount;
        Log.UI($"Removed {amount} rocks. Total: {_rockCount}", "Inventory");

        // Emit signal for UI updates
        EmitSignal(SignalName.RockCountChanged, _rockCount);
        return true;
    }

    /// <summary>
    /// Gets the current rock count
    /// </summary>
    /// <returns>Current number of rocks</returns>
    public int GetRockCount()
    {
        return _rockCount;
    }

    /// <summary>
    /// Checks if the player has enough rocks
    /// </summary>
    /// <param name="amount">Number of rocks to check for</param>
    /// <returns>True if player has enough rocks</returns>
    public bool HasRocks(int amount)
    {
        return _rockCount >= amount;
    }

    /// <summary>
    /// Sets the rock count directly (useful for loading saved games)
    /// </summary>
    /// <param name="count">New rock count</param>
    public void SetRockCount(int count)
    {
        if (count < 0)
        {
            Log.Warning($"Invalid rock count: {count}", "Inventory");
            return;
        }

        if (count > MaxRocks)
        {
            count = MaxRocks;
            Log.Warning($"Rock count exceeds maximum, clamped to {MaxRocks}", "Inventory");
        }

        _rockCount = count;
        Log.System($"Rock count set to {_rockCount}", "Inventory");

        // Emit signal for UI updates
        EmitSignal(SignalName.RockCountChanged, _rockCount);
    }
    #endregion

    #region Public API
    /// <summary>
    /// Gets a summary of the inventory contents
    /// </summary>
    /// <returns>String representation of inventory</returns>
    public string GetInventorySummary()
    {
        return $"Rocks: {_rockCount}/{MaxRocks}";
    }

    /// <summary>
    /// Checks if the inventory is full
    /// </summary>
    /// <returns>True if inventory is at maximum capacity</returns>
    public bool IsFullRocks()
    {
        return _rockCount >= MaxRocks;
    }

    /// <summary>
    /// Gets the remaining rock capacity
    /// </summary>
    /// <returns>Number of rocks that can still be added</returns>
    public int GetRemainingRockCapacity()
    {
        return MaxRocks - _rockCount;
    }
    #endregion
}
