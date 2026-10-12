# Changelog

## Unreleased

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
