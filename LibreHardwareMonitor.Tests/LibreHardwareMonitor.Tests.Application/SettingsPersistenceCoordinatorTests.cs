// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class SettingsPersistenceCoordinatorTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly string _directory;

    public SettingsPersistenceCoordinatorTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            nameof(SettingsPersistenceCoordinatorTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Autosave_ProjectsBeforeCleanCheckAndSkipsUnchangedStore()
    {
        List<string> events = new();
        int writeCount = 0;
        PersistentSettings settings = new((_, _) => writeCount++);
        string path = ConfigPath("clean-autosave");
        Directory.CreateDirectory(path);
        SettingsPersistenceCoordinator coordinator = new(
            settings,
            path,
            () => events.Add("project"),
            _ => events.Add("report"));

        coordinator.Save(autoSave: true);

        Assert.Equal(new[] { "project" }, events);
        Assert.Equal(0, writeCount);
        Assert.False(settings.Modified);
    }

    [Fact]
    public void Autosave_ProjectedChangePersistsAndClearsDirtyState()
    {
        List<string> events = new();
        string path = ConfigPath("changed-autosave");
        PersistentSettings settings = new((fileName, contents) =>
        {
            events.Add("write");
            File.WriteAllBytes(fileName, contents);
        });
        SettingsPersistenceCoordinator coordinator = new(
            settings,
            path,
            () =>
            {
                events.Add("project");
                settings.SetValue("mode", "projected");
            },
            _ => events.Add("report"));

        coordinator.Save(autoSave: true);

        Assert.Equal(new[] { "project", "write" }, events);
        Assert.False(settings.Modified);
        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal("projected", reloaded.GetValue("mode", null));
    }

    [Fact]
    public void FinalSave_WritesEvenWhenStoreIsClean()
    {
        (string Suffix, string Description)[] unsafeDestinations =
        {
            (string.Empty, "The runtime settings file"),
            (".backup", "The runtime settings backup"),
            (".new", "The runtime settings staging file")
        };
        foreach ((string suffix, string description) in unsafeDestinations)
        {
            int guardedWriteCount = 0;
            string guardedPath = ConfigPath("unsafe-" + description.Replace(" ", "-"));
            PersistentSettings guardedSettings = new((_, _) => guardedWriteCount++);
            SettingsPersistenceCoordinator guardedCoordinator = new(
                guardedSettings,
                guardedPath,
                () => { },
                _ => { });

            Directory.CreateDirectory(guardedPath + suffix);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => guardedCoordinator.Save(autoSave: false));

            Assert.Contains(description, exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, guardedWriteCount);
        }

        List<string> events = new();
        string path = ConfigPath("clean-final");
        PersistentSettings settings = new((fileName, contents) =>
        {
            events.Add("write");
            File.WriteAllBytes(fileName, contents);
        });
        SettingsPersistenceCoordinator coordinator = new(
            settings,
            path,
            () => events.Add("project"),
            _ => events.Add("report"));

        Assert.False(settings.Modified);

        coordinator.Save(autoSave: false);

        Assert.Equal(new[] { "project", "write" }, events);
        Assert.True(File.Exists(path));
        Assert.False(settings.Modified);
    }

    [Fact]
    public void Autosave_IOExceptionIsReportedSuppressedAndRemainsDirty()
    {
        List<string> events = new();
        IOException failure = new("injected autosave failure");
        Exception reportedFailure = null;
        int writeCount = 0;
        string path = ConfigPath("autosave-io-failure");
        PersistentSettings settings = new((fileName, contents) =>
        {
            events.Add("write");
            if (Interlocked.Increment(ref writeCount) == 1)
                throw failure;

            File.WriteAllBytes(fileName, contents);
        });
        SettingsPersistenceCoordinator coordinator = new(
            settings,
            path,
            () =>
            {
                events.Add("project");
                settings.SetValue("mode", "retry");
            },
            exception =>
            {
                events.Add("report");
                reportedFailure = exception;
            });

        coordinator.Save(autoSave: true);

        Assert.Equal(new[] { "project", "write", "report" }, events);
        Assert.Same(failure, reportedFailure);
        Assert.True(settings.Modified);

        coordinator.Save(autoSave: true);

        Assert.Equal(
            new[] { "project", "write", "report", "project", "write" },
            events);
        Assert.Equal(2, writeCount);
        Assert.False(settings.Modified);
        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal("retry", reloaded.GetValue("mode", null));

        IOException projectionFailure = new("injected projection failure");
        int projectionWriterCount = 0;
        bool projectionFailureReported = false;
        PersistentSettings projectionSettings = new((_, _) => projectionWriterCount++);
        SettingsPersistenceCoordinator projectionCoordinator = new(
            projectionSettings,
            ConfigPath("autosave-projection-io-failure"),
            () => throw projectionFailure,
            _ => projectionFailureReported = true);

        IOException propagated = Assert.Throws<IOException>(
            () => projectionCoordinator.Save(autoSave: true));

        Assert.Same(projectionFailure, propagated);
        Assert.False(projectionFailureReported);
        Assert.Equal(0, projectionWriterCount);
    }

    [Fact]
    public void Autosave_UnauthorizedAccessIsReportedSuppressedAndRemainsDirty()
    {
        List<string> events = new();
        UnauthorizedAccessException failure = new("injected autosave access failure");
        Exception reportedFailure = null;
        int writeCount = 0;
        string path = ConfigPath("autosave-access-failure");
        PersistentSettings settings = new((fileName, contents) =>
        {
            events.Add("write");
            if (Interlocked.Increment(ref writeCount) == 1)
                throw failure;

            File.WriteAllBytes(fileName, contents);
        });
        SettingsPersistenceCoordinator coordinator = new(
            settings,
            path,
            () =>
            {
                events.Add("project");
                settings.SetValue("mode", "retry");
            },
            exception =>
            {
                events.Add("report");
                reportedFailure = exception;
            });

        coordinator.Save(autoSave: true);

        Assert.Equal(new[] { "project", "write", "report" }, events);
        Assert.Same(failure, reportedFailure);
        Assert.True(settings.Modified);

        coordinator.Save(autoSave: true);

        Assert.Equal(
            new[] { "project", "write", "report", "project", "write" },
            events);
        Assert.Equal(2, writeCount);
        Assert.False(settings.Modified);
        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal("retry", reloaded.GetValue("mode", null));

        UnauthorizedAccessException projectionFailure = new("injected projection access failure");
        int projectionWriterCount = 0;
        bool projectionFailureReported = false;
        PersistentSettings projectionSettings = new((_, _) => projectionWriterCount++);
        SettingsPersistenceCoordinator projectionCoordinator = new(
            projectionSettings,
            ConfigPath("autosave-projection-access-failure"),
            () => throw projectionFailure,
            _ => projectionFailureReported = true);

        UnauthorizedAccessException propagated = Assert.Throws<UnauthorizedAccessException>(
            () => projectionCoordinator.Save(autoSave: true));

        Assert.Same(projectionFailure, propagated);
        Assert.False(projectionFailureReported);
        Assert.Equal(0, projectionWriterCount);
    }

    [Fact]
    public void FinalSave_IoFailurePropagatesAndRemainsDirty()
    {
        Exception[] failures =
        {
            new IOException("injected final-save I/O failure"),
            new UnauthorizedAccessException("injected final-save access failure")
        };
        foreach (Exception failure in failures)
        {
            bool reported = false;
            string path = ConfigPath("final-failure-" + failure.GetType().Name);
            PersistentSettings settings = new((_, _) => throw failure);
            SettingsPersistenceCoordinator coordinator = new(
                settings,
                path,
                () => settings.SetValue("mode", "unsaved"),
                _ => reported = true);

            SettingsPersistenceException thrown = Assert.Throws<SettingsPersistenceException>(
                () => coordinator.Save(autoSave: false));

            Assert.Same(failure, thrown.InnerException);
            Assert.False(reported);
            Assert.True(settings.Modified);
        }

        foreach (Exception projectionFailure in failures)
        {
            int writerCount = 0;
            bool reported = false;
            PersistentSettings settings = new((_, _) => writerCount++);
            SettingsPersistenceCoordinator coordinator = new(
                settings,
                ConfigPath("final-projection-failure-" + projectionFailure.GetType().Name),
                () => throw projectionFailure,
                _ => reported = true);

            Exception thrown = Record.Exception(
                () => coordinator.Save(autoSave: false));

            Assert.Same(projectionFailure, thrown);
            Assert.False(reported);
            Assert.Equal(0, writerCount);
        }
    }

    [Fact]
    public async Task FinalSave_WaitsBehindInFlightAutosaveAndPersistsLatestProjection()
    {
        ConcurrentQueue<string> events = new();
        TaskCompletionSource<bool> firstWriteStarted = CreateBarrier();
        TaskCompletionSource<bool> releaseFirstWrite = CreateBarrier();
        TaskCompletionSource<bool> secondSaveEntered = CreateBarrier();
        TaskCompletionSource<bool> secondSerializationAcquired = CreateBarrier();
        int projectionCount = 0;
        int writeCount = 0;
        int enteredCount = 0;
        int serializationCount = 0;
        string path = ConfigPath("overlapping");

        PersistentSettings settings = new((fileName, contents) =>
        {
            int currentWrite = Interlocked.Increment(ref writeCount);
            events.Enqueue("write-" + currentWrite + "-start");
            if (currentWrite == 1)
            {
                firstWriteStarted.TrySetResult(true);
                if (!releaseFirstWrite.Task.Wait(TestTimeout))
                    throw new TimeoutException("Timed out waiting to release the first settings write.");
            }

            File.WriteAllBytes(fileName, contents);
            events.Enqueue("write-" + currentWrite + "-end");
        }, stage =>
        {
            if (stage == PersistentSettings.SaveStage.Entered)
            {
                int currentEntry = Interlocked.Increment(ref enteredCount);
                events.Enqueue("entered-" + currentEntry);
                if (currentEntry == 2)
                    secondSaveEntered.TrySetResult(true);
            }
            else
            {
                int currentSerialization = Interlocked.Increment(ref serializationCount);
                events.Enqueue("acquired-" + currentSerialization);
                if (currentSerialization == 2)
                    secondSerializationAcquired.TrySetResult(true);
            }
        });
        SettingsPersistenceCoordinator coordinator = new(
            settings,
            path,
            () =>
            {
                int currentProjection = Interlocked.Increment(ref projectionCount);
                events.Enqueue("project-" + currentProjection);
                settings.SetValue(
                    "mode",
                    currentProjection == 1 ? "autosave" : "final");
            },
            exception => throw new InvalidOperationException(
                "The autosave should not have been suppressed.",
                exception));

        Task autoSave = Task.Run(() => coordinator.Save(autoSave: true));
        Task finalSave = Task.CompletedTask;
        try
        {
            await firstWriteStarted.Task.WaitAsync(TestTimeout);

            finalSave = Task.Run(() => coordinator.Save(autoSave: false));
            await secondSaveEntered.Task.WaitAsync(TestTimeout);

            Assert.Equal(2, Volatile.Read(ref projectionCount));
            Assert.False(secondSerializationAcquired.Task.IsCompleted);
            Assert.True(settings.Modified);
        }
        finally
        {
            events.Enqueue("release-first-write");
            releaseFirstWrite.TrySetResult(true);
            await Task.WhenAll(autoSave, finalSave).WaitAsync(TestTimeout);
        }

        Assert.True(secondSerializationAcquired.Task.IsCompletedSuccessfully);
        Assert.Equal(2, Volatile.Read(ref writeCount));
        Assert.False(settings.Modified);
        Assert.Equal(
            new[]
            {
                "project-1",
                "entered-1",
                "acquired-1",
                "write-1-start",
                "project-2",
                "entered-2",
                "release-first-write",
                "write-1-end",
                "acquired-2",
                "write-2-start",
                "write-2-end"
            },
            events.ToArray());

        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal("final", reloaded.GetValue("mode", null));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }
        catch (IOException)
        { }
        catch (UnauthorizedAccessException)
        { }
    }

    private string ConfigPath(string name)
    {
        return Path.Combine(_directory, name + ".config");
    }

    private static TaskCompletionSource<bool> CreateBarrier()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
