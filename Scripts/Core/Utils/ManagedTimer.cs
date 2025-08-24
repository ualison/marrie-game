using Godot;
using System;

/// <summary>
/// Managed timer system that properly cleans up when nodes are freed
/// Prevents dangling timer references and memory leaks
/// </summary>
public class ManagedTimer
{
    private SceneTreeTimer _timer;
    private readonly WeakReference _ownerRef;
    private bool _isCanceled = false;

    public ManagedTimer(Node owner, float duration, Action callback)
    {
        _ownerRef = new WeakReference(owner);
        
        if (IsOwnerValid())
        {
            _timer = owner.GetTree().CreateTimer(duration);
            _timer.Timeout += () => {
                if (!_isCanceled && IsOwnerValid())
                {
                    try
                    {
                        callback?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error in ManagedTimer callback: {ex.Message}", "ManagedTimer");
                    }
                }
            };
        }
        else
        {
            Log.Warning("ManagedTimer created with invalid owner", "ManagedTimer");
        }
    }

    public void Cancel()
    {
        _isCanceled = true;
        _timer = null;
    }

    private bool IsOwnerValid()
    {
        var owner = _ownerRef.Target as Node;
        return owner != null && GodotObject.IsInstanceValid(owner);
    }

    public bool IsActive => !_isCanceled && _timer != null && IsOwnerValid();
}

/// <summary>
/// Extension methods for easy ManagedTimer usage
/// </summary>
public static class ManagedTimerExtensions
{
    public static ManagedTimer CreateManagedTimer(this Node node, float duration, Action callback)
    {
        return new ManagedTimer(node, duration, callback);
    }
}
