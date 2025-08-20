using Godot;

// Player health management with invulnerability periods and event notifications
// Simplified with better performance and cleaner responsibility separation
public partial class PlayerLife : Node
{
	[Signal] public delegate void LifeChangedEventHandler(int currentLife, int maxLife);
	[Signal] public delegate void PlayerDiedEventHandler();
	
	[Export] public int MaxLife { get; set; } = 3;
	[Export] public float InvulnerabilityTime { get; set; } = 2.0f;
	
	public int CurrentLife { get; private set; }
	public bool IsInvulnerable { get; private set; }
	public bool IsDead => CurrentLife <= 0;
	
	private Timer _invulnerabilityTimer;
	
	public override void _Ready()
	{
		CurrentLife = MaxLife;
		SetupInvulnerabilityTimer();
		NotifyLifeChanged();
	}
	
	// Attempts to damage player, returns true if damage was applied
	public bool TakeDamage(int damage = 1)
	{
		if (IsDead || IsInvulnerable) return false;
		
		CurrentLife = Mathf.Max(0, CurrentLife - damage);
		StartInvulnerability();
		NotifyLifeChanged();
		
		if (IsDead)
			EmitSignal(SignalName.PlayerDied);
		
		return true;
	}
	
	// Heals player without exceeding maximum health
	public void Heal(int amount = 1)
	{
		if (IsDead) return;
		
		CurrentLife = Mathf.Min(MaxLife, CurrentLife + amount);
		NotifyLifeChanged();
	}
	
	// Resets to full health for game restart
	public void Reset()
	{
		CurrentLife = MaxLife;
		IsInvulnerable = false;
		_invulnerabilityTimer.Stop();
		NotifyLifeChanged();
	}
	
	// Updates maximum health and clamps current health
	public void SetMaxLife(int newMaxLife)
	{
		MaxLife = newMaxLife;
		CurrentLife = Mathf.Min(CurrentLife, MaxLife);
		NotifyLifeChanged();
	}
	
	private void SetupInvulnerabilityTimer()
	{
		_invulnerabilityTimer = new Timer
		{
			WaitTime = InvulnerabilityTime,
			OneShot = true
		};
		_invulnerabilityTimer.Timeout += () => IsInvulnerable = false;
		AddChild(_invulnerabilityTimer);
	}
	
	private void StartInvulnerability()
	{
		IsInvulnerable = true;
		_invulnerabilityTimer.Start();
	}
	
	private void NotifyLifeChanged() => EmitSignal(SignalName.LifeChanged, CurrentLife, MaxLife);
}