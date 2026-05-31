# Local UI Customizations (beyond the Graph Menu spec)

This fork ("Sev IQ") carries local changes on top of upstream LibreHardwareMonitor that are
**not** covered by [`feature-graph-menu.md`](feature-graph-menu.md). They were delivered in the
same commits (`7b0e079`, `1f4225c`, `bb432e1`) and are recorded here for traceability so future
upstream merges and reviewers know they are intentional.

## Sensor tree

- **Compact Mode** (`View > Compact Mode`, `MainForm.cs` `ApplySensorTreeLayout`). Opt-in, default
  off. When on: reduces row height, sets `GridLineStyle.None`, narrows the value column, and hides
  the Min/Max columns. The `Show Min` / `Show Max` items are disabled while compact is active so the
  checkmark cannot lie about visibility. On save, the pre-compact column widths are written back so
  the narrowed widths are not persisted.
- **Multi-select hide/unhide** (`treeView.SelectionMode = TreeSelectionMode.Multi`,
  `GetSelectedSensorNodes` / `SetSensorNodesVisible`). Right-clicking a multi-selection shows
  `Hide Selected Sensors (N)` / `Unhide Selected Sensors (N)` and suppresses the single-sensor
  actions (Parameters, Rename, Pen Color, etc.).

## Plot panel

- **Grid Density** (plot context menu: Off / Major / Normal / Fine). Default is **Fine**, which is a
  visible change from upstream's default grid. Fine uses a custom "nice step" algorithm
  (`GetNiceAxisStep`, factors {1, 2, 2.5, 5, 10}) targeting ~20 major divisions. This only changes
  gridline/tick rendering — it never touches sensor data, so it does not smooth, average, or
  downsample anything. Steps are recomputed per refresh but only re-assigned when they actually
  change, so the grid does not "pop" between frames.
- **Time-axis presets**: added `30 sec`, `1 min`, `2 min` to the plot's right-click Time Axis menu.

## Library (`LibreHardwareMonitorLib`)

- **Averaging-accumulator reset** (`Sensor.cs`). The pre-existing 4-sample averaging
  (`_sum` / `_count`) is now reset wherever the `_values` graph buffer is cleared
  (`ValuesTimeWindow == Zero`, `ClearValues()`). This is a correctness fix: previously a clear left
  a stale partial sum that skewed the next averaged graph point. Exposed `Value` / `Min` / `Max`
  come from the raw value and are unaffected. This is a shared-library change, so it applies to all
  consumers of `ISensor.Values` (e.g. the Prometheus HTTP endpoint), not just the WinForms graph.

## Modernization (traceable to `discovery-librehw-sync-upgrade.md`)

- **High DPI**: `Program.cs` `Application.SetHighDpiMode(SystemAware)` (under `NETCOREAPP`),
  `ApplicationHighDpiMode=SystemAware` in the csproj for non-`net472`, and removal of the legacy
  `dpiAware` block from `app.manifest`. Clears the WFO0003 warning.
- **`System.Web` removal**: `HttpServer.cs` now uses `request.QueryString` instead of
  `HttpUtility.ParseQueryString`, and the `System.Web` / `System.Configuration.Install` references
  are dropped. Behavior-preserving for the sensor API; clears the MSB3245 warning.
- **`Aga.Controls` multi-targeting** to `net472;net10.0-windows`, with `#if NETFRAMEWORK` guards
  around `Thread.Abort` (`AbortableThreadPool`), `SecurityPermission` attributes, and a
  `SYSLIB0050` pragma. **Behavior change on `net10.0-windows`:** `AbortableThreadPool` can no longer
  forcibly abort work items — `Cancel`/`CancelAll` with `allowAbort` become no-ops that report the
  item as still executing (`Thread.Abort` is unsupported on modern .NET). This is an accepted
  consequence of modernization, not a regression to fix.

## Branding

- **Window title and tray tooltip** changed from `Libre Hardware Monitor` to
  `Libre Hardware Monitor - Sev IQ` (`MainForm.Designer.cs`, `SystemTray.cs`).
