using Godot;

public partial class Monster : CharacterBody2D
{
    #region Movement Helpers (moved for readability)
    private void ApplyGravity(double delta)
    {
        if (!IsOnFloor())
            Velocity = new Vector2(Velocity.X, Velocity.Y + _gravity * (float)delta);
    }

    private void UpdateMovement()
    {
        if (_player == null) return;

        float distanceToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);

        // If too close to player, don't move toward them
        if (distanceToPlayer <= MinimumFollowDistance)
        {
            Velocity = new Vector2(0, Velocity.Y);
            return;
        }

        // Normal following behavior - maintain FollowDistance behind player
        Vector2 targetPos = _player.GlobalPosition - new Vector2(FollowDistance, 0);
        float distanceToTarget = GlobalPosition.DistanceTo(targetPos);

        if (distanceToTarget > 10.0f)
        {
            Vector2 direction = (targetPos - GlobalPosition).Normalized();
            Velocity = new Vector2(direction.X * WalkSpeed, Velocity.Y);
            UpdateSpriteDirection();
        }
        else
        {
            // Stop moving horizontally but keep current velocity for smoother stopping
            Velocity = new Vector2(0, Velocity.Y);
        }
    }

    private void UpdateSpriteDirection()
    {
        if (_spriteNode == null || _player == null) return;

        bool shouldFaceLeft = _player.GlobalPosition.X > GlobalPosition.X;
        var scale = _spriteNode.Scale;

        // Only update scale if direction actually changed to avoid unnecessary updates
        bool currentlyFacingLeft = scale.X < 0;
        if (currentlyFacingLeft != shouldFaceLeft)
        {
            _spriteNode.Scale = new Vector2(shouldFaceLeft ? -Mathf.Abs(scale.X) : Mathf.Abs(scale.X), scale.Y);
        }
    }
    #endregion
}
