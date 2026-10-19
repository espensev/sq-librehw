// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using Xunit;
using PawnIoModule = LibreHardwareMonitor.PawnIo.PawnIo;

namespace LibreHardwareMonitor.Tests;

public sealed class PawnIoPlatformGateTests
{
    [Fact]
    public void FindInstalledVersion_OnNonWindows_ReturnsNullWithoutThrowing()
    {
        Assert.Null(PawnIoModule.FindInstalledVersion(isUnix: true));
    }

    [Fact]
    public void LoadModuleFromResource_OnNonWindows_ReturnsNotLoadedInstance()
    {
        PawnIoModule pawnIo = PawnIoModule.LoadModuleFromResource(typeof(PawnIoModule).Assembly,
                                                                  "missing-resource-must-not-be-read",
                                                                  isUnix: true);

        Assert.NotNull(pawnIo);
        Assert.False(pawnIo.IsLoaded);
        Assert.Equal(new long[] { 0, 0 }, pawnIo.Execute("fixture", [1], 2));
        long[] output = [17, 23];
        Assert.Equal(0, pawnIo.ExecuteHr("fixture", [1], 1, output, 2, out uint returned));
        Assert.Equal(0u, returned);
        Assert.Equal(new long[] { 17, 23 }, output);
        pawnIo.Close();
        pawnIo.Close();
        Assert.False(pawnIo.IsLoaded);
    }
}
