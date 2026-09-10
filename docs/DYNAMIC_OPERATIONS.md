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

Open the tactical map, select **MIS**, then **SECONDARY**. Objectives are automatic
and shared by faction; there is no acceptance or reward-claim button. The original
briefing and authored objectives remain on their tabs. A host without this module
cannot supply secondary data; a missing or stale reply clears the panel.

| Objective | Trigger and completion | Base reward before faction tax | Special, when available |
|---|---|---|---|
| Secure the front | Capture the nearest eligible hostile base within 40 km of a friendly base | $1,800 + 150 XP | Three native defensive buildings at the captured base |
| Hold the line | A known hostile ground vehicle threatens a friendly base within 6.5 km; retain ownership for 180 observed seconds | $1,200 + 100 XP | Six vehicles launch from another friendly base toward it |
| Break enemy pressure | Neutralize a known hostile ground vehicle or building within 15 km of a friendly base; nearby AA/combat roles take priority | $800 + 75 XP | None |

Every connected player still in the faction at completion receives the base reward,
including when faction AI completes the task. XP is native **mission score**, so it
also feeds score-based perks; there is no persistent XP profile. Money follows the
normal faction tax. `RewardMultiplier` scales both values (0.25–4). This initial
team-award policy does not attempt individual contribution attribution.

Each faction can have three cards. Each target/objective combination is offered once
per mission. Objectives expire after 20 minutes; terminal cards remain for one minute.
Lost defense bases and invalid/despawned targets cancel objectives. A target's actual
disable event is remembered even if its object disappears before the next tick.
The editor does not run the director.

## Strategic consequences

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
per mission, 64 registered airbases, 4,096 unit candidates per faction per generation,
and 64 payment recipients/query records. Road preflight accepts at most 512 nodes,
2,048 roads and 8,192 road points. Larger road graphs omit convoy awards.

The client only requests its own faction's snapshot. Protocol 1 carries bounded cards;
the host derives identity and faction, rate-limits reads, and never accepts completion
or reward data from clients. Request/scene tokens reject old responses; snapshots clear
after six seconds without a valid refresh. Native Mirage owns spawned objects.

Required runtime checks before enabling by default: single-player capture/defense/
interdiction and tax/score awards; convoy path and supply behavior; blocked/partial spawn
cleanup; listen-host/remote-client/late-join faction views; pause/resume, faction switch,
mission reload and disable cleanup. Build and pure tests cannot prove those behaviors.

Research and independent design rationale:
[builders and logistics](RESEARCH_DYNAMIC_OPERATIONS.md),
[AI and command](RESEARCH_DYNAMIC_AI.md),
[combat customization and compatibility](RESEARCH_DYNAMIC_COMBAT.md).
The requested Ponytail and UI/UX Pro Max skills guided native reuse, narrow ownership,
bounded work, readable hierarchy and explicit state feedback.
