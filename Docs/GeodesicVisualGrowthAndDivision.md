# Passive geodesic visual growth and division

Implemented on `codex/geodesic-biology-parity`. Changes are uncommitted for manual Unity review. Existing milestone documentation is preserved.

## Carbon to diameter

`Replicator.size` and packed `Size` remain the **mature mesh scale**. The renderer applies a separate diameter multiplier to the previous passive scale, `0.1 * max(0.1, Size) * 0.50`. Consequently `Size = 1`, multiplier `1`, uses exactly the previous mature diameter. Multipliers affect all three mesh axes equally.

For normal growth cycles:

```text
C = packed OrganicCStore (authoritative inventory)
B = carbonBaseline (presentation-only observation at birth / after successful division)
T = max(1, divisionBiomassMultiple) * packed BiomassTarget (actual carbon gate)
p = clamp01((C - B) / (T - B))
curve = lerp(p, p*p*(3 - 2*p), growthCurveSmoothing)
diameterMultiplier = lerp(minimumDiameterMultiplier, maximumDiameterMultiplier, curve)
```

The defaults are minimum `0.333`, maximum `1.0`, curve smoothing `1`, division duration `0.65` seconds at normal simulation speed, and requested daughter separation `0.06` world units. Controls are under **ReplicatorManager → Geodesic Visual Growth and Division → Visual Growth Settings**. Configuration is sanitized locally; maximum diameter never exceeds the previous mature diameter.

Founders have zero stored carbon; their packed entry captures that baseline on insertion. Age is irrelevant, including founders given a sampled age. They start at the minimum diameter. Uptake increases progress; no uptake means no further growth. Carbon loss can reduce size, clamped at the minimum. A 0.002 progress deadband suppresses tiny oscillations. Smoothing is a curve in carbon space, so no time-based growth continues after uptake stops. Rendering still samples the existing discrete reaction updates; it does not invent intervening carbon.

Progress saturates at one: temperature, energy, population-cap, or random-trial blocking cannot produce unlimited size. Each descendant captures its **own actual retained store after the existing split** as its new baseline. Unequal splits therefore do not leave one descendant accidentally mature. The baseline is never added to or subtracted from biological inventory.

The formula reads each organism's current packed target, including different/mutated targets. If `T - B` is zero, negative, or within numerical tolerance, there is no remaining interval to the absolute threshold. The fallback visual span is one `T` of additional carbon above `B`. Such a descendant starts small even if already biologically eligible and can divide again immediately under the original rules. This deliberate presentation convention avoids division by zero and repeated instant inflation. Invalid/nonpositive thresholds leave progress at zero; they do not repair biological state. High stores and progress are bounded. Existing biomass mutation probabilities and the geodesic runtime's currently inherited biomass targets are unchanged.

## Successful division and separation

The original reproduction gate, random draws, thermal mutation, conservative energy split and carbon split execute in their original order. Presentation starts only after a successful birth; it never blocks or repeats reproduction.

Before the transfer, the presentation samples the parent's current diameter and displayed planet-local position. After the transfer:

1. Both packed visual cycles reset to their respective retained carbon baselines.
2. The existing parent and daughter render instances start at the same displayed center and diameter. Overlapping spheres act as two lobes of the original body.
3. Both diameters ease toward their new cycle's carbon-derived size. With no new uptake, the endpoint is `0.333` for both.
4. The daughter's display center eases to its actual newborn position. The parent's authoritative position is unchanged; an interrupted previous transition can also ease its remaining display offset to zero.
5. At completion, both centers equal their packed positions, without a final snap back into a shared position.

This is a two-lobe sphere animation, without a generated membrane neck or extra temporary instance. Both organisms continue independent passive drift throughout; there is no animation-dependent movement force, steering, speed, reproduction cooldown, habitat choice, or metabolism.

The daughter receives a small deterministic **authoritative placement offset at birth**, derived by hashing its existing seed without consuming any random stream. Candidate tangents are checked against the source cell's real neighboring-cell bisectors. The accepted position stays within the same valid ocean cell and layer; no habitat transition occurs. The angular offset is capped at 10% of the minimum neighbor spacing and 0.1 radians, as well as the requested world distance. Eight directions and at most twelve distance halvings per direction bound placement work. The largest valid candidate is used.

Placement preserves the parent's current spherical radius, including the existing smooth depth transition to its logical layer center. It never reconstructs a surface-shell position. Newborn local direction, passive tangent, radius, wander schedule and vertical schedule initialize before a possible render. Subsequent movement uses those separate coordinates, so ending the visual offset cannot restore the original shared position. Natural later encounters remain possible; no collision avoidance was added.

If a valid local offset cannot be represented, birth remains successful at its inherited position and `Failed Daughter Separations` increments. Normal passive initialization still runs. This fail-safe favors conservation and habitat validity over an arbitrary relocation; inspect a nonzero failure count during manual validation. A configured separation of zero intentionally disables displacement.

The display offset is transported along the organism's local spherical direction and projected onto its existing radius. Duration advances in the existing lifecycle pass using simulation dt, even when rendering is disabled. Pause freezes it; accelerated simulation shortens or skips transitions between rendered frames. A new division replaces the parent's one existing transition from its current presentation. There is no event queue or retained pair history.

## Ownership, removal and snapshots

`ReplicatorPopulationState.VisualGrowth` stores one value-type `ReplicatorVisualState` per packed capacity slot. Insertion initializes or restores it; capacity growth resizes it with the other arrays; swap-back copies the whole value alongside its organism; removal clears the vacated entry. Reset retains capacity but every reused slot is overwritten before becoming live. There are no animation references to packed indices, companion objects, or sibling lifetimes. One sibling can die or divide again independently.

Snapshot schema 4 carries the entire visual struct: baseline, progress, diameter, relative animation elapsed/duration, initial diameter, planet-local offset and birth direction. The population snapshot also carries explicit geodesic cell identity and reads biomass target from packed authority. Capture/apply reads/writes the packed state directly; no broad synchronization was added. Older snapshots without visual state initialize a new visual baseline at their loaded carbon store. This retains their biological inventory but cannot reconstruct historical growth.

**Full geodesic save-file loading remains unavailable in the existing save/load service.** Its explicit guard is preserved. The population DTO/capture/apply path now retains presentation state, but this milestone does not implement missing geodesic environmental persistence or change the existing passive-kinematics save policy. A population-only round trip requires the same initialized planet/runtime and does not restore that environment. Existing runtime diagnostic IDs are regenerated on reload, while the visual struct needs no IDs to resume correctly.

## Inspection and cost

The existing growth snapshot APIs expose current carbon, actual threshold, visual baseline, growth progress, mature scale, current diameter multiplier, division status, start diameter and eased transition progress. `visualSize` now means mature `Size * diameterMultiplier` for passive geodesic organisms. Other locomotion render sizes are unchanged.

Use **Biology Diagnostics → Refresh Selected Growth** while paused or running. **Refresh Latest Division Visuals** reads the latest recorded birth's stable parent/daughter IDs and exposes both current snapshots and found flags. These ID scans occur only on explicit inspection, not per frame. Latest birth accounting also records both baseline/diameter values at the event. Accounting diagnostics must be enabled to retain that latest event.

Manager aggregate fields show minimum/average/maximum diameter multipliers and currently dividing count from the latest render, plus cumulative invalid visual-state repairs, replaced transitions, constrained separations and failed separations. Invalid state is repaired only in presentation fields; no carbon or energy repair occurs. Rendering-disabled aggregate values can be stale; on-demand growth snapshots still calculate current carbon progress and division phase. Existing reporting frequency is unchanged, with no new per-frame or per-birth logs.

Division accounting still checks C/E balance, gates, age and identical habitat. Position checks now allow the configured bounded tangential daughter displacement, verify the common radius and neighbor bisectors, and keep the parent fixed. `daughterSeparationDistance` is the actual displacement; `placementError` / `maxPlacementError` measure excess beyond the allowed displacement. Movement diagnostics record the actual submitted centers and explicit presentation offset; drift continuity and selection scores use the offset-corrected center. Simulation trails and identity tracking remain intact.

`Graphics.DrawMeshInstanced` retains the same 1023-instance batches and shared mesh/material/property block. There is still one instance per passive organism, including division. Growth arithmetic and aggregates run in the existing render loop; only dividing organisms need the offset transforms. The lifecycle adds one bounded timer update. Birth placement has bounded local-neighbor work. No per-organism GameObjects, meshes, materials, managed frame allocations, extra population scan or animation queue were added. Visual state adds roughly 52 bytes per packed capacity slot plus the companion's same-sized serialization mirror; snapshots become larger. Capacity growth retains its existing amortized array allocations. Performance has not been measured in this milestone.

## Manual Unity validation

- Start a fresh geodesic population at normal speed. Compare founder and mature diameters: approximately 1:3, with mature `Size = 1` matching the previous display. Check sampled founder ages do not change their initial size.
- Inspect one stable organism ID. Confirm uptake raises `visualGrowthProgress` from its saved baseline; no uptake holds size; carbon loss gives a bounded response. At the absolute threshold, block reproduction via temperature, energy, probability or cap and verify it stays mature without an attempted-division animation.
- Observe a successful birth at normal speed, then pause midway. Verify two lobes shrink and separate, both baselines equal their own retained carbon, C/E accounting remains balanced, pause freezes the transition, and resuming finishes without a positional snap or re-merging.
- Try asymmetric carbon splits, a changed packed biomass target, surplus carbon above the threshold, zero animation duration, and zero/requested-large separation. Confirm finite bounded size, documented surplus-baseline behavior and bounded valid placement.
- Follow parent and daughter through a coastal cell, a pentagon, a layer-depth transition and a translated/rotated planet. Check cell/layer validity, drift diagnostics, selection, and constrained/failed separation readouts. There should be no unexplained movement anomalies or invalid planet-center positions.
- Cause a death/removal during another organism's division, including a swap from the final population slot. Follow IDs and verify animation/baseline stay with the correct survivor; no extra recycling occurs. Start a new simulation and check clean visual/readout initialization.
- Accelerate through many births, disable/re-enable rendering, and use a population exceeding 1023. Check mixed instance sizes, bounded transition replacement, every-frame visibility and no allocation spikes from presentation beyond existing capacity growth.
- Review schema-4 population capture/apply under the same initialized runtime, including midway through division, and an older snapshot without visual fields. Verify baseline/relative transition restoration without inventory changes. Keep the existing rejection of full geodesic save-file loading as an expected limitation.

## Biological limits and verification scope

Stored OrganicC is reserve inventory, not tracked structural biomass. This display therefore can shrink with reserve consumption; it resets after retained-carbon splitting even when substantial reserves remain. One-third diameter corresponds to about one twenty-seventh the sphere volume. Neither that ratio nor two shrinking overlapping lobes represents conserved physical cell volume. The surplus-baseline fallback can show a small organism that is already biologically ready to divide. No structural carbon pool, volume conservation, additional uptake, or biomass consumption is implied or implemented.

Verification is source inspection, C# 9 syntax-only parsing (editor and non-editor preprocessing), and `git diff --check`. Unity Editor/Play Mode, automated tests, builds, simulations, planet generation and benchmarks were not run. Manual appearance, runtime correctness and performance validation remain with the user. Stop for manual review; no commit or merge.
