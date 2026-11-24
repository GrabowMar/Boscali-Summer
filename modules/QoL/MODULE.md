# QoL — Quality of life (`qol`)

> Client-local flight aids: a restyled night-vision picture (white phosphor / fused thermal contours with metered tube gain) and camera marks — a local map observation of whatever the native camera looks at, offered to Support as `IObservationSource`.

## Player-facing behaviour

- SET → **FLIGHT AIDS**: **NIGHT VISION** choice (GAME / WHITE / FUSED; config default `Fused`) and **CAMERA MARKS** toggle. **MARK KEY** (default F8) is a client key binding (`AddClientKey(..., onPrompt: false)`, so not on the mission prompt's CONTROLS card).
- Night vision (game's N key unchanged): WHITE = mint-white phosphor, fine grain, protected highlights, adaptive gain that gates on bright sources; FUSED adds amber contours on hot units. GAME leaves vanilla untouched.
- Goggle feel: flip-down opens the picture from the middle, flip-up blink, scintillating specks while on (overlay above the HUD).
- A camera change no longer turns the player's night-vision choice off.
- MARK KEY (or TGT CAMERA) marks the surface at the centre of the live native camera as one local point for 120 s; Support's "CALL AT MARK" uses it.
- The module is not installed in batch mode (roster `QoL.Enabled && !batchmode`).

## How it works

- [QoLModule](QoLModule.cs) `Install`: adds [ObservationManager](Runtime/ObservationManager.cs) (scene service 54, registered as `IObservationSource`) and [NightVisionLook](Visuals/NightVisionLook.cs) (scene service 55), and registers three SET client entries via `AddClientChoice`/`AddClientSetting`/`AddClientKey`.
- Camera marks: `ObservationManager.Update` polls the key (`ForeignMods.KeyDown`, ignoring input fields and pause), `Capture` raycasts from `NativeCamera.TryGet` (≤ 60 km, ignores own aircraft), stores one point in [ObservationStore](Runtime/ObservationStore.cs) (validated, 120 s lifetime). `TryGet` clears the mark when the ownship, view, faction or ejection state changes.
- Night vision: `NightVisionLook` (static `Live`) drives the overrides of vanilla's night-vision Volume (colour, bloom, grain, vignette) while goggles are on, runs [NvgPass](Visuals/NvgPass.cs) (URP pass on the world camera: phosphor, depth-tested hot silhouettes, 16×16 luminance readback 10×/s), scans hot units every 0.25 s (≤ 40, ≤ 12 km), feeds [NvgGain](Domain/NvgGain.cs) (pure gain/gating model), and shows [NvgOverlay](Visuals/NvgOverlay.cs).
- Shaders come from the embedded `nightvision.bundle` ([NvgShaderBundle](Visuals/NvgShaderBundle.cs), resource `BoscaliSummer.QoL.nightvision.bundle`); sources in [Assets/Source](Assets/Source). Built by nomodkit `mods/boscalisummer/tools/Compile-NightVisionAssets.ps1`.
- Gain ownership/state is shared through Core's static `NightVisionLink` (`OwnsGain`, `Gating`, `GainStep`, `Legend`).
- No host/client split: everything is local presentation; the mark is local to the pilot.

## Where to change things

| To change… | Look in |
|---|---|
| Config keys/defaults | [Configuration/QoLSettings.cs](Configuration/QoLSettings.cs) |
| SET rows (labels, help) | [QoLModule.cs](QoLModule.cs) |
| Camera-mark capture rules/raycast | [Runtime/ObservationManager.cs](Runtime/ObservationManager.cs) |
| Mark lifetime/validation | [Runtime/ObservationStore.cs](Runtime/ObservationStore.cs) |
| NVG tuning (target luminance, boost, grain, colours) | statics/constants at the top of [Visuals/NightVisionLook.cs](Visuals/NightVisionLook.cs) |
| Gain/gating behaviour | [Domain/NvgGain.cs](Domain/NvgGain.cs) |
| Render pass, hot-unit contours, metering | [Visuals/NvgPass.cs](Visuals/NvgPass.cs) |
| Goggle flip/scintillation overlay | [Visuals/NvgOverlay.cs](Visuals/NvgOverlay.cs) |
| Shaders | [Assets/Source](Assets/Source) + rebuild `Assets/nightvision.bundle` |
| Vanilla NVG gain/camera-switch behaviour | [Patches/NightVisionGainPatch.cs](Patches/NightVisionGainPatch.cs), [Patches/NightVisionChoicePatch.cs](Patches/NightVisionChoicePatch.cs) |

## Talks to other modules

- Provides [IObservationSource](../../Core/Contracts/IObservationSource.cs) (`CanCapture`, `Capture`, `TryGet`, `Clear`) — consumed by Support (`SupportModule`, `CallsController`, `SupportManager`) for "CALL AT MARK".
- Writes Core `NightVisionLink` statics; Hud (`HudBoard`) shows its `Legend`, and Autopilot's cockpit menu (`MenuGame.Aircraft.cs`) reads `OwnsGain`/`GainStep` and binds `view/nvg-gain` to `NightVisionLink.CycleGain`.
- Uses Core `NativeCamera`, `GameAccess` night-vision helpers.

## Settings, network, patches

- `[QoL]` keys are client-local (3 SET client settings); `Enabled` is a roster switch.
- No network.
- `NightVisionChoicePatch` returns `false` unconditionally from `NightVision.NightVis_OnSwitchCam` — vanilla's camera-switch handler never runs while the module is installed, even with the GAME look.
- `NightVisionGainPatch` skips `NightVision.UpdateGain` only while `NightVisionLink.OwnsGain` is true.
- Both targets are private game methods named by string; a rename in a game update breaks patching.

## Tests, sim hooks and pinned names

- Sim hook (dev only): [NightVisionAutomation](Runtime/NightVisionAutomation.cs) `Set` (`on`, `look`, `gain`) / `Readout` — scenarios `night-vision`, `night-dark`.
- PatchProbe names `BoscaliSummer.Modules.QoL.QoLModule`, `…Patches.NightVisionChoicePatch`, `…Patches.NightVisionGainPatch`.
- nomodkit `tests/BoscaliSummer.Tests` compiles [Domain/NvgGain.cs](Domain/NvgGain.cs) and [Runtime/ObservationStore.cs](Runtime/ObservationStore.cs) by path; keep both free of Unity-scene dependencies.
- `nomod sim ls night --mod boscalisummer`.

## Gotchas

- The night-vision look fails closed: without shaders (bundle missing/unsupported) it falls back to a white tint on vanilla's own gain.
- `NightVisionLook` saves vanilla Volume override values (`saved*` fields) and writes them back when it stops owning the picture; touching a new override means extending that save/restore too.
- Look tuning values (`TargetLuminance`, `MaxBoost`, `HeatBrightness`, `SignalGrain`, …) are `internal static` (not const) on purpose: they are tuned live through the dev bridge. Scan limits (`ScanInterval`, `HeatRange`, `MaxHotUnits`) are consts.
- The embedded bundle is conditional in the csproj (`Condition="Exists(...)"`): a missing bundle builds fine and silently disables the custom look.
- `ObservationManager.TryGet` distinguishes a destroyed owner (`owner == null` but not `ReferenceEquals(null)`) from no mark — Unity null semantics.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [QoLModule](QoLModule.cs), id `qol`, "Quality of life"; depends on nothing; roster: QoL.Enabled && !batchmode; mission profile: optional.
- 12 .cs files, 1050 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 34 |
| Configuration/ | 1 | 26 |
| Domain/ | 1 | 60 |
| Patches/ | 2 | 23 |
| Runtime/ | 3 | 201 |
| Visuals/ | 4 | 706 |

### Largest files

| File | Lines |
|---|---|
| [Visuals/NightVisionLook.cs](Visuals/NightVisionLook.cs) | 298 |
| [Visuals/NvgPass.cs](Visuals/NvgPass.cs) | 202 |
| [Visuals/NvgOverlay.cs](Visuals/NvgOverlay.cs) | 163 |
| [Runtime/ObservationManager.cs](Runtime/ObservationManager.cs) | 128 |
| [Domain/NvgGain.cs](Domain/NvgGain.cs) | 60 |

### Harmony patches (2)

- `NightVision.NightVis_OnSwitchCam` by `NightVisionChoicePatch` in [Patches/NightVisionChoicePatch.cs](Patches/NightVisionChoicePatch.cs)
- `NightVision.UpdateGain` by `NightVisionGainPatch` in [Patches/NightVisionGainPatch.cs](Patches/NightVisionGainPatch.cs)

### Contracts

- Registers as service: `IObservationSource`
- Implements: `IObservationSource`
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `QoL` (4 keys)
- SET client settings: 3; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.QoL.Runtime.NightVisionAutomation` (dev)

### Pinned by nomodkit

2 files compiled by path, 4 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
