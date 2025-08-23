using Godot;
using System.Collections.Generic;

// Movement recording system for ghost replay functionality with pause/resume support
public partial class ReplayRecorder : Node
{
    #region Singleton
    public static ReplayRecorder Instance { get; private set; }
    #endregion

    #region Configuration
    [Export] public float RecordInterval { get; set; } = 0.1f;
    #endregion

    #region State Management
    public bool IsRecording { get; private set; }
    private bool _isPausedForReplay = false;
    private float _recordTimer;
    private float _startTime;
    #endregion

    #region Frame Storage
    private readonly List<ReplayFrame> _playerFrames = new();
    private readonly List<ReplayFrame> _monsterFrames = new();
    #endregion

    #region Character References
    private Player _player;
    private FollowerMonster _followerMonster;
    private Monster _monster;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        
        CallDeferred(nameof(InitializeRecording));
    }

    public void InitializeRecording()
    {
        DiscoverCharacters();

        if (_player != null)
        {
            StartRecording();
        }
        else
        {
            // Retry if player not found
            GetTree().CreateTimer(0.1f).Timeout += InitializeRecording;
        }
    }

    private void DiscoverCharacters()
    {
        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        
        // Look for both monster types
        _followerMonster = GetTree().GetFirstNodeInGroup("enemies") as FollowerMonster;
        if (_followerMonster == null)
        {
            _monster = GetTree().GetFirstNodeInGroup("enemies") as Monster;
        }

        GD.Print($"ReplayRecorder discovered - Player: {_player != null}, FollowerMonster: {_followerMonster != null}, Monster: {_monster != null}");
    }
    #endregion

    #region Core Recording Loop
    public override void _Process(double delta)
    {
        if (!IsRecording || _isPausedForReplay) return;

        _recordTimer += (float)delta;

        if (_recordTimer >= RecordInterval)
        {
            CaptureCurrentFrame();
            _recordTimer = 0.0f;
        }
    }

    private void CaptureCurrentFrame()
    {
        float currentTime = Time.GetTicksMsec() / 1000.0f - _startTime;

        // Record player
        if (_player != null)
        {
            var frame = CreateFrameForCharacter(_player, currentTime);
            _playerFrames.Add(frame);
        }

        // Record monster (prioritize FollowerMonster)
        Node2D activeMonster = _followerMonster != null ? _followerMonster : _monster;
        if (activeMonster != null)
        {
            var frame = CreateFrameForCharacter(activeMonster, currentTime);
            _monsterFrames.Add(frame);
        }
    }
    #endregion

    #region Recording Control
    public void StartRecording()
    {
        ClearRecording();
        IsRecording = true;
        _recordTimer = 0f;
        _startTime = Time.GetTicksMsec() / 1000.0f;
        
        GD.Print($"ReplayRecorder: Started recording - Player: {_player != null}");
    }

    public void StopRecording()
    {
        IsRecording = false;
        GD.Print($"Recording stopped. Player: {_playerFrames.Count}, Monster: {_monsterFrames.Count} frames");
    }

    public void ClearRecording()
    {
        _playerFrames.Clear();
        _monsterFrames.Clear();
        _recordTimer = 0f;
    }

    public void PauseRecording()
    {
        _isPausedForReplay = true;
        GD.Print("Recording paused for replay");
    }

    public void ResumeRecording()
    {
        _isPausedForReplay = false;
        GD.Print("Recording resumed");
    }
    #endregion

    #region Data Access
    public List<ReplayFrame> GetPlayerFrames() => new(_playerFrames);
    public List<ReplayFrame> GetMonsterFrames() => new(_monsterFrames);
    public bool HasRecordedData() => _playerFrames.Count > 0 || _monsterFrames.Count > 0;
    #endregion

    #region Frame Creation
    private ReplayFrame CreateFrameForCharacter(Node2D character, float timestamp)
    {
        return new ReplayFrame
        {
            Timestamp = timestamp,
            Position = character.GlobalPosition,
            Rotation = character.GlobalRotation,
            Animation = ExtractCurrentAnimation(character),
            SpriteFlip = ExtractSpriteFlip(character)
        };
    }

    private string ExtractCurrentAnimation(Node2D character)
    {
        var animSprite = character.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        return animSprite?.IsPlaying() == true ? animSprite.Animation : "idle";
    }

 private bool ExtractSpriteFlip(Node2D character)
{
    Node2D sprite = null;
    
    // For FollowerMonster, check the _spriteNode which could be AnimatedSprite2D or MonsterSprite
    if (character is FollowerMonster)
    {
        sprite = character.GetNodeOrNull<Node2D>("AnimatedSprite2D")
            ?? character.GetNodeOrNull<Node2D>("MonsterSprite");
    }
    // For Monster, check MonsterSprite
    else if (character is Monster)
    {
        sprite = character.GetNodeOrNull<Node2D>("MonsterSprite")
            ?? character.GetNodeOrNull<Node2D>("Sprite2D");
    }
    // For Player, check the usual suspects
    else
    {
        sprite = character.GetNodeOrNull<Node2D>("AnimatedSprite2D")
            ?? character.GetNodeOrNull<Node2D>("MarrieSprite")
            ?? character.GetNodeOrNull<Node2D>("Sprite2D");
    }

    // Return true if sprite is flipped (negative X scale)
    return sprite?.Scale.X < 0;
}
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    #endregion
}