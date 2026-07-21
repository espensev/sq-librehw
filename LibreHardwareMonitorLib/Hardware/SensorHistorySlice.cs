// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// An immutable bounded result from <see cref="ISensorHistoryReader.ReadHistory" />.
/// </summary>
public sealed class SensorHistorySlice : IReadOnlyList<SensorValue>
{
    private readonly SensorValue[] _values;

    /// <summary>
    /// Initializes a history slice by taking an immutable copy of the supplied values.
    /// </summary>
    /// <param name="version">The history version represented by this slice.</param>
    /// <param name="resetRequired">
    /// <see langword="true" /> when a consumer must replace its existing history rather than append.
    /// </param>
    /// <param name="values">Values in oldest-to-newest chronological order.</param>
    public SensorHistorySlice(long version, bool resetRequired, IEnumerable<SensorValue> values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        List<SensorValue> copiedValues = new(values);
        Version = version;
        ResetRequired = resetRequired;
        _values = copiedValues.Count == 0 ? Array.Empty<SensorValue>() : copiedValues.ToArray();
    }

    /// <summary>
    /// Initializes a history slice over an array exclusively owned by the history reader.
    /// </summary>
    /// <param name="version">The history version represented by this slice.</param>
    /// <param name="resetRequired">Whether consumers must replace their existing history.</param>
    /// <param name="values">Values in oldest-to-newest chronological order.</param>
    /// <param name="takeOwnership">Whether the supplied array is exclusively owned by this slice.</param>
    internal SensorHistorySlice(long version, bool resetRequired, SensorValue[] values, bool takeOwnership)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        Version = version;
        ResetRequired = resetRequired;
        _values = values.Length == 0
            ? Array.Empty<SensorValue>()
            : (takeOwnership ? values : (SensorValue[])values.Clone());
    }

    /// <summary>
    /// Gets the history version represented by this slice.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// Gets whether a consumer must replace existing history with <see cref="Values" />.
    /// </summary>
    public bool ResetRequired { get; }

    /// <summary>
    /// Gets a non-null immutable list ordered from oldest to newest.
    /// </summary>
    public IReadOnlyList<SensorValue> Values => this;

    /// <inheritdoc />
    public int Count => _values.Length;

    /// <inheritdoc />
    public SensorValue this[int index] => _values[index];

    /// <inheritdoc />
    public IEnumerator<SensorValue> GetEnumerator()
    {
        return ((IEnumerable<SensorValue>)_values).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _values.GetEnumerator();
    }
}
