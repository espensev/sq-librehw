// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Based on DiskInfoToolkit, Copyright (c) 2026 Florian K.

using System;
using DiskInfoToolkit.Smart;
using StorageDeviceDIT = DiskInfoToolkit.Devices.StorageDevice;

namespace LibreHardwareMonitor.Hardware.Storage;

/// <summary>
/// Preserves the pre-byte-migration host-counter values. DiskInfoToolkit 2.1.3
/// (7bc6437a) returned whole GiB for sector/MiB counters and unchanged GB counters;
/// 2.2.1 (87ffc498) returns bytes for both. These divisor rules mirror its
/// SmartAttributeSummarySettingsResolver. Review them on future toolkit updates.
/// </summary>
internal static class StorageHostCounterUnits
{
    internal const ulong BytesPerGiB = 1024UL * 1024UL * 1024UL;
    internal const ulong BytesPerGB = 1000UL * 1000UL * 1000UL;

    internal static ulong GetDivisor(StorageDeviceDIT storage)
    {
        SmartAttributeProfile profile = SmartAttributeProfileResolver.Resolve(storage);
        if (profile == SmartAttributeProfile.NVMe || storage.Nvme.SmartLogData?.Length > 0)
            return BytesPerGiB;

        string model = Trim(storage.ProductName);
        if (string.IsNullOrWhiteSpace(model))
            model = Trim(storage.DisplayName);
        string firmware = Trim(storage.ProductRevision);

        bool decimalUnits = profile switch
        {
            SmartAttributeProfile.Toshiba => !ContainsAny(model, "THNSNC", "THNSNJ", "THNSNK", "KSG60", "TL100", "TR150", "TR200"),
            SmartAttributeProfile.SanDisk or SmartAttributeProfile.SanDiskGb or SmartAttributeProfile.SanDiskHP or
                SmartAttributeProfile.SanDiskHPVenus or SmartAttributeProfile.SanDiskDell or SmartAttributeProfile.SanDiskLenovo or
                SmartAttributeProfile.SanDiskLenovoHelenVenus or SmartAttributeProfile.SanDiskCloud => SanDiskUsesDecimalUnits(model),
            SmartAttributeProfile.WDC => !ContainsAny(model, "SA530"),
            SmartAttributeProfile.Corsair => !ContainsAny(model, "Voyager GTX"),
            SmartAttributeProfile.SKhynix =>
                (ContainsAny(model, "HFS") && ContainsAny(model, "TND", "MND", "TNF")) || !ContainsAny(model, "SC311", "SC401"),
            SmartAttributeProfile.Phison => !StartsWith(firmware, "S9"),
            SmartAttributeProfile.Marvell => !(StartsWith(model, "LEXAR") && (StartsWith(firmware, "SN0") || StartsWith(firmware, "V6"))),
            SmartAttributeProfile.SSD => StartsWith(model, "LITEON S960"),
            SmartAttributeProfile.SandForce or SmartAttributeProfile.OczVector or SmartAttributeProfile.Kingston or
                SmartAttributeProfile.KingstonSuv or SmartAttributeProfile.KingstonDC500 or SmartAttributeProfile.KingstonSA400 or
                SmartAttributeProfile.Realtek or SmartAttributeProfile.Seagate or SmartAttributeProfile.SeagateBarraCuda or
                SmartAttributeProfile.SeagateIronWolf or SmartAttributeProfile.SiliconMotionCVC or SmartAttributeProfile.Maxiotek or
                SmartAttributeProfile.Recadata or SmartAttributeProfile.Ssstc => true,
            _ => false
        };

        return decimalUnits ? BytesPerGB : BytesPerGiB;
    }

    private static bool SanDiskUsesDecimalUnits(string model)
    {
        if ((ContainsAny(model, "X600") && ContainsAny(model, "2280")) || ContainsAny(model, "X400", "X300", "X110", "SD5"))
            return false;
        if (ContainsAny(model, "Z400"))
            return true;
        if (ContainsAny(model, "1006"))
            return false;
        if (ContainsAny(model, "G1001", "G1012"))
            return true;

        return !ContainsAny(model, "SSD P4", "SSD U100", "SSD U110", "SSD i100", "SSD i110", "pSSD", "iSSD P4", "SDSSDP", "SDSSDRC");
    }

    private static string Trim(string value) => value?.Trim('\0', ' ', '\t', '\r', '\n') ?? string.Empty;

    private static bool StartsWith(string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            if (value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
