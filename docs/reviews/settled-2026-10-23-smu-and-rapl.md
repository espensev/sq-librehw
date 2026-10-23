# Settled — Ryzen SMU replies and Intel RAPL limits

**Date:** 2026-10-23
**Status:** closed. Do not reopen these claims or schedule work from them.

## SMU reply validation

Closed claim: SMU reply validation is success-only. LibreHardwareMonitor checks `MP1_RSP == OK (0x1)` and otherwise throws. It does not parse `ALREADY_EXISTS` / `REJECTED_*` / `CMD_REJECTED_*`, so a rejected command is indistinguishable from a generic failure.

The C# mailbox path does not read `MP1_RSP`. `LibreHardwareMonitorLib/Hardware/RyzenSMU.cs` and `LibreHardwareMonitorLib/PawnIo/RyzenSmu.cs` call PawnIO ioctls (`ioctl_update_pm_table`, `ioctl_resolve_pm_table`, `ioctl_read_pm_table`). This checkout's history contains no `MP1_RSP`, `SendRsmuSmnMessage`, or `CMD_REJECTED`.

Reply mapping is in the PawnIO module `RyzenSMU.p`, function `smu_status_to_nt` ([namazso/PawnIO.Modules](https://github.com/namazso/PawnIO.Modules/blob/main/RyzenSMU.p)):

| Reply | Value | Driver result |
|---|---|---|
| OK | `0x01` | `STATUS_SUCCESS` |
| Rejected, busy | `0xFC` | `STATUS_DEVICE_BUSY` |
| Rejected, prerequisite | `0xFD` | `STATUS_INTERNAL_ERROR` |
| Unknown command | `0xFE` | `STATUS_INVALID_INFO_CLASS` |
| Failed | `0xFF` | `STATUS_UNSUCCESSFUL` |

`ALREADY_EXISTS` is not a Ryzen SMU reply. The documented set is `0x00` (still busy), `0x01`, `0xFC`, `0xFD`, `0xFE`, `0xFF`, plus amdgpu's `0xFB` debug-dump end. This module leaves `0xFB` unnamed and returns `STATUS_NOT_IMPLEMENTED`.

`PawnIo.Execute` drops a failed ioctl and returns a zero buffer. `UpdatePmTable` ignores that buffer. `GetPmTable` retries once when the call throws or the first table value is 0, then returns the table or `[0]`. `ExecuteHr` in the same file already returns the Win32 code and is unused on this path. The sensor layer therefore does not show which reply occurred. The embedded `Resources/PawnIo/RyzenSMU.bin` was not byte-compared with the module source above.

Switching those calls to `ExecuteHr` would only make the existing code visible in a report. That diagnostic is not scheduled. Do not add an `ALREADY_EXISTS` parser.

## Intel package and platform power limits

Closed claim: this tree reads or writes Intel package or platform RAPL limits.

`LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs` reads energy counters and the unit MSR only:

| MSR | Address |
|---|---|
| Package energy | `0x611` |
| PP0 energy | `0x639` |
| PP1 energy | `0x641` |
| DRAM energy | `0x619` |
| Platform energy | `0x64D` |
| RAPL power unit | `0x606` |

There is no `MSR_PKG_POWER_LIMIT` (`0x610`) and no `MSR_PLATFORM_POWER_LIMIT` (`0x65C`). `LibreHardwareMonitorLib/PawnIo/IntelMsr.cs` exposes `ReadMsr` only.

Reading `0x610`, or writing any power limit, would be a new feature. It is not scheduled.
