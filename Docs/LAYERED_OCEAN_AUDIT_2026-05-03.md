# Layered Ocean Migration Audit (May 3, 2026)

## Scope
Audit of layered-ocean migration status across movement, rendering, spawning, predation, steering, metabolism, resource storage, light/heating, atmosphere coupling, OrganicC transport, and compatibility bridges.

## Status table

| Area | Status | Notes |
|---|---|---|
| Movement | **Fully layer-aware** | Movement post-job shell placement clamps to a valid ocean layer and uses `GetOceanLayerShellRadius(...)`, keeping world radius tied to layer per-cell. |
| Rendering | **Fully layer-aware (via movement authority)** | Render system uses simulation/world position from population state; position radius comes from movement’s layer shell mapping, so rendering remains consistent with movement. |
| Spawning | **Partially layer-aware / compatibility fallback** | Spawn orchestration is extracted, but layer placement still depends on manager-side candidate/spawn helpers and viability hooks; hydrotroph floor behavior is preserved via existing spawn path constraints rather than a fully isolated layered spawn policy object. |
| Predation / proximity | **Fully layer-aware** | Predation bins are keyed by `(cell,layer)` and support optional adjacent-layer search depth. |
| Steering | **Fully layer-aware** | Habitat score and scent/resource reads resolve layer context via current/preferred layer and layered map queries. |
| Metabolism | **Fully layer-aware (with explicit fallbacks)** | Layered reads/writes use `GetResourceForCellLayer`/`AddResourceForCellLayer` wrappers and attempt current->preferred->aggregate compatibility only when context is missing/invalid. |
| Resource storage APIs | **Partially layer-aware / compatibility fallback** | `Get(...)`/`Add(...)` still support aggregate callers and bridge to layered resources in ocean cells; layered-specific APIs exist and are preferred. |
| Light / heating | **Fully layer-aware** | Layer light factors, layer temperature offsets, layered solar/vent factors, and vertical layered process updates are in place. |
| CO2 atmosphere coupling | **Still aggregate by design (surface-first bridge)** | Atmosphere exchange is intentionally top-layer/surface coupled, with legacy CO2 arrays synchronized for global coupling and existing consumers. |
| OrganicC transport | **Fully layer-aware** | OrganicC has layered downward settling (marine snow), lateral spread, and tiny upward bleed with layer-array operations. |
| Compatibility bridges | **Intentionally present** | Aggregate Add/Get compatibility, layered read/write fallback counters, and callsite telemetry remain to protect legacy call paths and support migration auditing. |

## Compatibility bridges that should remain (for now)

1. **Aggregate `Get(...)` bridge for layered ocean resources**
   - Why keep: legacy and debug/UI callers can continue reading effective values while migration finishes.
2. **Aggregate `Add(...)` bridge into layered arrays**
   - Why keep: unresolved callsites still write safely, with deterministic distribution and telemetry instead of silent breakage.
3. **`AddResourceForCellLayer(...)`/`GetResourceForCellLayer(...)` fallback-to-aggregate paths**
   - Why keep: protects cases with invalid/unavailable layer context (land cells, stale indices, transitional systems).
4. **Legacy array synchronization from layered state (`SyncLegacyOceanResourceFromLayers`)**
   - Why keep: preserves atmosphere/global debug and remaining non-layered consumers during staged conversion.
5. **Per-callsite compatibility telemetry arrays/counters**
   - Why keep: needed to validate that fallback volume trends downward and to catch regressions before removing bridges.

## Tuning/validation next steps (not further migration)

1. **Tune layered rates**
   - `layeredVerticalMixRate`, `layeredMarineSnowRate`, `layeredOrganicCLateralSpreadRate`, `layeredOrganicCUpwardBleedRate`, and layered heating/light factors should be tuned via fixed-seed sweeps.
2. **Validate ecological gradients**
   - Track top-vs-bottom means (`debugLayeredTop*Mean`, `debugLayeredBottom*Mean`) for O2/OrganicC/H2S/S0 and verify stable stratification under different vent strengths.
3. **Validate compatibility pressure**
   - Use compatibility counters (`debugLayeredAggregateAddCompatibility*`, read/write fallback counts, callsite arrays) as release gates; target monotonic decline, not immediate zero.
4. **Predation vertical reach balance**
   - Treat `predatorAdjacentLayerSearchDepth` as gameplay tuning only (0 default, small nonzero for experiments) rather than migration work.
5. **Performance/quality checks**
   - Repeat existing migration profile protocol (warm-frame medians/p95 at steps-per-frame 1 and >=20) and confirm no regression from layered pathways.
6. **Invariant checks**
   - Keep runtime checks that each ocean replicator has valid `(cell,layer)` and radius matching `GetOceanLayerShellRadius` to catch data drift early.

## Conclusion
Layer-awareness is substantially complete across core simulation loops (movement, steering, metabolism, predation, layered resource physics). Remaining bridges are deliberate safety/compatibility mechanisms and should be reduced only after telemetry proves low usage; near-term effort should focus on parameter tuning and validation.
