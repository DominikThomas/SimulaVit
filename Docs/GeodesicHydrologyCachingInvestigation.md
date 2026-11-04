# Geodesic hydrology caching investigation

Investigation date: 2026-11-04. Proposal only; no production changes, tests, Unity generation runs, benchmarks, or sweeps were performed. This report inspects the current working tree, including the uncommitted generation-stage/loading-screen changes. Existing documentation and existing Unity Editor log entries supplied performance evidence.

## Recommendation

Use a **bounded, session-only cache of immutable generation snapshots**, separate from live planet and simulation objects. Design four logical layers: completed carved surface, authoritative TIN/basins, raw river networks plus their rendered geometry, and lake geometry/routing. Dependencies between those layers must be explicit; they are not four independent caches.

For the **first milestone, implement only the completed carved-surface snapshot**, with one retained entry and a byte cap. Restore the exact final vertex positions, paired terrain arrays, original frozen ocean mask, and carving evidence, then run the existing final TIN, basin, lake, routing, rendering, and delivery sequence normally. This offers substantial savings in the observed three-pass case, has a small fixed payload, and avoids serializing the mutable TIN/lake object graph. A heights-only array is insufficient for exact equivalence.

Follow with authoritative TIN/raw-network snapshots only after restoration equivalence is established. Lake routing offers another large saving but has the highest ownership/conservation risk. Lake mesh caching alone is a low priority: the observed geometry build costs about 19 ms, versus about 9.6 seconds for lake routing.

Lake minimum depth and area can change without invalidating carved terrain. The current caller deliberately supplies **null lake geometry and routing** to the carving planner and uses unqualified basin descriptors during intermediate passes. Conversely, **river flow threshold and runoff density can invalidate carving**: the planner consumes extracted networks and their accumulated discharge.

## 1. Existing caches and lifetime

Paths below are relative to the repository root.

| Cache / owner | Cached contents | Lifetime and key | Invalidation / limits |
|---|---|---|---|
| `Assets/Scripts/Planet/Geodesic/Grid/GeodesicTopologyCache.cs` | `GeodesicGridTopology`: unit directions, triangle indices, neighbor counts/indices, pentagons, cell areas, angular distances, dual-edge lengths and dual corners | Static managed dictionary, keyed by subdivision clamped to 0–8 | Explicit `Clear()`, normal domain reload/process exit; no eviction or disk persistence |
| `Assets/Scripts/Planet/Geodesic/Rendering/IcosphereRenderGeometryCache.cs` | `IcosphereRenderGeometry`: unit vertices and triangle indices, **not Unity Mesh objects** | Static managed dictionary, clamped subdivision | Explicit clear, normal domain reload/process exit; no eviction |
| `Assets/Scripts/Planet/Geodesic/Rendering/IcosphereDirectionMappingCache.cs` | Nearest simulation cell and neighbor interpolation samples, flattened neighbor indices/weights, identity/build statistics | Static managed dictionary keyed by simulation subdivision, target subdivision and `IcosphereDirectionMapping.Version` (currently 1) | Explicit clear, mapping-version key change, domain reload/process exit; no eviction |
| `PlanetGenerator.RebuildGeodesicCellTerrainCache()` | Three cell arrays: height, normalized height and raw radius | Per-generator managed state, freshly allocated each generation | Regeneration overwrites; runtime teardown nulls it; no cross-game/disk cache |
| `PlanetGenerator.geodesicCurrentRenderTerrainData` | Raw, hydrological and completed surface radii, heights, normalized heights, mountain masks, inland diagnostics | Per-generation managed arrays | Rebuilt with visible terrain; carving mutates five arrays; teardown resets the struct |
| Other `PlanetGenerator` derived state | Ocean/bathymetry/classification arrays, inland-distance data, visible seafloor anchors/normals, direction-to-debug-radius dictionary and carved-surface query helper | Per-generator runtime/query caches | Generation/explicit surface changes rebuild or invalidate them; teardown releases references |
| `GeodesicRiverSystem` | Current TIN, basins, networks, lake routing, terrain-query helper, path/mouth metadata and runtime visual meshes | Current planet/component only | `Clear()`, `OnDestroy()`, explicit rebuilds and `UpdateRunoff()`; no reusable hydrology cache |
| `Assets/Scripts/Planet/Common/Generation/PlanetGenerationCache.cs` | Legacy planet unit vertices, indices, final radii, ocean mask/depth/shore distance/threshold; separate resource and surface-texture payloads | Binary files under `Application.persistentDataPath/SimulaVitPlanetCache`; SHA-256 of explicit textual key | Format/generation versions and parameter keys; header/dimension checks and load-failure fallback; explicit `ClearAllCacheFiles()`; oldest access-by-write-time pruning to 256 files / 1024 MB |

`PlanetGenerator.GeneratePlanet()` uses the disk planet cache in the **legacy cube-sphere path**, around lines 2822–2909. `GenerateGeodesicPrototypeStages()` does not use it. Resource persistence is likewise the legacy `PlanetResourceMap` path; geodesic startup skips that resource map and initializes `GeodesicOceanResourceField` normally. The existing disk key is not a geodesic terrain or hydrology key.

`PlanetGenerator.ClearGeodesicRenderGeometryCache()` clears render geometry and mappings; `ClearGeodesicTopologyCache()` clears simulation topology. Menu teardown does neither. Existing static caches have no explicit subsystem-registration reset, so disabling domain reload can leave them alive across Play Mode sessions. Their arrays are only immutable by convention: `readonly` protects an array reference, not its elements.

`GeodesicHydrologyTopology.Build()` constructs lightweight render-vertex adjacency on each hydrology rebuild. It shares render directions/indices but is **not itself cached**. `GeodesicRiverTerrain` also rebuilds per-instance face-query data despite using cached unit geometries.

## 2. Current active pipeline and insertion points

The active implementation is continuous render-triangle TIN drainage. `InitializeLegacyReference()`, `GeodesicDrainageGraph` and the historical graph lake classes are not the production path being proposed for caching.

1. **Initial terrain and simulation initialization.** `PlanetGenerator.GenerateGeodesicPrototypeStages()` (around 787–1010) obtains simulation topology, creates a fresh transport graph, evaluates cell terrain, resolves ocean classification/bathymetry and inland uplift, and initializes ocean layers, resources and temperatures. It then obtains render geometry/mapping, builds a new mesh, applies terrain displacement and colors, recalculates normals/bounds, and assigns the visible mesh. This remains normal work on a hydrology cache hit.
2. **Entry.** `GeodesicRiverSystem.InitializeStages()` (162 onward) optionally records the broad diagnostic fingerprint and pre-carve hashes. `InitializeCoreStages()` clears old hydrology/visuals and decides whether carving is eligible: enabled, not already attempted, `HydrologyTerrainMode.Visible`, and exactly zero configured `tinFilterStrength`.
3. **Initial TIN.** `InitializeRenderHydrologyStages()` (537 onward) builds lightweight adjacency and maps the retained simulation ocean to the visible render surface, respecting excluded potential-lake basins. It samples vertex magnitudes, derives epsilon, relief-dependent bump allowance and mean simulation-cell area, and builds the selected hydrology height field. `GeodesicTinHydrology.BuildAdaptive()` constructs facet/incident adjacency, gradients and flat routing, traces every valid land triangle's original runoff parcel, and extracts raw networks. Optional filtering retries rebuild that entire result when visible disagreement occurs.
4. **Raw basins before carving.** Intermediate calls use `BuildUnqualifiedTinBasins()`: area threshold zero and depth `double.MaxValue`. The lower-star sweep retains floors, spills, parent IDs, footprint members and downstream witnesses. No lake mesh, lake routing or river visual is built.
5. **Every carving pass.** `GeodesicValleyCarving.Plan()` selects closed-basin networks meeting the carve-runoff threshold, plans bounded cuts using raw basins and existing raw rivers, and validates coastal proposals with temporary proposed TINs when necessary. It receives the **original frozen render ocean mask**. `ApplyGeodesicValleySurface()` applies accepted heights, renormalizes edited positions, updates paired terrain arrays, colors, normals, bounds, surface query data and seafloor anchors. The caller clears hydrology and rebuilds TIN/networks/basins even when no cuts were accepted. With P passes there are P+1 authoritative accumulation builds, plus any temporary coastal-proof TINs.
6. **Final terrain/TIN.** The last rebuild uses the actual completed mesh and the frozen pre-carve ocean membership. It builds `GeodesicRiverTerrain`, triangle channel anchors and triangle-to-simulation-cell mapping. This is the boundary at which the proposed terrain-only hit resumes. It must not recompute retained-ocean membership from the newly carved coast.
7. **Final basins and lake mesh.** `RefreshTinLakeVisuals()` (1025 onward) applies current minimum lake depth/area, then `GeodesicTinLakeGeometry.Build()` suppresses submerged descendants, clips wet polygons, excludes retained-ocean facets and emits water geometry at spill radius. It also currently creates the Unity mesh/material/GameObject immediately. Lakes disabled still produces unqualified final basins.
8. **Final networks and lake routing.** `RebuildRenderHydrologyVisualStages()` (647 onward) normally calls `ExtractNetworks()` again, although `BuildAdaptive()` already extracted networks. It optionally captures rejected-path evidence. `GeodesicTinLakeRouting.Build()` clips raw reaches at exact lake entries, traces witnessed spill outlets, creates directed lake edges and channel joins, and calls `VisitRunoffParcels()` to replay parcel paths. It builds unique parcel ancestry, resolves deterministic DAG discharge, and enforces cycle/duplicate/conservation checks.
9. **Delivery and ribbons.** Delivery analysis runs after lake routing and before visual emission. Raw or lake-adjusted paths are checked against crossed facets; interpolation/projection retain existing fallbacks. Raw lines or three-column ribbons are emitted, including tapered lake outlets. Mouths map to retained simulation-ocean cells. Visual observations update delivery diagnostics. There is a consequential mutation here: a missing rendered trunk can change a raw network's `Termination`/`JunctionNetwork` and diagnostic counters. A raw-network cache must be captured **before** this mutation, with final render outcomes stored separately.
10. **Completion.** Final hashes and delivery/catchment reports are logged, and parcel destination scratch is released. `PlanetGenerator` then finishes experienced-temperature/vent initialization, collider construction, ocean/atmosphere visuals and diagnostics. A carved surface forces the collider to use the completed render mesh. Startup later initializes biology and resumes the configured simulation state.

## 3. Proposed boundaries and safe payloads

| Boundary | Reusable snapshot | What must remain fresh |
|---|---|---|
| **C: completed carving** | Exact local `Vector3[]` positions; final `RawRadii`, `HydrologicalRadii`, `SurfaceRadii`, `Heights`, `NormalizedHeights`; original frozen render ocean mask; whether any surface application occurred; pass/candidate evidence | New mesh ownership, current colors/materials, normals/bounds as needed, seafloor anchors, query helper, collider; final hydrology for milestone 1 |
| **D/B: authoritative drainage and raw basins** | Final visible/input/filtered hydrology heights; facet data and adjacency/incident/flat-routing arrays; receiver, local area, drainage area, local/accumulated runoff; raw basin floor/spill/parent/downstream/footprint facts | New mutable `GeodesicTinHydrology` working instance, diagnostics, qualified basin wrappers and lookup maps bound to that instance |
| **N/R: river networks and visual geometry** | N: ordered sources, exact double `TinPoint` samples, centrelines, joins, terminations, lengths, failure triangles and extraction evidence. R: emitted vertices/indices/colors/normals, topology mode, path/mouth metadata and visual acceptance/outcome evidence | Per-game lists/dictionaries and all Unity objects; mutable network instances; debug overlays and current display enablement |
| **G/L: lake geometry and routing** | G: qualified body records, vertices, indices and terrain-point carriers. L: adjusted reaches, outlet paths, downstream/receiver IDs, witnessed departure samples, unique parcel membership and contribution events | Rebound basin/node references, indexes and mutable parcel sets; conservation/cycle validation; runtime mesh/material/objects |

Separate **raw river networks** from **final river mesh output**. N does not depend on lake thresholds; R does, when integration is active, because lake clipping, outlet paths and added discharge change vertices and widths. Similarly, lake geometry G depends on basin eligibility, while lake routing L additionally depends on N and runoff.

Exact terrain restore is critical. Reconstructing `direction * cachedHeight`, or calling `ApplyGeodesicValleySurface()` once with final radii, does not reproduce repeated normalization and subtraction across passes. `SurfaceRadii` also is not always identical to the requested carve height. Restore captured final values directly; never reapply carving to an already carved surface. Preserve the distinction between “carving attempted” and “surface actually changed,” including zero-cut cases, because the latter controls surface queries and collider selection.

Capture successful generation output, not live references. `Vector3`, `Color`, numeric arrays and `TinPoint` are managed value data despite their Unity namespace. `GeodesicTinHydrology` itself is managed but **mutable**: `Accumulate()` replaces discharge/receivers and `ExtractNetworks()` replaces networks and diagnostics. `TinLakeFlowNode` has mutable sets/lists; adjusted reaches can alias raw sample arrays. `GeodesicTinBasins`, routing and delivery objects retain references to their source TIN/basins. Do not share these objects between games.

Later snapshot restore should use explicit constructors/factories, not reflection or general object serialization. Store relationships as scoped IDs; rebuild `byTriangle`, `wetFacets`, `nodes`, `descriptors`, `rawContributors` and `addedToNetwork` against the newly restored owner. Basin IDs are floor-vertex IDs and network IDs are list indices; neither is a globally stable identity across terrain or settings changes.

Do not retain `Mesh`, `Material`, `GameObject`, `Transform`, colliders, renderers, `MonoBehaviour`, owner delegates, chemistry arrays, temperatures, environmental fields, biology state, or a previously updated runoff instance in the cache. Existing cleanup must run before installation; create one fresh visual object set. Preserve deactivation before deferred `Destroy()` so a replacement cannot briefly render twice.

## 4. Exact key contracts and invalidation

Use explicit stage schemas and deterministic SHA-256 manifests. Prefer hashing **realized boundary inputs** after normal terrain/ocean generation. This closes over terrain noise, seed derivation, bathymetry, inland uplift, sea-level solving and ocean-connectivity settings without duplicating every upstream rule in another fragile settings list.

The following are proposed key contracts, not existing APIs:

- **Geometry identity:** ordered unit XYZ and triangle-index hashes, actual render subdivision/counts and an explicit topology algorithm version. For outputs using simulation cells, also include simulation topology identity and mapping version/content. Do not rely on array length alone.
- **C key:** stage/algorithm versions; geometry identity; exact pre-carve XYZ; pre-carve values of the five terrain arrays restored by C; resolved base radius and sea-level radius; actual initially mapped retained-ocean mask; simulation mean-cell area; effective epsilon and clamped micro-bump fraction; `hydrologyTerrainMode` and exact carve-eligibility gate; enabled carving, clamped pass count, normalized maximum depth/width and resolved minimum carve runoff; resolved river-flow threshold and nonnegative uniform runoff density; effective terrain min/max used by normalized-height updates. Record relevant terrain/bathymetry seed provenance and generation version explicitly, excluding the visual seed. The actual arrays/mask remain authoritative. Milestone 1 should only serve fresh, eligible visible/unfiltered generation; bypass already-attempted or unsupported entry paths.
- **D key:** final XYZ and ordered geometry; frozen ocean mask; resolved sea/radius; exact pre-filter hydrology-input heights; epsilon; final relief-derived bump allowance; requested effective filter strength; resolved network threshold and initial local runoff/density; TIN algorithm version and spherical/boundary modes. Keep threshold/runoff in this key while `BuildAdaptive()` includes network extraction and uses visible disagreement to select its final filtering strength. Do not assume this combined builder is threshold-independent. For non-visible hydrology, compute/hash `GeodesicHydrologyTerrain.Build()`'s actual output before lookup: this covers displacement, derived terrain seed/settings, mode and the paired raw/large-scale samples or their sampler fallback.
- **B key:** final TIN geometric/height/ocean identity plus basin-sweep version. Store raw facts and total valid area, not threshold flags. If implemented initially as a child of D it will safely over-invalidate on runoff changes; separating that later is optional. Requalify fresh `TinBasinDescriptor` wrappers using the existing exact comparisons: depth versus `minimumLakeDepth`, area versus total valid TIN area times `minimumLakeBasinAreaFraction`, plus existing escape/subsea facts. The current class embeds these flags in its constructor, so wrappers must be recreated; the existing `Build()` does not offer a requalification API yet.
- **N key:** D identity, exact local/accumulated runoff, resolved threshold, physical radius and extraction version. Preserve network ordering/tie-breaking and exact double barycentrics. No lake settings, colors or selection/debug IDs.
- **G key:** B identity plus `generateHydrologicalLakes`, actual final minimum depth and area fraction, final visible TIN/ocean identity and lake-clipping/hierarchy version. Pass the values actually consumed by `GeodesicTinBasins.Build()`; startup currently normalizes them, while direct API calls validate and may reject them.
- **L key:** G identity, ordered N identity, D runoff identity, `integrateTinLakeRivers`, resolved minimum visible outlet discharge and routing version. Preserve all outlet witnesses, unique first-lake parcel ownership, joins and contribution events. The outlet display threshold is currently the resolved river threshold.
- **R key:** final XYZ/query-field identity; N and active L identity (or explicit no-integration state); triangle-to-simulation-cell mapping and simulation retained-ocean mask for mouths; effective radius, epsilon/bump allowance; `drawRawTrianglePaths`; normalized `surfaceOffset`; for ribbons, normalized `widthAtThreshold`, `maximumWidthMultiplier`, the actual `riverFlowThreshold`/`majorRiverThreshold` expressions and `riverColor`; grade-color mode if storing final diagnostic colors; rendering algorithm version. Raw lines ignore width/major-threshold controls. `showRivers`, `enableGeodesicRivers`, layer, transform and material binding are runtime presentation gates, not reasons to invalidate geometric data.

The C key's explicit array inputs also prevent a misleading seed-only hit when custom terrain seeds or non-seed settings differ. A planet seed change normally changes realized terrain/ocean inputs and misses. When a seed change produces identical relevant inputs under custom overrides, content equality is sufficient for mathematical reuse; keeping relevant derived terrain/bathymetry seeds in the key is a conservative restriction. Never include unrelated biology or chemistry seed domains.

| Change | Invalidation consequence |
|---|---|
| Same-input regenerate / new game after menu | Reuse eligible snapshots; build fresh runtime and simulation state |
| Terrain/bathymetry seed, terrain settings, inland uplift, sea level or ocean connectivity | Recompute boundary inputs; C and downstream miss when relevant input identity changes |
| Render subdivision | C and every downstream geometric layer miss |
| Simulation subdivision | Mapping/ocean inputs and mean-cell-area thresholds change; C/D/N/L/R must be reconsidered, not just render geometry |
| Carve count/depth/width/minimum runoff | C misses, then downstream follows new final terrain |
| River flow threshold / runoff density | C misses when carving is enabled; N/L/R change; current combined adaptive D key also changes |
| Lake minimum depth/area or lake-generation toggle | C and raw D/B/N remain valid; G/L and lake-integrated R miss |
| Integration toggle | L and R change; C/D/B/N/G remain valid |
| Ribbon width/color/raw-line mode | R changes as applicable; upstream mathematical results remain valid |
| Ocean chemistry, biology or temperature-only settings | Initialize normally; no hydrology invalidation unless a future feature actually supplies generation runoff |
| Algorithm/schema version | Invalidate the affected stage and descendants |

**Fingerprint infrastructure:** `GeodesicGenerationFingerprint.cs` already supplies ordered typed parameter manifests, invariant round-trip float strings, length-prefixed binary array hashes, little-endian IEEE-754 values and SHA-256. Reuse these primitives with explicitly added stage fields. Do **not** reuse the current `terrainParametersHash`: `InitializeStages()` reflects all public planet/river fields, includes unrelated settings, `carvingAlreadyAttempted`, all seed domains, Unity/platform metadata and assembly MVID. It is diagnostic provenance, not a minimal cache contract. Add deterministic encoders for any new payload types/lengths; never use `GetHashCode()`, Unity instance IDs, rounded numbers, dictionary enumeration order or time in a cache key. Build keys even when diagnostics are disabled.

## 5. Diagnostics, conservation and lifecycle

For milestone 1, final TIN, lake routing and delivery still execute, so their mathematical guards and optional parcel capture remain unchanged. Preserve per-pass `ValleyCarvingResults` and candidate facts needed by gizmos, summaries and `CarvingEvidence`; publish restored evidence as originating from the cached generation. Do not falsely claim skipped coastal verification ran again. Keep enough data to preserve the current inspection API, or bypass caching when a requested inspection cannot be reconstructed.

For later D/L/R caching:

- `logDrainageDelivery=false` currently avoids capture, hashing and analysis; enabled generation captures endpoints only on the final TIN, and releases them after reporting. An enabled hit cannot analyze a snapshot lacking endpoints: `Analyze()` would report `ParcelEndpointNotCaptured`. Treat endpoint evidence as an optional capability sidecar, not a terrain-key field. On an absent capability, rebuild the necessary final drainage on a private instance and capture endpoints, or use an explicitly designed observation replay. Never silently report incomplete data. Always retaining endpoints would cost 12.5 MiB at L7 and change the diagnostics-off cost; it is not recommended by default for the first implementation.
- Rejected-path capture is similarly optional. If a cached N lacks that evidence, rerun extraction on the fresh working TIN for this debug request. Grade coloring/termination overlays can be derived from retained geometry/evidence; a logging-only toggle must not force carving.
- `GeodesicDrainageDelivery` is analysis state, not a reusable live object. Recompute it from current restored references and evidence. A later R hit must replay stored visual outcomes (short links, projection/centreline failures, missing trunks) or reconstruct those observations, rather than losing the final diagnostic stage.
- Retain and rerun the existing DAG/discharge and conservation checks against newly bound lake nodes. `ResolveDischarge()` mutates `ContributingParcels`, so call it on fresh sets. Validate missing IDs, witness ownership and basin-parent cycles; retain the existing failure behavior. A cached “zero errors” counter alone is not validation. Restore preserves existing unresolved topology; caching must not conceal or repair it.
- `UpdateRunoff()` must continue to operate on a per-game TIN, rebuilding networks/routing/widths normally. Never overwrite the generation snapshot with this simulation result. Future caching of nonuniform generation runoff must hash its actual ordered values and the simulation-to-triangle mapping.
- Publish entries only when their dependency chain completed successfully. On exception/cancellation do not leave a partially usable graph. Validate payload lengths, indices, finite required inputs and dependency IDs before installation; on invalid cache data evict and run the existing miss path. Distinguish valid terminal-basin sentinel values from malformed floats.

Menu return calls `SimulationStartupController.ClearRuntimePlanetState()`, including biology deinitialization and `PlanetGenerator.ClearGeneratedPlanetRuntime()`. Keep the proposed cache outside that owner cleanup; dispose/null all runtime data normally. The next game must still execute ocean/resource/temperature initialization before hydrology and biology initialization afterward. Do not shortcut `InitializeAuthoritativePlanetStages()` as a whole.

Normal Unity domain reload/process exit clears session data. Add an explicit play-session reset for the new cache when domain reload is disabled; retain it across menu transitions within that session. Manual cache-clear actions should include the appropriate hydrology descendants. No persistent Unity references may survive either reset.

Loading reports currently mark boundaries **before synchronous work**, using `PlanetGenerationProgress`, consumed by `SimulationStartupController`; synchronous callers drain the same enumerators. On a C hit, report surface preparation/restoration and then actual final routing/lake/ribbon stages. Do not emit fictitious carving passes or introduce delays to display skipped stages. Log hit/miss, restore time and bytes separately from original build timings. Existing stopwatches can span coroutine frame waits, so elapsed outer-stage duration is not pure CPU time.

## 6. Memory estimates

These are source-derived managed payload estimates, not measured process RSS. Counts are `V = 10*4^L+2`, `T = 20*4^L`. Array headers, object/list/dictionary overhead, capacity slack, GC behavior and Unity native/GPU copies add to them. Current scene defaults are simulation L6/render L7; existing logged runs used three carving passes, while the inspected serialized scene fields show one. Do not infer the selected startup configuration from scene defaults alone.

| Payload | Estimate | L6 | L7 | L8 |
|---|---|---:|---:|---:|
| C positions + five float arrays + frozen bool mask | `33*V` bytes | 1.29 MiB | **5.16 MiB** | 20.63 MiB |
| TIN retained fixed arrays, excluding shared unit geometry | approximately `164*T + 25*V` | 13.79 MiB | **55.16 MiB** | 220.63 MiB |
| Optional parcel endpoint evidence | `40*T` | 3.13 MiB | **12.50 MiB** | 50.00 MiB |
| One `GeodesicRiverTerrain` query helper | approximately `84*T + 4*V` | 6.72 MiB | **26.87 MiB** | 107.50 MiB |
| Existing unit render geometry | `12*V + 12*T` | 1.41 MiB | **5.63 MiB** | 22.50 MiB |

The TIN estimate assumes a 96-byte `Face` (two double XYZ vectors, five doubles and flags/alignment), 32-byte `TinPoint` and 40-byte `TinParcelDestination`, consistent with the delivery code's own endpoint estimate. Runtime packing should be verified during an authorized implementation. It includes adjacency, incident triangles, flat-routing data, four double triangle arrays, completed vertices, two height arrays, ocean flags and incident offsets. It excludes networks, basins, lookup dictionaries and construction scratch. Shared geometry/position/height arrays must be counted once when calculating an actual cache entry, not summed blindly across this table.

Additional costs and risks:

- Full per-pass `Result.Heights` retention adds `4*V` per pass: 0.625 MiB per L7 pass, or 1.875 MiB for three, plus candidate paths, incoming samples, dictionaries and evidence. Proposed C fixed storage is therefore about **7.03 MiB with three result height arrays**, before candidate data. A 32 MiB initial admission cap and one-entry limit are reasonable conservative policy choices; decline oversized entries and keep correct miss behavior.
- One cached TIN plus an independently mutable restored copy is already about **110 MiB at L7**, before endpoints, basins, networks, query helpers and Unity meshes. At L8 that pair is about 441 MiB. Two current query helpers can coexist after carving (generator surface and river terrain), roughly another 54 MiB at L7. Rebuild these helpers first rather than caching duplicate copies; a shared immutable query-data optimization is a separate change.
- Basin footprints overlap through the hierarchy. Storage scales with **summed membership**, not just T. `byTriangle` adds references/list/dictionary overhead. Do not assume one basin per facet.
- Lake `rawContributors` stores parcel IDs for many river facets; `ContributingParcels` repeats upstream membership across cascades. These hash sets and repeated ancestry can dominate memory. Cache compact membership arrays and rebuild fresh sets only when L caching is justified. Preserve iteration/summation behavior where it affects exact numeric outputs; do not change summation order as an incidental optimization. Do not cache every parcel's entire traced path.
- An N snapshot costs approximately `32*S + 12*C` bytes for S exact samples and C centreline points, plus network records. R costs `40*Rv + 4*Ri` for positions, normals, colors and indices, before optional query/debug data. Triangular ribbons normally emit three vertices and twelve indices per longitudinal step; raw lines are smaller. The inspected log does not give enough counts to claim a precise river payload size.
- Observed G had 17,544 vertices and 20,358 indices. Positions + `TinPoint` carriers + indices total **853,368 bytes (0.81 MiB)**; normals add 210,528 bytes. It had 59 bodies from 1,816 raw basins. Routing ancestry is additional and unmeasured.
- The observed delivery diagnostic reports approximate peak **23,369,248 bytes (22.29 MiB)**, including endpoints/owners/graph scratch. It is not all retained after analysis. Existing simulation-topology memory reporting omits the jagged `DualCorners` storage and general allocation overhead, so it is not a complete process-memory budget either.
- Capture-by-deep-copy temporarily overlaps live generation, cache payload, old entries, mesh uploads and scratch. Evict old entries before accepting a new large one; account for peak copying, not just retained bytes. Never retain initial/intermediate full TINs for every pass.

## 7. Existing timing evidence and expected benefit

Read-only evidence: `C:/Users/domin/AppData/Local/Unity/Editor/Editor.log`, approximately lines 4397–4455, seed 123456, simulation L6/render L7, threshold 2, visible/unfiltered terrain, three carving passes, raw river lines. These are pre-existing runs, not measurements made by this investigation.

| Existing logged operation | Time |
|---|---:|
| Initial parcel accumulation | 5.609 s |
| Post-pass-1 parcel accumulation | 8.238 s |
| Post-pass-2 parcel accumulation | 9.181 s |
| Final parcel accumulation | 9.293 s |
| Total of those accumulation timings | **32.321 s** |
| Final lake routing, including parcel replay | **9.565 s** |
| Final lake geometry | 0.019 s |
| Final recorded extraction/tracing | 0.397 s |
| Final ribbon/visual timer (raw-line mode here) | 2.192 s |
| Fingerprinting / delivery analysis / endpoint capture | 0.591 / 0.200 / 0.022 s |
| Outer “river drainage and ribbons” stage | **55.214 s** |

An adjacent run recorded 54.631 s. A later outer-stage entry was 131.635 s despite similar inner accumulation/routing timings; its cause is not established, so it is not a basis for promising larger savings. Outer timers may include frame waits or other elapsed pauses. The TIN summary's literal `lakeRouting=OFF` is stale wording: the separate routing log proves 59 lakes were routed. `GenerationMilliseconds` and per-TIN timings do not sum the complete multi-pass stage; extraction is also performed more than once. Avoid double-counting inclusive timers.

The older `Docs/TinHydrology.md` reconstructed L7 fixture reported 7.330 s accumulation, 0.062 s adjacency, 0.188 s gradients and 0.075 s extraction. It did not measure native ribbons and used a different terrain fixture; it corroborates accumulation cost but should not replace the recent log.

**Expected, unbenchmarked benefits:**

- C-only hits skip the initial and first two post-pass TINs in this three-pass example: **23.028 seconds of logged accumulation alone**, plus their extraction/basin work and all carving/proposal verification. They still pay the final roughly 9.3-second accumulation, 9.6-second lake routing, visual work, diagnostics and restoration. A working estimate is **roughly 25–33 seconds saved out of the 55-second hydrology stage**, with a remaining stage in the low-to-high twenties; this is an inference, not a guarantee. One-pass worlds save less; carving-disabled worlds receive no C-only benefit.
- Adding D/N/B snapshots can remove the remaining final accumulation/build/extraction/sweep work, with copying/index construction/validation costs. The known additional accumulation opportunity is about **9.3 seconds**.
- Adding L snapshots can avoid the approximately **9.6-second** parcel replay and outlet reconstruction, while still restoring fresh sets/references and running conservation/cycle checks.
- Adding R snapshots targets the approximately **2.2-second** visual interval, but mesh creation/upload and debug/diagnostic restoration remain. G-only reuse saves milliseconds in this example.
- Full hits could remove most of this hydrology CPU work; “instant generation” is unsupported. Initial terrain/ocean/environment setup, Unity uploads/collider cooking, validation and biology remain outside the cache.

## 8. Session memory versus disk

| Session memory | Versioned persistent disk |
|---|---|
| Simplest first implementation; no serialization/migration/I/O path | Useful across application/domain reloads |
| Natural same-runtime floating-point compatibility | Requires explicit algorithm/schema and compatible numeric-runtime version policy; bitwise equality across Unity/platform changes cannot be assumed |
| One-entry byte cap bounds retained cost | Needs disk quota, eviction, integrity checks, length/allocation bounds, atomic temporary-write/replace, corruption fallback and migration/invalidation policy |
| Menu return can reuse data; domain reload intentionally misses | Must reconstruct all runtime references and validate every restored relationship |
| Deep copies isolate live mutation | Serialization does not solve aliasing/mutability after load |

Do not expand the existing legacy `PlanetGenerationCache` format to hold a serialized live hydrology graph. If persistence is later approved, give these stage payloads their own versioned format and deterministic keys; write integer/float bits explicitly and store checksums/dependency identities. Keep build compatibility separate from incidental assembly MVID. Never persist chemistry, simulation state or Unity instance references as part of generation acceleration.

## 9. Narrow first-milestone implementation plan — for later approval

1. Add a dedicated session cache and explicit C-key builder in the geodesic hydrology area. One entry, 32 MiB admission cap including retained evidence, explicit clear/reset, optional disable/bypass, immutable private storage and copy-on-restore. Cache only fresh eligible visible/unfiltered carving initially.
2. Add capture/restore methods beside `PlanetGenerator.GeodesicRenderTerrainData` and `ApplyGeodesicValleySurface()`. Capture exact positions and the five changed arrays; retain unchanged mountain masks/inland diagnostics from freshly generated input. Restore surface-change status, current colors/normals/bounds/query helper/anchors/maximum radius and preserve collider behavior. Do not modify the carve planner.
3. In `GeodesicRiverSystem.InitializeCoreStages()`, resolve the original ocean mask and C key before the expensive initial TIN. On a hit install C and execute exactly one normal final `InitializeRenderHydrologyStages()` with the frozen mask. On a miss keep the existing pass order and calculations; capture the final surface/evidence and publish only after successful completion. Restore the pass-result inspection contract with private copies; avoid retaining planner scratch unnecessarily.
4. Keep final `GeodesicTinHydrology`, `GeodesicTinBasins`, `GeodesicTinLakeGeometry`, `GeodesicTinLakeRouting`, river generation and delivery algorithms unchanged. Emit truthful loading boundaries and separate cache diagnostics. Keep menu/environment/biology lifecycle intact.
5. In a separately authorized validation phase, compare hit/miss bitwise positions and all paired heights, TIN receivers/runoff/samples/joins/terminations, qualified basin/body identities, outlets and unique parcel membership, mesh buffers, mouths, collider/query agreement, delivery residuals and cycle checks. Include zero-cut/multi-pass cases, changed lake thresholds, seed/subdivision/carve/river controls, diagnostics off→on, runoff mutation followed by a new game, menu return, failure, explicit clear and domain reload. This investigation has **not executed** these checks.
6. Review measured savings and peak memory before adding D/B/N snapshots. Their exact export/import and pre-render capture boundaries are the next milestone. Add L/R only with explicit restoration validation and bounded ancestry storage. Disk persistence is a separate later decision.

Primary production touchpoints for that first milestone are `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs`, `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverSystem.cs`, and new cache/key/snapshot files alongside `GeodesicGenerationFingerprint.cs`. `PlanetGenerationProgress.cs` and the startup loading view/controller need only compatible stage reporting/lifecycle integration if existing stages suffice. Later stages would add explicit snapshot APIs to `GeodesicTinHydrology.cs`, `GeodesicTinBasins.cs`, `GeodesicTinLakeGeometry.cs` and `GeodesicTinLakeRouting.cs`, with delivery evidence handled in `GeodesicDrainageDelivery.cs`. No hydrology mathematics or carving algorithm changes are required by this proposal.

Investigation complete. Implementation is stopped for review.
