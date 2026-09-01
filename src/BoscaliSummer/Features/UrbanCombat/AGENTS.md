# Urban combat module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/UrbanCombat`. This module
owns eligible-shell discovery, occupancy, defensive proxy spawning, capture/destruction
cleanup, visuals, and its Harmony patches.

Do not edit Fire and Destruction or Radio implementations. Publish occupancy only through
`IBuildingOccupancy`; keep `GarrisonOccupancy` as the private marker implementation. Use
invisible vanilla DEF proxies and the cached per-scene shell catalogue. Do not add rooftop
bunker geometry, per-zone scene scans, or unsupported infantry claims.
