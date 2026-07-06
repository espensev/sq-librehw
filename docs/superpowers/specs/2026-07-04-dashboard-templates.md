# Feature Spec: Dashboard Preview Tabs (Secondary Multi-Route Lane)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Draft / secondary UI preview lane
**Updated:** 2026-07-04
**Related specs:** [`feature-web-dashboard-customization.md`](../../feature-web-dashboard-customization.md), [`feature-web-dashboard-card-truth.md`](../../feature-web-dashboard-card-truth.md), [`2026-07-04-web-dashboard-telemetry-console-design.md`](2026-07-04-web-dashboard-telemetry-console-design.md)

---

## 1. Summary

Add lightweight dashboard tabs/routes that can display and compare alternative UI arrangements — initially **Main**, **Gaming**, and **Storage** — without changing telemetry truth, gauge scaling, or the card-first control contract.

This is intentionally secondary. The tabs are a preview/inspection lane for seeing UI variants and context-focused layouts. They must not distract from or replace the primary v3 work in [`feature-web-dashboard-card-truth.md`](../../feature-web-dashboard-card-truth.md): no hardcoded/unlabeled maxima, real/derived ranges where possible, clean two-GPU identity, fan % gauges with RPM readouts, card-carried detail/actions, and graceful unknown data.

## 2. Motivation

Different monitoring contexts may benefit from different views:
- **Main** - general overview, all subsystems visible
- **Gaming** - CPU/GPU-focused layout for comparing a game-oriented UI
- **Storage** - NVMe/HDD-focused layout for comparing a storage-oriented UI

The first purpose is visual comparison and quick navigation, not a new authority for sensor truth. Template defaults are allowed only as display hints layered over the same honest card model.

## 2.1 Priority and Boundaries

- Primary work stays in the card-truth/card-first spec.
- Tabs/routes are secondary and should be cut as an isolated UI worktree only after the truth/range/identity baseline is stable.
- Tabs must not introduce guessed hardware maxima, separate sensor identities, or hidden server-side behavior.
- Browser-local state is acceptable for experiments, but the route cannot make raw telemetry disappear from `data.json`, `/metrics`, CSV, or the desktop sensor tree.
- If per-route state exists, it must reuse the same normalized dashboard state schema and the same range/provenance rules as Main.

## 3. Architecture

### 3.1 Route Structure

| Route | Template | Purpose |
|-------|----------|---------|
| `/` or `/#/main` | Main | General hardware overview |
| `/#/gaming` | Gaming | CPU/GPU thermal monitoring during gaming |
| `/#/storage` | Storage | Drive health, lifespan, I/O metrics |

### 3.2 Isolated State Namespaces

If route-specific state is implemented, each dashboard stores its state in a separate localStorage key:

```
sq.dashboard.main      → Main dashboard state
sq.dashboard.gaming    → Gaming dashboard state
sq.dashboard.storage   → Storage dashboard state
```

Route state is display state only. Each dashboard may independently store:
- `hiddenSensorIds` — different sensors hidden per dashboard
- `pinnedCards` — different cards per dashboard
- `panelOrder` — different panel order per dashboard
- `rate` — different visual poll preference per dashboard
- `theme` — different theme per dashboard
- `collapsedPanels` — different collapse state per dashboard
- `cardStyle` — different style per dashboard

### 3.3 Hash Routing

Client-side router uses `window.location.hash` for navigation:

```javascript
// URL parsing
/#/main     → main dashboard
/#/gaming   → gaming dashboard
/#/storage  → storage dashboard
/           → redirects to /#/main (default)
```

**Why hash routing:**
- Zero C# changes (pure client-side)
- Instant navigation (no page reload)
- Browser history works natively
- Hyperlinks work everywhere
- Can upgrade to History API later

## 4. Template Defaults

### 4.1 Main Template (Default)

Current dashboard defaults:

```javascript
{
  version: 1,
  hiddenSensorIds: [],
  pinnedCards: [],
  panelOrder: [],
  pinnedOrder: [],
  graphsEnabled: false,
  paused: false,
  rate: 2,
  theme: 'dark',
  collapsedPanels: {},
  cardStyle: {}
}
```

### 4.2 Gaming Template (Preview Default)

Preview default for a CPU/GPU-focused view. It must remain truthful under `feature-web-dashboard-card-truth.md`: no guessed GPU/CPU power ceilings, each GPU separate, and fan cards still gauge Control % when available.

```javascript
{
  version: 1,
  hiddenSensorIds: [
    // Hide non-thermal sensors
    '/lpc/nct6701d/0/temperature/3',
    '/lpc/nct6701d/0/temperature/5',
    '/lpc/nct6701d/0/temperature/6',
    '/ram/*/data',           // Hide RAM usage
    '/nic/*/data',           // Hide network traffic
    '/hdd/*/data',           // Hide HDD activity
  ],
  pinnedCards: [
    { id: '/amdcpu/0/temperature/0', title: 'CPU Core Temp' },
    { id: '/gpu-nvidia/0/temperature/0', title: 'GPU Core Temp' },
    { id: '/amdcpu/0/power/0', title: 'CPU Power' },
    { id: '/gpu-nvidia/0/power/0', title: 'GPU Power' },
  ],
  panelOrder: ['cpu', 'gpu'],      // CPU/GPU panels first
  pinnedOrder: ['cpu-temp', 'gpu-temp', 'cpu-power', 'gpu-power'],
  graphsEnabled: true,             // Show trend graphs
  paused: false,
  rate: 1,                         // Fast poll (1 second)
  theme: 'dark',
  collapsedPanels: {
    'motherboard': true,
    'ram': true,
    'network': true,
    'storage': true,
    'controllers': true,
  },
  cardStyle: {
    'default': 'gauge',            // Speedo gauges for fast reads
  }
}
```

### 4.3 Storage Template (Preview Default)

Preview default for a drive-health view. It must keep duplicate same-name drives distinct by `HardwareId`, not merge identical model names into one panel.

```javascript
{
  version: 1,
  hiddenSensorIds: [
    // Hide non-storage sensors
    '/amdcpu/*/temperature',
    '/gpu-nvidia/*/temperature',
    '/gpu-nvidia/*/power',
    '/gpu-nvidia/*/clock',
    '/gpu-nvidia/*/fan',
    '/amdcpu/*/power',
    '/amdcpu/*/clock',
    '/amdcpu/*/voltage',
    '/ram/*/temperature',
    '/ram/*/data',
    '/nic/*/data',
    '/lpc/nct6701d/0/temperature/3',
    '/lpc/nct6701d/0/temperature/5',
    '/lpc/nct6701d/0/temperature/6',
  ],
  pinnedCards: [
    { id: '/nvme/0/temperature/0', title: 'NVMe #0 Temp' },
    { id: '/nvme/0/life/0', title: 'NVMe #0 Life' },
    { id: '/hdd/0/temperature/0', title: 'HDD #0 Temp' },
  ],
  panelOrder: ['storage'],         // Storage panel first
  pinnedOrder: ['nvme0-temp', 'nvme0-life', 'hdd0-temp'],
  graphsEnabled: false,
  paused: false,
  rate: 5,                         // Slow poll (5 seconds)
  theme: 'light',                  // Light theme for readability
  collapsedPanels: {
    'cpu': true,
    'gpu': true,
    'motherboard': true,
    'ram': true,
    'network': true,
    'controllers': true,
  },
  cardStyle: {
    'default': 'number',           // Numbers for precise reads
  }
}
```

## 5. UI Design

### 5.1 Dashboard Switcher (Tab Bar)

Located in the masthead, left of the existing controls:

```html
<nav class="dash-nav" role="tablist">
  <a href="#/main" class="dash-tab active" data-route="main">
    <span class="icon">⊞</span>
    <span class="label">Main</span>
  </a>
  <a href="#/gaming" class="dash-tab" data-route="gaming">
    <span class="icon">🎮</span>
    <span class="label">Gaming</span>
  </a>
  <a href="#/storage" class="dash-tab" data-route="storage">
    <span class="icon">💾</span>
    <span class="label">Storage</span>
  </a>
</nav>
```

**Visual design:**
- Horizontal pill/toggle bar
- Active route highlighted (theme-aware)
- Icons for visual distinctiveness
- Hover state shows destination
- Keyboard-accessible (←→ arrow keys navigate)

### 5.2 Route Indicator

Page title updates with current dashboard:

```javascript
// Gaming dashboard
document.title = 'Gaming — SQ Telemetry Console';

// Storage dashboard
document.title = 'Storage — SQ Telemetry Console';
```

## 6. Behavior Specification

### 6.1 Initial Load

1. Parse `window.location.hash`
2. Extract route name (default: `main`)
3. Load `sq.dashboard.{route}` from localStorage
4. If missing, apply template defaults for that route
5. Render dashboard with loaded state

### 6.2 Navigation

1. User clicks dashboard tab
2. Hash changes: `window.location.hash = '#/gaming'`
3. Router detects `hashchange` event
4. Parse new route: `gaming`
5. Save current dashboard state to `sq.dashboard.{currentRoute}`
6. Load `sq.dashboard.gaming` from localStorage
7. Render gaming dashboard
8. Update page title
9. Update tab active state

### 6.3 State Persistence

Each dashboard's state persists independently:

```javascript
// Switching from Main to Gaming
// 1. Save Main state
localStorage.setItem('sq.dashboard.main', JSON.stringify(mainState));

// 2. Load Gaming state
const gamingState = JSON.parse(localStorage.getItem('sq.dashboard.gaming'));

// 3. If Gaming never used, apply template defaults
if (!gamingState) {
  localStorage.setItem('sq.dashboard.gaming', JSON.stringify(GAMING_TEMPLATE));
}
```

### 6.4 Hyperlink Support

Any link to a dashboard route works:

```html
<!-- From external page -->
<a href="http://localhost:8085/#/gaming">Open Gaming Dashboard</a>

<!-- From within dashboard -->
<a href="#/storage" class="dash-link">View Storage</a>

<!-- From email, bookmark, etc. -->
<a href="http://localhost:8085/#/main">SQ Telemetry — Main</a>
```

## 7. Implementation Plan

Do not start this before the v3 truth/range/identity baseline is green, unless the operator explicitly asks for a UI-only preview spike.

### 7.1 Phase 1: Router Architecture
- Add `ROUTER` object to `console.js`
- Implement `init()`, `navigate()`, `link(route)`
- Wire `hashchange` event listener
- Add per-route storage key resolution

### 7.2 Phase 2: Template Defaults
- Define `MAIN_TEMPLATE`, `GAMING_TEMPLATE`, `STORAGE_TEMPLATE`
- Implement template-on-first-use logic
- Add route detection and template application

### 7.3 Phase 3: Tab Bar UI
- Add `<nav class="dash-nav">` to `index.html`
- Style tab bar in `console.css`
- Implement active state updates
- Add keyboard navigation (←→ arrows)

### 7.4 Phase 4: State Isolation
- Modify all state operations to use route-scoped keys
- Ensure no state leakage between routes
- Test navigation preserves each dashboard's state

### 7.5 Phase 5: Testing
- Add route switching tests
- Verify template defaults apply correctly
- Test state isolation (changes in Gaming don't affect Main)
- Verify hyperlinks work from all contexts

## 8. Compatibility and Risk

| Risk | Mitigation |
|------|------------|
| Hash URLs less elegant | Acceptable trade-off for zero C# changes |
| State isolation complexity | Clear namespace prefix, no shared keys |
| Template defaults too opinionated | User can override via Customize drawer |
| Upstream sync friction | Pure client-side changes, no server impact |
| Preview tabs hide truth work | Gate behind `feature-web-dashboard-card-truth.md`; no template branch may carry guessed maxima or duplicate hardware merges |
| Per-route state drifts from main state schema | Reuse the same normalizer and range/provenance resolver; route state is display preference only |

## 9. Open Decisions

| Decision | Options | Recommendation |
|----------|---------|----------------|
| Dashboard count | Fixed 3 vs. user-creatable | Fixed 3 for v1 |
| Template editability | Templates immutable vs. editable | Editable (user overrides via Customize) |
| Route naming | main/gaming/storage vs. configurable | Fixed for v1, configurable later |
| Template export | Individual vs. all dashboards | Export all (backup entire sq.dashboard.* namespace) |

## 10. Future Enhancements (Out of Scope)

- User-created dashboard templates
- Template import/export
- Template cloning (duplicate Gaming → "Streaming")
- Dashboard-specific hero selection logic
- Per-dashboard data polling (different data.json endpoints)
- History API routing (clean URLs without hashes)

---

## Appendix A: Router Implementation Sketch

```javascript
const ROUTER = {
  routes: ['main', 'gaming', 'storage'],
  currentRoute: null,

  init() {
    window.addEventListener('hashchange', () => this.navigate());
    this.navigate(); // Initial load
  },

  navigate() {
    const hash = window.location.hash.slice(2); // Strip '#/'
    const route = this.routes.includes(hash) ? hash : 'main';

    if (this.currentRoute !== route) {
      // Save previous state
      if (this.currentRoute) {
        SQ.saveState(this.currentRoute);
      }

      // Load new state
      this.currentRoute = route;
      SQ.loadState(route);
      SQ.render();
      this.updateUI();

      // Update page title
      document.title = `${this.label(route)} — SQ Telemetry Console`;
    }
  },

  link(route) {
    window.location.hash = '/' + route;
  },

  label(route) {
    const labels = { main: 'Main', gaming: 'Gaming', storage: 'Storage' };
    return labels[route] || 'Dashboard';
  },

  updateUI() {
    // Update tab active states
    document.querySelectorAll('.dash-tab').forEach(tab => {
      tab.classList.toggle('active', tab.dataset.route === this.currentRoute);
    });
  }
};

// State storage keys
SQ.storageKey = function(route) {
  return `sq.dashboard.${route || ROUTER.currentRoute}`;
};

SQ.saveState = function(route) {
  const key = SQ.storageKey(route);
  localStorage.setItem(key, JSON.stringify(SQ.state));
};

SQ.loadState = function(route) {
  const key = SQ.storageKey(route);
  const raw = localStorage.getItem(key);

  if (!raw) {
    // Apply template defaults
    const template = SQ.templateFor(route);
    SQ.state = SQ.clone(template);
    localStorage.setItem(key, JSON.stringify(SQ.state));
  } else {
    SQ.state = SQ.normalizeDashboardState(JSON.parse(raw));
  }
};

SQ.templateFor = function(route) {
  switch (route) {
    case 'gaming': return GAMING_TEMPLATE;
    case 'storage': return STORAGE_TEMPLATE;
    default: return MAIN_TEMPLATE;
  }
};
```

---

## Appendix B: Polished Tab Bar CSS (Main-First)

Integrates with existing design system (Chakra Petch, cyan accent, glass-cockpit aesthetic). Main tab gets distinctive treatment; Gaming/Storage remain minimal for now.

```css
/* === Dashboard Tab Bar (masthead, left of controls) === */
.dash-nav {
  display: flex;
  gap: 4px;
  margin-right: 18px;
  padding: 4px;
  background: color-mix(in srgb, var(--panel) 60%, var(--bg));
  border: 1px solid var(--line-soft);
  border-radius: 10px;
}

.dash-tab {
  position: relative;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border-radius: 8px;
  font-family: var(--mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--muted);
  text-decoration: none;
  transition: all 0.15s ease;
  border: 1px solid transparent;
}

.dash-tab:hover {
  background: color-mix(in srgb, var(--cy) 8%, var(--panel-2));
  border-color: color-mix(in srgb, var(--cy) 20%, var(--line-soft));
  color: var(--ink);
}

/* === Main tab: distinctive treatment === */
.dash-tab[data-route="main"] {
  border-color: color-mix(in srgb, var(--cy) 18%, var(--line-soft));
  background: linear-gradient(180deg, color-mix(in srgb, var(--cy) 6%, var(--panel)), var(--panel-2));
  color: var(--cy);
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--cy) 8%, transparent) inset,
              0 4px 12px -4px color-mix(in srgb, var(--cy) 15%, transparent);
}

.dash-tab[data-route="main"]:hover {
  border-color: color-mix(in srgb, var(--cy) 35%, var(--line-soft));
  background: linear-gradient(180deg, color-mix(in srgb, var(--cy) 10%, var(--panel)), var(--panel-2));
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--cy) 12%, transparent) inset,
              0 6px 16px -4px color-mix(in srgb, var(--cy) 22%, transparent);
}

.dash-tab[data-route="main"].active {
  border-color: var(--cy);
  background: linear-gradient(180deg, color-mix(in srgb, var(--cy) 14%, var(--panel)), var(--panel-2));
  box-shadow: 0 0 0 1px var(--cy) inset,
              0 0 14px -4px var(--cy),
              0 6px 20px -6px color-mix(in srgb, var(--cy) 28%, transparent);
}

/* === Gaming/Storage tabs: simpler (for now) === */
.dash-tab[data-route="gaming"].active,
.dash-tab[data-route="storage"].active {
  border-color: var(--cy);
  background: color-mix(in srgb, var(--cy) 12%, var(--panel-2));
  color: var(--cy);
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--cy) 10%, transparent) inset,
              0 4px 12px -4px color-mix(in srgb, var(--cy) 18%, transparent);
}

.dash-tab .icon {
  flex: none;
  width: 14px;
  height: 14px;
  opacity: 0.85;
}

.dash-tab.active .icon {
  opacity: 1;
  filter: drop-shadow(0 0 4px color-mix(in srgb, var(--cy) 30%, transparent));
}

/* === 150ms crossfade on route change === */
main {
  transition: opacity 0.15s ease, filter 0.15s ease;
}

main.route-changing {
  opacity: 0;
  filter: blur(1px);
}

/* === Mobile: icons only, stacked === */
@media (max-width: 640px) {
  .dash-nav {
    order: 3; /* After brand, before spacer collapses */
    width: 100%;
    margin-right: 0;
    margin-top: 8px;
  }
  .dash-tab {
    flex: 1;
    justify-content: center;
    padding: 8px 10px;
  }
  .dash-tab .label {
    display: none;
  }
}
```
