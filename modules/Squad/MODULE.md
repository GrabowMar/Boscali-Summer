# Squad — Squad / ace hunts (`squad`)

> Host-side player pilot careers (generated pilot, deaths, One Life replacement, bonus perk points) and enemy "ace hunts": kill-driven enemy HEAT that sends named ace wings (spawned through Wing) after a player, synced to clients as a polled snapshot.

## Player-facing behaviour

- Each player gets a generated pilot (name, callsign, background from Wing's generator). `Squad/PilotLives`: Respawning keeps the pilot; OneLife retires a confirmed-dead pilot and the next aircraft gets a new generation (ejection alone is not death).
- Kills raise enemy HEAT (vanilla kill score: sqrt of unit value × damage share, max 40% of the bar per kill, none in the first 180 s mission time, cools after 60 s quiet). Shown as the HEAT row on Hud's SYSTEMS strip.
- At full heat an ace wing (tier 1–5; 2/3/4 aircraft) spawns at the enemy's territory edge (Command's `ITerritoryIngress`, rejected within 15 km of a friendly field) or else 22 km from the player toward the enemy's airbases, and hunts that player for up to 900 s; threshold grows 35% per defeated ace (capped ×5).
- Shooting down the ace after you damaged it yourself (and with the same pilot generation) gives +1 bonus perk point (max 20, used by Progression). Aces that eject may return later ("ACE MIA — MAY RETURN", up to 3 returns, 30% reuse chance).
- Progression's SQD page lists the pilot and up to 8 enemy wings; enemy radio intercepts are voiced through Wing (`WingLink.EnemyChatter`); the TAC BOARD shows an ACE HUNT needle (host/solo only).
- SET SERVER table "SQUAD AND ACES": PILOT CAREER and ACE HUNTS. F1 config `Debug` has "Spawn tier N hunting wing" / "Clear my debug wings" buttons (host only).

## How it works

- Installed only when `Progression.Enabled` (with Progression, which `GetRequired<ISquadView>`), after Wing ([Core/BoscaliMod.cs](../../Core/BoscaliMod.cs) `Compose`).
- [SquadModule](SquadModule.cs) `Install`: adds [SquadManager](Runtime/SquadManager.cs) as scene service (order 44) and [SquadNet](Networking/SquadNet.cs) as component, wires them, registers `ISquadView`, `IAceWingRoster`, `IAircraftTaskExclusion`, registers [AceScopeGauge](Runtime/AceScopeGauge.cs) (`ScopeGauges` id `ace`), adds the host settings table, logs whether `WingLink.SquadAvailable`.
- `SquadManager` (MonoBehaviour) `Update` at 1 Hz: on mission change or a clock rewind → `ResetForScene`; on the host: `GatherPlayers` (≤8 HQs, ≤64 players), `TickHunt` for each hunt, `TickCareer` for each career (seat change, death, cooling, spawn when heat ≥ threshold, 15 s retry spacing, ≤4 live wings). Then `SquadNet.Request(fast)` on every peer.
- Rules are in pure [AceCareer](Domain/AceCareer.cs) (heat, cooling, tiers, wing size, replacement); `Career`/`Hunt`/`RivalRecord` are private classes in the manager.
- Harmony patches ([Patches/SquadPatches.cs](Patches/SquadPatches.cs)) forward `Unit.RecordDamage` (postfix: marks that the player hit the ace), `Unit.ReportKilled` (postfix: kill heat, ace defeated) and `Pilot.ApplyDamage` (prefix captures the seated `Player`, postfix reports the death) to the static `SquadRuntime.Active`, host only.
- Everything that touches aircraft goes through [WingLink](../../Core/Game/WingLink.cs) → Wing's `IWingSquad`: `TryCreatePilot`, `SpawnAceWing`, `SetAceWingTarget`, `ReleaseAceWing`, `SurvivorStatus`/`RecoverSurvivor`, `EnemyChatter`, `AceAbilityMask`.
- Sync: client sends `SquadQuery` (scene, token, last applied revision) every 1 s while a hunt is on or the board was read in the last 2 s, else every 5 s; host answers (≤ once per 0.75 s per player) with a `SquadSnapshot`, or a header-only `Unchanged` reply when the [SnapshotRevision](Domain/SnapshotRevision.cs) hash matches. The host applies its own snapshot in-process.

## Where to change things

| To change… | Look in |
|---|---|
| Heat amounts, grace, cooling, tier/wing size, threshold growth | [Domain/AceCareer.cs](Domain/AceCareer.cs) |
| What counts as a kill for heat | `AddKillHeat` / `RecordKill` in [Runtime/SquadManager.cs](Runtime/SquadManager.cs) |
| When/where an ace wing spawns, rival reuse | `TickCareer`, `Spawn`, `TryCalculateEnemyIngress`, `IsTooCloseToFriendlies` in [SquadManager](Runtime/SquadManager.cs) |
| Which aircraft other modules must leave alone | `IsExcluded` / `CopyAceAircraft` in [SquadManager](Runtime/SquadManager.cs) |
| Hunt end conditions, credit, notices and enemy chatter text | `TickHunt`, `End`, `Notice` in [SquadManager](Runtime/SquadManager.cs) |
| SQD board content (pilot status words, wing rows) | `Snapshot` in [SquadManager](Runtime/SquadManager.cs) (rendering is Progression's) |
| Wire format | [Networking/SquadNet.cs](Networking/SquadNet.cs) + `Revision` in SquadManager |
| ACE HUNT needle | [Runtime/AceScopeGauge.cs](Runtime/AceScopeGauge.cs) |
| Config keys, debug buttons | [Configuration/SquadSettings.cs](Configuration/SquadSettings.cs) |
| SET SERVER rows | `AddHostSettings` in [SquadModule.cs](SquadModule.cs) |
| How aces are spawned/flown | Wing: [WingSquad.cs](../Wing/Runtime/WingSquad.cs) (not this module) |

## Talks to other modules

- Provides [ISquadView](../../Core/Contracts/ISquadView.cs): pilot, heat, status, enemy wings, bonus points/generation/score origin — Progression (SQD page, perks), Hud (SYSTEMS HEAT, tape, visor callsign).
- Provides [IAceWingRoster](../../Core/Contracts/IAceWingRoster.cs) (host-only ace aircraft, for Progression's tail paint) and [IAircraftTaskExclusion](../../Core/Contracts/IAircraftTaskExclusion.cs) (ace aircraft are off-limits to AirSurvival and TheaterOps).
- Consumes [ITerritoryIngress](../../Core/Contracts/ITerritoryIngress.cs) (Command) for the spawn edge, with its own fallback (resolved through the `ServiceRegistry` handed to `SquadManager.Configure`, not `ModuleServices`); consumes Wing only through `WingLink`/`IWingSquad`.

## Settings, network, patches

- `PilotLives`, `EnemyAceHunts`, `HeatThreshold`, `HuntCooldown` are host-authoritative (listed in [HostAuthority](../../Core/Config/HostAuthority.cs), pushed by Session); only the first two are on SET SERVER. `Debug/HuntingWingTier` and the spawn buttons are host-only.
- `SquadNet.ProtocolVersion` = 4. Any change to `SquadQuery`/`SquadSnapshot` fields or their serializers needs a bump, an update of `Revision()` (every wire field must be hashed or "Unchanged" can hide an update), and nomodkit PatchProbe's field list for both messages (user's job; nomodkit is not edited here).
- Readers never throw (a throw in a Mirage handler can drop the connection); out-of-range data returns `default` (protocol 0, ignored).
- `AddKillHeat` (from `RecordKill`) reads the private `Unit.damageCredit` through an `AccessTools.FieldRefAccess` held in a static field of `SquadManager`; it is bound when that type initializes, so a game rename of the field breaks the whole manager, not just heat.

## Tests, sim hooks and pinned names

- [SquadAutomation](Runtime/SquadAutomation.cs) (`Spawn`, `Heat`; dev builds only, guarded by `NOMODKIT_SIM_SCENARIO`) is called by scenarios `ace-alerts`, `ace-heat`, `hud-brow`, `hud-brow-ace`. Do not rename or move it, its methods or its namespace `BoscaliSummer.Modules.Squad.Runtime`.
- `tests/BoscaliSummer.Tests` compiles [AceCareer.cs](Domain/AceCareer.cs) and [SnapshotRevision.cs](Domain/SnapshotRevision.cs) by path (keep them pure and in place). PatchProbe names `SquadModule`, the three patch classes and the two message types with their exact field names/types.
- `nomod sim ls ace --mod boscalisummer`.

## Gotchas

- `SquadManager` is a MonoBehaviour registered as a service: consumers test `squad is Object gone && gone == null` (Unity null) before use. The patches go through the static `SquadRuntime.Active`.
- `survivingRivals` is `static` and is not cleared by `ResetForScene`; it holds `FactionHQ` references from earlier scenes (they simply stop matching `ProvokedHq`).
- Heat byte `255` (`SquadSnapshot.NoHeat`) means hunts off/unavailable; `ISquadView.Heat` is then `NaN`, not 0.
- `TryAce`/`IAceWingRoster` only work on the host: a remote client knows about the hunt (snapshot) but has no aircraft handle.
- Non-Steam players (`SteamID == 0`) whose index is reused get a new career generation and their running hunt ends as TargetLost.
- On every peer the first chatter of a newly active hunt is held 10.4 s (`pendingChatterRelease` in `Apply`); later ones play immediately. Nothing is voiced in batch mode.
- With Wing missing, `GeneratePilot` writes "Pilot generator unavailable" and hunts never start; `WingLink` caches `IWingSquad` once resolved.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [SquadModule](SquadModule.cs), id `squad`, "Squad / ace hunts"; depends on nothing; roster: Progression.Enabled; mission profile: battlefield.
- 9 .cs files, 1394 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 44 |
| Configuration/ | 1 | 66 |
| Domain/ | 2 | 140 |
| Networking/ | 1 | 203 |
| Patches/ | 1 | 37 |
| Runtime/ | 3 | 904 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/SquadManager.cs](Runtime/SquadManager.cs) | 784 |
| [Networking/SquadNet.cs](Networking/SquadNet.cs) | 203 |
| [Domain/AceCareer.cs](Domain/AceCareer.cs) | 89 |
| [Runtime/SquadAutomation.cs](Runtime/SquadAutomation.cs) | 71 |
| [Configuration/SquadSettings.cs](Configuration/SquadSettings.cs) | 66 |

### Harmony patches (2)

- `Pilot.ApplyDamage` by `SquadPilotDeathPatch` in [Patches/SquadPatches.cs](Patches/SquadPatches.cs) — SHARED with Spooktober, Wing (ORDER-SENSITIVE: wing → spooktober)
- `Unit.RecordDamage` by `SquadDamagePatch` in [Patches/SquadPatches.cs](Patches/SquadPatches.cs) — SHARED with HighCommand (INDEPENDENT)

### Contracts

- Registers as service: `IAceWingRoster`, `IAircraftTaskExclusion`, `ISquadView`
- Implements: `IAceWingRoster`, `IAircraftTaskExclusion`, `ISquadView`
- Resolves: `ITerritoryIngress`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Squad`, `Debug` (6 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `SquadNet.ProtocolVersion` = 4
- Messages (2): `SquadQuery`, `SquadSnapshot`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Squad.Runtime.SquadAutomation` (dev)

### Pinned by nomodkit

2 files compiled by path, 7 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
