# Rendered river visible-grade audit

The startup **Debug Settings > River Debug** section retains **Show rejected uphill river segments** and adds two independent, persisted options, both OFF by default:

- **Colour rendered rivers by visible terrain grade** audits the final emitted centreline against the completed terrain and colours uphill portions yellow through orange to red. Downhill and effectively flat portions retain their normal colour.
- **Show river termination reasons** adds purple markers at rendered closed-basin endpoints and white markers at rendered unexpected routing-failure endpoints. It does not create lakes.

Startup persistence is schema 12. Loading older settings leaves both new options OFF. The optional `SimulationStartupPanel` also exposes the two settings.

## What is measured

The observer reads centreline positions after production line/ribbon generation, including its interpolation and projection. It samples the completed visible terrain facets along every emitted edge, including facet boundaries and interior radial extrema. Router acceptance, suppression and the production micro-bump allowance do not gate this audit.

Each sample preserves source triangle, rendered segment, directions, routing delta, visible delta, surface distance, routing slope, visible slope and visible-grade classification. Routing values use the routing interpolation reference at the same sampled locations; visible values use the completed surface. A rendered edge receives the worst material uphill grade found along it. Colour isolation duplicates vertices only when enabled, preserving every rendered primitive's positions and preventing colour bleeding into adjacent cyan edges. Sampling intervals may be finer than the original coloured mesh edge.

The numerical height epsilon is the larger of twice the measured 95th-percentile float-versus-double terrain sampling error and two float radius ULPs. No hydrology tolerance participates. Positive rises above epsilon are slight below slope 0.001, moderate below sqrt(0.01 * 0.1), and strong above that. Colour uses the existing continuous logarithmic gradient with yellow/orange/red anchors at slopes 0.0001/0.01/0.1. Uphill length percentage uses only material uphill sampled intervals, rather than the entire coloured original edge.

`[GeodesicRiverVisibleGradeAudit]` logs visible classifications, routing/visible mismatches, maxima, uphill length, epsilon/noise and declared versus rendered termination counts. The capture CSV retains the routing/visible pairs for detailed classification, including routing downhill + visible downhill and routing downhill/flat + visible uphill.

## Seed 123456 results

Captured the checked-in PlanetScene with simulation level 6 and render level 7, using the previous diagnostic's seed and fixed camera. Full output is in [the capture directory](../Artifacts/RiverVisibleGrade/20261027-183601/report.txt).

| Measurement | Result |
| --- | ---: |
| Rendered segments / sampled intervals | 973 / 1371 |
| Visible downhill / flat | 968 / 0 |
| Slight / moderate / strong uphill segments | 2 / 3 / 0 |
| Routing-valid but visibly uphill segments | 5 |
| Material uphill rendered length | 0.216889% |
| Maximum material uphill slope | 0.00232415952 |
| Maximum uphill height delta | 0.0000767104522 |
| Measured sampling noise, P95 | 0.000000860701398 |
| Audit height epsilon | 0.00000190734863 |
| Declared closed basins / purple markers | 118 / 118 |
| Unexpected network count / white markers | 1 / 0 |

All five measured climbs have downhill routing-reference values. Their source triangle / rendered segment pairs are 12229/250, 256251/377, 256230/382, 164524/410 and 100142/949. The two slight portions share geometry in two rendered networks; length statistics count the actual rendered paths.

The production router still reports zero rejected uphill reaches. This is consistent with its own looser grading, but does not imply zero visible rises. The new independent audit detects five small rises and no strong climbs.

The 118 purple markers account for all declared closed-basin endpoints, making declared basin stops a substantial explanation for the fragmented appearance. This does not prove that every visible gap is a basin or that those basin classifications are hydrologically correct. The remaining unexpected count is an uncovered active component without an extracted rendered network/source, so it has no final rendered point on which to place a white marker. It is not evidence of one additional visible unexplained endpoint.

## Screenshot inspection

- [A: normal rendering](../Artifacts/RiverVisibleGrade/20261027-183601/A-normal.png)
- [B: visible grade](../Artifacts/RiverVisibleGrade/20261027-183601/B-visible-grade.png)
- [C: visible grade and terminations](../Artifacts/RiverVisibleGrade/20261027-183601/C-visible-grade-terminations.png)

A/B/C retain the same camera. All five uphill portions lie on the far hemisphere in that view, explaining why A and B look alike. C shows purple markers at basin endpoints on the visible hemisphere.

Supplemental [D: close-up normal](../Artifacts/RiverVisibleGrade/20261027-183601/D-supplemental-closeup-normal.png) and [E: close-up audit](../Artifacts/RiverVisibleGrade/20261027-183601/E-supplemental-closeup-audit.png) use a separate, matching close-up camera around source 12229 / segment 250. Inspection shows the orange uphill portion near the top and purple basin markers at local river endings. These images supplement rather than replace the required same-camera views.

Reproduce using **Tools > Hydrology > Capture Visible Grade A-B-C (seed 123456)**. The capture writes screenshots, settings, grade samples, termination points and a report, then restores debug settings and camera state. Batch entry point: `RiverVisibleGradeCapture.CaptureBatch`.

## Validation and scope

- Native Unity EditMode: **7/7 passed**, including raw-line and ribbon integration, independent settings persistence/migration, exact OFF mesh restoration and unchanged terrain, topology, paths, runoff, mouths and lakes across debug modes.
- Managed hydrology suite: **93 passed, 1 failed**. The failure is the previously reproduced baseline `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted` (expected 13, actual 0); this diagnostic does not change its routing tolerance.
- Runtime compilation passed. Unity batch capture completed and verified primitive-position and hydrology-state invariants. `git diff --check` passed.

No hydrology fix, suppression, lake generation or routing adjustment was made. Implementation lives in `GeodesicRiverVisibleGradeAudit.cs`, the render observer and marker lifecycle in `GeodesicRiverSystem.cs`, and completed-facet sampling exposed by `GeodesicRiverTerrain.cs`; startup files provide the controls and persistence. Tests and the editor capture utility accompany the feature.
