# Geodesic drainage, stage 1

Branch: `codex/implement-geodesic-river-drainage`, based on local `codex/implement-geodesic-planet-prototype-mode` at `bab3082`. Changes are local and uncommitted.

## Terrain audit and authority

`PlanetTerrainSampler.Evaluate` already computes deterministic continents, domain-warped mountain masks, ridges, and additive fine detail on the CPU. `PlanetGenerator` calls it for the simulation-cell cache and again at the independent, denser render vertices. Therefore a cell-centre value alone cannot describe the terrain inside that cell. Coastal bathymetry can also alter the completed visible mesh. `GeodesicVertexColorURP` transforms and lights existing vertices; it adds no terrain displacement.

The sampler now returns `LargeScaleHeightOffset` alongside its unchanged full `HeightOffset`. Both come from the same continent/mountain calculation and the same contrast/clamp operations. Only the additive fine-detail term is omitted, and only when its frequency is at least twice the larger continent/mountain base frequency and its amplitude is at most one quarter of the larger continent/mountain amplitude. Broad or high-amplitude detail remains authoritative for routing. All mountain octaves, masks, warping and ridges remain. This classification is a practical first-stage heuristic, not a spectral climate/terrain model.

The existing terrain-displacement pass caches the large-scale radii at render vertices with no additional noise evaluation. `GeodesicRiverTerrain` interpolates these heights for drainage. It separately intersects the completed visible triangles for ribbon placement. This also handles a collider with a lower subdivision than the render mesh: hydrology does not query the collider. Polygon chord sag is ignored when comparing large-scale altitudes, but included in exact visible placement. All positions and radii remain planet-local until the renderer applies the planet transform.

No simulation-cell elevation, visible mesh vertex, ocean mask, chemistry, climate, erosion, or resource source is changed by the river system. The original full terrain formula remains unchanged.

## Files and components

- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicDrainageGraph.cs`: pure terrain DAG and independently replaceable discharge.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverTerrain.cs`: hierarchical height/visible-mesh queries, cached triangle normals and planes.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverPath.cs`: bounded downhill corridor refinement and first sea-level crossing.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverSystem.cs`: generation integration, static mesh, mouths, inspector controls, diagnostics, cleanup.
- `Assets/Scripts/Planet/Geodesic/Terrain/PlanetTerrainSampler.cs`: exposes shared large-scale height without changing full height.
- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs`: enable flag, generation hook, height cache, and cleanup on regeneration/Legacy transition.
- `Assets/Tests/EditMode/GeodesicDrainageTests.cs`: ten deterministic tests. Unity metadata accompanies new assets.

## Drainage and basins

Each land cell chooses one nearby low terrain anchor, constrained to 20% of its neighbour spacing from its centre. All tributaries use that same anchor. This is a channel junction coordinate, not a replacement elevation for the simulation cell. Three interior samples on each neighbouring corridor estimate a saddle height.

An indexed binary min-heap performs an ocean-rooted minimax priority flood. It computes the minimum spill elevation needed to connect each land cell to an outlet. Deterministic ordering by elevation and cell index gives a cycle-free parent forest. A subsequent pass prefers the steepest lower real neighbour when compatible with the filled surface and the settled order. Filled flats retain an earlier parent, without adding artificial epsilon slopes. Reverse flood order provides iterative upstream-to-downstream accumulation.

`HydrologicalElevation` and `FilledElevation` are planet-local radii; their difference is `FillDepth`. Filling is virtual and never edits terrain. Small depressions therefore cannot trap the drainage DAG. Large depressions remain identifiable through fill depth and basin outlet identity. They also spill virtually to the ocean in this first version. An all-land planet has an explicit unresolved sink at its lowest cell; no fictitious ocean or preselected river mouth is created. An ocean world contributes no terrestrial runoff.

## Area, water, rivers and mouths

`DrainageArea` sums existing spherical cell area multiplied by planet radius squared, in squared planet-local length units. Ocean cells contribute zero terrestrial area. It changes only when terrain/topology is rebuilt.

`AccumulatedRunoff` is a separate double array. Initial local supply is `LocalArea * uniformRunoffPerArea`. With density 1 it equals catchment area, but it is not an alias of it.

Future climate calls `GeodesicRiverSystem.UpdateRunoff(double[] localRunoff)` with a nonnegative per-cell water supply such as `precipitation * LocalArea * runoffCoefficient`. Dry cells can contribute zero. This reaccumulates discharge and rebuilds visibility/widths while retaining receivers, catchment areas, terrain, anchors, and cached paths. Newly wet reaches are refined lazily. It does not recompute drainage topology. Call this on climate cadence, not every frame.

Continuous `RiverStrength` is discharge divided by the visibility threshold. Thresholds use mean-cell-area equivalents at unit runoff density so inspector values are convenient. A river has strength >= 1; width grows with the square root of strength and is capped. Every land-to-ocean receiver edge is recorded in `Mouths`, including dry potential outlets. Records retain land/ocean IDs, area, discharge, strength, and a visible coast position when a valid visible reach exists.

## Visible channels and honest limitations

A dynamic-programming search chooses a low, downhill sequence of samples in a bounded corridor between shared cell junctions. Intermediate transition samples reject hidden ridges. Paths stop at the first visible sea-level crossing rather than extending to the ocean cell centre. Ribbon edges and additional longitudinal samples are projected individually onto actual visible triangles, with a small radial offset. One static mesh and material render the network; there is no per-frame drainage work.

Filling a basin virtually does not create a downhill channel on the unchanged terrain. If the bounded search cannot find one, that reach remains in the drainage DAG but its ribbon is suppressed and counted in `suppressedUphillReaches`. Thus some visible networks have gaps at basins, steep corridor barriers, or shoreline mismatches. No floating lake surface, invented uphill water, channel carving, or high-resolution global hydrology is used to disguise that limitation. A later lake/channel task can resolve these reaches. The coarse graph and its saddle estimates can still miss narrow off-corridor passes; increasing refinement does not replace a sufficiently resolved simulation grid.

Fine bumps may make the projected ribbon rise slightly on the visible microtexture; its route is downhill on the retained large-scale surface. The finite sampling cannot guarantee discovery of features narrower than the chosen mesh/corridor resolution. Large fine-detail settings are deliberately retained and may produce more flagged basins. Mouths and widths are diagnostic stage-one visuals, not physical estuaries or discharge-calibrated channels.

## Controls and diagnostics

Enable **Geodesic Rivers** on `PlanetGenerator` (default true). Generation adds `GeodesicRiverSystem` to that planet. Its principal defaults are:

- River/major thresholds: 8 / 64 mean-cell equivalents.
- Uniform runoff density: 1; zero makes the same drainage network dry.
- Corridor: 12 steps, 7 lanes, 0.35 cell-spacing half-width.
- Uphill tolerance: 0.000002 planet-local units, solely for floating-point comparisons.
- Major basin threshold: 0.01 planet-local units.
- Width: 0.0006 planet radii at threshold, capped at 5 times that width.
- Surface offset: 0.0003 planet-local units.

Use **Refresh River Thresholds And Visuals** after changing appearance/thresholds/refinement. Use **Apply Uniform Runoff** after changing runoff density; this preserves drainage topology. Use **Rebuild Rivers For Current Terrain** only after terrain generation, or regenerate the planet when terrain settings change. Disabling the component hides the mesh; disabling the generator option and regenerating clears it.

`[GeodesicDrainage]` reports cells, river candidates, visible/suppressed reaches, mouths, filled/major basin cells, unresolved sinks, and timing. Enter a simulation cell ID in `debugCell`, select the component, and use **Log Selected Drainage Cell** for height, receiver, final outlet, fill depth, area, discharge and strength. Selected gizmos trace up to 256 downstream links, with major filled-basin cells in magenta. Cached arrays and paths are also exposed for inspection.

## Cost and checks

Global drainage uses O(N log N) heap operations and O(N) graph storage. Coarse sampling is bounded per cell/edge. Projection setup is O(V) in render geometry, with O(log V) triangle lookup; this builds a lookup structure, not a hydrological graph on every render vertex. Local refinement runs only for river candidates with bounded steps/lanes. Paths are cached. A runoff update is O(N) accumulation plus ribbon rebuilding and any newly needed paths.

Ten focused NUnit test methods passed in a standalone C# harness using the actual production files and installed Unity vector math. They cover deterministic sink filling, real-neighbour receivers and absence of cycles, area/runoff conservation, wet/dry topology independence, invalid-runoff rejection, dry/ocean worlds, sampled saddle handling, large-scale/fine-detail separation, visible triangle projection, synthetic ridge avoidance, and coast clipping.

Seeded CPU fixtures using Earthlike terrain, radius 8, and the current threshold produced:

| Seed | Simulation level | Land cells | Land with ocean outlet | Candidate / visible reaches |
|---|---:|---:|---:|---:|
| 1 | 4 | 1,211 | 1,211 | 90 / 78 |
| 42 | 4 | 933 | 933 | 26 / 19 |
| 872 | 4 | 1,112 | 1,112 | 65 / 53 |
| 42 | 6 | 14,929 | 14,929 | 1,979 / 1,856 |

The level-6 fixture took approximately 3.1 seconds for terrain/topology setup and 3.8 seconds for refinement on this computer. These are standalone CPU measurements, include fixture terrain construction, and exclude Unity mesh upload/rendering. They are not Unity frame timings or visual acceptance results.

A separate comparison with the original sampler preserved full terrain heights, mountain masks, and ridges exactly in 1,926 samples across three seeds. Legacy terrain code was not modified.

The simulation assembly and EditMode test assembly are compiled against the project's Unity compiler/reference response files. The full Unity Test Runner and scene visual acceptance remain to be run in the open editor.

## Practical visual validation

1. Generate at least three seeds with smooth, Earthlike and rugged settings. Prefer simulation level 5 or 6 and a render level at least as high. Inspect the day side with normal terrain colours and cell outlines toggled off.
2. Follow several major rivers from tributaries to the coast. Check that shared junctions meet, width increases downstream, and mouths stop at the visible shore. Compare basin area and discharge using cell diagnostics.
3. Inspect ridges, valleys, narrow passes and filled basins. The large-scale route should descend; compare any missing reach with fill-depth and suppression diagnostics. Increase refinement or simulation resolution where coarse corridors are insufficient.
4. Set uniform runoff to zero and invoke **Apply Uniform Runoff**: rivers disappear while receivers and drainage area remain unchanged. Restore density 1, then feed an array with a dry hemisphere and a wet hemisphere through `UpdateRunoff`; compare the same receivers and areas.
5. Regenerate with the same seed/settings: arrays and paths should match. Change seed: old ribbons must be removed. Translate/rotate the planet: terrain and ribbons should remain attached through their common parent transform.
6. Check OceanWorld, disabled ocean/all-land, rivers disabled, repeated generation, and transition to Legacy Cube Sphere. Legacy must have no river object left visible. Check the Console for unresolved sinks and suppressed reaches rather than assuming every filled basin has a visible downhill channel.
