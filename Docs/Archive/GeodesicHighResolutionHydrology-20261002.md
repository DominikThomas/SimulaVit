ARCHIVED FAILED EXPERIMENT: superseded by the 6f48085 restoration. These results do not describe the current runtime and did not receive Unity visual acceptance.

# Terrain-following Geodesic rivers

## Corrective routing after 55e5cf4

The reference history is `6f48085` (earlier terrain corridor routing), `e4510e3` (raw render-topology edges), and `55e5cf4` (failed valley snap and spline attempt).

The regression was a correctness bypass in `GeodesicLakeRiverRouting.Build`: ordinary high-resolution reaches skipped `GeodesicRiverPath.Refine` and used an unconditional two-point edge or a spline. A monotonically descending **filled** graph was treated as sufficient evidence to draw a river over dry terrain. It is not: priority flood intentionally crosses depressions virtually. The latest spline also retained unresolved visible uphill reaches. Its coarse endpoint/ridge diagnostics did not validate the final rendered polyline.

The corrected runtime restores the earlier bounded terrain-aware corridor search. It now uses completed visible terrain for both drainage elevations and path acceptance. The existing lightweight render-resolution adjacency remains the default because applying a strict visible-terrain rule to the coarse graph lost many real valleys and produced more gaps. `SimulationTerrainCorridor` remains available as a coarse fallback. The unsafe raw/spline switches are removed, so existing serialized false values cannot preserve the failed default.

## Authority and geometry

- Simulation, chemistry, biology, temperature, and ocean layers stay at their configured simulation resolution (L6 in validation).
- Hydrology reuses the existing L7 render directions and triangles through packed adjacency; no second full `GeodesicGridTopology` is built.
- Priority flood uses an array of completed visible vertex radii. Simplified `HydrologicalRadii` remain available to other code but no longer override the terrain that the rivers must descend on.
- The retained simulation ocean mask still owns ocean component identity. Existing mapped shoreline and inland-sea filtering are preserved.
- Internal river controls may move toward the adjacent chain controls only when the complete terrain route on both sides descends. Sources, confluences, ocean approaches, and lake boundaries remain fixed.
- The restored corridor search retains shared endpoints. It tests the visible height at each candidate and tracks the minimum reached so numerical allowances cannot accumulate into an uphill route.
- A final gate intersects each path arc with the hierarchical terrain triangles and tests every entry/exit altitude. Within each triangle, the interpolated radial altitude is monotone along the arc. This catches narrow ridges that uniform sampling can miss.
- The height tolerance remains `0.000002` planet-local units. Altitude checks exclude spherical triangle chord sag; exact ray/triangle radius is still used to place the ribbon on the actual mesh.
- Height interpolation uses double precision on height differences to avoid creating artificial micro-steps on flat terrain. Terrain vertices are not modified.

The earlier local snap/spline helper is retained solely for the offline failed-version comparison. Runtime river generation no longer calls it.

## Lakes and interruptions

Significant real filled basins still produce static lakes when `Generate Hydrological Lakes` is enabled. Area/depth thresholds and user configuration are preserved. Failed path searches never create a lake.

Inlets and outlets intersect the authoritative river/spill edge with the actual lake shoreline, rather than snapping sideways to an arbitrary nearest shoreline vertex. A short, verified downhill shoreline connector avoids sending a spill path back into the pool. Submerged internal receiver edges are lake connections, not dry river ribbons.

Small basins below the configured lake thresholds, disabled lakes, and unresolved corridors can still interrupt rivers. They are explicitly counted; their virtual flood routes are not rendered uphill. This prioritizes physically valid visible paths over artificial continuity. Following all visible relief exposes more small basins than using simplified terrain, so interruptions remain a known limitation.

## Validation

Run:

```powershell
pwsh -NoProfile -File Tools/ValidateGeodesicLakes.ps1 -DownhillRouting
pwsh -NoProfile -File Tools/ValidateGeodesicLakes.ps1 -HydrologyAB
```

The first runs the current route; the second also reconstructs ordinary routing from the earlier corridor, raw L7, and failed spline modes using the same deterministic terrain fixture. These are algorithm comparisons, not screenshots or exact historical Unity builds. Shared numerical interpolation improvements can slightly change old reference counts.

Seed 123456, Earthlike fixture, simulation L6, render L7, threshold 8 simulation-cell equivalents (area `0.157071963`), lakes enabled:

| Current route metric | Result |
|---|---:|
| Candidate reaches | 7,328 |
| Rendered reaches | 4,571 |
| Major rendered reaches | 1,228 |
| Final rendered uphill reaches | 0 |
| Largest sampled uphill excursion | 0.00000190735 (below tolerance) |
| Filled hydrology cells | 13,255 |
| Candidate basins | 1,104 |
| Visible lakes / river inlets / rendered outlets | 1 / 1 / 1 |
| Suppressed lake outlets | 0 |
| Lake-connected reaches | 36 |
| Unrendered depression reaches | 2,630 |
| Unresolved dry corridor reaches | 93 |
| Inland visible terminations | 1,409 |
| River mesh vertices | 335,314 |
| Core/path memory estimate | 18,916,194 bytes |

The benchmark independently resamples every final segment at 31 subdivisions and compares it with the lowest earlier visible altitude. It does not reuse the production triangle-crossing validator or rely on endpoint/same-anchor self-comparisons. The failed spline reference had approximately 3,390 final paths with excursions over the same tolerance, despite its earlier endpoint diagnostic reporting only about 502 unresolved edges.

The focused run took about 10.6 seconds excluding its 4.8-second independent dense audit: about 0.26 s priority flood, 0.95 s shared controls, 7.84 s path search, 0.35 s shoreline/coast work, and 0.49 s ribbon projection sampling. Timing is a pure-algorithm measurement, not a Unity frame-time measurement. It is intentionally slower than drawing unchecked graph edges. Detailed results are recorded in `GeodesicRiverRoutingValidation.txt`.

The pure suite has 64 passing tests, including a visible uphill receiver under a descending filled graph, a narrow ridge missed by uniform samples, accumulated tiny rises, locked confluences, lake shore identity, lake outlet/inlet grade, retained/excluded seas, and unchanged terrain. Runtime and EditMode assemblies compile with the installed Unity Roslyn compiler. Native Unity Test Runner and a rendered scene comparison have not been run for this correction; the earlier batch licensing attempt was blocked and has not been repeated.

## Runtime inspection

Regenerate the planet to rebuild cached drainage and ribbons. The default routing field requires no Inspector reset. Existing startup lake settings still apply.

`[GeodesicRiverFinalPathAudit]` reports the selected route, rendered count, rendered uphill count, maximum excursion, and numerical tolerance. The uphill count must be zero. Existing drainage, lake outlet, density, and performance logs report the remaining breaks and their causes.
