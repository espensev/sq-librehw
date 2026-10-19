// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class HardwareOptionFailurePresenterTests
{
    [Fact]
    public async Task FailedEnable_RestoresMenuAndSavedSettingOnDispatcher_WithoutRetry_ThenClickRetries()
    {
        int enables = 0;
        await using var harness = new Harness((option, value) =>
        {
            if (option == HardwareOptionKind.Cpu && value && ++enables == 1)
                throw new InvalidOperationException("injected enable failure");
        });
        await harness.InitializeAsync();

        harness.CpuMenu.PerformClick();
        await harness.WaitForIdleAsync();

        Assert.True(harness.Cpu.Value);
        Assert.True(harness.CpuMenu.Checked);
        Assert.Empty(harness.Errors);
        int dispatcherThread = Environment.CurrentManagedThreadId;
        harness.Dispatch();

        Assert.False(harness.Cpu.Value);
        Assert.False(harness.CpuMenu.Checked);
        Assert.False(harness.Settings.GetValue("cpu", true));
        harness.Settings.Save("unused");
        using var savedStream = new MemoryStream(harness.SavedSettings);
        var saved = XDocument.Load(savedStream);
        Assert.Equal("false", saved.Root.Element("appSettings").Element("add").Attribute("value").Value);
        Assert.Equal(new[] { (HardwareOptionKind.Cpu, dispatcherThread) }, harness.Errors);
        Assert.Equal(new[] { "Cpu=True" }, harness.Requests);
        Assert.Equal(new[] { "Cpu=True" }, harness.Applied);
        Assert.Equal(0, harness.Resets);

        harness.CpuMenu.PerformClick();
        await harness.WaitForIdleAsync();
        harness.Dispatch();

        Assert.True(harness.Cpu.Value);
        Assert.True(harness.CpuMenu.Checked);
        Assert.True(harness.Settings.GetValue("cpu", false));
        Assert.Equal(new[] { "Cpu=True", "Cpu=True" }, harness.Applied);
        Assert.Single(harness.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedEnable_DoesNotOverwriteNewerSameKeyIntent_AndOtherKeysStillDrain(bool newerValue)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int enables = 0;
        await using var harness = new Harness((option, value) =>
        {
            if (option == HardwareOptionKind.Cpu && value && ++enables == 1)
            {
                started.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                throw new InvalidOperationException("injected enable failure");
            }
        });
        await harness.InitializeAsync();

        harness.Cpu.Value = true;
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            harness.Cpu.Value = false;
            harness.Gpu.Value = true;
            harness.Cpu.Value = newerValue;
        }
        finally
        {
            release.Set();
        }

        await harness.WaitForIdleAsync();
        harness.Dispatch();

        Assert.Equal(newerValue, harness.Cpu.Value);
        Assert.Equal(newerValue, harness.CpuMenu.Checked);
        Assert.Equal(newerValue, harness.Settings.GetValue("cpu", !newerValue));
        Assert.True(harness.Gpu.Value);
        Assert.Equal(newerValue
            ? new[] { "Cpu=True", "Gpu=True", "Cpu=True" }
            : new[] { "Cpu=True", "Cpu=False", "Gpu=True" }, harness.Applied);
        Assert.Single(harness.Errors);
        Assert.Equal(newerValue ? 4 : 3, harness.Requests.Count);
        Assert.Equal(0, harness.Resets);
    }

    [Fact]
    public async Task FailedEnable_QueuedUiCallbackDoesNotOverwriteRequestMadeAfterFailure()
    {
        int enables = 0;
        await using var harness = new Harness((option, value) =>
        {
            if (option == HardwareOptionKind.Cpu && value && ++enables == 1)
                throw new InvalidOperationException("injected enable failure");
        });
        await harness.InitializeAsync();

        harness.Cpu.Value = true;
        await harness.WaitForIdleAsync();
        harness.Cpu.Value = false;
        harness.Cpu.Value = true;
        await harness.WaitForIdleAsync();
        harness.Dispatch();

        Assert.True(harness.Cpu.Value);
        Assert.True(harness.CpuMenu.Checked);
        Assert.True(harness.Settings.GetValue("cpu", false));
        Assert.Single(harness.Errors);
        Assert.Equal(3, harness.Requests.Count);
    }

    [Fact]
    public async Task FailedEnable_QueuedUiWorkIsDroppedAfterShutdown()
    {
        await using var harness = new Harness((_, value) =>
        {
            if (value)
                throw new InvalidOperationException("injected enable failure");
        });
        await harness.InitializeAsync();

        harness.Cpu.Value = true;
        await harness.WaitForIdleAsync();
        harness.Lifecycle.BeginStop();
        harness.Dispatch();

        Assert.True(harness.Cpu.Value);
        Assert.True(harness.CpuMenu.Checked);
        Assert.True(harness.Settings.GetValue("cpu", false));
        Assert.Empty(harness.Errors);
        Assert.Single(harness.Requests);
    }

    [Fact]
    public async Task FailedEnable_AfterShutdownDoesNotPostUiWork()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var harness = new Harness((_, value) =>
        {
            if (value)
            {
                started.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                throw new InvalidOperationException("injected enable failure");
            }
        });
        await harness.InitializeAsync();

        harness.Cpu.Value = true;
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            harness.Lifecycle.BeginStop();
        }
        finally
        {
            release.Set();
        }

        await harness.Lifecycle.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, harness.PendingUiWork);
        Assert.True(harness.Cpu.Value);
        Assert.Empty(harness.Errors);
        Assert.Single(harness.Requests);
    }

    [Fact]
    public async Task FailedDisable_DoesNotClaimEnableRollback()
    {
        bool runtime = false;
        await using var harness = new Harness((_, value) =>
        {
            if (runtime && !value)
                throw new InvalidOperationException("injected disable failure");
        });
        harness.Cpu.Value = true;
        await harness.InitializeAsync();
        runtime = true;

        harness.Cpu.Value = false;
        await harness.WaitForIdleAsync();
        harness.Dispatch();

        Assert.False(harness.Cpu.Value);
        Assert.False(harness.CpuMenu.Checked);
        Assert.False(harness.Settings.GetValue("cpu", true));
        Assert.Empty(harness.Errors);
        Assert.Equal(new[] { "Cpu=False" }, harness.Requests);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ConcurrentQueue<Action> _uiWork = new();
        private readonly ToolStripMenuItem _gpuMenu = new();

        internal Harness(Action<HardwareOptionKind, bool> apply)
        {
            Settings = new PersistentSettings((_, contents) => SavedSettings = contents);
            Cpu = new UserOption("cpu", false, CpuMenu, Settings);
            Gpu = new UserOption("gpu", false, _gpuMenu, Settings);
            Lifecycle = new ApplicationLifecycleCoordinator(
                _ => Task.CompletedTask,
                (option, value) =>
                {
                    Applied.Enqueue($"{option}={value}");
                    apply(option, value);
                },
                () => Resets++,
                () => Task.CompletedTask);
            var presenter = new HardwareOptionFailurePresenter(
                Lifecycle,
                option => option == HardwareOptionKind.Cpu ? Cpu : Gpu,
                _uiWork.Enqueue,
                option => Errors.Enqueue((option, Environment.CurrentManagedThreadId)));
            Lifecycle.OptionFailed += (option, value, sequence, _) => presenter.Report(option, value, sequence);
            Cpu.Changed += (_, _) => Request(HardwareOptionKind.Cpu, Cpu.Value);
            Gpu.Changed += (_, _) => Request(HardwareOptionKind.Gpu, Gpu.Value);
        }

        internal ApplicationLifecycleCoordinator Lifecycle { get; }
        internal PersistentSettings Settings { get; }
        internal ToolStripMenuItem CpuMenu { get; } = new();
        internal UserOption Cpu { get; }
        internal UserOption Gpu { get; }
        internal byte[] SavedSettings { get; private set; }
        internal ConcurrentQueue<string> Requests { get; } = new();
        internal ConcurrentQueue<string> Applied { get; } = new();
        internal ConcurrentQueue<(HardwareOptionKind, int)> Errors { get; } = new();
        internal int Resets { get; private set; }
        internal int PendingUiWork => _uiWork.Count;

        internal async Task InitializeAsync()
        {
            await Lifecycle.InitializeAsync();
            Requests.Clear();
            Applied.Clear();
        }

        internal void Dispatch()
        {
            while (_uiWork.TryDequeue(out Action action))
                action();
        }

        internal async Task WaitForIdleAsync()
        {
            var idle = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            void CheckIdle()
            {
                if (!Lifecycle.IsBusy)
                    idle.TrySetResult(null);
            }

            Lifecycle.StateChanged += CheckIdle;
            try
            {
                CheckIdle();
                await idle.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                Lifecycle.StateChanged -= CheckIdle;
            }
        }

        private void Request(HardwareOptionKind option, bool value)
        {
            Requests.Enqueue($"{option}={value}");
            Assert.True(Lifecycle.RequestOption(option, value));
        }

        public async ValueTask DisposeAsync()
        {
            await Lifecycle.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Lifecycle.Dispose();
            CpuMenu.Dispose();
            _gpuMenu.Dispose();
        }
    }
}
