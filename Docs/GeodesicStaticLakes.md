# Geodesic static lakes: implementation and validation

Branch: `codex/implement-geodesic-static-lakes`, based on `7858b1f` (the drainage branch with inland-sea filtering merged). Changes are local and uncommitted.

The first-stage lake data, meshes, settings, river integration and diagnostics are implemented. Standalone tests pass, but native Unity integration and runtime visual acceptance remain pending. This is not a claim that all river gaps or visible spillways are solved.

## Options

Advanced Options for Geodesic planets now include:

| Option | Default | Meaning |
| --- | --- | --- |
| Generate hydrological lakes | Off | Build static water over qualifying depressions. |
| Minimum lake basin area (% of planet) | 0.01% | Area-weighted threshold; internal fraction 0.0001, configurable from 0 to 5%. |
| Minimum lake depth (planet units) | 0.005 | Minimum hydrological floor-to-spill difference; configurable from 0 to 1. |

Settings use the existing save schema (now version 10). Older saves remain disabled with these defaults. Normal reset preserves the lake options; Advanced reset restores them. The built-in setup UI is wired; optional UGUI fields are also supported. Inspector settings are available on PlanetGenerator. Lakes can be generated while river ribbons are disabled.

## Authority and data

The existing priority flood now preserves its original `FloodParent` and `FloodRank`, in addition to original hydrological elevations, filled elevations and fill depths. A basin is a connected set of non-ocean cells with positive fill depth and an identical spill level. Sampled edge saddles separate pools that meet only at the waterline. Adjacent different spill levels remain distinct. Sub-basins fully submerged under one connected spill level form one full lake; this is not a complete hierarchy of partially filled nested lakes.

Each `GeodesicLakeBasin` stores membership, area/fraction, floor/depth, spill elevation, inside spill cell, outside spill cell, downstream receiver, catchment area, selection/rejection status and incoming river count. Its mutable `CurrentSurfaceElevation` is separate from this terrain-derived geometry and initially equals the spill level. Runoff does not determine basin existence or change lake level.

The earliest settled basin member supplies the original flood edge to the outside. Selected basin receivers are routed to that one edge; a cycle check precedes publication, and catchment area and runoff accumulation are rebuilt. Terrain elevations, terrain vertices, OceanMask and marine grids are unchanged. The routing spill is authoritative for hydrology; a fine visible-terrain spillway can still disagree and fail visual refinement (see limitations).

`BasinId` preserves candidate hydrological membership, including small/rejected basins. `LakeMask` uses the actual clipped water footprint at each simulation cell centre, and `LakeSurfaceElevation` is zero outside it. The picker reports Lake separately from Ocean, with original/fill/water elevations, visible depth, spill, catchment, inlet and rejection information. Excluded below-sea depressions use the same basin rules as above-sea depressions. Retained oceans never receive LakeMask; lake cells do not acquire marine layers, chemistry, temperature columns or biology.

All-land worlds retain terminal/unresolved basin metadata and do not invent ocean outlets. Disabled mode preserves the old river paths; an exact path-comparison test covers this.

## Geometry and river connections

A local flood on completed terrain vertices validates each already detected basin. It starts at the lowest visible vertex, stays inside its hydrological footprint plus an adjacent-cell margin, and rejects pools that escape into other basins or the ocean. No basin is created from a failed river path.

Water polygons are clipped against completed terrain triangle edges at the lake radius. Their vertices all have that radius; they do not follow terrain bumps. Actual visible area is also checked against the area threshold. There is no global lake sphere or terrain carving. The independent URP shader uses depth offset for shore contact and lives in Resources so builds retain it. The footprint query uses double-precision edge tests with an angular tolerance to avoid false dry holes on shared high-resolution mesh edges.

Submerged receiver edges become explicit lake bridges. Inlet ribbons stop at the shoreline. Only the authoritative spill chain can produce an outlet, including a shoreline extending into the neighbouring routing cell. Incoming lakes and ocean mouths are separate. A visual intersection with an unrelated lake is reported as a projection mismatch rather than silently redirecting the graph. Lake approaches use visible radial elevations for grading and the actual terrain triangle intersection for ribbon placement; this avoids treating spherical facet sag as an uphill grade.

The river log retains aggregate suppressed reaches and separates projection mismatch, corridor failure, unresolved small depression and topology failure. Lake-connected reaches include bridges plus inlet/outlet ribbons; the lake log also reports submerged bridges separately. Candidate reach counts can change because a single basin outlet changes runoff distribution. Visible land reaches should decrease where lake water replaces submerged ribbons.

## Measured comparisons

These are **standalone CPU comparisons of the actual pure C# classes**, not Unity scene captures. Both sides share the same immutable terrain and ocean inputs. The sampler seed is 12345, radius/sea radius 8, retained-ocean threshold 0.1%, default lake thresholds, river flow threshold 8, and render subdivision equals simulation subdivision. Startup seed derivation, generated bathymetry, native Mesh upload and rendering are not included.

The enclosed-basin fixture uses Earthlike settings except continent and mountain amplitudes are zero and fine-detail amplitude is 0.08 (scale 18). This extends the earlier inland-sea-filter validation fixture. It contains 1,373 / 6,419 excluded below-sea cells and 5,794 / 17,980 priority-filled cells at subdivisions 6 / 7.

| Metric | Subdivision 6 OFF | Subdivision 6 ON | Subdivision 7 OFF | Subdivision 7 ON |
| --- | ---: | ---: | ---: | ---: |
| Candidate river reaches | 928 | 857 | 6,042 | 6,172 |
| Visible land reaches | 362 | 209 | 3,500 | 2,321 |
| Suppressed reaches | 566 | 331 | 2,542 | 1,359 |
| Lake-connected reaches | 0 | 326 | 0 | 2,551 |
| Inland visible terminations | 142 | 36 | 1,046 | 399 |
| Ocean-connected chains | 51 | 56 | 843 | 852 |
| Ocean mouth candidates above threshold | 204 | 204 | 1,302 | 1,302 |
| Rendered lakes | 0 | 69 | 0 | 96 |
| Visible lake inlet / outlet ribbons | 0 / 0 | 4 / 5 | 0 / 0 | 55 / 4 |

Residual ON failures at 6: 37 projection, 62 corridor, 232 unresolved depression, zero topology. At 7: 98 projection, 94 corridor, 1,167 unresolved depression, zero topology. The low number of successfully rendered outlet ribbons is a material remaining limitation; these results do not prove complete river continuity through every rendered lake.

There are 3,306 / 7,350 candidate basins. Thresholds select 108 / 126; visible footprint checks reject 28 / 18, and visible-area checks reject another 11 / 12. The resulting meshes contain 4,058 / 17,564 triangles. Local basin/geometry/rerouting work measured about **178 ms / 1,381 ms**. Terrain/topology setup measured 4.3 s / 37.9 s and OFF/ON path refinement 665/396 ms and 7,055/4,686 ms. These single local runs are not frame-time guarantees; the standalone benchmark's ocean lookup also has different caching from PlanetGenerator. There is no per-frame flood or mesh reconstruction.

An additional unmodified Earthlike fixture with the same sampler seed produces **zero qualifying lakes at the defaults** at both resolutions: suppressed reaches remain 107 / 530 and visible reaches 3,089 / 23,035. Reducing minimum depth to 0.001 alone still leaves most candidates incompatible with the completed visible terrain (fine detail covers the lower hydrological spill surface). This implementation does not raise water arbitrarily to conceal that mismatch.

`oceanConnectedChains` counts candidate network headwater chains whose downstream paths/bridges remain usable; it is not a pixel-level measure. Inland termination counts exclude valid lake inlets. Use the other failure counts alongside both metrics.

## Validation

- **42 pure tests passed:** 19 new lake tests, 10 existing drainage tests, 13 existing ocean-connectivity tests.
- New coverage includes small/shallow rejection, above/below-sea basins, ocean exclusion/no marine layers, spill-level surfaces, unchanged meshes/heights, unique acyclic outlet routing, runoff independence, thresholds/disable, determinism, ridge clipping, leaked or absent visible depressions, terminal worlds, different/equal-level saddle separation, multiple inlets, outlet/bridge construction, exact disabled river paths, and high-resolution shared-edge precision. Synthetic geometry is exercised at several subdivisions.
- The full runtime and EditMode C# assemblies compile with Unity's Roslyn compiler and package references. Existing unused-field warnings remain.
- Three additional native integration tests compile: save migration/round-trip, reset semantics, and runtime mesh/inlet/outlet/cleanup/planet-transform/Legacy behavior. **They have not executed successfully in Unity.**
- Native batch testing of the working project was refused because it is open in another Editor. An isolated copy then repeatedly failed Unity licensing IPC initialization; no test-result XML was produced. The working Editor was left open.
- `git diff --check` passes. No chemistry, biology, movement, transport, temperature, ocean-mask algorithm or terrain-production code was changed.

Reproduce the pure tests from PowerShell 7:

```powershell
./Tools/ValidateGeodesicLakes.ps1
./Tools/ValidateGeodesicLakes.ps1 -Benchmarks
```

Pass `-UnityData` if Unity is installed elsewhere. The project must have its NUnit package restored. In Unity Test Runner run `GeodesicLakeTests`, `GeodesicLakeIntegrationTests`, `GeodesicDrainageTests`, `GeodesicOceanConnectivityTests`, `GeodesicOceanIntegrationTests`, and `SimulationStartupConfigTests`.

## Remaining acceptance and limits

Runtime visual validation remains required before claiming subdivision 6 or 7 is sufficient. Compare identical New Game settings with lakes OFF/ON, start paused, and preserve terrain/ocean settings. Inspect actual inlet shores and spill-side outlet ribbons, level water, ridge clipping and unchanged retained oceans. Use Cell Picker on an above-sea lake, a filtered below-sea basin and a retained ocean cell. Repeat at 6 and 7 and record both generation logs and screenshots.

The current drainage surface deliberately omits fine procedural detail, and coarse edge saddles use three samples. A valid routing basin can therefore disagree with the completed mesh or lack a downhill visible outlet corridor. Such failures remain explicit; terrain is not eroded, routes are not forced uphill, and no resolution-sufficiency claim is made. One connected visible pool is represented per coarse basin; disconnected fine-scale sub-pools are not a full nested-lake hierarchy. Tiny within-triangle puddles with no wet vertex are intentionally omitted. Lake mesh facets approximate the spherical equipotential surface. Dynamic water budgets, precipitation, erosion and lake simulation remain outside this change.

## Files

- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicLakeBasins.cs`: basin identity, thresholds, state and receivers.
- `GeodesicLakeGeometry.cs` in the same folder: connected completed-terrain clipping and water lookup.
- `GeodesicLakeRiverRouting.cs` in the same folder: bridge, inlet/outlet and failure planning.
- `GeodesicDrainageGraph.cs`, `GeodesicRiverTerrain.cs`, `GeodesicRiverSystem.cs`: flood identity, triangle query, generation/render integration and diagnostics.
- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs` and `Assets/Scripts/Planet/Geodesic/Diagnostics/GeodesicCellPicker.cs`: settings, classification and inspection.
- `Assets/Scripts/Core/Startup/SimulationStartupConfig.cs`, `SimulationStartupController.cs`, `SimulationStartupPanel.cs`: UI, persistence, migration and resets.
- `Assets/Shaders/Geodesic/Resources/GeodesicLakeURP.shader`: standalone lake surface shader.
- `Assets/Tests/EditMode/GeodesicLakeTests.cs`, `GeodesicLakeIntegrationTests.cs`: algorithm and native integration coverage.
- `Tools/ValidateGeodesicLakes.ps1`, `Tools/GeodesicLakeBenchmark.cs`: reproducible standalone verification.
- This report and Unity metadata for the new assets.
