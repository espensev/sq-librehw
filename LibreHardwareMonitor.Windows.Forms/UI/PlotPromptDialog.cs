// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>Content-sized graph prompts, positioned on the owning window's screen.</summary>
internal sealed class PlotPromptDialog : Form
{
    private readonly IWin32Window _owner;
    private readonly TableLayoutPanel _layout;

    internal PlotPromptDialog(IWin32Window owner, string title)
    {
        _owner = owner;
        Text = title;
        Font = (owner as Control)?.Font ?? SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        int padding = Math.Max(8, Font.Height / 2);
        Padding = new Padding(padding);
        _layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_layout);
    }

    internal TextBox AddInput(string label, string name, string text)
    {
        int row = _layout.RowCount++;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label { AutoSize = true, Text = label, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 6) };
        var field = new TextBox { Name = name, Text = text, Dock = DockStyle.Fill, MinimumSize = new Size(Font.Height * 16, 0), Margin = new Padding(3, 6, 3, 6) };
        _layout.Controls.Add(caption, 0, row);
        _layout.Controls.Add(field, 1, row);
        return field;
    }

    internal Label AddValidation()
    {
        var label = new Label
        {
            Name = "validation", AutoSize = true, MinimumSize = new Size(0, Font.Height),
            MaximumSize = new Size(Font.Height * 28, 0), Margin = new Padding(3, 6, 3, 6)
        };
        AddWide(label);
        return label;
    }

    internal Button AddButtons()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false };
        var cancel = new Button { Name = "cancel", Text = "Cancel", AutoSize = true, MinimumSize = new Size(Font.Height * 5, 0), DialogResult = DialogResult.Cancel };
        var ok = new Button { Name = "ok", Text = "OK", AutoSize = true, MinimumSize = new Size(Font.Height * 5, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AddWide(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        return ok;
    }

    private void AddWide(Control control)
    {
        int row = _layout.RowCount++;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.Controls.Add(control, 0, row);
        _layout.SetColumnSpan(control, 2);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PlaceOnOwnerScreen();
    }

    protected override void OnShown(EventArgs e)
    {
        PlaceOnOwnerScreen();
        base.OnShown(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Visible)
            PlaceOnOwnerScreen();
    }

    private void PlaceOnOwnerScreen()
    {
        Screen screen = _owner == null ? Screen.FromControl(this) : Screen.FromHandle(_owner.Handle);
        Rectangle ownerBounds = _owner is Control ownerControl
            ? ownerControl.RectangleToScreen(ownerControl.ClientRectangle)
            : screen.WorkingArea;
        Location = CenteredLocation(Size, ownerBounds, screen.WorkingArea);
    }

    internal static Point CenteredLocation(Size size, Rectangle owner, Rectangle workingArea) => new(
        Math.Max(workingArea.Left, Math.Min(owner.Left + (owner.Width - size.Width) / 2, workingArea.Right - size.Width)),
        Math.Max(workingArea.Top, Math.Min(owner.Top + (owner.Height - size.Height) / 2, workingArea.Bottom - size.Height)));
}
