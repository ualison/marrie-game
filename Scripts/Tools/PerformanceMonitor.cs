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
            _timers[name] = new Stopwatch();

        _timers[name].Restart();
    }

    public static void StopTimer(string name)
    {
        if (!_isEnabled) return;

        if (_timers.ContainsKey(name))
        {
            _timers[name].Stop();
            RecordMeasurement(name, _timers[name].Elapsed.TotalMilliseconds);
        }
    }

    public static void RecordMeasurement(string name, double value)
    {
        if (!_isEnabled) return;

        if (!_measurements.ContainsKey(name))
            _measurements[name] = new List<double>();

        var measurements = _measurements[name];
        measurements.Add(value);

        // Keep only recent samples
        if (measurements.Count > _maxSamples)
            measurements.RemoveAt(0);
    }
    #endregion

    #region Counter Operations
    public static void IncrementCounter(string name, long amount = 1)
    {
        if (!_isEnabled) return;

        if (!_counters.ContainsKey(name))
            _counters[name] = 0;

        _counters[name] += amount;
    }

    public static void ResetCounter(string name)
    {
        if (_counters.ContainsKey(name))
            _counters[name] = 0;
    }

    public static long GetCounter(string name)
    {
        return _counters.ContainsKey(name) ? _counters[name] : 0;
    }
    #endregion

    #region Statistics
    public static double GetAverageTime(string name)
    {
        if (!_measurements.ContainsKey(name) || _measurements[name].Count == 0)
            return 0;

        var measurements = _measurements[name];
        double sum = 0;
        foreach (var measurement in measurements)
            sum += measurement;

        return sum / measurements.Count;
    }

    public static double GetMaxTime(string name)
    {
        if (!_measurements.ContainsKey(name) || _measurements[name].Count == 0)
            return 0;

        double max = 0;
        foreach (var measurement in _measurements[name])
            if (measurement > max) max = measurement;

        return max;
    }

    public static double GetMinTime(string name)
    {
        if (!_measurements.ContainsKey(name) || _measurements[name].Count == 0)
            return 0;

        double min = double.MaxValue;
        foreach (var measurement in _measurements[name])
            if (measurement < min) min = measurement;

        return min == double.MaxValue ? 0 : min;
    }

    public static int GetSampleCount(string name)
    {
        return _measurements.ContainsKey(name) ? _measurements[name].Count : 0;
    }
    #endregion

    #region Reporting
    public static void PrintReport(string name = null)
    {
        if (!_isEnabled) return;

        GD.Print("=== Performance Report ===");

        if (name != null)
        {
            PrintTimerStats(name);
            PrintCounterStats(name);
        }
        else
        {
            // Print all timers
            foreach (var timerName in _measurements.Keys)
                PrintTimerStats(timerName);

            // Print all counters
            foreach (var counterName in _counters.Keys)
                PrintCounterStats(counterName);
        }

        GD.Print("========================");
    }

    private static void PrintTimerStats(string name)
    {
        if (_measurements.ContainsKey(name))
        {
            var avg = GetAverageTime(name);
            var max = GetMaxTime(name);
            var min = GetMinTime(name);
            var count = GetSampleCount(name);

            GD.Print($"{name}: Avg={avg:F2}ms Max={max:F2}ms Min={min:F2}ms Samples={count}");
        }
    }

    private static void PrintCounterStats(string name)
    {
        if (_counters.ContainsKey(name))
        {
            GD.Print($"{name}: Count={_counters[name]}");
        }
    }

    public static void ClearAll()
    {
        _timers.Clear();
        _measurements.Clear();
        _counters.Clear();
        GD.Print("PerformanceMonitor: All data cleared");
    }
    #endregion
}
