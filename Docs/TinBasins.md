# TIN basin topology

`GeodesicTinBasins.Build(activeTin)` is an optional, read-only topology audit. It does not execute during startup or river generation. The river component offers **Audit TIN Basin Topology** (summary) and **Log Selected TIN Basin And Termination Matches** (selected floor-vertex ID plus detailed closed/noise matches). There is no water rendering, outlet creation, river clipping, receiver rewriting, or debug mesh in this step.

## Terrain audit and method

The active `GeodesicTinHydrology` fits a linear scalar field from `HydrologyHeight` on each render facet. Visible mode supplies the same heights as `VisibleHeight`. `CompletedVisibleRadius` is a separate geometric acceptance/coast check; it does not define the gradient's scalar field. Basin construction reads the active hydrology heights, existing triangle incidence, adjacency, valid-face flags, local areas and `IsOcean` checks. It never calls the historical `GeodesicLakeBasins.Build` or builds `GeodesicDrainageGraph`.

`TraceInto` classifies failed downhill continuation using local incident heights and separately attempts flat routing. Its closed/noise labels are not complete depression boundaries. Basin detection therefore runs independently of runoff, networks, receivers and trace outcomes. Only the subsequent diagnostic matching reads network terminations.

For a linear triangular field, sublevel components connect along mesh edges and topology changes at vertex elevations. The detector sorts vertices by exact active elevation, then vertex ID, and unions incident active components. An external component wins over inland components; otherwise the lower minimum wins, with floor ID breaking ties (the elder rule). A losing inland component records its first connection to the older component at the actual saddle vertex. Zero-depth transient components from equal-height ordering are suppressed; all positive-depth components survive irrespective of visibility thresholds. This is a lower-star merge-tree sweep, not a vertex drainage network.

An open-boundary vertex is external only when the active TIN permits boundary outflow. Retained ocean vertices use the active `IsOcean` predicate on incident facets. Ocean components never become freshwater basins, fully ocean facets are omitted from footprints, and ocean points never match basin diagnostics. Below-sea minima not classified as ocean remain raw topology with `SubseaUnclassified`; they do not automatically qualify for freshwater visibility. No ocean masks or thresholds change.

## Descriptor semantics

- IDs are floor vertex IDs, stable for identical indexed TINs/settings, not guaranteed across retriangulation or subdivision changes.
- `Floor` and `Spill` use exact vertex barycentrics on named facets. `DownstreamTriangle` and `DownstreamVertex` identify an incident edge on the winning side before union. Equal-height saddles may have a level downstream edge. For immediate boundary escape there may be no downstream triangle (`-1`). These are geometry witnesses, not routed outlets.
- `ParentBasinId` identifies the elder component's eventual descriptor. Its larger footprint includes merged children. `-1` means external escape or an unmerged terminal root. This compact elder-rule hierarchy preserves merge elevations without creating a separate node for every merge.
- `HasEscape` means a connection to an older/downstream component, including a parent that may ultimately be terminal. `Terminal` means the remaining closed root with no such escape. Follow parents to determine ultimate endorheic fate.
- Terminal spill elevation/depth are `NaN`, with no spill point. No arbitrary rim height is invented. Such roots remain inspectable; they cannot be visibility-qualified until a water level is supplied by a future system.
- Footprints contain facets incident to the component's vertices strictly below its spill. They conservatively cover partial wet facets; they are **not** a catchment, exact shoreline, or a water surface. Terminal footprints cover their entire connected component. Later shoreline code must clip to spill/current level and retained ocean. Full-facet areas are upper estimates. Nested footprints overlap, so diagnostics report both unique `totalBasinArea` and `summedFootprintArea`.
- Area/depth flags use caller-supplied thresholds (defaults 0.0001 of valid TIN area and 0.005 height units). They never delete raw topology. Terminal depth statistics are excluded because depth is unknown. `TouchesRetainedOcean` records local geometric adjacency, not eventual connectivity to ocean through downstream terrain.
- Matching checks point elevation, conservative footprint, and ocean status, selecting the smallest spill level before its ancestors. Only `TrueClosedBasin` and `TinyNoiseDepression` are reported as basin termination diagnostics; routing failures are never promoted to basins.

Construction costs sorting plus incidence unions and materializing footprints. Deeply nested components can repeat footprint storage. This intentionally simple representation is suitable for optional auditing; no dynamic filling or simulation state is introduced.

## Validation and seed 123456

Run:

```powershell
python Tools/ValidateTinHydrology.py --unity-data 'C:/Program Files/Unity/Hub/Editor/6000.3.19f1/Editor/Data' --output Artifacts/TinBasins/managed --benchmark 7
```

The existing benchmark reconstructs seed 123456 with Earthlike sampling, simulation-threshold subdivision 6, render subdivision 7, Visible heights, no filtering and the unchanged threshold 8 (area threshold 0.157071963). It is **not** the completed live scene with all serialized settings and terrain edits. Lakes remain visually disabled. Generated logs/compilation products are ignored under `Artifacts/TinBasins/`.

Managed fixture results:

| Metric | Value |
| --- | ---: |
| Raw / overflowing / terminal basins | 1600 / 1600 / 0 |
| Future visible candidates | 1 |
| Shallow / nonqualifying | 1599 / 1599 |
| Ocean-adjacent footprints | 0 |
| Unique / summed footprint area | 100.361525 / 121.506566 |
| Maximum / mean known depth | 0.00557804108 / 0.000883079469 |
| Closed-basin terminations matched | 316 / 316 |
| Tiny-noise terminations matched | 19 / 19 |

Largest conservative footprints (planet-local area and height units):

| Basin ID | Area | Depth |
| --- | ---: | ---: |
| 148988 | 1.64772079 | 0.0046081543 |
| 17111 | 1.33087825 | 0.00441789627 |
| 120659 | 1.27486447 | 0.00306129456 |
| 140897 | 1.19275836 | 0.00417041779 |
| 117935 | 1.1717499 | 0.00377845764 |

Ten focused basin tests cover the bowl footprint and exact spill, two saddle levels, nested terminal hierarchy, ocean escape and downstream geometry, flat ties, tiny depressions, deterministic reconstruction, active-height authority, excluded inland water, and exact preservation of receiver/runoff/area/height/network/path/ocean state. All pass. The broader managed run reports 110 passed and one failure: `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted` (expected 13, got 0). An isolated compile/run of the committed baseline reproduces this same failure (100 passed, one failed). The validator still returns failure for this existing regression; requesting a benchmark now runs it even if a test fails, without changing the exit status.

Native Unity integration, context-menu interaction, and a live-scene screenshot have not been run. Visualization is intentionally deferred; diagnostics and tests are the deliverable for this topology-only step.
