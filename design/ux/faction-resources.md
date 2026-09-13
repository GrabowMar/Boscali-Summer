# Faction resource panel

Status: implemented, awaiting in-game acceptance. Date: 2026-09-12.
Platform: Nuclear Option PC map MFD; existing mouse/bezel input and green-glass shell.
Owner: Command. User request is the design authority; no system GDD or systems index exists.

## Player need and navigation

A player opening either faction bezel needs to assess available resources and recent
changes before returning to the map. RESOURCES is the initial page, followed by FORCES,
LEDGER and STATUS. Existing bezel entry, page switching and map exit remain native.
Tabs select local views only; they never issue gameplay commands. Standard AvButton
hover/selected/disabled states and the pinned explanation strip apply. No new animation.
Use the existing mouse/bezel input; Unity directional navigation stays disabled to avoid
stealing flight-control axes. No new keyboard/gamepad bindings are advertised.

## Layout and data

| Zone | Source and behavior |
|---|---|
| Header | Faction identity, native score, funds and warheads |
| Four resource cards | FactionHQ funds/warhead stockpile, current manpower summed across native asset classes, Command Morale |
| Resource history | Select funds, warheads, manpower or Morale; labeled line, numeric change, observed duration, y-axis bounds |
| Forces | Two wider columns of current/lost counts; rows grow with body height, bounded at 12 |
| Ledger | Four asset cards and paired horizontal bars; common scale, full category names, numeric values, ordered legend |
| Status | Airbases or players; up to 16 visible rows with paging |

Live values refresh through the existing visible-only 0.15-second presenter cycle.
History retains 60 local observations at five-second mission-time intervals, at most
295 seconds between oldest/newest samples. Pauses do not create samples. No backfill.
An observation gap over 15 seconds, time reversal, faction change or panel rebuild
starts a new history; a short hidden interval can fall between samples. Only samples
and series changes update line geometry. Chart objects are preallocated (59 segments).

Money graphs include zero and negative balances. Morale always uses 0–100. Counts and
money never share a scale. Numeric labels convey meaning independently of color.
Unknown statistics show dashes; unavailable Morale is explicitly labeled. No HQ hides
the data pages and shows a waiting state. One observation shows collecting samples;
recorded zeroes become a flat zero line. The existing adapter-error footer remains.
Labels use existing type tokens (10px floor), with explicit resource names/units. Layout
is bounded to the existing 512px panel width and 596–896px height. English matches the
current MFD; localization and arbitrary font scaling are not newly implemented here.

## Morale contract

`BoscaliSummer.Features.Command.Runtime.FactionResources.TryGetMorale(FactionHQ, out float)`
and `TrySetMorale(FactionHQ, float)` are public main-thread APIs. Storage belongs to the
Command runtime, independent of panel lifetime. Host reads initialize a faction at 100;
at most eight HQ instance IDs exist per scene. Valid writes are finite and within 0–100;
invalid writes fail without modifying state. Mission reset clears the store. Remote
clients return false and display unavailable. No gameplay effects, replication, disk
persistence, config knob or client mutation request exists yet. A future sibling module
must receive a narrow Framework contract when it becomes a real consumer.

## Acceptance and UX review

- The default page exposes all four resources without visiting a submenu.
- Negative funds retain their sign; manpower is labeled as personnel in active assets.
- Changing graph series changes units and scale; Morale stays 0–100.
- Missing HQ/statistics/Morale never retain another faction's values or invent zeroes.
- Histories remain bounded, clear on faction change, and never invent past samples.
- Morale reads survive panel closure; invalid writes preserve state; scene reset restores 100.
- At 596px and 896px, labels, graph axes and page controls remain above the pinned footer.
- Forces, ledger modes, status lists and paging remain available.

UX review using the requested Game Studios ux-review checklist: implementation-ready
for the existing PC MFD input model. Advisory gaps: in-game font/theme/resolution checks,
multiplayer acceptance, and future localization/input accessibility remain outstanding.
Standalone Unity renders use sample adapters and a substitute monospace font; they do
not establish live-game correctness.

Validation: Release solution build (zero warnings/errors), full pure/architecture suite,
installed-game PatchProbe, and nomod assembly verification (49 patches, 60 reflection
lookups) passed. Unity 2022.3.62f3 rendered all four pages at both supported panel heights,
plus flat Morale and unavailable data. Visual review caught and fixed a clipped caption.
Sample previews: [compact resources](faction-preview/resources-compact.png),
[tall resources](faction-preview/resources-tall.png), [ledger](faction-preview/ledger.png),
[unavailable data](faction-preview/unavailable.png). No game launch or deployment.
