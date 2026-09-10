// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class PlotLanesTests
{
    [Fact]
    public void ResolveLane_UsesSameTypeLane_AndFallsBackForUnknownOrMismatchedKeys()
    {
        var lanes = new PlotLanes("Voltage#2=Vcore:1;Fan#2=Pump:1");

        Assert.Equal("Voltage#2", lanes.ResolveLane(SensorType.Voltage, "Voltage#2").Key);
        Assert.Equal("Voltage", lanes.ResolveLane(SensorType.Voltage, "Fan#2").Key);
        Assert.Equal("Voltage", lanes.ResolveLane(SensorType.Voltage, "Voltage#99").Key);
        Assert.Equal("Voltage", lanes.ResolveLane(SensorType.Voltage, null).Key);
    }

    [Fact]
    public void CreateAfterRemove_UsesMonotonicKeyAndNeverReusesMembershipKey()
    {
        var lanes = new PlotLanes();
        PlotLane removed = lanes.CreateLane(SensorType.Power);

        Assert.True(lanes.RemoveLane(removed.Key));
        PlotLane replacement = lanes.CreateLane(SensorType.Power);

        Assert.Equal("Power#2", removed.Key);
        Assert.Equal("Power#3", replacement.Key);
        Assert.Equal("Power", lanes.ResolveLane(SensorType.Power, removed.Key).Key);
    }

    [Fact]
    public void Parse_SkipsMalformedEntries_ClampsWeights_AndRepairsNextNumber()
    {
        var next = new Dictionary<SensorType, int> { [SensorType.Voltage] = 2 };
        var defaults = new Dictionary<SensorType, int> { [SensorType.Voltage] = 9 };
        var lanes = new PlotLanes(
            "bad;Voltage#4=Core:9;Missing#2=Nope:1;Voltage#5=NoWeight:x;Fan#2=Pump:0",
            next,
            defaults);

        Assert.Equal(3, lanes.GetDefaultLane(SensorType.Voltage).Weight);
        Assert.Equal(3, lanes.ResolveLane(SensorType.Voltage, "Voltage#4").Weight);
        Assert.Equal(1, lanes.ResolveLane(SensorType.Fan, "Fan#2").Weight);
        Assert.Equal(5, lanes.GetNextNumber(SensorType.Voltage));
        Assert.Equal("Voltage#5", lanes.CreateLane(SensorType.Voltage).Key);
    }

    [Fact]
    public void Serialize_RoundTripsCreationOrderNamesAndWeights()
    {
        var lanes = new PlotLanes();
        PlotLane voltage = lanes.CreateLane(SensorType.Voltage, " Core:=; Rail ");
        PlotLane fan = lanes.CreateLane(SensorType.Fan, "Pump");
        lanes.SetWeight(voltage.Key, 3);
        lanes.SetWeight(fan.Key, 2);

        string serialized = lanes.Serialize();
        var restored = new PlotLanes(serialized);

        Assert.Equal(new[] { "Voltage#2", "Fan#2" }, restored.UserLanes.Select(lane => lane.Key));
        Assert.Equal("Core    Rail", restored.UserLanes[0].Name);
        Assert.Equal(3, restored.UserLanes[0].Weight);
        Assert.Equal(2, restored.UserLanes[1].Weight);
    }

    [Fact]
    public void CalculateStackedShares_UsesOnlyVisibleLaneWeights()
    {
        var defaults = new Dictionary<SensorType, int>
        {
            [SensorType.Voltage] = 3,
            [SensorType.Fan] = 2
        };
        var lanes = new PlotLanes(defaultWeights: defaults);
        PlotLane extra = lanes.CreateLane(SensorType.Voltage);
        lanes.SetWeight(extra.Key, 1);

        IDictionary<string, double> shares = lanes.CalculateStackedShares(
            new[] { "Voltage", "Voltage#2", "Fan" });

        Assert.Equal(0.5, shares["Voltage"]);
        Assert.Equal(1.0 / 6.0, shares["Voltage#2"], 10);
        Assert.Equal(1.0 / 3.0, shares["Fan"], 10);
    }

    [Fact]
    public void DefaultLane_CannotBeRenamedOrRemoved_ButWeightCanChange()
    {
        var lanes = new PlotLanes();

        Assert.False(lanes.RenameLane("Voltage", "Rail"));
        Assert.False(lanes.RemoveLane("Voltage"));
        Assert.True(lanes.SetWeight("Voltage", 2));
        Assert.Equal("Voltage", lanes.GetDefaultLane(SensorType.Voltage).Name);
        Assert.Equal(2, lanes.GetDefaultLane(SensorType.Voltage).Weight);
    }
}
