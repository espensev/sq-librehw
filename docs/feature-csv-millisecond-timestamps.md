# Feature Spec: CSV log millisecond timestamps (sub-second resolution)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Implemented <!-- Draft | Accepted | Implemented | Verified | Done -->
**Updated:** 2026-06-13
**Related docs:** [`feature-workflow.md`](feature-workflow.md), [`local-ui-customizations.md`](local-ui-customizations.md), [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md) (the other CSV-contract change); GitHub issue #9
**Purpose:** give every CSV log row a sub-second (millisecond) timestamp so sub-second samples stop colliding on a duplicate whole second and keep their true ordering for identifier-/time-keyed consumers.

## 1. Summary

The CSV logger formatted each row's timestamp with the .NET general specifier `"G"`
(`MM/dd/yyyy HH:mm:ss`, no fractional seconds) even though it samples faster than 1 Hz. In real
daily logs this collapsed ~1 in 4 samples onto a duplicate second, losing their sub-second position
and their ordering relative to a same-second neighbour. The fix appends `.fff` (milliseconds) to the
existing layout: `MM/dd/yyyy HH:mm:ss.fff`. The higher-resolution time was already available at the
call site (`DateTime.Now`); only the *formatting* dropped it.

## 2. Problem and Motivation

`LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs` built the timestamp column as:

```csharp
row.Append(now.ToString("G", CultureInfo.InvariantCulture));   // -> "05/28/2026 16:11:57"
```

Measured directly from real exports captured on the maintainer's machine:

| log slice | data rows | distinct timestamps | seconds carrying ≥2 rows | collision rate |
|---|---|---|---|---|
| partial-day | 815 | 612 | 203 | ~25% |
| day (237 MB head+tail) | 400 | 328 | 72 | ~18% |

Two consecutive rows sharing one second become time-ambiguous:

```
05/28/2026 16:11:57, ...
05/28/2026 16:11:57, ...
```

**Why it matters.** These logs feed **ThermalTrace**, a sub-second thermal tracer that correlates LHM
data against HWiNFO logs (true 30–90 ms millisecond timestamps). At 1-second LHM resolution:
fan-response delay and thermal-transient onset are quantized to ±1 s; ~25% of samples lose ordering
relative to their same-second neighbour; per-interval heuristics (sampling rate, gap detection)
become approximate wherever collisions occur. The fork's CSV output is an external contract for that
consumer ([[repo-feeds-downstream-consumer]]).

## 3. Goals and Non-Goals

**Goals**

- Each CSV row timestamp carries millisecond resolution, so sub-second samples are distinct and ordered.
- The visible layout is otherwise unchanged: existing second-resolution readers keep working.

**Non-Goals**

- No change to the header rows, the identifier row, the friendly-name row, sensor values, or column
  ordering.
- No change to the file-name date format (`LibreHardwareMonitorLog-yyyy-MM-dd[-n].csv`).
- Not switching to ISO-8601 (the considered alternative; would change the visible date layout — see §9).
- No attempt to raise `DateTime.Now`'s own tick resolution; this is a formatting-only fix.

## 4. Behavior Specification

- **Logger (`Logger.cs`).** The per-row timestamp is produced by a single internal helper
  `FormatRowTimestamp(DateTime)` using the constant format `RowTimestampFormat = "MM/dd/yyyy HH:mm:ss.fff"`
  with `CultureInfo.InvariantCulture`. The row builder calls it in place of the old inline `"G"` format.
- **Resulting column:** `05/28/2026 16:11:57` → `05/28/2026 16:11:57.123`. The leading fields are
  byte-for-byte the legacy `"G"` output; only `.fff` is appended.
- **Resume/parse path:** unchanged. `TryOpenExistingLogFile` parses only the first (identifier) header
  line and never reads the timestamp column, so the format change cannot affect Daily-rotation reopen
  or column matching.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | None. |
| Settings/config | None. |
| Remote web/API | None. `data.json` / `/metrics` do not carry this timestamp; the data.json golden master is unaffected. |
| Logging/files | New CSV rows carry `.fff` milliseconds in the `Time` column. The column header (`Time`) and all other columns are unchanged. Existing log files are not rewritten. A Daily-rotation file written across the upgrade contains second-resolution rows before the upgrade and `.fff` rows after, under one unchanged header — both parse. |
| Hardware/admin flow | None. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Downstream consumer | ThermalTrace's LHM parser was updated ahead of time to accept **both** the second-only `"G"` form and the `.fff` form (US and ISO variants), so no coordinated release is required and existing second-only logs keep working. |
| Second-resolution readers | `.fff` is appended to an unchanged prefix; a reader that parses `MM/dd/yyyy HH:mm:ss` and ignores trailing characters is unaffected. A reader using strict `ParseExact("G")` on new rows must add the `.fff` field — covered by the consumer update above. |
| Upstream sync | Edits `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs` (fork-only WinForms project). Deliberate local-fork divergence from upstream's second-resolution `"G"`; low merge-conflict surface (one helper + one call site). |
| `net472` vs `net10.0-windows` | Framework-agnostic string formatting; both targets build clean. |
| Culture | `CultureInfo.InvariantCulture` is used explicitly, so a non-US/comma-decimal machine locale produces identical bytes (pinned by test). |

## 7. Acceptance Criteria

- [x] CSV row timestamps include milliseconds: `MM/dd/yyyy HH:mm:ss.fff`.
- [x] The output's leading fields are identical to the legacy `"G"` form (only `.fff` appended).
- [x] Formatting is culture-invariant (same bytes under a non-US locale).
- [x] Two samples 1 ms apart format to distinct strings.
- [x] Output round-trips through `DateTime.ParseExact(RowTimestampFormat, InvariantCulture)`.
- [x] `data.json` golden-master tests stay green (serialization path untouched).
- [x] Both `net10.0-windows` and `net472` Release x64 build with 0 warnings / 0 errors.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Unit / contract tests | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | all pass (5 timestamp + 2 golden) |
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build ... -f net472 -p:Platform=x64` | 0 errors |
| Runtime CSV (maintainer) | enable logging, capture a fresh `LibreHardwareMonitorLog-*.csv`, inspect the `Time` column | timestamps carry `.fff`; faster-than-1 Hz rows no longer share a whole second |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| US-locale `.fff` vs ISO-8601 `yyyy-MM-ddTHH:mm:ss.fff` | n/a (decided) | Keep the US-locale `.fff` (issue's "primary", least disruptive — preserves the existing visible layout). ISO-8601 was the lexically-sortable alternative; not adopted to avoid changing the date layout. |

## 10. Implementation Notes

- `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs`: added `internal const RowTimestampFormat`
  and `internal static FormatRowTimestamp(DateTime)`; the row builder now calls the helper instead of
  inlining `now.ToString("G", ...)`. Extracted as `internal` so the contract is unit-testable via the
  existing `InternalsVisibleTo("LibreHardwareMonitor.Tests")`.
- `LibreHardwareMonitor.Tests/CsvTimestampContractTests.cs`: 5 facts locking layout, legacy-prefix
  compatibility, culture-invariance, round-trip, and sub-second distinctness.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-13 | `dotnet test` (x64) | pass | 7/7 (5 CSV-timestamp contract + 2 data.json golden) |
| 2026-06-13 | `-f net10.0-windows` + `-f net472` Release x64 (redirected temp `OutDir`; normal output path locked by the running app) | pass | both 0 warnings / 0 errors |
| _pending_ | maintainer launch: fresh CSV `Time` column | _outstanding_ | runtime capture of `.fff` rows; the emitted format is identical to the unit-pinned helper, so this confirms wiring rather than format |
