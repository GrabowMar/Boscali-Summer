# Additional Boscali Summer missions

Open the native mission picker and choose **Boscali Summer Gamemodes**. WarBox,
Fuzzball and Heavyweight are generated from the installed **Escalation** mission;
the original mission and player mission files are not edited. Dedicated-server
launch arguments are `-mission gamemode:warbox`, `-mission gamemode:fuzzball` and
`-mission gamemode:heavyweight`.

Every peer needs the same Boscali Summer build. The native mission description
carries the mode marker to clients. The host controls match clocks, role changes,
cargo eligibility, scores and results; clients receive presentation snapshots.
Mission-specific module exclusions remain in force for the whole mission.
The compact in-flight score and route display sits below the shared log/radio
column; the full briefing remains visible on the spawn screen. Draws clear the
sorties and show a draw result; the host restarts or returns through the native
pause menu because the game's result type only distinguishes victory and defeat.

## WarBox

**Brief, choose a role, fly, recover, resupply and report.** A sustained combined-arms
war built on Escalation's actual units, factories, supplies and victory graph.

- Nuclear and strategic escalation thresholds are four times the installed
  baseline. Nuclear production and strategic timers of at least five minutes are
  also four times slower; short messages retain their original timing.
- Both factions receive four times the starting funds, aircraft stocks and reserve
  allocation. Existing factories continue producing replacements. The initial army
  is not duplicated. The native AI wing allowance is eight per faction, reduced by
  friendly players, without an additional increase for enemy players.
- The existing MIS Dynamic Operations board offers reconnaissance, rescue, supply
  escort, repair cover, interception and strike assignments when those modules are
  enabled. Use flight briefings, designated roles and sortie reports for roleplay.
  Dynamic Operations retains its bounded contract board and host update cadence.
- Native flight, damage, logistics and economy remain authoritative. Combat
  adjustment and the Ares/Spooktober event modules are excluded by the preset.
- Win through the original strategic objectives: destroy enemy aircraft factories
  and the reinforcement carrier. There is no forced match timer. The initial
  design targets long sessions, but a four-hour duration is a tuning intention,
  not a measured guarantee.

## Fuzzball

**Choose a plane, spawn airborne, fight and respawn.** Twenty minutes or 75 credited
enemy aircraft kills, whichever concludes first. Higher team score wins when time
expires; equal scores are a draw.

Both teams have the same fixed-wing roster: COIN, trainer, VTOL trainer, CAS,
small fighter, fighter and multirole aircraft. Aircraft replacement stock and
allocation are replenished. Air starts are placed above local terrain and avoid
nearby aircraft. A bounded airborne bot population supports small sessions.

Only the host's native final enemy-aircraft kill credit awards a point. Friendly,
neutral, duplicate and environmental losses do not score. There are no ground or
naval forces, capture objectives or ground reinforcement chains. Surface spawning,
cargo deployment, dismounted pilots and incompatible mod directors are suppressed.
Terrain scenery and the airfield selection interface remain available.

## Heavyweight

**Load, escort, transport and unload; the other side intercepts.** BDF transports
first, then PALA. Each leg lasts twenty minutes with a sixty-second intermission.
The team with the most delivered containers across its transport leg wins. Equal
scores are a draw; kills never add points.

1. Spawn the transport aircraft at **Maris Airport**. Fit actual container cargo,
   such as the QuadVTOL's munitions container or Utility Helicopter's fuel container.
   Native mount keys verified in installed missions are `MunitionsContainerx1`
   and `FuelContainer2x1`.
2. Remain landed at Maris long enough for the host to register the actual mounted
   cargo. The status panel shows the assigned route and eligible cargo aboard.
3. Fly to **K92 Highway Strip** or **South Boscali General Aviation**, according to
   the assigned route. The destination offered to new source loads alternates
   every five minutes; a departed load keeps its assignment.
4. Release the physical container at the destination. It must be intact, unslung,
   close to the ground and stationary within the receiving zone for five seconds
   on the host's mission clock. Each accepted container earns **one point** and is consumed.
   Merely landing with cargo still fitted does not score.
5. Launch another transport sortie or provide escort. Interceptors deny deliveries
   by attacking aircraft and cargo. Fixed airfield infrastructure is protected so
   both legs retain the same usable course.

At intermission the host clears aircraft, missiles and cargo. Scores retain their
original faction identity; the transport/interceptor roles exchange. The same
physical source and destinations change ownership, giving the second transport
team the same geography and route schedule. Aircraft supply and funds use the
same replacement policy for both sides. All earlier cargo eligibility is discarded.
Return to Maris and obtain a fresh container load before another delivery; cargo
fitted or rearmed elsewhere has no source eligibility. A new transport sortie also
provides a fresh load.

## Implementation and checks

`modules/Gamemodes` owns the catalog, JSON transforms, pure match rules, native
game adapter, scoped patches, snapshot protocol and status overlay. Core supplies
the mission-picker provider and persistent mission module policy. Existing
Scenario picker providers coexist with the new group.

The JSON builder copies unknown fields and validates the native objective format.
Competitive missions discard the inherited campaign unit lists and objective
chains; runtime enforcement also handles built-in map objects and subsequent
spawns. WarBox retains the native objective/outcome graph and modifies named
timing/resource fields only.

Run the offline checks:

```powershell
nomod test Gamemodes.Tests --mod-root .
nomod test Gamemodes.MissionTests --mod-root .
```

The mission-construction runner also accepts an installed Escalation JSON document
on standard input with `--stdin`. This checks the actual route keys, both faction
stocks and every retained/removed mission list without committing the game's
mission content to the repository. The initial construction check passed 199
assertions using the installed game's `resources.assets` mission.

The rules runner passed 126 assertions. Cold native startup passed for all three
modes. Fuzzball had zero surface units while its airborne population grew from
four to six. WarBox initialized its combined-arms battlefield with Dynamic
Operations enabled. These are startup observations, not balance measurements.
Fuzzball's native player-launch and combat scenario also passed: airborne spawn,
one point from the native credited aircraft kill, unchanged score after a repeat
report, matching native scoreboard and zero physical surface units.

The Heavyweight gameplay scenario passed 25 checks: native Maris hangar spawn,
actual mounted cargo registration and release, physical destination settlement,
one-point credit without a duplicate, halftime cleanup, retained score and swapped
roles. Its following warm restart restored leg one, zero scores and twelve Maris
hangars. Aircraft repositioning and shortened deadlines are explicit sandbox
fixtures; the test does not establish piloted flight or forty-minute balance.

Run those reproducible native checks with nomodkit:

```powershell
nomod sim run fuzzball warbox --mod-root . --inst gamemodes --mode hidden
nomod sim run play-fuzzball --mod-root . --inst gamemodes --mode hidden
nomod sim run play-heavyweight heavyweight --mod-root . --inst gamemodes --mode hidden
```

Feature-specific loopback checks are in nomodkit at `mods/boscalisummer/tests/Gamemodes.Tests/peers.py`;
run with nomodkit's Python environment. Multiplayer is still unverified: the
initial attempt could not satisfy the toolkit's 11 GB free-memory requirement,
and its retry stopped at a concurrent checkout compilation error before joining.
Long campaign duration, spawn fairness,
cargo-route balance, latency and Heavyweight peer delivery still need playtesting.
WarBox does not add campaign saves.

## Design references

The requested [game-design-skills-bundle](https://github.com/Stanestane/game-design-skills-bundle)
was reviewed before implementation. Its
[core-loop extractor](https://github.com/Stanestane/game-design-skills-bundle/blob/master/game-design-core-loop-extractor/SKILL.md)
guided the three distinct loops; the
[multiplayer audit](https://github.com/Stanestane/game-design-skills-bundle/blob/master/game-design-multiplayer-feature-audit/SKILL.md)
guided team contribution and coordination; the
[fairness audit](https://github.com/Stanestane/game-design-skills-bundle/blob/master/game-design-fairness-frustration-audit/SKILL.md)
and [red-team audit](https://github.com/Stanestane/game-design-skills-bundle/blob/master/design-red-team-audit/SKILL.md)
guided symmetric rounds, visible scoring and abuse cases. These were read as design
guidance; no third-party skill package was installed or vendored.
