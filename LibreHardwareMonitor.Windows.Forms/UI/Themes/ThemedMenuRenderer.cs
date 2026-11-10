// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI.Themes;

/// <summary>Fork menu presentation, without changing the upstream renderer or item tags.</summary>
internal sealed class ThemedMenuRenderer : ThemedToolStripRenderer
{
    private sealed class ChoiceMarker { }
    private static readonly ConditionalWeakTable<ToolStripMenuItem, ChoiceMarker> Choices = new();

    internal static void MarkChoice(ToolStripMenuItem item) => Choices.GetValue(item, _ => new ChoiceMarker());

    internal static bool IsChoice(ToolStripMenuItem item) => Choices.TryGetValue(item, out _);

    internal static ToolStripMenuItem Choice(string text, bool selected = false)
    {
        var item = new ToolStripMenuItem(text) { Checked = selected };
        MarkChoice(item);
        return item;
    }

    // WinForms measures this column even on submenu parents; the renderer paints those explicitly.
    internal static void SetState(ToolStripMenuItem item, string text)
    {
        item.ShortcutKeyDisplayString = text;
        item.AccessibleDescription = text;
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        float size = Math.Min(e.ImageRectangle.Width, e.Item.Height * 0.48f);
        float cx = e.ImageRectangle.Left + e.ImageRectangle.Width / 2f;
        float cy = e.Item.Height / 2f;
        GraphicsState state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = Foreground(e.Item);
        if (e.Item is ToolStripMenuItem item && IsChoice(item))
        {
            float diameter = size * 0.62f;
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, cx - diameter / 2, cy - diameter / 2, diameter, diameter);
        }
        else
        {
            using var pen = new Pen(color, Math.Max(1.5f, size / 6)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(cx - size * 0.38f, cy),
                new PointF(cx - size * 0.1f, cy + size * 0.27f),
                new PointF(cx + size * 0.4f, cy - size * 0.3f)
            });
        }
        e.Graphics.Restore(state);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        Color color = Foreground(e.Item);
        if (e.Item is ToolStripMenuItem item && e.Text == item.ShortcutKeyDisplayString && item.Enabled)
            color = Theme.Current.MenuStateForegroundColor;

        if (e.Item is MenuHeader)
        {
            using Font font = MenuHeader.HeaderFont(e.TextFont);
            TextRenderer.DrawText(e.Graphics, e.Text, font, e.TextRectangle, Theme.Current.MenuForegroundColor, e.TextFormat);
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
            // ToolStripMenuItem suppresses its native shortcut paint when it has children, but
            // ToolStripDropDownMenu still reserves the measured width. Use that same rectangle.
            if (e.Item is ToolStripMenuItem parent && parent.HasDropDownItems &&
                e.Text == parent.Text && !string.IsNullOrEmpty(parent.ShortcutKeyDisplayString))
            {
                TextFormatFlags alignment = parent.RightToLeft == RightToLeft.Yes ? TextFormatFlags.Left : TextFormatFlags.Right;
                TextRenderer.DrawText(e.Graphics, parent.ShortcutKeyDisplayString, e.TextFont, e.TextRectangle,
                    parent.Enabled ? Theme.Current.MenuStateForegroundColor : Theme.Current.MenuDisabledForegroundColor,
                    alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        using (var brush = new SolidBrush(Theme.Current.MenuBackgroundColor))
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        if (!e.Item.Enabled || !e.Item.Selected)
            return;

        float inset = Math.Max(2, e.Item.Height / 12f);
        RectangleF bounds = new(inset, 1, e.Item.Width - 2 * inset - 1, e.Item.Height - 3);
        float diameter = Math.Min(8 * e.Graphics.DpiX / 96, bounds.Height / 2);
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        GraphicsState state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(Theme.Current.MenuHighlightColor))
            e.Graphics.FillPath(brush, path);
        e.Graphics.Restore(state);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Current.MenuSeparatorColor);
        int inset = (int)Math.Round(8 * e.Graphics.DpiX / 96);
        if (e.Vertical)
            e.Graphics.DrawLine(pen, e.Item.Width / 2, inset, e.Item.Width / 2, e.Item.Height - inset);
        else
            e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        float cx = e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2f;
        float cy = e.ArrowRectangle.Top + e.ArrowRectangle.Height / 2f;
        float size = Math.Max(2.5f, e.Item.Font.Height * 0.18f);
        PointF Point(float x, float y) => e.Direction switch
        {
            ArrowDirection.Left => new PointF(cx - x, cy + y),
            ArrowDirection.Up => new PointF(cx + y, cy - x),
            ArrowDirection.Down => new PointF(cx + y, cy + x),
            _ => new PointF(cx + x, cy + y)
        };
        using var pen = new Pen(Foreground(e.Item), Math.Max(1.25f, e.Graphics.DpiX / 96)) { LineJoin = LineJoin.Round };
        GraphicsState state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[]
        {
            Point(-size / 2, -size),
            Point(size / 2, 0),
            Point(-size / 2, size)
        });
        e.Graphics.Restore(state);
    }

    private static Color Foreground(ToolStripItem item) => !item.Enabled
        ? Theme.Current.MenuDisabledForegroundColor
        : item.Selected ? Theme.Current.MenuSelectedForegroundColor : Theme.Current.MenuForegroundColor;
}

/// <summary>A readable context label which cannot become a keyboard or mouse command.</summary>
internal sealed class MenuHeader : ToolStripLabel
{
    internal MenuHeader(string text) : base(text) { AccessibleRole = AccessibleRole.StaticText; }

    internal static Font HeaderFont(Font font) => new("Segoe UI Semibold", font.Size, FontStyle.Regular, font.Unit);

    public override Size GetPreferredSize(Size constrainingSize)
    {
        Size preferred = base.GetPreferredSize(constrainingSize);
        using Font font = HeaderFont(Font);
        preferred.Width += Math.Max(0, TextRenderer.MeasureText(Text, font).Width - TextRenderer.MeasureText(Text, Font).Width);
        return preferred;
    }
}
