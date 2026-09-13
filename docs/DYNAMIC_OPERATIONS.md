# Dynamic frontlines and secondary missions

This development slice extends Command's map overlay and adds the independent
`dynamic-operations` module. It preserves authored victory objectives and vanilla
airbase capture rules. In-game validation remains pending.

## Enable and play

Set `[DynamicOperations] Enabled = true` in
`BepInEx/config/com.marci.boscalisummer.cfg`, then restart. The default is **false**
until gameplay and multiplayer validation pass. Enable it on the host and modded
clients. The full MIS panel also requires `Progression.Enabled`, `Command.Enabled`
and `Command.ExpandedMapUi` under the existing Command registration.

Open the tactical map, select **MIS**, then **SECONDARY → AVAILABLE**. Accept a contract
for your faction before doing it. **ACTIVE** shows execution progress; **RESULTS** keeps
recent outcomes. Dismiss an offer, or confirm an active abort, without a penalty.
Accepted objectives receive numbered map markers. Host-confirmed enemy tracking must
remain recent (30 seconds) to display a moving target; lost contacts hide their marker.
The original
briefing and authored objectives remain on their tabs. A host without this module
cannot supply secondary data; a missing or stale reply clears the panel.

| Objective | Trigger and completion | Base reward before faction tax | Special, when available |
|---|---|---|---|
| Secure the front | Capture the nearest eligible hostile base within 40 km of a friendly base | $1,800 + 150 XP | Three native defensive buildings at the captured base |
| Hold the line | Threatened friendly base: retain ownership and keep a player aircraft within 1.5 km, at least 50 m above the base, for 180 continuous seconds | $1,200 + 100 XP | Six vehicles launch from another friendly base toward it |
| Break enemy pressure | Neutralize a known hostile ground vehicle or building within 15 km of a friendly base; nearby AA/combat roles take priority | $800 + 75 XP | None |
| Air intercept | Down a recently tracked hostile aircraft selected within 25 km of a friendly base; ten-minute execution window | $1,600 + 125 XP | None |
| Watch the approach | Keep a player aircraft within 1.5 km and at least 50 m above the point for 90 continuous seconds | $700 + 60 XP | None |
| Silence the radar | Deliver positive native jamming to the selected emitting ground target for 45 continuous observed seconds; interruptions beyond 1.5 s reset progress, destroying the emitter cancels | $1,400 + 125 XP | None |
| Establish a beachhead | Ibis fast-rope eight troops onto dry ground within 100 m of the marked landing zone, below 45 m above the surface; completed landing required | $1,500 + 125 XP | Existing insertion mechanics may establish defenses if placement succeeds |
| Rooftop insertion | Ibis fast-rope eight troops onto the exact marked civilian shell, within 40 m of the mark; completed landing required | $1,500 + 125 XP | Landing is the objective, not a guarantee of roof occupation |

Every connected player still in the faction at completion receives the base reward,
including when faction AI completes a capture, strike or jamming task after acceptance.
Patrol/defense require a connected faction player's aircraft; parking does not count.
Holds reset when their condition stops being observed. XP is native **mission score**, so it
also feeds score-based perks; there is no persistent XP profile. Money follows the
normal faction tax. `RewardMultiplier` scales both values (0.25–4). This initial
team-award policy does not attempt individual contribution attribution.

Each faction can have three cards, with at most two accepted contracts. The director
shuffles eligible mission families and randomly samples airborne/jamming contacts and
friendly insertion/patrol anchors. Rooftops come from UrbanCombat's cached shell catalogue;
the relevant capabilities must exist. Empty terrain-only missions still require a friendly base.
Each target/objective combination is offered once per mission. Offers expire after five
minutes; acceptance starts a fresh 20-minute timer (10 minutes for intercept). Terminal
cards remain for one minute. An unaccepted objective never progresses or pays.
Lost defense bases and invalid/despawned targets cancel objectives. A target's actual
disable event is remembered even if its object disappears before the next tick.
The editor does not run the director.

## Strategic consequences

Each success adds 3 to the faction's stored Morale and subtracts 3 from the hostile
target's original faction, where applicable. Command clamps this to 0–100; initially
100, positive awards matter after losses. Morale is still host-only and has no combat
multiplier. Money/XP remain available when Command is absent. There are no new direct
perk-point grants, timed faction modifiers or friendly aircraft wing spawns in this
implementation; native mission score already feeds the existing perk budget.

The advisory control field retains recent pressure, shifts over elapsed time, and
gradually recovers toward strategic base ownership when pressure leaves. Actual base
captures change strategic influence. Enemy inputs are recorded faction observations
and expire after 30 seconds; neutral bases do not fabricate an opposing faction.
Aircraft and ships do not paint land control. Rendering sleeps while the map is closed,
then reconciles current intelligence when reopened. Mission logic continues on the host
independently of the map.

Convoys contain four native armored vehicles and two supply trucks with a rearmer.
They receive native ground destinations and require a connected road from another
friendly base, clear dry footprints and capacity. The panel says **launched**; it does
not claim arrival or delivery. Fortifications are three visible native defensive
buildings with normal combat and network behavior. No custom units, free stock loops,
unlimited ammo, aircraft retasking or purchase-menu edits are introduced.

Physical awards share a 120-second faction cooldown and a 24-object ceiling for
retained reward unit roots. Native wreckage uses the game's lifecycle. Offers depend
on loaded definitions and capacity. Conditions can change before completion: if the
destination, route, terrain, space or cooldown prevents deployment, the card reports
that outcome and the base money/XP award remains. Partial batches are removed. The
module owns its spawned roots and removes them on scene/mission reset and teardown.

## Validation and ceilings

Server work runs at 1 Hz; generation runs at most every 30 seconds per faction and
at most one faction generates in any tick. Hard
limits are eight faction boards, three cards each, 128 issued objectives per faction
per mission, 64 registered airbases, at most three passes over 4,096 unit candidates per faction per generation,
and 64 payment recipients/query records. Road preflight accepts at most 512 nodes,
2,048 roads and 8,192 road points. Larger road graphs omit convoy awards.

The client requests only its own faction's snapshot and sends accept/dismiss intent by
contract ID. Protocol **2** requires matching peers. The host derives identity and faction,
rate-limits reads/actions, and never accepts completion or reward data from clients.
Request/scene tokens reject old responses; IDs remain monotonic across mission resets,
so delayed commands cannot accept a different mission's reused ID. Snapshots clear
after six seconds without a valid refresh. Native Mirage owns spawned objects.

Required runtime checks before enabling by default: all eight contract families, acceptance/
abort/expiry, marker positions through zoom and floating-origin shifts, jamming on headless
and listen hosts, Ibis completion/abort, and tax/score/morale awards; convoy path and supply behavior; blocked/partial spawn
cleanup; listen-host/remote-client/late-join faction views; pause/resume, faction switch,
mission reload and disable cleanup. Build and pure tests cannot prove those behaviors.

Research and independent design rationale:
[builders and logistics](RESEARCH_DYNAMIC_OPERATIONS.md),
[AI and command](RESEARCH_DYNAMIC_AI.md),
[combat customization and compatibility](RESEARCH_DYNAMIC_COMBAT.md).
The requested Ponytail and UI/UX Pro Max skills guided native reuse, narrow ownership,
bounded work, readable hierarchy and explicit state feedback.
