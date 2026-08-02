# Feature Spec: Memory, Lifetime, and UI Reliability

**Status:** reliability baseline shipped and deployed; 2026-07-25 hardware-option
queue, scrollbar teardown, transactional open, and dynamic-group snapshot fixes
deployed and live-verified on SND-HOST

**Updated:** 2026-08-02

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
- Keep state-changing HTTP actions off GET and reject cross-origin browser
  mutations before changing sensor state.
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
- No campaign or tracker artifacts are part of the shipped product contract.

## Required behavior and failure handling

### Memory and persistence

- Closing or resetting a hardware group releases all event subscriptions,
  devices, sensors, histories, tasks, and native resources it owns.
- `Computer.Open()` is transactional. A constructor, group-add, or consumer
  event failure unwinds partial groups and attempted global owners in reverse
  order, preserves the initiating exception even if cleanup also fails, leaves
  `Hardware` empty, and permits an idempotent close and clean retry.
- `Computer.Open()` and `Computer.Reset()` commit a replacement group
  generation only when the desired hardware-option version remains stable
  across construction. A reentrant option change discards that generation and
  rebuilds from the latest flags; continuous churn stops after four attempts,
  cleans the partial generation, and reports a deterministic failure.
- Process-global Mutex, OpCode, and NVIDIA ML ownership is reference-counted
  across concurrent `Computer` instances. One close or failed acquisition
  cannot release another open instance's lease.
- Dynamic Storage and NVIDIA groups publish stable read-only copy-on-write
  hardware snapshots. A Computer traversal/report/removal captures a snapshot
  once instead of combining count/index/enumeration reads from different
  generations. Close publishes empty state before external cleanup, and a late
  or blocked refresh cannot republish hardware after ownership is detached.
- Storage subscribes before its initial enumeration behind a serialized,
  bounded initialization gate. Synchronous changes are coalesced by stable
  device identity and replayed once; overflow fails and rolls back instead of
  dropping topology. Failed unsubscribe ownership remains retryable without
  closing devices twice.
- An NVIDIA monitor-cycle failure is retained in a bounded error history and
  does not stop later hot-plug refreshes. An in-flight refresh cannot reacquire
  NVIDIA ML after close, including when enumeration was blocked. Once
  detachment commits, the group's NVIDIA ML lease is released before external
  callbacks or device cleanup can block.
- Sensor history remains time-window compatible but retains at most 10,000
  representative points per sensor, preserving the newest point and old-bucket
  extrema. `ISensor` stays compatible through an optional history-reader seam.
- Default metrics reads only the latest value; archive requests read a bounded
  tail. Plot updates append deltas and rebuild only after reset/decimation.
- Settings XML is streamed, not loaded as a complete DOM. Removed oversized
  history and backup recovery mark settings dirty so autosave compacts them.
- Snapshot creation and disk write have one ordering boundary so an old save
  cannot overwrite a newer one.
- One internal `SettingsPersistenceCoordinator` owns the synchronous sequence
  of projecting current state, applying the autosave-only dirty skip, validating
  the primary/backup/staging paths, and delegating the write. `MainForm` retains
  all concrete UI/server reads, settings keys and values, the autosave timer,
  messages, and final-shutdown placement. `PersistentSettings` remains the sole
  ordered atomic backup-aware store and still owns modified-state transitions,
  load recovery, duplicate normalization, and stale-history compaction.
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
- Hardware-option clicks capture their desired value before asynchronous work.
  One bounded drain retains the latest pending value per option, preserves
  request order across distinct options, and never treats the disabled busy
  presentation as permission to discard a click. Manual and resume resets are
  coalesced and run after pending option values; shutdown cancels and drains the
  coordinator before disposing its lifecycle gate.
- One internal `ApplicationLifecycleCoordinator` now owns initialization,
  lifecycle cancellation and admission, the existing ordered option/reset
  coordinator, independent single-flight poll admission/completion, and
  idempotent drain/close sequencing. `MainForm` retains composition and policy.
  PawnIO prompt/install still occurs before `Computer.Open`, but now runs after
  the asynchronously released lifecycle start barrier rather than synchronously
  during `MainForm` construction. This observable timing deviation is policy-
  and outcome-neutral and safer: shutdown can observe and drain the complete
  prompt/install/open sequence before closing hardware.
- Option setters invoked reentrantly during open, close, or reset retain the
  requested backing flag but do not mutate the group collection being built or
  drained. Open/reset version reconciliation either rebuilds from those latest
  flags or fails cleanly at the bounded churn limit. Normal open-state option
  changes keep their existing synchronous commit-on-success behavior.
- Repeated form/theme/font/gadget operations remain within a stable GDI-handle
  envelope.
- The sensor-tree scroll indicators mirror the native system-metric hit area
  instead of collapsing it to a thin overlay. The painted hit target exposes a
  distinct UI Automation `ScrollBar` with writable `RangeValue` that mirrors the
  native scrollbar range and value. Its resting thumb has at least 3:1 contrast
  in Light, Dark, and Black, grows and brightens on hover/drag, retains a 24 px
  minimum length, and falls back to the native scrollbar when Windows enters
  high-contrast mode. Once queried, either indicator can destroy or recreate its
  HWND without passing a null provider through the managed UI Automation API or
  truncating WinForms teardown.

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
- `ISensor` and external data contracts remain unchanged; optimized history is
  an optional additive interface with legacy fallbacks. The legacy reset HTTP
  actions intentionally move from GET to POST; header-less automation remains
  supported by posting the same query.
- Existing XML settings and browser-local `sq.dashboard.v1` state remain readable.
- A 24-hour graph still represents 24 hours, with older density compacted rather
  than truncating the selected time range.
- Admin/hardware access requirements do not change.
- Upstream-sync risk is concentrated in existing local-fork WinForms, settings,
  history, HTTP, and embedded-web surfaces.

## Acceptance criteria

- [x] Repeated storage reset/resume/toggle does not retain closed groups or add
  duplicate device-change callbacks.
- [x] A failed `Computer.Open()` closes partial groups/global owners, preserves
  the original failure, leaves no hardware, and can retry without duplicate
  events.
- [x] Captured Storage/NVIDIA snapshots remain stable across add, remove,
  driver availability changes, and close; Computer traversals and reports use
  one captured generation per group.
- [x] Storage initialization reconciles synchronous add/remove callbacks without
  duplicates, retries failed unsubscribe, and rolls back on bounded-buffer
  overflow; NVIDIA monitor failures remain observable and later cycles recover.
- [x] Multiple NVIDIA groups hold independent process-wide ML leases; closing
  either group cannot release the other's lease. Detachment releases the local
  lease before blocking removal callbacks/device cleanup, and a blocked late
  refresh cannot reacquire after close.
- [x] A simulated 24 hours at 250 ms retains no more than 10,000 history points,
  preserves newest/extrema, and does not change current/min/max values.
- [x] Large-config loading stays bounded, compacts cleanup, orders overlapping
  saves, and rejects excessive decompression expansion.
- [x] Settings projection, autosave dirty-skip, safe-path validation, save
  delegation, retry behavior, and final-save error propagation have one tested
  owner without moving storage semantics or concrete UI policy out of their
  established owners.
- [x] Metrics and plots use bounded tail/delta reads and retain their existing
  public/visual contracts.
- [x] Session/form shutdown executes once on the UI thread and releases all
  static subscriptions and owned resources.
- [x] Repeated gadget resize, theme/font changes, tray failure, and modal dialogs
  do not leak GDI/native handles or freeze the UI.
- [x] Late hardware events cannot mutate controls before handle creation or after
  closing.
- [x] A second hardware-option click made while the first is still applying is
  retained, same-option bursts remain bounded and last-value-wins, and a
  concurrent manual/resume reset runs after the pending option values.
- [x] Initialization, ordered option/reset work, timer-driven single-flight
  polling, and shutdown drain/close have one tested owner; polling remains
  independent of option/reset serialization, busy ticks drop, and late poll
  completion cannot redraw after shutdown.
- [x] Reentrant option requests during close/reset cannot add replacement groups
  or prevent teardown from terminating; the requested flag remains available
  for the next explicit lifecycle reconciliation.
- [x] An option request that arrives after its category was visited during
  open/reset causes a complete rebuild from the latest configuration; continuous
  reentrant churn stops after four attempts, leaves no partial generation, and
  permits a clean retry.
- [x] Destroying either queried scroll-indicator HWND reaches base WinForms
  teardown without an escaped `ArgumentNullException`.
- [x] HTTP request bursts and slow clients stay within configured concurrency;
  stop cancels and drains active handlers.
- [x] `ResetMinMax` and `/ResetAllMinMax` mutate only on POST; GET cannot reset
  telemetry, and cross-origin browser POSTs are rejected before mutation.
- [x] Prometheus label values escape backslash, quote, and line feed without
  changing metric names, label names, units, or numeric values.
- [x] Dashboard appearance changes leave telemetry sample counts and derived
  values byte-for-byte unchanged.
- [x] Poll pause/reconfigure/visibility changes leave no overlapping or stale
  request able to paint.
- [x] Throwing browser storage still boots with usable in-memory state.
- [x] Departed web sensor state is pruned and stable sections avoid full rebuilds.
- [x] UI inputs and stateful controls pass accessible-name/focus checks.
- [x] Sensor-tree scrollbars keep a native-sized gutter without creating an
  overflow-only horizontal bar; all three themes meet the scrollbar contrast
  floor and expose distinct hover/drag states. Both painted orientations are
  discoverable by screen position as UI Automation `ScrollBar`/`RangeValue`
  elements, and `SetValue` updates the native scrollbar.
- [x] Contributor documentation contains no required links to absent files.
- [x] The golden `data.json` contract remains unchanged; all tests and both
  Release target builds pass.

## Open follow-ups

- [x] Promote the verified scrollbar follow-up only with explicit deployment
  approval. It shipped in the verified 2026-07-18 SND-HOST build.
- [x] Extend keyed DOM reuse and focus preservation to the Standard view
  (`renderPinnedCards`, `renderPFD`, `renderPanels`). The "stable regions reuse
  keyed DOM nodes" acceptance now covers Studio, Workspace, and Standard.
- [ ] Repeat the real monitor hit-target, drag, and UI Automation smoke on
  SND-HOST; deployment validation covered runtime and HTTP health, not manual UI.
- [x] Preserve the forced first dashboard snapshot when a persisted-paused page
  starts hidden, so first visibility does not stay blank until Resume.
- [ ] Preserve the last bounded on-disk sensor histories across autosave until a
  clean hardware close refreshes `/values`.
- [x] Make `Computer.Open()` transactional: on any group-construction or
  add-event failure, close every partial group/global owner, leave `Hardware`
  empty, preserve the original exception, and permit a clean retry.
- [x] Publish stable snapshots from dynamic Storage/NVIDIA groups and capture one
  snapshot per Computer traversal/report/removal. Concurrent hotplug, driver
  restart, and close must not skip, double-close, or index a changing list.
- [x] Reference-count process-global Mutex, OpCode, and NVIDIA ML leases across
  multiple `Computer` instances, and make close/reset cleanup continue after
  consumer or device cleanup failures.
- [x] Select rollback-to-confirmed-state for failed hardware-option
  reconciliation. Automatic retry/reset is rejected because it can repeatedly
  probe a failing driver without a fresh operator action.
- [ ] Make each runtime hardware-option mutation transactional, then implement
  that rollback policy: retain the prior group membership/backing flag on
  failure, restore the checkbox and persisted value without recursively queuing
  another operation, and show one concise UI-thread error. A later click or
  explicit Reset is the deliberate retry. Checkbox, persisted flag, backing
  flag, and group membership must never remain contradictory.
- [x] Give process-global NVIDIA ML state an explicit multi-`Computer` lease
  contract, including phase-aware release while enumeration is blocked and
  bounded monitor-error recovery.
- [ ] Decide whether failed `Computer.Reset()` must reconstruct the last known
  group set. The current failure path closes every partial replacement and
  leaves no mixed generation, but already-closed prior groups cannot be restored.
- [ ] Optional confidence gate: run a 60-minute current-commit live soak.

## Verification plan

Use red-capable regression tests at the owning seam before or with each fix:

- weak-reference/event-count reset tests for storage groups and static UI events;
- deterministic 24-hour history, bounded decompression, save-ordering, metrics,
  plot-delta, shutdown-coordinator, HTTP-concurrency, reset-method/origin, and
  Prometheus label-escaping tests;
- Win32 GDI-count loops for gadget resize/theme/font where practical;
- focused WinForms checks for scrollbar contrast, native bounds/accessibility,
  effective range endpoints, minimum thumb geometry, and shown-window UI
  Automation hit-testing for both orientations;
- deterministic hardware-operation coordinator tests that block one option,
  enqueue distinct and same-key updates, coalesce reset/resume intent, and
  cancel/drain pending work during shutdown;
- injected `Computer.Open()` owner/group/event failures with cleanup failures
  and retry, reentrant open/reset, shared global-owner leases, cleanup after
  throwing removal handlers, bounded configuration-version rebuild/churn,
  immutable Storage/NVIDIA snapshot generations,
  Storage initialization/subscription/callback failures, two NVIDIA ML lease
  holders, monitor-cycle recovery, acquired-lease NVIDIA refresh blocked while
  close detaches ownership, and lease release before blocking removal/device
  cleanup;
- Node tests with a throwing storage stub, delayed fetches, visibility/pause
  transitions, departed sensors, and cached Studio rerenders;
- manual current-HEAD smoke for graph, gadget, tray, Studio/Standard, pause,
  dark/light, narrow viewport, and web-server restart.

Required final commands:

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
git diff --check
```

Build Release outputs in a staging directory while the currently deployed
binary is running. After automated verification, run a bounded current-HEAD
memory/GDI/reset-resume/poll smoke; do not replace the deployed runtime without
separate maintainer approval.

## Verification log

- 2026-08-02 Plan-011 source-only settings extraction: AP source/integration
  `e58e38d` (stable patch-id `4fd7e493`, blobs `ccccdbd3`/`bb1f0e8b`)
  added the internal settings persistence coordinator and exactly seven facts;
  AQ source/integration `8f68c09` (stable patch-id `32b27bfa`, MainForm blob
  `13d2a728`) wired only `MainForm`. Coordinator passed 7/7; settings passed
  65 with the one established opt-in skip/66; lifecycle families passed 27/27;
  Application was exactly 167 discovered/166 passed/one established skip;
  Contracts passed 73/73; and the deterministic aggregate was exactly
  308/307/1. Both WinForms x64 Release targets built 0W/0E, Avalonia remained
  75/75 with only established AVLN3001, web remained 315/315 plus 18/18, and
  the final non-deploying sweep passed all eight included gates. The data.json
  golden retained blob `05113704a` and SHA-256
  `BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.
  Read-only SND-HOST closure proof retained the separate live PID 11980, exact
  task/listener and three HTTP 200s while the CSV grew 12,885 bytes in six
  seconds. This campaign created no candidate and made no deployment or live
  change; ledger state is `implemented`, not person-accepted.
- 2026-08-01 Plan-010 source-only lifecycle extraction: AM initial source
  `6871323` plus race fix `7fc7f77` and AN source/integration `479c89b` added the
  internal lifecycle coordinator and rewired `MainForm`. The new facts passed
  8/8; all coordinator families passed 27/27; lifetime passed 18/18; Application
  was exactly 160 discovered/159 passed/one established live-config skip;
  Contracts passed 73/73; and the deterministic aggregate was exactly
  301/300/1. Both WinForms x64 Release targets built with 0 warnings/errors,
  Avalonia remained 75/75 with only established AVLN3001, web remained 315/315
  plus 18/18, and all eight included non-deploying gates passed after the
  criterion ledger was added. The data.json golden retained blob `05113704a`
  and SHA-256 `BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.
  The PawnIO timing deviation above was independently reviewed as behaviorally
  compatible and safer, not silently treated as literal wiring-spec timing.
  This campaign created no candidate and made no deployment or live change.
- 2026-07-30 SND-HOST upstream integration: dashboard self-test 315/315 and
  focused Node tests 18/18 passed; the .NET suite passed 258 with one
  intentional skip; both x64 Release targets built with zero warnings/errors.
  Runtime-path and PawnIO lease regressions passed 31/31 under the current
  non-elevated SND-HOST identity, including fail-closed descriptor probes, exact
  protected ACL rules, fully qualified trusted-root enforcement, and an actual
  Windows image launch while the read-only identity lock remained held. This
  was source verification only: the live product remains
  `0.9.6+d693da7.2026-07-25`, and no process, task, configuration, or logs were
  replaced.
- 2026-07-25 SND-HOST clean deployment: candidate
  `0.9.6-20260725-165558646-d693da7` was created from clean commit
  `d693da7b1cd23159732123ba1a672ed8d9cf244b` under
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Releases`. Both PowerShell engines
  independently accepted it as current-source and promotable. The manifest
  SHA-256 is
  `B65E7A7C4E3A38E6AFA57683323B239B72325FECCADE1A68A73670EC0E984CC5`;
  the deployed 35-file framework-dependent net10/win-x64 ZIP SHA-256 is
  `e6408c4023bc578f5aea97e19dad5693442aad70baac8f88ce0327ea389d990e`.
  The app exited normally through its hidden WinForms window; no force stop was
  needed. A clean directory swap preserved only configuration, backup
  configuration, and the active CSV alongside the manifest-declared payload.
  Product version `0.9.6+d693da7.2026-07-25` then ran as the sole exact-path
  process through `\LibreHardwareMonitor`, whose result was `0x41301`. `/`,
  `/data.json`, and `/metrics` returned 200, the retired route returned 404,
  all 443 baseline sensors stabilized with identical group counts, and the CSV
  resumed growth. The complete old runtime and task XML are recoverable at
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Rollback\20260725-175907-pre-0.9.6-20260725-165558646-d693da7`.
  Historical `LibreHW-No-UAC` and `C:\ProgramData` entries below describe older
  deployments, not the current runtime owner/path.
- 2026-07-25 SND-HOST source fix: a red shown-window regression captured the
  managed `ArgumentNullException` escaping vertical scrollbar `WM_DESTROY`
  after UI Automation queried both indicators. It passed after both indicators
  stopped calling the managed null-provider overload and always continued into
  base teardown. Five coordinator regressions passed for queued distinct
  options, ordered same-key coalescing, reset coalescing, initialization
  barrier retention, and shutdown cancellation. A follow-on lifecycle pass
  verified transactional-Open failure/retry and reentrancy, shared global-owner
  leases, best-effort close/reset cleanup, single-generation Computer
  traversal/reporting, stable Storage/NVIDIA snapshots, bounded Storage
  initialization replay and unsubscribe retry, shared NVIDIA ML leases,
  monitor-cycle recovery, and an acquired-lease NVIDIA refresh blocked while
  close detaches ownership. Final clearance added immediate NVIDIA ML lease
  release before potentially blocking removal/device cleanup and
  configuration-version reconciliation that rebuilds open/reset generations
  from late option changes with a four-attempt churn bound. The final
  affected-surface gate passed 45/45. The full .NET suite passed (182 passed,
  one opt-in skip), including the unchanged `data.json` golden contract;
  JavaScript syntax, the 306/306 dashboard self-test, and all 18 focused
  console/workspace tests passed; both x64 Release targets built with zero
  warnings/errors. This was source verification only: no installed runtime,
  task, or live configuration was replaced.
- 2026-07-25 SND-DESK source-only dashboard review hardening:
  `ResetMinMax` and `/ResetAllMinMax`
  are POST-only, GET cannot mutate min/max telemetry, and cross-origin browser
  POSTs are rejected before reset; header-less automation may POST the same
  query. Prometheus label values escape `\`, `"`, and newline;
  workspace table min/max and panel-head Load guards use explicit null/empty
  checks; Standard-view `renderPinnedCards`, `renderPFD`, and `renderPanels`
  converted from full `innerHTML=''` rebuilds to `syncKeyedRegion` keyed reuse
  with exhaustive range, control, and bounded-animation signatures; keyboard
  focus is captured/restored across cards, rows, detail overlays, and panel
  controls during polls. `System.Text.Json` bumped 10.0.8 -> 10.0.10 to match
  sibling `System.*` packages. Node selftest 315/315, Node suites 18/18, and the
  .NET suite passed 163 with 1 skipped; both x64 Release targets built cleanly.
  This record did not replace the SND-HOST runtime.
- 2026-07-18 SND-DESK persisted-paused hidden-start follow-up: an exact Node
  regression first observed zero requests and zero paints after first visibility.
  A second red regression showed that hide-before-settlement could consume the
  intent with one request and no paint; a third reproduced the same loss during
  poll-rate reconfiguration. All passed after the poll controller retained the
  forced-snapshot intent until successful data handling and queued a forced
  single-flight follow-up after either cancellation. Pause remained active and
  no recurring timer was scheduled. JavaScript syntax, 18 focused
  polling/workspace tests, the 285/285 dashboard self-test, and the .NET suite
  (150 passed, one opt-in skip) passed. Both x64 Release targets built in isolated
  output folders with zero warnings/errors because the running local process held
  the standard `net10.0-windows` output; it was not stopped or replaced.
- 2026-07-18 SND-DESK -> SND-HOST deployment: the scrollbar follow-up shipped
  in product version `0.9.6+ebedd8b-dirty.2026-07-18`. Identity, process/task,
  HTTP, live telemetry, logging, and controller-to-host access checks passed.
  The manual scrollbar hit-target/drag/UI Automation smoke was not repeated on
  the target and remains explicitly open above.
- 2026-07-15 scrollbar accessibility follow-up: five focused WinForms checks
  and the full suite passed (129 passed, one existing opt-in skip); both x64
  Release targets built in isolated output folders with zero warnings/errors.
  A shown-window UI Automation hit-test found both painted orientations as
  `ScrollBar`/`RangeValue`, propagated `SetValue` to the native controls, and
  passed 10 consecutive stress iterations. A non-elevated real-control preview
  confirmed the 17 px native gutter, 11 px resting thumb, brighter/wider hover
  state, true bottom drag endpoint, readable value column, unchanged graph
  split, and no incidental horizontal scrollbar. The deployed
  `LibreHW-No-UAC` task/process was not modified or restarted.
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
