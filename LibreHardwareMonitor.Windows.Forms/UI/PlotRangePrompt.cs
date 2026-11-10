// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Globalization;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>Minimum/maximum prompt for a graph pane's fixed value range, in the current culture.</summary>
internal static class PlotRangePrompt
{
    internal static bool TryShow(IWin32Window owner, string title, string unit, double minimum, double maximum, out double rangeMinimum, out double rangeMaximum)
    {
        using Form dialog = CreateDialog(owner, title, unit, minimum, maximum);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            rangeMinimum = 0;
            rangeMaximum = 0;
            return false;
        }

        (rangeMinimum, rangeMaximum) = ((double, double))dialog.Tag;
        return true;
    }

    internal static Form CreateDialog(IWin32Window owner, string title, string unit, double minimum, double maximum)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        (string minimumText, string maximumText) = FormatPrefill(minimum, maximum, culture);
        var dialog = new PlotPromptDialog(owner, title);
        string suffix = string.IsNullOrEmpty(unit) ? ":" : " (" + unit + "):";
        TextBox minimumBox = dialog.AddInput("Minimum" + suffix, "minimum", minimumText);
        TextBox maximumBox = dialog.AddInput("Maximum" + suffix, "maximum", maximumText);
        Label error = dialog.AddValidation();
        // OK validates first, so invalid input keeps the dialog open with the reason shown.
        Button ok = dialog.AddButtons();
        ok.Click += (sender, args) =>
        {
            if (TryParseRange(minimumBox.Text, maximumBox.Text, culture, out double parsedMinimum, out double parsedMaximum))
            {
                dialog.Tag = (parsedMinimum, parsedMaximum);
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
        return dialog;
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
