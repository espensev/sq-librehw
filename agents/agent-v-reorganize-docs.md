# Agent Task — Reorganize Docs

**Scope:** git mv the 11 feature-*.md to docs/features/, refactor-roadmap.md and repository-build-output-cleanup.md to docs/architecture/, and retire the 2 discovery-*.md point-in-time reviews after recording a fold note. Tooling-coupled docs (README, campaign-backlog/history/playbook, rendered campaign-plan-*.md) stay at docs/ root.

**Depends on:** (none)

**Output files:** `docs/feature-avalonia-fixture-sensor-explorer.md`, `docs/feature-host-log-management.md`, `docs/feature-host-operator-utilities.md`, `docs/feature-independent-text-scaling.md`, `docs/feature-local-release-system.md`, `docs/feature-memory-ui-reliability.md`, `docs/feature-native-ui-modernization.md`, `docs/feature-release-packaging.md`, `docs/feature-sensor-workspace.md`, `docs/feature-standard-context-layouts.md`, `docs/feature-thermal-trends.md`, `docs/feature-upstream-sync-2026-07-25.md`, `docs/feature-web-dashboard-studio-view.md`, `docs/refactor-roadmap.md`, `docs/repository-build-output-cleanup.md`, `docs/discovery-librehw-structural-audit.md`, `docs/discovery-pre-avalonia-readiness.md`

---

## Context — read before doing anything

1. `AGENTS.md`
2. Review the owned files listed above before editing.
3. Respect dependency outputs from: (none)

---

## Task

Implement the scoped change above within the owned files. Keep edits
bounded to this task's declared ownership and preserve interfaces
expected by dependent agents.

## Exit Criteria

- git mv the 11 feature-*.md to docs/features/, refactor-roadmap.md and repository-build-output-cleanup.md to docs/architecture/, and retire the 2 discovery-*.md point-in-time reviews after recording a fold note. Tooling-coupled docs (README, campaign-backlog/history/playbook, rendered campaign-plan-*.md) stay at docs/ root. is implemented in the owned files for Agent V.
- Owned files updated by this agent: docs/feature-avalonia-fixture-sensor-explorer.md, docs/feature-host-log-management.md, docs/feature-host-operator-utilities.md, docs/feature-independent-text-scaling.md, docs/feature-local-release-system.md, docs/feature-memory-ui-reliability.md, docs/feature-native-ui-modernization.md, docs/feature-release-packaging.md, docs/feature-sensor-workspace.md, docs/feature-standard-context-layouts.md, docs/feature-thermal-trends.md, docs/feature-upstream-sync-2026-07-25.md, docs/feature-web-dashboard-studio-view.md, docs/refactor-roadmap.md, docs/repository-build-output-cleanup.md, docs/discovery-librehw-structural-audit.md, docs/discovery-pre-avalonia-readiness.md.
- All verification commands in this spec complete successfully.
- The work remains consistent with plan-005 plan requirements.

---

## Constraints

- Existing tests must pass — `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
- Do not expand scope beyond the files and behaviors listed in this spec.
- Escalate only through the completion summary; do not leave placeholders in the spec.

---

## Verification

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
```

---

## Do NOT

- Edit files outside the declared ownership without a documented reason.
- Leave the scope partially implemented after running verification.

---

## Post-completion

Update `live-tracker.md`:

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| REORGANIZE-DOCS-001 | Done | agent-v | `docs/feature-avalonia-fixture-sensor-explorer.md`, `docs/feature-host-log-management.md`, `docs/feature-host-operator-utilities.md`, `docs/feature-independent-text-scaling.md`, `docs/feature-local-release-system.md`, `docs/feature-memory-ui-reliability.md`, `docs/feature-native-ui-modernization.md`, `docs/feature-release-packaging.md`, `docs/feature-sensor-workspace.md`, `docs/feature-standard-context-layouts.md`, `docs/feature-thermal-trends.md`, `docs/feature-upstream-sync-2026-07-25.md`, `docs/feature-web-dashboard-studio-view.md`, `docs/refactor-roadmap.md`, `docs/repository-build-output-cleanup.md`, `docs/discovery-librehw-structural-audit.md`, `docs/discovery-pre-avalonia-readiness.md` | git mv the 11 feature-*.md to docs/features/, refactor-roadmap.md and repository-build-output-cleanup.md to docs/architecture/, and retire the 2 discovery-*.md point-in-time reviews after recording a fold note. Tooling-coupled docs (README, campaign-backlog/history/playbook, rendered campaign-plan-*.md) stay at docs/ root. | Completed agent scope and verification. |

