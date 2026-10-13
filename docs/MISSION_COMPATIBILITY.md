# Dedicated Boscali Summer mission

The native mission picker receives a **Boscali Summer** group with one mission named **Boscali Summer**. The mod copies the installed game's built-in **Escalation** on demand, changes its description, and reuses its preview. Native objectives, outcomes, units, factions and settings come from that source. No loose mission payload is shipped, and no user or workshop mission file is overwritten. An older user mission with the same display name remains independent; use the new group.

The copy carries a fixed identifying sentence at the end of its serialized description: "Full Boscali Summer experience enabled for this dedicated Escalation scenario." Mission names do not enable the overhaul. An unmarked mission, including vanilla Escalation and a workshop mission named Boscali Summer, uses compatibility mode. The identifier survives the native network mission serialization, where a joining client does not retain the host's local mission key.

## Feature scope

Existing configuration switches still apply in the dedicated mission. Compatibility cannot be bypassed by enabling a module in the config.

| Systems | Vanilla / workshop | Dedicated mission |
| --- | --- | --- |
| Session handshake, HUD, general avionics/MFD and map presentation, radio, chatter, comms | Available | Available |
| Player-controlled Wing, local autopilot and interaction menu, QoL | Available | Available |
| Immersion, display effects, performance monitor, cinematography | Available | Available |
| STR workspace and strategic map overlays | Disabled | Available |
| Living Front staff, ground/naval AI direction, mission AI air stations | Disabled | Available |
| Support / OPS, orbital/cyber/SOF operations and their effects | Disabled | Available |
| Dynamic objectives/contracts, high command, intel seeding, progression, enemy ace hunts | Disabled | Available |
| Garrison spawning, modified capture, airborne troops, trenches | Disabled | Available |
| Fire spread, destruction changes, persistent wrecks, dynamic weather / cloud seeker effects | Disabled | Available |
| Player hangar spawn priority and automatic AI eviction | Disabled | Available |
| VANGUARD runtime and Boscali custom content | Disabled | Available with Blueprinter |

## Lifecycle and ownership

The core starts in compatibility mode. Native mission selection and multiplayer mission synchronization resolve the profile before mission spawning. A scene-load hook also handles native mission restarts. Stopping/leaving a mission revokes the full profile.

General Command remains installed without Progression. A separate strategic-command module owns STR, its view hub, resource recorder and overlay. Full mission modules use the existing dependency graph, with already-installed general modules satisfying dependencies. Every native mission preparation resets the scene services before the new environment is applied, including restarts or full-to-full transitions on the same map that do not emit Unity's scene-loaded event.

When the profile changes back to compatibility, the host removes full-module Harmony patches, withdraws their services/settings/HUD feeds, disables their runtime components, stops their coroutines, resets scene state and destroys those components synchronously. General scene services reset so cached references and settings panels cannot carry old strategic state into another mission. Native spawning, simulation and multiplayer authority remain with the game and the existing host-side feature code.

## Verification boundary

Pure regression tests cover marker matching, default-deny module/content scope, transition notifications and dependencies on already-installed modules. The Release build and patch probe check the compiled native seams and patch ownership. Offline checks do not establish native picker input, live mission transitions, saved-loadout handling or multiplayer gameplay; those require an in-game run with matching mod versions.

Verified on 2026-10-13:

- Release candidate build: zero warnings, zero errors.
- Pure runner: 307 suites passed, zero failed, 4,880,364 assertions. Final identity/graph check: two suites passed, 97 assertions.
- Final candidate patch probe: 158 registered patch classes and 30 module descriptors; native lifecycle signatures, parameter bindings, module scope and dependency closure passed.
- Actual compiled mission factory, using the installed Escalation JSON: every original JSON value preserved after restoring only the description; malformed input rejected; unknown future fields preserved.
- Candidate: `.nomodkit/mission-profile/candidate/BoscaliSummer.dll`, SHA256 `084426b114131abea741a4433e6e55e84513af929767fd4fd4e9cc6d1bacdc4d`.

The candidate was built offline. No game installation, user mission, deployment or live multiplayer session was changed for validation.
