MISSION ASSETS 0.1.0 — ORIGINAL BLENDER MODELS AND MISSION DESIGN STUBS

MissionAssets.nobp: Blueprinter schema-3 asset library; Unity 2022.3.62f3,
Windows x64, URP/Lit. Prefabs use BS_Mission_<model> names. This pack has no
automatic unit, BuildingDefinition, contract or gameplay registration.

QUALITY PASS / 2026-10-08
Locally extracted vanilla Truck2-R, RadarContainer1 and frigate material maps
were inspected for construction, scale and surface conventions. Reference art
remains under .nomodkit/evidence/2026-10-08-model-quality/reference; none is shipped.
All 16 designs now use original, packed, metre-scaled tileable albedo, normal
and metallic/smoothness maps, also assigned in the actual Unity bundle.
The jammer has an angled cab, glazing, wipers, mirrors, doors, steps, chassis,
treaded tires, hubs, slatted vents and hydraulic stabilizers. The wreck adds
open fuselage damage, exposed ribs, cockpit glazing, engines and bent props.
Workshop trusses, drawers, case hardware and facility fittings add readable detail.
Satellite buses have distinct dimensions, thermal blanket texture, access panels,
fasteners, radiator channels and star trackers; the tug adds tank and docking detail.
QualityComparison.jpg shows original and revised Blender renders using the same
studio lighting and view direction, with framing fitted to each model's bounds.
geometry-validation.json and surface-validation.json record offline checks.

PERFORMANCE POLISH
Solar cells are now texture detail on four panel laminates, with real frames and
fold hinges. Each rig exports one merged mesh per moving pivot (3–5 renderers),
while the authoring file retains the separate original editable parts.
The three static LODs remain; validation checks retained solar span and surface
coverage at all distances. Round components and sandbags use smooth shading
without subdivision. Foil folds are restrained and irregular. Textures remain
512px tiles; no added runtime effects or scripts. polish-validation.json compares
triangle and renderer budgets against the previous delivery.

GROUND MODELS / INTENDED OWNERS
RadioRelay                Support/CYBER/SPACE, Intel: remote microwave uplink
MobileJammer              DynamicOperations/EW: six-wheel jammer target scenery
AmmunitionDepot           DynamicOperations/Logistics: protected storage and pickup
RepairStation             DynamicOperations/RepairCover: service canopy and spares
ForwardAidStation         UrbanCombat/SOF/Rescue: triage tents and supplies
RescueCrashSite           DynamicOperations/Rescue: original broken aircraft, recorder
SmugglerCache             Intel/SOF/Survey: crates, temporary shelter and radio
CoastalSensorPost         Intel/Recon: surface radar and EO surveillance tower
DroneGroundStation        Support/Recon: control container and drone launch rail
PowerSubstation           CYBER/TheaterOps/Repair: transformers, switchgear and cooling
BridgeDemolitionKit       SOF/Interdict: equipment, command unit and barriers
EvacuationLandingZone     SOF/Rescue/Defend: landing markings, beacons and extraction

ORBITAL MODELS / SUPPORT-SPACE PRESENTATION
OpticalSatellite          MLI bus, nadir-facing telescope, solar arrays and RCS
SarSatellite              MLI bus, deployable rectangular radar array and solar wings
RelaySatellite            MLI bus, paired parabolic reflectors and solar arrays
OrbitalServiceTug         docking collar, capture arms, main engine and RCS pods

Each main model has three LODs, a coarse static collider, an editable .blend,
an FBX and an inspected Blender preview. Catalog bounds and transforms are in
metres; Blender +Y forward/+Z up becomes Unity +Z forward/+Y up. Anchors are
stored in Unity coordinates in catalog.json and included on the bundled prefabs.

Each satellite also includes:
 - <Name>Authoring.blend: individual geometry plus exported pivot hierarchy
 - <Name>Rig.fbx: Bus, SolarPort and SolarStarboard; SAR adds RadarBoom;
   relay adds DishFore/DishAft. These are visual pivots, with no animation script.
 - BS_Mission_<Name>_Rig prefab: articulated presentation geometry.
Use the main prefab for distant/static LOD presentation and the rig prefab for
an interactive close view; do not draw both simultaneously. Solar hinge axes
follow the FBX local frame. GroundOrigin is simply the model origin on spacecraft.
The optical aperture and SAR active face point toward nadir (Blender -Z / Unity -Y).

MissionStubs contains nine explicitly disabled JSON design stubs:
 Cut the Signal       emitter suppression / EW confirmation
 Quiet Horizon        SAR-assisted coastal reconnaissance
 Broken Wing          downed-crew rescue and aid-station delivery
 Fuse Box             engineer repair cover at a power node
 Sealed Manifest      SOF cache inspection and intelligence recovery
 Logistics Window     native resupply convoy escort
 Last Light           evacuation-site defense
 Lost Packet          uplink restoration and confirmed sortie reporting
 Orbital Handshake    proposed orbital servicing plus ground authorization

Each stub names assets/anchors, compatible existing operation family, candidate
module owners, objective phases, required native service binding, host authority,
faction-known visibility, accepted-reward rules, scene reset and hard budgets.
Proposed follow-ons are design suggestions, not changes to OperationChains.
Stubs do not register contracts or add operation enum/network-message values.
Static assets do not jam, repair, emit radar, create pilots or satisfy objectives.
Bind mission actors to real native Units/Buildings/services when implementing.
Space presentation does not grant observation, strikes or orbital physics.

REBUILD
From this folder: ./Source/Build-Pack.ps1
Use -SkipModels to rebuild only the .nobp from existing sources.
The build reuses assets/FutureOps export helpers and the shared verified Unity
builder in assets/FutureOps/Editor. It uses the thread-owned scratch project
.nomodkit/futureops-unity, with locally cached Blueprinter and URP packages.
Generate stubs: <Blender Python> Source/BuildMissionStubs.py <this folder>
Verify: <Blender Python> Source/Verify-Pack.py <this folder>

EVIDENCE / LIMITS
Previews/Overview.jpg is the full set; Previews/Unity contains actual bundle renders.
validation.txt records Unity reopening/rendering, scale, LODs, collisions and anchors.
checksums.json records the delivered source/bundle/preview/mission files.
This is an asset/design pack. No gameplay implementation, deployment, live input,
orbital behaviour, native damage, performance or multiplayer acceptance is claimed.
All geometry is original procedural Blender work; no downloaded art or game meshes.
Existing Support A1 satellite, cargo ship and all other FutureOps assets are preserved.
