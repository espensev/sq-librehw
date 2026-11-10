// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using Xunit;

namespace LibreHardwareMonitor.Tests;

[CollectionDefinition("Graph menu presentation", DisableParallelization = true)]
public sealed class GraphMenuPresentationCollection { }

[Collection("Graph menu presentation")]
public sealed class GraphMenuPresentationTests
{
    [Theory]
    [InlineData(6, 0)] // IDYES
    [InlineData(7, 2)] // IDNO
    public void ClearHistory_RealConfirmationDefaultsToNoAndOnlyYesClears(int answer, int remaining)
    {
        InSta(() =>
        {
            using var owner = new Form { ShowInTaskbar = false, Opacity = 0 };
            _ = owner.Handle;
            var history = new List<int> { 10, 20 };
            int defaultButton = 0;
            bool sawDialog = false;
            using var timer = new System.Windows.Forms.Timer { Interval = 25 };
            timer.Tick += (_, _) =>
            {
                IntPtr dialog = IntPtr.Zero;
                EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
                {
                    if (GetDlgItem(window, 6) != IntPtr.Zero && GetDlgItem(window, 7) != IntPtr.Zero)
                        dialog = window;
                    return true;
                }, IntPtr.Zero);
                if (dialog == IntPtr.Zero)
                    return;
                timer.Stop();
                ShowWindow(dialog, 0);
                defaultButton = (int)(SendMessage(dialog, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt64() & 0xffff); // DM_GETDEFID
                sawDialog = true;
                SendMessage(dialog, 0x0111, new IntPtr(answer), IntPtr.Zero); // WM_COMMAND
            };
            timer.Start();
            MainForm.ConfirmClearGraphHistory(owner, history.Clear);
            Assert.True(sawDialog);
            Assert.Equal(7, defaultButton);
            Assert.Equal(remaining, history.Count);
        });
    }

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowCallback callback, IntPtr data);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void Prompts_KeepLabelsFieldsValidationAndButtonsInsideAtScaledFonts(int percent)
    {
        InSta(() =>
        {
            using var font = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size * percent / 100f);
            using var owner = new Form { Font = font, Bounds = new Rectangle(200, 160, 900, 700), ShowInTaskbar = false };
            using Form range = PlotRangePrompt.CreateDialog(owner, "Power Range", "W", 30, 140);
            ExerciseDialog(range, owner, () =>
            {
                Assert.Contains(Descendants(range).OfType<Label>(), label => label.Text == "Minimum (W):");
                Assert.Contains(Descendants(range).OfType<Label>(), label => label.Text == "Maximum (W):");
                TextBox minimum = Descendants(range).OfType<TextBox>().Single(control => control.Name == "minimum");
                minimum.Text = "not a number";
                ((Button)range.AcceptButton).PerformClick();
                Assert.Equal(DialogResult.None, range.DialogResult);
                Assert.False(string.IsNullOrEmpty(Descendants(range).Single(control => control.Name == "validation").Text));
                AssertLayout(range);
                minimum.Text = "30";
                ((Button)range.AcceptButton).PerformClick();
                Assert.Equal(DialogResult.OK, range.DialogResult);
                Assert.Equal((30d, 140d), Assert.IsType<ValueTuple<double, double>>(range.Tag));
            });

            using Form name = PlotLaneNamePrompt.CreateDialog(owner, "Rename Graph Lane", "CPU");
            ExerciseDialog(name, owner, () =>
            {
                AssertLayout(name);
                Descendants(name).OfType<TextBox>().Single().Text = "Cancelled change";
                ((Button)name.CancelButton).PerformClick();
                Assert.Equal(DialogResult.Cancel, name.DialogResult);
                Assert.Null(name.Tag);
            });
        });
    }

    [Theory]
    [InlineData(-1920, 0)]
    [InlineData(1920, -1080)]
    public void PromptPosition_CentersAndClampsWithinTheOwnersWorkingArea(int left, int top)
    {
        var work = new Rectangle(left, top, 1920, 1040);
        var size = new Size(640, 300);
        var owner = new Rectangle(left + 100, top + 100, 1000, 700);
        Assert.Equal(new Point(left + 280, top + 300), PlotPromptDialog.CenteredLocation(size, owner, work));
        owner = new Rectangle(left + 1800, top + 950, 500, 300);
        var positioned = new Rectangle(PlotPromptDialog.CenteredLocation(size, owner, work), size);
        Assert.True(work.Contains(positioned));
        Assert.Equal(work.Right, positioned.Right);
        Assert.Equal(work.Bottom, positioned.Bottom);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void StateColumn_IsPaintedForSubmenuParents(int percent)
    {
        using var font = new Font(SystemFonts.MenuFont.FontFamily, SystemFonts.MenuFont.Size * percent / 100f);
        using var menu = new ContextMenuStrip { Font = font, Renderer = new ThemedMenuRenderer() };
        var values = new ToolStripMenuItem("Values");
        ThemedMenuRenderer.SetState(values, "30 – 140 W");
        values.DropDownItems.Add("Auto Range");
        menu.Items.Add(values);
        menu.PerformLayout();
        menu.CreateControl();
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, menu.ClientRectangle);
        // Property assertions miss WinForms suppressing shortcut text on submenu parents.
        int statePixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = bitmap.Width / 2; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).ToArgb() == Theme.Current.MenuStateForegroundColor.ToArgb())
                statePixels++;
        Assert.True(statePixels > 10, "The right-hand state column was not painted.");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(48)]
    public void ChoiceAndToggleGlyphs_StayCenteredAndVisibleInEveryTheme(int rowHeight)
    {
        Theme original = Theme.Current;
        try
        {
            foreach (Theme theme in new Theme[] { new LightTheme(), new DarkTheme(), new BlackTheme() })
            {
                Theme.Current = theme;
                foreach (bool choice in new[] { false, true })
                foreach (bool enabled in new[] { false, true })
                {
                    using var item = choice ? ThemedMenuRenderer.Choice("Choice", true) : new ToolStripMenuItem("Toggle") { Checked = true };
                    item.AutoSize = false;
                    item.Size = new Size(200, rowHeight);
                    item.Enabled = enabled;
                    using var bitmap = new Bitmap(200, rowHeight);
                    using Graphics graphics = Graphics.FromImage(bitmap);
                    graphics.Clear(theme.MenuBackgroundColor);
                    var renderer = new ThemedMenuRenderer();
                    renderer.DrawItemCheck(new ToolStripItemImageRenderEventArgs(graphics, item, new Rectangle(5, 3, rowHeight - 6, rowHeight - 6)));
                    var painted = new List<Point>();
                    for (int y = 0; y < rowHeight; y++)
                    for (int x = 0; x < rowHeight + 5; x++)
                        if (bitmap.GetPixel(x, y).ToArgb() != theme.MenuBackgroundColor.ToArgb())
                            painted.Add(new Point(x, y));
                    Assert.NotEmpty(painted);
                    double center = (painted.Min(p => p.Y) + painted.Max(p => p.Y)) / 2.0;
                    Assert.InRange(center, rowHeight / 2.0 - 2, rowHeight / 2.0 + 2);
                    Assert.InRange(painted.Max(p => p.Y) - painted.Min(p => p.Y), rowHeight / 5, rowHeight - 4);
                    Assert.NotEqual(theme.MenuBackgroundColor, theme.MenuBorderColor);
                    Assert.NotEqual(theme.MenuBackgroundColor, theme.MenuHighlightColor);
                }
            }
        }
        finally
        {
            Theme.Current = original;
        }
    }

    private static void AssertLayout(Form dialog)
    {
        dialog.PerformLayout();
        foreach (Control control in Descendants(dialog).Where(control => control is Label || control is TextBox || control is Button))
        {
            Assert.True(control.Parent.ClientRectangle.Contains(control.Bounds), $"{control.Name}/{control.Text}: {control.Bounds} outside {control.Parent.ClientRectangle}");
            Rectangle onDialog = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            Assert.True(dialog.ClientRectangle.Contains(onDialog), $"{control.Text}: outside dialog");
            if (control is Label || control is Button)
            {
                Size preferred = control.GetPreferredSize(new Size(control.Width, 0));
                Assert.True(control.Height >= preferred.Height, $"{control.Text}: height {control.Height} below preferred {preferred.Height}");
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));

    private static void ExerciseDialog(Form dialog, IWin32Window owner, Action exercise)
    {
        Exception failure = null;
        dialog.Opacity = 0;
        dialog.Shown += (_, _) =>
        {
            try { exercise(); }
            catch (Exception exception) { failure = exception; }
            finally { dialog.Close(); }
        };
        dialog.ShowDialog(owner);
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void InSta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Prompt fixture did not finish.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
