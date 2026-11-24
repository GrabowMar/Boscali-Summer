# AirSurvival — Mission AI air survival and stations (`air-survival`)

> Two host-side Harmony postfixes that make the game's own mission AI aircraft (not the player's wing, not ace hunts) pop last-second flares against IR missiles and spread idle sorties over TheaterOps air stations or nearby visible objectives.

## Player-facing behaviour

- Enemy and friendly mission AI jets drop flares just before an IR missile arrives (0.15–3.5 s to impact), so fewer AI die to the first heater.
- Idle AI jets in combat modes without enemy contact (helicopters only when they had no destination) are sent to a point near TheaterOps' air station for their faction instead of all stacking on the nearest objective (about half of them, see Gotchas).
- Otherwise fixed-wing AI spread across the nearest up to three visible objectives within 25 km of the nearest one.
- Hidden objectives and helicopters' authored destinations keep priority.
- Shown in the mission prompt as "AIR STATIONS" (BATTLESPACE group); no page, no settings.

## How it works

- Installed only with TheaterOps (`TheaterOps.Enabled`, [Core/BoscaliMod.cs](../../Core/BoscaliMod.cs) `Compose`), metadata depends on `theater-ops`; battlefield module in [MissionProfile](../../Core/Game/MissionProfile.cs).
- [AirSurvivalModule](AirSurvivalModule.cs) `Install` is empty; the module is its two patch types.
- [AirMissileFlarePatch](Patches/AirMissileFlarePatch.cs): postfix on `AIPilotCombatModes.FixedUpdateState`. Server, AI-flown, local-sim aircraft only. Per aircraft state (≤64 tracked; when full, one entry unseen for >10 s is evicted, else the new aircraft is not tracked) checked every 0.2 s; skips wingmen and `IAircraftTaskExclusion` aircraft and aircraft already firing countermeasures; looks at up to 8 `MissileWarning.knownMissiles` targeting it with seeker `"IR"`; decision in `AirFlarePolicy.ShouldPop` (closing ≥50 m/s, flares >5%, ≥3 s since last pop); each missile flared once (ring of 8 ids); calls `countermeasureManager.PopFlares()`.
- [AirMissionStationPatch](Patches/AirMissionStationPatch.cs): postfix on `MissionPosition.TryGetClosestPosition(Unit, out GlobalPosition)` (resolved in `TargetMethod`). Server, AI aircraft in `AIPilotCombatModes` or `AIHeloCombatState` without enemy contact. Order: keep rotary result if any; keep a fixed-wing result that is a hidden objective; else TheaterOps station via `AirStationPolicy.TryOperationFix`; else (fixed-wing with a result) re-rank among the 3 nearest visible objective positions (`ChooseObjectiveRank`).
- Pure rules: [Domain/AirStationPolicy.cs](Domain/AirStationPolicy.cs) (`AirStationPolicy`, `AirFlarePolicy`).

## Where to change things

| To change… | Look in |
|---|---|
| Flare timing / thresholds | `AirFlarePolicy` in [Domain/AirStationPolicy.cs](Domain/AirStationPolicy.cs) |
| Which aircraft get flare help | guard clauses in [Patches/AirMissileFlarePatch.cs](Patches/AirMissileFlarePatch.cs) |
| Station offset / which aircraft take the station | `AirStationPolicy.TryOperationFix` |
| Objective spreading (range, choices) | `AirStationPolicy.ChooseObjectiveRank`, `MaximumObjectives`, loop in [AirMissionStationPatch](Patches/AirMissionStationPatch.cs) |
| Hidden-objective priority | `IsHiddenOriginal` in AirMissionStationPatch |
| Where stations are | TheaterOps' `LivingFrontService` (implements `ITheaterAirStationView`), not here |

## Talks to other modules

- Consumes [ITheaterAirStationView](../../Core/Contracts/ITheaterAirStationView.cs) (TheaterOps): one station per faction name (x, z, radius).
- Consumes [IAircraftTaskExclusion](../../Core/Contracts/IAircraftTaskExclusion.cs) (Squad, optional): ace-hunt aircraft are skipped.
- Reads Wing's roster through [WingLink](../../Core/Game/WingLink.cs)`.TryIsWingMember` (PresenceBoard `NO.Wing.ids.v1`): wingmen are skipped.

## Settings, network, patches

- No config, no network; host/server only (`GameAccess.IsServer()` and `aircraft.IsServer`).
- `AirMissionStationPatch.Target()` throws `MissingMethodException` if the `TryGetClosestPosition(Unit, out GlobalPosition)` overload disappears. The target is shared with TheaterOps (see block).
- `AIPilotCombatModes.FixedUpdateState` runs per AI aircraft per physics tick: keep the postfix's early-outs cheap.

## Tests, sim hooks and pinned names

- `tests/BoscaliSummer.Tests` compiles [Domain/AirStationPolicy.cs](Domain/AirStationPolicy.cs) by path (`AirStationPolicyTests` exercise both policy classes): keep it pure, keep names/signatures.
- PatchProbe names `AirSurvivalModule`, `Patches.AirMissionStationPatch`, `Patches.AirMissileFlarePatch` by full name: don't rename or move.
- No sim hooks or scenarios of its own.

## Gotchas

- Fail-closed on Wing: `TryIsWingMember` returns false until Wing has published its roster on the PresenceBoard at least once, and both patches then do nothing. With Wing switched off before any mission, AirSurvival is inert.
- Only aircraft whose `persistentID` hash is even can take a theater station (`(identity & 1) != 0` → no fix), and only for stations of radius ≤ 6000 m; the others fall through to objective spreading.
- Neither postfix has a try/catch (unlike e.g. PlayerSpawnPriority's `PatchGuard`): an exception propagates into the game's AI/mission code.
- Flare state is a static dictionary keyed by `persistentID.GetHashCode()`; it is cleared only when `Time.timeSinceLevelLoad` goes backwards (scene restart), apart from the one-at-a-time stale eviction when it is full.
- `IsHiddenOriginal` answers true (keep the original) when objectives can't be read, there are more than 12, or a hidden objective has more than 4 positions, so busy missions keep vanilla targeting.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [AirSurvivalModule](AirSurvivalModule.cs), id `air-survival`, "Mission AI air survival and stations"; depends on `theater-ops`; roster: TheaterOps.Enabled; mission profile: battlefield.
- 4 .cs files, 292 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 16 |
| Domain/ | 1 | 53 |
| Patches/ | 2 | 223 |

### Largest files

| File | Lines |
|---|---|
| [Patches/AirMissionStationPatch.cs](Patches/AirMissionStationPatch.cs) | 125 |
| [Patches/AirMissileFlarePatch.cs](Patches/AirMissileFlarePatch.cs) | 98 |
| [Domain/AirStationPolicy.cs](Domain/AirStationPolicy.cs) | 53 |
| [AirSurvivalModule.cs](AirSurvivalModule.cs) | 16 |

### Harmony patches (2)

- `AIPilotCombatModes.FixedUpdateState` by `AirMissileFlarePatch` in [Patches/AirMissileFlarePatch.cs](Patches/AirMissileFlarePatch.cs)
- `MissionPosition.TryGetClosestPosition` by `AirMissionStationPatch` in [Patches/AirMissionStationPatch.cs](Patches/AirMissionStationPatch.cs) (TargetMethod) — SHARED with TheaterOps

### Contracts

- Registers as service: none
- Implements: none
- Resolves: `IAircraftTaskExclusion`, `ITheaterAirStationView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

None.

### Pinned by nomodkit

1 files compiled by path, 3 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
