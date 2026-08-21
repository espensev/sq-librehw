# Agent Task — Docs Move Close

**Scope:** Add the Plan-005 ledger row and criterion evidence in docs/campaign-history.md (without automatic acceptance), record Plan-005 tracker rows in live-tracker.md, close the Phase 2 documentation-grouping roadmap item (at its new docs/architecture path), and remove the completed Plan-005 section from the backlog.

**Depends on:** Agent V, Agent W

**Output files:** `docs/campaign-history.md`, `live-tracker.md`, `docs/architecture/refactor-roadmap.md`, `docs/campaign-backlog.md`

---

## Context — read before doing anything

1. `AGENTS.md`
2. Review the owned files listed above before editing.
3. Respect dependency outputs from: Agent V, Agent W

---

## Task

Implement the scoped change above within the owned files. Keep edits
bounded to this task's declared ownership and preserve interfaces
expected by dependent agents.

## Exit Criteria

- Add the Plan-005 ledger row and criterion evidence in docs/campaign-history.md (without automatic acceptance), record Plan-005 tracker rows in live-tracker.md, close the Phase 2 documentation-grouping roadmap item (at its new docs/architecture path), and remove the completed Plan-005 section from the backlog. is implemented in the owned files for Agent X.
- Owned files updated by this agent: docs/campaign-history.md, live-tracker.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md.
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
| DOCS-MOVE-CLOSE-001 | Done | agent-x | `docs/campaign-history.md`, `live-tracker.md`, `docs/architecture/refactor-roadmap.md`, `docs/campaign-backlog.md` | Add the Plan-005 ledger row and criterion evidence in docs/campaign-history.md (without automatic acceptance), record Plan-005 tracker rows in live-tracker.md, close the Phase 2 documentation-grouping roadmap item (at its new docs/architecture path), and remove the completed Plan-005 section from the backlog. | Completed agent scope and verification. |

