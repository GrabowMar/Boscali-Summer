# Quick Design Spec: Support effects and map areas

Type: Addition to existing Support presentation and kinetic impact rules
Date: 2026-09-12
Source: user request and two visual references; current Support code is the specification baseline (no GDD or systems index exists).
Method: Game Studios quick-design structure, Ponytail reuse of Unity particles and existing map overlay, UI/UX Pro Max status labels alongside color.

## Design delta

- Rod: visible penetrator, hot nose and trailing plume. Impact has a short white flash, expanding fireball, condensation front, low ground dust, vertical soil column and ballistic glowing debris. The 150 m high-damage core falls off to zero at 420 m. No nuclear warhead or radioactive aftermath.
- EMP: blue-white central discharge, turbulent concentric particle fronts and branching lightning, following the supplied reference. Host-owned jamming lasts 18 seconds after a three-second delay. Client effects affect only the local cockpit presentation.
- Flare: warm particle ignition bloom complements existing native flares.
- All five support abilities retain distinct map icons and labeled areas. Rod additionally shows its core. Fortification marks the owned base, not an invented damage circle. Countdown wording indicates an estimate, never a confirmed collision.
- Host acknowledgement supplies active-marker position, radius and duration. Local armed previews use local settings. Support protocol changes to 3; peers need the same build.

## Budgets and lifecycle

- At most four rod descent visuals, four impacts and four EMP roots. Two outstanding rod jobs, 30-second expiry.
- Rod impact: at most 720 particles in six layers. EMP: at most 768 particles plus twelve 25-point lightning lines refreshed at 12.5 Hz. Shared cached materials and a 128-square procedural texture.
- Damage uses one 512-collider nonallocating query, one hit per damageable and impulse per rigidbody; server only, including headless. Clients do not simulate blast damage.
- Scene reset clears active visual roots. Missile destruction without a detonation does not produce a phantom impact.

## Acceptance and evidence

- Automated checks: core and blast-edge behavior, monotonic falloff, finite/bounded EMP metadata; full solution build, pure suite, installed-game compatibility probe, Harmony verification, diff check.
- Unity 2022.3 preview: render the actual particle setup and inspect texture edges, alpha blending, silhouette and distinct palettes. Standalone preview does not establish final in-game URP lighting, sound, map clipping or multiplayer behavior.
- Remaining playtest: single player, listen host, headless server with remote client, late join, origin shifts and scene reload. Observe impacts at ground and flight altitude; confirm no damage beyond 420 m and compare small nuclear weapons in the installed game.
