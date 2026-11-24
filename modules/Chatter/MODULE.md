# Chatter — Chatter (`chatter`)

> The military radio: loads `.chatter` packs, runs the Core `ChatterDirector` on one shared channel, publishes the player's own nets (AWACS, tower, enemy, allied, command), asks YES/NO questions, plays recorded voices and shows the COMMS strip; other modules publish through `IChatter`.

## Player-facing behaviour

- **COMMS** block in the left screen column (under the game's event log): net tag + callsign + words typed as spoken; amber for emergency/intercepted enemy traffic. Shown only while flying or while a scenario line is up; hidden while the map is maximised.
- Radio lines from AWACS (bandits, missile launches, kills, periodic picture, SAM calls), the tower (clearances replace the game's centre notice box), intercepted enemy pilots, the allied net, the command net (events, operations, insertions, base captures) and wingmen (via other modules).
- YES/NO questions on the radio: COMMAND offers new contracts, OPS offers perks, VANGUARD offers drones on a new lock; answer with the configured keys (prompt shows them); a visor ack sound confirms.
- Recorded ElevenLabs voices for built-in stations (`Assets/Voices/boscali-*`), plus the pilot's own voice packs; key-up click and squelch around transmissions; music ducks while voices speak.
- After an SA-74 nuclear strike: command net silenced, scripted emergency calls, then long silence and occasional survivor calls.
- Lines also go to the MFD tactical log.
- Players can add packs in `BepInEx/config/BoscaliSummer/chatter/*.chatter`.

## How it works

- [ChatterModule](ChatterModule.cs) `Install`: adds [ChatterHost](Runtime/ChatterHost.cs) as scene service (order 35), configures it with **`context.Settings.Wing`** (Wing's `WingConfig` implements `IChatterSettings`), registers it as `IChatter`.
- The engine is in Core: `Core/Chatter` ([ChatterDirector](../../Core/Chatter/ChatterDirector.cs), [ChatterScript](../../Core/Chatter/ChatterScript.cs) pack parser/library, [ChatterQuestions](../../Core/Chatter/ChatterQuestions.cs), [ChatterTypes](../../Core/Chatter/ChatterTypes.cs), [ChatterVoiceKeys](../../Core/Chatter/ChatterVoiceKeys.cs)). This module is the Unity host and publishers.
- `ChatterHost.Configure` loads embedded packs (`BoscaliSummer.Chatter.*.chatter`, ordinal order — hence the `zz-`/`zzz-` file names) then user packs. `ResetForScene` resets director/questions/nets and activates voice packs.
- `ChatterHost.Update` (every client, every frame): `RadioAftermath.Advance`, voices tick, receive/blackout handling, every 2 s ticks [CommandNet](Runtime/CommandNet.cs), [FlightNets](Runtime/FlightNets.cs) and (when flying) [RadioAsks](Runtime/RadioAsks.cs) + world conditions (night/rain from `IWeatherView`), then `director.Tick`, answer keys, strip tick.
- `director.Aired` → `OnAired`: [ChatterVoices](Runtime/ChatterVoices.cs) plays a clip (holds the channel as long as it speaks), [ChatterAudio](Runtime/ChatterAudio.cs) click/squelch, `IRadioComms.DuckMilitary`, [ChatterStrip](Presentation/ChatterStrip.cs)`.Show`, `ITacticalLog.Radio`.
- Harmony patches feed `FlightNets`: kill RPC (`OnKill`), `Ship.UnitDisabled` (`OnShipDisabled`), `AircraftActionsReport.ReportText` (`TakeNotice`: clearances become tower lines, other notices go to the TAC BOARD ticker; a taken notice suppresses the native box).
- No authority split: every client runs its own radio from game state it already has (kill messages, track file, MWS, airbases). Nothing is sent over the network.

## Where to change things

| To change… | Look in |
|---|---|
| Words, events, exchanges | `Assets/Packs/*.chatter` (format parsed by `Core/Chatter/ChatterScript.cs`) |
| Director rules (priority, rationing, holds, levels) | `Core/Chatter/ChatterDirector.cs` |
| When AWACS/tower/enemy/allied lines fire | [Runtime/FlightNets.cs](Runtime/FlightNets.cs) |
| Command-net events from theater/operations | [Runtime/CommandNet.cs](Runtime/CommandNet.cs) |
| YES/NO offers (contracts, perks, drones) | [Runtime/RadioAsks.cs](Runtime/RadioAsks.cs) |
| Question window/answer handling | `Core/Chatter/ChatterQuestions.cs`, answer keys in `ChatterHost.Update` |
| Voice pack selection/decoding | [Runtime/ChatterVoices.cs](Runtime/ChatterVoices.cs), clip names in `Core/Chatter/ChatterVoiceKeys.cs` |
| Built-in voice clips | `Assets/Voices/<pack>/*.mp3`, `pack.txt`, [Assets/Voices/voices.tsv](Assets/Voices/voices.tsv), [Assets/Voices/README.txt](Assets/Voices/README.txt) |
| Click/squelch | [Runtime/ChatterAudio.cs](Runtime/ChatterAudio.cs) |
| COMMS strip look/layout | [Presentation/ChatterStrip.cs](Presentation/ChatterStrip.cs) |
| Nuclear emergency/survivor sequence | `Emergency`/`Survivor` in [ChatterHost](Runtime/ChatterHost.cs), timing in `Core/Contracts/RadioAftermath.cs`, lines in `zzz-sa74.chatter` |
| Kill/ship/notice hooks | [Patches/ChatterPatches.cs](Patches/ChatterPatches.cs) |
| Radio level, voice packs, volume, answer keys | Wing's `WingConfig` (`IChatterSettings`), not here |

## Talks to other modules

- Provides [IChatter](../../Core/Contracts/IChatter.cs): `Raise`, `Say`, `Ask`, `Scripted`/`EndScripted` (Scenario), `Station`, `NearTower`, `Chime`, `Director`, `Now`. Wing, Scenario and others publish through it.
- Consumes `IRadioComms` (Radio: receive switch, military volume, duck), `ITacticalLog` (MFD log), `IWeatherView` (night/rain conditions), `IHudBoard`, `ITacBoardSettings`, `IBaseDefenseAlarmService` (all three in FlightNets), `IActiveEventsView`, `ITheaterWarView`, `IAirAssaultObservation` (CommandNet), `IOpsPerks`, `IDroneCommand`, `ISecondaryObjectivesView` (RadioAsks).
- Reads/advances static `RadioAftermath` (Core/Contracts); registers a voice probe with `Core.Fx.VisorSound`.

## Settings, network, patches

- No config section of its own: the radio's settings are Wing's (`[Wing]` keys behind `IChatterSettings`). Client-local.
- No network messages. Kill RPC prefix runs on host and clients alike ("host and received").
- `ChatterKillPatch` uses `TargetMethod` via `KillMessageRpc.Target()` (Mirage-generated `UserCode_RpcKillMessage_*`), shared with Command, Nation, Wing — fragile on game/Mirage updates.
- `ChatterNoticePatch` (shared with Hud) fails open: exceptions or untaken notices show natively.

## Tests, sim hooks and pinned names

- Sim hook (dev only): [ChatterAutomation](Runtime/ChatterAutomation.cs) — `Demo`, `Quiet`, `Acceptance`, `Question`, `Read`; used by `chatter`, `apocalypse-radio`, `battlefield-radio`, `radio-questions`, `vg-sa74`, `vg-sa74-radio`, `readme-flight`, `radial-visor`, `hud-zones`, `hud-edge`, `hud-ar-glow`.
- `apocalypse-radio` reflects `ChatterHost.Instance.Voices.Loading` / `.Sounding`: keep `Instance`, `Voices`, `ChatterVoices.Loading`/`Sounding` names.
- nomodkit `HudLogUnityCheck.Scope.Left.cs` names `BoscaliSummer.Modules.Chatter.Presentation.ChatterStrip`.
- nomodkit `tests/Chatter.Tests` compiles `Core/Chatter/*`, `Core/Contracts/RadioAftermath.cs`, Wing's `Domain/Chatter/WingCues.cs` and `Domain/Personnel/ChatterPersona.cs` by path.
- `nomod sim ls chatter --mod boscalisummer` (also `radio`).

## Gotchas

- `ChatterAutomation` is `#if !PUBLIC_RELEASE`; `StripSuppressed` exists for it in all builds.
- Pack load order is ordinal by resource name; a later pack adds lines to an event (or clears them with `!replace`) and replaces an exchange with the same id — renaming a pack file changes behaviour.
- Turning military receive off (Radio) silences the director, stops voices and drops open questions; `RadioAftermath.Blocking` does the same and forces `director.Level = Off` except for the scripted emergency/survivor raises.
- `Ask` refuses in batch mode and when the receiver is off; answer callbacks are wrapped in `Guard` and must re-read their service (the thing asked about may be gone).
- `ChatterAudio` fails closed: one exception sets `unavailable` and silences click/squelch/chime until the next `ChatterAudio.Reset` (new mission, receiver switched off, nuclear blackout).
- Code comments reference `docs/CHATTER_PACKS.md`, which is not in this repo.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [ChatterModule](ChatterModule.cs), id `chatter`, "Chatter"; depends on nothing; roster: always; mission profile: optional.
- 10 .cs files, 2413 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 30 |
| Patches/ | 1 | 53 |
| Presentation/ | 1 | 472 |
| Runtime/ | 7 | 1858 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/FlightNets.cs](Runtime/FlightNets.cs) | 479 |
| [Presentation/ChatterStrip.cs](Presentation/ChatterStrip.cs) | 472 |
| [Runtime/ChatterVoices.cs](Runtime/ChatterVoices.cs) | 380 |
| [Runtime/ChatterHost.cs](Runtime/ChatterHost.cs) | 374 |
| [Runtime/ChatterAutomation.cs](Runtime/ChatterAutomation.cs) | 243 |

### Harmony patches (3)

- `AircraftActionsReport.ReportText` by `ChatterNoticePatch` in [Patches/ChatterPatches.cs](Patches/ChatterPatches.cs) — SHARED with Hud (INDEPENDENT)
- `MessageManager.UserCode_RpcKillMessage_*` by `ChatterKillPatch` in [Patches/ChatterPatches.cs](Patches/ChatterPatches.cs) (TargetMethod) — SHARED with Command, Nation, Wing (ORDER-SENSITIVE: chatter → command; wing → command)
- `Ship.UnitDisabled` by `ChatterShipPatch` in [Patches/ChatterPatches.cs](Patches/ChatterPatches.cs)

### Contracts

- Registers as service: `IChatter`
- Implements: `IChatter`
- Resolves: `IActiveEventsView`, `IAirAssaultObservation`, `IBaseDefenseAlarmService`, `IDroneCommand`, `IHudBoard`, `IOpsPerks`, `IRadioComms`, `ISecondaryObjectivesView`, `ITacBoardSettings`, `ITacticalLog`, `ITheaterWarView`, `IWeatherView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Chatter.Runtime.ChatterAutomation` (dev)

### Pinned by nomodkit

0 files compiled by path, 3 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
