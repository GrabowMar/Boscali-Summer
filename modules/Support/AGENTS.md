# Support module

Owns the OPS MFD screen — the perk board (PASSIVE, AUTH), the support call-in page and the
career RECORD — plus request validation, allocation costs, cooldowns, jobs, vanilla spawn
selection, network messages, and cleanup.

OPS is about the player. The theater picture belongs to the STR screen and reaches OPS
through nothing at all; the two share only the `AvScreen` shell factory, which is a widget,
not a seam. Do not host another module's page here again.

Adding an action is one `SupportCatalog` row, one `ISupportAction` file, and one perk row in
Progression that grants its capability. Do not add a `switch` over `SupportActionId` back
into the manager — it owns authority, economy and bounded concurrency only. An action
reaches the feature exclusively through `ISupportHost` (bounded pools, coroutines, the
vehicle cap, settings, logging). An action whose game capability cannot be resolved is left
out of the catalogue rather than rendered and failed at request time.

The server derives faction, cost, yield, definition, authorisation and limits; costs for
spawning actions come from vanilla `UnitDefinition.value`, not hand-picked constants. Keep
requests idempotent and bounded, and remember only accepted ids so a denial never burns an
id the client would retry with. Prefer vanilla network spawning and effects. Fortification
crosses into Urban Combat only through `IZoneFortificationService` — never import its
implementation, and charge only when it returns true.

The panel may not render a state it has not verified. It reads the perk board through
`IProgressionView` and never imports the Progression namespace.

## Observation handoff

- QoL owns local camera/HUD behaviour and observation marks. Consume only its Framework
  contracts; do not import its manager or camera implementation.
- A ground ray hit is an observation point, not a laser lock, tracked enemy, or authorised
  support order. Store persistent points in GlobalPosition, convert for rendering, and pass
  any later support intent through the existing server validation and economy.
- Correlate delayed spawn work to its actual weapon/request and scene generation, with hard
  capacity plus expiry. Never identify it as just the next missile from an aircraft.
