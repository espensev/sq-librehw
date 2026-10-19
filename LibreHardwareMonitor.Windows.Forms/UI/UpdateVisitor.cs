// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public class UpdateVisitor : IVisitor
{
    private readonly Action<IHardware, Exception> _reportFailure;

    public UpdateVisitor()
        : this((hardware, exception) => Debug.WriteLine($"Hardware update failed for {hardware.Identifier}: {exception}"))
    { }

    internal UpdateVisitor(Action<IHardware, Exception> reportFailure)
    {
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    public void VisitComputer(IComputer computer)
    {
        computer.Traverse(this);
    }

    public void VisitHardware(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            try
            {
                if (hardware is Hardware.Hardware device)
                    device.InvalidateSensorValues();
            }
            catch (Exception invalidationFailure) when (IsRecoverable(invalidationFailure))
            {
                ReportFailure(hardware, invalidationFailure);
            }

            ReportFailure(hardware, exception);
        }

        foreach (IHardware subHardware in hardware.SubHardware)
            subHardware.Accept(this);
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private void ReportFailure(IHardware hardware, Exception exception)
    {
        try
        {
            _reportFailure(hardware, exception);
        }
        catch (Exception reportingFailure) when (IsRecoverable(reportingFailure))
        {
            // Diagnostics must not turn one failed device into a failed sweep.
        }
    }

    public void VisitSensor(ISensor sensor) { }

    public void VisitParameter(IParameter parameter) { }
}
