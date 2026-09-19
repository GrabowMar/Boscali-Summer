# Event art brief — Boscali Summer Events

Optional poster art for the graded event director. The mod ships no images; if you draw
nothing, every event still renders its vector category glyph.

## How it works

- Drop PNGs into `BepInEx/plugins/BoscaliSummer/Events/` (that is `Paths.PluginPath` +
  `BoscaliSummer/Events`; the folder is created on first load).
- File name is the event id: `<id>.png`, lowercase, exactly as listed below.
- Lookup order: `<id>.png` → tier fallback → `default.png` → vector glyph. The EVN MFD
  panel asks for `tier_super.png` when the event is a superevent and `tier_medium.png`
  otherwise (so minor events fall back to `tier_medium.png`); the full-screen alert always
  asks for `tier_super.png`.
- Constraints: PNG only, at most 1024 px on either side, at most 2 MB. The PNG signature
  and IHDR header are checked before decode; anything oversized or malformed is silently
  ignored and the fallback is used.
- Loaded art is cached until scene reset: new or replaced files appear after a mission
  restart or scene reload, never mid-mission.

## Style

One consistent look across the whole set: gritty war-room and archival newsreel, a duotone
print-poster feel — desaturated, heavy blacks, one restrained accent colour per poster.
No text, lettering, numerals, watermarks or logos anywhere in the image; the mod draws all
typography on top. Keep the image dark and low-contrast enough that white UI text stays
readable over it. Avoid hard white highlights and busy high-frequency detail in the center.

Sizes, all 16:9: superevents 1024x576, medium 768x432, minor 512x288. Shared fallbacks
follow the same look: `tier_super.png` 1024x576, `tier_medium.png` and `default.png`
768x432.

Two placements:

- EVN MFD panel, active card: the poster is a 16:9 plate, 148x84 at reference, with the
  title, target and clock beside it. Treat this as a small thumbnail — one clear subject,
  no fine detail, nothing important in the outer 10% (the edges are cropped by the frame).
  The same image is reused at 40x22 in every history row, where only the silhouette reads.
  When no PNG exists the plate is a generated stripe pattern over the category mark, so the
  layout looks deliberate with or without art.
- Full-screen superevent alert (superevents only): 1920x1080 reference, drawn full-bleed
  with aspect preserved under a dark backdrop and a heavy shade over the lower half. Keep
  the essential subject and all interest inside the central ~60% and the upper two-thirds,
  because edges are darkened and cropped on non-16:9 displays.

## Event posters

| Filename | Event | Tier | Category | Target | Image brief |
|---|---|---|---|---|---|
| `ceasefire_rumors.png` | Ceasefire Rumors | Minor | Political | ALL THEATER | A cramped command radio room at night, headphone operators leaning toward one lit receiver, papers half-read on the table; muted olive and amber, shared tension without a visible enemy. |
| `homefront_rally.png` | Homefront Rally | Minor | Political | ALL THEATER | A night rally in a capital square seen from above, a tight-packed crowd under searchlights and torches, banners reduced to plain cloth shapes; warm amber against cold blue, buoyant and impersonal. |
| `monsoon_season.png` | Monsoon Season | Minor | Hazard | ALL THEATER | A rain-swept airfield apron beneath a low monsoon wall, parked aircraft glistening, water sheeting off hangar roofs; slate blue and wet concrete, visibility already going soft. |
| `war_bond_drive.png` | War Bond Drive | Minor | Political | ALL THEATER | A bond-drive collection desk in a marble bank hall, a quiet queue and stacked war-loan ledgers under a brass scale; soft tungsten warmth, orderly and quietly hopeful. |
| `global_supply_chain_crisis.png` | Global Supply Chain Crisis | Medium | Economic | ALL THEATER | A jammed container port at dusk, cranes frozen over stacked containers while a column of lorries stretches to the horizon on wet tarmac; rust red, sodium orange and steel grey, scale overwhelming. |
| `diplomatic_sanctions.png` | Diplomatic Sanctions | Medium | Political | ALL THEATER | An empty diplomatic table in a cold conference hall, microphones and unread folders abandoned, hard light through tall windows; desaturated teal and bone, a decision already made. |
| `insurance_premium_hike.png` | Insurance Premium Hike | Medium | Economic | ALL THEATER | A cramped underwriting office lit by green bankers' lamps, endless ledgers and an adding machine mid-tally in cigarette haze; low amber pools inside deep shadow. |
| `fuel_depot_fire.png` | Fuel Depot Fire | Medium | Hazard | ALL THEATER | A burning fuel depot at dusk seen from the air, black smoke column, scattered firefighting lights, muted ochre and charcoal palette. |
| `rail_embargo.png` | Rail Embargo | Medium | Economic | ALL THEATER | A freight rail yard at grey dawn, loaded flatbeds standing idle behind a hand-cranked barrier, coats hung on a buffer; cold steel and damp gravel, everything stalled. |
| `volunteer_logistics_corps.png` | Volunteer Logistics Corps | Medium | Economic | ALL THEATER | A country depot at first light, civilian trucks and farm lorries queued at a loading ramp while volunteers pass crates hand to hand; warm dust and chrome, busy and improvised. |
| `veteran_contractor_influx.png` | Veteran Contractor Influx | Medium | Economic | ALL THEATER | Weathered aircrews signing papers at a folding table inside a hangar, flight jackets and a blank maintenance board behind them; hangar shade and warm work lights, routine competence. |
| `black_market_surplus.png` | Black Market Surplus | Medium | Economic | ALL THEATER | A fenced rear depot at night, unmarked crates under tarpaulins, hooded lamps and a hand cart mid-transfer; deep shadow with a single amber light, knowingly illicit. |
| `strategic_reserve_release.png` | Strategic Reserve Release | Medium | Economic | ALL THEATER | Depot doors rolling open onto a vast concrete bunker of sealed stores as forklifts advance into the dark; cold white shafts and long pallet shadows, official and immense. |
| `salvage_boom.png` | Salvage Boom | Medium | Economic | ALL THEATER | A wreck field at dawn being picked over, cut airframe sections laid in rows, a recovery crane lifting a wing spar past smoking flares; ash grey and torn metal under a low orange sun. |
| `allied_intervention.png` | Allied Intervention | Super | Political | HARD-PRESSED SIDE | A coalition supply convoy crossing a frontier bridge at dusk, unfamiliar pennants reduced to cloth shapes, an officer's map case open on a staff car; cold blue evening streaked with headlamps. |
| `emergency_appropriation.png` | Emergency Appropriation | Super | Economic | HARD-PRESSED SIDE | Reserve vault doors thrown wide, clerks hauling crates down torch-lit stairs under a silhouetted war cabinet; burnt gold and deep shadow, urgency from the top down. |
| `frontline_overstretch.png` | Frontline Overstretch | Super | Economic | LEADING SIDE | An overextended supply column strung along a bombed highway at dusk, fuel bowsers half-empty, depot fires on the far horizon; dust ochre and exhaustion, glory outrun by logistics. |
| `munitions_crisis.png` | Munitions Crisis | Super | Hazard | ALL THEATER | A cavernous arsenal hall with empty racking and acres of bare pallets, one overhead lamp swinging over the last sealed crate; near-monochrome and cold, scarcity made architectural. |
| `ceasefire_ultimatum.png` | Ceasefire Ultimatum | Super | Political | ALL THEATER | A frozen moment over a smoke-hazed frontline as one white flare hangs above an empty no-man's land, guns paused in silhouette; cold grey-blue, the whole theater holding its breath. |

## Shared fallbacks

| Filename | Size | Use | Image brief |
|---|---|---|---|
| `tier_super.png` | 1024x576 | Backdrop for any superevent without its own art, and the full-screen alert | The most important image in the set: a dramatic, mostly-dark, center-composed poster frame that reads at full screen — a lone silhouette or structure in the middle 60%, black edges, minimal accent light. |
| `tier_medium.png` | 768x432 | Fallback for any minor or medium event without its own art | A quieter command-post frame: map table or operations room in deep shadow, one warm lamp, low contrast, calm and reportorial. |
| `default.png` | 768x432 | Last resort for any event, and the neutral backstop | A neutral calm-theater frame: empty runway or depot at flat dawn light, no event-specific subject, dark enough for white text over it. |

## Effort

Spend the time on the five superevents. They own the full-screen alert, so they are the
only posters most players will see at size; medium and minor art is panel furniture. The
two ALL THEATER supers, `munitions_crisis.png` and `ceasefire_ultimatum.png`, must not
depict a specific side: no national markings, no recognizable airframes or uniforms
belonging to one faction.
