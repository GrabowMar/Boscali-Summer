# Module status

Working inventory of every module and its features, with a status call on each so you can
see at a glance what is solid, what is mid-rework, and what has never been confirmed in a
live mission. This is a planning aid, not a spec — [ARCHITECTURE](ARCHITECTURE.md) and
[MODULE_BOUNDARIES](MODULE_BOUNDARIES.md) still own the design rules.

Last swept: 2026-09-10, against `main` (dev build `0.1.1`). Updated after the
September repo reorganization (flatten to `modules/` at the root, AGENTS.md
consolidation, README/doc drift fixes).
Squad-focused update: 2026-09-12. Ace career and radio transition assertions pass with
the full pure suite; the new Squad/UI/audio integration has not been deployed or flown.
Release build: **passes**, 0 warnings. Pure test suite (`dotnet run --project
tests/BoscaliSummer.Tests -c Release`): **passes** (module + framework + architecture).

## Status legend

| Tag | Meaning |
|---|---|
| **Stable** | Shipped behaviour, on by default, covered by tests, no active rework |
| **In-flight** | Works, but code is being reshaped this cycle — expect churn |
| **Unverified** | Code complete and logic-tested, never confirmed in a live/in-game mission |
| **Experimental** | Behind a default-off flag; needs a full validation pass before default-on |
| **Absent** | Named in docs/roadmap but not implemented, or removed from the build |
| **Drift** | Code and the prose docs (README / DESIGN_NOTES) currently disagree |

## Module matrix

| Module | Feature id | Default | Depends on | Overall |
|---|---|---|---|---|
| Fire & destruction | `fire-and-destruction` | on | — | **Stable** (minor drift) |
| Urban combat | `urban-combat` | on | — | **In-flight** |
| Radio | `radio` | on (client-local) | optional `ISquadView` | **Stable / Unverified** hunt override |
| Quality of life | `qol` | on (client-local) | — | **Unverified** (new module) |
| Autopilot landing | `autopilot` | on (client-local) | - | **Unverified** (new module) |
| Squad / ace hunts | `squad` | on (host-auth) | — | **Unverified** |
| Progression | `progression` | on (host-auth) | `squad` | **Unverified** SQD/career integration |
| Support operations | `support` | on | `progression` | **In-flight** (drift) |
| Tactical command | `command` | on | `progression` | **In-flight / Unverified** |
| Dynamic operations | `dynamic-operations` | **off** | — | **Experimental** |
| Trenches | `trenches` | on | Command | **Combat implementation; in-game acceptance pending** |
| Weather | — | — | — | **Absent** (archived) |

Load order (composition root): fire → urban → radio → qol → squad → progression → support →
command → dynamic-operations → trenches. Progression/Support/Command are simply not constructed when
disabled; Squad is installed with Progression. `qol`, `dynamic-operations`, and `trenches`
are gated on their own `Enabled` flag. The whole plugin now requires Wing Command `0.9.2.6`+
with its public Squad API.

---

## Fire & destruction — `fire-and-destruction`

**Purpose:** ignition, forest fire spread, impact scorch, ruins and aftermath. Independent,
host-authoritative world mutation, two replicated channels.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Projectile ignition (bullet + missile) on forests / civilian buildings | `Patches/ImpactPatches.cs`, `Runtime/ImpactFireManager.cs` | Stable | 256-item host queue, 8/frame |
| Ground-vehicle destruction secondary ignition | `GroundVehicleDestructionPatch` | Stable | 32-event queue, 1 spatial query/frame |
| Forest index (built once per scene) | `Runtime/ForestIndex.cs` | Stable | |
| Wind-biased forest spread, bounded child fronts | `Runtime/ImpactFireManager.cs` | Stable | **Drift:** code `FireSpreadGenerations => 3`; README/ARCHITECTURE say "≤2 generations" |
| Fire visuals (Fuel Depot smoke clone, 3-light budget) | `Visuals/FireVisualPool.cs`, `Visuals/FuelDepotSmokePool.cs` | Stable | |
| Impact scorch decals (local cosmetic, 1–3 marks/hit) | `Buildings/ImpactScorch*.cs` | Stable | Replaced the old HP-tier damage model; nothing on the wire |
| Ruin aftermath: collapse burst → hot smoke → smoulder | `Buildings/RuinAftermathManager.cs`, `Buildings/CollapseBurstPool.cs` | Stable | 256 logical / 24 smoke visuals / 4 bursts |
| Direct-kill ruin hook | `Buildings/MapBuildingRuinPatch.cs` | Stable | |
| Burnout demolition of unoccupied buildings | `Runtime/ImpactFireManager.cs` | Stable | Occupied shells preserved |
| Aircraft wreck persistence (30s → 180s) | `Patches/AircraftWreckPersistencePatch.cs` | In-flight | Transpiler on `Aircraft.UnitDisabled`; **undocumented in README** |
| Replication: `FireIgnitedMessage`, `RuinCreatedMessage`, late-join snapshots | `Networking/ModNet.cs` | Stable | Protected wire names — do not rename |

**Needs attention**
- Reconcile the fire caps: code has `MaxActiveFires => 32` and `FireSpreadGenerations => 3`;
  README + ARCHITECTURE tables say 24 sites and ≤2 generations.
- Document `AircraftWreckPersistencePatch` in README / ARCHITECTURE (new patch, new behaviour).
- Roadmap: split `ImpactFireManager` / `ModNet` behind tested seams (combat-impact bridge,
  building catalogue, network transport) — not started.

---

## Urban combat — `urban-combat`

**Purpose:** occupied civilian rooftops with visible defensive positions, capture cleanup,
air-assault presentation. Publishes `IBuildingOccupancy`, `IZoneFortificationService`,
`IBaseDefenseAlarmService`.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Zone garrisons — suitable roofs → native MG/AT/AA nests | `Runtime/ZoneGarrisonManager.cs`, `Runtime/RooftopPlacement.cs`, `Runtime/GarrisonOccupancy.cs` | In-game acceptance pending | 49 roof candidates with nine support samples each; cooked mesh or readable geometry, corrected box fallback for non-readable props; one weapon per shell, six per zone, 96 overall. Native spawn replication; cleanup clears dead occupancy. |
| Capture cleanup | `Patches/AirbaseCapturePatches.cs` | Stable | Highway strips supported; ship airbases ignored |
| Garrison visuals / occupied-building marking | `Visuals/GarrisonVisual.cs`, `Visuals/OccupiedBuildingMarking.cs` | Unity preview checked; in-game pending | Visible weapons/crew; local sandbags and faction flag. Terrain dugout hidden on roofs. Four decoration renderers, under 3,000 vertices per position, no cosmetic colliders/lights. |
| `IZoneFortificationService.TryFortify` (consumed by Support) | `Runtime/ZoneGarrisonManager.cs` | Stable | Verifies definition/spawner/shells before charging |
| Air assault — visible insertion sequences, bounded outposts | `Runtime/AirAssaultController.cs`, `Visuals/AirAssaultVisuals.cs` | Unverified | 8 visual ops / 12 encampment cap; in-game visual validation pending |
| Infantry encampment / makeshift fortification builders | `Runtime/InfantryEncampmentBuilder.cs`, `Runtime/MakeshiftFortificationBuilder.cs` | In-flight | Presentation attached to networked vanilla emplacements |
| Mounted troops fire | `Patches/MountedTroopsFirePatch.cs` | Stable | |
| Chimera/Tarantula paratrooper loadout station | `Patches/ChimeraLoadoutPatches.cs` (4 patch classes), `Runtime/ChimeraInfantryLoadoutAdapter.cs` | In-flight | Injects a `MountedTroops` station into MC-260 cargo bays; **undocumented in README/ARCHITECTURE** |
| Base defense alarm — hostile strike-package detection, OPS ticker | `Runtime/BaseDefenseAlarmService.cs` | In-flight | 2s poll, 7.5 km radius; feeds `SupportPanel`; **undocumented** |

**Needs attention**
- Three subsystems here are undocumented: Chimera loadout injection, `BaseDefenseAlarmService`,
  and the encampment/fortification builders. Decide which are keepers and write them up, or cut.
- Air assault + Chimera paradrop want an in-game pass together (does the station appear, does
  the drop work, does cleanup follow the emplacement lifecycle).
- Roadmap "first release" gate: global proxy cap (~96), stable references instead of
  nearest-position repair, `Occupied → Neutralized/Ruined` transitions under late-join.

---

## Radio — `radio`

**Purpose:** client-local map-MFD music player (`RAD` bezel). Zero multiplayer data. Headless
servers skip it.

| Feature | Where | Status | Notes |
|---|---|---|---|
| OGG/WAV library, directory channels, async decode | `Runtime/RadioLibrary.cs`, `Runtime/RadioManager.cs` | Stable | 32 channels / 512 tracks / 1 active decode |
| Three built-in stations (Agrapol FM, Maris Network, Base Broadcast) | `Runtime/RadioStarterLayout.cs`, `Runtime/RadioStation.cs` | Stable | Embedded 256px PNG identities; no bundled audio |
| MFD panel — transport, shuffle, repeat, rescan, folder shortcut | `Presentation/RadioPanel.cs`, `Presentation/PngIconHeader.cs` | Stable | |
| Custom `station.png` loading (≤256×256, ≤256 KiB) | `Presentation/RadioStationIconCache.cs` | Stable | |
| Vanilla-music ownership handoff (defer while on air, restore on stop) | `Patches/VanillaMusicPatches.cs` | Stable | Play / CrossFade / Queue patches |
| Hunt soundtrack and previous station/position/pause restoration | `Runtime/RadioManager.cs`, `Runtime/HuntMusicGate.cs` | Unverified | Local Hunt station or installed tactical clip; manual transport wins, including snapshot recovery |
| MP3 support | — | Absent | Unadvertised until a real target-runtime decode test passes |
| Synchronized stations across peers | — | Absent | Designed (`RadioHello`/`RadioTuneIntent`/`RadioState`), gated, not enabled — see DESIGN_NOTES |

**Needs attention**
- Roadmap gates before this is "done": in-game interaction pass, long-session behaviour.
- Keep the copyright boundary intact — never bundle/download/log/transmit soundtrack audio.

---

## Quality of life — `qol`

**Purpose:** local HUD/camera conveniences + observation marks. **New module this cycle** —
extracted from the old Avionics/Support code, legacy `Avionics.*` config keys retained.
Independent of Progression/Support; skipped on headless.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Third-person flight camera — orbit + rear chase smooth follow, steady horizon, orbit return | `Runtime/ThirdPersonFlightCamera.cs`, `Patches/ThirdPersonFlightCameraPatches.cs` | Unverified | Flight feel unverified in-game; `Avionics.ThirdPersonFlightCameraEnabled` restores native motion |
| Third-person HUD restore (tactical HUD + native minimap in external views) | `Runtime/ThirdPersonHudController.cs`, `Patches/ThirdPersonHudPatches.cs` | Unverified | Publishes `IThirdPersonHud` |
| Framed target-camera feed panel (lower-right, only while targets selected) | `Presentation/ThirdPersonCameraPanel.cs` | Unverified | Borrows `TargetCam.cam` texture; never landing mode / disabled source |
| Camera observation marks — F8 / MARK CAMERA, one point, 120s, coord/range/age | `Runtime/ObservationManager.cs`, `Runtime/ObservationStore.cs` | In-flight | Publishes `IObservationSource`; consumed by OPS "CALL AT MARK" |
| Selected-contact freshness readout (faction tracking timestamp, 4 Hz) | `Runtime/ObservationManager.cs` | In-flight | Never derived from an enemy Transform |
| Gun aim assist — 2.5° cone, 4% input ceiling, yields to steering | `Runtime/GunAimAssist.cs`, `Runtime/GunAimAssistPolicy.cs`, `Patches/GunAimAssistPatches.cs` | Experimental | `QoL.GunAimAssist` default **false**; flight feel, MP, allocation profiling all unverified |

Config: `QoL.Enabled`, `QoL.GunAimAssist`, `QoL.GunAimAssistStrength` (0–0.08), `QoL.CameraMarks`,
`QoL.MarkCameraKey` (F8); legacy `Avionics.ThirdPersonHudEnabled` / `ThirdPersonCameraEnabled` /
`ThirdPersonFlightCameraEnabled` / `ThirdPersonHudKey` (F7) / `ThirdPersonHidePitchLadder`.

**Needs attention**
- The whole camera/HUD stack is code-complete but **never validated in a running game** — this
  is the single biggest untested surface in the mod. Needs one focused flight session:
  orbit/chase framing, minimap restore, target feed show/hide, transitions, teardown.
- Decide gun aim assist's future: ship opt-in as-is, or cut. It has been "pending flight
  testing" across multiple changelog entries.

---

## Squad / ace hunts — `squad`

**Purpose:** host-owned pilot careers, enemy ace encounters and bonus points, exposed
through `ISquadView`. Public Wing Command API reuse; no copied generator or wing AI.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Pilot identity and F1 respawn/one-life career | `Runtime/SquadManager.cs`, `Configuration/SquadSettings.cs` | Unverified | Confirmed death retires a one-life pilot; ejection alone does not |
| Hostile damage threat and escalating ace-led pursuit | `Domain/AceCareer.cs`, `Runtime/SquadManager.cs`, `Patches/SquadPatches.cs` | Unverified | 25 credited damage, 60s grace, 180s cooldown; pure policy assertions pass |
| One-time ace bonus and surviving rival returns | `Runtime/SquadManager.cs` | Unverified | Bonuses beyond score-point cap; bounded mission history |
| Read-only snapshots and encounter notices | `Networking/SquadNet.cs` | Unverified | Protocol 1, 1Hz while panels closed, eight hostile wing summaries |

Hard caps: 64 careers, four owned wings, four aircraft/wing, 32 encounter records,
900-second aircraft lifetime. After pursuit, surviving aircraft resume normal AI
within that lifetime. Friendly wings remain managed in WMC.

**Needs attention:** single-player/listen-host/client/late-join, target ejection and
respawn, both life modes, survivor return evidence, music transitions and scene cleanup.
No deployment or flight testing was performed for this change. See [ACE_HUNTS](ACE_HUNTS.md).

---

## Progression — `progression`

**Purpose:** session-scoped skill board and the four-page `SQD` pilot experience
(PILOT dossier / SKILLS board / WINGS roster / STUDIO pilot+emblem editor).
Depends on Squad's read-only career/bonus state. Never touches vanilla rank/unlocks. Required by Support and
Command (they consume `IPlayerPerks` / `IProgressionView` only).

| Feature | Where | Status | Notes |
|---|---|---|---|
| Score → points (1 per `ScorePerPoint`, cap `MaximumPoints`) | `Runtime/PerkCatalog.cs` (`PerkPoints`), `Runtime/ProgressionManager.cs` | Stable | Reads `Player.PlayerScore`; rank shown as flavour only |
| Flat 9-perk catalogue, per-perk cost, no prerequisites | `Runtime/PerkCatalog.cs` | Stable | 5 passives + 4 support authorisations; 12 points to buy the whole board |
| Passive effects — fuel use, combat/service/objective reward, support cost | `Patches/ProgressionPatches.cs` | Stable | Hooks `Aircraft.UseFuel` + `FactionHQ.RewardPlayer`; reward mapped by enum member |
| SQD presentation — pilot dossier, shared skill board, enemy wings and pursuit HUD | `Presentation/SqdMfdPanel*.cs`, `Presentation/SqdGlyphs.cs`, `Presentation/EmblemRenderer.cs`, `Presentation/AceHuntHud.cs` | Unverified | Uses `IProgressionView` and `ISquadView`; friendly wings remain in WMC |
| Shared combat-skill presentation (AI/ace skills beside player skills) | `Runtime/AceSkillCatalog.cs`, `SqdMfdPanel.Skills.cs`, `SqdMfdPanel.Wings.cs` | Unverified | Display metadata over Wing Command's replicated four-bit `AbilityMask`; grants nothing |
| Wing Command custom-pilot studio (edit/save/recruit) | `Presentation/SqdMfdPanel.Studio.cs`, `Infrastructure/GameInterop/WingLink.cs` | Unverified | Additive companion API resolved separately; page fails closed on older Wing Command builds |
| Local squadron identity — procedural/PNG emblem, name, local pilot profile | `Runtime/EmblemDesign.cs`, `Presentation/EmblemRenderer.cs`, `Configuration/ProgressionSettings.cs` | Unverified | Client-local cosmetics; never networked |
| Ace bonuses and one-life successor perk reset | `Runtime/ProgressionManager.cs` | Unverified | Score points plus server-owned bonuses, 20 total point ceiling |
| Networking — protocol byte `3`, client polls while SQD open | `Networking/ProgressionNet.cs` | Unverified | Scene/request/pilot generation validation; host fast-path in-process; cosmetics are not on the wire |
| `PerkStrength` scaling of passives | `Runtime/ProgressionManager.cs` | Stable | 0 = cosmetic, 2.0 = double |
| Persistent cross-mission profiles | — | Absent | Gated on the persistence service (schema-versioned atomic writes); custom pilots persist in Wing Command's own folder |

Config: `Progression.Enabled`, `ScorePerPoint` (500), `MaximumPoints` (6), `PerkStrength` (1.0).
`Squadron.Name` / `Emblem` / `EmblemFile` / `PilotProfile` are client-local dossier cosmetics.
Debug: `Debug.BypassRequirements` (grants everything free — testing aid).

**Needs attention**
- In-game acceptance is pending for the four-page SQD redesign, the studio against the
  companion Wing Command build, and emblem/PNG rendering at panel scale.
- Balance dials to revisit once mission-length data exists: perk costs, `ScorePerPoint`.

---

## Support operations — `support`

OPS is now four pages: SUPPORT (abilities and camera marks), SPACE (constellation command),
CYBER (infrastructure and hacks) and STATUS. Orbital abilities require a matching-role
satellite overhead; satellites are real orbiting objects the player launches, moves and
recalls. Cyber infrastructure is bought with allocation and gates five bounded hacks.
Host-authoritative, bounded snapshots, pure-model regressions and wire round-trips pass;
live multiplayer acceptance is pending.


**Purpose:** OPS `SUPPORT` page — server-validated support requests, costs derived from vanilla
unit value, one `CostMultiplier`, typed denials, verified card state.

| Action (catalogue) | Id | Capability / perk | Status | Notes |
|---|---|---|---|---|
| Satellite Scan | `Recon` | `Recon` / Satellite Scan | In-flight | Immediate coverage-gated native tracking snapshot, capped at 48 contacts; **absent from catalogue** if the seam can't be resolved |
| Zone Fortification | `Fortify` | `Fortify` / Combat Engineering | Stable | Calls `IZoneFortificationService`; charged only after defenders verified. Absent if Urban Combat missing |
| Rod from God (kinetic strike) | `Artillery` | `Artillery` / Rod from God | In-flight | Native missile delivery with server-only 150 m core / 420 m blast; requires STRIKE coverage. In-game MP pending |
| EMP Shock | `Emp` | `Emp` / EMP Shock | In-flight | Particle shock fronts and lightning; requires EW coverage; host-only 18 s jamming, local cockpit feedback. In-game MP pending |
| Flare Barrage | `FlareMissile` | **`Recon`** (shared) / — | In-flight | Airburst IR countermeasure. **No dedicated perk** — reuses the Recon capability to authorise |

| Cyber operation | Id | Gate | Status | Notes |
|---|---|---|---|---|
| Ping Sweep | `HackPing` | SIGINT LV1 | Pure-tested | Immediate ground reveal; radius scales with SIGINT |
| Track Uplink | `HackTrack` | SIGINT LV2 | Pure-tested | Re-stamps air tracks for a duration; radius/duration scale with SIGINT |
| Radar Blackout | `HackBlackout` | C2D LV1 | Pure-tested | Native `Unit.Jam` on hostile units only; radius/strength scale with C2D |
| Ghost Shield | `HackGhost` | EW LV1 | Pure-tested | Hostile tracking of your aircraft decays to stale blips (shared `CyberEffects` layer) |
| Spoof Contacts | `HackSpoof` | EW LV2 | Pure-tested | Hostile track feeds overwritten with a false formation position |

| Supporting piece | Where | Status | Notes |
|---|---|---|---|
| Constellation model | `Runtime/OrbitalConstellation.cs` | Pure-tested | Three shells, real angles/footprints/fuel, coverage and next-pass queries, per-satellite windows, merged forward coverage forecast, manoeuvre easing |
| Infrastructure model | `Runtime/InfoNetwork.cs` | Pure-tested | Four facilities × 3 levels, prereqs, hack scaling, prices |
| Track deception | `Runtime/CyberEffects.cs` | In-flight | Bounded: 4 effects, 64 aircraft, hostile tracking dictionaries only |
| Map overlay | `Presentation/SupportMapOverlay.cs` | In-flight | Satellite tracks, footprints, role badges; coverage warning on the armed reticle |
| Camera surface mark | `ICameraTargetService` (Support) + Command TGT CAMERA tab | In-flight | Capture/call/clear plus telemetry; OPS no longer hosts it |
| Map-cursor target resolution | `Runtime/SupportTargeting.cs`, `Runtime/SupportMapGesture.cs` | Stable | Clearance-sphere / slope tolerance retained |
| Missile visual patch | `Patches/SupportMissileVisualPatch.cs` | Stable | |
| Request pipeline, cooldown, rate limit, typed denials, 5s silent-host timeout | `Runtime/SupportManager.cs`, `Runtime/SupportModel.cs` | Stable | |
| Networking — protocol byte `5`, host fast-path | `Networking/SupportNet.cs` | Stable | Request/result, ops query/command/state, cyber effect broadcast; host validates and charges every fleet/infrastructure command |

Config: `Support.Enabled`, per-action toggles (`ReconSweep`, `Fortification`, `RodFromGod`,
`EmpShock`, `FlareBarrage`, `CyberOperations`), `CostMultiplier`, per-action cost/range/radius keys,
`MaximumSatellites` (4), per-role satellite costs, `SatelliteRecallRefund` (0.4),
`MaximumRangeMeters` (30 km), `ReconRangeMeters` (120 km), `RequestCooldownSeconds` (30),
`FireMissionDefinitionKey`. Debug: `Debug.DisableOpsCooldowns`.

**Needs attention**
- **Flare Barrage** is the loose thread: undocumented, and authorised by the Recon perk
  instead of its own. Either give it a perk row (and a distinct capability) or document the
  shared-capability decision.
- README's config table and "Perks & support" section list only recon/fortify/kinetic/EMP —
  missing Flare Barrage and the `EmpShockRadiusMeters` / `FlareBarrage*` keys.
- Reconcile "Rod from God default-off" (DESIGN_NOTES) vs `RodFromGod = true` (code).
- Roadmap gates: multiplayer + long-session validation.

---

## Tactical command — `command`

MAP panel update: Layers / Readability pages, 48–72px layer rows, 56px exclusive
choices, explicit text states and 44px bulk visibility actions. Missing native map
state disables controls. Standalone control/layout checks use sample adapters;
in-game marker effects, font/theme and input acceptance remain pending.

Faction Resources update: larger stockpile cards and 60-sample local trend graphs,
adaptive force/status lists and labeled ledger comparisons. Morale is stored by
`CommandManager` (eight factions, mission reset, initial 100/100), with public
`FactionResources` host-side access. No effects, replication or disk persistence yet;
remote clients display unavailable. Build/pure checks cover storage and history;
in-game visual and multiplayer acceptance remain pending.

**Purpose:** owns the expanded tactical-map GUI, the `STR` strategic bezel screen, map
overlays, and mission-AI target scoring. Largest and most-churned module. Never tasks a
recruited Wing Command wing.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Expanded tactical map UI — left MFD dock + event log, central map, right bezel rail, spawn footer | `Presentation/MapUi/` (~30 files, `MapUiManager.cs`) | In-flight | Patches `MfdRailPatch`, `MfdScreenChromePatch`, `MfdSinglePanelPatch`; `VanillaMfdRebuild.*` partial classes are new; `Command.ExpandedMapUi` default on |
| `STR` strategic screen — SA / FRONT / TASKING / LOG / CMD pages | `Presentation/StrMfdPanel.cs`, `Domain/TacticalTheaterState.cs`, `Domain/TheaterReadout.cs`, `Domain/CommandScoring.cs`, `Domain/SortieClassifier.cs` | In-flight / Unverified | **Replaced** the old COM 4th bezel + `ITheaterPage` contract (both deleted). Several bugs fixed just now (airbase counts, sortie breakdown, DEFCON) |
| Dynamic frontline / sector-control overlay | `Runtime/TacticalSectorGrid.cs`, `Runtime/MissionMapCompatibilityEngine.cs`, `Presentation/ComMapOverlay.cs`, `Patches/DynamicMapHooks.cs` | Unverified | Elapsed-time pressure/recovery, base-ownership anchored, objective ground presence independent of faction tracking (spotting cannot change cell occupation; both sides see the same cells). Advisory only — vanilla capture unchanged. In-game validation pending |
| Mission-AI target scoring by doctrine | `Patches/AiTargetScoringPatch.cs`, `Domain/CommandDoctrine.cs`, `Runtime/CommandManager.cs` | In-flight | Biases friendly mission AI only |
| `MIS → SECONDARY` objectives view | `Presentation/MapUi/MfdSecondaryObjectives.cs` | Experimental | Reads `ISecondaryObjectivesView`; only live when `dynamic-operations` is enabled |
| `SET` MFD settings page | `Presentation/MapUi/SettingsMfdPanel.cs` | In-flight | Shared `AvScreen`; bounded steppers explain disabled limits |
| Wing Command coexistence (NOAvionics, no assembly dep) | `Presentation/MapUi/`, `Infrastructure/GameInterop/MfdBezel.cs` | Stable | Named same-frame bezel claims plus exclusive armed map gestures |

Config: `Command.Enabled`, `ExpandedMapUi`, `FrontlinesOverlay`, `OverlayOpacity` (0.35),
`GridResolution` (32), `GridRefreshInterval` (0.5s).

**Needs attention**
- **Drift:** DESIGN_NOTES still says "COM is no longer a fourth bezel — theater SA mounts as
  the OPS **THEATER** tab". Reality: `STR` is its own bezel, `ITheaterPage` is gone,
  `ComMfdPanel` deleted. Rewrite the "Wing Command reuse boundary" section.
- `STR` and the frontline overlay are the two things most in need of a live mission pass —
  a lot of the last few commits were "produced rather than declared and left at zero".
- `Presentation/MapUi/` is ~30 files and the main source of bloat. Worth a pass to see what
  is dead after the COM→STR migration.
- Live visual certification is still needed for the unified `AvScreen` shell at each
  supported resolution and with Wing Command present.

---

## Dynamic operations — `dynamic-operations`

**Purpose:** experimental host-side secondary-mission director. **Default off.** New module.
Independent; publishes `ISecondaryObjectivesView` and morale outcomes; observes `Unit.Jam` on the host.

| Feature | Where | Status | Notes |
|---|---|---|---|
| 1 Hz host director — 17 contract types including rescue/return, recon/BDA, logistics, repair cover, jammer hunts and surveys | `Runtime/OperationsManager.cs`, `Runtime/OperationMissionPool.cs`, `Domain/OperationBoard.cs` | Experimental | 8 boards / 3 cards / 2 active / 128 issued per faction per mission; randomized generation ≤ every 30s, 1 faction/tick |
| Native rescue, repair and supply observations; continuous surveys and same-aircraft intelligence return | `Runtime/OperationServicePatches.cs`, `Runtime/OperationMissionPool.cs` | In-game acceptance pending | Release/pure/patch checks pass; 2 unit passes/generation, 32 sightlines/tick and 32 recent jammer sources; no new spawns |
| One-time faction money (normal tax) + mission-score XP awards | `Runtime/OperationRewards.cs` | Experimental | `RewardMultiplier` 0.25–4; team award, no individual attribution |
| Special outcomes — 3 native DEF buildings on capture, 6-vehicle convoy on defend | `Runtime/OperationsManager.cs` | Experimental | 120s faction cooldown, 24-object ceiling; convoy needs a connected road |
| Acceptance/dismissal and client snapshots (protocol 2) | `Networking/OperationsNet.cs` | Experimental | Own-faction validated IDs and bounded rate limits; no client completion/reward data |
| Accepted-objective markers, adaptive MIS board, host morale +3 / hostile target faction -3 | `Runtime/OperationMarkers.cs`, `Runtime/OperationMarkerPatch.cs`, `Runtime/OperationZoneHud.cs`, Command MIS presenter | Experimental | Native map marker, cockpit pointer and area ring via the UI-only `MissionPosition` query; cockpit zone readout with enter/leave feedback; markers omit untracked enemies; morale remains host-only |

Config: `DynamicOperations.Enabled` (**false**), `DynamicOperations.RewardMultiplier` (1.0).
Full MIS panel also needs `Progression.Enabled` + `Command.Enabled` + `Command.ExpandedMapUi`.

**Needs attention**
- Default-on gate (see [DYNAMIC_OPERATIONS.md](DYNAMIC_OPERATIONS.md)): single-player
  capture/defense/interdiction + tax/score awards; convoy path & supply behaviour;
  blocked/partial-spawn cleanup; listen-host / remote-client / late-join faction views;
  pause/resume, faction switch, mission reload, disable cleanup. **None proven** — build and
  pure tests can't cover any of it.
- Four `RESEARCH_DYNAMIC_*.md` docs are untracked in git alongside this — decide if they are
  committed reference or scratch.

---

## Trenches — `trenches`

**Purpose:** autonomous node-based modular trench networks, geometric growth simulation,
procedural parapet/berm meshes, and tactical map crenellations. Non-destructive terrain
solution designed for high flight-sim performance.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Domain math — zigzag traverses, sapping criteria, flank hooks, belt stage progression | `Domain/TrenchTacticalMath.cs` | Stable | Pure C#, verified by unit tests |
| Graph data model — nodes, edges, network corridor bounds | `Runtime/TrenchNode.cs`, `Runtime/TrenchEdge.cs`, `Runtime/TrenchNetwork.cs` | Stable | 16 networks / 64 nodes / 96 edges per network hard ceiling |
| Growth — 132m seed line, flank extension, support line, rear redoubt | `Runtime/TrenchGrowthSimulator.cs` | Unity regression passed | Five stages / four growth ticks; atomic additions with rejection reasons; default 45s per step |
| Combat — native MG/AT/AA emplacements, suppression, permanent losses | `Runtime/TrenchGarrison.cs` | Adapter regression passed; in-game acceptance pending | 2/4/6 defenders spread along the sector line, six/site and 96 total; damage pauses construction 60s; no healing or replacement |
| Scene manager — frontline sector-slot chain, corridor validation, cleanup | `Runtime/TrenchManager.cs`, `Runtime/TrenchPlacement.cs` | Awaiting in-game validation | Reset order 60; Command contract, one slot/frame; 360m/250m spacing; host only |
| Procedural mesh generator — wide berms, parados, sandbag parapet, pits, dugouts | `Visuals/TrenchMeshBuilder.cs` | Stable | Zero terrain edits; shared 10-point cross-section profile; prevents PhysX stalls and resolution artifacts |
| Material resolver — procedural cross-section palette + native concrete/sandbags | `Visuals/TrenchMaterialResolver.cs` | Stable | Zero external asset bundle dependencies; native URP lighting; one baked 256px texture per scene |
| 3-tier flight LOD chunks — continuous LOD0, LOD1/2 + collider distance culling | `Visuals/TrenchVisualChunk.cs` | Stable | Full 3D + colliders < 250m (two-bay boxes), berms 250m–1.2km, ground scar 1.2km–3.5km, culled > 3.5km |
| Tactical map overlay — NATO APP-6 crenellated trench lines & strongpoint marks | `Presentation/TrenchMapOverlay.cs` | Stable | Reset order 61; hooks `DynamicMap.mapImage` |

Config: `Trenches.Enabled` (true), `GrowthIntervalSeconds` (45s), `MaxNetworks` (16, max 16),
`LODNearDistance` (250m), `LODFarDistance` (3500m), `ShowOnTacticalMap` (true).

**Needs attention**
- In-game flight session verification (all LODs, map markers, origin shifts, frontline sector-slot placement, corridor ground fit).
- Native defenders use vanilla replication; procedural earthworks/map marks remain host-local. Verify host/client/late-join targeting, destruction and cleanup in-game.
- Future high-poly custom asset injection pipeline via AssetBundles when artist models are authored.

---

## Weather — absent

Shelved at the user's request. Runtime, debug controls, settings, shader build integration
and tests were removed from the active build. The planned `archive/` snapshot was never
completed; Weather survives only as removed-feature notes in the CHANGELOG. No work
scheduled.

---

## Shared / plumbing

| Area | Where | Status | Notes |
|---|---|---|---|
| Feature graph, host, transactional startup, ordered scene reset, reverse teardown | `Framework/` | Stable | "Framework extraction is done and behaviour-preserving" |
| Cross-feature contracts | `Framework/Contracts/` | In-flight | New this cycle: `IObservationSource`, `ISecondaryObjectivesView`, `IThirdPersonHud`. Deleted: `ITheaterPage` |
| Cached game reflection, capability report | `Infrastructure/GameInterop/` | Stable | Startup logs resolved patch list, capabilities, forest index size — first place to look after a game update |
| Config composition + legacy-key migration | `Configuration/`, `Infrastructure/Diagnostics/DiagnosticSettings.cs` | Stable | |
| NOAvionics kit (bezel claims, map picker, `AvScreen`) | `Avionics/`, `AvionicsUi/` | In-flight | Unified across OPS/STR/RAD/SET and rebuilt vanilla panels; live resolution/coexistence pass pending |
| Large managers not yet split | `ImpactFireManager`, `ZoneGarrisonManager`, `ModNet` | In-flight | Roadmap: only split behind tested seams |

### Test coverage (pure suite)

Present: Command (`CommandTests`, `FrontlineTests`, `MfdPanelTests`,
`MfdSecondaryObjectivesTests`, `StrPanelTests`), DynamicOperations (`OperationTests`),
FireAndDestruction (`ImpactScorchTests`), Progression (`ProgressionTests`), QoL
(`CameraTests`, `GunAimAssistTests`, `ObservationTests`), Radio (`RadioTests`), Support
(`SupportTests`), UrbanCombat (`TroopDeploymentTests`), Framework, Architecture boundary.
`BoscaliSummer.PatchProbe` validates Harmony targets / private fields / wire contracts
against the installed `Assembly-CSharp.dll`.

Thin spots: no dedicated fire-spread/ruin test, no ModNet serializer test, no Command
map-overlay grid test beyond `FrontlineTests`, garrison lifecycle only via
`TroopDeploymentMath`.

---

## Consolidated doc/code drift (fix list)

Resolved in the September reorg: perk count/cost (README + DESIGN_NOTES + ROADMAP now say
nine perks / twelve points), STR-vs-THEATER-tab (DESIGN_NOTES rewritten), Rod from God
default (DESIGN_NOTES no longer says "default-off"), fire caps (ARCHITECTURE now 32 sites /
≤3 generations; the README budgets table was dropped in favour of ARCHITECTURE), Flare
Barrage (now in the README action list, with a note that it shares the Satellite Scan
authorisation), wreck persistence (README + ARCHITECTURE mention it), the README Support
config table (now an explicitly curated subset), and the untracked docs (committed).

Still open:

1. **Flare Barrage has no perk of its own** — it is authorised by the Satellite Scan perk's
   `Recon` capability. Either give it a distinct capability + perk row, or keep the
   shared-capability decision and document it in the Support module. (Support)
2. **Chimera paratrooper loadout + `BaseDefenseAlarmService` + encampment builders** are not
   described in any narrative doc — decide keep vs cut, then write them up. (Urban Combat)

## Refactor / cleanup debt

- `modules/Command/Presentation/MapUi/` (~30 files) — audit for dead code after COM→STR.
- Split `ImpactFireManager`, `ZoneGarrisonManager`, `ModNet` (roadmap, behind seams only).
- Decide keep-or-cut: gun aim assist, Chimera paradrop, Urban Combat encampment builders.
- Persistence service (schema-versioned atomic JSON) — blocks persistent perk profiles and
  any other saved state.

## Biggest untested surfaces (validation backlog)

1. **QoL camera/HUD stack** — entire third-person camera, HUD restore and target feed;
   code-complete, never run in-game.
2. **Command STR + frontline overlay** — recently made functional; needs a live theater.
3. **Dynamic operations** — nothing proven in a mission; default-off until it is.
4. **Air assault + Chimera paradrop** — visual/insertion sequences unconfirmed.
5. **Multiplayer** — Support/Progression protocol-2 channels, late-join for garrisons and
   dynamic ops, listen-host vs dedicated.
