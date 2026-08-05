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
/// Verifies unit-specific CSV rounding, the significant-digit floor, invariant formatting, and
/// lossless fallbacks.
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
        Assert.Equal("37.77", Logger.FormatRowValue(37.771072f, SensorType.Temperature));
        Assert.Equal("1250.45", Logger.FormatRowValue(1250.4471435546875f, SensorType.Fan));
    }

    [Fact]
    public void FormatRowValue_KeepsThirdPlaceForVoltageAndCurrent()
    {
        Assert.Equal("0.934", Logger.FormatRowValue(0.9339999f, SensorType.Voltage));
        Assert.Equal("1.208", Logger.FormatRowValue(1.208f, SensorType.Voltage));
        Assert.Equal("1.436", Logger.FormatRowValue(1.4359999f, SensorType.Current));
    }

    [Fact]
    public void FormatRowValue_PreservesSmallScaledAndRateTelemetry()
    {
        Assert.Equal("0.001763", Logger.FormatRowValue(0.001763016f, SensorType.Data));
        Assert.Equal("0.0003937", Logger.FormatRowValue(0.00039373524f, SensorType.Load));
        Assert.Equal("0.004987", Logger.FormatRowValue(0.00498691f, SensorType.TemperatureRate));
        Assert.Equal("-0.0001023", Logger.FormatRowValue(-0.000102321144f, SensorType.Load));
    }

    [Fact]
    public void FormatRowValue_KeepsLargeMagnitudesInExponentForm()
    {
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
        Assert.Equal("12.12", Logger.FormatRowValue(12.125f, SensorType.Load));
        Assert.Equal("12.38", Logger.FormatRowValue(12.375f, SensorType.Load));
    }

    [Fact]
    public void FormatRowValue_FallsBackLosslesslyBelowTheDecimalRoundingLimit()
    {
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
        foreach (float sample in new[] { 37.771072f, 0.9339999f, -40.5f, 0f, 3.49785e9f })
        {
            string formatted = Logger.FormatRowValue(sample, SensorType.Temperature);

            Assert.True(float.TryParse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        }
    }

    [Fact]
    public void FormatRowValue_IsCultureInvariant()
    {
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
