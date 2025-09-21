using Godot;
using MarrieGame.Core.Logging;

/// <summary>
/// PuffCageKey item that behaves similar to rocks in the puzzle system.
/// Initially invisible and becomes visible when puzzle crystals are active.
/// Uses physics simulation with RigidBody2D for natural falling behavior.
/// </summary>
public partial class PuffCageKey : Node2D
{
    #region Private Variables
    private RigidBody2D _rigidBody;
    private Sprite2D _keySprite;
    private CollisionShape2D _collisionShape;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        InitializeComponents();
        Log.System("PuffCageKey ready", "PuffCageKey");
    }

    private void InitializeComponents()
    {
        // Get components from the scene
        _rigidBody = GetNode<RigidBody2D>("RigidBody2D");
        _keySprite = GetNode<Sprite2D>("RigidBody2D/KeySprite");
        _collisionShape = GetNode<CollisionShape2D>("RigidBody2D/CollisionShape2D");

        if (_rigidBody == null)
        {
            Log.Error("PuffCageKey: RigidBody2D not found in scene", "PuffCageKey");
        }

        if (_keySprite == null)
        {
            Log.Error("PuffCageKey: KeySprite not found in scene", "PuffCageKey");
        }

        if (_collisionShape == null)
        {
            Log.Error("PuffCageKey: CollisionShape2D not found in scene", "PuffCageKey");
        }
    }
    #endregion

    #region Physics Management
    /// <summary>
    /// Re-enable physics for the key (useful when key becomes visible after being hidden)
    /// Similar to Rock.ReEnablePhysics()
    /// </summary>
    public void ReEnablePhysics()
    {
        if (_rigidBody != null)
        {
            // Reset sleeping state and ensure physics updates
            _rigidBody.SetSleeping(false);
            _rigidBody.LinearVelocity = Vector2.Zero; // Reset any existing velocity
            _rigidBody.AngularVelocity = 0.0f; // Reset rotation
            
            // Enable physics if it was disabled
            _rigidBody.Freeze = false;
            
            // Add a tiny downward impulse to kickstart physics
            CallDeferred(nameof(ApplyGravityImpulse));
            
            Log.System("PuffCageKey physics re-enabled", "PuffCageKey");
        }
    }

    private void ApplyGravityImpulse()
    {
        if (_rigidBody != null && !_rigidBody.Freeze)
        {
            // Apply a small downward impulse to ensure gravity kicks in
            _rigidBody.ApplyImpulse(new Vector2(0, 1.0f));
        }
    }

    /// <summary>
    /// Disable physics for the key (useful when hiding the key)
    /// </summary>
    public void DisablePhysics()
    {
        if (_rigidBody != null)
        {
            _rigidBody.Freeze = true;
            _rigidBody.LinearVelocity = Vector2.Zero;
            _rigidBody.AngularVelocity = 0.0f;
            Log.System("PuffCageKey physics disabled", "PuffCageKey");
        }
    }

    /// <summary>
    /// Debug method to log the current physics state of the key
    /// </summary>
    public void LogPhysicsState()
    {
        if (_rigidBody != null)
        {
            Log.System($"PuffCageKey Physics State - Frozen: {_rigidBody.Freeze}, Gravity: {_rigidBody.GravityScale}, Layer: {_rigidBody.CollisionLayer}, Mask: {_rigidBody.CollisionMask}, Sleeping: {_rigidBody.Sleeping}, Position: {GlobalPosition}", "PuffCageKey");
        }
    }
    #endregion
}