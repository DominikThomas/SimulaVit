# Deposited seabed sediment visibility calibration

2026-11-12. Static review only; no Unity, automated tests, builds, simulations, or benchmarks were run. Changes are uncommitted for manual review.

## Root cause and rendering history

The reported run had 266,969 deposited FeS inventory units globally, a peak of about 481 per column, and about 240 in the inspected column. The visual mapping required **more than 10,000 in a single column** before applying any FeS tint. Both examples therefore produced exactly zero tint before any mesh upload, lighting, or water blending. Suspended and globally produced quantities do not participate in this visual mapping.

- `0ced756` introduced independent mineral abundance mapping with a floor at 1% of each full-tint reference. Its earlier approach modified mesh colours directly.
- `f124e8e` established the working **live terrain mesh** path: barycentric column sampling, premultiplied mixed sediment colour and coverage in UV channel 2 (`TEXCOORD2`), and the underwater tint branch in `GeodesicVertexColorURP.shader`. It also set fixed references to S0/Fe(III) 10,000 and FeS 1,000,000, giving floors of 100 / 100 / 10,000.
- `e943f46` changed material delivery from immediate deposition to conserved suspension and gradual settling. It did not change `GeodesicOceanSedimentVisual`, the terrain or water shaders, or the `PlanetGenerator` rendering integration. The live rendering fix was not lost. The previous display calibration simply no longer matched deposited quantities.

`PlanetGenerator` still initializes the visual against its completed live terrain `MeshFilter`, the ocean mask, and the existing water renderer. Mesh replacement/clipping is handled by the existing rebind. The terrain shader still blends sediment into the underwater base colour **before** its existing surface illumination. No shader, overlay, mesh, terrain geometry, normal, water material, transparency, reflection, or light setting was changed.

## Revised fixed calibration

All quantities below are **deposited inventory per column**, in concentration * physical km3.

| Mineral | Previous no-tint floor | Previous full tint | Revised no-tint floor | Revised full tint |
| --- | ---: | ---: | ---: | ---: |
| FeS | 10,000 | 1,000,000 | **1** | **500** |
| Fe(III) | 100 | 10,000 | **0.1** | **50** |
| S0 | 100 | 10,000 | **0.1** | **50** |

The floor is now 0.2% of each independent reference. The existing continuous, monotonic response is retained:

`weight = sqrt(clamp01((depositedInventory - floor) / (fullTintReference - floor)))`

At or below the floor the source contributes no tint. Full tint is reached only at the reference. There is no ocean-wide maximum normalization, time-driven amplification, minimum forced opacity, or global colour multiplier. A remote hotspot cannot change the reference and brighten or dim all other columns. Neighbour interpolation remains limited to the existing simulation triangles, allowing smooth boundaries without an ocean-wide visual plume. A sub-threshold column may still lie under the interpolated edge of a neighbouring real deposit.

FeS 500 is calibrated to the reported peak of 481. At the supplied examples, single-mineral **source coverage** becomes about **69.2% at 240** and **98.1% at 481**, instead of zero. FeS 5 gives about 9%, 25 about 22%, and 100 about 45%. FeS <=1 and S0/Fe(III) <=0.1 still give zero source coverage. The smaller oxidation-product references give those independent minerals a useful range without requiring FeS-sized inventories; these are explicit display defaults, to be assessed manually, not a claim about their unreported inventory distributions.

Charcoal FeS `(0.035, 0.04, 0.045)`, rust Fe(III) `(0.62, 0.20, 0.06)`, and yellow S0 `(0.88, 0.72, 0.12)` are unchanged. The existing mixture combines independent weights into coverage and a weighted colour; one mineral is not sequentially painted over the others.

## Normal water view and diagnostics

The normal water shader already uses transparent alpha blending, `ZWrite Off`, and opacity 0.58 in the checked-in scene. Near a surface-facing view that leaves approximately 42% of the lit seabed contrast in the composite; grazing Fresnel reflections attenuate it further. With nonzero and appreciable seabed tint, the ordinary view can show it without disabling water. Seabed Sediment View continues to hide the same water renderer for diagnosis only. Day/night lighting, viewing angle, and reflections still affect the final appearance.

The selected-cell panel no longer calls raw source weights "applied intensity." It now distinguishes:

- Source-column weights/coverage and the exact floor/full-tint values used by the last upload.
- Actual uploaded **local seabed vertex** coverage: minimum, mean, maximum, and sample count, grouped by each vertex's nearest simulation cell. Zero-coverage samples are included. Cells without local samples are explicitly identified.
- Each mineral's mean share of uploaded coverage, calculated with the same interpolated weights and premultiplied mixture as the shader input. In a mixture, the three shares sum to total mean coverage instead of each being presented as the whole tint.
- Whether the live tint stream and its terrain rendering/material-property path are active, and the applied/current sediment revisions.

These are statistics of the uploaded render input, not a claim that a screen pixel has the same brightness after triangle interpolation, lighting, and transparent water blending. They are rebuilt only during the existing visual refresh, using one allocated array of derived statistics; they do not store another sediment inventory. Periodic chemistry telemetry also reports all three floors/references and whether terrain tint rendering is active.

Serialized reference field names remain unchanged. A versioned upgrade replaces only the exact previous stock references in older component data; custom reference values are preserved. The common floor rule becomes 0.2% of the retained reference. Simulation/save data and mineral inventories are untouched; no save format changed. Geodesic live simulation saving/loading remains unsupported as before.

## Expected manual appearance

The renderer responds to **accumulated deposited inventory**, not elapsed time. A single measurement at 3,122 seconds cannot establish exact deposition at earlier times, especially with transport and reaction startup. These are qualitative expectations around continuously active vents, not simulated forecasts:

| Simulated time | What to look for in the normal water view |
| --- | --- |
| **5 minutes** | The first faint, small patches in columns that have crossed the relevant floor. Empty or still-sub-threshold columns remain untinted; a delay from real settling is expected. |
| **15 minutes** | More readable localized charcoal, rust, or yellow where those minerals have actually accumulated. For orientation, FeS 10-50 corresponds to roughly 13-31% source coverage; neighbours receive smooth patch edges. |
| **30 minutes** | Stronger vent-centred staining where deposits have reached tens/hundreds of units, with mixed colours where species coexist. For orientation, FeS 100 gives about 45% source coverage and 240 about 69%. Full tint requires FeS 500 or S0/Fe(III) 50; it is not forced by the clock. |

Verify in daylight, first in normal water mode, then briefly compare Seabed Sediment View. Check startup/telemetry floors are 1 / 0.1 / 0.1 with stock references; compare a positive-deposit cell's source coverage with its uploaded coverage range and revisions. Confirm negligible remote traces stay untinted, hotspots strengthen as deposits increase, and the three minerals and mixtures retain distinct colours. Conservation telemetry should behave as before because chemistry, settling, transport, ocean volumes, biology, and hydrology were not edited.

Files changed: `GeodesicOceanSedimentVisual.cs`, `GeodesicCellPicker.cs`, `GeodesicChemistryTelemetry.cs`, and this report. **STOP for manual review.**
