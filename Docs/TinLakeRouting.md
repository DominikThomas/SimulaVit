# Static TIN river/lake integration

> **Original static visible-lake sidecar contract.** Later all-lakes routing, sediment-filled crossings, verified delivery and staged snapshots extend participation beyond only visibly emitted lakes. Consult [sediment channels](SedimentRiverMeanders.md), the [cache investigation/design history](GeodesicHydrologyCachingInvestigation.md), [presentation cache](GeodesicRiverPresentationCache.md) and current `GeodesicTinLakeRouting` source. Original captured results below remain checkpoint evidence; current priorities are in the [roadmap](GEODESIC_MIGRATION_ROADMAP.md).


`GeodesicTinLakeRouting` inserts lake nodes between continuous TIN reaches. It owns a sidecar of adjusted paths; it does not rewrite `TriangleReceiver`, extracted sources, raw `TinRiverNetwork` samples, terrain, gradients, runoff, ocean authority, or lake geometry. `GeodesicRiverSystem.integrateTinLakeRivers` is an A/B inspection switch. Lakes OFF bypasses the sidecar entirely. Only bodies actually emitted by `GeodesicTinLakeGeometry` participate, including its hierarchy suppression and ocean exclusions.

## Entry and continuation

Each emitted body's terrain-point carriers identify its wet facets. A path segment in a facet enters water where its linear TIN scalar height crosses that body's spill elevation. Barycentric interpolation produces the exact endpoint, including partial-facet shorelines. A source already inside water emits no inlet ribbon. Touching the rim without entering is not an inlet. Inlet ribbons end at the crossing, with their final banks collapsed to the shoreline point.

An outlet starts at the descriptor's exact `TinPoint` spill witness. The first normal TIN descent uses the recorded downstream facet and a midpoint outside the source lake. Remaining steps use the continuous gradient, flat routing, coast intersection and grade constraints. An exact, validated same-facet connection can join an existing dry channel. Its downstream lake is resolved through the original confluence links. Otherwise the outlet follows TIN tracing to the next routing lake, ocean, or an existing termination. An unresolved overflowing outlet throws unless the tracer proves an ocean contact at the spill. A terminal basin can hold its flow.

At most one outlet reach is published per body. The existing discharge threshold determines its visibility, and existing material/width rules use the resolved discharge. A subthreshold outlet still transfers hydrological contribution downstream. Raw source selection and thresholds are unchanged.

## Runoff accounting

The current accumulator sends one parcel from each dry facet's centroid. Its amount is that facet's local runoff (unit density times area by default). A parcel contributes once to every visited facet. Consequently, cumulative values at two inlet facets can overlap, and summing them is incorrect.

Integration replays those same continuous traces without recording diagnostics or changing their data. The first rendered lake encountered owns that source parcel directly. A centroid already under lake water contributes locally to that lake. Source triangle IDs identify parcels. Lake-to-lake transfers union these IDs in deterministic topological order, then sum each parcel's original amount once. Per-inlet cumulative values remain useful observations, but do not determine the outlet sum. The lake DAG rejects cycles.

When an outlet joins an existing raw channel, its parcel set is added from the exact junction onward. Width computation unions that set with the original channel ancestry, so shared runoff is not added twice. The same parcel identity carries through cascades. Per-node checks compare resolved discharge with its unique authoritative contributors; a global check compares all first-lake parcels with discharge delivered outside the lake graph or held at its terminal nodes.

This is static transport at existing spill levels. There is no filling simulation, changing level, evaporation, precipitation, groundwater, erosion or sediment model.

## Validation and capture

`GeodesicTinLakeRoutingTests` covers exact entry/spill geometry, multiple tributaries and one outlet, independent and overlapping contributors, terminal storage, cycle rejection, a geometrically traced two-lake cascade, misses, excluded bodies, unchanged raw state and deterministic output. The existing shared coastal-vertex regression is retained. The four reference unmatched endpoints must continue to be excluded from freshwater topology.

### Unresolved lake outlet capture

On a spherical TIN, scalar hydrology and visible radii are linear in the direction-facet barycentrics. The actual terrain radius is measured at the intersection of the corresponding ray with the completed planar facet. The resulting radial minimum can occur inside the segment even when its scalar heights decrease. The existing grade check uses that minimum and the route's running low. The downstream witness supplies lower-star topology, while this check tests the geometry of a candidate continuous path.

In the Editor, an unresolved spill emits `[GeodesicOutletTrace]` before throwing. The trace records world settings, lake eligibility, the spill fan and witness, candidate and selected transitions, their geometry and receiving ownership, and the failure reason. The bounded replay uses the original routing rules with diagnostic state updates disabled. Run the four generated-world EditMode cases listed in the maintenance report and save the XML or full matching Console messages. [Manual outlet capture](GeodesicTestSuiteMaintenance.md#manual-outlet-capture)

### Historical reference capture

Run managed checks with `Tools/ValidateTinHydrology.py` and the installed Unity Data directory. The broader suite has a previously reproduced failure in `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted` (expected 13, actual 0); this milestone does not change that test or the historical grade planner.

Use **Tools > Hydrology > Capture TIN River Lake Integration (actual planet)** in the running reference scene, or create `Library/TinLakeRoutingCapture.request`. It requires seed 123456, sim 6/render 7, Visible input, river threshold 2, lakes ON, depth .045 and area 0. The capture toggles integration only, preserves the lake meshes, verifies raw hydrology/terrain/ocean/lake fingerprints and untouched paths, and restores the integration setting, time scale and main camera. Output goes under ignored `Artifacts/TinLakeRouting/<timestamp>/`.

Live measurements and image review are recorded after the reference capture completes.
