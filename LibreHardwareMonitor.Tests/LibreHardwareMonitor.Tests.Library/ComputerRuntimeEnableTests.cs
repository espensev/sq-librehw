// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Controller.AquaComputer;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Hardware.Memory;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

public sealed class ComputerRuntimeEnableTests
{
    [Theory]
    [InlineData(nameof(Computer.IsBatteryEnabled))]
    [InlineData(nameof(Computer.IsControllerEnabled))]
    [InlineData(nameof(Computer.IsCpuEnabled))]
    [InlineData(nameof(Computer.IsGpuEnabled))]
    [InlineData(nameof(Computer.IsPowerMonitorEnabled))]
    [InlineData(nameof(Computer.IsMemoryEnabled))]
    [InlineData(nameof(Computer.IsMotherboardEnabled))]
    [InlineData(nameof(Computer.IsNetworkEnabled))]
    [InlineData(nameof(Computer.IsPsuEnabled))]
    [InlineData(nameof(Computer.IsStorageEnabled))]
    public void Enable_WhenAddSubscriberThrows_RollsBackEveryCategory(string option)
    {
        var failure = new InvalidOperationException("add subscriber failed");
        var existing = new TestGroup("existing");
        var created = new List<TestGroup>();
        var computer = CreateComputer(_ =>
        {
            var group = new TestGroup($"attempt-{created.Count}");
            created.Add(group);
            return group;
        }, existing);
        computer.Open();
        bool fail = true;
        int notifications = 0;
        computer.HardwareAdded += _ =>
        {
            notifications++;
            if (fail)
                throw failure;
        };

        Exception thrown = Record.Exception(() => SetEnabled(computer, option));

        Assert.Same(failure, thrown?.InnerException);
        Assert.False(GetEnabled(computer, option));
        Assert.Equal(existing.Hardware, computer.Hardware);
        TestGroup attempted = Assert.Single(created);
        Assert.Equal(1, attempted.CloseCount);
        Assert.Equal(0, attempted.SubscriberCount);
        Assert.Equal(0, existing.CloseCount);

        fail = false;
        SetEnabled(computer, option);

        Assert.True(GetEnabled(computer, option));
        Assert.Equal(existing.Hardware.Concat(created.Skip(1).SelectMany(group => group.Hardware)), computer.Hardware);
        Assert.Equal(created.Count, notifications);
        attempted.RaiseAdded();
        Assert.Equal(created.Count, notifications);
        computer.Close();
        Assert.All(created, group => Assert.Equal(1, group.CloseCount));
        Assert.All(created, group => Assert.Equal(0, group.SubscriberCount));
    }

    [Fact]
    public void Enable_WhenLaterConstructorThrows_CleansEveryOwnerAndCanRetry()
    {
        var failure = new InvalidOperationException("third constructor failed");
        var cleanupFailure = new InvalidOperationException("cleanup failed");
        var existing = new TestGroup("existing");
        var created = new List<TestGroup>();
        bool fail = true;
        var computer = CreateComputer(type =>
        {
            if (fail && type == typeof(AquaComputerGroup))
                throw failure;

            var group = new TestGroup($"attempt-{created.Count}", fail ? cleanupFailure : null);
            created.Add(group);
            return group;
        }, existing);
        computer.Open();
        var removed = new List<IHardware>();
        computer.HardwareRemoved += _ =>
        {
            if (fail)
                throw cleanupFailure;
        };
        computer.HardwareRemoved += removed.Add;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => computer.IsControllerEnabled = true);

        Assert.Same(failure, thrown);
        Assert.False(computer.IsControllerEnabled);
        Assert.Equal(existing.Hardware, computer.Hardware);
        Assert.Equal(2, created.Count);
        Assert.All(created, group => Assert.Equal(1, group.CloseCount));
        Assert.All(created, group => Assert.Equal(0, group.SubscriberCount));
        Assert.Equal(created.AsEnumerable().Reverse().SelectMany(group => group.Hardware), removed);
        Assert.Equal(0, existing.CloseCount);

        fail = false;
        computer.IsControllerEnabled = true;

        Assert.True(computer.IsControllerEnabled);
        Assert.Equal(10, created.Count);
        Assert.Equal(9, computer.Hardware.Count);
        Assert.All(created.Skip(2), group => Assert.Equal(2, group.SubscriberCount));
        computer.Close();
        Assert.All(created, group => Assert.Equal(1, group.CloseCount));
        Assert.Equal(1, existing.CloseCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enable_WhenLaterRegistrationFails_DetachesAndClosesAttempt(bool subscriptionFailure)
    {
        var failure = new InvalidOperationException("registration failed");
        var created = new List<TestGroup>();
        var computer = CreateComputer(_ =>
        {
            var group = new TestGroup($"attempt-{created.Count}");
            if (created.Count == 1 && subscriptionFailure)
                group.SubscriptionFailure = failure;
            created.Add(group);
            return group;
        });
        computer.Open();
        int notifications = 0;
        computer.HardwareAdded += _ =>
        {
            if (++notifications == 2)
                throw failure;
        };

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => computer.IsPsuEnabled = true);

        Assert.Same(failure, thrown);
        Assert.False(computer.IsPsuEnabled);
        Assert.Empty(computer.Hardware);
        Assert.Equal(2, created.Count);
        Assert.All(created, group => Assert.Equal(1, group.CloseCount));
        Assert.All(created, group => Assert.Equal(0, group.SubscriberCount));
        computer.Close();
        Assert.All(created, group => Assert.Equal(1, group.CloseCount));
    }

    [Fact]
    public void Enable_WhenAnotherCategoryIsEnabledReentrantly_RollbackPreservesIt()
    {
        var failure = new InvalidOperationException("later constructor failed");
        var nested = new TestGroup("nested-memory");
        var attempted = new TestGroup("attempted-controller");
        int controllerFactories = 0;
        var computer = CreateComputer(type =>
        {
            if (type == typeof(MemoryGroup))
                return nested;

            if (++controllerFactories == 2)
                throw failure;
            return attempted;
        });
        computer.Open();
        computer.HardwareAdded += hardware =>
        {
            if (ReferenceEquals(hardware, attempted.Hardware[0]))
                computer.IsMemoryEnabled = true;
        };

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => computer.IsControllerEnabled = true));

        Assert.False(computer.IsControllerEnabled);
        Assert.True(computer.IsMemoryEnabled);
        Assert.Equal(nested.Hardware, computer.Hardware);
        Assert.Equal(0, nested.CloseCount);
        Assert.Equal(1, attempted.CloseCount);
        computer.Close();
        Assert.Equal(1, nested.CloseCount);
    }

    [Fact]
    public void Enable_WhenSameCategoryIsEnabledReentrantly_DoesNotDuplicateGroups()
    {
        var created = new List<TestGroup>();
        var computer = CreateComputer(_ =>
        {
            var group = new TestGroup($"attempt-{created.Count}");
            created.Add(group);
            return group;
        });
        computer.Open();
        int notifications = 0;
        computer.HardwareAdded += _ =>
        {
            if (++notifications == 1)
                computer.IsPsuEnabled = true;
        };

        computer.IsPsuEnabled = true;

        Assert.True(computer.IsPsuEnabled);
        Assert.Equal(2, created.Count);
        Assert.Equal(2, computer.Hardware.Count);
        computer.Close();
        Assert.All(created, group => Assert.Equal(1, group.CloseCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enable_WhenCallbackChangesLifecycle_AbortsWithoutPublishingMoreGroups(bool reset)
    {
        var created = new List<TestGroup>();
        var computer = CreateComputer(_ =>
        {
            var group = new TestGroup($"attempt-{created.Count}");
            created.Add(group);
            return group;
        });
        computer.Open();
        computer.HardwareAdded += _ =>
        {
            if (reset)
                computer.Reset();
            else
                computer.Close();
        };

        Assert.Throws<InvalidOperationException>(() => computer.IsPsuEnabled = true);

        Assert.False(computer.IsPsuEnabled);
        Assert.Empty(computer.Hardware);
        Assert.Single(created);
        Assert.Equal(1, created[0].CloseCount);
        computer.Close();
        Assert.Equal(1, created[0].CloseCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnableGpu_IncludesIntelOnlyWhenCpuIsEnabled(bool cpuEnabled)
    {
        var requestedTypes = new List<Type>();
        var computer = CreateComputer(type =>
        {
            requestedTypes.Add(type);
            return new TestGroup(type.Name);
        });
        computer.IsCpuEnabled = cpuEnabled;
        computer.Open();

        computer.IsGpuEnabled = true;

        Assert.Equal(cpuEnabled
            ? [typeof(AmdGpuGroup), typeof(NvidiaGroup), typeof(IntelGpuGroup)]
            : new[] { typeof(AmdGpuGroup), typeof(NvidiaGroup) }, requestedTypes);
        Assert.True(computer.IsGpuEnabled);
        computer.Close();
    }

    [Fact]
    public void Enable_WhenConstructionEndsLifecycle_ClosesUnpublishedGroupAndPreservesFailure()
    {
        var created = new TestGroup("unpublished", new InvalidOperationException("cleanup failed"));
        Computer computer = null;
        computer = CreateComputer(_ =>
        {
            computer.Close();
            return created;
        });
        computer.Open();
        var published = new List<IHardware>();
        computer.HardwareAdded += published.Add;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => computer.IsPsuEnabled = true);

        Assert.Contains("lifecycle changed", thrown.Message);
        Assert.False(computer.IsPsuEnabled);
        Assert.Empty(published);
        Assert.Empty(computer.Hardware);
        Assert.Equal(0, created.SubscriberCount);
        Assert.Equal(1, created.CloseCount);
    }

    private static Computer CreateComputer(Func<Type, IGroup> createGroup, params IGroup[] initialGroups)
    {
        return new Computer(null, new Computer.OpenDependencies(
            () => null,
            () => { },
            () => { },
            () => { },
            () => { },
            add =>
            {
                foreach (IGroup group in initialGroups)
                    add(group);
            },
            createGroup));
    }

    private static bool GetEnabled(Computer computer, string option)
    {
        return (bool)typeof(Computer).GetProperty(option)!.GetValue(computer);
    }

    private static void SetEnabled(Computer computer, string option)
    {
        typeof(Computer).GetProperty(option)!.SetValue(computer, true);
    }

    private sealed class TestGroup : IGroup, IHardwareChanged
    {
        private readonly Exception _cleanupFailure;
        private HardwareEventHandler _added;
        private HardwareEventHandler _removed;

        public TestGroup(string identifier, Exception cleanupFailure = null)
        {
            Hardware = [new TestHardware(identifier)];
            _cleanupFailure = cleanupFailure;
        }

        public event HardwareEventHandler HardwareAdded
        {
            add => _added += value;
            remove
            {
                _added -= value;
                if (_cleanupFailure != null)
                    throw _cleanupFailure;
            }
        }

        public event HardwareEventHandler HardwareRemoved
        {
            add
            {
                _removed += value;
                if (SubscriptionFailure != null)
                    throw SubscriptionFailure;
            }
            remove => _removed -= value;
        }

        public IReadOnlyList<IHardware> Hardware { get; }

        public int CloseCount { get; private set; }

        public int SubscriberCount => (_added?.GetInvocationList().Length ?? 0) + (_removed?.GetInvocationList().Length ?? 0);

        public Exception SubscriptionFailure { get; set; }

        public void Close()
        {
            CloseCount++;
            if (_cleanupFailure != null)
                throw _cleanupFailure;
        }

        public string GetReport() => null;

        public void RaiseAdded() => _added?.Invoke(Hardware[0]);
    }

    private sealed class TestHardware : HardwareBase
    {
        public TestHardware(string identifier) : base("Test hardware", new Identifier(identifier), new TestSettings())
        {
        }

        public override HardwareType HardwareType => HardwareType.Cpu;

        public override void Update()
        {
        }
    }

    private sealed class TestSettings : ISettings
    {
        public bool Contains(string name) => false;

        public string GetValue(string name, string value) => value;

        public void Remove(string name)
        {
        }

        public void SetValue(string name, string value)
        {
        }
    }
}
