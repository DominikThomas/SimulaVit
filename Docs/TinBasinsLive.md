# TIN basin live visual validation

Open **Tools > Hydrology > TIN Basin Live Inspector** in the normal running scene. The tool only observes an already-generated planet; it does not generate a replacement or use the managed Earthlike fixture.

The overlay is off by default and editor-only. Select a basin ID, qualifying basins, top N by area/depth, or the basin at a closed-termination network index, then click **Build / Show selected overlay**. **Overlay OFF** removes all temporary geometry/materials. Closing the inspector or reloading assemblies also removes them. No scene assets are saved.

Wireframes show conservative complete-triangle footprints, **not water or a shoreline**. Green indicates future-visible candidates; orange depth rejection; yellow area rejection; red both; magenta terminal; gray subsea-unclassified; cyan other raw topology. White crosses mark floor vertices, pink marks the spill, blue marks its downstream triangle, and purple indicates a parent relationship. The F capture additionally marks the exact termination in cyan. Inspect the textual flags for overlapping reasons.

**Log selected basin and matching terminations** prints exact floor/spill vertex and triangle IDs, heights, depth, area, parent, escape/terminal flags, qualification/exclusions, downstream identity, and matching terminations with TIN barycentrics/directions. The river component's existing selected-basin context action uses this same report.

**Capture live acceptance set A–F** verifies seed 123456, simulation subdivision 6, render subdivision 7, Visible hydrology, threshold 2, and zero lake-mesh vertices. The historical `generateHydrologicalLakes` setting may remain true while the active TIN path renders no lakes; the capture records that distinction and does not change the setting. Actual scene and river settings are saved as JSON alongside screenshots.

Outputs go to ignored `Artifacts/TinBasinsLive/<timestamp>/`:

- A: unchanged main-camera terrain/river view.
- B: qualifying footprints, plus up to ten focused views.
- C/D: ten largest/deepest footprints, plus one focused view each.
- E: selected basin (or largest when the selection is absent), close-up.
- F: first matched closed-basin termination and its basin, close-up.
- `report.txt`: live summary, threshold categories, top-ten tables, selected termination details, connectivity/ocean/minimum checks, and before/after fingerprints.

A separate temporary camera produces captures. The live camera and river renderer are not rebuilt. Temporary meshes have no colliders, use terrain depth testing, and are removed in `finally`. Fingerprints include serialized planet/river settings, terrain and child render mesh vertices/indices/colors, active heights, receivers, runoff/areas, sources, networks/paths, and sampled ocean classifications. A mismatch fails validation. Numeric depth statistics omit unknown terminal levels. Threshold-rejection categories are mutually exclusive, with terminal then subsea exclusions taking precedence; individual flags are still printed per basin.

## Validation record

The runtime assembly and editor capture assembly compile against the project's Unity response files. The two added inspection-helper tests pass; the managed suite is 112 passed with the same previously confirmed baseline failure in `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted`.

Live statistics and visual acceptance must come from the actual capture report and inspected screenshots, not the earlier 1600-basin reconstructed fixture. See the final task report for captured results or any remaining live-editor blocker.
