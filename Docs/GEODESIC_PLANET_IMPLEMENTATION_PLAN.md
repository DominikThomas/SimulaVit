# Geodesic Planet Implementation Plan

## Purpose

This document is the persistent roadmap for migrating SimulaVit from the legacy cube-sphere grid to a geodesic icosphere-based planet.

Future Codex prompts should reference this file and update it whenever a phase is completed, changed, or split.

The long-term target is:

- a welded icosphere render surface;
- a separate geodesic simulation topology;
- mostly hexagonal cells with exactly 12 pentagons;
- deterministic generation from one master planet seed;
- one authoritative planet radius and terrain-height API;
- support for terrain, ocean, atmosphere, resources, temperature, vents, layered ocean chemistry, replicators, visuals, picking, and save/load;
- preservation of the legacy cube-sphere path until geodesic mode reaches sufficient feature parity.

---

# 1. Architectural principles

## 1.1 Separate render geometry from simulation topology

The visible planet mesh and simulation grid must not be the same data structure.

### Render geometry

The render mesh may use a higher icosphere subdivision for smooth terrain. It is responsible for:

- visible terrain;
- normals and vertex colours;
- collider geometry;
- ocean and atmosphere shells;
- visual overlays.

### Simulation topology

The simulation grid uses the dual of a subdivided icosphere:

- each icosphere vertex is one simulation-cell centre;
- exactly 12 cells have 5 neighbors;
- all remaining cells have 6 neighbors;
- adjacency comes from shared icosphere edges;
- each cell has a spherical area and shared-edge metrics.

Recommended current defaults:

- simulation subdivision: 4;
- render subdivision: 5.

A likely production-equivalent simulation subdivision is 5, giving 10,242 cells.

## 1.2 One authoritative radius

`PlanetGenerator.radius` is the single authoritative base planet radius.

All geodesic surface geometry must use:

```csharp
surfaceRadius = PlanetGenerator.radius + terrainHeightOffset;
```

No geodesic subsystem should own an independent base planet radius.

The following must share the same radius convention:

- terrain mesh;
- MeshCollider;
- debug outlines;
- cell picker;
- camera zoom and surface clearance;
- ocean surface;
- atmosphere shell;
- replicator placement;
- vent placement;
- layered-ocean radii.

Required shared queries:

```csharp
float BasePlanetRadius { get; }
float MinimumSurfaceRadius { get; }
float MaximumSurfaceRadius { get; }

float GetTerrainHeightAtDirection(Vector3 direction);
float GetSurfaceRadiusAtDirection(Vector3 direction);
float GetCellTerrainHeight(int cellIndex);
float GetCellSurfaceRadius(int cellIndex);
```

## 1.3 One master seed with stable derived seeds

The existing planet seed is the master seed.

Subsystems should derive stable domain seeds rather than reuse the raw seed directly.

Suggested domains:

- Terrain
- SurfaceVisuals
- Vents
- Climate
- Resources
- Biology
- Ocean
- Atmosphere

Do not use `string.GetHashCode()`, `object.GetHashCode()`, or runtime-dependent hashes.

Feature-specific custom seed overrides may remain available, but the default should be a deterministic domain seed derived from the master seed.

## 1.4 Direction-based sampling

Authoritative world queries should use normalized directions rather than render-vertex indices, texture pixels, or cube-face coordinates.

Examples:

```csharp
float EvaluateTerrainHeight(Vector3 unitDirection);
int DirectionToSimulationCell(Vector3 unitDirection);
float SampleTemperature(Vector3 unitDirection);
```

This ensures subdivision independence and avoids cube-face simulation seams.

## 1.5 Variable neighbor counts

Pentagons have 5 real neighbors. Hexagons have 6 real neighbors.

Never add duplicate, dummy, or fake sixth neighbors. Algorithms must iterate the actual neighbor count.

## 1.6 Cell area and conservation

Geodesic cells are similar in size but not exactly equal.

The simulation must distinguish:

- concentration;
- total cell inventory;
- source rate per area;
- global total;
- area-weighted global mean.

Diffusion and exchange should ultimately use cell area, shared-edge length, and center-to-center distance.

## 1.7 Preserve legacy mode during migration

Keep the legacy cube-sphere mode operational until geodesic mode reaches sufficient parity.

Startup selection:

- Cube Sphere (Legacy)
- Geodesic Icosphere

Old saves without grid metadata are legacy cube-sphere saves. Geodesic saves must never be loaded as legacy saves or vice versa.

---

# 2. Current implementation status

This section is based on reported branch summaries and local runtime fixes. Unity play mode remains the final source of truth.

## 2.1 Reported completed foundation

- startup selection between legacy cube-sphere and geodesic icosphere;
- persisted startup configuration;
- save schema version 3 with grid metadata;
- rejection of incomplete geodesic saves;
- welded icosphere topology generation;
- deterministic midpoint caching;
- 5/6-neighbor simulation adjacency;
- ordered dual polygon corners;
- spherical cell areas;
- neighbor angular distances and shared dual-edge estimates;
- topology validation and subdivision scaling tests;
- coherent indexed render mesh;
- combined geodesic outline mesh;
- brute-force direction-to-cell picking with deterministic tie-breaking;
- visible runtime cell-selection popup;
- runtime geodesic cell picker popup is resolution-aware, vertically scrollable, keeps its header/footer fixed, and blocks popup-local input from world picking; this diagnostics/UI-only refactor did not complete a new implementation phase;
- new Input System support;
- geodesic startup path that skips legacy resources, vents, replicators, and stepping;
- geodesic vertex-colour shader and runtime-owned material;
- deterministic procedural surface colours;
- deterministic direction-based terrain sampler;
- continent/basin shaping, domain-warped ridged mountains, and fine detail;
- separate simulation and render subdivisions;
- terrain presets;
- terrain-aware selection diagnostics;
- refreshed MeshCollider after displacement;
- outlines sampled against authoritative terrain radius.

## 2.2 Runtime fixes already identified

### Shared-edge initialization

Build all `DualCorners` entries before estimating shared dual edges. Topology metrics require two passes.

### Picker input

`GeodesicCellPicker` must use the new Input System when enabled.

### Picker visibility

The picker needs a visible popup or linked inspector, not only public debug fields.

### Radius consistency

A separate geodesic base radius caused camera and mesh scale disagreement. Permanent rule:

- use `PlanetGenerator.radius`;
- terrain values are offsets;
- radius is applied exactly once.

## 2.3 Recommended source organization

Organize by feature rather than generic C# type:

```text
Assets/
├── Scripts/
│   ├── Core/
│   │   ├── Startup/
│   │   └── Simulation/
│   ├── Planet/
│   │   ├── Common/
│   │   ├── LegacyCubeSphere/
│   │   ├── Geodesic/
│   │   │   ├── Grid/
│   │   │   ├── Terrain/
│   │   │   ├── Rendering/
│   │   │   └── Diagnostics/
│   │   ├── Environment/
│   │   │   ├── Ocean/
│   │   │   ├── Atmosphere/
│   │   │   ├── ResourceSimulation/
│   │   │   ├── Temperature/
│   │   │   ├── Vents/
│   │   │   └── Sediments/
│   │   └── Visuals/
│   ├── Biology/
│   │   └── Replicators/
│   ├── Persistence/
│   ├── UI/
│   └── Utilities/
├── Shaders/
│   └── Geodesic/
└── Tests/
    ├── EditMode/
    └── PlayMode/
```

This source organization is an organizational refactor, not completion of a simulation roadmap phase. Move `.meta` files with the assets so existing script, shader, and material GUIDs remain stable. If Unity needs metadata for newly introduced folders, let Unity generate only those missing folder `.meta` files on import rather than fabricating GUIDs by hand.

Placement rules for this structure:

- Do not add, remove, or rename C# namespaces as part of file movement; moving a C# file does not require a namespace change.
- Do not add assembly definition files during this organization pass; dependency boundaries need a dedicated audit later.
- Keep runtime geodesic diagnostics, picker scripts, validation used in builds, and debug renderers out of `Editor` folders.
- Do not create Unity-special `Resources`, `Plugins`, or `StreamingAssets` folders merely for organization. `ResourceSimulation` is an ordinary runtime folder for resource-map code.
- ShaderLab shader names should remain stable when shader files move, unless there is a demonstrated path-dependent reason to change them.
- Only update hard-coded references that genuinely depend on physical paths.

---

# 3. Implementation roadmap

## Phase 0 — Project organization and shared generation contract

### Goal

Stabilize architecture before ocean and atmosphere work.

### Work

- organize geodesic files into feature folders;
- centralize authoritative radius;
- centralize stable seed derivation;
- expose a compact generated-planet runtime descriptor;
- make camera consume generated planet radius;
- keep legacy and geodesic generation backends separate.

Suggested descriptor:

```csharp
public sealed class PlanetRuntimeDescriptor
{
    public PlanetGridType GridType;
    public int MasterSeed;
    public int GenerationVersion;

    public float BaseRadius;
    public float MinimumSurfaceRadius;
    public float MaximumSurfaceRadius;

    public int SimulationCellCount;
    public int CubeSphereResolution;
    public int GeodesicSimulationSubdivision;
    public int GeodesicRenderSubdivision;
}
```

### Validation gate

- no duplicate geodesic base-radius source;
- camera and mesh agree on size;
- derived terrain and visual seeds are stable;
- changing visuals does not change terrain;
- changing terrain does not change topology;
- legacy mode still starts.

---

## Phase 1 — Geodesic terrain and topology prototype

### Status

Mostly implemented.

### Required final validation

- subdivisions 2–5 compile and run;
- exactly 12 pentagons;
- all remaining cells have 6 neighbors;
- reciprocal adjacency and connected graph;
- area sum approximately `4π`;
- no visible mesh cracks or cube-face seam;
- terrain remains stable across render subdivisions;
- cell picking works;
- outlines follow displaced terrain;
- collider matches terrain;
- runtime cleanup works.

---

## Phase 2 — Sea-level visual and authoritative land/ocean classification

### Status

Implemented on the current geodesic child branch as a rendering/classification-only phase. This does not complete atmosphere, scalar diffusion, resources, layered oceans, chemistry, vents, biology, or geodesic save/load.

### Files added

- `Assets/Shaders/Geodesic/GeodesicOceanURP.shader` — dedicated transparent URP-compatible runtime geodesic ocean shader.

### Files changed

- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs` — authoritative geodesic sea-level offset/radius API, simulation-cell land/ocean/depth/coastline classification arrays, area-weighted diagnostics, welded smooth geodesic ocean visual, runtime ocean material cleanup, and debug mask handoff.
- `Assets/Scripts/Planet/Geodesic/Diagnostics/GeodesicCellPicker.cs` — selected-cell land/ocean/coastline/depth/sea-level diagnostics while preserving normalized terrain-hit-direction picking.
- `Assets/Scripts/Planet/Geodesic/Diagnostics/GeodesicGridDebugRenderer.cs` — bounded combined line mesh can colour ocean/coastline simulation-cell outlines without per-cell GameObjects.

### APIs introduced

```csharp
float GeodesicSeaLevelRadius { get; }
float GetWaterDepthAtDirection(Vector3 direction);
bool IsDirectionOcean(Vector3 direction);
bool IsGeodesicCellOcean(int cellIndex);
float GetGeodesicCellWaterDepth(int cellIndex);
byte GetGeodesicOceanNeighborCount(int cellIndex);
bool IsGeodesicCellCoastline(int cellIndex);
```

`geodesicSeaLevelOffset` is exposed in `PlanetGenerator` and uses `seaLevelRadius = PlanetGenerator.radius + geodesicSeaLevelOffset`. Terrain remains authoritative through `GetSurfaceRadiusAtDirection(direction)`.

### Ocean rendering architecture

Geodesic mode creates one `Geodesic Ocean` child with one welded indexed icosphere mesh at `GeodesicSeaLevelRadius`, using separate `geodesicOceanRenderSubdivisionLevel`. It is smooth and undisplaced, uses 32-bit indices through the shared geodesic mesh builder when required, recalculates bounds, and uses a runtime-owned material based on `SimulaVit/GeodesicOceanURP`. Legacy cube-sphere ocean generation remains on the legacy path.

### Picker collision/layer decision

The geodesic ocean visual deliberately has no collider. The picker continues to target the terrain `MeshCollider` on the planet object and derives the selected cell from the normalized terrain hit direction.

### Validation actually performed

- Static source inspection for forbidden legacy routing/folder reorganization.
- `git status --short` to verify the local change set.
- Unity Play Mode was not run in this environment; geodesic/legacy startup, material transparency, terrain picking around the ocean, no stale ocean after menu return, and visual seam checks still require local Unity validation.


### Shared ocean appearance Inspector and renderer wiring correction

`PlanetGenerator` is the authoritative visible Inspector owner for shared ocean appearance. It owns one serialized `OceanAppearanceSettings` instance drawn as the **Ocean Appearance** foldout in the normal Inspector; custom editor drawing must include the nested property children so `baseWaterColor`, `shallowWaterColor`, `deepWaterColor`, `opacity`, `smoothness`, `fresnelStrength`, and `fresnelPower` are visible without Debug Inspector mode.

Base water colour is distinct from oxygenated water colour. `baseWaterColor` is the renderer baseline consumed by both legacy cube-sphere and geodesic ocean runtime materials through `OceanMaterialBinder`; `oxygenatedWaterColor` is a future chemistry endpoint in the shared settings, blended only through `OceanAppearanceSample.oxygenation01`. Geodesic chemistry inputs are still defaulted, so normal geodesic generation binds `oxygenation01 = 0` and must not use hidden deprecated geodesic colour fields as runtime authority.

Legacy oxygenation has not been fully migrated into a shared per-cell ocean chemistry sample. The current compatibility path preserves old serialized `oxygenatedWaterColor` values by migrating them into `OceanAppearanceSettings.oxygenatedWaterColor`, while the shared base ocean material binding uses a default zero-oxygenation sample and legacy resource/chemistry visual paths remain legacy-owned until a dedicated migration.

Runtime ocean materials remain instance-owned. Legacy cube-sphere ocean material creation clones the configured material or creates a runtime fallback before binding shared appearance. Geodesic ocean material creation creates a runtime `Geodesic Ocean (Runtime)` material and binds the same `PlanetGenerator.oceanAppearance` with a default sample. No global material asset is modified.

### Known limitations

- Ocean is a simple transparent sphere only; it has no waves, physical shoreline cuts, chemistry, resources, layers, save arrays, or simulation stepping.
- Area-weighted diagnostics are logged during geodesic generation; numerical results should be captured from the Unity Console for each tested sea-level offset/subdivision combination.

### Next recommended phase

Proceed to Phase 3, a rendering-only geodesic atmosphere shell, after local Unity validation confirms Phase 2 startup, cleanup, picking, coastline classification around pentagons, and area-weighted land/ocean diagnostics.

### Original contract

### Goal

Add a simple geodesic ocean surface and authoritative land/ocean classification without resources or layered ocean chemistry.

### Sea level

```csharp
seaLevelRadius = PlanetGenerator.radius + seaLevelOffset;
```

Ocean surface is a smooth sphere at `seaLevelRadius`.

### Land/ocean classification

For each simulation cell:

```csharp
terrainRadius = GetCellSurfaceRadius(cellIndex);
isOcean = terrainRadius < seaLevelRadius;
waterDepth = Mathf.Max(0f, seaLevelRadius - terrainRadius);
```

Derived arrays:

- `bool[] geodesicOceanMask`;
- `float[] geodesicWaterDepth`;
- `byte[] geodesicOceanNeighborCount`;
- `bool[] geodesicCoastlineMask`.

A coastline cell has at least one real neighbor with the opposite classification.

### Ocean rendering

Create one coherent smooth ocean sphere with its own render subdivision and one runtime-owned material or dedicated material asset.

Do not create one GameObject per cell. Do not use the legacy cube-face ocean path.

### Selected-cell diagnostics

Add:

- land/ocean status;
- sea-level radius;
- terrain radius;
- water depth;
- ocean-neighbor count;
- coastline status.

### Out of scope

- ocean resources;
- ocean layers;
- vertical mixing;
- oxygen exchange;
- vents;
- temperature;
- sediments;
- replicators;
- geodesic ocean save arrays.

### Validation gate

- changing sea-level offset changes ocean coverage;
- classification is deterministic;
- mask is independent of render subdivision;
- ocean area is area weighted;
- coastline uses actual 5/6-neighbor topology;
- pentagons classify normally;
- terrain picking remains usable;
- legacy mode remains unchanged.

---

## Phase 3 — Atmosphere visual shell

### Goal

Add a rendering-only geodesic atmosphere shell.

### Requirements

- smooth sphere enclosing maximum terrain and ocean;
- thickness as a visual offset;
- runtime-owned material or dedicated asset;
- no atmosphere chemistry yet;
- correct cleanup and mode switching.

---

## Phase 4 — Geodesic environment-state container

### Goal

Introduce geodesic simulation arrays without migrating full chemistry.

Suggested component:

```csharp
GeodesicEnvironmentState
```

Possible initial contents:

- topology reference;
- cell areas;
- terrain radius;
- land/ocean mask;
- water depth;
- one scalar test field;
- temporary diffusion buffers.

Use a non-production scalar or temperature preview first.

### Validation gate

- conservative diffusion;
- no pentagon sinks or sources;
- stable area-weighted totals;
- simulation independent of render subdivision.

---

## Phase 5 — Temperature and ice

- compute insolation from cell and sun directions;
- store temperature per geodesic cell;
- diffuse with area and edge metrics;
- distinguish land and ocean heat capacity;
- render via vertex sampling or projected texture;
- add ice classification and visuals.

---

## Phase 6 — Vents and simple resources

- derive a stable vent-domain seed;
- choose geodesic vent cells;
- bias to ocean/seafloor suitability;
- place visuals using authoritative terrain radius;
- add minimal resources such as H2, H2S, CO2, and Fe2+;
- use area-aware inventories and source rates.

---

## Phase 7 — Layered geodesic ocean

Must migrate together:

- ocean mask and depth;
- active layer count;
- layer radii;
- top/bottom lookup;
- horizontal mixing;
- vertical mixing;
- layered resource arrays;
- surface and seafloor source targets.

Horizontal mixing must use actual neighbors, edge length, distance, and active-layer compatibility.

Vertical mixing occurs only between adjacent active layers in the same cell.

---

## Phase 8 — O2/Fe2 chemistry, precipitates, and sediments

Migrate as one group:

- dissolved Fe2+;
- local O2;
- same-layer oxidation;
- O2 consumption;
- FeOx production;
- suspended precipitate;
- settling;
- bottom sediment;
- surface and seafloor overlays.

Required per-layer diagnostics:

- O2 inventory;
- Fe2+ inventory;
- Fe2+ oxidized;
- O2 consumed;
- FeOx produced and settled;
- mass-balance error.

---

## Phase 9 — Replicator integration

Start with:

- spawn in a known cell;
- surface placement;
- movement across ordinary cells and a pentagon;
- ocean-to-land and land-to-ocean transitions;
- current/preferred ocean layer resolution;
- simple replication.

Then add metabolism, chemotaxis, scent, predation, mutation gates, and spawning systems.

Important rules:

- organisms keep continuous direction/position;
- land uses `currentLayer = -1`;
- ocean uses a valid active layer;
- habitat replication checks occur before mutation selection;
- mutation telemetry is aggregated rather than logged per attempt.

---

## Phase 10 — Full resource and ecosystem migration

Migrate remaining production resources, exchange, scents, marine snow, sulfur/iron loops, metabolism, and population telemetry.

Validation should focus on conservation and statistical behavior, not cell-for-cell equality with legacy runs.

---

## Phase 11 — Geodesic persistence

Save identity should include:

- schema version;
- grid type;
- master seed;
- generation version;
- base radius;
- simulation subdivision;
- topology version/hash;
- terrain and sea-level settings;
- resource and layered arrays;
- vent and organism state.

Rules:

- save metadata determines grid type on load;
- startup selection does not override save metadata;
- old saves remain legacy;
- topology mismatches are rejected or explicitly converted;
- cell indices are never silently reinterpreted.

---

## Phase 12 — Visual parity and legacy dependency cleanup

- migrate remaining overlays;
- improve ocean and seafloor rendering;
- improve atmosphere effects;
- optimize nearest-cell lookup;
- remove geodesic dependencies on legacy indexing;
- retain or archive legacy mode only after geodesic parity.

---

# 4. Immediate next Codex prompt

```text
Read Docs/GEODESIC_PLANET_IMPLEMENTATION_PLAN.md first.
Treat it as the current architecture and migration contract.

Implement Phase 2: geodesic sea-level visual and authoritative land/ocean classification.

This is an implementation task.
Work only on the current geodesic feature branch.
Do not merge to main.
Preserve legacy cube-sphere behavior.
Update the plan document when the implementation is complete.

Current geodesic state

The project already has:

- a welded icosphere render mesh;
- separate simulation and render subdivisions;
- geodesic topology with exactly 12 pentagons;
- deterministic terrain;
- authoritative direction-based terrain and surface-radius queries;
- cell outlines;
- terrain-aware picking;
- procedural vertex colours;
- startup grid selection;
- incomplete geodesic save/load intentionally disabled.

Goal

Add:

1. a smooth ocean surface at one authoritative sea-level radius;
2. deterministic geodesic land/ocean classification;
3. per-cell water depth;
4. coastline diagnostics.

Do not add ocean resources, ocean layers, chemistry, temperature, vents, replicators, sediments, marine snow, or geodesic save-state arrays.

1. Authoritative sea level

Use:

seaLevelRadius = PlanetGenerator.radius + geodesicSeaLevelOffset

Do not introduce another planet base radius.

Expose:

- geodesicSeaLevelOffset;
- GeodesicSeaLevelRadius;
- GetWaterDepthAtDirection;
- IsDirectionOcean;
- IsGeodesicCellOcean;
- GetGeodesicCellWaterDepth.

Terrain remains authoritative through GetSurfaceRadiusAtDirection(direction).

Water depth is:

max(0, seaLevelRadius - terrainSurfaceRadius)

2. Per-cell classification

After geodesic terrain generation, derive arrays for the simulation topology:

- bool[] geodesicOceanMask;
- float[] geodesicWaterDepth;
- byte[] geodesicOceanNeighborCount;
- bool[] geodesicCoastlineMask.

A coastline cell has at least one real neighbor with the opposite land/ocean classification.
Use actual 5/6-neighbor topology and do not pad pentagons.

3. Area-weighted diagnostics

Calculate and log:

- land cell count;
- ocean cell count;
- coastline cell count;
- area-weighted land fraction;
- area-weighted ocean fraction;
- minimum/maximum/mean ocean depth;
- sea-level radius;
- terrain minimum/maximum radius.

Do not use raw cell count as the authoritative ocean-area percentage.

4. Ocean render mesh

Create one coherent smooth ocean sphere.

Use a configurable ocean render subdivision independent of the simulation subdivision.

Requirements:

- radius equals the authoritative sea-level radius;
- no terrain displacement;
- one indexed welded mesh;
- one runtime-owned ocean material instance or dedicated material asset;
- transparent/translucent URP-compatible rendering;
- no cube-face ocean meshes in geodesic mode;
- no one-GameObject-per-cell approach;
- correct bounds and cleanup.

5. Picking and layers

Preserve terrain/simulation-cell picking.
Do not let the transparent ocean collider block terrain picking accidentally.
Prefer either no ocean collider for this phase, or a separate excluded layer/mask.
Document the choice.

6. Debug and popup

Keep cell outlines following terrain.
Extend selected-cell diagnostics with:

- land/ocean;
- coastline status;
- terrain radius;
- sea-level radius;
- water depth;
- ocean-neighbor count;
- cell area.

An optional coastline-highlight toggle may use the existing combined debug renderer.

7. Startup and cleanup

In geodesic mode:

- create terrain first;
- derive ocean classification;
- create ocean visual;
- keep resources, vents, replicators, and stepping disabled.

When returning to the menu, regenerating, or switching to legacy mode:

- clear the geodesic ocean mesh;
- clear runtime ocean material state;
- clear derived ocean arrays;
- leave no stale child objects.

8. Resolution independence

Classification must use simulation-cell centre directions and the authoritative terrain sampler.
Changing render subdivision must not change simulation-cell classification.

9. Explicitly out of scope

Do not implement:

- active ocean layers;
- ocean resource arrays;
- atmosphere/ocean exchange;
- temperature or ice;
- vents;
- O2/Fe2 chemistry;
- precipitates or sediments;
- marine snow;
- replicators;
- geodesic save/load arrays;
- erosion, rivers, waves, or tides.

10. Validation

Test or report:

- multiple sea-level offsets;
- subdivisions 3, 4, and 5;
- exactly 12 pentagons remain;
- area-weighted land + ocean fraction approximately equals 1;
- coastline classification uses actual neighbors;
- ocean radius matches sea-level radius;
- terrain picking still works;
- no cube-face seams;
- no stale ocean after returning to the menu;
- legacy mode remains unchanged.

Do not claim Unity play-mode validation unless it was actually performed.

11. Plan update

Update Docs/GEODESIC_PLANET_IMPLEMENTATION_PLAN.md:

- mark completed items;
- document files added/changed;
- record architectural decisions;
- add discovered risks;
- identify the next recommended phase.
```

---

# 5. Branch and commit strategy

Keep changes stacked and reviewable:

```text
main
└── geodesic integration branch
    ├── runtime fixes
    ├── surface visuals
    ├── terrain
    ├── mountain sampling
    ├── project organization
    ├── sea-level and ocean classification
    ├── atmosphere visual
    ├── scalar diffusion
    └── later simulation phases
```

For each phase:

1. branch from the latest working geodesic branch;
2. implement one coherent feature;
3. run Unity compilation and play-mode tests locally;
4. commit;
5. create PR metadata against the preceding geodesic branch, not `main`;
6. merge into the geodesic integration branch only after validation;
7. do not merge geodesic integration into `main` until the chosen milestone is stable.

---

# 6. Required update discipline

Every future Codex prompt should begin with:

```text
Read Docs/GEODESIC_PLANET_IMPLEMENTATION_PLAN.md first.
Treat it as the current architecture and migration contract.
Update it only when implementation decisions or phase status actually change.
```

Every completed phase should record:

- status;
- files added and changed;
- APIs introduced;
- validation performed;
- known limitations;
- next recommended phase;
- commit hash when available.

Do not claim Unity compilation or play-mode validation unless it was actually run.

---

## Phase 2A — Shared ocean visual appearance architecture

### Status

Implemented as a focused rendering-architecture refactor. Geometry and renderer mapping remain grid-specific: legacy cube-sphere still builds the existing `Ocean Layer` mesh from cube-face-derived vertices, while geodesic mode still builds one smooth undisplaced `Geodesic Ocean` sphere at `GeodesicSeaLevelRadius` with its separate render subdivision and no collider.

### Audit summary

- Legacy authoritative visual defaults were the assigned `OceanMaterial` asset values: `_BaseColor` `(0.15966536, 0.30129746, 0.49056602, 0.58)` and `_Smoothness` `0.876`. Legacy transparency came from the material alpha/render state, while geometry came from the cube-sphere ocean mesh.
- The geodesic prototype duplicated ocean colour, shallow tint, opacity, and smoothness fields on `PlanetGenerator` and bound them directly to `SimulaVit/GeodesicOceanURP`.
- Shallow/deep geometry behaviour is grid-specific today: legacy depth and bathymetry data are derived from cube-sphere cells and `PlanetResourceMap`; geodesic depth is classification-only and currently samples no resource chemistry.
- Existing resource-driven visual paths remain legacy/resource-map-specific: dissolved Fe2+, suspended precipitate visuals, sulfur/FeOx bottom tint/sediment diagnostics, temperature estimates, and ice surface visuals are not migrated into geodesic mode in this phase.

### Architecture

- `PlanetGenerator.oceanAppearance` is the authoritative shared visual settings owner for both modes.
- `OceanAppearanceSettings` contains only shared visual controls: base water colour, shallow/deep tint, opacity, smoothness, Fresnel controls, ambient/intensity response, and placeholder visual coefficients for future dissolved Fe2, suspended FeOx, suspended sulfur, organic turbidity, temperature, and ice tinting.
- `OceanAppearanceSample` is the geometry-independent colour-model input. It has future-compatible fields for base depth fraction, dissolved Fe2, suspended FeOx, suspended sulfur, organic turbidity, temperature, and ice fraction.
- `OceanAppearanceModel.Evaluate` is a pure visual evaluation utility independent of cube-sphere indexing, geodesic indexing, meshes, textures, and `PlanetResourceMap` ownership.
- Iron visual semantics are explicitly separated:
  - dissolved Fe2 water tint;
  - suspended FeOx water turbidity;
  - deposited FeOx seafloor sediment, which remains outside this water-column appearance refactor and will be supplied by future layered resource migration.
- `OceanMaterialBinder` standardizes shared shader/material property names (`_BaseColor`, `_ShallowColor`, `_DeepColor`, `_Opacity`, `_Smoothness`, `_FresnelStrength`, `_FresnelPower`, `_Fe2Tint`, `_FeOxTint`, `_SulfurTint`, `_Turbidity`) and applies settings to runtime-owned material instances only.

### Shader strategy

Legacy and geodesic renderers remain separate. The legacy path keeps using a runtime clone of the assigned legacy ocean material so the material asset is not modified globally. The geodesic path keeps using `SimulaVit/GeodesicOceanURP`, updated to accept the standardized shared property names without adding cube/geodesic-specific assumptions.

### Current geodesic chemistry inputs

Geodesic chemistry visual inputs are defaulted to zero in this phase. No geodesic ocean resource arrays, reactions, Fe2 chemistry, vents, biology, sediments, temperature, layered-ocean data, or save-state arrays were added. Future layered resource migration should populate `OceanAppearanceSample` from grid-specific resource sampling, then feed the same shared colour model.

### Validation still required in Unity

- Legacy ocean retains its existing appearance with the runtime material clone.
- Geodesic ocean retains its expected transparent smooth-sphere appearance while using shared settings.
- Changing `PlanetGenerator.oceanAppearance.baseWaterColor` affects both modes.
- Changing `PlanetGenerator.oceanAppearance.opacity` affects both modes.
- No material asset is modified globally at runtime.
- No stale runtime ocean material or geodesic ocean object remains after returning to the main menu.
- Terrain picking remains unaffected by the collider-free geodesic ocean.

---

## 2.5 Geodesic bathymetry foundation — implemented on this branch

This phase is inserted after geodesic sea-level/ocean classification and before any future layered-ocean geometry. The legacy `BuildOceanBathymetry` path remains unchanged and continues to use cube-sphere cell indexing, six-slot legacy neighbors, graph-step shelf distance, shelf depth, continental slope, maximum ocean depth, basin noise, shoreline preservation, smoothing, visual deformation strength, and final terrain-radius storage in the legacy generated radius arrays.

### Architecture

- Geodesic terrain now separates raw procedural terrain radius from final seafloor radius. Land/ocean classification is performed once from raw radius versus `GeodesicSeaLevelRadius`; bathymetry only deepens cells already classified as ocean and must not convert additional land into ocean.
- The authoritative generated geodesic bathymetry arrays are simulation-topology state, not resource or save-state arrays: raw terrain radius, final seafloor radius, base water depth, final water depth, distance to shore, ocean mask, coastline mask, basin-noise contribution, and shelf/slope/deep region classification.
- Future layered-ocean active layer counts and volumes must use `GeodesicSeaLevelRadius`, final geodesic seafloor radius, final geodesic water depth, and geodesic cell area. Future replicator/resource code must not independently derive depth from raw terrain.

### Shore distance units and algorithm

- Geodesic shoreline distance is stored in planet-radius surface arc units.
- Shoreline ocean cells are ocean cells with at least one real land neighbor.
- Distance propagation uses weighted Dijkstra over `NeighborCounts`, `Neighbors6`, and `NeighborAngularDistances6`; pentagons use their five real neighbors and no fake sixth neighbor is added.

### Depth profile and deterministic basins

- The profile preserves raw coastline depth, ramps across an angular continental shelf toward `geodesicShelfDepth`, descends beyond the shelf with `geodesicContinentalSlopeExponent`, and approaches `geodesicMaximumOceanDepth` in offshore basins.
- Low-frequency basin modulation is deterministic direction-based 3D noise seeded through the stable Bathymetry seed domain derived from the master planet seed.
- `enableGeodesicBathymetry = false` leaves ocean depths at raw sea-level-minus-terrain values while preserving the raw ocean mask.

### Simulation/render separation and visuals

- The authoritative bathymetry field belongs to the geodesic simulation topology.
- Render vertices sample final seafloor radius through a deterministic nearest-cell plus real-neighbor weighted interpolation. Land render vertices return raw terrain radius so coastline coverage is preserved and neighboring land is not carved below sea level.
- The geodesic ocean shell writes normalized authoritative depth into vertex colour red and the geodesic ocean shader blends existing shared shallow/base/deep water colors spatially from that input. No ocean collider, resources, chemistry, waves, tides, sediments, temperature, biology, or save resource arrays were added.

### Files changed

- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs`
- `Assets/Scripts/Planet/Common/Generation/PlanetRuntimeDescriptor.cs`
- `Assets/Scripts/Planet/Geodesic/Diagnostics/GeodesicCellPicker.cs`
- `Assets/Shaders/Geodesic/GeodesicOceanURP.shader`
- `Docs/GEODESIC_PLANET_IMPLEMENTATION_PLAN.md`

### Validation performed in this environment

- Static repository inspection of legacy `BuildOceanBathymetry` confirmed the legacy cube-sphere implementation remains in place.
- Text-level checks confirmed no new resource arrays, ocean layers, chemistry, vents, biology, atmosphere simulation, waves, tides, sediments, or save-state resource arrays were added by this phase.
- Unity Play Mode validation was not run in this environment.

### Known limitations

- The direction sampler uses nearest simulation cell plus real neighbors rather than containing primal-triangle interpolation, so very high render subdivisions may still show subtle low-frequency topology influence offshore.
- Dijkstra currently uses a simple deterministic O(N²) implementation, acceptable for current supported geodesic subdivisions but replaceable with a binary heap if larger simulation grids are added.
- Visual screenshots and collider/picker runtime validation still require local Unity execution.

### Next recommended phase

Implement layered-ocean geometry/data contracts on top of the authoritative geodesic seafloor/water-depth arrays without adding independent depth derivation in resource or replicator systems.

---

## Runtime visual cleanup contract (mode-transition audit)

Geodesic runtime visuals are owned by geodesic mode only and must not survive into legacy cube-sphere generation. `PlanetGenerator.ClearGeodesicRuntimeVisuals(...)` is the centralized cleanup path for this ownership boundary. It explicitly clears the geodesic topology, terrain/classification/bathymetry arrays, picker topology/selection popup, the geodesic debug renderer, geodesic ocean mesh/renderer/object, and runtime-owned geodesic surface/ocean materials before legacy terrain generation starts.

`GeodesicGridDebugRenderer.ClearAndDisable()` is the debug-renderer lifecycle API. Cleanup must clear its runtime mesh, detach `MeshFilter.sharedMesh`, disable the `MeshRenderer`, clear ocean/coastline masks, clear the surface-radius sampling delegate, clear selected-cell state, clear cached vertex/index/color buffers, and deactivate the debug GameObject. Geodesic generation is responsible for reactivating the debug object and rebuilding it from a fresh topology.

Unity destroys objects at the end of the frame, so mode-switch cleanup must disable renderers, detach meshes, clear mesh data, and deactivate geodesic-only GameObjects before calling `Destroy`. Runtime geodesic materials are owned by geodesic mode and must be released on cleanup; the planet terrain renderer must be restored to the legacy runtime material before applying the legacy surface texture.

Mode-transition diagnostics should log once after cleanup and once after generation, listing child renderers under the planet with active/enabled state, mesh vertex count, material, shader, and whether each renderer is legacy, geodesic, or shared. Legacy completion should warn if a geodesic-only renderer remains active after cleanup corrections.

Validation sequences for this contract:

- Fresh Play → Legacy.
- Fresh Play → Geodesic.
- Geodesic → Main Menu → Legacy.
- Legacy → Main Menu → Geodesic.
- Geodesic → Main Menu → Geodesic.
- Legacy → Main Menu → Legacy.

For `Geodesic → Main Menu → Legacy`, compare against `Fresh Play → Legacy` with the same seed/settings. The terrain mesh, legacy texture/material, legacy ocean/atmosphere, and child-renderer inventory should match; no geodesic debug lines, selected-cell highlight/popup, coastline/ocean-cell highlighting, filled polygon overlays, or geodesic ocean shell should remain.

Surface texture cache audit: the legacy surface-texture key already includes the planet-generation key, seed/noise offset, large/medium/detail noise scales, rock palette colors, contrast, crack darkening, texture width/height/format, linear color-space flag, and `SurfaceTextureCacheFormatVersion`. This cache can explain broad legacy surface coloration if inputs or versions are wrong, but it does not create discrete geodesic-shaped polygon overlays because the legacy render path applies it as a single texture on the terrain material rather than as separate polygon renderers. No cache-version bump is required for the geodesic visual-cleanup fix.

## Legacy surface texture and ice-mask lifecycle contract

Legacy cube-sphere terrain appearance has two separate owners:

- `PlanetGenerator` owns legacy terrain geometry, UVs, the runtime planet material, and the generated `_BaseMap` surface rock texture.
- `PlanetTemperatureIceVisuals` owns the legacy mesh vertex-colour ice mask. The `SimulaVit/PlanetSurfaceIceVertexURP` shader reads vertex colour alpha as land-ice amount: alpha `0` means no ice and alpha `1` means full ice. The red channel is also written to the same value for CPU-side/debug inspection parity, but the shader surface blend and force-preview output both read alpha.

Mode switches must invalidate persistent ice-visual mesh bindings because the terrain mesh can be cleared, replaced, or rebound without restarting the `PlanetTemperatureIceVisuals` component. Entering geodesic mode suspends the legacy ice visual path so it cannot overwrite geodesic procedural vertex colours. Returning to legacy mode must rebind to the current `PlanetGenerator` terrain `MeshFilter.sharedMesh`, rebuild cached vertices/colour arrays when mesh instance ID or vertex count changes, and write either temperature-derived ice colours or the shader-neutral no-ice baseline before final visual integrity diagnostics.

Required legacy lifecycle order after geodesic cleanup:

1. restore the legacy runtime terrain material;
2. build or load legacy terrain geometry;
3. assign legacy UVs;
4. build or load and bind the runtime surface texture;
5. refresh/rebind `PlanetTemperatureIceVisuals` to write the vertex-colour ice mask;
6. initialize/reinitialize resources and temperatures through the existing startup lifecycle;
7. refresh the ice mask again when resources report ready;
8. run immediate and one-frame-later integrity checks for material, texture, UV count, colour count/range, and ice binding.

The legacy surface texture cache remains owned by `PlanetGenerator`; do not clear or version-bump it for mode-switch ice-mask fixes unless diagnostics prove `_BaseMap` is missing or bound to the wrong runtime texture.

---

## Performance refactor note — render-only geometry and collider split

This focused refactor keeps the authoritative geodesic simulation topology unchanged while removing simulation-only topology construction from the visual render path.

### Render-only geometry ownership

Terrain and ocean rendering now use an immutable render-only unit icosphere representation (`IcosphereRenderGeometry`) containing only welded unit-sphere vertex directions and indexed triangles. It deliberately does not include simulation-cell data such as neighbor arrays, dual polygons, cell areas, shared-edge lengths, neighbor angular distances, or validation state. Those remain owned by `GeodesicGridTopology` for the authoritative simulation grid.

`IcosphereRenderMeshBuilder.BuildUnitGeometry(subdivision)` preserves deterministic welded midpoint subdivision and indexed triangle order for the visible icosphere. Unity `Mesh` instances are still generated per terrain/ocean/collider use because terrain vertices are displaced, ocean vertices stay smooth at sea level, and collider vertices may use a different subdivision.

### Base-geometry caching

`IcosphereRenderGeometryCache` owns a process-local cache of immutable unit icosphere geometry keyed only by subdivision. The cache is safe to reuse for terrain rendering, ocean rendering, collider mesh generation, regeneration with another seed, and returning to the menu before starting another geodesic planet because it contains no terrain, bathymetry, sea-level, colour, seed, or resource data.

Cached arrays must not be mutated. Mesh construction copies unit vertices into new mutable Unity mesh vertex arrays and clones triangle indices before assignment. Cleanup is explicit through `IcosphereRenderGeometryCache.Clear()` and the `PlanetGenerator` context menu item **Clear Geodesic Render Geometry Cache**.

### Separate collider subdivision

Geodesic terrain now exposes an independent `geodesicColliderSubdivisionLevel` with default 6. The collider samples the same authoritative terrain/seafloor radius function as the render mesh but is used only as an interaction approximation. Geodesic picking still resolves the selected simulation cell from normalized hit direction against the authoritative simulation topology, so lowering collider subdivision does not change cell lookup semantics.

Do not assign a subdivision-8 terrain render mesh to the `MeshCollider` by default. If collider subdivision is intentionally raised to extreme visual resolutions, expect high MeshCollider cooking cost.

### Recommended defaults

- simulation subdivision: 5 for production-equivalent 10,242-cell geodesic simulation comparisons;
- terrain render subdivision: 7 as a guarded visual default when startup time matters;
- collider subdivision: 6;
- ocean render subdivision: 5 or 6 unless the ocean silhouette needs to match very high terrain render subdivisions.

Every additional icosphere subdivision approximately quadruples triangle count. Current diagnostics warn when render subdivision 8 is selected, when collider subdivision is unnecessarily extreme, and when estimated render/collider triangle counts exceed the configured diagnostic threshold.

### Performance diagnostics

Geodesic generation now logs one scoped stopwatch entry per generation for:

- simulation topology generation;
- render icosphere generation;
- terrain displacement;
- bathymetry sampling/interpolation;
- vertex-colour generation;
- normal recalculation;
- bounds recalculation;
- terrain mesh assignment/upload;
- MeshCollider assignment/cooking;
- ocean mesh generation;
- debug renderer generation.

The summary log also includes simulation/render/collider/ocean subdivision levels, vertex and triangle counts, approximate managed geometry/topology byte counts where practical, cache entry count, validation status, and total generation duration.

### Audit finding

Before this refactor, the terrain render path built `GeodesicGridTopology.Build(renderSubdivision)`, and the geodesic ocean path independently built `GeodesicGridTopology.Build(oceanSubdivision)`. That means render-only meshes paid for simulation-oriented data that rendering did not need: neighbor counts, 6-slot neighbor arrays, pentagon flags, dual polygon corners, spherical cell areas, neighbor angular distances, shared dual-edge angular lengths, and validation inputs. Terrain and ocean could also rebuild identical unit icospheres independently when their subdivisions matched.

### Measurements obtained

No Unity Play Mode timing was captured in this environment. The new diagnostics are designed to capture comparable timings for render subdivisions 5, 6, 7, and 8 with simulation subdivision 5, including topology build time, displacement time, colour time, normal time, collider time, and total time. Record actual Unity Console measurements here after local runs before making performance claims.

### Remaining potential optimizations

Potential future work, not completed by this refactor:

- Jobs/Burst terrain displacement and colour sampling;
- asynchronous or incremental generation to avoid main-thread stalls;
- chunked render meshes for culling/upload granularity;
- runtime LOD for distant planets;
- faster nearest-cell lookup acceleration for very high simulation subdivisions.

These are intentionally deferred until the lightweight render-only builder and collider separation are validated in Unity.

### Direction-to-surface sampling optimization note

A subsequent profiling pass found terrain displacement, collider vertex generation, ocean colour sampling, and debug line generation all cost about 0.283 ms per sampled direction. The common path was `GetSurfaceRadiusAtDirection(direction)` in geodesic mode, which evaluates raw terrain, checks ocean/bathymetry state, calls `DirectionToGeodesicCell(direction)`, and then interpolates seafloor radius from the nearest ocean simulation cell and its real 5/6 neighbors.

Before optimization, each uncached geodesic query performed an O(simulationCellCount) scan over every simulation-cell direction inside `DirectionToGeodesicCell`. With simulation subdivision 6, that meant up to 40,962 dot-product candidates per output vertex before the bounded neighbor interpolation. Debug rendering amplified the issue because shared dual-corner endpoints were sampled repeatedly while emitting line vertices.

The optimized render/collider/ocean generation path uses immutable `IcosphereDirectionMapping` data cached by `(simulationSubdivision, targetSubdivision, mappingVersion)`. Each target vertex stores its nearest authoritative simulation cell plus the bounded neighbor indices and precomputed angular weights needed to reproduce the existing seafloor interpolation. Runtime sampling for mapped geometry is therefore O(1) plus at most the real neighbor count of the mapped cell, rather than O(simulationCellCount). When target subdivision equals simulation subdivision, the mapping validates one-to-one unit direction identity before using direct cell-index correspondence.

For target subdivisions higher than the simulation subdivision, mappings are built by starting from the simulation subdivision unit icosphere and carrying compact candidate simulation-cell sets through deterministic midpoint subdivision. Each target direction resolves its nearest cell from that small carried candidate set, preserving deterministic tie order without scanning all simulation cells per vertex. Lower-than-simulation target subdivisions validate prefix identity against the authoritative simulation topology and use direct cell-index correspondence where possible; only unexpected geometry ordering falls back to a one-time brute-force mapping build, after which sampling remains bounded.

The mapping cache is topology-only and independent of terrain seed, terrain settings, sea level, bathymetry values, ocean masks, colours, and generated Unity meshes. Seed/settings-dependent displaced positions are regenerated each planet generation. Debug rendering now caches unique sampled debug directions for the current line mesh build so repeated shared dual-corner endpoints no longer repeat the full surface-radius query.

Development diagnostics now aggregate surface-radius query counts, direction-to-cell query counts, candidate cells inspected, terrain-noise evaluations, bathymetry interpolations, mapping cache hits, and mapping cache misses once per generation. Unity Play Mode before/after timings and maximum surface-radius deviation still need to be recorded locally; do not claim runtime speedups until those measurements exist.
