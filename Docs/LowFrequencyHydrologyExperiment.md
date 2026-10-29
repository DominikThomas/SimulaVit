# Low-frequency hydrology experiment

Checkpoint: `0a9480f` (`WIP - Add rejected uphill river diagnostics`). Experimental
branch: `codex/test-low-frequency-hydrology-terrain`. No history was rewritten and
nothing was merged or committed. Pre-existing staged artifacts and validation copies
were left untouched; they must not be included in a future source commit.

## Audit and exact frequency definition

| Sampler component | Existing definition | Experimental definition |
| --- | --- | --- |
| Continents | 3 fBm octaves, persistence .55, lacunarity 2; configured scale, amplitude and bias | Unchanged |
| Domain warp | Three single-band noise samples, configured scale and strength | Unchanged |
| Mountain mask | 3 fBm octaves at mountainScale * .35, persistence .5, lacunarity 2; coverage/softness and continent gating | Unchanged |
| Ridges | Configured 1-8 fBm octaves; normalized by amplitude sum, folded ridge and sharpness | Cap configured octaves at 2; preserve persistence/lacunarity, normalization, transform and amplitude |
| Explicit fine detail | 2 fBm octaves, persistence .45, lacunarity 2.1 | Amplitude zero |
| Final height | Signed height contrast and min/max offset clamp | Unchanged |

`PlanetTerrainSampler.HydrologyLowFrequencySettings` derives a value copy of
`PlanetTerrainSettings`; `EvaluateHydrologyLowFrequency` calls the existing evaluator.
No noise equations are duplicated and the normal evaluator is unchanged. The generator
supplies its existing `DerivedTerrainSeed`, including the terrain-domain derivation or
custom-seed override. On the actual scene, ridge octaves change from 5 to 2 and fine
amplitude from .018 to 0. Continent and mountain-mask values remain identical.

The older `PlanetTerrainSample.LargeScaleHeightOffset` and
`PlanetGenerator.GeodesicHydrologicalRenderRadii` remove qualifying fine detail only,
retaining all ridge octaves. They serve the historical drainage path. Current
`InitializeRenderHydrology` instead feeds completed visible mesh vertex magnitudes to
`BuildAdaptive`, with existing retained-ocean mapping. `GeodesicRiverTerrain` already
accepts separate routing and visible radii, and projects onto completed visible facets.
It requires no changes for this experiment.

## Implementation and invariants

The Advanced option **Use low-frequency terrain for hydrology** defaults OFF. Startup
saves use schema 13; older saves explicitly default OFF. Advanced reset clears it;
normal reset preserves it. The optional custom startup-panel binding is also supported.

OFF makes the original TIN builder call without a supplied drainage field. ON samples
a separate render-vertex radius array. Retained-ocean vertices keep their completed
visible radii; disabled terrain displacement retains the entire visible field. No
visible mesh, ocean mask, sea level, bathymetry, or lake setting is modified.

The existing optional weak adaptive filter operates on the selected input. Backoff
returns toward that input, never silently replacing the experiment with visible heights.
Continuous-gradient routing, receivers, accumulation, visible-grade rejection, projection
and termination handling are unchanged. Smoother drainage can still be rejected against
the rough visible surface; no acceptance tolerances were relaxed.

`[GeodesicHydrologyTerrain]` reports unconditioned input **radii**, before the optional
adaptive filter: sample count, min/max/mean/population standard deviation, mean absolute
and RMS difference, and mean absolute adjacent-vertex height difference. All numbers
are in planet units. Roughness includes ocean and coast edges; each edge occurs twice
on the closed render TIN, which leaves the mean unchanged. Existing TIN logs retain
their filter, routing, termination and visible-grade diagnostics.

## Repeat the comparison

Generate master seed **123456**, simulation subdivision **6**, render subdivision **7**.
Keep every other setting identical. Toggle the generator option and use the existing
GeodesicRiverSystem context action **Rebuild Rivers For Current Terrain** to rebuild
only rivers. This action reapplies uniform runoff, as it already did before the change.

Use **Tools / Hydrology / Capture Low-Frequency Terrain A-B (seed 123456)** on an
initialized planet. The helper pauses time, rebuilds OFF and ON, captures matching
camera pairs at four orientations, a close-up centered on the longest baseline candidate,
and visible-grade overlays. It saves settings, termination records and a comparison
table under ignored `Artifacts/LowFrequencyHydrology/<timestamp>/`.

The helper verifies exact visible vertices/indices, simulation ocean mask, TIN ocean
classification, sea level and paused lakes, then verifies that OFF restores exact
receivers/runoff. Original option/debug/camera/time settings are restored in `finally`.
For batch capture in a disposable project, execute
`LowFrequencyHydrologyCapture.CaptureBatch`. It opens PlanetScene, applies the standard
seed/subdivisions and generates once without saving the scene or startup preferences.

## Tests

Unity 6000.3.19f1 native EditMode selection: **129 passed, 1 failed** (130 total).
All six new tests passed. They cover defaults/migration/persistence/reset, OFF array
identity and exact rebuild restoration, deterministic finite values, unchanged broad
components, lower roughness, separate TIN gradients, valid receivers/areas/runoff,
exact mesh/mask preservation, unchanged sea level and paused lakes.

Managed current sources: **97 passed, 1 failed**. A fresh managed run compiled from
original HEAD `0a9480f`: **93 passed, 1 failed**. The same pre-existing failure in all
runs is `GeodesicRiverGradeTests.DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted`
(expected 13, got 0). It was not changed or suppressed. Relevant TIN, topology, drainage,
lake, ocean-connectivity, visible-grade, uphill-debug and startup-config tests were
included. No lake tests were edited. `git diff --check` passed.

Managed command: `python Tools/ValidateTinHydrology.py --unity-data "<Unity Editor>/Data"`.
Generated compilation files default to `Artifacts/LowFrequencyHydrology/managed`;
`--output` overrides that location. Native XML/logs and managed baseline results are
under `Artifacts/LowFrequencyHydrology/`. Tests and screenshots use a new disposable
Unity project there, preserving the older validation copy.

## Actual seed-123456 results

163,842 render vertices and 327,680 TIN triangles. Original scene terrain/ocean settings
were retained; this is not a reconstruction using generic Earthlike settings. Two
captures produced identical non-timing metrics. The OFF result also matches the earlier
visible-grade checkpoint capture (247 networks, 78 ocean outlets, 118 closed-basin
terminations and 5 audited visible uphill segments).

| Metric | Visible input | Low-frequency input | Change |
| --- | ---: | ---: | ---: |
| Mean absolute neighboring height difference | 0.0124045387 | 0.0110565082 | -10.9% |
| Depression/fill count (TIN does not fill) | 0 | 0 | unchanged |
| Tiny/noise depression terminations | 0 | 1 | n/a; baseline zero |
| Unrouted active triangles | 2 | 4 | +100.0% |
| Candidate TIN networks | 247 | 82 | -66.8% |
| Visible river reaches/chains | 247 | 79 | -68.0% |
| Ocean-connected rendered mouths | 78 | 14 | -82.1% |
| Unexpected/broken chains (existing TIN diagnostic) | 1 | 40 | +3900.0% |
| Audited visible uphill segments | 5 | 9 | +80.0% |
| Audited uphill length fraction | 0.216889% | 3.513771% | +3.296882 percentage points |
| TIN material-uphill / ridge crossings | 0 / 0 | 0 / 0 | unchanged |
| Closed-basin terminations | 118 | 12 | -89.8% |
| Flat terminations | 0 | 0 | unchanged |
| Rejected visible steps | 0 | 60,701 | n/a; baseline zero |
| Confluences | 51 | 18 | -64.7% |
| Mean TIN candidate length | 0.151495445 | 0.128556405 | -15.1% |
| Maximum TIN candidate length | 0.952429537 | 0.613491674 | -35.6% |

Visible radius min/max/mean/std = 7.77689505 / 8.51040173 / 7.98178431 / 0.190815317.
Low-frequency input = 7.77689505 / 8.49637699 / 7.97490994 / 0.182475970.
Mean absolute difference = 0.0118793732; RMS difference = 0.0278601065.
Adaptive filter strength was zero in both runs.

Counter interpretation: current TIN performs no priority filling; fill count zero is
not evidence of improvement. Noise and closed depressions are trace terminations, not
unique basin counts. Candidate networks precede final rendered-path validation. Visible
chains use actual `RiverPaths.Count`; historical `RiverNetworks` can include rejected
paths. Ocean-connected rendered mouths and TIN ocean terminations coincide here. Broken
chains are the existing diagnostic, including unresolved active components and rendering
breaks. The independent visible-grade audit differs from TIN's material-uphill/ridge
counters. Mean/maximum lengths describe TIN candidates, not only rendered paths.

**Preservation passed:** exact visible vertices and triangle indices, authoritative
simulation ocean mask, TIN ocean classification and sea level stayed identical. Lakes
remained paused. Switching back OFF restored exact receivers and runoff arrays.

## Visual assessment and conclusion

Final screenshots/report: `Artifacts/LowFrequencyHydrology/20261028-210556/`.
The initial capture at `20261028-210225/` has the same metrics without the close-up.
Whole-planet screenshots, especially view 2, show a substantially sparser network.
The matching close-up shows a locally smoother, straighter trace, but no richer dendritic
tributary structure. Neither the images nor the shorter network lengths and lost ocean
outlets support accepting this as a better network. Small ridge crossings are difficult
to assess visually on the dark surface; the independent grade audit instead records
more uphill segments and a much larger uphill length fraction.

**This definition does not improve the network under the current TIN and visible-surface
constraints.** The drainage input is smoother, but many directions conflict with the
unchanged visible surface and are rejected. Fewer closed-basin terminations are not a
success when most visible chains, ocean outlets and confluences disappear and unexpected
breaks increase. The retained visible-grade guards mean this is a negative result for
the requested input-only experiment, not a proof that terrain frequency is irrelevant.
The option remains OFF by default. No routing relaxation, carving, erosion, lake rework,
or follow-on terrain work was performed.

## Files changed

- `Assets/Scripts/Planet/Geodesic/Terrain/PlanetTerrainSampler.cs`: derived settings and reusable sampler entry point.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicHydrologyTerrain.cs` (+ meta): separate input array and statistics.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicTinHydrology.cs`: optional input to the existing adaptive builder.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverSystem.cs`: input selection, summary and source label.
- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs`: default-OFF option and internal access to existing settings.
- `Assets/Scripts/Core/Startup/SimulationStartupConfig.cs`, `SimulationStartupController.cs`, `SimulationStartupPanel.cs`: Advanced UI, application, save migration and reset behavior.
- `Assets/Tests/EditMode/GeodesicHydrologyTerrainTests.cs`, `GeodesicHydrologyTerrainIntegrationTests.cs` (+ metas): six focused tests.
- `Assets/Editor/HydrologyBaseline/LowFrequencyHydrologyCapture.cs` (+ meta): river-only A/B capture and preservation checks.
- `Tools/TinHydrologyValidation.cs`, `Tools/ValidateTinHydrology.py`: new managed tests and Artifacts output location.
- `.gitignore`: ignore experiment artifacts.
- `Docs/LowFrequencyHydrologyExperiment.md`: audit, definition, reproduction, validation and results.
