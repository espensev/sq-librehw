// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Computes a bounded, smoothed temperature rate from direct update samples.
/// </summary>
internal sealed class TemperatureRateSensor : Sensor
{
    internal const int MaxSamples = 64;
    internal static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly Func<DateTime> _utcNow;
    private readonly Queue<TemperatureSample> _samples = new();

    internal TemperatureRateSensor(string name, int index, Hardware hardware, ISettings settings)
        : this(name, index, hardware, settings, () => DateTime.UtcNow)
    { }

    internal TemperatureRateSensor
    (
        string name,
        int index,
        Hardware hardware,
        ISettings settings,
        Func<DateTime> utcNow)
        : base(name,
               index,
               false,
               SensorType.TemperatureRate,
               hardware,
               Array.Empty<ParameterDescription>(),
               settings,
               false,
               utcNow)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    internal int SampleCount => _samples.Count;

    internal void Observe(float? temperature)
    {
        DateTime now = _utcNow();
        if (!temperature.HasValue || !IsFinite(temperature.Value))
        {
            ResetWindow();
            return;
        }

        if (_samples.Count > 0 && now < _samples.Peek().Time)
        {
            ResetWindow();
        }
        else if (_samples.Count > 0)
        {
            DateTime newest = default;
            foreach (TemperatureSample sample in _samples)
                newest = sample.Time;

            if (now < newest)
                ResetWindow();
        }

        DateTime cutoff = now - Window;
        while (_samples.Count > 0 && _samples.Peek().Time < cutoff)
            _samples.Dequeue();

        _samples.Enqueue(new TemperatureSample(now, temperature.Value));
        while (_samples.Count > MaxSamples)
            _samples.Dequeue();

        if (_samples.Count < 3 || now - _samples.Peek().Time < MinimumSpan)
        {
            Value = null;
            return;
        }

        double meanSeconds = 0;
        double meanTemperature = 0;
        DateTime origin = _samples.Peek().Time;
        foreach (TemperatureSample sample in _samples)
        {
            meanSeconds += (sample.Time - origin).TotalSeconds;
            meanTemperature += sample.Temperature;
        }

        meanSeconds /= _samples.Count;
        meanTemperature /= _samples.Count;

        double numerator = 0;
        double denominator = 0;
        foreach (TemperatureSample sample in _samples)
        {
            double x = (sample.Time - origin).TotalSeconds - meanSeconds;
            numerator += x * (sample.Temperature - meanTemperature);
            denominator += x * x;
        }

        double rate = denominator > 0 ? numerator / denominator : double.NaN;
        Value = double.IsNaN(rate) || double.IsInfinity(rate) ? null : (float)rate;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void ResetWindow()
    {
        _samples.Clear();
        ResetPendingHistoryBucket();
        Value = null;
    }

    private readonly struct TemperatureSample
    {
        internal TemperatureSample(DateTime time, float temperature)
        {
            Time = time;
            Temperature = temperature;
        }

        internal DateTime Time { get; }

        internal float Temperature { get; }
    }
}
