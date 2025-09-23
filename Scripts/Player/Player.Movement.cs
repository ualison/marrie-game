using Godot;

// Movement-related helpers for Player (partial class)
public partial class Player
{
    #region Movement Implementation (partial)
    private void ProcessMovement(double delta)
    {
        // Apply gravity, handle input, and move character
        ApplyGravity(delta);
        HandleJumpInput();
        HandleMovementInput();
        HandleCatapultInteraction();
        MoveAndSlide();
    }

    private void ApplyGravity(double delta)
    {
        // Apply gravity when not grounded
        if (!IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, Velocity.Y + _gravity * (float)delta);
        }
    }

    private void HandleJumpInput()
    {
        // Jump when accept pressed and on ground
        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, _jumpVelocity);
        }
    }

    private void HandleMovementInput()
    {
        // Get horizontal input and apply movement or friction
        float horizontal = Input.GetAxis("ui_left", "ui_right");

        Velocity = horizontal != 0
            ? new Vector2(horizontal * Speed, Velocity.Y)
            : new Vector2(Mathf.MoveToward(Velocity.X, 0, Speed), Velocity.Y);
    }

    private void HandleCatapultInteraction()
    {
        // Handle collision behavior with catapults based on input
        bool isPushingCatapult = Input.IsActionPressed("push_catapult");
        
        // Check if player is colliding with any catapults
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            var collision = GetSlideCollision(i);
            var collider = collision.GetCollider();
            
            if (collider is Node node && node.IsInGroup("catapults"))
            {
                if (!isPushingCatapult && collider is Node2D catapult)
                {
                    // Push player away from catapult when not trying to push it
                    Vector2 pushDirection = (GlobalPosition - catapult.GlobalPosition).Normalized();
                    Velocity += pushDirection * 50.0f; // Small push away
                }
                // When isPushingCatapult is true, let normal collision physics handle the interaction
            }
        }
    }
    #endregion
}
