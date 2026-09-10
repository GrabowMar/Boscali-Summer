# Command module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/Command`. This module owns
the STR bezel screen (theater SA, the frontline board, faction tasking, theater logistics and
doctrine), the SET settings screen, the DynamicMap overlay renderer, the discrete tactical
sector grid, AI order vector extraction, rank-gated strategic doctrine postures, and AI target
scoring integration.

STR is a screen, not a tab inside someone else's panel, and it must install and fail on its
own: no free bezel slot logs a warning and leaves OPS untouched. It reads faction tasking
through `ISecondaryObjectivesView` and simply drops the tab when nothing publishes one.

A readout states what it verified. A counter nothing produces does not belong on the panel,
and a figure that could not be established reads as a dash — never as a confident zero.

Do not import sibling feature implementations directly; resolve cross-feature contracts through
`ServiceRegistry` and `Framework/Contracts` (e.g. `IProgressionView`).

All map rasterization is performance-first: use 32x32 discretized tactical sector grids, flat pre-allocated buffers,
throttled update frequencies (2 Hz for grid, 10 Hz for vectors), and zero-cost sleep when the map
is closed. Faction troop presence dictates sector control and frontline boundaries. AI target scoring integrates via
`AiTargetScoringPatch` without mutating vanilla mission state.

## Observation and planning lessons

- Boscali planning annotates the battlefield. Aircraft routes, formations, autopilot
  overrides, and standing aircraft orders remain outside this module; follow the shared
  avionics product split and exclude Wing Command's published wing from doctrine effects.
- Persistent observation pins and planning geometry need GlobalPosition plus mission
  identity. Distinguish AGL from sea-level altitude; an unsuccessful terrain query means
  unknown clearance. Sample between endpoints before presenting corridor clearance.
- Enemy displays use faction-known positions and observation age. If that information
  is unavailable, show unknown/stale or omit it; do not fall back to the live Transform
  of an untracked enemy. Camera visibility alone does not grant faction tracking.
- Route/arrival estimates are advisory and must invalidate on changed destination,
  stale observations, or lost source. A sampled clear corridor is not guaranteed safe.
- Any future workspace/bookmark persistence needs a bounded, versioned schema, finite
  numeric/range checks, coordinate-space tags, validated loading before replacement,
  and atomic writes. Do not persist Unity instance IDs as durable identity.
- New armed map gestures must use the shared MapPicker. Register input deliberately,
  respect text-entry focus, and verify actual binding support before advertising HOTAS.
