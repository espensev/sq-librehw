// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Locks the CSV log sensor-value precision contract. The logger previously wrote every value with
/// the round-trip "R" specifier, which emits the shortest string reproducing the exact binary float
/// - far past what the hardware resolves (37.771072 °C, 1250.4471 RPM). Those trailing digits are
/// effectively random and compress badly. Values are now rounded per sensor unit before formatting,
/// with a four-significant-digit floor so genuine small Data, Load and TemperatureRate readings do
/// not become zero. These tests pin: (1) an exhaustive minimum-precision map for every current
/// SensorType, (2) the significant-digit floor, (3) lossless fallback for future types and values
/// beneath Math.Round's decimal limit, (4) compact exponent formatting, (5) culture invariance,
/// (6) genuine negative-zero suppression, and (7) NaN/Infinity passthrough.
/// </summary>
public class CsvValuePrecisionContractTests
{
    [Theory]
    [InlineData(SensorType.Voltage, 3)]
    [InlineData(SensorType.Current, 3)]
    [InlineData(SensorType.Factor, 3)]
    [InlineData(SensorType.Timing, 3)]
    [InlineData(SensorType.Temperature, 2)]
    [InlineData(SensorType.Fan, 2)]
    [InlineData(SensorType.Load, 2)]
    [InlineData(SensorType.Power, 2)]
    [InlineData(SensorType.Clock, 2)]
    [InlineData(SensorType.Data, 2)]
    [InlineData(SensorType.TemperatureRate, 2)]
    [InlineData(SensorType.Throughput, 2)]
    public void GetMinimumValueDecimals_UsesTheExpectedUnitProfile(SensorType sensorType, int expected)
    {
        Assert.Equal(expected, Logger.GetMinimumValueDecimals(sensorType));
    }

    [Fact]
    public void GetMinimumValueDecimals_ExplicitlyClassifiesEveryCurrentSensorType()
    {
        SensorType[] sensorTypes = (SensorType[])System.Enum.GetValues(typeof(SensorType));

        Assert.Equal(22, sensorTypes.Length);
        Assert.All(sensorTypes, sensorType => Assert.NotNull(Logger.GetMinimumValueDecimals(sensorType)));
        Assert.Equal(4, sensorTypes.Count(sensorType => Logger.GetMinimumValueDecimals(sensorType) == 3));
        Assert.Equal(18, sensorTypes.Count(sensorType => Logger.GetMinimumValueDecimals(sensorType) == 2));
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
    public void FormatRowValue_PreservesSmallScaledAndRateTelemetry()
    {
        // These are representative live SND-HOST values. A fixed two-decimal policy turned each
        // into zero even though Data is expressed in GB and TemperatureRate is a derived signal.
        Assert.Equal("0.001763", Logger.FormatRowValue(0.001763016f, SensorType.Data));
        Assert.Equal("0.0003937", Logger.FormatRowValue(0.00039373524f, SensorType.Load));
        Assert.Equal("0.004987", Logger.FormatRowValue(0.00498691f, SensorType.TemperatureRate));
        Assert.Equal("-0.0001023", Logger.FormatRowValue(-0.000102321144f, SensorType.Load));
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
    public void FormatRowValue_CollapsesOnlyGenuineNegativeZero()
    {
        Assert.Equal("0", Logger.FormatRowValue(-0f, SensorType.Load));
        Assert.Equal("0", Logger.FormatRowValue(-0f, (SensorType)int.MaxValue));
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
        // Exact binary halves at a magnitude where the four-significant-digit floor still permits
        // two-decimal rounding.
        Assert.Equal("12.12", Logger.FormatRowValue(12.125f, SensorType.Load));
        Assert.Equal("12.38", Logger.FormatRowValue(12.375f, SensorType.Load));
    }

    [Fact]
    public void FormatRowValue_FallsBackLosslesslyBelowTheDecimalRoundingLimit()
    {
        // 6e-16 rounds to a non-zero 1e-15 when capped at 15 decimal places, so a zero-only
        // fallback would silently introduce about 67% error. Both signs and a much smaller value
        // prove that the decision is based on required precision, not only on a zero result.
        foreach (float tiny in new[] { 6e-16f, -6e-16f, 1e-25f })
        {
            Assert.Null(Logger.GetValueDecimals(tiny, SensorType.Load));
            Assert.Equal(
                tiny.ToString("R", CultureInfo.InvariantCulture),
                Logger.FormatRowValue(tiny, SensorType.Load));
        }
    }

    [Fact]
    public void FormatRowValue_StaysWithinTheFourDigitErrorBoundAtDecadeEdges()
    {
        // Exercise both sides of the 15-decimal fallback boundary and carry-producing values near
        // 0.1 and 10. Four-significant-digit rounding has at most about 0.05% relative error;
        // lossless fallback is tighter still.
        foreach (float value in new[] { 1e-12f, 1e-13f, 0.099995f, 9.9995f })
        {
            string formatted = Logger.FormatRowValue(value, SensorType.Load);
            float parsed = float.Parse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture);
            double relativeError = Math.Abs(((double)parsed - value) / value);

            Assert.NotEqual(0f, parsed);
            Assert.True(
                relativeError <= 0.00051,
                $"{value:R} became {formatted} ({relativeError:P6} relative error)");
        }
    }

    [Fact]
    public void FormatRowValue_PreservesUnknownFutureSensorTypesLosslessly()
    {
        const float value = 37.771072f;

        Assert.Null(Logger.GetMinimumValueDecimals((SensorType)int.MaxValue));
        Assert.Equal(
            value.ToString("R", CultureInfo.InvariantCulture),
            Logger.FormatRowValue(value, (SensorType)int.MaxValue));
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
