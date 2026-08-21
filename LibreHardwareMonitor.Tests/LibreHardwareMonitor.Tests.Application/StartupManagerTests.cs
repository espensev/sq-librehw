// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class StartupManagerTests
{
    [Fact]
    public void ManagedInstall_HidesPortableStartupControl()
    {
        StartupManager manager = new(@"\SevGrp\AdminTask\LibreHW-No-UAC");

        Assert.False(manager.IsAvailable);
        Assert.False(manager.Startup);
    }

    [Fact]
    public void ManagedInstall_EnablingPortableStartupThrowsAndLeavesStateDisabled()
    {
        StartupManager manager = new(@"\SevGrp\AdminTask\LibreHW-No-UAC");

        Assert.Throws<InvalidOperationException>(() => manager.Startup = true);

        Assert.False(manager.IsAvailable);
        Assert.False(manager.Startup);
    }
}
