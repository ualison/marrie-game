using Godot;

/// <summary>
/// Simple performance monitoring - just FPS and basic metrics
/// </summary>
public partial class SimplePerformanceMonitor : Node
{
    public static SimplePerformanceMonitor Instance { get; private set; }
    
    [Export] public bool Enabled { get; set; } = true;
    [Export] public float UpdateInterval { get; set; } = 1.0f;
    
    private Timer _updateTimer;
    private Label _debugLabel;
    private float _currentFps;
    private float _frameTime;
    
    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            
            if (Enabled && OS.IsDebugBuild())
            {
                SetupDebugDisplay();
                SetupUpdateTimer();
            }
        }
        else
        {
            QueueFree();
        }
    }
    
    private void SetupDebugDisplay()
    {
        _debugLabel = new Label
        {
            Position = new Vector2(10, 10),
            Text = "FPS: --"
        };
        
        // Add to the main viewport so it's always visible
        GetViewport().AddChild(_debugLabel);
    }
    
    private void SetupUpdateTimer()
    {
        _updateTimer = new Timer
        {
            WaitTime = UpdateInterval,
            Autostart = true
        };
        _updateTimer.Timeout += UpdatePerformanceDisplay;
        AddChild(_updateTimer);
    }
    
    private void UpdatePerformanceDisplay()
    {
        if (!Enabled || _debugLabel == null) return;
        
        _currentFps = (float)Engine.GetFramesPerSecond();
        _frameTime = (float)(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0);
        
        var color = _currentFps >= 45 ? "green" : _currentFps >= 30 ? "yellow" : "red";
        
        _debugLabel.Text = $"[color={color}]FPS: {_currentFps:F1}[/color]\n" +
                          $"Frame: {_frameTime:F1}ms";
        
        // Simple warning system
        if (_currentFps < 30)
        {
            GD.PrintRich($"[color=red]Performance Warning: FPS dropped to {_currentFps:F1}[/color]");
        }
    }
    
    public void Toggle()
    {
        Enabled = !Enabled;
        if (_debugLabel != null)
        {
            _debugLabel.Visible = Enabled;
        }
    }
    
    public override void _ExitTree()
    {
        _debugLabel?.QueueFree();
        if (Instance == this) Instance = null;
    }
}
