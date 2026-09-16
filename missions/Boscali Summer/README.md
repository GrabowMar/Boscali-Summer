# Boscali Summer

A full-length custom mission for Nuclear Option, authored in-repo and built from the built-in
`Escalation` battlefield (same map, airbases and unit placement) with a staged campaign timeline.

> The PALA coup has already failed. For three weeks the putschists held the capital ring and half
> the airfields; the BDF reorganised outside the cordon, and this morning the counter-offensive
> opens. The old order is gone — what began as a coup is now ordinary war, and it will be fought
> with everything both sides have.

**BDF (`Boscali`)** is the counter-offensive; **PALA (`Primeva`)** is the putsch holding the ground
it seized. Both sides are joinable. Victory is the vanilla path: destroy every enemy aircraft
factory and sink the enemy fleet carrier, which arrives with the fleets mid-battle.

## Install

Copy the folder so the file sits next to a folder of the same name:

```
%USERPROFILE%\AppData\LocalLow\Shockfront\NuclearOption\Missions\Boscali Summer\Boscali Summer.json
```

One-liner from the repo root:

```powershell
$dst = Join-Path $env:USERPROFILE 'AppData\LocalLow\Shockfront\NuclearOption\Missions\Boscali Summer'
New-Item -ItemType Directory -Path $dst -Force | Out-Null
Copy-Item '.\missions\Boscali Summer\Boscali Summer.json' $dst -Force
```

`Mission.Name` is derived from the folder/file name, so both must stay `Boscali Summer`.
The file in this folder is the source of truth; the mod does not copy missions into the game
folder at runtime, so re-copy after rebuilding. No `meta.json` is needed to play.

## Act structure

| Act | Trigger | What changes |
| --- | --- | --- |
| I — H-Hour | mission start; `Beat_02_OpenFire` at H+2 min | Dawn, haze, wind light. BDF ring-strike flight launches from Maris, PALA alert flight from The Farm; both nets go weapons free; the ring objective opens. |
| II — The Ring Breaks | `Act1_Ring`: BDF captures The Farm + Dustbowl; `Act2_Clock` 300 s counter-attack; `Act2_Garrison` clearance | PALA counter-attack column spawns from Agrapol; BDF gets funds/score, +income, +airframes; The Farm is hardened (capturable off, defence 30). |
| III — Haze, Storm and Reinforcements | `Beat_05/08/11/14/17` (H+5…17 min) | Desert and mountain convoys, BDF city convoys, a BDF heavy bomber package, and the storm front: 08:30 haze → 12:30 storm (cloud base 950, wind 16). |
| IV — The Capital | `Act3_Capital`: BDF captures Agrapol (objective opens when the ring falls) | 900 s bridgehead beat spawns a BDF armoured push at South Boscali; Agrapol rearm/harden; +reserves; the coast objective opens. |
| V — The Coast and the Island | `Act4_Coast`: BDF captures Sandrift → `Act5_Island`: 60 % of the island garrison | Sandrift opened as the forward rearm field; BDF funds/score; the Vigil Cay raid opens. |
| VI — Naval Turn and Strategic Release | `Beat_20_NavalWarn` 20 min, `Beat_24_Naval` 24 min, `Act6_Strategic` (ring + capital + coast + island + fleets in play) | Both carrier groups spawn; Sink Fleet Carrier objectives open; dusk 18:36; both sides get +warheads, +AI limit, +airframes; Vigil Cay and Sandrift hardened. |
| VII — Night Endgame | `Beat_28/32/36/40/46/52` (28…52 min) | Coastal sweeps and patrol groups, deep night 21:30, night-strike and last-stand waves, pre-dawn 03:48, both sides' reserves and kill rewards raised. |
| Escalation ladder | `ScoreGate_BDF_*` / `ScoreGate_PALA_*` at 200 / 400 / 625 / 850 / 1225 | Per-faction cumulative sortie score: each step releases a reinforcement wave and an economy step. 625/1225 match the tactical/strategic warhead gates. |
| PALA counter-offensive | `PALA_Push1` → `PALA_Push2` → `PALA_Push3` | PALA breaks K92, takes and hardens Maris Airport, then hits the city factory belt; each step pays funds/score and lifts income or reserves. |
| Victory | BDF: `DestroyAllCriticalPALAFacilities` + `SinkPALACarrier`; PALA: `DestroyAllCriticalBDFFacilities` + `SinkBDFCarrier` | `EndGame` (Victory) for the side that finishes both. |

The escalation ladder uses `SuccessfulSortie` with `additive: true`: the objective tracks each
faction's own accumulated sortie bonuses, so the side that pushes hardest crosses the steps first.
(`additive: false` only keeps the best single sortie's score, which cannot reach 200+.)

## Rebuild and validate

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-boscali-summer-mission.ps1
powershell -ExecutionPolicy Bypass -File tools\validate-boscali-summer-mission.ps1
```

The builder reads the baseline mission (default `Escalation`) and rewrites only
`missionSettings`, `environment`, the faction economy, the reinforcement waves and the
`objectives`/`outcomes` script; the map, airbases and baseline unit placement are kept. All authoring
data (acts, beats, waves, messages) lives in the builder script. The validator is an independent
static gate over the produced JSON — run it after every rebuild.
