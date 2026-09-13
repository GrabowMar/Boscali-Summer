# Secondary contracts

Implementation reference: [dynamic operations](../../docs/DYNAMIC_OPERATIONS.md).
Extends the existing director and MIS panel; preserves authored victory objectives.

## Player loop

1. Read three randomized faction offers with requirements, target and reward visible.
2. Accept up to two. Acceptance starts execution time and reveals objective markers.
3. Fly an appropriate aircraft/loadout. Holds require continuous presence or real jamming;
   insertions require eight troops to finish fast-roping, not just hovering over a point.
4. Receive automatic faction money/mission score and strategic consequences once.
5. Check recent results, then choose another offer when the director has an eligible target.

Available / Active / Results distinguish intent from execution. Offer and execution clocks
are labeled separately. Large buttons include accept, dismiss and two-step abort. Compact
596 px MFDs show one card per page; tall 896 px MFDs show two. Empty states explain the next
step. Primary objectives strip authored text markup before display and use taller wrapping rows.
Progress-bar rectangles resize directly, including a truly empty zero-percent state.

## Balance check

Applied the requested Claude-Code-Game-Studios balance-check guidance to OperationBoard,
OperationsManager, OperationRewards, DynamicOperationsSettings and the existing Progression
score budget. This is a static economy review, not a flight-playtest verdict.

| Concern | Design decision / evidence |
|---|---|
| Passive farming | Offers never progress; patrol/defense require an aircraft 50+ m above the reference point. Each kind/target is offered once per mission, capped at 128 issued. |
| Repeated claims | Only the host transitions completion and consumes the award latch before side effects. No claim button or client-selected payment. |
| Hold exploits | Interrupted activity resets the hold; a scheduling stall credits at most two seconds. Destroying a jam target cancels rather than paying. |
| Risk/reward | Patrol pays $700/60 XP for 90 s; defense $1,200/100 XP for 180 s plus possible convoy; jamming $1,400/125 XP for 45 s with a live emitter; Ibis insertion $1,500/125 XP. Travel, equipment and exposure are part of these costs. |
| Perk inflation | XP uses native mission score and the existing configured perk cap; no second perk currency or uncapped points. |
| Team scaling | Every connected faction member gets the reward, up to 64 recipients. Large teams create more total money; this preserves the existing faction-award policy and needs playtesting. |
| Morale | +3 friendly / -3 hostile target owner, clamped 0–100. Initial 100 means recovery rewards have no effect until morale has fallen. No combat buff or remote-client morale replication is claimed. |
| Reward supply | Convoys/fortifications share the existing 24-object ceiling and 120 s faction cooldown. Terrain/route/capacity failure keeps earned money/XP and reports the failed special. |

No new friendly aircraft spawning or commands: Wing Command ownership stays intact.
Timed faction modifiers and direct bonus perk-point grants remain outside this implementation.

## Acceptance criteria

- Given an offer, performing its activity before acceptance grants neither progress nor reward.
- Given two accepted contracts, accepting a third fails with feedback.
- Given a lost contact, the accepted hunt remains active but its enemy marker disappears.
- Given a stale prior-mission ID, the host does not accept a new contract on its behalf.
- Given repeated completion observations, money/score and morale are awarded once.
- Given an interruption, a continuous hold resets; given destroyed/missing targets, the mission ends without reward.
- Given a roof insertion, the landing must match the selected shell and marked area.
- Given a small MFD, controls and pager remain within the body and descriptions remain readable.
- Flight, listen-host, remote-client, late-join and scene-reset acceptance remain required.

## Verification

Release build (zero warnings), module/framework/architecture tests, PatchProbe, nomod test,
nomod assembly verification and whitespace checks passed. Unity 2022.3.62f3 rendered the
production MIS presenter at 596 and 896 px with fixture game data; the accept button
requested the correct ID, and the final render logged no text-overflow warnings.
The long briefing now scrolls inside its card. This fixture does not simulate mission AI,
networking, live map coordinates or actual jamming/air-assault execution.

Preview images use sample contract state, not an in-game session:
[available, compact](../ux/mission-preview/available-compact.png),
[active, tall](../ux/mission-preview/active-tall.png),
[primary objectives](../ux/mission-preview/objectives-compact.png),
[briefing](../ux/mission-preview/briefing-compact.png).
