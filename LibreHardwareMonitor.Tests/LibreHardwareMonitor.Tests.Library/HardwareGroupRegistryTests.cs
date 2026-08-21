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
using LibreHardwareMonitor.Hardware.Cpu;
using LibreHardwareMonitor.Hardware.Gpu;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;
using OperandType = System.Reflection.Emit.OperandType;

namespace LibreHardwareMonitor.Tests;

public sealed class HardwareGroupRegistryTests
{
    private static readonly OpCode[] _singleByteOpCodes = new OpCode[256];
    private static readonly OpCode[] _multiByteOpCodes = new OpCode[256];

    private static readonly HashSet<string> _categoryGateFields = new()
    {
        "_batteryEnabled",
        "_controllerEnabled",
        "_cpuEnabled",
        "_gpuEnabled",
        "_memoryEnabled",
        "_motherboardEnabled",
        "_networkEnabled",
        "_powerMonitorEnabled",
        "_psuEnabled",
        "_storageEnabled"
    };

    static HardwareGroupRegistryTests()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
                continue;

            ushort value = (ushort)opCode.Value;
            if (value < 0x100)
                _singleByteOpCodes[value] = opCode;
            else if ((value & 0xFF00) == 0xFE00)
                _multiByteOpCodes[value & 0xFF] = opCode;
        }
    }

    [Fact]
    public void Add_RegistersInOrderDedupesIgnoresNullAndReadsHardwareOnlyWithSubscribers()
    {
        var host = new RegistryHost();
        var registry = host.Registry;
        var silent = new DynamicGroup("silent", new TestHardware("silent"));
        var firstHardware = new TestHardware("first");
        var first = new DynamicGroup("first", firstHardware);
        var secondHardware = new TestHardware("second");
        var second = new DynamicGroup("second", secondHardware);
        var added = new List<IHardware>();

        registry.Add(null);
        registry.Add(silent);

        Assert.Equal(0, silent.HardwareReadCount);

        host.HardwareAdded += added.Add;
        registry.Add(first);
        registry.Add(second);
        registry.Add(first);
        registry.Add(null);

        Assert.Equal([silent, first, second], registry.Groups);
        Assert.Equal([firstHardware, secondHardware], added);
        Assert.Equal(1, first.HardwareReadCount);
        Assert.Equal(1, second.HardwareReadCount);
        first.AssertSubscriptionCounts(1, 0);
        second.AssertSubscriptionCounts(1, 0);
    }

    [Fact]
    public void HardwareChangedEvents_ForwardOnlyWhileGroupIsRegistered()
    {
        var host = new RegistryHost();
        var registry = host.Registry;
        var firstHardware = new TestHardware("first");
        var liveHardware = new TestHardware("live");
        var lateHardware = new TestHardware("late");
        var group = new DynamicGroup("group", firstHardware);
        var added = new List<IHardware>();
        var removed = new List<IHardware>();
        host.HardwareAdded += added.Add;
        host.HardwareRemoved += removed.Add;

        registry.Add(group);
        group.RaiseAdded(liveHardware);
        group.RaiseRemoved(firstHardware);

        Assert.Equal([firstHardware, liveHardware], added);
        Assert.Equal([firstHardware], removed);

        registry.Remove(group);
        group.RaiseAdded(lateHardware);
        group.RaiseRemoved(liveHardware);

        Assert.Equal([firstHardware, liveHardware], added);
        Assert.Equal([firstHardware, liveHardware], removed);
        Assert.Equal(1, group.CloseCount);
        group.AssertSubscriptionCounts(1, 1);
    }

    [Fact]
    public void Remove_WhenUnsubscribeThrows_StillRemovesNotifiesClosesAndRethrowsFirstFailure()
    {
        var unsubscribeFailure = new InvalidOperationException("unsubscribe failed");
        var hardware = new TestHardware("hardware");
        var group = new DynamicGroup("group", hardware) { UnsubscribeFailure = unsubscribeFailure };
        var host = new RegistryHost();
        var registry = host.Registry;
        var removed = new List<IHardware>();
        host.HardwareRemoved += removed.Add;
        registry.Add(group);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => registry.Remove(group));

        Assert.Same(unsubscribeFailure, thrown);
        Assert.Empty(registry.Groups);
        Assert.Equal([hardware], removed);
        Assert.Equal(1, group.CloseCount);
        group.AssertSubscriptionCounts(1, 1);
    }

    [Fact]
    public void RemoveGroups_DrainsInExactReverseRegistrationOrder()
    {
        var closeOrder = new List<string>();
        var firstHardware = new TestHardware("first");
        var secondHardware = new TestHardware("second");
        var thirdHardware = new TestHardware("third");
        var first = new DynamicGroup("first", firstHardware) { CloseOrder = closeOrder };
        var second = new DynamicGroup("second", secondHardware) { CloseOrder = closeOrder };
        var third = new DynamicGroup("third", thirdHardware) { CloseOrder = closeOrder };
        var host = new RegistryHost();
        var registry = host.Registry;
        var removed = new List<IHardware>();
        host.HardwareRemoved += removed.Add;
        registry.Add(first);
        registry.Add(second);
        registry.Add(third);

        registry.RemoveGroups();

        Assert.Equal(["third.close", "second.close", "first.close"], closeOrder);
        Assert.Equal([thirdHardware, secondHardware, firstHardware], removed);
        Assert.Empty(registry.Groups);
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, second.CloseCount);
        Assert.Equal(1, third.CloseCount);
        first.AssertSubscriptionCounts(1, 1);
        second.AssertSubscriptionCounts(1, 1);
        third.AssertSubscriptionCounts(1, 1);
    }

    [Fact]
    public void Notifications_AreRaisedOutsideTheRegistryLock_ReentrantDrainDoesNotDeadlock()
    {
        var hardware = new TestHardware("reentrant");
        var group = new DynamicGroup("reentrant", hardware);
        var host = new RegistryHost();
        var registry = host.Registry;
        bool drainCompleted = false;
        host.HardwareAdded += _ =>
        {
            Task drain = Task.Run(registry.RemoveGroups);
            drainCompleted = drain.Wait(TimeSpan.FromSeconds(5));
            Assert.True(drainCompleted, "Registry notifications must be raised outside the registry lock.");
        };

        registry.Add(group);

        Assert.True(drainCompleted);
        Assert.Empty(registry.Groups);
        Assert.Equal(1, group.CloseCount);
        group.AssertSubscriptionCounts(1, 1);
    }

    [Fact]
    public void RemoveType_RethrowsFirstFailureAndRemovesEveryMatch()
    {
        var firstFailure = new InvalidOperationException("first close failed");
        var secondFailure = new InvalidOperationException("second close failed");
        var firstHardware = new TestHardware("first");
        var secondHardware = new TestHardware("second");
        var first = new DynamicGroup("first", firstHardware) { CloseFailure = firstFailure };
        var second = new DynamicGroup("second", secondHardware) { CloseFailure = secondFailure };
        var keep = new LiveListGroup(new List<IHardware>());
        var host = new RegistryHost();
        var registry = host.Registry;
        var removed = new List<IHardware>();
        host.HardwareRemoved += removed.Add;
        registry.Add(first);
        registry.Add(keep);
        registry.Add(second);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => registry.RemoveType<DynamicGroup>());

        Assert.Same(firstFailure, thrown);
        Assert.Equal([firstHardware, secondHardware], removed);
        Assert.Equal([keep], registry.Groups);
        Assert.Equal(0, keep.CloseCount);
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, second.CloseCount);
        first.AssertSubscriptionCounts(1, 1);
        second.AssertSubscriptionCounts(1, 1);
    }

    [Fact]
    public void RemoveGroups_WhenSnapshotAndCloseThrow_RethrowsFirstFailureAndClosesEverything()
    {
        var snapshotFailure = new InvalidOperationException("snapshot failed");
        var firstCloseFailure = new InvalidOperationException("first close failed");
        var secondCloseFailure = new InvalidOperationException("second close failed");
        var firstHardware = new TestHardware("first");
        var first = new DynamicGroup("first", firstHardware) { CloseFailure = firstCloseFailure };
        var second = new DynamicGroup("second", new TestHardware("second"))
        {
            SnapshotFailure = snapshotFailure,
            CloseFailure = secondCloseFailure
        };
        var host = new RegistryHost();
        var registry = host.Registry;
        var removed = new List<IHardware>();
        host.HardwareRemoved += removed.Add;
        registry.Add(first);
        registry.Add(second);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(registry.RemoveGroups);

        Assert.Same(snapshotFailure, thrown);
        Assert.Equal([firstHardware], removed);
        Assert.Empty(registry.Groups);
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, second.CloseCount);
    }

    [Fact]
    public void AddGroups_ConstructsCategoryGroupsInPinnedOrder()
    {
        string[] expected =
        [
            "_motherboardEnabled", "MotherboardGroup",
            "_cpuEnabled", "CpuGroup",
            "_memoryEnabled", "MemoryGroup",
            "_gpuEnabled", "AmdGpuGroup", "NvidiaGroup", "_cpuEnabled", "GetIntelCpus", "IntelGpuGroup",
            "_powerMonitorEnabled", "PowerMonitorGroup",
            "_controllerEnabled", "TBalancerGroup", "HeatmasterGroup", "AquaComputerGroup", "AeroCoolGroup",
            "NzxtGroup", "RazerGroup", "ArcticGroup", "MsiGroup",
            "_storageEnabled", "StorageGroup",
            "_networkEnabled", "NetworkGroup",
            "_psuEnabled", "CorsairPsuGroup", "MsiPsuGroup",
            "_batteryEnabled", "BatteryGroup"
        ];
        MethodInfo addGroups = GetPrivateComputerMethod("AddGroups");

        string[] actual = GetReferencedMembers(addGroups)
                         .Where(IsCategoryPolicyMember)
                         .Select(MemberName)
                         .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetIntelCpus_KeepsRegisteredCpuGroupLookupAndTemporaryFallback()
    {
        MethodInfo getIntelCpus = GetPrivateComputerMethod("GetIntelCpus");
        MemberInfo[] members = GetReferencedMembers(getIntelCpus).ToArray();

        string[] sequence = members
                           .Where(IsGetIntelCpusMember)
                           .Select(MemberName)
                           .ToArray();

        Assert.Equal(["_groups", "Find", "_settings", "CpuGroup", "get_Hardware"], sequence);

        MethodInfo predicate = Assert.Single(
            members.OfType<MethodInfo>(),
            m => m.Name.Contains("<GetIntelCpus>b__") && m.ReturnType == typeof(bool));
        Assert.Contains(GetReferencedMembers(predicate), member => member is Type { Name: "CpuGroup" });

        Assert.NotNull(
            typeof(IntelGpuGroup).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [typeof(List<IntelCpu>), typeof(ISettings)],
                null));
    }

    [Fact]
    public void Registry_ReadsLiveHardwareListsWithoutSnapshotAssumptions()
    {
        var firstHardware = new TestHardware("live-first");
        var secondHardware = new TestHardware("live-second");
        var liveHardware = new List<IHardware> { firstHardware };
        var group = new LiveListGroup(liveHardware);
        var dependencies = new Computer.OpenDependencies(
            () => null,
            () => { },
            () => { },
            () => { },
            () => { },
            add => add(group));
        var computer = new Computer(new TestSettings(), dependencies);

        computer.Open();

        Assert.Same(liveHardware, group.Hardware);
        Assert.Equal([firstHardware], computer.Hardware);

        liveHardware.Add(secondHardware);

        Assert.Same(liveHardware, group.Hardware);
        Assert.Equal([firstHardware, secondHardware], computer.Hardware);

        computer.Close();

        Assert.Equal(1, group.CloseCount);
    }

    [Fact]
    public void AmdAndIntelGpuGroups_ExposeTheirLiveHardwareListDirectly()
    {
        foreach (Type groupType in new[] { typeof(AmdGpuGroup), typeof(IntelGpuGroup) })
        {
            PropertyInfo hardware = groupType.GetProperty("Hardware");
            Assert.NotNull(hardware);

            MemberInfo[] members = GetReferencedMembers(hardware.GetGetMethod()).ToArray();

            Assert.Contains(members, member => member is FieldInfo { Name: "_hardware" });
            Assert.DoesNotContain(members, member => member is MethodInfo or ConstructorInfo);
        }
    }

    private static MethodInfo GetPrivateComputerMethod(string name)
    {
        MethodInfo method = typeof(Computer).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method;
    }

    private static bool IsCategoryPolicyMember(MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo field:
                return _categoryGateFields.Contains(field.Name);
            case ConstructorInfo constructor:
                return IsLibreHardwareGroup(constructor.DeclaringType);
            case MethodInfo method:
                return method.Name == "GetIntelCpus";
            default:
                return false;
        }
    }

    private static bool IsGetIntelCpusMember(MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo field:
                return field.Name is "_groups" or "_settings";
            case ConstructorInfo constructor:
                return constructor.DeclaringType?.Name == "CpuGroup";
            case MethodInfo method:
                return method.Name is "Find" or "get_Hardware";
            default:
                return false;
        }
    }

    private static bool IsLibreHardwareGroup(Type type)
    {
        return type != null &&
               type.FullName != null &&
               type.FullName.StartsWith("LibreHardwareMonitor.Hardware.", StringComparison.Ordinal) &&
               type.Name.EndsWith("Group", StringComparison.Ordinal);
    }

    private static string MemberName(MemberInfo member)
    {
        return member is ConstructorInfo constructor ? constructor.DeclaringType?.Name : member.Name;
    }

    private static IReadOnlyList<MemberInfo> GetReferencedMembers(MethodBase method)
    {
        var members = new List<MemberInfo>();
        byte[] il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null)
            return members;

        Type[] typeArguments = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
        Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
        Module module = method.Module;

        for (int position = 0; position < il.Length;)
        {
            OpCode opCode = ReadOpCode(il, ref position);
            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    continue;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineVar:
                    position += 1;
                    continue;
                case OperandType.InlineVar:
                    position += 2;
                    continue;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineR:
                    position += 4;
                    continue;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    continue;
                case OperandType.InlineSwitch:
                    position += 4 + 4 * BitConverter.ToInt32(il, position);
                    continue;
                case OperandType.InlineString:
                case OperandType.InlineSig:
                    position += 4;
                    continue;
            }

            int token = BitConverter.ToInt32(il, position);
            position += 4;

            MemberInfo member = ResolveMember(module, token, typeArguments, methodArguments);
            if (member != null)
                members.Add(member);
        }

        return members;
    }

    private static OpCode ReadOpCode(byte[] il, ref int position)
    {
        byte value = il[position++];
        if (value != 0xFE)
            return _singleByteOpCodes[value];

        return _multiByteOpCodes[il[position++]];
    }

    private static MemberInfo ResolveMember(Module module, int token, Type[] typeArguments, Type[] methodArguments)
    {
        try
        {
            return module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch
        {
            return null;
        }
    }

    private sealed class RegistryHost
    {
        private HardwareEventHandler _hardwareAdded;
        private HardwareEventHandler _hardwareRemoved;

        public RegistryHost()
        {
            Registry = new HardwareGroupRegistry(() => _hardwareAdded, () => _hardwareRemoved);
        }

        public event HardwareEventHandler HardwareAdded
        {
            add { _hardwareAdded += value; }
            remove { _hardwareAdded -= value; }
        }

        public event HardwareEventHandler HardwareRemoved
        {
            add { _hardwareRemoved += value; }
            remove { _hardwareRemoved -= value; }
        }

        public HardwareGroupRegistry Registry { get; }
    }

    private sealed class DynamicGroup : IGroup, IHardwareChanged
    {
        private readonly List<IHardware> _hardware = new();
        private readonly string _name;
        private HardwareEventHandler _hardwareAdded;
        private HardwareEventHandler _hardwareRemoved;
        private IReadOnlyList<IHardware> _snapshot;

        public DynamicGroup(string name, params IHardware[] hardware)
        {
            _name = name;
            _hardware.AddRange(hardware);
            PublishSnapshot();
        }

        public event HardwareEventHandler HardwareAdded
        {
            add
            {
                HardwareAddedSubscriptionCount++;
                _hardwareAdded += value;
            }
            remove
            {
                HardwareAddedUnsubscriptionCount++;
                if (UnsubscribeFailure != null)
                    throw UnsubscribeFailure;

                _hardwareAdded -= value;
            }
        }

        public event HardwareEventHandler HardwareRemoved
        {
            add
            {
                HardwareRemovedSubscriptionCount++;
                _hardwareRemoved += value;
            }
            remove
            {
                HardwareRemovedUnsubscriptionCount++;
                if (UnsubscribeFailure != null)
                    throw UnsubscribeFailure;

                _hardwareRemoved -= value;
            }
        }

        public Exception CloseFailure { get; set; }

        public int CloseCount { get; private set; }

        public IList<string> CloseOrder { get; set; }

        public IReadOnlyList<IHardware> Hardware
        {
            get
            {
                if (SnapshotFailure != null)
                    throw SnapshotFailure;

                HardwareReadCount++;
                return _snapshot;
            }
        }

        public int HardwareAddedSubscriptionCount { get; private set; }

        public int HardwareAddedUnsubscriptionCount { get; private set; }

        public int HardwareReadCount { get; private set; }

        public int HardwareRemovedSubscriptionCount { get; private set; }

        public int HardwareRemovedUnsubscriptionCount { get; private set; }

        public Exception SnapshotFailure { get; set; }

        public Exception UnsubscribeFailure { get; set; }

        public void AssertSubscriptionCounts(int subscriptionCount, int unsubscriptionCount)
        {
            Assert.Equal(subscriptionCount, HardwareAddedSubscriptionCount);
            Assert.Equal(subscriptionCount, HardwareRemovedSubscriptionCount);
            Assert.Equal(unsubscriptionCount, HardwareAddedUnsubscriptionCount);
            Assert.Equal(unsubscriptionCount, HardwareRemovedUnsubscriptionCount);
        }

        public void Close()
        {
            CloseCount++;
            CloseOrder?.Add($"{_name}.close");

            if (CloseFailure != null)
                throw CloseFailure;
        }

        public string GetReport() => null;

        public void RaiseAdded(IHardware hardware)
        {
            _hardware.Add(hardware);
            PublishSnapshot();
            _hardwareAdded?.Invoke(hardware);
        }

        public void RaiseRemoved(IHardware hardware)
        {
            _hardware.Remove(hardware);
            PublishSnapshot();
            _hardwareRemoved?.Invoke(hardware);
        }

        private void PublishSnapshot()
        {
            _snapshot = Array.AsReadOnly(_hardware.ToArray());
        }
    }

    private sealed class LiveListGroup : IGroup
    {
        private readonly List<IHardware> _hardware;

        public LiveListGroup(List<IHardware> hardware)
        {
            _hardware = hardware;
        }

        public int CloseCount { get; private set; }

        public IReadOnlyList<IHardware> Hardware => _hardware;

        public void Close()
        {
            CloseCount++;
        }

        public string GetReport() => null;
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
