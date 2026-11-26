# Effects — Effects (`effects`)

> Client-only, reversible post-processing on top of the game's URP picture: colour grade, .cube LUT, RCAS sharpening, sky dithering, SMAA and an optional "cinematic" look with light streaks.

## Player-facing behaviour

- SET › VISUALS tab, section **COLOUR GRADE**: SKY DITHER, EDGE SMOOTHING (GAME/SMAA), CINEMATIC (+ CINEMA LOOK, CINEMA STRENGTH, LIGHT GLOW, LENS VIGNETTE, FILM GRAIN), COLOUR GRADE (+ SHARPEN, CONTRAST, SATURATION, WARMTH, SHARPNESS). All apply live, no restart.
- COLOUR GRADE: contrast, saturation (via channel mixer) and white balance over the game's own grade.
- Optional `.cube` 3D LUT from `BepInEx/config/BoscaliSummer/luts` (config key `Effects.Lut`, not in the SET UI); file edits reload live.
- SHARPEN: AMD FidelityFX RCAS in URP's final blit (no shader of our own).
- CINEMATIC: selectable **CINEMA LOOK** (`ATMOSPHERIC`, `REALISTIC`, `TACTICAL`) with sun- and weather-reactive `SplitToning`, `ShadowsMidtonesHighlights`, `LiftGammaGain`, `ColorCurves`, exposure-relative wide `Bloom` (night-damped), adjustable `Vignette` and `FilmGrain`, and horizontal lens streaks around bright lights.
- Night vision: the grade volume and sharpening are bypassed while night vision is **active**; the cinematic look and streaks while it is **selected or active**, so thermal/phosphor pictures are never recoloured. SKY DITHER / EDGE SMOOTHING are not bypassed.
- Nothing is networked: each player sees only their own settings.

## How it works

- Entry: [EffectsModule](EffectsModule.cs) `Install` adds one scene service, [DisplayGrade](Runtime/DisplayGrade.cs) (`AddSceneService<DisplayGrade>(49)`), registers it as an `IClientEffect` (`AddClientEffect`) and declares the SET rows. No patches, no network.
- `DisplayGrade` is a `MonoBehaviour` that owns every sub-effect and does all the work in `Update`, throttled to every 0.25 s unscaled (`ApplySeconds`); the night-vision check runs every frame.
  - Grade: creates a global `Volume` "BoscaliSummer.DisplayGrade" at priority **0.75** (above the game's `LevelInfo.PostProcessing` at 0.5, below night vision at 1) on the game volume's layer, with `ColorAdjustments` (contrast only), `WhiteBalance`, `ChannelMixer` (saturation) and `ColorLookup` (LUT).
  - [Sharpening](Runtime/Sharpening.cs): flips the active `UniversalRenderPipelineAsset` to the FSR upscaling filter with overridden sharpness; snapshots and restores the three asset fields.
  - [CameraPostQuality](Runtime/CameraPostQuality.cs): sets `dithering` / SMAA on the **base** camera's `UniversalAdditionalCameraData` (URP14 copies them to overlays), only if the native post-process shaders/textures survived stripping; reads them via reflection on `UniversalRenderer.postProcessPass.m_Data`.
  - [CinematicPost](Runtime/CinematicPost.cs): second global volume "BoscaliSummer.Cinematic" at priority **0.8** (`SplitToning`, `ShadowsMidtonesHighlights`, `LiftGammaGain`, `ColorCurves`, `Bloom`, `Vignette`, `FilmGrain`), values added on top of the native profile's own and shaped by sun elevation, cloud shade, rain and altitude (`IFlightEnvironmentView`); bloom threshold follows the game's exposure (`ExposureController.colorAdjustments`, read by reflection).
  - [CinematicStreakPass](Runtime/CinematicStreakPass.cs): a `ScriptableRenderPass` (AfterRenderingTransparents) bound to the main camera, using the shader `Hidden/Boscali/CinematicStreak` loaded by [CinematicAssets](Runtime/CinematicAssets.cs) from the embedded `BoscaliSummer.Effects.cinematic.bundle` ([Assets/cinematic.bundle](Assets/cinematic.bundle), source in [Assets/Source](Assets/Source/CinematicStreak.shader)).
- Lifecycle: `ResetForScene` (scene-service reset, order 49) and `ReleaseFx`/`OnDisable`/`OnDestroy` restore every pipeline/camera field and destroy volumes, profiles, LUT texture and the asset bundle. All sub-effects are idempotent "apply or restore" each tick.
- Pure logic: [Domain/CubeLut](Domain/CubeLut.cs) parses `.cube` text and bakes a URP LUT strip (engine-free, unit-tested); [Domain/EdgeSmoothing](Domain/EdgeSmoothing.cs) is the GAME/SMAA enum.
- Background notes: [GRAPHICS-RESEARCH.txt](GRAPHICS-RESEARCH.txt), asset provenance in [Assets/PROVENANCE.txt](Assets/PROVENANCE.txt).

## Where to change things

| To change… | Look in |
|---|---|
| SET rows, labels, ranges, `requires:` gating | [EffectsModule.cs](EffectsModule.cs) |
| Config keys / defaults | [Configuration/EffectsSettings.cs](Configuration/EffectsSettings.cs), [Configuration/CinematicSettings.cs](Configuration/CinematicSettings.cs) |
| Grade maths (contrast, saturation mixer, white balance), volume priority, tick rate | [Runtime/DisplayGrade.cs](Runtime/DisplayGrade.cs) |
| LUT parsing / resampling | [Domain/CubeLut.cs](Domain/CubeLut.cs) |
| LUT folder, file reload key | `DisplayGrade.Configure` / `LoadLut` in [Runtime/DisplayGrade.cs](Runtime/DisplayGrade.cs) |
| Sharpening mechanism | [Runtime/Sharpening.cs](Runtime/Sharpening.cs) |
| Dithering / SMAA, which camera is touched | [Runtime/CameraPostQuality.cs](Runtime/CameraPostQuality.cs) |
| Cinematic colour, bloom, vignette, grain | [Runtime/CinematicPost.cs](Runtime/CinematicPost.cs) |
| Lens-streak render pass | [Runtime/CinematicStreakPass.cs](Runtime/CinematicStreakPass.cs), shader in [Assets/Source/CinematicStreak.shader](Assets/Source/CinematicStreak.shader) (rebuild `cinematic.bundle`) |
| Night-vision bypass | `Update` in DisplayGrade, `Apply` in CinematicPost, the render pass in CinematicStreakPass (`GameAccess.TryGetNightVisionState`) |
| FX readout keys (`grade`, `lut`, `sharpen`, `cinematic*`, `dithering`, `edgeSmoothing`) | `DescribeFx` / `Describe` methods in the Runtime files |

## Talks to other modules

- Provides `IClientEffect` (`EffectId` `"display-grade"`) to the Core FX registry for readouts and global release.
- Consumes optional `IRenderTuning` (implemented by the Performance module) through `ModuleServices.TryGet`: `IndependentSharpening` + `SharpenStrength` let sharpening run without COLOUR GRADE, and `OptimizeCinematicStreaks` is passed to the streak pass. Absent service = Effects' own settings only.
- Uses Core helpers only: `GameAccess` (night vision), `FeedCamera.Noise()` (grain texture), `EmbeddedResources`.

## Settings, network, patches

- All settings are client-only (section `Effects`); no host settings, no messages, no Harmony patches.
- `Effects.Lut` and `Effects.LutStrength` exist only in the config file, not on the SET page.
- Reflection targets that may break on a game/URP update: `ExposureController.colorAdjustments` (static field), `UniversalRenderer.postProcessPass` and its `m_Data`. Failures degrade to "no effect" rather than exceptions (`CameraPostQuality` logs a warning when dithering/SMAA resources are missing).

## Tests, sim hooks and pinned names

- No `*Automation` hook types in this module.
- nomodkit compiles [Domain/CubeLut.cs](Domain/CubeLut.cs) and [Domain/EdgeSmoothing.cs](Domain/EdgeSmoothing.cs) by path (tests/BoscaliSummer.Tests, `CubeLutTests`); `Run-CameraPostUnityCheck.ps1` compiles [Runtime/CameraPostQuality.cs](Runtime/CameraPostQuality.cs) by path. Unity checks (`CinematicUnityCheck`, `CameraPostUnityCheck`, `tools/CinematicAssets/CinematicAssetsUnityCheck`) import the `Runtime` / `Configuration` / `Domain` namespaces.
- Referenced by full name: `BoscaliSummer.Modules.Effects.Runtime.DisplayGrade` (`tests/RenderTuningCheck/RenderSharpeningUnityCheck.cs` via `Type.GetType`) and `...Runtime.CameraPostQuality.Live.CameraData.*` (in-game scenario `mods/boscalisummer/tests/ingame/post-quality.json`). Keep the `Live` static, `CameraData` property, namespaces and file locations.
- Embedded resource name `BoscaliSummer.Effects.cinematic.bundle` and shader name `Hidden/Boscali/CinematicStreak` are matched by string.
- Scenarios: `nomod sim ls post --mod boscalisummer` → `post-quality` (dithering/SMAA); `nomod sim ls cinematic --mod boscalisummer` → `agx-cinematic` (stale, see Gotchas) and Cinematography's `cinematic-film`.

## Gotchas

- Never override `postExposure` or `ColorAdjustments.saturation`: the game's `ExposureController` and G-LOC own them. Saturation goes through the channel mixer for that reason; CinematicPost deliberately adds no `ColorAdjustments`.
- Every change to shared URP state (pipeline asset FSR fields, camera dithering/AA) must snapshot first and restore on toggle-off, scene reset, quality-level asset swap and dispose; `Sharpening.Apply` hands back the old asset when the quality level swaps it.
- Volume priorities are ordered on purpose: game 0.5 < grade 0.75 < cinematic 0.8 < night vision 1. New volumes must sit below 1.
- SMAA/dithering go on the base camera, not the final overlay (URP14 copies base fields into overlays); `PostCamera` only finds the overlay to check resources.
- Bloom threshold must track exposure, otherwise the whole daylight sky blooms at the game's negative EV.
- nomodkit still names `BoscaliSummer.Modules.Effects.Patches` (AgxUnityCheck.cs) and `...Runtime.AgxTonemapping` (tests/ingame/agx-cinematic.json); neither exists in this repo any more, so those nomodkit checks are stale, not something to recreate.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [EffectsModule](EffectsModule.cs), id `effects`, "Effects"; depends on nothing; roster: !batchmode; mission profile: optional.
- 11 .cs files, 1230 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 56 |
| Configuration/ | 2 | 76 |
| Domain/ | 2 | 81 |
| Runtime/ | 6 | 1017 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/CinematicPost.cs](Runtime/CinematicPost.cs) | 361 |
| [Runtime/DisplayGrade.cs](Runtime/DisplayGrade.cs) | 236 |
| [Runtime/CameraPostQuality.cs](Runtime/CameraPostQuality.cs) | 168 |
| [Runtime/CinematicStreakPass.cs](Runtime/CinematicStreakPass.cs) | 143 |
| [Domain/CubeLut.cs](Domain/CubeLut.cs) | 77 |

### Harmony patches (0)

None.

### Contracts

- Registers as service: none
- Implements: `IClientEffect`
- Resolves: `IFlightEnvironmentView`, `IRenderTuning`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Effects` (16 keys)
- SET client settings: 14; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

None.

### Pinned by nomodkit

2 files compiled by path, 2 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
