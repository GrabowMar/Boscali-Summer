# Dynamic operations module

Owns session-scoped secondary objectives, automatic faction awards, vanilla reward spawns,
and its read-only snapshot protocol. Experimental, default-off.

Only the server generates, completes, or pays objectives. Clients request their own
faction's view and never nominate targets, rewards, or completion. Preserve authored mission
objectives and vanilla capture rules. Use known hostile contacts only for target selection.
UI integrates through `ISecondaryObjectivesView`; reward vehicles and defensive buildings use
native spawners.

Hard limits: 8 faction boards, 3 cards per board, 128 issued objectives per faction per
mission, 64 airbases, 4096 units inspected per faction every 30 s, 64 player snapshots, 24
reward units. Reset all state and owned objects on mission/scene change.
