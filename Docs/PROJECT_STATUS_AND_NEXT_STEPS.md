# Project status and next steps

Reviewed **2026-11-20** against the local source at `f0b4b01`. This page is an entry point. The **[Geodesic migration roadmap](GEODESIC_MIGRATION_ROADMAP.md)** is the single authority for current status, priorities, dependencies and completion gates.

The geodesic environment has terrain/bathymetry, layered ocean chemistry/temperature, global atmosphere and greenhouse forcing, TIN rivers/static lakes, mineral settling and generation caches. Hydrogenotrophy, sulfur chemosynthesis and fermentation are normally reachable, with recorded execution evidence at earlier checkpoints. Active locomotion/taxis, reserve-based growth/division visuals, gradual founders and position-dependent vent temperature are wired; acceptance of the latest thermal/coverage behavior, visuals and ecological balance remains pending. Source and fixtures do not establish a fully validated world.

B2.2 adds Geodesic oxygenic photosynthesis evolution and optional photosynthetic founders. Methane reaction branches remain unreachable, and saprotrophic and predatory feeding remain deferred. Temperature-dependent geodesic land snow/ice and sea-ice visuals are missing. Full geodesic simulation save/load is explicitly rejected by the current service; legacy saves, population DTOs, saved startup settings and generated-planet caches must not be confused with that feature. Reaction-backed execution remains a future consolidation beyond existing scaffold/resource bindings. [B2.2 restoration](GeodesicOxygenicPhotosynthesis.md)

For the implementation sequence, choose a milestone in the roadmap. It covers stabilization and early independent ice visuals, remaining biological parity, integrated ecology/conservation/performance, full persistence and safe retirement/merge gates. Expanded ecology, climate and alternative planetary environments are separate post-migration sections there.

- [Documentation inventory and audit](README.md): every Markdown file, supersession decisions, evidence limits and cleanup record.
- [Geodesic development record](GEODESIC_PLANET_IMPLEMENTATION_PLAN.md): architectural contracts and historical implementation reports, not a competing roadmap.
- [Archived June status](Archive/PROJECT_STATUS_AND_NEXT_STEPS_2026-06-28.md): complete previous overview, legacy persistence/refactor analysis and old task sequence.

This documentation review ran no Unity, builds, tests, simulations or benchmarks. It changed no settings; preserve saved values, including Hydrogen → Fermentation **0.02**. Historical tests/results remain scoped to their documented checkpoints.
