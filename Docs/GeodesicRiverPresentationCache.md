# Final geodesic river presentation cache

The accepted whole-basin meander, safety, width, clipping and draping calculations
are unchanged. Stage 6 caches their completed presentation independently of the
Milestone 2 TIN, all-lakes and ocean-delivery records (stages 3–5, unchanged).
No Unity execution, tests, builds, simulations or benchmarks were run for this change.

## Why a hydrology hit still did expensive work

`RebuildRenderHydrologyVisualStages` previously rebuilt every ribbon and performed
the geometric outlet audit after restoring stage 5. That includes completed-path
terrain sampling and coast fitting, crossing construction, whole-basin meanders,
and ribbon clipping against the sediment surface triangles. In the fitter,
`Covers`, `Coverage` and `LateralRoom` scan a basin's polygon union repeatedly;
collision checks scan other crossing segments and nonadjacent parts of the same
crossing. The outlet audit searches receiver triangles/segments and samples
terrain/ocean contact. These operations can all be restored from a presentation
record bound to the same upstream output. Source inspection cannot establish
which operation consumed most of the reported 120 seconds; the new miss timings
are needed to establish that split.

## Stored data and hit behaviour

The record contains positions, indices, normals, optional UV0, bounds, per-vertex
normal/major colour classes, route IDs and destination classes, final diagnostic
vertex ranges, completed crossing paths and their measured offsets, river mouth
and exposed path data, and completed outlet audit results/inspector centrelines.
It contains no GameObject, Mesh, Material, live routing object or instance ID.

A hit creates a fresh mesh, material and renderer using the existing shader and
display settings. It skips network centreline preparation/validation, coast
fitting, crossing construction, meander fitting and constraint searches, ribbon
clipping/draping, normal calculation, and geometric outlet contact searches.
It restores the completed geometric audit as a read-only proof of the stored
mesh; it does not claim to have repeated that geometric audit on a hit.

Authority restoration and its existing conservation, membership, corridor and
topology validation still happen first. Lake/sediment terrain meshes and collider
setup still run. `RiverStrength` and public river/mouth data remain available.
The inner presentation iterator returns on a hit; later planet/simulation/biology
initialization continues through its existing caller.

Grade colouring is a special cosmetic overlay: the existing diagnostic duplicates
vertices and rewrites indices. The record stores the completed normal river mesh;
if grade colouring is enabled, its observations and exact duplicated-vertex
overlay are reconstructed afterward. Optional grade sampling therefore still
costs time, reported under `displayDiagnosticsMs`. Termination markers are also
fresh display objects. Destination and meander colours use the saved final ranges.

## Identity and invalidation

The SHA-256 key includes:

- Stage 6 schema and presentation algorithm versions, plus the explicit
  `GeodesicTinLakeCrossings.AlgorithmVersion`.
- The verified stage 5 key **and payload digest**, transitively covering final TIN
  terrain/geometry, runoff, raw networks, lake routing, sediment corridors,
  retained ocean classification and shoreline geometry.
- Runtime numeric compatibility (Unity/platform/architecture/backend), render
  subdivision, planet radius, visual seed and simulation-cell mapping.
- Resolved discharge threshold, major threshold, width, width multiplier, surface
  offset, raw-line mode and lake visibility qualification (which changes sediment).

Seed alone is insufficient. Biology, chemistry, assembly MVID, runtime object IDs,
logging switches, `showRivers`, river colour and destination/meander/grade colour
selection are excluded. Changing colour can reuse the same geometry. The river
component context action **Refresh River Colours Only** reapplies the current
base/major colours without routing or fitting; existing palette toggles still work.

Bump `RiverPresentationAlgorithm` for changes to ribbon, sampling, draping or
outlet-fit/audit mathematics; bump the meander version for fitter changes. Bump
`RiverPresentationSchema` for payload changes. None require an authority schema
or terrain generation version bump. Shader/palette-only changes need no bump.

Caching is restricted to a verified completed integrated ocean-delivery parent.
Historical/unintegrated paths and runoff updates without such a parent build
normally. Rejected-path capture (`showRejectedUphillRiverSegments`) also builds
normally because rejected intermediate geometry is deliberately not persisted.
Grade/termination/destination/meander display alone does not bypass the cache.

## Files, integrity and lifetime

Files use the existing `PlanetGenerationCache` directory:
`Application.persistentDataPath/SimulaVitPlanetCache/geodesic/v1/6_<key>.bin`.
The precise absolute path is logged by `[GeodesicPlanetCache]`. Other stages and
their schemas are unchanged. Missing, incompatible, truncated or checksum-invalid
files fall back to normal construction; decoded arrays, indices, ranges, route
classes and audit graph references are checked before touching Unity objects.

The existing same-directory temporary-file, flush and atomic replace/move path is
reused. Publication is queued with frozen arrays/bytes through the existing
successful-startup snapshot stage; a failed/cancelled startup discards pending
publication. No Unity references are captured by the publisher. Existing cleanup
deactivates and releases the previous display before either restoration or rebuild.

The geometry digest covers positions, normals, UV0, indices, colour classes and
bounds. A hit reports `recordedUncachedGeometryMatch=true` after comparing decoded
data with the digest captured on its original miss. This is integrity against that
uncached result, **not an independent live regeneration test**.
After a hit, the component action **Rebuild River Presentation And Compare Cache**
deliberately regenerates once and reports `liveRegenerationCompared=true` and
`liveRegenerationMatchesRecorded`. A mismatch retains the prior disk record for
review. This action is expensive and only runs when explicitly invoked.

## Size and memory

For V vertices, I indices, U UV entries (currently zero), and M metadata bytes,
the disk record is approximately **176 + 25V + 4I + 8U + M bytes**. The generated
log reports these sizes, and a hit reports actual file bytes. Example: one million
vertices and three million indices use about 35.3 MiB plus metadata on disk.

Fresh Unity CPU mesh data is approximately 40V + 4I + 8U bytes with float colours;
GPU storage is comparable but driver dependent. Managed normal colours and saved
bands use roughly 17V bytes, plus paths/audit metadata and container overhead.
Each requested diagnostic/destination colour array adds roughly 16V bytes.
Restoration temporarily holds decoded geometry/metadata while uploading; a miss
retains frozen arrays (~25V + 4I + 8U + M) until successful snapshot publication.
The restored audit keeps centrelines instead of another full receiver mesh copy.
The unchanged shared limits are 128 MiB per payload, 1 GiB total and 64 files;
oversized records are skipped. No compression or cache size settings were added.

## Next manual runs

1. First run with existing stages 3–5: expect a stage 6 miss, the accepted river
   appearance, normal conservation diagnostics, then a saved `6_...bin` after
   successful startup. No authority cache deletion is necessary.
2. Same-process hit: after that publication, invoke **Refresh River Thresholds And
   Visuals** with unchanged geometry settings, or return through the startup menu.
   Expect the same stage 6 key and a hit, a single live river mesh, working route
   picking and diagnostic colours, and unchanged meander summary measurements.
3. Restart the application with the same settings: expect the same key/digest and
   a disk hit. Compare this with the same-process hit to verify persistence.
4. Optionally invoke the explicit rebuild/compare action once to compare a fresh
   deterministic generation with the hit's reference digest. Recolour and toggle
   visibility/diagnostics; these should not change the normal geometry key.

Compare `[GeodesicRiverPresentationTiming]` fields: `geometryGenerationMs`
(path preparation + crossing build + meanders + terrain/ribbon vertices),
`pathPreparationMs`, `crossingBuildMs`, `meandersMs`, `containmentMs`, `collisionsMs`,
`terrainAndRibbonVerticesMs`, `meshUploadMs`, `outletCaptureMs`, `outletAuditMs`,
`displayDiagnosticsMs`, and `authorityAndSurfacesMs`. Containment and collision
times are subsets of generation time and must not be added again. The
`centrelineTerrainSamplingMs` subset identifies network grade-validation terrain
sampling within path preparation; projection/coast-fit preparation is also part
of `pathPreparationMs`. On a hit the
generation and live outlet audit fields should be zero. Compare cache
`readAndChecksumMs`, `validationDecodeMs`, `reconstructionMs`, key/version, file
bytes, mesh counts and digest. `meshUploadMs` measures mesh setup/upload calls,
not asynchronous GPU completion. Shared cache logs also separate header/checksum
reads and report publication `writeMs` and managed heap deltas.

Lake/sediment construction, authority validation, disk decoding/hashing, mesh
uploads, optional grade sampling, other startup stages and biology remain outside
the eliminated geometry searches. No measured startup speedup is claimed before
manual execution.
