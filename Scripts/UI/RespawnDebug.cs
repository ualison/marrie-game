using System.Text;
using System.Collections.Generic;
using Godot;

public partial class RespawnDebug : Control
{
    [Export] public Vector2 MarginFromTopRight { get; set; } = new Vector2(16, 16);
    [Export] public float UpdateInterval { get; set; } = 0.25f;

    private Label _headerLabel;
    private VBoxContainer _timersBox;
    private float _timeAccum = 0f;

    public override void _Ready()
    {
        // Prefer existing scene nodes if present to avoid overlapping UI
        _headerLabel = GetNodeOrNull<Label>("Container/Header") ?? GetNodeOrNull<Label>("Header");
        _timersBox = GetNodeOrNull<VBoxContainer>("Container/TimersBox") ?? GetNodeOrNull<VBoxContainer>("TimersBox");

        if (_headerLabel == null)
        {
            _headerLabel = new Label();
            _headerLabel.AddThemeColorOverride("font_color", Colors.White);
            _headerLabel.AddThemeFontSizeOverride("font_size", 14);
            _headerLabel.CustomMinimumSize = new Vector2(300, 24);
            AddChild(_headerLabel);
        }

        if (_timersBox == null)
        {
            _timersBox = new VBoxContainer();
            _timersBox.CustomMinimumSize = new Vector2(300, 96);
            AddChild(_timersBox);
        }

    RefreshText();
    SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _timeAccum += (float)delta;
        if (_timeAccum >= UpdateInterval)
        {
            _timeAccum = 0f;
            RefreshText();
        }
    }

    private void RefreshText()
    {
        if (EnemyManager.Instance == null)
        {
            _headerLabel.Text = "EnemyManager: (not present)";
            return;
        }

        // Header shows configured global/local hide times
        _headerLabel.Text = $"GlobalMonsterHideTime: {EnemyManager.Instance.GlobalMonsterHideTime:F1}s\nLocalMonsterHideTime: {EnemyManager.Instance.LocalMonsterHideTime:F1}s";

        var timers = EnemyManager.Instance.GetActiveRespawnTimers();
        // Clear previous timer labels
        foreach (Node child in _timersBox.GetChildren())
            child.QueueFree();

        if (timers == null || timers.Count == 0)
        {
            var none = new Label { Text = "Active timers: none" };
            none.AddThemeColorOverride("font_color", Colors.Gray);
            _timersBox.AddChild(none);
        }
        else
        {
            foreach (var t in timers)
            {
                string label = t.TimerType == "Pair" ? "GlobalDeath" : "LocalDeath";
                var l = new Label { Text = $"{label}: {t.SecondsLeft:F1}s" };
                l.AddThemeColorOverride("font_color", label == "GlobalDeath" ? Colors.Red : Colors.Blue);
                _timersBox.AddChild(l);
            }
        }
    }
}
