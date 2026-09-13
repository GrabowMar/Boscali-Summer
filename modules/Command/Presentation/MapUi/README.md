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
  type-derived category names. MIS wraps its title and briefing, shows an escalation
  ladder, and gives each active objective a percentage and progress bar.
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
| User-saved presets | Snapshot all filter groups and distinguish saved/custom/HUD-linked states. Needs stable definition identifiers and migration for changed catalogs. | Medium; defer until built-in profiles are play-tested |
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

Players arrive from the SET bezel to improve map readability or choose a background.
The compact 596-unit panel leaves space for the event log; its pages share a fixed
status strip. MAP owns tactical/terrain visibility and update interval; STYLE owns
console opacity, map darkening, one decoration selector and ticker controls; IMAGE
owns image opacity, local file selection, fit and explicit rescan. Re-selecting the
bezel closes SET; selecting another bezel replaces it in the expanded dock. Map
close hides the UI and restores native presentation. No enter/exit animation is added.

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

Input follows the shared avionics contract: pointer controls, 44-unit step buttons,
no automatic joystick navigation, and deselection after clicks. Text labels and
ON/OFF states supplement color. There is no claim of keyboard-only or screen-reader
accessibility. Small bodies scroll rather than compressing controls below their
minimum size. The fixed status strip remains outside the scroll viewport.

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

The wire now behaves like a news service instead of a kill log. Consequential events
become headlines — airbase captures, strategic launches, ace defeats, aircrew rescue or
capture, capital ship losses, warhead interceptions, aircraft shootdowns, demolitions and
war-effort deliveries — while routine traffic (missile interceptions, anonymous armor
losses, crash sites) is counted and condensed into periodic air-defense, front and
recovery digests. First blood, three-kill and ace watch streaks, a session casualty
ledger, one-line follow-up reactions and a wider LARP pool (with HOME FRONT / ECONOMY /
RUMOR desks) fill the rest.

Priority is explicit: critical and major headlines interrupt and render bold, notable
headlines join the next cycle without rewinding the scroll, routine traffic never becomes
a headline. Queue, history, streak and pending-digest counts stay hard-capped. Breaking
news rewinds the marquee to the badge and pulses the channel dot and alert rail for eight
seconds; the rail and dot return to accent when calm, so the cue never relies on color
alone. Logistics chat is throttled, repairs and non-event chatter are ignored.

Validation: pure parse, suppression/digest, streak, throttle, dedup and marquee-loop
checks in `MfdNewsTickerTests`; Release build, module-boundary tests, patch probe and
`nomod asm verify` pass. In-game visual acceptance (720p/1080p/ultrawide, map open/close,
scene reload) remains pending.

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
