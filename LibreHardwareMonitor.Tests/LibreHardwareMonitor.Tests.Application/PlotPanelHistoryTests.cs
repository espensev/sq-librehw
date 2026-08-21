// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using OxyPlot;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class PlotPanelHistoryTests
{
    private static readonly DateTime TimeOrigin = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReaderAppend_UsesOnlyDeltaAndPreservesChronologicalSessionGap()
    {
        ReaderSensor sensor = new(SensorType.Load);
        sensor.SetSlice
        (
            3,
            false,
            new SensorValue(10, TimeOrigin.AddMinutes(-1)),
            new SensorValue(float.NaN, TimeOrigin),
            new SensorValue(20, TimeOrigin.AddSeconds(2))
        );
        PlotPanelHistoryStore store = CreateStore(sensor);

        store.Synchronize(TemperatureUnit.Celsius, false);

        PlotPanelSeriesState state = GetState(store, sensor);
        Assert.Equal(3, state.Points.Count);
        Assert.Equal(new[] { -60d, 0d, 2d }, state.Points.Select(point => point.X));
        Assert.Equal(10d, state.Points[0].Y);
        Assert.True(double.IsNaN(state.Points[1].Y));
        Assert.Equal(20d, state.Points[2].Y);
        Assert.Equal((0L, PlotPanelHistoryStore.MaxPlotHistoryValues), sensor.ReadRequests[0]);

        DataPoint[] existingPoints = state.Points.ToArray();
        sensor.SetSlice(4, false, new SensorValue(30, TimeOrigin.AddSeconds(3)));

        store.Synchronize(TemperatureUnit.Celsius, false);

        Assert.Equal(4, state.Points.Count);
        Assert.Equal((3L, PlotPanelHistoryStore.MaxPlotHistoryValues), sensor.ReadRequests[1]);
        for (int i = 0; i < existingPoints.Length; i++)
        {
            Assert.Equal(existingPoints[i].X, state.Points[i].X);
            if (double.IsNaN(existingPoints[i].Y))
                Assert.True(double.IsNaN(state.Points[i].Y));
            else
                Assert.Equal(existingPoints[i].Y, state.Points[i].Y);
        }

        Assert.Equal(3d, state.Points[3].X);
        Assert.Equal(30d, state.Points[3].Y);
        Assert.Equal(0, sensor.ValuesAccessCount);

        store.Synchronize(TemperatureUnit.Celsius, false);

        Assert.Equal(2, sensor.ReadRequests.Count);
        Assert.Equal(4, state.Points.Count);
    }

    [Fact]
    public void ReaderVersionChangeWithResetRequired_RebuildsOnceWithoutNewPoint()
    {
        ReaderSensor sensor = new(SensorType.Load);
        SensorValue retained = new(20, TimeOrigin.AddSeconds(2));
        sensor.SetSlice
        (
            2,
            false,
            new SensorValue(10, TimeOrigin.AddSeconds(1)),
            retained
        );
        PlotPanelHistoryStore store = CreateStore(sensor);
        store.Synchronize(TemperatureUnit.Celsius, false);

        // Quiet expiry and history decimation both advance the version and set ResetRequired;
        // neither needs a newly appended point to invalidate the materialized plot tail.
        sensor.SetSlice(3, true, retained);

        store.Synchronize(TemperatureUnit.Celsius, false);

        PlotPanelSeriesState state = GetState(store, sensor);
        Assert.Single(state.Points);
        Assert.Equal(2d, state.Points[0].X);
        Assert.Equal(20d, state.Points[0].Y);
        Assert.Equal(2, sensor.ReadRequests.Count);
        Assert.Equal((2L, PlotPanelHistoryStore.MaxPlotHistoryValues), sensor.ReadRequests[1]);
        Assert.Equal(3, state.LastHistoryVersion);
        Assert.Equal(0, sensor.ValuesAccessCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemperatureUnitChange_ForcesOneBoundedRebuildRegardlessOfReaderReset(bool resetRequired)
    {
        ReaderSensor sensor = new(SensorType.Temperature);
        SensorValue freezing = new(0, TimeOrigin.AddSeconds(1));
        SensorValue boiling = new(100, TimeOrigin.AddSeconds(2));
        sensor.SetSlice(2, false, freezing, boiling);
        PlotPanelHistoryStore store = CreateStore(sensor);
        store.Synchronize(TemperatureUnit.Celsius, false);

        sensor.SetSlice(2, resetRequired, freezing, boiling);

        store.Synchronize(TemperatureUnit.Fahrenheit, true);

        PlotPanelSeriesState state = GetState(store, sensor);
        Assert.Equal(2, state.Points.Count);
        Assert.Equal(32d, state.Points[0].Y, 5);
        Assert.Equal(212d, state.Points[1].Y, 5);
        Assert.Equal(2, sensor.ReadRequests.Count);
        Assert.Equal((0L, PlotPanelHistoryStore.MaxPlotHistoryValues), sensor.ReadRequests[1]);
        Assert.Equal(0, sensor.ValuesAccessCount);

        store.Synchronize(TemperatureUnit.Fahrenheit, false);

        Assert.Equal(2, sensor.ReadRequests.Count);
        Assert.Equal(2, state.Points.Count);
    }

    [Fact]
    public void TemperatureRateUnitChangeScalesWithoutApplyingFahrenheitOffset()
    {
        ReaderSensor sensor = new(SensorType.TemperatureRate);
        SensorValue first = new(0.5f, TimeOrigin.AddSeconds(1));
        SensorValue second = new(1f, TimeOrigin.AddSeconds(2));
        sensor.SetSlice(2, false, first, second);
        PlotPanelHistoryStore store = CreateStore(sensor);
        store.Synchronize(TemperatureUnit.Celsius, false);

        sensor.SetSlice(2, false, first, second);
        store.Synchronize(TemperatureUnit.Fahrenheit, true);

        PlotPanelSeriesState state = GetState(store, sensor);
        Assert.Equal(0.9d, state.Points[0].Y, 5);
        Assert.Equal(1.8d, state.Points[1].Y, 5);
        Assert.Equal((0L, PlotPanelHistoryStore.MaxPlotHistoryValues), sensor.ReadRequests[1]);
    }

    [Fact]
    public void NonReaderFallback_KeepsSnapshotReuseAndTemperatureConversionBehavior()
    {
        FallbackSensor sensor = new(SensorType.Temperature);
        CountingValues initial = new
        (
            new SensorValue(0, TimeOrigin.AddSeconds(1)),
            new SensorValue(100, TimeOrigin.AddSeconds(2))
        );
        sensor.CurrentValues = initial;
        PlotPanelHistoryStore store = CreateStore(sensor);

        store.Synchronize(TemperatureUnit.Celsius, false);
        store.Synchronize(TemperatureUnit.Celsius, false);

        PlotPanelSeriesState state = GetState(store, sensor);
        Assert.Equal(2, state.Points.Count);
        Assert.Equal(1, initial.EnumerationCount);
        Assert.Equal(2, sensor.ValuesAccessCount);

        CountingValues replacement = new(new SensorValue(10, TimeOrigin.AddSeconds(3)));
        sensor.CurrentValues = replacement;
        store.Synchronize(TemperatureUnit.Celsius, false);

        Assert.Single(state.Points);
        Assert.Equal(10d, state.Points[0].Y);
        Assert.Equal(1, replacement.EnumerationCount);

        store.Synchronize(TemperatureUnit.Fahrenheit, true);

        Assert.Single(state.Points);
        Assert.Equal(50d, state.Points[0].Y, 5);
        Assert.Equal(2, replacement.EnumerationCount);
        Assert.Equal(4, sensor.ValuesAccessCount);
    }

    [Fact]
    public void RemovedAndReaddedReader_DiscardsVersionAndSnapshotState()
    {
        ReaderSensor sensor = new(SensorType.Load);
        sensor.SetSlice(7, false, new SensorValue(42, TimeOrigin.AddSeconds(1)));
        PlotPanelHistoryStore store = CreateStore(sensor);
        store.Synchronize(TemperatureUnit.Celsius, false);
        PlotPanelSeriesState originalState = GetState(store, sensor);

        store.RetainSensors(Array.Empty<ISensor>());

        Assert.False(store.TryGetState(sensor, out _));

        PlotPanelSeriesState readdedState = store.GetOrCreateState(sensor);
        store.Synchronize(TemperatureUnit.Celsius, false);

        Assert.NotSame(originalState, readdedState);
        Assert.Single(readdedState.Points);
        Assert.Equal(7, readdedState.LastHistoryVersion);
        Assert.Equal(2, sensor.ReadRequests.Count);
        Assert.Equal(0L, sensor.ReadRequests[1].SinceVersion);
        Assert.Equal(PlotPanelHistoryStore.MaxPlotHistoryValues, sensor.ReadRequests[1].MaxValues);
    }

    private static PlotPanelHistoryStore CreateStore(ISensor sensor)
    {
        PlotPanelHistoryStore store = new(TimeOrigin);
        store.GetOrCreateState(sensor);
        return store;
    }

    private static PlotPanelSeriesState GetState(PlotPanelHistoryStore store, ISensor sensor)
    {
        Assert.True(store.TryGetState(sensor, out PlotPanelSeriesState state));
        return state;
    }

    private abstract class FakeSensor : ISensor
    {
        protected FakeSensor(SensorType sensorType)
        {
            SensorType = sensorType;
            Identifier = new Identifier("plot-panel-test", sensorType.ToString().ToLowerInvariant());
        }

        public IControl Control => null;
        public IHardware Hardware => null;
        public Identifier Identifier { get; }
        public int Index => 0;
        public bool IsDefaultHidden => false;
        public float? Max => null;
        public float? Min => null;
        public string Name { get; set; } = "Test";
        public IReadOnlyList<IParameter> Parameters => Array.Empty<IParameter>();
        public SensorType SensorType { get; }
        public float? Value => null;
        public abstract IEnumerable<SensorValue> Values { get; }
        public TimeSpan ValuesTimeWindow { get; set; }

        public void ResetMin() { }

        public void ResetMax() { }

        public void ClearValues() { }

        public void Accept(IVisitor visitor) => visitor.VisitSensor(this);

        public void Traverse(IVisitor visitor) { }
    }

    private sealed class ReaderSensor : FakeSensor, ISensorHistoryReader
    {
        private SensorValue[] _slice = Array.Empty<SensorValue>();
        private bool _resetRequired;

        public ReaderSensor(SensorType sensorType) : base(sensorType) { }

        public long HistoryVersion { get; private set; }

        public int ValuesAccessCount { get; private set; }

        public List<(long SinceVersion, int MaxValues)> ReadRequests { get; } = new();

        public override IEnumerable<SensorValue> Values
        {
            get
            {
                ValuesAccessCount++;
                throw new InvalidOperationException("The incremental plot path must not enumerate ISensor.Values.");
            }
        }

        public SensorHistorySlice ReadHistory(long sinceVersion, int maxValues)
        {
            ReadRequests.Add((sinceVersion, maxValues));
            return new SensorHistorySlice(HistoryVersion, _resetRequired, _slice);
        }

        public void SetSlice(long version, bool resetRequired, params SensorValue[] values)
        {
            HistoryVersion = version;
            _resetRequired = resetRequired;
            _slice = values;
        }
    }

    private sealed class FallbackSensor : FakeSensor
    {
        public FallbackSensor(SensorType sensorType) : base(sensorType) { }

        public CountingValues CurrentValues { get; set; } = new();

        public int ValuesAccessCount { get; private set; }

        public override IEnumerable<SensorValue> Values
        {
            get
            {
                ValuesAccessCount++;
                return CurrentValues;
            }
        }
    }

    private sealed class CountingValues : IEnumerable<SensorValue>
    {
        private readonly SensorValue[] _values;

        public CountingValues(params SensorValue[] values)
        {
            _values = values;
        }

        public int EnumerationCount { get; private set; }

        public IEnumerator<SensorValue> GetEnumerator()
        {
            EnumerationCount++;
            return ((IEnumerable<SensorValue>)_values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
