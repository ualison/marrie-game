using Godot;
using MarrieGame.Core.Interfaces;

// Ghost-specific life that only tracks purple hearts (magical)
public partial class GhostMonsterLife : Node, IHealthSystem
{
    #region Signals
    [Signal] public delegate void LifeChangedEventHandler(int currentLife, int maxLife, int purpleHearts);
    [Signal] public delegate void MonsterDiedEventHandler();
    #endregion

    #region Exported
    [Export] public int MaxPurple { get; set; } = 3; // max purple hearts
    [Export] public int StartingPurple { get; set; } = 1;
    [Export] public float InvulnerabilityTime { get; set; } = 1.0f;
    #endregion

    #region State
    private int _currentPurpleHearts = 0;
    private Timer _invulnerabilityTimer;
    #endregion

    #region Properties
    public int CurrentLife => _currentPurpleHearts;
    public bool IsInvulnerable { get; private set; }
    public bool IsDead => CurrentLife <= 0;

    // IHealthSystem
    public int CurrentHealth => CurrentLife;
    public int MaxHealth => MaxPurple;

    public int PurpleHearts => MaxPurple;
    public int CurrentPurpleHearts => _currentPurpleHearts;
    #endregion

    public override void _Ready()
    {
        MaxPurple = Mathf.Max(0, MaxPurple);

        int startPurple = Mathf.Clamp(StartingPurple, 0, MaxPurple);
        _currentPurpleHearts = startPurple;

        _invulnerabilityTimer = new Timer { WaitTime = InvulnerabilityTime, OneShot = true };
        _invulnerabilityTimer.Timeout += EndInvulnerability;
        AddChild(_invulnerabilityTimer);

        NotifyLifeChanged();
    }

    #region Damage / Healing
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

    // IHealthSystem generic damage entry - for ghosts this maps to purple-heart damage
    public bool TakeDamage(int amount)
    {
        return TakePurpleHeartDamage(amount);
    }

    public bool TakeMagicalDamage(int damage = 1) => TakePurpleHeartDamage(damage);

    // Rocks in past reality hit purple hearts
    public bool TakeRockDamageInPastReality(int damage = 1) => TakePurpleHeartDamage(damage);

    public void Heal(int amount)
    {
        if (IsDead) return;
        int can = MaxPurple - _currentPurpleHearts;
        int use = Mathf.Min(can, amount);
        _currentPurpleHearts += use;
        NotifyLifeChanged();
    }

    public void Reset()
    {
        int startPurple = Mathf.Clamp(StartingPurple, 0, MaxPurple);
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
            _invulnerabilityTimer.Start();
    }

    private void EndInvulnerability()
    {
        IsInvulnerable = false;
    }
    #endregion

    private void NotifyLifeChanged()
    {
        Log.Debug($"GhostMonsterLife.NotifyLifeChanged: Current={CurrentLife}, Max={MaxPurple}, Purple={_currentPurpleHearts}", "GhostMonsterLife");
        EmitSignal("LifeChanged", CurrentLife, MaxPurple, _currentPurpleHearts);
    }

    // Matches MonsterLife signature for compatibility - red param ignored
    public void SetHearts(int redHearts, int purpleHearts)
    {
        int newPurple = Mathf.Clamp(purpleHearts, 0, MaxPurple);
        if (newPurple == _currentPurpleHearts) return;
        _currentPurpleHearts = newPurple;
        Log.Debug($"GhostMonsterLife.SetHearts: P={newPurple}", "GhostMonsterLife");
        NotifyLifeChanged();
        if (IsDead) EmitSignal("MonsterDied");
    }
}
