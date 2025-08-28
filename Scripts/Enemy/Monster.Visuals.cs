using Godot;
using MarrieGame.Core.Enums;

public partial class Monster : CharacterBody2D
{
    #region Visual and Audio Effects (moved for readability)
    private void FadeIn()
    {
        if (_spriteNode == null) return;

        _currentTween?.Kill();

        _spriteNode.Visible = true;
        _spriteNode.Modulate = new Color(1, 1, 1, 0);

        _currentTween = CreateTween();
        _currentTween.TweenProperty(_spriteNode, "modulate:a", 1.0f, 0.5f);
        _currentTween.TweenCallback(Callable.From(() =>
        {
            // Now that fade-in finished, allow player interactions again
            _isFullyVisible = true;
            _isTransitioning = false; // No longer transitioning
            SetCollisionMask(1u);
            SetCollisionLayer(1u);

            // If the damage area exists, ensure it's monitoring and check for any
            // overlapping players that might already be inside the area. Godot
            // does not emit BodyEntered when an area's collision mask/layer is
            // changed while another body is already overlapping, so we must
            // manually check and process overlaps here to avoid requiring the
            // player to exit/re-enter.
            try
            {
                if (_damageArea != null)
                {
                    _damageArea.Monitoring = true;
                    _damageArea.Monitorable = true;

                    var overlappingBodies = _damageArea.GetOverlappingBodies();
                    foreach (var b in overlappingBodies)
                    {
                        if (b is Node2D body && body.IsInGroup("player"))
                        {
                            // Call the same handler used by the BodyEntered signal
                            OnPlayerEntered(body);
                        }
                    }
                }
            }
            catch {
                // Swallow any errors to avoid breaking fade-in flow; logging already
                // happens elsewhere and this is defensive for editor/runtime differences.
            }

            // Delay showing life bar slightly to ensure position is correct
            GetTree().CreateTimer(0.1f).Timeout += () =>
            {
                if (_monsterLifeBar != null)
                {
                    // Update position right before showing the life bar
                    DoUpdateLifeBarPositionForced(); // Force position update even when hidden
                    _monsterLifeBar.Show();
                }
            };

            Log.Debug("Monster fade in complete - fully visible", "Monster");
        }));
    }

    private void FadeOut()
    {
        if (_spriteNode == null) return;

        _currentTween?.Kill();

        // Keep ground collision but disable player interactions when hidden
        SetCollisionMask(1u); // Keep ground collision (bit 0)
        SetCollisionLayer(2u); // Move to layer 2 to avoid player interactions
        _isFullyVisible = false;

        // Hide MonsterLifeBar when fading out
        if (_monsterLifeBar != null)
        {
            _monsterLifeBar.Hide();
        }

        _currentTween = CreateTween();
        _currentTween.TweenProperty(_spriteNode, "modulate:a", 0.0f, 0.5f);
        _currentTween.TweenCallback(Callable.From(() =>
        {
            _spriteNode.Visible = false;
            _isTransitioning = false; // No longer transitioning
            // Life bar is already hidden, no need to update position when not visible
            Log.Debug("Monster fade out complete - fully hidden", "Monster");
        }));
    }

    private void PlayAudio(bool play)
    {
        if (_footstepAudio == null) return;

        if (play && !_footstepAudio.Playing)
            _footstepAudio.Play();
        else if (!play)
            _footstepAudio.Stop();
    }

    public void PlayDamageEffect()
    {
        Reality currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;
        Color effectColor = currentReality == Reality.CurrentReality ? Colors.Red : Colors.Purple;
        PlayDamageEffect(effectColor);
    }

    public void PlayDamageEffect(Color damageColor)
    {
        var targetSprite = _sprite ?? _spriteNode;
        if (targetSprite == null) return;

        var tween = CreateTween();
        tween.TweenProperty(targetSprite, "modulate", damageColor, 0.2f);
        tween.TweenProperty(targetSprite, "modulate", Colors.White, 0.3f);
    }
    #endregion
}
