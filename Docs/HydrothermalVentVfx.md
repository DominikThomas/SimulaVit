# Hydrothermal vent effects

## Diagnosis

The current geodesic renderer (`GeodesicVentVisualizer`) only created the orange outlet discs. It never called the original soot implementation. `VentVisualizer.RebuildVentVisuals` deliberately exits when the legacy resource map is not authoritative, so its soot prefab was never instantiated in geodesic mode.

The legacy implementation also parented the emitter to a small marker facing inward (`LookRotation(-dir)`), reset the prefab's rotation to identity, and retained a wide cone/noise configuration. Cone emission is along local +Z, so those emitters faced into the planet and inherited the marker's small scale. The existing soot material already uses URP Particles/Unlit; an incompatible legacy shader was not the cause. It had no soft particle texture and multiplied dark particle colors by another dark material tint.

## Placement and motion

`GeodesicVentVisualizer` now passes the existing authoritative compact outlets to the scene's configured `VentVisualizer`. It does not generate candidates, change clustering, or write chemistry/temperature values. Terrestrial vents keep their existing markers and do not receive underwater effects.

The emitter position uses `TransformPoint(outlet.PlanetLocalPosition)`, with a small lift limited to 3% of the available water column. The outlet already stores the completed visible seafloor radius. The emitter forward direction is `normalize(ventWorldPosition - planetCenterWorldPosition)`. A zero-angle cone emits along that local +Z axis with no random or spherical direction component. The same configuration is applied after prefab instantiation and to the runtime fallback.

An independent unit-scale root follows planet translation and rotation. Emitters never inherit the marker's scale or inward orientation. Particles simulate in emitter-local space so existing particles follow a moving or rotating planet. Planet scale at construction is included in the dimensions; rebuild the effects after changing the planet's scale at runtime.

Radial start speed stays positive. Gravity, velocity/force modules, inherited velocity, collision and subemitters are disabled. Small transverse noise is zero through the first 20% of particle age, then increases gently; axial noise is zero. Particle size grows and alpha fades with age. Height, maximum speed/lifetime, and maximum particle diameter are constrained by the seafloor-to-surface gap, leaving clearance before the surface. Very shallow columns (at most 0.002 world units remaining) omit the effect.

## Inspector controls

Use the existing **VentVisualizer** object under the planet in `PlanetScene`. The **Hydrothermal Effects** controls apply to both rendering paths. Inspector edits during Play mode reconfigure the existing systems; they do not create new emitters.

| Control | Default | Behavior |
| --- | --- | --- |
| Enable Smoke | On | Independent soot toggle |
| Enable Heat Distortion | On | Independent thermal mixing toggle |
| Smoke Intensity Multiplier | 1 | Emission, moderately related to outlet strength; capped at 40 particles/second |
| Smoke Height Multiplier | 1 | Requested height; water clearance still limits it |
| Smoke Width Multiplier | 1 | Requested diameter; water clearance still limits it |
| Smoke Upward Speed Multiplier | 1 | Upward speed; lifetime adjusts to preserve height and surface clearance |
| Smoke Turbulence Strength | 0.25 | Small age-dependent transverse irregularity |
| Smoker Appearance | Mixed | Black, White, or a deterministic mixture |
| Distortion Strength | 0.03 | Very small URP distortion setting |
| VFX Visibility Distance | 6 | World-unit camera-to-outlet cutoff, fading over the final half |
| Max Active Vent Effects | 48 | Closest eligible outlets allowed to simulate |

**Black** uses a dark, denser, narrower column. **White** uses a pale, softer, broader column. **Mixed** uses the planet seed and source cell to select approximately one-quarter white smokers. This hash does not consume the simulation random stream or inspect chemistry. Heat size, speed and lifetime are independent of all smoke multipliers.

The original `Soot_Particle` prefab remains the smoke source. Its transform and cone were corrected, and play-on-awake was disabled so configuration happens before emission. One soft irregular alpha mask and one mixing normal map are generated per visualizer and shared by all outlets. No new smoke prefab or texture file is needed. Per-visualizer material copies protect the configured source assets from runtime edits.

## Rendering and transparent water

Heat uses Unity URP **Particles/Unlit**, with `_DISTORTION_ON`, `_NORMALMAP`, and `_SOFTPARTICLES_ON`. Slowly rotating, rising, overlapping irregular patches suggest thermal mixing. They have no emission or colored fill. The shared normal texture provides small refractive offsets; particle age and camera distance fade the effect. Soft depth intersection fading suppresses hard seams against opaque geometry.

The inspected PC pipeline enables both Opaque Texture and Depth Texture. The main camera inherits those settings. The Mobile pipeline disables both, so heat is disabled there unless the current camera provides both textures. Existing pipeline/camera settings are never overwritten. Smoke remains available without those textures.

Render order is opaque terrain/markers, heat **2980**, smoke **2990**, transparent geodesic ocean **3000**, atmosphere **3010**. Drawing the underwater effects before the water lets the current water tint/reflections and atmosphere composite over them. This avoids drawing a refracted copy of bare terrain on top of the ocean. No renderer feature, scene-color copy, full-screen pass, or custom shader was added.

URP's opaque texture does not contain transparent water, smoke or atmosphere. Consequently this implementation refracts the opaque seafloor and opaque markers only. It does not warp ocean reflections, transparent objects, other smoke or atmospheric scattering. At grazing angles or with highly opaque water, the ocean can obscure the plumes. These are limits of the chosen inexpensive rendering path. Camera selection follows `Camera.main`; Scene view and independent secondary-camera effect selection are not supported. GPU appearance and transparent ordering still require an in-editor visual check.

## Performance and cleanup

- Two ParticleSystems per existing compact submarine outlet, created only when the outlet visuals are built. Hidden outlets remain inactive; there is no per-frame GameObject creation.
- At most 48 outlets simulate by default, each limited to 96 soot and 12 heat particles: a maximum of 5,184 particles across both effects. Actual counts are generally lower.
- Visibility and nearest-outlet selection update four times per real-time second. Range, view direction, viewport and camera layer mask reject unnecessary effects. At whole-planet distances, all systems stop and clear; subsequent visibility updates return before per-outlet native calls.
- Two runtime materials and two 64x64 data textures are reused across rebuilds and disposed with the visualizer. No material instances are created per outlet. Glow meshes continue using their existing shared meshes.
- Particle simulation speed is 1 and uses unscaled time, independent of accelerated biological steps.
- Regeneration immediately deactivates the old root and destroys it, including both particle systems. Return-to-menu cleanup clears all effects. Geodesic startup's legacy-overlay cleanup preserves the newly created geodesic effects.

These bounds limit cost; no performance benchmark has been run.

## Validation status

The complete runtime assembly and EditMode test assembly compile with the installed Unity 6000.3.19f1 C# compiler and the project's Unity references. Compilation reports existing unused-field warnings, with no errors. `git diff --check` passes.

`VentVisualizerVfxTests` covers radial initial velocities at poles, equator and underside; transformed/scaled planets; prefab and runtime emitters; aged transverse motion; surface clearance at shallow and deep sites with large multipliers; black/white distinction; deterministic mixed selection; shared materials; independent heat configuration; render queues; cleanup; startup isolation; distance culling and the active-outlet budget.

The sandboxed Unity test launch exited before compilation because it could not connect to the licensing service. Automatic approval review rejected the outside-sandbox retry. The user subsequently chose to handle Unity testing themselves; no further Unity test runs are authorized. Test execution and rendered appearance are therefore not claimed as verified. No gameplay simulation or benchmark was run.

For a visual check, inspect submarine vents on the near side, poles and underside after rotating the planet; compare Black and White; zoom beyond the visibility distance; toggle each effect separately; and regenerate twice. Check that soot stays below the surface, lower-plume refraction is subtle, distant views have no particles, and old effect roots disappear. Keep saved biology and mutation settings, including Hydrogen to Fermentation probability 0.02, unchanged.

## Changed files

- `Assets/Scripts/Planet/Environment/Vents/VentVisualizer.cs`
- `Assets/Scripts/Planet/Environment/Vents/VentVisualizer.Vfx.cs` and its meta file
- `Assets/Scripts/Planet/Geodesic/Diagnostics/GeodesicVentVisualizer.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupController.cs` (legacy-only visual cleanup call)
- `Assets/Scripts/SimulaVit.asmdef` (URP runtime reference for capability checks)
- `Assets/Prefabs/Vents/Soot_Particle.prefab`
- `Assets/Materials/Vents/Soot_Particle_Mat.mat`
- `Assets/Materials/Vents/Vent_HeatDistortion_Mat.mat` and its meta file
- `Assets/PlanetScene.unity` (heat material reference only)
- `Assets/Tests/EditMode/VentVisualizerVfxTests.cs` and its meta file
- This document
