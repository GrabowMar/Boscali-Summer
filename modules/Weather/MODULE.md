# Weather — Dynamic weather and environment (`weather`)

> Host-keyed, deterministic moving weather (a "tape" of skies crossing the map) that drives the game's native fog/light/wind, plus all client-side sky, cloud, rain, lightning, ocean and canopy-water rendering.

## Player-facing behaviour

- Weather systems cross the map over mission time (fronts, storms, fog, set-pieces); native conditions, cloud height, wind and turbulence follow the weather at the aircraft.
- **ENV** page on the maximised map (MFD slot `MfdSlots.Weather`): current conditions, cloud profile, 60-minute outlook, sun/moon and wind ([WeatherMfdPanel](Presentation/WeatherMfdPanel.cs)).
- Weather console ("Met Office", **Ctrl+O** by default, `ConsoleKey`): host can pin skies, hold/resume, force storm set-pieces, re-roll the layout; clients see it read-only ("HOST CONTROLS THE WEATHER").
- SET client rows (RAIN, SKY, SEA, GROUND on the VISUALS tab; WEATHER QUALITY and CLOUD DETAIL on PERF): sections RAIN (RAIN FX, CANOPY DROPLETS, TERRAIN WET PASS, RAIN DENSITY, RAIN SOUND), SKY (REDUCED FLASHES, CINEMATIC CLOUDS, SUN SHAFTS, AURORA & SPRITES, LIGHTNING, DARK NIGHTS), WEATHER QUALITY / CLOUD DETAIL (quality preset + 4 knobs), SEA (OCEAN WAVES, SEA SPRAY, SURFACE WAKES), GROUND (TERRAIN DETAIL).
- Host table **WEATHER** on the SERVER settings Effects page: CHANGING WEATHER, WEATHER PACE, CLEAREST SKY, STORMIEST SKY, WIND VARIABILITY, TURBULENCE.
- Gameplay effect: IR missiles may lose lock on an aircraft inside cloud ([CloudSeekerPatch](Patches/CloudSeekerPatch.cs), host-side).

## How it works

- Entry: [WeatherModule](WeatherModule.cs) `Install`:
  - `AddComponent<WeatherNet>` (plain component, not a scene service);
  - [WeatherManager](Runtime/WeatherManager.cs) (scene service 64) → services `IFlightEnvironmentView`, `IWeatherView`, `IWeatherDirector`; `RegisterClientEffects` adds the volume clouds, terrain rain, rain atmosphere, atmosphere sky, lightning and canopy shader as `IClientEffect`s;
  - [WeatherMfdPanel](Presentation/WeatherMfdPanel.cs) (65), [OceanDressing](Visuals/OceanDressing.cs) (66, also `IClientEffect`);
  - SET rows and the host table; one patch (`CloudSeekerPatch`).
- Core ([Core/BoscaliMod.cs](../../Core/BoscaliMod.cs)) separately registers `ICanopyGlassView` ([CanopyGlassView](Visuals/CanopyGlassView.cs)) and `ICloudVolumeView` (`CloudVolumeView` in [WeatherVolumeDressing.cs](Visuals/WeatherVolumeDressing.cs)) **even when Weather is disabled** (skipped only in batch mode).
- Model: a [WeatherKey](Domain/WeatherKey.cs) (seed, epoch, heading, speed, story, sky pin, set-pieces, climate…) deterministically generates a [WeatherTape](Domain/WeatherTape.cs) (sequence of skies from [SkyLibrary](Domain/SkyLibrary.cs) / [WeatherStories](Domain/WeatherStories.cs)), baked into a [WeatherField](Domain/WeatherField.cs) that answers `Sample(x, z)` → `WeatherPoint` at any place and mission time. Storms, cumulus and superstructures: [StormCell](Domain/StormCell.cs), [CumulusLattice](Domain/CumulusLattice.cs), [Superstructures](Domain/Superstructures.cs). Native mapping: [NativeWeather](Domain/NativeWeather.cs).
- Lifecycle (`WeatherManager`, `[DefaultExecutionOrder(100)]`): `Update` waits until the mission environment is applied (`LevelInfo.LoadedMapSettings` + mission clock) then `CaptureBaseline` (mission's own conditions/wind, restored at teardown/disable); host builds the key once per scene (`ResolveSeed`: Random / Mission / Fixed); `UpdateField`; `UpdateNativeTargets` → `SnapToTargets` (first time) / `ApplySmoothModulation` writes `LevelInfo` natives. `LateUpdate` in [WeatherManager.Visuals.cs](Runtime/WeatherManager.Visuals.cs) publishes the `FlightEnvironmentSnapshot` and ticks rain/cloud/sky visuals. A map swap without scene load is detected (`capturedMap`) and re-captures.
- Host vs client: only the host creates/changes the key (`CanCommand => GameAccess.IsServer()`). `BroadcastSync` sends `WeatherSyncMessage` (key + host native targets + mission time) on a heartbeat ([WeatherHeartbeat](Domain/WeatherHeartbeat.cs)), plus two resends (`WeatherNet.ArmResends`) when a peer joins and when the host creates its key. Clients run `ApplyNetworkSync` through [SyncGate](Domain/SyncGate.cs) (buffer until captured, ignore a moving key whose clock is > 10 s off), then generate the same field locally and derive their own natives.
- Partial class split of `WeatherManager`: [.Console](Runtime/WeatherManager.Console.cs) (console commands), [.Director](Runtime/WeatherManager.Director.cs) (`IWeatherDirector`: `Hold("storm"|"hurricane"|"front"|"fog"|"wave")`, `Resume`, `OpenConsole`), [.Forecast](Runtime/WeatherManager.Forecast.cs), [.Visuals](Runtime/WeatherManager.Visuals.cs), [.Diagnostics](Runtime/WeatherManager.Diagnostics.cs) (readouts for automation).

## Map of folders

| Folder | What lives there |
|---|---|
| [Domain/](Domain/WeatherField.cs) | 63 engine-light files: key, tape, field, skies, stories, storms, climate, sea state, forecast/METAR text, Met Office console model/rules/text, sync gate. All compiled by nomodkit tests. |
| [Runtime/](Runtime/WeatherManager.cs) | `WeatherManager` (+5 partials), [LightningDirector](Runtime/LightningDirector.cs) (4 bounded strikes), [MetOfficeAdapter](Runtime/MetOfficeAdapter.cs) (console ↔ manager), automation/probe hooks. |
| [Visuals/](Visuals/WeatherVolumeDressing.cs) | Volumetric clouds ([WeatherVolumeDressing](Visuals/WeatherVolumeDressing.cs), [WeatherCloudPass](Visuals/WeatherCloudPass.cs), [CloudBodies](Visuals/CloudBodies.cs), [CloudMaps](Visuals/CloudMaps.cs), [CloudNoise3D](Visuals/CloudNoise3D.cs), [CloudLowRes](Visuals/CloudLowRes.cs)), cloud shadows, sky/sun/sun shafts, sky phenomena, rain emitter + atmosphere, canopy droplets/glass/shader, terrain rain/detail, ocean + sea spray, shader bundles. |
| [Presentation/](Presentation/WeatherMfdPanel.cs) | ENV page ([WeatherEnvView](Presentation/WeatherEnvView.cs), [WeatherEnvParts](Presentation/WeatherEnvParts.cs), [WeatherGlyph](Presentation/WeatherGlyph.cs)). |
| [Presentation/MetOffice/](Presentation/MetOffice/MetOfficeWindow.cs) | The weather console window (desk, sky picker, fronts, strip, input capture). |
| [Audio/](Audio/RainSoundscape.cs) | Rain soundscape, thunder arrivals, listener math. |
| [Networking/](Networking/WeatherNet.cs) | `WeatherNet` (handler registration, broadcast, explicit Mirage serializers) and [WeatherSyncMessage](Networking/WeatherSyncMessage.cs). |
| [Patches/](Patches/CloudSeekerPatch.cs) | IR lock break in cloud. |
| [Configuration/](Configuration/WeatherSettings.cs) | 33 `Weather` keys. |
| [Assets/](Assets/Source/BUILD-SOURCE.txt) | `sky.bundle`, `surfaces.bundle`, `weatherrain.bundle`, shader sources in `Assets/Source`. |

## Where to change things

| To change… | Look in |
|---|---|
| Which skies exist and their parameters | [Domain/SkyLibrary.cs](Domain/SkyLibrary.cs), [Domain/SkyParams.cs](Domain/SkyParams.cs), [Domain/SkyArchetypes.cs](Domain/SkyArchetypes.cs) |
| Sequence of weather over a mission | [Domain/WeatherTape.cs](Domain/WeatherTape.cs), [Domain/WeatherStories.cs](Domain/WeatherStories.cs), [Domain/TapeOpening.cs](Domain/TapeOpening.cs), [Domain/TapePace.cs](Domain/TapePace.cs) |
| Weather at a point (rain, cloud base, wind) | [Domain/WeatherField.cs](Domain/WeatherField.cs), [Domain/StormCell.cs](Domain/StormCell.cs) |
| How weather drives the game's fog/light/wind | [Domain/NativeWeather.cs](Domain/NativeWeather.cs), `UpdateNativeTargets`/`WriteNative` in [Runtime/WeatherManager.cs](Runtime/WeatherManager.cs) |
| Map-specific climate | [Domain/WeatherClimate.cs](Domain/WeatherClimate.cs) |
| Seed source | [Domain/WeatherSeed.cs](Domain/WeatherSeed.cs), `ResolveSeed` |
| Console presets / mission-script set-pieces | [Domain/WeatherScenario.cs](Domain/WeatherScenario.cs), [Runtime/WeatherManager.Director.cs](Runtime/WeatherManager.Director.cs) |
| Console UI and wording | [Presentation/MetOffice/](Presentation/MetOffice/MetOfficeWindow.cs), [Domain/MetOfficeText.cs](Domain/MetOfficeText.cs), [Domain/MetOfficeRules.cs](Domain/MetOfficeRules.cs) |
| ENV page | [Presentation/WeatherMfdPanel.cs](Presentation/WeatherMfdPanel.cs), [Presentation/WeatherEnvView.cs](Presentation/WeatherEnvView.cs), [Domain/EnvBriefing.cs](Domain/EnvBriefing.cs) |
| Cloud look / cost | [Visuals/WeatherVolumeDressing.cs](Visuals/WeatherVolumeDressing.cs), [Visuals/WeatherCloudPass.cs](Visuals/WeatherCloudPass.cs), `Quality` presets (`ApplyQuality`) in [Configuration/WeatherSettings.cs](Configuration/WeatherSettings.cs), SET rows in [WeatherModule.cs](WeatherModule.cs) |
| Rain visuals / canopy water | [Visuals/ProceduralRainEmitter.cs](Visuals/ProceduralRainEmitter.cs), [Visuals/CanopyDropletSim.cs](Visuals/CanopyDropletSim.cs), [Visuals/CanopyShaderDressing.cs](Visuals/CanopyShaderDressing.cs), [Domain/RainVisualMath.cs](Domain/RainVisualMath.cs) |
| Lightning | [Runtime/LightningDirector.cs](Runtime/LightningDirector.cs), [Domain/StormLightning.cs](Domain/StormLightning.cs), [Domain/LightningMath.cs](Domain/LightningMath.cs) |
| Ocean waves / spray | [Visuals/OceanDressing.cs](Visuals/OceanDressing.cs), [Domain/SeaState.cs](Domain/SeaState.cs), [Visuals/SeaSpray.cs](Visuals/SeaSpray.cs) |
| Sync payload | [Networking/WeatherSyncMessage.cs](Networking/WeatherSyncMessage.cs), [Networking/WeatherNet.cs](Networking/WeatherNet.cs), [Domain/WeatherKeyWire.cs](Domain/WeatherKeyWire.cs) |
| SET rows, host WEATHER table | [WeatherModule.cs](WeatherModule.cs); keys/defaults in [Configuration/WeatherSettings.cs](Configuration/WeatherSettings.cs); host-synced list in [HostAuthority](../../Core/Config/HostAuthority.cs) |
| Console hotkey | `ConsoleKey` / `ConsoleKeyRequiresCtrl` in [Configuration/WeatherSettings.cs](Configuration/WeatherSettings.cs), `ConsoleKeyPressed` in [Runtime/WeatherManager.cs](Runtime/WeatherManager.cs) |
| IR-in-cloud rule | [Patches/CloudSeekerPatch.cs](Patches/CloudSeekerPatch.cs) (`BreakChancePerCheck`) |

## Talks to other modules

- Provides `IFlightEnvironmentView` (per-frame cockpit air: Immersion, Hud), `IWeatherView` (`TrySample` at a global point: Chatter, Compatibility, Hud, Immersion's sky optics, Support's `SpaceSky`), `IWeatherDirector` (ARES and Scenario force set-pieces / open the console). Core also exposes `ICanopyGlassView` (Immersion) and `ICloudVolumeView` (Vanguard's SA-74 mushroom) from this module's Visuals.
- Consumes `IHudBoard` (HUD notices) and `IWeatherCameraExclusion` (Spooktober's hell dimension: the cloud pass skips those cameras).
- Reads Core flags: `ForeignMods.RainYielded` / `SkyYielded` (Compatibility hands rain/sky to NoAtmoFX), `NuclearAftermath.Active` (suppresses lightning), `MissionTimeSource` (shared mission clock; reset in `ResetForScene`).

## Settings, network, patches

- Host-authoritative (listed in [HostAuthority](../../Core/Config/HostAuthority.cs), so clients take the host's values; clients also use them to derive their own natives): `TapeMoves`, `Pace`, `MinConditions`, `MaxConditions`, `WindVariability`, `TurbulenceMultiplier`. `SeedMode` / `FixedSeed` are read only by the host when it builds the key (not synced; the key carries the seed). `Weather.Enabled` is a compared master switch. Everything shown on SET is client-local rendering.
- `Weather.Enabled` off restores the captured native weather and tears down rain/clouds live; the key is kept for the scene.
- **`WeatherNet.ProtocolVersion` (= 9) must be bumped whenever `WeatherSyncMessage`, its serializer order, or anything that changes what a key generates (tape/field generation in Domain) changes**: peers must produce identical fields from the same key. Mismatched messages are dropped by both reader and handler.
- Serializers are installed explicitly via `MirageSerializers` (BepInEx assembly is not woven); handlers re-registered every second (`HandlerSlot.Swap`).
- `CloudSeekerPatch` runs on the server's local sim only (`missile.IsServer && LocalSim`), invokes private `IRSeeker.LoseLock` by reflection.

## Tests, sim hooks and pinned names

- Sim hooks: `BoscaliSummer.Modules.Weather.Runtime.WeatherAutomation` (**all builds**, not inside `#if !PUBLIC_RELEASE`; ~56 nomodkit references; public hooks PrepareCapture, SetRainVisuals, SetDarkNights, ForceWeather, SetFixtureTape, PinSky, ApplyScenario, Console, View, Readout, NuclearLightning, CloudProbe), dev-only `CloudMotionProbe` and `LightningVisualProof`.
- nomodkit compiles by path **all 63 Domain files**, [Audio/EnvironmentAudioMath.cs](Audio/EnvironmentAudioMath.cs) and Visuals [CloudBodies](Visuals/CloudBodies.cs), [CloudMaps](Visuals/CloudMaps.cs), [CloudNoise3D](Visuals/CloudNoise3D.cs). Domain is included by glob (`modules\Weather\Domain\*.cs` in both `BoscaliSummer.Tests` and `WeatherClimate.Tests`), so a new Domain file is compiled there automatically and must build without Unity/game types beyond what those projects reference; a Domain file that starts depending on a file outside Domain needs nomodkit's csproj changed (outside this repo) — prefer extending existing files.
- Named by full name in nomodkit: `WeatherSettings`, `Domain.WeatherField`, `Domain.WeatherKey`, `Domain.WeatherRegimeType`, `Networking.WeatherNet`, `Networking.WeatherSyncMessage`, `Patches.CloudSeekerPatch`, `Runtime.WeatherManager`, `Visuals.OceanDressing`, `Visuals.SurfaceShaderBundle`, `Visuals.WeatherCloudShadows`.
- Embedded names: `BoscaliSummer.Weather.sky.bundle`, `...surfaces.bundle`, `...weatherrain.bundle`, `...orbit-clouds.png`.
- Scenarios: `nomod sim ls weather --mod boscalisummer` (17: weather-capture, -climate, -clouds, -console, -handshake, -lightning-look, -rain, -settings, -skies, -tape-*…), `nomod sim ls lightning --mod boscalisummer`.

## Gotchas

- Determinism is the multiplayer contract: field generation must depend only on the key and mission time (no `UnityEngine.Random`, no frame-time, no per-peer state). The host sends the key, not the weather.
- Never capture at scene load: the mission environment is applied later (`LevelInfo.ApplyMapSettings`); `GameState.Encyclopedia` never captures. Never build the field before `MissionTimeSource.TryNow` succeeds or the sky jumps.
- Floating origin: `WeatherManager` runs at execution order 100, after `CameraStateManager` (2) moves the camera and shifts the origin; placing clouds earlier caused a ~1 km flash.
- `CloudMotionProbe` is marked "temporary dev probe; delete after the check" but nomodkit names it — deleting it breaks nomodkit scenarios.
- Code comments cite a weather spec (§3.2, 8.1, 9.4…) and `modules/Weather/AGENTS.md` (in [MetOfficeInput](Presentation/MetOffice/MetOfficeInput.cs)); neither exists in this repo — treat the code and Domain tests as the spec.
- Several things keep working with Weather disabled (Core-registered `ICanopyGlassView`/`ICloudVolumeView`); consumers of `IWeatherView`/`IFlightEnvironmentView` must handle "service absent" and `TrySample` returning false.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [WeatherModule](WeatherModule.cs), id `weather`, "Dynamic weather and environment"; depends on nothing; roster: Weather.Enabled; mission profile: battlefield.
- 126 .cs files, 19805 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 136 |
| Audio/ | 3 | 270 |
| Configuration/ | 1 | 235 |
| Domain/ | 63 | 6918 |
| Networking/ | 2 | 263 |
| Patches/ | 1 | 32 |
| Presentation/ | 12 | 2667 |
| Runtime/ | 11 | 2772 |
| Visuals/ | 32 | 6512 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/WeatherManager.cs](Runtime/WeatherManager.cs) | 1052 |
| [Presentation/WeatherEnvParts.cs](Presentation/WeatherEnvParts.cs) | 926 |
| [Visuals/CloudBodies.cs](Visuals/CloudBodies.cs) | 694 |
| [Visuals/WeatherVolumeDressing.cs](Visuals/WeatherVolumeDressing.cs) | 648 |
| [Visuals/OceanDressing.cs](Visuals/OceanDressing.cs) | 451 |

### Harmony patches (1)

- `IRSeeker.IRLockCheck` by `CloudSeekerPatch` in [Patches/CloudSeekerPatch.cs](Patches/CloudSeekerPatch.cs)

### Contracts

- Registers as service: `IFlightEnvironmentView`, `IWeatherDirector`, `IWeatherView`
- Implements: `ICanopyGlassView`, `IClientEffect`, `ICloudVolumeView`, `IFlightEnvironmentView`, `IWeatherDirector`, `IWeatherView`
- Resolves: `IHudBoard`, `IWeatherCameraExclusion`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Weather` (34 keys)
- SET client settings: 21; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `WeatherNet.ProtocolVersion` = 9
- Messages (1): `WeatherSyncMessage`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Weather.Runtime.CloudMotionProbe` (dev), `BoscaliSummer.Modules.Weather.Runtime.LightningVisualProof` (dev), `BoscaliSummer.Modules.Weather.Runtime.WeatherAutomation`

### Pinned by nomodkit

67 files compiled by path, 14 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
