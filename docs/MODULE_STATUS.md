# Module status

Working inventory of every module and its features, with a status call on each so you can
see at a glance what is solid, what is mid-rework, and what has never been confirmed in a
live mission. This is a planning aid, not a spec — [ARCHITECTURE](ARCHITECTURE.md) and
[MODULE_BOUNDARIES](MODULE_BOUNDARIES.md) still own the design rules.

Last swept: 2026-09-14, against `quality/code-pass-2026-09-14` (dev build `0.1.1`) after
the quality-pass trims (Waves A–C). Gun aim CUT; Chimera paradrop / infantry encampments /
base-defense alarm KEEP; `TheaterBias`, `MakeshiftFortificationBuilder` and `EventIconCache`
deleted. Squad-focused update: 2026-09-12. Ace career and radio transition assertions pass
with the full pure suite; the new Squad/UI/audio integration has not been deployed or flown.
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
| Fire & destruction | `fire-and-destruction` | on | — | **Stable** |
| Urban combat | `urban-combat` | on | — | **In-flight** |
| Radio | `radio` | on (client-local) | optional `ISquadView` | **Stable / Unverified** `RAD` RECEIVER + MUSIC pages; hunt override unverified |
| Common HUD element | `hud` | on (client-local) | optional for its consumers | **Unverified** (new module) - one vanilla-styled line stack every HUD feature draws through; in-game visual acceptance pending |
| Quality of life | `qol` | on (client-local) | — | **Unverified** (new module) |
| Autopilot landing | `autopilot` | on (client-local) | - | **Unverified** (new module) |
| TGT target presets + quick slots + native-radial target page | `Presentation/MapUi/TargetPresetModel.cs`, `TargetPresetRuntime.cs`, `TargetPresetRadialPage.cs`, `Runtime/TargetPresetHotkeys.cs` | In-flight | Player-saved filter profiles (max 12, 14-char names) persist in `Command.TargetPresets`/`TargetPresetSlots`; F6/F9/F10 quick slots; radial host in Autopilot resolves `IRadialMenuPage`. In-game visual/input/MP acceptance pending |
| Squad / ace hunts | `squad` | on (host-auth) | — | **Unverified** |
| Progression | `progression` | on (host-auth) | `squad` | **Unverified** SQD/career integration |
| Support operations | `support` | on | `progression` | **In-flight** |
| Tactical command | `command` | on | `progression` | **In-flight / Unverified** |
| Dynamic operations | `dynamic-operations` | **off** | — | **Experimental** |
| Chain of command | `high-command` | on | — | **Unverified** (new module) |
| Theater operations | `theater-ops` | on (host-auth) | — | **Unverified** (new module) |
| Trenches | `trenches` | on | Command | **Combat implementation; in-game acceptance pending** |
| World events | `events` | on | — | **Unverified** (new module) |
| Campaign mission | `campaign` | on | — | **Awaiting in-game validation** (new module) |
| Weather | — | — | — | **Absent** — previous implementation removed; replacement not yet designed |

Load order (composition root): fire → urban → hud → radio → qol → squad → progression → support →
command → dynamic-operations → high-command → theater-ops → trenches → events → campaign. Progression/Support/Command are simply not constructed when
disabled; Squad is installed with Progression. `campaign`, `qol`, `dynamic-operations`,
and `trenches` are gated on their own `Enabled` flag. The whole plugin now requires Wing Command
`0.9.2.6`+ with its public Squad API.

---

## Fire & destruction — `fire-and-destruction`

**Purpose:** ignition, forest fire spread, impact scorch, ruins and aftermath. Independent,
host-authoritative world mutation, two replicated channels.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Projectile ignition (bullet + missile) on forests / civilian buildings | `Patches/ImpactPatches.cs`, `Runtime/ImpactFireManager.cs` | Stable | 256-item host queue, 8/frame |
| Ground-vehicle destruction secondary ignition | `GroundVehicleDestructionPatch` | Stable | 32-event queue, 1 spatial query/frame |
| Forest index (built once per scene) | `Runtime/ForestIndex.cs` | Stable | |
| Wind-biased forest spread, bounded child fronts | `Runtime/ImpactFireManager.cs` | Stable | 32 sites, 2 attempts, ≤3 generations |
| Fire visuals (flame/ember layers, Fuel Depot smoke clone, 3-light budget) | `Visuals/FireVisualPool.cs`, `Visuals/FuelDepotSmokePool.cs` | Stable | Forest plume tinted lighter/taller with buoyancy and stronger shear |
| Burn scars — nuke-scale blast-map ash bed, small tree-clear blast, pooled ground soot decal | `Runtime/FireScorchPolicy.cs`, `Visuals/BurnScarPool.cs`, `Runtime/ImpactFireManager.cs` | Stable | Tree removal decoupled from the ash stamp: ≤3 ash stamps (221–338 m) and one ~2 m tree-clearing blast per site; lobe decal offsets scale with the scar (45 m base), so a site reads as one overlapping scar; 64 decals, oldest recycled |
| Impact scorch decals (local cosmetic, 1–3 marks/hit) | `Buildings/ImpactScorch*.cs` | Stable | Replaced the old HP-tier damage model; nothing on the wire |
| Ruin aftermath: collapse burst → hot smoke → smoulder | `Buildings/RuinAftermathManager.cs`, `Buildings/CollapseBurstPool.cs` | Stable | 256 logical / 24 smoke visuals / 4 bursts |
| Direct-kill ruin hook | `Buildings/MapBuildingRuinPatch.cs` | Stable | |
| Burnout demolition of unoccupied buildings | `Runtime/ImpactFireManager.cs` | Stable | Occupied shells preserved |
| Aircraft wreck persistence (30s → 180s) | `Patches/AircraftWreckPersistencePatch.cs` | In-flight | Transpiler on `Aircraft.UnitDisabled`; README + ARCHITECTURE mention it |
| Replication: `FireIgnitedMessage`, `RuinCreatedMessage`, late-join snapshots | `Networking/ModNet.cs` | Stable | Protected wire names — do not rename |

**Needs attention**
- Fire caps match code: `MaxActiveFires => 32`, `FireSpreadGenerations => 3` (ARCHITECTURE
  hard-budget table; DESIGN_NOTES spread bullet).
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
| Garrison visuals / occupied-building marking | `Visuals/GarrisonVisual.cs`, `Visuals/OccupiedBuildingMarking.cs`, `Runtime/GarrisonMarkerInfo.cs` | Unity preview checked; in-game pending | Visible weapons/crew; local sandbags; shell-scaled twin masts/flags and roof-edge faction band with viewer-relative accent, rebuilt from the measured flat-roof patch encoded in the defense's unique name; definition-sized nest marker kept as fallback. Six decoration renderers, under 4,000 vertices per position, no cosmetic colliders/lights. Pure `$m` round-trip tests added. |
| `IZoneFortificationService.TryFortify` (consumed by Support) | `Runtime/ZoneGarrisonManager.cs` | Stable | Verifies definition/spawner/shells before charging; occupies the doctrine's shell count (bounded by the zone/theater ceilings, true when at least one landed) |
| Air assault — visible insertion sequences, bounded outposts | `Runtime/AirAssaultController.cs`, `Visuals/AirAssaultVisuals.cs` | Unverified | Cargo access animates open before one eight-troop stick exits over ~8 s at a steady interval; engine-generated parachute mesh (`ParachuteMeshBuilder`: dome + 20 shroud ribbons, double-sided, vanilla fabric/rope materials) on a pendulum above each jumper; per-jumper golden-angle drift, varied chute timing and descent rates plus mission wind fan the stick out; canopy collapses on landing; descent timer scales with drop altitude so sticks do not vanish mid-air; 8 visual ops / 12 encampment cap; in-game visual validation pending |
| Infantry encampments (Ibis rappel / Chimera ground landing) | `Runtime/InfantryEncampmentBuilder.cs` | Unverified | **KEEP.** Presentation on networked vanilla emplacements; a rappel insertion establishes the `IGroundForceReadiness` camp count (1..4, absent = 1) within the twelve-site ceiling. `MakeshiftFortificationBuilder` deleted (never spawned). |
| Mounted troops fire | `Patches/MountedTroopsFirePatch.cs` | Stable | |
| Chimera/Tarantula paratrooper loadout station | `Patches/ChimeraLoadoutPatches.cs` (4 patch classes), `Runtime/ChimeraInfantryLoadoutAdapter.cs` | Unverified | **KEEP.** Injects a `MountedTroops` station into MC-260/Tarantula cargo bays and mirrors it into the definition prefab so `WeaponChecker.VetLoadout` keeps it at spawn; registered in `Encyclopedia.IndexLookup` for serialization. README + ARCHITECTURE mention it. In-game acceptance pending. |
| Base defense alarm — hostile strike-package detection, OPS/STR ticker | `Runtime/BaseDefenseAlarmService.cs` | Unverified | **KEEP.** 2s poll, 7.5 km radius; feeds OPS STATUS and STR. README + ARCHITECTURE mention it. |

**Needs attention**
- Keep-or-cut **decided**: Chimera paradrop KEEP, infantry encampments KEEP, base-defense
  alarm KEEP, makeshift fortification CUT, gun aim CUT.
- Air assault + Chimera paradrop want an in-game pass together (does the station appear, does
  the drop work, does cleanup follow the emplacement lifecycle).
- Roadmap "first release" gate: global proxy cap (~96), stable references instead of
  nearest-position repair, `Occupied → Neutralized/Ruined` transitions under late-join.

---

## Radio — `radio`

**Purpose:** client-local radio (`RAD` bezel: RECEIVER and MUSIC pages). Zero multiplayer
data. Headless servers skip it.

| Feature | Where | Status | Notes |
|---|---|---|---|
| OGG/WAV library, directory channels, async decode | `Runtime/RadioLibrary.cs`, `Runtime/RadioManager.cs`, `Runtime/RadioProgram.cs` | Stable | 32 channels / 512 tracks / 1 active decode per program (receiver, deck) |
| Three built-in stations (Agrapol FM, Maris Network, Base Broadcast) | `Runtime/RadioStarterLayout.cs`, `Runtime/RadioStation.cs` | Stable | Embedded 256px PNG identities; no bundled audio |
| RECEIVER page — tuner card with scale meter and squelch gate, click-to-tune band scope, preset list, transport, band keys, setup row | `Presentation/RadioPanel.cs`, `Presentation/RadioWaterfall.cs`, `Runtime/RadioFrequencies.cs` | Unverified | In-game visual acceptance pending; frequencies client-local; no track switching |
| MUSIC page — now-playing card with position bar, transport, folder stepper, track list with per-row progress, empty-state card | `Presentation/RadioPanel.cs`, `Runtime/RadioManager.cs` | Unverified | Player's own library; the one deck rule is the transmission duck |
| Bands, modulation, fine step, squelch, bandwidth, mode override | `Runtime/RadioFrequencies.cs`, `Runtime/RadioManager.cs`, `Configuration/RadioSettings.cs` | Unverified | FM 87.5–108 @100 kHz, VHF air 118–136.975 @25 kHz AM, MW 530–1700 @10 kHz; FINE divides the step by five |
| Modelled reception — link budget, horizon, terrain LOS, tower loss | `Runtime/RadioPropagation.cs`, `Runtime/RadioTransmitterAnchors.cs`, `Runtime/RadioManager.cs` | Unverified | 2 Hz, one linecast per evaluation on the game's ground mask; lost tower = off air, unresolved map or no listener = full scale |
| Synthetic spectrum row (carriers + noise) | `Runtime/RadioSpectrum.cs` | Unverified | 96 bins @ 0.12 s; no FFT, no per-row allocation |
| Programme caption, rotating wire line, enemy chatter intercepts | `Runtime/RadioProgramming.cs`, `Runtime/RadioManager.cs` | Unverified | Ring-bounded wire text; intercepts read `ISquadView.LastChatter` (read-only) |
| Synthesized receiver audio — carrier, squelch, morse ident, mode/bandwidth character | `Runtime/RadioBroadcastFx.cs` | Unverified | Generated in memory; no bundled asset; no music metadata crosses the wire |
| Custom `station.png` loading (≤256×256, ≤256 KiB) | `Presentation/RadioStationIconCache.cs` | Stable | |
| Vanilla-music hold — sticky through dead air, restored on STOP | `Patches/VanillaMusicPatches.cs`, `Runtime/VanillaMusicHold.cs`, `Runtime/RadioManager.cs` | Stable | Play / CrossFade / Queue patches plus a 0.5 s silence sweep |
| Hunt soundtrack and previous station/position/pause restoration | `Runtime/RadioManager.cs`, `Runtime/HuntMusicGate.cs` | Unverified | Local Hunt station or installed tactical clip; manual transport wins, including snapshot recovery |
| MP3 support | — | Absent | Unadvertised until a real target-runtime decode test passes |
| Voice transmit, crypto nets, jamming, voice receive ducking | `Runtime/RadioLinkStub.cs` | Absent | Inert by design; the panel's RECEIVE ONLY copy says so; `DeckGain` duck is wired with no trigger |
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
| Camera observation marks — F8 / TGT MARK CAMERA, one point, 120s, coord/range/age | `Runtime/ObservationManager.cs`, `Runtime/ObservationStore.cs` | In-flight | Publishes `IObservationSource`; TGT CAMERA hosts MARK CAMERA / CALL AT MARK |
| Selected-contact freshness readout (faction tracking timestamp, 4 Hz) | `Runtime/ObservationManager.cs` | In-flight | Never derived from an enemy Transform |

Config: `QoL.Enabled`, `QoL.CameraMarks`,
`QoL.MarkCameraKey` (F8); legacy `Avionics.ThirdPersonHudEnabled` / `ThirdPersonCameraEnabled` /
`ThirdPersonFlightCameraEnabled` / `ThirdPersonHudKey` (F7) / `ThirdPersonHidePitchLadder`.

**Needs attention**
- The whole camera/HUD stack is code-complete but **never validated in a running game** — this
  is the single biggest untested surface in the mod. Needs one focused flight session:
  orbit/chase framing, minimap restore, target feed show/hide, transitions, teardown.

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
within that lifetime. Friendly wings are read-only here; recruiting and orders remain in WMC.

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
| Score → picks (grade n costs n x `ScorePerPoint`, cap `MaximumPoints`) | `Runtime/PerkCatalog.cs` (`PerkPoints`), `Runtime/ProgressionManager.cs` | Stable | Reads `Player.PlayerScore`; rank shown as flavour only |
| Qualification board — four lanes x six grades, one pick per grade | `Runtime/PerkCatalog.cs` | Stable | Grade 1 is the lane's OPS tool, grades 2-6 chain off it; a career holds two tools, so two lanes stay closed |
| Passive effects — fuel use, combat/service/objective reward, support cost, re-tasking tempo, effect size | `Patches/ProgressionPatches.cs`, `modules/Support/Runtime/SupportManager.cs`, `Actions/EmpAction.cs` | In-flight | Fuel/reward hook `Aircraft.UseFuel` + `FactionHQ.RewardPlayer`; the two support kinds are read by Support through `IPlayerPerks` (cooldown check, host reply, client countdown, EMP radius); rod blast scaling is deferred until a rod's detonation can be correlated to its request |
| SQD presentation — pilot dossier, shared skill board, friendly/hostile wings and pursuit HUD | `Presentation/SqdMfdPanel*.cs`, `Presentation/SqdGlyphs.cs`, `Presentation/EmblemRenderer.cs`, `Presentation/AceHuntHud.cs` (the ace line on the common HUD element) | Unverified | Uses `IProgressionView` and `ISquadView`; SKILLS is a grade-row matrix (four qualification columns, one row per grade) whose cell width, row height and one-screen fit are pinned by `SkillBoardLayout` and `SqdPanelTests`; a rail key sits above the columns, each cell states its own `PerkView.Block` in words and prints the grade's own effect ("+15% COMBAT") when it is takeable, the clause note is refreshed in place from `PerkPoints.RemainingToNext`, and the selected grade's description plus the single CONFIRM are pinned to the foot of the sheet; the friendly wing is a read-only roster and hostile wings carry a deterministic generated crest |
| Shared combat-skill presentation (AI/ace skills on the wing cards) | `Runtime/AceSkillCatalog.cs`, `SqdMfdPanel.Wings.cs` | Unverified | Display metadata over Wing Command's replicated four-bit `AbilityMask`; grants nothing |
| Wing Command custom-pilot studio (edit/save/recruit) | `Presentation/SqdMfdPanel.Studio.cs`, `Infrastructure/GameInterop/WingLink.cs` | Unverified | Additive companion API resolved separately; page fails closed on older Wing Command builds |
| Local squadron identity — procedural/PNG emblem, name, local pilot profile | `Runtime/EmblemDesign.cs`, `Presentation/EmblemRenderer.cs`, `Configuration/ProgressionSettings.cs` | Unverified | Client-local cosmetics; never networked |
| Ace bonus picks and one-life successor reset | `Runtime/ProgressionManager.cs` | Unverified | Score pays six grades; server-owned ace bonuses go on top, 20 total pick ceiling |
| Networking — protocol byte `4`, client polls while SQD open | `Networking/ProgressionNet.cs` | Unverified | Scene/request/pilot generation validation; host fast-path in-process; cosmetics are not on the wire; version 4 separates builds that disagree about grade ids |
| `PerkStrength` scaling of passives | `Runtime/ProgressionManager.cs` | Stable | 0 = cosmetic, 2.0 = double |
| Persistent cross-mission profiles | — | Absent | Gated on the persistence service (schema-versioned atomic writes); custom pilots persist in Wing Command's own folder |

Config: `Progression.Enabled`, `ScorePerPoint` (250), `MaximumPoints` (7), `PerkStrength` (1.0).
`Squadron.Name` / `Emblem` / `EmblemFile` / `PilotProfile` are client-local dossier cosmetics.
Debug: `Debug.BypassRequirements` (grants everything free — testing aid).

**Needs attention**
- In-game acceptance is pending for the four-page SQD redesign, the studio against the
  companion Wing Command build, and emblem/PNG rendering at panel scale.
- Balance dials to revisit once mission-length data exists: the grade ramp
  (`ScorePerPoint`), the pick ceiling, and whether the two-tool cap starves a solo career of
  useful picks late in a long mission.

---

## Support operations — `support`

OPS has four domain pages on the shared `AvScreen` chrome: SPACE (orbital station console:
PLATFORM / MISSION PLANNER / ENEMY ACTIVITY, plus the full-screen station uplink), CYBER
(spectrum-defence network: NETWORK / ARCHITECT / THREATS / OPERATIONS, plus the full-screen
network-defence console), SPEC OPS (base-of-operations doctrine, task-group programs, zone
fortification) and INTEL (network programs). One modular station per faction flies real orbits
and is visible in the sky; radar scan, ELINT, Rod from God and EMP need it overhead with the
module fitted, powered and recharged. Every airbase a faction holds is a CYBER node by itself
(Cyber Command on the central base, gateways on up to five more, all on one backbone, DOWN while
the base's tower is destroyed), and up to eight field trucks (real vehicles leaving the nearest
owned vehicle depot) extend it; once a truck is out a host adversary campaign probes, intrudes and jams it,
the console isolates, patches, baits and traces, a trace buys a foothold and an INTEL token, and
jammers raise an ECM umbrella against hostile radar seekers and back the attack operations; programs accrue
token reserves, SOF tokens buy doctrine ranks that make ground forces fortify more and leave more rappel encampments, and the intel reserve waits on theater events.
Host-authoritative, bounded snapshots, pure-model regressions and the protocol-13 wire
round-trip pass; in-game visual and multiplayer acceptance are pending. Specs:
`design/ux/ops-panel.md`, `design/ux/orbital-platform.md`, `design/ux/cyber-defense.md`.

**Purpose:** server-validated support requests, costs derived from vanilla unit value, one
`CostMultiplier`, typed denials, and host-owned orbital, CYBER network, doctrine and program state.

| Action (catalogue) | Id | Capability / perk | Status | Notes |
|---|---|---|---|---|
| Satellite Scan | `Recon` | `Recon` / Satellite Scan | In-flight | Immediate coverage-gated native tracking snapshot, capped at 48 contacts; **absent from catalogue** if the seam can't be resolved |
| Zone Fortification | `Fortify` | `Fortify` / Combat Engineering | Stable | Calls `IZoneFortificationService` with the doctrine's shell count (1 + FTD rank, capped by zone/theater ceilings); charged only after defenders verified. Absent if Urban Combat missing |
| Rod from God (kinetic strike) | `Artillery` | `Artillery` / Rod from God | In-flight | Native missile delivery with server-only 150 m core / 420 m blast; requires STRIKE coverage. In-game MP pending |
| EMP Shock | `Emp` | `Emp` / EMP Shock | In-flight | 30 km airburst: light-speed E1 prompt footprint and cockpit upset, E2 branching arcs, E3 geomagnetic heave holds the host-only 30 s jamming; local cockpit feedback. In-game MP pending |
| Flare Barrage | `FlareMissile` | **`Recon`** (shared) / Satellite Scan | In-flight | Airburst IR countermeasure. **DECIDED:** no dedicated perk — shares Satellite Scan / Recon authorisation |

| Cyber operation | Id | Gate | Status | Notes |
|---|---|---|---|---|
| Ping Sweep | `HackPing` | SIGINT LV1 | Pure-tested | Immediate ground reveal; radius scales with SIGINT |
| Track Uplink | `HackTrack` | SIGINT LV2 | Pure-tested | Re-stamps air tracks for a duration; radius/duration scale with SIGINT |
| Radar Blackout | `HackBlackout` | C2D LV1 + working, emitting (NOISE or DECEPTION) CYBER jammer within reach | Pure-tested | Native `Unit.Jam` on hostile units only; radius/strength scale with C2D |
| Ghost Shield | `HackGhost` | EWD LV1 + emitting jammer within reach | Pure-tested | Hostile tracking of your aircraft decays to stale blips (shared `CyberEffects` layer) |
| Spoof Contacts | `HackSpoof` | EWD LV2 + emitting jammer within reach | Pure-tested | Hostile track feeds overwritten with a false formation position |

Every operation is refused while the faction's Cyber Command is compromised, costs 25 % less
while a trace foothold is open, raises the faction's adversary heat, and is heard (and becomes
traceable) by any enemy network whose SIGINT post covers the target.

| Supporting piece | Where | Status | Notes |
|---|---|---|---|
| Orbital model | `Domain/Orbital/OrbitMath.cs`, `TheaterTrack.cs`, `PlatformModules.cs`, `OrbitalPlatform.cs`, `PlatformTelemetry.cs`, `PlatformWords.cs` | Pure-tested | Real period/velocity/elevation/off-nadir/slant; LOW/MID/HIGH bands with 55° reach passes and compressed far side; 5×3 grid, placement/strand rules, mass, kW/kJ power and brownout, radiator/relay/shield utilities, holds (insertion, transfer, rephase, safe mode), recharge, rods, fuel, debris, snapshot export/mirror with jitter hold |
| SAR image formation | `Domain/Orbital/SarImageFormer.cs`, `Visuals/SarCollector.cs` | Former pure-tested; collector in-game pending | Layover h·cot(inc), moving-target azimuth shift, shadow, sidelobes, two-look speckle, percentile dB stretch; ≤ 900 rays per frame over an 8 s collect |
| Station presentation | `Visuals/PlatformSky.cs`, `Visuals/SatelliteImager.cs`, `Visuals/SarCollector.cs`, `Presentation/PlatformUplink.cs`, `PlatformProducts.cs`, `SupportPanel.Space/Platform/Planner/Enemy.cs`, `Patches/UplinkInputGuardPatch.cs` | Built; in-game visual/MP pass pending | 1/20-scale cube-cluster station in the sky; full-screen uplink (EO/IR feed, radar product, taskings, input ownership); PLATFORM / MISSION PLANNER / ENEMY ACTIVITY pages |
| Infrastructure model | `Runtime/InfoNetwork.cs` | Pure-tested | Four facilities × 3 levels, prereqs, hack scaling, prices. CRYPTO `CostScale` discounts hack cost; copy must not claim a host cooldown discount |
| CYBER network model | `Domain/Cyber/CyberSites.cs`, `CyberNetwork.cs`, `CyberNetwork.Campaign.cs`, `CyberWords.cs` | Pure-tested | Two airbase kinds (host-raised Cyber Command and gateways, backbone, DOWN, root moves when its base falls) and four field kinds, 16 slots (6 airbase + 10 field), range links and BFS to Cyber Command, bandwidth pool, five console verbs with costs/recharges, relocation, loss/scrap; seeded adversary campaign (heat, phases, probes, intrusions, raids, heard enemy operations), honeypot hold, trace → foothold, INFOCON, notices; snapshot export/mirror with clock rebasing and garbage rejection |
| CYBER host runtime | `Runtime/CyberDefense.cs` | In-flight; in-game pending | Airbase infrastructure once a second (owned bases, central root kept while held, anchor building down/repaired); real vehicles per field site leaving the nearest owned depot's bay via `VehicleDepot.spawnTransform` (spawn, arrival, loss), EWR air reveals, SIGINT emitter reveals, ECM umbrella on hostile `ARHSeeker`/`SARHSeeker` `jamAccumulation` (host-simulated missiles only), probe exposure into the origin's tracking, trace rewards (INTEL token, emitter reveal), origin tables, operation reporting |
| CYBER presentation | `Presentation/SupportPanel.Cyber/CyberArchitect/CyberThreats/CyberOps.cs`, `CyberGraph.cs`, `CyberConsole.cs`, `CyberMapLayer.cs`, `CyberAlarm.cs`, `FullscreenInput.cs` | Built; in-game visual/MP pass pending | Four sub-pages, the geographic mesh widget, the full-screen console (keys 1–5, Tab, Q/E), map sites/links/covers/incidents/placement preview, the klaxon, shared full-screen input hold |
| Track deception | `Runtime/CyberEffects.cs` | In-flight | Bounded: 4 effects, 64 aircraft, hostile tracking dictionaries only |
| Map overlay | `Presentation/SupportMapOverlay.cs` | In-flight | Station and foreign-station tracks, uplink aim, station readiness on the armed reticle, the CYBER map layer |
| Camera surface mark | `ICameraTargetService` (Support) + Command TGT CAMERA tab | In-flight | Capture/call/clear plus telemetry; OPS no longer hosts it |
| Map-cursor target resolution | `Runtime/SupportTargeting.cs`, `Runtime/SupportMapGesture.cs` | Stable | Clearance-sphere / slope tolerance retained |
| OPS domain models | `Domain/OpsDomain.cs`, `TheaterGrid.cs`, `EwPostures.cs`, `InfoOperations.cs` | Pure-tested | Four tab labels and doctrine, operator formatting (grid, countdown, GET), the jammer mode rule (any emitting mode backs every station operation) and umbrella strengths the host enforces, operation gate order (doctrine → C2 breach → jammer → emitting) and copy |
| Programs (SPEC OPS / INTEL) | `Domain/OpsPrograms.cs` | Pure-tested; in-game pending | Six programs × 3 tiers, two reserves capped at 8 tokens, host-only accrual (5 s tick clamp), all-or-nothing `TryConsume` seam; the SOF reserve pays for doctrine ranks. Client mirror clamps hostile bytes |
| Base of operations (SPEC OPS) | `Domain/OpsGarrison.cs`, `Framework/Contracts/IGroundForceReadiness.cs` | Pure-tested; in-game pending | Two doctrine tracks × 3 ranks (2/3/4 SOF tokens); ranks raise fortification shells and rappel camps, read by Urban Combat through the owner-scoped readiness contract (absent = 1); snapshot mirrors clamp hostile bytes |
| OPS panel | `Presentation/SupportPanel.cs` + `.Space` / `.Cyber*` / `.Programs` | Built; in-game visual/MP pass pending | Shared `AvScreen` chrome, four metrics (the third is `CYBER on-net/nodes` + INFOCON), four tabs, fixed-height rows with status words + rail, disabled reasons in hover help, ~6 Hz refresh of the visible page only |
| Missile visual patch | `Patches/SupportMissileVisualPatch.cs` | Stable | |
| Request pipeline, cooldown, rate limit, typed denials, 5s silent-host timeout | `Runtime/SupportManager.cs`, `Runtime/SupportModel.cs` | Stable | |
| Networking — protocol byte `13`, host fast-path | `Networking/SupportNet.cs` | Stable | Request/result, ops query/command (module launch, jettison, rephase, orbit shift, resupply, invest, garrison upgrade, CYBER build/scrap/verb/mode/move)/state (station layout, outages, band/seed/clock/hold, energy/fuel/rods/brownout, cargo, recharge, notice; foreign stations with layout masks; facilities; CYBER sites with airbase static/down flags, incidents, footholds, notices and origin names; programs, doctrine) roundtrip and over-bound site count rejection in the patch probe |

Config: `Support.Enabled`, per-action toggles (`ReconSweep`, `ElintSweep`, `Fortification`,
`RodFromGod`, `EmpShock`, `FlareBarrage`, `CyberOperations`), `CostMultiplier`, per-action
cost/range/radius keys, `PlatformCostScale` (1.0), `PlatformJettisonRefund` (0.4),
`PlatformInsertionSeconds` (45), `PlatformDockingSeconds` (20), `PlatformDebrisEvents`,
`OrbitGapScale` (1.0), `MaximumRangeMeters` (30 km), `RequestCooldownSeconds` (30),
`ElectronicWarfare` (spectrum defence), `CyberSiteCostScale` (1.0), `CyberScrapRefund` (0.5),
`CyberSiteLimit` (8 field sites, 1-10), `CyberCampaignIntensity` (1.0, 0 = off), `EwProximityRadiusMeters`
(15 km), `FireMissionDefinitionKey`. `EwTruckCost` is gone. Debug: `Debug.DisableOpsCooldowns`.

**Needs attention**
- **Flare Barrage** is **DECIDED**: shares Satellite Scan / Recon authorisation; no extra
  perk. README, OPS copy and this table document it.
- CRYPTO copy must not claim a cooldown discount; host `RequestCooldown` is unchanged.
- CYBER in-game acceptance: airbase nodes on every owned base with Cyber Command on the central
  one, a destroyed tower reading DOWN and returning on repair, a captured base changing hands,
  trucks leaving the nearest depot's bay and arriving, incident cadence and balance, the umbrella
  actually breaking an AI radar missile, console input release, listen host and remote client.
  SPECTRUM mode, decoy/HPM/nav-denial sites and capstones are later slices.
- Roadmap gates: multiplayer + long-session validation.

---

## Tactical command — `command`

MAP panel update: Layers / Readability pages on the shared section-and-grid language —
a two-column switch grid over the six game layers plus Boscali's Control field and Front
line overlays, All on / Hide all / Defaults presets and a live overlay readout; Readability
keeps the native hover/size choices and adds a scaled preview and the overlay legend.
Unavailable map or overlay state disables controls and says why. In-game marker effects,
font/theme and input acceptance remain pending.

Faction Resources update: larger stockpile cards and 60-sample local trend graphs,
adaptive force/status lists and labeled ledger comparisons. Morale is stored by
`CommandManager` (eight factions, mission reset, initial 100/100), with public
`FactionResources` host-side access. No effects, replication or disk persistence yet;
remote clients display unavailable. Build/pure checks cover storage and history;
in-game visual and multiplayer acceptance remain pending.

**Purpose:** owns the expanded tactical-map GUI, the `STR` strategic bezel screen and map
overlays. The old CMD tactical-command code (doctrine, per-cell Sector Focus, map
right-click menu, AI target scoring) stays removed; the tab is now the operations board
consuming TheaterOps' `ITheaterPriorityView` and issues no unit order of any kind. Largest
and most-churned module. Never tasks a recruited Wing Command wing.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Expanded tactical map UI — left MFD dock + event log, central map, right bezel rail, spawn footer | `Presentation/MapUi/` (~30 files, `MapUiManager.cs`) | In-flight | Patches `MfdRailPatch`, `MfdScreenChromePatch`, `MfdSinglePanelPatch`; `VanillaMfdRebuild.*` partial classes are new; `Command.ExpandedMapUi` default on |
| `STR` strategic screen — SA (air + frontline) / COC / CMD operations board | `Presentation/StrMfdPanel.cs`, `Presentation/StrMfdPanel.Coc.cs`, `Presentation/StrMfdPanel.Cmd.cs`, `Domain/TacticalTheaterState.cs`, `Domain/TheaterReadout.cs`, `Domain/SortieClassifier.cs` | In-flight / Unverified | Own bezel; `ITheaterPage` gone. Empty-board air/territory ratios print "—", not 50%. CMD names the main effort through `ITheaterPriorityView`; TASKING/LOG stay removed |
| Dynamic frontline / sector-control overlay | `Runtime/TacticalSectorGrid.cs`, `Runtime/SectorClusterTree.cs`, `Runtime/SectorContour.cs`, `Runtime/TerritoryControlView.cs`, `Presentation/ComMapOverlay.cs` | Unverified | Cells sit on the map's own base grid (its grid offset, 1 km minor squares, coarsened in powers of two only if a theater exceeds the 16384-cell budget); the bake draws `SectorClusterTree` clusters, so the rear is a few blocks and the front stays fine. Marching-squares contour: real positions, local normals, lengths and per-stretch pressure; forward-band tint only, contested squares hatched red/blue with the stripe split following the cell's control value (no third colour), the front itself one plain vector line (`Presentation/MapUi/FrontlineGraphic`: map-pixel width and sampling, one colour, no teeth), front length in km. Enclosed ground with no opposing presence (troops or an airbase anchor) is claimed for the enclosing side. Both map overlays share `Infrastructure/GameInterop/TheaterFrame`. Objective ground presence independent of faction tracking. Advisory only — vanilla capture unchanged. In-game validation pending |
| `MIS → SECONDARY` objectives view | `Presentation/MapUi/MfdSecondaryObjectives.cs` | Experimental | Reads `ISecondaryObjectivesView`, including the host's `ActiveLimit`; adaptive dossier grid (1–4 dossiers) with `AVAILABLE` / `ACTIVE` / `CLOSED` filters; only live when `dynamic-operations` is enabled |
| `MAP` layer/readability screen | `Presentation/MapUi/VanillaMfdRebuild.Map.cs` | In-flight / Unverified | Two-column switch grid over the six native layers plus Boscali's Control field, Front line and Threat heat overlays; All on / Hide all / Defaults presets; live overlay readout from `ComMapOverlay` and `ThreatMapOverlay`; native hover-tooltip and symbol-size choices, scaled symbol preview and overlay legend. Cells size from the panel height, so both pages fit `PanelHeight` through `PanelHeightMax`. In-game visual acceptance pending |
| `SET` MFD settings page — CLIENT sub-tabs MAP / STYLE / IMAGE / COCKPIT | `Presentation/MapUi/SettingsMfdPanel.cs` | In-flight | Shared `AvScreen`; two main tabs CLIENT / SERVER; compact rows; panel resolves up to `PanelHeightMax`; COCKPIT reads QoL's `IThirdPersonHud`; bounded steppers explain disabled limits |
| `SET` SERVER page — host tasking board + host-authoritative settings | `Presentation/MapUi/SettingsMfdPanel.cs`, `Presentation/MapUi/SettingsServerPage.cs` | Unverified | The ADM bezel's tasking board relocated here (`ISecondaryObjectivesView`, late through `ModServices`); every installed feature's `IHostSettingsView` from the framework `HostSettingsBoard` renders as a host-only row. `GameAccess.IsServer()` re-read each refresh; remote clients see it read-only with the reason on the status strip. Only live-read gameplay knobs; startup `Enabled` gates stay in the config file |
| TGT target presets / quick slots / native-radial page | `Presentation/MapUi/TargetPresetModel.cs`, `TargetPresetRuntime.cs`, `TargetPresetRadialPage.cs`, `Runtime/TargetPresetHotkeys.cs` | In-flight | Bounded config-persisted library; F6/F9/F10; Autopilot hosts the page through `IRadialMenuPage`. In-game acceptance pending |
| Wing Command coexistence (NOAvionics, no assembly dep) | `Presentation/MapUi/`, `Infrastructure/GameInterop/MfdBezel.cs` | Stable | Named same-frame bezel claims plus exclusive armed map gestures. EVN is hosted as an appended vanilla slot (`Infrastructure/GameInterop/MfdScreenHost.cs`), so Boscali claims five and Wing Command's list-scanning WMC installer keeps one free. The former ADM appended slot is gone with the bezel |
| Hostile sensor-coverage heat map (one field over all tracked emitters) | `Presentation/ThreatMapOverlay.cs`, `Domain/ThreatEnvelope.cs` | Unverified | Client-local, no patches, no wire. Each spot is as hot as the best tracked emitter would find the local aircraft there: the game's own gates (`maxRange / minSignal × RCS^0.25`, radio horizon from both altitudes, twice-nominal scan limit) for radar and `min(detector sweep, visibility × magnification)` for optics, merged per cell by maximum with a cubed radial falloff (`ThreatEnvelope.Heat01`, squared again in the alpha so the far field stays silent). Optical coverage is weighted to half intensity, so an optical-only watcher reads as a soft amber patch. Tracked contacts only, at the faction-known position; jammed radars drop out. Tuned against the measured theater: 81920 m across, while a ground radar's radio horizon against a high target reaches past 200 km, so envelopes cover the map — hence the steep falloff and the panel's widest-reach readout. Cost: one stretch-anchored quad and one texture (≤256 px on the long side), no per-emitter objects, no per-frame work, a 1 Hz bake confined to each emitter's bounding box, flat pre-allocated buffers, rendering asleep while the map is closed. Terrain LOS, look-down clutter and scan cones are not cut out (they only shrink a real envelope). In-game validation pending |

Config: `Command.Enabled`, `ExpandedMapUi`, `FrontlinesOverlay` (control field), `FrontlineTrace`
(front line trace), `ThreatHeat` (hostile sensor coverage), `OverlayOpacity` (0.35),
`GridCellSizeMetres` (1000, the map's base grid
square), `GridRefreshInterval` (0.5s), `TargetPresetWheel` (on), `TargetPresetKey1/2/3`
(F6/F9/F10), `TargetPresets`/`TargetPresetSlots` (managed by TGT). The MAP bezel's layer
switches write the three overlay entries, so the config file stays the single source of truth.

**Needs attention**
- COM copy is settled: `STR` is its own bezel, `ITheaterPage` is gone, DESIGN_NOTES matches.
- `STR` and the frontline overlay still need a live mission pass.
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
| 1 Hz host director — 17 contract types including rescue/return, recon/BDA, logistics, repair cover, jammer hunts and surveys | `Runtime/OperationsManager.cs`, `Runtime/OperationMissionPool.cs`, `Domain/OperationBoard.cs` | Experimental | 8 boards / 3 cards / 2 active / 128 issued per faction per mission; escalation-aware generation every 18–30 s, 1 faction/tick |
| Escalation-aware tempo — generation interval + fresh-offer reward scale | `Domain/OperationTempo.cs` | Pure-tested | 30/24/18 s and 1.0/1.15/1.35 at conventional/tactical/strategic; reads only the mission's `tacticalThreshold`/`strategicThreshold` (unset is not a gate); stacked under the money/XP and `RewardMultiplier` clamps |
| Follow-on chains — a paid completion may seed one related next contract | `Domain/OperationChains.cs`, `Runtime/OperationsManager.cs` | Pure-tested | Capture→Defend; Recon/SortieReport/DamageAssessment→Interdict; SupplyEscort→SupplyInterdict; Jam/ElectronicWarfare→Intercept (Rescue/BattlefieldSurvey exhausted); 2 links per operation, one pending follow-on per board, skipped when no candidate exists; never on cancel or expiry; no new wire fields |
| Abort consequence — deliberately aborting an accepted contract costs 1 morale | `Domain/OperationFailure.cs`, `Runtime/OperationsManager.cs` | Pure-tested | Through the existing local host `MoraleAwarded` event; dismissing an offer or letting it lapse stays penalty-free; the dismissal message states what happened |
| Native rescue, repair and supply observations; continuous surveys and same-aircraft intelligence return | `Runtime/OperationServicePatches.cs`, `Runtime/OperationMissionPool.cs` | In-game acceptance pending | Release/pure/patch checks pass; 2 unit passes/generation, 32 sightlines/tick and 32 recent jammer sources; no new spawns |
| One-time faction money (normal tax) + mission-score XP awards | `Runtime/OperationRewards.cs` | Experimental | `RewardMultiplier` 0.25–4; team award, no individual attribution |
| Special outcomes — 3 native DEF buildings on capture, 6-vehicle convoy on defend | `Runtime/OperationsManager.cs` | Experimental | 120s faction cooldown, 24-object ceiling; convoy needs a connected road |
| Acceptance/dismissal and client snapshots (protocol 2) | `Networking/OperationsNet.cs` | Experimental | Own-faction validated IDs and bounded rate limits; no client completion/reward data |
| Accepted-objective markers, adaptive MIS board with a truthful contract tab, host morale +3 / hostile target faction -3 | `Runtime/OperationZoneHud.cs`, Command MIS presenter (`Presentation/MapUi/MfdSecondaryObjectives.cs`, `VanillaMfdRebuild.Mission.cs`) | Experimental, in-game acceptance pending | The board reads the host's `ActiveLimit` instead of hardcoding the ceiling; offers read `#N TITLE` and the HUD's `T-mm:ss` clock, the ACCEPT button is one of `ACCEPT CONTRACT` / `ACTIVE LIMIT REACHED` / `OFFER ENDED` (never enabled on a lapsed offer), pay and threshold figures are culture-invariant (`$1400`, never `$1 400`), dossier heights never overlap the row pitch, the empty tab names the real reason (unavailable / link lost / limit reached / director exhausted), and SET's tasking rows request a snapshot at most every 2 s instead of freezing when the map is closed |
| Contract markers drawn by the mod, in the game's own likeness - cockpit HUD, tactical map and the contract feed | `Runtime/ContractHud.cs`, `Runtime/ContractMarker.cs`, `Runtime/ContractMapHud.cs`, `Runtime/ContractMapTag.cs`, `Runtime/OperationZoneHud.cs`, `Domain/ContractMarkerLook.cs`, `Domain/ContractMarkerMath.cs`, `Domain/ContractSelection.cs`, `Domain/OperationMarkerCopy.cs`, `Infrastructure/GameInterop/VanillaHudStyle.cs` | Pure-tested layout, look and copy, in-game visuals pending | Every sprite, font, colour and number is read read-only from the game once per scene (`VanillaHudStyle`: `ObjectiveOverlayManager.overlayPrefab`, `ObjectiveMarkerManager.markerPrefab`, the four `ObjectiveMarker` icon sprites, `MapMarker.markerImg`, `GameAssets.exclusionZoneDisplay`, `ThemeManager.Active`, `PlayerSettings`): cockpit pointer (25 px) and dot (10 px) with vanilla's axis test, the vanilla area-ring sprite with vanilla's angular scale and `RingAlpha` fade, and text at vanilla's *effective* size - `overlayTextSize` carried through the label rect's 0.5 scale (16 px at the default 32), never the raw font size, which is what made the first attempt read twice as loud as the objective beside it. Vanilla map icons at 20/40 px with the map label at its own 0.5 scale, plus vanilla's label glide and 50 px anti-overlap nudge. Distinctive by the `#N` number, the second line (`STRIKE 9.4km T-2:41`, vanilla distance formatting, metric or imperial) and the vanilla warning colour inside two minutes. The card is plain HUD text at the right edge, no panel: header, one line per contract, a bar. **No vanilla marker, overlay or label is written to, re-parented, patched or fed, and nothing goes into `MissionPosition`** - the style read fails closed with one warning - the earlier borrowing version was what came back buggy (pooled markers re-handed between objectives, vanilla's label nudger) |

Config: `DynamicOperations.Enabled` (**false**), `DynamicOperations.RewardMultiplier` (1.0).
Full MIS panel also needs `Progression.Enabled` + `Command.Enabled` + `Command.ExpandedMapUi`.

**Needs attention**
- The mod-drawn markers and the contract feed are new and unverified in game: check the cockpit
  marker on screen and clamped at each frame edge (including a target behind the aircraft), the
  pointer/dot switch as the nose crosses the target, the area ring against a vanilla capture
  ring at the same radius, the marker and card text sitting at the same size as a vanilla
  objective label beside it (16 px at the default text size), the second line and the `#N`
  prefix, three contracts in one direction nudging apart instead of stacking, the map icons at
  20/40 px and the 12 px map label at minimum and maximum zoom, `MapOptions.showObjectives` off,
  the card with an inside contract / a lost contact / three contracts at once, and the enter and
  leave banners. Also confirm the log's one-line warning appears (and markers stay hidden) if
  `VanillaHudStyle` cannot read the game's marker style.
- The MIS contract tab's reworked states are unverified in game: watch an offer's last seconds
  (button must flip to `OFFER ENDED` and stop being clickable), a full roster (`ACTIVE LIMIT
  REACHED`), the pay line on a pl-PL machine (`$1400`, never `$1 400`), two-row boards (no card
  may cover the buttons of the card above), an unavailable host link (the tab must say so rather
  than promise offers), and SET > SERVER with the tactical map closed (tasking rows must keep
  updating, offers included).
- Default-on gate (see [DYNAMIC_OPERATIONS.md](DYNAMIC_OPERATIONS.md)): single-player
  capture/defense/interdiction + tax/score awards; convoy path & supply behaviour;
  blocked/partial-spawn cleanup; listen-host / remote-client / late-join faction views;
  pause/resume, faction switch, mission reload, disable cleanup. **None proven** — build and
  pure tests can't cover any of it.
- Four `RESEARCH_DYNAMIC_*.md` docs are untracked in git alongside this — decide if they are
  committed reference or scratch.

---

## Chain of command — `high-command`

**Purpose:** generated faction staff as living battlefield assets - real command posts on the
map, VIP convoys, per-commander bonuses and kill pay. **Default on.** New module. Publishes
`IHighCommandView`; Command's STR console adds a COC page and a COMMAND metric. The board is
read-only: no orders, marks or spends exist. Effects are funds/score and information only — no
vanilla AI, spawn or damage behaviour is touched.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Deterministic roster — 6 posts, names, traits, bios, Wing Command portraits | `Domain/CommanderGenerator.cs`, `Domain/CommandTree.cs`, `Runtime/HighCommandManager.cs` | Pure suite passes | Seed-stable across host/client; portrait sprite borrowed from Wing Command's generated pilot pool (never destroyed here) |
| Per-commander bonuses (income, kill value, patrol reach, cost of loss) | `Domain/CommandTraits.cs`, `Runtime/HighCommandManager*.cs` | Pure suite passes | Stated in words as a bounded bonus line; every effect is economic or informational; no spawn, retask, damage or AI effect |
| Spawned command posts, last-damage kill credit, succession | `Runtime/HighCommandManager*.cs`, `Patches/HighCommandDamagePatch.cs` | Awaiting in-game validation | Death via `Unit.onDisableUnit`; kill pay to the hostile faction that dealt the last damage (no marking step); destroyed post rebuilt after `PostRespawnSeconds`, successor disrupted meanwhile |
| VIP convoys and intel fog | `Runtime/HighCommandManager.Assets.cs` | Awaiting in-game validation | 1 transfer/faction, 8 convoys; lead vehicle carries the VIP; 45s intel memory, 2.6/4.2km reveal; unconfirmed enemy posts withhold position and transit |
| Map markers for own and confirmed posts | `Presentation/CommandPostMarkers.cs`, `Domain/CommandMarkerPolicy.cs` | Awaiting in-game visual check | One pooled, tier-sized diamond per post the view lists as friendly or known, ringed while under fire, parented to `DynamicMap.iconLayer` and scaled by the map transform; no marker for a dead post, no map gesture, `MapMarkersEnabled` hides the layer only |
| Protocol-4 read-only snapshot, per-faction scoping, global post ids, bounded staff log | `Networking/HighCommandNet.cs`, `Domain/CommandLog.cs` | Awaiting in-game validation | Both staffs listed by identity; global id folds in the faction index; 32-node ceiling; 6-row/64-char log arrays with an alert flag; the only client intent is a rate-limited refresh; hostile log entries filtered by the observer's sight record |
| STR COC page and third COMMAND metric | `modules/Command/Presentation/StrMfdPanel.Coc.cs`, `Domain/CommandRosterOrder.cs` | Awaiting in-game visual check | Chain of command in the left column and the selected commander's **personnel file** in the right: form number, photo with reference, name and office, RANK/STATION fields with leader dots, a tilted disposition stamp in the state's ink, share of staff, the bonus as file entries and the service record - or redaction bars while an enemy post is unconfirmed. Every portrait is cropped into its own masked plate, so plates are uniform whatever shape or pivot Wing Command's sprite carries. The card is laid out against the text it holds and its frame then runs to the column's bottom. Four-row STAFF LOG under the tree; rows are re-ordered parents-first with trunk guides, a per-post share track, the state right-aligned and the office on its own full-width line; UNDER FIRE state and a row flash on change; nothing to press. Opening a post's file brackets that post's diamond on the map (lifted ink, drawn above the others) until the file closes; with no staff the page says NO STAFF BOARD instead of leaving the column empty. `Run-CocUnityCheck.ps1` renders the real page offline (596/896, allied/hostile/no post/no staff, mixed portrait shapes) for visual review and asserts the map highlight matches the open file |

Config: `HighCommand.Enabled` (true), `EconomyEnabled` (true), `StipendIntervalSeconds`
(120), `MaximumStipends` (10), bounties 1500/3000/6000, `MarkedBountyPercent` (50),
`CommandPointsMaximum` (12), `TransfersEnabled` (true), `TransferMinSeconds`/`MaxSeconds`
(180/420), `DisruptionSeconds` (120), `PostRespawnSeconds` (60).

**Needs attention**
- In-game acceptance is **pending for every behaviour**: post placement on live terrain,
  convoy pathing and arrival, kill/bounty attribution, intel timing, page layout, and
  single-player/listen-host/remote-client/late-join snapshots plus scene reload.
- Enemy AI has no reason to attack command posts; the mechanic currently rewards player
  strikes (LARP + economy scope). AI interest would be a deliberate future slice.
- Kill-list marks are economic only; they do not steer friendly AI.

---

## Theater operations — `theater-ops`

**Purpose:** the theatre operations layer behind the CMD page: the faction's main effort,
funded reinforcement calls, the readiness picture, the replicated read model and the map
marker. **Default on.** New module. Publishes `ITheaterPriorityView` and
`ITheaterLogisticsView`; Command's STR console adds the CMD operations board. It never
selects, spawns, retasks or moves a unit; two postfixes on pure `MissionPosition` queries
plus the vanilla convoy funding path are its whole world effect.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Host main effort — one active objective per faction, seeded from the faction's own objective list | `Runtime/TheaterPriorityService.cs`, `Domain/PriorityDirective.cs` | Pure suite passes | Keyed by `Faction.factionName`, 8-faction/12-option ceilings; hidden objectives never listed; non-finite positions rejected; scene reset clears |
| Reinforcement delivery bias | `Patches/MissionPositionPriorityPatch.cs` (`TryGetClosestDistance(FactionHQ, Transform, out float)`) | Awaiting in-game validation | `FactionHQ.SortDepots/SortAirbases` pick the depot/airbase nearest the effort, so the next vanilla convoy or AI flight arrives there |
| Advance bias | `Patches/MissionPositionPriorityPatch.cs` (`TryGetClosestPosition(Unit, out GlobalPosition)`) | Awaiting in-game validation | Ground vehicles, mobile artillery and idle aircraft with no contact head for the effort instead of the nearest objective; clearing restores vanilla on the next query |
| Funded reinforcement — mission convoy group paid from the shared faction pool | `Runtime/TheaterLogisticsService.cs`, `Domain/ReinforcementGate.cs` | Pure suite passes | Vanilla gates enforced host-side: `preventDonation`, the group's own cooldown (`CmdGetDelaySpawnConvoy`) and `GetCost()` against `factionFunds`; delivery is the vanilla supply queue spawning at the delivery bias above. 8-row ceiling |
| Readiness — units awaiting rearm, ready/tracked and depleted rearm assets | `Runtime/TheaterLogisticsService.cs` | Awaiting in-game validation | Local read of `RearmMissionController.Rearmers`/`UnitsNeedingRearm` on every peer, capped at 256 assets; unobserved reads as dashes, never zeroes |
| Host priority replication (protocol 1) | `Networking/TheaterOpsNet.cs` | Roundtrip probed | One state per changed faction; a client query is answered with one state per set faction, throttled per player, bounded at 8 factions and 64 querying players; clients never set or clear it |
| Effort map marker | `Runtime/TheaterEffortMarker.cs` | Awaiting in-game visual check | Client-local diamond on `DynamicMap.iconLayer`, transform maths mirrored from the game's objective markers; `TheaterOps.MapMarkerEnabled` |
| STR CMD operations board | `modules/Command/Presentation/StrMfdPanel.Cmd.cs` | Awaiting in-game visual check | MAIN EFFORT card + CLEAR EFFORT, selectable objective rows (read-only on clients), REINFORCE rows with pool/cooldown/affordability, READINESS counters |

Config: `TheaterOps.Enabled` (true), `MapMarkerEnabled` (true). Priority is mission-scoped
and never persisted.

**Needs attention**
- In-game acceptance is pending for everything: reinforcement arrival near the effort, the
  cooldown/affordability copy, the replicated client view (query on join, change broadcast,
  late join), ground push direction, and the board's layout/input/live visuals.
- The replicated read model is informational only. When clients get anything actionable,
  their intents must be host-validated; do not grow a client-driven mutation path.

---

## Trenches — `trenches`

**Purpose:** natural front-line fieldworks — a Bezier trench curve fitted to Command's ordered
front traces (beachhead rings, diagonal fronts, whole frontiers), sited on the owned side over
the flattest low ground the planner can reach, carved as a terrain-conforming ditch, matured
into a belt, defended by native emplacements and marked on the theater map.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Domain math — Bezier resampling, owned-side offset planning, traversed densify, run splitting, stage gates | `Domain/TrenchTraceMath.cs` | Stable | Pure C#, verified by unit tests (no UnityEngine types) |
| Trace intake — ordered contour polylines from Command's control field | `Framework/Contracts/ITerritoryIngress.cs`, `modules/Command/Runtime/TacticalSectorGrid.cs` | Stable | 4096 points / 64 traces, flat buffers, stitched on demand; a pocket ring repeats its first point |
| Planner — trace to position: resample, signed-control side, depth search, ground-refused runs | `Runtime/TrenchPlanner.cs` | Pure regression passed; in-game acceptance pending | 2400m / 320 stations per position, 5 candidate depths (56–104m), ≥140m runs, wraparound tangents on closed rings. Each window is cut by arc length on the raw contour points and resampled at the fixed ~10m curve spacing; the earlier trace-wide resample widened the spacing to `length / 320` (about 150m on a real front) and produced eight-station slabs. Stations are indexed window-locally — the earlier `windowStart + station` mix fitted the second window to the wrong stretch and read stale stations past the buffer's end. The side comes from `TryGetHoldStrength` sign through `TrenchTraceMath.TryResolveInward` (40m→8km probe ladder), never from `SectorControl.Friendly`: a real front is a contested band and the Friendly-only gate placed nothing. Ground counts when the hold is on the faction's side **or inside its contested band** (`HoldOwnSideFloor`), because the field is one value per kilometre cell and a ragged front reads hostile-leaning on its own side. Each refusal is named (`TrenchRefusal`) |
| Curve model — stations, inward vectors, chosen depths, anchors, belt traces | `Runtime/TrenchLine.cs` | Stable | 64 anchors ≈ 60m apart and ≤32 fire-bay nodes ≈ 20m apart (`Nodes`, recomputed in `Validate()`); support/redoubt/link/spur traces attached as the belt grows |
| Scene manager — faction scan rotation, spacing, growth ticks, retirement, cleanup | `Runtime/TrenchManager.cs`, `Runtime/TrenchTerrain.cs` | Awaiting in-game validation | Reset order 60; one plan attempt per 2s; 360m/250m centre spacing; 16 positions; host only; terrain probe is dry ground past the slope floor (`MinimumNormalY`, a hillside digs, a cliff does not). The scan walks one window per attempt and keeps its cursor across the 5s trace refresh — resetting it (the earlier shape) restarted at the first window of the first faction every time, so a long front was never covered. Logs front-trace intake and, at most once a minute, an entirely refusing front with the refusal reason; ownership overrun is the hold floor past the line centre |
| Growth — Scrape → FireTrench → Support (150m trace + links) → Redoubt (300m trace) → Saps | `Runtime/TrenchPlanner.cs` (`TryGrowBelt`) | Unity regression passed | Atomic per stage with retry on refusal; default 45s per tick; the nest chain grows 2 → 3 → 5 → 7 → 8 with the stages (four MG first, then ATGM, then MANPADS), each adding a bay and its crew. The belt sits at those depths on purpose: fire/support/reserve lines are what make a position read as doctrine-dig fieldworks instead of one ribbon |
| Combat — 8 in-ditch nests (4 MG, 2 ATGM, 2 MANPADS) with a crewman each, suppression, permanent losses | `Runtime/TrenchGarrison.cs` | Awaiting in-game revalidation | A chain of nests on the fire-bay nodes, standing in the ditch 0.4m forward of the centreline; the sandbag ring is a `dugout` child part vanilla cannot remove over the network, so every peer hides it locally in `TrenchNestVisual` while keeping the part registered and the root hitbox intact; a blocked bay walks to a neighbouring node before the slot gives up, and its crewman follows the nest to that bay; establishment stands as long as one of the two opening MG teams spawns (requiring both discarded the whole dug position two times in three in a live session). The footprint query ignores terrain colliders — on real ground the box clipped the surface and every placement was refused — using a bounded `OverlapBoxNonAlloc` buffer that fails closed when saturated; a solid obstacle still refuses, and a rejected position now names its cause (`LastFailure`: no spawner, missing definition, no anchor, outside the position, uneven ground, blocked, spawn null); damage pauses construction 60s; no healing or replacement. The game has no infantry unit, so soldiers are its dismounted-pilot figure (`Spawner.SpawnPilot`), one crewman per nest (2/3/5/7/8 by stage), standing in the ditch at its nest's own bay node facing the threat and spawned only after that nest has landed; the prefab is found by component in the encyclopedia's instance lists and a missing one stays a named refusal with the position manned by emplacements only. A killed soldier is a permanent casualty and never feeds `Alive`/`Overrun` or the suppression timer |
| Works — infantry-scale vanilla scenery on the ditch line (HESCO/sandbag/light gabion) | `Runtime/TrenchWorks.cs` | Awaiting in-game revalidation | 8 works/position on the anchor bays; even slots on the parapet crest as fire positions, odd slots 7m behind the anchor as shelters clear of the rear skirt; runtime keyword+footprint filter (≤6m) rejects vehicle-scale pieces; a live host produced zero works because the static `Encyclopedia.Lookup` dictionary was never populated — the catalog now resolves from `Encyclopedia.i`'s instance lists (`scenery`, `otherUnits`) matching `jsonKey`/`unitName`, with a bounded one-time near-miss line so the keyword list can be widened from evidence; selected keys logged once; no match stays ditch-only |
| Ditch mesh generator — carved earthwork profile conformed to each side's ground | `Visuals/TrenchMeshBuilder.cs` | Unity render passed | Zero terrain edits; 10-point cross-section re-derived per ring (so a fire bay flares as one earthwork) and mitred at every traverse corner, capped at both ends; man scale — a ~1.6m cut with a 0.8–1.1m walkable fire-step floor, a parapet 1.2–1.45m above ground, a lower parados, ~0.9m spoil berms and ~0.7m skirts, a total footprint near 4.8m (measured in the harness: half-width 2.41m, crest 1.24m, floor 0.88m) — because the work must read correctly beside the 1.8m models that stand in it; live screenshots showed the earlier ~13m/3.4m profile as blocky knee-high pods. A fire bay every ~20m widens it +1.4m (measured 3.11m half-width) so each nest and its crew stand inside the cut. Outer berm/skirt sample terrain per side; smooth spoil irregularity; no procedural strongpoints. The obstacle band is a forward parapet wall (bottom at ground) so the crew stands on the ditch floor while vehicles are still stopped by the berm. The traverse wave is a subtle zigzag (9m period, 0.5m amplitude). `BuildWireBeltMesh` is the procedural wire belt (crossed pickets + two strands) draped 7m in front of the ditch line |
| Material resolver — procedural ditch cross-section palette | `Visuals/TrenchMaterialResolver.cs` | Unity render passed | No external bundles; native URP lighting; one baked 256px texture per scene |
| 3-tier flight LOD chunks — the curve densified and traversed into one continuous ditch | `Visuals/TrenchVisualChunk.cs` | Awaiting in-game flight check | `TrenchTraceMath.DensifyTraversed` lays the subtle 9m/0.5m traverse wave phased by world position, so neighbouring positions continue one pattern. Every LOD is real earthwork at a coarser ring pitch — 3.5m with the wire belt (7m ahead, 0.75m pickets), ≤48 obstacle boxes and colliders < 600m, man-scale berms to ~2.6km, and the one deliberate exaggeration at the far end: a bold 18m ridge silhouette (now about 4.7m wide, 1.8m high, since the man-scale profile itself would be invisible) out to `LODFarDistance` (12km) — never a flat scar, so the front still reads from cruise altitude. The distance test converts the line's global centre into local space and prefers `CameraStateManager.mainCamera`: mixing the global centre with the local camera measured the floating origin (a live chunk logged 14.6km while the player stood beside it) and parked every earthwork at LOD3, fully culled. A failed ground probe keeps the curve's own height instead of y=0, which used to bury rings under the terrain and spike the mesh |
| Tactical map overlay — NATO APP-6 crenellated fire line, strongpoints, stage ticks | `Presentation/TrenchMapOverlay.cs` (`TrenchMapGraphic`) | Awaiting in-game visual check | Reset order 61; one Canvas UI mesh layer under `DynamicMap.mapImage`, no baked texture. Curves are drawn in map-local units at a constant screen width (dark under-stroke then ink), so zoom magnifies the curves instead of pixelating blocks; fire line solid, support/redoubt/link/sap traces dimmer and thinner, one strongpoint mark per bay, stage ticks and a crossed-out centre. Rebuilds on a line change, a zoom step or a faction change only; Command's front symbol already shows the contested trace |

Config: `Trenches.Enabled` (true), `GrowthIntervalSeconds` (45s), `MaxNetworks` (16, max 16),
`LODNearDistance` (600m, 100–2000), `LODFarDistance` (12000m, 3000–24000), `ShowOnTacticalMap` (true).

**Needs attention**
- In-game flight session verification (all LODs, works placement, map markers, origin shifts, belt trace seams, ground fit on slopes and coastal pockets).
- Confirm a position actually commits on an active front: the log must show
  `Front traces for <faction>: N trace(s)` followed by `'<name>' dug at global ...`. A
  `No position accepted yet: the front was refused (...)` warning means the control field or
  terrain probe still refuses — read its reason before changing the planner.
- Native defenders and scenery works use vanilla replication; carved ditches/map marks remain host-local. Verify host/client/late-join targeting, destruction and cleanup in-game.
- `TrenchWorks` selects infantry scenery at runtime from the game encyclopedia by keyword
  (`hesco`/`sandbag`/`gabion`/`dugout`) and footprint (≤6m); it logs the chosen keys once.
  Verify the selected pieces read as infantry positions in-game; a game update that
  resizes or renames them degrades safely to ditch-only positions.

---

## World events — `events`

**Purpose:** a host-rotated, graded world-event director — minor weather, medium price moves,
and rare scripted superevents aimed at the leading or losing side — with a bounded mission
history on its own `EVN` bezel screen and a full-screen superevent alert. **Default on.**
Publishes `IActiveEventsView`; Support optionally multiplies it into support pricing.
Installs independently and owns no Harmony patches.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Curated catalog — 19 entries graded minor (4) / medium (10) / superevent (5), ids, honest flavor text | `Domain/EventCatalog.cs`, `Domain/EventDefinition.cs` | Pure suite passes | Superevents are scripted: authored timed beats, targeted at a side or the whole theater |
| Director — theater balance from airbase custody, escalation odds, super eligibility, recent-entry exclusion, per-mission cap and spacing | `Domain/EventDirector.cs`, `Domain/EventSelector.cs` | Pure suite passes | Seeded from mission generation + rotation counter; three minutes of mission, five-minute gap, at most three supers, none repeated; targeted supers need a two-base deficit; the first roll is always a medium and never a super |
| Host rotation, heartbeat, bounded history, client mirror | `Runtime/EventsManager.cs` | Awaiting in-game validation | 1 Hz tick; one event at a time (90–300 s windows); 15 s heartbeat for late joiners; history ≤ `HistoryLength` (16); 1 Hz airbase-custody read on every peer for the director band |
| Scripted beats — funds to the faction pool, allocation to each player on the side, one funded vanilla convoy group | `Runtime/EventsManager.cs` | Awaiting in-game validation | Host-only; scaled by `EffectStrength`; convoy is the vanilla `AddFunds(-cost)` + `AddConvoy` path, refused while `preventDonation`; a beat with no resolvable side is skipped |
| Protocol-3 state + response — catalog index, target faction hash, mission timestamps; intent/reply for the decision | `Networking/EventsNet.cs` | Awaiting in-game validation | -1 = calm, 0 = all theater; title/flavor stay catalog-local; no backfill; a query converges a mid-mission reconnect |
| Response decision — CONTAIN/LEVERAGE once per player, host-priced from the requester's own effective multiplier | `Runtime/EventsManager.cs`, `Domain/EventSelector.cs` | Awaiting in-game validation | 200–1200 allocation rounded to 50; `player.SetAllocation` on the host; response map ≤ 64, cleared on rotation; a side the event is not aimed at is offered no response |
| `EVN` screen - media card (16:9 poster plate or generated stripe plate over the category mark, title, target, clock, live effect with plain-words consequence, scripted beats, response control), one director line, compact history feed with thumbnails | `Presentation/EventsMfdPanel.cs`, `Presentation/EventsMfdPanel.Cards.cs` | Awaiting in-game visual check | Reset order 63; hosted on an appended vanilla slot (`MfdScreenHost`, prefers right), so a full six-slot bezel cannot starve it; every fact is printed once (status word, one metric per fact, no repeated title) |
| Superevent alert — client-local full-screen canvas, one per super per peer (late joiners included), fades and auto-dismisses | `Presentation/SuperEventAlert.cs`, `Presentation/EventAlertTone.cs` | Awaiting in-game visual check | Reset order 67; created on first use and destroyed on reset; takes no input but its dismiss button; synthesized tone, no bundled audio |
| Optional poster art + vector glyph fallback | `Presentation/EventArtCache.cs`, `Presentation/EventGlyph.cs` | Stable | Loose PNGs in `BepInEx/plugins/BoscaliSummer/Events/`, `<id>.png` → tier fallback → `default.png` → glyph; PNG only, ≤1024 px, ≤2 MB, cached until scene reset; see `docs/EVENT_ART_BRIEF.md` |
| Support cost seam — both shared pricing points, per player and per side | `modules/Support/Runtime/SupportManager.cs` | Awaiting in-game validation | Action `Cost` and satellite/facility/EW `Price` resolve `IActiveEventsView.SupportCostMultiplierFor(playerId)` late; exactly 1 when calm or when the event is aimed at another side |

Config: `Events.Enabled` (true), `RotationGapMinSeconds`/`MaxSeconds` (90/240),
`EffectStrength` (1.0, 0–2), `HistoryLength` (16), `SuperEventsEnabled` (true, host),
`AlertsEnabled` (true, client), `AlertSeconds` (24).

**Needs attention**
- In-game acceptance is **pending for every behaviour**: the EVN host button and screen
  install, card and alert layout, rotation timing (the intended texture is mostly minor and
  medium with a superevent every several minutes once the theater leans), the
  heartbeat/late-join path, and a support action price visibly changing with the active
  event, asymmetrically for a targeted super, and reverting when it ends.
- The scripted beats are unverified against a live match: fund credits, per-player
  allocation grants and the funded convoy (which spends the shared pool) have never been
  observed in game. The director's two-base-deficit trigger needs a real mission where one
  side loses ground.
- Superevent poster art is optional; with the user's images absent the vector glyph and
  shaded backdrop carry the alert. `docs/EVENT_ART_BRIEF.md` is the brief for generating it.
- Faction morale and vanilla aircraft/unit prices remain deliberately untouched; no catalog
  text may claim them until a real seam exists.

---

## Campaign mission — `campaign`

**Purpose:** ships the authored **Boscali Summer** campaign mission into the game's user
mission list. **Default on.** New module. One bounded file write at startup; no Harmony
patches, no scene service, no networking, no Framework contract and no dependency in either
direction.

| Feature | Where | Status | Notes |
|---|---|---|---|
| Mission install — one staged write to `Application.persistentDataPath/Missions/Boscali Summer/Boscali Summer.json` | `Runtime/CampaignMissionInstaller.cs` | Awaiting in-game validation | Idempotent; a marker at `MissionInstallPlan.Revision` skips; a same-named mission with no mod marker is never touched (warning log instead); every failure is a warning and the rest of the mod runs |
| Overwrite policy — install / update / skip / foreign | `Domain/MissionInstallPlan.cs` | Pure-tested | A missing mission installs, the mod's own older marker updates, the shipped revision skips; `Revision` = 1 |
| Authored mission — seven acts, both factions joinable, vanilla mission data | `missions/Boscali Summer/Boscali Summer.json` | Authored data | 44 objectives / 112 outcomes / 13 airbases; 16 timed beats, score gates at 200/400/625/850/1225 per faction, 25 spawn waves / 227 units (largest 17); thresholds 625/1225 like the game's own Escalation |
| Builder + validator | `tools/build-boscali-summer-mission.ps1`, `tools/validate-boscali-summer-mission.ps1` | Tooling | Builder rebuilds the JSON from the built-in Escalation layout and the authored sections; validator is an independent static gate (dangling references, duplicate names, unknown types, bad waits, over-long messages, folder/name contract) |

Config: `Campaign.Enabled` (true). One key; restart to apply.

**Needs attention**
- In-game acceptance is pending for **every** behaviour: the install path, timeline pacing,
  spawn placement, balance and the multi-peer path. Only the Release build, the pure tests
  and a deserialisation check against the real game assembly have run.

---

## Weather — removed

The previous weather feature was removed because its visual result and runtime cost did not
suit the game. No replacement architecture is claimed here; the lightweight successor will
be planned separately. The campaign's authored `ModifyEnvironment` beats remain vanilla
mission content.

---

## Common HUD element - `hud`

| Feature | Files | Status | Notes |
|---|---|---|---|
| The one cockpit HUD element every Boscali presentation feature draws through | `Presentation/HudBoard.cs`, `Presentation/HudRow.cs`, `Configuration/HudSettings.cs` | Implemented, in-game visual acceptance pending | Reset order 80, after every consumer. Hangs under the vanilla weapon and capacitor column by default, right-aligned to it at its own 460 px width, and measures that column's live rectangle once a second from `CombatHUD.topRightPanel` (read only). A feature holds a line while its condition is true (`Acquire` is idempotent per owner+key, a line nobody re-sets goes stale after 1.5 s) or pushes a notice that expires on its own. Notices draw first and take at most two rows; held lines follow, loudest tone first. Font, material, the objective label's ink, palette and text size come from `VanillaHudStyle`; when vanilla's material carries no outline or underlay the element clones it once and adds a thin black outline, because saturated green and red over a sunlit cloud measure 1.3:1 and 2.4:1. Ten held lines, three notices, eight feeds, ten hertz, nothing allocated after the build |
| Transient notice ring | `Domain/HudNoticeQueue.cs` | Unit-tested | Fixed three-slot ring; identical channel+text refreshes the dwell instead of stacking, a full ring drops the oldest, reads are oldest-first |
| Presentation ladder shared by the board, the settings page and the tests | `Framework/Contracts/HudLayout.cs`, `Framework/Contracts/HudPrimitives.cs` | Unit-tested | Eight anchors (growth comes from the pivot, and every offset points inward from the edge it hangs from), four size steps off vanilla's own overlay text size, four opacity steps, 1-6 rows, 3-20 s notice dwell, eight feeds |
| The seam | `Framework/Contracts/IHudBoard.cs` | Contract | `DeclareChannel` / `Acquire` / `Notice` / `ReleaseOwner` plus the presentation knobs and the declared `Channels`. Feature-agnostic: the board never names a consumer, and the settings page lists whatever feeds were declared rather than a list kept by hand |
| SET rows | `Command/Presentation/MapUi/SettingsMfdPanel.cs` (COCKPIT > 04 COMMON HUD) | Implemented | Seven element-wide rows plus one switch per declared feed, all live via the contract. The only section this panel does not own: `Hud` entries are read live by the board and the panel merely repaints |

Config: `Hud.Enabled` (true), `Anchor` (0, UNDER WEAPONS), `ScaleStep` (1, NORMAL),
`OpacityStep` (1, HIGH), `MaxRows` (4, 1-6), `Notices` (true), `NoticeSeconds` (8, 3-20),
`DisabledChannels` (empty; the feeds this pilot switched off, comma separated).

Consumers: accepted contracts (`DynamicOperations/Runtime/OperationZoneHud.cs`, channel
`contracts`) and world-event notices (`Events/Runtime/EventsManager.cs`, channel `events`). The hostile
ace hunt keeps its own dossier overlay (`Progression/Presentation/AceHuntHud.cs`) and is not a
consumer.
## Shared / plumbing

| Area | Where | Status | Notes |
|---|---|---|---|
| Feature graph, host, transactional startup, ordered scene reset, reverse teardown | `Framework/` | Stable | "Framework extraction is done and behaviour-preserving" |
| Cross-feature contracts | `Framework/Contracts/` | In-flight | Includes `IObservationSource`, `ISecondaryObjectivesView`, `IThirdPersonHud`, `IBaseDefenseAlarmService`. Deleted: `ITheaterPage` |
| Cached game reflection, capability report | `Infrastructure/GameInterop/` | Stable | Startup logs resolved patch list, capabilities, forest index size — first place to look after a game update |
| Config composition + legacy-key migration | `Configuration/`, `Infrastructure/Diagnostics/DiagnosticSettings.cs` | Stable | |
| NOAvionics kit (bezel claims, map picker, `AvScreen`) | `Avionics/`, `AvionicsUi/` | In-flight | Unified across OPS/STR/RAD/SET and rebuilt vanilla panels; live resolution/coexistence pass pending |
| Large managers not yet split | `ImpactFireManager`, `ZoneGarrisonManager`, `ModNet` | In-flight | Roadmap: only split behind tested seams |

### Test coverage (pure suite)

Present: Command (`CommandTests`, `FrontlineTests`, `MfdPanelTests`,
`MfdSecondaryObjectivesTests`, `StrPanelTests`), DynamicOperations (`OperationTests`,
`OperationDirectorTests`), FireAndDestruction (`ImpactScorchTests`), Progression
(`ProgressionTests`), QoL (`CameraTests`, `ObservationTests`), Radio (`RadioTests`), Support
(`SupportTests`), UrbanCombat (`TroopDeploymentTests`), Campaign
(`MissionInstallPlanTests`), Framework, Architecture boundary.
`BoscaliSummer.PatchProbe` validates Harmony targets / private fields / wire contracts
against the installed `Assembly-CSharp.dll`.

Thin spots: no dedicated fire-spread/ruin test, no ModNet serializer test, no Command
map-overlay grid test beyond `FrontlineTests`, garrison lifecycle only via
`TroopDeploymentMath`.

---

## Consolidated doc/code drift (fix list)

Resolved in the September reorg: perk count/cost (README + DESIGN_NOTES + ROADMAP now say
the current board size and total), STR-vs-THEATER-tab (DESIGN_NOTES rewritten), Rod from God
default (DESIGN_NOTES no longer says "default-off"), fire caps (ARCHITECTURE now 32 sites /
≤3 generations; the README budgets table was dropped in favour of ARCHITECTURE), Flare
Barrage (now in the README action list, with a note that it shares the Satellite Scan
authorisation), wreck persistence (README + ARCHITECTURE mention it), the README Support
config table (now an explicitly curated subset), and the untracked docs (committed).

Resolved in the 2026-09-14 quality pass:

1. **Flare Barrage** — **DECIDED:** shares Satellite Scan / Recon; no extra perk. Documented.
2. **Keep-or-cut** — Chimera paradrop KEEP, infantry encampments KEEP, base-defense alarm
   KEEP, gun aim CUT, `MakeshiftFortificationBuilder` CUT, `TheaterBias.cs` CUT,
   `EventIconCache` CUT (glyphs only).
3. **COM copy** — STR is the bezel; `ITheaterPage` gone; DESIGN_NOTES matches.
4. **Fire caps** — MODULE_STATUS no longer claims 24 sites / ≤2 generations.
5. **CRYPTO** — copy does not claim a host cooldown discount.
6. **STR empty theater** — dash, not 50%. CMD rebuilt as the TheaterOps intent board;
   doctrine/Sector Focus stay removed.
7. **EW encampment UI** — gone with the single EW truck; the CYBER network replaced both.

## Refactor / cleanup debt

- `modules/Command/Presentation/MapUi/` (~30 files) — audit for dead code after COM→STR.
- Split `ImpactFireManager`, `ZoneGarrisonManager`, `ModNet` (roadmap, behind seams only).
- Persistence service (schema-versioned atomic JSON) — blocks persistent perk profiles and
  any other saved state.

## Biggest untested surfaces (validation backlog)

1. **QoL camera/HUD stack** — entire third-person camera, HUD restore and target feed;
   code-complete, never run in-game.
2. **Command STR + frontline overlay** — recently made functional; needs a live theater.
3. **Dynamic operations** — nothing proven in a mission; default-off until it is.
4. **Air assault + Chimera paradrop** — visual/insertion sequences unconfirmed.
5. **Multiplayer** — Support protocol 11 / Progression protocol 3, late-join for garrisons and
   dynamic ops, listen-host vs dedicated.
