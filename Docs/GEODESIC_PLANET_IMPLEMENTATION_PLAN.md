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

## Atmosphere v1 authority

The Geodesic visual atmosphere is a separate smooth icosphere shell built from the shared render-only `IcosphereRenderGeometryCache` and `IcosphereRenderMeshBuilder`. It uses subdivision 4 by default, is centered on the planet at `CurrentVisibleOuterRadius * atmosphereRadiusMultiplier` (default multiplier 1.04), and reuses the existing `Atmosphere_Fresnel_Mat` / `Shader Graphs/Atmosphere_Fresnel` asset unchanged. It has no collider or simulation authority and has no dependency on Legacy cube-sphere geometry generation.

The first authoritative Geodesic atmosphere is a dedicated global, well-mixed `GeodesicAtmosphereField`; it is independent of `PlanetResourceMap` and is not spatially resolved. Its gases are N2, CO2, O2, CH4, H2, and H2S. Authoritative state is gas inventory in the same simulation bookkeeping units used by dissolved inventory. Partial pressure is derived as `inventory / atmosphereInventoryPerBar`, where the configurable capacity is an explicit simulation conversion rather than an Earth-derived physical constant; total pressure is the sum of partial pressures.

Ocean startup CO2/O2/CH4/Fe2 remain dissolved concentrations and are configured independently from atmospheric starting partial pressures. Conservative defaults set every atmospheric partial pressure to zero, `atmosphereInventoryPerBar` to 100 inventory units/bar as an explicit simulation scaling convention, all equilibrium-concentration-per-bar coefficients to 1, and every exchange half-life to zero (disabled). The inventory scale is not a physically calibrated pressure-to-ocean-inventory conversion. No ocean value initializes atmosphere or vice versa.

`GeodesicAirSeaGasExchange` is invoked only by the completed authoritative resource interval and has no `Update`. For CO2, O2, CH4, H2, and H2S, `equilibriumSurfaceConcentration = partialPressureBar * equilibriumConcentrationPerBar`, `fraction = 1 - exp(-ln(2) * dt / exchangeHalfLife)`, and requested inventory is `(equilibriumSurfaceConcentration - L0 concentration) * fraction * actual L0 node volume`. N2 contributes to pressure but cannot exchange because no dissolved N2 channel exists. Non-positive half-life disables exchange.

Each gas-major pass evaluates all active surface L0 cells against one pre-exchange pressure. Atmosphere-to-ocean requests are proportionally limited by the pre-exchange inventory; simultaneous outgassing is netted in the deterministic batch but cannot fund or favor uptake cells. Only L0 is directly changed, and the atmosphere change is the exact negative of the committed ocean inventory change. No inventory or concentration may become negative.

The resource operator order is now: compact vent/source injection -> finite atmosphere/L0 exchange -> horizontal dissolved transport -> vertical dissolved transport -> staged dissolved-state application -> local abiotic chemistry -> chemistry-owned sediment deposition. Thus absorbed gas may transport and react in the same resource tick, while deep vent gas must transport to L0 before outgassing.

Atmosphere lifecycle state and exchange diagnostics are reset on teardown and reconstructed exclusively from new-world startup configuration. Read-only `TotalPressureBar`, `GetPartialPressureBar`, and `GetInventory` APIs form the future thermal boundary, including CO2 and CH4 partial pressures. **Atmosphere-to-temperature coupling, greenhouse warming, pressure thermal effects, chemistry, circulation, weather, water vapour, clouds, and escape are not implemented.**

Atmosphere v1 diagnostics expose the global pressure and partial pressures, inventory-per-bar scale, configured effective surface-L0 relaxation half-life, completed exchange ticks, and cumulative net transfer to the ocean for all five exchangeable gases in Detailed Debug only. The existing throttled chemistry telemetry appends the same O(1) global atmosphere state without adding a logger or ocean traversal. The air-sea half-life is explicitly presented as the relaxation timescale of surface-ocean L0 concentration toward atmosphere-controlled equilibrium, not as depletion half-life of the finite reservoir; zero remains exchange-disabled.

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
- geodesic startup path that skips legacy resources, vents, replicators, and biological stepping while retaining the shared authoritative world clock;
- explicit replicator-runtime lifecycle: current geodesic prototype startup leaves biology uninitialized, so `ReplicatorManager.Update` performs no biological simulation, rendering, resource scans, or telemetry work; cleanup clears this state, while an initialized legacy runtime remains initialized even at zero population;
- geodesic vertex-colour shader and runtime-owned material;
- deterministic procedural surface colours;
- deterministic direction-based terrain sampler;
- continent/basin shaping, domain-warped ridged mountains, and fine detail;
- separate simulation and render subdivisions;
- terrain presets;
- terrain-aware selection diagnostics;
- refreshed MeshCollider after displacement;
- outlines sampled against authoritative terrain radius.

The replicator lifecycle optimization changes no biological rule or cadence. During deferred startup, `ReplicatorManager.Start` prepares reusable references but does not initialize a biological world; final Legacy startup is the authoritative initialization owner, while current Geodesic startup leaves biology uninitialized on the first and every subsequent generation. `ReplicatorSimulationPipeline` remains the shared authoritative world clock even while the biology runtime is uninitialized; only its biological simulation, render synchronization, and telemetry phases are gated by biology initialization. This separation is required for repeated startup-menu generations because world temperature, sun/orbit motion, and speed control consume that clock. In initialized legacy simulations, metabolism counts still update each rendered simulation frame, while the allocation-heavy telemetry snapshot and its full legacy vent-chemistry scan are built only when the existing three-second log is actually due. Enum-sized mutation telemetry buffers are initialized/reused rather than rediscovered through allocating reflection on each snapshot.

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

---

# 18. OceanWorld sea-level control update

## 18.1 Current implementation status after bathymetry-relief and shelf-variation work

The geodesic migration now has the following completed or substantially implemented pieces, subject to normal Unity Play Mode validation on target hardware:

- geodesic topology, adjacency, pentagon handling, ordered dual corners, spherical cell areas, neighbor metrics, and logical cell picking;
- independent simulation, render, collider, and ocean subdivisions;
- topology, render-unit-geometry, and direction-mapping caches, with runtime sea-level and depth data kept out of immutable subdivision-only caches;
- generation profiling and performance-oriented cache reuse, including optimized shoreline-distance calculation for coastline-bearing oceans;
- deterministic geodesic terrain generation, displacement, render colouring, terrain diagnostics, collider refresh, and terrain-aware outlines;
- `ManualOffset` and `TargetAreaCoverage` geodesic sea-level controls, with `TargetAreaCoverage = 100%` retained as its percentage-based all-ocean endpoint rather than an OceanWorld alias;
- geodesic bathymetry arrays for raw solid radius, final seafloor radius, water depth, shore distance, coastline mask, ocean mask, basin noise, and bathymetry region;
- shelf width and shelf-break depth variation for coastline-bearing geodesic oceans;
- continental-margin versus oceanic-island-margin architecture, including coast ownership, shelf profiles, and margin diagnostics;
- ridge, plateau/bank, seamount, and island-chain relief architecture sampled into the solid geodesic terrain before sea-level classification;
- depth-driven geodesic ocean visuals based on final local water depth;
- legacy/geodesic menu transitions that preserve the legacy cube-sphere path while rebuilding geodesic runtime state when selected.

Chemistry-driven ocean or seafloor visuals are **not** complete. Existing depth-driven visuals are terrain/bathymetry visuals only.

## 18.2 OceanWorld design

`GeodesicSeaLevelControlMode.OceanWorld` is a dedicated global-ocean mode for geodesic planets. It is distinct from `TargetAreaCoverage = 100%`: target coverage remains a percentage resolver that chooses a sea level from simulation-cell area ordering, while OceanWorld guarantees a water shell above the highest solid-surface feature using an explicit minimum cover depth.

OceanWorld semantics:

- when `enableOcean` is true, every simulation cell is classified as ocean;
- land cells, coastline cells, and connected land components are all zero;
- no fake coastline source cells are created;
- shore distance uses the explicit no-shore sentinel and UI/diagnostics should present it as `N/A — OceanWorld`;
- coastline detection, shoreline-distance Dijkstra, shelf smoothing, continental-shelf shaping, oceanic-island-margin shaping, shelf-width variation, shelf-depth variation, and continental/oceanic margin classification are bypassed;
- raw solid-surface terrain variation remains intact, including base terrain, mountains, ridges, plateaus/banks, seamounts, and island-chain relief;
- the global water-surface radius is resolved from the highest solid radius plus `geodesicOceanWorldMinimumDepth` and a conservative fine-detail safety margin for render/collider peaks between simulation-cell centres;
- local water depth is `resolvedWaterSurfaceRadius - finalSeafloorRadius`, so basins and submerged peaks remain nonuniform and OceanWorld must never flatten every cell to one uniform depth;
- `geodesicMaximumOceanDepth` remains a generated bathymetric basin-lowering control where appropriate; it must not clamp OceanWorld total water-column depth or lift the seafloor to invalidate minimum cover.

The initial OceanWorld surface state is a globally liquid ocean. Future cryosphere work may add climate-driven partial sea ice, complete surface ice cover, or a global ice shell over a liquid ocean, but those states are separate from OceanWorld's water-distribution and depth controls. Future biology can remain compatible with vents, seafloor gradients, ice-water interfaces, cracks, or oxidant-delivery regions without adding placeholder gameplay fields in this phase.

OceanWorld runtime state is seed- and generation-dependent. Changing `geodesicOceanWorldMinimumDepth` must regenerate the resolved water-surface radius, ocean/depth arrays, render sampling, collider sampling, ocean visual data, and diagnostics. It must not invalidate immutable topology, render-unit-geometry, or direction-mapping caches unnecessarily.

Startup persistence audit: current startup config applies grid, seed, timing, and pause settings, but does not persist the existing geodesic ManualOffset/TargetAreaCoverage sea-level controls. OceanWorld therefore does not add a partial-only startup persistence path in this phase; geodesic config/save migration remains deferred.

Current limitations:

- OceanWorld is geodesic-only;
- no sea ice, ice shell, climate, layered ocean, chemistry, vents, replicators, biology, or save/load migration is implemented here;
- render/collider sub-cell peak coverage is protected by the current conservative terrain fine-detail margin and validated by diagnostics; a later implementation may resolve the water surface after temporary render/collider terrain sampling if stronger guarantees are required.

## 18.3 Revised dependency-aware roadmap

1. OceanWorld mode.
2. Isolated geodesic surface-temperature scalar.
3. Geodesic resource-grid foundation:
   - authoritative cell/layer indexing;
   - areas, neighbor metrics and conservative diffusion;
   - save-schema/versioning groundwork.
4. Geodesic layered ocean:
   - local active-layer counts;
   - horizontal and vertical mixing;
   - light and thermal profiles.
5. Atmosphere and area-weighted air-sea exchange.
6. Full temperature integration and cryosphere/sea ice.
7. Geodesic vents and geothermal sources.
8. Ocean chemistry and inorganic Fe/S precipitates and sediments.
9. Chemistry-driven ocean and seafloor visuals.
10. Replicator placement, layer resolution and movement.
11. Biology, metabolisms, mutations and ecological interactions.
12. Marine snow and biology-driven sediment.
13. Full geodesic save/load round trip:
    - environment;
    - layers;
    - atmosphere;
    - vents;
    - sediments;
    - replicators.
14. Optional hotspot, vent and island-chain coupling.

Save/load work has two stages: schema/versioning groundwork during resource-grid migration, then complete round-trip serialization after the major environment, layer, atmosphere, vent, sediment, and population structures stabilize.

## Authoritative immutable layered geodesic ocean domain

The scene-owned `GeodesicOceanLayerDomain` on the Planet Generator now owns the compact configuration, cached O(1) diagnostics, manual sample output, explicit validator, and one nonserialized immutable `GeodesicOceanLayerGrid`. `PlanetGenerator` orchestrates it after simulation topology, the shared `GeodesicTransportGraph`, final ocean classification, resolved water-surface radius, and final bathymetry/seafloor radii exist. Cleanup precedes topology replacement and occurs on regeneration, mode transition, return to startup, and destruction. A missing scene component is an isolated layered-domain error and does not stop terrain or surface-temperature generation. No runtime `AddComponent` is used for this authority.

The default normalized depth boundaries are exactly `0.00, 0.12, 0.32, 0.58, 0.82, 1.00` for five layers. Reduced maximum layer counts use the applicable leading configured boundaries and `1.00` as the final boundary. Boundaries must be finite, strictly increasing, and within zero through one. They are multiplied by the maximum current generated local ocean depth to make globally aligned physical radial bands. Each ocean column intersects those global bands with its unmodified local seafloor; shallow columns consequently end in one partial band while sufficiently deep basins use every configured band. Land has no active nodes.

Node storage is fixed and contiguous (`nodeIndex = cellIndex * maximumLayerCount + layerIndex`), while active geometry and links are represented by parallel primitive arrays rather than objects or repeated graphs. Each active radial wedge uses the topology unit-cell solid angle:

```
volume = unitCellSolidAngle * (outerRadius^3 - innerRadius^3) / 3
```

Horizontal connectivity iterates each canonical unique `GeodesicTransportGraph` edge and each common active band exactly once, retains the source edge index and canonical endpoints, and records the shared radial overlap thickness. Vertical connectivity joins adjacent active bands once per column. Its interface area is `unitCellSolidAngle * interfaceRadius^2`, and it records positive layer-center distance. This is immutable physical topology/geometry, not transported state or transport coefficients.

The explicit context-menu validator checks source identity and dimensions, land/ocean activation, contiguous layers, surface/seafloor closure, thickness and independently calculated wedge-volume conservation, finite positive geometry, canonical source-backed horizontal links, duplicate prevention, adjacent same-column vertical links, and expected vertical degree. Sample diagnostics refresh only after construction, a sample-index `OnValidate`, explicit refresh, or explicit validation; there is no update loop or Inspector-time array scan.

### Future temperature ownership contract

`GeodesicSurfaceTemperatureField` remains authoritative for land and ocean surface-layer temperature. A future `GeodesicOceanTemperatureField` will own subsurface layers without duplicating layer 0 as an independent second value. Future vertical heat transfer must exchange equal-and-opposite energy between adjacent layers, with heat capacity derived from `LayerVolume`. Direct solar forcing should be strongest at layer 0, weaker or optional at layer 1, and zero or near-zero deeper; later bottom vent heat belongs on the bottom active layer. Horizontal heat transport will use layered horizontal links and vertical heat transport will use vertical links.

The legacy cube-sphere `PlanetResourceMap` remains a behavioral reference only and is not called by geodesic mode. Its useful fast-surface/slow-depth behavior and reference defaults are recorded, not activated here: top solar factor `1`, second-layer solar factor `0.55`, deep direct solar factor `0`, initial temperature drop `2 K` per layer, bottom vent factor `1`, and above-bottom vent factor `0.45`. Layered temperature, passive tracers, dissolved chemistry, reactions, vents, currents/advection, plume behavior, sedimentation, marine snow, biology, atmosphere coupling, and layered save payloads remain deferred and are not marked complete.

## 18.4 Explicitly deferred features

Not implemented in the OceanWorld phase:

- full geodesic resource simulation;
- ocean layers;
- air-sea chemistry;
- sea ice or ice shells;
- vents;
- chemistry reactions;
- replicators;
- biology;
- marine snow;
- complete geodesic save/load;
- dynamic tectonics or hotspot evolution.

## 18.5 Acceptance criteria for the next phase

After OceanWorld lands, the next architectural task is the isolated geodesic surface-temperature field, not another bathymetry expansion, unless testing reveals a concrete bathymetry defect that blocks later temperature, resource-grid, or layered-ocean work.

---

# 19. Authoritative sun direction and geodesic main-light prerequisite

## 19.1 Confirmed pre-correction architecture and cause

`SunSkyRotator` on the PlanetScene `Directional Light` already owned the apparent solar orbit. At runtime it created a separate quad named `Sun Visual`, placed that quad relative to the planet, and billboarded the quad toward the camera in `LateUpdate`. The visual had no light component. The scene contained one enabled directional light, but `RenderSettings.sun` was unassigned in scene serialization. The legacy cube-sphere used its existing URP-lit runtime material and therefore responded to that scene light.

The static geodesic illumination had two confirmed causes. `GeodesicVertexColorURP` was a custom shader using a fixed material `_LightDirection` rather than URP main-light data, while `GeodesicOceanURP` applied only depth colour, ambient response, and Fresnel. Moving/billboarding the generated visual therefore could not change either shader's illumination. The visual transform's forward direction was also camera-dependent and was not a valid physical-light input.

Legacy `PlanetResourceMap` temperature/insolation code already used `max(0, dot(surfaceDirection, sunDirection))`. Its normal runtime fallback interpreted `-DirectionalLight.transform.forward` as planet-to-sun, while day-phase sampling from `SunSkyRotator` previously returned the opposite ray direction. The provider now makes that sign convention explicit and the legacy consumer prefers the authoritative property. This work does not add or migrate a geodesic temperature field.

## 19.2 Ownership and synchronization contract

`SunSkyRotator` is the single authoritative owner because it already owns orbit phase, seasonal declination, simulation-speed/pause coupling, sky rotation, and visible-sun placement. Future code must read `PlanetToSunDirectionWorld` for world-space insolation normals. `SunToPlanetDirectionWorld` is the opposite world-space direction in which parallel sunlight rays travel. `CurrentDirectionalLight` identifies the physical light, and `IsSunDirectionValid` guards use before initialization.

After orbit movement, `LateUpdate` derives planet-to-sun from `Sun Visual.position - planetCenter.position`; it never reads the billboard's forward vector. The controller rotates the separate directional light so its transform forward equals `SunToPlanetDirectionWorld`, using a stable alternate up vector near the world-up poles. Unity/URP's convention is verified by both the existing legacy calculation and URP's main-light API: a directional light emits along transform forward, while shader `Light.direction` points from the surface toward the light. Colour, intensity, and shadow configuration are untouched. The controller assigns its light to `RenderSettings.sun` when that slot is unassigned or already references the same light.

This synchronization changes only transform rotation and compact diagnostic state. It performs no mesh regeneration, vertex-colour recalculation, material instantiation, or repeated scene search per frame. Scene-wide light discovery occurs once during initialization solely to warn about competing enabled directional lights.

## 19.3 Terrain, ocean, and diagnostics

Both geodesic shaders now transform normals into world space and consume URP `GetMainLight`, including main-light colour, Lambertian N-dot-L diffuse response, attenuation, and available main-light shadow attenuation. Terrain vertex colour remains its albedo. Its default ambient strength is a low visual nightside floor and its diffuse strength defaults to one; ambient is not thermal energy and must never feed temperature. Ocean depth colour, transparency, opacity, Fresnel, runtime material ownership, and future chemistry property bindings remain intact while the same main light darkens its nightside.

Optional, non-per-frame diagnostics report both authoritative directions, physical-light forward, angular error, light names, `RenderSettings.sun`, terrain/ocean runtime material and shader names, and main-light support. The Inspector exposes read-only provider/light/direction/error/support state and warnings for a missing physical light or excessive mismatch. Initialization also warns when multiple enabled directional lights compete.

The spherical day/night boundary is normal-based and therefore works when the planet rotates in world space, when the apparent sun direction moves, and when both are stationary. Camera motion only changes the visual billboard. URP main-light shadow variants and existing light shadow settings are preserved, but detailed terrain self-shadowing is not an acceptance requirement and depends on URP shadow-map configuration and mesh casting. No atmospheric scattering is implemented; the existing atmosphere path is unchanged.

This visual and architectural correction is a prerequisite to the next major phase, **Isolated geodesic surface-temperature scalar**. That phase should compute `max(0, dot(surfaceNormalWorld, sunSkyRotator.PlanetToSunDirectionWorld))` and must not infer energy from shader ambient strength.

## 19.4 Visible-sun apparent horizon correction

The original sunset appearance did calculate a camera-relative angular separation, but it defined the planet limb with the serialized `SunSkyRotator.planetRadius`. That value was refreshed only from `PlanetGenerator.radius`, the base radius, and therefore ignored the current generated terrain maximum and visible ocean shell. A broad fixed angular transition was then also used for colour and brightness on both sides of that estimated limb. It happened to match the legacy camera composition, but a differently sized geodesic silhouette made the warm transition and centre-brightness change occur at the wrong apparent position. The billboard orientation was not used for this calculation, and normal depth testing already provided exact per-pixel clipping against opaque terrain.

`PlanetGenerator.CurrentVisibleOuterRadius` is now the grid-independent opaque/liquid silhouette API. For either legacy or geodesic generation it uses the current generated render-terrain maximum and, when ocean rendering is enabled, the greater of that terrain radius and the current water-shell radius. Thus ManualOffset and TargetAreaCoverage use their resolved sea level, OceanWorld uses its global ocean-shell radius, ocean-disabled planets use terrain alone, and the atmosphere shell is deliberately excluded. Mode selection resets the cached generated maximum so the previous mode cannot temporarily supply a stale horizon.

Every visual update uses degrees consistently. It computes camera-to-planet and camera-to-sun directions from positions, their angular separation, `asin(visibleWorldRadius / cameraPlanetDistance)` for apparent planet radius, and separation minus planet angular radius for signed sun-centre height above the limb. The bright disc's apparent angular radius is derived from the existing billboard scale, procedural texture `coreRadius`, and current camera-to-sun distance; no grid-specific or fixed apparent-size constant is required.

Sunset colour, central-disc visibility, and glow are separate factors. Colour warms over the configurable horizon-relative band while the centre is still above the limb. Central-disc visibility uses a smoothstep spanning one derived sun-core radius on either side of the limb, giving approximately one, one-half, and zero at one radius above, centred on, and one radius below the limb. Existing depth testing remains authoritative for the actual partial-disc shape rather than uniformly cutting the whole billboard. A separately configurable glow factor can fade the textured outer glow after the centre passes behind the limb, and the minimum sunset centre brightness prevents redness alone from erasing a still-visible core. These visual factors never modify the authoritative physical Directional Light direction, colour, or intensity.

Compact optional diagnostics report grid mode, camera distance, resolved visible radius, both apparent angular radii, signed limb height, and the three appearance factors once after visual initialization and whenever grid mode changes. Per-frame work is constant-size vector/scalar math plus existing material-property writes: it performs no scene search, allocation, material creation, texture rebuild, or mesh regeneration. Sunset redness remains an artistic visual approximation, not atmospheric scattering or radiative transfer.
# 20. Isolated geodesic surface-temperature scalar

## Scope and prerequisite

The authoritative-sun prerequisite is complete: `SunSkyRotator.PlanetToSunDirectionWorld` is shared by synchronized directional lighting and geodesic insolation. Camera direction, the billboard, shader ambient light, and independently reconstructed solar positions are not temperature inputs.

This phase adds exactly one Kelvin surface value per geodesic simulation cell, independent of render subdivision, with area-aware horizontal diffusion and land/ocean response categories. It adds no ice, ocean layers, vertical profile, atmosphere exchange, chemistry, currents, vents, biology, or full geodesic save/load.

## Ownership audit

Legacy cube-sphere temperature remains authoritative in `PlanetResourceMap`: its temperature arrays and lookup are cube-face/cell-indexed, and its legacy biology, resource simulation, layered estimates, and HUD consume them. Startup's existing **Base Temp Kelvin** and **Insolation Temp Gain** configure it. `PlanetTemperatureIceVisuals` is a legacy-only visual consumer, not a physical owner; it reads resource-map temperatures and retains its 273.15 K land and 269.15 K sea thresholds. It is cleared in geodesic mode.

Geodesic ownership is focused in `GeodesicSurfaceTemperatureField`. It owns `surfaceTemperatureKelvinByCell` plus preallocated target, working, energy-delta, and heat-capacity buffers. Its length always equals `PlanetGenerator.GeodesicTopology.CellCount`. It initializes after topology, terrain, and ocean classification and clears on legacy generation/cleanup. The existing startup temperature controls are reused rather than duplicated.

## Initialization, update, and diffusion

Initialization deterministically uses the instantaneous legacy-style additive target:

```text
insolation = max(0, dot(planet.TransformDirection(cellDirectionLocal), PlanetToSunDirectionWorld))
targetKelvin = baseTempKelvin + insolationTempGain * pow(insolation, insolationExponent)
```

This is an interim surface-energy approximation, not a radiative climate model. Updates accumulate authoritative `ReplicatorManager.FrameSimulationDeltaTime`; zero delta while paused advances nothing, and the accumulator is bounded. Heating/cooling use `1-exp(-dt/timescale)` with distinct timescales and local land/ocean capacity multipliers. OceanWorld consequently uses ocean response everywhere, ocean-disabled planets use land everywhere, and sea-level reclassification changes only the response category.

Diffusion processes each real edge once using `NeighborCounts`, `Neighbors6`, cell areas, angular center distances, and shared dual-edge lengths. Equal-and-opposite energy increments are accumulated before committing; temperature change divides by `UnitCellAreas[cell] * localHeatCapacityMultiplier`. This conserves effective area-weighted thermal energy for unequal cells and naturally supports five-neighbor pentagons. A cell-wise explicit stability limit selects substeps and warns/clamps pathological settings. Steady-state work is O(V + E), without LINQ or per-tick managed allocation.

The context-menu validation covers a uniform field, one hot cell (including real five/six-neighbor traversal), and before/after weighted-energy error. Subdivision 5/6 Play Mode behavior, allocations, tick timing, and generation impact still require measurement in Unity; static inspection does not justify performance claims.

## API, diagnostics, and persistence groundwork

The public API exposes initialization, the read-only temperature list, cell lookup, explicitly named local/world direction queries, area-weighted min/mean/max, last tick data, insolation, target, thermal category, and neighbor statistics. Direction queries seed from the base icosahedron and ascend actual neighbors rather than scanning all cells. World-direction queries accept a world-space normal vector; local-direction queries accept planet-local coordinates.

The picker displays Kelvin/Celsius, insolation, day/night, target, response multiplier, land/ocean category, and neighbor min/mean/max in its scrollable body. The HUD conditionally reads the geodesic owner and otherwise preserves its legacy resource-map source and unit toggle. Default production visuals remain unchanged; the serialized debug-visualization flag is off by default and does not recolour terrain.

Future serialization must include the temperature array, last simulation tick/time, parameter/schema version, subdivision/cell count, and initialization state. The save schema is unchanged in this phase.

## Limitations and next phase

This phase does not model atmospheric greenhouse feedback, ocean vertical heat storage, currents, latent heat, ice-albedo feedback, geothermal heat, or chemistry-driven climate.

After validation the next task remains **Geodesic resource-grid foundation**: authoritative cell/layer indexing, area and neighbor metrics, conservative resource diffusion, and save-schema/versioning groundwork. The broader migration order is unchanged.

## 20.1 Shared immutable geodesic transport graph

`GeodesicTransportGraph` is now the shared immutable horizontal transport topology derived from the authoritative `GeodesicGridTopology`. It stores exactly one canonical entry (`CellA < CellB`) for every undirected cell edge: endpoint indices, center-to-center angular distance, shared dual-edge angular length, their geometry-only conductance ratio, and the sum of that ratio incident on each cell. Transported values and system policy remain consumer-owned; the graph contains no temperatures, capacities, concentrations, masks, velocities, sources, timers, or work buffers.

`PlanetGenerator` owns the sole active runtime graph as a nonserialized reference. It constructs the graph immediately after topology validation, before terrain-classification consumers initialize, and clears it with geodesic runtime cleanup, legacy-mode transitions, destruction, and regeneration. Only constant-size graph diagnostics appear in the Inspector. The geodesic surface-temperature field is the first consumer and requires that exact owner-provided graph; it neither constructs nor caches a duplicate topology.

Temperature diffusion now traverses the compact unique-edge arrays once per substep. Thermal capacities and inverse capacities remain temperature-owned and are rebuilt on initialization or capacity/classification input changes. The explicit stability limit is cached from graph incident-conductance sums and invalidated only by capacity or diffusion-strength changes. Solar targets remain dynamic.

Intended future consumers include chemical-resource mixing, layered-ocean horizontal transport, atmospheric transport, and currents/wind after directional advection data exists. Shared geometry is distinct from every system's evolving state and coefficients. Vertical ocean links are deliberately not part of this horizontal graph; a later layered transport structure will own them. This infrastructure phase implements no chemistry, resources, ocean layers, atmosphere, currents, wind, vents, advection, buoyancy, settling, or reactions, and none of those phases is marked complete.

## Authoritative layered-ocean temperature (first vertical prototype)

Geodesic temperature ownership is explicit: `GeodesicSurfaceTemperatureField` owns every land surface and ocean layer 0, while the scene-owned `GeodesicOceanTemperatureField` owns persistent temperatures only for active ocean layers 1-4. Layer-0 queries are exact read-throughs to the surface field; no copied layer-0 temperature exists. The geodesic implementation has no dependency on the legacy `PlanetResourceMap`.

Initialization follows topology, shared transport graph, final ocean classification/bathymetry, ocean-layer domain, surface temperature, then ocean temperature. Surface reinitialization (including startup temperature configuration) notifies the ocean field, which discards its old arrays, revalidates grid identity and initializes subsurface state again. Cleanup reverses the dependency order: ocean temperature, surface temperature, ocean domain, transport graph/topology. Legacy mode leaves the ocean field cleared.

The surface tick commits sunlight forcing and the existing conservative horizontal **surface-only** diffusion first, then raises a synchronous event with the same simulation-time delta. The ocean field performs vertical exchange during that event, before surface diagnostics are finalized. It has no `Update` clock. Surface changes caused by coupling use the surface field's energy-delta API and authoritative heat capacity.

Active subsurface capacity is `LayerVolume * subsurfaceHeatCapacityPerVolume`; inactive and layer-0 placeholder slots have zero capacity. Startup is either locally isothermal from layer 0 or a depth gradient of `surfaceK - dropPerLayerK * layerIndex` (default 2 K), clamped only at 0 K. Every vertical link is visited once per explicit substep with conductance `verticalThermalDiffusivity * VerticalInterfaceArea / VerticalCenterDistance`. Endpoint energy deltas are accumulated simultaneously and applied equally and oppositely.

Stability uses cached vertical base-conductance sums and both authoritative surface and volume-derived subsurface capacities with a 0.45 safety factor. When the configured substep cap would be exceeded, effective diffusivity is reduced for that tick rather than executing an unstable capped step. Diagnostics report substeps, clamp state, timing, transferred energy, coupled-energy conservation error, surface-to-bottom contrast, fixed five-layer min/capacity-weighted-mean/max values, counts and memory. The context-menu validator checks ownership, identities, active state, capacities, links/conductances, finite values, uniform and warm-column behavior, and isolated-column conservation without changing live state.

The picker retains selection-time cached geometry and connectivity degrees. Its controlled 0.25-second dynamic refresh performs only direct selected-column temperature/capacity queries, showing concise layer temperature/depth/thickness lines in Compact mode and Celsius, capacity and authority in Detailed Debug mode.

Current limitations intentionally deferred: horizontal subsurface diffusion; currents/advection; thermohaline circulation; direct subsurface solar absorption; vent heating; atmosphere coupling; ice/ocean latent heat; chemistry; biology migration; and save/load. Existing horizontal surface diffusion remains a temporary unresolved surface-transport approximation.

### Layered-ocean temperature steady-state performance

Vertical transport now builds compact runtime-only participation tables at initialization: active subsurface node indices, surface cells with a vertical link, decoded link endpoints, and precomputed interface-area/center-distance conductance bases. Each substep clears and applies only those participating entries; it no longer clears fixed node/cell capacity buffers, scans inactive cell/layer slots, divides node indices, or recomputes geometry in the transport loop. The explicit 0.45 stability limit is cached against grid/capacity construction, configured diffusivity and substep cap, and the surface field's monotonic thermal-capacity version.

Profiler markers separate surface tick stages, the committed callback, ocean stability, sparse clears, flux accumulation, endpoint application, exact conservation, throttled diagnostics, and picker refresh. Exact coupled-energy auditing uses compact participation arrays and normally runs once per five simulation seconds (or every tick when profiling diagnostics are enabled); skipped ticks retain the latest exact result and report use of the algebraic equal-and-opposite invariant. Serialized Inspector snapshots and full five-layer diagnostics update at most once per unscaled second, while picker queries remain current and independently controlled. Runtime grid traversal was removed from ocean-domain `OnValidate`.

### Simulation-time-invariant thermal scheduling

Geodesic surface temperature advances against the authoritative double-precision `ReplicatorManager.SimulationTimeSeconds`, not a capped sum of rendered-frame deltas. A double thermal cursor processes every complete fixed interval and retains the fractional remainder; no supported speed discards temperature time. Each interval evaluates the pure `SunSkyRotator` ephemeris at its simulation-time midpoint, so multiple ticks in one rendered frame cross dawn, noon, and dusk using distinct solar directions. Each completed surface interval still commits exactly one ocean callback with the same fixed delta.

The sun ephemeris evaluates orbit angle, equatorial path, axial tilt, seasons, phase offset, reset epoch, and loaded orbit offset without mutating the visible transform. The visible sun derives its current angle from authoritative simulation time when speed-coupled, removing component `Update` ordering from thermal forcing. Tick-level diagnostics expose received/integrated/remainder/discarded time, cursor, tick counts, solar phase/advance, and completed daily min/mean/max independent of HUD sampling. The geodesic HUD already reads cached O(1) surface statistics every draw; only legacy resource-map statistics retain the unscaled-time throttle. A context-menu frame-partition validator compares small and large rendered-frame partitions using the identical fixed tick/midpoint sequence and temporary surface/layer state.

### Bounded simulation throughput and production thermal cadence

`ReplicatorSimulationPipeline` accepts at most 1/30 simulation second per configured step. At healthy 60 FPS the raw frame delta is accepted unchanged; under overload the authoritative clock advances by only the bounded work accepted, with no wall-clock accumulator or later catch-up. Requested 10×–100× speeds are target throughput: an overloaded simulation slows coherently instead of letting a slow frame create a larger next-frame thermal workload.

The approximate production thermal interval is two simulation seconds; `ConservativeImplicit` retains its serialized 0.25-s interval. Initialization latches the active interval from the already-latched active model, and the interval remains fixed in authoritative simulation time across playback speeds. The absolute cursor, interval-midpoint ephemeris, deterministic fractional remainder, zero-discard contract, and one ocean callback per completed surface interval remain unchanged. A 64-tick emergency guard retains explicit backlog rather than discarding it.

The isolated **Validate Approximate Thermal Cadence Sensitivity** comparison uses the previous 0.25-s cadence as its reference, runs 12 warm/cold, dawn/dusk, latitude/season, shallow/partial/deep-ocean, and vent/non-vent cases for 960 simulated seconds, and samples every candidate at identical simulation-time boundaries. The comparison produced:

| Cadence (s) | Max surface K | Mean surface K | RMS surface K | Max subsurface K | Mean subsurface K | RMS subsurface K |
|---:|---:|---:|---:|---:|---:|---:|
| 0.50 | 0.000796 | 0.000081 | 0.000130 | 0.021211 | 0.002867 | 0.005013 |
| 1.00 | 0.003216 | 0.000374 | 0.000605 | 0.063364 | 0.008575 | 0.014988 |
| 2.00 | 0.009039 | 0.001532 | 0.002454 | 0.146588 | 0.019906 | 0.034767 |
| 5.00 | 0.051642 | 0.009744 | 0.015245 | 0.387830 | 0.053098 | 0.092525 |

Two seconds is selected for production, reducing approximate thermal ticks from 480 at the former 1-s cadence to 240 per 480-s day. The previous deterministic sensitivity result for 2 s was 0.009039 K maximum surface difference and 0.146588 K maximum transient subsurface difference versus the 0.25-s reference (surface mean/RMS 0.001532/0.002454 K; subsurface mean/RMS 0.019906/0.034767 K). Recalculation against the current implementation produced the same values. The roughly 0.15 K worst transient subsurface difference is negligible for the intended ecological simulation. The former 0.1-K limit was a deliberately conservative screening threshold, not an ecological or biologically derived requirement; the validator's ecological acceptance bounds apply specifically to the selected 2-s candidate and do not generally loosen sensitivity validation or justify moving to 5 s. The cadence change is expected to halve thermal topology/profile solves per equivalent simulated duration. These are deterministic validator measurements, not Unity Profiler or full subdivision-6 performance measurements; no post-change FPS, achieved-speed, frame-time, or marker measurement is claimed. Cadence never branches on rendered FPS, wall-clock time, or requested playback-speed multiplier.

Per tick, the world sun direction is transformed to planet-local space once, normalized topology directions are dotted directly, and exponent 1/2 avoid `Pow`. Full surface min/mean/max diagnostics are sampled at most every 0.25 unscaled second after the frame's thermal ticks; completed-day values are therefore sampled extrema. Exact surface and vertical diffusion-energy scans run once per five simulation seconds or every tick only when profiling is enabled, while ordinary ticks rely on the algebraic equal-and-opposite edge invariant and retain the latest exact result. The stable diffusion limit is cached by strength, thermal-capacity version, and graph identity.

The recurring environment-traversal audit retained one local same-domain fusion: each thermal tick calculates a cell's solar/geothermal target and immediately applies that cell's surface response in one all-cell pass. Target calculation has no cross-cell dependency, resource state cannot change during the synchronous loop, and every target is still stored before the ocean callback, so operator order and the approximately two-simulated-second cadence are unchanged. Initialization retains its target-only pass. This removes one complete all-planet-cell traversal per steady-state thermal tick; it is a static traversal-count reduction, not a claim of runtime speedup without Unity profiling.

The attempted resource-diagnostic fusion was selectively reverted after Unity validation showed requested-100x throughput falling from approximately 89–92x to approximately 55x, with about 30 ms of unexplained `GeodesicOceanResourceField.Update` self time outside the transport marker. The fused node-major loop retained a seven-resource inner loop over channel-major concentration storage, replacing seven sequential per-channel scans with strided cross-channel reads on every diagnostic refresh. The production five-second resource cadence makes that refresh run after every resource tick, including every catch-up iteration. Resource summaries therefore again use the previous resource-major active-node scans and O2 layer means use the previous layer-major cell scans. A coarse `GeodesicOceanResource.RecurringDiagnostics` marker now encloses both recurring calls so a focused Unity capture can verify their cost separately from `Update` self time. The marker changes no cadence or state. Post-revert recovery toward the earlier throughput still requires Unity validation and is not yet claimed.

### Final startup lifecycle

New Game applies sun day/year timing and assigns geodesic base/gain parameters before authoritative planet generation. Parameter assignment is allocation-free and does not rebuild a field. Generation then creates topology, transport graph, ocean domain, surface temperature, and ocean temperature exactly once in dependency order. Because cleanup unsubscribes the old ocean field before surface initialization, the surface reinitialization event has no ocean subscriber during generation; the generator's explicit ocean initialization is therefore the sole startup ocean build. Genuine runtime parameter replacement uses the explicit rebuilding API and its reinitialization event.

The cadence-sensitivity context command is intentionally an isolated representative-state comparison. It is useful for deterministic screening but is not evidence that a full subdivision-6 surface and layered ocean remain within 0.1 K; that full-field Unity comparison and performance profiling remain required when the Editor is available.

---

## Phase 6 — Geodesic layered-ocean resource-state foundation

### Status

Implemented on the current geodesic resource-state child branch as an ownership, storage, initialization, query, lifecycle, diagnostics, and validation foundation only. This phase deliberately does not add resource transport, chemistry, vents, atmosphere exchange, biology, sedimentation, visuals, or geodesic save/load payloads.

### Resource ownership

`GeodesicOceanResourceField` is the scene-owned authority for dissolved geodesic ocean resources. It is serialized on the same Planet Generator object as `GeodesicOceanLayerDomain`, `GeodesicSurfaceTemperatureField`, and `GeodesicOceanTemperatureField`. Legacy `PlanetResourceMap` remains unchanged and authoritative only for legacy cube-sphere mode.

### Channel contract

The stable geodesic dissolved-ocean channels are:

- `CO2`;
- `O2`;
- `CH4`;
- `H2`;
- `H2S`;
- `Fe2`;
- `OrganicC`.

Reaction products, sediments, and biological stores remain deferred.

### Concentration and inventory semantics

Geodesic resources are stored as concentration per active ocean-layer volume. For any active node:

```text
inventory = concentration * GeodesicOceanLayerGrid.LayerVolume[node]
```

APIs keep concentration and inventory explicit: direct concentration reads/writes/adds, inventory adds, bounded inventory withdrawal, node inventory, global inventory, and volume-weighted mean concentration. Invalid cells, invalid layers, inactive nodes, non-finite writes, and negative writes are rejected safely. Global reductions and validator checks use double precision.

This differs from legacy `PlanetResourceMap` startup/runtime labels, which may mix atmosphere-style startup values, legacy normalized totals, and legacy per-cell resource arrays. Geodesic initialization logs now identify values as dissolved-ocean concentrations, not atmospheric values or legacy normalized totals.

### Storage layout and memory

`GeodesicOceanResourceField` uses one channel-major contiguous `float[]` indexed by:

```text
resourceOffset = (int)resource * nodeCapacity
nodeIndex = GeodesicOceanLayerGrid.GetNodeIndex(cellIndex, layerIndex)
storageIndex = resourceOffset + nodeIndex
```

Fixed node indexing, active-layer counts, and layer volumes come only from `GeodesicOceanLayerGrid`. Inactive slots remain zero and are not active ocean state. Full scans that are required for diagnostics traverse a compact active-node index list and cached active-node volume list; ordinary direct queries perform no allocation and use no dictionaries.

At subdivision 5 (10,242 cells, five possible layers, seven channels), the channel-major concentration buffer is approximately 1.37 MiB, plus compact active-node indices/volumes and diagnostics. For the known subdivision-5 configuration with `activeNodes=26452`, the total reported resource-field runtime memory is expected to be about 1.57 MiB.

### Initialization order and lifecycle

Geodesic startup order is now:

1. topology;
2. transport graph;
3. final bathymetry/classification;
4. `GeodesicOceanLayerDomain`;
5. `GeodesicSurfaceTemperatureField`;
6. `GeodesicOceanTemperatureField`;
7. `GeodesicOceanResourceField`.

Cleanup reverses ownership by clearing resources before ocean temperature, surface temperature, the ocean-layer domain, and then transport/topology. Legacy startup leaves the geodesic resource field cleared. Regeneration and mode changes replace old arrays and do not subscribe the resource field to temperature ticks because resources do not evolve in this phase.

### Initialization values and diagnostics

Existing startup values with direct dissolved-ocean equivalents are applied uniformly across active ocean volume:

- initial CO2 -> `CO2` concentration;
- initial O2 -> `O2` concentration;
- initial CH4 -> `CH4` concentration;
- initial dissolved Fe2 -> `Fe2` concentration.

`H2`, `H2S`, and `OrganicC` initialize to zero. Land and inactive slots are not initialized. A single initialization log reports all applied geodesic dissolved-ocean concentrations with active-node, volume, and memory diagnostics.

Inspector-visible diagnostics include initialization state, cell count, node capacity, active-node count, active ocean volume, approximate memory, initialization count, clear count, invalid query count, rejected non-finite write count, rejected negative write count, and per-channel cached min/mean/max/global inventory.

### Picker integration

`GeodesicCellPicker` keeps its existing static/dynamic temperature split. Ocean cells show compact per-layer resource concentrations for `O2`, `CO2`, `H2`, `H2S`, and `Fe2`; detailed mode also includes `CH4` and `OrganicC` next to existing layer geometry. Land cells and cells with no active ocean layers report that no active ocean resource layers exist. The picker reads selected nodes directly and does not perform global field scans.

### Validation performed

`GeodesicOceanResourceField` exposes a context-menu command named **Validate Geodesic Ocean Resource Field**. It verifies source-grid identity, storage dimensions, finite/non-negative active values, zero inactive slots, land-cell inactivity, active-node count, independent double-precision global inventories, volume-weighted means, invalid-query non-mutation/counter behavior, and deterministic sentinel coverage for one-layer shallow ocean, partial-layer ocean, five-layer deep ocean, and land cells.

Static checks run in this environment are recorded in the PR/final report. Unity Play Mode and the context-menu validator still require local Unity execution when an editor is available.

### Explicitly deferred features

Deferred: passive/horizontal/vertical transport, mixing, advection, chemistry/reactions, H2/O2 reactions, H2S/O2 reactions, Fe2 oxidation, reaction products, hydrothermal vents, heat sources, atmosphere exchange, photosynthesis, methanogenesis, biology, replicator sampling, sedimentation, marine snow, resource-based ocean colour, save/load payloads, temperature cadence/solver changes, and legacy resource-physics changes.

### Next recommended phase

Proceed to passive conservative layered transport for the geodesic dissolved-ocean resource field. Do not proceed directly to chemistry or biology until transport conservation and sparse active-node traversal have been validated.

### Conservative dissolved-resource transport and vent sources

The passive environmental resource-dynamics layer is now implemented. `GeodesicOceanResourceField` advances on an independent fixed **5.0 simulated-second** cadence read from the shared authoritative world clock. The scheduler retains remainder and guarded backlog, resets on regeneration/mode changes, does not advance while paused, and never changes its physical model with playback multiplier or rendered FPS.

Each of the seven channel-major concentration fields is transported with an allocation-free staged inventory pass. Horizontal links reuse `GeodesicOceanLayerGrid`'s same-layer links and the geodesic shared-edge/distance geometry; vertical links reuse its adjacent-layer interface-area/center-distance geometry. Initialization caches geometry-weighted conductances normalized so the incident conductance sum cannot exceed node volume. A tick reads only the old concentration state, accumulates equal-and-opposite inventory deltas into one reusable double-precision node buffer, and then commits concentrations. The combined configured horizontal and vertical rates are stability-scaled when necessary so a node cannot export more inventory than it owned at tick start. Transport conserves inventory (`concentration * layerVolume`), rather than raw summed concentration.

Defaults are a horizontal mixing rate of `0.02 s^-1` and vertical mixing rate of `0.005 s^-1`. Compact channel multipliers preserve a small tuning surface; the O2 vertical multiplier defaults to **0.1**, making its effective vertical rate `0.0005 s^-1` so surface oxygen spreads horizontally well before it penetrates middle and bottom water. There is no oxygen clamp or destruction.

No independent geodesic logical vent dataset existed before this phase: `VentVisualizer` and `PlanetResourceMap` remained legacy-owned, while the thermal fallback explicitly was not a resource source. The resource field therefore builds a generation-stable compact logical vent map at initialization (default two percent of multi-layer ocean columns). It caches only each vent's true deepest active node and strength. After transport commits, each fixed resource tick injects configured H2, H2S, CO2, and Fe2 inventory at those bottom nodes. The historically named `Per Tick` startup values retain their original one-second-tick meaning and are treated internally as inventory rates per simulated second; a five-second resource tick injects five times the configured value, so integrated source inventory is cadence-independent. The default Fe2 source is `0.002` per simulated second per unit vent strength. Invalid/non-ocean and one-layer columns cannot enter the cached source map. Legacy vent behavior is unchanged.

Production state remains concentration-authoritative and exposes direct local concentration, inventory-add, and bounded inventory-withdraw APIs for the later reaction pass. Transport and vent injection are intentionally separate from chemistry: there is still no oxidation, methanogenesis, organic decay, atmosphere exchange, biological uptake, replicator biology, or other resource sink. Context validators cover pairwise conservation, vent accounting/deepest-layer mapping, five-layer O2 propagation, and fixed-cadence frame partitioning. Cached volume-weighted mean O2 by layer is refreshed on a throttled resource diagnostic cadence or explicit request. Profiler markers cover total transport, horizontal mixing, vertical mixing, and vent sources.

Persistent memory added by transport is seven resource-major double staging values per node-capacity slot (56 bytes/node), one float per horizontal and vertical link, fourteen tick-coefficient floats, and one `(int node, float strength)` pair per vent. The larger staging buffer lets production traverse each horizontal link once and each vertical link once per resource tick, with the seven channel operations unrolled inside each link visit; the former implementation repeated both topology traversals seven times. Per-resource accumulation order, staged old-state semantics, commit math, vent ordering, fixed cadence, and conservation behavior are unchanged. Initialization-only geometry normalization scratch arrays are released before production stepping. Unity Play Mode validation and profiler measurements remain required before claiming optimized runtime timings or managed-allocation measurements.

Horizontal transport now also maintains one conservative spatial-variation bit per dissolved channel. Initialization and regeneration clear all seven bits because every active node receives the same configured concentration. Every supported local concentration/inventory write, chemistry inventory write, and nonzero vent source marks its affected channel before horizontal transport selection; bits are deliberately never cleared during an initialized world because proving that a previously varying field has returned to exact global uniformity would require the full scan this optimization avoids. Thus false-positive work is possible but an incorrect uniform-channel skip is not. Vent marking occurs before mixing in the same resource tick. Vertical transport cannot create a difference from an exactly globally uniform field, while any local state capable of producing a later vertical difference has already conservatively marked the channel.

The horizontal audit is: exactly `GeodesicOceanLayerGrid.HorizontalLinkCount` canonical links are visited per active horizontal channel group and zero links are visited when no channel is active; all active channels remain unrolled inside one link traversal rather than traversing topology seven times; geometry-normalized link conductances are initialization-cached; resource storage and staging remain channel-major, so successive operations for different channels jump by `nodeCapacity`; and the full seven-channel staging buffer is still cleared because the unchanged vertical pass and fused staged-apply/chemistry-candidate pass use it. An exactly zero transfer now returns before either staging write. The existing `GeodesicOceanResource.HorizontalMixing` marker remains, supplemented by counters for active channels, skipped uniform channels, and link-resource evaluations (`HorizontalLinkCount * active channels`). Dense seven-channel worlds therefore retain the prior authoritative calculation and link/resource operator order, apart from the exact-zero write guard and small mask/branch bookkeeping.

Unity validation remains required and no runtime speedup is claimed from static tests. Capture (1) a uniform world with no local sources, expecting 0 active / 7 skipped channels and zero link-resource evaluations; (2) a sparse-source world where only vent-fed H2, H2S, and Fe2 vary, expecting 3 active / 4 skipped channels and `3 * HorizontalLinkCount` evaluations; and (3) a dense benchmark with all seven fields locally varied, expecting 7 active / 0 skipped channels and `7 * HorizontalLinkCount` evaluations. Record the existing horizontal marker time and GC allocation for each scenario, compare the uniform case with the supplied roughly 14 ms baseline, and use the dense case to quantify bookkeeping overhead rather than assuming an improvement.

#### Resource-cadence sensitivity

An isolated deterministic comparison tested 2, 5, and 10 simulated-second ticks against the original 1-second reference at 60, 300, 600, and 1200 seconds. Cases included unequal-volume CO2/H2/H2S horizontal gradients, five-layer surface-first O2, five-layer vent columns, and partial three-layer vent columns. Errors aggregated across all checkpoints were:

| cadence | horizontal max / mean / RMS | depth max / mean / RMS | vent max / mean / RMS |
|---|---|---|---|
| 2 s | 0.013446 / 0.001529 / 0.002527 | 0.0000457 / 0.0000108 / 0.0000173 | 0.006715 / 0.000824 / 0.001414 |
| 5 s | 0.054918 / 0.006180 / 0.010274 | 0.0001829 / 0.0000431 / 0.0000693 | 0.026953 / 0.003305 / 0.005673 |
| 10 s | 0.128124 / 0.014154 / 0.023781 | 0.0004121 / 0.0000971 / 0.0001562 | 0.060996 / 0.007476 / 0.012826 |

Geometry normalization bounds a node's incident horizontal and vertical conductance sums by its volume. The worst configured combined removal bounds `dt * (0.02 + 0.005)` are 0.025, 0.05, 0.125, and 0.25 for 1/2/5/10 seconds respectively; O2 bounds are 0.0205, 0.041, 0.1025, and 0.205. All candidates are explicitly stable without clamps or internal substeps. Every case remained finite/nonnegative and inventory-conservative; O2 stayed strictly surface-first with strong depth lag, vent enrichment remained strongest at the true bottom, and source accounting was identical because injection is `rate * simulatedSeconds`.

Five seconds is selected as the coarsest clearly equivalent production cadence: its largest representative horizontal difference is about 0.055 on initial values up to 10, depth difference remains below 0.000183, and vent difference remains below 0.027, while 10 seconds more than doubles each localized maximum. The selected cadence reduces requested topology solves by 80% versus the reference (at requested 10x: 10 to 2 ticks/real-second; at requested 100x: 100 to 20). These are requested frequencies, not measured achieved throughput. Transport equations/order, O2 multiplier, vent placement and absolute per-simulated-time source flux remain unchanged. No additional persistent cache was added for the cadence change, and no post-change Unity profiler measurement is claimed here.

### Legacy/Geodesic runtime isolation

`PlanetResourceMap` is authoritative only while the initialized `PlanetGenerator` reports `LegacyCubeSphere`; `GeodesicOceanResourceField` remains authoritative for dissolved resources in `GeodesicIcosphere`. Legacy initialization and update boundaries reject/inertly exit in every other mode. Debug reads and gizmo rendering must be lifecycle-side-effect free: selecting an object or switching between Game and Scene views may not initialize an inactive simulation system. Legacy cell inspection, markers, atmosphere/resource HUD fields, vent visuals, and resource gizmos are suppressed when Geodesic is authoritative. Legacy and Geodesic logical vent datasets remain separate migration-era systems and are never merged or substituted for one another.

### PR #238 correction notes — resource availability diagnostics

Runtime testing found picker resource rows could show channel-level `--` values for every layer, which made it impossible to distinguish a missing/stale picker reference from an uninitialized resource field or inactive node. The correction keeps the same ownership/storage contract but adds explicit initialization failure reporting, `LastInitializationFailure` diagnostics, a bool-returning initialization path, PlanetGenerator post-call verification, and a startup sentinel read that confirms configured CO2/O2/CH4/Fe2 values plus zero H2/H2S/OrganicC reached one deterministic active node.

Picker resource resolution is now refreshed from the authoritative Planet Generator object during `Awake`, topology binding, and selected-cell resource diagnostic caching. If the field itself is unavailable, the picker reports one actionable status (`component missing`, `field not initialized`, `source grid mismatch`, or `node inactive`) instead of showing rows of ambiguous channel `--` values. A real initialized zero concentration remains formatted as `0`.

The runtime HUD remains legacy-exact in cube-sphere mode. In geodesic mode it labels atmosphere values as global placeholders because geodesic atmosphere coupling is not migrated, and it displays geodesic dissolved-ocean volume-weighted means for CO2/O2/CH4/Fe2 plus Fe2 global inventory when `GeodesicOceanResourceField` is initialized. It deliberately omits legacy Fe2 remaining percentage in geodesic mode because no geodesic depletion baseline exists yet.

Public per-node writes still recompute diagnostics immediately for this foundation phase. Future passive conservative transport should use a dedicated batch mutation path so transport loops do not recalculate full diagnostics per node.

Unity Profiler spikes in `GeodesicOceanTemperature.AccumulateVerticalFlux`, `GeodesicOceanTemperature.ApplySurfaceDeltas`, `GeodesicOceanTemperature.ApplySubsurfaceDeltas`, and `GeodesicTemperature.HorizontalDiffusion` are recorded as a separate follow-up performance task after PR #238. This resource-state correction does not add periodic stepping.

### Implicit geodesic ocean vertical-temperature solver

The production geodesic ocean-temperature coupling now uses one backward-Euler implicit tridiagonal solve per participating multi-layer ocean column for each authoritative surface thermal tick. The fixed thermal cadence remains owned by `GeodesicSurfaceTemperatureField`; the ocean field still advances synchronously from `SurfaceTemperatureTickCommitted`, but `ExchangeVerticalHeat` no longer computes a stability-limited explicit substep count, no longer repeatedly clears delta buffers, no longer traverses the global vertical-link table per substep, and no longer clamps effective diffusivity in production.

For every active column, layer 0 remains the authoritative surface-temperature unknown and layers 1-4 remain persistent subsurface unknowns owned by `GeodesicOceanTemperatureField`. Adjacent interfaces use the cached geometry-only base conductance from `GeodesicOceanLayerGrid.VerticalInterfaceArea / VerticalCenterDistance`, scaled by `verticalThermalDiffusivity`, and the solver applies:

```text
(C_i + dt * (k_above + k_below)) * T_i_new
- dt * k_above * T_above_new
- dt * k_below * T_below_new
= C_i * T_i_old
```

The tridiagonal matrix has at most five unknowns and is solved with fixed preallocated scratch arrays using double-precision coefficients and elimination. Column mutation is two-phase: solved layer-0 temperatures are staged into a compact surface batch, solved subsurface temperatures are staged into compact active-node order, the surface field validates the whole authoritative batch, and subsurface storage commits only after the surface batch succeeds. A failed column or rejected batch logs the first failure and retains previous valid column state rather than partially mutating the coupled column.

Reusable compact metadata is built during ocean-temperature initialization from the existing `GeodesicOceanLayerGrid`: participating surface cells, active subsurface node indices, and per-column interface conductance bases. One-layer ocean cells are skipped because there is no vertical interface to solve. The old explicit delta arrays and per-substep sparse clear/apply production path are removed; the explicit approach should be retained only as a validation/reference concept, not as ordinary Play Mode stepping.

Vertical exchange conserves coupled column energy algebraically as `sum(C_i * T_i)` for surface plus subsurface layers. Production ticks rely on the conservative solve and throttled exact participating-ocean audits instead of whole-field exact audits every tick. The existing tolerance remains `2e-5` relative error; no tolerance was loosened for this migration. Residual and conservation diagnostics are exposed alongside solver mode, solved column/layer counts, failed-column count, callback/frame counts, solver duration, surface-batch duration, subsurface-commit duration, and exact-audit state. Deprecated explicit-substep/clamp serialized fields are hidden for scene compatibility.

Profiler-marker semantics now distinguish `GeodesicOceanTemperature.Callback`, `PrepareColumns`, `SolveImplicitColumns`, `ValidateSolution`, `ApplySurfaceBatch`, `CommitSubsurface`, `ExactConservationAudit`, and `DiagnosticSnapshot`. The expected next performance phase, after Unity profiling this vertical fix, is to isolate `GeodesicTemperature.HorizontalDiffusion` if it becomes the dominant remaining thermal spike. Baseline profiler values supplied for this task were approximately 80-117 ms vertical flux, 62 ms repeated surface apply, 21 ms repeated subsurface apply, 11-12 ms repeated clear, and 20 ms horizontal diffusion on subdivision-6 affected frames. Optimized Unity profiler captures were not produced in this non-Unity environment and must be recorded before claiming a measured speedup.

### Default ecological temperature profiles

Geodesic generation now has one authoritative serialized developer selector on `GeodesicSurfaceTemperatureField`: `GeodesicThermalModel.ApproximateEcologicalProfiles = 0` (the scene and serialization-safe default) or `ConservativeImplicit = 1` (the advanced thermodynamic model). The selector applies to both surface and ocean fields at generation. Runtime switching, startup-menu exposure, save-schema changes, and persistence of either selected mode or geodesic thermal state remain future migration work.

`thermalModel` is configuration for the next generated planet and is never reset by startup-menu entry, cleanup, mode transitions, or temperature deinitialization. Successful surface-temperature initialization latches it once into `activeThermalModel`; all production surface and ocean paths use that active value for the generated planet's lifetime. Cleanup clears only the active latch. Editing the configured value after generation therefore affects only the next generation, while an Inspector selection made before Play Mode or at the startup menu survives generation and is latched exactly as configured. Initialization logs both configured and active values so lifecycle regressions are visible without per-tick logging.

Approximate mode uses the evidence-selected fixed 2.0-s simulation-time cadence, while 0.25 s remains the comparison reference and the unchanged `ConservativeImplicit` cadence. Existing sunlight/albedo/latitude/season surface targets and exponential surface inertia are unchanged, and both surface and subsurface responses continue to use the actual fixed tick duration. Each tick visits every surface cell once, publishes one completed surface tick, and deliberately skips the conservative horizontal edge pass. The synchronous ocean callback visits only persistent active subsurface nodes (layers 1-4) once; layer 0 remains exact read-through owned solely by `GeodesicSurfaceTemperatureField`. One-layer columns therefore have no ocean-temperature work.

For subsurface node center depth `z = clamp01((oceanSurfaceRadius - layerCenterRadius) / maximumOceanDepth)`, the exact ecological target is:

```text
p = pow(z, depthProfileExponent)
deepOceanTarget = clampKelvin(baseTemperatureKelvin + deepOceanTemperatureOffsetKelvin)
target = clampKelvin(lerp(authoritativeSurfaceTemperature, deepOceanTarget, p)
                      + ventStrength * bottomVentTemperatureGainKelvin * ventLayerFactor)
```

`ventLayerFactor` is `1` in the deepest active layer, `aboveBottomVentHeatingFactor` only in the layer immediately above it, and `0` elsewhere. Vent strength first reads an existing compatible vent array when present; otherwise a generation-stable hash of cell index and the derived terrain seed selects `thermalVentColumnFraction` of multi-layer ocean columns as ecological thermal refuges. This fallback never adds resources or changes vent-resource behavior. Each subsurface value initializes directly to this target, including partial-bottom and two- through five-layer columns, avoiding a generation-wide surface-temperature transient. Thereafter it independently relaxes with `timescale = lerp(shallowResponseTimescaleSeconds, deepResponseTimescaleSeconds, z)` and `response = 1 - exp(-deltaTime / timescale)`. This produces diminishing surface influence, greater seasonal lag, and increasing thermal stability with depth.

This default is explicitly an ecological temperature-suitability approximation for thermal niches, migration pressure, stable deep habitat, and warm vent refuges. It is intentionally not energy-conserving and is not an ocean-circulation or thermodynamic model. `ConservativeImplicit` retains horizontal conservative surface diffusion, compact column metadata, staged surface batches, the unchanged backward-Euler column solve, and its conservation/residual validation.

The measured before-change subdivision-6 thermal frame was 43.729 ms CPU / 39.740 ms scripts, including 20.683 ms horizontal diffusion, 7.722 ms implicit columns, and 4.761 ms surface response; its neighbouring ordinary frame was 5.333 ms CPU / 2.070 ms scripts. Unity was unavailable during implementation, so after-change marker timings, allocations, 1x/2x rotating-camera observations, and conservative validator reruns remain required rather than estimated.

### Approximate subsurface relaxation cache

`ApproximateEcologicalProfiles` builds a compact structure-of-arrays cache during ocean-temperature initialization, after the active model and its 2.0-s simulation-time interval have been latched. For each active subsurface node it stores the owning surface-cell index, depth-profile coefficient, fixed-interval relaxation response, and geometric vent-gain coefficient. The production relaxation tick therefore performs no per-node invariant `Pow`, `Exp`, normalized-depth, layer-center, layer-timescale, node-to-cell, or bottom-layer calculations. Dynamic authoritative surface temperatures are still read from `GeodesicSurfaceTemperatureField` every tick, and vent strength is still resolved dynamically; only its immutable layer geometry/gain coefficient is cached.

The approximate-only cache adds exactly 16 bytes per active subsurface node (one `int` and three `float` arrays), or 1,229,680 bytes for 76,855 nodes, excluding managed-array headers. It is not allocated for `ConservativeImplicit` and is cleared with the existing ocean-temperature lifecycle. Approximate cadence is 2.0 simulated seconds, conservative cadence and solver behavior remain unchanged, and no post-change Unity Profiler result is claimed because the Editor was unavailable in the implementation environment.

### Authoritative simulation-speed semantics

Simulation-speed HUD labels are requested/target authoritative world-clock multipliers relative to unscaled real time, not guarantees of achieved throughput. The configured choices are `0x`, `1x`, `2x`, `5x`, `10x`, `20x`, `50x`, and `100x`, and each now maps to the same numeric requested multiplier. Achieved speed is measured independently over a throttled real-time window as the change in `SimulationTimeSeconds` divided by unscaled elapsed real time.

For an advancing rendered frame, the shared clock uses:

```text
stepDelta = min(Time.unscaledDeltaTime, maximumSimulationStepDeltaSeconds)
frameSimulationDelta = stepDelta * requestedSimulationSpeedMultiplier
SimulationTimeSeconds += frameSimulationDelta
```

The maximum-step clamp intentionally means achieved throughput can fall below the requested multiplier on slow rendered frames; accumulated environmental scheduler time is not discarded to improve displayed FPS. Current Geodesic mode has no initialized biology, so it advances authoritative time once per rendered frame and does not perform empty biological substep iterations. The fixed simulation-time environmental schedulers consume the resulting clock normally (approximate temperature at 2 simulated seconds and dissolved-ocean resources at 5 simulated seconds by default).

Initialized Legacy biology preserves its stability-sensitive integration semantics: the requested integer multiplier remains the biological substep count and each substep uses `stepDelta`. Consequently, requesting 50x or 100x in Legacy can execute 50 or 100 full biological iterations per rendered frame and may achieve less than requested on hardware that cannot keep up. This focused correction does not combine those iterations into a large biological timestep.

### Advanced startup environment timing

The startup menu now keeps expert timing controls in a collapsed-by-default **Advanced** section. The authoritative `SimulationStartupConfig` and its existing `startup_config.json` persistence own both values; startup applies them to the environment components before planet initialization, and the components latch them for the new run. The UI is not runtime configuration authority and opening or closing Advanced performs no initialization.

The exposed model parameters are deliberately limited to:

- `approximateThermalIntervalSeconds`: fixed authoritative simulated-time cadence for `ApproximateEcologicalProfiles` only; presets are 0.5, 1, 2, and 5 seconds, with the validated production default of 2 seconds. `ConservativeImplicit` retains its independent cadence and behavior.
- `geodesicResourceTransportIntervalSeconds`: fixed authoritative simulated-time cadence for existing conservative Geodesic dissolved-resource transport; presets are 1, 2, 5, and 10 seconds, with the validated production default of 5 seconds.

Saved startup schema version 3 persists these fields. Loading older JSON begins from validated defaults before overlaying fields that exist, so missing cadence fields migrate to 2 and 5 seconds without deleting the file. Non-finite, non-positive, and off-preset values normalize safely (invalid values use the production default; finite positive values use the nearest preset). **Reset Advanced to Defaults** resets only these two fields and leaves seed, resources, population, and other basic startup choices unchanged.

Geodesic simulation subdivision remains in the existing basic grid setup because it was already user-facing. Render subdivision and thermal-model selection are meaningful future Advanced candidates, but remain hidden in this focused change because they do not yet have complete startup persistence/UI paths. Horizontal/vertical mixing rates and the O2 vertical multiplier also remain hidden pending a dedicated safe-range and stability audit. Engine internals—including catch-up guards, staging buffers, profiler/diagnostic cadence, solver tolerances, layer count, cache layout, and frame-clock clamps—remain Inspector/implementation details rather than startup settings.
### Geodesic dissolved Fe2 ocean-colour sampling

The visual-only `GeodesicOceanFe2Visual` samples dissolved Fe2 from the optically relevant upper ocean only: L0 has weight 1 and active L1 has a configurable visual weight that defaults to 0.4. The result is normalized by the weights of the layers that exist. L2-L4 have no direct influence on visible ocean colour, so deep vent Fe2 must propagate into L1/L0 before becoming visually dominant.

The dissolved-Fe2 colour mapping is an absolute concentration mapping, independent of the configured startup concentration. Its default range is 0 to 8, values above 8 clamp to maximum tint, and the high-Fe2 endpoint remains greenish to distinguish dissolved Fe2 from future oxidized iron precipitates. This is a rendering decision only and does not change transport, chemistry, temperature, or sunlight.

### Geodesic environment diagnostics and vent visualization

Selected-cell diagnostics separate immutable topology, terrain, bathymetry, and layer geometry text from live environmental text. While a cell remains selected, the live portion samples the authoritative surface/ocean temperature fields and all seven dissolved-resource channels directly at a default two-Hz unscaled-real-time cadence. The lookup visits only the selected column's active layers, explicitly labels inactive L0-L4 slots, performs no world aggregation, and is independent of simulation speed and environmental tick cadence.

The environment HUD now has explicit mode ownership. Geodesic mode reports authoritative surface-temperature summaries and cached volume-weighted means for CO2, O2, CH4, H2, H2S, Fe2, and OrganicC under an ocean-dissolved-resource heading; it does not read `PlanetResourceMap`. Legacy mode retains its existing atmosphere/resource presentation.

`GeodesicVentVisualizer` renders static, toggleable seafloor markers from allocation-free indexed read-only access to the logical vent records already owned and consumed by `GeodesicOceanResourceField`. Each marker uses the exact vent cell, while its visual anchor and normal come from the corresponding vertex of the completed visible terrain mesh rather than the logical bottom-layer centre. A small uniform normal offset prevents z-fighting, and a shared emissive disc mesh lies tangent to the rendered seafloor. Visualization performs no vent selection or RNG, so no second vent dataset exists. The single manager owns shared runtime geometry/material and clears its marker root during Geodesic cleanup and mode transitions.

Raw Geodesic geothermal candidates are now deterministic generation-only data. Strongest-first angular clustering, with cell index as the tie-breaker, creates the single authoritative vent-system dataset; submarine and terrestrial candidates are clustered separately so a coastline cannot merge their source habitats. A system's representative is its strongest candidate, its simulation weight is the sum of member raw strengths, and weights normalize to one within each independently configured source habitat. Terrestrial systems are generated and visualized, but their atmospheric injection remains pending because Geodesic does not yet own an authoritative atmospheric resource reservoir; they are never redirected into the ocean or Legacy atmosphere.

Submarine H2, H2S, CO2, and Fe2 startup values are global planetary inventory-per-simulated-second budgets. Each authoritative submarine system receives its normalized share, then distributes that same fixed share over the deepest active nodes of its raw-member footprint in proportion to member raw strength. Raw members are therefore localized outlets, not independently budgeted logical vents. The member shares and system weights each sum to one, so production is global-rate times elapsed simulated seconds and is independent of raw-candidate count, authoritative-system count, clustering strength, simulation subdivision, visible marker count, and resource cadence. One authoritative system produces one marker; marker diameter uses a sublinear square-root mapping of normalized system weight and never feeds back into simulation.

Clustered submarine and terrestrial markers continue to consume `TryGetVisibleGeodesicSeafloorWorldAnchor(...)`. Despite its historical name, this cache maps every simulation cell to the completed displaced terrain mesh; clustering does not restore analytical bottom-layer placement or duplicate terrain sampling.

### Visible Geodesic Seafloor Geometry Contract

Geodesic simulation coordinates and visible terrain coordinates are deliberately
separate concepts.

Simulation systems own logical state using:

- Geodesic simulation-cell index;
- ocean-layer index;
- authoritative layer/node resources.

Visual systems attached to the seabed must use the completed visible terrain
mesh rather than analytical layer-centre or seafloor radii.

PlanetGenerator currently establishes this mapping after final render-terrain
displacement and normal recalculation:

1. The final Geodesic terrain mesh is displaced using the authoritative
   bathymetry/terrain mapping.
2. Mesh normals are recalculated from the completed visible geometry.
3. `CacheVisibleGeodesicSeafloorAnchors(...)` maps simulation cells onto the
   best corresponding final render vertices.
4. For each simulation cell it caches:
   - final local visible seafloor position;
   - final visible terrain normal.
5. `TryGetVisibleGeodesicSeafloorWorldAnchor(...)` converts these into world
   position and correctly transformed world normal.

This fixed the earlier Geodesic vent-marker artifact where logical bottom-layer
centres could place markers above or below the terrain actually rendered to the
player.

#### Reuse requirement

The visible-seafloor mapping is a reusable geometry boundary and should be
preferred for future seabed-attached visuals.

Expected consumers include:

- Geodesic hydrothermal vent markers;
- sessile/bottom-layer replicators; - Bottom habitat rendering: bottom-layer/sessile replicators must use the shared visible-seafloor geometry contract for render position/orientation rather than the analytical bottom-layer shell.
- bottom-crawling replicator visual placement;
- S0 seabed deposits;
- Fe3+/iron-oxide seabed deposits;
- OrganicC or other future sediment overlays;
- other discrete seafloor structures.

Simulation ownership remains cell/layer based. Visual placement must not become
a second simulation state.

For discrete objects:

    simulation cell/layer
        -> visible seafloor anchor
        -> small normal offset
        -> orientation from visible terrain normal

For continuous seabed fields such as S0/Fe3+ tint:

    authoritative bottom-cell state
        -> simulation-to-render mapping
        -> completed visible terrain mesh
        -> vertex/shader/overlay representation
	or better yet: Seafloor precipitates: S0/Fe3 and other sediment/deposit visuals should consume authoritative bottom-layer state through the shared simulation→visible-seafloor mapping.

Do not independently re-evaluate terrain or bathymetry inside each consumer.

The analytical bottom-layer centre remains appropriate for simulation,
transport and habitat ownership, but it is not authoritative for final visible
placement.

### Patchy geothermal provinces and authoritative vent heating

Geodesic geothermal generation now evaluates a deterministic, direction-only low-frequency activity field made from four seed-derived spherical provinces before raw-candidate selection. The Advanced Inspector parameter `geothermalPatchiness` defaults to `0.8`: it blends the former uniform candidate probability/strength with correlated province activity. This creates broad inactive areas, isolated systems, and groups of systems inside active provinces without introducing latitude/longitude coordinates or render seams. Existing strongest-first clustering remains deterministic, habitat-separated, and non-transitive.

The clustered `GeodesicVentSystem[]` owned by `GeodesicOceanResourceField` is also the sole thermal-source authority. Generation precomputes one-ring submarine and terrestrial influence maps from real system members. Local heat uses square-root-bounded raw cluster/member strength, not normalized global production share: member cells receive the strongest influence, matching-habitat immediate neighbors receive `0.3`, and distant cells receive none. Geography and thermal settings therefore do not change the independently normalized global H2/H2S/CO2/Fe2 budgets.

Hydrothermal and terrestrial source-fluid temperatures default to `350 C`; these intrinsic geological temperatures are not ecological cell temperatures. Approximate submarine profiles blend at most `0.08` toward the source in the bottom layer and apply `0.35` of that anomaly only to the layer immediately above. Terrestrial surface targets blend at most `0.06` toward the source. Submarine influence is restricted to ocean-bottom habitat, terrestrial influence to land, and terrestrial chemical injection remains pending an authoritative Geodesic atmosphere.

The cold-vent regression was an authority and initialization-order mismatch: `GeodesicOceanTemperatureField` still read the Legacy `PlanetResourceMap.ventStrength` or generated a private hash fallback before the new clustered resource field existed. Planet generation now builds authoritative clustered vent systems before surface/ocean temperature initialization, and both thermal consumers read their precomputed influence maps directly. No second thermal vent geography remains.

Authoritative clustered systems may remain geographically broad for member-level chemistry and thermal footprints, but their compact outlets are a shared local environment/visual representation rather than a display of the full system footprint. Outlet selection starts at the representative member and deterministically chooses only real members inside the independently configured representative-centred `visualOutletRadiusDegrees` (default `3.5` degrees), ordered by proximity with strength and cell-index tie-breakers. If fewer members exist in that local field, fewer markers are rendered rather than selecting distant members. Deterministic visual-only archetypes produce a single dominant outlet, a dominant outlet with smaller satellites, or several similarly sized local outlets, bounded by `maxVisibleOutletsPerSystem` (default `5`).

Outlet count and placement remain independent of production and cannot multiply, rebalance, or relocate authoritative chemical production or heating. Marker diameter is the experienced-temperature outlet's bounded strength-derived hot-core diameter; it never uses marker count as simulation input. Every outlet still resolves the completed visible terrain vertex and its final normal, then applies the existing small positive normal offset. Representative-centred compact visual fields are the preferred future precedent when broad logical ownership should read as a local seafloor feature, including bottom-layer replicator groups, Fe3+/S0 bottom-tint placement, and other seafloor-attached visual/resource markers; those consumers must likewise preserve their authoritative simulation state and use the completed-visible-terrain placement boundary.

### Local / Experienced Temperature Contract

Geodesic coarse and experienced temperature are separate environmental concepts. `GeodesicSurfaceTemperatureField` and `GeodesicOceanTemperatureField` remain the authoritative stored/evolved planetary, layer, cadence, equilibrium, and broad vent-warming state. `GeodesicExperiencedTemperatureField.TryGetLocalTemperatureKelvin(...)` is a read-only query that starts with those coarse values and evaluates a sub-cell outlet anomaly on demand; it owns no second temperature grid and never mutates either coarse field.

`GeodesicVentOutlet[]` is lightweight non-visual runtime environment data built once from the deterministic compact member selection of each authoritative `GeodesicVentSystem`. Each record owns habitat, simulation cell/bottom layer, system id, completed-visible-terrain planet-local anchor and normal, and bounded raw system/member strength. The visualizer consumes these records rather than regenerating outlets or making transforms authoritative. Consequently the authoritative vent system still owns geological grouping, coarse source footprint, and the independently normalized global chemistry budget, while each localized outlet owns one extreme-temperature microhabitat. Authoritative system extent is not microthermal hotspot extent.

The default planet-local outside-core falloff distance is `0.12`, independent of clustering radius and resource production. Each strength-derived visible core remains at `sqrt(clamp01(localRawStrength))` influence; outside its edge, normalized falloff distance uses the compact C2 smootherstep complement `1 - x^3(6x^2 - 15x + 10)`, reaching exactly zero at the core radius plus falloff distance. Nearby outlet influences combine by maximum, never sum, and temperature is bounded interpolation from coarse temperature toward the shared intrinsic `350 C` source-fluid authority. Stronger outlets therefore cannot be cooler at an otherwise identical sample, but source temperature never increases with strength or global production share.

A generation-time compressed per-cell lookup indexes outlet ids for the outlet's owning cell and real immediate neighbors. Steady-state queries traverse only that cell slice: no allocations, LINQ, global outlet scan, GameObject/Transform lookup, raycast, or terrain recomputation. Positions are transformed into planet-local space before distance evaluation, so the habitat follows planet transforms and agrees with the completed visible-terrain outlet anchor. Initialization logs outlet count, indexed cells, min/mean/max nearby outlets, and approximate lookup bytes.

Submarine anomalies apply only to the active bottom layer; immediately-above and upper layers retain their existing coarse temperatures. Terrestrial anomalies apply only to layer-zero land surface queries. Habitat filtering prevents terrestrial outlets from warming ocean habitat and submarine outlets from warming land. Vent chemistry continues to use its authoritative coarse local source footprint, which intentionally need not match the much smaller extreme-fluid thermal kernel.

Future bottom/sessile Geodesic organisms and any temperature-tolerance, mutation, inhibition, or death behavior must use the experienced-temperature API whenever an actual position is available. This permits ordinary, thermophile, and hyperthermophile niches within one coarse cell without making the entire tile extreme. Biology behavior itself remains pending. Compact picker diagnostics report local/experienced temperature at the preserved clicked terrain position, with authoritative coarse temperature as an initialization/teardown fallback; Detailed Debug retains the coarse-versus-local distinction, sampled layer, and lookup/radius context.

### Abyssal asymptote and two-scale vent refinement

The approximate coarse ocean profile no longer derives its deep target by subtracting a fixed offset from the planetary base temperature. It approaches the serialized `abyssalBaselineTemperatureC` (default `1.5 C`) instead, with only the serialized `abyssalClimateCoupling` fraction of departures from the Earth-reference planetary base. Normalized layer-centre depth and the existing profile exponent still interpolate from the authoritative surface, so mid-depth water normally remains warmer than the deepest abyss while trench backgrounds converge near cold liquid-water temperatures rather than continuing to large negative Celsius values. This equilibrium rule belongs only to `ApproximateEcologicalProfiles`; `ConservativeImplicit` deliberately remains an energy-conserving isolated-column model rather than acquiring an unbalanced abyssal thermostat.

Coarse hydrothermal warming remains part of the real authoritative ocean environment, not the experienced-temperature query. The clustered Geodesic vent systems provide a precomputed, deterministic one-ring submarine influence: source-member cells are strongest, matching-habitat immediate neighbors receive a smaller influence, and all more distant cells receive none. The approximate target weakly and boundedly blends only the bottom layer toward source-fluid temperature, applies `aboveBottomVentHeatingFactor` only one layer above, and applies no direct anomaly higher in the column. Thus the cell picker, ecological baseline, and stored ocean layers see a local warm refuge without assigning `350 C` fluid temperature to an entire coarse cell. Terrestrial influence continues to affect only the land surface. No chemistry or production budget is coupled to these thermal parameters.

Each compact outlet record now also carries its planet-local visible/hot-core radius, derived once from bounded local outlet strength between serialized minimum and maximum radii. The marker disc consumes that exact diameter, and experienced temperature stays at the outlet's strength-bounded source regime through the whole core before beginning its compact smootherstep falloff over `ventMicrothermalFalloffDistance`. Larger markers therefore retain their hot regime at their visible edge. Indexed cell-to-outlet ranges, maximum (not additive) overlap combination, habitat filtering, and allocation-free query loops remain unchanged, so overlaps cannot exceed source temperature and the hot path performs neither a global scan nor a physics query.

The two thermal scales are an explicit API contract for future replicators, sediments, and bottom-surface visuals. `GeodesicSurfaceTemperatureField` and `GeodesicOceanTemperatureField` own coarse authoritative environmental background, layer state, cell-environment display, and ecological baseline. `GeodesicExperiencedTemperatureField` is a read-only positional microhabitat query layered over that coarse state; it may approach source-fluid temperature near an individual outlet but owns no grid and never mutates coarse temperature. Callers with a real habitat position should use experienced temperature for local organism behavior, while coarse displays, transport-scale state, and broad environmental decisions must continue to use the authoritative fields.

### Abiotic Ocean Chemistry Contract

The first authoritative Geodesic abiotic chemistry pass is implemented. `GeodesicOceanResourceField` remains the sole dissolved-state authority for CO2, O2, CH4, H2, H2S, Fe2, and OrganicC. On each completed resource interval the deterministic operator order is global-budget vent/source injection, horizontal and vertical dissolved transport, then local abiotic chemistry. The chemistry receives that completed interval's authoritative simulated duration; it has no rendered-frame or playback-multiplier clock, so pause advances nothing.

Chemistry discovery is now fused into the already-required staged concentration application traversal. After all seven staged dissolved channels have been applied for an active node, the resource field appends that node to a preallocated active-node-capacity array exactly when its authoritative H2, H2S, or Fe2 concentration is positive. The list is rebuilt from zero every resource tick after vent injection, horizontal transport, vertical transport, and staged application, and before oxidation and FeS precipitation. Consequently newly transported reactants react in the same tick, exhausted nodes disappear on the following tick, and globally distributed Fe2 degrades directly to the original active-node traversal order without sparse-container overhead or per-tick allocation. Chemistry node volume continues to come from the authoritative layer grid. Sparse ticks therefore retain the staged-application full traversal but replace the second full active-node chemistry traversal with a candidate-count traversal.

Every reaction is evaluated independently in one active simulation-cell/layer node. Concentrations are converted to inventory using that node's volume before limiting and stoichiometry, then remaining inventories are converted back to concentrations. Surface or column-average oxygen is never consulted: reduced material in a deep layer cannot consume surface O2 until the transport system brings oxygen into the same node.

The implemented reactions are `H2 + 0.5 O2 -> H2O`, `H2S + 0.5 O2 -> S0 + H2O`, and `Fe2 + 0.25 O2 -> oxidized Fe(III) precipitate`; water is intentionally untracked. Requested H2, H2S, and Fe2 extents use `1 - exp(-ln(2) * dt / halfLife)` with configurable Inspector defaults of 60, 120, and 180 simulated seconds. Non-positive half-lives disable the corresponding reaction. All three requested extents are computed before consumption and, when their combined demand exceeds local O2, receive the same bounded oxygen scale. This prevents code-order priority.

`GeodesicOceanSedimentField` is the authoritative lightweight per-column deposited-inventory store for elemental sulfur S0 and oxidized Fe(III). These are not dissolved channels and do not enter the seven-channel transport loop. This first implementation immediately deposits products from any layer into the same column, a coarse precipitation-plus-eventual-settling abstraction; suspended particles, sinking time, horizontal sediment transport, reduction, and coloration remain future work. Its public read-only inventory queries are the boundary for later seabed tinting through the existing Visible Geodesic Seafloor Geometry Contract. Two double arrays cost 16 bytes per simulation cell (about 160 KiB at 10,242 cells) and are cleared with every Geodesic teardown/regeneration and mode transition.

The Legacy audit found local layered Fe2 oxidation in `PlanetResourceMap`, including the same simplified 0.25 O2/Fe2 stoichiometry, plus Legacy S0 dissolved/storage and visual precipitate pools. It also found OrganicC natural oxidation and biological sulfur/hydrogen/methane reactions, but no reusable authoritative abiotic H2 or H2S oxidation framework. Only the 0.25 Fe2 oxygen stoichiometry and same-layer locality were intentionally retained as semantics. Legacy's rate, arrays, S0 transport/storage, precipitate visuals, and `PlanetResourceMap` authority are not used by Geodesic chemistry.

There is deliberately no temperature, experienced vent microtemperature, pressure, pH, or biology dependence. Later metabolisms must share the same local dissolved authority and inventory accounting and must treat S0/oxidized-iron deposits as the single deposited authority; recycling those products is not part of this phase.

### Periodic Geodesic chemistry telemetry

Periodic chemistry telemetry is diagnostic-only and retains authoritative simulated-time report deadlines, but expensive snapshots are additionally gated by a serialized minimum unscaled-real-time interval (5 seconds by default). Crossed simulated deadlines coalesce into one latest-state snapshot, then the next simulated deadline is based on that authoritative snapshot time; no historical-report backlog or simulation catch-up work is retained. A serialized enable switch makes ordinary `Update` calls constant-time and prevents all ocean/sediment scanning when detailed telemetry is disabled. World cleanup/regeneration resets both clocks and snapshot diagnostics. Lightweight abiotic-chemistry Profiler counters remain owned and updated by `GeodesicAbioticChemistry`, independently of telemetry scheduling.

Each detailed snapshot deliberately performs one full active-ocean-node traversal: it reads all seven dissolved concentrations, accumulates volume-weighted whole-ocean and per-layer totals/means, bottom and precomputed vent-footprint-bottom reducing summaries, O2 minima/maxima, and volume-weighted anoxic fractions. The same outer traversal also visits every simulation column to total S0, oxidized-Fe, and FeS sediment inventories and occupied-column counts. The vent footprint itself is cached at world initialization. Formatting and `StringBuilder` allocation happen only after these scans when a snapshot is eligible. Existing global resource diagnostics could replace only the whole-ocean totals; they do not contain the layer, bottom, vent, redox, or sediment detail, so this focused performance phase intentionally leaves snapshot semantics intact rather than introducing a broad incremental-statistics architecture. Atmospheric chemistry remains explicitly `notYetAuthoritative`; telemetry must not read Legacy atmosphere state, and atmospheric fields will be added only when an authoritative Geodesic atmospheric reservoir exists.

### Abiotic FeS precipitation and sediment appearance

The Geodesic interval operator is now explicitly **vent injection -> dissolved transport -> O2-based oxidation -> FeS precipitation**. Oxidation is evaluated first in each local node, and `Fe2 + H2S -> FeS(s)` then consumes the remaining local Fe2 and H2S at one-to-one inventory stoichiometry. Its exponential half-life is serialized; a non-positive value disables it. FeS immediately deposits into the same simulation column and has no dissolved channel, suspended authority, or shadow transport array.

`GeodesicOceanSedimentField` is the single per-column authority for S0, oxidized Fe precipitate, and FeS sediment. Dissolved reduced Fe2 therefore remains resource-field state; rusty oxidized iron and black FeS are deposited products with separate inventories. This enables reducing vent/deep nodes to make FeS before photosynthesis, while initial or later O2 produces ferric deposits and takes deliberate operator priority in oxic nodes.

Rusty water is intentionally a **visual-only** short-lived column memory of recent local Fe2 oxidation. It decays through one compact simulation-column pass per chemistry tick rather than piggybacking on a full layered-node chemistry scan. It shares the existing allocation-free Geodesic ocean mesh-colour refresh and is independent of the green-channel dissolved-Fe2 signal, so dissolved Fe2 alone cannot turn the ocean brown. It is not transported, conserved, or exposed as chemistry authority, and resets on world cleanup. A future suspended-particle transport model should replace rather than reinterpret this proxy.

Continuous underwater seabed appearance samples all three authoritative sediment inventories through the existing simulation-cell-to-render-vertex mapping and tints the completed displaced terrain mesh: S0 yellow, oxidized Fe rusty red-brown, and FeS charcoal/black. It creates no per-cell objects and never uses logical layer-centre radii. The completed visible-seafloor anchor/mapping system established for vents is now a reusable pattern and the required placement boundary for future bottom tint, stored-resource rendering, and bottom-attached replicators: retain simulation-cell ownership, map to completed visible terrain, and follow its final surface geometry and normals rather than inventing an approximate radius.

Sediment appearance is revision-driven and visual-only. The authoritative sediment field increments one monotonic visual revision for each valid call that deposits any non-zero S0, oxidized Fe, or FeS inventory; zero deposits do not invalidate rendering. The terrain visual retains the completed simulation-cell-to-render-vertex mapping, compares that revision in its rendered-frame `Update`, and performs no scans, allocations, colour-array reconstruction, or mesh writes while it is unchanged. Pending revisions are coalesced behind a default 1.0-second unscaled-real-time refresh interval, independent of requested simulation speed, so chemistry inventory remains immediate while accelerated simulation produces at most one expensive terrain-colour rebuild per real second. Initialization still forces the initial terrain-colour application, and Geodesic teardown restores original terrain colours and resets both field revision and visual cache diagnostics.

### Compact physical vent-mouth authority

Raw vent candidates and clustered-system members are generation-only after clustering: they determine geography, system strength, and each system's normalized share of the configured global resource budgets, but they are not physical emission points. Each clustered system deterministically retains a compact outlet set. The system budget is redistributed across only those outlets in proportion to their selected member strengths, normalized to one within the system, so global H2/H2S/CO2/Fe2 rates and stronger-system weighting remain unchanged.

`GeodesicOceanResourceField` owns this compact outlet dataset. Direct dissolved-resource injection, full-strength coarse submarine/terrestrial heating, experienced-temperature indexing, completed-terrain vent markers, telemetry vent-footprint diagnostics, and future vent-local visual byproducts must consume it. Intentional coarse heat falloff reaches only immediate matching-habitat neighbors of compact outlets. Removed members can therefore affect another location only indirectly through normal dissolved transport; they cannot inject, heat, appear in a source list, or localize chemistry products. The context validator verifies source-node/bottom-layer mapping, normalized global distribution, and exact agreement between compact outlet cells and direct thermal-source cells.
