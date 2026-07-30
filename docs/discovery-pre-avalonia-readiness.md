# Discovery — Pre-Avalonia Readiness

**Goal:** Continue the release-candidate gate and identify what remains before
starting a parallel Avalonia lane.
**Date:** 2026-07-30
**Status:** complete
**Recommended next:** draft a compact fixture-only Avalonia spike spec, run
`/planner` with this document as input, then freeze the accepted spec/plan and
cut their exact current-source candidate before implementation.

---

## Questions

1. What release checkpoint must be green before the Avalonia lane starts?
2. What documented work is still open, and how much of it blocks the spike?
3. What is the smallest safe first Avalonia seam?
4. Which existing contracts can the spike consume without taking ownership?
5. Which files and responsibilities must remain serialized across parallel
   lanes?
6. Is the repository ready to start, and what remains explicitly out of scope?

---

## Findings

### Q1: What release checkpoint must be green before the Avalonia lane starts?

**Answer:** The configured external release gate must produce a clean,
promotable candidate from the exact accepted spike-spec and campaign-plan
checkpoint and pass independent `-RequirePromotable -RequireCurrentSource`
verification immediately before implementation starts. Candidate creation is
not deployment authority.

**Evidence:**

- `.codex/skills/project.toml:9-12` defines the candidate build, independent
  current-source/promotable verifier, and release-system fixture.
- `docs/feature-release-packaging.md:225-253` separates read-only candidate
  validation from identity-verified runtime coordination and promotion.
- The pre-documentation checkpoint produced external candidate
  `0.9.6-20260730-153204994-99e9787` from clean commit `99e9787`, passed
  current-source/promotable verification under Windows PowerShell 5.1 and
  PowerShell 7, and passed the 114-assertion release fixture under both engines.
  Because this findings document is tracked source, this discovery handoff
  candidate must be rebuilt from the documentation commit. Accepting the later
  spike spec and campaign plan will deliberately require one more
  current-source baseline candidate before implementation starts.

**Implications:**

- Freeze and commit each discovery/spec/plan checkpoint before building its
  corresponding current-source candidate.
- Do not promote either candidate as part of this discovery.

### Q2: What documented work is still open, and how much of it blocks the spike?

**Answer:** There are 41 unchecked documentation items: 26 future
native-modernization phase gates, five reliability follow-ups, and ten
not-yet-implemented operator-utility checks. None blocks a bounded,
recorded-fixture Avalonia spike after its own spec is accepted.

**Evidence:**

- `docs/feature-native-ui-modernization.md:169-192` defines the Phase 0A–0D
  ordering; its later phase exit gates account for 26 unchecked items.
- `docs/feature-memory-ui-reliability.md:222-259` leaves five items open:
  attended scrollbar/UI Automation smoke, bounded-history autosave,
  transactional hardware-option reconciliation, failed-reset semantics, and
  an optional 60-minute soak.
- `docs/feature-host-operator-utilities.md:187-202` contains ten open acceptance
  checks for the snapshot client and streaming analyzer; their planned
  directories do not yet exist.
- Release packaging, host log management, local release, and Workspace
  acceptance lists have no unchecked criteria.

**Implications:**

- Reliability work may run beside the spike, but owns `Computer`, `MainForm`,
  sensor lifetime, and current settings behavior.
- Native Phase 0 and operator utilities are independent candidate lanes; they
  are not reasons to delay the isolated spike.
- Attended live checks remain explicit promotion evidence, not source blockers.

### Q3: What is the smallest safe first Avalonia seam?

**Answer:** A new, non-shipping modern-.NET project should parse bounded,
recorded `data.json` fixtures into immutable Avalonia-local view models and
render hierarchy, labels, nullable current/min/max values, and honest
unavailable states.

The first slice must have no reference to the WinForms assembly, no
`Computer.Open()`, no admin manifest, no POST/control path, no current-settings
writes, and no inclusion in WinForms candidates or the live task.

**Evidence:**

- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:828-856` owns the
  external `data.json` object/stream serialization boundary.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:1188-1238`
  exposes stable `SensorId`, `HardwareId`, labels, types, formatted values, and
  nullable raw values.
- `LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs:24-65` byte-locks both
  serializer paths.
- `docs/feature-native-ui-modernization.md:154-165` requires new UI work to
  consume immutable snapshots without enumerating live hardware or taking
  process ownership.
- `docs/feature-native-ui-modernization.md:349-375` keeps hardware/process
  ownership with WinForms and requires a separate spike or migration spec.

**Implications:**

- Recorded fixtures make the lane deterministic, non-elevated, and independent
  of the SND-HOST runtime.
- Read-only HTTP polling can be a later spike milestone only after endpoint,
  error, authentication, backoff, and history bounds are specified.

### Q4: Which existing contracts can the spike consume without taking ownership?

**Answer:** Stable identifiers, hierarchy/order semantics, nullable sensor
values, and the external read-only JSON feed are usable now. Hardware-library
types and Workspace profile ideas are reusable only with caution. Current
WinForms nodes, settings, formatting, and runtime-path implementations are not
host-neutral contracts.

**Evidence:**

- `LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj:3-5` targets
  `netstandard2.0` and modern .NET in addition to `net472`.
- `docs/feature-native-ui-modernization.md:68-77` protects canonical hierarchy,
  order, and stable sensor IDs from presentation movement.
- `docs/feature-sensor-workspace.md:86-95` treats Workspace as presentation-only
  and anticipates an equivalent future profile without hardware/task ownership.
- `LibreHardwareMonitor.Windows.Forms/UI/SensorNode.cs:16-109` mixes useful
  formatting with `ISensor`, `PersistentSettings`, `System.Drawing`, plot, and
  UI state.
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs:18-58`
  owns the current mixed hardware/UI store; its save path performs
  primary/backup rotation at `PersistentSettings.cs:290-387`.
- `LibreHardwareMonitor.Windows.Forms/Utilities/RuntimePaths.cs:55-150` is
  internal, WinForms-executable-aware, and creates current runtime directories.

**Implications:**

- Do not extract a premature shared model solely for Avalonia.
- Let the spike prove DTO shape and bounded rendering first; shared
  snapshot/formatter/profile extraction belongs to the later Phase 5 seam.

### Q5: Which files and responsibilities must remain serialized across parallel lanes?

**Answer:** Hardware ownership, lifecycle, current settings, HTTP production,
solution/package registration, release integration, and roadmap truth need one
integration owner. That owner must perform the initial central-package and
isolated-project bootstrap as one serialized change. Afterward, the Avalonia
lane should add only isolated spike project, test, fixture, and spec files.

**Evidence:**

- `docs/feature-native-ui-modernization.md:140-165` explicitly permits pure
  modules/tests/assets in parallel while serializing `MainForm`, tree/theme,
  settings integration, and promotion.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:115-190` owns settings,
  the canonical tree, and `Computer`; `MainForm.cs:204-253` owns gadget/logger
  state, `MainForm.cs:356-387` owns HTTP startup, and `MainForm.cs:857-887`
  owns periodic update orchestration.
- `LibreHardwareMonitorLib/Hardware/Computer.cs:650-725` transactionally opens
  hardware; `Computer.cs:1152-1169` owns the process-global mutex/OpCode leases,
  and `Computer.cs:168-210` makes CPU/GPU group ordering semantically
  significant.
- `LibreHardwareMonitor.Windows.Forms/Resources/app.manifest:6-10` and
  `app.net472.manifest:7-11` require administrator elevation.
- `Directory.Packages.props:3-13` centralizes package versions.

**Implications:**

The initial Avalonia lane must avoid concurrent edits to:

- `MainForm.cs`, `Computer.cs`, `HttpServer.cs`, `PersistentSettings.cs`, and
  `RuntimePaths.cs`;
- `LibreHardwareMonitor.sln`, both existing application/test project files, and
  `Directory.Packages.props`;
- `docs/README.md` and `docs/feature-native-ui-modernization.md`.

Before parallel contributors start, the integration owner selects the current
supported stable Avalonia packages from official metadata, registers their
versions in root `Directory.Packages.props`, and scaffolds a separate non-shipping
spike project/solution without adding it to the WinForms release solution.
Thereafter, other lanes treat the root package file and spike scaffolding as
frozen unless that owner serializes a reviewed update.

### Q6: Is the repository ready to start, and what remains explicitly out of scope?

**Answer:** Conditional go. The repository is ready for planning and then an
isolated, fixture-only Avalonia feasibility spike once its compact spec and
campaign plan are accepted and committed and that exact checkpoint passes the
clean/promotable current-source candidate gate. It is not ready for an Avalonia
hardware host, settings co-owner, shipping package, scheduled-task owner, live
replacement, or cutover.

**Evidence:**

- `AGENTS.md:18-20` requires meaningful new behavior to begin from an accepted
  feature spec, with readiness fields defined at `AGENTS.md:49-59`.
- `docs/feature-native-ui-modernization.md:40-52` forbids changes to hardware,
  canonical ordering, JSON/CSV/Prometheus/routes/IDs, or live promotion in its
  native packet.
- `docs/feature-native-ui-modernization.md:403-443` requires accessibility,
  DPI, lifecycle, performance, automated, attended, packaging, and
  multi-architecture evidence before product promotion.

**Implications:**

- The spike spec must define DTO and payload/node/string/depth limits plus
  normal, null/missing, hot-plug disappearance, malformed, and oversized
  fixtures.
- Milestone one must explicitly remain non-elevated, read-only, non-packaged,
  non-live, and independent of current settings.
- Any future live polling, hardware ownership, packaging, or migration needs a
  later accepted gate.

---

## Cross-Cutting Analysis

### Constraints

- WinForms remains the sole hardware/process/task owner.
- The producer-side `data.json` bytes and stable identifiers cannot change.
- The spike cannot share or concurrently write the current settings file.
- Existing production release candidates remain WinForms-only.
- Root package/project bootstrap and later shared roadmap edits require a
  serialized integration owner.

### Risks

| Risk | Likelihood | Impact | Notes |
|---|---|---|---|
| Premature shared-model extraction | Medium | High | Current formatting and node types mix hardware, persistence, drawing, and UI state. |
| Two hardware owners | Low if bounded | High | Avoid `Computer.Open()` and privileged manifests entirely in the spike. |
| Contract drift | Medium | High | Use recorded golden-derived fixtures and stable `SensorId`/`HardwareId`, never generated numeric IDs. |
| Parallel merge conflicts | Medium | Medium | Keep the spike out of the listed shared files until one integration owner takes over. |
| Prototype mistaken for replacement | Medium | High | Keep it non-shipping and exclude it from release candidates, task ownership, and live docs. |
| Unbounded or hostile payloads | Medium | Medium | Specify hard byte/node/string/depth/history bounds before parsing fixtures. |

### Open Questions

- Which current Avalonia package/version and project target should the spike
  use? Decide during spike planning from current official package metadata.
- Should the isolated project enter the main solution after the fixture
  milestone, or stay in a separate spike solution until read-only polling is
  accepted?
- What exact DTO limits and fixture sizes become the version-one contract?

---

## Recommendation

Findings support proceeding. First draft a compact feature spec for the
Avalonia read-only spike covering the bounds, fixture matrix, independent
project/settings roots, and non-shipping constraints. Then run:

```text
/planner Fixture-only Avalonia sensor explorer (see docs/discovery-pre-avalonia-readiness.md)
```

Review and accept the generated spec/campaign plan, commit both, then cut and
independently verify a clean/promotable candidate from that exact checkpoint.
Only then does implementation begin with one integration owner registering the
central Avalonia package versions and scaffolding the isolated non-shipping
project/solution. It may then run the fixture-only Avalonia lane beside the
reliability lane and either native Phase 0 or operator utilities. It must leave
live polling, shared-contract extraction, packaging, hardware ownership, and
cutover for separately accepted gates.
