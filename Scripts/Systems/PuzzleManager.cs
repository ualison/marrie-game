using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class PuzzleManager : Node2D
{
    #region Export Variables
    [Export] public float FadeOutDuration { get; set; } = 3.0f; // Time to wait before hiding all elements
    [Export] public float MainPlatformFadeDuration { get; set; } = 1.0f; // How long the main platform fade takes
    [Export] public float PlatformFadeInDuration { get; set; } = 0.5f; // How long platforms take to fade in
    [Export] public float PlatformFadeOutDuration { get; set; } = 0.5f; // How long platforms take to fade out
    #endregion

    #region Private Variables
    private List<Node2D> rocks = new List<Node2D>();
    private List<Node2D> mainPlatforms = new List<Node2D>();
    private List<Node2D> randomPlatforms = new List<Node2D>();
    private List<PuzzleCrystal> puzzleCrystals = new List<PuzzleCrystal>();
    
    private bool isMainPlatformFading = false;
    private bool allElementsHidden = false;
    private Tween fadeOutTween;
    // Per-node tween map for platform fades
    private Dictionary<Node2D, Tween> nodeTweens = new Dictionary<Node2D, Tween>();
    
    // Track previous state to avoid spam logging
    private int previousActiveCrystals = -1;
    
    // Track visibility states to prevent repeated calls
    private bool rocksVisible = false;
    private bool mainPlatformsVisible = false;
    private bool randomPlatformsVisible = false;
    
    // Removed global platform fading flag; handled per-node via nodeTweens
    #endregion

    public override void _Ready()
    {
        // Scan the scene for the different types of objects
        ScanSceneObjects();
        
        // Initially hide rocks, main platforms, and random platforms
        InitializeObjectVisibility();
    }

    public override void _Process(double delta)
    {
        if (allElementsHidden) 
        {
            // In final sequence - no longer manage visibility, let rocks fall naturally
            return;
        }
        
        // Check the state of puzzle crystals and manage visibility accordingly
        CheckPuzzleCrystalStates();
    }

    private void ScanSceneObjects()
    {
        // Clear existing lists
        rocks.Clear();
        mainPlatforms.Clear();
        randomPlatforms.Clear();
        puzzleCrystals.Clear();

        // Recursively scan all children in the scene
        ScanChildrenRecursively(this);

        GD.Print($"PuzzleManager found: {rocks.Count} Rocks, {mainPlatforms.Count} MainPlatforms, {randomPlatforms.Count} RandomPlatforms, {puzzleCrystals.Count} PuzzleCrystals");
        
        // Debug: Print names of found objects
        foreach (var rock in rocks) GD.Print($"Found Rock: {rock.Name}");
        foreach (var platform in mainPlatforms) GD.Print($"Found MainPlatform: {platform.Name}");
        foreach (var platform in randomPlatforms) GD.Print($"Found RandomPlatform: {platform.Name}");
        foreach (var crystal in puzzleCrystals) GD.Print($"Found PuzzleCrystal: {crystal.Name}");
    }

    private void ScanChildrenRecursively(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            // Check node names or types to categorize objects
            string nodeName = child.Name.ToString().ToLower();
            
            // Check if this is a Rock class instance (more reliable than name checking)
            if (child is Rock rock && !rocks.Contains(rock))
            {
                rocks.Add(rock);
            }
            else if (nodeName.Contains("mainplatform") && child is Node2D mainPlatform && !mainPlatforms.Contains(mainPlatform))
            {
                mainPlatforms.Add(mainPlatform);
            }
            else if ((nodeName.Contains("randomplatform") || nodeName.Contains("randompat")) && child is Node2D randomPlatform && !randomPlatforms.Contains(randomPlatform))
            {
                randomPlatforms.Add(randomPlatform);
            }
            else if (child is PuzzleCrystal puzzleCrystal && !puzzleCrystals.Contains(puzzleCrystal))
            {
                puzzleCrystals.Add(puzzleCrystal);
            }
            
            // Continue scanning recursively
            ScanChildrenRecursively(child);
        }
    }

    private void InitializeObjectVisibility()
    {
        // Initially hide rocks, main platforms, and random platforms
        SetObjectsVisibility(rocks, false);
        SetObjectsVisibility(mainPlatforms, false);
        SetObjectsVisibility(randomPlatforms, false);
        
        // Track initial states
        rocksVisible = false;
        mainPlatformsVisible = false;
        randomPlatformsVisible = false;
        
        GD.Print("PuzzleManager: Initially hidden rocks, main platforms, and random platforms");
    }

    private void CheckPuzzleCrystalStates()
    {
        if (puzzleCrystals.Count == 0) return;

        int activeCrystals = puzzleCrystals.Count(crystal => crystal.IsActive);
        
        // Only log when state changes to avoid spam
        if (activeCrystals != previousActiveCrystals)
        {
            GD.Print($"PuzzleManager: Active crystals: {activeCrystals}/{puzzleCrystals.Count}");
            foreach (var crystal in puzzleCrystals)
            {
                GD.Print($"Crystal {crystal.Name}: IsActive = {crystal.IsActive}");
            }
            previousActiveCrystals = activeCrystals;
        }
        
        if (activeCrystals >= 1 && activeCrystals < puzzleCrystals.Count)
        {
            // Show rocks, main platforms, and random platforms when at least one crystal is active (but not all)
            // Only change visibility if it's different from current state
            if (!rocksVisible)
            {
                FadePlatformsIn(rocks);
                rocksVisible = true;
                GD.Print("PuzzleManager: Fading in rocks");
            }
            
            if (!mainPlatformsVisible)
            {
                FadePlatformsIn(mainPlatforms);
                mainPlatformsVisible = true;
                GD.Print("PuzzleManager: Fading in main platforms");
            }
            
            if (!randomPlatformsVisible)
            {
                FadePlatformsIn(randomPlatforms);
                randomPlatformsVisible = true;
                GD.Print("PuzzleManager: Fading in random platforms");
            }
            
            // Stop main platform fading if it was happening
            if (isMainPlatformFading)
            {
                StopMainPlatformFade();
            }
        }
        else if (activeCrystals == 0)
        {
            // Hide objects when no crystals are active
            // Only change visibility if it's different from current state and not in final sequence
            if (!allElementsHidden && rocksVisible)
            {
                FadePlatformsOut(rocks);
                rocksVisible = false;
                GD.Print("PuzzleManager: Fading out rocks (no active crystals)");
            }
            
            if (!allElementsHidden && mainPlatformsVisible)
            {
                FadePlatformsOut(mainPlatforms);
                mainPlatformsVisible = false;
                GD.Print("PuzzleManager: Fading out main platforms (no active crystals)");
            }
            
            if (!allElementsHidden && randomPlatformsVisible)
            {
                FadePlatformsOut(randomPlatforms);
                randomPlatformsVisible = false;
                GD.Print("PuzzleManager: Fading out random platforms (no active crystals)");
            }
            
            // Stop main platform fading if it was happening
            if (isMainPlatformFading)
            {
                StopMainPlatformFade();
            }
        }
        else if (activeCrystals == puzzleCrystals.Count && puzzleCrystals.Count > 0)
        {
            // All crystals are active - start the final sequence
            if (!isMainPlatformFading && !allElementsHidden)
            {
                // Ensure rocks are visible for the final sequence
                if (!rocksVisible)
                {
                    FadePlatformsIn(rocks);
                    rocksVisible = true;
                    GD.Print("PuzzleManager: Fading in rocks for final sequence");
                }
                
                GD.Print("PuzzleManager: All crystals active - starting final sequence");
                StartFinalSequence();
            }
        }
    }

    private void SetObjectsVisibility(List<Node2D> objects, bool visible)
    {
        foreach (Node2D obj in objects)
        {
            if (obj != null && IsInstanceValid(obj))
            {
                obj.Visible = visible;
                
                // If this is a rock being made visible, re-enable its physics
                if (visible && obj is Rock rock)
                {
                    // Defer physics enabling to next frame to ensure proper initialization
                    rock.CallDeferred(nameof(rock.ReEnablePhysics));
                    GD.Print($"PuzzleManager: Re-enabling physics for rock: {rock.Name}");
                }
                
                // Handle collision management for RandomPlatforms
                if (randomPlatforms.Contains(obj))
                {
                    if (visible)
                    {
                        EnableCollisionsRecursive(obj);
                    }
                    else
                    {
                        DisableCollisionsRecursive(obj);
                    }
                }
            }
        }
    }

    private void FadePlatformsIn(List<Node2D> objects)
    {
        foreach (Node2D obj in objects)
        {
            if (obj == null || !IsInstanceValid(obj)) continue;

            // Stop any existing tween on this node
            KillNodeTween(obj);

            // Ensure visible and start from transparent
            obj.Visible = true;
            var c = obj.Modulate;
            obj.Modulate = new Color(c.R, c.G, c.B, 0f);

            // Create a tween for this node
            var tween = CreateTween();
            nodeTweens[obj] = tween;
            tween.TweenProperty(obj, "modulate:a", 1.0f, PlatformFadeInDuration);

            // Capture local variable for closure safety
            var target = obj;
            tween.Finished += () =>
            {
                if (!IsInstanceValid(target)) return;
                // Only clear if this is still the active tween for this node
                if (nodeTweens.TryGetValue(target, out var active) && active == tween)
                {
                    // Ensure fully visible at end
                    target.Modulate = Colors.White;
                    nodeTweens.Remove(target);
                    
                    // Re-enable collisions for RandomPlatforms when they become visible
                    if (randomPlatforms.Contains(target))
                    {
                        EnableCollisionsRecursive(target);
                    }
                }
            };

            // Re-enable physics for rocks when they appear
            if (obj is Rock rock)
            {
                rock.CallDeferred(nameof(rock.ReEnablePhysics));
                GD.Print($"PuzzleManager: Re-enabling physics for rock: {rock.Name}");
            }
        }
    }

    private void FadePlatformsOut(List<Node2D> objects, System.Action onComplete = null)
    {
        // Track completion across multiple nodes
        int remaining = 0;
        foreach (var obj in objects)
        {
            if (obj != null && IsInstanceValid(obj)) remaining++;
        }
        if (remaining == 0)
        {
            onComplete?.Invoke();
            return;
        }

        void OnOneDone()
        {
            remaining--;
            if (remaining <= 0)
            {
                onComplete?.Invoke();
            }
        }

        foreach (Node2D obj in objects)
        {
            if (obj == null || !IsInstanceValid(obj)) { OnOneDone(); continue; }

            // Stop any existing tween on this node
            KillNodeTween(obj);

            var tween = CreateTween();
            nodeTweens[obj] = tween;
            tween.TweenProperty(obj, "modulate:a", 0.0f, PlatformFadeOutDuration);

            var target = obj;
            tween.Finished += () =>
            {
                if (!IsInstanceValid(target)) { OnOneDone(); return; }
                if (nodeTweens.TryGetValue(target, out var active) && active == tween)
                {
                    target.Visible = false;
                    target.Modulate = Colors.White; // reset for next time
                    nodeTweens.Remove(target);
                    
                    // Disable collisions for RandomPlatforms when they become invisible
                    if (randomPlatforms.Contains(target))
                    {
                        DisableCollisionsRecursive(target);
                    }
                }
                OnOneDone();
            };
        }
    }

    private void KillNodeTween(Node2D node)
    {
        if (node == null) return;
        if (nodeTweens.TryGetValue(node, out var t) && t != null && IsInstanceValid(t))
        {
            t.Kill();
        }
        nodeTweens.Remove(node);
    }

    private void StartFinalSequence()
    {
        GD.Print("PuzzleManager: All crystals active - starting final sequence");
        
        // Start fading out the main platform
        FadeOutMainPlatforms();
        
        // After the fade duration, hide all elements
        var timer = GetTree().CreateTimer(FadeOutDuration);
        timer.Timeout += () => HideAllElements();
    }

    private void FadeOutMainPlatforms()
    {
        if (mainPlatforms.Count == 0) return;
        
        isMainPlatformFading = true;
        
        // Stop any existing fade tween
        if (fadeOutTween != null)
        {
            fadeOutTween.Kill();
        }
        
        fadeOutTween = CreateTween();
        fadeOutTween.SetParallel(true); // Allow multiple properties to animate at once
        
        foreach (Node2D platform in mainPlatforms)
        {
            if (platform != null && IsInstanceValid(platform))
            {
                // Ensure no per-node tween is running concurrently
                KillNodeTween(platform);
                // Fade out the platform's modulate alpha
                fadeOutTween.TweenProperty(platform, "modulate:a", 0.0f, MainPlatformFadeDuration);
            }
        }
    }

    private void StopMainPlatformFade()
    {
        isMainPlatformFading = false;
        
        if (fadeOutTween != null)
        {
            fadeOutTween.Kill();
            fadeOutTween = null;
        }
        
        // Stop any per-node tweens on main platforms
        foreach (var platform in mainPlatforms)
        {
            if (platform != null && IsInstanceValid(platform))
            {
                KillNodeTween(platform);
            }
        }
        
        // Reset main platform visibility
        foreach (Node2D platform in mainPlatforms)
        {
            if (platform != null && IsInstanceValid(platform))
            {
                platform.Modulate = Colors.White; // Reset to fully opaque
            }
        }
    }

    private void HideAllElements()
    {
        GD.Print("PuzzleManager: Hiding platforms and crystals, but keeping rocks visible to fall");
        
        allElementsHidden = true;
        
        // Ensure any active platform fading stops now that we're finishing
        if (isMainPlatformFading)
        {
            isMainPlatformFading = false;
            if (fadeOutTween != null)
            {
                fadeOutTween.Kill();
                fadeOutTween = null;
            }
        }
        
        // Hide main platforms and random platforms, but keep rocks visible so they can fall
        SetObjectsVisibility(mainPlatforms, false);
        SetObjectsVisibility(randomPlatforms, false);
        
        // Disable collisions for hidden platforms so invisible colliders don't block the player
        DisableCollisionsForNodes(mainPlatforms);
        DisableCollisionsForNodes(randomPlatforms);
        
        // Update visibility tracking for platforms only
        mainPlatformsVisible = false;
        randomPlatformsVisible = false;
        
        // Keep rocks visible and ensure they can fall - they should remain rocksVisible = true
        GD.Print("PuzzleManager: Rocks remain visible and falling due to gravity");
        
        // Hide puzzle crystals
        foreach (PuzzleCrystal crystal in puzzleCrystals)
        {
            if (crystal != null && IsInstanceValid(crystal))
            {
                crystal.Visible = false;
                // Disable collisions for crystals as they're no longer interactable/visible
                DisableCollisionsRecursive(crystal);
            }
        }
        
        // Optionally hide the entire puzzle manager
        // this.Visible = false;
    }

    /// <summary>
    /// Disables all collisions for provided nodes and their descendants.
    /// Keeps rocks untouched by only applying this to platform/crystal lists.
    /// </summary>
    /// <param name="nodes">Nodes to recursively disable collisions on</param>
    private void DisableCollisionsForNodes(List<Node2D> nodes)
    {
        foreach (var n in nodes)
        {
            if (n != null && IsInstanceValid(n))
            {
                DisableCollisionsRecursive(n);
            }
        }
    }

    /// <summary>
    /// Recursively disables collisions for CollisionObjects and CollisionShapes under a node.
    /// Uses deferred property setting to avoid mutating physics state mid-step.
    /// </summary>
    private void DisableCollisionsRecursive(Node node)
    {
        if (node == null || !IsInstanceValid(node)) return;

        if (node is CollisionObject2D co)
        {
            // Zero out layers/masks so it no longer participates in collisions
            co.SetDeferred("collision_layer", 0u);
            co.SetDeferred("collision_mask", 0u);
        }

        if (node is CollisionShape2D cs)
        {
            cs.SetDeferred("disabled", true);
        }
        else if (node is CollisionPolygon2D cp)
        {
            cp.SetDeferred("disabled", true);
        }

        foreach (Node child in node.GetChildren())
        {
            DisableCollisionsRecursive(child);
        }
    }

    /// <summary>
    /// Recursively enables collisions for CollisionObjects and CollisionShapes under a node.
    /// Uses deferred property setting to avoid mutating physics state mid-step.
    /// </summary>
    private void EnableCollisionsRecursive(Node node)
    {
        if (node == null || !IsInstanceValid(node)) return;

        if (node is CollisionObject2D co)
        {
            // Re-enable with default collision layer/mask values
            co.SetDeferred("collision_layer", 1u);
            co.SetDeferred("collision_mask", 1u);
        }

        if (node is CollisionShape2D cs)
        {
            cs.SetDeferred("disabled", false);
        }
        else if (node is CollisionPolygon2D cp)
        {
            cp.SetDeferred("disabled", false);
        }

        foreach (Node child in node.GetChildren())
        {
            EnableCollisionsRecursive(child);
        }
    }

    public override void _ExitTree()
    {
        // Clean up tweens
        if (fadeOutTween != null)
        {
            fadeOutTween.Kill();
            fadeOutTween = null;
        }

        // Kill any remaining per-node tweens
        foreach (var kv in nodeTweens)
        {
            var t = kv.Value;
            if (t != null && IsInstanceValid(t))
            {
                t.Kill();
            }
        }
        nodeTweens.Clear();
    }
}