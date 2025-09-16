using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MarrieGame.Core.Enums;
using MarrieGame.Core.Models;

public partial class RespawnDebug : Control
{
    [Export] public Vector2 MarginFromTopRight { get; set; } = new Vector2(16, 16);
    [Export] public float UpdateInterval { get; set; } = 0.25f;

    private Label _headerLabel;
    private VBoxContainer _pairsBox;
    private float _timeAccum = 0f;

    public override void _Ready()
    {
        SetupUI();
        SetProcess(true);
    }

    private void SetupUI()
    {
        // Try to use existing scene structure first
        var sceneContainer = GetNodeOrNull<VBoxContainer>("Container");
        if (sceneContainer != null)
        {
            _headerLabel = sceneContainer.GetNodeOrNull<Label>("Header");
            _pairsBox = sceneContainer.GetNodeOrNull<VBoxContainer>("PairsBox") ?? sceneContainer.GetNodeOrNull<VBoxContainer>("TimersBox");
        }

        // Create missing components
        if (_headerLabel == null)
        {
            _headerLabel = new Label();
            _headerLabel.AddThemeColorOverride("font_color", Colors.Green);
            _headerLabel.AddThemeFontSizeOverride("font_size", 14);
            _headerLabel.CustomMinimumSize = new Vector2(400, 24);
        }

        if (_pairsBox == null)
        {
            _pairsBox = new VBoxContainer();
            _pairsBox.CustomMinimumSize = new Vector2(400, 150);
        }

        // Create container if needed
        if (sceneContainer == null)
        {
            var container = new VBoxContainer();
            container.CustomMinimumSize = new Vector2(420, 180);

            if (_headerLabel.GetParent() == null)
                container.AddChild(_headerLabel);
            if (_pairsBox.GetParent() == null)
                container.AddChild(_pairsBox);

            AddChild(container);
        }
        else
        {
            // Add to existing container if not already there
            if (_headerLabel.GetParent() == null)
                sceneContainer.AddChild(_headerLabel);
            if (_pairsBox.GetParent() == null)
                sceneContainer.AddChild(_pairsBox);
        }

        RefreshDisplay();
    }

    public override void _Process(double delta)
    {
        _timeAccum += (float)delta;
        if (_timeAccum >= UpdateInterval)
        {
            _timeAccum = 0f;
            RefreshDisplay();
        }
    }

    private void RefreshDisplay()
    {
        if (EnemyManager.Instance == null)
        {
            _headerLabel.Text = "EnemyManager: Not Found";
            ClearPairsList();
            return;
        }

        UpdateHeader();
        UpdatePairsList();
    }

    private void UpdateHeader()
    {
        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;

        // Get the actual active timer values from EnemyManager
        float activeGlobalLeft = EnemyManager.Instance.GlobalRespawnTimeLeft;
        float activeLocalLeft = EnemyManager.Instance.LocalRespawnTimeLeft;

        // FALLBACK: Sometimes the aggregated GlobalRespawnTimeLeft may report -1
        // even when a FullPair timer exists (edge cases during reality switches).
        // As a safety net, compute the smallest per-pair FullPair timer left and use it.
        if (activeGlobalLeft <= 0)
        {
            var pairs = EnemyManager.Instance.GetAllPairsSnapshot();
            float fallbackMin = float.PositiveInfinity;
            foreach (var p in pairs)
            {
                var type = EnemyManager.Instance.GetRespawnType(p);
                if (type == "FullPair")
                {
                    var tleft = EnemyManager.Instance.GetRespawnTimeLeft(p);
                    if (tleft > 0)
                        fallbackMin = Math.Min(fallbackMin, tleft);
                }
            }

            if (!float.IsPositiveInfinity(fallbackMin))
                activeGlobalLeft = fallbackMin;
        }

        // FIXED: When global timer is active (> 0), it means it has overridden any local timers
        // So we should show the global timer and indicate that local is overridden
        string globalDisplay, localDisplay;

        if (activeGlobalLeft > 0)
        {
            // Global timer is active - show it
            globalDisplay = $"{activeGlobalLeft:F1}s (active)";

            // If there was a local timer, it's been overridden
            if (activeLocalLeft > 0)
                localDisplay = $"OVERRIDDEN by Global Timer";
            else
                localDisplay = $"{EnemyManager.Instance.LocalHideTime:F1}s (config)";
        }
        else
        {
            // No global timer active
            globalDisplay = $"{EnemyManager.Instance.GlobalHideTime:F1}s (config)";

            // Show local timer if active, otherwise show config
            localDisplay = activeLocalLeft > 0 ? $"{activeLocalLeft:F1}s (active)" : $"{EnemyManager.Instance.LocalHideTime:F1}s (config)";
        }

        _headerLabel.Text = $"Reality: {currentReality}\nMonster Global Time: {globalDisplay}\nMonster Local Time: {localDisplay}";
    }

    private void UpdatePairsList()
    {
        ClearPairsList();

        var pairs = EnemyManager.Instance.GetAllPairsSnapshot();
        if (pairs.Count == 0)
        {
            AddPairLabel("No enemy pairs found", Colors.Gray);
            return;
        }

        var currentReality = RealityManager.Instance?.CurrentReality ?? Reality.CurrentReality;

        foreach (var pair in pairs.OrderBy(p => p.PairId))
        {
            if (currentReality == Reality.CurrentReality)
                DisplayMonsterInfo(pair);
            else
                DisplayGhostInfo(pair);
        }
    }

    private void DisplayMonsterInfo(EnemyPair pair)
    {
        if (!IsInstanceValid(pair.Monster)) return;

        var redHearts = EnemyManager.Instance.GetMonsterRedHearts(pair.Monster);
        var respawnTimeLeft = EnemyManager.Instance.GetRespawnTimeLeft(pair);
        var respawnType = EnemyManager.Instance.GetRespawnType(pair);

        var status = GetHiddenStatus(pair.IsHidden, respawnTimeLeft, respawnType);
        var pairName = GetFriendlyPairName(pair);

        var text = $"{pairName} | Red: {redHearts} | {status}";
        var color = pair.IsHidden ? Colors.Orange : Colors.LightGreen;

        AddPairLabel(text, color);
    }

    private void DisplayGhostInfo(EnemyPair pair)
    {
        if (!IsInstanceValid(pair.Ghost)) return;

        var purpleHearts = EnemyManager.Instance.GetGhostPurpleHearts(pair.Ghost);
        var respawnTimeLeft = EnemyManager.Instance.GetRespawnTimeLeft(pair);
        var respawnType = EnemyManager.Instance.GetRespawnType(pair);

        var status = GetHiddenStatus(pair.IsHidden, respawnTimeLeft, respawnType);
        var pairName = GetFriendlyPairName(pair);

        var text = $"{pairName} | Purple: {purpleHearts} | {status}";
        var color = pair.IsHidden ? Colors.Orange : Colors.LightBlue;

        AddPairLabel(text, color);
    }

    private string GetHiddenStatus(bool isHidden, float timeLeft, string respawnType)
    {
        if (!isHidden)
            return "Active";

        if (timeLeft > 0)
        {
            var timerType = respawnType == "MonsterOnly" ? "Local" : "Global";
            return $"Hidden ({timerType}: {timeLeft:F1}s)";
        }

        return "Hidden (No Timer)";
    }

    private string GetFriendlyPairName(EnemyPair pair)
    {
        if (pair == null) return "Unknown";

        var monsterName = GetFriendlyNodeName(pair.Monster);
        var ghostName = GetFriendlyNodeName(pair.Ghost);

        return ghostName != "None" ? $"{monsterName}+{ghostName}" : monsterName;
    }

    private string GetFriendlyNodeName(Node node)
    {
        if (!IsInstanceValid(node)) return "None";

        // Use type name if node has generic scene name
        if (node.Name == "CharacterBody2D")
            return node.GetType().Name;

        return node.Name;
    }

    private void AddPairLabel(string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(380, 20)
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 12);
        _pairsBox.AddChild(label);
    }

    private void ClearPairsList()
    {
        foreach (Node child in _pairsBox.GetChildren())
            child.QueueFree();
    }
}