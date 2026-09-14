# Urban combat — legibility and warfare design proposal

Status: **P0 implemented** (A1 shell-scaled masts/flags, A2 roof-edge band); the rest is a
proposal, nothing here is a README claim. Gated future scope, see [ROADMAP.md](ROADMAP.md).
Where this doc and the code disagree, the code wins.

Scope owner: `modules/UrbanCombat` (config, patches, runtime, visuals). Proposed work stays
inside that module and `tests/BoscaliSummer.Tests/Features/UrbanCombat`.

## Problem

An enemy-held building is functionally dangerous the moment it is occupied, but the current
local marker does not communicate it at combat distances. Observed in a live pass over a
large occupied block: no flag, no band, no ground presence was identifiable.

Code-level causes (verified against the current tree):

- `OccupiedBuildingMarking` is parented to the spawned rooftop emplacement and sized from the
  **emplacement definition**, not the civilian shell (`Visuals/OccupiedBuildingMarking.cs:48-50`).
  A roughly 2.8 x 1.6 m flag sits next to a ~4 m weapon nest somewhere on a 100 m roof, at one
  corner only, and is easily hidden by parapets or roof clutter.
- Marking is color-only, static, and single-point: no shape or motion redundancy, no
  ground-level cue, weak emissive (`0.18`), no night or side-angle read.
- The marker is attached to the networked defense (`Runtime/ZoneGarrisonManager.cs:93` and
  `:257` via `Visuals/GarrisonVisual.cs:24`), which is correct for cleanup and late join
  (`Patches/AirbaseCapturePatches.cs:20-36`) but wrong for scale: it inherits the nest's
  footprint instead of the building's.
- Ground dressing exists only in the air-assault path (`MakeshiftFortificationBuilder`,
  `VanillaSoldierFactory`); zone garrisons get nothing at street level.

Design target: **a player crossing a zone at any altitude and any light level should know
within seconds which buildings are occupied and whether they are hostile — without color
being the only carrier of that meaning.**

## Design pillars

- **Honest.** The game has no infantry squad system. This remains occupied buildings /
  defensive positions, never room-clearing infantry (`modules/UrbanCombat/AGENTS.md`,
  `docs/DESIGN_NOTES.md:43-50`).
- **Readable first.** Legibility is the feature, not decoration. Vanilla-native visuals and
  the game's own HUD color tokens beat bespoke art.
- **Vanilla-first and bounded.** Reuse game assets and existing builders; every new cue has a
  hard cap, no new wire messages, no scene scans (`AGENTS.md`).
- **Authority stays server-side.** Occupancy, nest spawning and cleanup are server-owned;
  every proposed cue is local decoration following the networked defense.

## Legibility model

Principles borrowed from UI/UX practice, applied to world markers:

- **Redundant encoding.** Shape (mast, band, chevron) + motion (smoke/flare) + color
  (faction). Color never carries meaning alone.
- **Range tiers.** Aerial: roof-edge band + mast. Mid: flag, smoke. Ground: props and
  street-level sandbags.
- **Relative semantics.** Faction identity comes from the flag; hostile/friendly emphasis
  uses the game's own viewer-relative HUD colors, verified in `GameAssets`:
  `HUDHostile`, `HUDFriendly`, `HUDNeutral`.
- **Night parity.** Emissive band and an optional light-bearing cue (flare) so the marker is
  not daylight-only.

## Part A — occupied-building legibility

### A1 (P0) Re-anchor and scale the marker to the occupied roof — implemented

The marking root stays a child of the networked defense (cleanup, late join, destruction all
keep working). At spawn the server measures the flat roof patch around the chosen nest
(bounded 2 m steps up to 40 m along the defense's local axes) and encodes its defense-local
min/max X and Z in the defense's networked unique name, so every client and late joiner
derives the same marker:

- mast height `clamp(longSpan * 0.22, 6 m, 12 m)`;
- flag width `clamp(longSpan * 0.16, 3 m, 7 m)`, a second mast only when `longSpan >= 24 m`;
- masts on the measured roof corners; bands inset inside the measured edges;
- legacy definition-sized nest marking when no measured patch is encoded.

Acceptance: at 2 km slant / 25 degree depression, a clear-day pass identifies at least one
cue within two seconds of looking at the building.

### A2 (P0) Roof-edge faction band — implemented

One long, low strip (0.9 m tall) inset inside two opposite measured roof edges, faction
color with a viewer-relative accent stripe (the game's HUD hostile/friendly/neutral tokens)
so shape and color both carry the meaning. Geometry is merged into the existing marker
meshes; the marker stays at six renderers.

Acceptance: band alone identifies occupancy when the flag is occluded.

### A3 (P1) Colored smoke / flare cue

A single, slow, intermittent colored smoke puff over the occupied cluster, visible far
beyond model-reading range and providing the motion cue the system lacks. Feasibility notes:

- `GameAssets.contactSmoke` is a confirmed vanilla smoke prefab and is the safe source;
  tint via a cloned material.
- `SpecialFlare` (`smokeParticles`, `listColor`, `flareLight`) exists but has no
  `GameAssets` entry, so it needs a resolvable prefab source first; treat as optional.
- `modules/FireAndDestruction` owns `FuelDepotSmokePool`; UrbanCombat must not import it
  (architecture test). If shared reuse is wanted later, it goes through a narrow
  `Framework/Contracts` seam with a real second consumer.
- Hard bounds: at most 1-2 active puffs, nearest occupied zone only, pooled, no scans;
  server headless skips it.

Acceptance: a puff is visible at 3 km in daylight and its light is visible at night.

### A4 (P1) Street dressing around occupied shells

Reuse the existing builders as local decoration on each occupied shell (same pattern as
`MakeshiftFortificationBuilder.ApplyPresentation`, triggered from the existing
`OnStartClient` path):

- two jersey barriers and a sandbag line at the main approach;
- one or two visual sentries (`VanillaSoldierFactory`, `GameAssets.pilotDismounted`);
- optional clutter sized to the building footprint.

Cap: at most two decorated shells per zone object family and a hard per-scene decoration
ceiling, merged meshes, no colliders, no lights, no networking. Purpose is ground-read and
atmosphere, not the aerial fix.

### A5 (P2) Map / HUD intel

Occupied shells as faction pips on the tactical map, using existing sprites
(`GameAssets.targetUnitSprite`, `airbaseSprite`). This requires:

- a new **read-only** snapshot contract (position, owner, healthy-nest count) beside
  `IBuildingOccupancy` — `IsOccupied(shell)` alone cannot feed a map;
- a real consumer (map/HUD module or Wing Command through the shared avionics path), per
  the two-consumer rule;
- bounded entries (<= 96), regenerated on occupancy change, never per frame.

### A6 (P2) Radio / OPS callout

Extend the existing `BaseDefenseAlarmService` ticker to announce enemy strongpoints when the
player enters a garrisoned zone radius, using the same bounded 2 s poll. Zero new systems,
zero wire traffic.

## Part B — urban warfare systems

### B1 Tiered zone garrisons

Today `Urban.GarrisonsPerZone` is a flat count. Propose a deterministic per-zone tier
(scene-local, no persistence yet) derived from zone importance (airbase radius / capture
points) and session recapture count, scaling nest count up to the existing
`MaxPerZone = 6` and rotating the existing MG/ATGM/23 mm mix (`RooftopPlacement.Keys`).
Recapture escalation gives a recaptured base visible teeth without touching the 96 global
cap. Pure selection math gets unit tests under `Features/UrbanCombat`.

### B2 Counterplay feedback

- Destroying a nest already removes its marker with the defense; add a zone-level read:
  when a majority of nests in a zone are down, remaining flags drop or the band dims
  (local, derived from the existing record) so the player can feel progress.
- Friendly troop insertion near a hostile shell is the natural "suppression" verb
  (`AirAssaultController` exists). A server-side temporary weapon-disable is **not yet
  committed**: it touches native weapon behavior and needs its own authority/RE pass.
  Listed as an open question, not a plan.

### B3 Capture interaction (gated, needs sign-off)

Option: enemy capture progress is paused while healthy nests remain at the base, turning the
garrison into a genuine "suppress the buildings first" objective. This patches vanilla
capture progress, so it is gated on a reverse-engineering pass, an explicit authority
design, and multiplayer tests. If that gate fails, keep today's behavior (garrison follows
capture, cleanup on flip) and ship only Part A + B1/B2.

### B4 Variety and faction identity

Variation inside existing budgets: nest loadout per building, sandbag tint per faction,
camo/banner stencils, one AA nest on the tallest roof in a zone. No new asset pipeline
required for the first pass.

### B5 Recon feedback

When a player overflies a garrisoned zone, the alarm service can reveal the occupied shells
temporarily (bounded, local, consistent with the existing recon reveal ceiling of 48).

## Explicit non-goals

Interiors, room clearing, breaching, per-floor damage, roaming infantry AI, cosmetic network
messages, per-frame scene scans, raising the 6/zone and 96 global caps, custom wire
messages, and any persistence before the persistence framework slice lands.

## Authority, cleanup and compatibility

- Server owns occupancy, nest spawning, tier state and any suppression state.
- Every cue is local decoration built from the networked defense, so late join, scene
  reload, capture churn and destruction keep working through the existing paths.
- No `ModNet` / Mirage message changes; the patch probe inventory changes only if a new
  Harmony target is added, which requires its own probe entry.
- Architecture test must stay green: no sibling-module imports; no new contract without a
  real second consumer.

## Budgets

- Marker: <= 8 renderers, <= ~4k vertices per building (current is 4 / ~3k).
- Smoke: <= 2 active sources, pooled, nearest zone only.
- Flags/bands: geometry merged into existing marker meshes; any animation is slow-ticked
  (<= 10 Hz) over a bounded list (<= 96), never per-frame per-object work.
- Ground dressing: <= 2 decorated shells per zone family, merged meshes, no colliders.
- Local decoration only; remote clients build their own from the same networked defense.

## Acceptance criteria

- Single-player, listen host, remote client, late join and scene reload: the marker set
  appears, follows ownership, disappears with the defense, and never duplicates.
- 2 km day pass: band or mast identifies occupancy within two seconds.
- Night pass: emissive band or flare light identifies occupancy at 3 km.
- Grayscale/color-blind check: shape alone encodes occupancy; HUD-relative accent encodes
  hostile/friendly.
- Ground approach at 300 m: street dressing reads as an occupied position.
- Saturate caps (6/zone, 96 global) and record frame time, renderer and allocation counts;
  regression against the current baseline.
- `nomod asm verify`, pure tests, patch probe and architecture test all green; no new
  messages in the protocol.

## Rollout

1. **A1 + A2** — pure marker geometry/math plus shell bounds plumbing; `RooftopUnityCheck`
   preview and unit tests for placement math.
2. **A3 + A4** — pooled smoke cue and street dressing behind the existing feature settings.
3. **B1 + B2 + B4** — tier math, progress feedback, variety; tests for determinism.
4. **A5 + A6 + B5** — map/intel only with a real consumer; A6 rides the alarm service.
5. **B3** — only after capture-progress reverse engineering and explicit sign-off.

## Open questions

- `SpecialFlare` prefab provenance: can a stable prefab reference be resolved, or does A3
  stay `contactSmoke`-only?
- Marker renderer budget under a full 96-building saturation run; is mesh sharing per size
  class worth it?
- Does viewer-relative accent need the local player's HQ at marker-build time, and is that
  available on the `OnStartClient` path for late join?
- B2 suppression and B3 capture interaction both touch native behavior; both need an
  authority/RE pass before they can be promised.
