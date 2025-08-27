using Godot;
using System.Collections.Generic;

/// <summary>
/// Data container for storing death-related replay information
/// </summary>
public class DeathRecord
{
    #region Core Properties
    public string DeathId { get; private set; }
    public Vector2 DeathPosition { get; private set; }
    public float DeathTime { get; private set; }
    public int DeathSequence { get; private set; } // Break timestamp ties
    #endregion

    #region Replay Data
    public List<ReplayFrame> PlayerFrames { get; set; } = new();
    public List<ReplayFrame> MonsterFrames { get; set; } = new();
    #endregion

    #region Statistics
    public int TotalFrames => PlayerFrames.Count + MonsterFrames.Count;
    public float RecordingDuration => PlayerFrames.Count > 0 ? PlayerFrames[^1].Timestamp : 0f;
    #endregion

    #region Constructor
    public DeathRecord(string deathId, Vector2 deathPosition, int deathSequence = 0)
    {
        DeathId = deathId ?? throw new System.ArgumentNullException(nameof(deathId));
        DeathPosition = deathPosition;
        DeathTime = (float)Time.GetUnixTimeFromSystem();
        DeathSequence = deathSequence;
    }
    #endregion

    #region Data Validation
    public bool HasValidReplayData()
    {
        return PlayerFrames.Count > 0 || MonsterFrames.Count > 0;
    }

    public bool IsPlayerDataAvailable()
    {
        return PlayerFrames.Count > 0;
    }

    public bool IsMonsterDataAvailable()
    {
        return MonsterFrames.Count > 0;
    }
    #endregion

    #region Utility Methods
    public override string ToString()
    {
        return $"DeathRecord[{DeathId}] at {DeathPosition} - " +
               $"Player: {PlayerFrames.Count} frames, Monster: {MonsterFrames.Count} frames";
    }
    #endregion
}