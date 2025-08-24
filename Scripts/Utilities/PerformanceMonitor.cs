using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Performance monitoring and profiling system for game optimization
/// </summary>
public static class PerformanceMonitor
{
    #region Performance Tracking
    private static readonly Dictionary<string, Stopwatch> _timers = new();
    private static readonly Dictionary<string, List<double>> _measurements = new();
    private static readonly Dictionary<string, long> _counters = new();
    #endregion

    #region Configuration
    private static bool _isEnabled = false;
    private static int _maxSamples = 100;
    #endregion

    #region Enable/Disable
    public static void Enable() => _isEnabled = true;
    public static void Disable() => _isEnabled = false;
    public static bool IsEnabled => _isEnabled;
    #endregion

    #region Timer Operations
    public static void StartTimer(string name)
    {
        if (!_isEnabled) return;

        if (!_timers.ContainsKey(name))
        {
            _timers[name] = new Stopwatch();
            _measurements[name] = new List<double>();
        }

        _timers[name].Restart();
    }

    public static void StopTimer(string name)
    {
        if (!_isEnabled || !_timers.ContainsKey(name)) return;

        _timers[name].Stop();
        var elapsed = _timers[name].Elapsed.TotalMilliseconds;
        
        var measurements = _measurements[name];
        measurements.Add(elapsed);
        
        // Keep only recent measurements
        if (measurements.Count > _maxSamples)
        {
            measurements.RemoveAt(0);
        }
    }

    public static double GetAverageTime(string name)
    {
        if (!_measurements.ContainsKey(name) || _measurements[name].Count == 0)
            return 0;

        var measurements = _measurements[name];
        var sum = 0.0;
        foreach (var measurement in measurements)
        {
            sum += measurement;
        }
        return sum / measurements.Count;
    }
    #endregion

    #region Counter Operations
    public static void IncrementCounter(string name, long amount = 1)
    {
        if (!_isEnabled) return;

        if (!_counters.ContainsKey(name))
        {
            _counters[name] = 0;
        }
        _counters[name] += amount;
    }

    public static long GetCounter(string name)
    {
        return _counters.ContainsKey(name) ? _counters[name] : 0;
    }

    public static void ResetCounter(string name)
    {
        if (_counters.ContainsKey(name))
        {
            _counters[name] = 0;
        }
    }
    #endregion

    #region Reporting
    public static void LogPerformanceReport()
    {
        if (!_isEnabled) return;

        GD.Print("=== Performance Report ===");
        
        foreach (var kvp in _measurements)
        {
            var name = kvp.Key;
            var avgTime = GetAverageTime(name);
            var sampleCount = kvp.Value.Count;
            GD.Print($"{name}: {avgTime:F2}ms avg ({sampleCount} samples)");
        }

        foreach (var kvp in _counters)
        {
            GD.Print($"{kvp.Key}: {kvp.Value} count");
        }
    }

    public static void ClearAll()
    {
        _timers.Clear();
        _measurements.Clear();
        _counters.Clear();
    }
    #endregion

    #region Memory Monitoring
    public static long GetMemoryUsage()
    {
        return GC.GetTotalMemory(false);
    }

    public static void LogMemoryUsage(string context = "")
    {
        if (!_isEnabled) return;

        var memory = GetMemoryUsage();
        var memoryMB = memory / (1024.0 * 1024.0);
        GD.Print($"Memory Usage {context}: {memoryMB:F2} MB");
    }
    
    // Simple performance warnings
    public static void CheckPerformanceWarnings()
    {
        if (!_isEnabled) return;
        
        var fps = Engine.GetFramesPerSecond();
        var frameTime = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        
        if (fps < 45)
        {
            GD.PrintRich($"[color=red]Low FPS warning: {fps:F1}[/color]");
        }
        
        if (frameTime > 20.0)
        {
            GD.PrintRich($"[color=orange]High frame time: {frameTime:F1}ms[/color]");
        }
    }
    #endregion
}
