# HUD module

Rebuilt 2026-09-28 (status item) and extended the same day with Wingview (third-person camera
and HUD, `docs/superpowers/specs/2026-09-28-wingview-third-person-design.md`). Pinned names:
`HudFeature` (id `hud`), `HudBoard` (also the static `HudBoard.Instance` patches reach into,
same pattern as `AutopilotLandController.Instance`), `StatusPanel`, `Runtime.ExternalHudEnabler`,
`Runtime.WingviewCameraState`, `Runtime.ThirdPersonHudCenter`, `Runtime.NativeFlightNumberHider`,
`Presentation.ThirdPersonHudCluster`, `Patches.WingviewCameraPatch`, `Domain.WingviewMath`,
`Domain.ThirdPersonHudLayout`. The one Harmony patch is Wingview's own postfix on
`CameraOrbitState.UpdateState`; nothing else in this module patches.

## The one design rule

`StatusPanel` is a plain UGUI tree parented directly under the native weapons panel
(`HUDCanvas/HMDCenter/TopRightPanel`, found through `SceneSingleton<CombatHUD>.i.iconLayer`
the same way the reference mod finds its own HUD canvas). Because it is native's own child,
visibility simply follows vanilla: shown whenever the native `FlightHud` canvas is active.
Never SetActive, force, replay or reposition a native object from `StatusPanel` itself --
the only Unity objects it creates are its own. It does hide the native boxes it replaces (the weapon,
countermeasure and capacitor boxes behind the stores header; the kill feed's plate and text under DL EVT LOG; the
minimised map's grey plate), with owned groups or renderer alpha, and gives them back the frame it stops drawing
(board off: the game's own weapon box is back, verified in game 2026-11-16). The one deliberate exception
to "no vanilla-visibility manipulation" lives in `Runtime/ExternalHudEnabler.cs`, below.

## Forcing the vanilla HUD open in orbit/chase (`ExternalHudEnabler`)

Vanilla disables `FlightHud`'s canvas (and, outside chase, the minimap) on entry to the orbit
and chase camera states -- see `CameraOrbitState`/`CameraChaseState.EnterState` -- which would
otherwise leave both the native instruments and this module's own status panel blank while the
pilot free-looks around their own aircraft. `ExternalHudEnabler`, ticked from `HudBoard`'s
`LateUpdate` every frame (cheap: a handful of field reads, no allocation), forces both canvases
back open while ALL of the following hold:

- The board is alive (always on since 2026-10-07; there is no switch).
- The local player's own live aircraft is being viewed: not `disabled`, not `HasEjected()`,
  cockpit attached and not detached, `CameraStateManager.followingUnit` is that aircraft, and
  `Player.IsLocalPlayer`.
- `CameraStateManager.currentState` is `orbitState` or `chaseState`.
- The map is not maximized, the game is not paused (`GameplayUI.GameIsPaused`), not
  `PlayerSettings.cinematicMode`, and not `Application.isBatchMode`.

`ExternalHudEnabler.IsOwnAircraftExternalView` is the pure query for the aircraft/camera-state
part of that gate; `HudBoard` also uses it to gate the target-camera inset (below).

Only a `GameObject` that is actually inactive gets `EnableCanvas(true)`'d. The enabler records
that exact object and restores its inactive state when the gate closes in the same native
camera state/ownship (map maximize, pause, cinematic mode, death, settings off, scene reset,
disable or teardown). A camera state/ownship transition releases the old override without
overwriting visibility chosen by native `EnterState`. Never deactivates a `GameObject` this
enabler did not activate; never restores a maximized minimap. The condition is re-checked
every tick because vanilla re-disables the canvas on every fresh orbit/chase entry.

## Rebuild safety

`StatusPanel.Ensure()` re-resolves the dock every tick (two null checks once built) and
rebuilds lazily the tick after the native canvas/panel is lost (scene reload). `Destroy()`
only ever destroys the GameObject this module created; a destroyed native parent has already
taken it with it, so `Destroy()` on an already-gone tree is a no-op. Batch mode creates
nothing (`HudBoard.LateUpdate` returns before `Ensure()` when `Application.isBatchMode`).

## Look native (base-game aligned, 2026-10-07)

Everything copies the native HUD's own language, verified offline by compositing onto real in-game
captures (`%TEMP%/nomodkit/sim/all-airframes-native-*`): HUD-theme ink (`ThemeManager.Active.ColorTheme`
`AllClear`/`Warning`/`Alert`), the native weapon label's font (`WeaponStatus.nameText`, by reflection),
thin sharp strokes, no rounded or smoky plates. `raycastTarget` is false on every element.

- **Status board** (`StatusPanel`, under `TopRightPanel`): an extension of the native weapons panel, sized from it
  at build (unit = native `WeaponStatus.panel` height / 95; text sizes = shares of the native name/ammo labels'
  point sizes, which run ~2.5x their glyph height) and right-aligned to the visible screen edge, because
  `TopRightPanel` itself runs past it (in-game review 2026-10-07: fixed sizes rendered at half height and spilled
  off-screen; borrowing the panel's own sprite drew its big diagonal). Plate: `Core/Ui/HudSprites.Chamfer`.
  Top row: always-on annunciator lamps (`Domain/HudLamps.cs`: GEAR FA HOVR RDR NVG SAFE ENG; read in
  `HudBoard.ReadSystems` from `CombatHUD.aircraft`, SAFE from the native `WeaponStatus.safetyImage`; systems an
  airframe lacks are left out). Each live feed is one outlined cell like the native IR Flares / gun cells: a figure top-left, line-art
  hardware in the middle (`Domain/HudSymbols.cs` `HudGlyphs`, keyed by the feed's channel: drop tank, runway,
  airframe-in-lock, headset, mast, strike reticle, hazard, hostile delta, pennant, shell, front line, cloud), a
  short label at the bottom (`HudCellText.Split`). One capacitor-style bar under the cells carries the most severe
  line's detail and progress. Caution/warning cells wash and light in their tone, a new warning flashes 1.6 s,
  a transient notice has a dashed outline. The symbol mesh (one `AvVector`) rebuilds only when content changes.
- **Tactical features** (2026-10-07, after KaceyTronic-RWR and NO_Tactitools; pure rules in `Domain/HudTactical.cs`):
  a threat row under the systems lamps -- LOCK / SPKE / MSL n (`Runtime/ThreatWatch.cs`: one `Aircraft.onRadarWarning`
  subscription, lock = `isTarget`, plus polling `MissileWarning.knownMissiles`), a threat dial (chevron at the nearest
  inbound missile, else the locking emitter, HI/LO), and shot-tracker pips (`Patches/ShotTrackerPatches.cs`: read-only
  postfixes on `Missile.StartMissile` and the `UserCode_RpcDetonate_*` RPC body found by name prefix, second arg =
  struck armour; asm_verify reports it "no-target" because TargetMethod is dynamic). Each new aircraft runs a lamp test
  (sweep, flash, SYSTEMS ONLINE). Third person adds a +/-45 bank arc and slip ball under HDG.
- **Third-person cluster**: bare ink numbers in corner brackets, thin tick scales for throttle/fuel, a black slab
  with a sharp outline for the target card; cues light brackets and text (see `HudCues`).

## Wingview camera

`Runtime/WingviewCameraState.cs`, driven by `Patches/WingviewCameraPatch.cs` (one postfix on
`CameraOrbitState.UpdateState`), adjusts the resting pose of vanilla's own orbit camera for the
local player's own live, undetached, unejected aircraft. v1 covers orbit only; chase positions
stay 100% vanilla. All pose math is pure and tested in `Domain/WingviewMath.cs`
(`Core.Math.Vec3`/`WingviewMath`, no UnityEngine): direction blend (60% flight-path/40% nose, pitch
halved so a vertical climb or dive never flips the camera over), a yaw-rate look-ahead clamped to
±12°, the pose itself (eye behind-and-above, look target ahead-and-slightly-above, vanilla's own
`(1 + maxRadius*(1+viewDistAdjust))*2` distance), and frame-rate-independent exponential
smoothing (`t = 1 - exp(-rate*dt)`, position rate 6, rotation rate 8 -- exact under any step size
for a fixed target, asserted directly in `WingviewMathTests`).

The postfix runs after vanilla has already placed the camera and read this frame's pan/tilt
input, so overwriting `cam.transform` never fights the frame that just ran; only
`panView`/`tiltView` (Harmony field injection, `ref`) feed forward into next frame's vanilla
math. Wingview yields entirely while native look-at-target is blending in
(`lookAtTargetLerp > 0`) and holds vanilla's own pan/tilt placement during free-look (pan/tilt
input this frame with the cursor hidden); after 1.5 s idle it eases `panView`/`tiltView` back to
0 (τ 0.6 s) and then takes the pose back over. A camera-state change, ownship change or the
global gate closing (map maximized, paused, cinematic, batch mode, not the local player's own
live aircraft) snaps every smoothing state -- no whip on re-entry. After placing the camera,
Wingview re-runs vanilla's own pivot-to-eye linecast (`PhysicsLayers.StaticsMask`) so terrain and
walls still pull the eye in. Always on with look-ahead (not configurable since 2026-10-07).

## Third-person HUD

Scoped to the same `ExternalHudEnabler.IsOwnAircraftExternalView` condition (orbit or chase, own
aircraft) -- computed once per tick in
`HudBoard.LateUpdate` as `thirdPerson` and threaded into all three pieces below, so they turn on
and off in lockstep with the forced-canvas condition that already governs whether the HUD they
live on is even visible.

- **`Runtime/ThirdPersonHudCenter.cs`** subscribes once (tied to `HudBoard`'s own
  Configure/OnEnable/OnDisable/OnDestroy) to `Canvas.preWillRenderCanvases` and URP
  `RenderPipelineManager.beginCameraRendering`. Canvas rebuilds re-project
  `FlightHud.HUDCenter` with vanilla's own formula (`cockpit.position + cockpit.forward*4000`
  through `WorldToScreenPoint`) but an **identity rotation** instead of vanilla's bank-compensating
  one, and re-projects the velocity vector the same way vanilla does in `FlightHud.Update()`.
  Every canvas preparation also refreshes native target/threat, hit and objective marker
  projection before Unity rebuilds the geometry; this repairs an early forced-canvas preparation
  followed by native Update even when the camera is unchanged. The main-camera callback is a
  changed-view fallback (frame, ownship/native HUD identity, camera matrices and pixel rect),
  which re-projects and forces a canvas rebuild. Target, relief, overlay and preview cameras
  cannot consume that refresh. Repeated same-frame preparations reuse one native jamming
  offset/alpha sample per marker. Objectives reuse native cached mission results and move label
  positions with the projected pointer, preserving declutter offsets without repeating mission
  queries, overlay creation, smoothing or nudge decay. Selected-target information and offscreen
  target captions and visible hovered-ally labels follow the refreshed marker/arrow tail.
  The presentation-only native `ShowTargetInfo` delegate refreshes current known-target content,
  onscreen/offscreen visibility and selected theme colour after jamming, with no input, target
  selection or weapon solver replay. `Runtime/ThirdPersonAirbaseProjection.cs`
  re-projects cached base/runway choices and delegates runway borders/glideslope to native
  geometry-only methods; it never calls native LateUpdate/PositionMarkers or advances taxi,
  reached-runway, takeoff or landing registration. Airbase and ally components are resolved from
  the native canvas only when that canvas changes.
  `Runtime/ThirdPersonWeaponProjection.cs` re-projects boresight/lead/lag,
  cached CCIP impact/CCRP alignment and native turret crosshairs using native cached world
  solutions. It never replays `UpdateWeaponDisplay`, `DisplayLead` or `UpdatePipperPosition`,
  which also mutate solver/smoothing/FOV state. Native weapon validity stays in normal Update.
  CCIP uses native `ccipFallTime.enabled` as cached trajectory validity and recomputes pipper
  visibility from the final camera, recovering a camera-only hide without reviving an invalid cue.
  The complete pause/map/cinematic/setting gate is shared by canvas forcing, hider, cluster
  and projection; nothing is written outside it. `HudBoard` runs after the native camera
  manager (order 1000 vs 2), hides the cluster immediately before its 10 Hz telemetry throttle,
  clears Wingview smoothing during inactive camera states, and releases native overrides and
  render subscriptions on disable.
- **`Runtime/NativeFlightNumberHider.cs`** hides the SpeedGauge airspeed text/border, the
  Altitude/Climbrate/Bearing/MachIndicator/GIndicators/FuelGauge/ThrottleGauge apps, the AoADisplay
  AoA text, `HeadMountedDisplay`'s speed/altitude/bearing/horizon boxes, and
  `FlightHud.compass`/`pitchCompassCenter` -- by adding (or reusing) a `CanvasGroup` per leaf
  graphic and driving its alpha to 0, exactly the retired `NativeHudPresentation`'s mechanism
  (`git show HEAD~:modules/Hud/Runtime/NativeHudPresentation.cs`). Stall/overspeed warning texts
  are never in the list. Discovery (walking the hierarchy) runs only on an ownship change or a new
  `HUDAppManager` instance (HUDExtras spawn late) -- never a per-frame scan. Restore is exact on
  every exit path (cockpit, map, pause, board disabled); groups are destroyed
  only on scene reset/teardown, never on an ordinary third-person exit.
- **`Presentation/ThirdPersonHudCluster.cs`** is our own objects parented under the HUDCanvas root
  (found the same way `StatusPanel` finds its dock), active only while `thirdPerson`: SPD/ALT
  boxes with a Mach/G and climb-rate sub-line, an HDG box, slim fuel/throttle bars, and the
  target-camera card relocated here from `StatusPanel` (bottom-right, 300 wide, aspect-fit, header
  `TGT · <code> · <range>`, collapses with no target). The missile feed card (2026-10-12, `MSL · <weapon> · <range to its target>`)
  is the same widget one slot up: above the target card in third person (in its place when there is none), and in the
  cockpit, where the cluster's root is shown with the flight boxes off. Feed column (2026-10-20, user: "uav on bottom, missile and
  target on top"): the VANGUARD UAV card (its own canvas) holds the bottom right and publishes its screen rect in `Core/Ui/FeedCorner`;
  `ThirdPersonHudLayout.TargetCard`/`MissileCard` stand the target card on it and the missile card on top. A missile card that would
  run into the TAC BOARD's box goes left of the card under it. Sim: `tests/ingame/feed-column.json`. Its picture is `Runtime/MissileFeedCamera.cs`: our own
  camera on the nose of the last own weapon in the air (a copy of the main camera's settings, no cockpit/HUD/UI layers, no post or
  shadows, 256x144 at 15 Hz by enabling the camera for one frame), held 1.5 s as SIGNAL LOST after the weapon ends, off while the
  orbit camera rides a missile. Mod clouds reach the feed through the weather's `CloudProbePass` (it marches into every `FeedCamera.IsLens` camera, 2026-10-20). All geometry is
  `Domain/ThirdPersonHudLayout.cs` (pure, `RectF` + fractions of the HUD canvas's own rect, tested
  at 16:9/21:9/32:9/4:3): the aircraft-frame guard (`AircraftFrame`, bottom-centre box) is
  authoritative -- `KeepClear` nudges an element up rather than trusting the nominal offsets alone,
  since the HDG box's nominal `-0.30·H` and the frame's nominal bottom 30% band overlap on paper.
  Text is written through `NOAvionics.AvNumFormat`/`AvUnitTable` into one reused `char[]` per
  cluster; a `string` is allocated only the tick a rounded displayed value actually changes.

## Contract and data

`IHudBoard`/`IHudLine` (`Core/Contracts/IHudBoard.cs`) and `HudLayout`/`HudPrimitives` are the
whole publisher surface (Comms, DynamicOperations, Events, Session, Weather):
`DeclareChannel` (a no-op kept so publishers stay unchanged), `Acquire` and `Notice`. `HudBoard`
owns `HudFeedStore` (bounded held lines) and `HudNoticeQueue` (bounded transient notices); both
are pure. Text writes only on change (cached per row); refresh is throttled to 10 Hz in
`HudBoard.LateUpdate`; row colour/fill/position writes happen only inside that tick.

## TAC BOARD controls (2026-10-18)

The pilot explicitly requested manual context and broader section switches. `ITacBoardSettings`
owns client-local `[TacBoard]` settings for C-menu HUD and SET's cockpit page. One to four saved
`BoardPage` slots (`Page1..Page4`); feed activity never changes the choices. All feeds keep
polling for alerts even when their page is not visible. Preserve selected-page titles at every
declutter level. Selecting an occupied page swaps the saved slots, including hidden slots, so
increasing page count cannot create duplicates.

Rework of 2026-10-18 (user: "inputs do not come into UI and it always awaits data", "Systems should
not be togglable but always display", "two windows max horizontally and two max vertically"):
- SYSTEMS is the board's own always-on block under the lamp line (`BoardGeometry.Status`), never a
  slot: `BoardOptions.Sanitize` moves a saved SYSTEMS slot to that slot's default, the C-menu and
  SET skip it. No switch hides it (`ShowSystemsStrip` is gone); it shows while its feed is Ready.
- Layout (`ScopeLayout.Compute`): every block but the WIDE stores and the second page column hangs
  in the right-hand 440 "spine". Pages are a grid of at most 2 x 2 (`ScopeLayout.Columns`: WIDE
  fills a row first, STACK a column first); a row that is not full sits against the right edge.
  One column starts under SYSTEMS; a row of two starts under the stores. Size and anchor are fitted
  to the settled (not the wiping) geometry.
- `Poll` means "has something to draw"; a page whose feed has nothing calls `PaintIdle` (its own
  `IdleWord`/`IdleHint`; THREAT draws the empty scope and CLEAR). WING is drawn whenever there are
  wingmen (the old "needs attention" gate only decided auto-selection). MISSILE CAM's feed follows
  the ridden missile, else the newest own missile in the air.
- The gauge row takes the own gauges of every visible page, then SYSTEMS'.
- EXPENDABLES (same day, user: countermeasure selection "buggy and unreadable"): one row per
  countermeasure, the jammer included, in the game's Next Countermeasure order
  (`CountermeasureWatch.InCycleOrder` via `AircraftReadout.CountermeasureOrder`; the mod's chaff,
  not in the game's list, goes last). Only the selected row has the caret, bold BRIGHT words and SEL
  at its end (a jamming jammer says ON); levels are bars in one column (`StoresLayout.BarX/BarW`).
  The old bug: the jammer row ignored `Selected`, and rows were in name order.
- CARGO is a drop computer: heading-up drop plot (reach ring, zone, steer word, rim pointer past the
  scale), jump light (`CargoManifestRules.Light`: RED holds, AMBER no zone, GREEN clear) with the next
  action under it (`Hint`, or seconds until in reach), the stick drawn, the release checklist
  (`LoadCheck/SafetyCheck/ZoneCheck/ReachCheck`) and every load aboard (`CargoScope.Loads`). Offline
  fixture: `HudLogUnityCheck.Scope.Cargo.cs` (-Pages cargo).
Board Parts has five major sections: stores/power, tactical pages, lamps and gauges (the `status`
slug), notices, and map/feed restyle (NAV DATA removed 2026-10-21; the mission clock sits under WIND on SYSTEMS). Sway is a display option. Stores off must restore
native panels. Legacy automatic-page whitelist/dwell config entries no longer control selection.
Wingview and native camera ownership remain unchanged.

## HUD zones (2026-11-16, branch feat/hud-zones; spec `docs/superpowers/specs/2026-11-16-hud-zones-design.md`)

`Core/Ui/HudZones.cs` (pure, linked into the test project) is the one zone map every layout family reads: `HudZones.Ref(zone, refW)`
gives a zone's x/y/w/h in 1080-high reference pixels (scale by canvasH / 1080). Left zones keep their x, right zones their distance to
the right edge, centre zones their distance to the centre line. Zones only place this module's own objects; nothing native moves.

| Zone | x | y | w | h | Holds |
|---|---|---|---|---|---|
| LOG (left) | 12 | 12 | 562 | 101 | DL EVT LOG, 4 rows, hangs from the game's feed box |
| COMMS | 12 | 129 | 562 | 154 | radio lines (2 rows), YES/NO prompt inside it |
| CUE | 12 | 299 | 281 | 96 | cue stack, 3 shown |
| NOTICE | 12 | 411 | 562 | 16 | one line (`ShowTicker` switches it); hidden when a route face is up and there is no room above it |
| ROUTE | 12 | 443 | 281 | 133 | DOCK, else CARGO, else OBJECTIVE (compact faces, `StatusPanel.Route.cs`, `ScopePick.Route`) |
| TARGET CAM | 12 | 592 | 281 | 200 | locked-target picture, above the minimised map |
| TAC MAP | 12 | 808 | 281 | 260 | the game's minimised map (dressed, not moved) |
| LANE (centre) | 804 | 8 | 312 | 180 | `TopCentre` slots A, B, C: strip, card, warning banner |
| CALLOUT | 888 | 594 | 140 | 38 | HIT / DESTROYED / MISSED / DECOYED |
| BOARD TOP (right) | 1132 | 6 | 383 | 134 | stores, power, expendables, left of the spine |
| BOARD SPINE | 1531 | 6 | 381 | 624 | lamps, SYSTEMS, at most two page slots in ONE column |
| FEEDS | 1632 | 714 | 264 | 342 | missile card; stands on the UAV card or on the floor |
| CENTRE (keep clear) | 576 | 324 | 768 | 432 | only the game's flight symbology, the lane's banner, the callout, the threat ring, world markers |

Narrow screens (ref width under 1920): LOG, COMMS and NOTICE shrink to `min(562, laneLeft - gutter - margin)`, never under 281; the board uses Stack.

- **Auto scope** (`BoardScopeMode { Manual = 0, Off = 1, Auto = 2 }`, Auto the default, one-time move from Manual for untouched installs):
  slot 0 by `ScopePick.Top` -- THREAT while a missile is inbound, else APPROACH, SHOT, STRIKE, else THREAT; a change waits out a 1.5 s
  minimum show, an inbound missile skips it. Slot 1 is WING only while the wing has members. Manual keeps `Page1` / `Page2`; `PageCount` is 1..2;
  `Page3`, `Page4` and the Wide/Stack pick are still stored but ignored. Off shows only lamps and SYSTEMS.
- **ROUTE slot**: three compact faces drawn from the pages' own feeds (the full DOCK/CARGO/OBJECTIVE pages are polled silently even with the board off,
  so the slot works in Auto, Manual and board-off alike). **NOTICE** is the board's ticker moved to the left column.
- **TARGET card**: moved from the bottom right to the TARGET CAM zone. It is still filled only in external views (`HudBoard`, the block around
  `viewingOwnExternally`); the cockpit zone stays empty. Spike 2026-11-16: filling it in the cockpit works, the native TargetCam camera and render
  texture are live while a target is locked and the picture is live (grainy, the same as the cockpit tac screen); it needs `ThirdPersonHudCluster.Present`
  to show its root without a missile feed and to stop nulling the texture when the flight boxes are off. Not enabled.
- **Wing strip** hides while the board paints the WING page (`IHudBoard.ShowsWing`).
- Sims: `tests/ingame/hud-zones.json` (one cockpit flight, a capture per state), `hud-zones-dock.json` (SKYWELL pit approach for the DOCK face),
  `tac-board.json` (looks). The TAC BOARD sections below that describe up to four pages and a 2 x 2 grid are history.

## Client-local

No network messages or host-synced keys; every HUD setting belongs to the local pilot.

## AR helmet layer (2026-11-14, branch feat/ar-hud-xr in dev/bs-arhud)

Not on main or on feat/hud-zones: there is no `Presentation/ArLayer.cs` or `Domain/ArRules.cs` here (they live on that branch only).
When it merges it draws inside the CENTRE keep-clear box and needs no zone.

Spec: `docs/superpowers/specs/2026-11-14-ar-helmet-layer-design.md` (gitignored, in the worktree). `Presentation/ArLayer.cs` +
pure `Domain/ArRules.cs`, owned and ticked by `HudBoard` exactly like `CombatAlerts` (10 Hz `Read`, per-frame `Frame`, same gate:
live aircraft + `CockpitOverlayGate.NotSuppressed`). Own objects under HUDCanvas on a NESTED Canvas (per-frame redraw never
rebuilds the rest of the HUD), first sibling (under the native symbology). Projection on the CPU (`projection * worldToCamera`,
near-plane cut in clip space, Liang-Barsky cut to the screen before drawing: a dashed line to a far target once filled the
1600-quad buffer). Every item is contextual (the skill-repo rule: nothing persistent in the centre):
- PATH: constant-turn prediction from the measured velocity turn rate, 16 beads x 0.5 s; shows on a pull >= 3 g, or when a
  linecast (`PhysicsLayers.StaticsMask`, sea = `Datum.LocalSeaY`) meets the ground within 8 s with the gear up: alert colour,
  X + ground ring, "TERRAIN nS" at NamePt.
- DOMES: `IThreatPicture` rings (own faction, 1 Hz), SAM kinds only, nearest 4 within 1.35 x reach. Outside: a cage round
  YOUR height (floor ring where full reach starts, cap 1.5 km above you, struts, cone to the site; struts facing you skipped)
  and a FENCE on the near 100 degrees of the ring at your height (bold line, rails +-250 m, posts) labelled "SAM EDGE n".
  Inside: an EXIT gate on the edge straight out from the site + "SAM EXIT n" (a ring at eye level is edge-on and reads as a
  horizon line: in game the full rings/struts were noise).
- MISSILES: own missiles (private `Missile.target`, same FieldRef as ShotBook) dashed to target, bracket, "IMPACT nS" RIGHT of
  the bracket (the game writes name above, range below), once per target; inbound: rotating red diamond + 2 s velocity line + "Mn".
- BEACONS: HOME pillar over `FuelWatch.Field` on JOKER/BINGO/lasting damage; OBJ pillar over a live contract zone > 2 km.
- HUD REACTIONS: lock spin-in (bracket 3x->1x, 45->0 deg, 0.18 s, gentle back ease, ends just outside the native target box);
  hit glitch (new lasting damage, 0.28 s, 4 scan bars, 5 px jitter of the layer AND the TAC BOARD via `ApplySway`, >= 0.6 s apart).
  REDUCED MOTION: no spin, no jitter, no breathing; one still band.
Settings `[TacBoard]` ArPath / ArDomes / ArMissiles / ArBeacons / HudFx (ITacBoardSettings), SET COCKPIT "AR HELMET LAYER",
C menu VIEW > AR OVERLAYS (also holds COMBAT ALERTS; the lone view/alerts row is gone).
Sim: kit `tests/ingame/ar-layer.json` (11 checks, three seats) through `HudAutomation.Ar` (pull, home, glitch, select, dive,
and read-backs incl. rings_known). The sim's `fly` op hands the aircraft to an AI pilot and the game drops the HUD: never read
the layer under `fly`. A spawned SAM enters the faction picture only once tracked (seat within ~4 km). Rules self-check:
scratchpad `archeck` (links ArRules + HudDamage + HudLamps + IThreatPicture).
