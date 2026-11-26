# Compatibility — Compatibility (`compatibility`)

> Lets Boscali share the client with other client mods (NoAtmoFX, Kelly's Client Effects, Expansion2082, UIPlus) so no
> effect or HUD is drawn twice and no key fires twice; client-only, no Harmony patches, no network.

## Player-facing behaviour

- SET rows in sections `NOATMOFX`, `KELLY'S FX`, `EXPANSION 2082`, `UIPLUS`, `KEY CLASHES`. Command's SET board
  (`CCompat`, not its own page) appends them to the VISUALS tab, and only shows a section when one of its rows
  has a `Gate` that passes, i.e. the other mod is installed.
- NoAtmoFX installed: its volumetric clouds can replace Boscali's clouds, sun shafts, lightning and thunder
  (NOATMOFX SKY); RAIN picks who draws rain and canopy rain; WEATHER LINK makes its clouds follow Boscali's weather.
- Kelly's Client Effects installed: OVERLAPS picks who draws contrails, dust wake, bullet cracks, airfield lights
  and the missile sonic boom; the other side's copy is switched off.
- Expansion2082 installed: SHIP HAZE and SHIP AA RULES drive that ship pack's own config switches.
- UIPlus installed: UIPLUS HUD yields Boscali's flight HUD (TAC BOARD, visor, arcs, brow, alerts, third-person
  cluster, external HUD and native marker repair) to UIPlus live without restarting; EXTRA OVERLAYS (off by default)
  also stands down Boscali's extra HUD-space overlays (chatter strip, wing panel/tint, ace hunt, contracts, comms
  markers, LZ cue, drone feed, AR approach, visor glass, terrain ghost). UIPlus's own `HudStyle` is never overwritten.
- MOVE CLASHING KEYS: a plain NoAtmoFX key that equals a Boscali key is rebound to Ctrl+key (Alt for F8).

## How it works

- Entry: [CompatibilityModule](CompatibilityModule.cs) `Install` adds one scene service
  [CompatBridge](Runtime/CompatBridge.cs) (order 50), registers the SET rows above, and
  `OnTeardown(ForeignMods.ClearYields)`. `PatchTypes` is empty.
- Roster: `!Application.isBatchMode` only (never on a headless server); mission profile: core.
- `CompatBridge.Update` runs every 0.5 s (unscaled time):
  1. Finds the other plugins by BepInEx GUID through `ForeignMods.Find` (`Core/Game/ForeignMods.cs`). Plugins
     that load after Boscali only appear once the chainloader reaches them, hence the polling.
  2. Writes the static yield flags on `ForeignMods` (`SkyYielded`, `RainYielded`, `FrostYielded`,
     `WorldFxYielded`, `MuffleYielded`, `AirfieldYielded`, `MissileBoomYielded`, `HudYielded`, `HudOverlaysYielded`).
  3. Builds a "picture" string of installed mods + SET choices; only when it changes does it `Push` the other
     mods' config entries (`plugin.Config.TryGetEntry(section, key)`), i.e. it writes their `.cfg` files.
  4. Every 5 s `Link` feeds NoAtmoFX's integration API (`NoAtmoFX.NoAtmoFXApi.SetConditions/SetCloudBase/
     ClearWeather`, found by reflection, no DLL reference) with the `IWeatherView` sample at the main camera.
     `Conditions(cover, rain)` maps Boscali cover/rain to NoAtmoFX's 0..1.
- `Unlink` (on link off, `OnApplicationQuit`, `OnDestroy`) calls `ClearWeather` so NoAtmoFX returns to the
  mission weather. `ResetForScene` just zeroes the timers so the next frame re-evaluates.
- Authority: everything is client-local. The NoAtmoFX API itself syncs host to clients inside NoAtmoFX.

## Where to change things

| To change… | Look in |
|---|---|
| Add support for another foreign mod (GUID constant) | `Core/Game/ForeignMods.cs` (`const string` GUIDs) + [CompatBridge](Runtime/CompatBridge.cs) |
| Add a new yield flag | `ForeignMods` field + `ClearYields`, set it in `CompatBridge.Update`, read it in the owning module |
| Which foreign config keys are written | `CompatBridge.Push`, `CompatBridge.Update` (Expansion2082 keys) |
| Weather mapping to NoAtmoFX | `CompatBridge.Conditions`, `CompatBridge.Link` |
| Key-clash rebinding | `CompatBridge.MoveKeys` (NoAtmoFX `ToggleKey`, `MenuKey`, `TestBlastKey`); on the Boscali side only callers of `ForeignMods.KeyDown` (Command `TargetPresetHotkeys`, QoL `ObservationManager`) ignore Ctrl/Alt chords |
| SET rows, labels, gates | [CompatibilityModule](CompatibilityModule.cs) |
| Config defaults / descriptions | [CompatibilitySettings](Configuration/CompatibilitySettings.cs) |
| Which SET tab the sections land on | Command's `SettingsMfdPanel.Board.cs` `ClientSections` table (not this module); a section string missing there falls to the foot of PERF |

## Talks to other modules

- Consumes `IWeatherView` (Weather) via `ModuleServices.TryGet` for the camera-point sky sample; guards for a
  destroyed Unity object (`view is Object lost && lost == null`).
- Provides nothing through `Core/Contracts`. Its output is the static `ForeignMods` yields in Core, read by:
  Weather (`WeatherManager`, `WeatherManager.Visuals`: clouds, sun shafts, lightning, thunder, rain visuals),
  Immersion (`ImmersionManager` canopy frost; `WorldFx` contrails/wake/crack, cockpit muffle, airfield lights,
  missile boom; `ArApproach`, `VisorOverlay`; `ImmersionAutomation` reports them), Hud (`HudBoard`), Core
  (`CockpitOverlayGate`), Wing (`WingHudPanel`, `WingHudTint`), Chatter (`ChatterStrip`), Progression (`AceHuntHud`)
  and Vanguard (`DroneFeedHud`).

## Settings, network, patches

- Config section `Compatibility` (13 keys), all client settings. No host views, no network messages, no patches.
- The foreign config writes go to the other mod's own BepInEx config file and persist after Boscali is removed.
- `MoveKeys` changes the other mod's saved shortcut permanently (there is no undo path).

## Tests, sim hooks and pinned names

- No automation types and nothing pinned by nomodkit in this folder.
- Scenarios `compat-world-fx` and `compat-world-fx-2` (nomodkit `tests/ingame`) call Immersion's
  `ImmersionAutomation.WorldReadout`, which reports the yields as `skyYielded`, `rainYielded`, `worldFxYielded`,
  `airfieldYielded` and `noAtmoFx`.
- Foreign identifiers are external contracts: GUIDs in `ForeignMods`, config section/key strings in `Push`/
  `Update`/`MoveKeys`, and `NoAtmoFX.NoAtmoFXApi` method names. A mismatch is logged and skipped, never thrown.

## Gotchas

- `ForeignMods` yields are process-wide statics; `ClearYields` runs on teardown and `CompatBridge.OnDestroy`.
  A reader must treat them as "possibly stale for up to 0.5 s".
- `Push` only runs when the picture string changes. If the player edits the foreign mod's config by hand while
  the picture is unchanged, Boscali does not overwrite it until something in the picture moves.
- `FindApi` caches its result once per bridge (`apiLooked`); a failing API call nulls `apiConditions`, which
  disables the weather link until the scene service is recreated.
- The `WeatherLink` config description still mentions NoAtmoFX's `ConditionsOverride` cfg and `-1`; the code now
  uses the NoAtmoFX 1.3+ API instead (older NoAtmoFX: link silently off with one log line).
- `Get(..., fallback: true)`: a missing NoAtmoFX/Kelly key counts as "on", so a renamed foreign key yields.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [CompatibilityModule](CompatibilityModule.cs), id `compatibility`, "Compatibility"; depends on nothing; roster: !batchmode; mission profile: core.
- 3 .cs files, 355 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 78 |
| Configuration/ | 1 | 59 |
| Runtime/ | 1 | 218 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/CompatBridge.cs](Runtime/CompatBridge.cs) | 218 |
| [CompatibilityModule.cs](CompatibilityModule.cs) | 78 |
| [Configuration/CompatibilitySettings.cs](Configuration/CompatibilitySettings.cs) | 59 |

### Harmony patches (0)

None.

### Contracts

- Registers as service: none
- Implements: none
- Resolves: `IWeatherView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Compatibility` (13 keys)
- SET client settings: 13; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

None.

### Pinned by nomodkit

0 files compiled by path, 0 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
