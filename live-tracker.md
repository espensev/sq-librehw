# Plan-001 fixture explorer tracker

This tracker records source-campaign work. It does not describe or authorize a
live deployment.

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVSPIKE-001 | Done | agent-a | package/project bootstrap and contracts | Fixture-only Avalonia explorer | Restored three isolated projects, passed the Core Release build, froze loader/snapshot interfaces, and passed 114/114 release-system assertions. Launch candidate creation internally verified clean/promotable state, but independent dual-shell current-source verification was not recorded before launch. |
| AVSPIKE-002 | Done | agent-b | bounded parser and fixtures | Fixture-only Avalonia explorer | Enforced every byte/depth/node/child/string/identity bound with atomic immutable projection; 49/49 parser and replacement cases passed. A reliable Windows ACL-denied test remains unavailable, although typed catches exist. A non-cooperative caller stream can outlive logical supersession; the app uses a bounded `FileStream`, and view-model request identity prevents stale publication. |
| AVSPIKE-003 | Done | agent-c | Avalonia shell and headless UI | Fixture-only Avalonia explorer | Covered explicit states, accessible labels, keyboard focus/navigation, cancellation, and safe errors; 19/19 UI/headless cases passed. Known `AVLN3001` source-build warning remains; attended smoke is pending. |
| AVSPIKE-004 | Done | agent-d | integration, verification, docs | Fixture-only Avalonia explorer | Added seven real parser-to-view-model cases (68 pre-D, 75/75 final) and passed 10/10 source/regression gates. Manager candidate/package inspection and attended smoke remain pending; read-only polling is worth a separate spec only. |
