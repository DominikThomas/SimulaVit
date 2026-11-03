# TIN valley carving and final lakes

Generation builds initial TIN drainage and raw basin topology, then repeats carving and
drainage/topology rebuilding for the configured number of passes. Even a pass accepting
no cuts is followed by a rebuild. Only the rebuild after the final carving pass applies
the configured lake area/depth thresholds, builds lake geometry, integrates river/lake
routing, and constructs runtime river and lake visuals.

Intermediate passes retain `GeodesicTinBasins`, including basin floors, spills, parents,
and downstream witnesses. The existing detector does not filter topology by lake
thresholds. The lifecycle uses zero minimum area and an unreachable finite minimum
depth (`double.MaxValue`) to defer all visible-lake eligibility during these builds;
saved lake settings are never modified. Intermediate qualification/depth flags are
therefore not physical lake classifications. The final rebuild uses the saved thresholds
when lake generation is enabled. With lakes disabled, topology remains available but
unqualified and no lake geometry or routing is created.

The carving planner receives raw topology and null lake geometry/routing. Prospective
lakes cannot veto erosion or become lake-outlet targets. Maximum cut depth, topology
validity, conflict checks, and sea-level safeguards remain unchanged. In particular,
excluded inland-sea vertices and their one-ring remain protected against lowering to
protect facet interiors; this protection is independent of future rendered lakes.
The retained-ocean mask remains frozen across the existing carving sequence.

Each accepted surface update still uses `ApplyGeodesicValleySurface`, and every rebuild
reads the resulting visible mesh. Carving still requires visible-terrain hydrology with
zero TIN filtering. Basin detection, carving, lake geometry, and river routing algorithms
are unchanged. Intermediate builds use the raw networks already extracted by
`BuildAdaptive`; they do not call the visual rebuild or create temporary river ribbons,
raw river line GameObjects, lake meshes, or debug river visuals.

Logs identify raw intermediate rebuilds with `basinTopology=intermediate-carving` and
`lakeConstruction=deferred`, and include raw basin counts before and after each pass.
The last carving result is logged before final lake construction. Final topology and
lake summaries carry `lakeConstruction=final`; `[GeodesicTinLakes]` qualification and
rendering summaries occur only then. If lake generation is off, the final topology log
instead reports `lakeConstruction=disabled`.

This checkout has a generic loading overlay, without generation-stage status callbacks.
No loading delays or new status system are introduced by this change.

Manual Unity acceptance remains pending: lower lake thresholds in the UI, verify that
all carving inputs have raw basins and zero rendered lakes, and inspect the surviving
final lakes and their river inlets/outlets. No automated tests, benchmarks, headless
validation, or visual sweeps are part of this change.
