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
        if (allElementsHidden) return;
        
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
            
            if (nodeName.Contains("rock") && child is Node2D rock)
            {
                rocks.Add(rock);
            }
            else if (nodeName.Contains("mainplatform") && child is Node2D mainPlatform)
            {
                mainPlatforms.Add(mainPlatform);
            }
            else if ((nodeName.Contains("randomplatform") || nodeName.Contains("randompat")) && child is Node2D randomPlatform)
            {
                randomPlatforms.Add(randomPlatform);
            }
            else if (child is PuzzleCrystal puzzleCrystal)
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
            SetObjectsVisibility(rocks, true);
            SetObjectsVisibility(mainPlatforms, true);
            SetObjectsVisibility(randomPlatforms, true);
            
            if (activeCrystals != previousActiveCrystals)
            {
                GD.Print("PuzzleManager: Showing platforms and rocks");
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
            SetObjectsVisibility(rocks, false);
            SetObjectsVisibility(mainPlatforms, false);
            SetObjectsVisibility(randomPlatforms, false);
            
            if (activeCrystals != previousActiveCrystals)
            {
                GD.Print("PuzzleManager: Hiding platforms and rocks (no active crystals)");
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
        GD.Print("PuzzleManager: Hiding all elements");
        
        allElementsHidden = true;
        
        // Hide all objects in the puzzle manager
        SetObjectsVisibility(rocks, false);
        SetObjectsVisibility(mainPlatforms, false);
        SetObjectsVisibility(randomPlatforms, false);
        
        // Also hide puzzle crystals
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