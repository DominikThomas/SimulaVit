# Static TIN lake surfaces

`GeodesicTinLakeGeometry` consumes only the current `GeodesicTinHydrology` and `GeodesicTinBasins`. The active render path does not call historical graph-based lake generation, routing, or receiver rewrites. Historical code remains available unchanged.

## Geometry

Each qualifying overflowing descriptor uses its exact `SpillElevation`. Each conservative footprint facet is clipped against the linear scalar condition `HydrologyHeight <= SpillElevation`. One wet corner yields a triangle; two yield a quad; fully submerged facets remain triangles; dry and zero-depth rim facets emit nothing. Quad fans and basin iteration are deterministic. Shared-edge crossings interpolate in canonical vertex-ID order, including exact endpoint-on-level cases.

For spherical TINs, barycentric directions are normalized and multiplied by the spill radius. Every vertex of a body has the same radial elevation; interiors use the ordinary planar triangles of the render mesh, approximating the spherical equipotential at render resolution. **Radial offset is zero.** The existing `GeodesicLakeURP` shader's small polygon depth bias (`Offset -1, -1`) handles coincident shoreline depth. Water never copies the terrain heights. Shorelines are exact for the hydrological scalar TIN; the completed radial terrain's chord geometry can differ slightly inside a facet.

Qualifying ancestors at an equal/higher spill own their qualifying descendants' submerged footprints. Such child meshes are suppressed without deleting their topology. A child whose ancestors do not qualify may render independently at its own spill. The merge hierarchy, not facet visitation order or river networks, determines ownership. Distinct components may share dry triangle corners or shoreline points without overlapping positive-area water regions.

Terminal basins with unknown levels and subsea-unclassified basins do not render. No freshwater triangle is emitted from a facet intersecting retained ocean. This coast guard computes the exact minimum radius of the completed physical facet over vertices, edges and interior, using the active retained-ocean flag. It conservatively skips a whole intersecting coastal facet rather than inventing a second ocean boundary. It does not alter ocean masks. The live reference skipped zero facets for this reason.

## Runtime integration

The existing **Hydrological lakes** setting controls the new `Geodesic TIN Lakes` runtime child. OFF creates no topology or water mesh through this rendering path. ON uses the configured depth and area settings; 0.045 is not a runtime default. The material reuses the existing translucent blue lake shader. Debug overlays remain separate.

`GeodesicRiverSystem.RefreshTinLakeVisuals()` rebuilds only lake objects and has its own context action. Initialization invokes it after existing river generation. The normal river refresh also refreshes water afterward. The lake-only refresh never extracts networks, modifies receivers, accumulates runoff, clips river paths, or creates outlets. Disable/destroy/reinitialize releases the lake mesh, object and material. Shader bias is the only visual offset.

The historical `HydrologicalLakesPausedForBaselineValidation` flag still applies only to historical graph-dependent code. TIN diagnostic text now says `lakeRouting=OFF`, independently of the water-rendering setting.

## Live capture

Run **Tools > Hydrology > Capture Static TIN Lakes (actual planet)** on the initialized normal scene. A queued `Library/TinLakesCapture.request` also runs once an actual initialized TIN is available; it never generates a substitute planet. It validates seed 123456, simulation 6, render 7, Visible, river threshold 2, minimum depth 0.045 and area 0 before capture. These are capture-reference assertions, not hard-coded rendering defaults.

The capture writes ignored `Artifacts/TinLakes/<timestamp>/` reports, exact current scene settings and images: same-camera OFF/ON, whole planet, three large/irregular-shoreline close-ups, same-camera close-up OFF/ON, topology/water comparisons and spill close-ups. It temporarily toggles only the lake setting/objects, restores the original setting, and compares hashes of terrain and river meshes, active heights, adjacency/receivers, areas/runoff, source/network/path/termination state and sampled ocean classifications. The main camera is unchanged. It records all unmatched closed terminations, exact endpoint vertices and incident retained-ocean classifications.

## Tests

Ten geometry tests cover triangular/quad clipping, full/dry facets, exact spill endpoints, uniform spherical radius, deterministic mesh/index order, qualifying-ancestor suppression, threshold/terminal/subsea exclusion, ocean separation, unchanged hydrology/network state and the mixed-coast carrier classification case. The managed suite reports 122 passed and the previously reproduced baseline failure `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted` (expected 13, got 0). Neither that assertion nor routing was changed.

## Live results

Initial actual-scene capture: 1162 raw basins, 67 qualifying descriptors, 65 rendered water bodies after suppressing two covered children. The geometry has 7674 clipped polygons, 24697 vertices and 9349 triangles; total approximate water area 14.656768 and largest body area 1.52120728, in planet-local units. No terminal basins, 237 subsea-unclassified descriptors, no emitted ocean-intersecting facets. Depth 0.045 and area 0 were preserved. The initial capture's before/after terrain/hydrology/river hashes matched exactly.

Final screenshot paths, unmatched-term explanation and visual acceptance are recorded in the task report after inspecting the final capture. The first capture exposed a capture-only deferred-destruction lookup issue; final close-up OFF/ON images use the current lake root reference rather than finding an object by name.

Final static validation completed on the actual scene in `Artifacts/TinLakes/20261029-153932/` (18 PNGs and `report.txt`). The corrected OFF/ON close-ups show the water switching cleanly while the exact observed river/hydrology/terrain hash remains equal. Settings are restored. Inspection of the three largest bodies, the footprint comparison and spill close-up found level blue surfaces, shorelines cutting across TIN facets, no obvious stacked child surfaces, and unchanged rivers visible through water as expected before integration.

The four unmatched terminations are a shared coastal-vertex authority case, not missing freshwater basins:

| Networks | Endpoint vertex | Endpoint elevation | Incident facets classifying the same point as ocean |
| --- | ---: | ---: | --- |
| 756, 1030, 1373 | 103741 | 8.0871706008911133 | 166353, 166354, 166355 |
| 2297 | 142732 | 8.0673370361328125 | 270441, 270443 |

Their current trace facets classify the endpoints as dry, whereas another incident facet grants retained-ocean authority. The basin sweep correctly treats a vertex as external when any incident facet identifies it as retained ocean. Both endpoints are strict local minima (minimum neighboring elevation differences 0.00630283356 and 0.00163650513 respectively), so this is not an epsilon/noise explanation. No freshwater basin or surface should be invented there. `SharedCoastVertexCanBeDryInTraceCarrierButOceanInAnotherIncidentFacet` reproduces this classification asymmetry. Routing, ocean classification and basin detection were left unchanged.
