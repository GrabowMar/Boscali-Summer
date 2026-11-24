# Ares — ARES game master (`ares`)

> Host-side game-master console on the MISC screen: spawn units/kits on map clicks, god powers (strikes, clock, OPS casts, fire, levelling) and session-only cheats/dev switches; `AresHost` also carries unit-management, weather and boss actions used by the sim hook.

## Player-facing behaviour

- Three tabs on the **MISC** MFD screen: **SPAWN** ("ARES · SPAWN"), **POWERS**, **CHEATS** (CHEATS also hosts the developer switches). Each page ends with the ARES result line.
- SPAWN: pick side, kind (AIR, GROUND, NAVAL, STRUCT, KITS), item, COUNT, ALT (AIR); PLACE arms a map pick: right-click places, right-drag sets heading, Esc or PLACE/STOP again stops; UNDO / CLEAR ALL remove ARES spawns.
- POWERS: STRIKE at a map click with a chosen munition and pattern, HOUR and SPEED steppers, OPS free CAST at a click (switches BYPASS REQS and NO OPS COOLDOWN on), FIRE and LEVEL 200 M at a click.
- CHEATS: BYPASS REQS, NO OPS COOLDOWN, then PILOT toggles (GOD MODE, INFINITE FUEL/AMMO/FLARES, NO BLACKOUT, ONE-HIT KILLS, BOOST x2, LOW GRAVITY), WORLD toggles (REVEAL ALL, TIME STOP, BULLET TIME — single player), FIX ME, TELEPORT ME, +1000 CREDIT, FLY BREAKWATER (dev builds only), a HUNTING WING section and a DEV block (verbose/wing log, footprint, dev tools, wing overlay).
- `AresHost` also has selection verbs (repair, rearm, kill, delete, side, invuln, freeze, follow, move, teleport, take control), weather and Kaiju/Fallen-Ace summon/dismiss, but **no tab button calls them** — only `AresAutomation` does.
- Mutating actions answer "HOST ONLY: ARES runs on the host." on a client. The `AresCheats` switches are static and never saved; BYPASS REQS, NO OPS COOLDOWN and the DEV toggles are ordinary config entries (`Diagnostics`/`Wing` settings) and do persist.

## How it works

- [AresModule](AresModule.cs) `Install`: adds [AresHost](Runtime/AresHost.cs) as scene service (order 76), `Configure(context.Settings, context.Logger)` (whole `ModConfiguration`; the OPS cast flips the `Diagnostics` switches), registers [AresTabs](Presentation/AresTabs.cs) (also given `ModConfiguration`, for the CHEATS/DEV config toggles) as `IMiscTabs`; patch types are the eight damage/fuel/G-LOC prefixes in [AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs).
- `AresHost` is one `partial` MonoBehaviour split by concern: [AresHost.cs](Runtime/AresHost.cs) (lifecycle, `Host()` guard, map pick via Core `MapPicker` owner `boscali.ares`), [AresHost.Spawn.cs](Runtime/AresHost.Spawn.cs) (catalogs, kits, placing, undo), [AresHost.Units.cs](Runtime/AresHost.Units.cs) (selection verbs, protect/freeze/follow sets), [AresHost.Powers.cs](Runtime/AresHost.Powers.cs) (strikes, weather, clock, bosses, casts, fire), [AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs) (`AresCheats` statics + Harmony patches + gravity/time).
- Every public action returns a string and sets `Result`; `AresTabs` buttons and `AresAutomation` call the same methods.
- `Update` → `TickPick` + `TickCheats`; `FixedUpdate` → `FixedCheats`. `ResetForScene` stops coroutines, disarms the pick, clears spawn batches, unit sets and catalogs, and `ResetCheats` (only LOW GRAVITY, BULLET TIME, TIME STOP and the speed step).
- Pure helpers in [Domain/AresMath.cs](Domain/AresMath.cs) (`AresKind`, kit members, drag-heading math).
- Authority: mutations check `GameAccess.IsServer()` and a live `Spawner`; spawned units are normal networked units, so clients see results through native replication.

## Where to change things

| To change… | Look in |
|---|---|
| Tab layout, buttons, help text | [Presentation/AresTabs.cs](Presentation/AresTabs.cs) |
| Spawn catalogs, kits, placement, undo | [Runtime/AresHost.Spawn.cs](Runtime/AresHost.Spawn.cs), kit data/math in [Domain/AresMath.cs](Domain/AresMath.cs) |
| Selection verbs (repair, freeze, follow, take control…) | [Runtime/AresHost.Units.cs](Runtime/AresHost.Units.cs) |
| Strikes, weather, clock, bosses, OPS cast, fire, level | [Runtime/AresHost.Powers.cs](Runtime/AresHost.Powers.cs) |
| A cheat's effect or its Harmony hook | [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs) |
| Map pick gesture (right-click/drag, Esc) | `Arm`/`TickPick` in [Runtime/AresHost.cs](Runtime/AresHost.cs) |
| Host-only rule | `Host(out string)` in [Runtime/AresHost.cs](Runtime/AresHost.cs) |
| Enable switch | [Configuration/AresSettings.cs](Configuration/AresSettings.cs) |

## Talks to other modules

- Provides [IMiscTabs](../../Core/Contracts/IMiscTabs.cs), shown by Command's MISC MFD panel (`MiscMfdPanel`).
- Consumes (all optional; each power answers "The X module is off." when missing): `IWeatherDirector` (Weather), `IKaijuBoss` (Kaiju), `IFallenAce` (Spooktober), `ISupportCast` (Support OPS casts), `IFireStarter` (Fire and Destruction).
- Uses Core `MapPicker`/`MapTool` for the shared map gesture rules.

## Settings, network, patches

- `[Ares] Enabled` is a roster switch, "applies at the next launch". No network messages.
- All patches are prefixes that read static `AresCheats` flags first (one branch when off). `AresPartDamagePatch` targets `UnitPart.TakeDamage` (shared with Gamemodes, Spooktober); `AresFuelPatch` targets `Aircraft.UseFuel` (shared with Skylark); `AresShockwavePatch` uses `TargetMethods` over `AeroPart`/`UnitPart.TakeShockwave`.
- God mode / INVULN suppress damage RPCs on the peer that runs them (`Unit.RpcDamage`, `Aircraft.RpcDamage`); G-force and water prefixes only spare the local aircraft's pilot; the G-LOC prefix forces `SimulateGLOC` to return 1 whenever NO BLACKOUT is on; ONE-HIT multiplies damage in `AresPartDamagePatch` only when the dealer is the local aircraft.

## Tests, sim hooks and pinned names

- Sim hook (dev only): [AresAutomation](Runtime/AresAutomation.cs)`.Step` with `action` = status/spawn/select/verb/point/weather/hour/summon/cheat/undo/clear; refuses anything but `status` unless env `NOMODKIT_SIM_SCENARIO` is set. Used by scenarios `ares`, `avionics-full-tour`, `avionics-auxiliary-tour`, `avionics-archive-preview`, `avionics-map-controls`.
- nomodkit `tests/BoscaliSummer.Tests` compiles [Domain/AresMath.cs](Domain/AresMath.cs) by path (AresMathTests); keep it Unity-free.
- `nomod sim ls ares --mod boscalisummer`.

## Gotchas

- Low gravity sets global `Physics.gravity` on this machine only and does not check host; bullet time changes `TimeScaleManager.Scale` and is single-player only.
- `AresCheats` are static and process-wide. `ResetCheats` (from `ResetForScene`) only undoes the world-changing ones (gravity, time scale, TIME STOP); GOD, FUEL, AMMO, FLARES, NO BLACKOUT, ONE-HIT, BOOST and REVEAL deliberately carry into the next mission ("Cheats last until the game closes" in the CHEATS page). A new cheat that alters global state must be added to `ResetCheats`.
- `Host()` deliberately does not use `MissionManager.IsRunning` (it flips false mid-mission in live runs); it checks the `Spawner` singleton instead.
- `StopAllCoroutines` in `ResetForScene` cancels barrages and delayed seats; new long-running actions must be coroutines on `AresHost` to be cleaned up.
- Selection verbs, `Weather`, `SummonKaiju`/`SummonAce`/`DismissBosses` have no UI caller (only `AresAutomation`); don't assume a player can reach them, and don't delete them as dead code — the `ares` scenario uses them.
- The map pick ignores the click on the frame it was armed (`armedFrame + 1`) and disarms if another tool takes `MapPicker` or the map is un-maximised.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [AresModule](AresModule.cs), id `ares`, "ARES game master"; depends on nothing; roster: Ares.Enabled; mission profile: optional.
- 10 .cs files, 2079 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 35 |
| Configuration/ | 1 | 16 |
| Domain/ | 1 | 130 |
| Presentation/ | 1 | 318 |
| Runtime/ | 6 | 1580 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/AresHost.Units.cs](Runtime/AresHost.Units.cs) | 357 |
| [Presentation/AresTabs.cs](Presentation/AresTabs.cs) | 318 |
| [Runtime/AresHost.Spawn.cs](Runtime/AresHost.Spawn.cs) | 285 |
| [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs) | 252 |
| [Runtime/AresHost.Powers.cs](Runtime/AresHost.Powers.cs) | 241 |

### Harmony patches (9)

- `AeroPart.TakeShockwave` by `AresShockwavePatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs) (TargetMethod)
- `Aircraft.RpcDamage` by `AresAircraftDamagePatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs)
- `Aircraft.UseFuel` by `AresFuelPatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs) — SHARED with Skylark (ORDER-SENSITIVE: ares → skylark)
- `GLOC.SimulateGLOC` by `AresBlackoutPatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs)
- `Pilot.TakeGForceDamage` by `AresGForceDamagePatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs)
- `Pilot.TakeWaterDamage` by `AresWaterDamagePatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs)
- `Unit.RpcDamage` by `AresUnitDamagePatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs)
- `UnitPart.TakeDamage` by `AresPartDamagePatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs) — SHARED with Gamemodes, Spooktober (ORDER-SENSITIVE: spooktober → ares)
- `UnitPart.TakeShockwave` by `AresShockwavePatch` in [Runtime/AresHost.Cheats.cs](Runtime/AresHost.Cheats.cs) (TargetMethod)

### Contracts

- Registers as service: `IMiscTabs`
- Implements: `IMiscTabs`
- Resolves: `IFallenAce`, `IFireStarter`, `IKaijuBoss`, `ISupportCast`, `IWeatherDirector`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Ares` (1 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Ares.Runtime.AresAutomation` (dev)

### Pinned by nomodkit

1 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
