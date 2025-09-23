using Godot;
using MarrieGame.Core.Logging;
using MarrieGame.AI;

namespace MarrieGame.UI
{
    /// <summary>
    /// Energy display for Puff companion that follows Puff and shows energy orbs
    /// Similar to MonsterLifeBar but displays energy using active/inactive energy textures
    /// 
    /// Parameters:
    /// - MaxEnergyOverride: Set to override the max energy value (-1 uses PuffAI's actual max energy)
    /// - ActiveEnergyOverride: Set to override the current energy value (-1 uses PuffAI's actual current energy)
    /// - MaxEnergyOrbs: Number of energy orbs to display (default: 5)
    /// </summary>
    public partial class PuffEnergyBar : Control
    {
        #region Configuration
        [Export] public Texture2D ActiveEnergyTexture { get; set; }
        [Export] public Texture2D InactiveEnergyTexture { get; set; }
        [Export] public Vector2 OffsetFromPuff { get; set; } = new Vector2(0, -60);
        [Export] public bool FollowPuff { get; set; } = true;
        [Export] public int MaxEnergyOrbs { get; set; } = 5; // Number of energy orbs to display
        [Export] public int MaxEnergyOverride { get; set; } = -1; // Override max energy (-1 means use PuffAI's max)
        [Export] public int ActiveEnergyOverride { get; set; } = -1; // Override active energy (-1 means use PuffAI's current)
        #endregion

        #region Components
        [Export] public NodePath EnergyOrbsContainerPath;
        [Export] public NodePath PuffAIPath;

        private HBoxContainer _energyOrbsContainer;
        private PuffAI _puffAI;
        private Node2D _puff;
        private Camera2D _cachedCamera;
        private Vector2 _lastPuffPos = new Vector2(float.NaN, float.NaN);
        private Vector2 _lastCameraPos = new Vector2(float.NaN, float.NaN);
        private int _lastEnergyValue = -1;
        #endregion

        #region Initialization
        public override void _Ready()
        {
            InitializeContainer();
            CallDeferred(nameof(FindPuffAI));

            // Cache the camera
            _cachedCamera = GetViewport().GetCamera2D();
            
            Log.Debug("PuffEnergyBar initialized", "PuffEnergyBarUI");
        }

        private void InitializeContainer()
        {
            // Find the energy orbs container
            if (EnergyOrbsContainerPath != null && EnergyOrbsContainerPath.ToString() != "")
            {
                _energyOrbsContainer = GetNode<HBoxContainer>(EnergyOrbsContainerPath);
            }
            else
            {
                _energyOrbsContainer = GetNode<HBoxContainer>("EnergyOrbsContainer");
            }

            if (_energyOrbsContainer == null)
            {
                Log.Error("HBoxContainer 'EnergyOrbsContainer' not found!", "PuffEnergyBarUI");
                Log.Error("Create an HBoxContainer child named 'EnergyOrbsContainer'", "PuffEnergyBarUI");
            }
        }

        private void FindPuffAI()
        {
            // Try to find PuffAI in parent first
            var parent = GetParent();
            _puffAI = parent as PuffAI;
            _puff = parent as Node2D;

            if (_puffAI == null)
            {
                // Try to find in scene tree
                var puffNodes = GetTree().GetNodesInGroup("puff");
                if (puffNodes.Count > 0)
                {
                    _puffAI = puffNodes[0] as PuffAI;
                    _puff = puffNodes[0] as Node2D;
                }
            }

            if (_puffAI != null)
            {
                // Connect to energy change signal (for cases where energy bar is not source of truth)
                if (!_puffAI.IsConnected(PuffAI.SignalName.PuffEnergyChanged, new Callable(this, nameof(OnEnergyChanged))))
                {
                    _puffAI.PuffEnergyChanged += OnEnergyChanged;
                    Log.Debug($"Successfully connected to PuffEnergyChanged signal from {_puffAI.Name}", "PuffEnergyBarUI");
                }
                else
                {
                    Log.Warning($"Already connected to PuffEnergyChanged signal from {_puffAI.Name}", "PuffEnergyBarUI");
                }
                
                // If overrides are not set, initialize them as source of truth from PuffAI values
                if (ActiveEnergyOverride < 0 && MaxEnergyOverride < 0)
                {
                    InitializeAsSourceOfTruth(_puffAI.CurrentEnergy, _puffAI.MaxEnergyLimit);
                }
                else
                {
                    // Overrides are already set - use them as source of truth
                    int currentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : _puffAI.CurrentEnergy;
                    int maxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : _puffAI.MaxEnergyLimit;
                    UpdateEnergyOrbs(currentEnergy, maxEnergy);
                }
                
                Log.Debug($"Connected to PuffAI: {_puffAI.Name} - Energy: {GetCurrentEnergyValue()}/{GetMaxEnergyValue()}", "PuffEnergyBarUI");
            }
            else
            {
                Log.Warning("PuffAI not found! Energy bar will not function.", "PuffEnergyBarUI");
            }
        }
        #endregion

        #region Position Updates
        public override void _Process(double delta)
        {
            // Follow Puff if enabled and Puff exists
            if (!FollowPuff || _puff == null || !IsInstanceValid(_puff))
                return;

            // Only update position when Puff or camera has moved
            Vector2 puffPos = _puff.GlobalPosition;
            Vector2 cameraPos = _cachedCamera != null ? _cachedCamera.GlobalPosition : new Vector2(float.NaN, float.NaN);

            bool puffMoved = float.IsNaN(_lastPuffPos.X) || puffPos != _lastPuffPos;
            bool cameraMoved = float.IsNaN(_lastCameraPos.X) || cameraPos != _lastCameraPos;

            if (puffMoved || cameraMoved)
            {
                UpdatePosition();
                _lastPuffPos = puffPos;
                _lastCameraPos = cameraPos;
            }
        }

        private void UpdatePosition()
        {
            // Position the UI above Puff
            var camera = _cachedCamera ?? GetViewport().GetCamera2D();
            if (camera != null)
            {
                // Convert Puff's world position to screen position
                Vector2 screenPos = camera.GetScreenCenterPosition() + (_puff.GlobalPosition - camera.GlobalPosition) * camera.Zoom;
                GlobalPosition = screenPos + OffsetFromPuff;
            }
            else
            {
                // Fallback: direct positioning
                GlobalPosition = _puff.GlobalPosition + OffsetFromPuff;
            }
        }
        #endregion

        #region Energy Display Management
        private void OnEnergyChanged(int currentEnergy, int maxEnergy)
        {
            // If overrides are set, they are the source of truth - don't update from PuffAI signals
            if (ActiveEnergyOverride >= 0 || MaxEnergyOverride >= 0)
            {
                Log.Debug($"PuffEnergyBar: Ignoring PuffAI energy change - using override values as source of truth", "PuffEnergyBarUI");
                return;
            }
            
            // Apply values from PuffAI only when overrides are not set
            int displayCurrentEnergy = currentEnergy;
            int displayMaxEnergy = maxEnergy;
            
            Log.Debug($"PuffEnergyBar: OnEnergyChanged called - Received: {currentEnergy}/{maxEnergy}, Display: {displayCurrentEnergy}/{displayMaxEnergy}, LastValue: {_lastEnergyValue}", "PuffEnergyBarUI");
            
            // Only update if energy actually changed or if this is the first update
            if (displayCurrentEnergy != _lastEnergyValue || _lastEnergyValue == -1)
            {
                _lastEnergyValue = displayCurrentEnergy;
                
                Log.Debug($"PuffEnergyBar: Updating energy orbs - Current: {displayCurrentEnergy}, Max: {displayMaxEnergy}", "PuffEnergyBarUI");
                UpdateEnergyOrbs(displayCurrentEnergy, displayMaxEnergy);
            }
            else
            {
                Log.Debug($"PuffEnergyBar: No update needed - energy value unchanged", "PuffEnergyBarUI");
            }
        }

        private void UpdateEnergyOrbs(int currentEnergy, int maxEnergy)
        {
            if (_energyOrbsContainer == null) return;

            ClearExistingOrbs();
            CreateNewOrbs(currentEnergy, maxEnergy);
        }

        private void ClearExistingOrbs()
        {
            // Clear existing orbs to prevent accumulation
            foreach (Node child in _energyOrbsContainer.GetChildren())
                child.QueueFree();
        }

        private void CreateNewOrbs(int currentEnergy, int maxEnergy)
        {
            // Calculate how many orbs to show based on MaxEnergyOrbs setting
            int orbsToShow = Mathf.Min(MaxEnergyOrbs, maxEnergy);
            int energyPerOrb = maxEnergy / orbsToShow;
            
            // Handle case where max energy doesn't divide evenly
            if (energyPerOrb == 0) energyPerOrb = 1;

            // Create energy orbs based on current energy
            if (HasEnergyTextures())
                CreateTextureOrbs(currentEnergy, maxEnergy, orbsToShow, energyPerOrb);
            else
                CreateTextOrbs(currentEnergy, maxEnergy, orbsToShow, energyPerOrb);
        }

        private bool HasEnergyTextures()
        {
            return ActiveEnergyTexture != null && InactiveEnergyTexture != null;
        }

        private void CreateTextureOrbs(int currentEnergy, int maxEnergy, int orbsToShow, int energyPerOrb)
        {
            for (int i = 0; i < orbsToShow; i++)
            {
                int energyForThisOrb = (i + 1) * energyPerOrb;
                bool isActive = currentEnergy >= energyForThisOrb;
                
                var orb = CreateTextureOrb(isActive);
                _energyOrbsContainer.AddChild(orb);
            }
        }

        private TextureRect CreateTextureOrb(bool isActive)
        {
            var orb = new TextureRect
            {
                Texture = isActive ? ActiveEnergyTexture : InactiveEnergyTexture,
                ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(24, 24)
            };

            // Full brightness for energy orbs
            orb.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);

            return orb;
        }

        private void CreateTextOrbs(int currentEnergy, int maxEnergy, int orbsToShow, int energyPerOrb)
        {
            for (int i = 0; i < orbsToShow; i++)
            {
                int energyForThisOrb = (i + 1) * energyPerOrb;
                bool isActive = currentEnergy >= energyForThisOrb;
                
                var orb = CreateTextOrb(isActive);
                _energyOrbsContainer.AddChild(orb);
            }
        }

        private Label CreateTextOrb(bool isActive)
        {
            var orbLabel = new Label();

            if (isActive)
            {
                orbLabel.Text = "●";
                orbLabel.AddThemeColorOverride("font_color", Colors.Cyan);
            }
            else
            {
                orbLabel.Text = "○";
                orbLabel.AddThemeColorOverride("font_color", Colors.Gray);
            }

            orbLabel.AddThemeFontSizeOverride("font_size", 16);
            return orbLabel;
        }
        #endregion

        #region Energy Management (Source of Truth)
        /// <summary>
        /// Use energy from the energy bar - this is the source of truth for Puff's energy
        /// </summary>
        public bool UseEnergy(int amount = 1)
        {
            int currentEnergy = GetCurrentEnergyValue();
            
            if (currentEnergy < amount)
            {
                Log.Debug($"PuffEnergyBar: Insufficient energy for action. Current: {currentEnergy}, Required: {amount}", "PuffEnergyBarUI");
                return false;
            }
            
            // Update the override value (source of truth)
            ActiveEnergyOverride = Mathf.Max(0, currentEnergy - amount);
            
            // Update the visual display immediately
            int maxEnergy = GetMaxEnergyValue();
            UpdateEnergyOrbs(ActiveEnergyOverride, maxEnergy);
            
            Log.System($"PuffEnergyBar: Used {amount} energy, current: {ActiveEnergyOverride}/{maxEnergy}", "PuffEnergyBarUI");
            return true;
        }
        
        /// <summary>
        /// Gain energy to the energy bar
        /// </summary>
        public void GainEnergy(int amount = 1)
        {
            int currentEnergy = GetCurrentEnergyValue();
            int maxEnergy = GetMaxEnergyValue();
            
            // Update the override value (source of truth)
            ActiveEnergyOverride = Mathf.Min(maxEnergy, currentEnergy + amount);
            
            // Update the visual display immediately
            UpdateEnergyOrbs(ActiveEnergyOverride, maxEnergy);
            
            Log.System($"PuffEnergyBar: Gained {amount} energy, current: {ActiveEnergyOverride}/{maxEnergy}", "PuffEnergyBarUI");
        }
        
        /// <summary>
        /// Get the current energy value (from override or PuffAI)
        /// </summary>
        public int GetCurrentEnergyValue()
        {
            int energyValue;
            
            if (ActiveEnergyOverride >= 0)
            {
                energyValue = ActiveEnergyOverride;
                Log.Debug($"PuffEnergyBar: GetCurrentEnergyValue from override: {energyValue}", "PuffEnergyBarUI");
            }
            else
            {
                energyValue = _puffAI?.CurrentEnergy ?? 0;
                Log.Debug($"PuffEnergyBar: GetCurrentEnergyValue from PuffAI: {energyValue}", "PuffEnergyBarUI");
            }
                
            return energyValue;
        }
        
        /// <summary>
        /// Get the max energy value (from override or PuffAI)
        /// </summary>
        public int GetMaxEnergyValue()
        {
            if (MaxEnergyOverride >= 0)
                return MaxEnergyOverride;
                
            return _puffAI?.MaxEnergyLimit ?? 100;
        }
        
        /// <summary>
        /// Initialize energy bar as source of truth
        /// </summary>
        public void InitializeAsSourceOfTruth(int startingEnergy, int maxEnergy)
        {
            ActiveEnergyOverride = startingEnergy;
            MaxEnergyOverride = maxEnergy;
            
            // Update display immediately
            UpdateEnergyOrbs(ActiveEnergyOverride, MaxEnergyOverride);
            
            Log.Debug($"PuffEnergyBar: Initialized as source of truth - {ActiveEnergyOverride}/{MaxEnergyOverride}", "PuffEnergyBarUI");
        }
        
        /// <summary>
        /// Handle energy regeneration - called from PuffAI
        /// </summary>
        public void HandleEnergyRegeneration()
        {
            int currentEnergy = GetCurrentEnergyValue();
            int maxEnergy = GetMaxEnergyValue();
            
            if (currentEnergy >= maxEnergy)
                return;
                
            GainEnergy(1);
        }
        #endregion

        #region Public API
        public void SetEnergyTextures(Texture2D activeTexture, Texture2D inactiveTexture)
        {
            ActiveEnergyTexture = activeTexture;
            InactiveEnergyTexture = inactiveTexture;

            // Refresh display if already connected to PuffAI
            if (_puffAI != null)
            {
                int currentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : _puffAI.CurrentEnergy;
                int maxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : _puffAI.MaxEnergyLimit;
                UpdateEnergyOrbs(currentEnergy, maxEnergy);
            }
        }

        public void ConnectToPuffAI(PuffAI puffAI)
        {
            // Manually connect to specific PuffAI
            DisconnectFromCurrentPuffAI();
            _puffAI = puffAI;
            _puff = puffAI;
            
            if (_puffAI != null)
            {
                _puffAI.PuffEnergyChanged += OnEnergyChanged;
                
                // Apply overrides if set
                int currentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : _puffAI.CurrentEnergy;
                int maxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : _puffAI.MaxEnergyLimit;
                OnEnergyChanged(currentEnergy, maxEnergy);
            }
        }

        private void DisconnectFromCurrentPuffAI()
        {
            if (_puffAI != null)
            {
                try 
                { 
                    _puffAI.PuffEnergyChanged -= OnEnergyChanged; 
                } 
                catch { /* Ignore disconnection errors */ }
                _puffAI = null;
            }
        }

        public void SetFollowPuff(bool follow)
        {
            FollowPuff = follow;
        }

        public void SetOffset(Vector2 offset)
        {
            OffsetFromPuff = offset;
        }

        public void SetMaxEnergyOrbs(int maxOrbs)
        {
            MaxEnergyOrbs = maxOrbs;
            
            // Refresh display if connected
            if (_puffAI != null)
            {
                int currentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : _puffAI.CurrentEnergy;
                int maxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : _puffAI.MaxEnergyLimit;
                UpdateEnergyOrbs(currentEnergy, maxEnergy);
            }
        }

        public void SetMaxEnergyOverride(int maxEnergy)
        {
            MaxEnergyOverride = maxEnergy;
            
            // Refresh display if connected
            if (_puffAI != null)
            {
                int currentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : _puffAI.CurrentEnergy;
                int displayMaxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : _puffAI.MaxEnergyLimit;
                UpdateEnergyOrbs(currentEnergy, displayMaxEnergy);
            }
        }

        public void SetActiveEnergyOverride(int activeEnergy)
        {
            ActiveEnergyOverride = activeEnergy;
            
            // Refresh display if connected
            if (_puffAI != null)
            {
                int displayCurrentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : _puffAI.CurrentEnergy;
                int maxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : _puffAI.MaxEnergyLimit;
                UpdateEnergyOrbs(displayCurrentEnergy, maxEnergy);
            }
        }

        public void ClearEnergyOverrides()
        {
            MaxEnergyOverride = -1;
            ActiveEnergyOverride = -1;
            
            // Refresh display to show actual values
            if (_puffAI != null)
            {
                UpdateEnergyOrbs(_puffAI.CurrentEnergy, _puffAI.MaxEnergyLimit);
            }
        }

        public void SetPuffReference(Node2D puff)
        {
            _puff = puff;
            Log.Debug($"Puff reference manually set to: {puff?.Name} at {puff?.GlobalPosition}", "PuffEnergyBarUI");
        }
        #endregion

        #region Cleanup
        public override void _ExitTree()
        {
            DisconnectFromCurrentPuffAI();
        }
        #endregion
    }
}