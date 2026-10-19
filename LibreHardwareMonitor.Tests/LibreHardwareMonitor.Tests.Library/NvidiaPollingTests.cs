// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Interop;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

public sealed class NvidiaPollingTests
{
    [Fact]
    public void NvmlPoll_ReacquiresStablePciIdentityAfterDeviceLoss()
    {
        int call = 0;
        NvidiaML.NvmlDevice? Lookup(string identity)
        {
            Assert.Equal("0000:20:00.0", identity);
            call++;
            return call == 2 ? null : Device(call);
        }

        NvidiaML.NvmlDevice? Poll() => NvidiaGpu.ResolveNvmlDevice(
            "0000:20:00.0", true, 0x20, 0, false, Lookup,
            _ => throw new InvalidOperationException("A known PCI identity must not fall back to an index."),
            _ => throw new InvalidOperationException("The stable identity needs no index validation."));

        Assert.Equal(new IntPtr(1), Poll().Value.Handle);
        Assert.Null(Poll());
        Assert.Equal(new IntPtr(3), Poll().Value.Handle);
        Assert.Equal(3, call);
    }

    [Theory]
    [InlineData(0x20u, true)]
    [InlineData(0x21u, false)]
    public void NvmlIndexFallback_RejectsAnotherGpuAfterIndexReordering(uint indexedBus, bool matches)
    {
        NvidiaML.NvmlDevice? device = NvidiaGpu.ResolveNvmlDevice(
            null, true, 0x20, 2, false,
            identity => { Assert.Equal("0000:20:00.0", identity); return null; },
            index => { Assert.Equal(2, index); return Device(4); },
            _ => new NvidiaML.NvmlPciInfo { bus = indexedBus });

        Assert.Equal(matches, device.HasValue);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void NvmlUnknownIdentity_UsesIndexOnlyDuringInitialDiscovery(bool discovery, int expectedCalls)
    {
        int indexCalls = 0;
        NvidiaML.NvmlDevice? device = NvidiaGpu.ResolveNvmlDevice(
            null, false, 0, 1, discovery,
            _ => throw new InvalidOperationException("No bus identity is known."),
            _ => { indexCalls++; return Device(1); },
            _ => null);

        Assert.Equal(expectedCalls, indexCalls);
        Assert.Equal(discovery, device.HasValue);
    }

    [Fact]
    public void NvmlFinalLeaseRelease_WaitsForReadLockAndPreservesOtherGroupLease()
    {
        int shutdownCount = 0;
        var owner = new NvidiaML.LeaseOwner(() => true, () => Interlocked.Increment(ref shutdownCount));
        Assert.True(owner.Acquire());
        Assert.True(owner.Acquire());
        owner.Release();
        Assert.Equal(1, owner.LeaseCount);
        Assert.Equal(0, shutdownCount);

        using var releaseStarted = new ManualResetEventSlim();
        using var releaseFinished = new ManualResetEventSlim();
        var release = new Thread(() =>
        {
            releaseStarted.Set();
            owner.Release();
            releaseFinished.Set();
        }) { IsBackground = true };
        lock (owner.SyncRoot)
        {
            release.Start();
            Assert.True(releaseStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(releaseFinished.Wait(TimeSpan.FromMilliseconds(100)));
            Assert.Equal(0, shutdownCount);
        }

        Assert.True(release.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, shutdownCount);
        Assert.Equal(0, owner.LeaseCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PawnUnavailable_ClearsPriorChannelsAndKeepsFreshNvApiFallback(bool acquired)
    {
        float?[] values = [92, 96, null];
        int reads = 0;
        int releases = 0;
        float? pawnTemperature = NvidiaGpu.ReadPawnTemperatures(values,
            () => { reads++; values[0] = 120; return false; },
            () => acquired,
            () => releases++);

        Assert.Null(pawnTemperature);
        Assert.All(values, value => Assert.Null(value));
        Assert.Equal(acquired ? 1 : 0, reads);
        Assert.Equal(acquired ? 1 : 0, releases);

        var fixture = new ThermalFixture();
        fixture.Publish(70, pawnTemperature);
        Assert.Equal(70f, fixture.Temperature.Value);
        Assert.Equal(1, fixture.Rate.SampleCount);
    }

    [Fact]
    public void PawnSuccess_PublishesMaximumAndObservesOnlyFinalSampleOncePerPoll()
    {
        var fixture = new ThermalFixture();
        float?[] values = [100, 110, 120];
        for (int i = 0; i < 3; i++)
        {
            int sample = i;
            float? maximum = NvidiaGpu.ReadPawnTemperatures(values,
                () => { values[0] = 78 + 2 * sample; values[1] = 80 + 2 * sample; return true; },
                () => true, () => { });
            fixture.Publish(60, maximum);
            fixture.Now = fixture.Now.AddSeconds(1);
        }

        Assert.Null(values[2]);
        Assert.Equal(84f, fixture.Temperature.Value);
        Assert.Equal(3, fixture.Rate.SampleCount);
        Assert.Equal(2f, fixture.Rate.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0f)]
    public void NoFreshHotSpot_ClearsPublishedValueAndRateWindow(float? nvApi)
    {
        var fixture = new ThermalFixture();
        for (int i = 0; i < 3; i++)
        {
            fixture.Publish(70 + i, null);
            fixture.Now = fixture.Now.AddSeconds(1);
        }

        Assert.NotNull(fixture.Rate.Value);
        fixture.Publish(nvApi, null);
        Assert.Null(fixture.Temperature.Value);
        Assert.Null(fixture.Rate.Value);
        Assert.Equal(0, fixture.Rate.SampleCount);
    }

    [Fact]
    public void PawnReadThrows_ReleasesBusAndDiscardsPartialValues()
    {
        float?[] values = [80, 90];
        int releases = 0;
        Assert.Throws<InvalidOperationException>(() => NvidiaGpu.ReadPawnTemperatures(values,
            () => { values[0] = 100; throw new InvalidOperationException(); },
            () => true, () => releases++));

        Assert.Equal(1, releases);
        Assert.All(values, value => Assert.Null(value));
    }

    private static NvidiaML.NvmlDevice Device(int value) => new() { Handle = new IntPtr(value) };

    private sealed class ThermalFixture
    {
        internal DateTime Now = new(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        internal Sensor Temperature { get; }
        internal TemperatureRateSensor Rate { get; }

        internal ThermalFixture()
        {
            var settings = new TestSettings();
            var hardware = new TestHardware(settings);
            Temperature = new Sensor("GPU Hot Spot", 1, SensorType.Temperature, hardware, settings);
            Rate = new TemperatureRateSensor("GPU Hot Spot Rate", 0, hardware, settings, () => Now);
        }

        internal void Publish(float? nvApi, float? pawn) =>
            NvidiaGpu.PublishHotSpotTemperature(nvApi, pawn, Temperature, Rate);
    }

    private sealed class TestHardware(ISettings settings) : HardwareBase("Test", new Identifier("test-gpu"), settings)
    {
        public override HardwareType HardwareType => HardwareType.GpuNvidia;
        public override void Update() { }
    }

    private sealed class TestSettings : ISettings
    {
        public bool Contains(string name) => false;
        public void SetValue(string name, string value) { }
        public string GetValue(string name, string value) => value;
        public void Remove(string name) { }
    }
}
