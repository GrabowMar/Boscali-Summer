# Dynamic operations

Owns session-scoped secondary objectives, automatic faction awards, vanilla reward
spawns, and its read-only snapshot protocol. Tests mirror this folder.

Only the server generates, completes, or pays objectives. Clients request their own
faction's view, never nominate targets, rewards, or completion. Preserve authored
mission objectives and vanilla capture rules. Known hostile contacts only for target
selection. UI integrates through ISecondaryObjectivesView; reward vehicles and
defensive buildings use native spawners. Never import sibling implementations.

Hard limits: 8 faction boards, 3 cards per board, 128 issued objectives per faction
per mission, 64 airbases, 4096 units inspected per faction every 30 seconds, 64 player snapshots,
24 reward units. Reset all state and owned objects on mission/scene change.
