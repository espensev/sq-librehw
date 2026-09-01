// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Drawing;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Owns a scaled <see cref="Font"/> that has been applied to a <see cref="Control"/>.
/// </summary>
/// <remarks>
/// <see cref="Control.Font"/>'s setter compares fonts by value and keeps the instance it already
/// holds when the new one is equal. The naive "assign new, dispose previous" swap then disposes the
/// font the control is still using, and the next <see cref="Font.Height"/> read throws
/// <c>ArgumentException: Parameter is not valid</c>. That is exactly what the Text Size slider does
/// when a drag-pause commit is followed by the menu-close commit at the same percent.
/// </remarks>
internal static class OwnedFont
{
    /// <summary>
    /// Applies <paramref name="candidate"/> to <paramref name="target"/>, taking ownership of it and
    /// disposing the previously owned font. If the control keeps its current (value-equal) font, the
    /// candidate is disposed instead and <paramref name="owned"/> is left untouched.
    /// </summary>
    public static void Apply(Control target, Font candidate, ref Font owned)
    {
        target.Font = candidate;

        if (!ReferenceEquals(target.Font, candidate))
        {
            candidate.Dispose();
            return;
        }

        Font previous = owned;
        owned = candidate;

        if (!ReferenceEquals(previous, candidate))
            previous?.Dispose();
    }
}
