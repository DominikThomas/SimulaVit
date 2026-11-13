# Whole-basin visual sediment channels

The previous eligible-span / shrinking-amplitude generator has been replaced.
Every constructed sediment-filled river crossing enters the same whole-crossing
generator, including crossings whose original path already turns. There are no
16-width, 0.015 chart-length, existing-turn, minimum-visible-offset, or fixed
approach eligibility gates. Only an actually zero/non-finite input length cannot
define a curve; it is reported as Numerical.

TIN paths, lake routing, runoff, sediment elevations, topology, ocean mouths,
chemistry and biology remain authoritative and unchanged. The edited Path belongs
only to GeodesicTinLakeCrossings. No authoritative fingerprint definitions, cache
keys, generation versions or persistent records have been changed.

## Using the complete filled basin

The original polygon-union route is a guide, not a chain of fixed vertices.
Its ordinary vertices are movable spline controls. Only its inlet, outlet and
actual tributary endpoint coordinates are pinned. A pinned node shares the
spline's tangent with neighbouring intervals; no straight approaches are reserved
and no independent meander is generated between pairs of pins.

At control stations, LateralRoom intersects a cross-section with EVERY clipped
polygon in the bed. It merges intervals across triangle/facet boundaries and
finds the connected interval through the station. Actual shores and island holes
limit this interval; internal triangulation does not. Basin-sized offsets use
that room, with allowance for the tapered ribbon width. Where the original banks
already clip, the method still proposes a smaller centreline deviation rather
than declaring the crossing ineligible.

The full crossing gets a correlated one-to-three-lobe Hermite field. Controls can
move sideways over much of the available basin width. A smaller correlated
longitudinal component makes asymmetric broad turns possible; there is no
forward-only restriction or maximum amplitude expressed in river widths.
The overall orientation favours the side with materially more usable room.
The longest quarter receives stronger initial characters.

Interpolated stations are first fitted directly to their actual local space.
Remaining boundary, width, self-intersection and branch problems adjust only
nearby stations with a smooth feather. After a few smooth local corrections,
genuinely blocked neighbourhoods return to their original course. Other lobes
retain their full displacement. This replaces the old whole-span amplitude
retry ladder; there is no global multiplier reducing every bend to a tiny wiggle.
A full original-course fallback remains only when a safe fitted channel cannot
be verified.

## Deterministic variation

The local integer PRNG combines the planet's existing visual seed, basin ID and
quantized inlet/outlet coordinates. It does not depend on a frame clock or Unity
random state. It controls broad-lobe count and irregular spacing, sign/asymmetry,
correlated control strength and overall winding character. Character changes
smoothly between controls; offsets are not independent random values at vertices.

Non-longest crossings vary from gentle to strong characters; the longest quarter
uses stronger initial characters. Both remain bounded by actual available
geometry. True junctions remain exact, even where they interrupt a broad bend.
The nominal sampling budget is 64 intervals for a complete crossing, plus guide
and pin controls; generation and local fitting happen only during presentation
rebuilds. There are no new GameObjects or per-frame calculations.

## Essential safeguards

Every emitted centreline segment must remain in the original filled polygon
union. Ribbon coverage uses its actual arc-length-interpolated inlet/outlet width.
Existing shoreline clipping is allowed without increasing the clipped fraction
relative to the original course at the corresponding station. The final ribbon
builder clips against the same terrain triangle fans, so no river geometry is
emitted across dry islands or hills.

The water vertices now follow those exact rendered sediment triangle planes at
the existing surface offset, instead of floating over their spherical chord
approximation. Exact existing 3D contact positions at genuine inlet, outlet and
junction pins are retained for continuity with adjacent reaches.

New centreline self-intersections and nonlocal ribbon collisions are checked.
Other branches retain actual tapered-width clearance. Exact contact at a known
shared node is allowed, as is existing confluence buffer overlap without making
that overlap worse. Unrelated new contacts are not exempt. Narrow numerical
coincidences at true pins do not impose a long straight section on the river.

No widths or materials are changed. All changes stay in the presentation path;
ordinary valley rivers, routed lake outlets and supplemental outlet corridors
retain their existing generator. Raw TIN debug lines remain raw.

## Concise logs

[GeodesicSedimentMeanders] reports:

- crossings: every constructed sediment crossing.
- nonzero: crossings with measured lateral movement beyond reporting precision.
- substantial: peak lateral movement of at least three widths AND 5% of the full
  original crossing length. This is a diagnostic classification, never a gate.
- medianWidths / p90Widths: length-weighted sampled lateral offsets, including
  unchanged crossings. Lateral distance is measured to the nearest original
  guide segment, not to an arbitrary corresponding rendering vertex.
- maxBasinWidthUsed: the largest final channel's lateral span divided by its
  basin's complete lateral extent, measured normal to its inlet/outlet chord.
  This measures the final path's use of basin width, including original turns.
- constrainedTerrain / constrainedTopology: crossings whose local fitting met
  real boundary/space or collision constraints; they may still meander elsewhere.
- unchanged and unchangedReasons: original-course results and their geometry,
  topology, no-space or numerical reasons. Reasons can overlap.

[GeodesicSedimentMeanderLongest] focuses on the longest original 25%: nonzero and
substantial counts, median/P90 peak widths, median peak/full-crossing fraction,
and median fixed fraction. These include unchanged crossings rather than
reporting only successes.

Fixed fraction measures portions still following the original course. A relative
one-part-per-million precision is used only to avoid counting float noise as
movement; it does not decide whether a candidate is generated or accepted.

## Next manual Unity run

Use the same planet/visibility/width settings and keep authoritative caches.
Inspect the longest diagonal crossings at normal orbital distance first. Expect
one coherent channel with a few broad bends, including formerly turning routes,
rather than repeated tiny independent spans. Then inspect wide open plains,
multiple tributary joins, narrow necks, concave shores and island exclusions.
Check exact connections, width continuity, terrain contact, local straight
fallbacks and absence of loops or new branch collisions.

The existing final-mesh diagnostic is retained, OFF by default:

1. Select the generated planet GameObject containing GeodesicRiverSystem.
2. In that component's context menu, choose Enable Sediment Meander Diagnostic Colours.
3. Green identifies changed channels; orange identifies boundary-constrained
   unchanged portions; yellow identifies branch/topology-constrained portions;
   purple identifies other fixed portions or other producers; cyan is ordinary
   network geometry.
4. Centre the Scene view on a ribbon and use Inspect River At Scene View Centre
   to identify the actual producer, basin, local constraints, displacement,
   basin-width use and projected main-camera pixels.
5. Disable Sediment Meander Diagnostic Colours restores the normal/grade/
   destination palette. Log Longest Rendered River Geometry remains available.

Colours are recorded after clipping on the final live ribbon vertex buffers.
No new debugging subsystem, collider, material or object was added.

Static review only. Unity, tests, builds, simulations, benchmarks and parameter
sweeps were not run. Changes remain uncommitted for manual review.