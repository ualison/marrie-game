using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PuzzleManager : Node2D
{
    #region Export Variables
    [Export] public float FadeOutDuration { get; set; } = 3.0f; // Time to wait before hiding all elements
    [Export] public float MainPlatformFadeDuration { get; set; } = 1.0f; // How long the main platform fade takes
    [Export] public float PlatformFadeInDuration { get; set; } = 0.5f; // How long platforms take to fade in
    [Export] public float PlatformFadeOutDuration { get; set; } = 0.5f; // How long platforms take to fade out
    [Export] public float MinRandomPlatformDuration { get; set; } = 2.0f; // Minimum time a random platform stays visible
    [Export] public float MaxRandomPlatformDuration { get; set; } = 5.0f; // Maximum time a random platform stays visible
    [Export] public float PlatformOverlapDuration { get; set; } = 1.0f; // Time both platforms are visible (overlap for jumping)
    [Export] public int RandomCycleCount { get; set; } = 5; // Number of random cycles before showing ordered pattern
    #endregion

    #region Private Variables
    private List<Node2D> rocks = new List<Node2D>();
    private List<Node2D> mainPlatforms = new List<Node2D>();
    private List<Node2D> randomPlatforms = new List<Node2D>();
    private List<Node2D> liftedPlatforms = new List<Node2D>();
    private List<PuzzleCrystal> puzzleCrystals = new List<PuzzleCrystal>();
    private List<Node2D> puffCageKeys = new List<Node2D>();
    private List<YellowCage> yellowCages = new List<YellowCage>();
    
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
    private bool liftedPlatformsVisible = false;
    private bool puffCageKeysVisible = false;
    
    // YellowCage detachment tracking
    private bool yellowCagesDetached = false;
    
    // Random Platform Cycling System
    private bool isRandomPlatformCyclingActive = false;
    private Node2D currentVisibleRandomPlatform = null;
    private Timer randomPlatformTimer;
    private Random random = new Random();
    
    // Pattern Recognition System
    private int currentCycleCount = 0;
    private bool isShowingOrderedPattern = false;
    private int orderedPatternIndex = 0;
    private List<Node2D> orderedPlatforms = new List<Node2D>();
    
    // Removed global platform fading flag; handled per-node via nodeTweens
    #endregion

    public override void _Ready()
    {
        // Defer scanning and initialization to ensure all nodes are in the tree
        CallDeferred(nameof(DeferredInitialize));
    }

    private void DeferredInitialize()
    {
        // Scan the scene for the different types of objects
        ScanSceneObjects();

        // Initially hide rocks, main platforms, random platforms, and lifted platforms
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
        liftedPlatforms.Clear();
        puzzleCrystals.Clear();
        puffCageKeys.Clear();
        yellowCages.Clear();

        // Recursively scan the entire current scene (not just children of this manager)
        Node scanRoot = GetTree().CurrentScene ?? (Node)this;
        ScanChildrenRecursively(scanRoot);

        GD.Print($"PuzzleManager found: {rocks.Count} Rocks, {mainPlatforms.Count} MainPlatforms, {randomPlatforms.Count} RandomPlatforms, {liftedPlatforms.Count} LiftedPlatforms, {puzzleCrystals.Count} PuzzleCrystals, {puffCageKeys.Count} PuffCageKeys, {yellowCages.Count} YellowCages");
        
        // Debug: Print names of found objects
        foreach (var rock in rocks) GD.Print($"Found Rock: {rock.Name}");
        foreach (var platform in mainPlatforms) GD.Print($"Found MainPlatform: {platform.Name}");
        foreach (var platform in randomPlatforms) GD.Print($"Found RandomPlatform: {platform.Name}");
        foreach (var platform in liftedPlatforms) GD.Print($"Found LiftedPlatform: {platform.Name}");
        foreach (var crystal in puzzleCrystals) GD.Print($"Found PuzzleCrystal: {crystal.Name}");
        foreach (var key in puffCageKeys) GD.Print($"Found PuffCageKey: {key.Name}");
        foreach (var cage in yellowCages) GD.Print($"Found YellowCage: {cage.Name}");
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
            // Check if this is a PuffCageKey class instance
            else if (child is PuffCageKey puffCageKey && !puffCageKeys.Contains(puffCageKey))
            {
                puffCageKeys.Add(puffCageKey);
            }
            else if (nodeName.Contains("mainplatform") && child is Node2D mainPlatform && !mainPlatforms.Contains(mainPlatform))
            {
                mainPlatforms.Add(mainPlatform);
            }
            else if ((nodeName.Contains("randomplatform") || nodeName.Contains("randompat")) && child is Node2D randomPlatform && !randomPlatforms.Contains(randomPlatform))
            {
                randomPlatforms.Add(randomPlatform);
            }
            else if ((nodeName.Contains("liftedplatform") || nodeName.Contains("liftedpat")) && child is Node2D liftedPlatform && !liftedPlatforms.Contains(liftedPlatform))
            {
                liftedPlatforms.Add(liftedPlatform);
            }
            // Check for LiftedPlatform instances by scene file name pattern
            else if (child is Node2D node2D && child.SceneFilePath.Contains("LiftedPlatform") && !liftedPlatforms.Contains(node2D))
            {
                liftedPlatforms.Add(node2D);
            }
            else if (child is PuzzleCrystal puzzleCrystal && !puzzleCrystals.Contains(puzzleCrystal))
            {
                puzzleCrystals.Add(puzzleCrystal);
            }
            // Check if this is a YellowCage class instance
            else if (child is YellowCage yellowCage && !yellowCages.Contains(yellowCage))
            {
                yellowCages.Add(yellowCage);
            }
            
            // Continue scanning recursively
            ScanChildrenRecursively(child);
        }
    }

    private void InitializeObjectVisibility()
    {
        // Initially hide rocks, main platforms, random platforms, lifted platforms, and puff cage keys
        SetObjectsVisibility(rocks, false);
        SetObjectsVisibility(mainPlatforms, false);
        SetObjectsVisibility(randomPlatforms, false);
        SetObjectsVisibility(liftedPlatforms, false);
        SetObjectsVisibility(puffCageKeys, false);
        
        // Track initial states
        rocksVisible = false;
        mainPlatformsVisible = false;
        randomPlatformsVisible = false;
        liftedPlatformsVisible = false;
        puffCageKeysVisible = false;
        
        GD.Print("PuzzleManager: Initially hidden rocks, main platforms, random platforms, lifted platforms, and puff cage keys");
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
            // Show rocks, main platforms, lifted platforms, and puff cage keys when at least one crystal is active (but not all)
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
            
            if (!liftedPlatformsVisible)
            {
                FadePlatformsIn(liftedPlatforms);
                liftedPlatformsVisible = true;
                GD.Print("PuzzleManager: Fading in lifted platforms");
            }
            
            if (!puffCageKeysVisible)
            {
                FadePlatformsIn(puffCageKeys);
                puffCageKeysVisible = true;
                GD.Print("PuzzleManager: Fading in puff cage keys");
            }
            
            // Start random platform cycling system instead of showing all platforms at once
            // LiftedPlatforms are independent, not part of random cycling
            if (!isRandomPlatformCyclingActive)
            {
                StartRandomPlatformCycling();
                randomPlatformsVisible = true; // Keep track for state logic
                GD.Print("PuzzleManager: Starting random platform cycling");
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
            
            if (!allElementsHidden && liftedPlatformsVisible)
            {
                FadePlatformsOut(liftedPlatforms);
                liftedPlatformsVisible = false;
                GD.Print("PuzzleManager: Fading out lifted platforms (no active crystals)");
            }
            
            if (!allElementsHidden && puffCageKeysVisible)
            {
                FadePlatformsOut(puffCageKeys);
                puffCageKeysVisible = false;
                GD.Print("PuzzleManager: Fading out puff cage keys (no active crystals)");
            }
            
            // Stop random platform cycling when no crystals are active
            if (!allElementsHidden && isRandomPlatformCyclingActive)
            {
                StopRandomPlatformCycling();
                randomPlatformsVisible = false;
                GD.Print("PuzzleManager: Stopping random platform cycling (no active crystals)");
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
                // Ensure rocks, lifted platforms, and puff cage keys are visible for the final sequence
                if (!rocksVisible)
                {
                    FadePlatformsIn(rocks);
                    rocksVisible = true;
                    GD.Print("PuzzleManager: Fading in rocks for final sequence");
                }
                
                if (!liftedPlatformsVisible)
                {
                    FadePlatformsIn(liftedPlatforms);
                    liftedPlatformsVisible = true;
                    GD.Print("PuzzleManager: Fading in lifted platforms for final sequence");
                }
                
                if (!puffCageKeysVisible)
                {
                    FadePlatformsIn(puffCageKeys);
                    puffCageKeysVisible = true;
                    GD.Print("PuzzleManager: Fading in puff cage keys for final sequence");
                }
                
                // Detach all YellowCages from their chains when all crystals are active
                if (!yellowCagesDetached && yellowCages.Count > 0)
                {
                    DetachAllYellowCages();
                    yellowCagesDetached = true;
                    GD.Print($"PuzzleManager: Detached {yellowCages.Count} YellowCages from chains (all crystals active)");
                }
                
                // Stop random platform cycling during final sequence
                if (isRandomPlatformCyclingActive)
                {
                    StopRandomPlatformCycling();
                    GD.Print("PuzzleManager: Stopping random platform cycling for final sequence");
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
                
                // If this is a puff cage key being made visible, re-enable its physics
                if (visible && obj is PuffCageKey puffCageKey)
                {
                    // Defer physics enabling to next frame to ensure proper initialization
                    puffCageKey.CallDeferred(nameof(puffCageKey.ReEnablePhysics));
                    GD.Print($"PuzzleManager: Re-enabling physics for puff cage key: {puffCageKey.Name}");
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
                
                // Handle LiftedPlatform: always toggle collisions; run animation if the node is actually a LiftedPlatform
                if (liftedPlatforms.Contains(obj))
                {
                    if (visible)
                    {
                        EnableCollisionsRecursive(obj);
                        if (obj is LiftedPlatform lp)
                        {
                            lp.StartLifting();
                        }
                    }
                    else
                    {
                        DisableCollisionsRecursive(obj);
                        if (obj is LiftedPlatform lp)
                        {
                            lp.StopLifting();
                        }
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
                    
                    // Start lifting animation for LiftedPlatforms when they become visible
                    if (liftedPlatforms.Contains(target))
                    {
                        EnableCollisionsRecursive(target);
                        if (target is LiftedPlatform lp)
                        {
                            lp.StartLifting();
                        }
                    }
                }
            };

            // Re-enable physics for rocks when they appear
            if (obj is Rock rock)
            {
                rock.CallDeferred(nameof(rock.ReEnablePhysics));
                GD.Print($"PuzzleManager: Re-enabling physics for rock: {rock.Name}");
            }

            // Re-enable physics for puff cage keys when they appear
            if (obj is PuffCageKey puffCageKey)
            {
                puffCageKey.CallDeferred(nameof(puffCageKey.ReEnablePhysics));
                GD.Print($"PuzzleManager: Re-enabling physics for puff cage key: {puffCageKey.Name}");
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
                    
                    // Stop lifting animation for LiftedPlatforms when they become invisible
                    if (liftedPlatforms.Contains(target))
                    {
                        DisableCollisionsRecursive(target);
                        if (target is LiftedPlatform lp)
                        {
                            lp.StopLifting();
                        }
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
        GD.Print("PuzzleManager: Hiding platforms and crystals, but keeping rocks and puff cage keys visible to fall");
        
        allElementsHidden = true;
        
        // Stop random platform cycling system
        if (isRandomPlatformCyclingActive)
        {
            StopRandomPlatformCycling();
        }
        
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
        
        // Hide main platforms and random platforms, but keep rocks and puff cage keys visible so they can fall
        SetObjectsVisibility(mainPlatforms, false);
        SetObjectsVisibility(randomPlatforms, false);
        SetObjectsVisibility(liftedPlatforms, false);
        
        // Disable collisions for hidden platforms so invisible colliders don't block the player
        DisableCollisionsForNodes(mainPlatforms);
        DisableCollisionsForNodes(randomPlatforms);
        DisableCollisionsForNodes(liftedPlatforms);
        
        // Update visibility tracking for platforms only
        mainPlatformsVisible = false;
        randomPlatformsVisible = false;
        liftedPlatformsVisible = false;
        
        // Keep rocks and puff cage keys visible and ensure they can fall - they should remain rocksVisible = true and puffCageKeysVisible = true
        GD.Print("PuzzleManager: Rocks and puff cage keys remain visible and falling due to gravity");
        
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

        // Handle TileMapLayer collisions (Godot 4)
        if (node is TileMapLayer tml)
        {
            tml.SetDeferred("collision_layer", 0u);
            tml.SetDeferred("collision_mask", 0u);
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

        // Handle TileMapLayer collisions (Godot 4)
        if (node is TileMapLayer tml)
        {
            tml.SetDeferred("collision_layer", 1u);
            tml.SetDeferred("collision_mask", 1u);
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

    #region Random Platform Cycling System

    /// <summary>
    /// Prepares the ordered platform list sorted by numerical values in platform names
    /// </summary>
    private void PrepareOrderedPlatforms()
    {
        orderedPlatforms.Clear();
        
        // Create a list with valid platforms and their extracted numbers
        var platformsWithNumbers = new List<(Node2D platform, int number)>();
        
        foreach (var platform in randomPlatforms)
        {
            if (platform != null && IsInstanceValid(platform))
            {
                int number = ExtractNumberFromPlatformName(platform.Name.ToString());
                platformsWithNumbers.Add((platform, number));
                GD.Print($"PuzzleManager: Platform '{platform.Name}' extracted number: {number}");
            }
        }
        
        // Sort by number and add to ordered list
        platformsWithNumbers.Sort((a, b) => a.number.CompareTo(b.number));
        
        foreach (var (platform, number) in platformsWithNumbers)
        {
            orderedPlatforms.Add(platform);
        }
        
        GD.Print($"PuzzleManager: Prepared ordered platform sequence with {orderedPlatforms.Count} platforms");
        foreach (var platform in orderedPlatforms)
        {
            GD.Print($"  - {platform.Name}");
        }
    }

    /// <summary>
    /// Extracts numerical value from platform name for ordering
    /// </summary>
    private int ExtractNumberFromPlatformName(string platformName)
    {
        // Try to extract numbers from the platform name
        string numberStr = "";
        
        foreach (char c in platformName)
        {
            if (char.IsDigit(c))
            {
                numberStr += c;
            }
        }
        
        // If we found numbers, parse them
        if (!string.IsNullOrEmpty(numberStr) && int.TryParse(numberStr, out int result))
        {
            return result;
        }
        
        // Fallback: use hash code for consistent ordering if no numbers found
        return Math.Abs(platformName.GetHashCode()) % 1000;
    }

    /// <summary>
    /// Selects the next platform in ordered sequence
    /// </summary>
    private Node2D SelectNextOrderedPlatform()
    {
        if (orderedPlatforms.Count == 0) return null;
        
        // Get the next platform in sequence
        Node2D nextPlatform = orderedPlatforms[orderedPatternIndex];
        orderedPatternIndex = (orderedPatternIndex + 1) % orderedPlatforms.Count;
        
        GD.Print($"PuzzleManager: Selected ordered platform '{nextPlatform.Name}' (index {orderedPatternIndex - 1})");
        
        // If we've completed one full cycle of ordered platforms, reset to random mode
        if (orderedPatternIndex == 0)
        {
            isShowingOrderedPattern = false;
            currentCycleCount = 0;
            GD.Print("PuzzleManager: Completed ordered pattern, returning to random mode");
        }
        
        return nextPlatform;
    }

    /// <summary>
    /// Starts the random platform cycling system when at least one crystal is active
    /// </summary>
    private void StartRandomPlatformCycling()
    {
        if (randomPlatforms.Count == 0 || isRandomPlatformCyclingActive) return;
        
        isRandomPlatformCyclingActive = true;
        GD.Print("PuzzleManager: Starting random platform cycling system");
        
        // Always reset pattern recognition system to start fresh
        int previousCycleCount = currentCycleCount;
        bool wasShowingOrderedPattern = isShowingOrderedPattern;
        
        currentCycleCount = 0;
        isShowingOrderedPattern = false;
        orderedPatternIndex = 0;
        
        // Log the reset for debugging
        if (previousCycleCount > 0 || wasShowingOrderedPattern)
        {
            GD.Print($"PuzzleManager: Resetting pattern system - was at cycle {previousCycleCount}/{RandomCycleCount}, ordered pattern: {wasShowingOrderedPattern}");
        }
        GD.Print($"PuzzleManager: Starting fresh with random count at 0/{RandomCycleCount}");
        
        PrepareOrderedPlatforms();
        
        // Initialize timer if not already created
        if (randomPlatformTimer == null)
        {
            randomPlatformTimer = new Timer();
            AddChild(randomPlatformTimer);
            randomPlatformTimer.Timeout += OnRandomPlatformTimerTimeout;
        }
        
        // Hide all random platforms initially
        HideAllRandomPlatforms();
        
        // Show the first random platform
        CycleToNextRandomPlatform();
    }

    /// <summary>
    /// Stops the random platform cycling system
    /// </summary>
    private void StopRandomPlatformCycling()
    {
        if (!isRandomPlatformCyclingActive) return;
        
        isRandomPlatformCyclingActive = false;
        GD.Print("PuzzleManager: Stopping random platform cycling system");
        
        // Reset pattern recognition system
        currentCycleCount = 0;
        isShowingOrderedPattern = false;
        orderedPatternIndex = 0;
        
        if (randomPlatformTimer != null)
        {
            randomPlatformTimer.Stop();
        }
        
        // Hide all random platforms
        HideAllRandomPlatforms();
        currentVisibleRandomPlatform = null;
    }

    /// <summary>
    /// Hides all random platforms instantly (for cycling system)
    /// </summary>
    private void HideAllRandomPlatforms()
    {
        GD.Print("PuzzleManager: Hiding all random platforms for clean state");
        
        foreach (Node2D platform in randomPlatforms)
        {
            if (platform != null && IsInstanceValid(platform))
            {
                // Stop any existing tween on this platform
                KillNodeTween(platform);
                
                platform.Visible = false;
                platform.Modulate = Colors.White; // Reset modulate for next show
                DisableCollisionsRecursive(platform);
            }
        }
        
        // Ensure the current platform reference is also cleared
        if (currentVisibleRandomPlatform != null)
        {
            GD.Print($"PuzzleManager: Clearing current visible platform reference: {currentVisibleRandomPlatform.Name}");
        }
    }

    /// <summary>
    /// Cycles to the next random platform, hiding the current one and showing a new one
    /// </summary>
    private void CycleToNextRandomPlatform()
    {
        if (!isRandomPlatformCyclingActive || randomPlatforms.Count == 0) return;
        
        Node2D newPlatform = null;
        
        // Check if we should switch to ordered pattern mode
        if (!isShowingOrderedPattern && currentCycleCount >= RandomCycleCount)
        {
            isShowingOrderedPattern = true;
            orderedPatternIndex = 0;
            GD.Print($"PuzzleManager: Switching to ordered pattern mode after {currentCycleCount} random cycles");
        }
        
        // Select platform based on current mode
        if (isShowingOrderedPattern)
        {
            newPlatform = SelectNextOrderedPlatform();
        }
        else
        {
            newPlatform = SelectRandomPlatform();
            
            // If SelectRandomPlatform returned null (to prevent same-platform selection), 
            // skip this cycle and try again on next timer timeout
            if (newPlatform == null)
            {
                GD.Print("PuzzleManager: Skipping cycle to avoid same-platform blinking");
                // Don't increment cycle count since we're not actually cycling
                
                // Set a shorter timer for the next attempt
                float retryDuration = 0.5f; // Quick retry
                randomPlatformTimer.WaitTime = retryDuration;
                randomPlatformTimer.Start();
                return; // Exit early without cycling
            }
            
            currentCycleCount++;
            GD.Print($"PuzzleManager: Random cycle count: {currentCycleCount}/{RandomCycleCount}");
        }
        
        if (newPlatform != null)
        {
            // Store reference to the platform that needs to fade out later
            Node2D platformToFadeOut = currentVisibleRandomPlatform;
            
            // Update current platform reference
            currentVisibleRandomPlatform = newPlatform;
            
            // Calculate the next cycle duration now
            float nextDuration = (float)(random.NextDouble() * (MaxRandomPlatformDuration - MinRandomPlatformDuration) + MinRandomPlatformDuration);
            
            // Start fading in the new platform
            FadeInSinglePlatform(newPlatform, () => {
                // After new platform is fully visible, wait for overlap duration, then fade out old platform
                if (platformToFadeOut != null && IsInstanceValid(platformToFadeOut))
                {
                    var overlapTimer = GetTree().CreateTimer(PlatformOverlapDuration);
                    overlapTimer.Timeout += () => {
                        if (IsInstanceValid(platformToFadeOut))
                        {
                            FadeOutSinglePlatform(platformToFadeOut);
                        }
                    };
                }
                
                // CRITICAL FIX: Start the next cycle timer ONLY after the fade-in is complete
                // This ensures proper timing and prevents race conditions
                randomPlatformTimer.WaitTime = nextDuration;
                randomPlatformTimer.Start();
                
                string modeText = isShowingOrderedPattern ? "ORDERED" : "RANDOM";
                GD.Print($"PuzzleManager: Platform '{newPlatform.Name}' fully visible [{modeText}], next cycle in {nextDuration:F2} seconds");
            });
            
            string modeText = isShowingOrderedPattern ? "ORDERED" : "RANDOM";
            GD.Print($"PuzzleManager: Starting fade-in for platform '{newPlatform.Name}' [{modeText}]");
        }
    }

    /// <summary>
    /// Selects a random platform from the list, ensuring it's different from the current one
    /// </summary>
    private Node2D SelectRandomPlatform()
    {
        if (randomPlatforms.Count == 0) return null;
        
        // If there's only one platform, return it (no choice)
        if (randomPlatforms.Count == 1)
        {
            return randomPlatforms[0];
        }
        
        // Create list of valid platforms (excluding current one)
        List<Node2D> availablePlatforms = new List<Node2D>();
        
        foreach (var platform in randomPlatforms)
        {
            if (platform != null && IsInstanceValid(platform) && platform != currentVisibleRandomPlatform)
            {
                availablePlatforms.Add(platform);
            }
        }
        
        // If no different platforms are available, this is a critical error
        if (availablePlatforms.Count == 0)
        {
            GD.PrintErr("PuzzleManager: CRITICAL ERROR - No different platforms available for random selection!");
            GD.PrintErr($"Total platforms: {randomPlatforms.Count}, Current platform: {currentVisibleRandomPlatform?.Name ?? "none"}");
            
            // As a last resort, try to find ANY different platform, even if it might not be ideal
            foreach (var platform in randomPlatforms)
            {
                if (platform != null && IsInstanceValid(platform) && platform != currentVisibleRandomPlatform)
                {
                    GD.PrintErr($"Found fallback platform: {platform.Name}");
                    return platform;
                }
            }
            
            // If we still can't find a different platform, return null to avoid blinking
            GD.PrintErr("PuzzleManager: Cannot find any different platform - returning null to prevent blinking");
            return null;
        }
        
        // Select random platform from available ones (guaranteed to be different from current)
        int randomIndex = random.Next(availablePlatforms.Count);
        var selectedPlatform = availablePlatforms[randomIndex];
        
        GD.Print($"PuzzleManager: Selected platform '{selectedPlatform.Name}' from {availablePlatforms.Count} available platforms (excluding current: {currentVisibleRandomPlatform?.Name ?? "none"})");
        
        return selectedPlatform;
    }

    /// <summary>
    /// Fades in a single platform
    /// </summary>
    private void FadeInSinglePlatform(Node2D platform, System.Action onComplete = null)
    {
        if (platform == null || !IsInstanceValid(platform)) return;

        // Stop any existing tween on this platform
        KillNodeTween(platform);

        // Ensure visible and start from transparent
        platform.Visible = true;
        var c = platform.Modulate;
        platform.Modulate = new Color(c.R, c.G, c.B, 0f);

        // Create a tween for this platform
        var tween = CreateTween();
        nodeTweens[platform] = tween;
        tween.TweenProperty(platform, "modulate:a", 1.0f, PlatformFadeInDuration);

        // Capture local variable for closure safety
        var target = platform;
        tween.Finished += () =>
        {
            if (!IsInstanceValid(target)) return;
            // Only clear if this is still the active tween for this platform
            if (nodeTweens.TryGetValue(target, out var active) && active == tween)
            {
                // Ensure fully visible at end
                target.Modulate = Colors.White;
                nodeTweens.Remove(target);
                
                // Enable collisions for the visible platform
                EnableCollisionsRecursive(target);
                
                // Start lifting animation for LiftedPlatforms when they become visible
                if (liftedPlatforms.Contains(target) && target is LiftedPlatform lp)
                {
                    lp.StartLifting();
                }
                
                // Call completion callback if provided
                onComplete?.Invoke();
            }
        };
    }

    /// <summary>
    /// Fades out a single platform
    /// </summary>
    private void FadeOutSinglePlatform(Node2D platform)
    {
        if (platform == null || !IsInstanceValid(platform)) return;

        // Stop any existing tween on this platform
        KillNodeTween(platform);

        var tween = CreateTween();
        nodeTweens[platform] = tween;
        tween.TweenProperty(platform, "modulate:a", 0.0f, PlatformFadeOutDuration);

        var target = platform;
        tween.Finished += () =>
        {
            if (!IsInstanceValid(target)) return;
            if (nodeTweens.TryGetValue(target, out var active) && active == tween)
            {
                target.Visible = false;
                target.Modulate = Colors.White; // reset for next time
                nodeTweens.Remove(target);
                
                // Disable collisions for the hidden platform
                DisableCollisionsRecursive(target);
                
                // Stop lifting animation for LiftedPlatforms when they become invisible
                if (liftedPlatforms.Contains(target) && target is LiftedPlatform lp)
                {
                    lp.StopLifting();
                }
            }
        };
    }

    /// <summary>
    /// Called when the random platform timer times out
    /// </summary>
    private void OnRandomPlatformTimerTimeout()
    {
        // Safety check: Don't cycle if a platform is currently fading in
        if (currentVisibleRandomPlatform != null && nodeTweens.ContainsKey(currentVisibleRandomPlatform))
        {
            GD.Print("PuzzleManager: Delaying cycle - platform is still fading in");
            // Retry in a short time
            randomPlatformTimer.WaitTime = 0.2f;
            randomPlatformTimer.Start();
            return;
        }
        
        CycleToNextRandomPlatform();
    }

    #endregion

    #region YellowCage Management System

    /// <summary>
    /// Detaches all YellowCages from their chains, allowing them to fall under gravity.
    /// This is called when all puzzle crystals become active.
    /// </summary>
    private void DetachAllYellowCages()
    {
        if (yellowCages == null || yellowCages.Count == 0)
        {
            GD.Print("PuzzleManager: No YellowCages found to detach");
            return;
        }

        int detachedCount = 0;
        
        foreach (var cage in yellowCages)
        {
            if (cage != null && IsInstanceValid(cage))
            {
                try
                {
                    // Force detachment regardless of cage's settings or unlock state
                    // This is specifically for when all puzzle crystals are active
                    cage.ForceChainDetachment();
                    detachedCount++;
                    GD.Print($"PuzzleManager: Force detached YellowCage '{cage.Name}' from its chain");
                }
                catch (System.Exception ex)
                {
                    GD.PrintErr($"PuzzleManager: Failed to detach YellowCage '{cage.Name}': {ex.Message}");
                }
            }
        }
        
        GD.Print($"PuzzleManager: Successfully detached {detachedCount} out of {yellowCages.Count} YellowCages");
    }

    #endregion

    public override void _ExitTree()
    {
        // Clean up random platform timer
        if (randomPlatformTimer != null)
        {
            randomPlatformTimer.Timeout -= OnRandomPlatformTimerTimeout;
            if (IsInstanceValid(randomPlatformTimer))
            {
                randomPlatformTimer.QueueFree();
            }
            randomPlatformTimer = null;
        }
        
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