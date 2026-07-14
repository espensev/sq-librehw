// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aga.Controls.Tree;
using Aga.Controls.Tree.NodeControls;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WinFormsUiLifetimeCollection
{
    public const string Name = "WinForms UI lifetime";
}

[Collection(WinFormsUiLifetimeCollection.Name)]
public sealed class WinFormsUiLifetimeTests
{
    [Fact]
    public void HardwareNode_CreatesOnlyPopulatedGroups_AndDetachesSensorEventsOnDispose()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var hardware = new TestHardware(settings);
        using var node = new HardwareNode(hardware, settings, unitManager);

        Assert.Equal(0, node.MaterializedTypeNodeCount);
        Assert.Empty(node.Nodes);

        Sensor sensor = hardware.CreateAndActivateSensor(SensorType.Temperature);
        Assert.Equal(1, node.MaterializedTypeNodeCount);
        Assert.Single(node.Nodes);
        Assert.Single(node.Nodes[0].Nodes);

        hardware.Deactivate(sensor);
        Assert.Empty(node.Nodes);

        node.Dispose();
        hardware.CreateAndActivateSensor(SensorType.Load);
        Assert.Empty(node.Nodes);
    }

    [Fact]
    public void TypeNodes_ShareBoundedEmbeddedImages()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var firstHardware = new TestHardware(settings, "first");
        var secondHardware = new TestHardware(settings, "second");

        firstHardware.CreateAndActivateSensor(SensorType.Temperature);
        secondHardware.CreateAndActivateSensor(SensorType.Temperature);

        using var first = new HardwareNode(firstHardware, settings, unitManager);
        using var second = new HardwareNode(secondHardware, settings, unitManager);

        Assert.Same(first.Nodes[0].Image, second.Nodes[0].Image);
    }

    [Fact]
    public void GadgetResize_ReleasesReplacedDibSections()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        using var window = new GadgetWindow();
        window.Paint = (_, _) => { };
        window.Visible = true;
        window.Redraw();

        int baseline = GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjects);
        for (int i = 0; i < 100; i++)
        {
            window.Size = new Size(140 + (i % 11), 90 + (i % 7));
            window.Redraw();
        }

        int afterResize = GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjects);
        Assert.InRange(afterResize - baseline, -4, 8);

        window.Dispose();
        Assert.Equal(IntPtr.Zero, window.Handle);
    }

    [Fact]
    public void TreeViewDispose_DetachesStaticExpandingIconEvent()
    {
        FieldInfo eventField = typeof(ExpandingIcon).GetField(
            "IconChanged",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(eventField);

        int before = ((MulticastDelegate)eventField.GetValue(null))?.GetInvocationList().Length ?? 0;
        var tree = new TreeViewAdv();
        int whileAlive = ((MulticastDelegate)eventField.GetValue(null))?.GetInvocationList().Length ?? 0;
        Assert.Equal(before + 1, whileAlive);

        tree.Dispose();
        int after = ((MulticastDelegate)eventField.GetValue(null))?.GetInvocationList().Length ?? 0;
        Assert.Equal(before, after);
    }

    [Fact]
    public void RepeatedThemeChanges_KeepStaticDrawingResourcesBounded()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        Theme original = Theme.Current;
        try
        {
            int baseline = GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjects);
            for (int i = 0; i < 100; i++)
                Theme.Current = i % 2 == 0 ? new LightTheme() : new DarkTheme();

            int afterChanges = GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjects);
            Assert.InRange(afterChanges - baseline, -4, 4);
        }
        finally
        {
            Theme.Current = original;
        }
    }

    [Fact]
    public void TrayRetryExhaustion_AllowsALaterShellEventToStartAFreshBoundedWindow()
    {
        Type implementationType = typeof(NotifyIconAdv).GetNestedType(
            "NotifyIconWindowsImplementation",
            BindingFlags.NonPublic);
        Assert.NotNull(implementationType);

        using var implementation = (IDisposable)Activator.CreateInstance(implementationType, nonPublic: true);
        FieldInfo retryAttempts = implementationType.GetField("_retryAttempts", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo retryShow = implementationType.GetField("_retryShow", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo retryTimer = implementationType.GetField("_retryTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo scheduleRetry = implementationType.GetMethod("ScheduleRetry", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(retryAttempts);
        Assert.NotNull(retryShow);
        Assert.NotNull(retryTimer);
        Assert.NotNull(scheduleRetry);

        retryAttempts.SetValue(implementation, 40);
        retryShow.SetValue(implementation, true);
        scheduleRetry.Invoke(implementation, new object[] { true });
        Assert.Equal(0, retryAttempts.GetValue(implementation));
        Assert.False(((Timer)retryTimer.GetValue(implementation)).Enabled);

        scheduleRetry.Invoke(implementation, new object[] { true });
        Assert.Equal(1, retryAttempts.GetValue(implementation));
        Assert.True(((Timer)retryTimer.GetValue(implementation)).Enabled);
    }

    private const int GdiObjects = 0;

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);

    private sealed class TestHardware : HardwareBase
    {
        private int _sensorIndex;

        public TestHardware(ISettings settings, string identifier = "test")
            : base("Test Hardware", new Identifier(identifier), settings)
        { }

        public override HardwareType HardwareType => HardwareType.Cpu;

        public Sensor CreateAndActivateSensor(SensorType sensorType)
        {
            var sensor = new Sensor(
                sensorType + " Sensor",
                _sensorIndex++,
                false,
                sensorType,
                this,
                Array.Empty<ParameterDescription>(),
                _settings);
            ActivateSensor(sensor);
            return sensor;
        }

        public void Deactivate(ISensor sensor)
        {
            DeactivateSensor(sensor);
        }

        public override void Update()
        { }
    }
}
