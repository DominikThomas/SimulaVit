# Startup Biology: metabolic evolution controls

> **UI checkpoint with later extensions.** [Gradual founder controls](GeodesicFounderEmergence.md) and [vent visual controls](HydrothermalVentVfx.md) now supplement this report. The [roadmap](GEODESIC_MIGRATION_ROADMAP.md) owns current reachability/status; enabled settings alone are not proof of an implemented gameplay pathway. Preserve saved Hydrogen → Fermentation **0.02**.


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
