# Support module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/Support`. This module owns
the OPS MFD page, request validation, allocation costs, cooldowns, jobs, vanilla spawn
selection, network messages, and cleanup.

Adding an action is one `SupportCatalog` row, one `ISupportAction` file, and one perk row in
Progression that grants its capability. Do not add a `switch` over `SupportActionId` back
into the manager: it owns authority, economy and bounded concurrency only. An action reaches
the feature exclusively through `ISupportHost` — bounded pools, coroutines, the vehicle cap,
settings and logging. Actions whose game capability cannot be resolved are left out of the
catalogue rather than rendered and failed at request time.

The server derives faction, cost, yield, definition, authorisation, and limits; costs for
spawning actions come from vanilla `UnitDefinition.value`, not hand-picked constants. Keep
requests idempotent and bounded, and remember only accepted ids so a denial never burns an
id the client would retry with. Prefer vanilla network spawning and effects. Fortification
crosses into Urban Combat only through `IZoneFortificationService`; never import its
implementation, and charge only when it returns true.

The panel may not render a state it has not verified. It reads the perk board through
`IProgressionView` and never imports the Progression namespace — the architecture test
rejects that.
