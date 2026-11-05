# Directional atmospheric lighting

Implemented 2026-11-05 for Unity 6000.3.19f1 / URP 17.3.0.

## Existing setup and scope

`PlanetGenerator` builds the existing `Atmosphere Layer` mesh: an icosphere in geodesic mode and a sphere using the legacy grid in legacy mode. Geodesic shell radius remains `CurrentVisibleOuterRadius * atmosphereRadiusMultiplier`; the scene's multiplier remains 1.01 and subdivision remains 4. Because that radius includes mountain peaks, the atmospheric volume above sea level can be considerably thicker than 1%.

`Atmosphere_Fresnel_Mat.mat` now uses `SimulaVit/AtmosphereScatteringURP`. Its asset GUID and scene reference are preserved. The previous `Atmosphere_Fresnel.shadergraph` remains available for comparison. `SunSkyRotator` already aligns the directional Main Light with its planet-to-Sun direction and assigns `RenderSettings.sun`. The shader consumes that light through URP `GetMainLight()`, including the `_CLUSTER_LIGHT_LOOP` variant required by the desktop Forward+ renderer. The Sun's orbit and rotation code are unchanged.

Terrain remains opaque. The geodesic ocean remains transparent with depth writes disabled. The atmosphere draws after ordinary transparent water and clips its integration at the analytic sea-level sphere. It supplies the volume's analytic entry depth to the normal depth test, so foreground objects can occlude it even though the mesh rasterizes exit faces. Inside the atmosphere, the entry depth becomes the near plane. The shader does not sample a camera depth texture and works with either project's renderer. No pipeline assets, terrain/ocean shaders, background material, simulation, or hydrology code were changed.

## Approximation and cost

The mesh rasterizes exit faces, including from inside the shell. Each fragment intersects the camera ray with the atmosphere and planet spheres in local coordinates and evaluates three fixed Gaussian quadrature samples. This is a small, fixed-cost integration, with no marching loop, lookup textures, extra camera, full-screen pass, or texture updates in gameplay. Analytic fragment depth can reduce early-depth optimization, so target-device GPU profiling is still useful.

Density falls exponentially with altitude and smoothly reaches zero at the outer boundary. A Rayleigh phase approximation supplies the blue/cyan limb; a bounded forward lobe supplies a small Mie-like contribution. Long Sun paths blend through cyan and gold to orange, with the warm response suppressed when looking away from the Sun. The apparent planetary horizon at each sample supplies smooth curved self-shadowing. Premultiplied transparency and bounded optical opacity preserve surface/background detail. The night residual is independently adjustable.

`PlanetGenerator.ConfigureAtmosphereScattering()` supplies the local atmosphere and sea/ground radii through a per-renderer property block when geometry is built, for both generation modes. Planet transforms and light direction are read on the GPU. There is no additional per-frame CPU update or camera callback.

## Material controls

| Property | Default | Purpose |
| --- | --- | --- |
| Scattering Intensity | 1.15 | Overall scattered light and optical opacity |
| Rayleigh Colour | (0.12, 0.42, 1) | Blue base colour; cyan transition is derived from it |
| Sunset Colour | (1, 0.24, 0.035) | Orange endpoint; gold transition is derived from it |
| Sunset Strength | 1 | Warm long-path response; zero disables warming |
| Forward Scattering | 0.08 | Bounded glow toward the Sun |
| Optical Density | 0.65 | Column opacity; zero disables atmosphere |
| Density Falloff / Rim Thinness | 4.5 | Larger values concentrate scattering nearer the ground |
| Day Night Softness | 0.035 | Angular softness of the planet-shadow edge |
| Night Residual Glow | 0.003 | Faint remaining blue; zero disables it |

For the current mountainous world, adjust Density Falloff first if the atmosphere feels too broad, then Optical Density or Scattering Intensity. The geometry and multiplier do not need to change.

## Verification

Run **Tools > Atmosphere > Validate and Capture** in Unity, or call `AtmosphereScatteringValidation.Capture` with `-executeMethod` in an isolated batch project. It creates an unsaved preview scene, restores temporary light/render-pipeline settings, and writes PNGs plus `validation.txt` into `Artifacts/AtmosphereScattering/`. This output directory is ignored by Git.

The harness uses synthetic spherical terrain and the project's actual terrain shader, old/new atmosphere shaders, material defaults, and desktop/mobile renderers. These are controlled GPU comparisons, not screenshots of the running simulation or a match to the unprovided reference image. No post-processing is applied.

Checked on Direct3D11:

- Runtime scripts and validation harness compile; the atmosphere shader renders without shader errors.
- Automated atmospheric light signal checks on a black surface for desktop Forward+ and mobile Forward, using renderer property blocks. These catch a missing Main Light shader variant even when shader compilation succeeds.
- Sun behind the camera: subtle blue limb and transparent daytime scattering.
- Sun near the horizon: localized gold/orange twilight with cyan/blue transitions.
- Sun behind the planet: greatly reduced atmospheric illumination.
- Full camera orbit in 30-degree steps, including matching start/end views.
- Distant, close, inside-shell, below-surface, and orthographic views.
- Narrow (1.01) and mountain-spanning (1.104) outer radii.
- Raised terrain with approximate sea-level integration, and mobile forward rendering with camera depth disabled.

Final PNGs show a blue daytime rim, localized warm twilight, and faint night atmosphere. Close and inside-shell captures show a continuous horizon fade. Compare `orbit-000.png` with `orbit-360.png` to inspect the orbit wrap. The harness supplies radii through the same per-renderer property mechanism used by gameplay.

## Limits and separate next steps

- This is an artistic single-scattering approximation, not spectral atmospheric transport. Three samples can smooth fine shadow/density variations. No GPU timing benchmark or mobile-device build was performed.
- Planet shadows and scattering endpoints are spherical. They do not include individual mountains or clouds. Raised terrain receives an approximate atmospheric veil rather than exact depth-dependent integration. Water clipping uses sea level because transparent water does not write depth.
- Spherical planets with uniform transform scale are the intended configuration. A nonuniform scale produces an ellipsoidal approximation rather than physical scattering distances.
- Inside-shell rendering relies on its exit geometry remaining within the camera far plane. It does not add a special near-camera full-screen path.
- Existing terrain/ocean ambient light can keep the night surface visible independently of the atmosphere. Surface exposure, ocean highlights, and star/background exposure are separate potential follow-ups if the cinematic target requires them.
- Final visual approval on a generated gameplay planet and Vulkan/GLES/device validation remain useful; controlled Direct3D11 captures do not replace those checks.
