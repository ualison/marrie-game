using Godot;

// Visual-related helpers for Player (partial class)
public partial class Player
{
    #region Visual Implementation (partial)
    private void UpdateVisuals()
    {
        // Update sprite direction, animations, and special effects
        float direction = Input.GetAxis("ui_left", "ui_right");
        UpdateSpriteDirection(direction);
        UpdateAnimations(direction);
        UpdateInvulnerabilityVisual();
    }

    private void UpdateSpriteDirection(float direction)
    {
        // Flip sprite based on movement direction
        if (_sprite == null || direction == 0) return;

        bool shouldFaceRight = direction > 0;
        if (shouldFaceRight != _facingRight)
        {
            _facingRight = shouldFaceRight;
            var scale = _sprite.Scale;
            _sprite.Scale = new Vector2(_facingRight ? Mathf.Abs(scale.X) : -Mathf.Abs(scale.X), scale.Y);
        }
    }

    private void UpdateAnimations(float direction)
    {
        // Play appropriate animation based on state
        if (_sprite is not AnimatedSprite2D animSprite || animSprite.SpriteFrames == null) return;

        string animation = DetermineAnimation(direction);
        if (animSprite.SpriteFrames.HasAnimation(animation))
            animSprite.Play(animation);
    }

    private string DetermineAnimation(float direction)
    {
        // Choose animation based on movement state
        if (!IsOnFloor())
            return Velocity.Y < 0 ? "jump" : "fall";
        
        return Mathf.Abs(direction) > 0 ? "walk" : "idle";
    }

    private void UpdateInvulnerabilityVisual()
    {
        // Start or stop blinking effect based on invulnerability state
        if (_playerLife == null) return;

        if (_playerLife.IsInvulnerable && !_isBlinking)
            StartBlinkEffect();
        else if (!_playerLife.IsInvulnerable && _isBlinking)
            StopVisualEffects();
    }

    private void StartBlinkEffect()
    {
        if (_sprite == null) return;

        _isBlinking = true;
        _blinkTween?.Kill();

        // Softer red color instead of pure red
        var lightRed = new Color(1.0f, 0.6f, 0.6f, 1.0f);

        _blinkTween = CreateTween();
        _blinkTween.SetLoops();
        _blinkTween.TweenProperty(_sprite, "modulate", lightRed, 0.15f);
        _blinkTween.TweenProperty(_sprite, "modulate", Colors.White, 0.15f);

        _visualTimer.WaitTime = 2.0f;
        _visualTimer.Start();
    }

    private void StopVisualEffects()
    {
        // Stop all visual effects and restore normal appearance
        _isBlinking = false;
        _visualTimer.Stop();
        _blinkTween?.Kill();
        RestoreNormalAppearance();
    }

    private void RestoreNormalAppearance()
    {
        // Reset sprite to normal color
        if (_sprite != null)
            _sprite.Modulate = Colors.White;
    }
    #endregion
}
