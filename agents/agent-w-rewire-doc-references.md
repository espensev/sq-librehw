# Agent Task — Rewire Doc References

**Scope:** Update every reference to the moved docs: AGENTS.md source map, docs/README, campaign-playbook, campaign-backlog, and inter-doc links; update project.toml smart-test/conflict-zone/module globs; extend the stale-reference gate move-map and allow-list to cover the doc moves.

**Depends on:** Agent V

**Output files:** `.codex/skills/project.toml`, `AGENTS.md`, `eng/ci/tests/Test-NoStaleReferences.ps1`

---

## Context — read before doing anything

1. `AGENTS.md`
2. Review the owned files listed above before editing.
3. Respect dependency outputs from: Agent V

---

## Task

Implement the scoped change above within the owned files. Keep edits
bounded to this task's declared ownership and preserve interfaces
expected by dependent agents.

## Exit Criteria

- Update every reference to the moved docs: AGENTS.md source map, docs/README, campaign-playbook, campaign-backlog, and inter-doc links; update project.toml smart-test/conflict-zone/module globs; extend the stale-reference gate move-map and allow-list to cover the doc moves. is implemented in the owned files for Agent W.
- Owned files updated by this agent: .codex/skills/project.toml, AGENTS.md, eng/ci/tests/Test-NoStaleReferences.ps1.
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
| REWIRE-DOC-REFERENCES-001 | Done | agent-w | `.codex/skills/project.toml`, `AGENTS.md`, `eng/ci/tests/Test-NoStaleReferences.ps1` | Update every reference to the moved docs: AGENTS.md source map, docs/README, campaign-playbook, campaign-backlog, and inter-doc links; update project.toml smart-test/conflict-zone/module globs; extend the stale-reference gate move-map and allow-list to cover the doc moves. | Completed agent scope and verification. |

