# Progression module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/Progression`. This module
turns the player's mission score into a bounded perk-point budget and owns selected perks,
capability lookups, reward/fuel patches, and the perk board's view data.

Do not alter Nuclear Option's rank thresholds, aircraft unlocks, score, or reward source.
The host owns selections and derives the point budget from vanilla `Player.PlayerScore`;
rank is read for display only. Clients may request one perk unlock but never submit score,
points, multipliers, or masks.

`PerkCatalog` is flat: no prerequisites, no tiers. A perk either scales one `PerkEffect` or
grants one capability string, never both, and `PerkCatalog.All[i].Id == i`. Every capability
in `SupportCapabilities` must be granted by exactly one perk — `ProgressionTests` enforces
that, and it is the only thing keeping this catalogue and `SupportCatalog` in step. Keep the
catalogue pure: it is compiled into the test project directly.

`ProgressionRuntime.Active` exists solely because Harmony patches are static. Nothing else
may use it; cross-feature access goes through `IPlayerPerks` in the service registry.
