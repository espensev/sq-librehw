# Discovery: RTX 50-series GPU Hot Spot temperature (GH #10)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Discovery — **resolved (negative): no fix is currently possible**. No code change. See §"Decisive finding".
**Updated:** 2026-06-14
**Related:** GH #10; [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md) (the other NVIDIA/`NvidiaGpu.cs` change); [`local-ui-customizations.md`](local-ui-customizations.md)

## Why this is a discovery note, not a fix

The correct value for the 50-series hot spot depends on **what NVAPI returns on the actual RTX 5090** —
and the only such card is the maintainer's live machine. Reading source alone cannot produce a
*verified* fix, and shipping a wrong value writes bad data to the most safety-relevant GPU temperature
(it drives fan control and feeds ThermalTrace).

This note first records what is provable from source, then resolves the question against the
maintainer's own controller telemetry from that 5090. **The answer is negative: the hot spot is not
exposed via NVAPI on this card, so there is no correct index to plug in — see §"Decisive finding".**
`NvidiaGpu.cs` is unchanged.

## What is provable from source

### 1. It's an upstream stub, not a fork regression

`LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs` (50-series branch, ~line 588):

```csharp
// RTX 50xx series
if (Name.StartsWith("NVIDIA GeForce RTX 50", ...)) {
    _hotSpotTemperature.Value = 0;                                  // stubbed
    _temperatures[0].Value     = thermalSensors.Temperatures[1] / 256.0f;  // Core
    _memoryJunctionTemperature.Value = thermalSensors.Temperatures[2] / 256.0f;
}
```

Upstream `LibreHardwareMonitor/LibreHardwareMonitor` `master` has the **identical** stub
(`_hotSpotTemperature.Value = 0;`) in its 50-series branch. The 50-series undocumented thermal-array
layout differs from the 40-series (where idx 1 = hot spot); upstream left the 50-series hot-spot index
undetermined and stubbed it. Because the value stays 0, `if (_hotSpotTemperature.Value != 0)
ActivateSensor(...)` is skipped, so the sensor is absent from the UI and the CSV.

### 2. The issue's suggested fix — "port the undoc index N" — is unreliable (controller evidence)

The maintainer's sibling controllers (`D:/Development/Thermals/nvg-gpu/unofficial-nvapi/nvapi-controller/`)
attempt the hot spot on the **same card** via direct NVAPI, with logic *more* careful than a fixed index
(and, as §"Decisive finding" shows, they currently get no hot-spot value either):

- `nvapi_thermals.cpp::discover_sensors` probes all 32 undoc thermal slots, then **deliberately does
  not publish a fixed undoc slot as the hot spot**: `out.hotspot_index` is left at its default `-1`,
  with the comment *"do not auto-publish index 1 as hotspot: some boards expose a limit-style sensor
  there instead."* It instead records `documented_hotspot_sensor_idx` from the **documented** thermal
  settings, by matching `target == NV_THERMAL_TARGET_HOTSPOT` (enum value **16**,
  `nvapi_undoc_types.h`) with `current_temp > 0`.
- `hotspot_available = (documented_hotspot_sensor_idx >= 0) || (hotspot_index >= 0)`
  (`nvapi_controller.cpp`).

Caveat (don't overstate): the controller's *runtime* read (`gpu_probe.cpp:83`,
`temps[thermal_disc.hotspot_index] / 256.0`) does use the undoc slot — but only when discovery has
*proven* that slot, which the documented-target validation gates. The takeaway is the **negative**: a
hard-coded undoc `Temperatures[N]` for the 50-series (what GH #10 proposes) is exactly what the
controller refuses to trust. Don't port a guessed N.

### 3. What LHM's NvApi binding lacks for the documented path

LHM already reads the documented thermal settings into `_temperatures[]` and labels them by target
(`NvidiaGpu.cs` ~line 87). But:

- **No hot-spot target.** `NvThermalTarget` (`Interop/NvApi.cs`) defines `None,Gpu,Memory,PowerSupply,
  Board,VisualComputing{Board,Inlet,Outlet},All=15` — **no `Hotspot = 16`**. A documented hot-spot
  sensor would hit the `_ => "GPU"` fallback and be mislabeled "GPU".
- **3-sensor cap.** `MAX_THERMAL_SENSORS_PER_GPU = 3`; `NvThermalSettings.Sensor` is
  `SizeConst = 3`; `GetThermalSettings` (`NvidiaGpu.cs:1252`) queries `NvThermalTarget.All` with
  `Count = 3`. If a documented hot-spot sensor sits beyond slot 3 on the 50-series, LHM never sees it.

## Decisive finding: the hot spot is not exposed via NVAPI on this 5090

The open question — does NVAPI return a usable hot spot on this card — is answered directly by the
maintainer's own controller telemetry on the same RTX 5090. **It does not.**

Live status (`NVG-SmoothControl/runtime/logs/nvg_control_status.json`, `gpuName: NVIDIA GeForce
RTX 5090`, updated 2026-06-13/14), and every A/B-run status snapshot from that day:

```
"thermal": { "coreC": 30.97, "memJunctionC": 42.0,
             "hotspotC": 0.0000, "hotspotIndex": -1, "primarySource": "memj" }
statusLine: "Core: 31.0C  HS--: --.-C  MJ: 42.0C ..."   // "HS--" = hot spot unavailable
```

The controller logs the hot spot as a **column** (`hotspot_C`, `hotspot_idx` in `nvg_control_*.csv`),
but the data is empty: across every archived CSV that *has* the column (2026-06-10 → 2026-06-13)
`hotspot_C` is `0.000` and `hotspot_idx` is `-1` in every row sampled. (Older logs, e.g. 2026-03, predate
the column entirely — they never tracked a hot spot. No controller CSV on this machine carries a non-zero
hot-spot reading.) The controller's `has_hotspot` is therefore false (both
`documented_hotspot_sensor_idx` and `hotspot_index` are `-1`), which is why it falls back to
`primarySource: "memj"`.

**Independent corroboration (ThermalTrace, the downstream consumer).** ThermalTrace's own fold spec
(`ThermalTrace/docs/PARSER_CROSS_SOURCE_FOLD_SPEC.md` §1, `LOG_FORMATS.md`) states, scoped to the same
card: *"On this RTX 5090 (ASUS ROG ASTRAL), LHM and HWiNFO expose only GPU Core and GPU Memory Junction —
there is no 'GPU Hot Spot' sensor. Only the svg/nvg controllers report a `hotspot_C`. (Verified: no
`Hot Spot` friendly name in either LHM build; not present in the real HWiNFO header.)"* So **HWiNFO has no
hot spot for this card either** — it is not merely an NVAPI-path quirk. (The ~61–67 °C hot-spot stats that
appear elsewhere in that spec are from a test fixture, `sensor_samples-fixture.csv`, not this card.)

**Conclusions:**

- There is **no correct undoc index `N`** to give LHM — the maintainer's own discovery, which probes all
  32 slots and cross-checks the documented HOTSPOT target, proves none on this card/driver. GH #10's
  "port index N" is not achievable: `N` does not exist here.
- The documented path is **also** a dead end on this driver (no `HOTSPOT (16)` sensor with a valid temp
  in the thermal settings), so the otherwise-attractive enum+label fix would activate nothing.
- LHM's `_hotSpotTemperature.Value = 0` stub (→ not activated → not logged) therefore **matches the
  hardware reality**: there is genuinely no hot-spot reading to surface. "GPU Hot Spot" being absent from
  the LHM CSV is correct, not a defect, on this card.
- The issue's premise that the controllers "do read a GPU hot spot via direct NVAPI on the same card" is
  **not borne out** by their current telemetry (`HS--`, value 0, index -1, primary = memj).

## Recommendation

1. **No LHM code change.** Any index would fabricate data; the enum+label path would activate nothing.
   The stub is the correct behavior while the card doesn't expose the sensor.
2. **Reframe / close GH #10** as *blocked on the hardware*: **this RTX 5090 (ASUS ROG ASTRAL), on the
   current driver,** does not expose a usable GPU hot-spot temperature — not via LHM/NVAPI, the
   controllers' NVAPI discovery, *or* HWiNFO (per ThermalTrace's verification above). This is not a
   LHM/upstream bug to fix in code. (Scope note: this is verified for *this* card only — it is **not** a
   claim that all RTX 50-series / Blackwell cards lack a hot-spot sensor.)
3. **Re-check trigger.** Revisit only if the situation changes on the hardware — concretely, when the
   controller's telemetry starts reporting a non-zero `hotspot_C` / `hotspot_idx >= 0` (e.g. after a
   driver/NVAPI update that exposes the sensor). At that point the controller's discovered index is the
   ground truth, and the fix is small: prefer the documented HOTSPOT target if present within LHM's
   3-slot window (add `Hotspot = 16` to `NvThermalTarget` + a `"GPU Hot Spot"` label case — no ABI
   change; the existing `_temperatures[]` loop activates it); otherwise port the controller-proven undoc
   index. Either way: feature spec + runtime verification on the card, mirroring
   [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md).

## Consumer note (ThermalTrace)

GH #10 assumed a fixed LHM-fixed hot spot would land at `/gpu-nvidia/<n>/temperature/2`
(`index = thermalSettings.Count + 1`). **ThermalTrace's implemented fold spec uses `/temperature/1`, not
`/temperature/2`** (`PARSER_CROSS_SOURCE_FOLD_SPEC.md` §2/§8 Decision A): it maps `hotspot_C` /
`gpu_hotspot_c` — and the LHM friendly name `GPU Hot Spot` *if it were ever emitted* — to
`/gpu-nvidia/0/temperature/1`, with Memory Junction at `/temperature/3`. ThermalTrace canonicalizes by
**friendly name**, so even if a future LHM build emitted the sensor at numeric index 2, the consumer would
still fold it to `/temperature/1` by name. So no LHM-side index alignment is required; the path
discrepancy is already handled downstream.
