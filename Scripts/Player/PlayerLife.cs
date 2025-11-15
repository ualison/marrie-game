using Godot;
using MarrieGame.Core.Interfaces;

// Player health management with invulnerability periods and event notifications
public partial class PlayerLife : Node, IHealthSystem
{
    #region Signals
    [Signal] public delegate void LifeChangedEventHandler(int currentLife, int maxLife);
    [Signal] public delegate void PlayerDiedEventHandler();
    #endregion

    #region Exported Properties
    [Export] public int MaxLife { get; set; } = 3;
    [Export] public int StartingLife { get; set; } = 3; // Starting life amount (full red hearts at beginning)
    [Export] public float InvulnerabilityTime { get; set; } = 2.0f;
    #endregion

    #region Public Properties
    public int CurrentLife { get; private set; }
    public bool IsInvulnerable { get; private set; }
    public bool IsDead => CurrentLife <= 0;
    
    // IHealthSystem implementation
    public int CurrentHealth => CurrentLife;
    public int MaxHealth => MaxLife;
    #endregion

    #region Components
    private Timer _invulnerabilityTimer;
    private AudioStreamPlayer2D _screamPlayer;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Initialize health to starting amount and setup systems
        CurrentLife = Mathf.Clamp(StartingLife, 0, MaxLife);
        SetupInvulnerabilityTimer();
        _screamPlayer = GetNodeOrNull<AudioStreamPlayer2D>("../ScreamSFX");
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
    PlayDamageScream();
        StartInvulnerability();
        NotifyLifeChanged();

        // Emit death signal if health depleted
        if (IsDead)
            EmitSignal(SignalName.PlayerDied);

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
        MaxLife = newMaxLife;
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
        EmitSignal(SignalName.LifeChanged, CurrentLife, MaxLife);
        
        // Also notify through simple event bus
        GameEventSystem.OnPlayerHealthChanged(CurrentLife, MaxLife);
    }
    #endregion

    #region Audio
    private void PlayDamageScream()
    {
        if (_screamPlayer == null)
            return;

        if (_screamPlayer.Playing)
            _screamPlayer.Stop();

        _screamPlayer.Play();
    }
    #endregion
}