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
validity and conflict checks remain authoritative. Ordinary carving protects excluded
inland-sea vertices and their one-ring. The bounded coastal exception described below
can relax that protection only with a verified retained-ocean connection.
The retained-ocean mask remains frozen across the existing carving sequence.

Each accepted surface update still uses `ApplyGeodesicValleySurface`, and every rebuild
reads the resulting visible mesh. Carving still requires visible-terrain hydrology with
zero TIN filtering. Basin detection, lake geometry, and river routing algorithms
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

## Bounded coastal breaches

A spill-directed proposal rejected by the ordinary `below-sea-level` guard can retry
with a shallow coastal exception. It still follows its basin's exact spill and recorded
downstream witness. Unmatched/invalid-spill fallback searches keep their original sea
protection. The candidate origin must be within eight mesh edges of a retained-ocean
vertex; this is only a search bound, never proof of connectivity.

The entire proposed centreline is limited to eight of the origin's longest local edge
lengths (and the existing shorter distance limits still apply). Edited lateral margins
must stay within that radius and coastal band too. Retained-ocean vertices are never
lowered. Non-ocean edits must remain above a sea-relative floor derived from
`min(maximumValleyCarveDepth, 0.005)`, with the existing facet-sagitta and numerical
clearance included. Deep inland depressions cannot use the exception to excavate a
seabed. This first implementation needs no new setting or UI control.

The existing maximum incision is per pass; there was no independent cumulative-depth
setting. Its implied total bound remains `passes * maximumValleyCarveDepth`. Proposals
within a pass still combine by minimum height, not addition. The coastal submergence
ceiling is absolute relative to sea level and does not increase with pass count.

After existing profile/conflict checks, a temporary TIN is built from the combined
proposed visible surface and the same frozen ocean mask. It reproduces the actual float
mesh projection and final rebuild's height sampling and bump tolerance. It performs
only topology construction and observational traces, with no runoff accumulation or
network extraction. Every previously closed incoming river source affected by a coastal
proposal must now trace to `OceanCoast`. On this closed sphere, with boundary outflow
disabled, that requires encountering the retained-ocean mask; submerged inland terrain
alone cannot pass. Failed coastal proposals are removed and remaining proposals are
rechecked on the resulting combined surface before commit.

Logs report `coastalAttempted`, `coastalAccepted`, `coastalRejectedNoOceanConnection`,
`coastalRejectedDepthOrDistance`, and `coastalMaxSubmergence`. After each authoritative
rebuild, `coastalVerification=authoritative-rebuild` reports the number of distinct former
closed river sources checked, resolved to retained ocean, and still unresolved. These
are river-termination counts, not merely accepted candidate or basin counts.

The frozen ocean classification, final lake qualification/routing lifecycle and runoff
conservation guards are unchanged. No flooded-valley water surface or fjord geometry is
created. In Unity, expect some short coastal breaches to replace closed terminations
with ocean outlets; isolated/deep basins, excessive cuts and conflicts still reject.
This change is compile-checked only; actual acceptance counts and appearance require
manual generation. Coastal verification adds a temporary TIN build when a pass has
provisionally accepted coastal candidates, and repeats it if any are removed.
