# Feature Workflow

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Accepted
**Updated:** 2026-07-06
**Purpose:** define how new features are specified, implemented, and verified in this fork.

## 1. Purpose

This workflow keeps feature work clear before implementation starts. It is adapted from the PiPlay spec-first process, but scaled down for this repository: LibreHardwareMonitor is an active codebase, so review, build, launch, and concrete bugfix tasks do not need a new spec every time.

The goal is simple: when we add a new feature or change user-visible behavior, reviewers should be able to compare the code against a written spec and know what "done" means.

## 2. When a Feature Spec Is Required

Create or update a feature spec before implementation for:

- new menus, dialogs, controls, graph behavior, tray behavior, or web/API behavior;
- settings, persistence, config, logging, or data-contract changes;
- hardware access changes or behavior that depends on admin rights;
- changes that intentionally diverge from upstream LibreHardwareMonitor;
- any task where acceptance criteria are not obvious from a bug report.

A new feature spec is usually not required for:

- build, launch, or monitoring requests;
- read-only review passes;
- spelling or documentation cleanup;
- small compile fixes;
- direct bugfixes where the expected behavior is already clear.

If a small fix grows into a behavior change, add the spec then.

## 3. Lifecycle

```text
Idea -> Draft Spec -> Accepted Spec -> Implemented -> Verified -> Done
```

- **Idea:** the feature exists as a request or rough note.
- **Draft Spec:** a file exists, usually copied from `feature-spec-template.md`, but open decisions remain.
- **Accepted Spec:** the maintainer has approved the behavior, scope, acceptance criteria, and verification plan.
- **Implemented:** code has landed against the accepted spec.
- **Verified:** build and manual/runtime checks have been recorded.
- **Done:** the spec records any implementation deltas and all acceptance criteria pass or have explicit follow-up items.

Approval can be explicit in chat or represented by the maintainer asking to implement an already-reviewed spec.
A draft spec being linked from this workflow is not acceptance by itself; keep `Status: Draft` until the maintainer has accepted the scope, behavior, acceptance criteria, and verification plan.

## 4. Promotion Gates

Before a feature becomes buildable work, the spec should answer these questions:

| Gate | Question |
|---|---|
| Problem | What concrete user or maintainer problem is this solving? |
| Scope | What are the goals and non-goals? |
| Behavior | What exactly happens, including edge cases and failure states? |
| UI/API/data | Which menus, settings, APIs, logs, or persisted values change? |
| Compatibility | What are the upstream-sync, framework, DPI, admin, or hardware risks? |
| Acceptance | How will we know the feature is done? |
| Verification | Which build commands, launch checks, or manual QA steps prove it? |

The spec does not need to be long. It does need to be testable.

## 5. Naming and Location

Use one file per feature in `docs/`:

```text
docs/feature-<slug>.md
```

Use `docs/feature-spec-template.md` as the starting point.

Existing docs:

- `feature-graph-menu.md` is an implemented feature spec and can be used as a local example.
- `feature-graph-panel-controls.md` is a draft spec for graph-local controls.
- `feature-long-window-graph-rendering.md` is a draft spec for extreme-preserving long-window rendering.
- `feature-real-time-axis.md` is the implemented spec for local clock-time labels on the graph T axis.
- `feature-webserver-json-stream.md` is the implemented spec for NaN/Infinity-safe remote web server JSON endpoints.
- `feature-unique-gpu-sensor-ids.md` is the implemented spec for unique NVIDIA GPU sensor identifiers (12VHPWR voltage collision) and the CSV logger one-column guard.
- `feature-graph-ui-review-fixes.md` is the implemented spec for five review-sweep fixes (column-width persistence, BindingList leak, plot recompute coalescing, time-axis label resolution, axis-step tie-break).
- `feature-sensor-list-bulk-selection.md` is the implemented spec for bulk multi-select context-menu actions, type-group actions, keyboard verbs, and Graph Inputs multi-row toggling.
- `feature-web-dashboard-customization.md` is the shipped v2 spec for dashboard-local sensor hiding, pinned/custom cards, drag reordering, and layout/background customization. Its drawer/list UI is transitional and superseded by the card-first v3 follow-up.
- `feature-web-dashboard-card-truth.md` is the accepted/in-progress v3 spec for honest gauge ranges, real/derived limits, fan percent gauges with RPM readouts, duplicate hardware identity, two-GPU display, dashboard-local aliases/renames, card-carried details/actions, and visible UI ordering for cards, panels, rows, and network subgroups.
- `ai-guide.md` is the start-here orientation doc: hard invariants, the phase-shipping loop, the lessons ledger, and the maintained overall-plan snapshot (read order: ai-guide → AGENTS.md → handoff §0 → v3-next-plan §4).
- `feature-web-dashboard-expansion-layout.md` is the verified spec for Phase D2 — the anchored-overlay card expansion that fills horizontal space with zero displacement (closes the "tall narrow strip" audit finding and the 2026-07-07 operator displacement complaint).
- `feature-web-dashboard-versioned-routes.md` is the verified spec for serving dashboard preview versions under `/dash/<version>/` while keeping `/` stable.
- `review-sensor-list-bulk-selection-follow-up.md` records the corrected post-implementation review and ranked remediation plan for the bulk-selection feature.
- `local-ui-customizations.md` records additional local changes that shipped with the same work.

## 6. Implementation Rules

When implementation starts:

- read the accepted feature spec and relevant existing docs first;
- keep edits scoped to the specified behavior;
- preserve settings and existing user workflows unless the spec says otherwise;
- preserve dense, auditable sensor data by default;
- avoid weakening remote web/API behavior unless explicitly specified;
- update the spec if implementation discovers a better behavior or a constraint.

For this fork, pay special attention to:

- `net10.0-windows` as the primary modern app target;
- `net472` compatibility when the project still targets it;
- admin-required hardware access and PawnIO prompts;
- DPI and multi-monitor behavior;
- upstream merge risk.

## 7. Verification

Each feature spec should include acceptance criteria and a verification plan. Typical checks:

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Add manual checks when relevant, for example:

- launch the rebuilt app and confirm it stays responsive;
- open the changed menu/dialog;
- toggle the setting and restart the app;
- verify no unexpected Application event-log errors;
- verify the web endpoint or log output if the feature touches those surfaces.

After implementation, fill in the feature spec's verification log or add implementation notes with the build/run evidence.

## 8. How Agents Should Use This

For a new feature request:

1. Check whether a matching `docs/feature-*.md` already exists.
2. If not, create one from `docs/feature-spec-template.md`.
3. Fill enough detail to make implementation and review concrete.
4. Implement only after the maintainer approves the spec or explicitly asks to continue.
5. Verify the implementation against the spec and record any deltas.

For direct "full-auto" bugfix/build requests, continue to act pragmatically. This workflow should prevent vague feature work, not slow down clear maintenance.
