# Review - Corsair PSU discovery and shared access

**Date:** 2026-10-07  
**Surface:** fixed-point diff `fd6f106a..e8d75cec`; installed implementation `07d2d25e`  
**Spec source:** `docs/features/feature-memory-ui-reliability.md`, `docs/features/feature-local-release-system.md`, and the request to restore HX1500i readings using C#  
**Standards sources:** `AGENTS.md`, `docs/README.md`, `.codex/skills/project.toml`, `.editorconfig`  
**Verdict:** FAIL - two medium-severity edge-case defects remain. The normal installed path is working.

## Findings

### Medium

1. **[regression] Repeated mutex timeouts leave stale readings exposed as current.**
   `LibreHardwareMonitorLib/Hardware/Psu/Corsair/CorsairPsu.cs:81` returns from
   `Update()` without changing any prior sensor value or recording its age.
   After one successful poll, repeated lock misses can preserve those readings
   indefinitely. `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:828`
   treats the return as a successful poll and proceeds to logging;
   `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs:440` writes
   `sensor.Value` into the current row. The HTTP snapshot also reads that
   value directly at
   `LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs:58`.
   The result is apparently current telemetry during a monitoring outage.

   Evidence: the isolated C# review probe seeded readings, held its own private
   mutex on another thread, and called `Update()` three times. After 1,515 ms,
   no device streams had opened, but all 18 fixture readings remained non-null
   and unchanged. This fixture advertises optional input current; the real
   HX1500i has 17 readings. The current busy-lock test at
   `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/CorsairPsuAccessTests.cs:100`
   checks only that no I/O occurs and never seeds or checks earlier values.

   Recommendation: define a bounded stale-value policy, then invalidate expired
   readings or expose their sample age. Include derived power readings and
   verify CSV/HTTP behavior after repeated failed polls and recovery.

2. **[regression/spec] A failed earlier PSU transfers its identity and saved labels to a later PSU.**
   `LibreHardwareMonitorLib/Hardware/Psu/Corsair/CorsairPsuGroup.cs:60` uses
   `_hardware.Count` as the identifier index. The new per-device catch skips
   failed devices without reserving their index. With the same two connected
   devices in the same enumeration order, PSU B changes from
   `/psu/corsair/1` to `/psu/corsair/0` when PSU A fails initialization.
   This redirects saved sensor labels and downstream identifier bindings to
   a different physical PSU, contrary to the sensor-ID preservation constraint
   in `docs/features/feature-memory-ui-reliability.md:42`.

   Evidence: the review probe first discovered A and B with separate saved
   temperature labels. It then made only A's vendor reply fail. B was discovered
   as `/psu/corsair/0`, and its temperature name became `PSU A temperature`.
   The existing discovery test at
   `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/CorsairPsuDiscoveryTests.cs:100`
   explicitly expects compressed indices, so it currently endorses the defect.

   Recommendation: reserve the supported-device enumeration slot before
   initialization, including on failure, without changing healthy-device IDs.
   Test failed-first, failed-middle, and recovered-device opens against the
   same persisted settings. This addresses failure-induced renumbering;
   general USB enumeration reordering is a separate pre-existing limitation.

### High

No high-severity finding established.

### Low

No additional low-severity finding established.

## Verification

- `git log -6 --oneline`, `git diff --stat fd6f106a..e8d75cec`, and the complete
  eight-file diff resolved the intended change set. The reviewed worktree was
  clean. The main checkout's existing deletions and audit artifacts were
  identified separately and excluded.
- `git diff --check fd6f106a..e8d75cec` passed.
- `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/LibreHardwareMonitor.Tests.Library.csproj -c Release -p:Platform=x64 --filter FullyQualifiedName~CorsairPsu --no-restore --logger "console;verbosity=minimal"`
  passed 22/22 tests during this review.
- `dotnet run --project D:/DevHome/tmp/librehw-psu-crossreview-20261007/ReviewProbe.csproj -c Release -p:Platform=x64`
  reproduced both findings. It links the existing fake HID fixture, uses an
  isolated local mutex, and does not contact the physical PSU.
- A fresh `Invoke-RestMethod` read of
  `http://127.0.0.1:18085/data.json` found 17/17 valid Corsair readings.
  `Get-FileHash` matched the installed `07d2d25e` release manifest. HWiNFO,
  iCUE, and Corsair.Service were running with their earlier process IDs.
- The retained `publish-mutex.log` confirms the previous release gate of
  436 passing tests, one existing skip, and both x64 framework builds with zero
  warnings/errors. Full release gates were not rerun for this review because
  product code did not change.

Review evidence: `D:\DevHome\tmp\librehw-psu-crossreview-20261007` contains
`Program.cs`, `ReviewProbe.csproj`, `edge-repro.log`, and `live-review.json`.
Prior release evidence remains under
`D:\DevHome\tmp\librehw-psu-discovery-20261007`.

## Coverage notes

Deep-reviewed changed files:

- `LibreHardwareMonitorLib/Hardware/Mutexes.cs`
- `LibreHardwareMonitorLib/Hardware/Psu/Corsair/CorsairPsu.cs`
- `LibreHardwareMonitorLib/Hardware/Psu/Corsair/CorsairPsuGroup.cs`
- `LibreHardwareMonitorLib/Hardware/Psu/Corsair/UsbApi.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/CorsairPsuAccessTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/CorsairPsuDiscoveryTests.cs`
- `docs/features/feature-memory-ui-reliability.md` - changed contract, acceptance, and evidence sections
- `docs/features/feature-local-release-system.md` - changed release status and repair record

Caller checks covered `Computer` mutex lifetime, `Hardware`/sensor settings,
`CompositeSensor`, the update visitor, background poll completion, CSV logging,
and HTTP snapshot capture. No new PowerShell runtime dependency or additional
package dependency was introduced. The lock scope, `finally` release paths,
bounded identity reads, and truncated-reply checks were consistent with the
implemented normal-path contract. Broad exception handling in discovery still
excludes unexpected programming errors.

Unrelated source changes, other hardware drivers, and other PSU models were
excluded. Current HX1500i runtime success does not test those models' timeout
compatibility. This review is a manual cross-check with executable fault
reproductions; no independent agent review is claimed.

## Open questions and next action

- Select a short stale-value grace period or immediate unavailability behavior
  before implementing finding 1. Repeated contention must not create unlimited
  apparently fresh samples.
- Address both findings with focused regression tests, then repeat the current
  live coexistence check. The existing runtime was not restarted or replaced
  during this review.
- Current unresolved findings are also tracked in the
  [reliability feature spec](../features/feature-memory-ui-reliability.md#open-follow-ups).
