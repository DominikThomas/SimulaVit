# Rejected uphill river visualization

This change observes the **current continuous TIN implementation**. It does not restore the inactive historical corridor router, change routing or adjust tolerances.

## Startup

The active built-in startup screen now has a separate expandable **Debug Settings** section alongside **Advanced**. Its **River Debug** subsection contains **Show rejected uphill river segments**, default **OFF**. Help text explains that these are diagnostic reaches and never valid drainage routes. Severity colouring is always enabled; there are no numeric controls.

The field follows startup JSON persistence (schema 11). Older schemas explicitly load it as OFF. The alternate serialized `SimulationStartupPanel` has matching optional button/root/toggle bindings for custom scenes; `PlanetScene` uses the built-in screen. Advanced resets do not change the debug preference.

## Observation and rendering

- The existing `AcceptStep` rejection and return remain unchanged. An optional observer retains a failed trace only during visible-network extraction, never during runoff accumulation. If the trace subsequently joins a valid trunk before that failure, the discarded tail is not displayed.
- Trace failures display the actual failed continuation, leaving the accepted prefix cyan. Full original trace samples are available temporarily for double-precision hydrological grade measurement.
- Entire raw or projected centrelines rejected by the production renderer's grade checks are retained as rejected reaches. Rejected optional smoothing candidates are not drawn when a valid fallback survives.
- The terrain audit records the same facet crossings and analytic completed-surface radial extrema it evaluates. These become overlay points; this is not a straight source-to-receiver approximation.
- Basins, unresolved flats, failed joins and other failures without a rejected grade candidate do not invent a diagnostic connection.
- The overlay owns a separate mesh, material and child object. It never enters `RiverPaths`, networks, mouths, lakes, receivers, runoff or drainage areas. Rebuild/clear/disable releases or hides it.
- Raw triangle mode uses the same pixel lines as accepted rivers. Ribbon mode uses the same flow-derived widths, width cap, projected banks and surface offset. Colours are opaque and unlit. Raw line mode, like ordinary raw rivers, does not encode width.

## Severity

Each reach retains source/receiver triangle IDs, rejection category, maximum positive sample step, total positive rise and maximum meaningful uphill slope. Metrics inspect hydrological, interpolated visible and exact completed-surface grades; the maximum per metric is retained rather than adding the same rise across three representations.

Surface distance is `planetRadius * atan2(|cross(unitA, unitB)|, dot(unitA, unitB))`. Slope is positive rise divided by this distance. Sub-epsilon single steps and nearly zero-length steps do not dominate the slope. Consecutive tiny rises are also measured over their combined distance once their accumulated rise exceeds epsilon.

The production epsilon is unchanged: `min(max(0, uphillTolerance), 0.000002, radius * 0.00000025)`. The existing visible bump allowance remains additional to epsilon. Only actual production rejections are observed; severity measurement never makes the rejection decision.

Fixed logarithmic colour anchors (dimensionless rise/distance):

| Colour | Slope |
|---|---:|
| Yellow | <= 0.0001 |
| Orange | 0.01 |
| Red | >= 0.1 |

Colours interpolate continuously between these anchors. Summary counts use midpoint bands: yellow `< 0.001`, orange `[0.001, sqrt(0.001))`, red `>= sqrt(0.001)` (approximately 0.0316228). No per-planet normalization occurs.

`[GeodesicRiverUphillDebug]` reports counts, maximum/median step and slope, maximum accumulated rise, retained path points, mesh vertices, overlay-build milliseconds and the fixed scale. Normal river diagnostics remain intact. Per-reach data are exposed through `RejectedUphillDebug`; no new interactive picker was added.

## Cost

OFF retains no rejected paths, collection, mesh or material and performs no diagnostic measurements. It adds optional-observer/null checks at extraction/rejection/sample collection points; it does not retrace or resample terrain. Timing impact has not been quantified.

ON copies failed trace candidates, measures three height representations over crossed facets, retains overlay paths and uploads a second mesh. Cost scales with rejected samples/facet crossings. Raw mode uses one vertex per point and two indices per edge; ribbon mode uses three vertices per point and twelve indices per edge. Temporary trace arrays are released after extraction. The capture report measures total visual rebuild OFF/ON time and approximate retained managed-memory difference; the latter excludes native/GPU storage and is not a peak-allocation measurement.

## Validation and captures

Run `Tools/ValidateTinHydrology.py --unity-data <Editor/Data>` for managed hydrology tests. It now selects Windows Mono/framework assemblies correctly on Windows. Native mesh and saved-config tests are in `GeodesicRiverUphillDebugIntegrationTests` and `SimulationStartupConfigTests`.

For a generated seed **123456** planet, choose **Tools > Hydrology > Capture Uphill Debug OFF-ON (seed 123456)**. It uses the current camera, renders A/B synchronously with identical terrain/settings, checks accepted mesh vertices/indices/colours plus receivers/runoff/areas/mouths/lakes, and restores the original toggle/time scale. Output is `Artifacts/RiverUphillDebug/<timestamp>/`.

`RiverUphillDebugCapture.CaptureBatch` additionally supports an isolated project copy: it opens `PlanetScene`, generates seed 123456 with the scene's current terrain and subdivision settings, and positions one fixed comparison camera. It never saves the scene or startup preferences. A batch capture is distinct from the user's currently running planet.

### Results (2026-10-26)

- Runtime/editor compilation succeeded in Unity 6000.3.19f1.
- **9/9 focused native Unity tests passed**, including exact accepted mesh vertices/indices/colours, explicit diagnostic vertex colours, OFF/ON/OFF cleanup, runoff/receivers/areas/terrain/mouth/lake invariants in **both raw-line and ribbon modes**, and startup JSON migration/round-trip.
- Managed suite: **89 passed, 1 failed**. The failing `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted` also fails on untouched HEAD (**83 passed, the same 1 failure**). Its tolerance/routing implementation was not changed.
- The nonempty synthetic native fixture retained **1,280** rejected reaches: **0 yellow / 1,208 orange / 72 red** by the summary bands. It generated **4,018 line vertices** or **12,054 ribbon vertices**. Overlay construction alone took **8.97 ms** / **35.48 ms**, respectively; these timings exclude diagnostic collection/measurement and are single fixture runs.
- Actual seed-123456 A/B captures are in `Artifacts/RiverUphillDebug/20261026-224605/`. They use the checked-in `PlanetScene` settings, L6 simulation / L7 rendering, default river threshold 8, inland-sea filtering OFF, and the current TIN router. These are an isolated scene reconstruction, not the user's live unsaved planet.
- That planet produced **0 rejected uphill reaches: 0 yellow / 0 orange / 0 red; 0 debug vertices**. Production diagnostics independently report zero rejected hydrological, visible or post-processing grade checks. They report **118 closed-basin terminations**, which are deliberately not coloured by this feature.
- A/B images were generated and inspected, and the capture's exact hydrology/accepted-mesh invariants passed. **Visual acceptance of the coloured overlay on this seed remains unverified because there are no qualifying rejected reaches.** No settings were adjusted to manufacture a red network.
- Visual rebuild time was **212.83 ms OFF / 218.12 ms ON** in this empty-overlay comparison. The managed-memory difference was **-45,056 bytes**, i.e. allocation/collection noise rather than evidence of a saving. This run cannot establish the cost of a populated overlay. OFF retains no diagnostic geometry; populated ON memory remains proportional to retained samples and mesh vertices/indices.

Native XML, managed output and the untouched-HEAD comparison are retained alongside the captures in `Artifacts/RiverUphillDebug/`.

### Compiler-error repair requested during implementation

The supplied compiler-error list traced to duplicate `*1.gen.cs` files in `Library/PackageCache/com.unity.collections@9796e5ee0d9e`. All 13 duplicates matched their original source after encoding/line-ending normalization. They and their metadata were moved to `work/package-cache-quarantine/collections-duplicates/`, outside Unity's compiled package. Originals, package versions and manifests were preserved. The same exclusion had already allowed the isolated native validation run to compile and pass. The user's open editor must refresh before its old console entries disappear.

## Changed code

- `SimulationStartupConfig`, `SimulationStartupController`, `SimulationStartupPanel`: setting, UI, application and persistence.
- `GeodesicTinHydrology`: optional observation at existing trace rejection, delivered only for retained failed tails.
- `GeodesicRiverTerrain`: optional collection of existing audit samples and diagnostic measurement.
- `GeodesicRiverUphillDebug`: metrics, fixed colour mapping and log summary.
- `GeodesicRiverSystem`: capture, separate overlay, lifecycle and public inspection data.
- `RiverUphillDebugCapture`: same-camera capture and state invariants.
- Focused debug/config/integration tests and the managed validation runner.
