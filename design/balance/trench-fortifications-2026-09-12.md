# Trench fortifications: behavior and balance check

Status: sector-belt rework implemented; isolated Unity checks complete; native combat
acceptance pending. Last reworked 2026-09-13.

## Intended encounter

A recognizable, connected defensive belt protects its faction's frontline. An early
attack meets two MG emplacements on a 132m fire trench. Leaving it undisturbed extends
the line across the sector and builds a support line and rear redoubt with AT and AA
coverage. Hitting defenders interrupts construction; destroying them has lasting
consequences. Placeholder earthworks are acceptable; cosmetic health, empty rings,
automatic healing and endless defender replacement are not.

| Default elapsed quiet time | Earthworks | Native defenders |
|---|---|---|
| Spawn | Seven bays connected across 132m | 2 MG |
| 45s | Fire trench with weapon pits | 2 MG + 2 ATGM |
| 90s | Line extended across the sector | 2 MG + 2 ATGM + 2 MANPADS |
| 135s | Support line with dugout and communications | Same six |
| 180s | Rear redoubt line, flank hooks, rear dugout and mortar pits | Same six, no additional weapons |

Times follow the configured growth interval (15–180s); damage delays them. One network
advances per 0.5s poll, so simultaneous positions may advance a few seconds apart.
Each addition is atomic: invalid ground rejects the whole stage and retries on the next
tick with a logged reason.

## Rules and counterplay

- Placement uses the owning faction's frontline field. A border cell side becomes a chain
  of sector slots up to 380m apart; there is no road preference. The exact corridor from
  the front line to the rear redoubt must pass ownership, ground and height checks
  (per-row variation ≤4m, row-to-row drift ≤12m). Same-faction networks keep 360m
  spacing, other factions 250m.
- Vanilla buildings own weapon behavior, ammunition, detection, health, damage and rewards.
  No custom DPS, immunity, accuracy boost, replenishment or hidden damage multiplier.
- Any observed part-health loss or defender destruction stops construction for 60s.
- Committed slots never refill. Zero survivors ends growth; losing territorial ownership
  withdraws remaining defenses. Advancing the friendly border does not erase the site.
  Neutralized earthworks expire after 300s.
- Cleared sites block re-seeding within 300m for the scene. History caps at 64 sites;
  new seeding then stops. Active networks cap at 16, defenders at six each / 96 total,
  and each network at 64 nodes / 96 edges.
- The corridor stays open: procedural collision boxes follow its berms, not its floor.
  Native defenses supply persistent combat collision; procedural colliders retain near LOD.
- Stage ticks show development; amber indicates suppression and crossed gray marks
  indicate neutralization. Visible native weapon models show the real threat.

## Balance health: concerns pending playtest

The progression changes capability twice rather than increasing hit points forever.
Losses remain losses and construction suppression rewards an early attack. There is no
repair loop or same-site reward farm. Missing native definitions fail closed; no unrelated
defense prefab substitutes for a requested weapon. Native spawn retries cap at three/slot.

Actual DPS/TTK and engagement range have not been measured: they are properties of the
installed vanilla weapons and target matchup. In-game tests must establish that initial
MGs threaten exposed approaches, AT threatens armor, AA threatens low/slow aircraft, and
appropriate standoff or area weapons can defeat the site. Do not call this balanced yet.

Native units replicate via the game. Procedural earthworks/map marks remain host-local;
remote visual parity is an existing limitation, not a completed feature.

## Evidence and acceptance

Production sources: `TrenchManager.cs`, `TrenchGarrison.cs`, `TrenchGrowthSimulator.cs`,
`TrenchPlacement.cs`, `TrenchTacticalMath.cs`, `TrenchMeshBuilder.cs` in `modules/Trenches`.

The isolated Unity harness exercises the actual graph/garrison adapter with game API
stubs: connected seed, all four growth advances to the full belt, invalid flank terrain,
64/96 graph ceilings through the mature counts, six-defender ceiling, health-loss
suppression, destruction without replacement and an overrun position. It renders all five
stages plus a close-up with the real earthwork material and checks winding/S-curve
continuity and the palette texture. Stubs do not verify native AI or Mirage. Build, pure
assertions and installed-game metadata probes supplement it.

Remaining in-game checks: exact front-line corridor fit; five quiet minutes of belt
growth; hit a gun and observe a quiet-minute pause; destroy one then all guns; verify no
replacements; frontline withdrawal; host/client/late-join native defense state; scene
reload cleanup; LOD0/1/2 transitions and origin shifts during flight.

## Requested skill sources applied

- [Game Studios balance-check](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/balance-check/SKILL.md): progression, unkillable states, loops and unmeasured balance risks.
- [UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): readable progress/state indicators, with shape as well as color.
- [Ponytail](https://github.com/DietrichGebert/ponytail): native combat/spawning instead of another targeting or damage system; one shared mesh profile and material instead of a bespoke asset pipeline; bounded regression checks.
