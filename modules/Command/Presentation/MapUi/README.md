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
