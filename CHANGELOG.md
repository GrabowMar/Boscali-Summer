# Changelog

## 0.2.4 — update

- **Classic corner map by default**: fresh configs use the game's own corner map, with the holo map
  and terrain ghost off; switching the holo map off now sticks. Opted-in holo maps draw visible air
  contacts ahead of ground clutter.
- **No more flicker with the expanded layout off**: the stock layout no longer redraws its panels
  every frame.
- **Infantry emplacements**: squad encampments are placed on clear ground and no longer spawn inside
  a helicopter.
- **Radar trucks join EW**: new friendly radar trucks are enrolled in the electronic-warfare network,
  so the truck count can grow.
- **Free Flight stays clear**: the native Free Flight mission gets no trenches and no occupied
  buildings.
- **EVENT PICTURES switch**: SET > CLIENT > PERFORMANCE > WORLD can turn event pictures off and show
  generic symbols instead.
- **OPS TASKED tab**: claim posted strikes and steer the cruise missile; the CRUISE perk now flies
  the game's own cruise missile, so steering works.
- **Distant clouds** no longer boil.

## 0.2.3 — update

- **Map selection reaches the target list**: while you fly, contacts picked on the expanded map (click, box,
  right-click menu) are now real weapon targets, several at a time; before, they only looked selected.
  A new tool row on the map corner sets what a left drag does (PAN / BOX), what a click does (ADDS / ONE),
  which side a box takes (ALL / HOSTILE / OWN) and clears the selection. New `Command` settings:
  `MapLeftDragBox`, `MapClickAdds`, `MapBoxSide`, `MapRightDragTurns`, `MapDoubleClickZoom`,
  `MapTurnSensitivity`, `MapZoomStep`.
- **Relief map**: contacts stand on the terrain as 3D miniatures again (`MapMinis3D`, on by default), now
  one mesh and one draw call that is rewritten only when something actually changed. Holding the right
  mouse button and dragging turns the view again, next to the held wheel, Alt + drag and Ctrl + wheel.
  The red and blue rings that formed on the front line are gone: an unheld square closed in by held
  ground is now tinted through instead of being left as a hole with both border lines bent round it.
- **The C menu stays open after you pick something**: every action now leaves the menu up (the row flashes and the result shows
  under the hub), so several things can be set in one visit. Close it with the open key, Esc, or a click away from it. The ring's
  icons and the hub are also much easier to hit: the cursor counts as on one from about 30 px outside its rim.
- **Chaff is a countermeasure like the others** (issue 10): `Next Countermeasure` now steps FLARES, JAMMER, CHAFF, and while chaff
  is selected the `Countermeasures` button dispenses it. The selection shows on the TAC BOARD, the game's own readout and the
  C menu. Single player and hosting, like the dispenser itself.
- **The C menu no longer takes the game's radial wheel** (issue 11): the `Radial Menu` binding opens the game's own wheel again, so
  mods that add to it work; the C menu opens on its own key (`Autopilot.AceRadialKey`, default C). Set the new
  `Autopilot.InteractionMenuOnRadialBinding` to on to open it from a HOTAS or gamepad button as before.
- **The game's radial wheel is stock again**: the `BOSCALI` and `Wing Command` slices are gone from it. Autopilot landing, the
  target presets and the wing orders are in the C menu (FLIGHT, TARGETS, WING).
- **Objective marker redrawn** (issue 13): the contract marker in the cockpit drew a black square behind its area ring. It is now
  drawn in the visor's own line work: corner brackets on the point, a chevron at the screen edge when the target is out of view,
  a dashed area ring, and a shelf with the title and the distance that fills as the hold progresses.
- **Map selection** (issue 14): one left click on the big map selected two contacts, or selected one and dropped it again,
  because the game handed the click to the icons twice. One click is one click now.
- **Visor terrain follows the map**: the terrain ghost and the holo map could keep the relief of the map flown before (Heartland
  hills over the Archipelago). They now check the loaded map every frame and fetch its own relief. The ghost's lines are about a
  quarter fainter and its twice-a-second repaint costs about half.
- **Boscali Summer, the mission:** the **Boscali Summer** entry in the mission picker is now a scripted mission on top of the game's own Escalation. A cutscene tells the build-up in a storm at sunset (the night of the coup, the fall of Agrapol, the fleet coming home), with armoured columns already rolling, place-and-date stamps and the mod's event paintings landing as photographs along a timeline, framed as a surveillance feed and set to its own music. You pick a side as a contract pilot, your handler and your side's command talk you through an opening (fly to the picket station, break the raid on your strip), and then the war opens into the full Escalation sandbox with the story following its milestones to the end. Existing user missions are preserved; no mission file is shipped.
- **Mission scripts:** the mission above is one text file. Your own go in `BepInEx/config/BoscaliSummer/scenarios/`: objectives and spawns written into a copy of any mission, named characters with dialogue, cutscene camera shots with captions, stamps and pinned photographs, hints, and campaigns whose chapters unlock one after another. A dedicated server starts one with `-mission scenario:<id>`. See [docs/SCENARIOS.md](docs/SCENARIOS.md). New F1 switch: **Cutscenes in scripted missions**.
- **Mission systems prompt:** every mission, vanilla and workshop included, runs the whole mod again; nothing is locked. When a mission starts, a prompt on the map screen lists the systems with **FULL EXPERIENCE** and **IMMERSION ONLY** presets (immersion only keeps the cockpit, HUD, wing, radio and comms and leaves the mission as its author made it). Start, keep the answer for that mission, or never be asked again; F1 > Mission brings the prompt back.

## 0.2.2 — update

- **Chaff**: your aircraft now carries a chaff dispenser (single player and hosting), built on the
  game's own unused chaff classes. It dazzles radar-guided missiles, most when you fly across the
  missile's line of sight and not at all head-on. Holding the countermeasure button dispenses it
  while a radar missile is inbound; DEFENCE has POP CHAFF and a switch for that link, and
  `Autopilot.ChaffKey` binds a key. The load follows the flare load (24 to 96 bundles), shows on
  the TAC BOARD's expendables and refills with a rearm.
- **C menu additions**: TARGETS has LOCK NEAREST THREAT (the nearest tracked hostile aircraft, alone)
  and TARGET INBOUND MSLS (every missile tracking you joins the selection); WEAPONS has OPEN /
  CLOSE BAY DOORS on airframes with a weapon bay.
- **C menu regrouped**: the ring is now WEAPONS, HUD, FLIGHT, WING, COMMS, COCKPIT, SUPPORT, VIEW,
  DEFENCE, TARGETS. HUD (key 2) holds every TAC BOARD switch: the board itself, declutter, layout,
  each scope page, each board part, shade, size and a held reset. The exterior lights are one FLIGHT
  row, night vision moved to VIEW next to the new holo map switch and ground ghost level, the radio
  became COCKPIT with the cabin effects and the kneeboard card, and DEFENCE gained GO DARK (radar
  silent and every light off in one press).
- **C menu handling**: the view no longer pans while a controller steers the menu; a controller
  enters a category by holding the stick on it for a second (a ring closes round the icon); letting
  go of the open key too early keeps the menu for a click instead of closing it; rows picked with
  the arrows or the mouse wheel run on release; switches inside a list stay up so several can be
  flipped in one visit; a click on the hub goes back and a click in the open closes.
- **Radio chatter rebuilt**: the radio is rebuilt from scratch as script packs, with AWACS, tower,
  enemy and command nets, voice packs, a comms block in the left column and the radio in the MFD
  log. Not yet played through in game.
- **Fused night vision**: a white-phosphor tube with metered, gating gain and a thermal glow on hot
  units; VIEW has SET NVG GAIN while night vision is on.
- **Holo map and terrain ghost**: the corner map is a standing mini hologram that the held map key
  grows into the big one (a tap still opens the MFD map), and a wire relief of the ground shows
  through the cockpit. NAV DATA and the DL EVT LOG event log join the visor.

## 0.2.1 — update


- **MFD index rail rebuilt**: the button column on the right of the map is a new panel in the
  same style as the MFD consoles. Its keys now follow the colour scheme chosen on SET > DISPLAY the
  moment it changes; before, the rail kept the colours it was first drawn with. Keys are grouped
  under SITUATION, TASKING, COMMS and SYSTEM, each screen has its own redrawn icon, and the open
  screen's key reads ON.

- **Relief map pass**: the STR layer now moves with the terrain instead of trailing it during a
  pan; the view redraws at up to 60 Hz while it moves (30 Hz when the frame rate is already low).
  Contact stacks are grouped on the ground, by faction, so panning or turning the map no longer
  reshuffles them, and a knot of units on a grid line reads as one count. Land now carries a light
  red/blue control tint like the sea. Ctrl + wheel turns the view and Shift + wheel tilts it, next
  to the middle-button drag. Reprojecting the contact roster costs far less per redraw.

- **Event responses removed**: the RESPONSE desk on the EVN screen (contain / leverage, treasury
  directive, contract intelligence, pilot channel) is gone. A world event now simply sets its
  support price and reset time until it ends. Host and clients must run the same build.
- **TAC BOARD HUD rework** (GitHub issue 8): a new helmet-visor look with no panel behind it,
  spread wide along the top edge of the screen; the classic column stays as the STACK layout.
  The selected weapon and the jammer are drawn with the game's own line-art pictures.
  New options for layout, size, position, opacity, shade, declutter level, every page
  and block, visor sway and the kill feed and map restyle, on SET > COCKPIT, the C menu (VIEW),
  F1 and the `[TacBoard]` config section, with Reset to defaults. The radar-warning page now
  holds a contact for a set time (6 s by default) and any page stays up a minimum time, so it no
  longer pops on and off. Names and readouts that were cut off or overlapped now fit, and the
  board scales down to 1280 x 720. Switching the board off keeps the kill feed and map restyle
  (it has its own switch), and the visor sway follows the pilot's head, not the aircraft's own turns.
- **Display grade (off by default)**: SET > CLIENT > PERF gains COLOUR GRADE and SHARPEN. Contrast,
  saturation, white balance and sharpening strength are in the F1 advanced view under Effects, and
  any `.cube` LUT dropped into `BepInEx/config/BoscaliSummer/luts` can be named there as the look.
- **Squadron insignia redrawn**: badges are cut, not rounded (angular shield, bullet arch, chamfered
  patch, heavier keylines), with three new shapes (ARROWHEAD, SAWBLADE, COFFIN). The centre marks are
  replaced by 48 new four-colour drawings: pin-ups, predators, cards and dice, skulls, gremlins and
  jokes. The mark's skin tone follows INK and its red follows SECOND. An insignia saved before this
  keeps its shape and colours but shows the new mark with the same number. Enemy ace wings fly only
  the predators, weapons and omens.
- **Tail markings sit where they should**: the multirole wears its insignia on its fins (it was on
  the boom underneath), the fast bomber's is no longer cut by the fin's leading edge, and the ground
  attack jet's and trainer's stay clear of the formation-light strips. Markings are painted at twice
  the resolution.
- **Gun pipper fixed** ([#9](https://github.com/GrabowMar/Boscali-Summer/issues/9)): with a gun
  selected and a target designated, the lead pipper and everything the HUD drew after it went
  missing. The tweak that switched off the game's aim-assist stick correction caused it and is
  removed; the game's own aim assist is back.
- **Occupied city buildings can be levelled again**: under URBAN SIEGE a garrisoned building
  ignored every hit. Bombs now wear it down as intended (3-8 bomb units by roof size), and hits on
  its rooftop nest count against the building.
- **C-key menu is quicker and more forgiving**: release fires after 50 ms on a row (the corner
  brackets close when it will), a pointer that strays between rows or just past the list keeps
  the nearest row, and categories you cross on the way to the list no longer steal the menu. It
  also remembers the last action per category: a chevron marks it, the arrow keys land on it
  first, and Space at the ring jumps straight back to it.
- Pressing Map in the aircraft selection menu no longer leaves the map's glass overlay on screen.
- A patch that cannot start on the player's runtime now switches its module off at load, with one
  line in the log, instead of breaking the game feature it patches.
- Blueprinter now loads the mod's content (Vanguard weapons, HALO paratroopers, container feeder)
  as one pack, "Boscali Summer assets", instead of three. Host and clients need the same build.

## 0.2.0 — first public alpha

- **Wing Command is built in.** Remove the stand-alone Wing Command mod; AI wingmen, the WMC
  page and formations now ship inside Boscali Summer.
- **TAC BOARD HUD**: stores and countermeasure header, system cautions, and nine automatic
  pages for threats, shots, missile camera, approach, wing, strikes, refuelling, objectives,
  and aircraft systems. Includes flight and weapon gauges, fuel endurance, and a notice ticker.
- **C-key interaction menu**: a ring with live action lists, keyboard navigation, formation
  selection, support perks, missile view, and SKYWELL pit-stop orders and station priority.
- **VANGUARD weapons** (needs Blueprinter): RG-12 LANCE railgun, AIM-X AEGIS defence pod,
  MALD-X BAIT and MALD-J SHADOW, REMORA escort drones, HAWC-X SKIP glider, GLAIVE-2A SENTINEL
  and GLAIVE-2S SPECTRE gun drones, ORCA glide torpedo, and ALE-X towed decoy.
- **SKYWELL service tender**: deploy a refuel/rearm kit from a compatible cargo hold. Receivers
  fly formation inside its service zone while the tender delivers fuel and rounds job by job;
  includes fuel/munitions orders, station priority, and threat-triggered breaks.
- **KS-01 KESTREL recon glider**: an unarmed cargo-hold-launched scout that orbits a designated
  ground target, reports hostile contacts, and provides laser designation.
- **CG-9 HAMILCAR cargo glider** (VANGUARD, needs Blueprinter): release it from a compatible
  cargo bay at a designated ground target. It flies fifteen metres for every metre of height
  (up to 45 km), lands on its belly
  about 250 m short of the target and a Hexhound GMG rolls off as a live unit. It needs flat dry
  ground; anything that hits it on the way in loses both.
- **Weather tape**: stories, front markers, contrast-scaled ramps, the multi-keyframe bake and TapeOpening.Solve; the legacy timeline, frontal shield, sky split and cloudShift are gone (held tape; motion is slice 5).
- **Weather console (Ctrl+O) is the MET OFFICE**: a synoptic chart of fronts, cells and storms, sky and
  METAR readouts, pin any of the 47 skies, hold and pace the weather, set-pieces and an anchor, rain
  override, six one-click scenarios and a key-driven command line. Clients see it read-only.
- **Airborne** (needs Blueprinter): HALO paratroopers, Ibis fast-rope and rappel insertions.
- **OPS fronts**: SPACE / CYBER / SOF fronts with directives, programmes and readiness.
- **Squadron insignia**: layered crest editor, tail-fin markings for supported aircraft, and
  cosmetic crest sharing between players.
- **Weather by role**: every player now gets native weather for their own seat. The host and a dedicated
  server set fog, wind, turbulence and cloud height from the theater; each client from their own camera and
  aircraft. The native sun no longer dies at a front (vanilla's grey-and-no-sun gate is never reached; the
  overcast look comes from the mod), a client's fog, wind and cloud height keep following their own camera
  and aircraft while the weather is held (the host's and a single player's are theater-level; light and rain
  are always local), and the cloud height follows the deck you are under. The MET OFFICE gets a working STORY stepper (AUTO and the five stories), and the
  ENV screen shows the story, the next front ahead and a forecast at your position (HELD while the tape is held).
- **Weather and cockpit immersion**: 47 sky profiles, revised volumetric clouds and canopy
  rain, pilot body and breathing cues driven by native G-LOC, recorded pilot/effect audio,
  and a kneeboard picture-card gallery.
- Reworked OPS/STR/COMMS consoles, command map, world-event art, destruction, and wildfire effects.
- Private spectator cinematography tools for camera paths and shot editing; no video encoding.
- Player build: developer cheats and tools are locked off and hidden from the F1 window.
- Clean Release builds after integration; unused fields removed and isolated-test telemetry
  warnings separated from production diagnostics.
