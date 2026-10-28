# Changelog

## 0.2.11 — stabilisation

Feature freeze: this round fixes, trims and tests what exists, and adds nothing new.

- **WMC on Nuclear Option 0.34.2** ([#30](https://github.com/GrabowMar/Boscali-Summer/issues/30)): 0.34.3 renamed the map
  icon's colour method, and on 0.34.2 the missing name stopped the whole Wing module from loading. The patch now takes
  whichever method the running game has.
- **NOMM no longer loses the mod**: the radio library and event pictures live in `BepInEx/config/BoscaliSummer` (moved
  over once from `plugins` on first start), so the mod never creates anything inside `BepInEx/plugins`. A `Music` /
  `Events` folder re-created there while the game ran became a second "BoscaliSummer" entry for NOMM, which then
  deleted the real install. The release zip no longer ships a `Music` folder.
- **Interaction menu key**: rebind it (and the chaff key) under CONTROLS on the mission prompt or SET > THIS PILOT >
  COCKPIT > KEYS: click, press a key, Esc keeps, Backspace clears. A note warns when the key is also one of the game's
  own controls (for example a camera switch on C), because a press then does both.
- **Fixes from the logs**: fuel-depot smoke no longer logs a Unity error each time it removes a light; an OPS command
  is no longer sent to the host before the host's own player exists; tail paint no longer reads a texture from
  materials that don't have one; missions with no objectives no longer flood the log with "Mission Runner was null".
- **Cleanup**: about 6,900 lines of dead, duplicated and over-built code removed with no feature removed (the comms
  client drops the remains of the retired polls, games and stickers; the TAC BOARD drops its unused page whitelist).
  Both builds compile with no warnings.
- **Licence**: the mod's own code is MIT again. Bundled third-party parts keep their licences (see the README).
- **Contributing**: new CONTRIBUTING.md, and a README rewritten around stabilisation, troubleshooting and a FAQ.

## 0.2.10 — update

- **Render scale** ([#31](https://github.com/GrabowMar/Boscali-Summer/issues/31)): `Performance > RenderScale`
  (50–100 %, default 80 %) sets the share RENDER SCALE renders at, and a change applies at once.
- **REMORA** ([#28](https://github.com/GrabowMar/Boscali-Summer/issues/28)): a fired round keeps its claim on the
  missile until the missile dies, so a drone returning to escort no longer frees that missile for the next drone.

- **Combat adjustments** (new module, on by default; every rule has its own switch on SET SERVER, same rules for
  both sides): radars and aircraft still share what they see at once, but ground and ship lookouts report distant
  contacts only now and then; a contact nobody is reporting drifts on the map and in the AI's picture, then leaves
  it (aircraft after 1 minute, ground and ships after 3); about 8 % of guided missiles fail on their own and flares,
  chaff and jamming are 1.5× as strong; armoured vehicles pop smoke at laser, TV and IR guided weapons; ships fire
  decoy salvos at incoming missiles; ground radars switch off when an anti-radiation missile comes for them. With
  another combat overhaul installed, the sharing, drift and missile rules leave the job to it.
- **OPS, simpler**: click a perk on the PERKS tab to arm it (the strip then offers FIRE; a map right-click still fires
  at once). The quick-slot buttons are gone from the page (their hotkeys still work).
- **OPS front windows**: the rail keeps the three postures and drops the funding-split nudges and the focus pin;
  each programme has one CHIP IN button (up to 25 AP, never more than it still needs).
- **SHADOW**: a mission button sends the order straight away when the team and target are ready; otherwise it
  drafts the mission and the form names what is missing.
- **OPS maps first**: the front window is wider and every extra pixel goes to the room, so the ORBIT, NETWORK and SHADOW
  maps are larger. The logs are gone (radio log, station log, session log, signal log, standing orders, the event line
  on the FRONTS cards), and the SHADOW map no longer carries the held/footprint/key notes; only the QRF note stays.
- **Satellite camera**: ENLARGE gives the camera the whole room, with MARK and TRANSMIT in its toolbar (click a bracket
  to pick a track). SHRINK or Esc docks it again.
- **OPS guidance**: each room's footer now says how to use it, in the order of the clicks. An empty track file or
  target list says why and what to do next (an SOF front with no camp points at the CAMP programme). The perk list on
  the rail is easier to read.
- **TASKED**: click a posted package to arm it, then FIRE (the same two steps as a perk).
- **Less noise**: the FRONTS cards drop the funding share and the lock detail; the NETWORK room drops the invented
  numbers (open ports, IP addresses, round-trip times and the hex dump) so everything shown is real game state.
- **OPS feedback**: every programme says what it does (READINESS names the perks it unlocks); hovering a
  posture tells you what it does before you pick it; a sent command shows SENDING … WAITING FOR THE HOST in the
  footer until the answer comes back.
- **OPS side column, streamlined**: superiority reads as one figure (+34 AHEAD) instead of a five-word scale;
  the strategy lock moved into its header; queued programmes are no longer listed twice; the active programme
  lost its repeated numbers line; the perks list says where perks are used (OPS page or C menu).
- **OPS milestones in the cockpit**: when a front programme finishes, fails or an asset is lost, the cockpit line
  says so once with a chime; READINESS UP names the perks it just unlocked. Hovering the superiority bar says what
  raises it on that front and what being ahead gives you.

## 0.2.9 — update

- **World effects**: contrails form behind aircraft at altitude, low passes over land kick up a dust wake, a fast
  close pass gives a sharp crack, the canopy muffles outside sound, and parked AI aircraft sit quiet.
- **Cockpit sound**: new softer RWR tones and missile alarm; switch clicks and the RPM and vortex-ring cues are gentler.
- **Night**: nights are darker and bluer, scaled by the moon (a full moon still lights the ground). The cockpit has
  night lighting. Night vision goggles now cover the eyes as they flip down, open from the middle while the tube
  warms, and show a few scintillating specks.
- **VANGUARD new stores**: SRB-2 TORCH (strap-on booster), HEL-60 SUNSPEAR (pylon laser), PCU-9 STORMCELL (capacitor
  pod), ASQ-X WATCHTOWER (recon pod) and XQ-90 MANTA (a large two-slot wing drone). AI aircraft do not pick them in
  random loadouts.
- **VANGUARD drones**: SEND WHERE I LOOK and SEND TO TARGET; orders stay with each drone, and a right-click on the
  feed sends the drone there.
- **STR map**: front lines draw as striped bands with teeth, offensives show as arrows, and you can draw your own
  offensive.
- **Rod impact**: no more black cloud hanging over the crater; the crater gets soot marks and a new impact sound.
- **Performance**: building damage culls far debris and pools pieces; clouds blend their detail levels between 27
  and 45 km and no longer flicker on the horizon.

## 0.2.8 — update

- **Target-selection smearing after mission restart** ([#22](https://github.com/GrabowMar/Boscali-Summer/issues/22)):
  integrated [qweaeqew's Feed Stack Fix](https://github.com/qweaeqew/Boscali-Summer-Feed-Stack-Fix).
  Missile and drone cameras now leave valid post-processing stacks for URP to reuse, with a recovery
  guard for disposed cached stacks, so the next target camera no longer breaks. Thanks to qweaeqew for
  the fix. The separate fix plugin is no longer needed; remove it when updating.
- **VANGUARD economy**: shooting down a VANGUARD decoy or drone now pays like a vanilla missile kill. MALD-J used to
  pay about three fighter kills. Reusable drones that kill several targets a sortie now cost about what that many
  vanilla missiles would: REMORA 2.4 M, SPECTRE 2.8 M, SENTINEL 2.0 M, HAMILCAR 2.0 M, KESTREL 1.2 M, ORCA 1.8 M,
  MALD-J 0.8 M, MALD-X 0.4 M, AEGIS dart 0.2 M, ALE-X 0.1 M. LANCE slugs cost 0.08 M and SPECTRE shells are at the
  vanilla 30 mm price.
- **MISC screen**: a new last key on the map's button rail, with its own DEV ACCESS page. It carries a plain warning:
  the tools behind it are cheating if you use them outside testing. ACCEPT unlocks them until the game closes
  (nothing is saved, so it asks again next launch); DENY keeps them locked. Once unlocked it shows **ARES**, a host-only
  game-master console (spawn aircraft, ground units, ships, buildings or whole kits for any side with map clicks; act on
  selected units; strikes, weather, the clock and free OPS casts; fire and levelling; god mode, infinite fuel and ammo and
  more), and **DEBUG** (logging and developer tools). The rail keeps its SITUATION / TASKING / COMMS / SYSTEM / AUX labels
  with the extra key, runs to the bottom of the screen with MISC pinned there, and UAV reads DRONE LINK under TASKING.
- **SET maintenance**: THIS PILOT → PERF now offers confirmed CLEAR CACHE and FACTORY RESET actions.
  Cache clearing removes generated Boscali files and unused renderer stacks. Factory reset backs up the
  config and restores defaults while keeping progress and saved presets; restart the game afterwards.
  Factory reset is locked while connected to another host.
- **Trench generation** ([#26](https://github.com/GrabowMar/Boscali-Summer/issues/26)): ditches and wire now lie on
  the ground on slopes. They used to rise into tall fins and hang wire in the air wherever the hill was steeper than
  digging allows. Lines no longer jog sideways in square steps either: a position moves at most one depth band
  between neighbouring stations and breaks the line instead of jumping.
- **TAC BOARD SYSTEMS** ([#23](https://github.com/GrabowMar/Boscali-Summer/issues/23)): SET → COCKPIT → TAC BOARD
  PARTS has a SYSTEMS BLOCK switch. Its warnings still reach the lamps and notices when it is hidden.
- **Map industry markers** ([#24](https://github.com/GrabowMar/Boscali-Summer/issues/24)): SET → MAP has an
  INDUSTRY MARKS switch for the squares, diamonds and triangles on factories, depots and ammo dumps. It is the
  same switch as the MAP screen's INDUSTRY layer.
- **COM / UAV / EVN buttons** ([#25](https://github.com/GrabowMar/Boscali-Summer/issues/25)): on the classic map
  layout their labels use the stock buttons' size on one line, instead of large letters broken over two lines.

## 0.2.7 — update

- **Campaign, chapter 1 — Twelve Miles Out**: the old scripted missions are gone and a new campaign
  mission takes their place. A PMC convoy escort in the middle of the PALA coup, told through animated news screens,
  voiced radio traffic and mission hint cues. A new Pirates side joins the
  PMC, and the script can set factions at peace with each other.
- **VANGUARD drone steering**: TAKE CONTROL now holds the linked drone's course and height instead of
  chasing your nose. MATCH MY NOSE hands it your heading and height, TURN LEFT / RIGHT 45° steer it
  while hand-flown, and the UAV terminal lets you click a point to steer to and queue orders. The
  terminal window is solid again, and the missile and target cards in the UAV screen share its style.
  Needs everyone on the same version.
- **OPS rooms**: the map rooms use the game's own 10 km plate grid. Arming and picking is steadier: an
  arm lasts 30 seconds, a bounce no longer drops it, and one click makes one pick.
- **HUD**: the NAV DATA block (way home, target, fuel endurance, mission clock) is removed from the
  cockpit view and from the TAC BOARD and C menu switches, along with its setting.
- **Cleanup**: the build runs with zero warnings and 34 unused declarations are gone.

## 0.2.6.2 — hotfix

- **Screen smearing after a mission restart fixed** ([#22](https://github.com/GrabowMar/Boscali-Summer/issues/22)):
  after restarting a mission, selecting a target could freeze the picture into ghosted, overlapping
  frames. The missile and drone feed cameras now hand the game's post-processing back cleanly when a
  mission ends, so the target camera no longer trips over them.

## 0.2.6 — spooky update

- **OPS works both ways**: every OPS room now lets you see and answer what the enemy does to you.
  CYBER: when an enemy holds one of our radars, SAM nets, relays, uplinks or data centers, HQ calls it
  out, the NETWORK room shows it under INFOCON, and PURGE (4 CR) cuts every enemy hold 20 s later;
  the intruder sees PURGE INCOMING on that node and can burn it first, and each cut intrusion's trace
  jumps 25 %. SOF: enemy teams near our camps, airbases, uplinks or EW sites are spotted and called
  out, and SWEEP (4 CR) sends a QRF that hunts them for a minute (enough to pin a lone team; cover,
  push or exfil saves it). SPACE: every orbital rod and STARFALL barrage is now announced to its
  targets, an ASAT first puts a launcher on the pad for 60 s that the target side can see and kill
  to stop the strike, and a satellite that relocates after the launch (or any satellite while SPACE
  stands on DEFEND) dodges it. ZERO-DAY and a new forward base are announced too. The WATCH OFFICER
  and AI factions defend with the same tools, and AI factions now build ASATs.
  Needs everyone on the same version (protocol 42).

- **Something is on the air this season.** We are not saying more. Happy Spooktober.
- **A living sea**: the ocean now has real waves that follow the wind and build under storms:
  long swell on a calm day, choppy wave rows and whitecaps in a fresh breeze, steep grey seas,
  foam streaks and breaking crests in a gale, and surf along the shallows. The sea picks up the
  storm you are flying under, so it roughens as a squall line arrives and calms behind it, and
  drifting gust patches keep it from looking uniform. Sun glitter widens as the water roughens,
  cloud and aircraft shadows fall on the waves, and ships sit in calm water instead of being cut
  by crests. Jets passing low and fast over the water throw spray up behind them and leave a
  foaming wash. Both are client-local, under SET › SEA (OCEAN WAVES, SEA SPRAY); off restores the
  game's own water.

- **Trenches look and fight better**: full trench detail (fire bays, wire, roofs, saps) now
  shows wherever you are near the line, not only near its middle, so walking or flying along a
  long position no longer drops to the plain far ridge halfway down it. The earthwork takes the
  colour of the local ground, the fire-side wall is revetted with planks and posts, dry grass
  creeps over the outer slopes, the wire strands show from every angle, and distant trenches no
  longer shimmer. Positions now keep digging all session: when the front moves and old lines
  are lost, new ones are dug instead of the map going empty. Nests spread along the whole line
  instead of crowding one end, hot positions fill out to their full garrison, a line cut off by
  the front comes back to life if the front swings back over its surviving nests, destroyed
  sandbagged buildings stay destroyed, and players who take a long time to load still see
  existing trench lines. A new position no longer gets dug along most of an existing friendly
  one, a hit that kills no nest pauses digging for 20 seconds instead of a full minute (a lost
  nest still stops it for a minute), the end of a long front is no longer skipped, and a sap
  dug on steep ground no longer dips to sea level for other players.
- **VANGUARD drone link**: a UAV TERMINAL in the spirit of Arma's drone terminal lists every VANGUARD drone you have
  in the air (MALD, REMORA, HAWC-X, GLAIVE, KESTREL, HAMILCAR) with its live camera feed, a north-up tactical plot and
  an asset card. Link any drone, TAKE CONTROL to fly it by hand (it goes the way your nose points while you keep flying),
  click the plot to send it to a point it circles, or put it BACK ON TASK. NEXT and PREV switch drones and hand the stick
  over. In flight the same controls live under C › WEAPONS › DRONE LINK, the linked drone's feed rides on a HUD card
  (EO or white-hot IR, zoom, analogue static that thickens with range, a signal-lost freeze), and a new UAV screen on the
  MFD shows the feed and the roster. The link reaches 40 km; past that the drone goes back to its own job.
  The feed is a proper sensor ball: hand-flown it looks ahead, on its own job it stares down, on a waypoint it watches
  the point and with a DESIGNATE it tracks and boxes the target. Its overlay carries heading, altitude and speed tapes,
  the horizon, a gimbal box, the sensor point's slant range and grid, REMORA rounds and flares, link latency and a frame
  counter; clouds and rain shafts now show in the drone and missile feeds. The UAV MFD adds a TELEMETRY row.
- **Faster REMORA**: the escort drone has twice the thrust, so it keeps up with you in afterburner and runs its
  targets down sooner.
- **Wing taxi fix**: a wingman that rolls just past its hold-short or stand now stops there and
  carries on, instead of circling back for the exact spot and getting stuck off the pavement
  (seen at highway strips, where wingmen could not reach the runway). A wingman leaving a hangar
  whose way out crosses a taxi road close to the door now turns onto the road there, instead of
  driving past it and looping back across the verge.
- **Manual TAC BOARD pages**: choose each HUD page from C-menu or SET and show up to four,
  two across and two down (WIDE fills a row first, STACK a column), so the board no longer runs
  across the screen. SYSTEMS is not a page choice any more: it always shows under the lamp line.
  A page with nothing to show draws its own idle face (an empty radar-warning scope reads CLEAR,
  WING says NO WINGMEN) instead of a generic standby; WING shows your wing whenever you have one
  and MISSILE CAM follows your newest missile from the cockpit. Events never replace the selected
  pages and alerts continue independently. Board Parts has six major section switches, with page
  count, layout, declutter, size, shade and visor sway under Display Options.

- **Countermeasures you can read**: the EXPENDABLES rows now run in the order the countermeasure
  key steps through them, the jammer included, and only the selected one carries a caret and SEL.
  Before, a selected jammer showed no mark at all. Rounds and charge are clean bars in one column.

- **CARGO drop computer**: the airdrop page is rebuilt around a heading-up drop plot (drop zone,
  release reach ring, steer cue), a RED / AMBER / GREEN jump light with the next action or the
  seconds until the zone is in reach, the stick drawn as fireteams, a release checklist (load,
  safety, drop zone, reach) and every load aboard by station.

- **Cinematic cameras**: scripts can author heading-level chase shots, fixed-vantage tracking
  and flybys, with off-centre subject framing that respects the camera's aspect and lens.
  Camera motion now reports real velocity during slow playback and held actor-follow shots.

- **Left HUD and dialogue**: mission dialogue joins COMMS with its speaker and organization.
  The event log, COMMS and status flashes share a left inset, native type and thin frames;
  wrapped radio traffic pushes flashes down, and smaller regular status text keeps adjacent
  messages clear. Scripted readouts pause with the mission and clear when it ends.
  A single barely visible shadow now backs the whole left column; brighter log copy and
  slightly larger COMMS text replace the separate dark clouds behind each section.

## 0.2.5 — update

- **Near-future helmet display**: the cockpit visor is a curved HMD glass with an electrochromic sun
  tint, breath fog, a faint coating that shifts hue toward the rim, and hairline scratches drawn at
  screen resolution that catch the sun only where it lines up, and the brow shade above 6 G. There is
  no helmet frame or outline. Sun glare is a tight core, a short halo and eight soft rays, with no
  full-screen veil. Canopy reflections are remade with a Fresnel rim, a sun glint and a faint night
  glow. New config-file settings: HelmetCurvature and HelmetReflection (IMMERSION section). The TAC board stays flat.

- **HALO jump flares**: orange-white burns and short spark trails sit on both ankle canisters
  during the wingsuit glide, with warm smoke near the jumper. They extinguish at canopy
  deployment; troop movement, landing outcomes and countermeasures are unchanged.
- **Landing zones**: right-click the big map to SET, MOVE or CLEAR a landing zone (parachute
  marker). While set, your HALO paratrooper sticks and Vanguard gliders (HAWC-X, KESTREL,
  HAMILCAR) aim at the zone instead of their designated target; moving it never diverts a
  stick or glider already airborne, and a respawn starts clean. Green smoke marks the
  touchdown once a landing is expected (first HALO canopy, HAMILCAR on Final), and a HUD
  cue card (HALO/HAMILCAR ▸ LZ range, DROP/RELEASE NOW in green, OUT OF GLIDE in amber)
  shows while you carry a load for the zone.

- **Chaff visuals**: dispenser bursts and MALD-X ribbons scatter denser, brighter folded silver fibres
  with drag, tumbling sunlight reflections and a soft fade, replacing the expanding smoke puffs.
  Countermeasure timing, ammunition, radar behavior and multiplayer authority are unchanged.
- **Wing departures**: taxi guidance keeps to nearby route segments, checks the actual hold-short and
  lineup positions, and recovers an overshot slot before starting the takeoff roll. South Boscali uses
  the runway end reached by its taxi roads; fixed-wing launches skip remote service points at highway
  strips while helicopters retain those launch positions.
- **Wing airport sweep**: keep curved taxi paths for queued launches, reject disconnected service
  routes, and give runway-entry priority to the physical convoy leader. Pull-aside recovery restores
  its route correctly; a stuck queue can tow to its assigned clear runway slot under the normal
  departure clearance, including landing and occupied-runway checks after the group takes its lock.

## 0.2.4 — update

- **Classic corner map by default**: fresh configs use the game's own corner map, with the holo map
  and terrain ghost off; switching the holo map off now sticks. Opted-in holo maps draw visible air
  contacts ahead of ground clutter.
- **No more flicker with the expanded layout off**: the stock layout no longer redraws its panels
  every frame.
- **Infantry emplacements**: squad encampments are placed on clear ground and no longer spawn inside
  a helicopter.
- **Radar trucks join EW**: new friendly radar trucks are enrolled in the electronic-warfare network,
  so the truck count can grow.
- **Free Flight stays clear**: the native Free Flight mission gets no trenches and no occupied
  buildings.
- **EVENT PICTURES switch**: SET > CLIENT > PERFORMANCE > WORLD can turn event pictures off and show
  generic symbols instead.
- **OPS TASKED tab**: claim posted strikes and steer the cruise missile; the CRUISE perk now flies
  the game's own cruise missile, so steering works.
- **Distant clouds** no longer boil.
- **PMC third faction**: scripted missions and the editor can bring in a PMC side; the Summer Wing mission is flown for the PMC.
- **WMC LOADOUT symmetry**: pylon pairs can be linked or split (issue 19).
- **Wing on the map**: wing markers carry tethers and outlined badges, and the right-click card has
  wing orders.
- **Cutscene boards**: the scripted missions show news, paper, map, satellite and dossier boards with
  voiced narration.
- **Tester feedback**: RWR and status are split, the board is a little smaller, and a one-time
  settings reset applies the new defaults.

## 0.2.3 — update

- **Map selection reaches the target list**: while you fly, contacts picked on the expanded map (click, box,
  right-click menu) are now real weapon targets, several at a time; before, they only looked selected.
  A new tool row on the map corner sets what a left drag does (PAN / BOX), what a click does (ADDS / ONE),
  which side a box takes (ALL / HOSTILE / OWN) and clears the selection. New `Command` settings:
  `MapLeftDragBox`, `MapClickAdds`, `MapBoxSide`, `MapRightDragTurns`, `MapDoubleClickZoom`,
  `MapTurnSensitivity`, `MapZoomStep`.
- **Relief map**: contacts stand on the terrain as 3D miniatures again (`MapMinis3D`, on by default), now
  one mesh and one draw call that is rewritten only when something actually changed. Holding the right
  mouse button and dragging turns the view again, next to the held wheel, Alt + drag and Ctrl + wheel.
  The red and blue rings that formed on the front line are gone: an unheld square closed in by held
  ground is now tinted through instead of being left as a hole with both border lines bent round it.
- **The C menu stays open after you pick something**: every action now leaves the menu up (the row flashes and the result shows
  under the hub), so several things can be set in one visit. Close it with the open key, Esc, or a click away from it. The ring's
  icons and the hub are also much easier to hit: the cursor counts as on one from about 30 px outside its rim.
- **Chaff is a countermeasure like the others** (issue 10): `Next Countermeasure` now steps FLARES, JAMMER, CHAFF, and while chaff
  is selected the `Countermeasures` button dispenses it. The selection shows on the TAC BOARD, the game's own readout and the
  C menu. Single player and hosting, like the dispenser itself.
- **The C menu no longer takes the game's radial wheel** (issue 11): the `Radial Menu` binding opens the game's own wheel again, so
  mods that add to it work; the C menu opens on its own key (`Autopilot.AceRadialKey`, default C). Set the new
  `Autopilot.InteractionMenuOnRadialBinding` to on to open it from a HOTAS or gamepad button as before.
- **The game's radial wheel is stock again**: the `BOSCALI` and `Wing Command` slices are gone from it. Autopilot landing, the
  target presets and the wing orders are in the C menu (FLIGHT, TARGETS, WING).
- **Objective marker redrawn** (issue 13): the contract marker in the cockpit drew a black square behind its area ring. It is now
  drawn in the visor's own line work: corner brackets on the point, a chevron at the screen edge when the target is out of view,
  a dashed area ring, and a shelf with the title and the distance that fills as the hold progresses.
- **Map selection** (issue 14): one left click on the big map selected two contacts, or selected one and dropped it again,
  because the game handed the click to the icons twice. One click is one click now.
- **Visor terrain follows the map**: the terrain ghost and the holo map could keep the relief of the map flown before (Heartland
  hills over the Archipelago). They now check the loaded map every frame and fetch its own relief. The ghost's lines are about a
  quarter fainter and its twice-a-second repaint costs about half.
- **Boscali Summer, the mission:** the **Boscali Summer** entry in the mission picker is now a scripted mission on top of the game's own Escalation. A cutscene tells the build-up in a storm at sunset (the night of the coup, the fall of Agrapol, the fleet coming home), with armoured columns already rolling, place-and-date stamps and the mod's event paintings landing as photographs along a timeline, framed as a surveillance feed and set to its own music. You pick a side as a contract pilot, your handler and your side's command talk you through an opening (fly to the picket station, break the raid on your strip), and then the war opens into the full Escalation sandbox with the story following its milestones to the end. Existing user missions are preserved; no mission file is shipped.
- **Mission scripts:** the mission above is one text file. Your own go in `BepInEx/config/BoscaliSummer/scenarios/`: objectives and spawns written into a copy of any mission, named characters with dialogue, cutscene camera shots with captions, stamps and pinned photographs, hints, and campaigns whose chapters unlock one after another. A dedicated server starts one with `-mission scenario:<id>`. New F1 switch: **Cutscenes in scripted missions**.
- **Mission systems prompt:** every mission, vanilla and workshop included, runs the whole mod again; nothing is locked. When a mission starts, a prompt on the map screen lists the systems with **FULL EXPERIENCE** and **IMMERSION ONLY** presets (immersion only keeps the cockpit, HUD, wing, radio and comms and leaves the mission as its author made it). Start, keep the answer for that mission, or never be asked again; F1 > Mission brings the prompt back.

## 0.2.2 — update

- **Chaff**: your aircraft now carries a chaff dispenser (single player and hosting), built on the
  game's own unused chaff classes. It dazzles radar-guided missiles, most when you fly across the
  missile's line of sight and not at all head-on. Holding the countermeasure button dispenses it
  while a radar missile is inbound; DEFENCE has POP CHAFF and a switch for that link, and
  `Autopilot.ChaffKey` binds a key. The load follows the flare load (24 to 96 bundles), shows on
  the TAC BOARD's expendables and refills with a rearm.
- **C menu additions**: TARGETS has LOCK NEAREST THREAT (the nearest tracked hostile aircraft, alone)
  and TARGET INBOUND MSLS (every missile tracking you joins the selection); WEAPONS has OPEN /
  CLOSE BAY DOORS on airframes with a weapon bay.
- **C menu regrouped**: the ring is now WEAPONS, HUD, FLIGHT, WING, COMMS, COCKPIT, SUPPORT, VIEW,
  DEFENCE, TARGETS. HUD (key 2) holds every TAC BOARD switch: the board itself, declutter, layout,
  each scope page, each board part, shade, size and a held reset. The exterior lights are one FLIGHT
  row, night vision moved to VIEW next to the new holo map switch and ground ghost level, the radio
  became COCKPIT with the cabin effects and the kneeboard card, and DEFENCE gained GO DARK (radar
  silent and every light off in one press).
- **C menu handling**: the view no longer pans while a controller steers the menu; a controller
  enters a category by holding the stick on it for a second (a ring closes round the icon); letting
  go of the open key too early keeps the menu for a click instead of closing it; rows picked with
  the arrows or the mouse wheel run on release; switches inside a list stay up so several can be
  flipped in one visit; a click on the hub goes back and a click in the open closes.
- **Radio chatter rebuilt**: the radio is rebuilt from scratch as script packs, with AWACS, tower,
  enemy and command nets, voice packs, a comms block in the left column and the radio in the MFD
  log. Not yet played through in game.
- **Fused night vision**: a white-phosphor tube with metered, gating gain and a thermal glow on hot
  units; VIEW has SET NVG GAIN while night vision is on.
- **Holo map and terrain ghost**: the corner map is a standing mini hologram that the held map key
  grows into the big one (a tap still opens the MFD map), and a wire relief of the ground shows
  through the cockpit. NAV DATA and the DL EVT LOG event log join the visor.

## 0.2.1 — update


- **MFD index rail rebuilt**: the button column on the right of the map is a new panel in the
  same style as the MFD consoles. Its keys now follow the colour scheme chosen on SET > DISPLAY the
  moment it changes; before, the rail kept the colours it was first drawn with. Keys are grouped
  under SITUATION, TASKING, COMMS and SYSTEM, each screen has its own redrawn icon, and the open
  screen's key reads ON.

- **Relief map pass**: the STR layer now moves with the terrain instead of trailing it during a
  pan; the view redraws at up to 60 Hz while it moves (30 Hz when the frame rate is already low).
  Contact stacks are grouped on the ground, by faction, so panning or turning the map no longer
  reshuffles them, and a knot of units on a grid line reads as one count. Land now carries a light
  red/blue control tint like the sea. Ctrl + wheel turns the view and Shift + wheel tilts it, next
  to the middle-button drag. Reprojecting the contact roster costs far less per redraw.

- **Event responses removed**: the RESPONSE desk on the EVN screen (contain / leverage, treasury
  directive, contract intelligence, pilot channel) is gone. A world event now simply sets its
  support price and reset time until it ends. Host and clients must run the same build.
- **TAC BOARD HUD rework** (GitHub issue 8): a new helmet-visor look with no panel behind it,
  spread wide along the top edge of the screen; the classic column stays as the STACK layout.
  The selected weapon and the jammer are drawn with the game's own line-art pictures.
  New options for layout, size, position, opacity, shade, declutter level, every page
  and block, visor sway and the kill feed and map restyle, on SET > COCKPIT, the C menu (VIEW),
  F1 and the `[TacBoard]` config section, with Reset to defaults. The radar-warning page now
  holds a contact for a set time (6 s by default) and any page stays up a minimum time, so it no
  longer pops on and off. Names and readouts that were cut off or overlapped now fit, and the
  board scales down to 1280 x 720. Switching the board off keeps the kill feed and map restyle
  (it has its own switch), and the visor sway follows the pilot's head, not the aircraft's own turns.
- **Display grade (off by default)**: SET > CLIENT > PERF gains COLOUR GRADE and SHARPEN. Contrast,
  saturation, white balance and sharpening strength are in the F1 advanced view under Effects, and
  any `.cube` LUT dropped into `BepInEx/config/BoscaliSummer/luts` can be named there as the look.
- **Squadron insignia redrawn**: badges are cut, not rounded (angular shield, bullet arch, chamfered
  patch, heavier keylines), with three new shapes (ARROWHEAD, SAWBLADE, COFFIN). The centre marks are
  replaced by 48 new four-colour drawings: pin-ups, predators, cards and dice, skulls, gremlins and
  jokes. The mark's skin tone follows INK and its red follows SECOND. An insignia saved before this
  keeps its shape and colours but shows the new mark with the same number. Enemy ace wings fly only
  the predators, weapons and omens.
- **Tail markings sit where they should**: the multirole wears its insignia on its fins (it was on
  the boom underneath), the fast bomber's is no longer cut by the fin's leading edge, and the ground
  attack jet's and trainer's stay clear of the formation-light strips. Markings are painted at twice
  the resolution.
- **Gun pipper fixed** ([#9](https://github.com/GrabowMar/Boscali-Summer/issues/9)): with a gun
  selected and a target designated, the lead pipper and everything the HUD drew after it went
  missing. The tweak that switched off the game's aim-assist stick correction caused it and is
  removed; the game's own aim assist is back.
- **Occupied city buildings can be levelled again**: under URBAN SIEGE a garrisoned building
  ignored every hit. Bombs now wear it down as intended (3-8 bomb units by roof size), and hits on
  its rooftop nest count against the building.
- **C-key menu is quicker and more forgiving**: release fires after 50 ms on a row (the corner
  brackets close when it will), a pointer that strays between rows or just past the list keeps
  the nearest row, and categories you cross on the way to the list no longer steal the menu. It
  also remembers the last action per category: a chevron marks it, the arrow keys land on it
  first, and Space at the ring jumps straight back to it.
- Pressing Map in the aircraft selection menu no longer leaves the map's glass overlay on screen.
- A patch that cannot start on the player's runtime now switches its module off at load, with one
  line in the log, instead of breaking the game feature it patches.
- Blueprinter now loads the mod's content (Vanguard weapons, HALO paratroopers, container feeder)
  as one pack, "Boscali Summer assets", instead of three. Host and clients need the same build.

## 0.2.0 — first public alpha

- **Wing Command is built in.** Remove the stand-alone Wing Command mod; AI wingmen, the WMC
  page and formations now ship inside Boscali Summer.
- **TAC BOARD HUD**: stores and countermeasure header, system cautions, and nine automatic
  pages for threats, shots, missile camera, approach, wing, strikes, refuelling, objectives,
  and aircraft systems. Includes flight and weapon gauges, fuel endurance, and a notice ticker.
- **C-key interaction menu**: a ring with live action lists, keyboard navigation, formation
  selection, support perks, missile view, and SKYWELL pit-stop orders and station priority.
- **VANGUARD weapons** (needs Blueprinter): RG-12 LANCE railgun, AIM-X AEGIS defence pod,
  MALD-X BAIT and MALD-J SHADOW, REMORA escort drones, HAWC-X SKIP glider, GLAIVE-2A SENTINEL
  and GLAIVE-2S SPECTRE gun drones, ORCA glide torpedo, and ALE-X towed decoy.
- **SKYWELL service tender**: deploy a refuel/rearm kit from a compatible cargo hold. Receivers
  fly formation inside its service zone while the tender delivers fuel and rounds job by job;
  includes fuel/munitions orders, station priority, and threat-triggered breaks.
- **KS-01 KESTREL recon glider**: an unarmed cargo-hold-launched scout that orbits a designated
  ground target, reports hostile contacts, and provides laser designation.
- **CG-9 HAMILCAR cargo glider** (VANGUARD, needs Blueprinter): release it from a compatible
  cargo bay at a designated ground target. It flies fifteen metres for every metre of height
  (up to 45 km), lands on its belly
  about 250 m short of the target and a Hexhound GMG rolls off as a live unit. It needs flat dry
  ground; anything that hits it on the way in loses both.
- **Weather tape**: stories, front markers, contrast-scaled ramps, the multi-keyframe bake and TapeOpening.Solve; the legacy timeline, frontal shield, sky split and cloudShift are gone (held tape; motion is slice 5).
- **Weather console (Ctrl+O) is the MET OFFICE**: a synoptic chart of fronts, cells and storms, sky and
  METAR readouts, pin any of the 47 skies, hold and pace the weather, set-pieces and an anchor, rain
  override, six one-click scenarios and a key-driven command line. Clients see it read-only.
- **Airborne** (needs Blueprinter): HALO paratroopers, Ibis fast-rope and rappel insertions.
- **OPS fronts**: SPACE / CYBER / SOF fronts with directives, programmes and readiness.
- **Squadron insignia**: layered crest editor, tail-fin markings for supported aircraft, and
  cosmetic crest sharing between players.
- **Weather by role**: every player now gets native weather for their own seat. The host and a dedicated
  server set fog, wind, turbulence and cloud height from the theater; each client from their own camera and
  aircraft. The native sun no longer dies at a front (vanilla's grey-and-no-sun gate is never reached; the
  overcast look comes from the mod), a client's fog, wind and cloud height keep following their own camera
  and aircraft while the weather is held (the host's and a single player's are theater-level; light and rain
  are always local), and the cloud height follows the deck you are under. The MET OFFICE gets a working STORY stepper (AUTO and the five stories), and the
  ENV screen shows the story, the next front ahead and a forecast at your position (HELD while the tape is held).
- **Weather and cockpit immersion**: 47 sky profiles, revised volumetric clouds and canopy
  rain, pilot body and breathing cues driven by native G-LOC, recorded pilot/effect audio,
  and a kneeboard picture-card gallery.
- Reworked OPS/STR/COMMS consoles, command map, world-event art, destruction, and wildfire effects.
- Private spectator cinematography tools for camera paths and shot editing; no video encoding.
- Player build: developer cheats and tools are locked off and hidden from the F1 window.
- Clean Release builds after integration; unused fields removed and isolated-test telemetry
  warnings separated from production diagnostics.
