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

The authoritative L6 `OceanMask` is mapped to L7. A hydrology vertex is ocean only when its nearest mapped simulation cell is retained ocean and the completed terrain vertex is submerged. Excluded inland seas therefore stay land/lake candidates. Hydrology does not perform a second L7 ocean-component classification.

Ordinary dedicated reaches use the two endpoint directions of the actual L7 receiver edge. They bypass channel-anchor lowering, intermediate edge-spill samples, corridor searches, four-subsegment grade sampling, and coarse-to-fine refinement. Lake shoreline correction, coast clipping, lake crossing validation, and final ribbon resampling remain active. The serialized `useLegacySimulationHydrology` field is an Inspector/debug migration fallback; it preserves the L6 anchors, edge spills, and corridor routing.

The configured river and major-river thresholds remain simulation-cell equivalents. Runtime resolves them as:

```
threshold area = configured threshold * (4 pi R^2 / simulation cell count)
```

At L6 simulation/L7 hydrology, a configured threshold of 8 is approximately 32 mean L7 cells. Per-node drainage uses actual spherical cell area. `UpdateSimulationRunoff` conservatively distributes a future simulation-cell runoff integral among mapped, non-ocean hydrology vertices by hydrology area.

## Memory

At L7, the packed topology is about 4.53 MiB. The retained drainage arrays, river strengths, and lake cell arrays bring the dedicated core estimate to about 15 MiB. The equivalent legacy core/path estimate in the seed benchmark was about 3.5 MiB, so the measured architectural increase is about 12 MiB. Existing render directions, triangles, and hydrological radii are excluded because the dedicated graph shares them.

Priority-flood scratch is approximately 1.4 MiB (settled flags plus indexed-heap arrays). Lake projection can temporarily use roughly 10-12 MiB for render incidence, visitation, allowed-cell, queue, and touched-face arrays. Those arrays are generation-local. Receiver/order/area/runoff arrays remain available for future discharge updates. Lake direction queries retain render-face lookup arrays in both migration modes; those are outside the core delta above.

## Seed 123456 pure-algorithm A/B

Command:

```
pwsh -NoProfile -File Tools/ValidateGeodesicLakes.ps1 -HydrologyAB
```

The benchmark uses the same L6 simulation topology, L7 terrain, sea level, retained L6 ocean mask, physical threshold area, and terrain seed for both modes. Render topology and terrain evaluation happen once and are excluded from the two hydrology totals because they already exist when runtime hydrology starts.

| Metric | Legacy L6 + refinement | Dedicated L7 |
|---|---:|---:|
| Hydrology nodes | 40,962 | 163,842 |
| Additional full topology builds | 0 | 0 |
| Candidate reaches | 5,771 | 7,157 |
| Visible reaches | 5,597 | 7,157 |
| Suppressed reaches | 174 | 0 |
| Corridor failures | 10 | 0 |
| Unresolved depressions at rendering | 164 | 0 |
| Inland visible terminations | 125 | 0 |
| Ocean-connected chains | 749 | 697 |
| Filled cells | 2,034 | 8,715 |
| Candidate basins | 379 | 468 |
| River mesh vertices | 398,812 | 57,256 |
| Core/path retained estimate | 3,709,740 B | 15,736,758 B |
| Priority flood | 1,201.0 ms | 219.4 ms |
| Flow accumulation | 2.0 ms | 7.9 ms |
| Lake topology | 13.5 ms | 8.2 ms |
| Lake geometry | 1.8 ms | 454.7 ms |
| Path extraction, excluding shore/coast | 10,586.4 ms | 106.7 ms |
| Shore/coast processing | 461.3 ms | 184.6 ms |
| Final-detail ribbon projection sample | 540.5 ms | 78.6 ms |
| Total | 14,074.7 ms | 1,174.1 ms |

The dedicated benchmark constructs no second `GeodesicGridTopology`. Its adjacency build took 82.1 ms and mapped-ocean construction took 2.1 ms. Ordinary direct paths have only two hydrology directions before visual resampling, which explains the smaller ribbon vertex count.

The filled-cell result is higher than the earlier independent-L7-ocean probe. This A/B deliberately maps the retained L6 ocean authority as required; high-resolution below-sea vertices mapped to L6 land remain land depressions instead of becoming a newly classified L7 ocean. The result should not be hidden or tuned toward the earlier probe. Runtime diagnostics now expose filled cells and basin counts so the scene comparison can determine whether these are valid coastal/inland depressions.

Default Earthlike benchmark thresholds selected no visible lakes in either mode, so the seed run could not exercise outlet counts. A synthetic high-resolution basin test verifies an identity-mapped visible lake, a neighboring spill edge, deterministic receiver rewiring, and outlet accumulated flow greater than or equal to every incoming flow. Runtime reports unique expected/rendered/suppressed lake outlets and separates below-threshold, projection, shoreline, topology, and flow-conservation failures.

## Runtime diagnostics

`GeodesicRiverSystem` emits:

- `[GeodesicHydrologyTopology]`: simulation/render/hydrology subdivisions, vertex/triangle count, topology source, radii reuse, retained core bytes.
- `[GeodesicDrainage]`: threshold area, candidate/visible reach and mesh counts, filled cells, failure/continuity categories, and ordinary versus lake corridor failures.
- `[GeodesicHydrologyPerformance]`: adjacency, ocean mapping, priority flood, accumulation, lake topology, lake geometry, path extraction, shoreline/coast, ribbon geometry, and total generation time.
- `[GeodesicLakeOutlets]`: unique expected/rendered/suppressed outlets with reason counts and outlet-flow violations.

The remaining causes of visible gaps are lake shoreline/projection rejection and intentional thresholding. Ordinary dedicated receiver edges cannot fail corridor refinement because that algorithm is not invoked for them. The legacy anchor, spill, and refinement implementation remains reachable only through the migration fallback and can be removed after interactive A/B validation.

## Validation status

The pure hydrology suite passes 59 tests. The runtime and EditMode C# assemblies compile against Unity 6000.3.19f1. An interactive seed-123456 screenshot comparison was not captured during this implementation because the project was already open in another Unity instance, which prevents a second batch Unity process from opening it. Use the existing scene with `useLegacySimulationHydrology` toggled to capture A and B from the same camera; C requires a temporary L7 simulation reference run.
