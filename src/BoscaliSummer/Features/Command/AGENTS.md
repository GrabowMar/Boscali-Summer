# Command module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/Command`. This module owns
the OPS THEATER tab (no separate COM bezel), DynamicMap overlay renderer, discrete tactical sector grid,
AI order vector extraction, rank-gated strategic doctrine postures, and AI target scoring integration.

Do not import sibling feature implementations directly; resolve cross-feature contracts through
`ServiceRegistry` and `Framework/Contracts` (e.g. `IProgressionView`).

All map rasterization is performance-first: use 32x32 discretized tactical sector grids, flat pre-allocated buffers,
throttled update frequencies (2 Hz for grid, 10 Hz for vectors), and zero-cost sleep when the map
is closed. Faction troop presence dictates sector control and frontline boundaries. AI target scoring integrates via
`AiTargetScoringPatch` without mutating vanilla mission state.
