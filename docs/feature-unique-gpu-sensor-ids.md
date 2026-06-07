# Feature Spec: Unique NVIDIA GPU sensor identifiers (12VHPWR voltage collision)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Verified <!-- Draft | Accepted | Implemented | Verified | Done -->
**Updated:** 2026-06-07
**Related docs:** [`feature-workflow.md`](feature-workflow.md), [`local-ui-customizations.md`](local-ui-customizations.md); GitHub issue #4
**Purpose:** make every NVIDIA GPU sensor `Identifier` unique so identifier-keyed consumers (CSV, `data.json`, Prometheus, persisted plot state) are unambiguous.

## 1. Summary

On ASUS RTX 50-series ("Astral") cards the 12VHPWR per-pin **voltage** sensors were created starting at
sensor index 0, the index already used by `GPU Core Voltage`. Two distinct sensors therefore shared the
identifier `/gpu-nvidia/<n>/voltage/0`. The fix shifts the 12VHPWR voltage pins to indices 1–6 (matching
the existing Current block) so every identifier is unique, and hardens the CSV logger so a single sensor
can never be fanned into multiple columns.

## 2. Problem and Motivation

A sensor `Identifier` is `/{hardware}/{type}/{index}` and is the contract key for identifier-keyed
consumers. On the maintainer's RTX 5090 (ASUS Astral):

- `GPU Core Voltage` → `/gpu-nvidia/0/voltage/0` (`NvidiaGpu.cs` ~line 336)
- `12VHPWR Pin 1` → `/gpu-nvidia/0/voltage/0` (`NvidiaGpu.cs` ~line 468) — **identical identifier**

Confirmed across surfaces: the CSV header row 1 contained `/gpu-nvidia/0/voltage/0` twice (`GPU Core
Voltage` and `12VHPWR Pin 1`), and `data.json` returned two sensor nodes with `SensorId =
/gpu-nvidia/0/voltage/0`. This breaks any downstream that keys on `Identifier` (the fork's output feeds
such a consumer), and it is a latent correctness bug in the CSV logger's resume/hot-plug path: the
match loops in `Logger.SensorAdded` / `OpenExistingLogFile` had no `break`, so one sensor whose
identifier is duplicated was written into **both** columns after a Daily-rotation reopen or a hot-plug
add (silently overwriting one real sensor's data).

The 12VHPWR **Current** (indices 1–6) and **Power** (2–7) blocks did not collide; only **Voltage** did,
because it started at 0 instead of 1.

## 3. Goals and Non-Goals

**Goals**

- Every `ISensor.Identifier` on the NVIDIA GPU is unique.
- The CSV logger maps each sensor to exactly one column, even if a duplicate identifier ever recurs.

**Non-goals**

- No change to friendly names, values, ordering, or any non-NVIDIA hardware.
- No change to `GPU Core Voltage` (`/voltage/0`), or to the 12VHPWR Current/Power identifiers.
- Not adding identifier de-duplication/suffixing in the logger (the root-cause fix makes it unnecessary,
  and a `#2` suffix would itself be a non-standard contract change).

## 4. Behavior Specification

- **NVIDIA backend (`NvidiaGpu.cs`).** The 12VHPWR pin **voltage** sensors are created with indices
  **1–6** instead of 0–5. The constructor index only feeds the `Identifier`; values are assigned by
  array position (`_12VHPwrPinVoltageSensors[i].Value`), so the shift is identifier-only.
- **Resulting identifiers (Astral cards only):** `GPU Core Voltage` → `/voltage/0` (unchanged);
  `12VHPWR Pin 1..6` → `/voltage/1..6` (was `/voltage/0..5`).
- **Logger (`Logger.cs`).** `SensorAdded` and `OpenExistingLogFile`'s match loops `break` on the first
  identifier match, so one sensor populates only one column. Defensive: with unique identifiers this is
  a no-op in the common case, but it removes the fan-out failure mode for any future collision.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | None. |
| Settings/config | Persisted plot/selection state keyed by identifier for `12VHPWR Pin 1..6` voltage now uses `/voltage/1..6`; old `/voltage/0..5` keys for those pins become stale (graph pen color/visibility may reset for the 5 pins once). |
| Remote web/API | `data.json` `SensorId` and `/metrics` `sensorId` for `12VHPWR Pin 1..6` voltage change to `/voltage/1..6`; the duplicate `SensorId` is gone. |
| Logging/files | New CSV files: `12VHPWR Pin 1..6` voltage identifier columns are `/voltage/1..6`; no duplicate identifier columns. Existing CSV files are not rewritten. |
| Hardware/admin flow | None. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Edits shared `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs`. This is an upstream defect (12VHPWR voltage pins colliding with GPU Core Voltage); candidate for an upstream bug report/PR so the fork can drop the local delta later. |
| `net472` vs `net10.0-windows` | Framework-agnostic; both targets built clean. |
| DPI/multi-monitor | Not applicable. |
| Hardware/admin rights | Unchanged. Pin sensors only exist on ASUS Astral subsystem IDs. |
| Existing settings/users | Downstream identifier-keyed consumers remap `12VHPWR Pin 1..6` voltage once (they are mis-keyed today regardless). Persisted plot state for those 5 pins may reset once. |

## 7. Acceptance Criteria

- [x] `data.json` contains no duplicate `SensorId` (`/gpu-nvidia/0/voltage/0` appears once: GPU Core Voltage; 0 duplicate SensorIds across the 533-node tree).
- [x] `GPU Core Voltage` = `/gpu-nvidia/0/voltage/0`; `12VHPWR Pin 1` = `/gpu-nvidia/0/voltage/1`.
- [x] A newly created CSV log has a fully unique identifier header row (533/533 unique).
- [x] 12VHPWR Current/Power pin identifiers are unchanged (only the Voltage block was shifted).
- [x] Both `net10.0-windows` and `net472` Release x64 build with 0 errors.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build ... -f net472 -p:Platform=x64` | 0 errors |
| data.json uniqueness | count `"SensorId":"/gpu-nvidia/0/voltage/0"` in `data.json` | 1 |
| data.json pin id | find `12VHPWR Pin 1` node | `SensorId` = `/gpu-nvidia/0/voltage/1` |
| CSV uniqueness | new `LibreHardwareMonitorLog-*.csv` header row 1: `uniq -d` over identifiers | empty (no duplicates) |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Report upstream and drop local delta after merge | follow-up | report as upstream bug; keep local fix until upstream ships |

## 10. Implementation Notes

- `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs`: 12VHPWR pin voltage indices 0–5 → 1–6.
- `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs`: `break` after first identifier match in
  `SensorAdded` and the `OpenExistingLogFile` visitor.
- The second collision the issue mentioned (`/load/3`, GPU Bus vs GPU Memory) was not reproducing in
  the current build (loads use a running `nextLoadIndex++`); only the voltage collision was live. If it
  recurs, the same root-cause approach (unique per-`(hardware, SensorType)` index) applies.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-07 | `-f net10.0-windows` + `-f net472` Release x64 to real output path | pass | both 0 warnings / 0 errors |
| 2026-06-07 | `GET /data.json` after relaunch | pass | `/gpu-nvidia/0/voltage/0` appears once (GPU Core Voltage); 12VHPWR Pin 1 = `/voltage/1`; 0 duplicate SensorIds across 533 nodes |
| 2026-06-07 | fresh CSV `LibreHardwareMonitorLog-2026-06-07-8.csv` header | pass | 533 id columns, 533 unique (no `uniq -d` output); Pin 1–6 = `/voltage/1–6`; was 453/452 with the collision |
