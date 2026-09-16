# Secondary contract markers — map, cockpit, vicinity

Status: implemented; in-game visual acceptance pending.
Date: 2026-09-16. Owner: DynamicOperations. Platform: Nuclear Option tactical map and cockpit.
Design authority: the user's remake request ("vanilla GUI + WMC squadron GUI") and the
follow-up that the first, vanilla-borrowing attempt was buggy and the vicinity card never
appeared. Method: Game Studios quick-design structure, UI/UX Pro Max status-word rule,
Ponytail reuse of the native marker objects and the shared avionics kit.

## Why the first attempt was thrown away

The first version fed synthetic objectives into `MissionPosition.GetAllPositionsResults` and
then restyled what vanilla drew with it: a patch on `ObjectiveMarker.UpdateMarker`, another on
`ObjectiveMarker.Show`, a third on `ObjectiveOverlay.UpdateOverlay`, plus reflected access to
two private label fields and extra child objects hanging off vanilla's pooled markers.

Everything that was reported as buggy came from that seam:

- a marker is a *pooled* object handed to whichever objective lands on its index, so a
  contract's plate could survive onto an authored objective, and the icon sizes (20 px vs
  40 px) change under a plate built for the other size;
- the label is vanilla's private legacy `Text`; the cockpit label is moved every frame by
  vanilla's anti-overlap nudger, so a two-line replacement inherits its jitter;
- hiding a marker only disables vanilla's own two graphics, so the plate had to be hidden by
  a patch on `Show` as well — three patches and two reflected fields to draw a label;
- the vicinity card only looked at contracts with an area *and* inside a band a few
  kilometres wide, so a contract 3.8 km away — or a point contract at any range — never
  appeared at all.

The mod now draws its contract markers itself. No vanilla marker, overlay, label field or
`MissionPosition` query is involved, patched or fed, and the whole presentation comes from the
snapshot the board already publishes.

## One vocabulary

`FAMILY · DISTANCE · CLOCK`, plus state on colour and in words (UI/UX Pro Max rule: colour is
never the only carrier). Fifteen seconds of copy: title line `#5 SURVEY THE AFTERMATH`, detail
line `RECON · 20.4 KM TO AREA · T-2:41`, inside the area `RECON · HOLD 42% · T-2:41`, a return
phase `RECON · LAND TO DELIVER · T-2:41`.

Tone: `RETURN` phases are green (`RailReady`), a clock at or under two minutes is amber
(`RailCaution`), everything else is cyan (`RailInfo`). Vanilla's own rails, no new colours.

## Cockpit markers (`ContractHud`)

One marker per accepted, located contract, projected through the game camera each frame:

- **On screen** — a pointer on the target's own point, turning toward it, with the two-line
  plate above it (`FontSmall` bold title, `FontMicro` tone detail).
- **Off screen** — the same marker clamped to the frame edge (170 px in from the sides, 130 px
  from top and bottom, so it clears the compass tape and the instruments) with the bearing
  taken from the camera-space direction, so a target behind the aircraft still points the
  right way and keeps its distance on screen.
- **Area** — a dotted ring (24 dots) at the contract's radius, sized with
  `viewport height / (2 · distance · tan(fov/2))` — the same relation the vanilla area ring
  uses, so a mod area matches a vanilla area drawn beside it. The ring is dropped when it is
  smaller than 16 px or larger than 2600 px (degenerate projections).
- Hidden while the tactical map is maximized, with no live aircraft, or when no contract has a
  position.

## Map markers (`ContractMapHud`)

The same plates, parented to `DynamicMap.mapImage` so they pan and zoom with the map: plates
counter-scale by `1 / zoom` to keep a constant screen size, the dot ring scales with the map
(`radius × mapDisplayFactor`), and the whole layer hides while the map's own objective layer
(`MapOptions.showObjectives`) is switched off. The layer re-parents itself if the map is
rebuilt.

## Vicinity card (`OperationZoneHud`)

A squadron-style card (3 px tone rail, `#5 SURVEY THE AFTERMATH`, family/distance/hold/clock,
5 px bar) listing at most three contracts:

- inside your area first, then the nearest marked contract, then anything whose contact the
  host lost — that last one is never dropped, because a lost contact is exactly what the pilot
  needs to read;
- a contract appears when it is inside `radius + max(4·radius, 20 km)`, so a contract 20 km out
  is listed rather than only in the last few seconds of the approach;
- the bar closes on the area edge and then carries the hold itself;
- entering and leaving an area raises a banner that fades in 0.14 s and out 0.24 s.

## Bounds

≤3 markers on each surface (the board's own card ceiling), content refresh 0.5 s (card 0.25 s),
positions glued per frame, one widget pool per surface built once per scene, strings cached and
rewritten only when the rounded value moves, `raycastTarget` off everywhere. No new scene
objects beyond the fixed pools, no new server state, no wire fields, and no Harmony patch on
any UI class: the only patches left in the module are the gameplay observations it already had.
