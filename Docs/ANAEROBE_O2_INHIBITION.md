# Anaerobe O2 Inhibition

Runtime oxygen pressure is modeled primarily as **metabolism reaction inhibition**, not as a simple concentration-based death timer.

## Mutation gate vs runtime inhibition vs direct damage

* `TooMuchO2` is a mutation-gate block reason. It prevents new anaerobic metabolism mutation attempts in oxic habitats and is intentionally separate from runtime ecology.
* Runtime anaerobe O2 inhibition affects organisms that already exist. Local layered O2 smoothly lowers the useful reaction efficiency of configured O2-sensitive metabolisms.
* Starvation or energy depletion should usually be the resulting death path when inhibited organisms can no longer gain enough energy/carbon.
* Direct `DeathCause.O2_Toxicity` is retained only for optional, extreme oxidative damage. It is disabled by default.

## Settings

Settings live on `ReplicatorManager` under **Anaerobe O2 Inhibition** and are passed into `ReplicatorMetabolismSystem.Settings`.

| Setting | Default | Meaning |
| --- | ---: | --- |
| `anaerobeO2InhibitionEnabled` | `true` | Master toggle for runtime O2 reaction inhibition. |
| `anaerobeO2ComfortMax` | `0.02` | Local layered O2 at or below this value leaves sensitive reactions at full efficiency. |
| `anaerobeO2StressMax` | `0.12` | Local layered O2 at or above this value applies the configured minimum efficiency. |
| `anaerobeO2MinEfficiencyMethanogenesis` | `0` | Methanogenesis is strongly inhibited by high O2. |
| `anaerobeO2MinEfficiencyFermentation` | `0.65` | Fermentation is only mildly/moderately inhibited and is not directly killed by default. |
| `anaerobeO2MinEfficiencyHydrogenotrophy` | `0.25` | Hydrogenotrophy is moderately inhibited when modeled as anaerobic H2 + CO2 metabolism. |
| `anaerobeO2MinEfficiencySulfurChemosynthesis` | `0.6` | Sulfur chemosynthesis is mild/configurable because sulfur metabolisms can be diverse. |
| `anaerobeO2ReplicationMinEfficiency` | `0.35` | Replication is suppressed only when inhibited efficiency falls below this threshold. |
| `anaerobeO2DirectDamageEnabled` | `false` | Optional direct oxidative damage timer. |
| `anaerobeO2DirectDamageThreshold` | `0.25` | High local O2 required before optional damage accumulates. |
| `anaerobeO2DirectDeathSeconds` | `120` | Damage seconds required for direct `O2_Toxicity` death if enabled. |
| `anaerobeO2StressSpeedMultiplier` | `0.75` | Speed cap at maximum inhibition. |

The inhibition curve uses smooth interpolation between comfort and stress. The resulting efficiency multiplier is applied to useful reaction output for affected metabolism branches.

## Per-metabolism behavior

* **Methanogenesis**: strongly O2-inhibited. In high O2 it gains little or no useful metabolism and should decline mainly through energy failure/starvation.
* **Fermentation**: weak/moderate O2 inhibition. Fermenters are not killed simply because O2 is present.
* **Hydrogenotrophy**: moderate O2 inhibition by default, configurable through its minimum-efficiency setting.
* **SulfurChemosynthesis**: mild/configurable O2 inhibition and no direct damage by default. TODO: split sulfur metabolism later into anaerobic sulfur phototrophy, sulfur reduction, and aerobic sulfur oxidation if the model needs those distinctions.
* **Methanotrophy**: not part of this anaerobe inhibition path; it keeps its separate CH4/O2 handling.
* **Saprotrophy, Predation, Photosynthesis**: not part of this anaerobe inhibition path.

## Debug/Inspector fields

Use the `ReplicatorManager` debug fields in play mode:

* `debugAnaerobeO2InhibitedCount`: organisms currently under O2 reaction inhibition.
* `debugAnaerobeO2AverageInhibition`: average inhibition severity for inhibited organisms.
* `debugAnaerobeO2DirectDamageCount`: organisms currently accumulating optional oxidative damage.
* `debugAnaerobeO2KilledCount`: direct O2 toxicity deaths this tick/window.
* `debugAnaerobeO2StressedAverageLocalO2`: average local O2 among inhibited organisms.

To verify the intended behavior, compare `debugAnaerobeO2KilledCount` with starvation/energy death telemetry. Under defaults, O2 pressure should mostly appear as inhibited metabolism followed by starvation or energy depletion, not direct O2 kills.

## Known limitation

Bottom-layer O2 may still become too high because of ocean layer mixing/propagation. That transport issue is separate from this metabolism inhibition correction and should be tuned independently.
