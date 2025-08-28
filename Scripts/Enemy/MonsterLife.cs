using Godot;
using MarrieGame.Core.Interfaces;

// Monster health management with dual heart types (red physical, purple magical)
public partial class MonsterLife : Node, IHealthSystem
{
    #region Signals
    [Signal] public delegate void LifeChangedEventHandler(int currentLife, int maxLife, int purpleHearts);
    [Signal] public delegate void MonsterDiedEventHandler();
    #endregion

    #region Exported Properties
    [Export] public int MaxLife { get; set; } = 5;
    [Export] public int StartingLife { get; set; } = 5; // Starting life amount
    [Export] public int PurpleHearts { get; set; } = 1; // Mandatory purple hearts (magic damage only)
    [Export] public float InvulnerabilityTime { get; set; } = 1.0f;
    #endregion

    #region Public Properties
    public int CurrentLife { get; private set; }
    public bool IsInvulnerable { get; private set; }
    public bool IsDead => CurrentLife <= 0;

    // IHealthSystem implementation
    public int CurrentHealth => CurrentLife;
    public int MaxHealth => MaxLife;

    // FIXED: Heart type properties with corrected calculations
    public int RedHearts => Mathf.Max(0, MaxLife - PurpleHearts);
    public int CurrentRedHearts => Mathf.Min(CurrentLife, MaxLife - PurpleHearts);
    public int CurrentPurpleHearts => Mathf.Max(0, CurrentLife - (MaxLife - PurpleHearts));
    #endregion

    #region Components
    private Timer _invulnerabilityTimer;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Initialize health to starting amount and setup systems
        PurpleHearts = Mathf.Max(1, PurpleHearts); // Ensure at least 1 purple heart
        MaxLife = Mathf.Max(PurpleHearts, MaxLife); // Ensure max life accommodates purple hearts
        CurrentLife = Mathf.Clamp(StartingLife, 0, MaxLife);
        SetupInvulnerabilityTimer();
        NotifyLifeChanged();
    }

    private void SetupInvulnerabilityTimer()
    {
        // Create timer for invulnerability period management
        _invulnerabilityTimer = new Timer
        {
            WaitTime = InvulnerabilityTime,
            OneShot = true
        };
        _invulnerabilityTimer.Timeout += EndInvulnerability;
        AddChild(_invulnerabilityTimer);
    }
    #endregion

    #region Health Management
    public bool TakeDamage(int damage = 1)
    {
        // Prevent damage if dead or invulnerable
        if (IsDead || IsInvulnerable) return false;

        // Apply damage and clamp to minimum of 0
        CurrentLife = Mathf.Max(0, CurrentLife - damage);
        StartInvulnerability();
        NotifyLifeChanged();

        // Emit death signal if health depleted
        if (IsDead)
            EmitSignal(SignalName.MonsterDied);

        return true;
    }

    public bool TakePhysicalDamage(int damage = 1)
    {
        // Physical damage can only affect red hearts
        Log.Debug($"TakePhysicalDamage called - IsDead: {IsDead}, IsInvulnerable: {IsInvulnerable}, CurrentRedHearts: {CurrentRedHearts}", "MonsterLife");

        if (IsDead || IsInvulnerable) return false;

        // Calculate how much red heart damage can be applied
        int maxPhysicalDamage = CurrentRedHearts;
        int actualDamage = Mathf.Min(damage, maxPhysicalDamage);

        Log.Debug($"Physical damage calculation - MaxPhysicalDamage: {maxPhysicalDamage}, ActualDamage: {actualDamage}", "MonsterLife");

        if (actualDamage <= 0) return false; // No red hearts to damage

        return TakeDamage(actualDamage);
    }

    public bool TakeMagicalDamage(int damage = 1)
    {
        // Magical damage can affect any hearts
        return TakeDamage(damage);
    }

    public bool TakePurpleHeartDamage(int damage = 1)
    {
        // Purple heart damage can only affect purple hearts
        Log.Debug($"TakePurpleHeartDamage called - IsDead: {IsDead}, IsInvulnerable: {IsInvulnerable}, CurrentPurpleHearts: {CurrentPurpleHearts}", "MonsterLife");

        if (IsDead || IsInvulnerable) return false;

        // Calculate how much purple heart damage can be applied
        int maxPurpleDamage = CurrentPurpleHearts;
        int actualDamage = Mathf.Min(damage, maxPurpleDamage);

        Log.Debug($"Purple heart damage calculation - MaxPurpleDamage: {maxPurpleDamage}, ActualDamage: {actualDamage}", "MonsterLife");

        if (actualDamage <= 0) return false; // No purple hearts to damage

        return TakeDamage(actualDamage);
    }

    // Reality-specific rock damage methods for simple damage rules
    public bool TakeRockDamageInCurrentReality(int damage = 1)
    {
        Log.Debug($"TakeRockDamageInCurrentReality called - CurrentRedHearts: {CurrentRedHearts}", "MonsterLife");

        if (IsDead || IsInvulnerable) return false;

        // In CurrentReality, rocks ONLY damage red hearts, ignore purple hearts completely
        int availableRedHearts = CurrentRedHearts;
        if (availableRedHearts <= 0) return false; // No red hearts to damage, rock does nothing

        int actualDamage = Mathf.Min(damage, availableRedHearts);

        // Standard damage that will affect red hearts first
        CurrentLife = Mathf.Max(0, CurrentLife - actualDamage);

        StartInvulnerability();
        NotifyLifeChanged();

        if (IsDead) EmitSignal(SignalName.MonsterDied);
        return true;
    }

    public bool TakeRockDamageInPastReality(int damage = 1)
    {
        Log.Debug($"TakeRockDamageInPastReality called - CurrentPurpleHearts: {CurrentPurpleHearts}", "MonsterLife");

        if (IsDead || IsInvulnerable) return false;

        // In PastReality, rocks ONLY damage purple hearts, ignore red hearts completely
        int availablePurpleHearts = CurrentPurpleHearts;
        if (availablePurpleHearts <= 0) return false; // No purple hearts to damage

        int actualDamage = Mathf.Min(damage, availablePurpleHearts);

        // Force damage to ONLY purple hearts by reducing CurrentLife directly
        CurrentLife = Mathf.Max(0, CurrentLife - actualDamage);

        StartInvulnerability();
        NotifyLifeChanged();

        if (IsDead) EmitSignal(SignalName.MonsterDied);
        return true;
    }

    public void Heal(int amount = 1)
    {
        // Cannot heal when dead
        if (IsDead) return;

        // Apply healing and clamp to maximum health
        CurrentLife = Mathf.Min(MaxLife, CurrentLife + amount);
        NotifyLifeChanged();
    }

    public void Reset()
    {
        // Reset to starting life amount for game restart
        CurrentLife = Mathf.Clamp(StartingLife, 0, MaxLife);
        IsInvulnerable = false;
        _invulnerabilityTimer.Stop();
        NotifyLifeChanged();
    }

    public void SetMaxLife(int newMaxLife)
    {
        // Update maximum health and clamp current health if needed
        MaxLife = Mathf.Max(PurpleHearts, newMaxLife); // Ensure room for purple hearts
        CurrentLife = Mathf.Min(CurrentLife, MaxLife);
        NotifyLifeChanged();
    }

    public void SetPurpleHearts(int newPurpleHearts)
    {
        // Update purple hearts count
        PurpleHearts = Mathf.Max(1, newPurpleHearts); // Ensure at least 1
        MaxLife = Mathf.Max(PurpleHearts, MaxLife); // Adjust max life if needed
        CurrentLife = Mathf.Min(CurrentLife, MaxLife);
        NotifyLifeChanged();
    }
    #endregion

    #region Invulnerability System
    private void StartInvulnerability()
    {
        // Begin invulnerability period after taking damage
        IsInvulnerable = true;
        _invulnerabilityTimer.Start();
    }

    private void EndInvulnerability()
    {
        // End invulnerability period when timer expires
        IsInvulnerable = false;
    }
    #endregion

    #region Event Notification
    private void NotifyLifeChanged()
    {
        // Notify subscribers of health changes
        EmitSignal(SignalName.LifeChanged, CurrentLife, MaxLife, PurpleHearts);

        // Also notify through event bus
        GameEventSystem.OnMonsterHealthChanged(CurrentLife, MaxLife, PurpleHearts);
    }
    #endregion
}