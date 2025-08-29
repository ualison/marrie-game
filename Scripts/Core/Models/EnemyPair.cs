using Godot;
using MarrieGame.Core.Models;

namespace MarrieGame.Core.Models
{
    // A simple serializable model representing a pairing between a Monster and a GhostMonster
   public class EnemyPair
{
    public Monster Monster { get; set; }
    public GhostMonster Ghost { get; set; }
    public string PairId { get; set; }
    public bool IsHidden { get; set; } = false;
}
}
