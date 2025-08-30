using Godot;

public interface IPoolable
{
    // Called when the object is taken from the pool or instantiated for reuse
    void ResetForReuse();

    // Called when the object is returned to the pool
    void OnReleasedToPool();

    // Optional: indicate if this instance was obtained from a pool
    bool IsFromPool { get; set; }
}
