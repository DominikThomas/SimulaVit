# Startup Biology controls

## B2.1 Geodesic biology availability

In **New Game → Biology → Biology availability**, each of the eight metabolisms and four locomotion types has its own checkbox. Availability applies at world startup and gates acquisition by founders and offspring mutations. Living organisms retain their traits and can pass the same traits to offspring. Geodesic methanotrophy also checks availability before feeding. Legacy Cube Sphere uses its existing biology rules.

Geodesic vent founders require **Hydrogenotrophy** and **Passive drift**, including delayed batches and ongoing gradual emergence. Optional founders require their respective metabolism and Passive drift. Photosynthetic founders use lit water, saprotroph founders use suitable death-recycling sites, methanogen founders use suitable anoxic submarine outlets, and methanotroph founders use local CH4 and O2 overlap. Predator founders need living prey at an oxygenated, thermally suitable site. All optional counts default to zero. An empty metabolism or locomotion selection blocks founding. Setup shows the requirements and the Console reports requested and placed counts. [Photosynthesis](GeodesicOxygenicPhotosynthesis.md) · [Saprotrophy](GeodesicAerobicSaprotrophy.md) · [Methanogenesis](GeodesicMethanogenesis.md) · [Methanotrophy](GeodesicAerobicMethanotrophy.md) · [Predation](GeodesicPredation.md)

Availability leaves the current division, habitat, resource, and temperature requirements in effect. If sulfur is enabled and its roll selects it, it retains priority over fermentation even when its habitat check fails. Disabling sulfur uses the existing fermentation-only draw sequence, as when the sulfur probability is zero. Disabling fermentation excludes its roll. A locomotion roll for a disabled destination yields ordinary inheritance with the original draws and no retry. For example, enabling flagellar movement alone cannot skip the amoeboid step.

Fresh configurations use the scene's startup defaults, which in the supplied scene enable Hydrogenotrophy, Sulfur chemosynthesis, Fermentation and all four locomotion types. Startup JSON files missing the new fields get those same availability defaults. Explicit empty selections are saved and restored. Availability is stored in two optional fields in the existing startup JSON format. Mutation probabilities, reaction rates and temperatures remain separately saved, including the configured Hydrogen → Fermentation probability of **0.02**.

Use **New Game → Profiles → Save As** to keep an experimental setup with its current biology settings and availability selections. Use **Load** to restore it before starting a fresh world. Named profiles use the same migration behavior and preserve deliberate zero values and hidden settings. [Configuration profiles](StartupSetupTabs.md#configuration-profiles)

| Preset | Availability selected |
|---|---|
| All implemented pathways | Hydrogenotrophy, Sulfur chemosynthesis, Fermentation, Oxygenic photosynthesis, Aerobic saprotrophy, Methanogenesis and Methanotrophy. Enables all four locomotion types. |
| Existing core biology only | Hydrogenotrophy, Sulfur chemosynthesis, Fermentation and all four locomotion types. |
| Passive-only movement | Passive drift only. Keeps the selected metabolisms. |
| Photosynthesis-focused | Hydrogenotrophy and Oxygenic photosynthesis only. Keeps the selected locomotion types. |

The Photosynthesis-focused preset keeps Hydrogenotrophy enabled for vent founders and leaves the optional founder counts as configured. B2.3 activates the Saprotrophy temperature template, both saprotrophy mutation probabilities, dissolved OrganicC intake and assimilation, and the shared aerobic yields. B2.4 activates the Methanogenesis template, Fermentation → Methanogenesis probability, CO2 and H2 limits, energy and assimilation. B2.5 activates the Methanotrophy template, Hydrogen → Methanotrophy probability, the CH4 and shared aerobic O2 acquisition thresholds, CH4 and O2 limits, energy and assimilation. [B2.3 controls](GeodesicAerobicSaprotrophy.md) · [B2.4 controls](GeodesicMethanogenesis.md) · [B2.5 controls](GeodesicAerobicMethanotrophy.md)

B2.6 activates the Predation temperature template and Saprotrophy → Predation probability. The Predation controls include the evolution time, motility gate, capture distance and probability, cooldown, assimilation, aerobic yield, energy yield, candidate budget and aggregate diagnostics. [B2.6 controls](GeodesicPredation.md)

### User-run manual validation checklist

- **Persistence and migration.** Back up the startup JSON (normally `startup_config.json`) and load a pre-B2.1 copy containing the saved `0.02` probability. Confirm the core metabolism and all-locomotion defaults. Change toggles, save through setup, and restart to verify the exact selections. Repeat with an empty metabolism selection and an empty locomotion selection independently. Verify the probability is still `0.02`.
- **Presets and startup summary.** Apply each preset and compare the checkboxes to the table. Verify the mutation probabilities, reaction rates and temperatures stay as configured. Start a world and check for one resolved availability summary. Let the world advance and confirm that line does not recur.
- **Founder restrictions.** Try immediate batch startup with Hydrogenotrophy off, delayed batch startup with Passive drift off, and gradual startup with ongoing emergence enabled and an empty selection. Confirm that the world starts, setup and the startup log explain the restriction, and the population stays empty after the configured delay or event times. Re-enable both founder traits and confirm founders emerge at eligible vents.
- **Metabolic transitions.** With eligible parent births and habitat, compare Sulfur and Fermentation enabled against each disabled individually. Confirm disabled destinations produce no new mutants and enabled destinations still require their existing resources and other gates. With the core preset, compare a fixed-seed run against the preceding version at identical settings and simulated times.
- **Locomotion and inheritance.** With Passive-only movement and high mutation probabilities, confirm founder descendants stay passive. Enable the alternate types individually and verify the existing upgrade order, bottom-layer anchoring requirement and anchored release setting. If checking existing disabled types with a diagnostic fixture, confirm the organisms still react, move and can pass their current traits to descendants.
- **Legacy isolation.** Save restrictive Geodesic availability, then start Legacy Cube Sphere. Confirm its founding and mutations still use the legacy settings. Return to Geodesic setup and confirm the selections were retained.

## Historical evolution controls checkpoint

> **Historical UI checkpoint.** B2.2–B2.5 extended the original controls below. Preserve saved Hydrogen → Fermentation **0.02**. [B2.2 photosynthesis](GeodesicOxygenicPhotosynthesis.md) · [B2.3 saprotrophy](GeodesicAerobicSaprotrophy.md) · [B2.4 methanogenesis](GeodesicMethanogenesis.md) · [B2.5 methanotrophy](GeodesicAerobicMethanotrophy.md) · [Gradual founder controls](GeodesicFounderEmergence.md) · [Vent visual controls](HydrothermalVentVfx.md) · [Roadmap](GEODESIC_MIGRATION_ROADMAP.md)


Later milestone: [Fermentation restoration](GeodesicFermentationParity.md) activates Hydrogen → Fermentation, its reaction parameters and its thermal template in geodesic mode. The earlier unsupported-fermentation entries below describe the prior UI.

The Biology tab now exposes the existing general metabolism mutation chance and Hydrogen → Sulfur conditional probability in both grid modes. Values are read from and edited directly in `currentConfig.biology`; they remain probabilities in the existing 0–1 representation.

The Evolution area has separate Metabolic evolution, Other metabolic transitions, Biomass mutation, Locomotion evolution and Temperature adaptation groups. Inactive geodesic settings remain visible and disabled with a “Not yet implemented” explanation. Their saved values are retained for legacy mode. No new configuration field, version change, default change or runtime algorithm is introduced.

| Settings | Geodesic Icosphere | Legacy Cube Sphere |
|---|---|---|
| `metabolismMutationChance`, `hydrogenToSulfurMutationChance` | Editable; active for hydrogenotroph offspring subject to division and local habitat gates | Editable; existing ordered legacy transitions and gates apply |
| `hydrogenToPhotosynthesisMutationChance`, `hydrogenToSaprotrophyMutationChance`, `hydrogenToFermentationMutationChance`, `fermentationToMethanogenesisMutationChance`, `hydrogenToMethanotrophyMutationChance`, `saprotrophyMutationChance`, `predatorMutationChance` | Visible, disabled; no corresponding reachable mutation edge | Editable; runtime unlocks, resource requirements and predator enablement still apply |
| `biomassMutationChance`, `biomassMutationScale` | Visible, disabled; offspring inherit their biomass target | Editable |
| `locomotionMutationChance`, `locomotionUpgradeChance`, `locomotionAnchoredMutationChance`, `allowAnchoredToAmoeboidMutation`, `anchoredToAmoeboidMutationChance` | Existing editable controls retained once; release probability is disabled when release is disallowed | Existing editable controls retained once |
| `tempMutationChance`, `tempMutationScale` | Existing editable controls retained once | Existing editable controls retained once |

The help text explains the outer general roll, subsequent conditional sulfur roll, and local H2S/CO2 requirements in the child's actual habitat. It does not display the product as an unconditional probability. Legacy help also notes earlier checks, transition ordering and unlock gates.

## Configuration path

The existing path is unchanged:

1. `DrawBiologyTab` calls the shared `DrawMetabolicEvolution`; `SetupFloat` edits the existing fields by reference.
2. `SaveCurrentStartupConfig` / normal startup saving uses `SavedStartupConfig.FromConfig` and the existing v23 flat field mappings.
3. `LoadSavedStartupConfig` invokes the existing deserializer and normalization. Missing fields retain configured defaults; explicit zero remains zero.
4. Startup `ApplyConfigStages` calls `config.biology.Apply(replicatorManager)` before biology initialization.
5. The manager's geodesic step applies its resolved controls before calling the runtime. `SetSulfurControls` therefore receives the selected probabilities before any reproductive event.

Reset Defaults still clones the configured scene defaults through the existing reset logic. No hardcoded probability is substituted by the UI. Locomotion diagnostic overrides retain their existing precedence and existing help text.

## Validation

`SimulationStartupEvolutionTests` exercises the real IMGUI drawing method using native key events, rather than an arithmetic substitute. It verifies editing .01 → .2 and .003 → .4, disabled geodesic controls, and every existing legacy transition/biomass control. Tests call the real startup file writer and reader with isolated GUID-named files, create a fresh controller for reload, verify configured-default reset/normalization, and run the actual manager geodesic step through births with selected .2/.4 and either probability zero. Deferred probabilities set to 1 still do not activate other pathways.

Existing sulfur fixtures verify local H2S/CO2 gates, inheritance and reproduction. Existing chemistry, hydrogenotrophy, movement and temperature fixtures are included in the broader native regression run. Full OS application restart is represented by a fresh startup-controller load through the production reader; no live user application is restarted.

The graphics-free attempt executed seven non-GUI cases successfully but could not create an IMGUI view. It is retained as a harness attempt, not a GUI pass. GUI cases explicitly require a graphics device; the native validation command uses graphics. An initial graphics-enabled run exposed an incorrect test assumption about an unrelated default; the assertion was changed to preserve and compare the actual starting value.

Native validation reuses the existing disposable project and the guarded tool:

```powershell
Tools/ValidateSulfur.ps1 -NativeProject <absolute-disposable-project> -Regressions -AllowStartupTestFiles -Graphics
```

`-AllowStartupTestFiles` (also enabled automatically by `-Regressions`) permits startup-file writes only below the disposable project's `Temp/StartupEvolutionAudit/`. The default shared startup file is blocked by a guard inserted only in the disposable source copy. Production persistence code remains unchanged. The original saved configuration is backed up read-only and its hash is compared after execution. `-Graphics` is needed for native IMGUI input; purely headless runs skip those three GUI cases.

**Final results:** all **10 native startup evolution cases passed**, including the three real IMGUI input cases, actual save/fresh-controller load, configured-default reset and manager births. The broad graphics-enabled EditMode run executed **659 cases: 642 passed and 17 failed**. The 17 failure names are identical to the established baseline; none are new UI failures. Managed compilation passed, along with the existing **55 carbon/chemistry** and **20 food/taxis** selected cases. Counts overlap and should not be added as unique tests.

The real user startup file remained byte-for-byte identical to its pre-test backup (SHA-256 `EBD39B23AAD0181D4D7AE356AC5A14E61D3658306CC72A0E52BA4ED40CFF5D0C`). Native counts, complete test names and the preservation check are recorded in `Diagnostics/StartupEvolutionValidation.json`. The known broader baseline failures are reported separately from the new UI cases. All source changes remain uncommitted on the current branch.
