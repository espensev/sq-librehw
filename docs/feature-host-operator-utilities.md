# Feature Plan: Host-Neutral Operator Utilities

**Status:** ready for implementation; lossy conversion remains gated
**Updated:** 2026-07-19

## Problem

The retired SND-HOST LibreHardwareMonitor workspace contains two useful ideas
that are not yet source-controlled here:

- one easy SSH command that identifies the telemetry source and prints useful
  CPU, GPU, fan, and board readings;
- lighter, more readable derived log data based on physical sampling principles
  instead of an arbitrary row interval or blanket rounding.

The surviving host helper is not portable. It depends on legacy WMI access and
hard-codes one Intel CPU, one NVIDIA adapter, and one motherboard controller.
The old log-processing intent is also unsafe to copy directly because the CSV
is an external contract and the verified raw archive must remain authoritative.

GPU hotspot rate and safe log archive/retention are already shipped under
`feature-thermal-trends.md` and `feature-host-log-management.md`. This plan does
not reopen either implementation.

## Decisions

| Host idea | Decision | Reason |
|-----------|----------|--------|
| Easy `thermals` snapshot over SSH | Implement first | `data.json` already provides a read-only, host-neutral contract. |
| GPU hotspot rate | No new work | The backend rate sensor is bounded, tested, and deployed. |
| Archive/sort by machine and date | No new work | Verified ZIP publish, retention, and current-day safety are deployed. |
| Log cleanup/downsampling | Evidence gate | Raw ZIPs stay authoritative; benefit and error must be measured first. |
| Copy the host WMI script | Reject | It is WMI-only and hard-codes host sensor instances. |
| Add a profile-global `thermals` alias here | Separate integration | The tracked DevHome shell repo owns profile commands. |

## Goal

Deliver two independent, read-only operations utilities:

1. a portable PowerShell thermal snapshot client over `data.json`;
2. a streaming, report-only analyzer that determines whether a separate derived
   log format is justified and what its safe sampling bounds would be.

Both utilities stay outside product code. They add no hardware access, server
route, scheduled task, runtime deployment, profile mutation, or data deletion.

## Non-goals

- No changes to `data.json`, CSV headers/rows, routes, Prometheus, WMI, or
  `AssemblyVersion`.
- No hardware writes or `/Sensor?action=Set` calls.
- No fixed machine sensor IDs, adapter index zero assumptions, or NVIDIA-only
  behavior.
- No missing-as-zero conversion; a real fan value of zero remains valid.
- No blind decimal rounding by sensor type.
- No converter, raw-log deletion, archive replacement, or scheduled processing
  in the first implementation campaign.
- No modification of the current polling-reliability WIP in `console.js`, its
  tests, or `feature-memory-ui-reliability.md`.

## Thermal snapshot contract

Create `ops/thermal-snapshot/Get-LhmThermalSnapshot.ps1` with these behaviors:

- PowerShell 5.1 and 7, built-in facilities only, no admin requirement.
- Source precedence is explicit `-DataUri`, then `LHM_DATA_URI`, then the
  product default `http://127.0.0.1:8085/data.json`.
- Fetch is GET-only, no-store, and bounded by a configurable timeout clamped to
  `1..30` seconds.
- The first computer node at `Children[0].Text` is printed as the source label.
  It is display data, not verified machine identity. JSON output also includes
  the URI and retrieval time so remote results cannot be confused with local
  provenance.
- Tree flattening retains every sensor's `SensorId`, type, raw label, hardware
  ancestor, and `HardwareId`.
- CPU and GPU summaries use ordered semantic candidates within each hardware
  group. All detected GPUs are reported. Unsupported or absent readings render
  `unavailable`; they are never substituted with another device or zero.
- Default text includes CPU package/representative temperature, each GPU's core,
  hotspot, memory-junction, power and available rate, plus available system/GPU
  fans and useful board temperatures. Exact IDs remain visible in verbose or
  JSON output.
- Human output uses type-appropriate units and enough precision to represent the
  raw value. It does not infer a universal sensor resolution.
- `-Format Text|Json` is deterministic. JSON uses schema
  `sq.lhm.thermal-snapshot`, version `1`, and preserves nullable numeric values.
- Failures distinguish unreachable/timeout, non-JSON, invalid telemetry tree,
  and a valid tree with partial or no matching thermal sensors.

The command must work with Intel and AMD CPUs; NVIDIA, AMD, Intel, multiple, or
missing GPUs; stopped fans; missing hotspot/memory sensors; malformed payloads;
and payloads with non-finite readings already sanitized to `null`.

## Log evidence contract

Create `ops/log-analysis/Measure-LhmLogAnalysis.ps1`. The first version is a
streaming analyzer, not a converter.

It must:

- accept an explicit LHM CSV or a verified one-entry ZIP without changing it;
- validate the two-row ID/name header, column counts, timestamps, row widths,
  and parse failures;
- report bytes, rows, sensor count, sensor-type counts derived from IDs, first
  and last timestamps, cadence distribution/regimes, gaps, missingness, and
  per-sensor observed non-zero delta statistics;
- mark a current-day or growing input as incomplete and never make it eligible
  for conversion;
- operate with bounded memory proportional to sensor count, not row count;
- emit deterministic text and JSON reports without writing beside the source;
- measure ZIP compression when a verified archive is supplied;
- calculate, but not apply, candidate sampling bounds from an explicit minimum
  thermal time constant.

For a first-order thermal system with minimum time constant `tau`, the assumed
bandwidth is `1 / (2*pi*tau)` and the absolute Nyquist interval is `pi*tau`.
Candidate output cadence must apply a documented safety factor of at least
three, so with `tau = 5 s` it may not exceed about `5.24 s`. It must align to
observed source cadence and recommend no downsampling when the source is already
at or slower than the safe candidate. This formula is a planning bound, not
permission to discard samples.

## Converter gate

A later converter needs its own accepted spec. It may proceed only when all of
these are true:

- the first real completed prior-day archive has passed the existing ZIP/hash
  inspection gate;
- a concrete consumer and output format are named;
- the analyzer shows material benefit beyond ordinary ZIP compression;
- cadence changes and gaps remain explicit;
- extrema are preserved and representative-value error stays within a
  sensor-specific observed-resolution bound;
- raw verified archives remain untouched and authoritative;
- output uses temp-write, hash manifest, atomic publish, collision refusal, and
  an explicit separate destination.

If those conditions are not met, the correct result is to keep verified ZIPs
and stop after the report-only analyzer.

## Implementation plan

| Phase | Work | Files | Depends on | Exit |
|-------|------|-------|------------|------|
| 0 | Preserve the current polling WIP and establish an isolated utility checkpoint. | No product edits | — | Existing three-file diff is unchanged. |
| 1 | Implement and fixture-test the portable thermal snapshot client. | `ops/thermal-snapshot/*` | 0 | Intel/AMD, multi-GPU, missing, zero, malformed, timeout, text, and JSON cases pass in PS 5.1/7. |
| 2 | Implement and fixture-test the streaming log evidence analyzer. | `ops/log-analysis/*` | 0 | Mixed cadence, gaps, nulls, malformed rows, current-day marking, ZIP validation, and bounded-memory cases pass in PS 5.1/7. |
| 3 | Reconcile docs and run contract regression checks. | This spec, `docs/README.md` | 1, 2 | Docs and external contracts remain current and unchanged. |
| Gate A | Review analyzer evidence and decide whether a converter has a justified consumer. | Follow-up spec only | 2, first rollover proof | Explicit go/no-go; no implicit converter work. |
| Gate B | Optionally expose `thermals` through the tracked DevHome shell authority. | Outside this repo | 1 | Separate review, install, and machine verification. |

Phases 1 and 2 are independent, but a single implementation lane may perform
them sequentially. No parallel-agent campaign is required.

## Codebase impact

| Path | Change | Risk |
|------|--------|------|
| `ops/thermal-snapshot/Get-LhmThermalSnapshot.ps1` | create | Medium: semantic sensor selection |
| `ops/thermal-snapshot/Test-LhmThermalSnapshot.ps1` | create | Low |
| `ops/thermal-snapshot/README.md` | create | Low |
| `ops/log-analysis/Measure-LhmLogAnalysis.ps1` | create | Medium: wide CSV streaming/parser correctness |
| `ops/log-analysis/Test-LhmLogAnalysis.ps1` | create | Low |
| `ops/log-analysis/README.md` | create | Low |
| `docs/feature-host-operator-utilities.md` | maintain | Low |
| `docs/README.md` | maintain | Low |

No product-code, dashboard-state, serialization, logger, project, package, or
schema file is owned by this plan.

## Compatibility and risks

- PowerShell 5.1/7 differences must be covered without external modules.
- Semantic label matches can be ambiguous; selection must remain inside the
  correct hardware ancestor, preserve IDs, and prefer explicit ordered rules.
- `data.json` has a retrieval time, not a sensor-sample timestamp. The client
  must not label a fetch time as hardware freshness proof.
- CSV names may contain quotes/commas and rows may be large. Tests need a real
  CSV parser path and must prove streaming behavior.
- Sensor type can be inferred from the identifier path for analysis, but units
  and true measurement resolution are not present in the CSV header. The
  analyzer must state that limitation instead of inventing metadata.
- ZIP compression may make lossy processing unnecessary. That is an acceptable
  no-go outcome.

## Acceptance

- [ ] The snapshot client is GET-only, host-neutral, and changes no runtime state.
- [ ] Text output includes a source label and honest unavailable states.
- [ ] JSON output retains exact IDs, hardware ancestry, nullable raw values,
  source URI, and retrieval time.
- [ ] Intel/AMD CPU plus NVIDIA/AMD/Intel/multiple/missing GPU fixtures pass.
- [ ] Zero fan readings remain zero; missing readings remain null/unavailable.
- [ ] The analyzer handles wide CSVs in bounded memory and reports cadence
  regimes, gaps, missingness, type counts, and parse errors.
- [ ] Current-day/growing logs are marked incomplete and never converted.
- [ ] The analyzer reports the formula, assumptions, and safety factor behind
  any candidate cadence; it does not rewrite input.
- [ ] Existing archive, `data.json`, CSV, dashboard, route, and version contracts
  are unchanged.
- [ ] No live task, profile, firewall, runtime, or archive is modified.

## Verification

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\thermal-snapshot\Test-LhmThermalSnapshot.ps1
pwsh -NoProfile -File ops\thermal-snapshot\Test-LhmThermalSnapshot.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-analysis\Test-LhmLogAnalysis.ps1
pwsh -NoProfile -File ops\log-analysis\Test-LhmLogAnalysis.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~DataJsonGoldenTests
git diff --check
```

After deterministic checks, a separate read-only smoke may query an explicitly
named local or verified remote runtime. It must compare the returned source
label and sensors with the same `data.json` payload and must not install a shell
alias, modify a task, or deploy files.
