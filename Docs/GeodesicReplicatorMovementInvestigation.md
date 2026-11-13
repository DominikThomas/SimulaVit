# Geodesic replicator movement and flicker investigation

Source inspection only, 2026-11-13. Unity, tests, builds, simulations and benchmarks were not run. The described recording was not reviewed frame by frame. The defects below follow from the source paths; their contribution to the reported recording still needs manual confirmation.

## Current movement and legacy comparison

The earlier movement path remains in `ReplicatorMovementSystem` and `ReplicatorSteeringSystem`. The repository calls its simulation grid `LegacyCubeSphere`: resource lookup uses six cube faces even when discussing the planet as a spherical/icosphere visual. The current geodesic path is a separate `GeodesicBiologyRuntime`; `ReplicatorSimulationPipeline.RunSimulationStep` returns after that runtime and bypasses legacy steering, movement, metabolism and lifecycle.

| Aspect | Legacy spherical movement | Current geodesic biology |
| --- | --- | --- |
| Random direction changes | Seeded spatial/time noise rotates the heading and adds lateral wobble every movement update. | Independent deterministic movement seeds, tangent headings and wander sequences. Random target curvature in [-2, 2] radians/second is refreshed every 1–5 simulation seconds; the heading turn rate approaches the target with a 2-second response. |
| Active locomotion | Amoeboid and flagellum types additionally sense local habitat on jittered intervals (defaults 0.5 and 0.2 seconds), adjust tumble probability from temporal suitability, and steer towards a tangent desired direction. Anchored organisms return without moving. | Only passive drift is advanced. Founders are passive hydrogenotrophs, and children inherit locomotion. No food/temperature seeking or run-and-tumble steering is used by this transport path. |
| Speed | `AngleAxis((MoveSpeed * habitatMultiplier * locomotionMultiplier * speedFactor) * dt / planetRadius, axis)`; the numeric angle is **degrees** because this API takes degrees. Energy/temperature influence `speedFactor`. | Constant angular travel rate 0.002 radians per simulation second, independent of the legacy `moveSpeed`, energy-derived speed factor and habitat suitability. At local radius 8, this is about 0.016 local distance units/second. |
| Clock | The movement job receives the pipeline's authoritative simulation delta and simulation time. | Horizontal integration consumes 0.1-second simulation intervals. The residual is retained; catch-up intervals are subdivided into at most eight integration steps. Wander and vertical schedules also use simulation time. Reactions have their own 0.25-second cadence. |
| Movement energy | Current legacy metabolism sets additional movement cost to zero for every locomotion type. The manager's `moveEnergyCostPerSecond` field is not consumed. Basal/stress accounting still applies. | Passive movement does not deduct energy. Lifecycle deducts basal maintenance times simulation delta. |
| Cell selection | A continuous direction is mapped to cube-face resource coordinates. There is no geodesic-neighbour transport in the legacy job. | Habitat authority is the packed `(GeodesicCellIndex, CurrentOceanLayerIndex)`. The continuous direction chooses a closer entry from the current cell's actual 5/6 neighbours by dot product; at most three crossings per integration substep. There is no global destination or long-distance nearest-cell reassignment during normal transport. |
| Local position | Radial vector and rotation about the origin; radius is rebuilt from the noise surface, then the resource ocean shell. | A continuous **planet-local unit vector** (`PassiveDriftDirection`) and tangent (`PassiveDriftTangent`), independent of the cell centre. This is a global spherical direction that carries sub-cell position, rather than cell-centre snapping or latitude/longitude. |
| Shore boundary | An ocean-only organism's proposed direction is rejected when it would enter non-ocean noise. | A dry neighbour causes reflection at the ocean/land bisector; it does not permit a land transition. |
| Ocean layers | The current requested layer is clamped for shell sampling. The visual radius is interpolated between top/floor according to layer index, plus 0.05 skin offset. | Horizontal crossings clamp the layer to the target cell's active layers. Independent exponential vertical opportunities have rate 0.0015/second (mean about 667 simulation seconds), choose one adjacent layer up/down, and respect bounds. |
| World placement | The movement job reconstructs a radial position using origin-centred surface queries. | Target radius is the authoritative layer centre. `PassiveVisualRadius` moves towards that radius at `max(0.01, MaximumOceanDepth) * 2` local units/second. World position is `planet.localToWorldMatrix * (localDirection * visualRadius)`. |
| Rendering | Shared instanced renderer; flagellum heading has visual smoothing and amoeboids have cosmetic wobble. | Passive bodies use the packed world position directly in `Matrix4x4.TRS`. No positional interpolation, waypoint interpolation or render-only shell reconstruction occurs. |

The legacy speed expression's degree units make its `moveSpeed` value unsuitable for a direct linear-speed comparison with the geodesic radian constant. With the scene's legacy `moveSpeed=4`, radius 8 and multipliers 1, the legacy expression yields 0.5 degrees/second, or about 0.0698 local units/second at radius 8. This inherited reference path was left unchanged; rescaling legacy movement is outside the current geodesic correction.

At 0.002 radians/second, a completely straight geodesic run travels at most 0.06 radians (about 3.44 degrees) in 30 simulation seconds. Random turning reduces net displacement. The current implementation allows temporary straight runs but does not deliberately send organisms around the planet or tether them to their spawn position. At 1x, horizontal movement targets change at 10 Hz with no positional render interpolation, so small visible steps remain possible.

The simulation pipeline executes one capped delta per configured speed step (`min(Time.unscaledDeltaTime, maximumSimulationStepDeltaSeconds)`, default maximum 1/30 second). Higher speed executes more simulation steps; it does not multiply rendering delta into movement again. Under load, simulation time can advance more slowly than recorded wall time. Movement bounds must use consumed simulation/integration time.

## Confirmed defect: newborn position overwritten before initialization

Before the correction:

1. `RunLifecycle` creates a child at the parent's valid packed world position, cell and layer, then calls `AddAgentFromReplicatorData`.
2. That method resets the child's separate passive direction, tangent and visual radius to zero. Founder initialization supplies these fields explicitly; child creation relies on lazy kinematic initialization.
3. `EnsurePassiveKinematicState` was called only inside the conditional 0.1-second horizontal integration loop.
4. On a simulation step before the next horizontal tick, the visual loop still ran for the child. It computed `localToWorld * (zeroDirection * layerRadius)`, replacing the inherited world position with the planet centre.
5. On the next horizontal tick, lazy initialization attempted to recover direction from that already-overwritten position. At the origin-centred planet this reaches the fallback `CellDirections[0]`. With other transforms, floating-point residuals at the centre can also yield an unrelated direction. The resulting position can be far from both the inherited habitat and the parent's cluster.

This establishes an actual invalid packed position followed by incorrect spherical state recovery. The renderer submits that invalid position faithfully, so this can produce both disappearance and apparent teleportation. It does not require a long straight drift path or a pool reuse problem.

The correction calls the existing independent-field initializer before each passive visual write as well as before integration. A newborn's inherited world position is therefore still valid when direction/radius are recovered, including on steps with no horizontal integration. The movement equations, random streams, movement cadence, habitat selection, energy, reproduction rules and chemistry are unchanged.

## Confirmed rendering defect: optional draw-frame skipping

The old `ShouldRenderThisFrame` option omitted `Graphics.DrawMeshInstanced` calls on selected high-speed frames. These submissions last one frame; omitting them hides organisms for that frame. There is no retained renderer or retained GPU instance population to display instead.

The obsolete skip controls and guard were removed. Every normal enabled rendering frame submits the current organisms, while simulation stepping remains unchanged. The checked-in scene had skipping disabled and a threshold of 20 simulation steps/frame, so this path does **not** account for the described 1x recording under those settings.

## Rendering and identity findings

- Passive markers are instanced sphere draws in batches of at most 1023. They are not pooled GameObjects. The matrices and colours in each live batch are rewritten; the submitted count excludes stale buffer entries.
- Packed arrays are authoritative. The companion `Replicator.position` can be stale in geodesic mode; inspecting that field alone is insufficient. The diagnostic ID lives on the companion solely to follow the same organism object as its packed index changes.
- Swap-back removal copies the passive direction, tangent, radius, seeds, schedule cursors, cell and layer with the surviving packed entry. The companion reference is moved with it. A packed array index is therefore not a stable identity, but no passive position cache is keyed by that index in the renderer.
- The renderer does retain index-keyed **flagellum orientation** smoothing. That can inherit a previous slot's visual heading after a removal. Passive founders bypass that code, and it does not alter translation; it is not established as a cause of the reported positional jumping.
- Geodesic integration normalizes the direction, projects the tangent back onto its tangent plane, and uses dot-product neighbour selection. There is no longitude wrap or interpolation between opposite hemispheres in this path.
- `Graphics.DrawMeshInstanced` supplies group bounds/culling. The source contains no per-passive-organism visibility toggle or lifetime fade; passive size/colour are constant in this path. Ordinary births/deaths can also add/remove markers and must not be mistaken for one organism moving.
- The replicator shader is transparent, uses `ZWrite Off`, and outputs instanced colour without a passive blink animation or vertex displacement. The ocean is also transparent. Group sorting, depth occlusion, overlapping markers and small projected pixel coverage remain possible visibility factors; they were not confirmed as the cause and were not changed.

## Manual tracking in Unity

1. In Play mode, select the object with **ReplicatorManager**. Expand **Geodesic Movement Diagnostics → Movement Diagnostics** and enable **Enabled**. Diagnostics default to off.
2. Keep **Maximum Tracked Organisms** at 4 initially. The diagnostic reads one previous observation per living passive organism to rank changes, but retains detailed live/peak readouts and bounded trails only for the selected few. It never logs every organism every frame.
3. Inspect **Tracked Organisms → Latest / Peak**, or filter the Console for `[ReplicatorMovement]`. The default log interval is two real seconds, with at most one entry per retained ID per interval. `Peak` preserves the largest incident even after a later normal step; `alive=false` distinguishes a retained dead organism from a moving survivor.
4. **Selected Organism Id = 0** automatically shows the highest-ranked living retained organism, or a retained dead one if none are alive. Copy an `organismId` into that field to pin it. IDs are stable across packed-index changes and are never reused for a different constructed organism during that runtime; a load or population restart creates new identities.
5. Enable Scene-view **Gizmos**. The selected organism has an ID/index/cell/layer label and short trails: **green** is continuous simulation direction at the authoritative layer-centre radius; **magenta** is the submitted render position; **yellow** connects the two. Use the ReplicatorManager component's context menu **Movement Diagnostics → Frame Selected Organism** to frame its position in the Scene view. Trails contain the most recent configured number of observations (96 by default), rather than a fixed number of seconds.

The green/magenta separation during a layer change is expected: the logical layer radius changes immediately, while the visual radius approaches it smoothly. Trails are stored in planet-local coordinates and transformed when drawn, so planet transform changes do not create a false trail around the world.

Each retained sample includes identity and packed index; simulation step/time/delta; consumed horizontal integration seconds; previous/current cell and layer; whether the explicit direction was initialized; local spherical direction; authoritative and visual radii; authoritative, packed and submitted world positions; angular/surface/total displacement; expected maximum angular and surface movement; render displacement/error; and anomaly flags. `angleRad` and `surfaceDistance` Console pairs are **measured / expected maximum**. Surface distances are planet-local geometric units, not the ocean model's physical kilometres.

Bounds use the actual consumed 0.1-second integration interval, including the interval residual accumulated across earlier simulation steps. They do not flag each valid 0.1-second move against just one 1/60-second rendered frame. Angular measurements use `atan2(|cross|, dot)` to resolve tiny movements that float `acos(dot)` can round to zero. Render bounds accumulate all integration time since the previous render submission. Whole-position distances are recorded, but ordinary radial layer changes are not classified as excessive angular movement.

| Flag | What to investigate |
| --- | --- |
| `InvalidPosition` | Non-finite/zero position or invalid active cell/layer. |
| `ExcessAngularMovement` | Simulation direction displacement exceeds angular speed times consumed integration time, plus tolerance. |
| `UnexpectedCellJump` | Cell-centre separation exceeds continuous travel plus conservative neighbour-spacing margins at both endpoints. This is a geographic sanity check; legal multiple-neighbour catch-up steps are allowed. |
| `DirectionOutsideHabitat` | Continuous direction lies more than a conservative cell width from its recorded habitat. |
| `PackedPositionMismatch` | Packed local position disagrees with direction times visual radius. |
| `RenderPositionMismatch` | Translation of the submitted instance matrix disagrees with the packed world position. |
| `RenderDiscontinuity` | Submitted angular displacement exceeds accumulated movement allowance since the previous submission. |

Interpret flags as inspection leads. If green and magenta both jump and simulation movement exceeds its bound, inspect simulation state. If only magenta disagrees with packed state, inspect rendering. If positions remain bounded and matched while the white marker disappears, inspect visibility, depth, transparent draw ordering and pixel coverage using the stable ID/trail.

The render diagnostic observes the actual CPU instance-matrix translation, not a readback of the final GPU image. `renderFrame=-1` means no instance submission has been observed for that ID. It cannot prove whether the GPU clipped, occluded, sorted or rasterized a marker. Initial observations establish a baseline; motion before enabling diagnostics is not reconstructed. When a new offender replaces a retained ID, both available incident endpoints are included, but no earlier full-population trail is kept.

## Static review scope

Reviewed the movement/visual initialization order, fixed-interval accounting, stable identity through swap-back and loading, neighbour and active-layer bounds, render callback arguments, conditional UnityEditor usage, and remaining references to the removed draw-skipping controls. `git diff --check` is the only command-level validation; runtime and compilation remain for the user's manual test. Hydrology, terrain, planet caches, ocean chemistry, and metabolism/reproduction implementation were not modified.
