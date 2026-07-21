// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Provides optional bounded and incremental access to a sensor's recorded values.
/// </summary>
/// <remarks>
/// This interface is intentionally separate from <see cref="ISensor" /> so third-party sensor
/// implementations remain source and binary compatible. Implementations must increase
/// <see cref="HistoryVersion" /> monotonically for their lifetime whenever visible history changes.
/// Reads must return a coherent immutable snapshot when sensor updates occur concurrently.
/// </remarks>
public interface ISensorHistoryReader
{
    /// <summary>
    /// Gets the current monotonically increasing history version.
    /// </summary>
    long HistoryVersion { get; }

    /// <summary>
    /// Reads at most <paramref name="maxValues" /> values relative to a previously observed version.
    /// </summary>
    /// <param name="sinceVersion">
    /// The <see cref="SensorHistorySlice.Version" /> returned by the caller's previous read, or zero
    /// for an initial read.
    /// </param>
    /// <param name="maxValues">The positive maximum number of values to return.</param>
    /// <returns>
    /// A chronologically ordered slice. When <see cref="SensorHistorySlice.ResetRequired" /> is
    /// <see langword="false" />, the values can be appended to state held for
    /// <paramref name="sinceVersion" />. When it is <see langword="true" />, that state must be
    /// replaced by the returned bounded tail.
    /// </returns>
    SensorHistorySlice ReadHistory(long sinceVersion, int maxValues);
}
