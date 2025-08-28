using Godot;
using MarrieGame.Core.Interfaces;

// Player mana management with regeneration and event notifications
public partial class PlayerMana : Node, IManaSystem
{
    #region Signals
    [Signal] public delegate void ManaChangedEventHandler(int currentMana, int maxMana);
    [Signal] public delegate void ManaDepletedEventHandler();
    [Signal] public delegate void ManaRestoredEventHandler();
    #endregion

    #region Exported Properties
    [Export] public int MaxMana { get; set; } = 5;
    [Export] public int StartingMana { get; set; } = 5; // Starting mana amount (full purple hearts at beginning)
    [Export] public float ManaRegenRate { get; set; } = 1.0f; // Mana per second
    [Export] public float ManaRegenDelay { get; set; } = 2.0f; // Delay after mana use before regen starts
    [Export] public bool AutoRegenerate { get; set; } = true;
    #endregion

    #region Public Properties
    public int CurrentMana { get; private set; }
    public bool IsEmpty => CurrentMana <= 0;
    public bool IsFull => CurrentMana >= MaxMana;
    public bool IsRegenerating { get; private set; }
    
    // IManaSystem implementation
    public int CurrentResource => CurrentMana;
    public int MaxResource => MaxMana;
    #endregion

    #region Components
    private Timer _regenDelayTimer;
    private Timer _regenTimer;
    private Player _player; // Reference to player for pause checking
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Initialize mana to starting amount and setup systems
        CurrentMana = Mathf.Clamp(StartingMana, 0, MaxMana);
        SetupRegenTimers();
        FindPlayer();
        NotifyManaChanged();
    }

    private void FindPlayer()
    {
        // Find the player reference for pause checking
        _player = GetParent() as Player;
        if (_player == null)
        {
            Log.Warning("PlayerMana could not find Player parent", "PlayerMana");
        }
    }

    private void SetupRegenTimers()
    {
        // Create timer for regeneration delay
        _regenDelayTimer = new Timer
        {
            WaitTime = ManaRegenDelay,
            OneShot = true
        };
        _regenDelayTimer.Timeout += StartRegeneration;
        AddChild(_regenDelayTimer);

        // Create timer for mana regeneration
        _regenTimer = new Timer
        {
            WaitTime = 1.0f / ManaRegenRate, // Convert rate to interval
            OneShot = false
        };
        _regenTimer.Timeout += RegenerateMana;
        AddChild(_regenTimer);
    }
    #endregion

    #region Mana Management
    public bool UseMana(int amount = 1)
    {
        // Cannot use mana if insufficient
        if (CurrentMana < amount) return false;

        // Consume mana and stop regeneration
        CurrentMana = Mathf.Max(0, CurrentMana - amount);
        StopRegeneration();
        NotifyManaChanged();

        // Emit depletion signal if mana exhausted
        if (IsEmpty)
            EmitSignal(SignalName.ManaDepleted);

        // Start regen delay if auto-regeneration is enabled and player is not paused
        if (AutoRegenerate && !IsPlayerPaused())
            _regenDelayTimer.Start();

        return true;
    }

    public void RestoreMana(int amount = 1)
    {
        // Apply mana restoration and clamp to maximum
        bool wasEmpty = IsEmpty;
        CurrentMana = Mathf.Min(MaxMana, CurrentMana + amount);
        NotifyManaChanged();

        // Emit restoration signal if mana was empty
        if (wasEmpty && CurrentMana > 0)
            EmitSignal(SignalName.ManaRestored);

        // Stop regeneration if at full mana
        if (IsFull)
            StopRegeneration();
    }

    public void Reset()
    {
        // Reset to starting mana amount for game restart
        CurrentMana = Mathf.Clamp(StartingMana, 0, MaxMana);
        StopRegeneration();
        NotifyManaChanged();
    }

    public void SetMaxMana(int newMaxMana)
    {
        // Update maximum mana and clamp current mana if needed
        MaxMana = newMaxMana;
        CurrentMana = Mathf.Min(CurrentMana, MaxMana);
        
        // Update regen timer interval if rate changed
        if (_regenTimer != null)
            _regenTimer.WaitTime = 1.0f / ManaRegenRate;
            
        NotifyManaChanged();
    }

    public void SetRegenRate(float newRate)
    {
        // Update regeneration rate
        ManaRegenRate = newRate;
        
        if (_regenTimer != null)
            _regenTimer.WaitTime = 1.0f / ManaRegenRate;
    }
    #endregion

    #region Regeneration System
    private void StartRegeneration()
    {
        // Begin mana regeneration if not at full capacity and player is not paused
        if (!IsFull && AutoRegenerate && !IsPlayerPaused())
        {
            IsRegenerating = true;
            _regenTimer.Start();
        }
    }

    private void StopRegeneration()
    {
        // Stop all regeneration processes
        IsRegenerating = false;
        _regenDelayTimer.Stop();
        _regenTimer.Stop();
    }

    private void RegenerateMana()
    {
        // Only regenerate if player is not paused (not in replay mode)
        if (IsPlayerPaused())
        {
            // Pause regeneration during replay
            StopRegeneration();
            return;
        }

        // Regenerate one point of mana
        RestoreMana(1);
        
        // Stop if at full capacity
        if (IsFull)
            StopRegeneration();
    }

    private bool IsPlayerPaused()
    {
        // Check if player is paused (during replay mode)
        return _player?.IsPaused() ?? false;
    }

    public void SetAutoRegenerate(bool enabled)
    {
        // Enable or disable automatic regeneration
        AutoRegenerate = enabled;
        
        if (!enabled)
            StopRegeneration();
    }
    #endregion

    #region Event Notification
    private void NotifyManaChanged()
    {
        // Notify subscribers of mana changes
        EmitSignal(SignalName.ManaChanged, CurrentMana, MaxMana);
        
        // Also notify through event bus
        GameEventSystem.OnPlayerManaChanged(CurrentMana, MaxMana);
    }
    #endregion

    #region Public Utility Methods
    public bool HasEnoughMana(int requiredAmount)
    {
        // Check if enough mana is available for an action
        return CurrentMana >= requiredAmount;
    }

    public float GetManaPercentage()
    {
        // Get mana as a percentage (0.0 to 1.0)
        return MaxMana > 0 ? (float)CurrentMana / MaxMana : 0.0f;
    }
    #endregion
}
