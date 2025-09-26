using Godot;
using MarrieGame.Core.Utils;

namespace MarrieGame.Scripts.Items
{
    // Scene Switcher crystal: floats and pulses like ReplayGem, shows a prompt when the
    // player is in range, and switches to a target scene when pressing the interact key (E).
    public partial class SceneSwitcherGem : Node2D
    {
        // UI / Nodes
        private Area2D _detectionArea;
        private Sprite2D _gemSprite;
        private Label _promptLabel;
        private Tween _floatingTween;
        private Tween _brightnessTween;

        // Floating settings (mirrors ReplayGem defaults)
        [Export] public float FloatingDistance { get; set; } = 8.0f;
        [Export] public float FloatingDuration { get; set; } = 2.0f;

        // Brightness pulse
        [Export] public bool BrightEffectEnabled { get; set; } = true;
        [Export] public float BrightnessFactor { get; set; } = 1.6f; // how bright the pulse gets
        [Export] public float BrightPulseDuration { get; set; } = 1.0f; // full cycle up+down

        // Scene switching target
        [Export(PropertyHint.File, "*.tscn")]
        public string TargetScenePath { get; set; } = string.Empty;

        private bool _playerNearby = false;
        private Vector2 _originalGemPosition;

        public override void _Ready()
        {
            InitializeComponents();
            ConnectSignals();
            SetupPromptLabel();

            // Start visible with floating + (optional) brightness effect
            if (_gemSprite != null)
            {
                _gemSprite.Show();
                StartFloatingAnimation();
            }

            if (_promptLabel != null)
                _promptLabel.Hide();

            SetProcess(true);
        }

        private void InitializeComponents()
        {
            _detectionArea = NodeHelpers.GetNodeOrNull<Area2D>(this, null, "DetectionArea");
            _gemSprite = NodeHelpers.GetNodeOrNull<Sprite2D>(this, null, "GemSprite");
            _promptLabel = NodeHelpers.GetNodeOrNull<Label>(this, null, "PromptLabel");

            if (_gemSprite != null)
            {
                _originalGemPosition = _gemSprite.Position;
                _gemSprite.Modulate = Colors.White;
            }
        }

        private void ConnectSignals()
        {
            if (_detectionArea != null)
            {
                _detectionArea.BodyEntered += OnBodyEntered;
                _detectionArea.BodyExited += OnBodyExited;
            }
        }

        private void SetupPromptLabel()
        {
            if (_promptLabel != null)
            {
                _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
                if (string.IsNullOrEmpty(_promptLabel.Text))
                    _promptLabel.Text = "Press E";
            }
        }

        public override void _Process(double delta)
        {
            if (!_playerNearby)
                return;

            if (Input.IsActionJustPressed("interact"))
            {
                TrySwitchScene();
            }
        }

        private void TrySwitchScene()
        {
            // Debug: always print current target before attempting
            Log.Debug($"SceneSwitcherGem '{GetPath()}' attempting switch. Current TargetScenePath='{TargetScenePath}'", nameof(SceneSwitcherGem));

            if (string.IsNullOrWhiteSpace(TargetScenePath))
            {
                UIManager.ShowGameplayMessage("No target scene set for crystal");
                Log.Warning("SceneSwitcherGem: TargetScenePath is empty", nameof(SceneSwitcherGem));
                return;
            }

            // Notify HazeMonsterManager of puzzle entry (if applicable) BEFORE switching
            if (HazeMonsterManager.Instance != null)
            {
                HazeMonsterManager.Instance.NotifyEnteringPuzzle(TargetScenePath);
            }

            var result = GetTree().ChangeSceneToFile(TargetScenePath);
            if (result != Error.Ok)
            {
                UIManager.ShowGameplayMessage($"Failed to load scene: {TargetScenePath}");
                Log.Error($"SceneSwitcherGem: ChangeSceneToFile failed with {result} for '{TargetScenePath}'", nameof(SceneSwitcherGem));
            }
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body != null && body.IsInGroup("player"))
            {
                _playerNearby = true;
                _promptLabel?.Show();
            }
        }

        private void OnBodyExited(Node2D body)
        {
            if (body != null && body.IsInGroup("player"))
            {
                _playerNearby = false;
                _promptLabel?.Hide();
            }
        }

        private void StartFloatingAnimation()
        {
            if (_gemSprite == null)
                return;

            // Stop previous tweens
            StopFloatingAnimation();

            // Floating tween
            _gemSprite.Position = _originalGemPosition;
            _floatingTween = CreateTween();
            _floatingTween.SetLoops();

            var up = new Vector2(_originalGemPosition.X, _originalGemPosition.Y - FloatingDistance);
            var down = new Vector2(_originalGemPosition.X, _originalGemPosition.Y + FloatingDistance);

            _floatingTween.TweenProperty(_gemSprite, "position", up, FloatingDuration / 2.0f);
            _floatingTween.TweenProperty(_gemSprite, "position", down, FloatingDuration);
            _floatingTween.TweenProperty(_gemSprite, "position", _originalGemPosition, FloatingDuration / 2.0f);

            // Brightness pulse tween (optional)
            if (BrightEffectEnabled)
            {
                // Kill existing brightness tween if any
                _brightnessTween?.Kill();
                _brightnessTween = CreateTween();
                _brightnessTween.SetLoops();

                var baseColor = Colors.White;
                var brightColor = new Color(
                    baseColor.R * BrightnessFactor,
                    baseColor.G * BrightnessFactor,
                    baseColor.B * BrightnessFactor,
                    baseColor.A
                );

                _brightnessTween.TweenProperty(_gemSprite, "modulate", brightColor, BrightPulseDuration / 2.0f);
                _brightnessTween.TweenProperty(_gemSprite, "modulate", baseColor, BrightPulseDuration / 2.0f);
            }
        }

        private void StopFloatingAnimation()
        {
            if (_floatingTween != null)
            {
                _floatingTween.Kill();
                _floatingTween = null;
            }

            if (_brightnessTween != null)
            {
                _brightnessTween.Kill();
                _brightnessTween = null;
            }

            if (_gemSprite != null)
            {
                _gemSprite.Position = _originalGemPosition;
                _gemSprite.Modulate = Colors.White;
            }
        }

        public override void _ExitTree()
        {
            // Clean up tweens
            StopFloatingAnimation();

            if (_detectionArea != null)
            {
                _detectionArea.BodyEntered -= OnBodyEntered;
                _detectionArea.BodyExited -= OnBodyExited;
            }
        }

        // Explicit setter to support deferred assignment reliability
        public void SetTargetScenePath(string path)
        {
            TargetScenePath = path ?? string.Empty;
            Log.Debug($"SceneSwitcherGem '{GetPath()}' SetTargetScenePath called. New value='{TargetScenePath}'", nameof(SceneSwitcherGem));
        }
    }
}
