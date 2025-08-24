using Godot;

/// <summary>
/// Simple input handler to clean up Player input logic
/// Much simpler than Command pattern for your needs
/// </summary>
public partial class InputHandler : Node
{
    private Player _player;
    
    public override void _Ready()
    {
        _player = GetParent<Player>();
    }
    
    public void HandleInput()
    {
        if (_player == null || _player.IsPaused() || _player.GetLifeSystem()?.IsDead == true)
            return;
            
        HandleMovement();
        HandleJump();
    }
    
    private void HandleMovement()
    {
        float horizontal = Input.GetAxis("ui_left", "ui_right");
        
        if (horizontal != 0)
        {
            _player.Velocity = new Vector2(horizontal * _player.Speed, _player.Velocity.Y);
        }
        else
        {
            _player.Velocity = new Vector2(Mathf.MoveToward(_player.Velocity.X, 0, _player.Speed), _player.Velocity.Y);
        }
    }
    
    private void HandleJump()
    {
        if (Input.IsActionJustPressed("ui_accept") && _player.IsOnFloor())
        {
            var gravity = ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle();
            var jumpVelocity = -Mathf.Sqrt(2.0f * gravity * _player.JumpHeight);
            _player.Velocity = new Vector2(_player.Velocity.X, jumpVelocity);
        }
    }
}
