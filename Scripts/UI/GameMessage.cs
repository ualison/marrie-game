using Godot;
using MarrieGame.Core.Utils;

/// <summary>
/// Reusable UI component for displaying temporary gameplay messages
/// Single Responsibility: Temporary Message Display
/// </summary>
public partial class GameMessage : Control
{
    #region Export Properties
    [Export] public float DisplayDuration { get; set; } = 2.0f; // How long to show the message
    [Export] public float FadeInDuration { get; set; } = 0.3f; // Fade in animation time
    [Export] public float FadeOutDuration { get; set; } = 0.5f; // Fade out animation time
    [Export] public Vector2 MessageOffset { get; set; } = new Vector2(0, -100); // Offset from center
    #endregion

    #region UI Components
    private NinePatchRect _messageBackground;
    private Label _messageLabel;
    private Timer _displayTimer;
    private Tween _animationTween;
    #endregion

    #region State
    private bool _isShowing = false;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup UI components
        InitializeComponents();
        SetupTimer();
        SetupInitialState();
        
        // Only log if critical components are missing
        if (_messageBackground == null || _messageLabel == null)
            Log.Error("GameMessage components missing - UI will not function properly", "GameMessage");
    }

    private void InitializeComponents()
    {
        // Get UI components with error handling via NodeHelpers
    _messageBackground = NodeHelpers.GetNodeOrNull<NinePatchRect>(this, null, "MessageBackground");
    _messageLabel = NodeHelpers.GetNodeOrNull<Label>(this, null, "MessageBackground/MessageLabel");
        
        if (_messageBackground == null)
        {
            Log.Error("MessageBackground (NinePatchRect) not found!", "GameMessage");
        }
        
        if (_messageLabel == null)
        {
            Log.Error("MessageLabel not found!", "GameMessage");
        }
    }

    private void SetupTimer()
    {
        // Create timer for message duration
        _displayTimer = new Timer();
        _displayTimer.WaitTime = DisplayDuration;
        _displayTimer.OneShot = true;
        if (_displayTimer != null)
        {
            _displayTimer.Timeout += OnDisplayTimeout;
            AddChild(_displayTimer);
        }
    }

    private void SetupInitialState()
    {
        // Start hidden
        Hide();
        Modulate = new Color(1, 1, 1, 0);
        
        // Position will be set dynamically when showing message
    }
    #endregion

    #region Public API
    public void ShowMessage(string message)
    {
        if (_isShowing)
        {
            // If already showing, update message and restart timer
            UpdateMessage(message);
            RestartDisplay();
            return;
        }

        // Set message text and show
        UpdateMessage(message);
        StartShowAnimation();
        
        // Debug level - message display is routine
        Log.Debug($"Showing message: {message}", "GameMessage");
    }

    public void HideMessage()
    {
        if (!_isShowing) return;
        
        StartHideAnimation();
        // Debug level - hiding messages is routine
        Log.Debug("Hiding message", "GameMessage");
    }

    public bool IsShowingMessage()
    {
        return _isShowing;
    }
    #endregion

    #region Message Management
    private void UpdateMessage(string message)
    {
        if (_messageLabel != null)
        {
            _messageLabel.Text = message;
        }
    }

    private void RestartDisplay()
    {
        // Restart the display timer
        _displayTimer.Stop();
        _displayTimer.WaitTime = DisplayDuration;
        _displayTimer.Start();
    }

    private void StartShowAnimation()
    {
        _isShowing = true;
        
        // Position at screen center with offset
        var viewport = GetViewport();
        if (viewport != null)
        {
            var screenSize = viewport.GetVisibleRect().Size;
            var centerPos = screenSize / 2 + MessageOffset;
            // Center the message based on its size
            if (_messageBackground != null)
            {
                centerPos.X -= _messageBackground.Size.X / 2;
                centerPos.Y -= _messageBackground.Size.Y / 2;
            }
            Position = centerPos;
        }
        
        Show();
        
        // Stop any existing animation
        if (_animationTween != null)
        {
            _animationTween.Kill();
        }
        
        // Create fade-in animation
        _animationTween = CreateTween();
        _animationTween.TweenProperty(this, "modulate:a", 1.0f, FadeInDuration);
        _animationTween.TweenCallback(Callable.From(OnShowAnimationComplete));
    }

    private void StartHideAnimation()
    {
        // Stop timer when manually hiding
        _displayTimer.Stop();
        
        // Stop any existing animation
        if (_animationTween != null)
        {
            _animationTween.Kill();
        }
        
        // Create fade-out animation
        _animationTween = CreateTween();
        _animationTween.TweenProperty(this, "modulate:a", 0.0f, FadeOutDuration);
        _animationTween.TweenCallback(Callable.From(OnHideAnimationComplete));
    }

    private void OnShowAnimationComplete()
    {
        // Start the display timer after fade-in completes
        _displayTimer.Start();
    }

    private void OnHideAnimationComplete()
    {
        // Hide the control and reset state
        Hide();
        _isShowing = false;
        Modulate = new Color(1, 1, 1, 0);
    }

    private void OnDisplayTimeout()
    {
        // Auto-hide when timer expires
        StartHideAnimation();
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        // Clean up tween
        if (_animationTween != null)
        {
            _animationTween.Kill();
        }
    }
    #endregion
}
