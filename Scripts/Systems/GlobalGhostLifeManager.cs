using Godot;
using MarrieGame.Core.Interfaces;
using MarrieGame.Core.Utils;

/// <summary>
/// Global manager for Ghost Monster life that persists across scenes
/// Single Responsibility: Global Ghost Life Management
/// </summary>
public partial class GlobalGhostLifeManager : Node, IHealthSystem
{
    public static GlobalGhostLifeManager Instance { get; private set; }

    #region Signals
    [Signal] public delegate void GhostLifeChangedEventHandler(int currentLife, int maxLife, int purpleHearts);
    [Signal] public delegate void GhostMonsterDefeatedEventHandler();
    #endregion

    #region Configuration
    [Export] public int MaxPurpleHearts { get; set; } = 3;
    [Export] public int StartingPurpleHearts { get; set; } = 1;
    [Export] public float InvulnerabilityTime { get; set; } = 1.0f;
    #endregion

    #region State
    private int _currentPurpleHearts = 0;
    private Timer _invulnerabilityTimer;
    private bool _isDefeated = false;
    #endregion

    #region Properties
    public int CurrentLife => _currentPurpleHearts;
    public int MaxLife => MaxPurpleHearts;
    public bool IsInvulnerable { get; private set; }
    public bool IsDead => CurrentLife <= 0;
    public bool IsDefeated => _isDefeated;

    // IHealthSystem implementation
    public int CurrentHealth => CurrentLife;
    public int MaxHealth => MaxPurpleHearts;
    public int PurpleHearts => MaxPurpleHearts;
    public int CurrentPurpleHearts => _currentPurpleHearts;
    #endregion

    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;

            MaxPurpleHearts = Mathf.Max(0, MaxPurpleHearts);
            int startPurple = Mathf.Clamp(StartingPurpleHearts, 0, MaxPurpleHearts);
            _currentPurpleHearts = startPurple;

            _invulnerabilityTimer = new Timer { WaitTime = InvulnerabilityTime, OneShot = true };
            _invulnerabilityTimer.Timeout += EndInvulnerability;
            AddChild(_invulnerabilityTimer);

            // Subscribe to game restart events
            GameEventSystem.GameRestart += OnGameRestart;

            NotifyLifeChanged();
            Log.System("Global Ghost Life Manager initialized", "GlobalGhostLifeManager");
        }
        else
        {
            QueueFree();
        }
    }

    #region Damage / Healing
    public bool TakePurpleHeartDamage(int damage = 1)
    {
        if (_isDefeated || IsInvulnerable) return false;

        int actual = Mathf.Min(damage, _currentPurpleHearts);
        if (actual <= 0) return false;

        _currentPurpleHearts = Mathf.Max(0, _currentPurpleHearts - actual);

        StartInvulnerability();
        NotifyLifeChanged();

        if (IsDead && !_isDefeated)
        {
            _isDefeated = true;
            OnGhostMonsterDefeated();
        }
        
        return true;
    }

    // IHealthSystem generic damage entry
    public bool TakeDamage(int amount)
    {
        return TakePurpleHeartDamage(amount);
    }

    public bool TakeMagicalDamage(int damage = 1) => TakePurpleHeartDamage(damage);
    public bool TakeRockDamageInPastReality(int damage = 1) => TakePurpleHeartDamage(damage);

    public void Heal(int amount)
    {
        if (_isDefeated) return;
        int can = MaxPurpleHearts - _currentPurpleHearts;
        int use = Mathf.Min(can, amount);
        _currentPurpleHearts += use;
        NotifyLifeChanged();
    }

    public void Reset()
    {
        _isDefeated = false;
        int startPurple = Mathf.Clamp(StartingPurpleHearts, 0, MaxPurpleHearts);
        _currentPurpleHearts = startPurple;
        IsInvulnerable = false;
        NotifyLifeChanged();
        Log.System("Global Ghost Life reset", "GlobalGhostLifeManager");
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

    #region Events
    private void NotifyLifeChanged()
    {
        Log.Debug($"GlobalGhostLifeManager.NotifyLifeChanged: Current={CurrentLife}, Max={MaxPurpleHearts}, Purple={_currentPurpleHearts}", "GlobalGhostLifeManager");
        EmitSignal("GhostLifeChanged", CurrentLife, MaxPurpleHearts, _currentPurpleHearts);
        
        // Also notify through GameEventSystem for UI updates
        GameEventSystem.OnMonsterHealthChanged(CurrentLife, MaxPurpleHearts, _currentPurpleHearts);
    }

    private void OnGhostMonsterDefeated()
    {
        Log.System("Ghost Monster has been defeated!", "GlobalGhostLifeManager");
        EmitSignal("GhostMonsterDefeated");
        
        // Show message UI
        UIManager.ShowGameplayMessage("Monster Defeated!");
        
        // Notify EnemyManager to destroy all ghost monsters permanently
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnGlobalGhostLifeDefeated();
        }
    }

    private void OnGameRestart()
    {
        Log.System("Game restarting - resetting global ghost life", "GlobalGhostLifeManager");
        Reset();
    }
    #endregion

    #region Compatibility Methods
    // Matches MonsterLife signature for compatibility
    public void SetHearts(int redHearts, int purpleHearts)
    {
        int newPurple = Mathf.Clamp(purpleHearts, 0, MaxPurpleHearts);
        if (newPurple == _currentPurpleHearts) return;
        _currentPurpleHearts = newPurple;
        Log.Debug($"GlobalGhostLifeManager.SetHearts: P={newPurple}", "GlobalGhostLifeManager");
        NotifyLifeChanged();
        if (IsDead && !_isDefeated)
        {
            _isDefeated = true;
            OnGhostMonsterDefeated();
        }
    }
    #endregion

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            GameEventSystem.GameRestart -= OnGameRestart;
            Instance = null;
        }
    }
}