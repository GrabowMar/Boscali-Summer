# Dynamic combat source review

Reviewed 2026-09-09 for the requested dynamic frontlines, secondary missions, and strategic
combat feel. These repositories provide implementation lessons only. No source, assets,
configuration formats, or complete features were copied, and neither mod was installed,
built, or executed. This document does not expand the runtime change scope.

## Pinned sources and provenance

| Repository | Reviewed revision on `main` | Commit date | License evidence |
|---|---|---|---|
| [lust-daddy](https://github.com/SonPamungkas/lust-daddy) | [`12ab26f5e53f789c0b5c10317808d75a8f787ff3`](https://github.com/SonPamungkas/lust-daddy/tree/12ab26f5e53f789c0b5c10317808d75a8f787ff3) | 2026-06-26 | No license file or source license notice found in the pinned tree; GitHub repository metadata returned `license: null`. |
| [NOMNOM-qol-combofix](https://github.com/SonPamungkas/NOMNOM-qol-combofix) | [`a8b5efc0aab4155393e38e04c3b3758b7c3f2eca`](https://github.com/SonPamungkas/NOMNOM-qol-combofix/tree/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca) | 2026-08-10 | No license file or source license notice found in the pinned tree; GitHub repository metadata returned `license: null`. |

Default-branch source was inspected; release binaries were not inspected. License absence
is recorded as a provenance fact, not inferred permission to reuse either implementation.

## LUST-DADDY

**Author documentation.** The README describes an editor for unit statistics, turrets, and
weapons, with JSON saved for later prefab modification. Its editor heading says F8 while
the usage instructions say F9. These are author claims, not proof of multiplayer behavior.
[Pinned README](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/README.md)

**Source observations.** `Plugin.cs` binds F9. `LustDaddyUI` has separate asset categories,
search fields, Lite/Full disclosure, and a cached catalogue initially scanned when opened.
The general lesson is to foreground the few values needed for a decision and retain
selection while details refresh.
[Plugin](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/Plugin.cs),
[UI](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/LustDaddyUI.cs)

`LustDaddyStartup` runs from an `Encyclopedia.AfterLoad` postfix. It catalogues non-scene
objects, checks requirements, orders configurations, and mutates prefab components and
fields. Missing assets are reported. `NeedsChecker` detects loaded mods and `PatchOrdering`
provides deterministic passes. Availability gates and predictable initialization are useful
patterns; arbitrary reflection edits, whole-resource searches, and the patch language are
outside Boscali's mission system.
[Startup](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/LustDaddyStartup.cs),
[Requirements](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/NeedsChecker.cs),
[Ordering](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/PatchOrdering.cs)

The plugin also suppresses `SetGlobalParticles.Start` exceptions through a finalizer;
Boscali should retain diagnostic failures instead. Its project has a post-build copy into
the game plugin directory, so this review did not build it.
[Plugin](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/Plugin.cs),
[Project](https://github.com/SonPamungkas/lust-daddy/blob/12ab26f5e53f789c0b5c10317808d75a8f787ff3/LustDaddy.csproj)

## NOMNOM QoL Combo Fix

**Author documentation.** The README describes gun trajectory changes, infrared missile
behavior, ship wake and carrier compatibility, and tailhooks. These are tactical and
compatibility features, not a dynamic mission director. The documented default infrared
off-boresight angle is 90 degrees; the pinned plugin configuration uses 45 degrees.
[Pinned README](https://github.com/SonPamungkas/NOMNOM-qol-combofix/blob/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca/README.md),
[Configuration](https://github.com/SonPamungkas/NOMNOM-qol-combofix/blob/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca/Plugin.cs)

**Source observations.** LOAL target searches are spaced 0.25 seconds apart, have a configured
search lifetime, and remove missile state on disabling. Each search nevertheless walks
`UnitRegistry.allUnits`, and its per-missile dictionaries have no explicit global capacity
limit. Throttling and cleanup are useful lessons; strategic processing also needs fixed
budgets and scene reset. This is not evidence that all those contacts should appear on a
player's map.
[LOAL implementation](https://github.com/SonPamungkas/NOMNOM-qol-combofix/blob/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca/IRplus/LOALPatch.cs)

The trajectory replacement has a 2,000-step ceiling and divergence exit. Catapult support
resolves optional mod types once. These illustrate bounded work and optional capabilities;
neither requires Boscali to replace ballistics or add carrier integrations.
[Gun calculations](https://github.com/SonPamungkas/NOMNOM-qol-combofix/blob/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca/GunControl/GunControlFix.cs),
[Catapult discovery](https://github.com/SonPamungkas/NOMNOM-qol-combofix/blob/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca/Aryx/AryxCatapultIntegration.cs)

`ShipAINavexFix` zeroes standoff distance in every patched `ShipAI.Awake` call; it does not
filter for Navex ships. The filename therefore understates its reach. Separately, the AI
tailhook patch explicitly excludes player aircraft. Scope checks must be in runtime code,
not inferred from labels.
[Ship AI patch](https://github.com/SonPamungkas/NOMNOM-qol-combofix/blob/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca/Aryx/ShipAINavexFix.cs),
[AI tailhooks](https://github.com/SonPamungkas/NOMNOM-qol-combofix/blob/a8b5efc0aab4155393e38e04c3b3758b7c3f2eca/Extra/AITailhookPatch.cs)

## Installed game verification

A read-only ECMA-335 metadata check inspected the installed
`NuclearOption_Data/Managed/Assembly-CSharp.dll`, SHA-256
`EB3B93BDAEC37DD7B3BAB72F801A2C84E5BE2AE3C559F39251E2320AE6B11CCC`.
It executed no game or external-mod code. The check confirmed the following method and
parameter names; this table is not a complete type/signature or runtime compatibility test.

| Locally present member | Relevance and limit |
|---|---|
| `Encyclopedia.AfterLoad()` and `AfterLoad(instance)` | A catalogue lifecycle seam exists. Does not verify external prefab mutations or load order. |
| `UnitRegistry.allUnits` | A registry exists. Does not make all enemy positions legitimate local knowledge. |
| `FactionHQ.GetTrackingData(id)` / `SetTrackingState(id, lastKnownPosition, lastSpottedTime)` | Tracking seams exist; player-facing targets still require faction-known information. |
| `CombatAI.AnalyzeTarget(weaponStation, analyzer, trackingInfo, armorTierOptimism, targetDistance, maxRangeMultiplier)` | Confirms the named target-scoring seam. Does not validate stacked patches or weapon changes. |
| `Spawner.SpawnVehicle(prefab, globalPosition, rotation, velocity, hq, uniqueName, skill, holdPosition, player)` | Native spawn entry exists. Metadata alone does not establish network authority, pathability, or working convoy orders. |
| `Spawner.SpawnBuilding(prefab, globalPosition, rotation, HQ, airbase, uniqueName, capturable, factoryOptions)` | Native building entry exists; terrain, ownership, spawning, and replication need separate validation. |
| `FactionHQ.RewardPlayer(player, target, rewardAllocation, rewardScore, missionType)` | Reward entry exists. Its actual money/score rules and idempotency require backend verification. |
| `IRSeeker.Seek()`, `Missile.UnitDisabled(oldState, newState)` | Confirms names cited in external source, not missile behavior. |
| `ShipAI.Awake`, `ShipAI.standoffDistance`, `Airbase.GetRadius()` | Confirms cited compatibility seams only. No ship or carrier changes are proposed here. |

No external-mod coexistence, flight test, listen-host test, remote-client test, late join,
or scene transition was performed by this research pass. Runtime claims must come from the
implementation's own installed-game inspection, build/probe results, and gameplay checks.

## Independent conclusions for Boscali

These are design inferences for the authorized frontline/secondary-mission work, not claims
that the external projects implement them or that every suggestion has shipped.

1. **Give battlefield actions a visible consequence.** A named frontline objective should
   explain its condition and offered reward. On completion, report what actually happened:
   funds/XP awarded, reinforcement dispatched, or fortification unavailable. An offered
   unique reward must not be presented as deployed until the host accepts its execution.
2. **Use force roles instead of stat inflation.** A bounded group of existing ground assets
   or defensive positions can make a mission matter without new turret hybrids, missile
   behavior, universal aim bonuses, or a second unit editor. Catalogue only supported native
   content and reject unavailable rewards honestly.
3. **Keep the map and objective panel accountable to the same battle.** Show faction-known
   targets and stable control information. Avoid rapid frontline oscillation from transient
   contacts; do not infer enemy positions from unrestricted world transforms. Objective
   deadlines and server completion state should remain separate from visual progress.
4. **Make mission browsing stable.** Preserve the selected tab and secondary page during
   refresh. Show the requirement, named target, progress, deadline, reward, and explicit
   terminal state in a fixed card hierarchy. Refresh labels in place and retain authored
   mission objectives and briefing.
5. **Bound every strategic loop.** Use slow authoritative ticks, capped candidates and active
   missions, capped spawned rewards, expiry, exactly-once payout, and scene-owned cleanup.
   Per-object throttling by itself does not impose a total workload ceiling.
6. **Preserve module and mod independence.** Resolve optional services through narrow
   contracts. Missing content or a disabled provider should explain the unavailable action
   while unrelated operations remain usable. Do not add a hard dependency on either source
   project or Wing Command.

## Non-goals and further gates

This review does not authorize copying a weapon editor, patch language, LOAL system,
ballistics replacement, tailhook/carrier integration, or any entire external feature.
It does not propose rewriting player aircraft controls, assigning wing orders, globally
changing ship engagement distance, or suppressing game exceptions.

A persistent strategic campaign, simulated supply network, custom reward unit variants,
and faction-wide AI commander are additional designs, not prerequisites for the requested
simple mission system. Add them only with their own scope, authority model, performance
ceilings, and local compatibility evidence. A convoy claim in the current implementation
still requires verified native movement and spawn behavior; the existence of a spawner
method alone is insufficient.
