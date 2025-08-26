using Godot;
using MarrieGame.Core.Enums;

/// <summary>
/// Singleton reality manager that controls the global reality state in the game.
/// Reality changes are triggered by ReplayGem interactions and affect all entities.
/// </summary>
public partial class RealityManager : Node
{
    #region Singleton
    public static RealityManager Instance { get; private set; }
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
    public Reality CurrentReality => _currentReality;

    /// Indicates if the game is currently in replay mode (PastReality)
    public bool IsInReplay => _isInReplay;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Set up singleton instance
        if (Instance == null)
        {
            Instance = this;
            Log.System("RealityManager singleton instance created", "RealityManager");
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
        
        Log.System($"RealityManager initialized - Starting reality: {_currentReality}", "RealityManager");
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
        if (_isInReplay)
        {
            Log.Warning("Already in replay mode", "RealityManager");
            return;
        }

        _currentReality = Reality.PastReality;
        _isInReplay = true;
        
        Log.System("Reality changed to PastReality (replay started)", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
    }

    /// Called when replay ends to return to current reality
    public void EndReplay()
    {
        if (!_isInReplay)
        {
            Log.Warning("Not currently in replay mode", "RealityManager");
            return;
        }

        _currentReality = Reality.CurrentReality;
        _isInReplay = false;
        
        Log.System("Reality changed to CurrentReality (replay ended)", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
    }

    /// Force set reality (for testing or special cases)
    public void SetReality(Reality newReality)
    {
        if (_currentReality == newReality) return;

        _currentReality = newReality;
        _isInReplay = (newReality == Reality.PastReality);
        
        Log.System($"Reality forcibly changed to {newReality}", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
    }

    /// Reset to current reality (for game resets)
    public void Reset()
    {
        _currentReality = Reality.CurrentReality;
        _isInReplay = false;
        
        Log.System("Reality reset to CurrentReality", "RealityManager");
        EmitSignal(SignalName.RealityChanged, (int)_currentReality);
    }
    #endregion

    #region Legacy Support (for transition period)
    /// Legacy method - now just returns current reality for all monsters
    [System.Obsolete("Use RealityManager.Instance.CurrentReality instead")]
    public static Reality GetMonsterReality(Monster monster)
    {
        return Instance?.CurrentReality ?? Reality.CurrentReality;
    }

    /// Legacy method - reality changes should go through StartReplay/EndReplay
    [System.Obsolete("Reality changes should be triggered by ReplayGem interactions")]
    public static void ChangeAllMonstersReality(Reality newReality, SceneTree sceneTree)
    {
        Log.Warning("ChangeAllMonstersReality is deprecated. Use RealityManager.Instance.SetReality()", "RealityManager");
        Instance?.SetReality(newReality);
    }

    /// Legacy method - individual monster reality changes are no longer supported
    [System.Obsolete("Individual monster reality changes are no longer supported")]
    public static void ChangeMonsterReality(Monster monster, Reality newReality)
    {
        Log.Warning("Individual monster reality changes are deprecated. All entities use global reality.", "RealityManager");
    }

    /// Legacy method - reality is now global, not per-monster
    [System.Obsolete("Reality toggling should be done through ReplayGem interactions")]
    public static void ToggleAllMonstersReality(SceneTree sceneTree)
    {
        Log.Warning("ToggleAllMonstersReality is deprecated. Use ReplayGem to trigger reality changes.", "RealityManager");
    }
    #endregion
}
