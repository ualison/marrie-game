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
    private int _keyCount = 0; // Number of PuffCageKeys collected
    #endregion

    #region Signals
    [Signal]
    public delegate void RockCountChangedEventHandler(int newCount);
    
    [Signal]
    public delegate void KeyCountChangedEventHandler(int newCount);
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

    #region Key Management
    /// <summary>
    /// Adds a PuffCageKey to the inventory
    /// </summary>
    /// <returns>True if key was added successfully</returns>
    public bool AddKey()
    {
        _keyCount++;
        Log.Debug($"Added 1 key. Total keys: {_keyCount}", "Inventory");

        // Emit signal for UI updates
        EmitSignal(SignalName.KeyCountChanged, _keyCount);
        return true;
    }

    /// <summary>
    /// Removes a key from the inventory
    /// </summary>
    /// <returns>True if key was removed successfully</returns>
    public bool RemoveKey()
    {
        if (_keyCount <= 0)
        {
            Log.UI("No keys available to remove", "Inventory");
            return false;
        }

        _keyCount--;
        Log.UI($"Removed 1 key. Total keys: {_keyCount}", "Inventory");

        // Emit signal for UI updates
        EmitSignal(SignalName.KeyCountChanged, _keyCount);
        return true;
    }

    /// <summary>
    /// Gets the current key count
    /// </summary>
    /// <returns>Current number of keys</returns>
    public int GetKeyCount()
    {
        return _keyCount;
    }

    /// <summary>
    /// Checks if the player has any keys
    /// </summary>
    /// <returns>True if player has at least one key</returns>
    public bool HasKeys()
    {
        return _keyCount > 0;
    }

    /// <summary>
    /// Sets the key count directly (useful for loading saved games)
    /// </summary>
    /// <param name="count">New key count</param>
    public void SetKeyCount(int count)
    {
        if (count < 0)
        {
            Log.Warning($"Invalid key count: {count}", "Inventory");
            return;
        }

        _keyCount = count;
        Log.System($"Key count set to {_keyCount}", "Inventory");

        // Emit signal for UI updates
        EmitSignal(SignalName.KeyCountChanged, _keyCount);
    }
    #endregion

    #region Public API
    /// <summary>
    /// Gets a summary of the inventory contents
    /// </summary>
    /// <returns>String representation of inventory</returns>
    public string GetInventorySummary()
    {
        return $"Rocks: {_rockCount}/{MaxRocks}, Keys: {_keyCount}";
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
