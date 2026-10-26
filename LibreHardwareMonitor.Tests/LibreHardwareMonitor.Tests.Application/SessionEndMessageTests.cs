// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;
using ApartmentState = System.Threading.ApartmentState;
using Thread = System.Threading.Thread;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Sends real session-end messages to a hidden top-level window whose FormClosing handler cancels
/// and whose WndProc routes through <see cref="UiShutdownCoordinator.TryHandleShutdownMessage"/>
/// exactly as MainForm does.
/// </summary>
[Collection(WinFormsUiLifetimeCollection.Name)]
public sealed class SessionEndMessageTests
{
    [Fact]
    public void BaseFormWithCancellingFormClosing_VetoesQueryEndSession()
    {
        RunOnSta(() =>
        {
            using var form = new SessionEndProbeForm(routeSessionEnd: false);
            IntPtr answer = SendMessage(form.Handle, UiShutdownCoordinator.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);

            // This is the pre-fix behaviour: the cancelling handler turns the query into a veto.
            Assert.Equal(IntPtr.Zero, answer);
            Assert.Equal(new[] { CloseReason.WindowsShutDown }, form.ClosingReasons);
        });
    }

    [Fact]
    public void RoutedQueryEndSession_AnswersTrueWithoutFormClosing()
    {
        RunOnSta(() =>
        {
            using var form = new SessionEndProbeForm(routeSessionEnd: true);
            IntPtr answer = SendMessage(form.Handle, UiShutdownCoordinator.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);

            Assert.Equal((IntPtr)1, answer);
            Assert.Empty(form.ClosingReasons);
            Assert.Equal(0, form.CheckpointCount);
            Assert.Equal(0, form.ShutdownCount);
        });
    }

    [Fact]
    public void CancelledSessionEnd_DoesNotLeaveCloseReasonAtWindowsShutDown()
    {
        RunOnSta(() =>
        {
            using var baseForm = new SessionEndProbeForm(routeSessionEnd: false);
            using var routedForm = new SessionEndProbeForm(routeSessionEnd: true);

            foreach (SessionEndProbeForm form in new[] { baseForm, routedForm })
            {
                SendMessage(form.Handle, UiShutdownCoordinator.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);
                SendMessage(form.Handle, UiShutdownCoordinator.WM_ENDSESSION, IntPtr.Zero, IntPtr.Zero);
                SendMessage(form.Handle, UiShutdownCoordinator.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            // Form keeps WindowsShutDown after a cancelled session end; routing keeps it out entirely.
            Assert.Equal(CloseReason.WindowsShutDown, baseForm.ClosingReasons[baseForm.ClosingReasons.Count - 1]);
            Assert.Equal(new[] { CloseReason.TaskManagerClosing }, routedForm.ClosingReasons);
            Assert.Equal(0, routedForm.CheckpointCount);
            Assert.Equal(0, routedForm.ShutdownCount);
        });
    }

    [Fact]
    public void EndingSession_CheckpointsThenStartsShutdownWithoutClosingOrDisposingTheForm()
    {
        RunOnSta(() =>
        {
            using var form = new SessionEndProbeForm(routeSessionEnd: true);
            SendMessage(form.Handle, UiShutdownCoordinator.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);
            IntPtr answer = SendMessage(form.Handle, UiShutdownCoordinator.WM_ENDSESSION, (IntPtr)1, IntPtr.Zero);

            // A second notification models SystemEvents.SessionEnded arriving after the form's own.
            form.Coordinator.RequestAfterCheckpoint(form.Checkpoint);

            Assert.Equal(IntPtr.Zero, answer);
            Assert.Equal(new[] { "checkpoint", "shutdown" }, form.Order);
            Assert.Empty(form.ClosingReasons);
            Assert.Equal(0, form.ClosedCount);
            Assert.False(form.IsDisposed);
            Assert.True(form.IsHandleCreated);
        });
    }

    [Fact]
    public void CloseAfterEndingSession_DoesNotCloseOrDisposeTheForm()
    {
        RunOnSta(() =>
        {
            using var form = new SessionEndProbeForm(routeSessionEnd: true);
            SendMessage(form.Handle, UiShutdownCoordinator.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);
            SendMessage(form.Handle, UiShutdownCoordinator.WM_ENDSESSION, (IntPtr)1, IntPtr.Zero);

            // Restart Manager follows a confirmed session end with WM_CLOSE. The shutdown body has
            // detached the cancelling handler, so Form alone would close and dispose the window.
            SendMessage(form.Handle, UiShutdownCoordinator.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            Assert.Equal(1, form.ShutdownCount);
            Assert.Empty(form.ClosingReasons);
            Assert.Equal(0, form.ClosedCount);
            Assert.False(form.IsDisposed);
            Assert.True(form.IsHandleCreated);
        });
    }

    private static void RunOnSta(Action body)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "Session-end message test"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Timed out waiting for the session-end test thread.");
        if (failure != null)
            throw new AggregateException(failure);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private sealed class SessionEndProbeForm : Form
    {
        private readonly bool _routeSessionEnd;

        internal SessionEndProbeForm(bool routeSessionEnd)
        {
            _routeSessionEnd = routeSessionEnd;
            ShowInTaskbar = false;
            int uiThreadId = Environment.CurrentManagedThreadId;
            Coordinator = new UiShutdownCoordinator(
                () => Environment.CurrentManagedThreadId == uiThreadId,
                action => BeginInvoke(action),
                () =>
                {
                    // Mirrors CloseApplicationCoreAsync, which detaches MainForm_FormClosing first.
                    FormClosing -= CancelClosing;
                    ShutdownCount++;
                    Order.Add("shutdown");
                    return new TaskCompletionSource<object>().Task;
                });

            FormClosing += CancelClosing;
            FormClosed += (_, _) => ClosedCount++;
            _ = Handle;
        }

        internal UiShutdownCoordinator Coordinator { get; }

        internal List<CloseReason> ClosingReasons { get; } = new();

        internal List<string> Order { get; } = new();

        internal int CheckpointCount { get; private set; }

        internal int ShutdownCount { get; private set; }

        internal int ClosedCount { get; private set; }

        internal void Checkpoint()
        {
            CheckpointCount++;
            Order.Add("checkpoint");
        }

        // Mirrors MainForm_FormClosing, which cancels every close it sees.
        private void CancelClosing(object sender, FormClosingEventArgs e)
        {
            ClosingReasons.Add(e.CloseReason);
            e.Cancel = true;
        }

        protected override void WndProc(ref Message m)
        {
            if (_routeSessionEnd &&
                Coordinator.TryHandleShutdownMessage(m.Msg, m.WParam, Checkpoint, out IntPtr result))
            {
                m.Result = result;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
