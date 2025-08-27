using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Simplified and optimized replay recording system
/// Combines recording and data extraction for better performance
/// </summary>
public partial class ReplayRecorder : Node
{
    #region Singleton
    public static ReplayRecorder Instance { get; private set; }
    #endregion

    #region Configuration
    [Export] public float RecordInterval { get; set; } = 0.1f;
    [Export] public bool EnableFrameOptimization { get; set; } = true;
    [Export] public bool EnableDebugLogging { get; set; } = false; // Conditional debug logging for performance
    #endregion

    #region State
    public bool IsRecording { get; private set; }
    public bool IsPausedForReplay { get; private set; }
    public bool HasRecordedData => _playerFrames.Count > 0 || _monsterFrames.Count > 0;
    #endregion

    #region Data Storage
    private readonly List<ReplayFrame> _playerFrames = new();
    private readonly List<ReplayFrame> _monsterFrames = new();
    private float _recordTimer;
    private float _elapsedTime;
    #endregion

    #region Character References (Simple Caching)
    private Player _cachedPlayer;
    private Monster _cachedMonster;
    private AnimatedSprite2D _playerSprite;
    private AnimatedSprite2D _monsterSprite;
    private Node2D _playerVisual;
    private Node2D _monsterVisual;
    #endregion

    #region Frame Optimization
    private Vector2 _lastPlayerPos;
    private Vector2 _lastMonsterPos;
    private int _skipCounter;
    private const float MIN_MOVEMENT = 0.3f;
    private const int MAX_SKIP_FRAMES = 8;
    #endregion

    #region Lifecycle
    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        AddToGroup("replay_recorder");
        
        CallDeferred(nameof(Initialize));
    }

    public override void _Process(double delta)
    {
        if (!IsRecording || IsPausedForReplay) return;

        _recordTimer += (float)delta;
        _elapsedTime += (float)delta;

        if (_recordTimer >= RecordInterval)
        {
            RecordFrame();
            _recordTimer = 0f;
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
    #endregion

    #region Initialization (Simplified)
    private void Initialize()
    {
        RefreshCharacterCache();
        
        if (_cachedPlayer != null)
        {
            StartRecording();
            Log.Debug("ReplayRecorder initialized and started", "ReplayRecorder");
        }
        else
        {
            GetTree().CreateTimer(0.1f).Timeout += () => {
                RefreshCharacterCache();
                if (_cachedPlayer != null)
                {
                    StartRecording();
                    Log.Debug("ReplayRecorder started after retry", "ReplayRecorder");
                }
                else
                {
                    Log.Warning("No player found for replay recording", "ReplayRecorder");
                }
            };
        }
    }

    public void RefreshCharacterCache()
    {
        _cachedPlayer = GetTree().GetFirstNodeInGroup("player") as Player;
        
        var enemies = GetTree().GetNodesInGroup("enemies").OfType<Monster>();
        _cachedMonster = enemies.FirstOrDefault(m => m.EnableFollowerBehavior) ?? enemies.FirstOrDefault();

        if (_cachedPlayer != null)
        {
            _playerSprite = FindSpriteInCharacter(_cachedPlayer);
            _playerVisual = _playerSprite ?? FindVisualInCharacter(_cachedPlayer);
            
            if (_playerSprite == null && _playerVisual == null)
            {
                Log.Warning($"No visual component (AnimatedSprite2D or Sprite2D) found for player {_cachedPlayer.Name}", "ReplayRecorder");
            }
        }

        if (_cachedMonster != null)
        {
            _monsterSprite = FindSpriteInCharacter(_cachedMonster);
            _monsterVisual = _monsterSprite ?? FindVisualInCharacter(_cachedMonster);
            
            if (_monsterSprite == null && _monsterVisual == null)
            {
                Log.Warning($"No visual component (AnimatedSprite2D or Sprite2D) found for monster {_cachedMonster.Name}", "ReplayRecorder");
            }
        }
    }

    private AnimatedSprite2D FindSpriteInCharacter(Node2D character)
    {
        var sprite = character.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D") ??
                    character.GetNodeOrNull<AnimatedSprite2D>("MarrieSprite") ??
                    character.GetNodeOrNull<AnimatedSprite2D>("GhostMarrieSprite") ??
                    character.FindChild("AnimatedSprite2D", recursive: false) as AnimatedSprite2D ??
                    character.FindChild("MarrieSprite", recursive: false) as AnimatedSprite2D;
        
        return sprite;
    }

    private Node2D FindVisualInCharacter(Node2D character)
    {
        var visual = character.GetNodeOrNull<Sprite2D>("Sprite2D") ??
                    character.GetNodeOrNull<Sprite2D>("MonsterSprite") ??
                    character.GetNodeOrNull<Sprite2D>("MarrieSprite") ??
                    character.GetNodeOrNull<Sprite2D>("GhostMarrieSprite") ??
                    character.FindChild("Sprite2D", recursive: false) as Node2D ??
                    character.FindChild("MonsterSprite", recursive: false) as Node2D;
        
        return visual;
    }
    #endregion

    #region Recording Control
    public void StartRecording()
    {
        ClearRecording();
        IsRecording = true;
        _recordTimer = 0f;
        _elapsedTime = 0f;
        
        if (_cachedPlayer != null) _lastPlayerPos = _cachedPlayer.GlobalPosition;
        if (_cachedMonster != null) _lastMonsterPos = _cachedMonster.GlobalPosition;
        _skipCounter = 0;
    }

    public void StopRecording()
    {
        IsRecording = false;
        Log.Debug($"Recording stopped - Player: {_playerFrames.Count}, Monster: {_monsterFrames.Count} frames", "ReplayRecorder");
    }

    public void ClearRecording()
    {
        _playerFrames.Clear();
        _monsterFrames.Clear();
        _recordTimer = 0f;
        _elapsedTime = 0f;
        _skipCounter = 0;
    }

    public void PauseRecording()
    {
        IsPausedForReplay = true;
    }

    public void ResumeRecording()
    {
        IsPausedForReplay = false;
    }
    #endregion

    #region Frame Recording (Optimized)
    private void RecordFrame()
    {
        if (EnableFrameOptimization && ShouldSkipFrame())
        {
            _skipCounter++;
            return;
        }

        _skipCounter = 0;

        if (_cachedPlayer != null)
        {
            var frame = CreatePlayerFrame();
            _playerFrames.Add(frame);
            _lastPlayerPos = _cachedPlayer.GlobalPosition;
        }

        if (_cachedMonster != null)
        {
            var frame = CreateMonsterFrame();
            _monsterFrames.Add(frame);
            _lastMonsterPos = _cachedMonster.GlobalPosition;
        }
    }

    private bool ShouldSkipFrame()
    {
        if (_skipCounter >= MAX_SKIP_FRAMES) return false;

        bool playerMoved = _cachedPlayer == null || 
                          _cachedPlayer.GlobalPosition.DistanceTo(_lastPlayerPos) >= MIN_MOVEMENT;
        
        bool monsterMoved = _cachedMonster == null || 
                           _cachedMonster.GlobalPosition.DistanceTo(_lastMonsterPos) >= MIN_MOVEMENT;

        return !playerMoved && !monsterMoved;
    }

    private ReplayFrame CreatePlayerFrame()
    {
        var pos = _cachedPlayer.GlobalPosition;
        var rot = _cachedPlayer.GlobalRotation;
        var anim = GetAnimationName(_playerSprite);
        var flip = GetSpriteFlip(_playerSprite, _playerVisual);
        var (visible, alpha) = GetVisibility(_playerVisual);

        return new ReplayFrame(_elapsedTime, pos, rot, anim, flip, visible, alpha);
    }

    private ReplayFrame CreateMonsterFrame()
    {
        var pos = _cachedMonster.GlobalPosition;
        var rot = _cachedMonster.GlobalRotation;
        var anim = GetAnimationName(_monsterSprite);
        var flip = GetMonsterSpriteFlip(_monsterSprite, _monsterVisual);
        var (visible, alpha) = GetVisibility(_monsterVisual);
        
        var state = _cachedMonster.EnableFollowerBehavior ? _cachedMonster.GetState().ToString() : "";
        var fullyVisible = _cachedMonster.EnableFollowerBehavior ? _cachedMonster.IsFullyVisible() : true;

        return new ReplayFrame(_elapsedTime, pos, rot, anim, flip, visible, alpha, state, fullyVisible);
    }
    #endregion

    #region Data Extraction (Simplified)
    private string GetAnimationName(AnimatedSprite2D sprite)
    {
        return sprite?.Animation?.ToString() ?? "idle";
    }

    private bool GetSpriteFlip(AnimatedSprite2D animSprite, Node2D visual)
    {
        if (animSprite?.IsInsideTree() == true)
        {
            try
            {
                // Check both FlipH property and Scale.X for flipping
                bool flipH = animSprite.FlipH;
                bool scaleFlip = animSprite.Scale.X < 0;
                bool isFlipped = flipH || scaleFlip;
                
                if (EnableDebugLogging)
                {
                    Log.Debug($"Player flip detected: FlipH={flipH}, ScaleFlip={scaleFlip}, Final={isFlipped}, Scale.X={animSprite.Scale.X:F3}", "ReplayRecorder");
                }
                
                return isFlipped;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Error accessing animated sprite properties: {ex.Message}", "ReplayRecorder");
            }
        }
        
        if (visual?.IsInsideTree() == true) 
        {
            try
            {
                bool scaleFlip = visual.Scale.X < 0;
                return scaleFlip;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Error accessing visual properties: {ex.Message}", "ReplayRecorder");
            }
        }
        
        Log.Warning("No valid sprite or visual found for flip detection", "ReplayRecorder");
        return false;
    }

    private bool GetMonsterSpriteFlip(AnimatedSprite2D animSprite, Node2D visual)
    {
        if (animSprite?.IsInsideTree() == true)
        {
            try
            {
                // Check both FlipH property and Scale.X for flipping
                bool flipH = animSprite.FlipH;
                bool scaleFlip = animSprite.Scale.X < 0;
                bool isFlipped = flipH || scaleFlip;
                
                if (EnableDebugLogging)
                {
                    Log.Debug($"Monster flip detected: FlipH={flipH}, ScaleFlip={scaleFlip}, Final={isFlipped}, Scale.X={animSprite.Scale.X:F3}", "ReplayRecorder");
                }
                
                return isFlipped;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Error accessing monster animated sprite properties: {ex.Message}", "ReplayRecorder");
            }
        }
        
        if (visual?.IsInsideTree() == true) 
        {
            try
            {
                bool scaleFlip = visual.Scale.X < 0;
                
                if (EnableDebugLogging)
                {
                    Log.Debug($"Monster visual flip detected: ScaleFlip={scaleFlip}, Scale.X={visual.Scale.X:F3}", "ReplayRecorder");
                }
                
                return scaleFlip;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Error accessing monster visual properties: {ex.Message}", "ReplayRecorder");
            }
        }
        
        Log.Warning("No valid monster sprite or visual found for flip detection", "ReplayRecorder");
        return false;
    }

    private (bool visible, float alpha) GetVisibility(Node2D visual)
    {
        if (visual == null) return (true, 1f);
        
        var modulate = visual.Modulate;
        return (visual.Visible && modulate.A > 0f, modulate.A);
    }
    #endregion

    #region Data Access
    public List<ReplayFrame> GetPlayerFrames() => new(_playerFrames);
    public List<ReplayFrame> GetMonsterFrames() => new(_monsterFrames);
    
    public (int playerFrames, int monsterFrames, float duration) GetStats()
    {
        return (_playerFrames.Count, _monsterFrames.Count, _elapsedTime);
    }
    #endregion
}
