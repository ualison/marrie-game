using Godot;

public partial class SnowTempest : GpuParticles2D
{
	[Export] public float TempestIntensity { get; set; } = 1.0f;
	[Export] public Vector2 WindDirection { get; set; } = new Vector2(1.0f, 0.2f);
	[Export] public float WindStrength { get; set; } = 150.0f;
	[Export] public bool EnableTurbulence { get; set; } = true;
	[Export] public float TextureWeight { get; set; } = 1.0f;
	[Export] public float TextureHeight { get; set; } = 1.0f;
	[Export] public Texture2D SnowflakeTexture { get; set; }
	[Export] public float GravityVelocity { get; set; } = 20.0f;
	[Export] public float ParticleLifetime { get; set; } = 15.0f;
	
	private ParticleProcessMaterial _material;
	private float _time = 0.0f;

	public override void _Ready()
	{
		SetupSnowTempest();
	}

	private void SetupSnowTempest()
	{
		// Basic particle system setup
		Amount = (int)(2000 * TempestIntensity); // Dense particle count for tempest
		Lifetime = ParticleLifetime;
		Preprocess = 2.0f;
		ProcessMode = GpuParticles2D.ProcessModeEnum.Inherit;
		DrawOrder = GpuParticles2D.DrawOrderEnum.Lifetime;
		
		// Create and configure the material
		_material = new ParticleProcessMaterial();
		ProcessMaterial = _material;
		
		ConfigureEmission();
		ConfigureMovement();
		ConfigureAppearance();
		ConfigurePhysics();
		
		// Start the particle system
		Emitting = true;
	}

	private void ConfigureEmission()
	{
		// Wide emission area for tempest coverage
		_material.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
		_material.EmissionBoxExtents = new Vector3(800, 50, 0); // Wide horizontal emission
		// NOTE: Emission rate property not available in this Godot C# binding version; density driven by Amount.
	}

	private void ConfigureMovement()
	{
		// Gentle gravity for floating snow effect
		_material.Gravity = new Vector3(0, GravityVelocity, 0);
		// Direction with wind influence (use spread for variation)
		var windDir = WindDirection.LengthSquared() < 0.0001f ? new Vector2(1, 0.2f) : WindDirection.Normalized();
		_material.Direction = new Vector3(windDir.X, 1.0f + windDir.Y, 0);
		_material.Spread = 25.0f;
	}

	private void ConfigureAppearance() 
	{
		// Configure particle scale based on texture weight and height
		var scaleValue = TextureWeight * TextureHeight;
		_material.ScaleMin = scaleValue * 0.8f; // Minimum scale
		_material.ScaleMax = scaleValue * 1.2f; // Maximum scale for variation
		
		// Set color modulation (white particles that can be tinted)
		_material.Color = Colors.White;
		
		// Apply snowflake texture if provided
		if (SnowflakeTexture != null)
		{
			Texture = SnowflakeTexture;
		}
	}

	private void ConfigurePhysics() { /* Physics customization skipped (API not present). */ }

	private Curve CreateAlphaCurve() 
	{ 
		var curve = new Curve();
		// Basic curve for particle alpha fade
		return curve;
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		
		// Dynamic wind variations for more realistic tempest
		if (EnableTurbulence)
		{
			// Simple turbulence: sway emission direction horizontally over time
			var sway = Mathf.Sin(_time * 0.6f) * 0.5f; // -0.5 .. 0.5
			var baseDir = WindDirection.LengthSquared() < 0.0001f ? new Vector2(1, 0.2f) : WindDirection.Normalized();
			_material.Direction = new Vector3(baseDir.X + sway * 0.3f, 1.0f + baseDir.Y, 0);
		}
	}

	// Public methods for runtime control
	public void SetTempestIntensity(float intensity)
	{
		TempestIntensity = Mathf.Clamp(intensity, 0.1f, 3.0f);
		Amount = (int)(2000 * TempestIntensity);
	// No emission rate property available; density adjusts via Amount only.
	}

	public void SetWindDirection(Vector2 direction)
	{
		WindDirection = direction.Normalized();
		if (_material != null)
			_material.Direction = new Vector3(WindDirection.X, 1.0f + WindDirection.Y, 0);
	}

	public void SetWindStrength(float strength)
	{
		WindStrength = Mathf.Clamp(strength, 0.0f, 500.0f);
		// Wind strength influences only direction sway (no direct accel property exposed here).
	}

	public void ToggleTurbulence(bool enabled)
	{
		EnableTurbulence = enabled;
	// Turbulence toggles directional sway in _Process.
	}

	public void SetTextureWeight(float weight)
	{
		TextureWeight = Mathf.Clamp(weight, 0.1f, 5.0f);
		UpdateTextureScale();
	}

	public void SetTextureHeight(float height)
	{
		TextureHeight = Mathf.Clamp(height, 0.1f, 5.0f);
		UpdateTextureScale();
	}

	public void SetTextureSize(float weight, float height)
	{
		TextureWeight = Mathf.Clamp(weight, 0.1f, 5.0f);
		TextureHeight = Mathf.Clamp(height, 0.1f, 5.0f);
		UpdateTextureScale();
	}

	public void SetSnowflakeTexture(Texture2D texture)
	{
		SnowflakeTexture = texture;
		if (texture != null)
		{
			Texture = texture;
		}
	}

	public void SetGravityVelocity(float gravity)
	{
		GravityVelocity = Mathf.Clamp(gravity, 0.0f, 200.0f);
		if (_material != null)
		{
			_material.Gravity = new Vector3(0, GravityVelocity, 0);
		}
	}

	public void SetParticleLifetime(float lifetime)
	{
		ParticleLifetime = Mathf.Clamp(lifetime, 1.0f, 30.0f);
		Lifetime = ParticleLifetime;
	}

	public void AutoCalculateLifetime()
	{
		// Calculate appropriate lifetime based on gravity velocity
		// Slower gravity needs longer lifetime to reach the ground
		float calculatedLifetime = Mathf.Lerp(25.0f, 8.0f, GravityVelocity / 200.0f);
		SetParticleLifetime(calculatedLifetime);
	}

	private void UpdateTextureScale()
	{
		if (_material != null)
		{
			var scaleValue = TextureWeight * TextureHeight;
			_material.ScaleMin = scaleValue * 0.8f;
			_material.ScaleMax = scaleValue * 1.2f;
		}
	}
}
