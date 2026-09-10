# Urban combat module

Owns eligible-shell discovery, occupancy, defensive-proxy spawning, capture/destruction
cleanup, visuals, and its Harmony patches.

Publish occupancy only through `IBuildingOccupancy`; keep `GarrisonOccupancy` as the private
marker implementation. Ordinary zone garrisons use invisible vanilla DEF proxies and the
cached per-scene shell catalogue. Air-assault outposts may use bounded visible vanilla
emplacements with cosmetic infantry clearly separated from combat logic. Do not add per-zone
scene scans, and do not claim the visual infantry has independent squad AI — the game has no
reusable infantry squad system, so describe this as occupied buildings / defensive
positions, not room-clearing infantry combat.
