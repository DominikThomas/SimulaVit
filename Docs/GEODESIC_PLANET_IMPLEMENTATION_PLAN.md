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

---

## Geodesic generation performance pass 2 — algorithmic and duplicate-work cleanup

This pass preserves terrain appearance, seed semantics, ocean/coastline classification, bathymetry profile inputs, topology, picker behavior, legacy cube-sphere behavior, save formats, and simulation values. It does not complete or start a new simulation-functionality phase.

### Profiling boundary

`GenerateGeodesicPrototype` now keeps the full synchronous-generation stopwatch running through post-core terrain diagnostics, query diagnostics, validation/inventory logging, and debug mesh construction. The summary reports both:

- `coreGenerationDurationMs`: work through mesh/ocean/debug generation, picker setup, runtime descriptor population, and generation validation;
- `fullSynchronousGenerationDurationMs`: core generation plus the synchronous diagnostics/log preparation that runs before the generated planet is considered fully reported/usable.

The difference is expected to be the remaining synchronous diagnostics and reporting boundary. Expensive diagnostic sampling must not be moved after the total timer just to make generation appear faster.

### Bathymetry shoreline distances

`ComputeGeodesicShoreDistances` now uses a deterministic multi-source Dijkstra traversal instead of the previous complete scan for the next minimum-distance ocean cell. Ocean coastline cells are initialized at distance zero, only ocean cells are expanded, each edge uses `NeighborAngularDistances6 * BasePlanetRadius`, and disconnected oceans remain represented by cells that are never reached from a coastline source.

The normal path is now approximately `O((V + E) log V)` rather than the former `O(V²)` scan. A Unity-compatible internal binary min-heap is used so the code does not depend on newer .NET priority-queue APIs. Duplicate heap entries are allowed and stale entries are skipped deterministically.

For temporary development validation, editor/development builds can enable shoreline-distance validation. That path runs the legacy scan against the same input distances and logs old/new execution time, maximum absolute deviation, mean deviation, and the count of ocean cells exceeding tolerance. This validation path is not the normal generation algorithm.

### Terrain-sample ownership

Simulation-cell terrain ownership is centralized in `RebuildGeodesicCellTerrainCache`. Each authoritative simulation-cell direction is evaluated once per generation, then the cached height is normalized directly from the authoritative min/max terrain offsets and converted to raw terrain radius. Ocean classification reuses those raw radii instead of evaluating the terrain sampler again.

Generation diagnostics now separate terrain evaluation counts into simulation-cell, render-vertex, and diagnostic-only categories so redundant sampling is visible in the console.

### Temporary render-generation data ownership

Terrain displacement can now produce a temporary per-generation render terrain data block containing raw terrain radius, final surface radius, height offset, normalized height, and mountain-mask values for the current render mesh. The data is regenerated with the mesh when seed/settings change, is not stored in immutable render geometry caches, and is not exposed as mutable shared cache state.

Vertex-colour generation consumes the normalized heights from this temporary data instead of re-running normalized terrain sampling for every render vertex. Terrain diagnostics also consume the generated height/radius/mountain-mask data by default, removing the extra full render-vertex diagnostic terrain-noise pass.

### Debug-outline lookup

Debug-outline generation still builds a single combined line mesh, but now keys sampled directions with stable quantized direction keys and reuses sampled positions and same-colour vertices for shared line endpoints. Debug rendering remains skipped and cleared when cell outlines are disabled. Normal generation with outlines disabled should remain effectively zero-cost for the debug renderer.

The remaining debug surface sampling uses bounded local nearest-cell knowledge from topology triangle corners instead of calling the brute-force `DirectionToGeodesicCell` full-cell scan.

### Topology-cache decision

`GeodesicGridTopology` is treated as immutable after `Build` completes: its arrays are populated during construction and consumed read-only by generation, picking, debug rendering, bathymetry, and validation. Terrain, ocean masks, bathymetry arrays, and generation diagnostics remain separately owned by `PlanetGenerator` and are never stored in the topology cache.

A subdivision-keyed `GeodesicTopologyCache` now reuses built simulation topology across menu/start cycles. It includes explicit diagnostics in generation logs and an explicit clear operation. Callers must continue to treat topology arrays as immutable; any future runtime system that needs mutable topology-derived state must allocate separate arrays.

### Cache diagnostics

Direction-mapping diagnostics now distinguish original cache-build work from work performed by the current request. On cache hits, `currentRequestCandidateCellsInspected` is zero; `originalCandidateCellsInspectedDuringBuild` remains available only as historical information about the cached mapping.

### Profiling results and remaining bottlenecks

No Unity Play Mode timings were captured in this non-Unity environment. The code now logs the stages needed to record cold, warm, outlines-disabled, outlines-enabled, menu/regeneration, and legacy/geodesic transition measurements locally, including shoreline distance time and terrain evaluation counts.

Remaining bottlenecks should be re-measured in Unity before making speedup claims. Expected remaining main-thread costs are terrain/noise evaluation for render and collider vertices, MeshCollider cooking, mesh normal recalculation/upload, and any enabled debug-outline mesh construction.

## 2.7 Geodesic ocean classification controls — implemented on `feature/geodesic-ocean-controls`

This pass clarifies ownership of ocean controls without adding ocean layers, chemistry, atmosphere, replicator, or save/load migration work.

### Ocean-control ownership audit

- `enableOcean` remains the shared, serialized ocean enable switch. Legacy cube-sphere generation uses it to enable the legacy ocean mask/mesh and geodesic generation uses it to gate geodesic ocean classification and the geodesic ocean renderer.
- `oceanCoveragePercent`, `oceanDepth`, `shelfDistance`, `shelfDepth`, `slopeStrength`, `maxOceanDepth`, `basinNoiseScale`, `basinNoiseStrength`, `basinNoiseOffset`, `bathymetrySmoothPasses`, `bathymetrySmoothStrength`, `shorelinePreservationDistance`, and `bathymetryVisualStrength` are legacy cube-sphere ocean and bathymetry controls. Their Inspector tooltips now identify them as legacy where ownership was ambiguous.
- `geodesicSeaLevelControlMode`, `geodesicSeaLevelOffset`, and `geodesicTargetOceanCoveragePercent` are the geodesic ocean classification controls. They determine the single resolved geodesic sea-level radius used by simulation classification and rendering.
- `enableGeodesicBathymetry`, `geodesicShelfWidthDegrees`, `geodesicShelfDepth`, `geodesicMaximumOceanDepth`, `geodesicContinentalSlopeExponent`, `geodesicBasinNoiseScale`, `geodesicBasinNoiseStrength`, `geodesicShorelinePreservationDegrees`, `geodesicBathymetrySmoothPasses`, `geodesicBathymetrySmoothStrength`, and `geodesicBathymetryStrength` are geodesic-only bathymetry controls. `geodesicMaximumOceanDepth` is the geodesic maximum ocean-depth authority.
- `OceanAppearanceSettings` is rendering/appearance ownership shared by legacy and geodesic ocean renderers; it does not classify land or ocean.
- `oceanCoverageRange` remains a legacy randomization input for `oceanCoveragePercent`. Its randomization clamp is now 0–100 rather than the old hidden 20–70 range.

### Control modes

`GeodesicSeaLevelControlMode.ManualOffset` is the default and preserves prior geodesic behavior:

```csharp
seaLevelRadius = PlanetGenerator.radius + geodesicSeaLevelOffset
isOcean = enableOcean && rawTerrainRadius < seaLevelRadius
```

`geodesicSeaLevelOffset` is a normal float Inspector field rather than a restrictive ranged slider. Zero means sea level is at `BasePlanetRadius`, positive values raise sea level and increase ocean coverage, negative values lower sea level and decrease ocean coverage, and sufficiently large values can intentionally produce no-ocean or all-ocean test planets.

`GeodesicSeaLevelControlMode.TargetAreaCoverage` resolves a sea-level radius from `geodesicTargetOceanCoveragePercent`. The target is approximate physical spherical area, not cell-index percentage. Generation evaluates raw terrain radius once for every simulation cell, sorts cells by raw radius, accumulates `UnitCellAreas * BasePlanetRadius^2`, selects the submerged cumulative area closest to the requested target, and then classifies all cells against one resolved radius. Target 0% explicitly resolves below all raw terrain and classifies every cell as land; target 100% resolves above all raw terrain and classifies every cell as ocean.

### Resolved sea-level ownership and cache rules

`resolvedGeodesicSeaLevelRadius` is runtime generation state owned by `PlanetGenerator`, not by immutable topology, direction-mapping, or render-geometry caches. It is regenerated with the geodesic terrain/classification/bathymetry arrays whenever geodesic generation runs. The same resolved value feeds ocean/land classification, coastline detection, base water depth, final bathymetry depth, geodesic terrain render interpolation, ocean visual depth colouring, collider sampling, picker diagnostics, and generation diagnostics.

Immutable topology and direction-mapping caches remain keyed only by subdivision/topological inputs and must not include sea-level settings. Mutable ocean classification, bathymetry, render displacement, and diagnostics regenerate when control mode, manual offset, target coverage, `enableOcean`, seed, terrain settings, or terrain subdivisions change.

Legacy cache correctness was preserved by including `PlanetGenerator.GenerationVersion` in the legacy planet cache key after legacy coverage endpoints were made explicit. This avoids loading a legacy cache generated under the previous hidden 20–70% threshold semantics.

### Diagnostics and validation status

Generation now logs `GeodesicSeaLevelDiagnostics` with the selected mode, manual offset, requested target coverage, resolved sea-level radius/offset, endpoint status where applicable, selected target cell count where applicable, and target-resolution time. `GeodesicBathymetryDiagnostics` now also logs selected mode, manual offset, requested target coverage, resolved sea-level radius/offset, ocean cell count, coastline ocean cell count, achieved cell-count ocean percentage, and achieved area-weighted ocean percentage.

Static/code-path validation in this non-Unity environment confirmed the following expected edge-case behavior by inspection:

- ManualOffset with a strongly negative offset resolves below terrain, yielding `oceanCells=0` and `coastlineOceanCells=0`; the shoreline-distance heap has no sources and exits without exception, and bathymetry skips all land cells.
- ManualOffset with a strongly positive offset resolves above terrain, yielding `oceanCells == total cell count` and `coastlineOceanCells=0`; the shoreline-distance heap has no coastline sources and exits without exception, and bathymetry treats missing shore distance as offshore depth input.
- TargetAreaCoverage 0% uses an explicit all-land endpoint; TargetAreaCoverage 100% uses an explicit all-ocean endpoint.
- TargetAreaCoverage 10%, 50%, and 90% are resolved by area accumulation, so achieved area-weighted coverage is limited by the area of the last selected simulation cell.

Unity Play Mode validation at simulation subdivisions 5 and 6 still needs to be captured locally for exact requested-versus-achieved coverage tables, picker confirmation, pentagon count confirmation, and before/after phase-2 performance measurements.

### Inspector mode UX follow-up

A follow-up corrected the Inspector affordance for geodesic sea-level modes. In `ManualOffset` mode, `geodesicSeaLevelOffset` remains enabled and `geodesicTargetOceanCoveragePercent` is disabled with an explanatory help box because target coverage is ignored. In `TargetAreaCoverage` mode, `geodesicTargetOceanCoveragePercent` remains enabled and `geodesicSeaLevelOffset` is disabled with an explanatory help box because the resolved offset is calculated automatically from the target area coverage.

The read-only runtime diagnostics now include both `resolvedGeodesicSeaLevelRadius` and `resolvedGeodesicSeaLevelOffset`, plus achieved cell-count coverage, achieved area-weighted coverage, ocean cell count, and coastline ocean cell count in both modes. Generation diagnostics warn when ManualOffset mode has an inactive target coverage value that differs from achieved coverage, and warn in TargetAreaCoverage mode that the manual offset field is inactive.

Unity Play Mode validation for ManualOffset 0.05 and TargetAreaCoverage 0/10/50/90/100 remains to be captured locally because this CLI environment still has no Unity runtime.

---

# Geodesic bathymetry relief branch notes

## Current bathymetry audit

The pre-existing geodesic ocean path generated simulation-cell terrain, resolved the geodesic sea level, classified land/ocean, flagged ocean coastline cells, ran the optimized Dijkstra shore-distance pass, smoothed that distance field, then applied one bathymetry profile before render/collider/ocean meshes sampled the authoritative simulation-cell seafloor through cached direction mappings. Because the profile used only `geodesicShelfWidthDegrees`, `geodesicShelfDepth`, distance to the nearest coastline, and one shared `geodesicContinentalSlopeExponent`, every land/ocean boundary received the same mean shelf and slope treatment. Small isolated islands therefore gained the same broad shallow ring as continental coastlines.

Legacy cube-sphere bathymetry remains separate and unchanged.

## Updated generation order

Geodesic generation now treats the authoritative pre-bathymetry solid radius as:

```text
preBathymetrySolidRadius =
    base continental/mountain terrain radius
    + oceanic ridge relief
    + oceanic plateau relief
    + seamount relief
```

The required order is now:

1. build/reuse the geodesic topology and base terrain cache;
2. generate deterministic oceanic ridge, plateau, and seamount relief per simulation cell;
3. combine those fields into `geodesicRawTerrainRadius` before sea-level resolution;
4. resolve ManualOffset or TargetAreaCoverage sea level;
5. classify ocean and land cells;
6. run connected land-component analysis;
7. classify coast ownership and continuous continental-shelf influence;
8. calculate shore distance with the existing optimized graph pass;
9. generate smooth shelf variation and final bathymetry;
10. sample final render, collider, picker, and ocean visuals through existing direction mappings.

TargetAreaCoverage therefore includes potentially emergent ridge/plateau/seamount relief before selecting sea level, so enabled oceanic islands do not silently change the achieved ocean percentage after coverage resolution.

## Continental versus oceanic margin ownership

Coast cells now carry a `GeodesicCoastType` diagnostic classification:

- `ContinentalMargin`;
- `ContinentalFragmentOrPlateau`;
- `OceanicIsland`;
- `MixedMargin`;
- `None`.

Bathymetry blends with a continuous `geodesicContinentalShelfInfluenceByCell` instead of a hard shelf/no-shelf switch. Continental margins use the existing shelf width, shelf-break depth, and slope exponent as mean/default values. Oceanic islands use separate narrow shelf width, shallower shelf depth, and steeper slope controls so small volcanic islands do not automatically get broad continental shelves. Continental fragments and plateau islands may still receive broader shallows when continent influence or plateau relief supports that interpretation.

## Connected land-component analysis

After ocean classification an O(V + E) breadth-first traversal assigns connected land-component IDs over the geodesic neighbor graph. The initial implementation exposes the component ID to diagnostics and picker data and combines local component adjacency, continent influence, and oceanic relief when deciding coastline type. Future refinement can store richer per-component summaries for continent-scale ownership thresholds without changing the traversal order.

## Open-ocean relief and submerged shallows

A deterministic bathymetry-domain relief layer generates broad ridged highs and plateau/bank fields directly on simulation cells using spherical direction noise, avoiding cube-face seams. These contributions are included in sea-level resolution when non-zero and are preserved as part of the raw solid surface rather than being erased by shore-distance shaping. Diagnostic bathymetry regions distinguish continental shelves, oceanic island margins, banks/plateaus, ridges, seamounts, and basins, so shallow water no longer implies nearby continental shelf.

## Seamount and island-chain architecture

The seamount layer is deterministic from the bathymetry seed and supports isolated peaks and short chains. Feature centers are generated on the sphere and sampled once into authoritative simulation-cell relief; render, collider, picker, and ocean visual paths continue using cached cell-direction mappings instead of feature-to-render-vertex searches. The data layout keeps separate ridge, plateau, seamount, and total oceanic-relief arrays so future hotspot or vent systems can query ridge influence, plateau influence, seamount feature influence, and chain ordering concepts. This branch intentionally does not implement tectonics, hydrothermal vents, chemistry, atmosphere, or replicator coupling.

## Performance and diagnostics

Generation diagnostics now report separate timings for oceanic relief generation, land-component analysis, coast-type classification, shelf-variation generation, and final bathymetry. The implementation preserves the phase-1/phase-2 optimized shore-distance and cached direction-mapping paths. Connected components remain O(V + E). The current lightweight seamount implementation bounds feature count from simulation-cell count and samples relief into cells only, but its per-feature cell pass is a known remaining limitation for very high density settings.

## Remaining limitations

- Connected-component summaries are currently diagnostic-oriented; richer persisted component records can be added later if simulation systems need them.
- Seamount chains are procedural approximations, not plate-tectonic hotspot tracks.
- Vent/hotspot coupling is deliberately deferred, though relief fields are structured for future use.
- Default new relief strengths are zero/disabled where necessary so existing default planets remain compatible within floating-point tolerance.

## Corrective note: shelf profile ownership and independent variation ranges

A follow-up audit found two issues in the first bathymetry-relief implementation:

- enabling oceanic-island margins blended every coastline toward the island profile through a generic continental-influence value, so broad continental shelves could disappear even when no coastline was truly classified as `OceanicIsland`;
- continental width variation was centered around a modest nonzero multiplier, so strength and scale changes could move the pattern but could not create coastline reaches with effectively absent shelves.

The corrected profile-selection rule is explicit:

- `ContinentalMargin` uses the continental shelf profile;
- `ContinentalFragmentOrPlateau` uses the broad continental/fragment profile;
- `OceanicIsland` uses the oceanic-island margin profile;
- `MixedMargin` receives only a conservative local island blend from `geodesicMixedMarginOceanicBlendStrength`, and that blend is further gated by actual oceanic relief;
- `None` does not receive an island profile.

The enable checkbox now only permits island-profile application where coast ownership already supports it; it no longer globally selects or suppresses shelf profiles.

Continental shelf width and shelf-break depth now have independent variation controls:

```text
localShelfWidth = geodesicShelfWidthDegrees * interpolatedWidthMultiplier
localShelfBreakDepth = geodesicShelfDepth * interpolatedDepthMultiplier
```

Width and depth multipliers are bounded by their configured min/max ranges. Strength 0 forces multiplier 1 everywhere; strength 1 uses the complete configured range. Width and depth use separate low-frequency spherical fields and separate scale controls, so changing scale changes patch size/geographic wavelength rather than amplitude. `geodesicShelfWidthDepthCorrelation` can optionally correlate or anticorrelate the two fields.

Diagnostics now report profile usage counts, min/mean/max applied shelf width by profile, applied shelf-break depth min/mean/max, configured multiplier ranges, variation scales, approximate local cell spacing, and coastline counts whose shelf width is below one cell or one-quarter cell. At simulation subdivision 6, deliberately small island widths such as 0.3 degrees may be below cell spacing and render as shelf-free steep margins; generation logs a warning instead of silently clamping them upward.
