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
    [Export] public float InvulnerabilityTime { get; set; } = 1.0f;
    #endregion

    #region State
    private int _currentRedHearts = 0;
    private Timer _invulnerabilityTimer;
    #endregion

    #region Properties
    // CurrentLife for monsters is only red hearts now; purple hearts are tracked on ghosts
    public int CurrentLife => _currentRedHearts;
    public bool IsInvulnerable { get; private set; }
    public bool IsDead => CurrentLife <= 0;

    // IHealthSystem
    public int CurrentHealth => CurrentLife;
    public int MaxHealth => MaxLife;

    // Red hearts are the monster's max life (purple hearts handled by ghosts)
    public int RedHearts => Mathf.Max(0, MaxLife);
    public int CurrentRedHearts => _currentRedHearts;
    // Compatibility: monsters do not maintain purple hearts locally
    public int CurrentPurpleHearts => 0;
    #endregion

    public override void _Ready()
    {
    // Ensure configured values are sane.
    MaxLife = Mathf.Max(1, MaxLife);

    int maxRed = MaxLife;
    int startRed = Mathf.Clamp(StartingLife, 0, maxRed);
    _currentRedHearts = startRed;

        _invulnerabilityTimer = new Timer { WaitTime = InvulnerabilityTime, OneShot = true };
        _invulnerabilityTimer.Timeout += EndInvulnerability;
        AddChild(_invulnerabilityTimer);

        NotifyLifeChanged();
    }

    #region Damage / Healing
    // Generic damage: prefer removing purple hearts first, then red
    public bool TakeDamage(int damage = 1)
    {
    // Monsters only have red hearts now; magical/purple damage is handled on ghosts
    if (IsDead || IsInvulnerable) return false;

    int actual = Mathf.Min(damage, _currentRedHearts);
    if (actual <= 0) return false;

    _currentRedHearts = Mathf.Max(0, _currentRedHearts - actual);

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
    // Compatibility stubs: MonsterLife no longer accepts purple-specific damage.
    public bool TakePurpleHeartDamage(int damage = 1) => false;
    public bool TakeMagicalDamage(int damage = 1) => false;
    public bool TakeRockDamageInCurrentReality(int damage = 1) => TakePhysicalDamage(damage);
    public bool TakeRockDamageInPastReality(int damage = 1) => false;

    public void Heal(int amount)
    {
        if (IsDead) return;

        int remaining = amount;
    // Fill red hearts up to RedHearts
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
    int maxRed = MaxLife;
    int startRed = Mathf.Clamp(StartingLife, 0, maxRed);
    _currentRedHearts = startRed;
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
    // Monsters report only red-heart based current life; purple hearts migrated to GhostMonsterLife
    Log.Debug($"MonsterLife.NotifyLifeChanged: Current={CurrentLife}, Max={MaxLife}, Purple=0", "MonsterLife");
    EmitSignal("LifeChanged", CurrentLife, MaxLife, 0);
    }

    // Sets heart values directly - used by EnemyManager for synchronization
    public void SetHearts(int redHearts, int purpleHearts)
    {
    // Clamp values to valid ranges; ignore purpleHearts parameter - ghosts manage purple separately
    int redMax = MaxLife;
    int newRed = Mathf.Clamp(redHearts, 0, redMax);

        // If nothing changed, avoid emitting signals
        if (newRed == _currentRedHearts)
            return;

        _currentRedHearts = newRed;

        Log.Debug($"MonsterLife.SetHearts: R={newRed}, P=0", "MonsterLife");

        NotifyLifeChanged();

        if (IsDead)
        {
            EmitSignal("MonsterDied");
        }
    }
}