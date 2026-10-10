# Geodesic river baseline restoration

Status: river baseline restored in code; Unity visual acceptance is still required. Lakes remain OFF. No claim is made that the full river/lake task is finished.

## Starting state and preservation

Started on `codex/implement-geodesic-static-lakes`, HEAD `55e5cf4` (Failed attempt to fix river routing), with 12 modified tracked files and one untracked validation report. Before editing, the full HEAD archive, binary working-tree patch, copies of all 13 changed files, SHA-256 manifest, status, and an archive of `6f48085` were saved outside the repository:

`C:\Users\domin\Documents\Codex\2026-09-06\prior-conversation-with-codex-conversation-role\HydrologyBackup-20261007-203500`

The `working-files` directory plus `HEAD.zip` preserve the entire preceding L7 experiment. The previous report and documentation are also retained in `Docs/Archive`, explicitly marked as a failed experiment. L7 topology/mapping classes, tests, and the experimental visual-path helper remain in the project. There is deliberately no runtime L7 switch during baseline acceptance; the archived complete implementation can be recovered without approximating it.

## Actual reference

`6f48085dea26a6f46cf215f7f026e66999208717` is authoritative. Source was extracted from Git, not reconstructed from remembered behavior.

Restored decisions:

- `GeodesicRiverSystem.Initialize`: existing simulation topology; large-scale terrain sampling; shared junction selection within 0.2 of a neighboring cell; three interior saddle samples per undirected edge; the original coarse drainage inputs.
- `GeodesicRiverPath.Refine`: the historical bounded corridor search, transition costs, sample checks and numerical tolerance behavior.
- `GeodesicRiverTerrain.Sample`: the original float barycentric arithmetic for large-scale/visible heights, and exact final-mesh ray projection. Double-precision and final-path audit helpers remain observation/experimental utilities only.
- `GeodesicLakeRiverRouting.Build`: original ordinary-reach height source, grade gates, refinement and coast clipping. Lake routing is currently inactive.
- `GeodesicRiverSystem.RebuildVisuals` and `AddRibbon`: the actual historical path consumption, three samples per segment, width/color calculation, and independently projected banks.

The drainage graph retains its newer interface and memory/timing diagnostics. For the simulation topology its neighbor distances and algorithm match the reference; a direct subdivision-6 comparison checks receivers, parents, flood order, filled elevations, areas and runoff exactly.

Why the later versions diverged: direct L7 edges removed the historical terrain corridor decisions; the spline attempt did not restore those decisions. The subsequent uncommitted patch kept a different graph and switched anchors, drainage elevations, interpolation and all grade decisions to final visible terrain. Even its coarse fallback was not the historical algorithm. This restoration removes those behavior changes from the default path rather than adjusting them again.

## Preserved systems

No changes to planet terrain generation, sea-level resolution, inland-sea filtering, retained OceanMask, startup Advanced Options, ocean layers, chemistry, biology or simulation resolution. The current render terrain is reused as-is. Existing grade diagnostics remain observation-only.

A temporary explicit validation gate in `GeodesicRiverSystem` prevents lake geometry and lake receiver rewrites, even when saved options request lakes. It logs that condition and does not overwrite the saved preference. Basin diagnostics may still describe computational priority-filled components; they are NOT accepted as true lake basins.

## Unity A/B checkpoint

The reference capture uses literal historical hydrology classes with only a namespace wrapper, operating on the same current terrain as the restored implementation. Terrain-generation code has not changed since the reference apart from non-generating accessors. It uses the current camera; it does not regenerate or save the scene.

1. Run `Tools/PrepareRiverBaselineReference.ps1` if the generated reference folder is absent. It has already been prepared on this computer. Sources are generated/ignored rather than committing another copy of the entire historical subsystem; a Git blob manifest records their origin.
2. Let Unity finish compiling. Start seed **123456**, simulation **6**, render **7**, using the recent sea-level/inland-sea settings. Rivers must be enabled. Lakes are automatically paused by the temporary gate.
3. Position the camera like the regression screenshot.
4. Choose **Tools > Hydrology > Capture 6f48085 A-B (current planet, lakes OFF)**.
5. Inspect `Artifacts/HydrologyBaseline/<timestamp>/A-6f48085-lakes-OFF.png` and `B-restored-lakes-OFF.png`. The same folder contains camera/planet/river settings and `comparison.json` with exact path/anchor/receiver/mesh comparisons, counts, and generation timings.

The capture leaves the restored rivers visible. It restores the saved lake flag and does not run any simulation reset. A single local capture request may run after script import; if no initialized matching planet exists, it writes an explicit NOT VALIDATED status and does not start a planet automatically.

A and B must look equivalent, with plausible valleys and tributaries. Historical gaps are acceptable. Numeric equality alone does not approve the screenshots.

## Checks completed

- Unity 6000.3.19f1 compiler: runtime assembly, EditMode test assembly, and Editor A/B capture assembly compile; runtime emitted existing unused-field warnings.
- `Tools/ValidateGeodesicLakes.ps1 -BaselineReference`: **67 passed, 0 failed**. Includes literal-reference checks at render L7 (sampling), simulation L6 (drainage), and ordinary corridor/coast planning including historical interruptions.
- Native EditMode integration execution is pending; compiling tests does not mean they ran in Unity.
- The obsolete `-HydrologyAB` / `-DownhillRouting` reconstruction benchmarks now refuse to present themselves as runtime/reference comparisons. Their source and earlier measurements remain archived.

## Pending lake stage and report

The giant-valley lake cause has not yet been established by an accepted reproduction. The current basin code groups connected priority-filled cells by equal filled elevation, with a sampled-saddle check when supplied. That is an audit target, not proof of the reported cause or an accepted true-basin test. No new lake algorithm has been applied before the required river visual gate.

After A/B acceptance: audit true closed basins separately from computational fill, add open-valley/closed-bowl/two-basins tests, prove ordinary river paths outside basins remain unchanged with lakes ON/OFF, and capture C. Do not enable the old lake result to make gaps disappear.

| Required result | Current status |
|---|---|
| A/B screenshots and visual acceptance | Pending Unity capture/inspection |
| Reference/restored generation times and river counts | Capture writes measured values; none invented here |
| Priority-filled cell count | Capture reports both versions |
| True lake-basin cells / giant-valley root cause | Not yet validated |
| Visible lakes / largest visible area in B | 0 / 0 by the temporary generation gate |
| Corrected C screenshot and lakes ON/OFF invariant | Pending A/B visual acceptance and lake correction |

Remaining gaps retain the historical diagnostic categories: unresolved depression, corridor failure, projection mismatch and topology failure. Counts of inland terminations and ocean-connected chains are captured. Distinguishing actual mesh occlusion/coastline artifacts from threshold/headwater starts still requires the screenshots; no continuity algorithm has been added to hide them.
