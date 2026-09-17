# Secondary contract markers — map, cockpit, vicinity (vanilla+)

Status: redesign in progress. Supersedes the "own plates/rings/rails" version.
Date: 2026-09-17. Owner: DynamicOperations. Platform: Nuclear Option tactical map and cockpit.
Design authority: the user's verdict that the plate/ring/card version was "very off and ugly",
and the instruction to look like vanilla while staying slightly distinctive.

## Rule zero

Every visual fact comes from the vanilla objects themselves, read **read-only** at runtime
(`Infrastructure/GameInterop/VanillaHudStyle.cs`) and cached per scene. Never write to, patch,
re-parent, disable or otherwise touch a vanilla marker, overlay, label or material. Never
hardcode a font, sprite or colour that vanilla can supply — custom themes and user options
exist. Geometry that vanilla computes (ring size, ring alpha, label glide and anti-overlap) is
re-implemented with vanilla's own numbers and constants, spelled out below.

## Vanilla facts we imitate (measured, decompiled + prefab-read)

Cockpit (`ObjectiveOverlay`, 3 nearest objectives, global namespace):

| Element | Vanilla value |
|---|---|
| pointer | `arrowOutline` sprite, 25×25, anchors/pivot (0.5, 1), local y −0.8, colour = HUD main colour (#00FF00 α 0.8) |
| dot | `CircleVeryThick` sprite, 10×10, same colour; shown instead of the pointer when the target is within 10° of the nose |
| area ring | `waypointSizeIndicator` sprite in a 20×20 rect, colour #00FF00 α 0.8, `localScale = canvasHeight / 20 / tan(fovY/2) · radius / distance`, alpha `clamp01(radius · 20 / distance − 0.5)`, rotation `Euler(0,0,−camera.eulerAngles.z)`, hidden while the marker is clamped to the frame edge |
| label | TMP `Brass Mono Regular` (+ its shared material), size = `PlayerSettings.overlayTextSize` (default 32) **carried through the label rect's 0.5 transform scale**, so the label renders at 16 px — copying the font size without the scale is what made the first vanilla+ attempt read twice as loud as the objective beside it |
| label motion | lerps 20 % per frame toward its anchor (`textLerp` 0.2); two labels closer than 50 px push apart at `(1000, 300)` px/s along the normalised delta (with `dy += 5` when `dy < 0.1`), and the nudge offset decays ×0.8 per frame |
| pointer rotation | `atan2(dy, dx) · Rad2Deg − 90°` |

Distance reading (`UnitConverter.DistanceReading`): metric → `>10000` ⇒ `{km:F0}km`, `>1000` ⇒
`{km:F1}km`, else `{m:F0}m`; imperial → `{yd:F0}yd` below 1000 (m × 1.09361), else
`{nm:F1}nm` (m × 0.000539957). No space in front of the unit, no group separators.

Map (`ObjectiveMarker : MapMarker`, parented under `DynamicMap.iconLayer`):

| Element | Vanilla value |
|---|---|
| icon | `Image` with the family sprite (see below), `sizeDelta` 20×20 for waypoint/destroy, 40×40 for recon/capture, colour white |
| label | legacy `Text` with font `regular`, 24 px **at the prefab's 0.5 scale** (12 px on screen), white, `MiddleCenter`, offset `(0, size)` in marker-root space (so 20 or 40 px above the icon), text `DisplayName` |
| transform | `localPosition = worldXZ · DynamicMap.mapDisplayFactor`, `localScale = 1 / mapImage.transform.localScale.x` (constant screen size) |
| duplicate masking | only two markers of the *same* objective within 40 screen px are masked (icon ×0.5 RGB @ α0.5, label off); one contract is one tag, so contracts are never masked |
| family sprites | `waypointObjective` (`waypointObjMarker`), `destroyObjective` (`steerpointMarker`), `reconObjective` (`targetLockOld`), `captureObjective` (`circleDot`) |
| toggle | all hidden while `MapOptions.showObjectives` is false |

Vanilla draws **no** area ring for objectives on the map. Vanilla *does* draw radius rings on
the map for nuclear exclusion zones (`GameAssets.exclusionZoneDisplay` instantiated under
`iconLayer` with `localScale = radius · mapDisplayFactor`), so an area circle sprite scaled by
`radius · mapDisplayFactor` is a vanilla-consistent idiom.

There is **no vanilla proximity popup** for objectives: the closest analogue is the HUD cargo
panel (`HUDCargoState.CapturePanel`, `Background` 9-slice frame, a 244×8.6 bar, TMP title,
CanvasGroup fade at 2 alpha/s) and the MFD `MIS` objective list. The vicinity card therefore
copies the HUD-text idiom (vanilla font, vanilla colour, thin bar, no chrome), not a box.

## One vocabulary (unchanged)

`FAMILY · DISTANCE · CLOCK` on the detail line, title line `#9 BREAK ENEMY PRESSURE`,
`16km` (past 10 km, no decimal), `9.4km` (a kilometre to ten), `842m`, `26.0nm` formatted exactly like vanilla (metric/imperial from
`PlayerSettings.unitSystem`, never grouped digits). Colour is never the only carrier: the
clock, the hold percentage and `CONTACT LOST` all print.

Tone (all vanilla theme colours, read live via `ThemeManager.Active.ColorTheme`):

| Tone | When | Colour |
|---|---|---|
| normal | clock > 120 s or none | `AllClear` (#00FF00) — identical to a vanilla marker |
| caution | clock ≤ 120 s | `Warning` (#FFFF00) |
| lost | host lost the contact | `Alert` (#FF0000) |

The icon/ring itself stays `AllClear` (vanilla-identical) for a located contract; tone shows on
the detail line and the card bar. A lost contact has no position: it is listed in the card,
never drawn.

Distinctive, minimally: the `#N` number prefix on both surfaces and the second line
(`STRIKE · 9.4km · T-2:41`) under the cockpit label. Nothing else deviates from vanilla.

## Cockpit marker (`ContractMarker.cs`, managed by `ContractHud`)

One widget pool per scene, ≤3 (the board's card ceiling), fixed hierarchy:
`root → pointer(25×25) / dot(10×10) / ring(20×20) / label(TMP, 2 lines)`.

- Projection and clamping: keep the existing `ContractMarkerMath` behaviour — in front of the
  camera the target's own point; behind or off screen, the bearing-mirrored point on the frame
  edge (`EdgeMarginX/Y` 170/130 px) and no ring. Never hide a located contract for being
  off screen.
- Pointer/dot switch at 10° from the camera forward axis (`Vector3.Angle(camera.forward, dir)`),
  with `dir` measured from the *aircraft* as vanilla does — not the screen-centre offset. The
  pointer's angle is taken before the edge clamp, so a marker pinned to the frame edge still
  points at its target; ring and label distances read from the aircraft for the same reason.
- Ring: vanilla formula above, using `canvasRect.rect.height` as `canvasHeight`, drawn only
  when not clamped and the pixel diameter is 16..2600 px, roll-locked to the camera.
- Label: two stacked TMP labels (title above detail; both the vanilla font and shared material,
  detail at `0.66 ×` size in the tone colour), one block, pivot (0, 0.5), anchored 18 px to the
  right of the marker point and vertically centred; the block carries vanilla's 0.5 scale, flips
  to the left of the point when it would cross `halfWidth`, and its rect width follows the text
  (the flip test multiplies by that scale, since the rect is measured pre-scale). Text is cached
  and rewritten only when its inputs move.
- Label motion: vanilla's glide + anti-overlap nudge (numbers above) applied to the label
  position only — the icons stay glued to their projected points. The separation test uses the
  labels' anchors, not their drifting points, and pushes *both* labels apart like vanilla's own
  manager, so a pair cannot pump in and out of range. This is what stops the unreadable
  stacking the user screenshotted.
- Hidden entirely while `DynamicMap.mapMaximized`, with no local aircraft, ejected/disabled, or
  when no contract has a position.

## Map markers (`ContractMapTag.cs`, managed by `ContractMapHud`)

Parented to `DynamicMap.mapImage` (re-parent if the map is rebuilt), hidden while
`MapOptions.showObjectives` is false, empty, or the map is closed. Icon rect 20×20 (waypoint /
destroy families) or 40×40 (recon / capture), counter-scaled `1 / zoom` at the tag root,
positioned `worldXZ · mapDisplayFactor`. Label is a legacy `Text` in the vanilla map font
(24 px, white, `MiddleCenter`) at offset `(0, iconSize)`; its text is `#N NAME`. Tone tints
only the detail-less label of a caution contract and the area ring:

- area ring: `waypointSizeIndicator`-style circle sprite, rect `2 · radius · mapDisplayFactor`
  square, colour `AllClear` @ α 0.35 (tone when caution), skipped below 24 px.
- no masking: vanilla's 40 px mask exists for two markers of the *same* objective, and one
  contract is always one tag, so two nearby contracts stay fully drawn, exactly as vanilla
  draws two distinct objectives side by side.

## Vicinity card (`OperationZoneHud`)

Vanilla HUD text, no panel chrome, right-centre of the screen (clear of the message log, ammo
block, throttle scale and compass tape): anchored `(1, 0.5)`, pivot `(1, 0.5)`, offset
(−28, 60). Own overlay canvas, `sortingOrder` 1 (above `HUDCanvas` 0, below the map's 2),
`CanvasScaler` 1920×1080 with `ScreenMatchMode.MatchWidthOrHeight`, match 1 — vanilla's own
scaler — and every size below is vanilla's *effective* one: `overlayTextSize × 0.5`, i.e. 16 px
at the default.

- header line `2 ACTIVE CONTRACTS` @ `0.7 ×` size, `AllClear` @ α 0.45 (the enter/leave banner
  swaps into this line for 4.5 s, tone-coloured). The block hangs on a child of the canvas:
  a screen-space canvas drives its own root rect, so anchors on the root are not layout input.
  The font is the vanilla HUD font; if the style read fails the card falls back to the engine's
  default TMP font and warns once — the card carries information a pilot needs, unlike a marker.
- one row per contract (≤3), right-aligned, a single TMP line built with rich-text colour
  segments — `#9 BREAK ENEMY PRESSURE   9.4km   T-2:41` with the title in `AllClear` @ α 0.9,
  the distance @ α 0.6 and the clock in tone (`OperationMarkerCopy.Distance`, so metric and
  imperial print like vanilla); a lost contact prints `CONTACT LOST` in `Alert`.
- one bar per row: 244 × 8.6 px (vanilla capture-bar size) `AllClear`/tone, left-pivot grow
  = `OperationMarkerCopy.Bar(...)`, over a plain dark backing 12.8 px tall @ α 0.3 only while
  the bar is shown.
- selection unchanged: ≤3 rows via `ContractSelection` (inside first, then nearest, then lost),
  band `radius + max(4·radius, 20 km)`, fades 0.14 s in / 0.24 s out.
- `raycastTarget` off everywhere, `CanvasGroup.blocksRaycasts = false`.

## Bounds

≤3 markers per surface, one fixed pool per surface per scene, content refresh 0.5 s (card
0.25 s), positions glued per frame, strings cached, no new wire fields, no server state, no
Harmony patch on any UI class. Style reads: `ObjectiveOverlayManager.overlayPrefab`,
`ObjectiveMarkerManager.markerPrefab`, `ObjectiveMarker`'s four sprites, `MapMarker.markerImg`,
`GameAssets.exclusionZoneDisplay`, `ThemeManager.Active`, `PlayerSettings` — all read-only,
cached per scene, invalidated on scene reset (`VanillaHudStyle.Invalidate()`).
