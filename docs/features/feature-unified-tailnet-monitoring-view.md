# Feature Spec: Unified Tailnet Monitoring View

**Machine scope:** SND-DESK presentation and Tailscale Serve configuration.
SND-HOST remains a read-only dashboard source.

**Status:** deployed and live-verified on SND-DESK
**Updated:** 2026-09-10

## Problem and motivation

SND-DESK and SND-HOST each expose a working LibreHardwareMonitor dashboard,
but separate URLs do not provide the requested main monitoring surface. The
operator needs one browser view on SND-DESK that keeps both machines visible
and independently usable.

## Goals

- Make the SND-DESK `https://snd-desk.tailbd0610.ts.net:8443/` root the
  combined monitoring view.
- Show the complete SND-DESK and SND-HOST dashboards side-by-side on desktop.
- Preserve standalone access and independent polling, controls, failures, and
  browser state for each dashboard.
- Keep every route private to the tailnet.

## Non-goals

- Merge, rewrite, or proxy either machine's `data.json` contract.
- Change either LibreHardwareMonitor binary, listener, settings, or task.
- Make the dashboards public with Tailscale Funnel.
- Hide machine identity or blend readings into one ambiguous sensor tree.

## User-visible behavior

- The combined page has two labelled panes: SND-DESK on the left and SND-HOST
  on the right.
- Each pane embeds the existing complete dashboard and remains independently
  scrollable and interactive.
- `Reload both` refreshes both embedded dashboards.
- Standalone links open the SND-DESK dashboard at private HTTPS port `9443`
  and the existing SND-HOST dashboard at private HTTPS port `8443`.
- Below 900 CSS pixels, panes stack vertically instead of becoming unusably
  narrow.
- A pane reports `loaded` only after its iframe emits a load event. The embedded
  dashboard remains authoritative for freshness and telemetry errors.

## Operational contract

- The repo-owned page is
  `ops/deploy/snd-desk/Unified-LibreHardwareMonitor.html`.
- The stable deployed copy belongs at
  `E:\Monitoring\LibreHW\UnifiedDashboard\index.html`.
- SND-DESK Tailscale Serve maps private HTTPS `9443` to
  `http://127.0.0.1:8085` and private HTTPS `8443` to the stable page.
- SND-DESK HTTPS `443` remains owned by Usage Atlas and is not changed.
- Production mutation requires verified identity
  `snd-desk/ca96d510-7d87-4cec-8e1a-bd8fc3866903`.
- Rollback restores private HTTPS `8443` to
  `http://127.0.0.1:8085`, disables the `9443` Serve route, and leaves the
  deployed static file inert.

## Compatibility and risk

- The panes are separate HTTPS origins. Browser storage and dashboard settings
  remain isolated per machine and port.
- The local dashboard moves from the former SND-DESK `8443` origin to `9443`,
  so browser-local dashboard customization does not automatically migrate.
- Embedding requires the existing dashboards to continue omitting restrictive
  `X-Frame-Options` or `frame-ancestors` response headers.
- The combined page reads neither cross-origin DOM nor API data; browser
  same-origin rules remain intact.
- Tailscale daemon configuration is machine-local runtime state and must be
  revalidated after Tailscale reset or device re-enrollment.

## Acceptance criteria

- [x] The SND-DESK `8443` root renders both labelled dashboards concurrently.
- [x] Both embedded dashboards reach their live telemetry state.
- [x] Both standalone links return HTTP 200.
- [x] Root, `data.json`, and `metrics` remain HTTP 200 for both underlying
  LibreHardwareMonitor instances.
- [x] Tailscale reports the combined and standalone desk routes as tailnet-only.
- [x] SND-DESK HTTPS `443` remains unchanged.
- [x] No LibreHardwareMonitor executable, settings, listener, task, source
  build, SND-HOST runtime, or public Funnel configuration changes.

## Verification plan

1. Validate the source page contains both exact machine URLs, two titled
   iframes, standalone links, and responsive layout rules.
2. Reverify SND-DESK identity before copying the stable page or changing Serve.
3. Probe both underlying roots, `data.json`, and `metrics` over private HTTPS.
4. Render the combined root in the isolated DevHome browser and confirm both
   nested documents show their matching machine identity and live readings.
5. Record the final Tailscale Serve/Funnel status and Git diff.

## Verification record

Live SND-DESK acceptance on 2026-09-10:

- The installed verifier returned `VERIFIED` for `snd-desk` instance
  `ca96d510-7d87-4cec-8e1a-bd8fc3866903` immediately before mutation.
- The deployed page at
  `E:\Monitoring\LibreHW\UnifiedDashboard\index.html` matched the repo-owned
  source at SHA-256
  `7AF272CA24DE284C08CF2AAC949FA056AB16CC3E4A3F0C178CFB30EB9F6A8EB5`.
- The combined `8443` root, both standalone roots, and both machines'
  `data.json` and `metrics` routes returned HTTP 200.
- An isolated 1920 x 1080 Opera Developer render showed SND-DESK and SND-HOST
  side-by-side with matching machine labels, loaded states, and fresh telemetry.
- Tailscale reported `8443` and `9443` as tailnet-only. Existing HTTPS `443`
  remained a proxy to `127.0.0.1:3857`; the temporary static probe route was
  removed.
- Static checks passed for exactly two frames, exact source URLs, desktop split
  layout, and inline JavaScript syntax. No product build was needed because
  neither application source nor binary changed.
