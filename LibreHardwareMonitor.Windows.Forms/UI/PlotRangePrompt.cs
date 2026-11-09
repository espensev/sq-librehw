// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>Minimum/maximum prompt for a graph pane's fixed value range, in the current culture.</summary>
internal static class PlotRangePrompt
{
    internal static bool TryShow(IWin32Window owner, string title, double minimum, double maximum, out double rangeMinimum, out double rangeMaximum)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        double parsedMinimum = 0;
        double parsedMaximum = 0;
        (string minimumText, string maximumText) = FormatPrefill(minimum, maximum, culture);

        using Form dialog = new()
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 132),
            Font = SystemFonts.MessageBoxFont
        };
        using Label minimumLabel = new() { Text = "Minimum:", Left = 12, Top = 15, Width = 80 };
        using TextBox minimumBox = new() { Left = 96, Top = 12, Width = 252, Text = minimumText };
        using Label maximumLabel = new() { Text = "Maximum:", Left = 12, Top = 43, Width = 80 };
        using TextBox maximumBox = new() { Left = 96, Top = 40, Width = 252, Text = maximumText };
        using Label error = new() { Left = 12, Top = 70, Width = 336, Height = 20 };
        // OK validates first, so invalid input keeps the dialog open with the reason shown.
        using Button ok = new() { Text = "OK", Left = 192, Top = 96, Width = 75 };
        using Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 273, Top = 96, Width = 75 };
        dialog.Controls.AddRange(new Control[] { minimumLabel, minimumBox, maximumLabel, maximumBox, error, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        ok.Click += (sender, args) =>
        {
            if (TryParseRange(minimumBox.Text, maximumBox.Text, culture, out parsedMinimum, out parsedMaximum))
            {
                dialog.DialogResult = DialogResult.OK;
                return;
            }

            error.Text = "Enter two numbers with the maximum greater than the minimum.";
        };
        dialog.Shown += (sender, args) =>
        {
            minimumBox.SelectAll();
            minimumBox.Focus();
        };
        Theme.Current.Apply(dialog);

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            rangeMinimum = 0;
            rangeMaximum = 0;
            return false;
        }

        rangeMinimum = parsedMinimum;
        rangeMaximum = parsedMaximum;
        return true;
    }

    /// <summary>
    /// The fields' initial text: <c>G6</c>, or round-trip <c>R</c> when <c>G6</c> would not parse back to
    /// an increasing pair (a narrow range such as 123456.7 to 123456.8). A NaN or infinite bound
    /// leaves its field empty.
    /// </summary>
    internal static (string Minimum, string Maximum) FormatPrefill(double minimum, double maximum, CultureInfo culture)
    {
        string minimumText = FormatBound(minimum, "G6", culture);
        string maximumText = FormatBound(maximum, "G6", culture);
        if (minimumText.Length > 0 && maximumText.Length > 0 && !TryParseRange(minimumText, maximumText, culture, out _, out _))
            return (FormatBound(minimum, "R", culture), FormatBound(maximum, "R", culture));

        return (minimumText, maximumText);
    }

    private static string FormatBound(double value, string format, CultureInfo culture)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? string.Empty : value.ToString(format, culture);
    }

    /// <summary>
    /// Parses a finite, increasing range. Group separators are rejected rather than silently dropped,
    /// so "1.5" in a comma-decimal culture is an error instead of 15.
    /// </summary>
    internal static bool TryParseRange(string minimumText, string maximumText, CultureInfo culture, out double minimum, out double maximum)
    {
        if (TryParseFinite(minimumText, culture, out minimum) &&
            TryParseFinite(maximumText, culture, out maximum) &&
            maximum > minimum)
        {
            return true;
        }

        minimum = 0;
        maximum = 0;
        return false;
    }

    private static bool TryParseFinite(string text, CultureInfo culture, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, culture, out value) &&
               !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }
}
