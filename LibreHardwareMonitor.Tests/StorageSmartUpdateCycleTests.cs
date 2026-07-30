// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Hardware.Storage;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class StorageSmartUpdateCycleTests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(10u)]
    [InlineData(25u)]
    [InlineData(50u)]
    [InlineData(100u)]
    public void AdvanceSmartUpdateCycle_RefreshesOnlyOnConfiguredCycle(uint cycleCount)
    {
        uint currentCycle = 0;

        for (uint cycle = 1; cycle < cycleCount; cycle++)
        {
            Assert.False(StorageDevice.AdvanceSmartUpdateCycle(ref currentCycle, cycleCount));
            Assert.Equal(cycle, currentCycle);
        }

        Assert.True(StorageDevice.AdvanceSmartUpdateCycle(ref currentCycle, cycleCount));
        Assert.Equal(0u, currentCycle);
    }

    [Fact]
    public void AdvanceSmartUpdateCycle_TreatsZeroAsEveryCycle()
    {
        uint currentCycle = 0;

        Assert.True(StorageDevice.AdvanceSmartUpdateCycle(ref currentCycle, 0));
        Assert.Equal(0u, currentCycle);
    }

    [Fact]
    public void AdvanceSmartUpdateCycle_RefreshesWhenConfiguredCountIsLowered()
    {
        uint currentCycle = 24;

        Assert.True(StorageDevice.AdvanceSmartUpdateCycle(ref currentCycle, 10));
        Assert.Equal(0u, currentCycle);
    }
}
