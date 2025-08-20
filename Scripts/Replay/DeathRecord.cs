using Godot;
using System.Collections.Generic;

public class DeathRecord
{
    public string DeathId { get; set; }
    public Vector2 DeathPosition { get; set; }
    public List<ReplayFrame> PlayerFrames { get; set; } = new();
    public List<ReplayFrame> MonsterFrames { get; set; } = new();
    public float DeathTime { get; set; }

    public DeathRecord(string deathId, Vector2 deathPosition)
    {
        DeathId = deathId;
        DeathPosition = deathPosition;
        DeathTime = (float)Time.GetUnixTimeFromSystem();
    }
}