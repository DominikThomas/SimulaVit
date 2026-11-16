# Geodesic horizontal dissolved-resource transport audit

## Scope and authoritative schedule

This audit is limited to horizontal dissolved-resource transport. The authoritative default is
**5 simulated seconds** (`SimulationStartupConfig` and `SimulationStartupController`), normalized
to a minimum of 0.01 s when applied. The scheduler repeatedly consumes every complete interval up
to its per-render-frame guard (default 64), retains both the unprocessed backlog and fractional
remainder, and resumes it on later frames. It does not discard simulation time.

The exact operator order in one completed interval is:

1. compact vent/source injection;
2. L0 air-sea exchange;
3. clear the resource-major staged inventory-delta buffer and prepare stable coefficients;
4. horizontal accumulation;
5. vertical accumulation;
6. staged concentration writeback, fused with chemistry-candidate refresh;
7. abiotic chemistry (including chemistry-owned sediment deposition).

Biology and public resource APIs can also write concentrations outside this operator. Chemistry
marks every resource it changes as spatially varying, so the next horizontal tick cannot skip it.

## Original algorithm and cost model

At the audited main revision, startup builds each horizontal link's physical conductance once from
edge conductance, overlap thickness, incident geometry sums, layer volume, and the physical
Unity-volume conversion. The hot loop therefore performs **no physical-volume reads** and no
geometric helper/property calls. Per active link/channel evaluation it performed:

- two packed concentration reads;
- one cached conductance read (shared across channels by the former link-major traversal);
- coefficient arithmetic and one concentration subtraction;
- zero delta writes when the computed transfer was exactly zero, otherwise two `double` delta
  read/modify/writes.

The previous loop was link-major and unrolled all seven resource channels. It read each link's two
node indices and conductance once, but executed seven mask branches and advanced two resource
indices seven times for every visited topology link. A monotonic `resourceMayHaveSpatialVariation`
bit skipped channels which had never received a potentially non-uniform write. `AccumulatePair`
already avoided delta writes for equal endpoints because `k * (Ca - Cb)` was exactly zero. Thus an
active localized channel still visited every horizontal link, while a globally untouched/uniform
channel visited none.

The full staged buffer is `7 * nodeCapacity` doubles and was (and remains) cleared once per
transport tick because vertical transport writes every channel and the mandatory writeback reads
every active node/channel. A touched-index scheme would add stamps/list traffic to both transport
stages and would not avoid that mandatory traversal, so it was rejected without Unity evidence
that clear time is material.

`horizontalLinkCount` is runtime topology authority, not a subdivision-only constant: land/ocean
mask, active layer counts, and neighbouring columns' overlapping layers determine it. A complete
subdivision-6 icosphere has 40,962 cells and 122,880 surface edges; five active ocean layers over
every edge would be 614,400 horizontal layer links. Consequently the previously reported ~328k is
plausible for one particular ocean/depth realization but is **not** a value that current source can
confirm generically. Initialization logs the exact active-node, horizontal-link, and vertical-link
counts for every generated world.

For a runtime with `L` links and `A` varying enabled channels, the original work was `L*A`
link/channel evaluations, `2*L*A` endpoint reads, `L*A` conductance uses, zero physical-volume
reads, and `2*nonzeroFluxLinks` delta writes. Existing fast paths skip `(7-A)*L` whole-channel
evaluations and all delta writes on equal endpoints. The profiler's reported 8-14 ms self time is
therefore most consistently attributed to the nested link/channel scan and accumulation rather
than geometry or volume lookup. Unity marker capture is still required to confirm that inference.

## Implemented dense-loop optimization

Horizontal accumulation is now channel-major. Channel selection occurs once; each active channel
computes its packed offset and tick coefficient once, then scans topology in authoritative link
order. This removes seven per-link activity branches and repeated resource-index stepping while
making concentration reads sequential within one packed channel. Equal endpoints are rejected
before conductance is loaded or floating-point flux arithmetic is performed. Zero/zero is a
strictly counted subset of this exact-equality path; no epsilon or ecological cutoff exists, so
`float.Epsilon` remains transportable. Link order, transfer formula, cached physical conductance,
staged `double` accumulation, volume-based writeback, clamps, cadence, and coefficients are
unchanged.

The topology arrays and conductance are reread for each active channel rather than shared across
channels. This is an evidence-driven trade: typical worlds have fewer than seven varying channels,
and the removed unpredictable mask branches plus packed channel locality target the observed hot
path. The near-dense worst case still performs exactly `7*L` evaluations with simple linear array
walks; it adds topology cache reads but no asymptotic work, allocation, hashing, or sorting.

## Why sparse/frontier tracking was rejected

Sparse transport cannot use chemistry's candidate set: it must include every link with one
nonzero endpoint so diffusion can expand into empty water. Exact support may change through reset,
vent injection, air-sea exchange, horizontal and vertical mixing, biology, chemistry, and public
set/add/withdraw APIs. The fused writeback can cheaply count nonzero nodes, but converting those
nodes to a deduplicated ordered candidate-link frontier requires another adjacency traversal plus
generation stamps (or pervasive writer bookkeeping). Writes after writeback invalidate such a
snapshot before the next transport tick. Rebuilding an exact frontier then costs an active-node
scan plus candidate-link construction before discovering whether dense fallback is required.

That maintenance is fragile for localized reappearance and likely costs as much as a dense scan
for widespread CO2/Fe2/CH4/O2. No sparse or hybrid path was therefore introduced. There is no mode
threshold or activation contract to maintain: each channel is either skipped by the existing
authoritative variation bit or processed by deterministic dense order. This is outcome B of the
optimization brief.

## Allocation-free measurements

The parent `GeodesicOceanResource.HorizontalMixing` marker is retained, with nested
`ChannelSelection` and `LinkScanAndAccumulate` markers. Staged writeback has its own accurately
scoped marker because it occurs after both horizontal and vertical accumulation and cannot
truthfully be nested under horizontal mixing.

Profiler counters report horizontal ticks, active/dense channels, sparse channels (always zero for
the rejected design), skipped uniform channels, links visited, zero-zero skips, all equal endpoint
links, nonzero-support links, nonzero-flux links, and delta writes. Detailed link classifications
are sampled every 12 ticks by default (60 simulated seconds at the default cadence); ordinary ticks
use a separate counter-free inner loop. Serialized per-resource arrays
report support nodes (collected during mandatory writeback), actual links visited, links with at
least one nonzero endpoint, and unequal-endpoint links. `supportNodes / activeNodeCount` is the
requested support fraction; unequal links are the direct gradient work measure. These counters use
preallocated arrays and local integer increments and perform no per-tick managed allocation.

The vertical audit remains deliberately observational: it visits `verticalLinkCount * 7`
link/channel pairs on every tick, with two endpoint reads, one cached conductance use, and the same
zero-transfer delta-write fast path. `VerticalMixing` remains separately marked and was not changed.

## Verification and profiling plan

EditMode differential tests compare the old/reference formula with the optimized exact-equality
kernel for all-zero, uniform nonzero, isolated support, compact plume, broad gradient, checkerboard,
almost-global, and globally varying states. Additional cases cover unequal volumes, tiny positive
values, pentagon-like degree-five and hexagon-like degree-six adjacency in the synthetic graph,
continued propagation into empty neighbours, and 1,000 conservative ticks. The production solver's
shallow-column behavior is represented by its existing horizontal-link arrays: links only exist for
overlapping active layers, so the kernel never invents a missing-layer edge.

Unity was unavailable in the implementation environment; no wall-clock improvement or support
distribution is fabricated. On a comparable subdivision-6 world with Deep Profile off, capture
20x, 50x, and (if practical) 100x ordinary and resource-tick frames. Record achieved multiplier,
CPU frame time, the parent and nested horizontal markers, staged writeback, Chemistry.Scan,
AirSeaExchange, VerticalMixing, and GC allocation/frame. For scenarios A (early anoxic vent), B
(oxygenated mature), and C (all varying), copy the per-resource support/visited/support-link/unequal
arrays. Scenario C should remain `7*horizontalLinkCount`; absent uniform channels should be zero;
localized channels remain dense in this conservative implementation. Compare against the supplied
historical 8-14 ms horizontal marker rather than claiming an unmeasured speedup.

If horizontal time falls as expected, air-sea exchange is the next already-observed environment
bottleneck. If it does not, use the new marker split and counters to decide whether exact frontier
maintenance is justified by measured localized-link ratios before adding any sparse state.
