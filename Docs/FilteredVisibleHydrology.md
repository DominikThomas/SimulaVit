# Filtered visible terrain river routing

The current river runtime uses the existing subdivision 7 render vertices and triangles when the simulation is at subdivision 6. The simulation, chemistry, biology, temperature, and ocean layers remain at subdivision 6. The old corridor code remains in `GeodesicRiverSystem` for historical comparison, but is bypassed in the L6/L7 configuration.

## Routing surface

The completed render mesh supplies vertex radii. One half-strength, inverse-angular-distance weighted neighbor pass produces a separate routing-height array. The visible mesh is not edited and procedural terrain noise is not resampled. The startup log reports mean absolute difference from both the visible radii and the previous large-scale `HydrologicalRadii`, plus sampled local ridge and valley retention. The filter is intentionally mild; visual acceptance must establish whether it removes enough micro-scale sinks without erasing ridges.

`GeodesicHydrologyTopology` builds six-neighbor adjacency and per-vertex spherical area from the render triangles. The existing priority flood virtually conditions sinks, and flow accumulation uses physical vertex area. The configured threshold is multiplied by the mean subdivision 6 cell area, so subdivision 7 adds spatial detail without multiplying the nominal stream density by four.

The retained subdivision 6 OceanMask supplies semantic ocean seeds. Subdivision 7 vertices must be below the actual sea-level radius. A graph flood through submerged subdivision 7 neighbors extends the retained components to the precise visible coastline, avoiding the nearest-L6-cell boundary as a routing barrier. Submerged components with no retained seed remain inland.

## Connectivity and drawing

Every vertex above the drainage-area threshold belongs to a channel. Each channel follows its receiver to the next channel or to the ocean; an intermediate edge is never silently suppressed by a per-reach grade gate. The river mesh uses a point inside an adjacent triangle for each receiver edge when that point does not rise over the visible endpoints. Receiver connectivity remains unchanged. This is a triangle-interior approximation, **not** full piecewise-linear gradient tracing. The shoreline endpoint is clipped to the visible sea-level crossing.

Runtime logs report chain counts, confluences, ocean and basin terminations, unexpected breaks, visible grade segments, ridge crossings, and generation stage times. Basin records are computed from the priority flood, but no lake mesh or lake receiver rewrite is enabled. A priority-filled vertex is not a visible lake.

## Visual acceptance

In a licensed Unity Editor, generate seed **123456** with simulation subdivision **6**, render subdivision **7**, rivers on, and lakes off. Place the main camera at a useful planet view and select **Tools > Hydrology > Capture Filtered L7 Rivers (seed 123456)**. The image and metrics are saved under `Artifacts/FilteredVisibleHydrology/<timestamp>/`. Inspect the screenshot for continuous branching systems, coast connections, valley agreement, and any icosphere-edge pattern. Compare that view with the archived 6f48085 and experimental L7 captures using the same terrain and camera. Do not enable visible lakes until the visual result is accepted.

The current execution environment has no Unity Editor entitlement, so compilation, EditMode tests, seed generation, and the screenshot cannot be completed here. Their results remain unverified.
