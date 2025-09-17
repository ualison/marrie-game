using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class PuzzleManager : Node2D
{
    #region Export Variables
    [Export] public float FadeOutDuration { get; set; } = 3.0f; // Time to wait before hiding all elements
    [Export] public float MainPlatformFadeDuration { get; set; } = 1.0f; // How long the main platform fade takes
    #endregion

    #region Private Variables
    private List<Node2D> rocks = new List<Node2D>();
    private List<Node2D> mainPlatforms = new List<Node2D>();
    private List<Node2D> randomPlatforms = new List<Node2D>();
    private List<PuzzleCrystal> puzzleCrystals = new List<PuzzleCrystal>();
    
    private bool isMainPlatformFading = false;
    private bool allElementsHidden = false;
    private Tween fadeOutTween;
    
    // Track previous state to avoid spam logging
    private int previousActiveCrystals = -1;
    
    // Track visibility states to prevent repeated calls
    private bool rocksVisible = false;
    private bool mainPlatformsVisible = false;
    private bool randomPlatformsVisible = false;
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
                SetObjectsVisibility(rocks, true);
                rocksVisible = true;
                GD.Print("PuzzleManager: Showing rocks");
            }
            
            if (!mainPlatformsVisible)
            {
                SetObjectsVisibility(mainPlatforms, true);
                mainPlatformsVisible = true;
                GD.Print("PuzzleManager: Showing main platforms");
            }
            
            if (!randomPlatformsVisible)
            {
                SetObjectsVisibility(randomPlatforms, true);
                randomPlatformsVisible = true;
                GD.Print("PuzzleManager: Showing random platforms");
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
                SetObjectsVisibility(rocks, false);
                rocksVisible = false;
                GD.Print("PuzzleManager: Hiding rocks (no active crystals)");
            }
            
            if (!allElementsHidden && mainPlatformsVisible)
            {
                SetObjectsVisibility(mainPlatforms, false);
                mainPlatformsVisible = false;
                GD.Print("PuzzleManager: Hiding main platforms (no active crystals)");
            }
            
            if (!allElementsHidden && randomPlatformsVisible)
            {
                SetObjectsVisibility(randomPlatforms, false);
                randomPlatformsVisible = false;
                GD.Print("PuzzleManager: Hiding random platforms (no active crystals)");
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
                    SetObjectsVisibility(rocks, true);
                    rocksVisible = true;
                    GD.Print("PuzzleManager: Ensuring rocks are visible for final sequence");
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
            }
        }
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
        
        // Hide main platforms and random platforms, but keep rocks visible so they can fall
        SetObjectsVisibility(mainPlatforms, false);
        SetObjectsVisibility(randomPlatforms, false);
        
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
            }
        }
        
        // Optionally hide the entire puzzle manager
        // this.Visible = false;
    }

    public override void _ExitTree()
    {
        // Clean up tweens
        if (fadeOutTween != null)
        {
            fadeOutTween.Kill();
            fadeOutTween = null;
        }
    }
}