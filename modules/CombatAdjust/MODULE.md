# CombatAdjust — Combat adjustments (`combat`)

> Host-authoritative combat realism rules over vanilla sensors and missiles: link discipline on the shared picture, drifting/expiring stale tracks, missile faults and stronger countermeasures, armour smoke, ship decoys and SAM EMCON, plus a water-impact fix for missiles.

## Player-facing behaviour

- **SET SERVER → COMBAT** table (host): LINK DISCIPLINE, TRACK FOG, ARMOUR SMOKE, SHIP DECOYS, SAM EMCON toggles and LINK CHANCE, CLOSE RANGE, FOG SCALE, TRACK MEMORY, MISSILE FAILURES, COUNTERMEASURES numbers; changes apply live.
- Ground/ship lookouts only sometimes put distant contacts on the faction picture; radars and aircraft share at once; close contacts always.
- Unreported contacts drift on the map and in the AI picture (further the longer unseen); unseen moving ground/sea contacts leave the picture after `TrackExpirySeconds` (aircraft after a third).
- Some guided missiles fail (dud proximity fuse or wandering seeker); flares/chaff/jamming are stronger.
- Armour pops smoke at laser/TV/IR-guided weapons (blocks those locks and eye line-of-sight); ships fire chaff/decoy salvos; ground radars go dark while an anti-radiation missile is inbound.
- Missiles hitting water detonate without the native velocity reset on a kinematic body.
- With the external overhaul `com.cnoevil.nuclearoption.overhaul` loaded, link, fog, expiry, faults and the countermeasure boost stand down (those rows read "ANOTHER MOD OWNS THIS"); smoke, decoys and EMCON stay.

## How it works

- [CombatAdjustModule](CombatAdjustModule.cs) `Install`: detects the external overhaul via `Chainloader.PluginInfos`, adds [CombatAdjustService](Runtime/CombatAdjustService.cs) as scene service (order 60), registers [CombatAdjustHostSettings](Configuration/CombatAdjustHostSettings.cs)`.Build` (the SET SERVER COMBAT table) via `AddHostSettings`.
- `CombatAdjustService` (MonoBehaviour, static `Active`) owns [MissileBook](Runtime/MissileBook.cs), [TrackExpiry](Runtime/TrackExpiry.cs), [SmokeField](Runtime/SmokeField.cs), [ShipDecoys](Runtime/ShipDecoys.cs), [Emcon](Runtime/Emcon.cs). `Update` ticks on `Time.timeSinceLevelLoad`: 0.1 s (prune book; host: wander failed seekers, smoke blinding), 0.2 s (smoke trigger, decoys with a host flag, host EMCON), 5 s (expiry sweep, only while `ExpiryOn`). Each step is individually guarded by `PatchGuard`.
- Rule switches (`LinkOn`, `FogOn`, …) re-read the `ConfigEntry` every call, so host settings changes are live.
- Patches call into `Active`: `CombatDetectScopePatch` opens a `[ThreadStatic]` "who is detecting" scope; `CombatShareGatePatch` gates `FactionHQ.RpcUpdateTrackingInfo` by [LinkRules](Domain/LinkRules.cs); `CombatFogPatch` adds [TrackFog](Domain/TrackFog.cs) drift to `TrackingInfo.GetPosition` except inside `IsTargetPositionAccurate` (`CombatAccuracyScopePatch`); missile start/local-start/proxy-fuse/aim patches implement faults, smoke and decoy offsets; `CombatSmokeLosPatch` blocks eye LoS checks through smoke.
- Determinism: rolls use [Seeded](Domain/Seeded.cs) hashes of ids and time windows, not `Random`.
- Authority: link, faults, smoke blinding, decoy seduction, EMCON run on the host; fog, expiry, and smoke/decoy visuals ([SmokeCloudVisual](Visuals/SmokeCloudVisual.cs)) run on every peer.
- `ResetForScene` releases EMCON, clears book/smoke/decoys, resets [CombatStats](Runtime/CombatStats.cs); `Update` also resets if `timeSinceLevelLoad` goes backwards.

## Where to change things

| To change… | Look in |
|---|---|
| Config keys, defaults, ranges | [Configuration/CombatAdjustSettings.cs](Configuration/CombatAdjustSettings.cs) |
| SET SERVER COMBAT rows | [Configuration/CombatAdjustHostSettings.cs](Configuration/CombatAdjustHostSettings.cs) |
| Who shares on the link | [Domain/LinkRules.cs](Domain/LinkRules.cs), `Shares` in [CombatAdjustService](Runtime/CombatAdjustService.cs) |
| Drift radius/speed of stale tracks | [Domain/TrackFog.cs](Domain/TrackFog.cs) |
| Track expiry | [Runtime/TrackExpiry.cs](Runtime/TrackExpiry.cs) |
| Missile failure odds/kinds/timing | [Domain/MissileFaults.cs](Domain/MissileFaults.cs), `OnLocalStart` in [CombatAdjustService](Runtime/CombatAdjustService.cs) |
| Countermeasure strength | `OnLocalStart` in [CombatAdjustService](Runtime/CombatAdjustService.cs) (seeker private fields `IRSeeker.flareRejection`, `ARHSeeker`/`SARHSeeker.jamTolerance`; skipped when `FaultsOn` is false) |
| External-overhaul detection | `ExternalCombatGuid` in [CombatAdjustModule.cs](CombatAdjustModule.cs), the `*On` switches in [CombatAdjustService](Runtime/CombatAdjustService.cs) |
| Smoke triggers/blinding | [Runtime/SmokeField.cs](Runtime/SmokeField.cs), [Domain/Triggers.cs](Domain/Triggers.cs) |
| Ship decoys | [Runtime/ShipDecoys.cs](Runtime/ShipDecoys.cs) |
| SAM EMCON | [Runtime/Emcon.cs](Runtime/Emcon.cs), [Domain/EmconBook.cs](Domain/EmconBook.cs) |
| Smoke/decoy visuals | [Visuals/SmokeCloudVisual.cs](Visuals/SmokeCloudVisual.cs) |
| Missile water impact | [Patches/MissileWaterImpactPatch.cs](Patches/MissileWaterImpactPatch.cs) |
| Patch seams | [Patches/CombatPatches.cs](Patches/CombatPatches.cs) |

## Talks to other modules

- No contracts provided or resolved. Uses Core `HostSettingsTable` (SET SERVER), `PatchGuard`, `GameAccess`.
- Gamemodes' WarBox preset switches this module off (`"combat"`) through `BoscaliMission.PresetModulesOff`.
- Shares Harmony targets with Support/Pmc (`FactionHQ.RpcUpdateTrackingInfo`, `TargetDetector.DetectTarget`), Vanguard (`TargetDetector.DetectTarget`, `Missile.StartMissile`, `Missile.DetectCollisions`), Spooktober/Wing (`Missile.SetAimpoint`), Trenches (`DetectorManager.RequestLoSCheck`). Support has its own `[ThreadStatic] Detecting` scope ([CyberPatches](../Support/Patches/CyberPatches.cs)); they don't import each other.

## Settings, network, patches

- All `[CombatAdjust]` keys except `Enabled` are host-authoritative (HostAuthority, synced via the SET SERVER table); `Enabled` is a roster switch.
- No mod messages: outcomes reach clients through vanilla replication (tracking RPCs, missile state, radar state).
- `MissileWaterImpactPatch` is a transpiler that expects exactly one native `Detonate(Vector3.up,false,false); rb.velocity = Vector3.zero` pattern in `Missile.DetectCollisions`; on mismatch it logs a warning and leaves the method untouched — check the log after game updates.
- Countermeasure scaling uses `FieldRefAccess` on private seeker fields; a missing field yields null and silently skips scaling.

## Tests, sim hooks and pinned names

- Sim hooks (dev only): **`BoscaliSummer.CombatAdjust.CombatAdjustAutomation`** (legacy namespace, in [Runtime/CombatAdjustAutomation.cs](Runtime/CombatAdjustAutomation.cs)) `.Step` — scenarios `combat-adjust`, `combat-decoys`, `combat-emcon`, `combat-smoke`; [MissileWaterProof](Runtime/MissileWaterProof.cs)`.Step` — `missile-water-impact`.
- PatchProbe names `BoscaliSummer.Modules.CombatAdjust.Patches.MissileWaterImpactPatch`.
- nomodkit `tests/BoscaliSummer.Tests` compiles `Domain/*.cs` by glob (today six files; a new Domain file joins it) for CombatAdjustTests; keep them Unity-light and in place.
- `nomod sim ls combat --mod boscalisummer`.

## Gotchas

- `CombatAdjustAutomation` lives in namespace `BoscaliSummer.CombatAdjust`, not `BoscaliSummer.Modules.CombatAdjust.*`; scenarios call it by that name — do not "fix".
- `CombatScope` fields are `[ThreadStatic]` and saved/restored around the native call (prefix + `Finalizer`, so an exception cannot leave a scope open); new scopes should follow the same pattern.
- The fog postfix changes `TrackingInfo.GetPosition` for everyone (map, AI, other modules) — code that needs the true position must run inside the accuracy scope or read the unit.
- `FaultsOn` has no own switch: faults are controlled by `MissileFailChance` (0 = off) and the external-overhaul check. `OnLocalStart` returns early when `FaultsOn` is false, so the countermeasure scaling goes with it.
- Settings comment points at `docs/superpowers/specs/2026-10-27-combat-adjustments-design.md`, which is not in this repo.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [CombatAdjustModule](CombatAdjustModule.cs), id `combat`, "Combat adjustments"; depends on nothing; roster: CombatAdjust.Enabled; mission profile: battlefield.
- 21 .cs files, 1861 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 39 |
| Configuration/ | 2 | 92 |
| Domain/ | 6 | 256 |
| Patches/ | 2 | 192 |
| Runtime/ | 9 | 1189 |
| Visuals/ | 1 | 93 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/CombatAdjustAutomation.cs](Runtime/CombatAdjustAutomation.cs) | 257 |
| [Runtime/CombatAdjustService.cs](Runtime/CombatAdjustService.cs) | 196 |
| [Runtime/MissileWaterProof.cs](Runtime/MissileWaterProof.cs) | 171 |
| [Runtime/SmokeField.cs](Runtime/SmokeField.cs) | 159 |
| [Patches/CombatPatches.cs](Patches/CombatPatches.cs) | 146 |

### Harmony patches (10)

- `DetectorManager.RequestLoSCheck` by `CombatSmokeLosPatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs) — SHARED with Trenches
- `FactionHQ.IsTargetPositionAccurate` by `CombatAccuracyScopePatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs)
- `FactionHQ.RpcUpdateTrackingInfo` by `CombatShareGatePatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs) — SHARED with Pmc, Support
- `Missile.DetectCollisions` by `MissileWaterImpactPatch` in [Patches/MissileWaterImpactPatch.cs](Patches/MissileWaterImpactPatch.cs) — SHARED with Vanguard
- `Missile.LocalStart` by `CombatMissileLocalStartPatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs)
- `Missile.SetAimpoint` by `CombatAimPatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs) — SHARED with Spooktober, Wing
- `Missile.SetProxyFuse` by `CombatProxyFusePatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs)
- `Missile.StartMissile` by `CombatMissileStartPatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs) — SHARED with Vanguard
- `TargetDetector.DetectTarget` by `CombatDetectScopePatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs) — SHARED with Pmc, Support, Vanguard
- `TrackingInfo.GetPosition` by `CombatFogPatch` in [Patches/CombatPatches.cs](Patches/CombatPatches.cs)

### Contracts

- Registers as service: none
- Implements: none
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `CombatAdjust` (12 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.CombatAdjust.CombatAdjustAutomation` (dev), `BoscaliSummer.Modules.CombatAdjust.Runtime.MissileWaterProof` (dev)

### Pinned by nomodkit

6 files compiled by path, 3 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
