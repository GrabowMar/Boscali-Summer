# Changelog

## 0.1.1 - Destruction aftermath

- Removed the experimental helicopter optical-smoke countermeasure, seeker patches, runtime cloud manager, icon asset, and public smoke settings; fire and ruin smoke are unaffected.
- Added synchronized mission-long ruin records with late-join position, footprint, and age snapshots.
- Added pooled footprint-shaped collapse dust and delayed ejected-dust bursts, capped globally with no Rigidbody debris or persistent colliders.
- Added hot-ruin and permanent intermittent smouldering phases using stripped vanilla Fuel Depot smoke.
- Added a 256-record logical ruin cap and nearest-24 visual budget with distance-scaled emissions.
- Fixed lightweight building damage by following the vanilla direct material-instance `_HitPoints`/`_Damage` path; scenery shaders without those controls receive a restrained, uneven soot fallback instead of silently showing no change.
- Registered both direct lightweight-building destruction and fire burnout with the ruin aftermath system.
- Added destruction performance settings for ruin records, active ruin smoke, collapse bursts, and hot-smoke duration.
- Consolidated nearby forest ignitions into synchronized scalable fire-front clusters instead of equal isolated fire sites.
- Broadened the three-core vanilla wildfire plume, increased wind shear and vertical development, and scaled it with merged-front intensity without adding smoke systems.
- Replaced circular single-pass forest scorching with three bounded overlapping vanilla blast-map stamps for a darker gray center and irregular ash edge.
- Reworked configuration into nine high-level controls; detailed effect and performance values are now derived and bounded, with legacy per-effect keys removed on upgrade.
- Increased default ignition rates to 0.25% per ordinary projectile impact and 6% per explosive impact, scaled by the single Fires/Intensity control.
- Added bounded server-side ground-vehicle destruction ignitions: one nearby civilian building query per loss, a lower derived chance than direct explosives, and a 32-event queue to prevent mass-loss physics spikes.
- Fixed forest propagation being visually swallowed by its parent merge radius; bounded child fronts now form an actual wind-biased fire line, with faster-growing flame beds and denser, broader forest-only Fuel Depot smoke.

## 0.1.0 - Phase 1

- Added helicopter smoke dispensers that break optical and INS/optical seeker sight lines.
- Added bounded, host-authoritative ignition for projectile impacts on procedural forests and buildings.
- Added pooled large fire effects, three-light global budget, persistent blast-map scorching, and tree removal.
- Added an intact/damaged/ruined visual lifecycle for lightweight map buildings.
- Added deterministic defensive garrisons to civilian buildings around airbases at mission start and capture.
- Added late-join snapshots for active fires and damaged lightweight buildings.
- Reworked smoke into a larger, staggered, wind-shaped curtain with smaller overlapping puffs.
- Replaced inherited explosion emitters with gradual low surface flames and broad drifting smoke.
- Fixed garrison discovery around mission-authored highway strips and added bounded late-load retries.
- Added capped, deterministic, wind-biased forest-fire propagation with progressive child ignition.
- Increased countermeasure screen density and added a guaranteed sustained smoke layer for fires.
- Kept rooftop gabions and sandbags visible so occupied civilian buildings are identifiable.
- Added a smaller vanilla-gray scorch radius and taller, denser ashen fire smoke.
- Added the custom smoke countermeasure icon and a spatial synthesized deployment thunk.
- Fixed delayed network-spawn setup for RAH/Black Hawk aircraft and made optical warning selection register the smoke station before the AI decision.
- Extended the vanilla helicopter combat-state countermeasure branch so optical warnings actually hold and deploy the smoke station (not just IR warnings).
- Switched fire plumes to the runtime-resolved vanilla `ContactSmoke` tall dark column with progressive, taller gray/ashen emission; building fires now use a narrow profile instead of the puffy tire-smoke effect.
- Replaced the tuned building column with a pooled smoke-only clone of the exact vanilla Fuel Depot destruction prefab; its fireball, flash, sparks, debris, audio, and damage logic are stripped while the original smoke rendering is retained.
- Broke up the artificial city-fire repetition with deterministic per-site plume scale, density, growth delay and pulsing; added world-space wind shear, compact roof-localized flames, reduced urban fire lighting, and immediate synchronized soot/damage treatment for burning lightweight buildings.
- Added synchronized burnout demolition for unoccupied map or networked civilian buildings; occupied buildings are preserved and finish as normal ruins only when later destroyed.
- Distributed each burning building's smoke across two or three smaller, staggered roof sources without increasing the overall particle budget; intermediate building damage now preserves intact geometry and facade materials with lighter uneven soot instead of prematurely borrowing the full ruin appearance.
- Replaced the intermediate building's flat color wash with its native `_HitPoints`/`_Damage` shader masks, anchored building flames to the actual collider roof under the impact point, and replaced the remaining forest `ContactSmoke` path with a wider three-source Fuel Depot smoke profile.
- Converted garrisons to invisible logic-only DEF proxies anchored to civilian buildings; no bunker geometry is left on rooftops, and networked shells inherit the owning HQ while occupied.
