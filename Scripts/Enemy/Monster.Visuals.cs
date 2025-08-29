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
                    // Ensure the damage area is configured to detect players when we
                    // enable it during fade-in. Area2D must be placed on a collision
                    // layer that the player belongs to; otherwise GetOverlappingBodies
                    // will return empty even if the bodies are overlapping.
                    // Configure the area's collision mask to detect the player's
                    // collision layer. Don't hardcode layer ids — read the player's
                    // CollisionLayer at runtime if possible.
                    try
                    {
                        var playerNode = GetTree().GetFirstNodeInGroup("player") as PhysicsBody2D;
                        if (playerNode != null)
                        {
                            _damageArea.CollisionMask = playerNode.CollisionLayer;
                        }
                        else
                        {
                            // Fallback to existing mask if present, otherwise common player layer 2
                            _damageArea.CollisionMask = _damageArea.CollisionMask != 0 ? _damageArea.CollisionMask : 2;
                        }

                        // Keep the area's collision layer consistent with the monster's
                        // collision layer so other systems that depend on layers behave
                        // correctly.
                        _damageArea.CollisionLayer = GetCollisionLayer();
                        // Use deferred set to avoid calling into Godot core during signal handling
                        try { _damageArea.SetDeferred("monitoring", true); } catch { _damageArea.Monitoring = true; }
                        try { _damageArea.SetDeferred("monitorable", true); } catch { _damageArea.Monitorable = true; }
                    }
                    catch { }

                    // Defer the overlap check until after monitoring has been
                    // applied. SetDeferred schedules the monitoring change for the
                    // end of the frame; CallDeferred will run our overlap check
                    // after that so GetOverlappingBodies() will work reliably.
                    CallDeferred(nameof(HandleDamageAreaEnable));
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

        // Disable the damage area while fading out so it doesn't continue to
        // detect or report overlaps while the monster is invisible.
        try
        {
                if (_damageArea != null)
                {
                    try { _damageArea.SetDeferred("monitoring", false); } catch { _damageArea.Monitoring = false; }
                    try { _damageArea.SetDeferred("monitorable", false); } catch { _damageArea.Monitorable = false; }
                    // Put the area on no layer to avoid any accidental detection
                    // from other systems while hidden.
                    _damageArea.CollisionLayer = 0;
                }
        }
        catch { }

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

    // Helper invoked deferred after enabling the damage area's monitoring so
    // GetOverlappingBodies() can be called without hitting the engine's locked state.
    private void HandleDamageAreaEnable()
    {
        if (_damageArea == null) return;

        try
        {
            var overlappingBodies = _damageArea.GetOverlappingBodies();
            foreach (var b in overlappingBodies)
            {
                if (b is Node2D body && body.IsInGroup("player"))
                    OnPlayerEntered(body);
            }
        }
        catch { }

        // Fallback proximity check
        try
        {
            var playerNode = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (playerNode != null)
            {
                float radius = 32.0f;
                try
                {
                    foreach (Node child in _damageArea.GetChildren())
                    {
                        if (child is CollisionShape2D cs && cs.Shape is CircleShape2D circle)
                        {
                            radius = circle.Radius;
                            break;
                        }
                    }
                }
                catch { }

                if (playerNode.GlobalPosition.DistanceTo(GlobalPosition) <= radius)
                    OnPlayerEntered(playerNode);
            }
        }
        catch { }
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
