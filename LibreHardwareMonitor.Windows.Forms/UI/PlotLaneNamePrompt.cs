// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;

namespace LibreHardwareMonitor.Windows.Forms.UI;

internal static class PlotLaneNamePrompt
{
    internal static bool TryShow(IWin32Window owner, string title, string initialValue, out string value)
    {
        using Form dialog = CreateDialog(owner, title, initialValue);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            value = null;
            return false;
        }

        value = (string)dialog.Tag;
        return true;
    }

    internal static Form CreateDialog(IWin32Window owner, string title, string initialValue)
    {
        var dialog = new PlotPromptDialog(owner, title);
        TextBox textBox = dialog.AddInput("Lane name:", "laneName", initialValue ?? string.Empty);
        Button ok = dialog.AddButtons();
        ok.Click += (sender, args) =>
        {
            dialog.Tag = textBox.Text;
            dialog.DialogResult = DialogResult.OK;
        };
        dialog.Shown += (sender, args) =>
        {
            textBox.SelectAll();
            textBox.Focus();
        };
        Theme.Current.Apply(dialog);
        return dialog;
    }
}
