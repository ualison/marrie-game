using Godot;
using MarrieGame.Core.Enums;

/// <summary>
/// Singleton reality manager that controls the global reality state in the game.
/// Reality changes are triggered by ReplayGem interactions and affect all entities.
/// </summary>
public partial class RealityManager : Node
{
    #region Singleton
    private static RealityManager _instance;
    
    public static RealityManager Instance 
    { 
        get 
        {
            if (_instance == null)
            {
                Log.Warning("RealityManager.Instance accessed but is null! Make sure RealityManager is in the scene tree.", "RealityManager");
            }
            return _instance;
        }
        private set => _instance = value;
    }
    
    /// <summary>
    /// Checks if RealityManager is available without triggering warning logs
    /// </summary>
    public static bool IsInstanceAvailable => _instance != null;
    #endregion

    #region Events
    [Signal] public delegate void RealityChangedEventHandler(Reality newReality);
    #endregion

    #region State
    private Reality _currentReality = Reality.CurrentReality;
    private bool _isInReplay = false;
    #endregion

    #region Properties
    /// Gets the current global reality state
    public Reality CurrentReality 
    { 
        get 
        {
            return _currentReality;
        }
    }

    /// Indicates if the game is currently in replay mode (PastReality)
    public bool IsInReplay => _isInReplay;
    
    /// Gets the current reality with debug logging
    public Reality GetCurrentRealityWithLogging()
    {
        Log.System($"Current Reality requested: {_currentReality} (IsInReplay: {_isInReplay})", "RealityManager");
        return _currentReality;
    }
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Set up singleton instance
        if (Instance == null)
        {
            Instance = this;
            Log.System("RealityManager singleton instance created and set", "RealityManager");
        }
        else
        {
            Log.Warning("Multiple RealityManager instances detected! Removing duplicate.", "RealityManager");
            QueueFree();
            return;
        }

        // Ensure this node persists across scene changes
        ProcessMode = ProcessModeEnum.Always;
        
        // Initialize in current reality
        _currentReality = Reality.CurrentReality;
        _isInReplay = false;
        
        Log.System($"RealityManager fully initialized - Starting reality: {_currentReality}, IsInReplay: {_isInReplay}", "RealityManager");
        
        // Log instance availability for debugging
        CallDeferred(nameof(LogInstanceStatus));
    }
    
    private void LogInstanceStatus()
    {
        Log.System($"RealityManager.Instance is now available: {Instance != null}", "RealityManager");
        if (Instance != null)
        {
            Log.System($"RealityManager ready for reality changes. Current Reality: {Instance.CurrentReality}", "RealityManager");
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    #endregion

    #region Public API
    /// Called by ReplayGem when player activates replay with sufficient mana
    public void StartReplay()
    {
        Log.System($"StartReplay called - Current state: Reality={_currentReality}, IsInReplay={_isInReplay}", "RealityManager");
        
        if (_isInReplay)
        {
            Log.Warning("Already in replay mode - ignoring StartReplay call", "RealityManager");
            return;
        }

        var previousReality = _currentReality;
        _currentReality = Reality.PastReality;
        _isInReplay = true;
        
        Log.System($"=== REALITY CHANGE === From {previousReality} to {_currentReality} (replay started)", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
        Log.System($"RealityChanged signal emitted with value: {(int)_currentReality}", "RealityManager");
    }

    /// Called when replay ends to return to current reality
    public void EndReplay()
    {
        Log.System($"EndReplay called - Current state: Reality={_currentReality}, IsInReplay={_isInReplay}", "RealityManager");
        
        if (!_isInReplay)
        {
            Log.Warning("Not currently in replay mode - ignoring EndReplay call", "RealityManager");
            return;
        }

        var previousReality = _currentReality;
        _currentReality = Reality.CurrentReality;
        _isInReplay = false;
        
        Log.System($"=== REALITY CHANGE === From {previousReality} to {_currentReality} (replay ended)", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
        Log.System($"RealityChanged signal emitted with value: {(int)_currentReality}", "RealityManager");
    }

    /// Force set reality (for testing or special cases)
    public void SetReality(Reality newReality)
    {
        Log.System($"SetReality called with {newReality} - Current state: Reality={_currentReality}, IsInReplay={_isInReplay}", "RealityManager");
        
        if (_currentReality == newReality) 
        {
            Log.System($"Reality is already {newReality} - no change needed", "RealityManager");
            return;
        }

        var previousReality = _currentReality;
        _currentReality = newReality;
        _isInReplay = (newReality == Reality.PastReality);
        
        Log.System($"=== REALITY CHANGE === From {previousReality} to {_currentReality} (force set)", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
        Log.System($"RealityChanged signal emitted with value: {(int)_currentReality}", "RealityManager");
    }

    /// Reset to current reality (for game resets)
    public void Reset()
    {
        Log.System($"Reset called - Current state: Reality={_currentReality}, IsInReplay={_isInReplay}", "RealityManager");
        
        var previousReality = _currentReality;
        _currentReality = Reality.CurrentReality;
        _isInReplay = false;
        
        Log.System($"=== REALITY CHANGE === From {previousReality} to {_currentReality} (reset)", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
        Log.System($"RealityChanged signal emitted with value: {(int)_currentReality}", "RealityManager");
    }
    #endregion
}
