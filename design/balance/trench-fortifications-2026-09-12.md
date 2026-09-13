# Trench fortifications: behavior and balance check

Status: implementation and isolated Unity checks complete; native combat acceptance pending.

## Intended encounter

A recognizable, connected defensive position protects its faction's frontline. An early
attack meets two MG emplacements. Leaving it undisturbed allows AT and AA coverage and
rear communications to develop. Hitting defenders interrupts construction; destroying
them has lasting consequences. Placeholder earthworks are acceptable; cosmetic health,
empty rings, automatic healing and endless defender replacement are not.

| Default elapsed quiet time | Earthworks | Native defenders |
|---|---|---|
| Spawn | Five bays connected across 72m | 2 MG |
| 45s | Deeper zigzag fire trench | 2 MG + 2 ATGM |
| 90s | Rear defensive/communication line | 2 MG + 2 ATGM + 2 MANPADS |
| 135s | Rear hub connected to the rear line | Same six, no additional weapons |

Times follow the configured growth interval (15–180s); damage delays them. One network
advances per 0.5s poll, so simultaneous positions may advance a few seconds apart.

## Rules and counterplay

- Placement uses the owning faction's orange-border field, near roads when possible.
  The complete 120m reserve and actual native emplacement footprints must pass ground checks.
- Vanilla buildings own weapon behavior, ammunition, detection, health, damage and rewards.
  No custom DPS, immunity, accuracy boost, replenishment or hidden damage multiplier.
- Any observed part-health loss or defender destruction stops construction for 60s.
- Committed slots never refill. Zero survivors ends growth; losing territorial ownership
  withdraws remaining defenses. Advancing the friendly border does not erase the site.
  Neutralized earthworks expire after 300s.
- Cleared sites block re-seeding within 1200m for the scene. History caps at 64 sites;
  new seeding then stops. Active networks cap at 16, defenders at six each / 96 total.
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
stubs: connected start, all growth stages, invalid rear terrain, six-defender ceiling,
health-loss suppression, destruction without replacement and an overrun position.
It renders all four stages and checks winding/S-curve continuity. Stubs do not verify
native AI or Mirage. Build, pure assertions and installed-game metadata probes supplement it.

Remaining in-game checks: exact road/ground fit; five quiet minutes of growth; hit a gun
and observe a quiet-minute pause; destroy one then all guns; verify no replacements;
frontline withdrawal; host/client/late-join native defense state; scene reload cleanup.

## Requested skill sources applied

- [Game Studios balance-check](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/balance-check/SKILL.md): progression, unkillable states, loops and unmeasured balance risks.
- [UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): readable progress/state indicators, with shape as well as color.
- [Ponytail](https://github.com/DietrichGebert/ponytail): native combat/spawning instead of another targeting or damage system; bounded regression checks.
