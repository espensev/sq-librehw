// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Hardware.Motherboard.Lpc;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class Nct677XFanConfigTests
{
    [Fact]
    public void TryNct6687DrFanConfigUpdate_StartTimeoutStopsWithoutApplying()
    {
        int startCount = 0;
        int applyCount = 0;
        int completeCount = 0;

        bool succeeded = Nct677X.TryNct6687DrFanConfigUpdate(
            () =>
            {
                startCount++;
                return false;
            },
            () => applyCount++,
            () =>
            {
                completeCount++;
                return true;
            });

        Assert.False(succeeded);
        Assert.Equal(1, startCount);
        Assert.Equal(0, applyCount);
        Assert.Equal(0, completeCount);
    }

    [Fact]
    public void TryNct6687DrFanConfigUpdate_AllCommitsRejectedReturnsFalseAfterThreeAttempts()
    {
        int startCount = 0;
        int applyCount = 0;
        int completeCount = 0;

        bool succeeded = Nct677X.TryNct6687DrFanConfigUpdate(
            () =>
            {
                startCount++;
                return true;
            },
            () => applyCount++,
            () =>
            {
                completeCount++;
                return false;
            });

        Assert.False(succeeded);
        Assert.Equal(3, startCount);
        Assert.Equal(3, applyCount);
        Assert.Equal(3, completeCount);
    }

    [Fact]
    public void TryNct6687DrFanConfigUpdate_SecondCommitSucceedsAndStopsRetrying()
    {
        int startCount = 0;
        int applyCount = 0;
        int completeCount = 0;

        bool succeeded = Nct677X.TryNct6687DrFanConfigUpdate(
            () =>
            {
                startCount++;
                return true;
            },
            () => applyCount++,
            () =>
            {
                completeCount++;
                return completeCount == 2;
            });

        Assert.True(succeeded);
        Assert.Equal(2, startCount);
        Assert.Equal(2, applyCount);
        Assert.Equal(2, completeCount);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void IsNct6687DrFanConfigAccepted_RequiresAcknowledgmentWithoutInvalidFlag(
        bool checkDoneObserved,
        bool configurationInvalid,
        bool expected)
    {
        Assert.Equal(
            expected,
            Nct677X.IsNct6687DrFanConfigAccepted(checkDoneObserved, configurationInvalid));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void GetRestoreRequiredAfterAttempt_RetainsOnlyFailedPendingRestore(
        bool restoreRequired,
        bool restoreSucceeded,
        bool expected)
    {
        Assert.Equal(
            expected,
            Nct677X.GetRestoreRequiredAfterAttempt(restoreRequired, restoreSucceeded));
    }
}
