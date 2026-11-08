# Static TIN river/lake integration

`GeodesicTinLakeRouting` inserts lake nodes between continuous TIN reaches. It owns a sidecar of adjusted paths; it does not rewrite `TriangleReceiver`, extracted sources, raw `TinRiverNetwork` samples, terrain, gradients, runoff, ocean authority, or lake geometry. `GeodesicRiverSystem.integrateTinLakeRivers` is an A/B inspection switch. Lakes OFF bypasses the sidecar entirely. Only bodies actually emitted by `GeodesicTinLakeGeometry` participate, including its hierarchy suppression and ocean exclusions.

## Entry and continuation

Each emitted body's terrain-point carriers identify its wet facets. A path segment in a facet enters water where its linear TIN scalar height crosses that body's spill elevation. Barycentric interpolation produces the exact endpoint, including partial-facet shorelines. A source already inside water emits no inlet ribbon. Touching the rim without entering is not an inlet. Inlet ribbons end at the crossing, with their final banks collapsed to the shoreline point.

An outlet starts at the descriptor's exact `TinPoint` spill witness. The first normal TIN descent is restricted to the dry downstream side of that lake; it is not moved to a centroid or nudged over the saddle. Remaining steps use the existing continuous gradient, flat routing, coast intersection and grade constraints. An exact, validated same-facet connection can join an existing dry channel. Its downstream lake is resolved through the original confluence links. Otherwise the outlet follows TIN tracing to the next rendered lake, ocean, or an existing termination. A node without a valid departing path holds its flow and creates no outlet.

At most one outlet reach is published per body. The existing discharge threshold determines its visibility, and existing material/width rules use the resolved discharge. A subthreshold outlet still transfers hydrological contribution downstream. Raw source selection and thresholds are unchanged.

## Runoff accounting

The current accumulator sends one parcel from each dry facet's centroid. Its amount is that facet's local runoff (unit density times area by default). A parcel contributes once to every visited facet. Consequently, cumulative values at two inlet facets can overlap, and summing them is incorrect.

Integration replays those same continuous traces without recording diagnostics or changing their data. The first rendered lake encountered owns that source parcel directly. A centroid already under lake water contributes locally to that lake. Source triangle IDs identify parcels. Lake-to-lake transfers union these IDs in deterministic topological order, then sum each parcel's original amount once. Per-inlet cumulative values remain useful observations, but do not determine the outlet sum. The lake DAG rejects cycles.

When an outlet joins an existing raw channel, its parcel set is added from the exact junction onward. Width computation unions that set with the original channel ancestry, so shared runoff is not added twice. The same parcel identity carries through cascades. Per-node checks compare resolved discharge with its unique authoritative contributors; a global check compares all first-lake parcels with discharge delivered outside the lake graph or held at its terminal nodes.

This is static transport at existing spill levels. There is no filling simulation, changing level, evaporation, precipitation, groundwater, erosion or sediment model.

## Validation and capture

`GeodesicTinLakeRoutingTests` covers exact entry/spill geometry, multiple tributaries and one outlet, independent and overlapping contributors, terminal storage, cycle rejection, a geometrically traced two-lake cascade, misses, excluded bodies, unchanged raw state and deterministic output. The existing shared coastal-vertex regression is retained. The four reference unmatched endpoints must continue to be excluded from freshwater topology.

Run managed checks with `Tools/ValidateTinHydrology.py` and the installed Unity Data directory. The broader suite has a previously reproduced failure in `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted` (expected 13, actual 0); this milestone does not change that test or the historical grade planner.

Use **Tools > Hydrology > Capture TIN River Lake Integration (actual planet)** in the running reference scene, or create `Library/TinLakeRoutingCapture.request`. It requires seed 123456, sim 6/render 7, Visible input, river threshold 2, lakes ON, depth .045 and area 0. The capture toggles integration only, preserves the lake meshes, verifies raw hydrology/terrain/ocean/lake fingerprints and untouched paths, and restores the integration setting, time scale and main camera. Output goes under ignored `Artifacts/TinLakeRouting/<timestamp>/`.

Live measurements and image review are recorded after the reference capture completes.
