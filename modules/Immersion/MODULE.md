# Immersion — Immersion (`immersion`)

> Client-only cosmetic immersion: cockpit camera motion, pilot body and kneeboard, helmet visor, cockpit audio and alarms, and world effects (contrails, lights, vapour, vegetation dressing).

## Player-facing behaviour

- SET › **VISUALS** tab (section → tab table in Command's [SettingsMfdPanel.Board.cs](../Command/Presentation/MapUi/SettingsMfdPanel.Board.cs)): sections IMMERSION (master + COMFORT MOTION), COCKPIT MOTION, COCKPIT VISUALS, COCKPIT AUDIO, WORLD FX, and GROUND (FOREST VARIETY, GROUND VEGETATION, shared with Weather's TERRAIN DETAIL). Every row requires IMMERSION MASTER, directly or through a parent row (HEAD STRENGTH → HEAD MOTION, PILOT MODEL → PILOT BODY, VISOR CURVE / REFLECTIONS → HELMET VISOR). The "SET > COCKPIT" comment in `ImmersionModule.Install` is stale.
- Cockpit view: G-load head inertia, breathing, gun/touchdown/Mach-buffet shake, near-miss crack, canopy-muffled explosions.
- Visuals: sun glare, MFD night glow + cockpit lights, helmet visor overlay (tint, fog, scratches, AR approach tunnel), canopy frost, damage shading, first-person pilot body with 4 suit models, windscreen pilot reflection, thigh kneeboard with picture gallery.
- Audio: airframe creaks, canopy wind, pilot breathing/AGSM, modern "soft" alarm tones replacing vanilla RWR/missile/stall/RPM/VRS/UI sounds, voice warnings (PULL UP, TERRAIN, OVER G, BINGO FUEL) in every view.
- World: contrails above ~7.5 km, dust wake, airfield and city night lights (off after an SA-74N burst), vapour cones/ribbons, sky optics (halo, rainbow, glory, meteors), missile sonic boom, quiet parked AI, forest variety and ground vegetation.
- Nothing is networked; every effect is local to this client.

## How it works

- Entry: [ImmersionModule](ImmersionModule.cs) `Install` adds three scene services and declares all SET rows:
  - [ImmersionManager](Runtime/ImmersionManager.cs) (47) — cockpit presentation; also `IClientEffect` "immersion" and `IImmersionSettings`;
  - [CockpitPilot](Runtime/CockpitPilot.cs) (48) — first-person body, reflection, kneeboard; `IClientEffect`; gets a [KneeboardGallery](Visuals/KneeboardGallery.cs) registered as `IKneeboardGallery`;
  - [WorldFx](Runtime/WorldFx.cs) (49) — world-side effects; `IClientEffect` "world-fx".
  - Sets static `SoftTones.Master/Soft` ([Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs)) to the config entries.
- `ImmersionManager.Update` binds to the followed aircraft + main camera only in cockpit camera mode (`CameraStateManager.cameraMode == cockpit` and the cockpit camera state is current), otherwise resets. `TickPresentation` reads [PilotVitals](Runtime/PilotVitals.cs), steps [PilotPhysiology](Domain/PilotPhysiology.cs) → `PilotCue`, [HeadMotion](Runtime/HeadMotion.cs) (measured in `FixedUpdate`), [CockpitShake](Runtime/CockpitShake.cs), composes the head offset via [ImmersionMath](Domain/ImmersionMath.cs), then ticks glare, visor, frost, MFD glow, cockpit lights, surface damage and the Audio/ components. Voice warnings ([CockpitWarnings](Audio/CockpitWarnings.cs)) tick for the local aircraft in any view.
- The head offset is applied by [CockpitHeadRotationPatch](Patches/CockpitCameraPatches.cs) (postfix on `CameraCockpitState.UpdateState`) via `ApplyCameraOffset` and removed on `LeaveState`; [CockpitCameraOffset](Runtime/CockpitCameraOffset.cs) removes only its own write so native free look/head tracking stay authoritative.
- Weather input: `IFlightEnvironmentView` snapshot (wind, temperature, turbulence, cloud) when it matches this frame/aircraft/camera; without Weather, fallbacks (standard-atmosphere speed of sound, `LevelInfo` cloud occlusion for glare, 15 °C for the visor, no canopy frost, humidity 0.4 in `WorldFx`).
- `CockpitPilot` (`[DefaultExecutionOrder(200)]`, after Weather at 100) hides the native pilot and drives [CockpitPilotRig](Visuals/CockpitPilotRig.cs) on the local aircraft only; [Kneeboard](Visuals/Kneeboard.cs) renders [KneeboardCard](Domain/KneeboardCard.cs) text and gallery images ([Assets/Kneeboard](Assets/Kneeboard/)); [PilotShaderBundle](Visuals/PilotShaderBundle.cs) loads `pilot.bundle` / `pilot-models.bundle`.
- `WorldFx` (`[DefaultExecutionOrder(9)]`) ticks [Contrails](Visuals/Contrails.cs), [DustWake](Visuals/DustWake.cs), [AirfieldLights](Visuals/AirfieldLights.cs), [CityLights](Visuals/CityLights.cs), [ArApproach](Visuals/ArApproach.cs), [Vapour](Visuals/Vapour.cs), [SkyOptics](Visuals/SkyOptics.cs), [MissileBoom](Audio/MissileBoom.cs), [VegetationDressing](Visuals/VegetationDressing.cs), and in `LateUpdate` [WorldDetailDressing](Visuals/WorldDetailDressing.cs) (instanced props from `world-detail.bundle`). It publishes static `CrackArmed`/`MuffleArmed`/`ParkedArmed` flags read by [Patches/WorldFxPatches.cs](Patches/WorldFxPatches.cs).
- Pure maths in [Domain/](Domain/ImmersionMath.cs) (and [Audio/CanopyWindMath.cs](Audio/CanopyWindMath.cs)) is Unity-free and unit-tested by nomodkit.

## Where to change things

| To change… | Look in |
|---|---|
| SET rows, sections, presets, `requires:` chain | [ImmersionModule.cs](ImmersionModule.cs) |
| Config keys/defaults | [Configuration/ImmersionSettings.cs](Configuration/ImmersionSettings.cs) |
| G-load physiology (greyout, breathing, cues) | [Domain/PilotPhysiology.cs](Domain/PilotPhysiology.cs), [Runtime/PilotVitals.cs](Runtime/PilotVitals.cs) |
| Head inertia / comfort clamp | [Runtime/HeadMotion.cs](Runtime/HeadMotion.cs), [Domain/ImmersionMath.cs](Domain/ImmersionMath.cs), [Domain/MotionEnvelope.cs](Domain/MotionEnvelope.cs) |
| Shake (gun, touchdown, Mach buffet) | [Runtime/CockpitShake.cs](Runtime/CockpitShake.cs) |
| Helmet visor look | [Visuals/VisorOverlay.cs](Visuals/VisorOverlay.cs), [Domain/VisorMath.cs](Domain/VisorMath.cs), [Domain/HelmetMath.cs](Domain/HelmetMath.cs) |
| Pilot body, hands on controls, suit models | [Visuals/CockpitPilotRig.cs](Visuals/CockpitPilotRig.cs), [Runtime/CockpitPilot.cs](Runtime/CockpitPilot.cs), [Domain/PilotPoseMath.cs](Domain/PilotPoseMath.cs) |
| Kneeboard card text / gallery pictures | [Domain/KneeboardCard.cs](Domain/KneeboardCard.cs), [Visuals/Kneeboard.cs](Visuals/Kneeboard.cs), [Visuals/KneeboardGallery.cs](Visuals/KneeboardGallery.cs), [Assets/Kneeboard](Assets/Kneeboard/) |
| Replacement alarm/UI tones | [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs) |
| Voice warnings | [Audio/CockpitWarnings.cs](Audio/CockpitWarnings.cs), [Domain/WarningMath.cs](Domain/WarningMath.cs) |
| Cockpit sounds (wind, creaks, breathing, foley) | [Audio/](Audio/CanopyWindAudio.cs) |
| Night lights on airfields/towns | [Visuals/AirfieldLights.cs](Visuals/AirfieldLights.cs), [Visuals/CityLights.cs](Visuals/CityLights.cs), [Visuals/LightPoints.cs](Visuals/LightPoints.cs) |
| Contrails, dust, vapour, sky optics | the matching file in [Visuals/](Visuals/Contrails.cs) |
| What WorldFx ticks, its `*Armed` patch gates, grid-down after SA-74N | [Runtime/WorldFx.cs](Runtime/WorldFx.cs) |
| Head-offset application to the cockpit camera | [Patches/CockpitCameraPatches.cs](Patches/CockpitCameraPatches.cs), [Runtime/CockpitCameraOffset.cs](Runtime/CockpitCameraOffset.cs), `ApplyCameraOffset` in [Runtime/ImmersionManager.cs](Runtime/ImmersionManager.cs) |
| Vegetation / ground props | [Visuals/VegetationDressing.cs](Visuals/VegetationDressing.cs), [Visuals/ForestClumpArt.cs](Visuals/ForestClumpArt.cs), [Visuals/WorldDetail*.cs](Visuals/WorldDetailDressing.cs), [Domain/VegetationRules.cs](Domain/VegetationRules.cs) |

## Talks to other modules

- Provides `IImmersionSettings` (no consumer outside Immersion today) and `IKneeboardGallery` (Progression's SQD STUDIO `CARD` stepper and Autopilot's menu game change `Selected`; the gallery's change callback refreshes the kneeboard and plays a page flip via `ImmersionManager.PageFlip`).
- Consumes Weather's `IFlightEnvironmentView` (cockpit air), `IWeatherView` (sky optics rain/cloud samples) and `ICanopyGlassView` (registered by Core in [BoscaliMod.cs](../../Core/BoscaliMod.cs), implemented in Weather) — all optional with fallbacks.
- Reads Core flags: `ForeignMods.*Yielded` (Compatibility hands features to other mods), `RadioAftermath.Active` (grid down after SA-74N), `VisorEvents.Hit` (raised here, consumed by Hud's visor glitch), `NOAvionics.AvFxDriver.Kick` on hits.

## Settings, network, patches

- All 37 `Immersion` keys are client-only; nothing is host-authoritative and there are no messages.
- `PlayerHitPatch` targets the Mirage-generated `Aircraft.UserCode_RpcDamage_<hash>` by prefix scan (hash changes per game build) and reads `DamageInfo` by position (`__1`). `ParkedNavLightPatch` also resolves its target dynamically (`TargetMethod`). Both skip cleanly when not found.
- `Bullet.TrajectoryTrace` is shared with FireAndDestruction; `CameraCockpitState.UpdateState` with Hud.
- Patch fast paths: `ImmersionManager.IsCockpitActive` + `ReferenceEquals(..., FollowingUnit)` and WorldFx's `*Armed` statics. Keep new patch logic behind such a static check — they fire for every gun shot / bullet trace in the mission.

## Tests, sim hooks and pinned names

- Sim hook (dev builds): `BoscaliSummer.Modules.Immersion.Runtime.ImmersionAutomation` (PilotSnapshot, Readout, WorldReadout, SetEnabled, Look, Configure, AudioCensus, InjectG, InjectHit), ~25 nomodkit references.
- Also named by nomodkit: `ImmersionModule`, `Configuration.ImmersionSettings`, `Runtime.ImmersionManager`, `Visuals.MaterialSlotClone`.
- nomodkit compiles by path: all 11 files in [Domain/](Domain/ImmersionMath.cs) plus [Audio/CanopyWindMath.cs](Audio/CanopyWindMath.cs). Keep them Unity-free and in place.
- Embedded names: `BoscaliSummer.Immersion.pilot.bundle`, `BoscaliSummer.Immersion.pilot-models.bundle`, `BoscaliSummer.Immersion.Kneeboard.<file>.png`, `BoscaliSummer.WorldDetail.bundle`, `BoscaliSummer.WorldDetail.png`; GameObjects `BoscaliSummer.SunGlare`, `BoscaliSummer.SunGlare.Rays`.
- Scenarios: `nomod sim ls immersion --mod boscalisummer` (pilot-immersion); others by name above.

## Gotchas

- Execution order matters: `WorldFx` at 9 (after native camera shift at 2 and detail processing at 8), `CockpitPilot` at 200 (after Weather at 100). Moving ticks around desyncs bodies/lights from the camera.
- `ImmersionManager.ResetForScene` is also the "unbind" path (called whenever the aircraft or camera changes, on leaving cockpit, or on disabling); it must stay cheap and idempotent.
- The pilot body only replaces the **local** player's pilot (`CockpitPilot.IsLocalAircraft`), not a spectated aircraft; head motion works for any followed aircraft in cockpit view.
- Several pieces read private game fields by reflection (`Pilot.skinnedMeshRenderer`, `Pilot.animator`, `Cockpit.joysticks/throttles` and their inner types) — fragile on game updates.
- nomodkit's `mods/boscalisummer/tools/WorldDetailAssets/BuildWorldDetailAssets.cs` names `BoscaliSummer.Modules.Immersion.Visuals.WorldDetailMeshCheck.Run`, which does not exist in this repo (stale reference).
- Documented simplifications ("ponytail" comments): contrails use an altitude band only, city lights never switch off late at night, dust wake has one tint, pilot reflection uses one plane.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [ImmersionModule](ImmersionModule.cs), id `immersion`, "Immersion"; depends on nothing; roster: !batchmode; mission profile: optional.
- 60 .cs files, 9760 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 170 |
| Audio/ | 8 | 825 |
| Configuration/ | 1 | 154 |
| Domain/ | 11 | 793 |
| Patches/ | 4 | 440 |
| Runtime/ | 8 | 1766 |
| Visuals/ | 27 | 5612 |

### Largest files

| File | Lines |
|---|---|
| [Visuals/CockpitPilotRig.cs](Visuals/CockpitPilotRig.cs) | 647 |
| [Visuals/Kneeboard.cs](Visuals/Kneeboard.cs) | 576 |
| [Runtime/ImmersionManager.cs](Runtime/ImmersionManager.cs) | 520 |
| [Runtime/CockpitPilot.cs](Runtime/CockpitPilot.cs) | 421 |
| [Visuals/AirfieldLights.cs](Visuals/AirfieldLights.cs) | 322 |

### Harmony patches (20)

- `Aircraft.UserCode_RpcDamage_*` by `PlayerHitPatch` in [Patches/CockpitCameraPatches.cs](Patches/CockpitCameraPatches.cs) (TargetMethod)
- `AoADisplay.Initialize` by `StallHornPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs)
- `BlastManager.AddBlast` by `WorldDetailBlastPatch` in [Patches/WorldDetailBlastPatch.cs](Patches/WorldDetailBlastPatch.cs)
- `Bullet.TrajectoryTrace` by `NearMissCrackPatch` in [Patches/WorldFxPatches.cs](Patches/WorldFxPatches.cs) — SHARED with FireAndDestruction (INDEPENDENT)
- `CameraCockpitState.LeaveState` by `CockpitHeadResetPatch` in [Patches/CockpitCameraPatches.cs](Patches/CockpitCameraPatches.cs)
- `CameraCockpitState.UpdateState` by `CockpitHeadRotationPatch` in [Patches/CockpitCameraPatches.cs](Patches/CockpitCameraPatches.cs) — SHARED with Hud (ORDER-SENSITIVE: immersion → hud)
- `Gun.SpawnBullet` by `GunShotShakePatch` in [Patches/CockpitCameraPatches.cs](Patches/CockpitCameraPatches.cs)
- `ManagedExplosion.Play` by `ExplosionMufflePatch` in [Patches/WorldFxPatches.cs](Patches/WorldFxPatches.cs)
- `MissileAlarm.Setup` by `MissileAlarmSoftPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs) (TargetMethod)
- `PilotParkedState.EnterState` by `ParkedEnterPatch` in [Patches/WorldFxPatches.cs](Patches/WorldFxPatches.cs)
- `PilotParkedState.LeaveState` by `ParkedLeavePatch` in [Patches/WorldFxPatches.cs](Patches/WorldFxPatches.cs)
- `RPMGauge.Initialize` by `RpmWarningSoftPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs)
- `RadarWarning.Start` by `RadarToneCapturePatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs)
- `SoundManager.PlayInterface` by `InterfaceSoftPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs) (TargetMethod)
- `SoundManager.PlayInterfaceOneShot` by `InterfaceSoftPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs) (TargetMethod)
- `SoundManager.PlayMenu` by `InterfaceSoftPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs) (TargetMethod)
- `SoundManager.PlayMenuOneShot` by `InterfaceSoftPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs) (TargetMethod)
- `SoundManager.PlayRadarWarningOneShot` by `RadarToneSwapPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs)
- `VRSWarning.Initialize` by `VrsWarningSoftPatch` in [Patches/SoftTonePatches.cs](Patches/SoftTonePatches.cs)
- `dynamic` by `ParkedNavLightPatch` in [Patches/WorldFxPatches.cs](Patches/WorldFxPatches.cs) (TargetMethod)

### Contracts

- Registers as service: `IImmersionSettings`, `IKneeboardGallery`
- Implements: `IClientEffect`, `IImmersionSettings`, `IKneeboardGallery`
- Resolves: `ICanopyGlassView`, `IFlightEnvironmentView`, `IKneeboardGallery`, `IWeatherView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Immersion` (37 keys)
- SET client settings: 36; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Immersion.Runtime.ImmersionAutomation` (dev)

### Pinned by nomodkit

12 files compiled by path, 5 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
