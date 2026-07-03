# Anaerobe O2 Toxicity

## Purpose

Mutation gates stop new anaerobic metabolisms from appearing in oxic habitats, but existing anaerobic organisms also need runtime pressure when oxygen rises. Anaerobe O2 toxicity adds a gradual, timer-based oxygen crisis instead of killing every anaerobe as soon as local O2 is non-zero.

## Affected metabolisms

The runtime toxicity helper is configurable per metabolism. Defaults enable toxicity for:

* Methanogenesis
* Fermentation
* Hydrogenotrophy
* SulfurChemosynthesis

The system intentionally does **not** apply this anaerobe toxicity path to:

* Saprotrophy
* Predation
* Photosynthesis, including the dark/anoxic fallback
* Methanotrophy, which keeps its existing O2 handling

## Default thresholds and settings

Settings live on `ReplicatorManager` under **Anaerobe O2 Toxicity** and are passed into `ReplicatorMetabolismSystem.Settings`.

| Setting | Default | Meaning |
| --- | ---: | --- |
| `anaerobeO2ToxicityEnabled` | `true` | Master toggle for runtime anaerobe O2 toxicity. |
| `anaerobeO2ComfortMax` | `0.02` | Local layered O2 at or below this value is treated as comfortable. |
| `anaerobeO2StressMax` | `0.12` | Local layered O2 at or above this value applies maximum stress. |
| `anaerobeO2ToxicDeathSeconds` | `30` | Accumulated toxic seconds before O2 toxicity death. |
| `anaerobeO2StressEnergyMultiplier` | `1.5` | Extra basal stress multiplier at maximum O2 stress. |
| `anaerobeO2StressSpeedMultiplier` | `0.5` | Speed cap at maximum O2 stress. |
| `anaerobeO2ToxicityAffectsHydrogenotrophy` | `true` | Per-metabolism toggle. |
| `anaerobeO2ToxicityAffectsSulfurChemosynthesis` | `true` | Per-metabolism toggle. |
| `anaerobeO2ToxicityAffectsFermentation` | `true` | Per-metabolism toggle. |
| `anaerobeO2ToxicityAffectsMethanogenesis` | `true` | Per-metabolism toggle. |

O2 stress is computed from the local layer-aware O2 value. At or below the comfort threshold the toxic timer decays. Between comfort and stress thresholds, organisms accumulate toxic seconds slowly and take mild performance penalties. At or above the stress threshold, the timer accumulates faster, replication is suppressed, basal stress increases, and speed is capped more strongly.

## Difference from mutation gate `TooMuchO2`

`TooMuchO2` is a mutation-gate block reason: it prevents new anaerobic mutation attempts in habitats whose local O2 is already too high. Anaerobe O2 toxicity is runtime ecology: it acts on organisms that already have an anaerobic metabolism after oxygenation reaches their current layer.

The two systems share the idea of local O2 sensitivity, but their thresholds are intentionally separate. This lets playtests tune mutation availability independently from survival during oxygenation events.

## Inspecting in play mode

In the `ReplicatorManager` inspector, watch the serialized debug fields:

* `debugAnaerobeO2StressedCount`
* `debugAnaerobeO2KilledCount`
* `debugAnaerobeO2StressedAverageLocalO2`

Death telemetry also records O2 toxicity as `DeathCause.O2_Toxicity`, shown in debug summaries as `O2Tox`.

## Expected playtest signs

* Anoxic or low-O2 anaerobes should behave mostly as before.
* Methanogenesis populations exposed to high local O2 should accumulate `O2ToxicSeconds`, lose replication ability while stressed, slow down, and eventually die with `O2_Toxicity` if exposure persists.
* Fermentation, Hydrogenotrophy, and SulfurChemosynthesis should show similar but configurable stress under high O2.
* Saprotrophy, Predation, Photosynthesis, and Methanotrophy should not be killed by this new anaerobe toxicity system.
