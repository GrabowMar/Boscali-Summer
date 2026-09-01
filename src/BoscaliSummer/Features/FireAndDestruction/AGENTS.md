# Fire and destruction module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/FireAndDestruction`.
This module owns impact ignition, wildfire simulation, local impact scorch marks, ruins,
its Mirage messages/handlers, and all related visuals and Harmony patches.

Do not edit Radio or Urban Combat implementations. Building occupancy is queried only via
`Framework/Contracts/IBuildingOccupancy.cs`. Preserve the full names and serializer order
of the two `BoscaliSummer.Runtime` wire messages (`FireIgnitedMessage`, `RuinCreatedMessage`);
`BuildingDamagedMessage` was removed in a deliberate protocol break. Preserve all documented
queue, fire, scorch, smoke, ruin, and light ceilings.
