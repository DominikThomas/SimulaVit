# Performance benchmark tests

`PerformanceBenchmarks` runs a deterministic, code-driven `PerfBenchmarkScene` and captures:

- `AvgFrameMs` over measured frames
- `P95FrameMs`
- `GCAllocPerFrame` (bytes) using Unity profiler recorder (`GC Allocated In Frame`)
- Population counts (`agents`, `predators`) for context

## Scenario configuration

- Planet resolution: `32`
- Agent count: `5000`
- Random seed: `424242`
- Predators benchmarked in two modes:
  - `PredatorsOff`
  - `PredatorsOn`
- Warmup: `5s`
- Measurement: `10s` (max `600` frames)

## Regression gate

Baselines are stored in `Assets/Tests/Performance/PerformanceBaseline.json`.

Current gate rules:

- `AvgFrameMs <= baseline.avgFrameMs * 1.15`
- `GCAllocPerFrame <= baseline.gcAllocPerFrame + gcAllocMarginBytes`

If baseline is missing:

- Local: test logs guidance and passes.
- CI (`CI=true` or `GITHUB_ACTIONS=true`): test fails.

## Intentional baseline update workflow

1. Run play mode tests locally and capture the `[PERF]` logs for `PredatorsOff` and `PredatorsOn`.
2. Update values in `PerformanceBaseline.json` for `avgFrameMs`, `p95FrameMs`, and `gcAllocPerFrame`.
3. Keep `gcAllocMarginBytes` small (for example 64-256 bytes) unless there is a justified allocation model change.
4. Commit the JSON update together with the code change that caused performance movement.
