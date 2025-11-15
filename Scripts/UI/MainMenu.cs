using Godot;
using System;

namespace MarrieGame.UI
{
    /// <summary>
    /// Main menu controller with start game, options, and exit functionality
    /// </summary>
    public partial class MainMenu : Control
    {
        #region Node References
        private Control _menuPanel;
        private Control _optionsPanel;
        private CheckButton _fullscreenCheckButton;
        private CheckButton _vsyncCheckButton;
        private OptionButton _resolutionOptionButton;
        private OptionButton _fpsLimitOptionButton;
        #endregion

        #region Resolution Options
        private readonly Vector2I[] _resolutions = new Vector2I[]
        {
            new Vector2I(1280, 720),
            new Vector2I(1366, 768),
            new Vector2I(1920, 1080),
            new Vector2I(2560, 1440),
            new Vector2I(3840, 2160)
        };
        #endregion

        #region Initialization
        public override void _Ready()
        {
            GetNodeReferences();
            LoadSettings();
        }

        private void GetNodeReferences()
        {
            _menuPanel = GetNode<Control>("CenterContainer/MenuPanel");
            _optionsPanel = GetNode<Control>("OptionsPanel");
            
            _fullscreenCheckButton = GetNode<CheckButton>("OptionsPanel/VBoxContainer/OptionsMargin/OptionsVBox/FullscreenContainer/CheckButton");
            _vsyncCheckButton = GetNode<CheckButton>("OptionsPanel/VBoxContainer/OptionsMargin/OptionsVBox/VSyncContainer/CheckButton");
            _resolutionOptionButton = GetNode<OptionButton>("OptionsPanel/VBoxContainer/OptionsMargin/OptionsVBox/ResolutionContainer/OptionButton");
            _fpsLimitOptionButton = GetNode<OptionButton>("OptionsPanel/VBoxContainer/OptionsMargin/OptionsVBox/FPSContainer/OptionButton");
        }

        private void LoadSettings()
        {
            // Load current window settings
            var window = GetWindow();
            
            // Fullscreen
            _fullscreenCheckButton.ButtonPressed = window.Mode == Window.ModeEnum.Fullscreen;
            
            // VSync
            _vsyncCheckButton.ButtonPressed = DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled;
            
            // Resolution - find closest match
            var currentSize = window.Size;
            int closestIndex = 0;
            float closestDistance = float.MaxValue;
            
            for (int i = 0; i < _resolutions.Length; i++)
            {
                float distance = currentSize.DistanceTo(_resolutions[i]);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }
            
            _resolutionOptionButton.Selected = closestIndex;
            
            // FPS Limit
            int maxFps = Engine.MaxFps;
            if (maxFps == 30)
                _fpsLimitOptionButton.Selected = 0;
            else if (maxFps == 60)
                _fpsLimitOptionButton.Selected = 1;
            else if (maxFps == 120)
                _fpsLimitOptionButton.Selected = 2;
            else
                _fpsLimitOptionButton.Selected = 3; // Unlimited
        }
        #endregion

        #region Main Menu Buttons
        private void OnStartPressed()
        {
            GD.Print("MainMenu: Starting game - Loading Forest scene");
            GetTree().ChangeSceneToFile("res://Scenes/Forest.tscn");
        }

        private void OnOptionsPressed()
        {
            GD.Print("MainMenu: Opening options panel");
            _menuPanel.Visible = false;
            _optionsPanel.Visible = true;
        }

        private void OnExitPressed()
        {
            GD.Print("MainMenu: Exiting game");
            GetTree().Quit();
        }
        #endregion

        #region Options Menu
        private void OnBackPressed()
        {
            GD.Print("MainMenu: Closing options panel");
            _optionsPanel.Visible = false;
            _menuPanel.Visible = true;
        }

        private void OnFullscreenToggled(bool toggled)
        {
            var window = GetWindow();
            
            if (toggled)
            {
                window.Mode = Window.ModeEnum.Fullscreen;
                GD.Print("MainMenu: Fullscreen enabled");
            }
            else
            {
                window.Mode = Window.ModeEnum.Windowed;
                GD.Print("MainMenu: Fullscreen disabled");
            }
        }

        private void OnVSyncToggled(bool toggled)
        {
            if (toggled)
            {
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
                GD.Print("MainMenu: VSync enabled");
            }
            else
            {
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
                GD.Print("MainMenu: VSync disabled");
            }
        }

        private void OnResolutionSelected(int index)
        {
            if (index >= 0 && index < _resolutions.Length)
            {
                var window = GetWindow();
                var newResolution = _resolutions[index];
                
                // If not in fullscreen, resize the window
                if (window.Mode != Window.ModeEnum.Fullscreen)
                {
                    window.Size = newResolution;
                    
                    // Center the window on screen
                    var screenSize = DisplayServer.ScreenGetSize();
                    var windowPos = (screenSize - newResolution) / 2;
                    window.Position = windowPos;
                }
                
                GD.Print($"MainMenu: Resolution changed to {newResolution.X}x{newResolution.Y}");
            }
        }

        private void OnFPSLimitSelected(int index)
        {
            switch (index)
            {
                case 0: // 30 FPS
                    Engine.MaxFps = 30;
                    GD.Print("MainMenu: FPS limit set to 30");
                    break;
                case 1: // 60 FPS
                    Engine.MaxFps = 60;
                    GD.Print("MainMenu: FPS limit set to 60");
                    break;
                case 2: // 120 FPS
                    Engine.MaxFps = 120;
                    GD.Print("MainMenu: FPS limit set to 120");
                    break;
                case 3: // Unlimited
                    Engine.MaxFps = 0;
                    GD.Print("MainMenu: FPS limit removed (unlimited)");
                    break;
            }
        }
        #endregion

        #region Input Handling
        public override void _Input(InputEvent @event)
        {
            // Allow ESC key to go back from options
            if (@event.IsActionPressed("ui_cancel") && _optionsPanel.Visible)
            {
                OnBackPressed();
                GetViewport().SetInputAsHandled();
            }
        }
        #endregion
    }
}
