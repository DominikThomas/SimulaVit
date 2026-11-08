# Planet-generation loading screen

The existing **Startup Fade Overlay** object in `PlanetScene` owns the new screen.
In its **StartupFadeOverlay → Planet Generation Screen** Inspector section, assign
**Loading Background** to an optional Sprite. No artwork is required. The separate
uGUI Image uses `AspectRatioFitter.EnvelopeParent` inside a masked viewport, so it
covers the screen without distortion. Transparent areas retain an opaque dark
fallback. Background and text-backing colours are also configurable there.

The screen is constructed once per overlay, reused across starts, hidden on return
to setup, and destroyed with its owner. It uses the project's uGUI and TextMeshPro.
The title, stage, subtitle and pass have separate text objects and setters. The
text column spans approximately 4.5–52% of screen width and is bottom-aligned;
text wraps as the window aspect changes. A local fading scrim supports future artwork.

## Generation boundaries

`PlanetGenerationProgress` carries a stable `PlanetGenerationStage` and optional
pass/count metadata. The generation methods expose `IEnumerable` sequences: each
report marks the point **before** its next synchronous work. The startup controller
shows the report and yields a frame before calling `MoveNext` again. Existing
synchronous entry points drain the same sequences immediately, preserving the
operation ordering and using the same computation code.

The normal geodesic sequence follows the implementation, rather than an invented
schedule: topology → cell terrain → oceanic relief → ocean classification →
bathymetry → inland uplift distances → ocean/temperature initialization → render
surface → initial river drainage → each enabled carving pass and its complete TIN
rebuild → final lake construction → final network extraction → lake routing when
lake bodies exist → river ribbons → remaining surface/collider/vent initialization
→ atmosphere visuals → startup resources/biology → ready.

Some labels recur because the pipeline genuinely returns to that kind of work.
Carving uses the existing configured count (including three passes); no count,
seed, terrain calculation, hydrology calculation or resource initialization
calculation was changed. Disabled optional labels are skipped. Stage/subtitle copy
lives in `PlanetGenerationLoadingView`, separate from generation logic. Identical
reports do not rewrite text; pass changes only rewrite the pass indicator.

The initial screen receives two null yields so starting from an IMGUI callback also
gets a complete render opportunity. The simulation clock remains paused and the sun
rotator retains its pre-generation enabled state while suspended, keeping render
waits from changing initial lighting. Final root restoration gets a frame under the
opaque screen before the HUD and completed world are revealed. Start Paused keeps
the simulation paused.

Exceptions reaching the startup runner are logged with `Debug.LogException` using
the original exception. Explicit topology/resource/required-component and biology
initialization failures also reach the failure screen. **Return to configuration**
uses the existing menu teardown before reopening configuration. Startup configuration
serialization, migration and save/load routines are unchanged.

## Limits and manual verification

Each TIN rebuild, carving plan, lake build/routing operation, mesh/collider build
and other computational stage remains synchronous. The screen stays visible but
cannot repaint or accept input during those calls. There is intentionally no
spinner, percentage, fictitious timer or inner-loop yield. Legacy cube-sphere
generation remains one synchronous planet-building stage, with repaint boundaries
around startup initialization. Existing elapsed-time profile logs can include the
render waits when using the interactive path; they are not new performance measurements.

Only a lightweight C# compile check was performed. No automated tests, benchmarks,
simulation captures, parameter sweeps or Play Mode runs were performed.

Manual checks in `PlanetScene`:

1. Start a geodesic simulation with the existing three-pass carving configuration.
   Verify immediate coverage, pass 1/3 through 3/3, final lake/ribbon stages, and no
   menu, HUD or partial-world flash before completion.
2. Try Start Paused, return to the main menu, and start again. Verify one overlay,
   no stale failure/pass text, correct HUD restoration and the intended clock state.
3. Disable carving/lakes and verify their stages are omitted. Try legacy mode too.
4. Assign a Sprite and resize through wide, 4:3 and portrait windows. Verify cropping,
   bottom-left wrapping, legibility and fallback coverage with no Sprite.
5. In a temporary scene copy, remove a required ocean resource component to exercise
   failure handling. Verify Console diagnostics, the failure screen and return to
   configuration. Restore the component and retry.
