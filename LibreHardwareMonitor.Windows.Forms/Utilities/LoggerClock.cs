// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Diagnostics;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

internal interface ILoggerClock
{
    TimeSpan Elapsed { get; }
    DateTime LocalNow { get; }
    void RefreshTimeZone();
}

internal sealed class SystemLoggerClock : ILoggerClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public DateTime LocalNow => DateTime.Now;
    public void RefreshTimeZone() => TimeZoneInfo.ClearCachedData();
}
