# Geodesic ocean connectivity

Implemented locally on `codex/filter-small-geodesic-inland-seas`, based on
`codex/implement-geodesic-river-drainage` at `956a934`. Changes are uncommitted.

## Classification audit

`PlanetGenerator.RebuildGeodesicOceanClassification` previously classified every
raw terrain cell below the resolved sea radius as ocean, with an explicit
all-ocean override for OceanWorld. `geodesicOceanMask` already fed the ocean layer
grid, temperature, resource/chemistry fields, submarine vent candidates, and
drainage outlets. Coastlines were the ocean-side cells adjacent to land; shelf
and basin categories were derived from this mask and shore distances.

The water renderer still used a complete sphere. Direction-based water-depth
queries, underwater camera effects, and river coast clipping also bypassed the
mask. These paths now honor the generated retained-ocean mask when filtering is
enabled. The generic direction query also delegates to that Geodesic path.
Legacy Cube Sphere generation/rendering is unchanged.

## Startup controls and persistence

In **Advanced**, when Geodesic Icosphere is selected:

* **Exclude small inland seas** — default **off**, preserving existing worlds.
* **Minimum ocean basin area (% of planet)** — default **0.1%**, range **0–5%**.
  The control is disabled while the toggle is off.
* Help explains that enclosed depressions remain unchanged terrain and can
  support future lakes.

`SimulationStartupConfig.excludeSmallDisconnectedSeas` and
`minimumOceanComponentAreaFraction` store the toggle and fractional threshold
(`0.001`, not `0.1`). They use the existing `startup_config.json` under
`Application.persistentDataPath`, now schema version 9. Schema 8 and older saves
explicitly keep the filter off. Normal reset preserves these advanced options;
advanced reset restores their defaults. Startup passes them into PlanetGenerator
before generation. Optional serialized bindings are also provided for the
alternative UGUI panel; the shipped PlanetScene uses the built-in setup screen.

## Algorithm and authority

`GeodesicOceanConnectivity.Build` walks the existing neighbor graph in ascending
seed-cell order, using an iterative queue. Each below-sea component records its
ID, cell count, geometric area, fraction of total planet area, and retained state.
Area is the sum of `UnitCellAreas * seaRadius²` (Unity units squared); fractions
are normalized by the total geometric surface area. Each component independently
passes when its fraction is at least the threshold. There is no largest-ocean-only
rule and no randomness.

The resulting `OceanMask` is published as `geodesicOceanMask` before any ocean
layers or environmental fields initialize. `OceanConnectivity` also retains
`BelowSeaLevel`, `ComponentId`, `PotentialLakeBasin`, component metadata,
`CoastalLand`, `CoastalOcean`, and ocean-neighbor counts for diagnostics and future
lake work. `oceanConnected` in the picker means retained ocean membership, not
membership in a single global ocean.

The existing procedural terrain and physical bathymetry are completed before
water occupancy is filtered. A private geometry-provenance mask preserves that
same terrain/collider interpolation for a same-seed A/B comparison. It is never
used for water, resources, or habitat. Excluded cells have zero water depth and
Land bathymetric classification; their physical basin floors remain in place.
Retained components have no ocean edge to a removed component, so their existing
shore distances and physical shelf profiles remain valid. Land components and
coast flags are updated; an excluded basin creates no surrounding coastal ring.

## Rendering and simulation consumers

With filtering enabled, `GeodesicMaskedOceanGeometry` builds spherical patches
only for retained cells. Patch boundaries are the nearest-cell spherical Voronoi
boundaries. Shared corners and subdivision midpoints prevent gaps between wet
cells. Geometry has a matching resource/depth mapping, so Fe2/rust tint updates
continue to work. A fully wet planet reuses the original sphere. Filtering off
uses the original sphere and original direction mapping unchanged.

The final mask feeds the existing `GeodesicOceanLayerGrid`; excluded cells have
zero active layers, zero marine volume, and no horizontal/vertical ocean links.
Existing chemistry, resources, thermal transport, sediment, air-sea exchange,
and marine biology consequently have no normal ocean nodes there. Submarine
vent selection uses the grid's bottom layer and mask, so those cells cannot
receive submarine vents. Terrestrial vents retain their existing rules.

Directional water queries and underwater effects check the cached mask; camera
world positions are transformed to planet-local space. The underwater radius
also uses the Geodesic sea radius when filtering is enabled. River drainage already consumed the cell mask;
anchor selection and coast clipping now also distinguish an excluded below-sea
basin from a retained sea. No lake filling, river lake inflow, or lake chemistry
has been implemented.

## Diagnostics and tests

One `[GeodesicOceanConnectivity]` line reports submerged/component counts,
retained/excluded component and cell counts, largest area fraction, threshold,
enabled state, and generation classification time. The picker reports
`InlandBasin`, `terrainBelowSeaLevel`, `oceanConnected`, `potentialLakeBasin`, and
coastal-land status. Existing ocean-layer diagnostics show zero active layers.

Validation on this computer:

* **13 new NUnit tests passed**, invoked from PowerShell against the actual C#
  sources, installed Unity CoreModule, and Unity's NUnit assembly. These cover
  one ocean, two large seas, a tiny basin at subdivisions 3 and 4, disabled-mode
  equivalence, geometric threshold/boundary semantics, determinism, unchanged
  input heights, coastline removal, zero active marine layers/volume/links,
  OceanWorld and disabled-ocean behavior, invalid thresholds, nearest-cell lookup,
  water triangle containment/winding/resource mapping, empty water geometry,
  and river coast clipping.
* **10 existing drainage NUnit tests passed** through the same standalone method.
* Full simulation and EditMode test assemblies compile with Unity's Roslyn
  compiler and the project's generated references. Existing unused-field
  warnings remain.
* Four additional Unity integration tests were added and compiled: default/old
  saved settings, persistence round trip, reset semantics, and same-seed generated
  terrain preservation plus removed habitat and translated/rotated coordinates.
* Unity batch execution exited during startup with licensing IPC/OS access errors
  before producing test results. **These four integration tests and the live
  visual A/B check have not been executed.** Standalone test execution is not a
  substitute for that editor validation.

## Generation cost

Local standalone measurements, seed 12345, Earthlike terrain settings, sea radius
8, threshold 0.1%, render subdivision equal to simulation subdivision. Topology
and terrain construction are excluded from these timings. These are indicative
single warm connectivity calls, not Unity Profiler measurements.

| Subdivision | Cells | Connectivity | Masked geometry + mapping | Excluded cells | Retained components |
|---|---:|---:|---:|---:|---:|
| 4 | 2,562 | 0.9 ms | 10.7 ms | 3 | 3 |
| 6 | 40,962 | 12.4 ms | 104 ms | 324 | 2 |
| 7 | 163,842 | 56.6 ms | 461 ms | 1,509 | 1 |

Subdivision 6 generated 163,096 water triangles and approximately 8.1 MB of
geometry/mapping arrays; subdivision 7 generated 651,196 triangles and 31.8 MB.
Classification and derived-coast updates are O(cells + edges), with O(cells)
storage. Rendering work scales with generated water mesh size. There are no
per-frame component searches. Direction queries walk nearby graph cells and can
reuse the previous nearest cell.

## Limitations and manual A/B checklist

Connectivity is resolved at the simulation-cell resolution. Narrow channels or
depressions smaller than a cell cannot be reliably classified; water boundaries
follow cell borders and may look angular at low subdivision. Increasing render
subdivision smooths spherical facets but does not increase connectivity detail.
Masked geometry uses at least the simulation subdivision and can contain more
triangles than the original water sphere. TargetAreaCoverage resolves sea level
before filtering; achieved ocean coverage can fall below its target afterward.
OceanWorld keeps its existing all-ocean override. No actual lakes are present.

1. In Unity, run EditMode tests `GeodesicOceanConnectivityTests`,
   `GeodesicOceanIntegrationTests`, `GeodesicDrainageTests`, and
   `SimulationStartupConfigTests`.
2. Select Geodesic, disable random seed, use a fixed seed and unchanged terrain,
   sea-level, and subdivision settings. Start paused with the filter **off**.
   Note a small inland water patch and its cell index. Record its raw and final
   surface radii in the picker.
3. Restart with the same settings, toggle **Exclude small inland seas** on, and
   use **0.1%**. Confirm the diagnostic reports excluded cells; if the selected
   basin is larger, increase the threshold without changing the seed.
4. Revisit that cell: water should be absent; picker should show **InlandBasin**,
   below-sea true, oceanConnected false, potentialLakeBasin true, zero active
   ocean layers, and unchanged terrain/seafloor radii. Check for underwater fog.
5. Inspect multiple large seas, their coasts and river mouths. Check that no blue
   rim remains around the excluded basin. Rotate/zoom and inspect cell-border
   faceting, especially at a low simulation subdivision.
6. Toggle off again and confirm the old water patches return. Restart the app
   to check saved settings, and check both reset buttons.

## Changed source files

* `Core/Startup/SimulationStartupConfig.cs`, `SimulationStartupController.cs`,
  `SimulationStartupPanel.cs` — settings, persistence, migration, UI, reset.
* `Planet/Common/Generation/PlanetGenerator.cs` — authoritative mask, preserved
  terrain geometry, coast classification, water mesh selection and queries.
* `Planet/Environment/Ocean/GeodesicOceanConnectivity.cs` (new) — components,
  area filter, cached metadata and nearest-cell query.
* `Planet/Geodesic/Rendering/GeodesicMaskedOceanGeometry.cs` (new) — water patches.
* `Planet/Environment/Ocean/UnderwaterVolumeController.cs` — wet-region gate.
* `Planet/Geodesic/Diagnostics/GeodesicCellPicker.cs` — basin diagnostics.
* `Planet/Geodesic/Hydrology/GeodesicRiverPath.cs`, `GeodesicRiverSystem.cs` —
  retained-ocean coast termination and anchor selection.
* `Assets/Tests/EditMode/GeodesicOceanConnectivityTests.cs` and
  `GeodesicOceanIntegrationTests.cs` (new), plus their Unity metadata.

Source paths above are relative to `Assets/Scripts` unless they start with Assets.
