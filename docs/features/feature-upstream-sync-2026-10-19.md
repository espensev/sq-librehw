# Official upstream integration - 2026-10-19

**Status:** source verified; live promotion and attended hardware checks remain separate

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
  native-driver failure injection and live promotion were not exercised.

See the [integration and open-work review](../reviews/review-2026-10-19-upstream-and-open-work.md)
for scope, recovery evidence, remaining work and verification commands.
