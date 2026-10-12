# Changelog

## Unreleased

- **Display grade (off by default)**: SET > CLIENT > PERF gains COLOUR GRADE and SHARPEN. Contrast,
  saturation, white balance and sharpening strength are in the F1 advanced view under Effects, and
  any `.cube` LUT dropped into `BepInEx/config/BoscaliSummer/luts` can be named there as the look.
- **Squadron insignia, sharper and meaner**: badges are cut, not rounded (angular shield, bullet arch,
  chamfered patch, heavier keylines), with three new shapes (ARROWHEAD, SAWBLADE, COFFIN). Every mark
  is redrawn as patch art with cut-in detail. Fifteen new marks: EAGLE, WOLF, BULL, AXES, JOLLY ROGER, SHARK, BOMB; the jokes
  COOL S, LOSS, SHADES, DUCK, MUSHROOM, STONKS; and two pieces of nose art, PINUP and BOMBSHELL. Enemy
  ace wings never roll the jokes or the nose art. Existing insignia keep their layers.
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
- **Airborne** (needs Blueprinter): HALO paratroopers, Ibis fast-rope and rappel insertions.
- **OPS fronts**: SPACE / CYBER / SOF fronts with directives, programmes and readiness.
- **Squadron insignia**: layered crest editor, tail-fin markings for supported aircraft, and
  cosmetic crest sharing between players.
- **Weather and cockpit immersion**: 47 sky profiles, revised volumetric clouds and canopy
  rain, pilot body and breathing cues driven by native G-LOC, recorded pilot/effect audio,
  and a kneeboard picture-card gallery.
- Reworked OPS/STR/COMMS consoles, command map, world-event art, destruction, and wildfire effects.
- Private spectator cinematography tools for camera paths and shot editing; no video encoding.
- Player build: developer cheats and tools are locked off and hidden from the F1 window.
- Clean Release builds after integration; unused fields removed and isolated-test telemetry
  warnings separated from production diagnostics.
