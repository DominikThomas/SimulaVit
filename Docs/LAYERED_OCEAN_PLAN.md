# Layered Ocean Plan

## Current state
- PlanetResourceMap supports up to 5 ocean layers per cell.
- Layered per-cell resources already exist for O2, OrganicC, H2, H2S, CH4, DissolvedFe2Plus.
- Vertical processes already exist: surface oxygenation, vertical diffusion, marine snow, layer light attenuation, layer temperature offsets.
- Replicators already store current/preferred ocean layer indices.
- Steering already reads layered resources.

## Not finished yet
- Movement still places replicators on a single terrain/ocean-floor shell.
- Rendering implicitly assumes a single shell (surface or bottom offset).
- Predation/proximity is still keyed only by surface cell and is not layer-aware.
- Spawning/reproduction/habitat validation may still assume a single surface shell.

## Target model

### Ocean layers
- Ocean cells can have 1..5 active layers depending on local depth.
- Layer 0 = top (sea surface)
- Highest valid layer index = deepest water layer (just above seafloor)

### Physical placement (movement + rendering)
Replicators must be placed on spherical shells based on (cell, layer):

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

Important:
- Movement and rendering must use the SAME radius logic.
- There must be a single authoritative function for layer → radius mapping.
- Layer transitions should not teleport agents; prefer gradual changes (e.g. one layer per step or smoothed convergence).

### Spawning rules
- Spontaneous hydrotroph spawning:
  - MUST remain on the seafloor layer only
- Other organisms:
  - spawn into a valid layer for the target cell
  - may inherit parent layer or use habitat-based selection

### Ecology model
- Resources are already layer-dependent and must remain authoritative.
- Surface layers: high light, higher O2, UV exposure
- Deep layers: low light, more reduced chemistry (H2, H2S, Fe2+, CH4)
- Mid layers: potential oxygen minimum zone behavior

### Predation / proximity (future step)
- Must become layer-aware:
  - primary interactions: same (cell, layer)
  - optional: adjacent layers

## Sequencing rules
1. Do not rewrite everything at once.
2. First make movement/radius layer-aware.
3. Then make rendering consistent with movement.
4. Then fix spawn/habitat validation.
5. Then make predation/proximity layer-aware.
6. Then update scent/fear systems.
7. Keep compatibility paths until all gameplay reads use layered APIs.

## Constraints
- Preserve current gameplay as much as possible.
- Do not break land organisms.
- Keep seafloor organisms behaving exactly as before.
- Avoid large object↔SoA sync regressions.
- Prefer small, reviewable commits.

## Key invariant
At any time:
- Each replicator has exactly one valid layer for its current cell.
- Its world position radius MUST match that layer.