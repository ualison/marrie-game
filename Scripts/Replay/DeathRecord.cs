using Godot;
using System.Collections.Generic;

// Data container for storing death-related replay information
public class DeathRecord
{
    #region Core Properties
    public string DeathId { get; private set; }
    public Vector2 DeathPosition { get; private set; }
    public float DeathTime { get; private set; }
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
    public DeathRecord(string deathId, Vector2 deathPosition)
    {
        // Initialize death record with ID, position, and timestamp
        DeathId = deathId ?? throw new System.ArgumentNullException(nameof(deathId));
        DeathPosition = deathPosition;
        DeathTime = (float)Time.GetUnixTimeFromSystem();
    }
    #endregion

    #region Data Validation
    public bool HasValidReplayData()
    {
        // Check if record contains usable replay data
        return PlayerFrames.Count > 0 || MonsterFrames.Count > 0;
    }

    public bool IsPlayerDataAvailable()
    {
        // Check if player replay data is available
        return PlayerFrames.Count > 0;
    }

    public bool IsMonsterDataAvailable()
    {
        // Check if monster replay data is available
        return MonsterFrames.Count > 0;
    }
    #endregion

    #region Utility Methods
    public override string ToString()
    {
        // Provide readable string representation for debugging
        return $"DeathRecord[{DeathId}] at {DeathPosition} - " +
               $"Player: {PlayerFrames.Count} frames, Monster: {MonsterFrames.Count} frames";
    }
    #endregion
}