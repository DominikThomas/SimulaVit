Status: Implemented
Last reviewed: 2026-06-28
Current summary: Layered ocean is the authoritative ocean resource path for core simulation, movement/radius placement, steering, predation, metabolism reads/writes, light/heating, organic-carbon transport, and persistence. Compatibility bridges remain intentional migration/diagnostic safeguards.
See also: Docs/PROJECT_STATUS_AND_NEXT_STEPS.md

# Layered Ocean Plan

## Current state
- PlanetResourceMap supports up to 5 ocean layers per cell.
- Layered per-cell resources already exist for O2, OrganicC, H2, H2S, CH4, DissolvedFe2Plus.
- Vertical processes already exist: surface oxygenation, vertical diffusion, marine snow, layer light attenuation, layer temperature offsets.
- Replicators store current/preferred ocean layer indices.
- Movement, rendering, predation/proximity, steering, and metabolism are layer-aware in core simulation paths.
- Light/heating and effective ocean chemistry reads (including OrganicC and CO2 usage in gameplay loops) are layer-aware.

## Compatibility status (intentional bridges)
- Aggregate `Get(...)`/`Add(...)` APIs are intentionally retained as compatibility bridges while older callsites are fully migrated.
- `GetResourceForCellLayer(...)` and `AddResourceForCellLayer(...)` keep fallback-to-aggregate paths for invalid/unknown layer context.
- Spawning/resource interaction APIs still contain compatibility fallbacks for non-ocean contexts and transitional callsites.
- Detailed compatibility telemetry remains available behind `enableLayeredCompatibilityCallsiteTelemetry` for targeted audits.

## Target model

### Ocean layers
- Ocean cells can have 1..5 active layers depending on local depth.
- Layer 0 = top (sea surface)
- Highest valid layer index = deepest water layer (just above seafloor)

### Physical placement (movement + rendering)
Replicators are placed on spherical shells based on (cell, layer):

- Land organisms:
  - unchanged (existing surface behavior)

- Lowest ocean layer:
  - behaves like current seafloor organisms
  - stays at terrain/ocean-floor radius (existing behavior preserved)

- Top ocean layer:
  - placed at ocean surface radius (sea level)

- Intermediate layers:
  - placed proportionally between seafloor and surface radius
  - monotonic spacing per cell

### Spawning rules
- Spontaneous hydrotroph spawning:
  - remains on the seafloor layer
- Other organisms:
  - spawn into a valid layer for the target cell
  - may inherit parent layer or use habitat-based selection
- Compatibility fallback remains for non-ocean and invalid-layer contexts until all spawning/resource callsites are layer-explicit.

### Ecology model
- Layered resources remain authoritative for ocean simulation.
- Surface layers: high light, higher O2, UV exposure.
- Deep layers: low light, more reduced chemistry (H2, H2S, Fe2+, CH4).
- Mid layers: potential oxygen minimum zone behavior.

## Constraints
- Preserve current gameplay as much as possible.
- Do not break land organisms.
- Keep seafloor organisms behaving exactly as before.
- Keep aggregate Get/Add compatibility bridges.
- Keep layer read/write fallbacks for safety and migration compatibility.
- Avoid large object↔SoA sync regressions.

## Key invariant
At any time:
- Each replicator has exactly one valid layer for its current cell.
- Its world position radius matches that layer.
