using Godot;
using MarrieGame.Core.Interfaces;
using MarrieGame.Core.Utils;

// Ghost-specific life that delegates to the global ghost life system
// This acts as a proxy to the global system for backward compatibility
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
    public int CurrentLife => GlobalGhostLifeManager.Instance?.CurrentLife ?? 0;
    public bool IsInvulnerable => GlobalGhostLifeManager.Instance?.IsInvulnerable ?? false;
    public bool IsDead => GlobalGhostLifeManager.Instance?.IsDead ?? true;

    // IHealthSystem
    public int CurrentHealth => CurrentLife;
    public int MaxHealth => GlobalGhostLifeManager.Instance?.MaxHealth ?? MaxPurple;

    public int PurpleHearts => GlobalGhostLifeManager.Instance?.PurpleHearts ?? MaxPurple;
    public int CurrentPurpleHearts => GlobalGhostLifeManager.Instance?.CurrentPurpleHearts ?? 0;
    #endregion

    public override void _Ready()
    {
        // Wait for GlobalGhostLifeManager to be ready
        CallDeferred(nameof(InitializeAfterGlobalManager));
    }

    private void InitializeAfterGlobalManager()
    {
        // Connect to the global ghost life manager's signals for UI updates
        if (GlobalGhostLifeManager.Instance != null)
        {
            GlobalGhostLifeManager.Instance.GhostLifeChanged += OnGlobalLifeChanged;
            GlobalGhostLifeManager.Instance.GhostMonsterDefeated += OnGlobalGhostDefeated;
            NotifyLifeChanged();
        }
        else
        {
            Log.Warning("GlobalGhostLifeManager not available when initializing GhostMonsterLife", "GhostMonsterLife");
        }
    }

    #region Damage / Healing - Delegate to Global System
    public bool TakePurpleHeartDamage(int damage = 1)
    {
        if (GlobalGhostLifeManager.Instance != null)
        {
            return GlobalGhostLifeManager.Instance.TakePurpleHeartDamage(damage);
        }
        return false;
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
        if (GlobalGhostLifeManager.Instance != null)
        {
            GlobalGhostLifeManager.Instance.Heal(amount);
        }
    }

    public void Reset()
    {
        if (GlobalGhostLifeManager.Instance != null)
        {
            GlobalGhostLifeManager.Instance.Reset();
        }
    }
    #endregion

    #region Event Handlers
    private void OnGlobalLifeChanged(int currentLife, int maxLife, int purpleHearts)
    {
        // Forward the signal to maintain compatibility with existing UI connections
        EmitSignal("LifeChanged", currentLife, maxLife, purpleHearts);
    }

    private void OnGlobalGhostDefeated()
    {
        // Forward the signal to maintain compatibility
        EmitSignal("MonsterDied");
    }
    #endregion

    private void NotifyLifeChanged()
    {
        Log.Debug($"GhostMonsterLife.NotifyLifeChanged: Current={CurrentLife}, Max={MaxHealth}, Purple={CurrentPurpleHearts}", "GhostMonsterLife");
        EmitSignal("LifeChanged", CurrentLife, MaxHealth, CurrentPurpleHearts);
    }

    // Matches MonsterLife signature for compatibility - red param ignored
    public void SetHearts(int redHearts, int purpleHearts)
    {
        if (GlobalGhostLifeManager.Instance != null)
        {
            GlobalGhostLifeManager.Instance.SetHearts(redHearts, purpleHearts);
        }
    }

    public override void _ExitTree()
    {
        // Disconnect from global signals
        if (GlobalGhostLifeManager.Instance != null)
        {
            GlobalGhostLifeManager.Instance.GhostLifeChanged -= OnGlobalLifeChanged;
            GlobalGhostLifeManager.Instance.GhostMonsterDefeated -= OnGlobalGhostDefeated;
        }
    }
}
