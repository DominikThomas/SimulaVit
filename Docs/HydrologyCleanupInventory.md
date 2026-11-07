# Hydrology cleanup inventory

This inventory covers the pipeline after the sediment corridor pass. Keep any removals in a cleanup commit after manual Unity validation. The experimental path remains available for comparison. The recorded seed 123456 major-outlet runs attempted 12 candidates each and accepted zero. The latest replay used terrain hash `4fb741bd…`, recorded in [MajorOutletBreaching.md](MajorOutletBreaching.md).

## Removal candidates

| Code | Cleanup boundary |
| --- | --- |
| `GeodesicMajorOutletBreaching`, `GeodesicMajorOutletCorridors`, `GeodesicMajorOutletOptimization` | Retire the optional major-outlet branch in `GeodesicRiverSystem.InitializeCoreStages` together with the old coarse search, refinement, verification rebuilds, and adaptive regrading. Move `OceanComponentAt` to shared ocean classification first. The active sediment corridor pass and delivery audit use it. |
| `GeodesicMajorOutletPathComparison`, `GeodesicMajorOutletTraceEvidence` | Remove the rejected-profile slope comparisons, iteration traces, and adaptive departure diagnostics with the old planner. Keep the TIN coast intersection and the exposed-outlet checks used by `GeodesicBasinOverflowAudit`. |
| `GeodesicMajorNetworkDiagnostics` | Retire `LogMajorOutlets`, `MajorOutletBreaching`, and `InspectTinOutletAttempt` with the old planner. Keep `LabelOceanComponents`. `CaptureRankedInlandNetworks` and `ClosedNetworkGroups` still feed the editor's raw-network picker. Replace that picker with all-lakes delivery outcomes before removing the separate raw ranking. |
| `TinBasinsLiveWindow` | Remove the **Inspect outlet attempt for basin ID** action and its orange initial bed, blue revised bed, purple rebuilt trace, and search-frontier overlays when the old attempt data goes away. Keep basin and lake footprint inspection and the raw downstream-arrow view. Label the raw view as pre-corridor if it stays available. |
| `GeodesicRiverSystem.InitializeLegacyReference` | The private initializer has no callers. Its synchronous `InitializeRenderHydrology` wrapper is only called by that initializer. Remove those two methods together. The old lake pause flag is asserted by native tests and `LowFrequencyHydrologyCapture`. Update those references before removing the flag. Check the remaining public legacy graph accessors and the comparison tools separately. |

## Shared code to keep

- Keep the ordinary passes in `GeodesicValleyCarving`, including the active ocean-chain planner, terrain application, and coastal verification. `GeodesicOceanDrainagePlan` remains part of these passes and also supplies shared closed and coast classification.
- Keep `GeodesicTinHydrology.BuildAdaptive`. Its adaptive input filtering is part of the active TIN builder. The old major-outlet adaptive regrading is a separate experiment.
- Keep the all-lakes basin and parcel routing, both sediment renderers, the terrain and collider composition, and the delivery audit. Keep the native lake mesh and corridor connectivity regressions.
- Audit the repeated network extraction in `BuildAdaptive` and `RebuildRenderHydrologyVisualStages` separately. Consolidation must preserve the final river threshold, rejected-path callbacks, and final diagnostic timing. Parcel replay also feeds first-lake membership, so the existing replay and accumulation calls have different outputs.
- The `Tools/ValidateTinHydrology.py` standalone source list already misses dependencies introduced by the major-outlet and infill work. Either repair its source selection or document the native Edit Mode workflow as its replacement. Keep the underlying fixtures.

## Saved settings and tests

- The obsolete major-outlet controls are `enableMajorOutletBreaching`, `maximumMajorOutletDepth`, `maximumMajorOutletVolume`, and `maximumMajorOutletDegrees`. `SimulationStartupController` introduced them at saved-config version 21. Remove their active UI, copy, normalization, reset, and apply paths together when retiring the experiment. Treat old values as retired fields during deserialization. Preserve `enableTerminationValleyCarving`, its pass count, depth, width, runoff threshold, and debug toggle. Never translate an old major-outlet value into a sediment corridor limit.
- Verify that a version 21 save containing the retired fields still restores ordinary carving and lake visibility settings. Include a save with the old experiment enabled and keep the defaults for earlier saves.
- Retire `GeodesicMajorOutletTests` with the old planner. Its adaptive branch revision, per-iteration failures, competing grades, and exact seed departure tests characterize the old rebuild algorithm. Keep coverage for protected terrain, correct primary-ocean targets, cut and volume limits, rejected high terrain, and parcel conservation in the new planner. Keep `GeodesicOceanDrainagePlanTests`, `GeodesicBasinOverflowAuditTests`, and the ordinary-carving integration tests.
- Update the historical pause-flag assertions in `GeodesicLakeIntegrationTests` and `GeodesicHydrologyTerrainIntegrationTests` to assert active TIN behavior when the unreachable initializer and its public flag are removed. Update the affected baseline capture checks and retire the obsolete acceptance presets separately.
