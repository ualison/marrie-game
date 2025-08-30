using Godot;
using MarrieGame.Core.Utils;
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
    private bool _lastMonsterRecordingState = false;
    public bool IsRecording { get; private set; }
    public bool IsPausedForReplay { get; private set; }
    // Include the small prebuffer in the recorded-data check to avoid losing fast deaths
    private const int PREBUFFER_CAPACITY = 64;
    private readonly Queue<ReplayFrame> _playerPreBuffer = new();
    public bool HasRecordedData => _playerFrames.Count > 0 || _monsterFrames.Count > 0 || _playerPreBuffer.Count > 0;
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

    // NEW METHOD: Called by EnemyManager when monster visibility changes
    public void OnMonsterVisibilityChanged(Monster monster, bool isVisible)
    {
        if (_cachedMonster != monster) return;

        bool currentRecordingState = ShouldRecordMonster();

        if (_lastMonsterRecordingState != currentRecordingState)
        {
            if (currentRecordingState)
            {
                Log.Debug($"Monster {monster.Name} became recordable - resuming monster frame recording", "ReplayRecorder");
            }
            else
            {
                Log.Debug($"Monster {monster.Name} became hidden - pausing monster frame recording", "ReplayRecorder");
            }

            _lastMonsterRecordingState = currentRecordingState;
        }
    }
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
            GetTree().CreateTimer(0.1f).Timeout += () =>
            {
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
        var node = NodeHelpers.GetNodeOrNull<AnimatedSprite2D>(character, null, "AnimatedSprite2D")
               ?? NodeHelpers.GetNodeOrNull<AnimatedSprite2D>(character, null, "MarrieSprite")
               ?? NodeHelpers.GetNodeOrNull<AnimatedSprite2D>(character, null, "GhostMarrieSprite");

        if (node != null) return node;

        // Fallback: search direct children for a sprite-like node
        var found = NodeHelpers.FindChildOfType<AnimatedSprite2D>(character);
        return found;
    }

    private Node2D FindVisualInCharacter(Node2D character)
    {
        var node = NodeHelpers.GetNodeOrNull<Sprite2D>(character, null, "Sprite2D")
               ?? NodeHelpers.GetNodeOrNull<Sprite2D>(character, null, "MonsterSprite")
               ?? NodeHelpers.GetNodeOrNull<Sprite2D>(character, null, "MarrieSprite")
               ?? NodeHelpers.GetNodeOrNull<Sprite2D>(character, null, "GhostMarrieSprite");

        if (node != null) return node as Node2D;

        // Fallback: find first Sprite2D child
        var found = NodeHelpers.FindChildOfType<Sprite2D>(character);
        return found as Node2D;
    }
    #endregion

    #region Recording Control
    // UPDATED METHOD: Initialize monster recording state
    public void StartRecording()
    {
        ClearRecording();
        IsRecording = true;
        _recordTimer = 0f;
        _elapsedTime = 0f;

        if (_cachedPlayer != null) _lastPlayerPos = _cachedPlayer.GlobalPosition;
        if (_cachedMonster != null)
        {
            _lastMonsterPos = _cachedMonster.GlobalPosition;
            _lastMonsterRecordingState = ShouldRecordMonster();
        }
        _skipCounter = 0;

        Log.Debug($"Recording started - Monster recording state: {_lastMonsterRecordingState}", "ReplayRecorder");
    }

    // NEW METHOD: Force refresh monster cache and recording state
    public void RefreshMonsterRecordingState()
    {
        if (_cachedMonster != null)
        {
            bool newState = ShouldRecordMonster();
            if (_lastMonsterRecordingState != newState)
            {
                Log.Debug($"Monster recording state changed: {_lastMonsterRecordingState} -> {newState}", "ReplayRecorder");
                _lastMonsterRecordingState = newState;
            }
        }
    }
    public void StopRecording()
    {
        IsRecording = false;
        // If we have no primary frames but the prebuffer holds data (very short session), promote it
        if (_playerFrames.Count == 0 && _playerPreBuffer.Count > 0)
        {
            while (_playerPreBuffer.Count > 0)
            {
                _playerFrames.Add(_playerPreBuffer.Dequeue());
            }
        }

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
            // Keep a short rolling prebuffer to preserve the last frames in case recording stops quickly
            _playerPreBuffer.Enqueue(frame);
            while (_playerPreBuffer.Count > PREBUFFER_CAPACITY)
                _playerPreBuffer.Dequeue();

            _playerFrames.Add(frame);
            _lastPlayerPos = _cachedPlayer.GlobalPosition;
        }

        // BUG FIX: Only record monster frames when monster is not hidden
        if (_cachedMonster != null && ShouldRecordMonster())
        {
            var frame = CreateMonsterFrame();
            _monsterFrames.Add(frame);
            _lastMonsterPos = _cachedMonster.GlobalPosition;
        }
    }

    // NEW METHOD: Check if monster should be recorded based on visibility state
    private bool ShouldRecordMonster()
    {
        if (_cachedMonster == null) return false;

        // Check if monster is hidden by EnemyManager (global or local hiding)
        if (EnemyManager.Instance != null)
        {
            bool isHiddenByPair = EnemyManager.Instance.IsMonsterHiddenByPair(_cachedMonster);
            if (isHiddenByPair)
            {
                if (EnableDebugLogging)
                {
                    Log.Debug($"Skipping monster frame recording - monster {_cachedMonster.Name} is hidden by EnemyManager", "ReplayRecorder");
                }
                return false;
            }
        }

        // Check if monster is currently visible (for follower behavior monsters)
        if (_cachedMonster.EnableFollowerBehavior)
        {
            bool isCurrentlyVisible = _cachedMonster.IsCurrentlyVisible;
            if (!isCurrentlyVisible)
            {
                if (EnableDebugLogging)
                {
                    Log.Debug($"Skipping monster frame recording - follower monster {_cachedMonster.Name} is not currently visible", "ReplayRecorder");
                }
                return false;
            }
        }

        // Check if monster is disabled for replay
        if (_cachedMonster.IsDisabledForReplay)
        {
            if (EnableDebugLogging)
            {
                Log.Debug($"Skipping monster frame recording - monster {_cachedMonster.Name} is disabled for replay", "ReplayRecorder");
            }
            return false;
        }

        // Check if monster has valid health (don't record dead monsters)
        if (_cachedMonster.LifeSystem?.IsDead == true)
        {
            if (EnableDebugLogging)
            {
                Log.Debug($"Skipping monster frame recording - monster {_cachedMonster.Name} is dead", "ReplayRecorder");
            }
            return false;
        }

        return true;
    }

    // UPDATED METHOD: Include monster recording status in skip logic
    private bool ShouldSkipFrame()
    {
        if (_skipCounter >= MAX_SKIP_FRAMES) return false;

        bool playerMoved = _cachedPlayer == null ||
                          _cachedPlayer.GlobalPosition.DistanceTo(_lastPlayerPos) >= MIN_MOVEMENT;

        bool monsterMoved = false;
        bool shouldRecordMonster = ShouldRecordMonster();

        if (shouldRecordMonster && _cachedMonster != null)
        {
            monsterMoved = _cachedMonster.GlobalPosition.DistanceTo(_lastMonsterPos) >= MIN_MOVEMENT;
        }

        return !playerMoved && !monsterMoved;
    }

    private ReplayFrame CreatePlayerFrame()
    {
        var pos = _cachedPlayer.GlobalPosition;
        var rot = _cachedPlayer.GlobalRotation;
        var animName = GetAnimationName(_playerSprite);
        var animId = ReplayStringRegistry.GetOrRegisterAnimation(animName);
        var flip = GetSpriteFlip(_playerSprite, _playerVisual);
        var (visible, alpha) = GetVisibility(_playerVisual);

        return new ReplayFrame(_elapsedTime, pos, rot, animId, flip, visible, alpha);
    }

    private ReplayFrame CreateMonsterFrame()
    {
        var pos = _cachedMonster.GlobalPosition;
        var rot = _cachedMonster.GlobalRotation;
        var animName = GetAnimationName(_monsterSprite);
        var animId = ReplayStringRegistry.GetOrRegisterAnimation(animName);
        var flip = GetMonsterSpriteFlip(_monsterSprite, _monsterVisual);
        var (visible, alpha) = GetVisibility(_monsterVisual);

        var stateName = _cachedMonster.EnableFollowerBehavior ? _cachedMonster.GetState().ToString() : "";
        var stateId = ReplayStringRegistry.GetOrRegisterMonsterState(stateName);
        var fullyVisible = _cachedMonster.EnableFollowerBehavior ? _cachedMonster.IsFullyVisible() : true;

        return new ReplayFrame(_elapsedTime, pos, rot, animId, flip, visible, alpha, stateId, fullyVisible);
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
