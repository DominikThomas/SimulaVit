# Broad inland elevation experiment

The startup **Advanced → Inland elevation magnitude** numeric field adds a broad
land elevation bias. Default **0**, range **0–0.05**, in the same planet-radius
units as terrain displacement. The upper bound is below the existing continent
amplitude (0.09), Earthlike mountain amplitude (0.16), and scene mountain amplitude
(0.8). It is a conservative experimental cap, not a recommended magnitude.

No nonzero value was saved to the startup configuration or scene. No visual
experiments, screenshots, parameter sweeps, or drainage optimization were run.

## Formula and distance authority

For angular inland distance `d` in radians:

```text
scale = 30° = π/6 radians
t = clamp01(d / scale)
uplift = magnitude * t³ * (6t² - 15t + 10)
finalSurfaceRadius = previousCompletedSurfaceRadius + uplift
```

Zero offset explicitly returns the previous radius without addition. The uplift
is added after the existing terrain contrast/clamp and bathymetry operations;
mountains, masks, ridges, fine detail, and regional variation are unchanged.
The existing terrain offset clamp does not clamp away the added bias.

The authoritative coastline is `GeodesicOceanConnectivity.OceanMask` after
retained-ocean classification and small inland-sea exclusion. A multi-source
Dijkstra pass starts at **land cell centres adjacent to retained ocean**, with
zero distance there, and traverses non-ocean neighbors using the topology's
cached `NeighborAngularDistances6`. Excluded inland seas are traversable land
for this calculation; they do not become new coast sources.

The old `geodesicDistanceToShore` cache cannot be reused: it is an ocean-side
bathymetry distance calculated before sea filtering, with no inland distances.
The new field reuses the existing simulation topology, adjacency, angular edge
lengths, and nearest-cell lookup. Construction is O((V+E) log V), storage O(V);
there is no all-pairs coast search or per-frame field rebuild.

Distances are interpolated barycentrically on the simulation triangles before
smootherstep, using the nearest cell's triangle fan. This gives continuous
interpolation rather than cellwise elevation steps. Every mixed ocean/land
triangle has zero distance at all its vertices, so the mask boundary and its
coastal land band receive zero uplift. Smootherstep has zero first and second
derivatives at zero and saturation.

Normalization uses an angular scale, never the largest continent distance or a
number of cells. The constructor accepts a scale for code-level configuration;
this milestone uses 30° and exposes only magnitude. At radius R the corresponding
arc length is Rπ/6. Render subdivision does not change the underlying field.
Simulation subdivision still determines coastline accuracy: the conservative
zero band is approximately one simulation-cell ring wide, and graph paths
approximate continuous spherical distance. These limitations are most visible
at very coarse simulation subdivisions.

All-ocean, dry, or fully excluded-ocean worlds have no reference coast and receive
zero uplift. Disabled terrain displacement also disables uplift.

## Generation, configuration, and preservation

Sea level, target coverage, ocean classification, inland-sea filtering, and
bathymetry are resolved from the original terrain. The distance field is built
afterward, before render/collider sampling. The existing physical bathymetry and
procedural/raw terrain caches remain unchanged. Completed surface queries,
simulation land-height getters, render vertices, and collider vertices add the
same uplift field. Rebuilding classification invalidates the previous distance
and debug surface caches; clearing the generated planet clears the field.

The paired raw and fine-detail-removed render samples remain baseline samples.
The existing fine-detail mode applies their difference to the completed surface,
preserving its uplift. **Visible** hydrology directly consumes the completed
mesh as before. The historical reduced-ridge reference mode retains its original
replacement-sampler behavior; use Visible for the requested manual experiment.

Startup schema **16** persists the field. Missing fields, including missing fields
with a customized fallback, and schemas before 16 resolve to zero. Invalid or
nonfinite magnitudes normalize to zero; finite values clamp to the allowed range.
Normal reset preserves the value; Advanced reset restores the default zero.
The controller assigns it before planet initialization. The generator holds a
runtime property, not a second serialized Inspector setting. The optional custom
startup panel also supports a numeric input and help label.

No river/lake implementation, thresholds, routing, topology, rendering, integration,
source geometry, TIN tolerance, ocean classification algorithm, or sea-level target
was changed. Existing mapped and unmapped bathymetry queries can differ at some
cell boundaries; each retains its original baseline and receives the same uplift.

## Diagnostics and manual comparison

One `[GeodesicInlandUplift]` report accompanies main render terrain generation:

- Configured magnitude, coast authority, angular normalization scale, and whether
  a coast exists.
- Rendered land vertex count and count with a positive final radius change.
- Minimum/mean/maximum inland angular distance and applied uplift over land.
- Mean uplift within 3° inland (10% of the normalization scale).
- Mean uplift in the farthest inland quartile of rendered land vertices.
- Maximum final radius difference versus the disabled baseline, measured in the
  same sampling pass without generating another planet.

Statistics are vertex-weighted, not area-weighted. Applied uplift statistics use
the actual float radius difference after addition. Empty groups report zero.
No per-vertex messages are emitted.

Existing, unchanged logs provide the other comparison metrics:

- `[GeodesicTIN]`: closed-basin/ocean terminations, river networks, mean/max network
  length, confluences, unexpected broken networks and inland breaks.
- `[GeodesicTinLakes]`: raw/qualifying candidates and rendered lake count.
- `[GeodesicTinLakeRouting]`: lake inflows, outlets, cascades and integration metrics.

For manual comparison, keep seed, simulation/render subdivisions, Visible
hydrology, river thresholds, and lake settings fixed. The suggested seed 123456,
simulation 6/render 7, and threshold 2 are not forced in code. Start a new
generation after changing magnitude; rebuilding rivers alone does not rebuild
the visible terrain. Choose experimental values manually. No value is recommended
by this implementation.

## Validation

Native Unity **6000.3.19f1**, headless EditMode tests in the existing disposable
test project: **all 12 new uplift tests passed**. Coverage includes bitwise zero
preservation, coastal/mixed triangles, retained ocean, monotonicity, magnitude
scaling, seeded determinism, unchanged inputs, excluded inland seas, no-coast
cases, interpolation continuity, render subdivision consistency, simulation
resolution sanity, aggregate diagnostics, persistence/migration/reset, exact
previous render calculation, unchanged ocean/bathymetry/sea level, collider and
query integration, unchanged existing river/lake state, and exact zero restoration.

The broader selected run was **95 passed / 1 failed** across 96 tests. The remaining
failure is the unchanged
`GeodesicOceanIntegrationTests.SameSeedFilterTogglePreservesTerrainAndRemovesMarineHabitat`
assertion at line 94: `GetGeodesicCellBathymetryRegion` returns the string `"Land"`,
but the test expects enum `GeodesicBathymetryRegion.Land`. Neither that API nor the
test assertion was changed. This is not reported as a fully green regression suite.

Results: `Artifacts/InlandUplift/native-tests.xml` and `native-tests.log` (ignored).
Selected filter:
`GeodesicInlandUplift;GeodesicOceanIntegration;GeodesicHydrologyTerrainIntegration;SimulationStartupConfig;GeodesicTin`.
These are correctness tests, not visual evaluations or magnitude tuning.

## Changed files

- `Assets/Scripts/Planet/Geodesic/Terrain/GeodesicInlandUplift.cs`: distance cache,
  interpolation, formula, bounds, and aggregate diagnostics.
- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs`: field lifecycle,
  additive surface integration, and generation report.
- `Assets/Scripts/Core/Startup/SimulationStartupConfig.cs`: default and help.
- `Assets/Scripts/Core/Startup/SimulationStartupController.cs`: numeric UI,
  application, persistence, migration, normalization, and reset copy.
- `Assets/Scripts/Core/Startup/SimulationStartupPanel.cs`: optional custom UI wiring.
- `Assets/Tests/EditMode/GeodesicInlandUpliftTests.cs` and
  `GeodesicInlandUpliftIntegrationTests.cs`: focused native tests.
- Three accompanying Unity `.meta` files, `.gitignore` for test artifacts, and
  this document.

Default **0 reproduces the previous terrain exactly** in the render regression
fixture. Work stops here for manual Unity evaluation; no further terrain or
hydrology experiment is included.
