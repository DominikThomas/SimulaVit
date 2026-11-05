# Planetary lighting refinement

Unity 6000.3.19f1 / URP 17.3.0. Refined 2026-11-05.

## Atmosphere

The existing Atmosphere Layer mesh and Atmosphere_Fresnel_Mat asset remain in use. AtmosphereScatteringURP reads the actual URP Main Light, including its Forward+ variant. It uses analytic sphere intersections, three fixed density samples, Rayleigh-like scattering, a bounded forward lobe and smooth planetary self-shadowing. No additional gameplay cameras, full-screen passes, textures, or per-frame CPU updates were added.

Warm colour now uses local solar elevation around the true geometric terminator, a narrow Gaussian twilight mask, and a squared limb mask. Warm light is suppressed beyond 0.4 times the twilight width on the dark side. Sunset Strength controls the brightness of this local contribution. Cool scattered light and the independently adjustable night residual remain.

Face-on haze defaults to only 0.015 of the limb response. The outer halo tapers relative to the physical sea/ground sphere, preventing a luminous detached ring at the global tallest-peak bound. The scattering volume itself remains above the terrain. Shadowing uses the physical planet radius independently of atmospheric clearance.

### Radius adjustment

PlanetGenerator changes are restricted to atmosphere visuals. Terrain vertices, generation inputs/cache keys, water geometry, hydrology, simulation, and Sun rotation are unchanged.

Let R = CurrentVisibleOuterRadius (maximum generated terrain or visible water), and B = BasePlanetRadius:

- Inner scattering radius = R + B * atmosphereTerrainClearance.
- Outer shell radius = inner radius + R * (atmosphereRadiusMultiplier - 1).
- Clearance defaults to 0.001 of the base radius, clamped to [0.0001, 0.01].

For the scene's geodesic multiplier of 1.01, the outer mesh moves outward only by the small clearance margin. The effective scattering layer becomes approximately 1% thick above the tallest terrain instead of extending down to sea level. With base radius 8, the default margin is 0.008 local units. The legacy visual builder resolves this bound after the complete existing terrain maximum is known. Both modes bind planet, inner, and outer radii per renderer.

The shader uses the analytic volume entry for its ordinary depth test, with depth writes disabled. It does not sample camera depth. Inside the inner boundary, rays see atmosphere only if they exit the cavity before hitting the physical planet.

### Atmosphere tuning

On the atmosphere material:

| Control | Default | Effect |
| --- | --- | --- |
| Scattering Intensity | 1.15 | Overall light and optical opacity |
| Rayleigh Colour | (0.12, 0.42, 1) | Blue base and derived cyan |
| Sunset Colour | (1, 0.24, 0.035) | Local warm endpoint |
| Sunset Strength | 0.4 | Warm brightness; previously 1 |
| Twilight Angular Width | 0.055 | Solar-elevation width, about 3.2 degrees |
| Limb Width | 0.12 | Silhouette transition and outer halo taper |
| Atmosphere Over Surface | 0.015 | Residual face-on haze |
| Forward Scattering | 0.08 | Bounded Sun-facing lobe |
| Optical Density | 0.65 | Optical column strength |
| Density Falloff | 4.5 | Concentration toward the inner boundary |
| Day Night Softness | 0.035 | Curved planet-shadow transition |
| Night Residual Glow | 0.012 | Faint cool residual, compensated for the thinner layer |

In the Planet Generator Inspector, Atmosphere Terrain Clearance controls the safety margin and Atmosphere Radius Multiplier controls thickness. These apply when the existing atmosphere mesh is built; no continuous rebuilding was added.

## Water

GeodesicOceanURP and Resources/GeodesicLakeURP share PlanetWaterLighting.hlsl. They now use view-dependent Fresnel reflection, the existing environment cubemap and a Main-Light specular Sun glint. Smoothness controls highlight width; positive normal/light alignment and Main Light shadow attenuation suppress night-side glints. Both shaders include the Forward+ variant.

Environment reflection is bounded and tinted to prevent an HDR sky washing water white. A restrained sky fill supports views without a usable probe. Grazing reflection raises water opacity. Existing ocean chemistry/depth colours and their binder remain unchanged. Lake runtime materials pick up the improved shader defaults automatically; no hydrology code or lake geometry was changed.

| Water control | Ocean default | Lake default |
| --- | --- | --- |
| Smoothness | Existing authoring value (scene: 0.876) | 0.92 |
| Fresnel Strength | Existing authoring value (0.18) | 0.22 |
| Fresnel Power | Existing authoring value (3) | 5 |
| Environment Reflection Strength | 0.85 | 0.85 |
| Sun Glint Strength | 4 | 4 |
| Sky Reflection Fill | (0.3, 0.5, 0.7) | (0.3, 0.5, 0.7) |
| Ambient Strength | Existing ocean value (0.08) | 0.08 |

These are shader/material controls. Existing renderers construct ocean/lake materials at runtime; new controls use shader defaults. No persistent ocean/lake material asset or binding script changed. Legacy URP/Lit ocean rendering remains as before.

## Land

GeodesicVertexColorURP preserves vertex colours, normal-based slope contrast and shadows. New controls are Sunlight Readability (1.25), Cool Ambient Tint (0.55, 0.7, 1), Night Ambient Multiplier (0.5), Twilight Light Tint (1, 0.8, 0.6), and Twilight Warmth (0.1). Existing generator Ambient/Diffuse Strength controls still apply. Diagnostic/river materials with Diffuse Strength = 0 keep their unlit ambient colour behaviour.

Controls are available on the runtime geodesic surface material. The legacy surface/ice shader is outside this geodesic refinement.

## Verification

Run Tools > Atmosphere > Validate Lighting Refinement, or batch-execute PlanetLightingRefinementValidation.Capture. Output goes to the ignored Artifacts/AtmosphereRefinement directory. The original atmosphere tool remains available and shares its render helper.

Controlled synthetic terrain includes a tall mountain near the limb, water and a lake cap. Tests use the actual shaders, renderer property blocks and Main Light without generating a gameplay world or saving scenes. Optional BeforeRefinement shader/material snapshots in the ignored validation copy provide comparisons to the preceding implementation; the menu also works without those snapshots.

Verified on desktop Direct3D11:

- Runtime/editor compilation and GPU rendering of all four shaders without shader errors.
- Before/after day, sunrise/sunset, backlit/night, and prominent mountain-limb views.
- Full 360-degree orbit at 30-degree intervals and a close horizon view.
- Inner radius above the maximum test terrain, with a 0.001 margin.
- Central-disc atmospheric signal on black terrain reduced from about 0.101 to 0.0033, approximately 97% less overlay.
- Automated ocean/lake glint-versus-disabled checks and no added night-side glint.
- Displacing the Sun visibly displaces the water highlight.
- Desktop Forward+ and mobile Forward renderer configurations; the latter with camera depth disabled.

The PNGs and validation.txt are controlled GPU captures, not screenshots of the user's running world. No mobile-device build or GPU timing benchmark was performed.

## Limits

Clearance, shadowing and scattering endpoints are spherical rather than per-mountain ray tracing. Unusually tall isolated peaks can suppress their local halo. Mirror-like water uses existing cubemaps and an analytical Sun highlight; no dynamic terrain/screen reflections were added. Analytic fragment depth can reduce early depth optimization, so device profiling remains useful. The atmosphere shell's exit geometry must remain within the camera far plane.
