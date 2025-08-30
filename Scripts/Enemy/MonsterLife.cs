using Godot;
using MarrieGame.Core.Interfaces;

// Monster health management with dual heart types (red = physical, purple = magical)
public partial class MonsterLife : Node, IHealthSystem
{
    #region Signals
    [Signal] public delegate void LifeChangedEventHandler(int currentLife, int maxLife, int purpleHearts);
    [Signal] public delegate void MonsterDiedEventHandler();
    #endregion

    #region Exported
    [Export] public int MaxLife { get; set; } = 5;
    [Export] public int StartingLife { get; set; } = 5;
    [Export] public int PurpleHearts { get; set; } = 1;
    [Export] public float InvulnerabilityTime { get; set; } = 1.0f;
    #endregion

    #region State
    private int _currentRedHearts = 0;
    private int _currentPurpleHearts = 0;
    private Timer _invulnerabilityTimer;
    #endregion

    #region Properties
    public int CurrentLife => _currentRedHearts + _currentPurpleHearts;
    public bool IsInvulnerable { get; private set; }
    public bool IsDead => CurrentLife <= 0;

    // IHealthSystem
    public int CurrentHealth => CurrentLife;
    public int MaxHealth => MaxLife;

    public int RedHearts => Mathf.Max(0, MaxLife - PurpleHearts);
    public int CurrentRedHearts => _currentRedHearts;
    public int CurrentPurpleHearts => _currentPurpleHearts;
    #endregion

    public override void _Ready()
    {
        PurpleHearts = Mathf.Max(0, PurpleHearts);
        MaxLife = Mathf.Max(PurpleHearts, MaxLife);

        int maxRed = MaxLife - PurpleHearts;
        int startRed = Mathf.Clamp(Mathf.Min(StartingLife, maxRed), 0, maxRed);
        int startPurple = Mathf.Clamp(StartingLife - startRed, 0, PurpleHearts);

        _currentRedHearts = startRed;
        _currentPurpleHearts = startPurple;

        _invulnerabilityTimer = new Timer { WaitTime = InvulnerabilityTime, OneShot = true };
        _invulnerabilityTimer.Timeout += EndInvulnerability;
        AddChild(_invulnerabilityTimer);

        NotifyLifeChanged();
    }

    #region Damage / Healing
    // Generic damage: prefer removing purple hearts first, then red
    public bool TakeDamage(int damage = 1)
    {
        if (IsDead || IsInvulnerable) return false;

        int remaining = damage;
        if (_currentPurpleHearts > 0)
        {
            int use = Mathf.Min(_currentPurpleHearts, remaining);
            _currentPurpleHearts -= use;
            remaining -= use;
        }
        if (remaining > 0 && _currentRedHearts > 0)
        {
            int use = Mathf.Min(_currentRedHearts, remaining);
            _currentRedHearts -= use;
            remaining -= use;
        }

        StartInvulnerability();
        NotifyLifeChanged();

        if (IsDead) EmitSignal("MonsterDied");
        return true;
    }

    // Physical damage targets red hearts only
    public bool TakePhysicalDamage(int damage = 1)
    {
        if (IsDead || IsInvulnerable) return false;

        int actual = Mathf.Min(damage, _currentRedHearts);
        if (actual <= 0) return false;

        _currentRedHearts = Mathf.Max(0, _currentRedHearts - actual);

        StartInvulnerability();
        NotifyLifeChanged();

        if (IsDead) EmitSignal("MonsterDied");
        return true;
    }

    // Magical/purple-heart-only damage
    public bool TakePurpleHeartDamage(int damage = 1)
    {
        if (IsDead || IsInvulnerable) return false;

        int actual = Mathf.Min(damage, _currentPurpleHearts);
        if (actual <= 0) return false;

        _currentPurpleHearts = Mathf.Max(0, _currentPurpleHearts - actual);

        StartInvulnerability();
        NotifyLifeChanged();

        if (IsDead) EmitSignal("MonsterDied");
        return true;
    }

    // Alias used by callers that expect magical damage to prefer purple hearts first
    public bool TakeMagicalDamage(int damage = 1)
    {
        return TakeDamage(damage);
    }

    public bool TakeRockDamageInCurrentReality(int damage = 1) => TakePhysicalDamage(damage);
    public bool TakeRockDamageInPastReality(int damage = 1) => TakePurpleHeartDamage(damage);

    public void Heal(int amount)
    {
        if (IsDead) return;

        int remaining = amount;
        // Fill purple hearts first up to PurpleHearts
        if (_currentPurpleHearts < PurpleHearts)
        {
            int can = PurpleHearts - _currentPurpleHearts;
            int use = Mathf.Min(can, remaining);
            _currentPurpleHearts += use;
            remaining -= use;
        }

        // Then fill red hearts up to RedHearts
        int redMax = RedHearts;
        if (remaining > 0 && _currentRedHearts < redMax)
        {
            int can = redMax - _currentRedHearts;
            int use = Mathf.Min(can, remaining);
            _currentRedHearts += use;
            remaining -= use;
        }

        NotifyLifeChanged();
    }

    public void Reset()
    {
        int maxRed = MaxLife - PurpleHearts;
        int startRed = Mathf.Clamp(Mathf.Min(StartingLife, maxRed), 0, maxRed);
        int startPurple = Mathf.Clamp(StartingLife - startRed, 0, PurpleHearts);

        _currentRedHearts = startRed;
        _currentPurpleHearts = startPurple;
        IsInvulnerable = false;
        NotifyLifeChanged();
    }
    #endregion

    #region Invulnerability
    private void StartInvulnerability()
    {
        IsInvulnerable = true;
        if (_invulnerabilityTimer != null)
        {
            _invulnerabilityTimer.Start();
        }
    }

    private void EndInvulnerability()
    {
        IsInvulnerable = false;
    }
    #endregion

    private void NotifyLifeChanged()
    {
        // Report actual current purple hearts, not the configured maximum
        Log.Debug($"MonsterLife.NotifyLifeChanged: Current={CurrentLife}, Max={MaxLife}, Purple={_currentPurpleHearts}", "MonsterLife");
        EmitSignal("LifeChanged", CurrentLife, MaxLife, _currentPurpleHearts);
    }

    // Sets heart values directly - used by EnemyManager for synchronization
    public void SetHearts(int redHearts, int purpleHearts)
    {
        // Clamp values to valid ranges
        int redMax = MaxLife - PurpleHearts;
        int newRed = Mathf.Clamp(redHearts, 0, redMax);
        int newPurple = Mathf.Clamp(purpleHearts, 0, PurpleHearts);
        
        // If nothing changed, avoid emitting signals
        if (newRed == _currentRedHearts && newPurple == _currentPurpleHearts)
            return;

        _currentRedHearts = newRed;
        _currentPurpleHearts = newPurple;

        Log.Debug($"MonsterLife.SetHearts: R={newRed}, P={newPurple}", "MonsterLife");

        NotifyLifeChanged();

        if (IsDead)
        {
            EmitSignal("MonsterDied");
        }
    }
}