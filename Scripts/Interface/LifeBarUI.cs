using Godot;

// Player health display with flexible heart representation and automatic PlayerLife connection
// Supports both texture-based and text-based heart display modes
public partial class LifeBarUI : Control
{
	[Export] public Texture2D FullHeartTexture { get; set; }
	[Export] public Texture2D EmptyHeartTexture { get; set; }
	
	private HBoxContainer _heartsContainer;
	private PlayerLife _lifeSystem;
	
	public override void _Ready()
	{
		// Hearts container must exist in scene structure
		_heartsContainer = GetNode<HBoxContainer>("HeartsContainer");
		
		if (_heartsContainer == null)
		{
			GD.PrintErr("LifeBarUI: HBoxContainer 'HeartsContainer' not found!");
			GD.PrintErr("Create an HBoxContainer child named 'HeartsContainer'");
			return;
		}
		
		// Defer finding life system to ensure Player is ready
		CallDeferred(nameof(FindLifeSystem));
	}
	
	// Automatically discovers and connects to player's PlayerLife
	private void FindLifeSystem()
	{
		// Strategy 1: Group-based search (most reliable)
		var player = GetTree().GetFirstNodeInGroup("player") as Player;
		if (player != null)
			_lifeSystem = player.GetNodeOrNull<PlayerLife>("PlayerLife");
		
		// Strategy 2: Safe node path search with type checking
		if (_lifeSystem == null)
		{
			var playerNode = GetNodeOrNull("../../Characters/Player");
			if (playerNode is Player playerDirect)
				_lifeSystem = playerDirect.GetNodeOrNull<PlayerLife>("PlayerLife");
		}
		
		// Strategy 3: Search by group membership
		if (_lifeSystem == null)
			_lifeSystem = GetTree().GetFirstNodeInGroup("life_system") as PlayerLife;
		
		// Connect to health change events for real-time updates
		if (_lifeSystem != null)
		{
			_lifeSystem.LifeChanged += OnLifeChanged;
			OnLifeChanged(_lifeSystem.CurrentLife, _lifeSystem.MaxLife);
			GD.Print("LifeBarUI: Connected to PlayerLife!");
		}
		else
		{
			GD.PrintErr("LifeBarUI: PlayerLife not found!");
			GD.PrintErr("Make sure Player node has the Player.cs script attached!");
		}
	}
	
	// Responds to health changes from PlayerLife
	private void OnLifeChanged(int currentLife, int maxLife)
	{
		UpdateHearts(currentLife, maxLife);
	}
	
	// Rebuilds heart display based on current health state
	private void UpdateHearts(int currentLife, int maxLife)
	{
		if (_heartsContainer == null) return;
		
		// Clear existing hearts to prevent accumulation
		foreach (Node child in _heartsContainer.GetChildren())
			child.QueueFree();
		
		// Choose display mode based on available resources
		if (FullHeartTexture != null && EmptyHeartTexture != null)
			CreateTextureHearts(currentLife, maxLife);
		else
			CreateTextHearts(currentLife, maxLife);
	}
	
	// Creates hearts using texture images for polished visual appearance
	private void CreateTextureHearts(int currentLife, int maxLife)
	{
		for (int i = 0; i < maxLife; i++)
		{
			var heart = new TextureRect();
			// Show full heart for remaining life, empty for lost life
			heart.Texture = i < currentLife ? FullHeartTexture : EmptyHeartTexture;
			heart.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
			heart.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			heart.CustomMinimumSize = new Vector2(32, 32);
			
			_heartsContainer.AddChild(heart);
		}
	}
	
	// Fallback display using Unicode heart characters
	private void CreateTextHearts(int currentLife, int maxLife)
	{
		for (int i = 0; i < maxLife; i++)
		{
			var heartLabel = new Label();
			
			if (i < currentLife)
			{
				heartLabel.Text = "♥"; // Filled heart for remaining life
				heartLabel.AddThemeColorOverride("font_color", Colors.Red);
			}
			else
			{
				heartLabel.Text = "♡"; // Empty heart for lost life
				heartLabel.AddThemeColorOverride("font_color", Colors.Gray);
			}
			
			heartLabel.AddThemeFontSizeOverride("font_size", 24);
			_heartsContainer.AddChild(heartLabel);
		}
	}
	
	// Allows external assignment of heart textures
	public void SetHeartTextures(Texture2D fullHeart, Texture2D emptyHeart)
	{
		FullHeartTexture = fullHeart;
		EmptyHeartTexture = emptyHeart;
		
		// Refresh display if already connected to life system
		if (_lifeSystem != null)
			UpdateHearts(_lifeSystem.CurrentLife, _lifeSystem.MaxLife);
	}
	
	// Manually connects to specific PlayerLife instance
	public void ConnectToLifeSystem(PlayerLife lifeSystem)
	{
		// Clean up previous connection to prevent duplicate events
		if (_lifeSystem != null)
			_lifeSystem.LifeChanged -= OnLifeChanged;
		
		_lifeSystem = lifeSystem;
		
		// Establish new connection and update display
		if (_lifeSystem != null)
		{
			_lifeSystem.LifeChanged += OnLifeChanged;
			OnLifeChanged(_lifeSystem.CurrentLife, _lifeSystem.MaxLife);
		}
	}
}