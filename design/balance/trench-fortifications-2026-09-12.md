# Trench fortifications: behavior and balance check

Status: natural-curve rework implemented; pure suite and isolated Unity checks complete;
native combat acceptance pending. Last reworked 2026-09-15.

## Intended encounter

A recognizable defensive line follows the real front and protects its faction's ground. An
early attack meets two MG emplacements on a shallow scrape. Leaving it undisturbed deepens
it into a full fire trench, then lays a support trace and communication links behind it,
then a rear redoubt with AT and AA coverage, and finally pushes listening posts into no
man's land. Hitting defenders interrupts construction; destroying them has lasting
consequences. Placeholder earthworks are acceptable; cosmetic health, empty rings,
automatic healing and endless defender replacement are not.

| Default elapsed quiet time | Earthworks | Native defenders |
|---|---|---|
| Spawn | Shallow scrape along the fitted curve | 2 MG |
| 45s | Full fire trench profile with traverses | 2 MG |
| 90s | Support trace ~110m behind, communication links, works | 2 MG + 1 ATGM |
| 135s | Rear redoubt trace ~220m, weapon pits | 2 MG + 1 ATGM + 1 MANPADS |
| 180s | Two forward saps with listening posts | Same four, no additional weapons |

Times follow the configured growth interval (15–180s); damage delays them. One position
advances per 0.5s poll, so simultaneous positions may advance a few seconds apart.
Each stage is atomic: invalid ground rejects the whole addition and retries on the next
tick with a logged refusal.

## Rules and counterplay

- Placement starts from Command's ordered front traces (the control field's interpolated
  zero contour). Each trace is resampled into a Bezier curve, its owner decided by probing
  ownership 40m either side of the line (160m when a contested cell answers both ways), and
  every station searches five candidate depths (36–84m) for the flattest, lowest ground. A
  position is at most 1200m long; positions keep 360m same-faction spacing, 250m
  other-faction, and cap at 16 active per theater.
- Water, cliffs and broken ground break the line into runs; the far side of a gap is still
  entrenched. A closed trace (beachhead pocket) is resampled with wraparound tangents.
- Vanilla buildings own weapon behavior, ammunition, detection, health, damage and rewards.
  No custom DPS, immunity, accuracy boost, replenishment or hidden damage multiplier.
- Any observed part-health loss or defender destruction stops construction for 60s.
- Committed slots never refill. Zero survivors ends growth; losing territorial ownership
  withdraws remaining defenses. Advancing the friendly border does not erase the position.
  Neutralized earthworks expire after 300s.
- Defenders cap at four per position (two MG teams spread along the curve, an ATGM at the
  centre, a MANPADS at the support centre once that trace exists), four positions
  neutralized at once cannot exceed 64 total. Each position keeps at most 48 low obstacle
  boxes, active only at LOD0; they stop vehicles without a collider per ditch segment.
- Stage ticks show development; amber indicates suppression and a crossed gray mark
  indicates neutralization. Visible native weapon models show the real threat.

## Balance health: concerns pending playtest

The progression changes capability twice rather than increasing hit points forever.
Losses remain losses and construction suppression rewards an early attack. There is no
repair loop or same-site reward farm. Missing native definitions fail closed; no unrelated
defense prefab substitutes for a requested weapon. Native spawn retries cap at three/slot.

Actual DPS/TTK and engagement range have not been measured: they are properties of the
installed vanilla weapons and target matchup. In-game tests must establish that initial
MGs threaten exposed approaches, AT threatens armor, AA threatens low/slow aircraft, and
appropriate standoff or area weapons can defeat the position. Do not call this balanced yet.

Native units replicate via the game. Procedural earthworks/map marks remain host-local;
remote visual parity is an existing limitation, not a completed feature.

## Evidence and acceptance

Production sources: `TrenchManager.cs`, `TrenchPlanner.cs`, `TrenchLine.cs`,
`TrenchTerrain.cs`, `TrenchGarrison.cs`, `TrenchWorks.cs`, `Domain/TrenchTraceMath.cs`,
`Visuals/TrenchMeshBuilder.cs`, `Visuals/TrenchVisualChunk.cs` in `modules/Trenches`, plus
`CopyFrontlineTraces` in `modules/Command/Runtime/TacticalSectorGrid.cs`.

The pure suite covers Bezier resampling (straight, bent, closed), the depth-planning
dynamic program (level ground, hollow, rise, blocked runs), traverse wave and ditch
densification, run splitting, stage gates and defender/works budgets, and the Command-side
front-trace chaining (ordering, boundary, diagonal contour, capacity, closure). The
isolated Unity harness exercises the planner, growth and garrison adapter with game API
stubs: a trace plans a position on owned ground, blocked ground refuses it, all four growth
advances lay their traces, native defender counts, health-loss suppression, destruction
without replacement and an overrun position. It renders all five stages plus a close-up
with the real earthwork material and checks winding, terrain conformance and S-curve
continuity. Stubs do not verify native AI or Mirage. Build, pure assertions and
installed-game metadata probes supplement it.

Remaining in-game checks: curve fit against a real diagonal front and a beachhead ring;
five quiet minutes of belt growth; hit a gun and observe a quiet-minute pause; destroy one
then all guns; verify no replacements; frontline withdrawal; host/client/late-join native
defense state; scene reload cleanup; LOD0/1/2 transitions and origin shifts during flight.

## Requested skill sources applied

- [Game Studios balance-check](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/balance-check/SKILL.md): progression, unkillable states, loops and unmeasured balance risks.
- [UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): readable progress/state indicators, with shape as well as color.
- [Ponytail](https://github.com/DietrichGebert/ponytail): native combat/spawning instead of another targeting or damage system; one shared mesh profile and material instead of a bespoke asset pipeline; the node/edge growth graph deleted rather than refactored; bounded regression checks.
