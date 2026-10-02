# Dedicated render-resolution Geodesic hydrology

## Runtime architecture

The normal Geodesic path keeps the simulation topology unchanged and builds hydrology from the render mesh that already exists:

```
simulation L6 (40,962 cells, ocean/climate authority)
  -> existing simulation-to-render mapping
render L7 geometry (163,842 directions, 327,680 triangles)
  -> lightweight packed adjacency and per-vertex spherical area
existing render L7 HydrologicalRadii
  -> priority flood, receivers, drainage area, runoff, lakes
hydrology L7 edge paths
  -> lake/coast clipping and completed-terrain projection
```

`GeodesicHydrologyTopology` retains one byte of degree data, six packed integer neighbor slots, and one float unit area per render vertex. `CellDirections` and `Triangles` are references to `IcosphereRenderGeometry.UnitVertices` and `Triangles`; it does not regenerate or copy them. It does not retain dual corners, transport metrics, or simulation data. The twelve degree-five vertices are validated and every other vertex must have degree six.

`PlanetGenerator` exposes the render mapping and the already evaluated `GeodesicRenderTerrainData.HydrologicalRadii` to `GeodesicRiverSystem`. The dedicated path shares the radii with both `GeodesicRiverTerrain` and `GeodesicDrainageGraph`. Fine completed terrain remains the final ribbon/lake projection surface and never changes receivers.

The authoritative L6 `OceanMask` is mapped to L7. A submerged hydrology vertex is ocean when its nearest mapped simulation cell, or one of that cell's direct L6 neighbours, belongs to the retained ocean component. L6 still owns component identity, while the completed L7 terrain determines the precise shoreline inside that component. This removes one-cell coarse coastal barriers without independently classifying an L7 ocean or restoring excluded inland seas. `[GeodesicHydrologyOceanMapping]` reports mapped ocean vertices, submerged L7 vertices, authority disagreements, coastal barriers, and filled depressions next to the mapped boundary.

The raw migration used the two endpoint directions of each L7 receiver edge as the rendered centreline. Ribbon subdivision only added linear samples, so rivers exposed the icosphere edges and angular junctions directly. The corrected path keeps the receiver DAG unchanged and builds one shared visual anchor per above-threshold river cell. It tests the cell direction, then candidates 34% toward compatible one-ring neighbours; it searches compatible two-ring candidates at 22% only when the first pass remains uphill or crosses a ridge. Candidates stay out of ocean and selected lakes, retain outlet/basin identity, remain locally close to the receiver edge, and cannot introduce a ridge crossing.

Each corrected edge is then sampled by a four-segment normalized Catmull-Rom curve using the dominant upstream tributary and the receiver's receiver for tangents. Both endpoints are the shared anchors, so tributaries meet at exactly one confluence position. A corridor-deviation bound and full-visible-terrain grade comparison fall back to a subdivided snapped edge if smoothing makes the result worse. Lake shoreline correction, coast clipping, and lake crossing validation remain active.

The serialized `useLegacySimulationHydrology` field preserves the old L6 anchor/spill/corridor route for A/B inspection. `useRawDedicatedVisualPaths` preserves the original raw L7 migration for the same purpose. Neither is the default.

`PlanetTerrainSampler.LargeScaleHeightOffset` already includes continents, domain-warped mountain masks, and the complete multi-octave ridge/mountain term. It excludes only the separately evaluated fine-detail term when that term is sufficiently small and high-frequency. The audit therefore found no missing medium-scale terrain input to restore to priority flood; visible-terrain disagreement is handled only by the local visual pass.

The configured river and major-river thresholds remain simulation-cell equivalents. Runtime resolves them as:

```
threshold area = configured threshold * (4 pi R^2 / simulation cell count)
```

At L6 simulation/L7 hydrology, a configured threshold of 8 is approximately 32 mean L7 cells. Per-node drainage uses actual spherical cell area. `UpdateSimulationRunoff` conservatively distributes a future simulation-cell runoff integral among mapped, non-ocean hydrology vertices by hydrology area.

## Memory

At L7, the packed topology is about 4.53 MiB. The retained drainage arrays, river strengths, and lake cell arrays bring the dedicated core estimate to about 15 MiB. The equivalent legacy core/path estimate in the seed benchmark was about 3.5 MiB, so the measured architectural increase is about 12 MiB. Existing render directions, triangles, and hydrological radii are excluded because the dedicated graph shares them.

Priority-flood scratch is approximately 1.4 MiB (settled flags plus indexed-heap arrays). Lake projection can temporarily use roughly 10-12 MiB for render incidence, visitation, allowed-cell, queue, and touched-face arrays. Those arrays are generation-local. Receiver/order/area/runoff arrays remain available for future discharge updates. Lake direction queries retain render-face lookup arrays in both migration modes; those are outside the core delta above.

## Seed 123456 pure-algorithm A/B/C

Command:

```
pwsh -NoProfile -File Tools/ValidateGeodesicLakes.ps1 -HydrologyAB
```

The benchmark uses the same L6 simulation topology, L7 terrain, sea level, retained L6 ocean mask, physical threshold area, and terrain seed for both modes. Render topology and terrain evaluation happen once and are excluded from the two hydrology totals because they already exist when runtime hydrology starts.

| Metric | GOOD_VISUAL_OLD L6 + refinement | CURRENT_HIGHRES raw L7 | Corrected L7 |
|---|---:|---:|---:|
| Hydrology nodes | 40,962 | 163,842 | 163,842 |
| Additional full topology builds | 0 | 0 | 0 |
| Resolved threshold area | 0.157071963 | 0.157071963 | 0.157071963 |
| Candidate reaches | 5,771 | 7,157 | 6,964 |
| Visible reaches | 5,597 | 7,157 | 6,964 |
| Major reaches | 339 | 746 | 703 |
| Total rendered length | 992.661 | 528.468 | 520.163 |
| Inland visible terminations | 125 | 0 | 0 |
| Ocean-connected chains | 749 | 697 | 673 |
| Filled cells | 2,034 | 8,715 | 7,251 |
| Candidate basins | 379 | 468 | 448 |
| Visible lakes / inlets / outlets | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| River mesh vertices | 398,812 | 57,256 | 179,462 |
| Core/path retained estimate | 3,709,740 B | 15,736,758 B | 15,979,626 B |
| Priority flood | 1,467.1 ms | 321.4 ms | 296.3 ms |
| Path extraction, excluding shore/coast | 13,521.3 ms | 129.8 ms | 361.1 ms |
| Shore/coast processing | 648.7 ms | 241.5 ms | 224.5 ms |
| Valley snap | 0 | 0 | 1,320.4 ms |
| Spline smoothing | 0 | 0 | 205.6 ms |
| Final-detail ribbon projection sample | 707.7 ms | 100.4 ms | 249.2 ms |
| Total | 17,888.0 ms | 1,589.3 ms | 2,648.4 ms |

The corrected benchmark constructs no second `GeodesicGridTopology`. In this run its packed adjacency build took 115.7 ms and ocean mapping took 8.9 ms. The local correction adds about 1.53 seconds, while corrected total generation remains about 6.8 times faster than GOOD_VISUAL_OLD.

Of 6,964 eligible shared anchors, 1,968 (28.26%) stayed at the graph vertex, 4,879 (70.06%) used a one-ring candidate, and 117 (1.68%) used a two-ring candidate. Five hundred and two (7.21%) retain a material full-visible-terrain mismatch after the bounded search. Materially uphill visible edges fell from 1,391 to 502 (63.9%); ridge crossings remained 0 before and 0 after. These remaining mismatches are reported rather than converted into lakes or allowed to widen the search.

The corrected authoritative shoreline mapping reduces filled cells from 8,715 to 7,251 and candidate basins from 468 to 448. Submerged vertices outside a retained L6 ocean neighbourhood remain land/depression candidates, so excluded inland seas cannot return. Runtime diagnostics expose the remaining boundary disagreements instead of silently classifying an independent L7 ocean.

Default Earthlike benchmark thresholds selected no visible lakes in either mode, so the seed run could not exercise outlet counts. A synthetic high-resolution basin test verifies an identity-mapped visible lake, a neighboring spill edge, deterministic receiver rewiring, and outlet accumulated flow greater than or equal to every incoming flow. Runtime reports unique expected/rendered/suppressed lake outlets and separates below-threshold, projection, shoreline, topology, and flow-conservation failures.

## Runtime diagnostics

`GeodesicRiverSystem` emits:

- `[GeodesicHydrologyTopology]`: simulation/render/hydrology subdivisions, vertex/triangle count, topology source, radii reuse, retained core bytes.
- `[GeodesicDrainage]`: threshold area, candidate/visible reach and mesh counts, filled cells, failure/continuity categories, and ordinary versus lake corridor failures.
- `[GeodesicHydrologyPerformance]`: adjacency, ocean mapping, priority flood, accumulation, lake topology, lake geometry, path extraction, shoreline/coast, ribbon geometry, and total generation time.
- `[GeodesicLakeOutlets]`: unique expected/rendered/suppressed outlets with reason counts and outlet-flow violations.
- `[GeodesicRiverVisualAudit]`: anchor-ring counts, visible grade/ridge counts before and after correction, unresolved edges, smoothing fallbacks, and correction timings.
- `[GeodesicHydrologyOceanMapping]`: authoritative shoreline mapping and boundary-depression counts.
- `[GeodesicRiverDensity]`: candidate/visible/major reach counts, physical threshold area, total rendered length, and mesh vertices.

The remaining causes of visible mismatch are the 502 edges whose small local neighbourhood has no acceptable full-visible-terrain route, plus lake shoreline/projection rejection and intentional thresholding. The legacy and raw-L7 routes remain available for visual A/B inspection and must not be removed before that comparison is accepted.

## Validation status

The pure hydrology suite passes 60 tests. The runtime and EditMode C# assemblies compile against Unity 6000.3.19f1. The deterministic seed-123456 numerical A/B/C above completed. Two isolated screenshot attempts compiled successfully but Unity's licensing service refused the `com.unity.editor.headless` entitlement while the live editor and import workers were active, so no trustworthy runtime screenshots were produced. Capture the three retained modes from the same saved camera once an interactive editor is available: legacy fallback, raw dedicated fallback, and the corrected default.
