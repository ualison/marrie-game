using Godot;

/// <summary>
/// Makes a CanvasLayer follow the camera so UI elements move with the camera
/// Attach this to your UserInterface CanvasLayer node
/// </summary>
public partial class CanvasCameraFollower : Control
{
    [Export] public bool FollowCamera { get; set; } = true;
    [Export] public bool DebugCameraFollow { get; set; } = false;
    
    private Camera2D _cachedCamera = null;
    private Vector2 _lastCameraPos = new Vector2(float.NaN, float.NaN);
    private double _debugAccum = 0.0;

    public override void _Ready()
    {
        // Start following immediately
        if (FollowCamera)
        {
            SetProcess(true);
        }
    }

    public override void _Process(double delta)
    {
        if (!FollowCamera) return;

        var camera = ResolveCamera();
        if (camera == null) return;

        Vector2 cameraPos = camera.GlobalPosition;
        bool cameraMoved = float.IsNaN(_lastCameraPos.X) || cameraPos != _lastCameraPos;

        if (cameraMoved)
        {
            // Only move HUD elements with the camera, not overlay elements
            var hudNode = GetNodeOrNull("HUD");
            if (hudNode is Control hud)
            {
                hud.Position = camera.GlobalPosition;
            }
            
            // Keep overlays fixed to screen (don't move with camera)
            var overlaysNode = GetNodeOrNull("Overlays");
            if (overlaysNode is Control overlays)
            {
                overlays.Position = Vector2.Zero; // Always keep at screen origin
            }
            
            _lastCameraPos = cameraPos;

            if (DebugCameraFollow)
            {
                _debugAccum += delta;
                if (_debugAccum >= 1.0)
                {
                    _debugAccum = 0.0;
                    GD.Print($"CanvasLayer camera follow - HUD: {camera.GlobalPosition}, Overlays: Fixed");
                }
            }
        }
    }

    private Camera2D ResolveCamera()
    {
        // Try viewport camera first
        var camera = GetViewport().GetCamera2D();
        if (camera != null) return camera;

        // Try player camera
        var player = GetTree().GetFirstNodeInGroup("player") as Node;
        if (player != null)
        {
            var playerCamera = player.GetNodeOrNull<Camera2D>("Camera2D");
            if (playerCamera != null) return playerCamera;
        }

        // Search for any current camera
        return FindCurrentCamera(GetTree().Root);
    }

    private Camera2D FindCurrentCamera(Node parent)
    {
        if (parent is Camera2D cam && cam.IsCurrent())
            return cam;

        foreach (Node child in parent.GetChildren())
        {
            var result = FindCurrentCamera(child);
            if (result != null) return result;
        }

        return null;
    }
}