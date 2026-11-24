# Spooktober — Spooktober (`spooktober`)

> Seasonal easter egg: a boss menu with three host-spawned bosses (KAIJU, FALLEN ACE, HELL'S ARMY), their AI, looks and the player's saved defeat progress.

## Player-facing behaviour

- **MISC › SPOOKTOBER** tab (Command's MISC panel, via `ISetExtras`): three case-file dossier cards with spawn/dismiss controls ([SpookMenuPage](Presentation/SpookMenuPage.cs), [SpookConsole](Presentation/SpookConsole.cs)). Only the host can spawn; one boss at a time.
- **KAIJU**: mountain-sized creature that rises ahead of the host's aircraft, marches on the nearest airbase, fires IR "spike" missiles, sweeps a lagging beam, claws close aircraft; 21 lockable glowing weak spots, then the heart. Also summonable from the F1 config window (Kaiju › Summon button). Works in multiplayer.
- **FALLEN ACE**: gun-only FS-20 on the GHOST side with three lives, damage resistance, blinks, phantom-veering long-range missiles, rad-ghost shader look. Single player / host without clients only.
- **HELL'S ARMY**: a ground rift ("HELLGATE") spawns themed waves (SCOUTS → IRON → SKYFIRE → HORDE, bigger each lap) under a violet HELL side; corruption spreads over terrain, takes bases, has a temporary "hell dimension" arena. Single player / host without clients only (the gate closes when a remote player joins).
- HELL units and captured airfields are drawn in violet on the map; territory and breach sigils on flat/relief maps ([HellPortalMapLayer](Presentation/HellPortalMapLayer.cs)).
- Defeats are remembered per player in a hidden config entry (`Spooktober.Progress`); voice stings play on spawn ([SpookAudio](Runtime/SpookAudio.cs)).

## How it works

- Entry: [SpooktoberModule](SpooktoberModule.cs) `Install` (only when `Spooktober.Enabled`, see roster):
  - if `Kaiju.Enabled`: [KaijuNet](Kaiju/Networking/KaijuNet.cs) (70), [KaijuDirector](Kaiju/Runtime/KaijuDirector.cs) (71) registered as `IKaijuBoss`;
  - `SpookStore.Init`, then scene services [SpookBosses](Runtime/SpookBosses.cs) (73), [RadGhostWatcher](Runtime/RadGhostWatcher.cs) (74), [FallenAceDirector](Runtime/FallenAceDirector.cs) (75), [HellPortalMapLayer](Presentation/HellPortalMapLayer.cs) (77), `HellGateDirector` in [HellGate.cs](Runtime/HellGate.cs) (78);
  - services `IFallenAce` ([SpookAce](Runtime/SpookAce.cs)), `IWeatherCameraExclusion` (`HellWeatherExclusion` in [HellDimension.cs](Runtime/HellDimension.cs)), `ISetExtras` ([SpookSetExtras](Presentation/SpookSetExtras.cs)) — registered last so MISC sees `IKaijuBoss`.
- [SpookBosses](Runtime/SpookBosses.cs) is the menu's backend: `Spawn(boss)` (host only, dismisses whatever is out first), `Dismiss`, `Active`, and credits defeats into [SpookStore](Runtime/SpookStore.cs)/[SpookProgress](Domain/SpookProgress.cs). It follows `IKaijuBoss` by lookup each frame because the kaiju may be off.
- Kaiju (`Kaiju/`): host-only [KaijuBrain](Kaiju/Runtime/KaijuBrain.cs) runs the fight using pure [KaijuRules](Kaiju/Domain/KaijuRules.cs); [KaijuBody](Kaiju/Runtime/KaijuBody.cs) is the animated 15× body every peer builds; [KaijuRig](Kaiju/Runtime/KaijuRig.cs) holds model facts; [KaijuAssets](Kaiju/Runtime/KaijuAssets.cs) loads `kaiju.bundle` and clones the game's smallest building as the hardpoint unit. Weak spots are real game `Unit`s, so damage/locks replicate natively; `KaijuNet` only streams pose/action (`KaijuState`) and which missiles are spikes (`KaijuSpikes`).
- Fallen ace: [FallenAce](Runtime/FallenAce.cs) (state, spawn, lives, hooks for [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs)); [FallenAceDirector](Runtime/FallenAceDirector.cs) flies its moods at 4 Hz on top of the game's combat AI using pure [AceRules](Domain/AceRules.cs). Side: `GhostSide` in [GhostSide.cs](Runtime/GhostSide.cs) (a runtime-created `FactionHQ` via `SpookSide`).
- Hell's Army: [HellGate](Runtime/HellGate.cs) (rift + waves, rules in [HellRules](Domain/HellRules.cs)), [HellTerritory](Runtime/HellTerritory.cs) (base capture, [HellControl](Domain/HellControl.cs)), [HellInfectionDamage](Runtime/HellInfectionDamage.cs) (host demolition at the front, plan in [HellInfection](Domain/HellInfection.cs)), [HellGrowth](Runtime/HellGrowth.cs), [HellPortal](Runtime/HellPortal.cs)/[HellPortalGround](Runtime/HellPortalGround.cs) (temporary terrain holes, restored on close), [HellDimension](Runtime/HellDimension.cs) (arena), [HellVehicleLook](Runtime/HellVehicleLook.cs), [HellAtmosphere](Runtime/HellAtmosphere.cs), [HellArmy](Runtime/HellArmy.cs) (membership + map ink), `HellSide` in [GhostSide.cs](Runtime/GhostSide.cs).
- Looks: [RadGhostLook](Runtime/RadGhostLook.cs) (Boscali/RadGhost material + embers), applied by `RadGhostWatcher` locally on every peer; [SpookAssets](Runtime/SpookAssets.cs) loads `spooktober.bundle`; [HellMeshes](Runtime/HellMeshes.cs) loads `hell-meshes.bin`.

## Where to change things

| To change… | Look in |
|---|---|
| Menu cards, buttons, menu text | [Presentation/SpookMenuPage.cs](Presentation/SpookMenuPage.cs), [Presentation/SpookConsole.cs](Presentation/SpookConsole.cs) |
| Spawn/dismiss rules, "one boss at a time", progress credit | [Runtime/SpookBosses.cs](Runtime/SpookBosses.cs) |
| Saved progress format | [Domain/SpookProgress.cs](Domain/SpookProgress.cs), [Runtime/SpookStore.cs](Runtime/SpookStore.cs) |
| Kaiju attacks, cooldowns, weak-spot effects | [Kaiju/Domain/KaijuRules.cs](Kaiju/Domain/KaijuRules.cs), [Kaiju/Runtime/KaijuBrain.cs](Kaiju/Runtime/KaijuBrain.cs) |
| Kaiju body, colliders, beam/stomp visuals | [Kaiju/Runtime/KaijuBody.cs](Kaiju/Runtime/KaijuBody.cs), [Kaiju/Runtime/KaijuRig.cs](Kaiju/Runtime/KaijuRig.cs) |
| Kaiju summon/dismiss, client stream | [Kaiju/Runtime/KaijuDirector.cs](Kaiju/Runtime/KaijuDirector.cs), [Kaiju/Networking/KaijuNet.cs](Kaiju/Networking/KaijuNet.cs) |
| Fallen ace decisions / tuning | [Domain/AceRules.cs](Domain/AceRules.cs), [Configuration/SpooktoberSettings.cs](Configuration/SpooktoberSettings.cs) (`Tuning()` applies four `Ace*` knobs; `AceGunDamageScale` is read separately) |
| Fallen ace flight, targets, lives | [Runtime/FallenAceDirector.cs](Runtime/FallenAceDirector.cs), [Runtime/FallenAce.cs](Runtime/FallenAce.cs), [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs) |
| Hell's Army waves | [Domain/HellRules.cs](Domain/HellRules.cs), [Runtime/HellGate.cs](Runtime/HellGate.cs) |
| Corruption spread, base capture | [Runtime/HellTerritory.cs](Runtime/HellTerritory.cs), [Domain/HellInfection.cs](Domain/HellInfection.cs), [Runtime/HellInfectionDamage.cs](Runtime/HellInfectionDamage.cs) |
| Violet map ink | [Patches/HellMapColourPatch.cs](Patches/HellMapColourPatch.cs), `HellArmy.MapInk` |
| Rad-ghost shader look | [Runtime/RadGhostLook.cs](Runtime/RadGhostLook.cs), [Runtime/RadGhostWatcher.cs](Runtime/RadGhostWatcher.cs) |
| Kaiju config / F1 summon button | [Kaiju/Configuration/KaijuSettings.cs](Kaiju/Configuration/KaijuSettings.cs); F1 row in [ConfigMenu](../../Core/Config/ConfigMenu.cs) |
| Hell dimension arena, weather camera exclusion | [Runtime/HellDimension.cs](Runtime/HellDimension.cs) (`HellWeatherExclusion`) |

## Talks to other modules

- Provides `IKaijuBoss` (KaijuDirector; ARES summons it), `IFallenAce` (SpookAce; ARES's SUMMON row), `ISetExtras` (MISC tab in Command's `MiscMfdPanel`), `IWeatherCameraExclusion` (Weather's cloud pass and Vanguard skip the hell-dimension camera).
- Consumes `IChatter` (FallenAce radio lines), `IMapProjection` (Command's map projection for the hell map layer), and its own `IKaijuBoss` through the registry (never a direct reference, since Kaiju may be off).

## Settings, network, patches

- No SET rows. `Spooktober.Ace*` keys are host-only and read at the next ace spawn; `Spooktober.Progress` is hidden (`Browsable = false`) and written by the game. `Kaiju.Enabled` is read at `Install`: toggling needs a restart.
- Network: only the kaiju streams (`KaijuState`, `KaijuSpikes`), serializers installed through `MirageSerializers.Strict`, handlers via `HandlerSlot`. Both carry `KaijuNet.ProtocolVersion` (= 1); bump it whenever a message layout changes — mismatched messages are dropped.
- GHOST and HELL sides are host-without-clients only (ruling "R15" in [GhostSide.cs](Runtime/GhostSide.cs)): a client has no scene object for a fresh HQ, Mirage would throw `SpawnObjectException`; `FallenAceDirector` evicts the ace when a client joins.
- Patches: fallen-ace hooks on `UnitPart.TakeDamage`, `Pilot.TakeDamage`/`ApplyDamage`, `Aircraft.StartEjectionSequence`, `Missile.SetAimpoint`, `CombatAI.ChooseHQTarget` (all but `Pilot.TakeDamage` shared with Wing and/or others; each is one reference check while no ace is out). `KaijuSelfDamagePatch` also sits on `UnitPart.TakeDamage`. `HellMapColourPatch` / `HellAirbaseMapColourPatch` (`UnitMapIcon.GetColor`, `AirbaseMapIcon.GetColor`) paint HELL violet. `KaijuPrefabPatch` on `NetworkManagerNuclearOption.RegisterPrefabs` registers the hardpoint prefab so joining clients can resolve hardpoints.

## Tests, sim hooks and pinned names

- Sim hooks (dev builds): `BoscaliSummer.Spooktober.SpooktoberAutomation` in [Runtime/SpooktoberAutomation.cs](Runtime/SpooktoberAutomation.cs) and `BoscaliSummer.Kaiju.KaijuAutomation` in [Kaiju/Runtime/KaijuAutomation.cs](Kaiju/Runtime/KaijuAutomation.cs). Namespaces differ from the folder on purpose; keep them and their static method names. Both refuse to run unless the game state is single player and this peer is the server.
- nomodkit tests/BoscaliSummer.Tests compiles by path: Domain `AceRules`, `HellControl`, `HellInfection`, `HellRules`, `SpookProgress`, `SpookText`, and `Kaiju/Domain/KaijuRules`. Keep them pure (no Unity) and in place.
- Embedded names matched by string: `BoscaliSummer.Spooktober.spooktober.bundle`, `...hell-meshes.bin`, `...hell-particles.bundle`, `...Dossiers.*.png`, `BoscaliSummer.Kaiju.kaiju.bundle`; GameObject names `BoscaliSummer.HellPortal`, `BoscaliSummer.RadGhost`, `BoscaliSummer.Hellfire`, `BoscaliSummer.SpookAudio`.
- Bundles are built by nomodkit `mods/boscalisummer/tools/Compile-SpooktoberAssets.ps1` / `Compile-KaijuAssets.ps1`; the bundles and `hell-meshes.bin` are optional in the csproj (`Condition="Exists(...)"`), the dossier PNGs are not.
- Scenarios: `nomod sim ls spook --mod boscalisummer` (spooktober, -ace, -hell*), `nomod sim ls kaiju --mod boscalisummer` (kaiju, kaiju-tune).

## Gotchas

- Single-player guard is easy to forget: anything that creates a `SpookSide` HQ must go through `GhostSide.RemoteClients()` checks; `GhostSide.FakeRemote` is a dev-only sim seam.
- A kaiju defeat reaches `SpookBosses.CreditKaiju` from both the host `Defeated` event and the per-frame `boss.Dead` watcher; `creditedGen` dedups per kaiju generation (the ace and Hell's Army credit once from their own `Defeated` events). Keep that when adding bosses.
- `HellPortalGround`/`HellGrowth` edit native terrain temporarily and restore only on `HellGate.Close` (dismiss, defeat, `OnDestroy`). Scene reset calls `HellGate.Forget()` instead, which drops state without restoring ("the scene took the portal"). New world edits must follow the same split.
- Spooktober patches sit on hot damage paths shared with Wing/Ares/Gamemodes/Squad; keep the "no boss out → one reference check" early return.
- Kaiju distances in `KaijuRules` assume the 15× model scale.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [SpooktoberModule](SpooktoberModule.cs), id `spooktober`, "Spooktober"; depends on nothing; roster: Spooktober.Enabled; mission profile: optional.
- 49 .cs files, 9557 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 58 |
| Configuration/ | 1 | 47 |
| Domain/ | 6 | 610 |
| Kaiju/ | 10 | 2151 |
| Patches/ | 2 | 137 |
| Presentation/ | 4 | 588 |
| Runtime/ | 25 | 5966 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/SpooktoberAutomation.cs](Runtime/SpooktoberAutomation.cs) | 776 |
| [Kaiju/Runtime/KaijuBody.cs](Kaiju/Runtime/KaijuBody.cs) | 654 |
| [Runtime/FallenAce.cs](Runtime/FallenAce.cs) | 599 |
| [Runtime/HellGate.cs](Runtime/HellGate.cs) | 562 |
| [Runtime/HellGrowth.cs](Runtime/HellGrowth.cs) | 525 |

### Harmony patches (10)

- `AirbaseMapIcon.GetColor` by `HellAirbaseMapColourPatch` in [Patches/HellMapColourPatch.cs](Patches/HellMapColourPatch.cs)
- `Aircraft.StartEjectionSequence` by `FallenAceEjectionPatch` in [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs) — SHARED with Wing
- `CombatAI.ChooseHQTarget` by `FallenAceTargetPatch` in [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs) — SHARED with Wing
- `Missile.SetAimpoint` by `FallenAcePhantomPatch` in [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs) — SHARED with CombatAdjust, Wing
- `NetworkManagerNuclearOption.RegisterPrefabs` by `KaijuPrefabPatch` in [Kaiju/KaijuPatches.cs](Kaiju/KaijuPatches.cs) — SHARED with SeaStructures, Vanguard
- `Pilot.ApplyDamage` by `FallenAcePilotDamagePatch` in [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs) — SHARED with Squad, Wing
- `Pilot.TakeDamage` by `FallenAcePilotGunPatch` in [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs)
- `UnitMapIcon.GetColor` by `HellMapColourPatch` in [Patches/HellMapColourPatch.cs](Patches/HellMapColourPatch.cs)
- `UnitPart.TakeDamage` by `FallenAcePartDamagePatch` in [Patches/FallenAcePatches.cs](Patches/FallenAcePatches.cs) — SHARED with Ares, Gamemodes
- `UnitPart.TakeDamage` by `KaijuSelfDamagePatch` in [Kaiju/KaijuPatches.cs](Kaiju/KaijuPatches.cs) — SHARED with Ares, Gamemodes

### Contracts

- Registers as service: `IFallenAce`, `IKaijuBoss`, `ISetExtras`, `IWeatherCameraExclusion`
- Implements: `IFallenAce`, `IKaijuBoss`, `ISetExtras`, `IWeatherCameraExclusion`
- Resolves: `IChatter`, `IKaijuBoss`, `IMapProjection`, `IWeatherCameraExclusion`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Spooktober`, `Kaiju` (12 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `KaijuNet.ProtocolVersion` = 1
- Messages (2): `KaijuSpikes`, `KaijuState`

### Sim hooks (never rename)

`BoscaliSummer.Kaiju.KaijuAutomation` (dev), `BoscaliSummer.Spooktober.SpooktoberAutomation` (dev)

### Pinned by nomodkit

7 files compiled by path, 2 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
