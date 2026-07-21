// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Automation;
using Aga.Controls.Tree;
using Aga.Controls.Tree.NodeControls;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;
using ApartmentState = System.Threading.ApartmentState;
using ManualResetEventSlim = System.Threading.ManualResetEventSlim;
using Thread = System.Threading.Thread;

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
    public void ScrollbarThemes_KeepRestHoverAndPressedThumbsClearlyVisible()
    {
        Theme[] themes = Theme.All.ToArray();

        foreach (Theme theme in themes)
        {
            Assert.True(
                ContrastRatio(theme.ScrollbarBackground, theme.ScrollbarTrack) >= 3,
                $"{theme.DisplayName} resting scrollbar contrast is too low.");
            Assert.True(
                ContrastRatio(theme.ScrollbarBackground, theme.ScrollbarTrackHover) >= 3,
                $"{theme.DisplayName} hover scrollbar contrast is too low.");
            Assert.True(
                ContrastRatio(theme.ScrollbarBackground, theme.ScrollbarTrackPressed) >= 3,
                $"{theme.DisplayName} pressed scrollbar contrast is too low.");
        }
    }

    [Fact]
    public void ThemedScrollIndicators_MirrorNativeHitTargetsWithoutAddingATabStop()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        using var host = new SystemColorsPanel { Size = new Size(240, 180) };
        using var vertical = new VScrollBar
        {
            Bounds = new Rectangle(
                host.ClientSize.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                0,
                System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                host.ClientSize.Height - System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight)
        };
        using var horizontal = new HScrollBar
        {
            AccessibleName = "Existing horizontal label",
            Bounds = new Rectangle(
                0,
                host.ClientSize.Height - System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight,
                host.ClientSize.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight)
        };
        host.Controls.Add(vertical);
        host.Controls.Add(horizontal);

        ThemedVScrollIndicator.AddToControl(host);
        ThemedVScrollIndicator.AddToControl(host);
        ThemedHScrollIndicator.AddToControl(host);
        ThemedHScrollIndicator.AddToControl(host);

        ThemedVScrollIndicator verticalIndicator = host.Controls.OfType<ThemedVScrollIndicator>().Single();
        ThemedHScrollIndicator horizontalIndicator = host.Controls.OfType<ThemedHScrollIndicator>().Single();

        Assert.Equal(vertical.Bounds, verticalIndicator.Bounds);
        Assert.Equal(horizontal.Bounds, horizontalIndicator.Bounds);
        Assert.Equal(System.Windows.Forms.SystemInformation.VerticalScrollBarWidth, verticalIndicator.Width);
        Assert.Equal(System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight, horizontalIndicator.Height);
        Assert.False(verticalIndicator.TabStop);
        Assert.False(horizontalIndicator.TabStop);
        Assert.NotSame(vertical.AccessibilityObject, verticalIndicator.AccessibilityObject);
        Assert.NotSame(horizontal.AccessibilityObject, horizontalIndicator.AccessibilityObject);
        Assert.Equal(AccessibleRole.ScrollBar, vertical.AccessibleRole);
        Assert.Equal(AccessibleRole.ScrollBar, horizontal.AccessibleRole);
        Assert.Equal("Sensor list vertical scrollbar", vertical.AccessibleName);
        Assert.Equal("Existing horizontal label", horizontal.AccessibleName);
        Assert.Equal(AccessibleRole.ScrollBar, verticalIndicator.AccessibilityObject.Role);
        Assert.Equal(AccessibleRole.ScrollBar, horizontalIndicator.AccessibilityObject.Role);
        Assert.Equal(vertical.AccessibleName, verticalIndicator.AccessibilityObject.Name);
        Assert.Equal(horizontal.AccessibleName, horizontalIndicator.AccessibilityObject.Name);

        int indicatorInvalidations = 0;
        verticalIndicator.Invalidated += (_, _) => indicatorInvalidations++;
        _ = host.Handle;
        vertical.Maximum = 200;
        vertical.LargeChange = 20;
        host.Invalidate();
        Assert.True(indicatorInvalidations > 0);

        bool expectedThemedVisibility = vertical.Visible && !System.Windows.Forms.SystemInformation.HighContrast;
        verticalIndicator.Visible = !expectedThemedVisibility;
        host.RaiseSystemColorsChanged();
        Assert.Equal(expectedThemedVisibility, verticalIndicator.Visible);

        using var realTree = new TreeViewAdv { Size = new Size(240, 180) };
        VScrollBar treeVertical = realTree.Controls.OfType<VScrollBar>().Single();
        HScrollBar treeHorizontal = realTree.Controls.OfType<HScrollBar>().Single();
        Assert.Equal(System.Windows.Forms.SystemInformation.VerticalScrollBarWidth, treeVertical.Width);
        Assert.Equal(System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight, treeHorizontal.Height);

        ThemedVScrollIndicator.AddToControl(realTree);
        ThemedHScrollIndicator.AddToControl(realTree);
        Assert.Equal(
            treeVertical.Bounds,
            realTree.Controls.OfType<ThemedVScrollIndicator>().Single().Bounds);
        Assert.Equal(
            treeHorizontal.Bounds,
            realTree.Controls.OfType<ThemedHScrollIndicator>().Single().Bounds);
    }

    [Fact]
    public void ThemedScrollIndicators_PublishRangeValueAtTheirPaintedHitTargets()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !Environment.UserInteractive)
            return;

        using var ready = new ManualResetEventSlim();
        Form shownForm = null;
        VScrollBar nativeVertical = null;
        HScrollBar nativeHorizontal = null;
        System.Drawing.Point verticalPoint = default;
        System.Drawing.Point horizontalPoint = default;
        IntPtr verticalHandle = IntPtr.Zero;
        IntPtr horizontalHandle = IntPtr.Zero;
        Exception uiThreadFailure = null;

        var uiThread = new Thread(() =>
        {
            try
            {
                shownForm = new Form
                {
                    ClientSize = new Size(240, 180),
                    FormBorderStyle = FormBorderStyle.None,
                    Location = new System.Drawing.Point(20, 20),
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    TopMost = true
                };
                var host = new Panel { Dock = DockStyle.Fill };
                nativeVertical = new VScrollBar
                {
                    AccessibleName = "Sensor list vertical scrollbar",
                    Bounds = new Rectangle(
                        shownForm.ClientSize.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                        0,
                        System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                        shownForm.ClientSize.Height - System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight),
                    Minimum = 10,
                    Maximum = 109,
                    LargeChange = 10,
                    SmallChange = 2,
                    Value = 40
                };
                nativeHorizontal = new HScrollBar
                {
                    AccessibleName = "Sensor list horizontal scrollbar",
                    Bounds = new Rectangle(
                        0,
                        shownForm.ClientSize.Height - System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight,
                        shownForm.ClientSize.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                        System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight),
                    Minimum = 20,
                    Maximum = 219,
                    LargeChange = 20,
                    SmallChange = 5,
                    Value = 65
                };
                host.Controls.Add(nativeVertical);
                host.Controls.Add(nativeHorizontal);
                shownForm.Controls.Add(host);
                ThemedVScrollIndicator.AddToControl(host);
                ThemedHScrollIndicator.AddToControl(host);

                shownForm.Shown += (_, _) =>
                {
                    ThemedVScrollIndicator verticalIndicator = host.Controls.OfType<ThemedVScrollIndicator>().Single();
                    ThemedHScrollIndicator horizontalIndicator = host.Controls.OfType<ThemedHScrollIndicator>().Single();
                    verticalPoint = verticalIndicator.PointToScreen(
                        new System.Drawing.Point(verticalIndicator.Width / 2, verticalIndicator.Height / 2));
                    horizontalPoint = horizontalIndicator.PointToScreen(
                        new System.Drawing.Point(horizontalIndicator.Width / 2, horizontalIndicator.Height / 2));
                    verticalHandle = verticalIndicator.Handle;
                    horizontalHandle = horizontalIndicator.Handle;
                    ready.Set();
                };

                Application.Run(shownForm);
            }
            catch (Exception exception)
            {
                uiThreadFailure = exception;
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Scrollbar UI Automation test"
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for the scrollbar test window.");
            Assert.Null(uiThreadFailure);

            // Resolve the indicators via their HWNDs, not AutomationElement.FromPoint: screen
            // hit-testing intermittently resolves to invisible full-desktop overlay windows owned
            // by other processes (observed: mstsc's 'Input Capture Window'), which no retry can
            // outwait. The painted-hit-target claim is preserved by asserting each element's
            // BoundingRectangle contains the indicator's painted center point.
            AutomationElement verticalElement = GetAutomationElementFromHandle(verticalHandle);
            Assert.Equal(ControlType.ScrollBar, verticalElement.Current.ControlType);
            Assert.Equal("Sensor list vertical scrollbar", verticalElement.Current.Name);
            Assert.True(verticalElement.Current.BoundingRectangle.Contains(verticalPoint.X, verticalPoint.Y),
                $"Painted hit target {verticalPoint} outside UIA bounds {verticalElement.Current.BoundingRectangle}.");
            Assert.True(verticalElement.TryGetCurrentPattern(RangeValuePattern.Pattern, out object verticalPatternObject));
            var verticalPattern = (RangeValuePattern)verticalPatternObject;
            Assert.False(verticalPattern.Current.IsReadOnly);
            Assert.Equal(40, verticalPattern.Current.Value);
            verticalPattern.SetValue(55);
            AssertScrollBarValue(shownForm, nativeVertical, 55);

            AutomationElement horizontalElement = GetAutomationElementFromHandle(horizontalHandle);
            Assert.Equal(ControlType.ScrollBar, horizontalElement.Current.ControlType);
            Assert.Equal("Sensor list horizontal scrollbar", horizontalElement.Current.Name);
            Assert.True(horizontalElement.Current.BoundingRectangle.Contains(horizontalPoint.X, horizontalPoint.Y),
                $"Painted hit target {horizontalPoint} outside UIA bounds {horizontalElement.Current.BoundingRectangle}.");
            Assert.True(horizontalElement.TryGetCurrentPattern(RangeValuePattern.Pattern, out object horizontalPatternObject));
            var horizontalPattern = (RangeValuePattern)horizontalPatternObject;
            Assert.False(horizontalPattern.Current.IsReadOnly);
            Assert.Equal(65, horizontalPattern.Current.Value);
            horizontalPattern.SetValue(85);
            AssertScrollBarValue(shownForm, nativeHorizontal, 85);
        }
        finally
        {
            if (shownForm?.IsHandleCreated == true)
                shownForm.BeginInvoke(new Action(shownForm.Close));

            Assert.True(uiThread.Join(TimeSpan.FromSeconds(10)), "Timed out closing the scrollbar test window.");
            shownForm?.Dispose();
            nativeVertical?.Dispose();
            nativeHorizontal?.Dispose();
        }
    }

    [Fact]
    public void ScrollIndicatorGeometry_UsesEffectiveRangeAndKeepsAMinimumThumb()
    {
        using var scrollbar = new VScrollBar
        {
            Minimum = 0,
            Maximum = 999,
            LargeChange = 100,
            Value = 0
        };
        var client = new Rectangle(0, 0, System.Windows.Forms.SystemInformation.VerticalScrollBarWidth, 200);

        Rectangle topThumb = ScrollIndicatorGeometry.GetThumbBounds(
            scrollbar,
            client,
            Orientation.Vertical,
            3);
        Assert.Equal(ScrollIndicatorGeometry.TrackEndInset, topThumb.Top);
        Assert.True(topThumb.Height >= ScrollIndicatorGeometry.MinimumThumbLength);
        Assert.Equal(900, ScrollIndicatorGeometry.GetEffectiveMaximum(scrollbar));

        int travel = client.Height - (ScrollIndicatorGeometry.TrackEndInset * 2) - topThumb.Height;
        Assert.Equal(
            900,
            ScrollIndicatorGeometry.GetValueFromDrag(
                scrollbar,
                startValue: 0,
                delta: travel,
                trackLength: client.Height,
                thumbLength: topThumb.Height));
        Assert.Equal(
            450,
            ScrollIndicatorGeometry.GetValueFromDrag(
                scrollbar,
                startValue: 450,
                delta: 50,
                trackLength: topThumb.Height + (ScrollIndicatorGeometry.TrackEndInset * 2),
                thumbLength: topThumb.Height));

        scrollbar.Value = 900;
        Rectangle bottomThumb = ScrollIndicatorGeometry.GetThumbBounds(
            scrollbar,
            client,
            Orientation.Vertical,
            3);
        Assert.Equal(client.Bottom - ScrollIndicatorGeometry.TrackEndInset, bottomThumb.Bottom);

        scrollbar.Maximum = 99_999;
        scrollbar.LargeChange = 1;
        scrollbar.Value = 0;
        Rectangle minimumThumb = ScrollIndicatorGeometry.GetThumbBounds(
            scrollbar,
            client,
            Orientation.Vertical,
            3);
        Assert.Equal(ScrollIndicatorGeometry.MinimumThumbLength, minimumThumb.Height);

        scrollbar.Maximum = int.MaxValue;
        scrollbar.LargeChange = int.MaxValue / 2;
        Rectangle extremeRangeThumb = ScrollIndicatorGeometry.GetThumbBounds(
            scrollbar,
            client,
            Orientation.Vertical,
            3);
        Assert.InRange(
            extremeRangeThumb.Height,
            ScrollIndicatorGeometry.MinimumThumbLength,
            client.Height - (ScrollIndicatorGeometry.TrackEndInset * 2) - 1);
    }

    [Fact]
    public void ThemedVerticalScrollIndicator_PaintsAWideContrastingRestingThumb()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        Theme original = Theme.Current;
        try
        {
            Theme.Current = new DarkTheme();
            using var host = new Panel { Size = new Size(180, 200) };
            using var scrollbar = new VScrollBar
            {
                Bounds = new Rectangle(
                    host.ClientSize.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                    0,
                    System.Windows.Forms.SystemInformation.VerticalScrollBarWidth,
                    host.ClientSize.Height),
                Minimum = 0,
                Maximum = 999,
                LargeChange = 100,
                Value = 450
            };
            host.Controls.Add(scrollbar);
            using var indicator = new RenderableVScrollIndicator(scrollbar);
            using Bitmap bitmap = indicator.Render();

            Rectangle thumb = ScrollIndicatorGeometry.GetThumbBounds(
                scrollbar,
                indicator.ClientRectangle,
                Orientation.Vertical,
                3);
            int sampleY = thumb.Top + (thumb.Height / 2);
            int thumbPixels = Enumerable.Range(0, bitmap.Width)
                .Count(x => bitmap.GetPixel(x, sampleY).ToArgb() == Theme.Current.ScrollbarTrack.ToArgb());

            Assert.True(thumbPixels >= 8, $"Expected at least 8 visible thumb pixels, found {thumbPixels}.");
            Assert.Equal(Theme.Current.ScrollbarBackground.ToArgb(), bitmap.GetPixel(1, sampleY).ToArgb());
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

    private static AutomationElement GetAutomationElementFromHandle(IntPtr handle)
    {
        var timeout = Stopwatch.StartNew();
        Win32Exception lastTransientFailure = null;
        do
        {
            try
            {
                return AutomationElement.FromHandle(handle);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
            {
                lastTransientFailure = exception;
                Thread.Sleep(25);
            }
        }
        while (timeout.Elapsed < TimeSpan.FromSeconds(2));

        throw new InvalidOperationException(
            "Windows UI Automation repeatedly denied the scrollbar element lookup.",
            lastTransientFailure);
    }

    private static void AssertScrollBarValue(System.Windows.Forms.Control uiThreadOwner, ScrollBar scrollbar, int expected)
    {
        var timeout = Stopwatch.StartNew();
        int actual;
        do
        {
            actual = (int)uiThreadOwner.Invoke(new Func<int>(() => scrollbar.Value));
            if (actual == expected)
                return;

            Thread.Sleep(10);
        }
        while (timeout.Elapsed < TimeSpan.FromSeconds(2));

        Assert.Equal(expected, actual);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(color.R)) +
               (0.7152 * Linearize(color.G)) +
               (0.0722 * Linearize(color.B));
    }

    private sealed class RenderableVScrollIndicator : ThemedVScrollIndicator
    {
        public RenderableVScrollIndicator(VScrollBar scrollbar)
            : base(scrollbar)
        { }

        public Bitmap Render()
        {
            var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            using Graphics graphics = Graphics.FromImage(bitmap);
            OnPaint(new PaintEventArgs(graphics, ClientRectangle));
            return bitmap;
        }
    }

    private sealed class SystemColorsPanel : Panel
    {
        public void RaiseSystemColorsChanged()
        {
            OnSystemColorsChanged(EventArgs.Empty);
        }
    }

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
