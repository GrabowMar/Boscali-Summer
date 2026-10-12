# Changelog

## Unreleased

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
