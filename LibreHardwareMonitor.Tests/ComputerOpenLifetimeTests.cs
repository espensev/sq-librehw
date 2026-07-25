// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

public sealed class ComputerOpenLifetimeTests
{
    [Fact]
    public void Open_WhenLaterGroupConstructionFails_RollsBackAndCanRetryCleanly()
    {
        var discoveryFailure = new InvalidOperationException("group discovery failed");
        var cleanupFailure = new InvalidOperationException("group cleanup failed");
        var firstHardware = new TestHardware("first-attempt");
        var secondHardware = new TestHardware("second-before-failure");
        var retryHardware = new TestHardware("retry");
        var firstGroup = new TestGroup(firstHardware, cleanupFailure);
        var secondGroup = new TestGroup(secondHardware);
        var retryGroup = new TestGroup(retryHardware);
        var probe = new OpenProbe();
        int attempt = 0;

        probe.AddGroups = add =>
        {
            attempt++;
            if (attempt == 1)
            {
                add(firstGroup);
                add(secondGroup);
                throw discoveryFailure;
            }

            add(retryGroup);
        };

        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        var added = new List<IHardware>();
        var removed = new List<IHardware>();
        computer.HardwareAdded += added.Add;
        computer.HardwareRemoved += removed.Add;
        computer.HardwareRemoved += _ => computer.Open();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(computer.Open);

        Assert.Same(discoveryFailure, thrown);
        Assert.Empty(computer.Hardware);
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = computer.SMBios;
        });
        Assert.Equal(1, firstGroup.CloseCount);
        Assert.Equal(1, secondGroup.CloseCount);
        Assert.Equal([firstHardware, secondHardware], added);
        Assert.Equal([secondHardware, firstHardware], removed);
        probe.AssertCounts(1, 1);

        computer.Close();

        Assert.Equal(1, firstGroup.CloseCount);
        Assert.Equal(1, secondGroup.CloseCount);
        probe.AssertCounts(1, 1);

        computer.Open();

        Assert.Same(retryHardware, Assert.Single(computer.Hardware));
        Assert.Equal([firstHardware, secondHardware, retryHardware], added);
        Assert.Equal([secondHardware, firstHardware], removed);
        probe.AssertCounts(2, 1);

        computer.Close();
        computer.Close();

        Assert.Empty(computer.Hardware);
        Assert.Equal(1, retryGroup.CloseCount);
        Assert.Equal([secondHardware, firstHardware, retryHardware], removed);
        probe.AssertCounts(2, 2);
    }

    [Fact]
    public void Open_WhenHardwareAddedHandlerThrows_RollsBackAndCanRetryWithoutDuplicateEvents()
    {
        var eventFailure = new InvalidOperationException("consumer failed");
        var firstHardware = new TestHardware("event-failure");
        var retryHardware = new TestHardware("event-retry");
        var firstGroup = new TestGroup(firstHardware);
        var retryGroup = new TestGroup(retryHardware);
        var probe = new OpenProbe();
        int attempt = 0;

        probe.AddGroups = add =>
        {
            attempt++;
            add(attempt == 1 ? firstGroup : retryGroup);
        };

        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        var added = new List<IHardware>();
        var addedAfterFailure = new List<IHardware>();
        var removed = new List<IHardware>();
        var removedAfterFailure = new List<IHardware>();
        HardwareEventHandler failingHandler = _ => throw eventFailure;
        computer.HardwareAdded += added.Add;
        computer.HardwareAdded += failingHandler;
        computer.HardwareAdded += addedAfterFailure.Add;
        computer.HardwareRemoved += removed.Add;
        computer.HardwareRemoved += removedAfterFailure.Add;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(computer.Open);

        Assert.Same(eventFailure, thrown);
        Assert.Empty(computer.Hardware);
        Assert.Equal(1, firstGroup.CloseCount);
        Assert.Equal([firstHardware], added);
        Assert.Equal([firstHardware], addedAfterFailure);
        Assert.Equal([firstHardware], removed);
        Assert.Equal([firstHardware], removedAfterFailure);
        probe.AssertCounts(1, 1);

        computer.HardwareAdded -= failingHandler;
        computer.Open();
        computer.Close();

        Assert.Empty(computer.Hardware);
        Assert.Equal(1, retryGroup.CloseCount);
        Assert.Equal([firstHardware, retryHardware], added);
        Assert.Equal([firstHardware, retryHardware], addedAfterFailure);
        Assert.Equal([firstHardware, retryHardware], removed);
        Assert.Equal([firstHardware, retryHardware], removedAfterFailure);
        probe.AssertCounts(2, 2);
    }

    [Fact]
    public void Open_WhenAddedHandlerReenters_DoesNotDuplicateGroupsOrGlobalOwners()
    {
        var hardware = new TestHardware("reentrant-open");
        var group = new TestGroup(hardware);
        var probe = new OpenProbe();
        int addGroupsCount = 0;
        probe.AddGroups = add =>
        {
            addGroupsCount++;
            add(group);
        };
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        computer.HardwareAdded += _ => computer.Open();

        computer.Open();

        Assert.Equal(1, addGroupsCount);
        Assert.Same(hardware, Assert.Single(computer.Hardware));
        probe.AssertCounts(1, 0);

        computer.Close();

        Assert.Equal(1, group.CloseCount);
        probe.AssertCounts(1, 1);
    }

    [Fact]
    public void Open_WhenOptionChangesAfterItsCategoryWasVisited_RebuildsFromLatestFlags()
    {
        var generations = new List<List<TestGroup>>();
        var triggerHardware = new HashSet<IHardware>();
        var probe = new OpenProbe();
        Computer computer = null;
        probe.AddGroups = add =>
        {
            var generation = new List<TestGroup>();
            generations.Add(generation);

            // This is the synthetic PSU-category position. The trigger is
            // deliberately added afterward so the first request arrives too
            // late for this generation.
            if (computer.IsPsuEnabled)
            {
                var desiredGroup = new TestGroup(new TestHardware($"open-desired-{generations.Count}"));
                generation.Add(desiredGroup);
                add(desiredGroup);
            }

            var trigger = new TestHardware($"open-trigger-{generations.Count}");
            var triggerGroup = new TestGroup(trigger);
            triggerHardware.Add(trigger);
            generation.Add(triggerGroup);
            add(triggerGroup);
        };
        computer = new Computer(new TestSettings(), probe.CreateDependencies());
        computer.HardwareAdded += hardware =>
        {
            if (triggerHardware.Contains(hardware))
                computer.IsPsuEnabled = true;
        };

        computer.Open();

        Assert.Equal(2, generations.Count);
        Assert.True(computer.IsPsuEnabled);
        Assert.Equal(2, GetGroupCount(computer));
        Assert.Equal(1, generations[0][0].CloseCount);
        Assert.All(generations[1], group => Assert.Equal(0, group.CloseCount));
        probe.AssertCounts(1, 0);

        computer.Close();

        Assert.All(generations.SelectMany(generation => generation), group => Assert.Equal(1, group.CloseCount));
        probe.AssertCounts(1, 1);
    }

    [Fact]
    public void Open_WhenConfigurationNeverStabilizes_RollsBackAtAttemptLimit()
    {
        var generations = new List<TestGroup>();
        var triggerHardware = new HashSet<IHardware>();
        var probe = new OpenProbe();
        Computer computer = null;
        bool churn = true;
        probe.AddGroups = add =>
        {
            var trigger = new TestHardware($"open-churn-{generations.Count + 1}");
            var group = new TestGroup(trigger);
            triggerHardware.Add(trigger);
            generations.Add(group);
            add(group);
        };
        computer = new Computer(new TestSettings(), probe.CreateDependencies());
        computer.HardwareAdded += hardware =>
        {
            if (churn && triggerHardware.Contains(hardware))
                computer.IsPsuEnabled = !computer.IsPsuEnabled;
        };

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(computer.Open);

        Assert.Contains($"{Computer.MaxConfigurationBuildAttempts} build attempts", thrown.Message);
        Assert.Equal(Computer.MaxConfigurationBuildAttempts, generations.Count);
        Assert.Empty(computer.Hardware);
        Assert.Equal(0, GetGroupCount(computer));
        Assert.All(generations, group => Assert.Equal(1, group.CloseCount));
        probe.AssertCounts(1, 1);

        churn = false;
        computer.Open();

        Assert.Equal(Computer.MaxConfigurationBuildAttempts + 1, generations.Count);
        Assert.Single(computer.Hardware);
        probe.AssertCounts(2, 1);

        computer.Close();

        Assert.Equal(1, generations[^1].CloseCount);
        probe.AssertCounts(2, 2);
    }

    [Fact]
    public void Close_WhenRemovalHandlerThrows_StillClosesEveryGroupAndGlobalOwner()
    {
        var removalFailure = new InvalidOperationException("removal consumer failed");
        var firstHardware = new TestHardware("close-first");
        var secondHardware = new TestHardware("close-second");
        var firstGroup = new TestGroup(firstHardware);
        var secondGroup = new TestGroup(secondHardware);
        var probe = new OpenProbe
        {
            AddGroups = add =>
            {
                add(firstGroup);
                add(secondGroup);
            }
        };
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        var observedRemovals = new List<IHardware>();

        computer.Open();
        computer.HardwareRemoved += _ => throw removalFailure;
        computer.HardwareRemoved += observedRemovals.Add;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(computer.Close);

        Assert.Same(removalFailure, thrown);
        Assert.Empty(computer.Hardware);
        Assert.Equal(1, firstGroup.CloseCount);
        Assert.Equal(1, secondGroup.CloseCount);
        Assert.Equal([secondHardware, firstHardware], observedRemovals);
        probe.AssertCounts(1, 1);

        computer.Close();
        Assert.Equal(1, firstGroup.CloseCount);
        Assert.Equal(1, secondGroup.CloseCount);
    }

    [Fact]
    public async Task Close_WhenRemovalHandlerEnablesOption_DefersGroupMutationAndRetainsRequest()
    {
        var initialHardware = new TestHardware("close-option-initial");
        var initialGroup = new TestGroup(initialHardware);
        var probe = new OpenProbe
        {
            AddGroups = add => add(initialGroup)
        };
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        int groupCountAfterSetter = -1;

        computer.Open();
        computer.HardwareRemoved += hardware =>
        {
            if (ReferenceEquals(hardware, initialHardware))
            {
                computer.IsPsuEnabled = true;
                groupCountAfterSetter = GetGroupCount(computer);
            }
        };

        Task close = Task.Run(computer.Close);
        Task completed = await Task.WhenAny(close, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(close, completed);
        await close;
        Assert.Equal(0, groupCountAfterSetter);
        Assert.Equal(0, GetGroupCount(computer));
        Assert.True(computer.IsPsuEnabled);
        Assert.Equal(1, initialGroup.CloseCount);
        probe.AssertCounts(1, 1);
    }

    [Fact]
    public void Reset_WhenReplacementFails_ClosesEveryPartialReplacement()
    {
        var resetFailure = new InvalidOperationException("replacement failed");
        var initialGroup = new TestGroup(new TestHardware("reset-initial"));
        var partialGroup = new TestGroup(new TestHardware("reset-partial"));
        var probe = new OpenProbe();
        int attempt = 0;
        probe.AddGroups = add =>
        {
            attempt++;
            add(attempt == 1 ? initialGroup : partialGroup);
            if (attempt == 2)
                throw resetFailure;
        };
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());

        computer.Open();
        computer.HardwareRemoved += _ => computer.Reset();
        computer.HardwareAdded += _ => computer.Reset();
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(computer.Reset);

        Assert.Same(resetFailure, thrown);
        Assert.Equal(2, attempt);
        Assert.Empty(computer.Hardware);
        Assert.Equal(1, initialGroup.CloseCount);
        Assert.Equal(1, partialGroup.CloseCount);

        computer.Close();
        probe.AssertCounts(1, 1);
    }

    [Fact]
    public async Task Reset_WhenOptionChangesAfterItsCategoryWasVisited_RebuildsFromLatestFlags()
    {
        var generations = new List<List<TestGroup>>();
        var triggerHardware = new HashSet<IHardware>();
        var groupCountsAfterSetter = new List<int>();
        var probe = new OpenProbe();
        Computer computer = null;
        probe.AddGroups = add =>
        {
            var generation = new List<TestGroup>();
            generations.Add(generation);

            // This is the synthetic PSU-category position. During the first
            // Reset build the trigger changes the option after this point.
            if (computer.IsPsuEnabled)
            {
                var desiredGroup = new TestGroup(new TestHardware($"reset-desired-{generations.Count}"));
                generation.Add(desiredGroup);
                add(desiredGroup);
            }

            var trigger = new TestHardware($"reset-trigger-{generations.Count}");
            var triggerGroup = new TestGroup(trigger);
            triggerHardware.Add(trigger);
            generation.Add(triggerGroup);
            add(triggerGroup);
        };
        computer = new Computer(new TestSettings(), probe.CreateDependencies());

        computer.Open();
        computer.HardwareAdded += hardware =>
        {
            if (triggerHardware.Contains(hardware))
            {
                computer.IsPsuEnabled = true;
                groupCountsAfterSetter.Add(GetGroupCount(computer));
            }
        };

        Task reset = Task.Run(computer.Reset);
        Task completed = await Task.WhenAny(reset, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(reset, completed);
        await reset;
        Assert.Equal(3, generations.Count);
        Assert.Equal([1, 2], groupCountsAfterSetter);
        Assert.Equal(2, GetGroupCount(computer));
        Assert.Equal(2, computer.Hardware.Count);
        Assert.True(computer.IsPsuEnabled);
        Assert.Equal(1, generations[0][0].CloseCount);
        Assert.Equal(1, generations[1][0].CloseCount);
        Assert.All(generations[2], group => Assert.Equal(0, group.CloseCount));

        computer.Close();

        Assert.All(generations.SelectMany(generation => generation), group => Assert.Equal(1, group.CloseCount));
        probe.AssertCounts(1, 1);
    }

    [Fact]
    public void Traverse_CapturesEachGroupHardwareSnapshotOnce()
    {
        var first = new TestHardware("traverse-first");
        var second = new TestHardware("traverse-second");
        var group = new SnapshotGroup(first, second);
        var probe = new OpenProbe
        {
            AddGroups = add => add(group)
        };
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        var visitor = new RecordingVisitor();

        computer.Open();
        computer.Traverse(visitor);

        Assert.Equal(1, group.HardwareReadCount);
        Assert.Equal([first, second], visitor.Hardware);

        computer.Close();
    }

    [Fact]
    public void GetReport_ReusesOneCapturedSnapshotForEveryReportSection()
    {
        var group = new SnapshotGroup(new TestHardware("report"));
        var probe = new OpenProbe
        {
            AddGroups = add => add(group)
        };
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());

        computer.Open();
        string report = computer.GetReport();

        Assert.Equal(1, group.HardwareReadCount);
        Assert.Contains("Test hardware", report);

        computer.Close();
    }

    [Fact]
    public void SharedOwner_ReleasesProcessGlobalOnlyAfterLastLeaseCloses()
    {
        int openCount = 0;
        int closeCount = 0;
        var owner = new Computer.SharedOwner(
            () => openCount++,
            () => closeCount++);

        owner.Open();
        owner.Open();

        Assert.Equal(1, openCount);
        Assert.Equal(0, closeCount);

        owner.Close();
        Assert.Equal(0, closeCount);

        owner.Close();
        owner.Close();

        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void SharedOwner_FailedAcquisitionCleansPartialOwnerAndCanRetry()
    {
        var failure = new InvalidOperationException("global owner failed");
        int openCount = 0;
        int closeCount = 0;
        var owner = new Computer.SharedOwner(
            () =>
            {
                openCount++;
                if (openCount == 1)
                    throw failure;
            },
            () => closeCount++);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(owner.Open);
        Assert.Same(failure, thrown);
        Assert.Equal(1, closeCount);

        owner.Open();
        owner.Close();

        Assert.Equal(2, openCount);
        Assert.Equal(2, closeCount);
    }

    [Fact]
    public void TwoComputers_ShareGlobalOwnersUntilBothClose()
    {
        int mutexOpenCount = 0;
        int mutexCloseCount = 0;
        int opCodeOpenCount = 0;
        int opCodeCloseCount = 0;
        int hardwareIndex = 0;
        var mutexOwner = new Computer.SharedOwner(
            () => mutexOpenCount++,
            () => mutexCloseCount++);
        var opCodeOwner = new Computer.SharedOwner(
            () => opCodeOpenCount++,
            () => opCodeCloseCount++);
        var dependencies = new Computer.OpenDependencies(
            () => null,
            mutexOwner.Open,
            mutexOwner.Close,
            opCodeOwner.Open,
            opCodeOwner.Close,
            add => add(new TestGroup(new TestHardware($"shared-{++hardwareIndex}"))));
        var first = new Computer(new TestSettings(), dependencies);
        var second = new Computer(new TestSettings(), dependencies);

        first.Open();
        second.Open();

        Assert.Equal(1, mutexOpenCount);
        Assert.Equal(1, opCodeOpenCount);

        second.Close();

        Assert.Equal(0, mutexCloseCount);
        Assert.Equal(0, opCodeCloseCount);
        Assert.Single(first.Hardware);

        first.Close();

        Assert.Equal(1, mutexCloseCount);
        Assert.Equal(1, opCodeCloseCount);
    }

    private static int GetGroupCount(Computer computer)
    {
        FieldInfo groupsField = typeof(Computer).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic);
        var groups = Assert.IsType<List<IGroup>>(groupsField?.GetValue(computer));
        return groups.Count;
    }

    private sealed class OpenProbe
    {
        public Action<Action<IGroup>> AddGroups { get; set; }

        public int CloseMutexesCount { get; private set; }

        public int CloseOpCodeCount { get; private set; }

        public int CreateSmbiosCount { get; private set; }

        public int OpenMutexesCount { get; private set; }

        public int OpenOpCodeCount { get; private set; }

        public Computer.OpenDependencies CreateDependencies()
        {
            return new Computer.OpenDependencies(
                () =>
                {
                    CreateSmbiosCount++;
                    return null;
                },
                () => OpenMutexesCount++,
                () => CloseMutexesCount++,
                () => OpenOpCodeCount++,
                () => CloseOpCodeCount++,
                add => AddGroups(add));
        }

        public void AssertCounts(int openCount, int closeCount)
        {
            Assert.Equal(openCount, CreateSmbiosCount);
            Assert.Equal(openCount, OpenMutexesCount);
            Assert.Equal(openCount, OpenOpCodeCount);
            Assert.Equal(closeCount, CloseOpCodeCount);
            Assert.Equal(closeCount, CloseMutexesCount);
        }
    }

    private sealed class TestGroup : IGroup
    {
        private readonly Exception _closeFailure;

        public TestGroup(IHardware hardware, Exception closeFailure = null)
        {
            Hardware = [hardware];
            _closeFailure = closeFailure;
        }

        public int CloseCount { get; private set; }

        public IReadOnlyList<IHardware> Hardware { get; }

        public void Close()
        {
            CloseCount++;

            if (_closeFailure != null)
                throw _closeFailure;
        }

        public string GetReport() => null;
    }

    private sealed class SnapshotGroup : IGroup
    {
        private readonly IReadOnlyList<IHardware> _hardware;

        public SnapshotGroup(params IHardware[] hardware)
        {
            _hardware = Array.AsReadOnly(hardware);
        }

        public int HardwareReadCount { get; private set; }

        public IReadOnlyList<IHardware> Hardware
        {
            get
            {
                HardwareReadCount++;
                return _hardware;
            }
        }

        public void Close()
        {
        }

        public string GetReport() => null;
    }

    private sealed class RecordingVisitor : IVisitor
    {
        public List<IHardware> Hardware { get; } = new();

        public void VisitComputer(IComputer computer)
        {
        }

        public void VisitHardware(IHardware hardware)
        {
            Hardware.Add(hardware);
        }

        public void VisitParameter(IParameter parameter)
        {
        }

        public void VisitSensor(ISensor sensor)
        {
        }
    }

    private sealed class TestHardware : HardwareBase
    {
        public TestHardware(string identifier)
            : base("Test hardware", new Identifier(identifier), new TestSettings())
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
