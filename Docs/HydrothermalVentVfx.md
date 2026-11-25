# Hydrothermal vent effects

## Diagnosis

The original missing-smoke issue came from the geodesic renderer (`GeodesicVentVisualizer`) creating only the orange outlet discs. It never called the original soot implementation. `VentVisualizer.RebuildVentVisuals` deliberately exits when the legacy resource map is not authoritative, so its soot prefab was never instantiated in geodesic mode.

The legacy implementation also parented the emitter to a small marker facing inward (`LookRotation(-dir)`), reset the prefab's rotation to identity, and retained a wide cone/noise configuration. Cone emission is along local +Z, so those emitters faced into the planet and inherited the marker's small scale. The existing soot material already uses URP Particles/Unlit; an incompatible legacy shader was not the cause. It had no soft particle texture and multiplied dark particle colors by another dark material tint.

The subsequent offscreen-growth issue had two causes: `ParticleSystemCullingMode.Pause`, plus visibility/LOD code that stopped and cleared soot, disabled its anchor and reduced its emission rate. Reentering view restarted an empty system. Smoke now prewarms, uses `AlwaysSimulate`, keeps its anchor active, and keeps the configured emission rate regardless of camera visibility. Visibility changes only affect its renderer. Heat retains its cheaper visibility-based simulation policy.

## Placement and motion

`GeodesicVentVisualizer` now passes the existing authoritative compact outlets to the scene's configured `VentVisualizer`. It does not generate candidates, change clustering, or write chemistry/temperature values. Terrestrial vents keep their existing markers and do not receive underwater effects.

The emitter position uses `TransformPoint(outlet.PlanetLocalPosition)`, with a small lift limited to 3% of the available water column. The outlet already stores the completed visible seafloor radius. The emitter forward direction is `normalize(ventWorldPosition - planetCenterWorldPosition)`. A zero-angle cone emits along that local +Z axis with no random or spherical direction component. The same configuration is applied after prefab instantiation and to the runtime fallback.

An independent unit-scale root follows planet translation and rotation. Emitters never inherit the marker's scale or inward orientation. Particles simulate in emitter-local space so existing particles follow a moving or rotating planet. Planet scale at construction is included in the dimensions; rebuild the effects after changing the planet's scale at runtime.

Radial start speed stays positive. Gravity, velocity/force modules, inherited velocity, collision and subemitters are disabled. Small transverse noise is zero through the first 20% of particle age, then increases gently; axial noise is zero. Particle size grows and alpha fades with age. Height, maximum speed/lifetime, and maximum particle diameter are constrained by the seafloor-to-surface gap, leaving clearance before the surface. Very shallow columns (at most 0.002 world units remaining) omit the effect.

## Startup GUI, Inspector and persistence

Open **Planet Simulation Setup → Advanced → Vent Visual Effects**. This collapsible group uses the existing toggle, slider and numeric-input helpers and is available for both grids. The same controls remain on the **VentVisualizer** object under the planet in `PlanetScene`.

`SimulationStartupConfig.ventVisuals` stores the eight startup parameters as a value type. Startup JSON schema 25 saves them through the existing `SavedStartupConfig`, `SaveStartupConfig` and `LoadSavedStartupConfig` path. Old or partial profiles inherit the captured scene defaults for missing fields, while explicit false and allowed zero values stay meaningful. Normal Reset Defaults preserves vent visuals; Reset Advanced restores their captured defaults. No existing saved file is rewritten merely by loading it.

Loading and changing GUI settings synchronize the visualizer's existing Inspector fields. During Play mode, Inspector edits write back into the bound startup config and use the same normalization limits. Configuration is applied before outlet creation and on actual changes, not copied every frame. Regeneration retains the binding and selected values. Glow/refraction/toggle edits from the GUI do not restart running smoke. Particle shape/appearance edits may reconfigure and prewarm existing systems, without creating duplicates.

| Control | Default | Behavior |
| --- | --- | --- |
| Enable Smoke | On | Independent soot toggle |
| Enable Heat Distortion | On | Independent thermal mixing toggle |
| Vent Glow Size Multiplier | 0.85 | GUI range 0.25–2; scales only glow marker dimensions |
| Smoke Intensity Multiplier | 1 | GUI range 0–3; emission relates moderately to strength, capped at 40 particles/second |
| Smoke Height Multiplier | 1 | GUI range 0–3; water clearance still limits height |
| Smoke Base Width Multiplier | 1.75 | GUI range 0–4; scales emission radius and particle size, with a water-depth safety cap |
| Smoke Upward Speed Multiplier | 1 | Upward speed; lifetime adjusts to preserve height and surface clearance |
| Smoke Turbulence Strength | 0.25 | Small age-dependent transverse irregularity |
| Smoker Appearance | Mixed | Black, White, or a deterministic mixture |
| Distortion Strength | 0.03 | Very small URP distortion setting |
| VFX Visibility Distance | 6 | Render-only world-unit cutoff, fading over the final half; smoke continues simulating |
| Max Active Vent Effects | 48 | Maximum rendered outlets and active heat systems; never limits smoke simulation |

The GUI turbulence range is 0–1, default 0.25. Heat distortion range is 0–0.2, default 0.03. Smoker Appearance, upward speed and rendering limits remain additional Inspector controls. There is no setting to pause smoke because it is offscreen.

## Resulting dimensions

- Every glow mesh scale is multiplied by **0.85**, including geodesic submarine/terrestrial discs and legacy individual/clustered patches: radius and diameter decrease **15%**, mesh area decreases **27.75%**. Strength and cluster-size ratios are preserved. For geodesic hot-core radii 0.02–0.08, visible glow radii become **0.017–0.068**, while the thermal radii remain exactly 0.02–0.08.
- At the new **1.75** width default, emission radius and initial particle size are **75% larger** wherever depth does not constrain them. Cone angle, initial radial velocity, source position and marker scale inheritance are unchanged. The shallow-water maximum grown diameter budget increases from 0.16 to **0.24 times remaining depth**, producing a **50%** increase where that cap dominates. Maximum rise remains 0.55 times depth, with clearance for the enlarged billboards.
- Example: for a black smoker at seafloor radius 7.5 and sufficient water depth, the emitter-disc radius is **0.00492** world units (previously 0.00281); maximum start-size parameter is **0.03281** (previously 0.01875). The age-zero size curve multiplies that by 0.65. All dimensions are independent of glow scaling; these are geometric values, not a visual measurement.

**Black** uses a dark, denser, narrower column. **White** uses a pale, softer, broader column. **Mixed** uses the planet seed and source cell to select approximately one-quarter white smokers. This hash does not consume the simulation random stream or inspect chemistry. Heat size, speed and lifetime are independent of all smoke multipliers.

The original `Soot_Particle` prefab remains the smoke source. Its transform and cone were corrected, and play-on-awake was disabled so configuration happens before emission. One soft irregular alpha mask and one mixing normal map are generated per visualizer and shared by all outlets. No new smoke prefab or texture file is needed. Per-visualizer material copies protect the configured source assets from runtime edits.

## Rendering and transparent water

Heat uses Unity URP **Particles/Unlit**, with `_DISTORTION_ON`, `_NORMALMAP`, and `_SOFTPARTICLES_ON`. Slowly rotating, rising, overlapping irregular patches suggest thermal mixing. They have no emission or colored fill. The shared normal texture provides small refractive offsets; particle age and camera distance fade the effect. Soft depth intersection fading suppresses hard seams against opaque geometry.

The inspected PC pipeline enables both Opaque Texture and Depth Texture. The main camera inherits those settings. The Mobile pipeline disables both, so heat is disabled there unless the current camera provides both textures. Existing pipeline/camera settings are never overwritten. Smoke remains available without those textures.

Render order is opaque terrain/markers, heat **2980**, smoke **2990**, transparent geodesic ocean **3000**, atmosphere **3010**. Drawing the underwater effects before the water lets the current water tint/reflections and atmosphere composite over them. This avoids drawing a refracted copy of bare terrain on top of the ocean. No renderer feature, scene-color copy, full-screen pass, or custom shader was added.

URP's opaque texture does not contain transparent water, smoke or atmosphere. Consequently this implementation refracts the opaque seafloor and opaque markers only. It does not warp ocean reflections, transparent objects, other smoke or atmospheric scattering. At grazing angles or with highly opaque water, the ocean can obscure the plumes. These are limits of the chosen inexpensive rendering path. Camera selection follows `Camera.main`; Scene view and independent secondary-camera effect selection are not supported. GPU appearance and transparent ordering still require an in-editor visual check.

## Performance and cleanup

- Two ParticleSystems per existing compact submarine outlet, created only when outlet visuals are built. All enabled smoke systems stay active and simulate continuously, including when their renderers are hidden. There is no per-frame GameObject creation or particle readback.
- For **N** enabled outlets, the smoke hard limit is **96 × N** particles. Heat remains capped at 12 particles per rendered outlet, with at most 48 such outlets by default: **576** additional particles. The default rendering budget is still at most **5,184** particles before normal frustum/depth rejection.
- Estimated steady-state smoke count is approximately `min(96, emissionRate × meanLifetime)` per outlet. A representative 0.5-unit-deep outlet at strength 0.75 carries roughly **50** particles. Thus 100 outlets are about 5,000 smoke particles (hard maximum 9,600), and 1,000 outlets about 50,000 (hard maximum 96,000). This estimates particle count, not milliseconds or FPS; actual CPU cost scales with every enabled smoker and its noise simulation. Compared with the previous 48-system cap, the worst-case smoke-simulation budget grows by roughly `N / 48` when N exceeds 48.
- Loop prewarming adds a one-time initialization/settings-rebuild cost per enabled smoker. It establishes the plume before first visibility. Emission intensity can reduce this recurring cost without making simulation depend on the camera.
- Wider billboards can cover up to **3.06 times** their previous pixel area (1.75 squared) near the camera, increasing potential overdraw. Rendering limits and normal frustum culling remain in place. This is an area estimate, not a GPU timing measurement.
- Visibility and nearest-outlet rendering selection update four times per real-time second. Range, viewport and camera layer mask reject unnecessary drawing. At whole-planet distances renderers stay off and subsequent rendering checks return early; smoke state is retained and simulated normally. The root transform is only rewritten when the planet moves or rotates.
- Two runtime materials and two 64x64 data textures are reused across rebuilds and disposed with the visualizer. No material instances are created per outlet. Glow meshes continue using their existing shared meshes.
- Particle simulation speed is 1 and uses unscaled time, independent of accelerated biological steps.
- Regeneration immediately deactivates the old root and destroys it, including both particle systems. Return-to-menu cleanup clears all effects. Geodesic startup's legacy-overlay cleanup preserves the newly created geodesic effects.

These bounds describe the required CPU/rendering tradeoff. No performance benchmark or frame-time claim is made.

## Historical validation (Unity 6000.3.19f1)

The runtime and EditMode test assemblies compiled with the then-installed Unity 6000.3.19f1 C# compiler. The final focused Unity EditMode run in the repository's disposable cached validation project passed **55/55 tests**, with no failures or skips. Results are in `Logs/VentRefinementFinalTests.xml`; the Editor log is `Logs/VentRefinementFinalTests.log`. Compilation reported existing unused-field warnings, with no errors; `git diff --check` passed. These historical results were not reproduced on the upgraded Editor.

`VentVisualizerVfxTests` covers radial initial velocities at poles, equator and underside; transformed/scaled planets; prefab and runtime emitters; prewarming/AlwaysSimulate; aged transverse motion; surface clearance with large multipliers; black/white variants; shared materials; independent heat; render queues; cleanup and startup isolation. Repeated near/far and away/back changes preserve particle identities and lifetimes, including while exceeding the rendering budget; an offscreen system continues replacing particles beyond a full lifetime. Wider-base and noncumulative glow tests verify dimensions and independence. `VentStartupSettingsTests` checks old/partial JSON fallback, actual isolated file save/load, explicit disable values, defaults, normalization, cloning, reset scopes and Inspector synchronization. Existing `SimulationStartupConfigTests` also runs.

The original implementation request explicitly authorized focused tests and compilation, with visual testing reserved for the human. The main-checkout Unity launch exited before import without a test result; the cached disposable project ran successfully instead. Its startup writer was restricted to isolated test files so it could not overwrite shared startup settings. No gameplay simulation, visual test or benchmark was run. GPU appearance and actual frame-time impact remain unverified.

For a human visual check, start with a vent offscreen and wait beyond its lifetime before rotating toward it. Repeatedly rotate away/back and zoom outside/inside the rendering distance: plumes should be fully developed immediately. Compare smaller glows and wider Black/White bases at poles and underside; confirm surface clearance and subtle heat. Change Advanced controls, return/reopen setup, restart the application, and regenerate twice to check persistence and cleanup. Distant views should draw no smoke, while its CPU simulation continues. Saved biology and mutation settings, including Hydrogen to Fermentation probability 0.02, are unchanged.

## Changed files in this refinement

- `Assets/Scripts/Planet/Environment/Vents/VentVisualizer.cs`
- `Assets/Scripts/Planet/Environment/Vents/VentVisualizer.Vfx.cs`
- `Assets/Scripts/Planet/Environment/Vents/VentVisualizer.Settings.cs` and its meta file
- `Assets/Scripts/Planet/Environment/Vents/VentVisualSettings.cs` and its meta file
- `Assets/Scripts/Planet/Geodesic/Diagnostics/GeodesicVentVisualizer.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupConfig.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupController.cs`, `.Defaults.cs`, `.Tabs.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupController.VentVisuals.cs` and its meta file
- `Assets/Prefabs/Vents/Soot_Particle.prefab`
- `Assets/Tests/EditMode/VentVisualizerVfxTests.cs`
- `Assets/Tests/EditMode/VentStartupSettingsTests.cs` and its meta file
- This document
