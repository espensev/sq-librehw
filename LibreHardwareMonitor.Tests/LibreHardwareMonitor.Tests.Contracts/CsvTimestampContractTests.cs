// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Globalization;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Locks the CSV log row-timestamp format (GH #9). The downstream ThermalTrace tracer correlates
/// these logs against millisecond-resolution HWiNFO data, so the timestamp column must carry
/// sub-second resolution; the legacy general "G" specifier emitted only whole seconds, collapsing
/// ~25% of faster-than-1 Hz samples onto a duplicate second and losing their ordering. These tests
/// pin: (1) the exact ".fff" layout, (2) that it still leads with the legacy second-resolution form
/// byte-for-byte (so a second-only consumer keeps parsing unchanged), (3) culture-invariance, (4)
/// round-trip parseability, and (5) that distinct sub-second instants stay distinct.
/// </summary>
public class CsvTimestampContractTests
{
    [Fact]
    public void FormatRowTimestamp_EmitsInvariantUsLayoutWithMilliseconds()
    {
        DateTime sample = new(2026, 5, 28, 16, 11, 57, 123, DateTimeKind.Local);

        Assert.Equal("05/28/2026 16:11:57.123", Logger.FormatRowTimestamp(sample));
    }

    [Fact]
    public void FormatRowTimestamp_ExtendsLegacySecondResolutionFormat()
    {
        // The fix only appends ".fff"; the leading fields must remain identical to the old "G" output
        // so a consumer that reads second-resolution timestamps keeps parsing unchanged.
        DateTime sample = new(2026, 12, 31, 23, 59, 59, 7, DateTimeKind.Local);

        string legacy = sample.ToString("G", CultureInfo.InvariantCulture);
        string formatted = Logger.FormatRowTimestamp(sample);

        Assert.StartsWith(legacy, formatted);
        Assert.Equal(legacy + ".007", formatted);
    }

    [Fact]
    public void FormatRowTimestamp_RoundTripsThroughParseExact()
    {
        DateTime sample = new(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified);

        string formatted = Logger.FormatRowTimestamp(sample);
        DateTime parsed = DateTime.ParseExact(formatted, Logger.RowTimestampFormat, CultureInfo.InvariantCulture);

        Assert.Equal(sample, parsed);
    }

    [Fact]
    public void FormatRowTimestamp_KeepsSubSecondSamplesDistinct()
    {
        // Two samples one millisecond apart collapsed to the same string under "G"; they must now be
        // distinguishable, which is the whole point of GH #9.
        DateTime first = new(2026, 5, 28, 16, 11, 57, 100, DateTimeKind.Local);
        DateTime second = first.AddMilliseconds(1);

        Assert.NotEqual(Logger.FormatRowTimestamp(first), Logger.FormatRowTimestamp(second));
    }

    [Fact]
    public void FormatRowTimestamp_IsCultureInvariant()
    {
        // The logger always writes invariant text; a machine in a comma-decimal / non-US-date locale
        // must still produce the exact bytes the contract specifies.
        DateTime sample = new(2026, 5, 28, 16, 11, 57, 123, DateTimeKind.Local);

        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("05/28/2026 16:11:57.123", Logger.FormatRowTimestamp(sample));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
