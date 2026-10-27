# Official upstream integration - 2026-10-19

**Status:** source verified; promoted to SND-DESK on 2026-10-23 within master
`9e68b348` and live-verified (see the
[local release record](feature-local-release-system.md#master-promotion--2026-10-23));
attended hardware checks remain separate. A source-only follow-on sync to
official `3f118427` landed on 2026-10-27 (see the last section); it is not
promoted

**Baseline:** local `5ff9d2d29f7373343273943d5954ad4971131a42`; official
`LibreHardwareMonitor/LibreHardwareMonitor` master
`e68a7ef7326fa875210e89e1402d93a45ffe4d3e`, with shared baseline `d6cb260`.

## Scope and compatibility

Merge the 50 official commits since the shared baseline, including hardware
support, NVIDIA handle refresh, storage discovery/dependency updates, and native
UI crash fixes. Preserve the fork's hardware lifetime coordination, shared
NVIDIA ML leases, Corsair failure handling, bounded HTTP dispatch, graph lanes,
hotspot rate sensor, and central package management.

The official byte-unit migration (`677a3a56`) is deferred: `Data` and `SmallData`
retain existing units, enum values, sensor identifiers, history, native display,
CSV, `data.json`, and Prometheus behavior. Adopting bytes needs a separate
versioned compatibility and migration specification. Preserve Flow in litres
per hour when accepting the corrected Intel 700 water-flow register address.

New motherboard enum members append after existing members. New AMD PM-table
entries use indices `1000 + table offset`, per-core temperatures use
`2000 + core ID`, and aggregate Core VIDs uses voltage index `2000`. These
separate indices leave existing sequential CPU sensor IDs and saved selections
unchanged; existing PM-table versions keep their original allocation.
Missing or invalid per-core PM-table readings clear the new cached voltage and
temperature; a valid current MSR voltage remains the fallback.
NCT6701D temperatures are rebuilt each poll so invalid or remapped register
sources cannot retain previous readings.

The new USB-storage exclusion option defaults to false and retains upstream's
setting name under `Options > Storage`. It requests an asynchronous rescan;
an already-running scan may finish with the prior selection. DiskInfoToolkit's
new namespaces use the fork's existing storage lifetime seams. Host counters
retain the old whole GiB or GB values according to the dependency's device
profile, including integer truncation. Values exceeding the new dependency's
UInt64-byte range remain unavailable; the compatibility adapter does not undo
its overflow guard. Review the profile rules on future dependency updates.
Accept explicit loopback selection without
weakening invalid-host rejection or asynchronous interface discovery.

NVIDIA polling must reacquire NVML handles under its existing global lock.
Retain group-owned lease release and publish the hotspot rate from the final
fresh hotspot sample, including unavailable readings when no source succeeds.
New PawnIO resources remain under the existing extraction and device-lifetime
contracts. AssemblyVersion remains `0.9.6`.

## Non-goals

- No runtime deployment, restart, hardware fault injection, task/service change,
  remote-host mutation, or push.
- No completion of the separate PawnIO OS-gating patch or unrelated open-work
  backlog in this integration.
- No claim of validation on physical hardware absent an attended check.

## Acceptance and verification

- Official `e68a7ef7` and the original local baseline are ancestors of the merged
  local master; no upstream-only commits remain at the captured reference.
- The original working-tree deletions, modified documentation and untracked
  audits remain intact; integration builds use a complete isolated worktree.
- All merge conflicts are reviewed; no conflict markers or inline NuGet package
  versions remain. Package changes are pinned in `Directory.Packages.props`.
- Existing contract/golden, lifecycle, Corsair and web tests pass. Targeted
  regressions cover adaptations where existing tests do not protect behavior.
- Both x64 Release application frameworks (`net472`, `net10.0-windows`) build.
- Record current unresolved work and exact verification results in the linked
  integration review before landing. Live hardware acceptance remains separate.

## Verification record

- Full Release .NET suite: **505 passed, one intentional opt-in skip**
  (Library 170, Application 222 plus one skip, Contracts 113).
- x64 Release application builds: `net10.0-windows` and `net472`, both zero
  warnings and errors. Library builds/pack also pass for `net472`,
  `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.
- Web self-test **339/339**; focused Node suites **18/18**; log-management fixture
  passes; release-system fixture **191 assertions** passes.
- New fake-register/sensor regressions cover invalid NCT6701D mappings, AMD
  missing/invalid PM tables and recovery, NVIDIA identity refresh and fresh
  hotspot selection, storage units, all 337 existing motherboard enum values,
  external data units, collapsed plots, gadget bounds, and explicit loopback.
- Independent integration review accepted the final source adaptations after
  the AMD and NCT6701D stale-reading corrections. Physical hardware, real Linux,
  and native-driver failure injection were not exercised. Live promotion
  followed on 2026-10-23 within master `9e68b348`: the SND-DESK runtime now
  exposes 613 sensors (41 added, none removed: NVIDIA per-channel hot-spot and
  memory temperatures on both GPUs plus an embedded-controller node), all nine
  NVIDIA power sensors read on both GPUs, and no crash event was logged.

See the [integration and open-work review](../reviews/review-2026-10-19-upstream-and-open-work.md)
for scope, recovery evidence, remaining work and verification commands.

## Follow-on sync - 2026-10-27 (source-only)

Official `e68a7ef7..3f118427` (six commits: `617ab24e`, `a8381734`, `f0ceab1f`,
`feb0e0ee`, `e4313b25`, `3f118427`) merged into master on 2026-10-27. The
installed SND-DESK runtime stays at `aa95772b`; promotion of this range is a
separate release step.

- Dependency bumps are pinned in `Directory.Packages.props`: DiskInfoToolkit
  2.2.1 to 2.2.2 (dependency updates only; the 2.2.1 USB exclusion, overlapped
  I/O, and NVMe percentage-used fixes were already integrated, so the host
  counter profile rules recorded above are unchanged), RAMSPDToolkit-NDD 1.6.2
  to 1.6.4 (1.6.3 improves DDR5 hub detection; 1.6.4 is dependencies only),
  Microsoft.Windows.CsWin32 0.3.363 to 0.3.367, and System.IO.Ports,
  System.Management, System.Resources.Extensions, System.Text.Json, and
  System.Threading.AccessControl 10.0.13 to 10.0.14. The three `.csproj`
  conflicts kept the fork's versionless `PackageReference` items.
- ASUS ROG MAXIMUS XI HERO / HERO (WI-FI) support (`e4313b25`): identification,
  the Intel 300 embedded-controller table, and the NCT6798D voltage/fan layout
  are taken as upstream. Upstream inserted `Model.ROG_MAXIMUS_XI_HERO` after
  `ROG_MAXIMUS_XI_FORMULA`; the fork appends it after `EX58_UD5` so the 337
  baseline enum values and the members appended on 2026-10-19 keep their
  numbers (`MotherboardModelCompatibilityTests`). Its `FanWaterFlow` source
  keeps the fork's litres-per-hour flow unit.
- Verification (worktree `wt/upstream-sync-20261027`, 2026-10-27):
  `eng\ci\Invoke-LhmGates.ps1 -All` under Windows PowerShell 5.1 passed all
  ten runnable gates in 2.4 minutes (`ci-gates` is self-skipped): Library
  193/193, Application 266 with the one opt-in skip, Contracts 115/115 with
  the golden `data.json` unchanged, the web self-test and 72 Node tests,
  PawnIO portable, log-management, live-verification, campaign-control,
  release-candidate, and the 191-assertion local release fixture. Both x64
  Release Windows Forms targets built with zero warnings or errors; the one
  warning in the run is the pre-existing Avalonia spike AVLN3001.
  `MotherboardModelCompatibilityTests` passes with the appended member. The
  range is not promoted, and the new board has no hardware check here.

The per-file state of the fork against official `3f118427` is kept in the
[upstream divergence ledger](../architecture/upstream-divergence.md).
