# Review - Memory, lifetime, efficiency, and UI handling

**Date:** 2026-07-14

**Review surface:** `origin/master...HEAD` at `63816bb`, plus a current-HEAD audit of memory, ownership, polling, and WinForms hot paths

**Specification:** `docs/feature-web-dashboard-studio-view.md` and the requested whole-application runtime/UI review

**Standards:** repository `AGENTS.md`

**Verdict:** **FAIL**

The four commits ahead of `origin/master` pass their existing automated tests, but the current source still contains definite managed and native leaks, large avoidable history allocations, unsafe shutdown ownership, and a Studio-view correctness regression. The verified fixes on `codex/memory-reliability` diverge from `master` at `541b05b` and are not present in the reviewed source.

## High-severity findings

### 1. Storage groups leak after every reset, resume, or storage toggle

`StorageGroup` subscribes to the static `StorageDIT.DevicesChanged` event in `LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs:35-45`, but its `Close()` method is empty at line 69. `Computer.Reset()` removes and recreates every group at `LibreHardwareMonitorLib/Hardware/Computer.cs:674-684`, and the application invokes that reset after resume at `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:702-706`.

Every reset leaves the old `StorageGroup` rooted by the static publisher, including its storage devices, sensors, and histories. It also adds another event callback, so later storage changes invoke every leaked instance.

**Required fix:** unsubscribe in `Close()`, close and clear every retained storage device, close devices when they are removed, and add a reset/resume regression test that verifies the old group can be collected.

### 2. Sensor history retention and copying scale badly at the active settings

`Sensor` keeps an uncapped `List<SensorValue>` plus a cached full-array snapshot in `LibreHardwareMonitorLib/Hardware/Sensor.cs:26-33`. It appends an averaged point every four readings and expires only by time at lines 119-159 and 382-390. Accessing `Values` copies the entire changed history at lines 176-185.

The current deployed configuration uses a 250 ms update interval and the default 24-hour history window. A changing sensor can therefore retain roughly 86,400 points per day. The live `/data.json` tree contained 546 sensor leaves, illustrating the scale even though not every sensor necessarily changes continuously.

The cost is then multiplied:

- `/metrics` requests a full snapshot for every sensor while holding the node lock at `LibreHardwareMonitor.Windows.Forms/HttpServer.cs:806-850`.
- `PlotPanel` rebuilds and retains another full `DataPoint` list at `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs:648-680`.
- Clearing or shortening a window does not return peak `List<T>` capacity.

This is time-bounded rather than infinite, but it creates severe retained memory, O(total history) copying, allocation pressure, and UI work.

**Required fix:** selectively port or reimplement the tested hard point cap, tail/delta readers, and incremental plot synchronization from `codex/memory-reliability`; trim oversized buffers when the configured window contracts.

### 3. Current settings loading can reproduce the previous huge startup spike

`LibreHardwareMonitorLib/PersistentSettings.cs:58-64,113-119` loads the complete settings file into an `XmlDocument`. Oversized persisted sensor histories are rejected only after the DOM is materialized at lines 90-93. The loader then forces `_modified = false` at line 107, so cleanup alone does not schedule a compacting save.

The deployed config is currently compact at about 22 KB, so this is not the cause of the process's present footprint. It remains a proven recurrence path: a historically observed hundreds-of-megabytes config will again be expanded into a much larger in-memory DOM and remain bloated on disk until another change causes a save.

**Required fix:** port the streaming `XmlReader` loader and cleanup-dirty behavior from the verified reliability work, with the large-config regression tests.

### 4. Session shutdown is cross-thread, duplicated, and statically rooted

`LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:637-646` registers anonymous `SystemEvents` handlers. The session-ended callback directly stops a WinForms timer, closes hardware, reads UI configuration, saves, and stops the server from the SystemEvents thread. `SaveConfiguration()` reads WinForms-owned state at lines 1388-1409, while the normal close path repeats teardown at lines 1495-1514.

The paths have no exactly-once gate and the static handlers are never removed. This creates cross-thread UI access, competing close/save operations, and a static root that retains the form and hardware graph when the form is disposed without immediate process termination.

**Required fix:** restore an exactly-once UI-thread shutdown coordinator, marshal session work to the UI context, and unsubscribe both `SessionEnded` and `PowerModeChanged` during teardown.

### 5. Gadget buffer recreation leaks an HBITMAP every time

`LibreHardwareMonitor.Windows.Forms/UI/GadgetWindow.cs:204-229` creates and selects a DIB into a memory DC but keeps the bitmap handle only in a local variable. `DisposeBuffer()` at lines 231-235 disposes the managed `Graphics` and DC but never reselects the previous object or deletes the DIB. Buffer recreation occurs on size changes at lines 249-253.

This is a definite unmanaged memory/GDI-object leak. Repeated gadget resizing or sensor-layout changes can eventually exhaust the per-process GDI handle budget.

**Required fix:** retain the DIB and previous selected-object handles, reselect the old object, call `DeleteObject` on the DIB before deleting the DC, make disposal idempotent, and dispose the gadget from `MainForm` shutdown.

### 6. Studio controls replay cached telemetry as if it were new

The new Studio controls call `commitDashboard()` and rerender cached `state.lastData` in `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:903-905,1584-1639`. Despite `freshTelemetry` being false, `render()` still updates sensor motion/history, observed maxima, power samples, and the tick counter at lines 982-998 and persists the result at lines 1024-1028.

A synthetic replay of one identical GPU sample through eight Studio rerenders produced eight samples and a fabricated 500 W derived limit. This violates the spec's requirement that Studio retain the same telemetry truth and not invent limits (`docs/feature-web-dashboard-studio-view.md:23,36,88`).

**Required fix:** separate telemetry ingestion from painting, or gate every accumulator, tick, and telemetry-persistence mutation behind `freshTelemetry`. Add a DOM-level test proving that any number of appearance changes leaves sample counts and derived telemetry unchanged.

### 7. Tray-icon retries can freeze the UI for eight seconds per icon

`LibreHardwareMonitor.Windows.Forms/UI/NotifyIconAdv.cs:565-606` retries Shell notification-area add/delete operations 40 times with `Thread.Sleep(200)` while holding its synchronization lock. These paths are reached from UI redraw and disposal.

An Explorer restart or shell failure can therefore freeze the application for up to eight seconds multiplied by the number of sensor tray icons.

**Required fix:** use bounded asynchronous/timer-driven retry with cancellation and no sleep under the ownership lock; keep disposal non-blocking and idempotent.

## Medium-severity findings

### 8. Concurrent saves can overwrite newer settings with an older snapshot

`PersistentSettings.Save()` snapshots state and clears `_modified` under `_sync` at `LibreHardwareMonitorLib/PersistentSettings.cs:127-151`, but it does not acquire `_ioSync` until after serialization at lines 153-182. A slower old save can therefore write after a newer save and permanently roll back the file while `_modified` remains false. Autosave and the SystemEvents shutdown callback make this reachable.

Serialize snapshot creation and writing under the same ordering gate, or use monotonic save generations.

### 9. HTTP and browser polling ownership is unbounded

The server starts an untracked `Task.Run` for every accepted context at `LibreHardwareMonitor.Windows.Forms/HttpServer.cs:171-179`, has no handler concurrency limit, and stops only the accept loop at lines 145-157. Data serialization is gated, but a response holds that gate through network writing at lines 709-742, so slow clients retain queued tasks and contexts.

The browser poller uses `setInterval` around an async request at `Resources/Web/console.js:1523-1538`, without an in-flight guard, timeout, abort controller, or request generation. Slow responses can overlap; pausing does not invalidate an already active request.

Bound server concurrency, track and drain handler tasks, release serialization ownership before slow socket writes, and switch the client to completion-driven polling with one abortable request.

### 10. Persisted-history decompression has no expansion limit

`LibreHardwareMonitorLib/Hardware/Sensor.cs:321-329` accepts a bounded compressed setting but copies decompressed data into an unrestricted `MemoryStream`. A small gzip payload can expand far beyond the input limit, and repeated sensor entries can exhaust memory during startup.

Decode through a byte- and record-budgeted stream; reject oversized, trailing, and malformed payloads before retention.

### 11. Hardware callbacks can mutate UI state off-thread

`MainForm.cs:1211-1243` marshals only when a handle exists and `InvokeRequired` is true; otherwise it directly mutates the UI tree. Hardware is opened and subscribed before explicit handle creation at lines 250-269 and 631-634. `HardwareNode.cs:53-72` has the same unsafe fallback when its marshaller is missing or disposed.

Capture the UI synchronization context after handle creation, queue only through that dispatcher, and discard callbacks once closing begins.

### 12. UI-owned GDI and static-event resources lack teardown

- `SensorGadget.cs:531-535` replaces its large and small fonts without disposing the previous pair.
- `Theme.cs:101-102` replaces static `Pen` and `SolidBrush` objects on every theme initialization without disposal.
- `TreeViewAdv.cs:263` subscribes to static `ExpandingIcon.IconChanged`, but the designer disposal path does not unsubscribe.
- `MainForm` does not dispose `_gadget`; `GadgetWindow.Dispose()` also does not detach `ShowDesktop`, destroy its native window, or dispose its context menu.
- `PlotPanel` owns a `ToolTip` and scaled fonts but has no disposal override.

These are concrete GDI/static-root leaks, primarily visible with repeated customization, form recreation, or long-lived test hosts.

### 13. Web storage failures can abort dashboard boot

Dashboard-state reads are guarded, but `localStorage.setItem`, migration enumeration, and removals are not guarded in `Resources/Web/console.js:233-294`. Migration and the first save run during boot. Browsers that deny storage access or hit quota can therefore abort initialization.

Use a single safe storage adapter with an in-memory fallback and test with a throwing storage implementation.

### 14. Several multi-second operations run synchronously on the UI thread

PawnIO installation and hardware discovery run synchronously during form setup (`MainForm.cs:269-279`), interface settings perform synchronous DNS resolution (`InterfacePortForm.cs:29-38`), and web-server stop can wait up to five seconds while called from a UI option (`HttpServer.cs:145-156`, `MainForm.cs:416-423`).

Move blocking discovery/network waits off the UI thread and return results through a closing-aware UI continuation.

## Low-severity findings

- Web telemetry maps retain departed sensor IDs indefinitely; each per-sensor history is capped, but key cardinality and persisted stale keys are not. Add grace-period pruning and global caps.
- Studio focus/system/network content is cleared and rebuilt on every tick, creating avoidable DOM allocation and GC churn. Reuse keyed nodes or skip unchanged regions.
- Several modal forms and `ColorDialog` instances rely on finalization instead of `using`, including call sites in `MainForm.cs:1524,1718-1720,1936-1937,2165-2170` and `SensorNotifyIcon.cs:71-78`.
- Every hardware node eagerly creates sensor-type groups and decodes duplicate images even for empty groups (`HardwareNode.cs:36-40`, `TypeNode.cs:24-110`). Cache images and create groups lazily.
- The Studio poll-rate range lacks an associated label/accessible name in `Resources/Web/index.html:16`; the theme control also does not expose its current state.
- `AGENTS.md` names `docs/ai-guide.md`, `docs/feature-workflow.md`, the feature template, and several other source-of-truth documents that are absent from the current tree. Restore those documents or update the contributor map so its required workflow is executable.

## Runtime observation

The running Release process is not a current-HEAD binary: its product version is `0.9.6+541b05b-dirty.2026-07-11`. A three-minute sample while requesting `/data.json` once per second showed sawtooth behavior rather than monotonic growth: private bytes started around 417 MiB and ended around 410 MiB, with an observed maximum around 419 MiB; working set ended below its starting point. Handle count fluctuated substantially.

That short sample does **not** prove a live process leak. It also does not invalidate the definite event, HBITMAP, and ownership leaks found in source. A current-HEAD build plus a 60-minute reset/resume, gadget-resize, graph, and web-poll soak with managed-heap and GDI-object counters is still needed after fixes.

## Verification

- `node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` - pass
- `node webtests/selftest.node.js` - pass, 252/252
- `node --test webtests/console.tests.js` - pass, 1/1
- `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -p:Platform=x64` - pass, 64/64; one existing `xUnit2020` warning
- Synthetic cached-rerender reproduction - fail: one sample replayed eight times became eight samples and produced a derived 500 W limit
- `git diff --check origin/master...HEAD` - reports only the intentional Markdown hard-break whitespace at `docs/feature-web-dashboard-studio-view.md:3`

No Release build was run against the active output directory because a non-current binary is running from it. The passing suites do not cover static-event collectability, GDI handle ownership, concurrent save ordering, shutdown thread affinity, or cached-render telemetry mutation.

## Coverage notes

Deep review covered the four branch commits, Studio specification and tests, settings persistence, sensor-history ownership, server request lifetime, hardware group reset/close behavior, WinForms shutdown and dispatch, gadget/tray/plot resources, and live process/configuration state. Hardware-specific driver implementations were sampled rather than exhaustively audited. No heap dump, ETW trace, GDIView capture, resume cycle, or long-duration current-HEAD soak was performed.

## Recommended fix order

1. Fix `StorageGroup.Close()` and native gadget buffer ownership first; both are definite leaks with narrow fixes.
2. Selectively transplant the streaming-settings, bounded-history/tail-reader, save-ordering, and shutdown-coordinator changes from `codex/memory-reliability`, rather than merging its entire tooling-heavy branch.
3. Separate Studio telemetry ingestion from cached UI rendering and add a behavioral test.
4. Bound HTTP/client polling ownership and remove synchronous tray/UI retry waits.
5. Run both target-framework builds, all tests, then a current-HEAD 60-minute memory/GDI/reset-resume soak.
