# CYBER — spectrum and network defence

Status: slice 1 and the slice 2 infrastructure pass implemented; in-game visual, balance and multiplayer
acceptance pending. Dates: 2026-09-17 (slice 1), 2026-09-18 (self-building infrastructure).
Owner: Support. Platform: Nuclear Option PC map MFD (480 px bezel), maximised map, full-screen console.
Design authority: the user's request to remake the OPS EW module to the depth of SPACE, defence-first,
merged with information warfare into one CYBER tab (reference: the multi-domain EW picture —
jammers, SIGINT, radars, relays, cyber operations), and their answers: merge EW + INFO into CYBER
with sub-tabs; one console with NETWORK and SPECTRUM modes; a host AI adversary campaign plus
enemy players' operations; map sites that are network nodes and real vanilla units; first slice =
the core loop end to end with the NETWORK console.
Method: Game Studios quick-design structure; Ponytail reuse of the uplink scaffolding, native
tracking reveals, the INFO doctrine ledger, the support command pipeline and the OPS row/tile/loop
widgets; UI/UX Pro Max status words beside colour; Superpowers verification before any claim.
Replaces the single EW truck (`EwAssets`), its posture page (`SupportPanel.Ew`) and the INFO tab
(`SupportPanel.Info`).

## Slice 2 — infrastructure that builds itself (2026-09-18)

Design authority: the user's 2026-09-18 feedback — keep the CYBER direction; managing trucks is
tedious; infrastructure should spawn by itself, trucks should leave from the nearest vehicle depot,
and the whole EW side needs streamlining and a better tie to the battlefield.

| Slice 1 friction | Slice 2 |
|---|---|
| Nothing works until a Cyber Command truck is driven somewhere | **Airbases are the network.** The host raises Cyber Command on the owned airbase nearest the centre of the faction's holdings and a gateway on up to five further owned airbases (nearest to Cyber Command first), once a second, with no player action. |
| OFF-NET everywhere, 12-site limit shared with the root | Airbase nodes share a **backbone**: always linked to each other whatever the distance. A field site needs radio range to *any* on-net node. Field sites have their own limit (`CyberSiteLimit`, default 8, at most 10). |
| Trucks appear at the airbase centre | A field truck leaves the **bay of the owned vehicle depot nearest the mark** (the vanilla depot spawn transform, facing out, spaced when several are ordered back to back); the nearest owned airbase only when the faction has no working depot. |
| Jammer modes gate operations (NOISE for blackout, DECEPTION for spoof) | **Any emitting jammer** (NOISE or DECEPTION) backs every station-backed operation. EMCON is "go dark". Modes still set umbrella strength and loudness. |
| The network is abstract | **Anchored on real buildings.** Each airbase node lives in the base's map tower (else its first building). While that building is destroyed the node is **DOWN** — off the backbone, no bandwidth, what hangs off it strands — and it returns when the game repairs the building. Capturing a base takes its node; losing Cyber Command's base moves the root to the next base. |

The adversary campaign still needs a fielded truck to start (see the loop below), so the defence
game stays opt-in. Airbase nodes are free, cannot be moved or scrapped, and are never ordered by hand; a gateway can
still be isolated to hold an intrusion, Cyber Command cannot. Carriers and other attached airbases
are never nodes. The campaign, verbs, incidents, footholds and console are unchanged.

## Player fantasy

You run your faction's integrated spectrum-defence network. You park real radar, jammer and
SIGINT vehicles along the front and watch a mesh of links grow back to Cyber Command. The
adversary probes, jams and breaks in: the klaxon sounds, INFOCON drops, red crawls along your
links. You take the console — isolate, patch, bait, trace — and when a trace lands you own a
foothold in their network and strike back with the offensive operations. **Defend → trace →
strike back.**

## Core loop

1. Hold an airbase. Cyber Command and the gateways come up by themselves on the faction's
   airbases and link over the backbone.
2. Optionally deploy radars, jammers, SIGINT posts and relays (ARCHITECT › DEPLOY, right-click the
   map). Each rolls from the nearest owned vehicle depot and is EN ROUTE until it arrives (or stops
   for 12 s after 20 s). A field site is ON the net when a path of links reaches Cyber Command.
3. The adversary campaign starts once Cyber Command is online **and the faction fields its first
   truck** (first move after ~90 s). Airbase nodes alone draw no campaign, so a player who ignores
   CYBER is never breached by it.
4. Answer incidents on THREATS or in the console; trace them for footholds; spend footholds on
   OPERATIONS.

## Sites (slice 1)

| Code | Site | Price | Bandwidth | Link | Cover | Emits | Copies | Effect |
|---|---|---|---|---|---|---|---|---|
| C2N | Cyber Command (airbase) | free | +6/s | 18 km + backbone | — | silent | 1 | Root; compromised, it locks OPERATIONS and stops feeding bandwidth |
| GWY | Airbase gateway | free | +2/s | 18 km + backbone | — | low | 5 | Backbone node; +5 Mb capacity; catches field sites near its base |
| EWR | Early-warning radar | 700 | −1 | 12 km | 22 km | loud | 3 | Every 4 s reveals hostile aircraft in cover to faction tracking |
| JAM | Defensive jammer | 800 | −2 | 12 km | 9 km | loud | 3 | ECM umbrella (below); backs RADAR BLACKOUT / GHOST / SPOOF |
| SIG | SIGINT post | 600 | −1 | 12 km | 15 km | silent | 2 | Every 6 s reveals hostile emitters; blocks probes; hears enemy operations; required to TRACE |
| REL | Relay mast | 350 | +2 | 24 km | — | low | 4 | Long links; +10 Mb capacity |

Prices scale with `CyberSiteCostScale`, `CostMultiplier`, events and the Logistics perk. Every
field site is the cheapest working vanilla `TRUCK` (the radar container type `RDR` does not drive);
vehicles are destructible. A destroyed site is LOST (no refund) and its slot frees after 30 s;
SCRAP (two clicks) withdraws the vehicle and refunds `CyberScrapRefund` (50 %). Airbase nodes are
never scrapped. At most `CyberSiteLimit` (8, at most 10) field sites; sixteen slots in all.

Site state words, most urgent first: LOST, DOWN, EN ROUTE, COMPROMISED / PATCHING, ISOLATED,
OFF-NET, BAITED, JAMMED, ONLINE. Effect strength: 0 unless working (online, not isolated, not
compromised); ×0.5 off the net; ×0.5 inside a jamming raid; ×0.75 on a congested net (draw >
production). MOVE drives a site to a new mark; it is off the net until it arrives.

### Jammer modes (the old postures)

| Mode | Umbrella | Emits | Backs |
|---|---|---|---|
| NOISE | 100 % | loud | every station-backed operation |
| DECEPTION | 50 % | moderate | every station-backed operation |
| EMCON | none | silent | nothing (hide from probes and hunters) |

Slice 1 split the operations between NOISE and DECEPTION; slice 2 lets any emitting jammer back all
of them, so the mode is a trade between umbrella strength and loudness rather than a gate.

### ECM umbrella (vanilla lever)

Every 0.25 s the host finds hostile missiles with an active (`ARHSeeker`) or semi-active
(`SARHSeeker`) seeker whose target is a friendly unit inside a working jammer's cover and adds
`0.2 × mode × site strength × (1 − 0.5 × distance/radius)` to the seeker's private
`jamAccumulation` (clamped 0–1 by the game, and it decays every frame). Past the seeker's
`jamTolerance` the game's own `GetRadarReturn` reads zero and the missile falls back to datalink,
which is what breaks the lock. Only missiles the host simulates are touched (AI shots). Every
seeker pushed past tolerance counts once in ECM KILLS.

## Bandwidth and console verbs

Pool capacity 40 Mb + 10 per relay and + 5 per gateway on the net. Refill 0.25/s plus half the net surplus while
Cyber Command is online. Cyber Command arrives with 24 Mb banked, so the first incident of a
match can always be answered.

| Key | Verb | Target | Cost | Recharge | Effect |
|---|---|---|---|---|---|
| 1 | ISOLATE | site (not C2) | 8 | 4 s | Cut its links: an intrusion there stalls and is contained after 40 s; the site stops working. Pressing again rejoins, free |
| 2 | PATCH | compromised site | 18 | 12 s | 8 s channel, then clean. An intrusion still sitting there re-compromises it: isolate first |
| 3 | HONEYPOT | site (not C2) | 14 | 25 s | 60 s bait: an intrusion that reaches it is HELD until contained (even after the bait lapses) and traces ×2 |
| 4 | TRACE | intrusion or heard operation | 22 | 20 s | Needs a working SIG; 20 s (×2 held, ×1.25 with two SIGs). Done: 240 s foothold on that origin (discount + backdoor), +1 INTEL token, host reveals hostile emitters within 40 km |
| 5 | BURN THROUGH | raid | 20 | 25 s | Needs a working jammer (not EMCON) within raid radius + 9 km; ends the raid |

Refusal words: NO CYBER COMMAND, SELECT A TARGET, SITE EN ROUTE, SITE LOST, SITE OFF-NET, NOT ON
CYBER COMMAND, SITE IS CLEAN, PATCH RUNNING, ALREADY BAITED, NEEDS A WORKING SIG POST, NO JAMMER
IN REACH, NOT TRACEABLE, TRACE RUNNING, LOW BANDWIDTH, RECHARGING.

## Adversary campaign (host, per faction network)

Heat 0–100 rises 1 per 20 s × `CyberCampaignIntensity` while Cyber Command is online, and +8 for
every offensive operation the faction runs. Phase: PROBING (< 30), ACTIVE (< 65), OFFENSIVE.
First move after 90 s / intensity; then every 150–240 s (probing), 90–150 s (active), 60–100 s
(offensive), divided by intensity. At most two intrusions (one while ACTIVE) and one raid at once,
six incident slots; resolved incidents linger 90 s on the board. Origins are the other factions
in name order; the snapshot names them.

| Incident | Opens on | Resolves |
|---|---|---|
| RECON PROBE | a clean emitting site | After 20 s: BLOCKED under a working SIG ear; otherwise EMITTERS EXPOSED — for 90 s the host writes every emitting site into the origin faction's tracking every 5 s |
| INTRUSION | the clean edge site with fewest links | Lands after 20 s and compromises its site, then walks one link toward Cyber Command every 26 s (16 s offensive), taking bait on the way. CONTAINED after 40 s held (isolated or baited), TRACED, or ATTACKER WITHDREW after 240 s — what it took stays compromised |
| JAMMING RAID | a sector 1.5–4.5 km off any site, 8 km radius | Links and radar cover inside halve; BURNED THROUGH or ENDED after 90 s |
| HOSTILE OPERATION | an enemy player's accepted cyber operation under your SIG ear | Traceable for 45 s |

INFOCON = 5 − (1 per active incident, 2 per intrusion, 1 per compromised site, +2 when Cyber
Command is compromised), never below 1.

**The player steers the escalation.** Every incident the defender wins cools the adversary by 4
heat, a completed trace by 8, so holding the line pushes the phase back down; the board counts
DEFENDED against BREACHED (a probe that exposed you or an intruder that withdrew with what it
took) so a match reads as a running score.

**Neglect degrades, it does not kill.** A compromised *field* site the adversary has left alone
(no intrusion on it, no patch running) is reimaged by the watch floor after 120 s. Cyber Command
never heals itself, and nothing heals while Cyber Command is breached: a player who ignores the
network loses capability, not the network, but a breached C2 must be patched by hand.

**A finished trace is a backdoor.** While a foothold is open, station-backed operations against
anyone need no jammer of your own in reach and no particular mode — the trace carries them —
on top of the 25 % discount and the INTEL token.

## Screens

- **OPS header** — the third metric reads `CYBER on-net/nodes` with INFOCON and the bandwidth bar.
- **CYBER › NETWORK** — banner (AEGIS NET, INFOCON word that flashes at 2 and below, adversary
  phase and heat, bandwidth bar and net flow, one-line situation, OPEN CONSOLE), eight annunciators
  (BANDWIDTH, LINKS, INTRUSION, JAMMING, EMCON, FOOTHOLD, SITES, ADVERSARY), the live mesh
  (click a site → ARCHITECT inspector), voice loop.
- **CYBER › ARCHITECT** — 01 FIELD SITES catalogue (four truck kinds, trucks/limit, emissions,
  READY or why not, DEPLOY arms the map, the truck rolls from the nearest depot); 02 INSPECTOR (< >
  cycling, call sign, state, grid, hops, emissions, cover, strength and umbrella, NOISE / DECEPTION
  / EMCON, MOVE, SCRAP with CONFIRM; an airbase node says BACKBONE or ANCHOR BUILDING DESTROYED and
  its tooltips say why it cannot move or be scrapped); 03 ROSTER (sixteen slots, airbase nodes
  tagged AIRBASE); 04 DOCTRINE (SIGINT, CRYPTO, C2D, EWD levels, unchanged from INFO).
- **CYBER › THREATS** — 01 campaign card (phase, next move, heat bar with the 30/65 marks,
  exposure or heat note, footholds with clocks); 02 incident board (six rows: code, kind · origin,
  state or outcome, detail, TRACE / BURN and ISO / JOIN with refusal words in the tooltip);
  03 defence drill.
- **CYBER › OPERATIONS** — the five offensive operations and the flare barrage; the note reads
  C2 BREACHED, FOOTHOLD −25 % (+ CRYPTO), CRYPTO −n %, or the arming hint.
- **Console** — full-screen: the mesh at real geography with traffic pulses toward C2, pulsing
  intrusion frames with trace %, raid sector boxes, bait halos; left rail WATCH OFFICER (INFOCON,
  adversary, heat, bandwidth, net flow, sites, links, foothold, emitters, ECM kills) and SELECTED
  SITE; right rail COUNTERMEASURES (keys 1–5, cost, refusal word, target, recharge bar) and the
  INCIDENT STACK (Tab); voice loop, key legend, status strip. The border burns amber at INFOCON 3
  and pulses red at 2 and below. Click a site to select, Q/E step, Esc or right-click closes.
  Input is held like the uplink (`FullscreenInput`, `UplinkInputGuardPatch` now asks
  `FullscreenInput.AnyOpen`).
- **Map** — field sites as diamonds and airbase nodes as larger squares, with call sign and state,
  link lines (green on net, amber off, red pulsing compromised; the backbone as faint blue spokes
  from each gateway to Cyber Command), jammer umbrellas (solid rings) and SIGINT ears (dotted), raid rings,
  pulsing intrusion rings, and while DEPLOY/MOVE is armed a dotted link-reach ring at the cursor
  that turns green when the new site would join the net.
- **Voice loop and klaxon** — every notice becomes a mission-control line. Lines that need the
  watch officer's eyes are prefixed `!!` (break-in, compromise, C2 breach, site or C2 loss, raid,
  exposure). Only four of them make noise — break-in, C2 breach, C2 loss and a raid — so the
  synthesized two-tone klaxon (at most once per 6 s) keeps meaning something. A broken hostile
  seeker speaks too ("ECM · RADAR MISSILE LOST LOCK OVER JAM-KILO"), rate limited to one line
  every 4 s, so the umbrella is audible instead of being a number in a rail.
- **The advisor** — `CyberWords.Advice` names the one thing to do next in the order a watch
  officer would do it (hold an airbase / C2 down → patch a breached C2 → isolate a landing or spreading intrusion
  → trace what is held → burn a raid → patch, rejoin, relink, a DOWN node → what the network
  still lacks →
  the running score). It is the NETWORK situation line, the THREATS note and a lit bar across the
  bottom of the console with **[SPACE] DO IT**, which fires exactly that verb on exactly that
  target. It is pure and tested, so the pages, the console and the tests agree.

## Authority and wire

Host owns sites, vehicles, bandwidth, recharges, incidents, heat, footholds, exposure, notices and
the seeker tally, and which airbases are nodes. Support protocol 13 (12 was slice 1).
`OpsStateMessage` carries a `CyberSnapshot` (≤ 16 sites with slot, kind, x/z, flags — en route,
lost, isolated, compromised, static (16), down (32) — mode, patch and bait clocks; bandwidth;
ECM tally; five recharge clocks; heat; next-move and exposure clocks; ≤ 6 incidents with kind,
state + tracing/held bits, site, origin, x/z, age, time left or since resolved, trace byte;
eight foothold second-bytes; the defended/breached tally; notice serial and ≤ 6 notices) plus
≤ 4 origin names (≤ 20 chars; further origins read as "HOSTILE ACTOR n"). A full snapshot is
257 bytes typical and 724 worst case, pinned under 900 by the patch probe so a poll stays in one
datagram. The host's airbase identity behind a node never crosses the wire; a client believes the
static flag only on a static kind.
Clients rebase clocks with a 0.5 s tolerance and drop garbage. Commands: CyberBuild (12, kind +
mark), CyberScrap (13, slot), CyberVerb (14, target + verb), CyberMode (15, slot + mode),
CyberMove (16, slot + mark); 4, 5 and 7 are retired. Refused verbs return
`SupportResult.CyberRefused + CyberDenial`.

## Settings (host)

`ElectronicWarfare` (spectrum defence on/off), `CyberSiteCostScale` (1.0), `CyberScrapRefund`
(0.5), `CyberSiteLimit` (8 field sites, 1–10; airbase nodes never count), `CyberCampaignIntensity`
(1.0; 0 = off), `EwProximityRadiusMeters` (15 km: emitting-jammer reach for station-backed
operations). SET SERVER rows 10, 22–25. `EwTruckCost` is gone. Airbase nodes follow `ElectronicWarfare`:
with spectrum defence off the host raises none.

## Budgets

Model: 16 slots (6 airbase + 10 field), 120 links recomputed per host tick with no allocation,
6 incidents, 6 notices. Host world pass: one airbase pass per second per faction (owned bases,
no allocation beyond one reused list); one `UnitRegistry` scan every 0.25 s for radar missiles;
reveals at most 48 units per sweep every 4 s (EWR) / 6 s (SIG) per site. Panel: 16 nodes + 120
lines + 6 marks per graph (NETWORK page and console); map layer 16 markers + 16 covers + 120 lines
+ 6 rings + preview. One klaxon clip, cached.

## Known limits

- The umbrella only reaches seekers the host simulates; player-fired missiles simulated on a
  client are untouched.
- A compromised early-warning radar simply goes dark; it does not yet feed false tracks.
- Heat is per defending network, not per origin; origins are drawn at random.
- The advisor names one move at a time and does not plan ahead (it will not tell you to bait the
  next hop before the intruder gets there).
- SPECTRUM console mode, DECOY/HPM/NAV sites, disinformation on the news wire, SEAD tasking,
  capstones, sky domes, radio jam level and weather link fade are slice 2 and 3.
- EMP and RADAR BLACKOUT still pass the player's aircraft as the vanilla jamming unit.
- Airbase nodes are raised only for factions the Support module already tracks (a faction whose
  players have opened OPS or polled it); an AI-only faction has no network to see.
- A node's anchor is the map tower, else the first building the base lists; whether every map's
  towers are destructible and repairable is an in-game check still to do.
- The depot bay is the vanilla spawn transform; trucks spawned in quick succession are spaced 10 m
  apart, four abreast, but the depot's own vehicle spawns are not coordinated with them.

## Acceptance

- Automated (done): pure suite (`CyberNetworkTests`: catalogue, placement, links/BFS/relays,
  isolation, bandwidth and verbs, congestion, intrusion walk to C2, containment, honeypot hold,
  trace and foothold expiry, patch, probes, raids and burn-through, campaign escalation and caps,
  hostile operations, loss/scrap/relocation, self-repair and heat relief, the seeker tally and its
  rate limit, the advisor's order, klaxon narrower than the alarm lines, snapshot mirror and
  garbage, words, and slice 2's airbase infrastructure: root and gateways, backbone at any
  distance, gateway capacity, field sites linking to the nearest airbase node, no scrap or move
  for airbase nodes, DOWN strands what hangs off it, root moving when its base falls, no base no
  network, static/down flags over the wire and never believed on a field kind; `OpsDomainTests`:
  four tabs, emitting-jammer gates, compromised command, the foothold backdoor), Release build,
  patch probe protocol-13 roundtrip with a static/down site, 16-site worst case (724 bytes),
  over-bound site count, private seeker fields and the vehicle depot spawn bay, Harmony
  verification (105/105 reflection lookups).
- Pending in game: airbase nodes appearing on every owned base with Cyber Command on the central
  one, a tower destroyed → DOWN → repaired → back, a base captured → node changes hands, trucks
  leaving the nearest depot's bay and driving to the mark, panel fit at 596 and 896 px, every
  sub-page and hover, deploy/move/scrap with real vehicles and arrival, links and OFF-NET on the map and board, incidents on schedule
  (use `CyberCampaignIntensity` 4 to speed them), console keys and input release, klaxon volume,
  a seeker defeated inside an umbrella, a site killed → LOST, listen host and remote client,
  late join, scene reload leaving no vehicles tracked, canvases or held input.
