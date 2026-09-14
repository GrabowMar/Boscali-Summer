# Quick Design Spec: EMP nuclear-pulse alignment

Type: Revision of Support EMP presentation and burst geometry
Date: 2026-09-13
Source: user request with the Wikipedia *Nuclear electromagnetic pulse* article (E1/E2/E3 components, 20–40 km gamma deposition, delayed sound) and the earlier support-effects reference; current Support code is the specification baseline.
Method: Game Studios quick-design structure; Ponytail reuse of the existing line/particle rig plus one optional `Layer` fade parameter; UI/UX Pro Max status wording alongside colour (the 20–40 km band and the 30 s duration are stated text and pinned by tests).

## Design delta

- The airburst moves from 6.75 km to the 30 km gamma-deposition band (`SupportEffectPolicy.EmpBurstAltitude`), where prompt gammas ionize the stratosphere. The delivery missile remains the visual.
- E1 (prompt, ≤0.4 s): the footprint ring snaps to the configured radius in 0.12 s instead of a 5.5 s supersonic front, because the pulse is electromagnetic; a compact HDR prompt flash and receiver crack carry the electronics upset.
- E2 (intermediate, 0.4–3.2 s): branching lightning cracks through the established footprint instead of a radial wall.
- E3 (late, 3.2–30 s): two slow geomagnetic heave rings and an auroral glow pulse for the full host-owned 30 s `Unit.Jam` window; the ring ripple is biased toward the geomagnetic equator to read the asymmetric HEMP footprint.
- Audio splits into a prompt avionics snap/crackle and a sub-bass atmospheric thunder scheduled after listener distance (flash-to-bang), replacing the single 5.5 s blast.
- Cockpit disruption reads the same phases: a hard E1 screen wash and avionics upset, E2 crackle and sparks, then a residual E3 glow, keeping the existing severity falloff and 4–10 s local duration.
- Gameplay is unchanged: same radius, host-only jamming, 30 s duration, friendly-fire exposure.

## Budgets and lifecycle

- At most four EMP roots, cleared on scene reset. Four particle layers stay within 768 particles (48 prompt, 160 scattered-gamma, 320 footprint, 240 heave).
- Lines: twelve 25-point arcs refreshed at 12.5 Hz plus three 72-point loop rings (one prompt footprint, two E3 heaves). Both audio clips are synthesized once and cached; no assets.
- Positions reuse fixed arrays; no per-frame mesh, material or array allocation.

## Acceptance and evidence

- Automated checks: the pure suite pins the 20–40 km burst band alongside the existing 30 s duration and bounded metadata; Release build, pure suite, installed-game patch probe, module-boundary tests, Harmony verification and diff check pass.
- Unity preview: confirm the flash reads as a high-altitude star, the footprint snap lands inside the first frames, the E3 tint stays distinct from E1/E2, and the delayed thunder lands after the flash.
- Remaining playtest: single player, listen host, headless server with remote client, late join, origin shifts and scene reload; observe from ground and flight altitude.
