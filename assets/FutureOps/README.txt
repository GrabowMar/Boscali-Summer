FUTURE OPS — ASSET LIBRARY 0.1.0

Original Blender-authored near-future military scenery for Boscali Summer.
FutureOps.nobp is a Blueprinter schema-3 asset library, built for Unity
2022.3.62f3 / Windows x64. Load prefab names BS_Future_<model> from the bundle.
The static asset pack has no registration operations. CargoShip.nobp is a separate
native ship content bundle, embedded as BoscaliSummer.CargoShip.nobp in the mod DLL.
Blueprinter registers BS_ContainerFeeder as Kestrel Class Container Feeder.

2026-10-08 quality pass: twelve ground designs now have original tileable PBR
albedo, normal and metallic/smoothness surfaces, metre-scaled UVs and additional
equipment fittings. Local vanilla mesh/material extraction was used as reference;
no extracted game art is distributed. The cargo ship retains its marine skins,
native parts, propulsion, damage graph and separate native content bundle.
SurfaceFinish.py in the sibling MissionAssets/Source directory is a shared
authoring dependency. Both packs must remain together for source rebuilds.
Additional polish smooths sandbags, tanks and round equipment without subdivision
or added polygons. All three LOD tiers and the separate coarse collision meshes
are retained; lower-LOD bevel remnants are cleaned before export.

Contents
 LaunchSite       38 x 48 m concrete pad, service gantry, tanks, control shelter
 FieldCamp        four canvas tents, service shelter, supplies and antenna
 FOB              fortified command compound, gabions, shelters and containers
 DataCenter       hardened service building, cooling banks and backup utilities
 CargoShip        126.6 m feeder; fair hull/bulb, detailed bridge, fittings and cargo
 TrenchStraight   8 m segment, 2.2 m passage, duckboards, revetments and sandbags
 TrenchCorner     90-degree turn; outlets at (0,-4) and (4,0) in Blender XY
 TrenchDugout     covered shelter with open entrance and benches
 SandbagWall      8 m defensive wall, four staggered courses
 RadarPost        parabolic dish, operating shelter and communications mast
 FuelDepot        paired storage tanks, bund, manifold and service shelter
 Checkpoint       guard shelter, gate, sandbags and steel obstacles
 Helipad          24 m pad with H, perimeter markings and lamps

Models/*.blend    editable metre-scale authoring sources, LOD0/1/2 and COL_Static
Models/*.fbx      Unity imports; convert units OFF, bake axis conversion ON
Models/CargoShipAuthoring.blend keeps the individually editable original ship pieces.
Models/CargoShipNative.fbx keeps the section/LOD split and propeller rotor pivots.
Textures/         original 2048-pixel paint, normals, damaged paint and metal masks
Previews/*.png    Blender lighting previews; Overview.jpg is the complete contact sheet
Previews/Unity/   offline renders loaded directly from the exported .nobp
catalog.json      stable prefab keys, bounds, triangle counts and material names
validation.txt    actual exported-bundle verification
Source/           reproducible Blender generator, build wrapper and verification
Editor/           Blueprinter prefab builder and material binding

Coordinates / placement
 Blender: +Y forward, +Z up. Unity: +Z forward, +Y up. All roots use scale 1.
 Facilities originate at ground centre; the cargo ship originates at waterline
 and its keel is approximately 4.5 m below the origin.
 Trench banks are above grade. Placing them does not excavate game terrain.
 Connect trench anchors at matching JoinStart/JoinEnd positions, rotating
 subsequent modules as required. Straight join spacing is 8 m.
 Colliders are coarse non-convex meshes for STATIC scenery, with open passages.
 Small dressing (wire fence, antenna stays, handrails) is deliberately non-solid.
 Prefab anchors include GroundOrigin; LaunchPad, Waterline, CargoDeck,
 LandingPoint, SensorOrigin and trench joins are supplied where applicable.

Scope / verification
 The static library contains asset geometry only: no BuildingDefinition, launch
 simulation, ship buoyancy/navigation, NetworkIdentity or OPS ownership rules.
 Those systems must be attached when integrating with native game/host authority.
 The native CargoShip.nobp adds ShipDefinition, Ship, ShipAI, ShipPart,
 ShipPropulsion, UnitCommand, ShipNetworkTransform and Mirage NetworkIdentity.
 It has 18 damage parts, eight port/starboard buoyancy compartments, two animated
 propellers, three LODs, dynamic convex/box collision, native wake/sound references
 and native damage-shader binding. Mass is 8,000 tonnes; declared speed is 33.3 km/h.
 The loaded native buoyancy function balances the mass at the authored waterline.
 These are calibrated offline values, not a claim of tested live seakeeping.
 The ship is unarmed. Its cargo is stowage geometry; it does not implement resupply,
 container spawning, unloading economics or any third-party mod behaviour.
 Three mesh LODs use screen-height transitions 0.30 / 0.12 / 0.015.
 Shared URP/Lit materials use the authored palette. The rebuilt cargo ship adds
 exported 2048-pixel surface and damage maps; other assets retain simple palettes.
 Bundle checks include Unity renders, scale, LODs, materials and trench clearance.
 They do not establish in-game rendering, placement or gameplay.
 No deployment or main-install changes are part of this asset build.

Rebuild (PowerShell from this folder)
 ./Source/Build-Pack.ps1
 ./Source/Build-Pack.ps1 -SkipModels
 ./Source/Build-CargoShip.ps1 -SkipModels
 Requires Blender 5.2, Unity 2022.3.62f3 and the locally configured Blueprinter
 editor project (override paths through the script parameters). The wrapper
 builds in ../../.nomodkit/futureops-unity, without editing the donor project.
 Verify / recreate overview with Blender Python and the existing nomodkit libs:
 python Source/Verify-Pack.py <this-folder>

All geometry is generated locally. No downloaded models or third-party art.
Aryx Naval Expansion 1.0.4 was inspected as an architectural reference, not copied.
Native topology and registration are reused from the installed game's own systems.
See cargo-native-validation.txt and cargo-bundle-validation.json for evidence.
