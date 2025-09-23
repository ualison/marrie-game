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
                // Connect to energy change signal
                _puffAI.PuffEnergyChanged += OnEnergyChanged;
                
                // Initial update with potential overrides
                int currentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : _puffAI.CurrentEnergy;
                int maxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : _puffAI.MaxEnergyLimit;
                OnEnergyChanged(currentEnergy, maxEnergy);
                
                Log.Debug($"Connected to PuffAI: {_puffAI.Name}", "PuffEnergyBarUI");
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
            // Apply overrides if they are set (not -1)
            int displayCurrentEnergy = ActiveEnergyOverride >= 0 ? ActiveEnergyOverride : currentEnergy;
            int displayMaxEnergy = MaxEnergyOverride >= 0 ? MaxEnergyOverride : maxEnergy;
            
            // Only update if energy actually changed
            if (displayCurrentEnergy == _lastEnergyValue && MaxEnergyOverride == -1 && ActiveEnergyOverride == -1)
                return;

            _lastEnergyValue = displayCurrentEnergy;
            
            Log.Debug($"PuffEnergyBar: Energy changed - Current: {displayCurrentEnergy}, Max: {displayMaxEnergy} (Overrides: Active={ActiveEnergyOverride}, Max={MaxEnergyOverride})", "PuffEnergyBarUI");
            UpdateEnergyOrbs(displayCurrentEnergy, displayMaxEnergy);
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