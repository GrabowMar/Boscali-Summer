# Progression — Progression (`progression`)

> Session-scoped OPS skill board (picks earned from vanilla score), the PILOT (SQD) page, the enemy-ace hunt HUD card, squadron insignia (emblem studio + tail-fin paint) and host-approved per-aircraft engine maps.

## Player-facing behaviour

- **PILOT** page (registry id `SQD`), three icon tabs: pilot status with the enemy ace watch, the OPS skill board (lanes SPACE, CYBER, SOF; grade 1 free, grades 2–6 one pick each), and STUDIO (squadron roster built by Wing, the insignia generator, kneeboard card picker, FLY AS).
- Picks: pick *n* costs *n* × `ScorePerPoint` of vanilla score (SET SERVER "PROGRESSION" > SCORE PER PICK); capped by `MaximumPoints`. Vanilla ranks / unlocks are never touched.
- Enemy ace hunt: a top-centre HUD card ("ENEMY ACE", callsign, skill, wing and range) and an on-screen marker on the ace (host / solo only).
- Squadron insignia: procedural heraldic emblem (or a PNG from `BepInEx/config/BoscaliSummer/Emblems`) painted on your tail fins, your wingmen's, other players' (their published design) and enemy ace wings' (OFF / COLOUR / LOWVIS).
- MY AIRCRAFT dossier (shown on Wing's WMC SUPPLY via `IMyAircraft`) and per-aircraft engine maps that cap throttle.
- Flavour pilot title from your score (display only).

## How it works

[ProgressionModule](ProgressionModule.cs) depends on `squad` and calls `context.Services.GetRequired<ISquadView>()` at install. It adds [ProgressionManager](Runtime/ProgressionManager.cs) (scene service 45) and the [ProgressionNet](Networking/ProgressionNet.cs) component, registers `IPlayerPerks` + `IProgressionView` (the manager) and the host table. Outside batch mode it also adds [SqdMfdPanel](Presentation/SqdMfdPanel.cs) (54), [AceHuntHud](Presentation/AceHuntHud.cs) (54), [TailPaint](Visuals/TailPaint.cs) (56) and registers `IMyAircraft` ([PlaneDossier](Presentation/PlaneDossier.cs)) and `ISquadronInsignia` ([SquadronInsignia](Presentation/SquadronInsignia.cs)).

- **ProgressionManager** (host-authoritative): the server holds every player's `PerkState` ([PerkCatalog](Runtime/PerkCatalog.cs)) and derives the point budget from vanilla score; a client holds only the last snapshot and sends intents (`ProgressionSubmit`, perk id or `QueryOnly`), polling only while a view is open. Static accessor `ProgressionRuntime.Active` (bottom of the same file) is what the patch reads.
- **Engine maps**: `PlaneTuneRequest` → host approval → `PlaneTuneState`; [AircraftEngineMapPatch](Patches/ProgressionPatches.cs) postfixes `Aircraft.FilterInputs` and clamps `throttle` via [PlaneEngineMap](Domain/PlaneEngineMap.cs).
- **Crests**: the local design ([EmblemDesign](Runtime/EmblemDesign.cs), 8 layer bytes) is published as `CrestSubmit`; the host relays `CrestState` into the bounded [CrestBoard](Runtime/CrestBoard.cs) (`ProgressionNet.Crests`). A local PNG never leaves the machine.
- **TailPaint**: bakes a marking on the GPU into a copy of the livery atlas with shader `Hidden/BoscaliSummer/EmblemBake` (from embedded `emblem.bundle`), packs it to DXT on a worker thread ([LiveryCodec](Runtime/LiveryCodec.cs), `Task.Run`) and swaps `_Livery` on the existing material instances; nothing per frame. Emblem pixels from [EmblemRaster](Runtime/EmblemRaster.cs) (signed distances) + [EmblemMarks](Runtime/EmblemMarks.cs) (centre marks from embedded `emblem-art.bin`), textures via [EmblemRenderer](Presentation/EmblemRenderer.cs).
- **SqdMfdPanel** is partial: [.Pilot](Presentation/SqdMfdPanel.Pilot.cs), [.Skills](Presentation/SqdMfdPanel.Skills.cs) (layout [SkillBoardLayout](Presentation/SkillBoardLayout.cs)), [.Studio](Presentation/SqdMfdPanel.Studio.cs), [.AceWatch](Presentation/SqdMfdPanel.AceWatch.cs); shared parts in [SqdParts](Presentation/SqdParts.cs). Built on AvionicsUi kit v2 (`AvConsole` + `AvFlow`).
- **Lifecycle**: scene services reset per mission; `ProgressionNet` clears crests and throttles on scene reset. Mission profile `battlefield`.

## Where to change things

| To change… | Look in |
|---|---|
| Skills, lanes, grades, what a skill unlocks | [PerkCatalog](Runtime/PerkCatalog.cs) (ids go over the wire: see ProtocolVersion rule) |
| Point budget / pick cost | [ProgressionManager](Runtime/ProgressionManager.cs), [ProgressionSettings](Configuration/ProgressionSettings.cs) |
| Shared ace combat skill names / badges | [AceSkillCatalog](Runtime/AceSkillCatalog.cs) (effects are Wing's) |
| PILOT page layout / tabs | [SqdMfdPanel](Presentation/SqdMfdPanel.cs) + its partials, [SqdParts](Presentation/SqdParts.cs) |
| Skill board geometry | [SkillBoardLayout](Presentation/SkillBoardLayout.cs) |
| Ace hunt HUD card / marker | [AceHuntHud](Presentation/AceHuntHud.cs), copy in [AceHuntHudCopy](Domain/AceHuntHudCopy.cs) |
| Emblem layers / encoding | [EmblemDesign](Runtime/EmblemDesign.cs) (`Squadron.Emblem` string format) |
| Emblem drawing / centre marks | [EmblemRaster](Runtime/EmblemRaster.cs), [EmblemMarks](Runtime/EmblemMarks.cs) (art rebuilt by nomodkit `tools/Build-EmblemArt.py`) |
| Tail-fin painting, which aircraft get crests | [TailPaint](Visuals/TailPaint.cs) |
| DXT packing | [LiveryCodec](Runtime/LiveryCodec.cs) |
| Engine map limits | [PlaneEngineMap](Domain/PlaneEngineMap.cs), [ProgressionPatches](Patches/ProgressionPatches.cs) |
| MY AIRCRAFT section | [PlaneDossier](Presentation/PlaneDossier.cs) |
| Pilot flavour titles | [PilotTitleCatalog](Domain/PilotTitleCatalog.cs) |
| Messages / protocol, crest relay and its throttles | [ProgressionNet](Networking/ProgressionNet.cs), [CrestBoard](Runtime/CrestBoard.cs) |
| SET SERVER "PROGRESSION" row | `Install` in [ProgressionModule](ProgressionModule.cs) |

## Talks to other modules

- Provides: `IPlayerPerks`, `IProgressionView` (Support gates/prices actions by skills; Support depends on this module), `IMyAircraft`, `ISquadronInsignia` (Wing builds them into its pages).
- Consumes: `ISquadView` (Squad, **required** at install), `IAceWingRoster` (Squad), `IWingSquad` (Wing builds the STUDIO squadron), `IKneeboardGallery` (Immersion), `IHudBoard` (Hud).

## Settings, network, patches

- `Progression` section is host-authoritative; `Squadron` section is client-local cosmetics. `PerkStrength` is retired (no effect) but kept so the host-authority list keeps its shape: do not remove.
- `ProgressionNet.ProtocolVersion` = 8. Its doc records why: bump on any message layout change **and** whenever perk ids change meaning (v7 re-themed grades, v8 new lanes).
- Crest sends are throttled (`CrestHostInterval` 2 s on the host, `CrestPublishInterval` 2.5 s on the publisher).
- Only patch: `Aircraft.FilterInputs` postfix (wrapped in `PatchGuard`).

## Tests, sim hooks and pinned names

- Sim hook: `BoscaliSummer.Modules.Progression.Visuals.TailPaintAutomation` (inside `#if !PUBLIC_RELEASE` at the end of [TailPaint.cs](Visuals/TailPaint.cs)).
- nomodkit compiles by path: `Domain/AceHuntHudCopy.cs`, `Domain/PlaneEngineMap.cs`, `Presentation/SkillBoardLayout.cs`, `Runtime/` AceSkillCatalog, CrestBoard, EmblemDesign, EmblemMarks, EmblemRaster, LiveryCodec, PerkCatalog, and embeds `Assets/emblem-art.bin`. Keep them engine-free and in place.
- Names referenced: the module, settings, all 6 messages, `AircraftEngineMapPatch`, `AceHuntHud`, `EmblemCanvas` (in EmblemRenderer.cs), `SqdMfdPanel`, `EmblemDesign`, `PerkState` (in PerkCatalog.cs), `ProgressionManager`, `TailPaint`, `TailPaintAutomation`.
- Tests: `Features/Progression/AceHuntHudCopyTests`, `PlaneEngineMapTests`, `PresentationUnityCheck`, `TailPaintUnityCheck`. Scenario: `nomod sim ls tail-paint`.

## Gotchas

- `emblem.bundle` is embedded only `Condition="Exists(...)"` in the csproj; without it `TailPaint.BakeReady` fails once (logged) and tail paint is silently off.
- `EmblemMarks` order in `emblem-art.bin` is the stored encoding (`EmblemDesign.ChargeNames`): append only, or saved `Squadron.Emblem` strings and published crests change meaning.
- TailPaint uses `Task.Run` for emblem pixels and DXT packing: those functions must stay pure (no Unity API); results are consumed back on the main thread.
- `CrestBoard` refuses new owners past `Capacity` rather than evicting; id 0 never owns a crest.
- The module installs SQD/HUD/TailPaint only when `!Application.isBatchMode`; host-side logic still runs headless.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [ProgressionModule](ProgressionModule.cs), id `progression`, "Progression"; depends on `squad`; roster: Progression.Enabled; mission profile: battlefield.
- 28 .cs files, 6948 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 54 |
| Configuration/ | 1 | 71 |
| Domain/ | 3 | 82 |
| Networking/ | 1 | 503 |
| Patches/ | 1 | 33 |
| Presentation/ | 12 | 3766 |
| Runtime/ | 8 | 1643 |
| Visuals/ | 1 | 796 |

### Largest files

| File | Lines |
|---|---|
| [Visuals/TailPaint.cs](Visuals/TailPaint.cs) | 796 |
| [Presentation/SqdParts.cs](Presentation/SqdParts.cs) | 586 |
| [Networking/ProgressionNet.cs](Networking/ProgressionNet.cs) | 503 |
| [Presentation/SqdMfdPanel.Skills.cs](Presentation/SqdMfdPanel.Skills.cs) | 503 |
| [Presentation/PlaneDossier.cs](Presentation/PlaneDossier.cs) | 489 |

### Harmony patches (1)

- `Aircraft.FilterInputs` by `AircraftEngineMapPatch` in [Patches/ProgressionPatches.cs](Patches/ProgressionPatches.cs) — SHARED with Megaplane, Vanguard (INDEPENDENT)

### Contracts

- Registers as service: `IMyAircraft`, `IPlayerPerks`, `IProgressionView`, `ISquadronInsignia`
- Implements: `IMyAircraft`, `IPlayerPerks`, `IProgressionView`, `ISquadronInsignia`
- Resolves: `IAceWingRoster`, `IHudBoard`, `IKneeboardGallery`, `ISquadView`, `IWingSquad`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Progression`, `Squadron` (9 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `ProgressionNet.ProtocolVersion` = 8
- Messages (6): `CrestState`, `CrestSubmit`, `PlaneTuneRequest`, `PlaneTuneState`, `ProgressionSnapshot`, `ProgressionSubmit`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Progression.Visuals.TailPaintAutomation` (dev)

### Pinned by nomodkit

11 files compiled by path, 17 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
