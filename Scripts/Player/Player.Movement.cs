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
    #endregion
}
