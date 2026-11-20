# Position-dependent geodesic vent temperature

Implemented from the clean local `eb46f65` founder-emergence commit on the existing branch. **Source-level review only: no Unity compilation, automated tests, simulations, ecological fixtures or benchmarks were run, and no test fixtures were created or edited.** All gameplay and performance results remain untested.

Saved startup settings were not written. The saved Hydrogen → Fermentation probability remains **0.02**, along with the current gradual mode, 10-second mean interval, 300-second initial delay, ongoing emergence off, population cap, metabolism and movement parameters. Startup schema stays v24. No new configuration toggle or tuning controls were introduced.

## Biological temperature authority

`GeodesicBiologyRuntime.GetBiologicalTemperature(index, state)` now resolves an organism's authoritative cell/layer **and packed world position** through `GetAgentTemperatureSample` → `SampleBiologicalTemperature` → `GeodesicExperiencedTemperatureField.TrySampleTemperature`.

The original `GetHabitatTemperature(cell, layer)` and `temperatureByNode` remain **coarse ocean background caches only**. Experienced temperature is never written into that node cache. Near indexed vents, a separate reusable value-type cache is keyed by biology step, coarse-temperature epoch, node and exact position. Organisms sharing a node cannot reuse each other's result unless they occupy the same position. Movement invalidates a result through its changed position; swap-back is safe because temperature depends on position/habitat rather than organism identity. Outside indexed influence candidates, sampling returns the coarse background without a transform or outlet search.

Consumers updated together:

- Reaction throughput and temperature performance for all existing geodesic metabolic paths.
- Maintenance/stress, dormancy, heat/cold exposure, mortality and reproduction eligibility.
- The temperature component of existing local chemotactic food readiness.
- Growth snapshots, selected-organism diagnostics and population temperature telemetry.
- Founder acceptance at candidate positions.
- Newborn reproduction permission at the **actual separated child position**, after normal template inheritance/mutation and existing birth placement.

Individual preferred ranges, margins, dormancy floors, exposure thresholds, energy/resource equations, mutation rolls and locomotion settings are unchanged. Existing sulfur/fermentation mutation gates check resources/inherited carbon and template validity; they do not acquire a new environmental temperature gate. Temperature physiology governs the resulting child normally. Anchored children retain existing nearby birth placement; no offspring or established organism is moved to seek a better temperature.

## Spatial profile and coordinates

The existing model is retained: a flat hot core, quintic smootherstep decay outside the core, `sqrt(strength)` scaling, and the **maximum** overlapping outlet contribution. Temperature blends from the authoritative coarse background toward the source-fluid reference, rather than adding another temperature anomaly. The source reference reads the existing submarine source setting on `GeodesicOceanTemperatureField` (currently 350°C / 623.15 K / 662°F). Weaker outlets need not reach the full reference temperature.

Visible markers and thermal source anchors retain the same exact simulation-cell direction and completed visible seafloor radius. Chemical injection remains at its authoritative source node. Organisms retain their actual world positions; no visual-only relocation to the seabed is added.

To prevent the rendered thickness of a bottom layer from making all its organisms appear far from a vent, the submarine thermal reference is a **radial segment from the visible seafloor anchor to that source's bottom-layer center**. Temperature uses the actual 3D distance to this segment. Horizontal displacement always matters. Radial displacement above or below the segment attenuates normally; the unresolved lower half-layer does not impose an artificial source-to-layer-center penalty.

All valid ocean layers may query submarine influence; actual 3D separation determines vertical reach. This replaces the old hard bottom-layer-only eligibility switch and avoids an artificial thermal cutoff at a layer boundary. Terrestrial outlets remain restricted to land surface queries. The field is read-only and never changes ocean heat capacity, heat integration, resources or transport.

The old fixed one-ring outlet index is replaced by a radius-aware indexed angular cap with a conservative cell-footprint pad. It is built once per world, traverses real topology neighbors across both land and ocean for lookup coverage, and stores compact per-cell outlet ranges. The vent contribution therefore has no arbitrary one-ring cell cutoff. The **coarse background still has the existing piecewise cell/layer resolution**, so different coarse backgrounds can retain boundary steps; this change does not interpolate or replace that authority.

## Preserved spatial scales

The authoritative horizontal conversion is **1000 km per planet-local Unity unit**, independent of a rendering transform. Existing radius defaults were preserved:

| Distance | Local Unity units | Horizontal kilometres |
|---|---:|---:|
| Hot-core radius, weakest to strongest | 0.02–0.08 | 20–80 |
| Falloff outside each core | 0.12 | 120 |
| Total core-plus-falloff reach | 0.14–0.20 | 140–200 |

These are much larger than real hydrothermal mixing zones. No automatic rescaling or balance adjustment was made. Some strong source cells may lie wholly inside the flat core, with a favorable region in nearby cells rather than inside the source cell itself.

The current physical ocean-depth scale is **0.0175**: a radial Unity unit represents 17.5 physical km below sea level. Corresponding radial core/falloff distances are 0.35–1.4 km and 2.1 km, with 2.45–3.5 km total radial support **beyond the unresolved reference segment**. That segment's length depends on the actual outlet and bottom layer. The physical zone is consequently anisotropic. Diagnostic distance to the seabed source uses horizontal length conversion and depth-scaled radial separation; it is distinct from the model's distance to the unresolved thermal column.

`[GeodesicVentThermalScale]` prints effective configured radii, conversion and source reference at initialization. Radius/index edits require world regeneration; no index is rebuilt each biology tick.

## Founder placement and failure handling

Batch and gradual modes share a bounded search of **32 uniform-area random points** in the selected outlet's core-plus-falloff footprint. There is no preferred placement radius, prescribed ring or explicit temperature-seeking steering force. Position draws use a separate `biologySeed ^ 0x5448524D` stream, leaving the emergence-time, mutation, reproduction and locomotion streams separate.

Each candidate resolves its actual cell using bounded real-neighbor traversal and checks cell containment, active ocean habitat and that cell's bottom-layer center. Its experienced temperature must satisfy the existing hydrogenotroph reproduction-temperature policy, allowing marginal conditions when current settings permit them. Its **actual node** must contain at least one configured reference reaction's H2 and CO2. Successful founders use that same point for biological position, packed position, rendering, local temperature and resource access. A legitimate neighboring-cell candidate updates all cell/layer fields.

The cached gradual outlet ranking retains production/resource/O2 opportunity. It no longer rejects an outlet solely because the source-center coarse temperature is unsuitable; suitability is resolved at the sampled point. Failed placement excludes that cached outlet until a normal later refresh. Existing event timing, event caps, population limits and optional global ongoing-emergence settings are preserved. No continuous per-vent emergence was added.

If all candidates fail, the event is rejected without creating an organism, awarding energy, reserving food or retrying indefinitely. The batch count returns actual successful placements. Initial energy, carbon, lifespan and age conventions remain unchanged. In particular, an immediate batch can now fail if no local H2/CO2 is available at startup; a suitable position or eventual survival is not guaranteed, and the code does not extend the delay or replenish rejected initial events.

Founder telemetry adds thermal, food and invalid-candidate rejection totals and failed placements containing thermal rejections. Candidate totals differ from event totals; a failed event can contain mixed rejection reasons. Direct vent coverage continues to identify the selected outlet of origin, even when its founder is placed at a nearby valid node. Existing one-ring vent-occupancy diagnostics are not the same spatial measurement as thermal influence.

## Inspector and manual diagnostics

- The active **GeodesicCellPicker** popup distinguishes selected surface temperature, actual coarse temperatures for every active ocean layer, and experienced temperature at the selected direction projected to the bottom-layer center. Detailed mode labels the probe and world position, outlet/system, source reference, influence, column distance and seabed-source distance.
- `PlanetResourceMap` inspection APIs now have an explicit geodesic data path, independent of legacy initialization. The position-taking API expects an actual world point in geodesic mode; the direction-taking API resolves the geodesic cell separately. `PlanetCellInspectorPanel` formats these snapshots with authoritative layer values and a labeled probe, retaining legacy estimates only in LegacyCubeSphere. The active geodesic popup remains the existing picker; the legacy controller is not enabled alongside it.
- The global HUD labels geodesic minimum/mean/maximum as **surface** temperatures. These are neither source-fluid nor individual experienced temperatures.
- On `ReplicatorManager`, **Biology Diagnostics → Refresh Selected Growth** reports the selected organism's actual position, experienced/coarse/source temperatures, outlet identity and distances, individual range, performance, temperature reproduction permission and exposure state. Existing structured growth readouts include these fields. Displayed absolute temperatures use the selected °C/K/°F unit.
- `[GeodesicVentThermalNiches]` adds ten-second aggregate counts for overheated cores, temperature-reproduction-permitted organisms within influence, organisms outside influence, and temperature-cause deaths. These reuse the existing population diagnostic pass; there is no per-organism console logging.

## Files changed

| Area | Files |
|---|---|
| Position query, profile, radius-aware lookup, probe/snapshot formatting | `GeodesicExperiencedTemperatureField.cs`, new `GeodesicExperiencedTemperatureField.Inspection.cs` |
| Read-only configured source reference | `GeodesicOceanTemperatureField.cs` |
| Shared biological query/cache and consumer integration | `GeodesicBiologyRuntime.cs`, new `GeodesicBiologyRuntime.Thermal.cs`, `GeodesicBiologyRuntime.Locomotion.cs` |
| Founder candidate placement and wiring | new `GeodesicBiologyRuntime.FounderThermalPlacement.cs`, `GeodesicBiologyRuntime.FounderEmergence.cs`, `ReplicatorManager.cs` |
| Temperature and selected-organism observations | `GeodesicBiologyTemperatureTelemetry.cs`, `GeodesicBiologyDiagnostics.cs` |
| Inspector data and visible labels | `PlanetResourceMap.cs`, `GeodesicCellPicker.cs`, `PlanetCellInspectorPanel.cs`, `ReplicatorHudPresenter.cs` |

The three new source files include Unity metadata. No scenes, startup persistence mappings, meshes, test fixtures, transport code or metabolic transaction implementations were edited.

## Performance, review and remaining limits

Source review checked signatures/call sites, coarse-versus-position cache ownership, coordinate transforms, candidate ownership/layers, birth/death sampling, finite guards and bounded loops. `git diff --check` reports no whitespace errors. This is **not compilation or execution evidence**.

Hot paths allocate no per-organism objects: sample/cache entries are structs in reusable capacity-grown arrays. Only indexed nearby outlets are examined, with coarse-only fallback elsewhere. Repeated metabolism/lifecycle/telemetry queries reuse the same-position sample; steering before movement and metabolism after movement may correctly require different samples. Lookup building occurs during world initialization, never each tick. Coarse temperature caching and reaction/resource authority remain unchanged. Diagnostic strings and selected-cell layer arrays are produced only at diagnostic/UI boundaries. Throughput and memory usage at 100000 organisms have not been measured.

The reference segment is an unresolved mixing representation, not heat transport or fluid dynamics. Radius defaults remain physically very large. Piecewise coarse backgrounds, hard-zero thermal clipping and temporal chemotaxis limitations remain. Food is still resolved at coarse node/layer scale, and some vents may offer no jointly viable temperature/resource positions. Existing purely visual division offsets remain presentation-only; biological temperature samples the packed physical center.

Older thermal/founder fixtures assume bottom-only eligibility or exact source-center placement and need adaptation before they can validate this model. They were not changed or run. Historical seed-283 ecology/performance results predate this temperature integration and are not results for the new behavior. Menu/popup wrapping, actual compilation, colony development, cross-boundary behavior and large-population responsiveness remain for manual review.

## Short Unity checklist

1. Start the saved gradual profile and confirm the resolved settings retain .02 fermentation probability, current movement/population values and the existing founder delay/count/interval. Inspect candidate failures if founders do not appear.
2. Click a strong submarine source. In detailed cell diagnostics, compare the ordinary selected surface, coarse bottom-layer background, source-fluid reference and explicitly projected bottom-center probe. Change °C/K/°F and check consistent conversions.
3. Inspect organisms at different positions in the **same** ocean node using selected growth diagnostics. Their coarse value should agree while experienced values can differ across a gradient. A whole strong-source cell can be inside its flat core; check nearby gradient cells too.
4. Watch organisms entering excessive heat: performance and temperature reproduction permission should fall; existing enabled exposure damage should accumulate. Far from influence, experienced temperature should equal the coarse background.
5. Observe founder positions, anchored descendants and boundary crossings. Founders should occupy accepted physical points with matching cell/layer/resource ownership; descendants should remain near their parents without thermal relocation. Inspect the aggregate thermal-niche and founder-rejection counters.
6. Assess long-session colony development, popup layout and responsiveness at the desired population. A ring-shaped colony, complete vent coverage or founder survival is not guaranteed, and no steering or survival bonus forces those outcomes.
