# Scenario — Mission scripts, dialogue and campaigns (`scenario`)

> Turns `.scenario` text scripts into missions in the game's picker (patched copies of an installed base mission) and plays each script's beats — dialogue, cards, hints, video, music, choices, orders — for the local pilot.

## Player-facing behaviour

- Scripted missions appear in the game's mission picker (inside the single "Boscali Summer" tab), one group per script `group`; campaign chapters unlock from remembered flags.
- Built-in script: [Assets/prologue-tassara-job.scenario](Assets/prologue-tassara-job.scenario) with recorded voice lines (`pro-*.mp3`), intro/heli/outro videos and music.
- During the mission: radio lines go to the COMMS strip (Chatter) or a fallback transmission plate; title cards, hints (also a HUD "task" notice), dips to black, countdown and an ace health bar on an overlay canvas.
- Interaction: pick a side with 1/2 when the script asks (`choose`), answer replies with Y/1 (paragon) or N/2 (renegade; timeout = paragon), skip videos with Space/Enter/Esc.
- Players can add or override scripts as `BepInEx/config/BoscaliSummer/scenarios/*.scenario` (re-read on change; same id replaces a built-in).
- `-mission scenario:<id>` on the command line launches a scripted mission (dedicated servers / auto-host).
- `[Scenario] TextSpeed` scales how long lines stay up.

## How it works

- [ScenarioModule](ScenarioModule.cs) `Install`: builds [ScenarioLibrary](Runtime/ScenarioLibrary.cs) (embedded `BoscaliSummer.Scenario.*.scenario` + user folder + `progress.txt`) and [ScenarioMissions](Runtime/ScenarioMissions.cs); hooks Core's `BoscaliMission.ExtraGroups`, `ScriptedModulesOff` (a script's `without` list switches modules off; ignored when a Gamemodes `PresetModulesOff` preset applies), `ScriptedPeace` (`FactionPeace`), and `LaunchMissionPatch.Resolve`; undoes all of it in `OnTeardown`. In batch mode it stops there; otherwise adds [ScenarioHost](Runtime/ScenarioHost.cs) as scene service (order 95).
- Parsing/running lives in Core: `Core/Scenario` ([ScenarioParser](../../Core/Scenario/ScenarioParser.cs), [ScenarioModel](../../Core/Scenario/ScenarioModel.cs), [ScenarioRunner](../../Core/Scenario/ScenarioRunner.cs), [ScenarioProgress](../../Core/Scenario/ScenarioProgress.cs), [FactionPeace](../../Core/Scenario/FactionPeace.cs)). This module is the Unity side.
- World changes are not done at runtime: [MissionPatcher](Runtime/MissionPatcher.cs) writes the script's world statements (units, objectives, outcomes) into a copy of the base mission JSON, so the game runs and networks them. `ScenarioMissions` builds that mission on demand from the installed base mission.
- `ScenarioHost` (MonoBehaviour, `IScenarioStage` + `IScenarioWorld` for the runner): subscribes to `MissionManager.onObjectiveStarted/Complete` from `MissionProfile.Preparing` (events before binding queue in `early`), binds when `MissionManager.CurrentMission` changes, raises `Start` 1.5 s after the gameplay UI exists, watches the local pilot (joined/aircraft/airborne/landed), ticks the runner, preloads all voice recordings asynchronously (UniTask), owns music and the radio AudioSource.
- [ScenarioStageView](Presentation/ScenarioStageView.cs) (+ `.Combat.cs`, `.Reply.cs`): overlay canvas (sorting order 30004), no input.
- [ScenarioOrders](Runtime/ScenarioOrders.cs) (host only): keeps scripted AI aircraft flying (orbit/formation/circle) by holding the AI's land timer and writing its destination via reflection.
- Authority: story presentation is per client; `Sky`, `Clock`, `Orders` (flight orders), `Seat`, `Leave` and objective start/complete/cancel (`Objectives`) act only on the server; objectives are the game's own (already networked).

## Where to change things

| To change… | Look in |
|---|---|
| Script language (statements, beats, triggers) | `Core/Scenario/ScenarioParser.cs`, `ScenarioModel.cs`, `ScenarioRunner.cs` (not this folder) |
| What a beat action does on screen/audio | matching method on [ScenarioHost](Runtime/ScenarioHost.cs) (`Say`, `Card`, `Hint`, `Video`, `Music`, `Seat`, `Enlist`, `Reply`, `Ace`, `Countdown`, `Sky`, `Clock`) |
| How world statements become mission JSON | [Runtime/MissionPatcher.cs](Runtime/MissionPatcher.cs) |
| Picker groups, mission build, campaign unlock | [Runtime/ScenarioMissions.cs](Runtime/ScenarioMissions.cs) |
| Script sources, user folder, progress file | [Runtime/ScenarioLibrary.cs](Runtime/ScenarioLibrary.cs) |
| Scripted AI flight behaviour | [Runtime/ScenarioOrders.cs](Runtime/ScenarioOrders.cs) |
| Overlay visuals (plate, cards, hints, video) | [Presentation/ScenarioStageView.cs](Presentation/ScenarioStageView.cs) |
| Countdown / ace bar | [Presentation/ScenarioStageView.Combat.cs](Presentation/ScenarioStageView.Combat.cs) |
| Reply prompt | [Presentation/ScenarioStageView.Reply.cs](Presentation/ScenarioStageView.Reply.cs) |
| Prologue content | [Assets/prologue-tassara-job.scenario](Assets/prologue-tassara-job.scenario) + `Assets/pro-*` |
| `-mission scenario:<id>` launch | [Patches/LaunchMissionPatch.cs](Patches/LaunchMissionPatch.cs) |
| Text speed setting | [Configuration/ScenarioSettings.cs](Configuration/ScenarioSettings.cs) |

## Talks to other modules

- Resolves `IChatter` (Core/Chatter): `Scripted(line)` airs dialogue on COMMS, `EndScripted()` on unbind; falls back to its own plate when Chatter is absent or busy.
- Resolves `IHudBoard` to post hints as a `task` notice.
- Resolves `IWeatherDirector` for `Sky` (`Hold`/`Resume`); without it writes `LevelInfo.Networkconditions` directly.
- Feeds Core `BoscaliMission` hooks (`ExtraGroups`, `ScriptedModulesOff`, `ScriptedPeace`) consumed by the mission profile / module roster.

## Settings, network, patches

- Only `[Scenario] TextSpeed` (client-local).
- No mod messages: everything that must be shared is baked into the mission JSON and run by the game.
- `LaunchMissionPatch` shares `CommandLineArgParser.LoadMission` with Gamemodes and Maps; it only claims names starting `scenario:`.
- `StopObjectivePatch` (prefix on private `MissionRunner.RemoveActiveObjective`) avoids a vanilla `KeyNotFoundException` when a script stops an objective whose faction never had one running; it uses `FieldRefAccess("activeByFaction")` — fragile on game updates.

## Tests, sim hooks and pinned names

- Sim hook (dev only): [ScenarioAutomation](Runtime/ScenarioAutomation.cs) (`State`, `Audio`, `World`, `Complete`, `Disable`, `Survey`; the scenarios call `State`, `World`, `Complete`, `Disable`) — scenarios `scenario-orders-mini` (script in nomodkit `tests/ingame/scenario-orders-mini.scenario`) and `scenario-prologue-tassara-job`.
- `scenario-prologue-tassara-job` reflects `ScenarioHost.Instance.linesSaid`, `ScenarioHost.Instance.runner.Finished` and `runner.Flags.Count`: do not rename `Instance`, `linesSaid`, `runner` (or `ScenarioRunner.Finished`/`Flags`).
- PatchProbe names `BoscaliSummer.Modules.Scenario.ScenarioModule`.
- Core/Scenario files are compiled by path into nomodkit `tests/BoscaliSummer.Tests` (ScenarioReplyTests, ScenarioScriptLintTests); the lint test parses every `modules/Scenario/Assets/*.scenario` from disk by that path — keep the folder where it is.
- `nomod sim ls scenario --mod boscalisummer`.

## Gotchas

- `ScenarioHost.Instance`, `linesSaid` and `Report()` exist only in non-`PUBLIC_RELEASE` builds.
- Dedicated server (`Application.isBatchMode`): no `ScenarioHost` at all, so host-only actions (`Sky`, `Clock`, `ScenarioOrders`, `Seat`, objective changes) only run on a listen host.
- Objective events can fire before `Update` binds the script; they are buffered in `early` (max 256) only after `MissionProfile.Preparing`. A session that bypasses that seam re-listens in `Bind`.
- A mission with the same title loaded from another picker group is not the scripted one (logged warning) — scripts are matched by mission object, not name.
- Voice clips that finish loading after the mission ended are discarded via `voiceRound`; don't reuse clips across binds.
- `ScenarioOrders` writes private AI fields (land timer, `destination`) by reflection; game AI changes break it quietly.
- `docs/SCENARIOS.md` is referenced in code comments but does not exist in this repo.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [ScenarioModule](ScenarioModule.cs), id `scenario`, "Mission scripts, dialogue and campaigns"; depends on nothing; roster: always; mission profile: core.
- 13 .cs files, 2551 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 45 |
| Configuration/ | 1 | 17 |
| Patches/ | 2 | 74 |
| Presentation/ | 3 | 442 |
| Runtime/ | 6 | 1973 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/ScenarioHost.cs](Runtime/ScenarioHost.cs) | 915 |
| [Runtime/MissionPatcher.cs](Runtime/MissionPatcher.cs) | 416 |
| [Presentation/ScenarioStageView.cs](Presentation/ScenarioStageView.cs) | 262 |
| [Runtime/ScenarioMissions.cs](Runtime/ScenarioMissions.cs) | 198 |
| [Runtime/ScenarioAutomation.cs](Runtime/ScenarioAutomation.cs) | 165 |

### Harmony patches (2)

- `CommandLineArgParser.LoadMission` by `LaunchMissionPatch` in [Patches/LaunchMissionPatch.cs](Patches/LaunchMissionPatch.cs) — SHARED with Gamemodes, Maps (ORDER-SENSITIVE: scenario → gamemodes; gamemodes → maps)
- `MissionRunner.RemoveActiveObjective` by `StopObjectivePatch` in [Patches/StopObjectivePatch.cs](Patches/StopObjectivePatch.cs)

### Contracts

- Registers as service: none
- Implements: none
- Resolves: `IChatter`, `IHudBoard`, `IWeatherDirector`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Scenario` (1 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Scenario.Runtime.ScenarioAutomation` (dev)

### Pinned by nomodkit

0 files compiled by path, 3 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
