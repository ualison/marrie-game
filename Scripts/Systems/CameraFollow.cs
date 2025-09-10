using Godot;

/// Simple Camera2D follow script with configurable offset and optional smoothing.
public partial class CameraFollow : Camera2D
{
    [Export]
    public Vector2 FollowOffset { get; set; } = Vector2.Zero;

    [Export]
    public NodePath TargetPath { get; set; } = new NodePath("");

    [Export]
    public bool UseSmoothing { get; set; } = true;

    [Export]
    public float SmoothSpeed { get; set; } = 8f; // higher = snappier

    [Export]
    public bool SetCurrentOnStart { get; set; } = true;

    [Export]
    public bool DebugTarget { get; set; } = false;

    private Node2D _target;

    public override void _Ready()
    {
        if (SetCurrentOnStart)
            MakeCurrent();
            
        CallDeferred(nameof(FindTarget));
    }

    private void FindTarget()
    {
        // Strategy 1: Use explicitly set TargetPath (more robust)
        if (!string.IsNullOrEmpty(TargetPath.ToString()))
        {
            // Try resolving relative to this node first
            _target = GetNodeOrNull<Node2D>(TargetPath);

            // If not found, try a root-wide search for the final segment of the path
            if (_target == null)
            {
                var segments = TargetPath.ToString().Split('/');
                var last = segments.Length > 0 ? segments[segments.Length - 1] : TargetPath.ToString();
                var found = FindNodeByNameRecursive(GetTree().Root, last);
                if (found is Node2D nd)
                    _target = nd;
            }

            if (_target != null)
            {
                // If the found node isn't the actual CharacterBody2D, try to resolve a child CharacterBody2D
                if (!(_target is CharacterBody2D))
                {
                    var childBody = FindCharacterBodyInTree(_target);
                    if (childBody != null)
                    {
                        _target = childBody;
                        if (DebugTarget)
                            GD.Print($"CameraFollow: Resolved CharacterBody2D inside TargetPath node: {_target.Name} at {_target.GlobalPosition}");
                        return;
                    }
                }

                if (DebugTarget)
                    GD.Print($"CameraFollow: Found target via TargetPath: {_target.Name} at {_target.GlobalPosition}");
                return;
            }
            // Don't error yet; try other strategies below before reporting failure
        }

        // Strategy 2: Find player via group (most reliable)
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0)
        {
            _target = players[0] as Node2D;
            if (_target != null)
            {
                if (DebugTarget)
                    GD.Print($"CameraFollow: Found target via 'player' group: {_target.Name} at {_target.GlobalPosition}");
                return;
            }
        }

        // Strategy 3: Search for CharacterBody2D in GameEntities/Player
        var gameEntities = GetTree().Root.FindChild("GameEntities", true, false);
        if (gameEntities != null)
        {
            var player = gameEntities.FindChild("Player", true, false);
            if (player != null)
            {
                var characterBody = player.FindChild("CharacterBody2D", true, false) as Node2D;
                if (characterBody != null)
                {
                    _target = characterBody;
                    if (DebugTarget)
                        GD.Print($"CameraFollow: Found target via GameEntities search: {_target.Name} at {_target.GlobalPosition}");
                    return;
                }
            }
        }

        // Strategy 4: Broad search for any CharacterBody2D in player group
        var allNodes = GetTree().GetNodesInGroup("player");
        foreach (Node node in allNodes)
        {
            if (node is CharacterBody2D characterBody2D)
            {
                _target = characterBody2D;
                if (DebugTarget)
                    GD.Print($"CameraFollow: Found CharacterBody2D in player group: {_target.Name} at {_target.GlobalPosition}");
                return;
            }
            
            // Check if this node has a CharacterBody2D child
            var childCharacterBody = node.FindChild("CharacterBody2D", true, false) as Node2D;
            if (childCharacterBody != null)
            {
                _target = childCharacterBody;
                if (DebugTarget)
                    GD.Print($"CameraFollow: Found CharacterBody2D as child: {_target.Name} at {_target.GlobalPosition}");
                return;
            }
        }

        // Strategy 5: Last resort - search entire tree for CharacterBody2D
        var allCharacterBodies = GetTree().GetNodesInGroup("player");
        if (allCharacterBodies.Count == 0)
        {
            // If no player group, search for any CharacterBody2D
            _target = FindCharacterBodyInTree(GetTree().Root);
        }

        if (_target == null)
        {
            GD.PrintErr("CameraFollow: Could not find any valid target!");
            GD.PrintErr("Make sure your player CharacterBody2D is in the 'player' group or set TargetPath explicitly.");
        }
        else if (DebugTarget)
        {
            GD.Print($"CameraFollow: Final target found: {_target.Name} at {_target.GlobalPosition}");
        }
    }

    private Node2D FindCharacterBodyInTree(Node parent)
    {
        if (parent is CharacterBody2D characterBody)
            return characterBody;

        foreach (Node child in parent.GetChildren())
        {
            var result = FindCharacterBodyInTree(child);
            if (result != null)
                return result;
        }

        return null;
    }

    // Helper to find a node by name recursively starting from the given parent
    private Node FindNodeByNameRecursive(Node parent, string name)
    {
        if (parent == null) return null;

        if (parent.Name == name)
            return parent;

        foreach (Node child in parent.GetChildren())
        {
            var result = FindNodeByNameRecursive(child, name);
            if (result != null)
                return result;
        }

        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Ensure this camera is current if requested
        if (SetCurrentOnStart && !IsCurrent())
        {
            MakeCurrent();
            if (DebugTarget)
                GD.Print($"CameraFollow: MakeCurrent() called to activate camera {Name}");
        }

        if (_target == null)
        {
            // Try to find target again if it was null
            if (DebugTarget)
                GD.Print("CameraFollow: Target is null, attempting to find again...");
            FindTarget();
            return;
        }

        // Validate target is still valid
        if (!IsInstanceValid(_target))
        {
            if (DebugTarget)
                GD.Print("CameraFollow: Target became invalid, searching for new target...");
            _target = null;
            FindTarget();
            return;
        }

        var desired = _target.GlobalPosition + FollowOffset;
        
        if (UseSmoothing)
        {
            var t = Mathf.Clamp((float)(SmoothSpeed * delta), 0f, 1f);
            GlobalPosition = GlobalPosition + (desired - GlobalPosition) * t;
        }
        else
        {
            GlobalPosition = desired;
        }

        if (DebugTarget)
        {
            GD.Print($"CameraFollow: Following {_target.Name} at {_target.GlobalPosition}, camera at {GlobalPosition}");
        }
    }

    // Public method to manually set target
    public void SetTarget(Node2D target)
    {
        _target = target;
        if (DebugTarget)
            GD.Print($"CameraFollow: Target manually set to: {target?.Name}");
    }

    // Public method to get current target
    public Node2D GetTarget()
    {
        return _target;
    }
}