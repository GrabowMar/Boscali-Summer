# Command module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/Command`. This module owns
the OPS THEATER tab (no separate COM bezel), DynamicMap overlay legend and overlay renderer, influence grid rasterization,
AI order vector extraction, rank-gated strategic doctrine postures, and AI target scoring integration.

Do not import sibling feature implementations directly; resolve cross-feature contracts through
`ServiceRegistry` and `Framework/Contracts` (e.g. `IProgressionView`).

All map rasterization is performance-first: use 64x64 discretized grids, flat pre-allocated buffers,
throttled update frequencies (2 Hz for grid, 10 Hz for vectors), and zero-cost sleep when the map
is closed. Vector visualization uses pooled game objects. AI target scoring integrates via
`AiTargetScoringPatch` without mutating vanilla mission state.
