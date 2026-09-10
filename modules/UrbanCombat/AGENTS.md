# Urban combat module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/UrbanCombat`. This module
owns eligible-shell discovery, occupancy, defensive proxy spawning, capture/destruction
cleanup, visuals, and its Harmony patches.

Do not edit Fire and Destruction or Radio implementations. Publish occupancy only through
`IBuildingOccupancy`; keep `GarrisonOccupancy` as the private marker implementation. Use
invisible vanilla DEF proxies and the cached per-scene shell catalogue for ordinary zone
garrisons. Air-assault outposts may use bounded visible vanilla emplacements with cosmetic
infantry clearly separated from combat logic. Do not add per-zone scene scans or claim the
visual infantry has independent squad AI.
