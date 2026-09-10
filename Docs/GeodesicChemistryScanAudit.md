# Geodesic abiotic chemistry scan audit

## Cadence and call chain

Chemistry is not independently scheduled. `GeodesicOceanResourceField.Update` advances a fixed
resource cursor and calls `TickResources` once per `transportIntervalSeconds` (default and startup
diagnostic: **5 simulated seconds**). A resource tick executes vent injection, air-sea exchange,
horizontal accumulation, vertical accumulation, staged concentration writeback plus candidate
refresh, and finally `GeodesicAbioticChemistry.Step`. The chemistry interval is therefore exactly
the resource transport interval; this change does not alter it.

`Step` computes four cadence-stable reaction fractions, decays the visual rusty-water signal once
per surface cell, and scans the packed candidate indices. For each candidate it loads O2, H2, H2S,
Fe2 and one physical layer volume, converts all four concentrations to double inventories, applies
oxidation followed by FeS precipitation, and commits all four concentrations only if a reaction
occurred. A reactive node also commits same-column sediment, rusty-water memory, cumulative
reaction inventories, and resource spatial-variation flags.

## Root cause and operation audit

The profiled scan cost is candidate-density dependent. Before this branch, the candidate predicate
was merely `H2 > 0 || H2S > 0 || Fe2 > 0`. Thus every node containing any single reduced resource
entered `Chemistry.Scan`, even when O2 was absent and the FeS pair was incomplete. Each such inert
candidate still paid three reduced-resource reads, one O2 read, one physical-volume lookup, four
float-to-double volume multiplications, `ReactNode`, and `PrecipitateFeS`. Only writeback, sediment,
and cumulative telemetry were already avoided by the result check.

The original full active-node work now occurs in the already-required staged transport writeback:
for each active node it performs seven state reads, seven staged-delta reads, seven concentration
writes, and one cached-volume read. Candidate refresh is fused into that pass and adds four direct
array reads plus a predicate; it does not add another ocean traversal. At subdivision 6 this pass
visits the reported 112,679 active nodes. `Chemistry.Scan` subsequently visits
`ChemistryCandidateCount`, from zero through all active nodes depending on state. There are no
per-tick temporary collections, LINQ, closures, boxing, or managed allocations. The candidate
array is allocated once at world initialization. The rusty-water array is allocated on first use or
world-size change only.

Reactions do not independently rescan nodes. Relevant concentrations and volume are loaded once,
both reactions operate sequentially on local inventory variables, and final concentrations are
written once. Oxidation intentionally precedes FeS precipitation, so FeS sees the remaining H2S
and Fe2. Cumulative authoritative product counters are cheap scalar additions. Expensive global
chemistry/sediment diagnostics are already isolated in `GeodesicChemistryTelemetry` and run at its
default 60 simulated-second interval with a default five-real-second throttle.

## Exact reaction predicates

Current abiotic reactions are:

* H2 oxidation: positive O2, positive H2, and positive configured reaction fraction; O2 demand is
  0.5 per H2 inventory.
* H2S oxidation to deposited S0: positive O2, positive H2S, and positive fraction; O2 demand is
  0.5 per H2S inventory.
* Fe2 oxidation to deposited Fe3: positive O2, positive Fe2, and positive fraction; O2 demand is
  0.25 per Fe2 inventory.
* FeS precipitation: positive H2S, positive Fe2, and positive FeS fraction, with 1:1 consumption.

The conservative cheap predicate is therefore `(O2 > 0 && (H2 > 0 || H2S > 0 || Fe2 > 0)) ||
(H2S > 0 && Fe2 > 0)`. IEEE comparisons also reject NaN and nonpositive values. It deliberately
does not inspect configuration fractions: a disabled reaction can cause a harmless false positive,
but no possible reaction is omitted and runtime chemistry settings can change without rebuilding
extra state.

## Optimization and invalidation contract

The packed candidate list now uses that complete-pair predicate. This is a safe refinement of the
existing sparse architecture, not a new write-observer system. The list is rebuilt in active-node
order every resource tick **after** vent sources, air-sea exchange, horizontal and vertical
transport, and staged writeback. Consequently vent, either transport destination/frontier,
air-sea O2, biology, initialization, and all direct resource APIs are observed from authoritative
concentrations before chemistry runs. Chemistry consumption naturally removes a node on the next
refresh, and later production re-adds it. Clearing/reinitializing the world resets the count and
buffer. The one-pass rebuild cannot emit duplicates.

This arrangement is already a natural hybrid: sparse oceans scan only packed candidates, while a
chemically widespread ocean degrades to the same active-node order and O(N) reaction work without
hashing, sorting, generation stamps, or a separate dense implementation. A density threshold would
only add branches and parity surface because packed dense iteration is equivalent to direct active
iteration. `denseFallbackTicks` is consequently reported as a diagnostic when candidate count
equals active count, rather than selecting a duplicate runtime path.

Profiler counters expose total active nodes, packed candidates/nodes visited, reactive nodes,
applied reaction operators, nodes skipped by the prefilter, oxidation candidates, FeS candidates,
sediment nodes, chemistry ticks, and dense-list ticks. `Chemistry.CandidateRefresh` identifies the
fused authoritative writeback/refresh stage, `Chemistry.VisualMemoryDecay` separates the per-column
visual pass, and the existing `Chemistry.Scan` remains the fused local reaction stage. Splitting
reactant read, inventory conversion, reactions, and writeback into separate ocean passes—or adding
per-node profiler scopes—would increase the hot-path cost, so they intentionally remain fused.

## Horizontal mixing audit (no optimization in this branch)

Horizontal mixing iterates every horizontal layer link once per resource tick. For each channel
whose `resourceMayHaveSpatialVariation` flag is set and whose coefficient is nonzero, it evaluates
one pair transfer, so work is `horizontalLinkCount * activeResourceChannels`; uniform channels are
already skipped by an active bit mask. A pair with equal endpoint concentration returns before
delta writes, but still incurs reads and arithmetic. The startup log reports exact link count and
the profiler counters report active/skipped channels and link-resource evaluations.

A future branch could evaluate per-channel active link frontiers or stronger uniform-region
tracking, but must account for source, exchange, vertical transport, biology, and generic writes.
That is a separate, higher-risk transport invalidation problem and is not required for chemistry
candidate correctness because candidate refresh observes post-transport authoritative state.

## Measurement status and next step

The deterministic tests establish predicate boundaries, sparse operation counts at 112,679 nodes,
reaction ordering, physical-volume scaling, stoichiometry, and conservation. They do not provide a
Unity wall-clock or managed-allocation measurement. Profile with Deep Profile off at 20x, 50x, and
100x, capturing ordinary and chemistry frames. Record `CandidateRefresh`, `VisualMemoryDecay`,
`Chemistry.Scan`, candidate/reactive counts, horizontal counters, and GC allocation. The next
optimization should be chosen from those measurements; if `HorizontalMixing` remains 8–9 ms, audit
transport frontier tracking in its own branch.
