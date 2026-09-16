# Squad and ace hunts

This is the encounter design contract for the Squad system. Code and automated
checks establish implemented behavior; encounter pacing, pilot survival and flight
feel still require in-game validation. Friendly wing management remains Wing
Command's responsibility. Squad initially lists enemy wings and their ace leaders.

## Pilot and progression

Squad holds the player pilot record and the perk board formerly shown in OPS.
Pilot identities come from Wing Command's generator. F1 selects respawning pilots
(default) or one-life careers. Losing an aircraft or ejecting is not proof of pilot
death. Respawning preserves the current pilot; confirmed death in one-life mode
retires that identity and starts a successor's career. Native spawning, aircraft
unlocks, rank thresholds and vanilla mission score remain the game's responsibility.

Ace victories grant a separate, server-owned pick bonus. The score-pick ceiling
must not swallow this bonus: `earned = min(20, scorePicks + aceBonus)`. Score itself
pays five qualification grades across four lanes, and a career may hold two of the four
OPS tools. A leader defeat can award once per encounter;
escort destruction does not award an additional ace pick. The host validates player
participation and consumes the encounter's reward before publishing the result.
Duplicate disable callbacks, reconnects and repeated snapshots cannot award again.

## Encounter loop

1. After a 60-second mission grace, effective damage to hostile units builds the
   attacking player's threat. The default trigger is 25 native credited part-damage
   points after armor (not a fraction of an aircraft's overall health). It rises
   35% per credited ace defeat, capped at five times the initial trigger. Friendly fire,
   non-positive damage and
   untrusted client values cannot create threat; ordinary reward/score changes are
   not a substitute for a damage event.
2. One ace-led wing spawns to hunt that player's current aircraft. Its introduction
   shows the wing's symbol, name, ace identity, tier and strength, accompanied by
   original enemy chatter and a local soundtrack transition.
3. The wing prioritizes the marked aircraft using Wing Command's verified native
   flight/wing systems. The aircraft is fixed for this hunt. A respawn does not
   silently inherit pursuit.
4. Leader defeat resolves the ace challenge and can pay one bonus point. Death or
   ejection of the marked player, disconnect, faction change, mission end or the
   900-second timeout releases pursuit. Surviving aircraft return to ordinary AI
   behavior without chasing the successor. Spawned aircraft are still cleaned up at
   their original 900-second lifetime ceiling.
5. A 180-second cooldown follows resolution. Damage during the hunt or cooldown
   does not bank the next spawn. New damage after cooldown starts the next challenge.

Difficulty rises with defeated aces and returning rivals, capped at tier five.
Use native AI skill and wing strength, without granting extra weapon damage or
invulnerable aircraft. The initial wing has a leader and one escort; tiers three
and five each add one escort, up to four aircraft.

A downed ace may return only when the pilot survived, keeping the same identity
and symbol with a visible return count. Wing Command observes the native dismounted
pilot's death, capture or return; missing survival evidence cannot revive an ace.
Downed aircraft remain for a 30-second ejection grace before early cleanup. Eligible
rivals may return after 300 seconds, at most twice per identity, with a stronger tier.
Aircraft loss alone must not be described
as a confirmed pilot kill. Rival memory is bounded and mission scoped; cross-mission
profiles require stable non-zero Steam identities and are a separate persistence
decision. A returned encounter receives a new encounter ID so its later defeat can
grant one new point without reopening the prior reward.

## Bounds and presentation

For local testing, **F1 → Boscali Summer → Debug** exposes `HuntingWingTier`
(1–5) and **Spawn hunting wing**. It targets the host's current aircraft, bypasses
the damage/grace/cooldown trigger, and uses the normal hunt display, chatter, music
and kill rewards. Tier 1–2 spawns two aircraft, 3–4 three, and 5 four. Repeating the
action replaces that player's earlier debug wings; **Clear my debug wings** removes
them without kill rewards. Normal hunts and other players' wings are untouched.
The action requires an active mission, a living pilot, a hostile faction and enabled
ace hunts, and retains the four-wing cap. Remote clients cannot invoke it. Debug
aces do not enter the returning-rival pool. Buttons do not persist a spawn request.

- One active hunt per player; at most four owned wings and four aircraft per wing,
  including survivors released to normal AI. There are at most 64 player careers,
  32 history entries and eight hostile wings per snapshot; spawn retries wait 15 seconds.
- The host chooses faction, ace identity, spawn definition, skill and reward. Clients
  receive bounded summaries. Native Mirage owns aircraft replication.
- Hunt state must reach the targeted client with Squad closed, including late join.
  Introductions and chatter are deduplicated by encounter/event identity. Text and
  symbols carry status as well as color; enemy coordinates are not needed by the
  roster or announcement.
- Radio reads the local hunt summary. Existing validated local `Music/Hunt` OGG/WAV
  files take priority; otherwise use the installed map's tactical soundtrack.
  No audio files are downloaded, bundled, transmitted or extracted.
- The radio reuses its two music sources and existing crossfade/vanilla handoff. At
  hunt end it restores the previous station, track, position and playing/paused
  state, or restores native music when radio was off. A manual transport or station
  action overrides this automation for the rest of that hunt. Disabled radio and
  headless servers do not play hunt music.

## Failure-mode checklist

- Damage ownership, effective amount and hostility are validated before threat.
- Spawn failure cannot consume a reward, leave partial wings or create retry loops.
- Leader destruction, pilot death, despawn and ejection are distinct outcomes.
- A released wing cannot reacquire a respawned player through a stale target handle.
- Simultaneous deaths and repeated callbacks cannot pay twice or advance tiers twice.
- Faction changes, reused non-Steam player slots and scene changes cannot expose a
  predecessor's pilot career or preserve stale network/UI/music state.
- Empty/missing local music and soundtrack capabilities fail without interrupting
  encounters; a decode failure releases or restores audio ownership.
- Validate single-player, listen host, remote client, late join, player ejection,
  both life modes, manual music Stop and scene reload before making live-play claims.

Design review used the progression, economy and degenerate-strategy checks in
[Claude-Code-Game-Studios balance-check](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/balance-check/SKILL.md)
and the states/formulas/edge-case structure in its
[design-system skill](https://github.com/Donchitos/Claude-Code-Game-Studios/blob/main/.claude/skills/design-system/SKILL.md).

## Hunt HUD visual contract

The passive alert answers who is hunting the player, how experienced the ace is,
and how many aircraft remain. It uses a 704 by 184 reference-pixel dossier at the
upper center of a 1920 by 1080 scaling canvas, leaving the aiming area clear.
An amber warning header and diagonal hazard strip establish caution without flashing.
The callsign is the largest text; name, wing symbol and wing name are secondary.
A generated enemy-squadron crest sits framed at the right edge: shape and charge are
deterministic from the wing identity, and the palette stays in the caution/danger band.
The same crest marks that wing's card on the SQD WINGS page.
The portrait is borrowed from Wing Command's identity-based generator and cached for
that ace; missing art shows NO VISUAL. Scene reset releases the UI without destroying
Wing Command's sprite. Text is literal, single-line fields truncate with ellipses.

Three vector icons label combat proficiency (the host's skill tier description),
pursuit (HUNTER) and wing leadership (live/total aircraft). These are ratings and
roles, not invented additional perk effects. Five pips repeat the numeric tier;
returning aces show their return count. Status explicitly identifies YOU as the
primary target. Data refreshes at 4 Hz from the existing read-only squad view.
The HUD introduces no input handlers, raycasters or independent encounter timers. The panel
hides when the map opens, the hunt ends, or the local aircraft is lost/ejected.

Review uses the Game Studios `ux-review` HUD checklist and UI/UX Pro Max contrast
and color-independent-label guidance. Implementation checks cover bounded widgets,
missing portraits, literal/overflow text and teardown. In-game visual review remains
required at 1280x720, 1920x1080 and ultrawide, with long ace names and returning aces.

### Ace abilities and portrait correction

Enemy leaders now reuse Wing Command survival/combat hooks without entering the
recruitable pilot roster. Tier 1 grants Toughness (reduced seated-pilot damage),
tier 2 adds Countermeasures (faster dispenser bursts and stronger ECM), tier 3 adds
Notch Expert (radar guidance disruption when beaming), and tiers 4–5 add Ghost
(guided-missile disruption). Tier 5 still increases native AI skill and wing size;
there is no additional fifth perk. Returning aces keep lower-tier perks. Effects
respect Wing Command's PilotProgression and RankEffect settings and remain server-only.
Countermeasures require the aircraft's native dispenser/jammer equipment.

Four badges beside the callsign use shield, flare, turn and ghost marks, labelled
TOUGH / CM / NOTCH / GHOST. Only active host-confirmed perks are shown. No active
perks shows an explicit text state. The host sends a bounded four-bit mask through
Squad protocol 2; peers need matching builds. Wing Command 0.9.2.6 adds the public
AbilityMask API. Ace registrations are separate, capped, and removed on aircraft
cleanup and scene reset; released surviving aces retain their perks during normal AI.

Portrait aspect fitting now uses a centered pivot, preventing the left-aligned
image and uneven right-hand strip. Opaque neutral portrait/badge backplates replace
translucent amber fills, and the overlay canvas snaps to pixels.

### Edge ingress and alert minimization

Each new hunt displays the full dossier for ten unscaled seconds, then shrinks and
crossfades over 0.4 seconds to a compact callsign/live-strength bar. Map open/close
and snapshot refreshes do not restart an existing hunt's introduction. Scene reset
clears the introduction state; a new hunt ID expands the next alert.

Aircraft are exact by tier: T/A-30, CT-7, FS-12, FS-20, KR-67. Wing sizes are
2, 2, 3, 3, 4 respectively, including the ace. Missing aircraft/loadouts defer a
hunt instead of substituting a random cheaper plane. Aircraft must spawn as a
complete flyable flight; failures roll back the flight. Snapshot totals retain the
actual created count, including after the aircraft references are cleaned up.

Ingress samples 64 positions on the map perimeter, inset 2 km to contain the whole
formation and at least 9 km from the player. Territory is determined from current
live ground-airbase ownership: the nearest base must belong to the hunting faction,
with a 2 km distance advantage over neutral/other-faction bases. Carriers do not
claim ground. This is airbase influence, not a claim that the game supplies territorial
polygons. Global map coordinates are converted through the current datum. Terrain
clearance is checked per aircraft. No eligible edge means no spawn and a later retry.
Tests cover exact tiers, perimeter bounds, player clearance, captured/missing bases,
contested influence and invalid maps. Flight behavior still needs an in-game pass.

### Persistent hunt intelligence

The minimized bar is flush with the top edge. While a hunt is active, its owned
planes refresh the marked player's native faction tracking record on each one-second
director tick and target evaluation, and reassert the primary target. This is shared
faction intelligence, so other aircraft in that faction can also use the fresh track.
Weapon range, line of sight, ammunition and seeker constraints remain native. Refreshes
stop after pursuit release, target loss/ejection or cleanup; existing intelligence
then ages normally. The tracking helper never retargets the player's replacement.

### Nearest controlled edge (current)

Squad now queries Command's `ITerritoryIngress` service instead of Wing Command's
legacy nearest-airbase approximation. The map and ingress queries consume the same
`TacticalSectorGrid` evaluation, with objective troop pressure from actual unit positions
(independent of faction tracking), strategic nodes, contested control and control history. Fields are cached for at most eight factions;
reads refresh at most twice per second, examine at most 4096 units, and work without
opening/rendering the map. A gap in observations cannot fast-forward capture history.

Selection compares distance across all boundary-cell candidates and chooses the
closest eligible position. Centers are 1200 m inside the map edge, at least 9 km
from the player, with a 900 m formation footprint checked against control cells.
Neutral, contested and opposing cells reject the entire footprint. Control is read
from the hunting faction's perspective. The actual map edge can still be far away;
there is no interior or uncontrolled-territory fallback. If Command is unavailable,
the hunt waits. The companion `SpawnWingAt` API takes the host-selected global
coordinates, verifies map/clearance bounds and executes the existing native spawn.
