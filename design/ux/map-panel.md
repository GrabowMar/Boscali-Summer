# Map panel accessibility and functionality

Status: implemented; in-game visual acceptance pending. Date: 2026-09-15 (first version
2026-09-12). Owner: Command. Platform: Nuclear Option PC map MFD.
Design authority: user's map-panel request and existing shared avionics rules.

## Player need and navigation

Players need to see which map layers are visible and change presentation without
decoding a grid of similar green buttons. The MAP bezel opens LAYERS; READABILITY
contains hover detail, symbol size, a preview and the overlay legend. Map/bezel entry
and exit remain native. Page switching does not alter settings. No new screen
reservation or map gesture.

## Layout and controls

Both pages use the shared control language of the rebuilt panels: a heading
(`AvStyled.SpineTick`, section title, note, hairline) and `MfdPagingGrid` cells in the
house toggle paint, each with a vector glyph, a state rail and a latch fill. Hover help
carries what used to be a permanent description line; the status strip already shows it.

- **LAYERS — MAP LAYERS**: the six game layers (objectives, target details, jamming,
  grid labels, dismounted pilots, airbases) as a two-column grid of switch cells. Each
  cell calls its own native toggle and reports `ON`/`OFF` by latch fill *and* hover help.
- **LAYERS — BOSCALI OVERLAYS**: **Control field** (the sector-control tint) and
  **Front line** (the vector front trace) as a second grid. A cell is enabled only while
  `ComMapOverlay` has resolved the map and a faction headquarters; otherwise it is
  disabled and its hover help says what is missing.
- **LAYERS — presets**: All on / Hide all / Defaults, with 30-unit controls. All on and
  Hide all state the whole set and skip layers already satisfied; Defaults restores the
  game's own answers (all six layers on, tooltips on Info, symbols at 100 %, both
  overlays on) instead of a remembered layout. Disabled presets explain why.
- **LAYERS — overlay readout**: a card carrying the sector cell size actually in use,
  whether control data is live, and both overlay states. A figure that is not
  established reads as a dash.
- **READABILITY — hover tooltip**: four exclusive choices (Off, Unit info, Ammunition,
  Orders) in a single 4-cell row, plus a written summary line so the selected mode never
  depends on colour.
- **READABILITY — symbol size**: the three native 60 % / 80 % / 100 % choices in a
  3-cell row. A native size the row does not own reads Custom rather than falsely
  selecting Large.
- **READABILITY — symbol preview**: aircraft / ground / ship glyphs, centred and scaled
  about their own middle at the selected size, with an explicit note that the preview is
  illustrative.
- **READABILITY — map legend**: friendly ground, hostile ground, contested (a split
  swatch) and the front line, drawn from the bake's own constants
  (`TacticalSectorGrid.FriendlyTint` / `HostileTint`, `FrontlineGraphic.Ink`) so the
  legend cannot drift from what the map paints.

Cell height is derived from the panel body (`clamp((body − 272) / 4, 42, 72)`), so the
LAYERS page fits whole at `AvTokens.PanelHeight` (596) through `PanelHeightMax` (896):
`4 × cell + 264 ≤ body` holds at both ends. Both pages fit without scrolling, shrinking
type below the shared minimums, new animation or an extra rendering loop. Buttons use the
shared press/hover feedback; status never depends on hue alone.

## Data, actions and missing state

`MapOptions` owns the six game layers, the hover mode and the symbol size. Boscali's two
overlays are owned by `ComMapOverlay`, which reads and writes the existing
`Command.FrontlinesOverlay` and `Command.FrontlineTrace` entries and re-bakes immediately
on a change — the config file stays the single owner of the state, and SET shows the same
two values. `Command.FrontlineTrace` was added so the front line can be read without the
tint; it only draws while the control field is on. The panel keeps no state of its own and
adds no persistence, networking or presets; the MAP bezel resolves the overlay through the
service registry (never a scene search) and treats it as optional.

Clicking an already-selected choice is a no-op. Changes made elsewhere (config file, SET,
native UI) appear on the existing visible-only 0.15-second refresh.

Missing `MapOptions` or `DynamicMap` disables every write and displays dashes / Waiting for
map. A missing or uninitialised overlay disables only the two overlay cells, with the
reason on hover. The click callback re-checks availability, so a scene transition between
refresh and click cannot call a missing native controller. Non-finite size values cannot
poison preview geometry. The existing adapter-error footer handles unexpected native
failures.

## Accessibility scope and UX review

Game Studios ux-review checklist: player purpose, navigation, hover descriptions, input
scope, data ownership, selected/disabled/unavailable states and acceptance criteria are
specified. Implementation-ready within the current PC avionics interaction model.

This improves pointer accessibility and legibility. It does not introduce screen-reader
support, localization or keyboard/gamepad navigation: shared avionics explicitly requires
`Navigation.Mode.None` and deselection to protect flight controls. Existing bezel access
remains. Arbitrary user themes and live-game fonts still require in-game review.

## Acceptance criteria

1. All six layer cells call the correct native setting and report ON/OFF in both paint and text.
2. Both overlay cells read and write `FrontlinesOverlay` / `FrontlineTrace`, and the map
   re-bakes without waiting for the next refresh interval.
3. All on / Hide all / Defaults reach the requested state, are idempotent, and preserve
   hover mode and symbol size (except Defaults, which sets them deliberately).
4. Exactly one standard hover/size choice shows selected; native external edits refresh.
5. Changing pages preserves all native settings and both overlay entries.
6. Missing map, or a missing/uninitialised overlay, disables only what it owns and says why.
7. Both pages fit above the status strip at minimum and maximum panel height.
8. A custom/non-finite size does not falsely select a standard size or corrupt geometry.
9. Preview icons stay visible and centred at each supported symbol size.
10. The legend swatches match the tint and ink the map actually paints.

## Validation

Release solution build (zero warnings/errors), full module/framework/architecture
assertions, installed-game PatchProbe, `nomod asm verify` and `git diff --check` pass. The
standalone Settings render check passes with the new SET CONTROL FIELD / FRONT LINE rows at
596 and 420 units, and the rail render check passes with the three new glyph kinds
(`control`, `front`, `grid`).

There is deliberately no standalone render check for this page: its presenter is one of the
`VanillaMfdRebuild` partials, so a render fixture would have to stub the entire panel
family (`TargetListSelector`, `ObjectiveInfoList`, `InfoPanel_Faction`, `HUDOptions`,
`FactionHQ`, …) to compile at all. In-game acceptance must cover 720p/1080p/ultrawide, map
open/close, scene reload, a mission with and without a resolved faction headquarters, both
panel heights, and the two overlay layers switching live.

The preview images that accompanied the previous revision are gone with it; they showed
the superseded row layout and would have misrepresented the panel.
