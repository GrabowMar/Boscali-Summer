# Fire and destruction module

Owns impact ignition, wildfire simulation, local impact scorch marks, ruins, its Mirage
messages/handlers, and all related visuals and Harmony patches.

Building occupancy is queried only via `Framework/Contracts/IBuildingOccupancy.cs`.

Preserve the full names and serializer order of the two `BoscaliSummer.Runtime` wire
messages (`FireIgnitedMessage`, `RuinCreatedMessage`); `BuildingDamagedMessage` was removed
in a deliberate protocol break when building damage became a local-only scorch mark. Keep
every documented queue, fire, scorch, smoke, ruin and light ceiling.

`ImpactFireManager` owns the bounded impact/vehicle/scorch queues and the fire-site
simulation — no per-impact allocations, no whole-scene searches. `FireVisualPool` is
flame-only; smoke is pooled smoke-only copies of the vanilla Fuel Depot destruction prefab
via `FuelDepotSmokePool`. `ImpactScorchManager` stamps one pooled vanilla scorch decal on a
wall where an explosive hit lands: local cosmetic only, no HP tracking, damage tiers,
per-building state or networking, and no facade tint or "battered" state — `MapBuilding` has
no vanilla damage shader. `RuinAftermathManager` keeps persistent logical records but
assigns smoke only to a camera-near bounded subset; collapse accents are particle-only, no
debris rigidbodies or colliders. Do not restore the removed helicopter optical-smoke
countermeasure.
