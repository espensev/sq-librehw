// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;

namespace LibreHardwareMonitor.Windows.Forms.UI;

internal static class PlotLaneNamePrompt
{
    internal static bool TryShow(IWin32Window owner, string title, string initialValue, out string value)
    {
        using Form dialog = new()
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 92),
            Font = SystemFonts.MessageBoxFont
        };
        using TextBox textBox = new() { Left = 12, Top = 12, Width = 336, Text = initialValue ?? string.Empty };
        using Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, Left = 192, Top = 50, Width = 75 };
        using Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 273, Top = 50, Width = 75 };
        dialog.Controls.AddRange(new Control[] { textBox, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (sender, args) =>
        {
            textBox.SelectAll();
            textBox.Focus();
        };
        Theme.Current.Apply(dialog);

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            value = null;
            return false;
        }

        value = textBox.Text;
        return true;
    }
}
