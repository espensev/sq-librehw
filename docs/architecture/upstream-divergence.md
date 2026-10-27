# Upstream divergence ledger

**Status:** current; checked by `eng/ci/tests/Test-UpstreamDivergenceLedger.ps1`
**Updated:** 2026-10-27
**Upstream:** `LibreHardwareMonitor/LibreHardwareMonitor` on GitHub, fetched as the
`upstream` remote (push disabled)
**Upstream base:** `3f118427b0fd38a72c3135c766807ebb5d605128`
**Fork head when written:** `4e4f1be7` on `master` (`origin/master`)

The upstream base is the merge-base of `master` and `upstream/master`. Every
official commit through it is an ancestor of `master`, so the complete
difference between this fork and the original source is exactly
`git diff <base>..master`. This file lists every upstream-owned path in that
difference with what changed, why, and where the owning record lives. New
fork-only areas are listed by area. Narrative for each upstream integration
stays in the sync specifications linked below; this file does not repeat it.

## Rule

- A path that exists in the upstream base and differs from it must have a row
  in the upstream-owned sections below. A row must name the owning feature
  spec, review, or commit.
- A fork-only file inside an inherited product root (`Aga.Controls`,
  `LibreHardwareMonitorLib`, `LibreHardwareMonitor.Windows.Forms`) must be
  listed in the fork-only section.
- Edit the row in the same pass as the code change. After an upstream sync,
  update the base commit, add rows for new adaptations, and remove rows for
  paths that no longer differ.
- `eng/ci/Test-LhmCiGates.ps1` runs the ledger check with the other `eng/ci`
  tests. The check fails when a differing upstream-owned path has no row, when
  a row names an upstream-owned path that no longer differs, or when the pinned
  base is no longer the merge-base with a fetched `upstream/master`. It skips
  when the base commit is absent from a shallow clone.

Classes used below: **fork** is intentional fork behavior; **adapt** is a
change carried only by an upstream integration merge to preserve fork
contracts; **declined** means the fork kept the pre-change upstream content;
**noise** is whitespace, line-ending, or byte-order-mark only; **removed** and
**moved** describe deletions and renames.

## Regenerating the inventory

```powershell
git fetch upstream
git merge-base master upstream/master                      # must equal the pinned base
git -c core.quotePath=false diff --name-status 3f118427 -- # upstream-owned rows are M, D, R
git diff --numstat 3f118427 -- <path>; git diff -w --numstat 3f118427 -- <path>   # noise check
git log --format='%h %ad %s' --date=short 3f118427..master -- <path>              # provenance
```

Counts at the pinned base on 2026-10-27: 464 paths differ; 74 upstream-owned
paths are modified, 32 deleted, 6 moved, and 352 are fork-only additions.
`.editorconfig`, `LICENSE`, `THIRD-PARTY-NOTICES.txt`, and `Directory.Build.props`
are unchanged from upstream.

Upstream integrations that carry the `adapt` rows: `3a805fab` (2026-07-25,
through official `81e8f83`), `08e1223e` (2026-08-09, through `d6cb2604`),
`93bd9704` (2026-10-19, through `e68a7ef7`), and `2e3b059b` (2026-10-27,
through `3f118427`). See the
[2026-07-25](../features/feature-upstream-sync-2026-07-25.md) and
[2026-10-19](../features/feature-upstream-sync-2026-10-19.md) sync
specifications and the
[integration review](../reviews/review-2026-10-19-upstream-and-open-work.md).

<!-- ledger:upstream-owned:begin -->
## 1. Upstream-owned files the fork modified

### Repository configuration and build

| Path | What differs | Class | Record |
|---|---|---|---|
| `.gitignore` | Adds fork ignores: local Claude settings, `.roslyn-mcp/`, Python bytecode, generated `.audit/` receipts, and the campaign runtime cache files under `data/`. Comments point at the cleanup record. | fork | [build-output cleanup](repository-build-output-cleanup.md); `dc7ad47c`, `e0758bf4`, `1fae77fb`, `9b1eb527`, `25bdde6e`, `774f46c1`, `f7f0d631` |
| `README.md` | Fork banner at the top linking to `docs/README.md` and to this ledger; the upstream text follows unchanged except one sample line re-indented from a tab to spaces. | fork, noise | `23913a01` |
| `LibreHardwareMonitor.sln` | Adds the four boundary test projects (Library, Application, Contracts, Attended) with x64 configurations. Leading byte-order mark dropped. | fork, noise | [plan-006](../campaign-plan-006-verification-suite-boundaries.md); `bef5bbf6`, `129d2f76` |
| `Aga.Controls/Aga.Controls.csproj` | Multi-targets `net472;net10.0-windows` with `UseWindowsForms`, net472-only framework references, WFO1000 suppressed on .NET, versionless package references under central package management, artifacts-aware output path. | fork | `7b0e0796`, `63b1b672`, `3f5b59d7`, `70e2eb74` |
| `LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj` | Versionless package references (central package management), artifacts-aware output paths, `InternalsVisibleTo` for the four test suites and the PawnIO portable fixture. Leading byte-order mark dropped. | fork, noise | [plan-006](../campaign-plan-006-verification-suite-boundaries.md), [PawnIO gating](../features/feature-pawnio-platform-gating.md); `63b1b672`, `3f5b59d7`, `e0758bf4`, `70e2eb74`, `129d2f76`, `8f4a143c` |
| `LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj` | Per-target application manifest (`LibreHardwareMonitor.Windows.Forms/Resources/app.net472.manifest` for net472, `LibreHardwareMonitor.Windows.Forms/Resources/app.manifest` plus `ApplicationHighDpiMode` for .NET); central package management with UI Automation references on net472 and the WPF framework reference on .NET; `InternalsVisibleTo` for the test suites; embeds `dashboard/web/` under the original `Resources.Web` logical names; local builds stamp FileVersion and ProductVersion with the git SHA and date (skipped under GitHub Actions, AssemblyVersion stays 0.9.6); artifacts-aware output path. | fork | [dashboard extraction](dashboard-extraction.md), [sensor workspace](../features/feature-sensor-workspace.md), [release packaging](../features/feature-release-packaging.md); `ee4d1fdc`, `63b1b672`, `3f5b59d7`, `cacc1bb2`, `48496529`, `70e2eb74`, `9b1eb527`, `129d2f76`, `5cc2856a` |

### Aga.Controls (tree control)

| Path | What differs | Class | Record |
|---|---|---|---|
| `Aga.Controls/Threading/AbortableThreadPool.cs` | `Thread.Abort` paths compile only for `NETFRAMEWORK`; on .NET an abort request reports the item as still executing. | fork | `7b0e0796` |
| `Aga.Controls/Tree/TreeNodeAdv.cs` | `System.Security.Permissions` usage limited to `NETFRAMEWORK`; `SYSLIB0050` suppressed around the serialization path. | fork | `7b0e0796` |
| `Aga.Controls/Tree/TreeViewAdv.cs` | Link demands limited to `NETFRAMEWORK`; owned measure bitmap; guarded `BeginInvoke` in icon redraw; `GetNodeBounds` made public and `GetNodeBoundsInClient` added for keyboard context-menu placement; horizontal grid-line extent cached per paint. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `7b0e0796`, `2d0ac02a`, `f931a8cc`, `5b9c6f9f` |
| `Aga.Controls/Tree/TreeViewAdv.Designer.cs` | `Dispose` unsubscribes the expanding-icon event, cancels the thread pool, disposes the tooltip, measure graphics, bitmaps, timer, and pens, and clears the fields. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `5b9c6f9f` |
| `Aga.Controls/Tree/TreeViewAdv.Draw.cs` | Header-band clip test uses the clip rectangle bottom so short partial repaints (for example under RDP) still draw rows; grid-line right edge computed once per paint instead of per row. | fork | `f931a8cc` |
| `Aga.Controls/Tree/NodeControls/BindableControl.cs` | Static member-lookup cache for the paint-path reflection. | fork | `f931a8cc` |
| `Aga.Controls/Tree/NodeControls/NodeCheckBox.cs` | `GlyphScalePercent` scales the checkbox glyph with the Text Size slider. | fork | [independent text scaling](../features/feature-independent-text-scaling.md); `d3e53f92` |

### Library: hardware core and lifetimes

| Path | What differs | Class | Record |
|---|---|---|---|
| `LibreHardwareMonitorLib/Hardware/Computer.cs` | Group registry extracted to `LibreHardwareMonitorLib/Hardware/HardwareGroupRegistry.cs`; open and close lifetimes hardened; failed category enables roll back their groups and report through the failure presenter. | fork | [hardware lifecycle seams](../features/feature-hardware-lifecycle-seams.md), [memory and UI reliability](../features/feature-memory-ui-reliability.md); `103c49c9`, `8745660e`, `c5ff5415` |
| `LibreHardwareMonitorLib/Hardware/Hardware.cs` | Adds `InvalidateSensorValues` so a failed device poll marks its readings unavailable while keeping extrema and history. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `8aae1fbc` |
| `LibreHardwareMonitorLib/Hardware/ISensor.cs` | `Data` stays GB and `SmallData` (MB) is retained instead of the upstream byte migration; adds `TemperatureRate` (degrees Celsius per second). | declined, fork | [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md), [thermal trends](../features/feature-thermal-trends.md); `bbaa9207` |
| `LibreHardwareMonitorLib/Hardware/Sensor.cs` | Timestamped bounded history exposed through `ISensorHistoryReader` and `SensorHistorySlice`; validated history checkpoint preserved across autosaves and crash reopen; `InvalidateValue` for polling isolation; non-finite handling for the external contracts. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `dc424c52`, `ee4d1fdc`, `d035a906`, `e0758bf4`, `5b9c6f9f`, `bbaa9207`, `8aae1fbc`, `7522851e` |
| `LibreHardwareMonitorLib/Hardware/CompositeSensor.cs` | Nullable reducer and seed so a failed component poll yields no value instead of a stale or zero composite. | fork | [Corsair cross-review](../reviews/review-2026-10-07-corsair-psu-crossreview.md); `957923fc` |
| `LibreHardwareMonitorLib/Hardware/Mutexes.cs` | Adds the CorsairLink read/write guard mutex (wait with timeout, release, close) shared with other Corsair software. | fork | [local release: Corsair repair](../features/feature-local-release-system.md#corsair-psu-discovery-repair--2026-10-07); `07d2d25e`, `957923fc` |
| `LibreHardwareMonitorLib/Hardware/Psu/Corsair/CorsairPsu.cs` | All HID exchanges run under the CorsairLink mutex with bounded timeouts; a failed poll invalidates readings and keeps device identity. | fork | [Corsair cross-review](../reviews/review-2026-10-07-corsair-psu-crossreview.md); `d035a906`, `07d2d25e`, `957923fc` |
| `LibreHardwareMonitorLib/Hardware/Psu/Corsair/CorsairPsuGroup.cs` | Discovery failures are isolated per device so other hardware still opens. | fork | same; `6a493f3d`, `957923fc` |
| `LibreHardwareMonitorLib/Hardware/Psu/Corsair/UsbApi.cs` | Identity reads recover from stale HID replies. | fork | same; `cb963503` |
| `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs` | Unique sensor identifiers for duplicate GPUs, NVML access through the shared lease, hotspot temperature-rate sensor, lifetime hardening. Leading byte-order mark dropped. | fork, noise | [thermal trends](../features/feature-thermal-trends.md), [memory and UI reliability](../features/feature-memory-ui-reliability.md); `d5e84ad8`, `d035a906`, `bbaa9207`, `103c49c9` |
| `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGroup.cs` | Monitoring loop and handle refresh moved to `LibreHardwareMonitorLib/Hardware/Gpu/Nvidia/NvidiaGroupLifecycle.cs`; the group keeps the public group and hardware-changed surface. | fork | [hardware lifecycle seams](../features/feature-hardware-lifecycle-seams.md); `103c49c9`, `ff5c9134` |
| `LibreHardwareMonitorLib/Interop/NvidiaML.cs` | Initialize and close become a counted lease under one sync root; availability flag is volatile. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `103c49c9` |
| `LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs` | Discovery, rescan, and close moved to `LibreHardwareMonitorLib/Hardware/Storage/StorageGroupLifecycle.cs`. Leading byte-order mark dropped. | fork, noise | [hardware lifecycle seams](../features/feature-hardware-lifecycle-seams.md); `5b9c6f9f`, `103c49c9`, `5de63074` |
| `LibreHardwareMonitorLib/Hardware/Storage/StorageDevice.cs` | Space and host-counter sensors stay in GB through `LibreHardwareMonitorLib/Hardware/Storage/StorageHostCounterUnits.cs`; testable `AdvanceSmartUpdateCycle`; unused last-update field removed; performance counters skipped on Unix. | adapt | [2026-07-25 sync](../features/feature-upstream-sync-2026-07-25.md), [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md); merges `3a805fab`, `08e1223e`, `93bd9704` |
| `LibreHardwareMonitorLib/Hardware/Cpu/Amd17Cpu.cs` | Upstream per-core PM-table sensors take indices 1000 plus table offset and 2000 plus core id so existing sequential sensor ids and saved selections stay stable; a failed or partial table clears the cached voltage and temperature. | adapt | [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md); merge `93bd9704` |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Model.cs` | Fourteen upstream enum members are appended after `Unknown` instead of inserted in place so public numeric values stay stable; pinned by `MotherboardModelCompatibilityTests`. | adapt | both sync specs; merges `3a805fab`, `93bd9704`, `2e3b059b` |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/Nct677X.cs` | NCT6687DR fan-configuration retry extracted into testable helpers that keep the restore-required flag when the EC restore fails; NCT6701D temperatures cleared each poll so remapped sources cannot retain old readings. Leading byte-order mark dropped. | adapt, noise | both sync specs; merges `3a805fab`, `93bd9704` |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/EC/EmbeddedController.cs` | Intel 700 water-flow factor multiplied by 60 so `SensorType.Flow` keeps litres per hour. | adapt | [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md); merge `2e3b059b` |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/LpcPort.cs` | `ReadIoPort` and `WriteIoPort` are virtual for fake-register tests. | adapt | merge `93bd9704` |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/LpcIO.cs` | Mixed CRLF and LF line endings on 23 lines from merge `93bd9704`; no code difference (`git diff -w` is empty). | noise | merge `93bd9704` |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Identification.cs` | Trailing whitespace removed on one line; no code difference. | noise | merge `2e3b059b` |
| `LibreHardwareMonitorLib/Hardware/Gpu/AmdGpu.cs` | Identical to upstream before `677a3a56`; memory sensors stay MB (`SmallData`). | declined | [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md) |
| `LibreHardwareMonitorLib/Hardware/Gpu/IntelDiscreteGpu.cs` | Identical to upstream before `677a3a56`; memory sensors stay MB. | declined | same |
| `LibreHardwareMonitorLib/Hardware/Gpu/IntelIntegratedGpu.cs` | Identical to upstream before `677a3a56`; D3D memory sensors stay MB. | declined | same |
| `LibreHardwareMonitorLib/Hardware/Memory/DimmMemory.cs` | Identical to upstream before `677a3a56`; capacity stays whole GiB. | declined | same |
| `LibreHardwareMonitorLib/Hardware/Memory/MemoryLinux.cs` | Identical to upstream before `677a3a56`; values stay GB. | declined | same |
| `LibreHardwareMonitorLib/Hardware/Memory/MemoryWindows.cs` | Identical to upstream before `677a3a56`; values stay GB. | declined | same |
| `LibreHardwareMonitorLib/Hardware/Network/Network.cs` | Identical to upstream before `677a3a56`; uploaded and downloaded totals stay GB. | declined | same |
| `LibreHardwareMonitorLib/PawnIo/PawnIo.cs` | Unix gate: the version probe and module load return a not-loaded instance instead of throwing from registry or device access. | fork | [PawnIO gating](../features/feature-pawnio-platform-gating.md); `1a18c5ab` |

### Windows Forms application

| Path | What differs | Class | Record |
|---|---|---|---|
| `LibreHardwareMonitor.Windows.Forms/Program.cs` | Release smoke entry point (`ReleaseSmoke.TryRun`) before any UI; `RuntimePaths.Initialize` with a failure dialog; `SetHighDpiMode` on .NET; the required-files check is net472-only. | fork | [local release system](../features/feature-local-release-system.md), [release packaging](../features/feature-release-packaging.md); `7b0e0796`, `9b1eb527`, `c733e891` |
| `LibreHardwareMonitor.Windows.Forms/Resources/app.manifest` | The `dpiAware` block is removed for .NET builds, where DPI comes from `ApplicationHighDpiMode` and `SetHighDpiMode`; net472 keeps it in `LibreHardwareMonitor.Windows.Forms/Resources/app.net472.manifest`. `requireAdministrator` is unchanged. | fork | `7b0e0796`, `ee4d1fdc` |
| `LibreHardwareMonitor.Windows.Forms/UI/MainForm.Designer.cs` | Window title "Libre Hardware Monitor - Sev IQ". | fork | `bb432e19` |
| `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` | Largest change (34 fork commits): lifecycle, settings-persistence, hardware-operation, and presentation coordinators; graph lanes and Graph Inputs dialog; bulk selection and keyboard access; text scaling with owned fonts; fail-closed HTTP listener host; hardware-option rollback; Windows session-end protocol. | fork | [plan-010](../campaign-plan-010-application-lifecycle-and-polling.md), [plan-011](../campaign-plan-011-settings-projection-and-persistence.md), [plan-012](../campaign-plan-012-winforms-presentation-surface-adapters.md), [graph lanes](../features/feature-graph-lanes.md), [independent text scaling](../features/feature-independent-text-scaling.md), [HTTP server safety](../features/feature-http-server-safety.md), [memory and UI reliability](../features/feature-memory-ui-reliability.md) |
| `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs` | Operator-defined graph lanes, value-axis wheel zoom with persisted lane zoom, local-time or elapsed time-axis labels, independent axis and tracker text scales, temperature-rate plotting. | fork | [graph lanes](../features/feature-graph-lanes.md), [independent text scaling](../features/feature-independent-text-scaling.md), [thermal trends](../features/feature-thermal-trends.md); `ee4d1fdc`, `8ebf19ea`, `bbaa9207`, `159b417a`, `29e19e47`, `d65d2ae3`, `fbb3a3ea` |
| `LibreHardwareMonitor.Windows.Forms/UI/ScaledPlotModel.cs` | CS0618 and CS0672 suppressed around the `HandleMouseDown` override that OxyPlot 2.2 still dispatches through; final newline added. | adapt | merge `93bd9704` |
| `LibreHardwareMonitor.Windows.Forms/UI/SensorNode.cs` | GB and MB formatting kept; temperature-rate format in Celsius or Fahrenheit per second; `GraphLaneKey` setting; construction no longer persists a plot entry per sensor or raises change events; change-guarded setters. | fork, declined | [graph lanes](../features/feature-graph-lanes.md), [thermal trends](../features/feature-thermal-trends.md); `f9114359`, `bbaa9207`, `29e19e47` |
| `LibreHardwareMonitor.Windows.Forms/UI/TypeNode.cs` | Sensor-type images cached once and disposed together; `IDisposable` unsubscribes node events; Temperature Rates and `SmallData` groups. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md), [thermal trends](../features/feature-thermal-trends.md); `5b9c6f9f`, `bbaa9207` |
| `LibreHardwareMonitor.Windows.Forms/UI/HardwareNode.cs` | Hardware events marshalled to the UI thread; type nodes keyed by sensor type; `IDisposable` unsubscription. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `f9114359`, `5b9c6f9f` |
| `LibreHardwareMonitor.Windows.Forms/UI/Node.cs` | Global `SyncRoot` serializes tree mutations against HTTP listener threads that read the node tree. | fork | same; `f9114359` |
| `LibreHardwareMonitor.Windows.Forms/UI/UpdateVisitor.cs` | Recoverable per-device update failures are isolated, invalidate that device's readings, are reported, and do not stop the sweep. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md#device-polling-isolation-follow-up-2026-10-19); `8aae1fbc` |
| `LibreHardwareMonitor.Windows.Forms/UI/UserOption.cs` | Initialize-on-subscribe fires only the newly added handler; `SetValueWithoutNotification` supports enable rollback. | fork | `f931a8cc`, `c5ff5415` |
| `LibreHardwareMonitor.Windows.Forms/UI/UserRadioGroup.cs` | Initialize-on-subscribe fires only the newly added handler. | fork | `f931a8cc` |
| `LibreHardwareMonitor.Windows.Forms/UI/StartupManager.cs` | The in-app startup option is unavailable while a managed scheduled-task path is configured; deployment tooling owns startup. | fork | [local release system](../features/feature-local-release-system.md#startup-and-single-instance-ownership); `9b1eb527` |
| `LibreHardwareMonitor.Windows.Forms/UI/SystemTray.cs` | UI-thread marshalling for hardware events, owned menu and icon disposal, hardware unsubscription. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `bb432e19`, `f9114359`, `5b9c6f9f` |
| `LibreHardwareMonitor.Windows.Forms/UI/SensorNotifyIcon.cs` | Icons regenerated only when text, value, or colour change; owned context menu disposed; temperature-rate text. | fork | same; `f931a8cc`, `5b9c6f9f`, `bbaa9207` |
| `LibreHardwareMonitor.Windows.Forms/UI/NotifyIconAdv.cs` | Shell notify-icon registration retries (40 attempts, 200 ms apart) for tray readiness after logon; disposal guards. | fork | same; `5b9c6f9f` |
| `LibreHardwareMonitor.Windows.Forms/UI/SensorGadget.cs` | UI-thread marshalling, owned string formats, disposal guards, GB and MB formatting kept. | fork, declined | same; `f9114359`, `f931a8cc`, `5b9c6f9f`, `bbaa9207`, `9b1eb527` |
| `LibreHardwareMonitor.Windows.Forms/UI/GadgetWindow.cs` | Layered-window bitmap handles tracked and released; disposal guards. | fork | same; `5b9c6f9f` |
| `LibreHardwareMonitor.Windows.Forms/UI/HardwareTypeImage.cs` | Adds `DisposeAll`. | fork | same; `5b9c6f9f` |
| `LibreHardwareMonitor.Windows.Forms/UI/InterfacePortForm.cs` | Interface discovery runs asynchronously after the form is shown; loopback is always offered; the configured listener address stays selected; duplicates removed. | fork | same, [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md); `5b9c6f9f` |
| `LibreHardwareMonitor.Windows.Forms/UI/Theme.cs` | Tree glyphs scale and centre with the Text Size slider; replaced pens and brushes are disposed; hover, pressed, and border scrollbar colours. | fork | [independent text scaling](../features/feature-independent-text-scaling.md), [sensor workspace](../features/feature-sensor-workspace.md); `edc173f5`, `5b9c6f9f`, `48496529` |
| `LibreHardwareMonitor.Windows.Forms/UI/Themes/BlackTheme.cs` | Scrollbar hover, pressed, and border colours. | fork | [sensor workspace](../features/feature-sensor-workspace.md); `48496529` |
| `LibreHardwareMonitor.Windows.Forms/UI/Themes/DarkTheme.cs` | Scrollbar hover, pressed, and border colours. | fork | same |
| `LibreHardwareMonitor.Windows.Forms/UI/Themes/LightTheme.cs` | Scrollbar hover, pressed, and border colours. | fork | same |
| `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedHScrollIndicator.cs` | Accessible scroll indicator: UI Automation provider, hover and pressed states, shared geometry helper, owner event unsubscription. | fork | same; `48496529`, `103c49c9` |
| `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedVScrollIndicator.cs` | Same as the horizontal indicator. | fork | same |
| `LibreHardwareMonitor.Windows.Forms/UI/UnitManager.cs` | Upstream byte-formatting helpers from `677a3a56` not adopted; adds `CelsiusRateToFahrenheit`. | declined, fork | [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md), [thermal trends](../features/feature-thermal-trends.md); `bbaa9207` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs` | Listener dispatch extracted to `LibreHardwareMonitor.Windows.Forms/Utilities/HttpListenerDispatchService.cs`; `data.json` projected from immutable snapshots by `LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs`; non-finite values serialize as null; sensor writes require a full matching origin; cardtruth preview route retired; embedded resource prefix from the executing assembly name. | fork | [plan-008](../campaign-plan-008-immutable-sensor-snapshot-and.md), [plan-009](../campaign-plan-009-http-listener-dispatch-service.md), [HTTP server safety](../features/feature-http-server-safety.md), [memory and UI reliability](../features/feature-memory-ui-reliability.md); `22e23748`, `5a95bd78`, `58e240b4`, `0b500f32`, `5b9c6f9f`, `bbaa9207`, `ceecf610`, `3a31eb59`, `b510f47d` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/IconFactory.cs` | Icons created from a handle are tracked so `DestroyIcon` runs exactly once. | fork | `f931a8cc` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs` | CSV rows carry millisecond timestamps on a monotonic interval gate; session-numbered file names in a configurable directory; per-unit value rounding with a significant-digit floor; duplicate identifiers fan out to distinct columns. Leading byte-order mark dropped. | fork, noise | [CSV storage efficiency](../features/feature-csv-log-storage-efficiency.md), [local release system](../features/feature-local-release-system.md); `d5e84ad8`, `d035a906`, `1ea62672`, `9b1eb527`, `72d44e18`, `8897a3e3`, `9609a4ca` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs` | Bounded history persistence; durable save with a preserved backup and failing fallback; validated history checkpoint; Windows session-end save protocol. | fork | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `e0758bf4`, `45abee20`, `5b9c6f9f`, `7522851e`, `aa95772b` |

## 2. Upstream-owned files the fork deleted

### GitHub configuration

Removed by `64898466` (2026-07-21) because they served the upstream project:
sponsorship, upstream Dependabot layout, and workflows gated on the upstream
repository name. The fork's hosted checks are
`.github/workflows/non-deploying-gates.yml` and
`.github/workflows/pawnio-portable.yml`; see the
[gate runner](../../eng/ci/README.md).

| Path | Class | Record |
|---|---|---|
| `.github/FUNDING.yml` | removed | `64898466` |
| `.github/dependabot.yml` | removed | `64898466` |
| `.github/workflows/master.yml` | removed | `22e23748` (publish guard), then `64898466` |
| `.github/workflows/pull requests.yml` | removed | `64898466` |

### Legacy web dashboard

The stock jQuery, jQuery UI, Knockout, and treeTable dashboard was replaced by
the fork's dashboard and deleted by `86dde89d` (2026-07-04). The fork page now
lives at `dashboard/web/index.html` and is embedded under the original resource
names; Git reports the upstream page as deleted because no content is shared.
Record: [Studio dashboard view](../features/feature-web-dashboard-studio-view.md),
[dashboard extraction](dashboard-extraction.md), `86dde89d`, `5cc2856a`.

| Path | Class |
|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/ohm_web.css` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/jquery.treeTable.css` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/jquery-ui-1.8.16.custom.css` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_flat_0_aaaaaa_40x100.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_glass_55_fbf9ee_1x400.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_glass_65_ffffff_1x400.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_glass_75_dadada_1x400.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_glass_75_e6e6e6_1x400.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_glass_75_ffffff_1x400.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_highlight-soft_75_cccccc_1x100.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-bg_inset-soft_95_fef1ec_1x100.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-icons_222222_256x240.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-icons_2e83ff_256x240.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-icons_454545_256x240.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-icons_888888_256x240.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/css/custom-theme/images/ui-icons_cd0a0a_256x240.png` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/ohm_web.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/jquery-1.7.2.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/jquery-1.7.2.min.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/jquery-ui-1.8.16.custom.min.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/jquery.tmpl.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/jquery.tmpl.min.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/jquery.treeTable.min.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/knockout-2.1.0.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/knockout-2.1.0.min.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/knockout.mapping-latest.js` | removed |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/js/knockout.mapping-latest.min.js` | removed |

## 3. Upstream-owned files the fork moved

Moved unchanged into the dashboard source tree by `5cc2856a` (2026-10-24) and
embedded under their original resource names. Record:
[dashboard extraction](dashboard-extraction.md).

| Upstream path | Fork path | Class |
|---|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/favicon.ico` | `dashboard/web/favicon.ico` | moved |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/images/toggle-collapse-dark.png` | `dashboard/web/images/toggle-collapse-dark.png` | moved |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/images/toggle-collapse-light.png` | `dashboard/web/images/toggle-collapse-light.png` | moved |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/images/toggle-expand-dark.png` | `dashboard/web/images/toggle-expand-dark.png` | moved |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/images/toggle-expand-light.png` | `dashboard/web/images/toggle-expand-light.png` | moved |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/images/transparent.png` | `dashboard/web/images/transparent.png` | moved |
<!-- ledger:upstream-owned:end -->

<!-- ledger:fork-only:begin -->
## 4. Fork-only files inside the inherited product roots

| Path | Purpose | Record |
|---|---|---|
| `LibreHardwareMonitorLib/Hardware/HardwareGroupRegistry.cs` | Group registry extracted from `Computer` | [hardware lifecycle seams](../features/feature-hardware-lifecycle-seams.md); `8745660e` |
| `LibreHardwareMonitorLib/Hardware/Gpu/Nvidia/NvidiaGroupLifecycle.cs` | NVIDIA monitoring loop and handle refresh | same; `ff5c9134` |
| `LibreHardwareMonitorLib/Hardware/Storage/StorageGroupLifecycle.cs` | Storage discovery, rescan, and close | same; `5de63074` |
| `LibreHardwareMonitorLib/Hardware/Storage/StorageHostCounterUnits.cs` | Host-counter divisor by DiskInfoToolkit device profile (GB contract) | [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md); merge `93bd9704` |
| `LibreHardwareMonitorLib/Hardware/ISensorHistoryReader.cs` | Bounded history read contract | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `5b9c6f9f` |
| `LibreHardwareMonitorLib/Hardware/SensorHistorySlice.cs` | Immutable history slice | same; `5b9c6f9f` |
| `LibreHardwareMonitorLib/Hardware/TemperatureRateSensor.cs` | Derived temperature-rate sensor | [thermal trends](../features/feature-thermal-trends.md); `bbaa9207` |
| `LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs` | Immutable node-tree capture | [plan-008](../campaign-plan-008-immutable-sensor-snapshot-and.md); `cbd1013c` |
| `LibreHardwareMonitor.Windows.Forms/Application/Snapshots/SensorSnapshot.cs` | Snapshot model | same; `cbd1013c` |
| `LibreHardwareMonitor.Windows.Forms/Resources/app.net472.manifest` | net472 manifest keeping the legacy `dpiAware` block | `ee4d1fdc` |
| `LibreHardwareMonitor.Windows.Forms/UI/ApplicationLifecycleCoordinator.cs` | Application lifecycle seam | [plan-010](../campaign-plan-010-application-lifecycle-and-polling.md); `6871323b` |
| `LibreHardwareMonitor.Windows.Forms/UI/SettingsPersistenceCoordinator.cs` | Settings persistence seam | [plan-011](../campaign-plan-011-settings-projection-and-persistence.md); `e58e38dc` |
| `LibreHardwareMonitor.Windows.Forms/UI/PresentationSurfaceCoordinator.cs` | Presentation surface contract | [plan-012](../campaign-plan-012-winforms-presentation-surface-adapters.md); `36c74675` |
| `LibreHardwareMonitor.Windows.Forms/UI/WinFormsPresentationAdapters.cs` | WinForms presentation adapters | same; `7d2600bb` |
| `LibreHardwareMonitor.Windows.Forms/UI/HardwareOperationCoordinator.cs` | Hardware open, close, and option operations | [memory and UI reliability](../features/feature-memory-ui-reliability.md); `103c49c9` |
| `LibreHardwareMonitor.Windows.Forms/UI/HardwareOptionFailurePresenter.cs` | Reports failed category enables | same; `c5ff5415` |
| `LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs` | Ordered UI shutdown | same; `5b9c6f9f` |
| `LibreHardwareMonitor.Windows.Forms/UI/GraphInputsForm.cs` | Graph Inputs management dialog | `1f4225cd`, `2d0ac02a` |
| `LibreHardwareMonitor.Windows.Forms/UI/PlotLanes.cs` | Operator-defined graph lanes | [graph lanes](../features/feature-graph-lanes.md); `29e19e47` |
| `LibreHardwareMonitor.Windows.Forms/UI/PlotLaneNamePrompt.cs` | Lane name prompt | same; `29e19e47` |
| `LibreHardwareMonitor.Windows.Forms/UI/UiScale.cs` | Text-size scaling math | [independent text scaling](../features/feature-independent-text-scaling.md); `1e71994b` |
| `LibreHardwareMonitor.Windows.Forms/UI/TextScaleSliderMenu.cs` | Debounced text-scale slider menu | same; `acd4b6bd` |
| `LibreHardwareMonitor.Windows.Forms/UI/UiTextScaleCommitGate.cs` | Deferred text-scale commit | same; `0f9476b7` |
| `LibreHardwareMonitor.Windows.Forms/UI/OwnedFont.cs` | Font ownership for text scaling | same; `159b417a` |
| `LibreHardwareMonitor.Windows.Forms/UI/SmartUpdateCyclePolicy.cs` | S.M.A.R.T. update-cycle menu policy and legacy throttle migration | [2026-07-25 sync](../features/feature-upstream-sync-2026-07-25.md); merge `3a805fab` |
| `LibreHardwareMonitor.Windows.Forms/UI/Themes/ScrollIndicatorAutomationProvider.cs` | UI Automation provider for themed scrollbars | [sensor workspace](../features/feature-sensor-workspace.md); `48496529` |
| `LibreHardwareMonitor.Windows.Forms/UI/Themes/ScrollIndicatorGeometry.cs` | Shared scrollbar geometry | same; `48496529` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs` | `data.json` projection from snapshots | [plan-008](../campaign-plan-008-immutable-sensor-snapshot-and.md); `3a31eb59` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/HttpListenerDispatchService.cs` | Bounded HTTP listener dispatch | [plan-009](../campaign-plan-009-http-listener-dispatch-service.md); `b510f47d` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/RuntimePaths.cs` | Managed data-root and startup-task resolution | [local release system](../features/feature-local-release-system.md); `9b1eb527` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/PawnIoInstallerLease.cs` | Serialized PawnIO installer extraction | same; `9b1eb527` |
| `LibreHardwareMonitor.Windows.Forms/Utilities/ReleaseSmoke.cs` | Candidate smoke entry point | [release packaging](../features/feature-release-packaging.md); `c733e891` |
<!-- ledger:fork-only:end -->

## 5. Fork-only areas

| Area | Purpose | Record |
|---|---|---|
| `AGENTS.md` | Contributor and agent rules, spec-first workflow | itself |
| `Directory.Packages.props` | Central package management; the single place NuGet versions are pinned | `3f5b59d7`, both sync specs |
| `global.json` | Pins the .NET SDK 10.0.400 with latest-patch roll-forward | `456a126b` |
| `sq-app.json` | Machine-verified application record for SND-DESK (source, runtime, consumers) | [local release system](../features/feature-local-release-system.md) |
| `live-tracker.md` | Campaign tracker, agent-level completion only | [campaign control plane](campaign-control-plane.md) |
| `.github/workflows` | Two non-deploying hosted workflows that delegate to the gate runner | [gate runner](../../eng/ci/README.md) |
| `.codex/skills` | Gate definitions (`project.toml`) and the planning contract | [gate runner](../../eng/ci/README.md) |
| `dashboard` | Dashboard source, fixtures, and Node tests embedded by the Windows project | [dashboard README](../../dashboard/README.md), [dashboard extraction](dashboard-extraction.md) |
| `docs` | Feature specs, architecture, campaign records, reviews | [docs README](../README.md) |
| `eng` | Guarded build-output cleanup and the non-deploying CI gate runner with its tests | [gate runner](../../eng/ci/README.md) |
| `ops/candidate` | External candidate creation, validation, and executable identity | [release packaging](../features/feature-release-packaging.md) |
| `ops/deploy/snd-desk` | SND-DESK local publish, promotion, rollback, and launcher tooling | [local release system](../features/feature-local-release-system.md) |
| `ops/live-verification` | Read-only manifest-driven live-state verifier | [live state verification](../features/feature-live-state-verification.md) |
| `ops/log-management` | Host log archive and retention package | [host log management](../features/feature-host-log-management.md) |
| `LibreHardwareMonitor.Tests` | Library, Application, Contracts, and Attended suites, fixtures, and the solution filter | [plan-006](../campaign-plan-006-verification-suite-boundaries.md) |
| `experiments/avalonia-fixture-explorer` | Fixture-only, non-shipping Avalonia spike | [Avalonia fixture explorer](../features/feature-avalonia-fixture-sensor-explorer.md) |
| `scripts` | Campaign planner, task runtime, and analysis tooling | [campaign playbook](../campaign-playbook.md) |
| `agents`, `data/plans` | Immutable campaign agent packets and plan JSON | [campaign control plane](campaign-control-plane.md) |

## 6. Upstream changes deliberately not adopted

| Upstream change | Decision | Record |
|---|---|---|
| `677a3a56` "Report raw sensor value for Data and SmallData in Bytes" (#2533, 2026-09-29) | Not adopted. `Data` stays GB and `SmallData` MB across native display, CSV, `data.json`, Prometheus, history, and saved selections. Adopting bytes needs a versioned compatibility and migration specification. The seven `declined` rows above plus the `ISensor.cs`, `UnitManager.cs`, `SensorNode.cs`, and `SensorGadget.cs` rows carry this decision. | [2026-10-19 sync](../features/feature-upstream-sync-2026-10-19.md) |
| Upstream GitHub sponsorship, Dependabot, and release workflows | Removed; the fork runs only non-deploying hosted gates and never publishes the upstream NuGet package. | `64898466`, [gate runner](../../eng/ci/README.md) |
| Upstream `HttpServer` named floating-point serializer (2026-07-25 range) | Fork side kept: non-finite sensor values serialize as null, which the external `data.json` contract requires. | [2026-07-25 sync](../features/feature-upstream-sync-2026-07-25.md) |
| Upstream in-place `Model` enum insertions | New members appended at the end of the enum so numeric values stay stable. | both sync specs |

## 7. Whitespace, line-ending, and byte-order-mark differences

These rows change no code. They came from merge resolutions and editor saves
and are recorded so a reviewer does not mistake them for behavior. They are not
normalized here because that would touch product files without a behavioral
reason.

| Path | Difference |
|---|---|
| `LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/LpcIO.cs` | 23 lines with mixed CRLF and LF endings |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Identification.cs` | one trailing-whitespace change |
| `LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/Nct677X.cs` | leading byte-order mark dropped (plus real changes) |
| `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs` | leading byte-order mark dropped (plus real changes) |
| `LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs` | leading byte-order mark dropped (plus real changes) |
| `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs` | leading byte-order mark dropped (plus real changes) |
| `LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj` | leading byte-order mark dropped (plus real changes) |
| `LibreHardwareMonitor.sln` | leading byte-order mark dropped (plus real changes) |
| `README.md` | one sample line re-indented from a tab to spaces (plus the banner) |

## Verification

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-UpstreamDivergenceLedger.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger
```

The check reads the pinned base from this file, lists every upstream-owned path
that differs from it in the working tree, and compares that list with the paths
named between the ledger markers. It runs under Windows PowerShell 5.1 and
PowerShell 7 and needs the base commit object; it prints `SKIP` and passes when
a shallow clone lacks it. The hosted `Non-deploying gates` workflow does not run
the `ci-gates` suite (it is self-referential), so this check is a local gate.

Verification log:

- 2026-10-27: ledger written at fork head `4e4f1be7` against base `3f118427`;
  112 upstream-owned rows (74 modified, 32 deleted, 6 moved) and 32 fork-only
  files inside product roots; the check passed under both engines with the
  fetched `upstream/master` merge-base equal to the pinned base.
