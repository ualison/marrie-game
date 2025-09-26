using Godot;
using System.Collections.Generic;
using MarrieGame.Core.Logging;

public partial class HazeMonsterManager : Node
{
    #region Singleton
    public static HazeMonsterManager Instance { get; private set; }
    #endregion

    #region Export Properties
    [ExportGroup("Haze Spawning")]
    [Export] public float MinSpawnDistance { get; set; } = 800.0f;
    [Export] public float MaxSpawnDistance { get; set; } = 1200.0f;
    [Export] public PackedScene HazePrefab { get; set; }
    [Export] public float DespawnDistance { get; set; } = 2000.0f; // When player is farther than this from the current haze, recycle it
    [Export] public float RespawnDelay { get; set; } = 2.0f;       // Delay after despawn before spawning a replacement
    
    [ExportGroup("Puzzle Management")]
    [Export] public string[] PuzzleScenes { get; set; } = {
        "res://Scenes/Puzzles-Act1/PuzzleDimension1.tscn",
        "res://Scenes/Puzzles-Act1/PuzzleDimension2.tscn",
        "res://Scenes/Puzzles-Act1/PuzzleDimension3.tscn",
        "res://Scenes/Puzzles-Act1/PuzzleDimension4.tscn"
    };
    #endregion

    #region Private Variables
    private static Dictionary<string, bool> _completedPuzzles = new Dictionary<string, bool>();
    private int _currentPuzzleIndex = 0;
    private RandomNumberGenerator _rng = new RandomNumberGenerator();
    private Camera2D _playerCamera;
    private InfiniteForestManager _forestManager;
    private float _spawnTimer = 0.0f;
    private float _spawnInterval = 10.0f; // Spawn attempt every 10 seconds
    private float _respawnCooldown = 0.0f;
    private Node2D _currentHaze; // Track the single active haze
    #endregion

    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
            _rng.Randomize(); // Ensure non-deterministic spawn distances each run
            InitializePuzzleState();
            FindReferences();
            SubscribeToEvents();
            
            // Debug info
            string prefabStatus = HazePrefab != null ? $"Loaded: {HazePrefab.ResourcePath}" : "NULL";
            Log.System($"HazeMonsterManager initialized - Prefab: {prefabStatus}", "HazeMonsterManager");
        }
        else
        {
            QueueFree();
        }
    }

    private void SubscribeToEvents()
    {
        // Subscribe to game restart to reset puzzle progress
        GameEventSystem.GameRestart += OnGameRestart;
        
        // Subscribe to scene changes to detect puzzle completion
        if (GetTree() != null)
        {
            GetTree().Connect("node_added", new Callable(this, nameof(OnNodeAdded)));
        }
    }

    private void OnGameRestart()
    {
        ResetPuzzleProgress();
    }

    private void OnNodeAdded(Node node)
    {
        // When returning to forest from puzzle, check if we should mark puzzle as completed
        if (GameManager.Instance?.IsForestScene == true)
        {
            CheckPuzzleCompletionOnReturn();
        }
    }

    private void CheckPuzzleCompletionOnReturn()
    {
        // Simple logic: if we're back in forest and current puzzle index hasn't advanced,
        // assume the current puzzle was completed
        string currentScene = GameManager.Instance?.CurrentSceneName ?? "";
        if (currentScene.Contains("Forest"))
        {
            // For now, we'll rely on external systems to call MarkPuzzleCompleted
            // This could be enhanced by tracking the last puzzle scene visited
        }
    }

    public override void _Process(double delta)
    {
        if (_playerCamera == null) return;
        
        // Update timers
        _spawnTimer += (float)delta;
        if (_respawnCooldown > 0)
            _respawnCooldown -= (float)delta;

        MaintainSingleHazeLifecycle();
    }

    private void InitializePuzzleState()
    {
        for (int i = 0; i < PuzzleScenes.Length; i++)
        {
            string puzzleKey = $"puzzle_{i + 1}";
            if (!_completedPuzzles.ContainsKey(puzzleKey))
            {
                _completedPuzzles[puzzleKey] = false;
            }
        }
        UpdateCurrentPuzzleIndex();
    }

    private void FindReferences()
    {
        _playerCamera = GetViewport().GetCamera2D();
        _forestManager = GetTree().GetFirstNodeInGroup("forest_manager") as InfiniteForestManager;
        
        if (_forestManager == null)
        {
            var forestNodes = GetTree().GetNodesInGroup("forest");
            foreach (Node node in forestNodes)
            {
                if (node is InfiniteForestManager manager)
                {
                    _forestManager = manager;
                    break;
                }
            }
        }

        // Ensure HazePrefab is loaded if not set in editor
        if (HazePrefab == null)
        {
            HazePrefab = GD.Load<PackedScene>("res://Prefabs/Haze.tscn");
            if (HazePrefab != null)
            {
                Log.System("HazePrefab loaded from resource path", "HazeMonsterManager");
            }
            else
            {
                Log.Error("Failed to load HazePrefab from res://Prefabs/Haze.tscn", "HazeMonsterManager");
            }
        }
    }

    public Vector2 GetRandomSpawnPosition()
    {
        if (_playerCamera == null) return Vector2.Zero;

        float playerX = _playerCamera.GlobalPosition.X;
        float distance = _rng.RandfRange(MinSpawnDistance, MaxSpawnDistance);
        bool spawnRight = _rng.Randf() > 0.5f;
        
        float spawnX = spawnRight ? playerX + distance : playerX - distance;
        // Based on ForestTest.tscn, the existing Haze is at Y=219, Player at Y=130
        float spawnY = 219.0f; // Ground level where Haze should appear
        
        return new Vector2(spawnX, spawnY);
    }

    public Node2D SpawnHazeMonster(Vector2 position)
    {
        if (HazePrefab == null)
        {
            // Try to load it again if it's missing
            HazePrefab = GD.Load<PackedScene>("res://Prefabs/Haze.tscn");
            if (HazePrefab == null)
            {
                Log.Error("HazePrefab not assigned and failed to load from resource path", "HazeMonsterManager");
                return null;
            }
        }

        var haze = HazePrefab.Instantiate<Node2D>();
        if (haze != null)
        {
            GetTree().CurrentScene.AddChild(haze);
            haze.GlobalPosition = position;
            haze.AddToGroup("haze");
            _currentHaze = haze;
            
            var sceneSwitcher = haze.GetNodeOrNull<Node2D>("SceneSwitcherCrystal");
            if (sceneSwitcher != null)
            {
                SetupSceneSwitcher(sceneSwitcher);
            }

            // Fade-in effect for visibility
            TryFadeInHaze(haze);
            
            Log.System($"Successfully spawned Haze Monster at {position}", "HazeMonsterManager");
        }
        else
        {
            Log.Error($"Failed to instantiate Haze Monster from prefab at {position}", "HazeMonsterManager");
        }
        
        return haze;
    }

    private void TryFadeInHaze(Node2D haze)
    {
        var hazeSprite = haze.GetNodeOrNull<Sprite2D>("HazeSprite");
        if (hazeSprite != null)
        {
            hazeSprite.Modulate = new Color(1,1,1,0);
            var tween = haze.CreateTween();
            tween.TweenProperty(hazeSprite, "modulate:a", 1.0f, 0.8f);
        }
    }

    private void SetupSceneSwitcher(Node2D sceneSwitcher)
    {
        var switcher = sceneSwitcher.GetScript().AsGodotObject();
        if (switcher != null && switcher.HasMethod("set"))
        {
            string nextPuzzleScene = GetNextPuzzleScene();
            switcher.Call("set", "TargetScenePath", nextPuzzleScene);
            Log.Debug($"Set SceneSwitcher target to: {nextPuzzleScene}", "HazeMonsterManager");
        }
    }

    public void MarkPuzzleCompleted(int puzzleNumber)
    {
        if (puzzleNumber < 1 || puzzleNumber > PuzzleScenes.Length) return;
        
        string puzzleKey = $"puzzle_{puzzleNumber}";
        _completedPuzzles[puzzleKey] = true;
        UpdateCurrentPuzzleIndex();
        
        Log.Gameplay($"Puzzle {puzzleNumber} marked as completed", "HazeMonsterManager");
    }

    public void MarkPuzzleCompleted(string scenePath)
    {
        for (int i = 0; i < PuzzleScenes.Length; i++)
        {
            if (PuzzleScenes[i] == scenePath)
            {
                MarkPuzzleCompleted(i + 1);
                break;
            }
        }
    }

    private void UpdateCurrentPuzzleIndex()
    {
        _currentPuzzleIndex = 0;
        for (int i = 0; i < PuzzleScenes.Length; i++)
        {
            string puzzleKey = $"puzzle_{i + 1}";
            if (!_completedPuzzles[puzzleKey])
            {
                _currentPuzzleIndex = i;
                break;
            }
        }
        
        if (_currentPuzzleIndex >= PuzzleScenes.Length)
        {
            _currentPuzzleIndex = PuzzleScenes.Length - 1;
        }
    }

    public string GetNextPuzzleScene()
    {
        if (_currentPuzzleIndex >= 0 && _currentPuzzleIndex < PuzzleScenes.Length)
        {
            return PuzzleScenes[_currentPuzzleIndex];
        }
        return PuzzleScenes[PuzzleScenes.Length - 1];
    }

    public bool IsPuzzleCompleted(int puzzleNumber)
    {
        if (puzzleNumber < 1 || puzzleNumber > PuzzleScenes.Length) return false;
        string puzzleKey = $"puzzle_{puzzleNumber}";
        return _completedPuzzles.ContainsKey(puzzleKey) && _completedPuzzles[puzzleKey];
    }

    public bool AreAllPuzzlesCompleted()
    {
        for (int i = 1; i <= PuzzleScenes.Length; i++)
        {
            if (!IsPuzzleCompleted(i)) return false;
        }
        return true;
    }

    public int GetCompletedPuzzleCount()
    {
        int count = 0;
        for (int i = 1; i <= PuzzleScenes.Length; i++)
        {
            if (IsPuzzleCompleted(i)) count++;
        }
        return count;
    }

    public void ResetPuzzleProgress()
    {
        _completedPuzzles.Clear();
        InitializePuzzleState();
        Log.System("Puzzle progress reset", "HazeMonsterManager");
    }

    private void MaintainSingleHazeLifecycle()
    {
        string currentScene = GameManager.Instance?.CurrentSceneName ?? "";
        bool isForestScene = currentScene.Contains("Forest") || currentScene.Contains("forest");
        if (!isForestScene || AreAllPuzzlesCompleted()) return;

        // Build active haze list
        var hazes = GetTree().GetNodesInGroup("haze");
        List<Node2D> active = new List<Node2D>();
        foreach (var n in hazes)
        {
            if (n is Node2D h && IsInstanceValid(h) && h.Visible)
            {
                active.Add(h);
            }
        }

        // If more than one (legacy or accidental), keep the nearest and remove the rest
        if (active.Count > 1)
        {
            active.Sort((a,b) => Mathf.Abs(a.GlobalPosition.X - _playerCamera.GlobalPosition.X)
                .CompareTo(Mathf.Abs(b.GlobalPosition.X - _playerCamera.GlobalPosition.X)));
            for (int i = 1; i < active.Count; i++)
            {
                active[i].QueueFree();
            }
            active.RemoveRange(1, active.Count - 1);
        }

        _currentHaze = active.Count == 1 ? active[0] : null;

        // Despawn if too far (horizontal) from player
        if (_currentHaze != null)
        {
            float horizDist = Mathf.Abs(_currentHaze.GlobalPosition.X - _playerCamera.GlobalPosition.X);
            if (horizDist > DespawnDistance)
            {
                Log.Debug($"Despawning haze at {_currentHaze.GlobalPosition} (horizDist={horizDist} > {DespawnDistance})", "HazeMonsterManager");
                _currentHaze.QueueFree();
                _currentHaze = null;
                _respawnCooldown = RespawnDelay;
                return; // Wait until next frame / after cooldown
            }
        }

        // If no haze and cooldown expired, spawn immediately (ignore 50% chance) to guarantee presence
        if (_currentHaze == null && _respawnCooldown <= 0)
        {
            Vector2 spawnPos = SelectValidSpawnPosition();
            if (spawnPos != Vector2.Zero)
            {
                SpawnHazeMonster(spawnPos);
                Log.Debug($"Spawned replacement haze at {spawnPos} (scene: {currentScene})", "HazeMonsterManager");
            }
        }
    }

    private Vector2 SelectValidSpawnPosition()
    {
        if (_playerCamera == null) return Vector2.Zero;
        int attempts = 0;
        const int MAX_ATTEMPTS = 8;
        while (attempts < MAX_ATTEMPTS)
        {
            attempts++;
            Vector2 candidate = GetRandomSpawnPosition();
            float horiz = Mathf.Abs(candidate.X - _playerCamera.GlobalPosition.X);
            if (horiz >= MinSpawnDistance && horiz <= MaxSpawnDistance)
            {
                return candidate;
            }
        }
        Log.Debug($"Failed to find spawn position after {attempts} attempts", "HazeMonsterManager");
        return Vector2.Zero;
    }

    public void ForceSpawnHazeForTesting()
    {
        Vector2 spawnPos = GetRandomSpawnPosition();
        SpawnHazeMonster(spawnPos);
        Log.System($"Force spawned Haze at {spawnPos} for testing", "HazeMonsterManager");
    }

    public string GetPuzzleProgressInfo()
    {
        int completed = GetCompletedPuzzleCount();
        int total = PuzzleScenes.Length;
        string nextScene = GetNextPuzzleScene();
        return $"Puzzles: {completed}/{total}, Next: {nextScene}";
    }

    public static void ClearPersistentData()
    {
        _completedPuzzles.Clear();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            // Cleanup event subscriptions
            GameEventSystem.GameRestart -= OnGameRestart;
            
            if (GetTree() != null)
            {
                GetTree().Disconnect("node_added", new Callable(this, nameof(OnNodeAdded)));
            }
            
            Instance = null;
            Log.System("HazeMonsterManager cleaned up", "HazeMonsterManager");
        }
    }
}