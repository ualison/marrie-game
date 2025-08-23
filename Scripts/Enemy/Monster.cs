using Godot;

// Simple enemy that damages player on contact
// Refactored for cleaner responsibility and better performance
public partial class Monster : CharacterBody2D
{
	[Export] public int DamageAmount { get; set; } = 1;
	[Export] public bool ShowForTesting { get; set; } = true;

	private Sprite2D _sprite;
	private Area2D _damageArea;

	public override void _Ready()
	{
		AddToGroup("enemies");

		InitializeComponents();
		SetVisibility();
	}

	// Visual feedback when dealing damage to player
	public void PlayDamageEffect()
	{
		if (_sprite == null) return;

		var tween = CreateTween();
		tween.TweenProperty(_sprite, "modulate", Colors.Purple, 0.2f);
		tween.TweenProperty(_sprite, "modulate", Colors.White, 0.3f);
	}

	private void InitializeComponents()
	{
		FindSprite();
		SetupDamageArea();
	}

	private void FindSprite()
	{
		_sprite = GetNodeOrNull<Sprite2D>("MonsterSprite");
		if (_sprite == null)
		{
			GD.PrintErr("Monster: MonsterSprite not found!");
		}
	}

	private void SetupDamageArea()
	{
		_damageArea = GetNodeOrNull<Area2D>("DamageArea");

		if (_damageArea == null)
		{
			CreateDamageArea();
		}

		_damageArea.BodyEntered += OnPlayerEntered;
	}

	private void CreateDamageArea()
	{
		_damageArea = new Area2D { Name = "DamageArea" };
		AddChild(_damageArea);

		var shape = new CollisionShape2D();
		shape.Shape = new CircleShape2D { Radius = 25.0f };
		_damageArea.AddChild(shape);
	}

	private void SetVisibility()
	{
		if (ShowForTesting)
		{
			Show();
			GD.Print("Monster: Ready for contact damage");
		}
		else
		{
			Hide();
		}
	}

	// In Monster.cs OnPlayerEntered method:
	private void OnPlayerEntered(Node2D body)
	{
		GD.Print($"Monster {Name} collision with {body.Name} - IsGhost: {body.IsInGroup("ghost_player") || body.IsInGroup("ghost_monster")}");

		if (body is Player player && !player.GetLifeSystem()?.IsDead == true)
		{
			GD.Print($"Monster {Name} attempting damage to player");
			if (player.TakeDamage(DamageAmount))
			{
				PlayDamageEffect();
			}
		}
	}
}