# Continuous TIN river drainage: implementation and validation

2026-10-11. Branch: `codex/implement-geodesic-river-drainage`.

**VISUAL ACCEPTANCE PENDING.** The Unity Editor started but exited with “No valid Unity Editor license found” and missing `com.unity.editor.headless` entitlement. No licensed seed-123456 screenshot was produced or inspected. Native Unity integration tests and actual ribbon rendering remain unverified. The managed results below establish algorithm behavior; they do not establish the requested planet appearance.

## Active routing and preserved references

`GeodesicRiverSystem.Initialize` now invokes continuous TIN hydrology. The former active L7 path used `GeodesicDrainageGraph` priority-filled **vertex receivers**, a half-strength vertex filter, and `GeodesicVisibleHydrology.InteriorPath` to alter the appearance of already-selected receiver edges. It also rendered reaches with visible uphill diagnostics. Those receivers and interior-point adjustments no longer choose active river paths.

The old files, inactive L6 reference initializer, existing lake geometry implementation, inland-sea/ocean mapping, and historical commits remain available. Commit `1a2b73a` is the previous L7 method; a local reference branch `reference/l7-vertex-rivers-1a2b73a` preserves it. Historical `6f48085` remains in the branch history. No lake basin rendering code was changed.

Simulation remains L6 and rendering remains L7. The new tracer shares the existing render geometry's vertex and triangle arrays. It builds three-neighbor triangle adjacency and an incident triangle fan for each vertex. The existing lightweight vertex adjacency is used for retained-ocean mapping, never to select geometric river receivers. It does not build an additional full `GeodesicGridTopology`.

## Authoritative height and terrain agreement

The source is the completed `surfaceMesh.vertices`, after the planet's terrain and inland-sea/bathymetry processing. Visible vertex heights are their radial magnitudes. The authoritative `HydrologyHeight` is a separate array, with linear interpolation inside each render facet.

The default filter strength is **zero**, so the initial debug field is the completed terrain. An optional single neighbor pass is capped at 0.1 strength. Its mean and maximum changes are measured. If tracing or joining encounters a material visible rise, the entire filtered field, accumulation, and networks are recomputed with half the filtering strength, eventually returning to raw heights. Epsilon is never increased during this retry.

Hydrological epsilon is capped at `min(2e-6, planetRadius * 2.5e-7)` local units, and may be configured smaller. A separate visible micro bump allowance is `completedTerrainRelief * visibleMicroBumpFraction`, with a default fraction of 0.001 and maximum 0.002. The tracer evaluates both visible altitude and the exact radial intersection of the completed facet. It includes the analytic interior radial minimum, so a bump between uniform samples cannot silently pass.

Every accepted step checks the start, endpoint, and running minimum before adding geometry. This enforces `H(next) <= H(current) + epsilon` and also prevents an epsilon staircase across a plateau. Material visible rises reject the candidate. No filled valleys, terrain edits, lakes, fake connections, or lake outlets are created. Tiny depressions currently stop as classified sinks; no micro-pit conditioning or escape surface is implemented in this stage.

## Gradients, tracing, and flats

For each triangle, the two local edge vectors define the facet plane. Solving their 2-by-2 Gram system fits the linear height plane and yields `-gradient(height)` and its three barycentric rates. On the sphere this is the local chord plane of the existing TIN. Normalization maps its path to the same spherical facet and preserves the interpolated height ordering.

The tracer follows that gradient from the entry point to the exact edge intersection. It transfers the same point into a incident facet and recalculates the feasible direction. It does not nudge across an uphill boundary. When both facets slope toward a valley edge, it follows the constrained downhill edge direction. At a vertex, it examines the incident fan for a feasible interior descent. Ordinary triangle interiors are crossed directly; centroid-to-centroid lines are never drawn.

Flat facets use deterministic breadth-first distance to a real downhill or coast exit. If an outlet is unavailable, they stop as `FlatRequiringRouting`. A level valley gutter between non-flat facets is also classified as an unresolved flat rather than a closed bowl; constrained gutter flat routing is not implemented. Other minima are classified as `TrueClosedBasin` or `TinyNoiseDepression` using the material one-ring barrier. This classification does not calculate global basin spill depth or construct lake basins. Numerical, visibility, and extraction failures remain explicit `RoutingFailure` cases.

## Triangle drainage, runoff, and whole networks

Each land facet contributes its physical spherical area, with uniform runoff per unit area. The configured threshold retains the L6 mean-cell-area conversion. At radius 8 and configured threshold 8, it is **0.157071963 local area units**, approximately 64 mean L7 triangle areas, equivalent to the previous 8 mean L6 cells. Increasing hydrology resolution therefore does not lower the physical threshold.

A triangle's entry position can change its exit edge. For that reason, accumulation traces each triangle's area parcel through the continuous field and counts its contribution once in each visited facet. Parcels stop exactly where their accepted route stops. `TriangleReceiver` records a representative first transfer for threshold-source connectivity and diagnostics; it is not used to draw the path or force all incoming water through the centroid's exit.

Visible sources initiate whole downstream traces. Visibility continues to their termination rather than being suppressed reach by reach. Ribbon width retains the maximum encountered discharge downstream. A tributary joins an existing channel only when a continuous accepted downhill connector can be built inside their common facet, with an exact existing junction sample. The entire downstream trunk is revalidated against the tributary's minimum before accepting the join. Rejected joins retain the gradient route.

An active representative-connectivity component without a valid visible source remains undrawn and is recorded by triangle id as a routing failure. This avoids restarting an independent downstream reach to conceal a break. The managed fixture has six such ambiguities, which need inspection.

## Ocean authority and post-processing

The original retained L6 OceanMask, inland-sea exclusions, and L7 submerged connectivity remain authoritative for ocean identity. Shoreline termination intersects the actual completed facet with the sea sphere. If ocean authority is acquired at an already-submerged shared edge, the same continuous path is clipped back to its exact shoreline crossing. The mask is not expanded into an excluded inland basin. Mouth metadata references retained L6 ocean cells.

Raw float centrelines, optional interpolation, and projected centrelines are checked at **every crossed facet**, including analytic radial extrema. Rejected interpolation keeps the raw path. If even the raw representation fails, the network is classified as a failure and no downstream continuation is invented. No Catmull-Rom or other spline is applied.

Thin raw gradient lines are the default output (`drawRawTrianglePaths = true`). Ribbons use three columns: independently projected banks plus explicit vertices on the checked centreline. Projecting banks cannot shift the centreline to a new route. All drainage work occurs during initialization or explicit rebuild/runoff calls; there is no per-frame drainage callback.

## Verification results

The Linux managed runner compiled the actual hydrology sources against the installed Unity assemblies and executed **84 tests: 84 passed, 0 failed**. These comprise 19 new TIN tests and 65 preserved algorithm tests. The river component also passed a compilation-only owner-API contract check; this is not a full native Unity project compilation.

All required terrain fixtures pass: analytic plane, V valley, ridge, saddle, winding valley, closed bowl, and tiny numerical flat. Additional checks cover flat-to-gradient continuity, unresolved gutter classification, visible hill rejection before adding a segment, no runoff propagation across a rejected continuation, exact confluence samples, spherical area conservation, coast intersection, projection, rejected smoothing fallback, and reduction of filtering without changing epsilon.

The winding-valley invariance test uses a refined 96-by-96 grid, flipped diagonals, and a 0.47-radian rotation in world coordinates. Maximum measured path deviations are **0.0294628** for diagonal flips and **0.0365568** for rotation, below the 0.14 fixture-unit limit. The coarser representation produced actual local minima; the tracer stopped at those minima rather than crossing their barriers.

Reproduce the managed checks:

```sh
python3 Tools/ValidateTinHydrology.py --unity-data /path/to/Unity/Editor/Data --benchmark 7
```

The seed-123456 benchmark is an explicitly **reconstructed managed fixture**: radius 8, Earthlike deterministic sampler with direct seed 123456, retained ocean components, L7 render geometry, and L6 threshold conversion. It does not reproduce every serialized scene setting, derived seed, bathymetry edit, or the final native Unity scene.

| Fixture measurement | Result |
| --- | ---: |
| Render vertices / hydrology triangles | 163842 / 327680 |
| Active river triangles | 1009 |
| Networks / sources | 424 / 424 |
| Confluences | 65 |
| Ocean terminations | 23 |
| Closed-minimum terminations | 315 |
| Tiny noise depression / unresolved flat terminations | 19 / 2 |
| Unexpected connectivity components / rendered inland breaks | 6 / 0 |
| Hydrology downhill / flat / uphill segments | 1574 / 13 / **0** |
| Completed-visible downhill / minor bump / material uphill segments | 1287 / 300 / **0** |
| Visible ridge crossings | **0** |
| Raw / projected paths passing all guards | 424 / 424 |
| Maximum hydrological uphill excursion after float conversion | 1.34558649e-8 |
| Maximum completed-visible micro excursion | 0.00010956173 |
| Mean / maximum network length, local units | 0.135187 / 0.962988 |
| Additional full simulation topology builds | **0** |

The fixture is heavily basin-terminated and its mean network length is short. It does **not** establish the requested long, recognizable, continuous drainage trees, fewer fragments than the historical implementations, or their visual absence of geodesic patterns. Those remain acceptance criteria for the actual licensed capture. Closed minima were not bridged to improve these numbers.

Measured managed costs on this run: height filtering **8.61 ms**, adjacency **62.44 ms**, triangle gradients/visible planes **188.06 ms**, flat topology **1.65 ms**, parcel accumulation **7330.46 ms**, network extraction/tracing **74.59 ms**, total reconstructed generation and validation **11857.39 ms**. Representative receiver discovery occurs during parcel tracing. Native ribbon generation was not executed; its benchmark time is zero, not a native performance measurement. Runtime capture logs will record actual ribbon and total generation cost.

## Licensed seed-123456 acceptance

Generate the actual scene with simulation L6, render L7, rivers on, and lakes off, using the same settings and camera for all views.

1. Capture the old vertex result from preserved commit `1a2b73a` as comparison A.
2. On the new branch, keep raw triangle paths enabled and refresh river visuals. Use **Tools > Hydrology > Capture Raw TIN Rivers (seed 123456)** for B. Inspect slope agreement, closed minima, the six connectivity ambiguities, tributary joins, and path continuity.
3. Only after inspecting and accepting B, disable raw paths and refresh visuals. Use **Tools > Hydrology > Capture TIN Ribbons After Raw Acceptance (seed 123456)** for C.

The new capture menus save the actual camera image, settings, complete diagnostics, classified terminations, and unrouted triangle ids under `Artifacts/TinHydrology/`. No A, B, or C licensed image was captured here. **VISUAL ACCEPTANCE PENDING. Lakes remain OFF.**
