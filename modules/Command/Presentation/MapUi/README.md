# Boscali tactical map presentation

This is the full-screen map layout: bottom-aligned MFD pages with a tactical log
above, central map, right-hand bezel rail, and native spawn controls in the footer.
Command installs it through its explicit patch list and `MapUiManager` scene service.
`Command.ExpandedMapUi` enables it by default.

Reopening the map restores only the last selected page for the current scene, across
both bezel columns. Deselecting it leaves the log bay open. The log reserves space
above the visible display surface, rather than the invisible controller root.

The layout implementation was recovered from the authoritative Wing Command source
at `b0a0c52^` (the last version before the layout was removed), then adapted into this
module. Its MIT notice is preserved in LICENSE. No Wing Command binary is referenced,
loaded by reflection, or required. Existing WMC pages are discovered through the game's
VirtualMFD lists just like OPS, RAD, and vanilla pages; wing orders remain WC-owned.

The `NOAvionics` widget kit (`Avionics/`, `AvionicsUi/`) and the pure layout arithmetic
are the shared vocabulary with Wing Command, vendored into each mod. Changes to this
Boscali-owned layout do not change Wing Command's standalone presentation.

## Adapted vanilla panels

- TGT has eight built-in acquisition presets: All, Hostile, Air, Ground, Sea,
  SEAD, Friendly and Laser. Applying one disables HUD linking, then uses native
  inclusion toggles. Manual edits update the active-profile indicator. Neutral
  factions retain the game's own filtering rules. SEAD covers AAA, IR SAM, radar
  SAM and radar vehicles; it does not identify every possible air-defense building.
- Controls use native unit/filter sprites where available and a consistent small
  vector symbol for other actions. Text labels, the existing selected underline,
  and a new selected edge remain visible alongside the icons.
- Both faction panels show relative force-count bars and a ledger comparison with
  four labeled categories on one linear scale. Reserves/losses compare with current
  units; current value/manpower compare with losses in the same units. Top and lower
  bars have distinct thicknesses and an explicit legend. These are current snapshots,
  not historical trends. The game's supply API does not expose building/ship reserves.
- MAP includes an illustrative symbol-size preview; HUD has a profile readout and
  type-derived category names. MIS gives its main tab a full-height briefing sheet —
  name, scrolling brief, mission time and player mode — then a three-stage escalation
  ladder carrying the mission's own threshold values, a live position marker and the
  remaining score to the next nuclear gate; an unset threshold reads as unset, never as
  a confident zero. Each active objective keeps a percentage and progress bar.
- Empty grid slots and unnecessary pagers disappear. Inventory, directory and
  selected-unit rows use quieter readout styling. Symbols and bars reuse bounded
  UI objects and refresh with the existing visible-panel throttle.

## Additional inexpensive improvements considered

| Improvement | Implementation and value | Scope |
|---|---|---|
| Hide zero-count inventory | Filter the existing definition list using current/lost counts; retain an explicit "show all" toggle. Makes large building catalogs easier to scan. | Small, faction only |
| Sort inventory by current or lost | Sort the cached definitions when the mode changes; use code as a stable tie-breaker. Helps locate dominant forces or expensive losses. | Small, faction only |
| Deselect a single target | Add a clearly labeled remove action to selected rows using the existing native `ForceDeselect` method. Avoid making the entire readout a destructive click target. | Small, TGT only; test HUD-linked selection |
| Objective detail view | Show the full existing `ToUIString` text for a chosen objective. Gives long mission instructions more room than a one-line row. | Small, MIS only |
| User-saved presets | Shipped: bounded config-persisted library, quick slots, native-radial page and shortcut keys. See "Player target presets" below. | Done |
| Historical faction sparklines | Sample into a fixed-size buffer and explicitly show gaps while the map is closed. Useful for trends, but needs lifecycle and time-window semantics. | Medium; current charts deliberately use snapshots |

Design approach used the requested [Ponytail skill](https://github.com/DietrichGebert/ponytail/blob/main/skills/ponytail/SKILL.md)
and [UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): reuse native
controllers, keep the change local, retain labels beside icons, and directly label
category comparisons. No runtime dependency was added.

Validation covers preset inclusion rules, graph normalization, the Release build,
module-boundary assertions, and the installed-game compatibility probe. In-game
visual and interaction checks remain necessary, particularly native sprite clarity,
long/localized labels, HUD unlinking, mission changes, and map close/reopen cleanup.

## SET redesign (2026-09-13)

Players arrive from the SET bezel to improve map readability, choose a background, or
tune the third-person view. The panel resolves its height like every sibling Boscali
screen — `AvTokens.PanelHeight` as the floor, `AvTokens.PanelHeightMax` as the ceiling —
instead of a fixed 596, so on a tall column the pages fit whole and the event log keeps
whatever space is left above. Its pages share a fixed status strip. MAP owns
tactical/terrain visibility and update interval; STYLE owns console opacity, map
darkening, one decoration selector and ticker controls; IMAGE owns image opacity, local
file selection, fit and explicit rescan; COCKPIT owns the third-person HUD, pitch ladder
and camera settings (QoL owns and applies the values, reached through `IThirdPersonHud`)
and Command's radial target presets. Re-selecting the bezel closes SET; selecting another
bezel replaces it in the expanded dock. Map close hides the UI and restores native
presentation. No enter/exit animation is added.

New configurations start with a plain backdrop. Background choices are PLAIN / GRID / CHECKER / HEXAGON / CARBON / RADAR / CUSTOM.
They write the existing configuration entries; no config path or key migration is
needed. Existing overlapping choices read MIXED and remain unchanged until edited.
Map darkening always controls the map tray, regardless of background mode. Grid
resolution is advanced config only because the grid is allocated during module
initialization; SET does not advertise it as a live setting.

All control state comes from CommandSettings. Pages are constructed once, values
refresh on config changes and panel entry, and hidden pages do not poll file names.
Relevant visual changes are applied on the main thread. Styling changes do not
rasterize tactical sectors. The scrolling ticker runs per frame; manager discovery,
layout reconciliation and footer data remain throttled. Disabled buttons explain
the prerequisite in the status strip. Long file names ellipsize, retain their full
name in hover help, and disable TMP rich-text parsing.

Custom files keep the existing search directories for compatibility. A scan visits
at most 512 entries across those directories. Decode accepts PNG/JPEG headers up to
4096 pixels per side and 16 MiB, retains one custom texture, caches failed attempts
until RESCAN or a different file, and reports errors instead of silently substituting
a pattern. Map close keeps the cache; scene reset releases it. No network access.

Input follows the shared avionics contract: pointer controls, compact 38-unit step and
78-unit toggle buttons behind a full-width row hover target, no automatic joystick
navigation, and deselection after clicks. Text labels and ON/OFF states supplement
color. There is no claim of keyboard-only or screen-reader accessibility. Small bodies
scroll rather than compressing controls below their minimum size. The fixed status strip
remains outside the scroll viewport.

Validation: run `Run-SettingsUnityCheck.ps1` in the Command test folder for actual
widget renders and interaction checks at 596 and 420 units (game adapters stubbed).
Pure checks cover background transitions and malformed/oversized image headers.
Release, architecture and installed-game compatibility gates also apply. Acceptance
in the game must cover 720p/1080p/ultrawide, map open/close, expanded/stock transitions,
scene reload, late panel binding, custom image replacement and spawn/telemetry overlap.

UX review used the requested game-studio [UX review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md):
explicit entry/exit, empty and failure feedback, setting ownership, disabled
prerequisites, bounded refresh and resolution checks. Shared avionics input rules
supersede generic controller navigation recommendations; in-game visual acceptance
remains outstanding.

## Theater Wire redesign (2026-09-13)

The wire is a news service for the theater, not a kill log. Only consequential events
become headlines — airbase captures, strategic launches, ace defeats, aircrew rescue or
capture, capital ship losses, warhead interceptions and strategic demolitions — plus the
throttled war-effort delivery line. Individual traffic (aircraft shootdowns, vehicle
kills, routine missile interceptions, crash sites) and every derived tally (armor
digests, casualty ledgers, kill streaks, first blood) never reach the wire; the tactical
event log above the map remains the place to read those. The LARP pool (with HOME FRONT /
ECONOMY / RUMOR desks) and periodic theater SITREPs fill the quiet stretches.

Priority is explicit: critical and major headlines interrupt and render bold, notable
headlines join the next cycle without rewinding the scroll. Queue and history counts stay
hard-capped. Breaking news rewinds the marquee to the badge and pulses the channel dot
and alert rail for eight seconds; the rail and dot return to accent when calm, so the cue
never relies on color alone. Logistics chat is throttled, repairs and non-event chatter
are ignored.

Validation: pure parse, individual-traffic suppression, throttle, dedup, marquee-loop and
theater-status checks in `MfdNewsTickerTests`; Release build, module-boundary tests, patch
probe and `nomod asm verify` pass. In-game visual acceptance (720p/1080p/ultrawide, map
open/close, scene reload) remains pending.

UX review followed the requested game-studio [UX review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md),
[Ponytail](https://github.com/DietrichGebert/ponytail) and
[UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): per-event
priority with queue/expiry semantics, a bounded visual budget, color-plus-text severity
cues, readable reflow of long unit names and no new runtime dependency.

## SET visibility and control feedback (2026-09-13)

The SET surface now follows the live screen state every frame: it renders only while the
screen is active and the maximised map canvas is open, so a map-close path that skips
`MFDScreen.CloseScreen` can no longer strand the panel over the cockpit. A screen root
destroyed with a dock slot releases its bezel reservation and reinstalls on the next map
open. The owned backdrop also restores itself whenever the map is not maximised, which
covers any close path that skips the minimise postfix.

Buttons gained whole-row hover help and a row highlight (`AvTooltipTarget`), a status-strip
action echo after each change (`FRONTLINES — ON`), and a short synthesized bezel tick
(`AvUiSound`) — the cue is text plus a value change, never colour alone. Rail buttons use a
ColorTint transition over the flat restyled fill instead of the stock SpriteSwap, which was
swapping in the vanilla highlight frame on hover.

Validation: Release build, pure tests, patch probe and `nomod asm verify` pass; in-game
acceptance remains pending for map close/reopen, expanded/stock transitions and audio level.

## SET compact controls, taller bay and COCKPIT page (2026-09-13)

The SET rows were oversized next to every other panel's inline controls. Toggle and step
buttons now sit at the house control size — a 46-unit row with 78-unit toggles and 38-unit
step buttons, 52-unit row pitch, a 15px step glyph — while the whole row remains the hover
and tooltip target, so the hit area did not shrink with the paint. The panel resolves its
height through the same `AvTokens.PanelHeight`/`PanelHeightMax` pair as STR, RAD, SQD, SUP
and the rebuilt vanilla panels: 596 is the floor, not the fixed answer.

The freed space carries a fourth **COCKPIT** tab. Its HUD, CAMERA and TARGETING sections
surface settings that previously existed only in the BepInEx file: the third-person pitch
ladder, target camera feed and flight camera (all QoL-owned, read and written through the
existing `IThirdPersonHud` contract, which gained three live properties) and Command's
radial target-preset page (`Command.TargetPresetWheel`). Camera rows disable with a reason
until the third-person HUD they belong to is on. `Command.GridCellSizeMetres` stays excluded:
it is allocated at module initialization and cannot honestly re-apply in flight.

The STR CMD page's claim that grid resolution lives on SET was corrected; overlay opacity
and refresh rate are the saved settings that actually do.

Validation: the standalone `Run-SettingsUnityCheck.ps1` render now builds and renders all
four pages at 596 and 420 units and passes (its map-width fixture was updated for the
150-unit rail); Release build, pure tests, patch probe and `nomod asm verify` pass. In-game
acceptance remains pending for 720p/1080p/ultrawide, panel height on short columns, QoL
disabled/failed states and the real third-person camera rows.

UX review followed the requested game-studio
[UX review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md),
[Ponytail](https://github.com/DietrichGebert/ponytail) and
[UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): grouped settings
by player context instead of piling rows onto MAP, kept text plus value as the state cue,
disabled prerequisites with an explanation, reused the existing contract and control
patterns rather than adding a second settings surface, and made no new runtime dependency.

## First-open sizing and relayout (2026-09-13)

`MaximizedMapCanvas` is a screen-space canvas whose `RectTransform` only takes its new
size on the canvas update after activation, so on the first open of a mission it can
still report the size it had the last time it was enabled — and it keeps that value for
as long as the map stays open. The layout is resolved from the live screen in canvas
units instead (`MfdLayout.CanvasSize` = `Screen / canvas.scaleFactor`, which the
CanvasScaler stamps on enable). Every region is anchored to the canvas centre, so the
first open lays out at the real size immediately; world-space canvases keep their
authored rect.

Panel relayouts resolve geometry from `DynamicMap.maximizedMapCanvas`, never from the
map root's nearest canvas. `DynamicMap` sits on `MapCanvas`, whose rect this feature
has already resized to the map column; reading it back divided the shrunk viewport
again, which is what collapsed the map further when a bezel panel of a different width
(any stock 450-unit screen such as RAD) was opened. `MapUiManager` now compares the
live root-canvas size with `MfdRailPatch.AppliedCanvasSize` and re-applies when they
diverge, covering resolution and UI-scale changes while the map is open.

Validation: `Run-SettingsUnityCheck.ps1` adds a regression fixture for a stale nested
canvas rect; Release build, pure tests, patch probe and `nomod asm verify` apply.
In-game acceptance must re-check first mission open, panel switches at both widths,
resolution changes and ultrawide.

## Rail branding, glyphs and event tone (2026-09-13)

The right-hand rail was a column of three-letter codes. Each adopted bezel button now
leads with a vector glyph, the game's own short code, and the descriptor that says what
the code means (BDF — BOSCALI HQ, MIS — MISSION, WMC — WING CMD), and the button for the
screen currently on the panel lights with the accent. The rail widened from 96 to 150
units to hold that column; the map gives up the width. A code the catalog does not know
keeps its sanitised code and the neutral glyph — a future game screen is never renamed to
fit the table. The glyphs are vector meshes in `MfdGlyph` (map, faction, radio, settings,
support, theater, funds, gauge and a drawn dot, beside the existing unit symbols); no
texture, font glyph or external icon dependency was added.

The rail's order belongs to the mod, not the stock columns: the two vanilla bezel columns
are merged and stably sorted by the catalog's lead codes, so PALA sits directly below BDF
and every other button keeps the order the game gave it. Only placement changes — each
adopted button keeps its own click binding, screen and latched state.

The Theater Wire's channel dot was a `●` character the game font has no glyph for and
rendered as a missing-glyph box; it is now drawn geometry, and its alert pulse is unchanged.
The tactical event stream colours each line by meaning — destroyed/demolished danger,
captured/repaired ready, intercepted/engaged caution, everything else neutral — using the
literal status rails rather than the live theme. The faction resource cards and every panel
tab carry a matching glyph, latched toggles brighten theirs, and the resource history gained
a soft glow under the line plus a marker on the latest sample.

Restore stays exact: the borrowed label's text, rich-text flag, alignment and rectangle are
snapshotted and put back, and the glyph is a child of the decoration object minimization
already destroys. The rail keeps the label's colour too: an adopted vanilla label is pure
green, and the game's `TextStyleApplier` re-applies that theme colour at first activation, so
the rail re-asserts its own text-primary colour alongside the branded line on every
reconcile tick instead of letting late style passes turn some keys green.

Validation: pure catalog/sanitise and tone-classification checks in `MfdPanelTests`; the new
`Run-RailUnityCheck.ps1` standalone render (all 16 glyph kinds produce geometry; adopted
buttons brand, latch and restore; a label reset by the game's style pass has its line and
colour re-asserted; PNG of the rail and the glyph strip); Release build,
module-boundary tests, patch probe and `nomod asm verify`. In-game visual acceptance remains
outstanding.

## Player target presets, quick slots and the radial page (2026-09-13)

The PRESETS page is now a profile manager instead of a fixed text list. Left click applies a
preset; right click assigns it to the first empty quick slot; right-clicking a slot clears it.
SAVE AS captures the current faction, class, platform and laser state under a 14-character
name, UPDATE overwrites the selected saved preset, RENAME edits in place, and DELETE requires a
two-press confirmation shown on the status strip. The active profile is named in the data bar
on every tab, on the quick-slot strip and in the library summary line; a custom capture reads
CUSTOM rather than being silently attributed to the nearest built-in.

The library is bounded (12 presets), versioned and stored in `Command.TargetPresets` /
`TargetPresetSlots` through a separator-free codec in `TargetPresetModel.cs`; unit-type names
and vehicle-type names are the stable identities, so a changed catalog disables entries it no
longer knows instead of corrupting a profile. All list parsing is fail-soft and capped.

The three quick slots apply with configurable keys (F6 / F9 / F10 by default) and appear in
the native cockpit radial menu as a Command-contributed page under Boscali Summer, hosted by
Autopilot via `IRadialMenuPage`: Autopilot owns the wheel, appearance and lifecycle, Command
owns labels and actions. The page is only offered while a live `TargetListSelector` is ready
and at least one slot is assigned; TARGET FILTERS wedges show the preset names and mark the
active one. SELECTED rows gain a right-click drop through the native `ForceDeselect` path.

Validation: pure codec round-trip/malformed-input, name-hygiene, library CRUD/slot and
match/summary checks in `TargetPresetTests`; Release build, module-boundary tests, patch probe
and `nomod asm verify`. In-game acceptance still needs 720p/1080p/ultrawide layout, text-entry
focus while flying, hotkey conflicts, wheel open/close and restore, scene reload, and
listen-host/remote-client behaviour of the shared selector state.

UX rules followed the requested game-studio
[ux-review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md),
[Ponytail](https://github.com/DietrichGebert/ponytail) and
[UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): explicit
empty/full/error states, confirmation before delete, status-strip feedback for every action,
text plus glyph cues instead of colour alone, progressive disclosure of the name editor, and
no new runtime dependency or second wheel implementation.

## Spawn-safe screen visibility (2026-09-13)

A screen can be left active when the map is already minimised: vanilla's own minimise
block is skipped, so its hide callbacks never run. The map UI used to enforce "closed
means hidden" by deactivating only that screen's display panel. That blanked the content
but left the screen's root backplate — a sliced avionics panel sprite with a hairline
border — drawing at its home position, which is the empty rectangle a player saw over
the cockpit after spawning. The invariant now closes the straggler through the native
`MFDScreen.CloseScreen` path with vanilla's park offset, so the root is moved off-canvas
and the chrome patch turns its border off. The dock also parks every closed screen on
restore instead of replaying a snapshot taken while the player had it open, so a
stranded home position cannot be carried into the next map cycle.

Validation: Release build, pure tests, patch probe and `nomod asm verify`; live
reproduction of the stranded state before the change showed an active screen while the
map was closed. In-game acceptance still needs spawn, map open/close, pause/resume,
scene reload and a late-join spawn.

## MIS main tab redesign (2026-09-13)

The mission overview used to cap the brief card at 340 units and then repeat the clock
and escalation as two loose labels, leaving roughly a third of the panel empty below the
contract button. The main tab now lays the brief card out from the page bottom: the
contract action is pinned to the bottom edge, the escalation block sits above it, and the
brief takes every pixel left over. The card carries the mission name, a hairline, the
scrolling brief, and a footer with mission time (left) and player mode (right, from the
synced `MissionSettings.playerMode`). A mission with no briefing reads as a muted italic
`NO BRIEFING FILED` instead of a normal-weight sentence.

The escalation ladder is now a real gauge rather than three unlabeled bars. The track
gives each stage an equal third — matching the game's own pointer, which maps
conventional, tactical and strategic to equal segments — with tick marks at the tactical
and strategic boundaries and each mission's threshold value printed beneath its tick. A
2px marker rides the current position; the fill and active stage use accent, caution or
alert by stage. The caption states `CURRENT n — m TO TACTICAL NUCLEAR` (or the distance
to strategic, or that a gate is cleared); when both thresholds are zero it says
`NO ESCALATION THRESHOLDS SET` instead of lighting STRATEGIC off an unset zero. The math
lives in `MfdMissionOverview.cs` (Unity-free) and is covered by
`MfdMissionOverviewTests`.

Validation: Release build, pure tests, patch probe and `nomod asm verify` pass. In-game
acceptance still needs 720p/1080p/ultrawide, a brief long enough to scroll, a mission with
real tactical/strategic thresholds, free flight with unset thresholds, and scene reload.

UX review followed the requested game-studio
[UX review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md),
[Ponytail](https://github.com/DietrichGebert/ponytail) and
[UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): the dead zone
became the brief, the escalation readout states the verified mission data and its
distance to the next gate, empty and unset states are named rather than guessed, and no
new dependency or second ladder implementation was added.

## Hosted EVN/ADM buttons and the icon pass (2026-09-15)

Boscali fields seven screens and Wing Command contributes WMC, but the stock layout only
leaves six free bezel buttons. A seventh and eighth screen therefore could not exist as
claims: whichever lost the race disappeared, and reserving a slot for WMC starved EVN/ADM.
Both are now **hosted** instead (`Infrastructure/GameInterop/MfdScreenHost.cs`). A host
appends its own button to the vanilla column lists and lets vanilla drive it — the button
and screen are added together so the two lists stay the same length, `SetupButtons` titles
it, `PressLeftButton`/`PressRightButton` toggle it, `ToggleAllButtons` shows and hides it
with the map and `HideAll*Screens` closes it. The six vanilla slots stay free for WMC and
the five claimed screens, so no reservation is needed and EVN/ADM can never be crowded
out. `MfdRailPatch.OnStructureChanged` now rebuilds the rail, so a hosted button (or ADM
tearing down when a second client connects) is branded without waiting for the next map
open.

The hosted button is built to read as vanilla before the rail restyles it: the first
sibling's fill, typography and size, plus the label and highlight children every panel's
`Build` resolves from its bezel button. When the expanded layout is off, the vanilla bezel
shows it in its own column instead; the rail adopts it like any borrowed button.

Rail glyphs were redrawn in the same pass: a heavier consistent stroke (`Line`), a solid
aircraft silhouette (`air`) now drawn as a filled polygon, a chevroned shield for the
faction HQs, a head-and-shoulders squad mark, a strapped crate for OPS and a broadcast mark
(`pulse`) for the event feed. That pass also introduced a tasking-board mark (`board`) for
ADM and the TASKING descriptor; both were removed on 2026-09-16 when the ADM bezel was
deleted (see "SET CLIENT/SERVER split" below).

Validation: the rail render check now adopts and brands thirteen keys (six vanilla
screens, WMC, five claimed, EVN) and verifies `PrepareCapacity` fits them; pure catalog
checks in `MfdPanelTests` cover the EVN entry and glyph distinctness; Release build,
module-boundary tests, patch probe and `nomod asm verify` pass. In-game visual acceptance
remains pending for 720p/1080p/ultrawide, EVN open/close, expanded/stock transitions and
scene reload.

UX review followed the requested game-studio
[UX review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md),
[Ponytail](https://github.com/DietrichGebert/ponytail) and
[UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): every panel stays
reachable instead of racing for slots, the hosted button degrades to the vanilla column
when the expanded layout is off, icons gained weight and distinct silhouettes instead of
new dependencies, and the rail's capacity is verified at its new worst case.

## MAP layer grid and overlay layers (2026-09-15)

The MAP page was six full-width bordered boxes with a persistent description line, two
oversized Show all / Hide all buttons and most of the body left empty — a wall of identical
green rectangles that said very little. It now uses the same control language as every other
rebuilt panel: a section heading on the spine and a two-column grid of toggle cells with a
state rail, a vector glyph and a latched fill. Descriptions moved into hover help, which the
status strip already carries for every control.

Layers is now eight switches in two sections — the six game layers, then Boscali's own
**Control field** and **Front line** overlays. The control tint and the front trace used to
share one config entry, so the front could not be read without the tint and neither could be
switched from the map. `Command.FrontlineTrace` splits them; the MAP switches write the two
overlay entries through `ComMapOverlay`, so the config file stays the only owner of the
state and the SET screen shows the same values. Cells are enabled only while the overlay has
resolved a map and a faction headquarters, and the readout card below reports the sector cell
size in use, whether control data is live, and both layer states. All on / Hide all /
Defaults replace the old pair, and Defaults restores the game's own answers rather than a
remembered layout.

Readability keeps the native hover-tooltip and symbol-size choices, now as 4- and 3-cell
selection rows with a written summary under them, and gains a scaled symbol preview plus a
legend drawn from the bake's own tint constants and the front line's ink
(`TacticalSectorGrid.FriendlyTint`/`HostileTint`, `FrontlineGraphic.Ink`), so the legend
cannot drift from the map. `MfdGlyph` gained `control`, `front` and `grid` shapes for the new
layer names.

No new persistence, networking or per-frame work: the panel refreshes on the existing
visible-only 0.15-second pass, and cell size is derived from the panel height
(`4 * cell + 264 ≤ body`, verified at `PanelHeight` 596 and `PanelHeightMax` 896).

Validation: Release build (zero warnings), pure tests, the Settings render check with the new
SET row, the rail render check with the three new glyphs, installed-game patch probe and
`nomod asm verify` all pass. There is no standalone render check for this page (its presenter
is one of the `VanillaMfdRebuild` partials, so a render fixture would have to stub the whole
family); in-game visual acceptance at 720p/1080p/ultrawide, with and without a faction
headquarters resolved, remains outstanding.

UX review followed the requested game-studio
[UX review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md),
[Ponytail](https://github.com/DietrichGebert/ponytail) and
[UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): the design reuses
`MfdPagingGrid`/`AvStyled` instead of growing a fourth row widget, the two new glyphs are
vector meshes rather than icon assets, layer descriptions moved to hover help rather than
being dropped, disabled cells explain themselves, and no new dependency or saved state was
introduced.

## SET CLIENT/SERVER split and host settings (2026-09-16)

SET now opens on two main tabs. **CLIENT** keeps the four client-local pages (MAP, STYLE,
IMAGE, COCKPIT) behind a second, smaller tab strip; the main strip names the audience, the
sub-strip names the console surface. **SERVER** is the host page: the faction tasking board
that used to live on the solo-only ADM bezel plus a host-only settings section for every
installed feature. The ADM bezel, its appended vanilla slot, its rail mapping and the
`board` glyph are gone.

The board consumes `ISecondaryObjectivesView` (DynamicOperations) late through
`ModServices`: card rail, title, target/status/clock and progress track, with REQUEST BOARD
disabled when the host or the module is absent, and an explicit line when dynamic
operations are not running. The old ADM chrome (its own data bar, metrics and chips) is not
reproduced; the host's tasking lives under the SET data bar, whose second chip now reads
HOST or CLIENT.

Host settings are declared by their owning feature through a new framework seam
(`Framework/Contracts/IHostSettingsView`, built with `Framework/Features/HostSettingsTable`)
and collected by `HostSettingsBoard`; Command renders them without importing a sibling's
settings object, and every row writes the module's own config entry, so the config file
stays the single owner of the value. Only settings the module reads live are exposed —
module `Enabled` install gates stay in the config file. Row values are re-read each refresh;
a feature that is not installed simply has no section. On a remote client the whole page is
read-only: values stay legible, controls are disabled, and the status strip says host only.
Authority is `GameAccess.IsServer()`, re-read every refresh.

Rows reuse the existing toggle and stepper widgets. The stepper gained a `readOnlyValue`
flag so a disabled row still shows its value (the SERVER page has to be readable without
being writable); client pages keep the old `--` for a dependency-gated row.

Validation: the standalone `Run-SettingsUnityCheck.ps1` render now builds CLIENT and SERVER
at 596 and 420 units and switches CLIENT sub-pages; pure `HostSettingMath` stepping checks
run in `FrameworkTests`; module boundary tests, the Release build and `git diff --check`
pass. In-game acceptance remains pending for host/client/listen-host, the tasking board with
and without DynamicOperations enabled, every provider section, scrolling at both panel
heights, and scene reload.

UX review followed the requested game-studio
[UX review skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/ux-review/SKILL.md),
[Ponytail](https://github.com/DietrichGebert/ponytail) and
[UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill): the second level is
one reused button style rather than a second navigation widget, host settings reuse the
existing row controls and the config entries behind them, the deleted bezel is not emulated,
and the read-only client state is explained rather than hidden.
