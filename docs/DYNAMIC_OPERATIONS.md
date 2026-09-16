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
recent outcomes. Dismissing an offer has no penalty; confirming the abort of an accepted
contract costs the faction 1 morale and pays nothing.
Accepted objectives are drawn by this module, not by the native objective UI: a marker per
contract on the tactical map (parented to the map image, so it pans and zooms, and hidden with
the map's own objective-markers toggle) and in the cockpit, where the game camera projects it -
a pointer turning toward the target, a two-line plate with the contract number and name over
`FAMILY · DISTANCE · CLOCK`, an edge-clamped copy when the target is off screen or behind, and
a dotted area ring at the contract's radius. The vicinity card lists up to three contracts
(inside your area first, then nearest, then a lost contact, which is never dropped), with
distance or hold progress, the clock, an approach-then-hold bar and a short banner when
entering or leaving the area. Host-confirmed enemy tracking must remain recent (30 seconds) to
keep a moving target's position; a lost contact keeps its row and says `CONTACT LOST` instead
of drawing a stale marker. Nothing is fed into `MissionPosition` or the mission runner, so
vanilla AI and authored objectives never see a contract.
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
| Bring them home | Recover the marked friendly dismounted pilot through native rescue, then land the rescuing player aircraft within 1 km of the marked friendly base | $1,800 + 125 XP | Use a native rescue-capable loadout, such as the slingload hook; the return leg is additional to vanilla rescue |
| Reconnaissance pass | Observe a tracked hostile ground contact from above within 1.5 km for 20 continuous seconds; faction tracking must be ≤2s old and terrain sightline clear | $900 + 75 XP | Does not create new sensor contacts |
| Confirm the strike | Neutralize a known hostile building after acceptance, then survey its last known site from above within 1.5 km for 20 continuous seconds | $1,200 + 125 XP | Combat disable is remembered; scripted despawn is not a kill |
| Cover the supply run | Keep the same player aircraft above a friendly supply truck within 1.5 km for 60s, then remain on station when it actually supplies another friendly unit | $1,400 + 125 XP | Observes native ammunition resupply or a positive rearmer-to-rearmer transfer |
| Cut the supply line | Neutralize a known hostile ground vehicle with a native rearmer | $1,000 + 125 XP | Removing the native truck removes its resupply capability; no artificial economic debuff |
| Cover the engineers | Cover a damaged friendly building from above within 1.5 km for 30s, then remain while native engineers finish its repairs | $1,400 + 125 XP | Offered only when a live friendly repairer is in the candidate area; native AI still chooses its repair work |
| Hunt the jammer | Neutralize a tracked hostile unit observed applying positive jamming to this faction within the last 60s | $1,400 + 125 XP | Can target aircraft or ground units; does not spawn a fictional jammer site |
| Bring back the intelligence | Observe a hostile ground contact for 30s using the reconnaissance rules, then land that same aircraft within 1 km of the marked friendly base | $1,500 + 125 XP | Acquired intelligence survives subsequent contact loss; losing the aircraft cancels delivery |
| Survey the aftermath | Survey a friendly ground wreck or damaged building from above within 1.5 km for 30 continuous seconds with a clear terrain sightline | $700 + 60 XP | A battlefield survey contract; no fabricated fighting, ambient effects or replay recording |

The pool now contains **17 families**. These additions are secondary contracts inside
DynamicOperations, using the existing MIS cards, markers and reward path. The debrief idea
is represented by an intelligence-return sortie; the ambient battlefield idea becomes an
aftermath survey. They do not add a separate debrief screen or ambient-effects subsystem.

All new aerial cover/survey holds require at least 50 m above the marked subject. Changing
the observing aircraft resets the hold; a lost sightline or stale reconnaissance contact
also resets it. Survey and reconnaissance describe an overflight with faction intelligence,
not a camera-photo or target-lock mechanic. BDA targets stationary buildings so the marked
last-known site remains meaningful after destruction; it never follows an untracked wreck.
Only two sightline attempts per objective per tick are made, favoring the existing observer.

Rescue/report markers switch to the return base after acquisition. The rescuing/observing
aircraft must still be alive, player-controlled and in the same faction; a respawn cannot
deliver its mission. Losing the return base cancels the task. Native rescue retains its
normal effects and rewards even if the extra return contract later fails; BS never edits
Squad's career state from this mission. Native supply/repair must finish after acceptance
and sufficient cover. Cover alone grants no service award, and no new truck or engineer
orders are issued. If a building is repaired before sufficient cover, the contract cancels.
Mission authors must supply appropriate native units and service demand for these tasks.

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

Generation and pricing follow the front's escalation ladder. A mission at conventional
posture offers every 30 seconds; tactical every 24; strategic every 18. A fresh offer's
money and XP are multiplied by 1.0, 1.15 or 1.35 for the same three stages, on top of
`RewardMultiplier` and inside the existing award limits. Only the mission's own
`tacticalThreshold`/`strategicThreshold` gate the stages; a zero threshold means the
mission never gated that stage, exactly as the MIS main tab shows it.

Completing a contract with a paid award may seed one follow-on for the same board:
a capture leads to a defense; reconnaissance, a sortie report or a confirmed strike lead
to an interdiction; an escorted supply run leads to cutting the enemy's; jamming leads
to an intercept. A chain stops after two links, at most one follow-on is pending per
board, and a follow-on with no valid candidate at that moment is skipped rather than
offered as an empty card. Cancelled and expired contracts never chain. Aborting an
accepted contract costs the faction 1 morale; dismissing an offer or letting it expire
does not.

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

Server work runs at 1 Hz; generation runs at most every 30 seconds per faction at
conventional escalation (24 tactical / 18 strategic) and
at most one faction generates in any tick. Hard
limits are eight faction boards, three cards each, 128 issued objectives per faction
per mission, 64 registered airbases, two passes over 4,096 unit candidates per faction per generation,
and 64 payment recipients/query records. Chains add at most one pending follow-on per
board and two links per operation, delivered inside the same two candidate passes. Road preflight accepts at most 512 nodes,
2,048 roads and 8,192 road points. Larger road graphs omit convoy awards.
All extra families share the second unit pass. Observation casts are capped at 32 per host
tick (two per objective); recent hostile jammer records at 32 with 60-second freshness.
The new contracts spawn no additional objects. Scene reset clears their candidates,
service evidence, acquired aircraft references and jammer history.

The client requests only its own faction's snapshot and sends accept/dismiss intent by
contract ID. Protocol **2** requires matching peers. The host derives identity and faction,
rate-limits reads/actions, and never accepts completion or reward data from clients.
Request/scene tokens reject old responses; IDs remain monotonic across mission resets,
so delayed commands cannot accept a different mission's reused ID. Snapshots clear
after six seconds without a valid refresh. Native Mirage owns spawned objects.
Protocol 2 remains unchanged: cards already carry title, instructions, status, progress
and marker coordinates, so return stages need no new fields or client completion messages.
Markers are built client-side from that snapshot and are never registered with the mission
runner, so vanilla AI cannot mistake a contract for a navigation objective.

Required runtime checks before enabling by default: all 17 contract families, acceptance/
abort/expiry, marker positions through map zoom and floating-origin shifts, jamming on headless
and listen hosts, Ibis completion/abort, and tax/score/morale awards; convoy path and supply behavior; blocked/partial spawn
cleanup; listen-host/remote-client/late-join faction views; pause/resume, faction switch,
mission reload and disable cleanup. Build and pure tests cannot prove those behaviors.
For the new families also test native rescue with a remote player and return-aircraft loss;
recon behind terrain and after contact expiry; BDA combat loss versus scripted removal;
real supply transfers versus empty attempts; repair before/after cover qualification;
jammer source attribution; observer changes; return-base capture; and late joining during
an acquired return stage. Also test a chain across its two links, a follow-on whose
candidate has expired (skipped, not an empty card), escalation changes moving the
generation interval and offer value, and the morale loss on an active abort only. Release build, pure state-machine regressions, the installed-game
signature/serializer probe and static Harmony verification pass; these are not flight tests.

Research and independent design rationale:
[builders and logistics](RESEARCH_DYNAMIC_OPERATIONS.md),
[AI and command](RESEARCH_DYNAMIC_AI.md),
[combat customization and compatibility](RESEARCH_DYNAMIC_COMBAT.md).
The requested Ponytail and UI/UX Pro Max skills guided native reuse, narrow ownership,
bounded work, readable hierarchy and explicit state feedback.
