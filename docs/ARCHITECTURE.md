# Boscali Summer 0.1.1 architecture

The plugin owns one persistent runtime object. Feature managers use bounded queues and slow ticks; no feature scans the whole scene every frame.

## Authority and replication

- Garrison spawning continues through Nuclear Option's normal server authority.
- Only the server rolls ignition chances. Successful fire, lightweight-building damage, and permanent ruin transitions use small reliable Mirage messages.
- A joining player receives two delayed snapshots after authentication so mission-scene loading cannot swallow persistent visual state.
- Spawned garrison buildings use vanilla Mirage spawning and need no custom position updates.

## Performance boundaries

- 256 queued impacts, processed eight per frame.
- 24 active fire sites by default; nearby impacts merge while generated forest children remain visible front sections.
- 32 queued ground-vehicle destruction events, with at most one nearby-building query processed per frame.
- One blast-map scorch request per frame.
- Three dynamic fire lights globally.
- 256 persistent logical ruin records, 24 nearest smoke visuals, and four simultaneous collapse bursts by default.
- Ruin smoke switches from a dense hot phase to lower-rate intermittent smouldering but remains logically present for the mission.
- Collapse dust is pooled particle rendering only; it creates no Rigidbody debris, colliders, or per-ruin update component.
- Fire scorch requests use the vanilla BlastManager gray ash map with a configurable 0.72 radius scale by default.
- Two spread attempts per forest site and at most the configured generation depth; all children share the global fire-site cap.
- Procedural tree data is indexed once per scene. Exact hit tests search only the nine neighboring CPU cells.
- Airbase building scans occur only after initial ownership or capture, one zone per frame. Missing scene dependencies or late-loaded town shells receive a bounded retry.
- Empty rural capture circles receive one bounded 2.5 km fallback search for the nearest settlement.

## Compatibility behavior

Reflection is resolved once and reported at startup. Optional Harmony patches use `Prepare` checks where their target can move. A missing defensive building definition disables only garrisons. Fire visuals build bounded emitters from materials discovered under vanilla `DamageParticles`; this prevents inherited explosion bursts, sparks, and debris. If no suitable materials exist, the module falls back to the pooled global smoke system.

The startup log lists installed patches, resolved capabilities, and loaded `DEF` building definitions. This is the first place to look after a game update.
