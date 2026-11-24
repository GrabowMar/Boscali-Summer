# Radio — Radio (`radio`)

> Client-local cockpit radio: three built-in broadcast stations (music, adverts, news), a local OGG/WAV music deck, the military-receive switch/volume other modules read, and silencing of the vanilla soundtrack while any of it plays.

## Player-facing behaviour

- **RAD** MFD page ("RADIO" console) with two tabs: **BROADCAST** (station presets, dial, military RECEIVE strip) and **MUSIC** (local deck: folders, track pages, play/next/prev, RESCAN, open folder).
- Built-in stations: Boscali Republic Radio (`agrapol-fm`), PALA State Radio (`maris-network`), Base Broadcast (`base-broadcast`, same mercenary set for every faction). Each song is followed by one advert or news item when `BroadcastTalk` is on.
- User music: folders under the mod's `Music` user-content folder become extra stations and deck folders; a starter `README.txt` and import folders are created on first scan.
- Station songs alternate with the installed vanilla soundtrack once it is discoverable; while radio/deck is on, the game's own music is held silent and restored afterwards.
- A cockpit HUD line ("RADIO · <station>", track, progress) and a TAC BOARD `radio` gauge mirror what plays.
- Military speech (Chatter) ducks station/deck volume; after a nuclear blackout (`RadioAftermath`) stations go to static and "SIGNAL LOST".
- Cockpit-menu actions (`cockpit/music`, `trknext`, `trkprev`, `receiver`, `seek`) drive the deck/receiver through `IRadioRemote`.

## How it works

- [RadioModule](RadioModule.cs) `Install`: adds [RadioManager](Runtime/RadioManager.cs) as scene service (order 40), configures it with `Settings.Radio`, registers it as `IRadioRemote` and `IRadioComms`, adds [RadioHudLine](Presentation/RadioHudLine.cs) (order 41) and registers [RadioScopeGauge](Runtime/RadioScopeGauge.cs).
- `RadioManager` (MonoBehaviour) owns everything: two [RadioProgram](Runtime/RadioProgram.cs) transports (`receiver` for stations, `deck` for local music), the station list, per-station resume cursors, the break queue and the [VanillaMusicHold](Runtime/VanillaMusicHold.cs).
- `Configure` resolves the user music folder (`ModFolders.UserContent("Music")`) and scans it immediately when `[Radio] Enabled` is on and not headless. `ResetForScene` stops both programs, clears cursors/breaks, forgets scene-owned vanilla clips and rebuilds stations.
- `Update` (every frame, skipped when headless): soundtrack probe → program-name tick → advance finished tracks → [RadioPanel](Presentation/RadioPanel.cs)`.Tick` (installs the RAD MFD panel lazily) → duck gain → aftermath static → vanilla hold sync.
- Station content: [RadioLibrary](Runtime/RadioLibrary.cs) scans disk; [RadioStation](Runtime/RadioStation.cs) also defines `VanillaSoundtrackCatalog` (installed-soundtrack discovery); [RadioBroadcastSegments](Runtime/RadioBroadcastSegments.cs) reads the embedded `broadcast.tsv` and unpacks mp3s to the BepInEx cache; `BreakPlan` rotates ad/news.
- `RadioProgram` loads files via `UnityWebRequestMultimedia` (async, one in-flight decode, generation-checked), crossfades between two AudioSources, levels loudness (`Loudness.Gain`).
- Dials/frequencies are cosmetic ([RadioFrequencies](Runtime/RadioFrequencies.cs)): no propagation or RF simulation.
- No host authority, no networking: every client runs its own radio.

## Where to change things

| To change… | Look in |
|---|---|
| Station list, built-in ids/names/colours, subtitles | [Runtime/RadioManager.cs](Runtime/RadioManager.cs) (`BuildStations`, `GetChannelColor`), [Runtime/RadioLibrary.cs](Runtime/RadioLibrary.cs) (`BuiltInStationRules`) |
| Song/advert/news sequencing | `NextTrack`/`PlayOnTune`/`ResumeStation` in [RadioManager](Runtime/RadioManager.cs), `BreakPlan` in [RadioBroadcastSegments](Runtime/RadioBroadcastSegments.cs) |
| Broadcast clips and titles | [Assets/Broadcast/broadcast.tsv](Assets/Broadcast/broadcast.tsv) + mp3s (file name = `<set>-<kind>-<n>.mp3`) |
| Programme names by time of day | [Runtime/RadioProgramming.cs](Runtime/RadioProgramming.cs) |
| Frequencies, bands, dial seek | [Runtime/RadioFrequencies.cs](Runtime/RadioFrequencies.cs) |
| Audio loading, crossfade, loudness | [Runtime/RadioProgram.cs](Runtime/RadioProgram.cs) |
| Vanilla soundtrack silencing/restoring | `BeginVanillaHold`/`ReleaseVanillaHold`/`AllowVanillaMusic` in [RadioManager](Runtime/RadioManager.cs), [Patches/VanillaMusicPatches.cs](Patches/VanillaMusicPatches.cs) |
| Military duck amount/timing | `Update` and `DuckMilitary` in [RadioManager](Runtime/RadioManager.cs) |
| Post-nuclear static/cues | `UpdateAftermathStatic` in [RadioManager](Runtime/RadioManager.cs) |
| RAD page layout/controls | [Presentation/RadioPanel.cs](Presentation/RadioPanel.cs), parts in [Presentation/RadioParts.cs](Presentation/RadioParts.cs) |
| HUD line text | [Domain/RadioHudCopy.cs](Domain/RadioHudCopy.cs), [Presentation/RadioHudLine.cs](Presentation/RadioHudLine.cs) |
| TAC BOARD gauge | [Runtime/RadioScopeGauge.cs](Runtime/RadioScopeGauge.cs) |
| Config keys/defaults | [Configuration/RadioSettings.cs](Configuration/RadioSettings.cs) |
| User-folder README text | [Assets/stations-readme.txt](Assets/stations-readme.txt) |

## Talks to other modules

- Provides [IRadioRemote](../../Core/Contracts/IRadioRemote.cs): deck/receiver transport for Autopilot's cockpit menu (`MenuGame.Contracts.cs`).
- Provides [IRadioComms](../../Core/Contracts/IRadioComms.cs): `ReceiveMilitary`, `MilitaryVolume`, `DuckMilitary` — read by Chatter (`ChatterHost`) to gate and level military speech and duck music.
- Reads the static [RadioAftermath](../../Core/Contracts/RadioAftermath.cs) (nuclear blackout state, fed by Vanguard's SA-74 fallout; Chatter's dev automation can fake it) — not a service, a shared static.
- Registers the `radio` gauge with `ScopeGauges` (TAC BOARD) and a `HudLineWidget` channel `radio`.

## Settings, network, patches

- `[Radio]` keys are all client-local (no SET SERVER view, no sync). `Enabled` controls stations/deck only; military receive is independent of it.
- No network messages; nothing about the radio is shared between players.
- The three Harmony prefixes on `MusicManager.PlayMusic/CrossFadeMusic/QueueMusicClip` (via `RadioManager.AllowVanillaMusic`) return `false` while the hold is up (radio/deck playing, or listening during a nuclear blackout) and stash the last requested clip, which `ReleaseVanillaHold` replays. `EnforceVanillaSilence` sweeps every 0.5 s as a backstop for unpatched paths.
- Embedded resources use csproj `LogicalName`s `BoscaliSummer.RadioAssets.*` and `BoscaliSummer.RadioAssets.Broadcast.*`; code builds these names as strings.

## Tests, sim hooks and pinned names

- Sim hooks (dev only): [RadioAutomation](Runtime/RadioAutomation.cs)`.Step` (scenarios `radio-breaks`, `radio-continuity`, `radio-voice-steal`, `apocalypse-radio`, `battlefield-radio`, `recorded-bank`, `recorded-world`), [RadioLoadProbe](Runtime/RadioLoadProbe.cs).
- Scenarios reflect into private state: `RadioManager.active.receiver.IsPlaying` and `RadioManager.active.IsTalking`. Do not rename the static field `active`, the field `receiver`, `RadioProgram.IsPlaying` or `IsTalking`.
- nomodkit `tests/BoscaliSummer.Tests` compiles by path: `Domain/RadioHudCopy.cs`, `Runtime/RadioFrequencies.cs`, `Runtime/RadioLibrary.cs`, `Runtime/RadioProgramming.cs`, `Runtime/VanillaMusicHold.cs` — keep them free of Unity-scene dependencies and in place.
- PatchProbe names `RadioModule` and the three `Vanilla*MusicPatch` types; `WingRadioUnityCheck` uses the `BoscaliSummer.RadioAssets` resource prefix.
- `nomod sim ls radio --mod boscalisummer`.

## Gotchas

- `RadioManager` is a static singleton via `active`; the Harmony prefixes go through it. With two instances the last configured wins.
- Headless (`GameManager.IsHeadless`): no scan, component disabled in `ResetForScene`. Don't rely on the radio in dedicated-server sims.
- `RadioProgram` sources play straight to the listener, off the game's mixer (comment in `Prepare`), so mixer effects (G-LOC low-pass) don't apply; the game's Master and Music slider values are multiplied in by hand in `RadioProgram.Level`. The aftermath static instead uses the Interface mixer group.
- Vanilla clips cached in `interruptedVanillaClip`/`deferredVanillaClip` are scene-owned; `ResetForScene` drops them on purpose — do not "fix" by restoring across maps.
- Station resume (`StationCursor`) uses wall time since leaving; leaving during talk resumes on the next song, not the advert.
- `RadioAftermath.Active` overrides almost every public string (`Status`, `TickerText`, `CurrentTrackTitle`); check it first when text looks wrong.
- Broadcast mp3s are unpacked to `BepInEx/cache/BoscaliSummer/Radio` and reused when the byte length matches; a same-size replacement clip is not refreshed.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [RadioModule](RadioModule.cs), id `radio`, "Radio"; depends on nothing; roster: always; mission profile: optional.
- 20 .cs files, 3237 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 33 |
| Configuration/ | 1 | 31 |
| Domain/ | 1 | 25 |
| Patches/ | 1 | 27 |
| Presentation/ | 4 | 743 |
| Runtime/ | 12 | 2378 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/RadioManager.cs](Runtime/RadioManager.cs) | 937 |
| [Runtime/RadioProgram.cs](Runtime/RadioProgram.cs) | 424 |
| [Presentation/RadioPanel.cs](Presentation/RadioPanel.cs) | 381 |
| [Runtime/RadioFrequencies.cs](Runtime/RadioFrequencies.cs) | 266 |
| [Presentation/RadioParts.cs](Presentation/RadioParts.cs) | 232 |

### Harmony patches (3)

- `MusicManager.CrossFadeMusic` by `VanillaCrossFadeMusicPatch` in [Patches/VanillaMusicPatches.cs](Patches/VanillaMusicPatches.cs)
- `MusicManager.PlayMusic` by `VanillaPlayMusicPatch` in [Patches/VanillaMusicPatches.cs](Patches/VanillaMusicPatches.cs)
- `MusicManager.QueueMusicClip` by `VanillaQueueMusicPatch` in [Patches/VanillaMusicPatches.cs](Patches/VanillaMusicPatches.cs)

### Contracts

- Registers as service: `IRadioComms`, `IRadioRemote`
- Implements: `IRadioComms`, `IRadioRemote`
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Radio` (8 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Radio.Runtime.RadioAutomation` (dev), `BoscaliSummer.Modules.Radio.Runtime.RadioLoadProbe` (dev)

### Pinned by nomodkit

5 files compiled by path, 6 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
