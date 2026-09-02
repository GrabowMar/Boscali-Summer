# Progression module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/Progression`. This module
turns the game's existing player rank into a bounded skill-point budget and owns selected
skills, entitlement snapshots, reward/fuel patches, and the Skills MFD page data.

Do not alter Nuclear Option's rank thresholds, aircraft unlocks, score, or reward source.
The host owns selections and derives the available point budget from vanilla `PlayerRank`.
Clients may request one skill unlock but never submit rank, points, multipliers, or masks.
