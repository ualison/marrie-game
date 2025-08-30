using Godot;
using MarrieGame.Core.Models;

namespace MarrieGame.Core.Models
{
    // A simple serializable model representing a pairing between a Monster and a GhostMonster
   public class EnemyPair
{
    // Use global-qualified types to reference scene classes that live in the root namespace
    public global::Monster Monster { get; set; }
    public global::GhostMonster Ghost { get; set; }
    public string PairId { get; set; }
    public bool IsHidden { get; set; } = false;
}
}
