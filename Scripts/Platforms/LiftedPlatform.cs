using Godot;

/// <summary>
/// A platform that continuously lifts up and down between its starting position and a specified height.
/// The platform moves in an infinite loop, lifting up to the specified height then returning to its original position.
/// </summary>
public partial class LiftedPlatform : Node2D
{
    #region Export Variables
    /// <summary>
    /// The height to lift above the starting position (in pixels)
    /// </summary>
    [Export] public float LiftHeight { get; set; } = 100.0f;
    
    /// <summary>
    /// Duration to lift up (in seconds)
    /// </summary>
    [Export] public float LiftUpDuration { get; set; } = 2.0f;
    
    /// <summary>
    /// Duration to lift down (in seconds)
    /// </summary>
    [Export] public float LiftDownDuration { get; set; } = 2.0f;
    
    /// <summary>
    /// Pause duration at the top position (in seconds)
    /// </summary>
    [Export] public float PauseAtTopDuration { get; set; } = 0.5f;
    
    /// <summary>
    /// Pause duration at the bottom position (in seconds)
    /// </summary>
    [Export] public float PauseAtBottomDuration { get; set; } = 0.5f;
    
    /// <summary>
    /// Auto-start the lifting animation when the platform becomes ready
    /// </summary>
    [Export] public bool AutoStart { get; set; } = true;
    #endregion

    #region Private Variables
    private Vector2 startingPosition;
    private Vector2 liftedPosition;
    private Tween liftTween;
    private bool isLifting = false;
    private bool isMovingUp = true;
    #endregion

    public override void _Ready()
    {
        // Store the starting position
        startingPosition = Position;
        liftedPosition = startingPosition + Vector2.Up * LiftHeight;
        
        GD.Print($"LiftedPlatform '{Name}': Starting position = {startingPosition}, Lifted position = {liftedPosition}");
        
        if (AutoStart)
        {
            StartLifting();
        }
    }

    /// <summary>
    /// Starts the continuous lifting animation
    /// </summary>
    public void StartLifting()
    {
        if (isLifting) return;
        
        isLifting = true;
        isMovingUp = true;
        GD.Print($"LiftedPlatform '{Name}': Starting lifting animation");
        
        // Start the lifting cycle
        LiftUp();
    }

    /// <summary>
    /// Stops the lifting animation
    /// </summary>
    public void StopLifting()
    {
        if (!isLifting) return;
        
        isLifting = false;
        
        if (liftTween != null && IsInstanceValid(liftTween))
        {
            liftTween.Kill();
            liftTween = null;
        }
        
        GD.Print($"LiftedPlatform '{Name}': Stopping lifting animation");
    }

    /// <summary>
    /// Resets the platform to its starting position
    /// </summary>
    public void ResetToStartingPosition()
    {
        StopLifting();
        Position = startingPosition;
        isMovingUp = true;
    }

    /// <summary>
    /// Animates the platform lifting up
    /// </summary>
    private void LiftUp()
    {
        if (!isLifting) return;
        
        isMovingUp = true;
        
        // Create new tween
        liftTween = CreateTween();
        liftTween.TweenProperty(this, "position", liftedPosition, LiftUpDuration);
        liftTween.TweenCallback(Callable.From(OnReachedTop));
        
        GD.Print($"LiftedPlatform '{Name}': Lifting up from {Position} to {liftedPosition}");
    }

    /// <summary>
    /// Animates the platform lifting down
    /// </summary>
    private void LiftDown()
    {
        if (!isLifting) return;
        
        isMovingUp = false;
        
        // Create new tween
        liftTween = CreateTween();
        liftTween.TweenProperty(this, "position", startingPosition, LiftDownDuration);
        liftTween.TweenCallback(Callable.From(OnReachedBottom));
        
        GD.Print($"LiftedPlatform '{Name}': Lifting down from {Position} to {startingPosition}");
    }

    /// <summary>
    /// Called when the platform reaches the top position
    /// </summary>
    private void OnReachedTop()
    {
        if (!isLifting) return;
        
        GD.Print($"LiftedPlatform '{Name}': Reached top position, pausing for {PauseAtTopDuration}s");
        
        // Pause at the top, then start going down
        var pauseTimer = GetTree().CreateTimer(PauseAtTopDuration);
        pauseTimer.Timeout += () => {
            if (isLifting)
            {
                LiftDown();
            }
        };
    }

    /// <summary>
    /// Called when the platform reaches the bottom position
    /// </summary>
    private void OnReachedBottom()
    {
        if (!isLifting) return;
        
        GD.Print($"LiftedPlatform '{Name}': Reached bottom position, pausing for {PauseAtBottomDuration}s");
        
        // Pause at the bottom, then start going up again
        var pauseTimer = GetTree().CreateTimer(PauseAtBottomDuration);
        pauseTimer.Timeout += () => {
            if (isLifting)
            {
                LiftUp();
            }
        };
    }

    /// <summary>
    /// Gets whether the platform is currently lifting
    /// </summary>
    public bool IsLifting()
    {
        return isLifting;
    }

    /// <summary>
    /// Gets whether the platform is currently moving up
    /// </summary>
    public bool IsMovingUp()
    {
        return isMovingUp;
    }

    /// <summary>
    /// Gets the starting position of the platform
    /// </summary>
    public Vector2 GetStartingPosition()
    {
        return startingPosition;
    }

    /// <summary>
    /// Gets the lifted position of the platform
    /// </summary>
    public Vector2 GetLiftedPosition()
    {
        return liftedPosition;
    }
}