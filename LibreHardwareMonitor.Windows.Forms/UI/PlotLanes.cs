// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public sealed class PlotLane
{
    internal PlotLane(string key, string name, SensorType sensorType, int weight, bool isDefault)
    {
        Key = key;
        Name = name;
        SensorType = sensorType;
        Weight = weight;
        IsDefault = isDefault;
    }

    public string Key { get; }

    public string Name { get; internal set; }

    public SensorType SensorType { get; }

    public int Weight { get; internal set; }

    public bool IsDefault { get; }
}

/// <summary>
/// Pure graph-lane state: parsing, monotonic key allocation, same-type membership resolution,
/// weights, ordering, and serialization. UI controls and plot axes remain outside this type.
/// </summary>
public sealed class PlotLanes
{
    private readonly Dictionary<SensorType, PlotLane> _defaultLanes = new();
    private readonly List<PlotLane> _userLanes = new();
    private readonly Dictionary<string, PlotLane> _lanesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<SensorType, int> _nextNumbers = new();

    public PlotLanes(
        string serialized = null,
        IDictionary<SensorType, int> nextNumbers = null,
        IDictionary<SensorType, int> defaultWeights = null)
    {
        foreach (SensorType type in Enum.GetValues(typeof(SensorType)))
        {
            string key = type.ToString();
            int weight = defaultWeights != null && defaultWeights.TryGetValue(type, out int storedWeight)
                ? ClampWeight(storedWeight)
                : 1;
            var lane = new PlotLane(key, key, type, weight, true);
            _defaultLanes.Add(type, lane);
            _lanesByKey.Add(key, lane);
            _nextNumbers[type] = nextNumbers != null && nextNumbers.TryGetValue(type, out int next)
                ? Math.Max(2, next)
                : 2;
        }

        ParseUserLanes(serialized);

        foreach (PlotLane lane in _userLanes)
        {
            int number = GetLaneNumber(lane.Key);
            _nextNumbers[lane.SensorType] = Math.Max(_nextNumbers[lane.SensorType], number + 1);
        }
    }

    public IReadOnlyList<PlotLane> Lanes => Enum.GetValues(typeof(SensorType))
        .Cast<SensorType>()
        .SelectMany(type => GetLanes(type))
        .ToList();

    public IReadOnlyList<PlotLane> UserLanes => _userLanes;

    public IReadOnlyList<PlotLane> GetLanes(SensorType type)
    {
        var lanes = new List<PlotLane> { _defaultLanes[type] };
        lanes.AddRange(_userLanes.Where(lane => lane.SensorType == type));
        return lanes;
    }

    public PlotLane GetDefaultLane(SensorType type) => _defaultLanes[type];

    public bool TryGetLane(string key, out PlotLane lane)
    {
        if (key != null)
            return _lanesByKey.TryGetValue(key, out lane);

        lane = null;
        return false;
    }

    public PlotLane ResolveLane(SensorType type, string persistedKey)
    {
        if (persistedKey != null &&
            _lanesByKey.TryGetValue(persistedKey, out PlotLane lane) &&
            lane.SensorType == type)
        {
            return lane;
        }

        return _defaultLanes[type];
    }

    public PlotLane CreateLane(SensorType type, string name = null)
    {
        int number = _nextNumbers[type];
        _nextNumbers[type] = checked(number + 1);
        string key = type + "#" + number.ToString(CultureInfo.InvariantCulture);
        string defaultName = type + " " + number.ToString(CultureInfo.InvariantCulture);
        var lane = new PlotLane(key, SanitizeName(name, defaultName), type, 1, false);
        _userLanes.Add(lane);
        _lanesByKey.Add(key, lane);
        return lane;
    }

    public bool RenameLane(string key, string name)
    {
        if (!_lanesByKey.TryGetValue(key ?? string.Empty, out PlotLane lane) || lane.IsDefault)
            return false;

        lane.Name = SanitizeName(name, lane.Name);
        return true;
    }

    public bool RemoveLane(string key)
    {
        if (!_lanesByKey.TryGetValue(key ?? string.Empty, out PlotLane lane) || lane.IsDefault)
            return false;

        _lanesByKey.Remove(lane.Key);
        _userLanes.Remove(lane);
        return true;
    }

    public bool SetWeight(string key, int weight)
    {
        if (!_lanesByKey.TryGetValue(key ?? string.Empty, out PlotLane lane))
            return false;

        lane.Weight = ClampWeight(weight);
        return true;
    }

    public int GetNextNumber(SensorType type) => _nextNumbers[type];

    public IDictionary<string, double> CalculateStackedShares(IEnumerable<string> visibleKeys)
    {
        var visible = new HashSet<string>(visibleKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        List<PlotLane> lanes = Lanes.Where(lane => visible.Contains(lane.Key)).ToList();
        int totalWeight = lanes.Sum(lane => lane.Weight);
        return lanes.ToDictionary(
            lane => lane.Key,
            lane => totalWeight == 0 ? 0 : (double)lane.Weight / totalWeight,
            StringComparer.Ordinal);
    }

    public string Serialize()
    {
        return string.Join(";", _userLanes.Select(lane =>
            lane.Key + "=" + SanitizeName(lane.Name, lane.SensorType.ToString()) + ":" +
            ClampWeight(lane.Weight).ToString(CultureInfo.InvariantCulture)));
    }

    public static int ClampWeight(int weight) => Math.Max(1, Math.Min(3, weight));

    private void ParseUserLanes(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
            return;

        foreach (string entry in serialized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = entry.IndexOf('=');
            int colon = entry.LastIndexOf(':');
            if (equals <= 0 || colon <= equals + 1 || colon == entry.Length - 1)
                continue;

            string key = entry.Substring(0, equals).Trim();
            if (_lanesByKey.ContainsKey(key) || !TryParseUserLaneKey(key, out SensorType type, out int number))
                continue;

            if (!int.TryParse(entry.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                continue;

            string defaultName = type + " " + number.ToString(CultureInfo.InvariantCulture);
            string name = SanitizeName(entry.Substring(equals + 1, colon - equals - 1), defaultName);
            var lane = new PlotLane(key, name, type, ClampWeight(weight), false);
            _userLanes.Add(lane);
            _lanesByKey.Add(key, lane);
        }
    }

    private static bool TryParseUserLaneKey(string key, out SensorType type, out int number)
    {
        type = default;
        number = 0;
        int hash = key.LastIndexOf('#');
        if (hash <= 0 || hash == key.Length - 1 ||
            !Enum.TryParse(key.Substring(0, hash), false, out type) ||
            !Enum.IsDefined(typeof(SensorType), type) ||
            !int.TryParse(key.Substring(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        return number >= 2;
    }

    private static int GetLaneNumber(string key)
    {
        int hash = key.LastIndexOf('#');
        return int.Parse(key.Substring(hash + 1), CultureInfo.InvariantCulture);
    }

    private static string SanitizeName(string name, string fallback)
    {
        string sanitized = (name ?? string.Empty)
            .Replace(';', ' ')
            .Replace('=', ' ')
            .Replace(':', ' ')
            .Trim();
        return sanitized.Length == 0 ? fallback : sanitized;
    }
}
