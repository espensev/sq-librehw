// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Globalization;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Locks the CSV log sensor-value precision contract. The logger previously wrote every value with
/// the round-trip "R" specifier, which emits the shortest string reproducing the exact binary float
/// - far past what the hardware resolves (37.771072 °C, 1250.4471 RPM). Those trailing digits are
/// effectively random and compress badly: on a measured 424-column day they accounted for 42% of the
/// compressed archive. Values are now rounded per sensor unit before formatting. These tests pin:
/// (1) the two-place default, (2) the three-place Voltage/Current exception, (3) that rounding still
/// formats via "R" so large magnitudes keep the compact exponent form rather than expanding, (4)
/// culture-invariance, (5) negative-zero suppression, and (6) NaN/Infinity passthrough.
/// </summary>
public class CsvValuePrecisionContractTests
{
    [Theory]
    [InlineData(SensorType.Temperature, 2)]
    [InlineData(SensorType.Fan, 2)]
    [InlineData(SensorType.Load, 2)]
    [InlineData(SensorType.Power, 2)]
    [InlineData(SensorType.Clock, 2)]
    [InlineData(SensorType.Throughput, 2)]
    [InlineData(SensorType.Voltage, 3)]
    [InlineData(SensorType.Current, 3)]
    public void GetValueDecimals_KeepsThreePlacesOnlyForVoltageAndCurrent(SensorType sensorType, int expected)
    {
        Assert.Equal(expected, Logger.GetValueDecimals(sensorType));
    }

    [Fact]
    public void FormatRowValue_RoundsGeneralSensorsToTwoPlaces()
    {
        // Real values observed in a SND-HOST daily log under the previous "R" formatting.
        Assert.Equal("37.77", Logger.FormatRowValue(37.771072f, SensorType.Temperature));
        Assert.Equal("1250.45", Logger.FormatRowValue(1250.4471435546875f, SensorType.Fan));
    }

    [Fact]
    public void FormatRowValue_KeepsThirdPlaceForVoltageAndCurrent()
    {
        // Board rails sit in the 0.9-1.5 V range where the third decimal is a real distinction;
        // two places would quantise them visibly.
        Assert.Equal("0.934", Logger.FormatRowValue(0.9339999f, SensorType.Voltage));
        Assert.Equal("1.208", Logger.FormatRowValue(1.208f, SensorType.Voltage));
        Assert.Equal("1.436", Logger.FormatRowValue(1.4359999f, SensorType.Current));
    }

    [Fact]
    public void FormatRowValue_KeepsLargeMagnitudesInExponentForm()
    {
        // The reason rounding is followed by "R" rather than by "F2": a fixed-point specifier
        // expands these instead. This machine logs Throughput around 3.5e9, which "F2" would
        // write as 3497850112.00, and float.MaxValue would become a 41-character digit run.
        Assert.Equal("3.49785E+09", Logger.FormatRowValue(3.49785e9f, SensorType.Throughput));
        Assert.Equal("3.4028235E+38", Logger.FormatRowValue(float.MaxValue, SensorType.Data));
    }

    [Fact]
    public void FormatRowValue_CollapsesNegativeZero()
    {
        // A small negative reading rounds down to negative zero, which would otherwise write "-0".
        Assert.Equal("0", Logger.FormatRowValue(-0.000102321144f, SensorType.Load));
        Assert.Equal("0", Logger.FormatRowValue(-1e-9f, SensorType.Voltage));
    }

    [Fact]
    public void FormatRowValue_PreservesGenuineNegatives()
    {
        Assert.Equal("-40.5", Logger.FormatRowValue(-40.5f, SensorType.Temperature));
    }

    [Fact]
    public void FormatRowValue_PassesThroughNonFiniteValues()
    {
        Assert.Equal("NaN", Logger.FormatRowValue(float.NaN, SensorType.Temperature));
        Assert.Equal("Infinity", Logger.FormatRowValue(float.PositiveInfinity, SensorType.Temperature));
        Assert.Equal("-Infinity", Logger.FormatRowValue(float.NegativeInfinity, SensorType.Temperature));
    }

    [Fact]
    public void FormatRowValue_RoundsHalfToEven()
    {
        // Banker's rounding, matching the formatter's previous implicit behaviour.
        Assert.Equal("0.12", Logger.FormatRowValue(0.125f, SensorType.Load));
        Assert.Equal("0.14", Logger.FormatRowValue(0.135f, SensorType.Load));
    }

    [Fact]
    public void FormatRowValue_EmitsParseableInvariantText()
    {
        // Every written value must read back as a float under invariant parsing - the downstream
        // ThermalTrace parser depends on it.
        foreach (float sample in new[] { 37.771072f, 0.9339999f, -40.5f, 0f, 3.49785e9f })
        {
            string formatted = Logger.FormatRowValue(sample, SensorType.Temperature);

            Assert.True(float.TryParse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        }
    }

    [Fact]
    public void FormatRowValue_IsCultureInvariant()
    {
        // A machine in a comma-decimal locale must still produce the exact bytes the contract
        // specifies; the CSV separator is a comma.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("37.77", Logger.FormatRowValue(37.771072f, SensorType.Temperature));
            Assert.Equal("0.934", Logger.FormatRowValue(0.9339999f, SensorType.Voltage));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
