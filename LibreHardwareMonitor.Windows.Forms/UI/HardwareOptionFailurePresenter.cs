// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;

namespace LibreHardwareMonitor.Windows.Forms.UI;

internal sealed class HardwareOptionFailurePresenter
{
    private readonly ApplicationLifecycleCoordinator _lifecycle;
    private readonly Func<HardwareOptionKind, UserOption> _getOption;
    private readonly Action<Action> _dispatch;
    private readonly Action<HardwareOptionKind> _showError;

    internal HardwareOptionFailurePresenter(
        ApplicationLifecycleCoordinator lifecycle,
        Func<HardwareOptionKind, UserOption> getOption,
        Action<Action> dispatch,
        Action<HardwareOptionKind> showError)
    {
        _lifecycle = lifecycle;
        _getOption = getOption;
        _dispatch = dispatch;
        _showError = showError;
    }

    internal void Report(HardwareOptionKind option, bool value, long sequence)
    {
        // Disable recovery is separate: a failed removal may already have closed devices.
        if (!value || _lifecycle.IsStopping)
            return;

        _dispatch(() =>
        {
            if (_lifecycle.IsStopping)
                return;

            // Recheck at dispatch time; another request can arrive while this callback waits.
            if (_lifecycle.IsLatestOptionRequest(option, sequence))
                _getOption(option).SetValueWithoutNotification(false);

            _showError(option);
        });
    }
}
