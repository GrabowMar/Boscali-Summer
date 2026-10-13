# Mission systems

Every mission runs the whole mod: vanilla, workshop and the **Boscali Summer** picker entry alike. Nothing is locked by mission type. The player decides per mission what runs.

The one exception is a scripted mission, such as the built-in **Boscali Summer**: its script says what runs (everything, unless the script switches a module off), so it is never prompted for. See [mission scripts](SCENARIOS.md).

## The prompt

When a mission starts, a window opens on the map screen listing every optional module in two groups:

| Group | Modules | Effect on the mission |
| --- | --- | --- |
| Cockpit and immersion | radio, chatter, comms, wing, autopilot and interaction menu, QoL, immersion, effects, avionics and tactical map, cinematography | None. The mission plays as its author made it. |
| Battlefield | STR, support / OPS, progression and squad, contracts, high command, intel, Living Front and AI air stations, garrisons and urban combat, trenches, events, weather, fire and destruction, spawn priority, VANGUARD | Changes units, objectives, economy or the world. |

- **FULL EXPERIENCE** switches everything on. **IMMERSION ONLY** switches the battlefield group off.
- Switching a module off also switches off the modules that need it; switching one on brings what it needs.
- **START** applies the selection, which is also what the next prompt opens with.
- **START / KEEP FOR THIS MISSION** remembers the selection for that mission name and stops asking there.
- **START / NEVER ASK AGAIN** keeps the selection for every mission and switches the prompt off.

Session handshake, HUD, the performance monitor and the prompt itself are always on. Modules switched off in F1 are not offered.

## Settings

```ini
[Mission]
AskAtStart = true
DisabledModules =
RememberedMissions = {}
```

`AskAtStart` is in the F1 window ("Ask which systems run at mission start"); turn it on to be asked again. Set `RememberedMissions` to `{}` to forget the per-mission answers.

## How it applies

A remembered or last-used selection is applied when the mission is selected, before anything spawns, so a mission remembered as immersion only never sees a battlefield module. A change made in the prompt applies at once: switched-off modules lose their Harmony patches, services and components; switched-on modules load into the running mission. A battlefield module switched on in the prompt therefore joins a mission already under way; restart the mission if it needs a clean start.

Embedded Boscali equipment (`VG_*`, the container feeder, the paratrooper mount) is filtered from stores and saved loadouts only while its owning module (VANGUARD, urban combat) is off.

The selection is local. In multiplayer the host's selection decides the battlefield systems, as host-side code owns them; each client chooses its own cockpit systems.

## Verification boundary

Offline: Release build, the pure `MissionProfileTests`, and the patch probe (no module locked by default, switches honoured, immersion-only dependency closure, mission copy JSON preserved). The prompt window, live module switching inside a running mission and multiplayer have not been run in game.
