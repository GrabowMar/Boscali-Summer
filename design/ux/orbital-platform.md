# Orbital platform — modular station, mission control LARP

Status: implemented; in-game visual and multiplayer acceptance pending. Date: 2026-09-16.
Owner: Support. Platform: Nuclear Option PC map MFD, maximised map, full-screen uplink, sky.
Design authority: the user's satellite redesign request ("too confusing, takes too much time,
panels look bland, steering fails; one customisable platform instead of payload satellites")
and their answers: module-by-module launches, grid utilities plus mass, altitude trade-off,
full-screen uplink, tabs PLATFORM / MISSION PLANNER / ENEMY ACTIVITY replacing SPACE's
sub-pages, more module designs than a station can carry.
Method: Game Studios quick-design structure; Ponytail reuse of the orbital geometry, SAR
former, EO camera, support request pipeline and launch streak; UI/UX Pro Max HUD/FUI style
with status in words beside colour; Superpowers verification before any claim.
Replaces the four-payload satellite fleet (`OrbitalFleet`, `Payloads`, `SatelliteWall`).

## Player fantasy

You run your faction's one orbital station. You design it cell by cell, launch it a module at
a time and watch it grow into a real object crossing your sky. When it is overhead you take
the uplink — a full-screen feed you fly with the mouse — and work the pass: radar-scan a
town, listen for SAM radars, drop a rod, pop an EMP. When it is on the far side of the planet
you plan the next module, reboost, or burn to come round sooner. Power, heat, mass and fuel
are the rules of the game; nothing is instant and nothing is everywhere.

## Core loop (short on purpose)

1. MISSION PLANNER › LAUNCH CORE (pick LOW / MID / HIGH). Insertion 45 s.
2. Pick a free cell next to the station, pick a module, LAUNCH (5 s count). Docks 20 s later.
3. PLATFORM shows when you are overhead (AOS/LOS clock and a pass bar) and which abilities
   are ready, and why not in words when they are not.
4. Work the pass from the uplink or the map; recharge, rephase or change orbit between passes.

## Station grid

5 × 3 truss, core in the centre cell (B3; rows A–C, columns 1–5). A module must dock orthogonally next to a docked
module; jettisoning one that would strand others is refused. One launch at a time. Mass
limit 40 t (core structure rating, core included).

Grid utilities (orthogonal neighbours):

- **RAD radiator** cools its neighbours. A hot module (EMP, RTG) without a cooling neighbour
  runs degraded: EMP recharge ×2, RTG output ×0.5.
- **REL relay** boosts neighbouring sensors (IMG, SIG): scan/sweep radius ×1.35.
- **SHD shield** protects itself and its neighbours from micrometeoroid strikes.

## Module catalogue (13 designs; a station carries about 8)

| Code | Module | Mass | Price | Power | Effect |
|---|---|---|---|---|---|
| COR | Core | 12 t | 800 | +4 kW sun, 600 kJ | Command, docking hub, required |
| SOL | Solar array | 1.5 t | 150 | +12 kW sun | — |
| BAT | Battery bank | 2.5 t | 200 | 1 500 kJ | — |
| RTG | Reactor | 6 t | 650 | +10 kW always | Hot |
| RAD | Radiator | 1.2 t | 120 | — | Cools neighbours |
| REL | Relay | 1.5 t | 250 | −1 kW | Boosts neighbouring sensors |
| CMG | Gyro cluster | 2 t | 300 | −0.5 kW | Uplink slews faster, rod scatter halved |
| SHD | Shield | 2 t | 180 | — | Protects itself and neighbours |
| PRP | Propulsion | 4 t | 400 | — | +100 fuel; rephase and orbit change |
| HAB | Habitat | 6 t | 550 | −1.5 kW | Crew of three: ability recharge ×0.75 |
| IMG | Spy imager | 3.5 t | 600 | −2 kW | UPLINK (EO/IR video) and RADAR SCAN |
| SIG | SIGINT array | 2.5 t | 450 | −1.5 kW | ELINT SWEEP |
| ROD | Rod magazine | 5.5 t | 700 | — | +3 rods; ROD STRIKE |
| EMP | EMP emitter | 4.5 t | 900 | −1 kW | EMP BURST; hot |

Copy limits keep designs honest (one HAB, RTG, SIG, CMG, EMP; two IMG, ROD, PRP, REL; three
BAT, RAD, SHD; four SOL). Launch price = module price + vehicle (LIGHT ≤ 2.5 t 100, MEDIUM
≤ 5 t 250, HEAVY ≤ 12.5 t 500), scaled by `PlatformCostScale`, `CostMultiplier`, events and
the Logistics perk. Jettison refunds `PlatformJettisonRefund` of what was paid; jettisoning the
core deorbits the station. CARGO resupply (250 + LIGHT) docks at the core and refills fuel and
rods.

## Power

kW × s = kJ. Generation is solar while sunlit (out of the theatre arc, or theatre daylight)
plus reactor output always; loads run continuously. At 0 kJ with a net drain the station
browns out: loads are shed and every ability is refused until 25 % charge returns.

## Orbit (altitude trade-off, real geometry)

| Band | Altitude | Pass | Gap (sim) | Scan radius | Rod scatter | EMP radius | Fuel drag |
|---|---|---|---|---|---|---|---|
| LOW | 300 km | ~2:00 | 0:45 | ×0.8 | 8 m | ×0.8 | 0.02 /s |
| MID | 450 km | ~3:15 | 1:00 | ×1.0 | 20 m | ×1.0 | — |
| HIGH | 700 km | ~5:40 | 1:30 | ×1.4 | 45 m | ×1.25 | — |

A pass is the time the station is within 55° off-nadir of the theatre centre — the reach of
every payload — flown at real ground speed with a per-pass cross-track offset; the
out-of-theatre arc is compressed (`OrbitGapScale`) and labelled simulated. Real period,
velocity, elevation, off-nadir and slant range are shown. Imager GSD: 0.3 / 0.5 / 0.9 m nadir.
A core cannot launch straight to LOW, and LOW needs propulsion; running dry at LOW puts the station into SAFE MODE and it climbs to MID.

Manoeuvres (PRP fitted): **REPHASE** (25 fuel, out of pass only) brings the next pass to
10 s; **RAISE / LOWER** (35 fuel per band) is a 30 s transfer with abilities offline.

## Abilities

| Ability | Needs | Energy | Recharge | Window | Effect |
|---|---|---|---|---|---|
| UPLINK | IMG | load only | — | overhead | Full-screen EO/IR feed; client-only, reveals nothing |
| RADAR SCAN | IMG | 240 kJ | 45 s | overhead | SAR product; host reveals stationary ground contacts in the scene (existing fee) |
| ELINT SWEEP | SIG | 180 kJ | 60 s | overhead | Host reveals enemy ground/ship radars that are emitting within 8 km (scaled) |
| ROD STRIKE | ROD | 80 kJ | 20 s | overhead | Rod from God with band/CMG scatter; spends a rod |
| EMP BURST | EMP | 900 kJ | 180 s | overhead | EMP shock, radius × band |
| REPHASE | PRP | — | 30 s | away | Next pass in 10 s |
| RAISE / LOWER | PRP | — | — | any | Change band |

Allocation fees, perks, the shared request cooldown and the Rod/EMP aircraft range still
apply. Reasons are words: NO STATION, NOT FITTED, MODULE OFFLINE, BROWNOUT, IN TRANSFER, NOT
OVERHEAD, LOW ENERGY, RECHARGING, NO RODS, NO FUEL.

## Debris (LARP events)

Every 6–10 minutes the host picks a random docked non-core module: shielded → "DEFLECTED",
otherwise it goes offline for 45 s. `PlatformDebrisEvents` turns it off.

## Screens

- **OPS › SPACE › PLATFORM** — station banner (callsign, band, OVERHEAD / AWAY / INSERTION /
  TRANSFER, large AOS/LOS clock, pass bar with NOW marker, GET), annunciator tiles (POWER,
  THERMAL, FUEL, LINK, CREW, DEBRIS, ORBIT, MODULES), energy/fuel/rods/mass gauges, a compact
  live schematic, full-width ability rows (two-line readiness, complete cost in its own lane,
  labelled action and recharge bar), voice loop. Without a station, one setup guide replaces
  empty telemetry and abilities, with OPEN PLANNER as the next step.
- **OPS › SPACE › MISSION PLANNER** — the 5 × 3 grid (category colour, code, HOT / +REL / SHD
  / OFF / DOCKING tags, connectors), cell inspector with JETTISON (click twice), launch card
  (next module, cell, mass, vehicle, price, projected mass / net power / storage, 5 s count with
  HOLD, docking progress), core launch with band choice when there is no station, the module
  catalogue as two-column tiles with separate name, fit state and specification lanes,
  CARGO resupply, the wrapped utility rules. Inspector and launch projection have enough
  vertical space for their full explanatory text.
- **OPS › SPACE › ENEMY ACTIVITY** — tracked foreign stations (band, size, overhead / away /
  manoeuvring) and a single COMING SOON notice explaining that ASAT warning, signals
  interception, debris watch and orbital defence are unavailable; no fake action cards.
- **Uplink** — full-screen overlay: 16:10 feed with corner brackets, crosshair, compass tape,
  north arrow, scale bar, frame counter and LIVE light; left rail telemetry; right rail radar
  product and ability buttons 1–4 that fire at the crosshair; pass bar; key legend. Drag or
  WASD to slew (gimbal lag, faster with CMG), wheel or Q/E to zoom to the imager's GSD, double-
  click to centre, Esc or right-click to close. While open the map ignores the mouse, Rewired
  keyboard and the pause keybind are held and restored on close; closing the map closes it.
- **Map** — own station track and position with LOS clock, foreign stations as unknown red
  tracks, the uplink aim point; the armed reticle states STATION OVERHEAD or the refusal.
- **Sky** — the station is a cluster of cubes laid out like the grid (panels flat, radiators
  thin) on its true line of sight at 1/20 scale, fading in and out at the pass edges; foreign
  stations the same in red; module and core launches leave streaks.

## Authority and wire

Host owns layout, pending launch, energy, fuel, rods, brownout, holds, cooldowns, debris and
seeds; snapshots carry them with relative clocks (cycle clock, dock-in, recharge-in, offline
seconds) rebased by clients with a 0.5 s tolerance; up to four foreign stations carry band,
seed, clock and a 15-bit layout mask. Support protocol 11. Commands: LAUNCH (module, cell or
band for the core), JETTISON (cell), REPHASE, ORBIT (band), RESUPPLY.

## Balance intent

- One station per faction; every capability costs a cell, mass, power and money.
- Weapons need the pass, energy, recharge, rods and range; EMP needs storage and cooling.
- LOW is sharp and precise but short and fuel-hungry; HIGH lingers but blurs and scatters.
- Uplink video is information for the viewer only; only the host reveals.

## Acceptance (pending in game)

Station cubes visible and moving believably; uplink drag/zoom responsive with the map inert;
radar product readable; debris and brownout messages clear; late-join client sees the same
station and passes; scene reload leaves no cubes, cameras or held input.
