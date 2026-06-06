# LibreHardwareMonitor Sev IQ - Contributor and agent notes

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Active implementation fork  
**Updated:** 2026-06-06  
**Purpose:** keep feature work spec-first without blocking normal review, build, launch, and bugfix work.

This repository is not specs-only: product code exists and normal maintenance can proceed. The rule is narrower: **new features and meaningful behavior changes need a clear feature spec before implementation starts**, unless the maintainer explicitly asks for a small direct fix or exploratory spike.

## 1. First classify the task

Before editing, decide which lane the user asked for:

- **Review/audit:** inspect code, docs, build output, or runtime behavior. Stay read-only unless the user asks for fixes.
- **Build/launch/verification:** run the requested build, clean, launch, or monitoring steps. No feature spec is needed.
- **Bugfix:** fix a concrete defect. For small fixes, implement directly and document the result. If the fix changes user-facing behavior or settings/API semantics, update or create a feature spec.
- **Spec/doc refinement:** edit docs only. Keep requirements traceable to acceptance and verification.
- **New feature or behavior change:** start with `docs/feature-workflow.md` and a feature spec from `docs/feature-spec-template.md`.

If a requested implementation is ambiguous and acceptance is unclear, draft the spec or ask for the missing decision before writing product code.

## 2. Source-of-truth map

- `docs/feature-workflow.md`: how new feature specs move from idea to verified implementation, including the current draft feature list.
- `docs/feature-spec-template.md`: copy this for a new feature spec.
- `docs/feature-graph-menu.md`: existing graph-menu feature spec; treat as the precedent for local feature specs.
- `docs/local-ui-customizations.md`: traceability for local changes that shipped outside the graph-menu spec.
- `docs/discovery-librehw-sync-upgrade.md`: upstream sync and modernization discovery notes.

When adding or changing a requirement, update the nearest feature spec, traceability note, or verification section in the same pass. A behavior change without acceptance criteria is unfinished.

## 3. Definition of ready for new features

A feature is ready for implementation only when its spec covers:

- problem and motivation;
- goals and non-goals;
- user-visible behavior, including edge cases and failure states;
- affected UI/menu paths, settings, API, logs, or data contracts;
- compatibility risks, especially upstream sync, `net472` vs `net10.0-windows`, admin rights, DPI, and hardware access;
- acceptance criteria;
- verification plan, including build commands and any manual launch/runtime checks.

## 4. During implementation

- Implement from the accepted spec, not from unstated assumptions.
- Keep edits scoped to the feature or bugfix.
- If implementation discovers the spec is wrong, update the spec before or with the code change.
- Preserve upstream compatibility unless the spec explicitly accepts a local-fork divergence.
- Keep generated output, logs, and local scratch artifacts out of source.

Useful baseline commands:

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

## 5. Before handoff

For docs/spec edits:

- check that new docs are linked from the workflow or relevant spec;
- search for stale path/name claims in docs you changed;
- confirm acceptance criteria and verification sections are not empty.

For product-code edits:

- run the repo-appropriate build;
- launch or otherwise exercise the changed workflow when practical;
- update the spec verification log or implementation notes after the behavior is checked.
