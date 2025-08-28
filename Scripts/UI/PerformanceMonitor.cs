using Godot;
using System;
using System.Collections.Generic;
using System.Text;

public partial class PerformanceMonitor : Control
{
    [Export]
    public int HistorySize { get; set; } = 60;

    [Export]
    public Key ToggleKey { get; set; } = Key.F3;

    [Export]
    public int WarningFps { get; set; } = 30;

    [Export]
    public int CriticalFps { get; set; } = 20;

    private readonly Queue<float> fpsHistory = new();
    private float fpsCurrent = 0f;
    private float fpsAvg = 0f;
    private float fpsMin = float.MaxValue;
    private float fpsMax = 0f;

    private double frameTimeMs = 0.0;
    private double memMb = 0.0;
    private int drawCalls = -1; // n/a by default
    private int nodeCount = 0;
    private double physicsTimeMs = 0.0;
    private double cpuPercent = 0.0;
    private double processRamMb = 0.0;
    private double gpuUsagePercent = -1.0; // best-effort placeholder
    private double vramMb = -1.0; // best-effort placeholder

    private readonly StringBuilder csvBuilder = new();

    // Node refs (initialized to null; guarded before use)
    private Label fpsLabel = null;
    private Label frameLabel = null;
    private Label memLabel = null;
    private Label drawLabel = null;
    private Label nodeLabel = null;
    private Label physLabel = null;
    private Line2D graphLine = null;

    // CPU sampling
    private TimeSpan prevTotalProcTime = TimeSpan.Zero;
    private DateTime prevSampleTime = DateTime.UtcNow;
    private readonly System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();

    private readonly System.Diagnostics.Stopwatch physicsStopwatch = new System.Diagnostics.Stopwatch();

    public override void _Ready()
    {
    // Try common paths; fall back to FindNode to be editor-safe
    fpsLabel = GetNodeOrNull<Label>("VBox/FpsLabel") ?? GetNodeOrNull<Label>("VBox/Panel/Inner/FpsLabel") ?? GetNodeOrNull<Label>("FpsLabel");
    frameLabel = GetNodeOrNull<Label>("VBox/FrameLabel") ?? GetNodeOrNull<Label>("VBox/Panel/Inner/FrameLabel") ?? GetNodeOrNull<Label>("FrameLabel");
    memLabel = GetNodeOrNull<Label>("VBox/MemLabel") ?? GetNodeOrNull<Label>("VBox/Panel/Inner/MemLabel") ?? GetNodeOrNull<Label>("MemLabel");
    drawLabel = GetNodeOrNull<Label>("VBox/DrawLabel") ?? GetNodeOrNull<Label>("VBox/Panel/Inner/DrawLabel") ?? GetNodeOrNull<Label>("DrawLabel");
    nodeLabel = GetNodeOrNull<Label>("VBox/NodeLabel") ?? GetNodeOrNull<Label>("VBox/Panel/Inner/NodeLabel") ?? GetNodeOrNull<Label>("NodeLabel");
    physLabel = GetNodeOrNull<Label>("VBox/PhysLabel") ?? GetNodeOrNull<Label>("VBox/Panel/Inner/PhysLabel") ?? GetNodeOrNull<Label>("PhysLabel");
    graphLine = GetNodeOrNull<Line2D>("Graph/Line2D") ?? GetNodeOrNull<Line2D>("VBox/Panel/Graph/Line2D") ?? GetNodeOrNull<Line2D>("Line2D");

        // Pre-fill history
        for (int i = 0; i < HistorySize; i++) fpsHistory.Enqueue(0f);

        csvBuilder.AppendLine("timestamp,fps,frame_ms,mem_mb,draw_calls,node_count,physics_ms");


        Visible = false;
        SetProcess(true);
        SetPhysicsProcess(true);
        // Try to populate VRAM via WMIC on Windows as a best-effort fallback.
        TryGetVramViaWmic();
    }

    private void TryGetVramViaWmic()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c wmic path Win32_VideoController get AdapterRAM /format:list")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p != null)
            {
                string outTxt = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1000);
                // Parse lines like: AdapterRAM=2147483648
                foreach (var line in outTxt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("AdapterRAM=", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split('=');
                        if (parts.Length == 2 && long.TryParse(parts[1].Trim(), out var bytes))
                        {
                            vramMb = bytes / (1024.0 * 1024.0);
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr("VRAM detection via WMIC failed: " + e.Message);
            vramMb = -1.0;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey k && k.Pressed && !k.Echo && ((Key)k.Keycode) == ToggleKey)
        {
            Visible = !Visible;
        }
    }

    public override void _Process(double delta)
    {
        // FPS
        fpsCurrent = (float)Engine.GetFramesPerSecond();
        frameTimeMs = fpsCurrent > 0 ? 1000.0 / Math.Max(1f, fpsCurrent) : 0.0;

        // Memory in MB (system static) and process RAM
        memMb = OS.GetStaticMemoryUsage() / (1024.0 * 1024.0);
        processRamMb = currentProcess.WorkingSet64 / (1024.0 * 1024.0);

        // CPU usage approximation for current process (percent)
        var now = DateTime.UtcNow;
        var totalProc = currentProcess.TotalProcessorTime;
        var elapsed = (now - prevSampleTime).TotalSeconds;
        if (elapsed >= 0.01)
        {
            var deltaProc = (totalProc - prevTotalProcTime).TotalSeconds;
            // CPU percent relative to logical processors
            var cpu = (deltaProc / elapsed) * 100.0 / System.Environment.ProcessorCount;
            cpuPercent = Math.Max(0.0, Math.Min(100.0, cpu));
            prevTotalProcTime = totalProc;
            prevSampleTime = now;
        }

        // Draw calls: not available consistently across backends; keep as -1
        drawCalls = -1;

        // Node count
        var root = GetTree().Root;
        nodeCount = CountNodes(root);

        // Update history
        fpsHistory.Enqueue(fpsCurrent);
        while (fpsHistory.Count > HistorySize) fpsHistory.Dequeue();

        // Recompute stats
        fpsMin = float.MaxValue;
        fpsMax = 0f;
        double sum = 0.0;
        foreach (var v in fpsHistory)
        {
            fpsMin = Math.Min(fpsMin, v);
            fpsMax = Math.Max(fpsMax, v);
            sum += v;
        }
        fpsAvg = fpsHistory.Count > 0 ? (float)(sum / fpsHistory.Count) : 0f;
        if (float.IsInfinity(fpsMin) || fpsMin == float.MaxValue) fpsMin = 0f;

    // Append CSV (extend with CPU/process RAM/GPU placeholders)
    csvBuilder.AppendLine($"{DateTime.UtcNow:o},{fpsCurrent:F1},{frameTimeMs:F2},{memMb:F2},{processRamMb:F2},{cpuPercent:F2},{gpuUsagePercent},{vramMb},{drawCalls},{nodeCount},{physicsTimeMs:F3}");

        if (Visible)
        {
            UpdateUi();
            UpdateGraph();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Approximate physics time by measuring this callback (lightweight)
        physicsStopwatch.Restart();
        // no heavy work here
        physicsStopwatch.Stop();
        physicsTimeMs = physicsStopwatch.Elapsed.TotalMilliseconds;
    }

    private void UpdateUi()
    {
    if (fpsLabel != null)
        {
            var fpsColor = GetColorForFps(fpsCurrent);
            fpsLabel.Text = $"FPS: {fpsCurrent:F1} (avg {fpsAvg:F1}, min {fpsMin:F1}, max {fpsMax:F1})";
            fpsLabel.AddThemeColorOverride("font_color", fpsColor);
        }
    // New metrics labels
    var cpuLabel = GetNodeOrNull<Label>("VBox/Panel/Inner/CpuLabel") ?? GetNodeOrNull<Label>("CpuLabel");
    var procRamLabel = GetNodeOrNull<Label>("VBox/Panel/Inner/ProcRamLabel") ?? GetNodeOrNull<Label>("ProcRamLabel");
    var gpuLabel = GetNodeOrNull<Label>("VBox/Panel/Inner/GpuLabel") ?? GetNodeOrNull<Label>("GpuLabel");
    var vramLabel = GetNodeOrNull<Label>("VBox/Panel/Inner/VramLabel") ?? GetNodeOrNull<Label>("VramLabel");

    if (cpuLabel != null) cpuLabel.Text = $"CPU: {cpuPercent:F1}%";
    if (procRamLabel != null) procRamLabel.Text = $"Proc RAM: {processRamMb:F2} MB";
    if (gpuLabel != null) gpuLabel.Text = (gpuUsagePercent >= 0 ? $"GPU: {gpuUsagePercent:F1}%" : "GPU: n/a");
    if (vramLabel != null) vramLabel.Text = (vramMb >= 0 ? $"VRAM: {vramMb:F1} MB" : "VRAM: n/a");
        if (frameLabel != null) frameLabel.Text = $"Frame: {frameTimeMs:F2} ms";
        if (memLabel != null) memLabel.Text = $"Memory: {memMb:F2} MB";
        if (drawLabel != null) drawLabel.Text = $"Draw calls: {(drawCalls >= 0 ? drawCalls.ToString() : "n/a")}";
        if (nodeLabel != null) nodeLabel.Text = $"Nodes: {nodeCount}";
        if (physLabel != null) physLabel.Text = $"Physics: {physicsTimeMs:F3} ms";
    }

    private void UpdateGraph()
    {
    var list = new Godot.Collections.Array<Vector2>();
        var arr = new List<float>(fpsHistory);
        float maxDisplay = Math.Max(60f, fpsMax > 0 ? fpsMax : 60f);
        float w = 140f;
        float h = 48f;
        for (int i = 0; i < arr.Count; i++)
        {
            float x = (i / (float)Math.Max(1, HistorySize - 1)) * w;
            float y = h - (arr[i] / maxDisplay) * h;
            list.Add(new Vector2(x, y));
        }
    // Line2D.Points expects a Vector2[]; convert manually
        var pts = new Vector2[list.Count];
        for (int i = 0; i < list.Count; i++) pts[i] = list[i];
        if (graphLine != null)
        {
            graphLine.Points = pts;
            graphLine.Width = 2f;
            graphLine.DefaultColor = GetColorForFps(fpsCurrent);
        }
    }

    private Color GetColorForFps(float fps)
    {
        if (fps <= CriticalFps) return new Color(1f, 0.2f, 0.2f);
        if (fps <= WarningFps) return new Color(1f, 0.8f, 0.2f);
        return new Color(0.4f, 1f, 0.5f);
    }

    private int CountNodes(Node node)
    {
        if (node == null) return 0;
        int count = 1;
        int children = node.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            var child = node.GetChild(i) as Node;
            if (child != null) count += CountNodes(child);
        }
        return count;
    }

    public string SaveCsv(string filePath = "user://performance_export.csv")
    {
        try
        {
            var abs = ProjectSettings.GlobalizePath(filePath);
            var fa = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
            fa.StoreString(csvBuilder.ToString());
            fa.Close();
            return abs;
        }
        catch (Exception e)
        {
            GD.PrintErr("Failed to save performance CSV: ", e.Message);
            return string.Empty;
        }
    }
}
