# Feature Spec: <Feature Name>

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Draft <!-- Draft | Accepted | Implemented | Verified | Done -->  
**Updated:** YYYY-MM-DD  
**Related docs:** <links>  
**Purpose:** one-line statement of the feature.

> Copy this file to `docs/feature-<slug>.md`. Replace placeholders, remove this instruction block, and keep the spec as short as the feature allows. A small feature can have short answers, but acceptance and verification must be concrete.

## 1. Summary

<What the feature is and the user-visible outcome.>

## 2. Problem and Motivation

<The concrete problem from observed usage or maintainer need. Why existing behavior is not enough.>

## 3. Goals and Non-Goals

**Goals**

- <what this feature will do>

**Non-goals**

- <what this feature will not do>

## 4. Behavior Specification

<Normative behavior. Include states, triggers, defaults, edge cases, and failure behavior. Be specific enough that a reviewer can compare code to this section.>

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | <none or details> |
| Settings/config | <none or details> |
| Remote web/API | <none or details> |
| Logging/files | <none or details> |
| Hardware/admin flow | <none or details> |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | <how this stays easy to merge, or why divergence is accepted> |
| `net472` vs `net10.0-windows` | <expected behavior on each target> |
| DPI/multi-monitor | <if UI-affecting> |
| Hardware/admin rights | <if hardware-affecting> |
| Existing settings/users | <migration or preservation plan> |

## 7. Acceptance Criteria

- [ ] <observable result>
- [ ] <observable result>
- [ ] Existing behavior not in scope remains unchanged: <name the critical surfaces>.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app, if affected | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | 0 errors |
| Runtime smoke | <manual launch/menu/dialog/API check> | <expected result> |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| <question> | <spec acceptance / implementation / release> | <default> |

If this stays `Draft`, list the decisions that block acceptance here. Do not hide acceptance-blocking UI, settings, data, or verification choices in Implementation Notes.

## 10. Implementation Notes

<Fill during or after implementation. Record important file paths, intentional deviations, or follow-up items.>

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| YYYY-MM-DD | <command/manual check> | <pass/fail> | <notes> |
