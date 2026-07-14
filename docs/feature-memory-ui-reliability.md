# Feature Spec: Memory, Lifetime, and UI Reliability

**Status:** shipped, deployed, and verified; follow-ups below

**Updated:** 2026-07-14

## Problem and motivation

The pre-patch application passed its automated suites but still had
definite managed/static-event and native GDI leaks, avoidable history and HTTP
allocation growth, unsafe shutdown/UI-thread ownership, synchronous UI stalls,
and web-dashboard state paths that can fabricate telemetry or retain stale work.

This work closed every actionable finding in the original findings-first
review without changing sensor truth, external payloads, the read-only
dashboard policy, or supported frameworks.

## Goals

- Make every long-lived event, task, native handle, form, timer, and drawing
  resource have an explicit, idempotent owner and teardown path.
- Bound sensor-history memory and make graph/metrics reads proportional to the
  requested or newly appended points.
- Stream and compact settings safely, preserve backup recovery, order concurrent
  saves, and bound decompression expansion.
- Keep shutdown exactly once and on the UI thread; drop late hardware callbacks.
- Bound HTTP request ownership and browser polling to one cancellable generation.
- Separate dashboard telemetry ingestion from cached rerendering.
- Make storage failures, empty states, focus, labels, and reduced-motion behavior
  safe and accessible.
- Remove avoidable UI-thread waits, duplicate image allocation, stale web-state
  cardinality, full DOM rebuilds, and nondeterministic modal-resource cleanup.
- Restore the contributor documentation map so the prescribed workflow points to
  files that actually exist.

## Non-goals

- No changes to `data.json`, CSV columns, Prometheus names/labels/units, sensor
  IDs, current/min/max semantics, or hardware polling cadence.
- No restored dashboard preview route or new server write capability.
- No automatic deletion of runtime logs or deployment/configuration changes.
- No upstream synchronization or unrelated visual redesign.
- No whole-branch merge of campaign/tracker artifacts from
  `codex/memory-reliability`.

## Required behavior and failure handling

### Memory and persistence

- Closing or resetting a hardware group releases all event subscriptions,
  devices, sensors, histories, tasks, and native resources it owns.
- Sensor history remains time-window compatible but retains at most 10,000
  representative points per sensor, preserving the newest point and old-bucket
  extrema. `ISensor` stays compatible through an optional history-reader seam.
- Default metrics reads only the latest value; archive requests read a bounded
  tail. Plot updates append deltas and rebuild only after reset/decimation.
- Settings XML is streamed, not loaded as a complete DOM. Removed oversized
  history and backup recovery mark settings dirty so autosave compacts them.
- Snapshot creation and disk write have one ordering boundary so an old save
  cannot overwrite a newer one.
- Persisted history decompression rejects malformed or expanded payloads beyond
  the retained record/byte budget before allocating an unbounded buffer.

### WinForms and native UI

- Session end, form close, and autosave converge on one UI-thread, exactly-once
  shutdown path; static `SystemEvents` handlers are detached.
- Hardware callbacks marshal through a captured live UI dispatcher and are
  discarded after closing begins. A missing/destroyed handle is not permission
  for background UI mutation.
- Gadget HBITMAP/DC selection, HWND, menu, ShowDesktop subscription, fonts,
  theme drawing resources, tree static events, tooltips, dialogs, icons, and
  cloned images are deterministically released.
- Tray retry, DNS resolution, server stop, PawnIO setup, and discovery do not
  sleep or wait for multi-second operations while blocking the UI thread.
- Repeated form/theme/font/gadget operations remain within a stable GDI-handle
  envelope.

### HTTP and dashboard

- Server handlers have bounded concurrency, are tracked, observe cancellation,
  and drain during stop. Slow response writes do not hold the serialization gate.
- Browser polling has at most one active request, aborts on pause/reconfigure,
  ignores stale generations, observes a timeout, and pauses while hidden.
- Cached rerenders paint only; they never increment samples, history, extrema,
  derived power limits, ticks, or telemetry persistence.
- Dashboard storage access is isolated behind a safe adapter with an in-memory
  fallback when access, quota, enumeration, or removal throws.
- Departed sensor state is pruned after a grace period and globally bounded.
- Stable dashboard regions reuse keyed DOM nodes or skip unchanged content rather
  than clearing and rebuilding complete sections on every poll.
- Every input has an accessible name, stateful controls expose state, keyboard
  focus remains valid, and reduced-motion behavior is preserved.

## Compatibility and risks

- Both `net472` and `net10.0-windows` x64 remain supported.
- `ISensor` and external HTTP/data contracts remain unchanged; optimized history
  is an optional additive interface with legacy fallbacks.
- Existing XML settings and browser-local `sq.dashboard.v1` state remain readable.
- A 24-hour graph still represents 24 hours, with older density compacted rather
  than truncating the selected time range.
- Admin/hardware access requirements do not change.
- Upstream-sync risk is concentrated in existing local-fork WinForms, settings,
  history, HTTP, and embedded-web surfaces.

## Acceptance criteria

- [x] Repeated storage reset/resume/toggle does not retain closed groups or add
  duplicate device-change callbacks.
- [x] A simulated 24 hours at 250 ms retains no more than 10,000 history points,
  preserves newest/extrema, and does not change current/min/max values.
- [x] Large-config loading stays bounded, compacts cleanup, orders overlapping
  saves, and rejects excessive decompression expansion.
- [x] Metrics and plots use bounded tail/delta reads and retain their existing
  public/visual contracts.
- [x] Session/form shutdown executes once on the UI thread and releases all
  static subscriptions and owned resources.
- [x] Repeated gadget resize, theme/font changes, tray failure, and modal dialogs
  do not leak GDI/native handles or freeze the UI.
- [x] Late hardware events cannot mutate controls before handle creation or after
  closing.
- [x] HTTP request bursts and slow clients stay within configured concurrency;
  stop cancels and drains active handlers.
- [x] Dashboard appearance changes leave telemetry sample counts and derived
  values byte-for-byte unchanged.
- [x] Poll pause/reconfigure/visibility changes leave no overlapping or stale
  request able to paint.
- [x] Throwing browser storage still boots with usable in-memory state.
- [x] Departed web sensor state is pruned and stable sections avoid full rebuilds.
- [x] UI inputs and stateful controls pass accessible-name/focus checks.
- [x] Contributor documentation contains no required links to absent files.
- [x] The golden `data.json` contract remains unchanged; all tests and both
  Release target builds pass.

## Open follow-ups

- [ ] Preserve the forced first dashboard snapshot when a persisted-paused page
  starts hidden, so first visibility does not stay blank until Resume.
- [ ] Preserve the last bounded on-disk sensor histories across autosave until a
  clean hardware close refreshes `/values`.
- [ ] Optional confidence gate: run a 60-minute current-commit live soak.

## Verification plan

Use red-capable regression tests at the owning seam before or with each fix:

- weak-reference/event-count reset tests for storage groups and static UI events;
- deterministic 24-hour history, bounded decompression, save-ordering, metrics,
  plot-delta, shutdown-coordinator, and HTTP-concurrency tests;
- Win32 GDI-count loops for gadget resize/theme/font where practical;
- Node tests with a throwing storage stub, delayed fetches, visibility/pause
  transitions, departed sensors, and cached Studio rerenders;
- manual current-HEAD smoke for graph, gadget, tray, Studio/Standard, pause,
  dark/light, narrow viewport, and web-server restart.

Required final commands:

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
node --test webtests\console.tests.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
git diff --check
```

Build Release outputs in a staging directory while the currently deployed
binary is running. After automated verification, run a bounded current-HEAD
memory/GDI/reset-resume/poll smoke; do not replace the deployed runtime without
separate maintainer approval.

## Verification log

- Implementation `5b9c6f9` deployed as `0.9.6+5b9c6f9.2026-07-14` through
  `\SevGrp\AdminTask\LibreHW-No-UAC`; 71 candidate files matched. Rollback:
  `C:\ProgramData\LibreHardwareMonitor\backups\20260714-042926-pre-5b9c6f9`.
- The task action was repaired from invalid command `:` to the deployed
  executable; its long-running settings now have no time limit or battery stop.
  It then passed a graceful close/start with one process, result `0x41301`, and
  healthy local routes.
  Broken-definition backup:
  `C:\ProgramData\LibreHardwareMonitor\backups\20260714-052253-task-fix`.
- Node passed 267/267 plus 3/3; .NET passed 124 with one opt-in skip; the golden
  `data.json` stayed unchanged; both x64 Release targets built cleanly.
- Browser, three staged resume cycles, 180 polls, 32 concurrent requests,
  shutdown, and deployed reset/restart smokes passed with bounded memory,
  GDI/USER, handles, and a clean browser console.
- Live `/`, `/data.json`, and `/metrics` returned 200; retired
  `/dash/cardtruth[/]` returned 404.
- A clean close persisted 475 valid bounded histories in 2,889,088 bytes; later
  autosaves compacted primary and backup to 22,643 bytes. The remaining history
  continuity edge is tracked above.
- Final fixed-point review found no high/medium residual. Detailed audit and
  measurement history remains in Git at `60b9e23`.
