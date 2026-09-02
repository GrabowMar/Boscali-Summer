# Support module

Scope edits to this folder and `tests/BoscaliSummer.Tests/Features/Support`. This module owns
support UI, request validation, allocation costs, cooldowns, jobs, vanilla spawn selection,
network messages, and cleanup.

The server derives faction, cost, yield, definition, entitlement, and limits. Keep requests
idempotent and bounded. Prefer vanilla network spawning and effects. Fortification crosses
into Urban Combat only through `IZoneFortificationService`; never import its implementation.
