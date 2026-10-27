# CSV clock recovery

**Status:** implemented, tested and reviewed; candidate and live state are recorded separately in release/operational manifests
**Updated:** 2026-10-27

## Problem

After a temporary host time-zone change, the long-running collector can retain
.NET's cached local zone. On SND-HOST this left CSV timestamps and daily rollover
six hours behind the host until the existing collector was restarted. Growing
CSV files alone did not reveal the fault; the live verifier now checks row time
against the file's local write time.

## Required behavior

- Refresh the process time-zone cache before the first emitted row and before
  the next emitted row once at least 60 seconds of monotonic time have elapsed
  since the last refresh. Skipped logging ticks do not clear the cache.
- Read local time after the refresh. Use that single reading for both the row
  timestamp and the daily filename, so a corrected zone cannot split them.
- Retain monotonic logging cadence and per-session gap detection. A backward
  wall-clock adjustment must not suppress writes or create an extra session.
- Daily mode follows the corrected local date in either direction. If that
  day's file already exists, preserve its existing bytes and append through
  the existing header-validation path. Per-session mode retains its existing
  continuous-session behavior even when a zone change crosses midnight.
- Keep `MM/dd/yyyy HH:mm:ss.fff`, sensor columns, numeric formatting, existing
  settings, API contracts and hardware polling unchanged. Older rows remain
  evidence; a time-zone correction may produce a visible clock jump.
- Refresh occurs on a due logging call, without timers, static event handlers,
  restarts, registry writes or a new background worker. Paused logging recovers
  on its next due call after the refresh interval has elapsed.

## Compatibility and boundaries

Use APIs supported by both net472 and net10.0-windows. The system clock adapter
uses Stopwatch, DateTime.Now and TimeZoneInfo.ClearCachedData; tests substitute
a clock with independent elapsed time and cached/system zones. Tests must write
real fixture CSV files without changing Windows time or querying hardware.

This is a source bugfix. Candidate packaging and live promotion remain separate
operations governed by the operational stack's exact-candidate approval gates.
The 2026-09-28 operational repair and its immutable evidence remain unchanged.

## Implementation and verification

1. Add regression cases in `CsvClockRecoveryTests.cs` for initially stale zones,
   forward/backward changes, daily rollover and append preservation, per-session
   continuity, and monotonic cadence after a wall-clock jump.
2. Introduce an internal clock adapter and apply the bounded refresh in
   `Logger.Log()` after the interval gate and before date selection.
3. Run the focused regressions, the complete .NET test solution filter, and
   Release x64 application builds for both supported frameworks. Request an
   independent review of the diff and address material findings.

Commands:

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -c Release -p:Platform=x64 --filter FullyQualifiedName~CsvClockRecoveryTests
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -c Release -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

## Acceptance

- [x] The stale-zone regressions fail before adding refresh logic.
- [x] The regressions pass against real fixture CSV output after the fix.
- [x] Existing suites and both framework builds pass.
- [x] Independent review has no unresolved material findings.
- [x] Source verification is explicitly distinguished from live acceptance.

## Verification record

On 2026-10-27, the unpublished SND-HOST history through `f0dadd06` was recovered
and integrated into the shared source. The current deterministic suites passed
583 tests (including all nine clock cases), with one existing opt-in skip.
Both Release x64 WinForms builds passed without warnings or errors. This
integration does not promote the recovered fix to either live runtime.

On 2026-09-28, the nine focused cases first produced eight stale-zone/rollover
failures and one passing cadence case before refresh logic was added. All nine
then passed. The complete .NET solution filter passed 383 tests with no
failures; one existing opt-in test,
`SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets`,
was skipped because its copied-config input was not supplied. Release x64
builds for net10.0-windows and net472 both passed with zero warnings/errors.

TRX results, build logs and baseline source hashes are under
`20260928-082226-csv-clock-recovery` below SND-HOST's `development.scratch`
path in its stack's `manifests/layout.json`.
The existing task-binding repairs remain intact. Independent review found no
material logger issues and led to additional CSV verifier fixes for blank rows,
concurrent/incomplete writes, and a stale cache in the invoking PowerShell
session. All review findings are closed. The final verifier fixture suite passed
under PowerShell 7 and Windows PowerShell 5.1; see
`feature-live-state-verification.md` for the revised snapshot contract.

The running collector was re-observed at 2026-09-28T08:46Z as PID 41032 with
the original `0.9.6+3ecc4d3.2026-08-22` payload and its expected hash. All 12 live
checks passed, including CSV growth and clock agreement. No candidate was
created and no product payload was deployed by this implementation.
