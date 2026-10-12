using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Runtime.Loader;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Reflection.Emit;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: BoscaliSummer.PatchProbe <game-dir> <plugin-dll>");
    return 2;
}

string gameDir = Path.GetFullPath(args[0]);
string pluginPath = Path.GetFullPath(args[1]);
string managedDir = Path.Combine(gameDir, "NuclearOption_Data", "Managed");
string bepInExDir = Path.Combine(gameDir, "BepInEx", "core");
string[] roots = { managedDir, bepInExDir, Path.GetDirectoryName(pluginPath)! };

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string root in roots)
    {
        string candidate = Path.Combine(root, name.Name + ".dll");
        if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }
    return null;
};

Assembly gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managedDir, "Assembly-CSharp.dll"));
Assembly mirageAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managedDir, "Mirage.dll"));
const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
Assembly pluginAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);

(string Type, string Method)[] targets =
{
    ("ControlsFilter", "SetFlightAssist"),
    ("ControlsFilter", "GetAim"),
    ("NightVision", "NightVis_OnSwitchCam"),
    ("Hangar", "TrySpawnAircraft"),
    ("Pilot", "GetAccel"),
    ("WeaponManager", "GetTargetList"),
    ("Airbase", "CaptureFaction"),
    ("Airbase", "ICapturable.get_CaptureDefense"),
    ("Unit", "get_CaptureStrength"),
    ("Capture", "ApplyChange"),
    ("Building", "OnStartClient"),
    ("Building", "OnStartServer"),
    ("BulletSim+Bullet", "TrajectoryTrace"),
    ("GroundVehicle", "UnitDisabled"),
    ("Aircraft", "UnitDisabled"),
    ("Aircraft", "WaitRemoveAircraft"),
    ("MapBuilding", "TakeDamage"),
    ("MapBuilding", "TakeShockwave"),
    ("MapBuilding", "Destruct"),
    ("MapBuildingSet", "UserCode_CmdDestroyBuilding_1002795805"),
    ("Missile", "UserCode_RpcDetonate_897349600"),
    ("Missile", "StartMissile"),
    ("Missile", "GetSeekerType"),
    ("Countermeasure", "GetThreatTypes"),
    ("Aircraft", "GetFuelLevel"),
    ("Aircraft", "GetFuelTanks"),
    ("PartDamageTracker", "GetDetachedRatio"),
    // TAC BOARD SYSTEMS page (HUD v8): lost parts by side, the jammer's charge.
    ("UnitPart", "IsDetached"),
    ("Aircraft", "GetPowerSupply"),
    ("PowerSupply", "GetCharge"),
    ("FlareEjector", "GetMaxAmmo"),
    ("ChaffEjector", "GetMaxAmmo"),
    ("Missile", "Detonate"),
    ("Missile", "OnStartClient"),
    ("MusicManager", "PlayMusic"),
    ("MusicManager", "CrossFadeMusic"),
    ("MusicManager", "QueueMusicClip"),
    ("MapSettings", "GetStartMusic"),
    ("MapSettings", "GetStrategicMusic"),
    ("MapSettings", "GetTacticalMusic"),
    ("VirtualMFD", "Start"),
    ("VirtualMFD", "SetupButtons"),
    ("VirtualMFD", "PressLeftButton"),
    ("VirtualMFD", "PressRightButton"),
    ("MFDScreen", "ShowScreen"),
    ("MFDScreen", "CloseScreen"),
    ("FactionHQ", "RewardPlayer"),
    ("Aircraft", "UseFuel"),
    ("Aircraft", "FilterInputs"),
    ("Aircraft", "GetAircraftParameters"),
    ("Unit", "RecordDamage"),
    ("Unit", "ReportKilled"),
    ("Pilot", "ApplyDamage"),
    ("DynamicMap", "TryGetCursorCoordinates"),
    ("DynamicMap", "GetCursorCoordinates"),
    ("DynamicMap", "IsCursorInMapRectangle"),
    ("DynamicMap", "Update"),
    ("DynamicMap", "JumptoTarget"),
    ("Spawner", "SpawnVehicle"),
    ("Spawner", "SpawnBuilding"),
    ("Spawner", "SpawnSavedMissile"),
    ("Missile", "GetYield"),
    ("Missile", "SetAimpoint"),
    ("FactionHQ", "GetTrackingData"),
    ("FactionHQ", "RpcUpdateTrackingInfo"),
    ("UnitPart", "TakeDamage"),
    ("Aircraft", "IsLanded"),
    ("MountedTroops", "Fire"),
    ("DynamicMap", "Maximize"),
    ("DynamicMap", "Minimize"),
    // HOLO MAP (2026-10-12): the map key reaches DynamicMap.Maximize from ExtraUiInput.Update (Rewired "Map"); HoloMapKeyScopePatch brackets that method.
    ("ExtraUiInput", "Update"),
    ("DynamicMap", "CenterMinimizedMap"),
    ("DynamicMap", "MapControls"),
    ("GridLabels", "GridLabels_OnMapChanged"),
    ("GridLabels", "LateUpdate"),
    ("GridLabels", "UpdateMinorGridLabels"),
    ("GridLabels", "Maximize"),
    ("RadialMenuMain", "SetupMain"),
    ("RadialMenuMain", "OpenMenu"),
    ("RadialMenuMain", "OnDestroy"),
    ("RadialMenuMain", "Update"),
    ("RadialMenuAction", "AllowedOnAircraft"),
    ("RadialMenuAction", "TriggerAction"),
    ("PilotPlayerState", "FixedUpdateState"),
    ("NuclearOption.Jobs.DetectorManager", "RequestLoSCheck"),
    ("CameraCockpitState", "UpdateState"),
    ("CameraCockpitState", "LeaveState"),
    ("Gun", "SpawnBullet"),
    ("CameraOrbitState", "UpdateState"),
    ("FlightHud", "GetHUDCenter"),
    ("CombatHUD", "UpdateHitMarkers"),
    ("HUDUnitMarker", "UpdatePosition"),
    ("HUDUnitMarker", "JammingDistortion"),
    ("HUDTurretCrosshair", "Refresh"),
    // DL EVT LOG (2026-10-13): EventFeedPatch is a postfix on the one place every line of the game's message feed passes.
    ("MessageFeed", "Enqueue")
};

foreach ((string typeName, string fieldName) in new[]
{
    ("Aircraft", "cockpit"),
    ("CameraStateManager", "cockpitCamRender"),
    ("HUDUnitMarker", "image"),
    ("HUDUnitMarker", "unit"),
    ("UnitPart", "hitPoints"),
    ("Aircraft", "partLookup"),
    ("Aircraft", "pilots")
})
    if (gameAssembly.GetType(typeName, true)!.GetField(fieldName, AllMembers) == null)
        throw new MissingFieldException(typeName, fieldName);

foreach ((string typeName, string methodName) in targets)
{
    // Some Unity Mono types contain self-references that CoreCLR refuses to materialize
    // even though the metadata and Mono runtime are valid (GroundVehicle/SteeringInfo).
    // Inspect that target directly in ECMA-335 metadata instead of weakening the probe.
    if (typeName == "GroundVehicle")
    {
        if (!MetadataHasMethod(Path.Combine(managedDir, "Assembly-CSharp.dll"), typeName, methodName))
            throw new MissingMethodException(typeName, methodName);
        Console.WriteLine("  " + typeName + "." + methodName);
        continue;
    }
    Type type = gameAssembly.GetType(typeName, true)!;
    if (type.GetMethod(methodName, AllMembers) == null)
        throw new MissingMethodException(typeName, methodName);
    Console.WriteLine("  " + typeName + "." + methodName);
}

(string Type, string Field)[] fields =
{
    ("MountedTroops", "captureStrength"),
    ("MountedTroops", "captureActive"),
    ("MountedTroops", "mass"),
    ("Weapon", "hardpoint"),
    ("Capture", "target"),
    ("TargetCam", "cam"),
    ("Aircraft", "targetCam"),
    ("MapBuilding", "hitPoints"),
    ("MapBuildingSet", "mapBuildings"),
    ("Missile", "blastYield"),
    ("GameAssets", "scorchMarkDecal"),
    ("MusicManager", "currentSource"),
    ("MusicManager", "fadeSource"),
    ("SoundManager", "MusicMixer"),
    ("Faction", "factionName"),
    ("FactionRegistry", "factions"),
    ("VirtualMFD", "speed"),
    ("GameplayUI", "selectAirbasePanel"),
    ("GameplayUI", "spectatorPanel"),
    ("MessageUI", "messageText"),
    ("MessageUI", "killFeedText"),
    ("VirtualMFD", "leftButtons"),
    ("VirtualMFD", "rightButtons"),
    ("VirtualMFD", "leftScreens"),
    ("VirtualMFD", "rightScreens"),
    ("UnitRegistry", "allUnits"),
    ("Unit", "persistentID"),
    ("GroundVehicle", "navigateToObjectives"),
    ("GroundVehicle", "commandedDestination"),
    ("UnitDefinition", "value"),
    ("UnitDefinition", "roleIdentity"),
    ("RoleIdentity", "antiAir"),
    ("RoleIdentity", "antiSurface"),
    ("GridLabels", "gridToolTip"),
    ("GridLabels", "gridAircraft"),
    ("LevelInfo", "cloudLayer"),
    ("CloudLayer", "cloudSystem"),
    ("CloudLayer", "cloudSizeMin"),
    ("CloudLayer", "cloudSizeMax"),
    ("CloudLayer", "densityMapScale"),
    ("CloudLayer", "layerThickness"),
    ("CloudLayer", "maxParticles"),
    ("CloudLayer", "distantCloudSystem"),
    ("CloudLayer", "flyThroughSystem"),
    ("CloudLayer", "cloudRenderer"),
    ("CloudLayer", "lightning"),
    ("CloudLayer", "layerMaterial"),
    ("Lightning", "lightningSystem"),
    ("Lightning", "flashLight"),
    ("CameraStateManager", "cloudSpeed"),
    ("CameraStateManager", "underwater"),
    ("NuclearOption.Effects.DetailRenderer", "treeRenderers"),
    ("NuclearOption.Effects.DetailRenderer", "grassRenderers"),
    ("NuclearOption.Effects.TreeRenderer", "mesh"),
    ("NuclearOption.Effects.GrassRenderer", "grassMaterial"),
    ("NuclearOption.Effects.GrassRenderer", "grassMaterialProps"),
    ("LevelInfo", "PostProcessing"),
    ("LevelInfo", "bloom"),
    ("LevelInfo", "windVelocity"),
    ("LevelInfo", "sun"),
    ("CameraStateManager", "cameraPivot"),
    ("Weapon", "attachedUnit"),
    ("Weapon", "info"),
    ("WeaponInfo", "massPerRound"),
    ("WeaponInfo", "muzzleVelocity"),
    ("CombatHUD", "iconLayer"),
    ("CombatHUD", "topRightPanel"),
    ("CombatHUD", "weaponStatus"),
    // TAC BOARD seeker cones and the countermeasure bank (ShotBook, MenuGame.Aircraft): private members read by name through AccessTools.
    ("ARHSeeker", "maxTrackingAngle"),
    ("ARHSeeker", "terminalRange"),
    ("SARHSeeker", "seekerAngle"),
    ("CountermeasureManager", "countermeasureStations")
};

foreach ((string typeName, string fieldName) in fields)
{
    if (typeName == "GroundVehicle")
    {
        if (!MetadataHasField(Path.Combine(managedDir, "Assembly-CSharp.dll"), typeName, fieldName))
            throw new MissingFieldException(typeName, fieldName);
        continue;
    }
    Type type = gameAssembly.GetType(typeName, true)!;
    if (type.GetField(fieldName, AllMembers) == null)
        throw new MissingFieldException(typeName, fieldName);
}

// Typed seams used by the external HUD and camera patches, including Harmony field injection.
(string Type, string Field, string FieldType)[] cameraFields =
{
    // First-person pilot binding and native control anchors (cached generic-rig access).
    ("Aircraft", "cockpit", "UnitPart"),
    ("Pilot", "skinnedMeshRenderer", "UnityEngine.SkinnedMeshRenderer"),
    ("Pilot", "animator", "UnityEngine.Animator"),
    ("Cockpit", "joysticks", "Cockpit+Joystick[]"),
    ("Cockpit", "throttles", "Cockpit+Throttle[]"),
    ("Cockpit+Joystick", "transform", "UnityEngine.Transform"),
    ("Cockpit+Throttle", "transform", "UnityEngine.Transform"),
    ("Cockpit+Joystick", "range", "System.Single"),
    ("Cockpit+Throttle", "range", "System.Single"),
    ("Cockpit+Throttle", "rotation", "System.Boolean"),
    ("Cockpit+Throttle", "motion", "System.Boolean"),
    ("ControlsFilter", "aircraft", "Aircraft"),
    ("ControlsFilter", "aimAssist", "ControlsFilter+AimAssist"),
    ("Hangar", "spawnedObject", "UnityEngine.GameObject"),
    ("Hangar", "clearDistance", "System.Single"),
    ("TargetCam", "cam", "UnityEngine.Camera"),
    ("TargetCam", "currentMode", "TargetCam+CamMode"),
    ("TrackingInfo", "lastSpottedTime", "System.Single"),
    ("UnitPart", "hitPoints", "System.Single"),
    ("Unit", "disabled", "System.Boolean"),
    ("WeaponStatus", "nameText", "TMPro.TextMeshProUGUI"),
    // Tactical HUD (2026-10-07): StatusPanel reads the native label font and safety lamp, ThreatWatch and MissileTracker the warning systems.
    ("WeaponStatus", "safetyImage", "UnityEngine.UI.Image"),
    // Corner takeover (2026-10-10): NativeCorner hides the three boxes of the native TopRightPanel (weaponPanel, countermeasuresBackground, PowerPanel) behind owned CanvasGroups; the stores
    // feed reads the guns' LINK (WeaponManager.gunsLinked, pinned below), the reload times of guns and missile launchers and the capacitor's full charge.
    ("WeaponStatus", "weaponPanel", "UnityEngine.GameObject"),
    // TAC BOARD stores art (2026-10-12): HERO and the jammer row draw the game's own weapon and countermeasure pictures (the sprites below) on the material of the native weapon panel's picture (weaponImage).
    ("WeaponStatus", "weaponImage", "UnityEngine.UI.Image"),
    ("WeaponInfo", "weaponIcon", "UnityEngine.Sprite"),
    ("Countermeasure", "displayImage", "UnityEngine.Sprite"),
    ("CombatHUD", "weaponStatus", "WeaponStatus"),
    ("CombatHUD", "countermeasureBackground", "UnityEngine.GameObject"),
    // Screen corners (2026-10-12): NativeScreenCorners finds the kill feed's plate (the child called Darkener under MessageUI's background box) and the minimised map's plate (DynamicMap's background).
    ("MessageUI", "messageBackground", "UnityEngine.GameObject"),
    // DL EVT LOG (2026-10-13): NativeScreenCorners hides the feed's two text objects behind owned CanvasGroups while the mod's log is drawn, and shows them whole while the chat input (MessageUI.chat) is open.
    ("MessageUI", "messageText", "TMPro.TextMeshProUGUI"),
    ("MessageUI", "killFeedText", "TMPro.TextMeshProUGUI"),
    ("MessageUI", "chat", "ChatBox"),
    ("DynamicMap", "mapBackground", "UnityEngine.UI.Image"),
    ("Gun", "reloadTime", "System.Single"),
    ("MissileLauncher", "reloadTime", "System.Single"),
    ("PowerSupply", "maxCharge", "System.Single"),
    ("Missile", "target", "Unit"),
    ("Unit", "onRegisterMissile", "System.Action`1<Missile>"),
    ("MissileWarning", "onMissileWarning", "System.Action`1<MissileWarning+OnMissileWarning>"),
    ("MissileWarning", "offMissileWarning", "System.Action`1<MissileWarning+OffMissileWarning>"),
    ("Missile", "seekerMode", "Missile+SeekerMode"),
    ("Countermeasure", "displayName", "System.String"),
    ("Countermeasure", "ammo", "System.Int32"),
    ("Countermeasure", "chargeable", "System.Boolean"),
    ("FuelTank", "isLeaking", "System.Boolean"),
    ("FuelTank", "ruptured", "System.Boolean"),
    ("FuelTank", "onFire", "System.Boolean"),
    // TAC BOARD SYSTEMS page: the jammer's "jamming now" (CountermeasureWatch).
    ("RadarJammer", "jammingIntensity", "System.Single"),
    ("Aircraft", "engineStates", "System.Collections.Generic.List`1<IEngine>"),
    ("Aircraft", "partDamageTracker", "PartDamageTracker"),
    ("AircraftParameters", "aircraftGLimit", "System.Single"),
    ("AircraftParameters", "PIDReferenceAirspeed", "System.Single"),
    ("AircraftParameters", "maxSpeed", "System.Single"),
    ("WeaponInfo", "blastDamage", "System.Single"),
    ("WeaponInfo", "jammer", "System.Boolean"),
    ("Unit", "onDeregisterMissile", "System.Action`1<Missile>"),
    ("MissileWarning", "knownMissiles", "System.Collections.Generic.List`1<Missile>"),
    ("Aircraft", "onRadarWarning", "System.Action`1<Aircraft+OnRadarWarning>"),
    ("FlightHud", "canvas", "UnityEngine.Canvas"),
    ("RadialMenuMain", "actionsMain", "RadialMenuAction[]"),
    ("RadialMenuMain", "aircraft", "Aircraft"),
    ("RadialMenuAction", "actionType", "RadialMenuAction+ActionType"),
    ("RadialMenuAction", "iconSprite", "UnityEngine.Sprite"),
    ("RadialMenuAction", "backgroundSprite", "UnityEngine.Sprite"),
    ("RadialMenuAction", "backgroundColorInactive", "UnityEngine.Color"),
    ("RadialMenuAction", "backgroundColorActive", "UnityEngine.Color"),
    ("Aircraft", "cockpitRenderers", "UnityEngine.Renderer[]"),
    ("NightVision", "nightVisSelected", "System.Boolean"),
    ("NightVision", "nightVisActive", "System.Boolean"),
    ("NavLights", "isOn", "System.Boolean"),
    ("WeaponManager", "gunsLinked", "System.Boolean"),

    // Wingview camera: Harmony field injection on CameraOrbitState.UpdateState (WingviewCameraPatch).
    ("CameraOrbitState", "panView", "System.Single"),
    ("CameraOrbitState", "tiltView", "System.Single"),
    ("CameraOrbitState", "viewDistAdjust", "System.Single"),
    ("CameraOrbitState", "lookAtTargetLerp", "System.Single"),

    // Third-person HUD: final-camera marker projection (ThirdPersonHudCenter) and
    // native flight-number hiding (NativeFlightNumberHider), all reflected via AccessTools.Field.
    ("CombatHUD", "markers", "System.Collections.Generic.List`1<HUDUnitMarker>"),
    ("CombatHUD", "jamAccumulation", "System.Single"),
    ("CombatHUD", "weaponState", "HUDWeaponState"),
    ("HUDUnitMarker", "image", "UnityEngine.UI.Image"),
    ("CombatHUD", "objectiveOverlay", "ObjectiveOverlayManager"),
    ("CombatHUD", "targetInfo", "TMPro.TextMeshProUGUI"),
    ("CombatHUD", "targetText", "TMPro.TextMeshProUGUI"),
    ("CombatHUD", "targetArrowTail", "UnityEngine.Transform"),
    ("AllyInfo", "hoveredAllyInfo", "TMPro.TextMeshProUGUI"),
    ("AllyInfo", "hoveredAllyMarker", "HUDUnitMarker"),
    ("AirbaseOverlay", "nearestAirbase", "Airbase"),
    ("AirbaseOverlay", "runwayUsage", "System.Nullable`1<Airbase+Runway+RunwayUsage>"),
    ("AirbaseOverlay", "landing", "System.Boolean"),
    ("AirbaseOverlay", "taxiingToRunway", "System.Boolean"),
    ("AirbaseOverlay", "reachedRunway", "System.Boolean"),
    ("AirbaseOverlay", "airbaseMarker", "UnityEngine.UI.Image"),
    ("AirbaseOverlay", "airbaseLabel", "TMPro.TextMeshProUGUI"),
    ("AirbaseOverlay", "glideslope", "UnityEngine.UI.Image"),
    ("AirbaseOverlay", "glideslopeAimPoint", "UnityEngine.UI.Image"),
    ("ObjectiveOverlayManager", "aircraft", "Aircraft"),
    ("ObjectiveOverlayManager", "overlays", "System.Collections.Generic.List`1<ObjectiveOverlay>"),
    ("ObjectiveOverlayManager", "resultCache", "System.Collections.Generic.List`1<MissionPosition+PositionResult>"),
    ("ObjectiveOverlay", "TextNoOverlap", "TextNoOverlap"),
    ("TextNoOverlap", "Text", "TMPro.TextMeshProUGUI"),
    ("TextNoOverlap", "TargetPosition", "UnityEngine.Vector2"),
    ("TextNoOverlap", "PreviousPosition", "UnityEngine.Vector2"),
    ("TextNoOverlap", "NudgeOffset", "UnityEngine.Vector2"),
    ("TextNoOverlap", "AutomaticlalySetPosition", "System.Boolean"),
    // Render-only weapon cues reuse the native solver's cached world points.
    ("HUDBoresightState", "gunDirectionRelative", "UnityEngine.Vector3"),
    ("HUDBoresightState", "boresight", "UnityEngine.UI.Image"),
    ("HUDBoresightState", "projectedPosition", "UnityEngine.UI.Image"),
    ("HUDBoresightState", "targetPosition", "UnityEngine.UI.Image"),
    ("HUDBoresightState", "line", "UnityEngine.UI.Image"),
    ("HUDBoresightState", "controlsFilter", "ControlsFilter"),
    ("ControlsFilter+AimAssist", "accurateAimpoint", "System.Nullable`1<GlobalPosition>"),
    ("HUDTurretState", "crosshairs", "HUDTurretCrosshair[]"),
    ("HUDBombingState", "ccipImpactPointSmoothed", "UnityEngine.Vector3"),
    ("HUDBombingState", "ccipPipper", "UnityEngine.UI.Image"),
    ("HUDBombingState", "ccipFallTime", "TMPro.TextMeshProUGUI"),
    ("HUDBombingState", "ccipLine", "UnityEngine.UI.Image"),
    ("HUDBombingState", "alignmentBar", "UnityEngine.UI.Image"),
    ("HUDBombingState", "averageTargetPosition", "GlobalPosition"),
    ("FlightHud", "compass", "UnityEngine.UI.RawImage"),
    ("FlightHud", "pitchCompassCenter", "UnityEngine.GameObject"),
    ("SpeedGauge", "airspeedDisplay", "TMPro.TextMeshProUGUI"),
    ("SpeedGauge", "border", "UnityEngine.UI.Image"),
    ("AoADisplay", "AoAText", "TMPro.TextMeshProUGUI"),
    ("HeadMountedDisplay", "speed", "HUDApp"),
    ("HeadMountedDisplay", "altitude", "HUDApp"),
    ("HeadMountedDisplay", "bearing", "HUDApp"),
    ("HeadMountedDisplay", "horizon", "HUDApp")
};
foreach (var seam in cameraFields)
{
    FieldInfo field = gameAssembly.GetType(seam.Type, true)!.GetField(seam.Field, AllMembers)
        ?? throw new MissingFieldException(seam.Type, seam.Field);
    Type fieldType = field.FieldType;
    string actualType = fieldType.IsGenericType
        ? fieldType.GetGenericTypeDefinition().FullName + "<" +
            string.Join(",", fieldType.GetGenericArguments().Select(t => t.FullName)) + ">"
        : fieldType.FullName;
    if (actualType != seam.FieldType)
        throw new InvalidOperationException($"Camera seam {seam.Type}.{seam.Field} changed type: {actualType}");
}

// Aircraft.cockpit is a physics part. Its Cockpit behaviour may live on an inactive
// child, so control binding must resolve that component before reflecting its arrays.
Type nativeCockpitPart = gameAssembly.GetType("UnitPart", true)!;
Type nativeControlOwner = gameAssembly.GetType("Cockpit", true)!;
MethodInfo controlOwnerLookup = nativeCockpitPart.GetMethods(AllMembers).FirstOrDefault(method =>
    method.Name == "GetComponentInChildren" && method.IsGenericMethodDefinition &&
    method.GetGenericArguments().Length == 1 &&
    method.GetParameters() is { Length: 1 } parameters && parameters[0].ParameterType == typeof(bool))
    ?? throw new MissingMethodException("UnitPart.GetComponentInChildren<Cockpit>(bool)");
if (controlOwnerLookup.MakeGenericMethod(nativeControlOwner).ReturnType != nativeControlOwner)
    throw new InvalidOperationException("Cockpit component lookup no longer returns the native control owner");

// Cached render-only delegate: a private signature change must fail before shipping.
MethodInfo hitMarkerRefresh = gameAssembly.GetType("CombatHUD", true)!
    .GetMethod("UpdateHitMarkers", AllMembers, null, Type.EmptyTypes, null)
    ?? throw new MissingMethodException("CombatHUD.UpdateHitMarkers()");
if (hitMarkerRefresh.ReturnType != typeof(void))
    throw new InvalidOperationException("CombatHUD.UpdateHitMarkers no longer returns void");

MethodInfo targetCaptionRefresh = gameAssembly.GetType("CombatHUD", true)!
    .GetMethod("ShowTargetInfo", AllMembers, null, Type.EmptyTypes, null)
    ?? throw new MissingMethodException("CombatHUD.ShowTargetInfo()");
if (targetCaptionRefresh.ReturnType != typeof(bool))
    throw new InvalidOperationException("CombatHUD.ShowTargetInfo no longer returns bool");

Type objectiveResult = gameAssembly.GetType("MissionPosition+PositionResult", true)!;
MethodInfo objectiveProjection = gameAssembly.GetType("ObjectiveOverlay", true)!
    .GetMethod("UpdateOverlay", AllMembers, null, new[] { objectiveResult }, null)
    ?? throw new MissingMethodException("ObjectiveOverlay.UpdateOverlay(MissionPosition.PositionResult)");
if (objectiveProjection.ReturnType != typeof(void))
    throw new InvalidOperationException("ObjectiveOverlay.UpdateOverlay no longer returns void");

Type airbaseOverlay = gameAssembly.GetType("AirbaseOverlay", true)!;
Type nativeRunway = gameAssembly.GetType("Airbase+Runway", true)!;
Type nativeRunwayUsage = gameAssembly.GetType("Airbase+Runway+RunwayUsage", true)!;
MethodInfo runwayProjection = airbaseOverlay.GetMethod("DrawRunwayBorders", AllMembers, null,
    new[] { nativeRunway }, null) ?? throw new MissingMethodException("AirbaseOverlay.DrawRunwayBorders");
MethodInfo glideslopeProjection = airbaseOverlay.GetMethod("DrawGlideslope", AllMembers, null,
    new[] { gameAssembly.GetType("Aircraft", true)!, nativeRunwayUsage }, null)
    ?? throw new MissingMethodException("AirbaseOverlay.DrawGlideslope");
if (runwayProjection.ReturnType != typeof(void) || glideslopeProjection.ReturnType != typeof(bool))
    throw new InvalidOperationException("AirbaseOverlay render-only delegate signatures changed");

// Harmony binds patch parameters by name, so a rename in a game update throws at patch
// time rather than degrading. Neither probe checked these names before.
(string Type, string Method, string[] Parameters)[] parameterNames =
{
    ("ControlsFilter", "SetFlightAssist", new[] { "enabled", "aircraft" }),
    ("Missile", "UserCode_RpcDetonate_897349600", new[] { "hitArmor", "useUnit", "hitTerrain" }),
    ("Hangar", "TrySpawnAircraft", new[] { "player", "definition" }),
    ("Aircraft", "UseFuel", new[] { "fuelDrawn" }),
    ("FactionHQ", "RewardPlayer", new[] { "player", "rewardAllocation", "missionType" })
    ,("Unit", "RecordDamage", new[] { "lastDamagedBy", "damageAmount" })
    ,("PilotDismounted", "Capture", new[] { "capturingUnit" })
    ,("Building", "Repair", new[] { "repairer" })
    ,("Rearmer", "ProcessRearmRequest", new[] { "unitToRearm" })
    ,("Rearmer", "RefillOtherRearmer", new[] { "toRearmer" })
    ,("PilotPlayerState", "FixedUpdateState", new[] { "pilot" })
    ,("Capture", "ApplyChange", new[] { "change", "highestHQ" })
    ,("MapBuilding", "TakeShockwave", new[] { "origin", "blastPower" })
    ,("MapBuilding", "TakeDamage", new[] { "pierceDamage", "blastDamage", "amountAffected", "fireDamage", "impactDamage" })
    ,("MapBuildingSet", "UserCode_CmdDestroyBuilding_1002795805", new[] { "index" })
    ,("NuclearOption.Jobs.DetectorManager", "RequestLoSCheck", new[] { "target" })
};
foreach ((string typeName, string methodName, string[] expected) in parameterNames)
{
    MethodInfo method = gameAssembly.GetType(typeName, true)!.GetMethod(methodName, AllMembers)
        ?? throw new MissingMethodException(typeName, methodName);
    string[] actual = method.GetParameters().Select(parameter => parameter.Name!).ToArray();
    foreach (string name in expected)
        if (!actual.Contains(name, StringComparer.Ordinal))
            throw new MissingMemberException(
                $"{typeName}.{methodName} no longer has a parameter named '{name}' " +
                $"(found: {string.Join(", ", actual)}). Harmony binds by name.");
}

// Theater priority binds MissionPosition by parameter name and by overload. The Unit
// overload is public; the Distance overload that sorts depot/airbase delivery is internal,
// so metadata signature probing cannot see it. Pin both by reflection.
MethodInfo priorityAdvance = gameAssembly.GetType("MissionPosition", true)!.GetMethods(AllMembers)
    .FirstOrDefault(method => method.Name == "TryGetClosestPosition" &&
        method.GetParameters() is { Length: 2 } parameters &&
        parameters[0].ParameterType.FullName == "Unit")
    ?? throw new MissingMethodException("MissionPosition.TryGetClosestPosition(Unit, out GlobalPosition)");
string[] priorityAdvanceNames = priorityAdvance.GetParameters().Select(parameter => parameter.Name!).ToArray();
if (!priorityAdvanceNames.SequenceEqual(new[] { "unit", "destination" }))
    throw new MissingMemberException(
        "MissionPosition.TryGetClosestPosition parameters changed: " + string.Join(", ", priorityAdvanceNames));

MethodInfo priorityDistance = gameAssembly.GetType("MissionPosition", true)!.GetMethods(AllMembers)
    .FirstOrDefault(method => method.Name == "TryGetClosestDistance" &&
        method.GetParameters() is { Length: 3 } parameters &&
        parameters[0].ParameterType.FullName == "FactionHQ" &&
        parameters[1].ParameterType.FullName == "UnityEngine.Transform")
    ?? throw new MissingMethodException("MissionPosition.TryGetClosestDistance(FactionHQ, Transform, out float)");
string[] priorityDistanceNames = priorityDistance.GetParameters().Select(parameter => parameter.Name!).ToArray();
if (!priorityDistanceNames.SequenceEqual(new[] { "factionHQ", "transform", "distance" }))
    throw new MissingMemberException(
        "MissionPosition.TryGetClosestDistance parameters changed: " + string.Join(", ", priorityDistanceNames));

Type levelInfo = gameAssembly.GetType("LevelInfo", true)!;
if (levelInfo.GetProperty("LoadedMapSettings", AllMembers) == null)
    throw new MissingMemberException("LevelInfo.LoadedMapSettings");
Type terrainSettings = gameAssembly.GetType("MapSettings", true)!;
if (terrainSettings.GetField("MapImage", AllMembers)?.FieldType.FullName != "UnityEngine.Sprite" ||
    terrainSettings.GetField("MapSize", AllMembers)?.FieldType.FullName != "UnityEngine.Vector2")
    throw new MissingMemberException("MapSettings.MapImage/MapSize terrain projection contract");
if (gameAssembly.GetType("MapIcon", true)!.GetField("globalPosition", AllMembers)?
        .FieldType.FullName != "UnityEngine.Vector3")
    throw new MissingMemberException("MapIcon.globalPosition faction-visible altitude contract");

// Contract markers are drawn by the mod, so the game side of that is only read: the map's
// objective layer toggle and the map's own world-to-map scale, and the style source every
// marker copies (prefab-fed sprites, the HUD label font, the theme palette, the unit system).
if (gameAssembly.GetType("MapOptions", true)!.GetField("showObjectives", AllMembers) == null)
    throw new MissingFieldException("MapOptions.showObjectives");
if (gameAssembly.GetType("DynamicMap", true)!.GetField("mapDisplayFactor", AllMembers) == null)
    throw new MissingFieldException("DynamicMap.mapDisplayFactor");
if (gameAssembly.GetType("ObjectiveOverlayManager", true)!.GetField("overlayPrefab", AllMembers) == null)
    throw new MissingFieldException("ObjectiveOverlayManager.overlayPrefab");
if (gameAssembly.GetType("ObjectiveMarkerManager", true)!.GetField("markerPrefab", AllMembers) == null)
    throw new MissingFieldException("ObjectiveMarkerManager.markerPrefab");
Type objectiveMarker = gameAssembly.GetType("ObjectiveMarker", true)!;
foreach (string sprite in new[] { "destroyObjective", "waypointObjective", "captureObjective", "reconObjective" })
    if (objectiveMarker.GetField(sprite, AllMembers) == null)
        throw new MissingFieldException("ObjectiveMarker." + sprite);
if (gameAssembly.GetType("GameAssets", true)!.GetField("exclusionZoneDisplay", AllMembers) == null)
    throw new MissingFieldException("GameAssets.exclusionZoneDisplay");
Type themeManager = gameAssembly.GetType("NuclearOption.UIStyleSystem.ThemeManager", true)!;
if (themeManager.GetProperty("Active", AllMembers) == null)
    throw new MissingMemberException("ThemeManager.Active");
if (gameAssembly.GetType("PlayerSettings", true)!.GetField("overlayTextSize", AllMembers) == null)
    throw new MissingFieldException("PlayerSettings.overlayTextSize");
if (gameAssembly.GetType("PlayerSettings", true)!.GetField("unitSystem", AllMembers) == null)
    throw new MissingFieldException("PlayerSettings.unitSystem");

string[] patchTypes =
{
    "BoscaliSummer.Fire.BulletImpactPatch",
    "BoscaliSummer.Fire.GroundVehicleDestructionPatch",
    "BoscaliSummer.Fire.MissileImpactPatch",
    "BoscaliSummer.Fire.BuildingHitPatch",
    "BoscaliSummer.Fire.BuildingDestructPatch",
    "BoscaliSummer.Fire.AircraftWreckPersistencePatch",
    "BoscaliSummer.Garrisons.AirbaseCapturePatch",
    "BoscaliSummer.Garrisons.GarrisonClientVisualPatch",
    "BoscaliSummer.Garrisons.MountedTroopsFirePatch",
    "BoscaliSummer.Garrisons.UrbanDefensePatch",
    "BoscaliSummer.Garrisons.SiegeFloorPatch",
    "BoscaliSummer.Garrisons.UrbanArmorPatch",
    "BoscaliSummer.Garrisons.ShellDamagePatch",
    "BoscaliSummer.Garrisons.ShellDestructPatch",
    "BoscaliSummer.Garrisons.StrongpointDamagePatch",
    "BoscaliSummer.Garrisons.ShellShockwavePatch",
    "BoscaliSummer.Garrisons.PartShockwavePatch",
    "BoscaliSummer.Garrisons.NestDamageRedirectPatch",
    "BoscaliSummer.Garrisons.StrongpointClientGuardPatch",
    "BoscaliSummer.Garrisons.DestroyCommandGuardPatch",
    "BoscaliSummer.Modules.Radio.Patches.VanillaPlayMusicPatch",
    "BoscaliSummer.Modules.Radio.Patches.VanillaCrossFadeMusicPatch",
    "BoscaliSummer.Modules.Radio.Patches.VanillaQueueMusicPatch",
    "BoscaliSummer.Modules.Progression.Patches.AircraftFuelUsePatch",
    "BoscaliSummer.Modules.Progression.Patches.AircraftEngineMapPatch",
    "BoscaliSummer.Modules.Progression.Patches.RewardAllocationPatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdRailPatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdReliefMapCursorPatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdReliefMapBoundsPatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdReliefGridLabelsPatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdReliefUnitIconPatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdReliefAirbaseIconPatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdScreenChromePatch",
    "BoscaliSummer.Modules.Command.Presentation.MapUi.MfdSinglePanelPatch",
    "BoscaliSummer.Modules.Command.Patches.DynamicMapMaximizePatch",
    "BoscaliSummer.Modules.Command.Patches.DynamicMapMinimizePatch",
    "BoscaliSummer.Modules.Command.Patches.HoloMapKeyScopePatch",
    "BoscaliSummer.Modules.Command.Patches.HoloMapKeyPatch",
    "BoscaliSummer.Modules.Command.Patches.MapControlsPanelGuardPatch",
    "BoscaliSummer.Modules.Command.Patches.MapCursorPanelGuardPatch",
    "BoscaliSummer.Modules.Command.Patches.GridLabelsPatch",
    "BoscaliSummer.Modules.Support.Patches.SupportMissileDetonatePatch",
    "BoscaliSummer.Modules.Support.Patches.SupportMissileAuthorityPatch",
    "BoscaliSummer.Modules.Support.Patches.SupportMissileDescentPatch",
    "BoscaliSummer.Modules.Support.Patches.CyberLaunchMountPatch",
    "BoscaliSummer.Modules.Support.Patches.CyberLaunchFirePatch",
    "BoscaliSummer.Modules.Support.Patches.CyberDetectScopePatch",
    "BoscaliSummer.Modules.Support.Patches.CyberShareBlockPatch",
    "BoscaliSummer.Modules.Support.Patches.CyberUnitKilledPatch",


    "BoscaliSummer.Modules.QoL.Patches.NightVisionChoicePatch",
    "BoscaliSummer.Modules.PlayerSpawnPriority.PlayerSpawnPriorityPatch",
    "BoscaliSummer.Modules.Autopilot.Patches.AutopilotLandInputPatch",
    "BoscaliSummer.Modules.Autopilot.Patches.FlightAssistReportPatch",
    "BoscaliSummer.Modules.Autopilot.Patches.RadialMenuLifecyclePatches",
    "BoscaliSummer.Modules.Autopilot.Patches.BoscaliMenuActionPatches"
    ,"BoscaliSummer.Modules.Squad.Patches.SquadDamagePatch"
    ,"BoscaliSummer.Modules.Squad.Patches.SquadKillPatch"
    ,"BoscaliSummer.Modules.Squad.Patches.SquadPilotDeathPatch"
    ,"BoscaliSummer.Modules.DynamicOperations.Runtime.OperationJamPatch"
    ,"BoscaliSummer.Modules.DynamicOperations.Runtime.OperationRescuePatch"
    ,"BoscaliSummer.Modules.DynamicOperations.Runtime.OperationRepairPatch"
    ,"BoscaliSummer.Modules.DynamicOperations.Runtime.OperationSupplyPatch"
    ,"BoscaliSummer.Modules.DynamicOperations.Runtime.OperationSupplyTransferPatch"
    ,"BoscaliSummer.Modules.HighCommand.Patches.HighCommandDamagePatch"
    ,"BoscaliSummer.Modules.TheaterOps.Patches.MissionPositionAdvancePriorityPatch"
    ,"BoscaliSummer.Modules.TheaterOps.Patches.MissionPositionDeliveryPriorityPatch"
    ,"BoscaliSummer.Modules.TheaterOps.Patches.GroundFrontDepotPatch"
    ,"BoscaliSummer.Modules.TheaterOps.Runtime.NavalFrontChooseTargetPatch"
    ,"BoscaliSummer.Modules.AirSurvival.Patches.AirMissionStationPatch"
    ,"BoscaliSummer.Modules.AirSurvival.Patches.AirMissileFlarePatch"
    ,"BoscaliSummer.Modules.Trenches.Visuals.TrenchNestClientPatch"
    ,"BoscaliSummer.Modules.Trenches.Visuals.TrenchNestServerPatch"
    ,"BoscaliSummer.Modules.Comms.Patches.CommsMapControlsPatch"
    ,"BoscaliSummer.Modules.Trenches.Runtime.TrenchWorksDetectionPatch"
    ,"BoscaliSummer.Modules.Hud.Patches.WingviewCameraPatch"
    ,"BoscaliSummer.Modules.Hud.Patches.ShotDetonatePatch"
    ,"BoscaliSummer.Modules.Hud.Patches.EventFeedPatch"
    ,"BoscaliSummer.Modules.Weather.Patches.CloudSeekerPatch"
};

foreach (string patchType in patchTypes)
    if (pluginAssembly.GetType(patchType, false) == null)
        throw new TypeLoadException("Plugin patch type missing: " + patchType);

// A [HarmonyPatch] class that no module lists in PatchTypes is never installed, and nothing logs it.
Type moduleContract = pluginAssembly.GetType("BoscaliSummer.Core.Modules.IModule", true)!;
PropertyInfo modulePatchTypes = moduleContract.GetProperty("PatchTypes")!;
var registeredPatchTypes = new HashSet<Type>();
foreach (Type module in pluginAssembly.GetTypes())
    if (!module.IsAbstract && moduleContract.IsAssignableFrom(module))
        registeredPatchTypes.UnionWith((Type[])modulePatchTypes.GetValue(Activator.CreateInstance(module, true)) ?? []);
string[] unregisteredPatchTypes = pluginAssembly.GetTypes()
    .Where(type => !registeredPatchTypes.Contains(type) &&
        type.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Name == "HarmonyPatch"))
    .Select(type => type.FullName!)
    .ToArray();
if (unregisteredPatchTypes.Length > 0)
    throw new InvalidOperationException(
        "Harmony patch classes no module registers: " + string.Join(", ", unregisteredPatchTypes));

string[] featureTypes =
{
    "BoscaliSummer.Modules.FireAndDestruction.FireAndDestructionModule",
    "BoscaliSummer.Modules.UrbanCombat.UrbanCombatModule",
    "BoscaliSummer.Modules.Radio.RadioModule",
    "BoscaliSummer.Modules.Progression.ProgressionModule",
    "BoscaliSummer.Modules.Support.SupportModule",
    "BoscaliSummer.Modules.QoL.QoLModule",
    "BoscaliSummer.Modules.PlayerSpawnPriority.PlayerSpawnPriorityModule",
    "BoscaliSummer.Modules.Autopilot.AutopilotModule",
    "BoscaliSummer.Modules.Command.CommandModule"
    ,"BoscaliSummer.Modules.Squad.SquadModule"
    ,"BoscaliSummer.Modules.HighCommand.HighCommandModule"
    ,"BoscaliSummer.Modules.TheaterOps.TheaterOpsModule"
    ,"BoscaliSummer.Modules.AirSurvival.AirSurvivalModule"
    ,"BoscaliSummer.Modules.Events.EventsModule"
    ,"BoscaliSummer.Modules.Trenches.TrenchesModule"
    ,"BoscaliSummer.Modules.Hud.HudModule"
    ,"BoscaliSummer.Modules.Comms.CommsModule"
    ,"BoscaliSummer.Modules.Session.SessionModule"
    ,"BoscaliSummer.Modules.Intel.IntelModule"
    ,"BoscaliSummer.Modules.Performance.PerformanceModule"
};
foreach (string featureType in featureTypes)
    if (pluginAssembly.GetType(featureType, false) == null)
        throw new TypeLoadException("Plugin feature type missing: " + featureType);

string[] radioResources =
{
    "BoscaliSummer.RadioAssets.agrapol-fm.png",
    "BoscaliSummer.RadioAssets.maris-network.png",
    "BoscaliSummer.RadioAssets.base-broadcast.png",
    "BoscaliSummer.RadioAssets.stations-readme.txt"
};
string[] resources = pluginAssembly.GetManifestResourceNames();
foreach (string resource in radioResources)
    if (!resources.Contains(resource, StringComparer.Ordinal))
        throw new MissingManifestResourceException("Plugin radio asset missing: " + resource);

using (Stream weatherResource = pluginAssembly.GetManifestResourceStream("BoscaliSummer.Weather.weatherrain.bundle")
    ?? throw new MissingManifestResourceException("Plugin weather shader bundle missing"))
{
    if (weatherResource.Length <= 0 || weatherResource.Length > 4 * 1024 * 1024)
        throw new InvalidDataException("Plugin weather shader bundle exceeds its bounded loader contract");
    string weatherHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(weatherResource));
    Console.WriteLine($"  Embedded weather shader bundle: {weatherResource.Length} bytes; SHA256 {weatherHash}");
}

using (Stream pilotResource = pluginAssembly.GetManifestResourceStream("BoscaliSummer.Immersion.pilot.bundle")
    ?? throw new MissingManifestResourceException("Plugin cockpit pilot bundle missing"))
{
    if (pilotResource.Length <= 0 || pilotResource.Length > 4 * 1024 * 1024)
        throw new InvalidDataException("Plugin cockpit pilot bundle exceeds its bounded loader contract");
    string pilotHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pilotResource));
    Console.WriteLine($"  Embedded cockpit pilot bundle: {pilotResource.Length} bytes; SHA256 {pilotHash}");
}

// Campaign is outside the startup roster and its payload is not currently embedded.

(string Type, string Field, Type FieldType)[] messageFields =
{
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.FireIgnitedMessage", "X", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.FireIgnitedMessage", "Y", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.FireIgnitedMessage", "Z", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.FireIgnitedMessage", "RemainingLifetime", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.FireIgnitedMessage", "ClusterScale", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.FireIgnitedMessage", "Forest", typeof(bool)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.RuinCreatedMessage", "X", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.RuinCreatedMessage", "Y", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.RuinCreatedMessage", "Z", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.RuinCreatedMessage", "HalfX", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.RuinCreatedMessage", "HalfZ", typeof(float)),
    ("BoscaliSummer.Modules.FireAndDestruction.Networking.RuinCreatedMessage", "AgeSeconds", typeof(float))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSubmit", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSubmit", "Perk", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "PerkMask", typeof(uint))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "Score", typeof(int))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "EarnedPoints", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "Rank", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "Result", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "Generation", typeof(int))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "ScorePerPoint", typeof(int))
    ,("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", "MaximumPoints", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.PlaneTuneRequest", "AircraftId", typeof(uint))
    ,("BoscaliSummer.Modules.Progression.Networking.PlaneTuneState", "AircraftId", typeof(uint))
    ,("BoscaliSummer.Modules.Progression.Networking.CrestSubmit", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.CrestSubmit", "Paint", typeof(byte))
    ,("BoscaliSummer.Modules.Progression.Networking.CrestState", "Owner", typeof(ulong))
    ,("BoscaliSummer.Modules.Progression.Networking.CrestState", "Paint", typeof(byte))
    ,("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", "Action", typeof(byte))
    ,("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", "X", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", "Y", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", "Z", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Radius", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Duration", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "X", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Y", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Z", typeof(float))

    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Action", typeof(byte))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Result", typeof(byte))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "CooldownSeconds", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", "Contacts", typeof(int))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandQuery", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandQuery", "Scene", typeof(uint))
,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandQuery", "Token", typeof(uint))
,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Id", typeof(int))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "ParentId", typeof(int))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Tier", typeof(byte))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Flags", typeof(byte))
,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "TraitMask", typeof(byte))
,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Seed", typeof(int))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "IntelAge", typeof(float))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Weight", typeof(float))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "X", typeof(float))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Z", typeof(float))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Name", typeof(string))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Rank", typeof(string))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Role", typeof(string))
,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Location", typeof(string))
,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Site", typeof(string))
,("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", "Bounty", typeof(int))
,("BoscaliSummer.Modules.HighCommand.Networking.CommanderLogWire", "TargetId", typeof(int))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderLogWire", "Tone", typeof(byte))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderLogWire", "Text", typeof(string))
    ,("BoscaliSummer.Modules.HighCommand.Networking.CommanderLogWire", "Age", typeof(float))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Scene", typeof(uint))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Token", typeof(uint))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Status", typeof(string))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Signal", typeof(string))
,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Cohesion", typeof(float))
,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Active", typeof(int))
    ,("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", "Kia", typeof(int))
};
foreach ((string typeName, string fieldName, Type fieldType) in messageFields)
{
    Type messageType = pluginAssembly.GetType(typeName, true)!;
    FieldInfo field = messageType.GetField(fieldName, AllMembers) ??
        throw new MissingFieldException(typeName, fieldName);
    if (field.FieldType != fieldType)
        throw new TypeLoadException(
            $"Message field type changed: {typeName}.{fieldName} is {field.FieldType}, expected {fieldType}.");
}

Type networkTime = mirageAssembly.GetType("Mirage.NetworkTime", true)!;
if (networkTime.GetProperty("Time", AllMembers) == null)
    throw new MissingMemberException("Mirage.NetworkTime.Time");
Type networkServer = mirageAssembly.GetType("Mirage.NetworkServer", true)!;
if (!networkServer.GetMethods(AllMembers).Any(method => method.Name == "SendToAll"))
    throw new MissingMethodException("Mirage.NetworkServer", "SendToAll");
Type messageHandler = mirageAssembly.GetType("Mirage.MessageHandler", true)!;
if (!messageHandler.GetMethods(AllMembers).Any(method => method.Name == "RegisterHandler"))
    throw new MissingMethodException("Mirage.MessageHandler", "RegisterHandler");

// Dynamic operations: exact native reward/road signatures, including Unity Mono types
// which cannot safely be materialized by CoreCLR reflection.
string operationAssembly = Path.Combine(managedDir, "Assembly-CSharp.dll");
(string Type, string Method, string Signature)[] operationMethods =
{
    ("Spawner", "SpawnVehicle", "GroundVehicle(UnityEngine.GameObject,GlobalPosition,UnityEngine.Quaternion,UnityEngine.Vector3,FactionHQ,System.String,System.Single,System.Boolean,NuclearOption.Networking.Player)"),
    ("Spawner", "SpawnBuilding", "Building(UnityEngine.GameObject,GlobalPosition,UnityEngine.Quaternion,FactionHQ,Airbase,System.String,System.Boolean,NuclearOption.SavedMission.SavedBuilding+FactoryOptions)"),
    ("UnitCommand", "SetDestination", "System.Void(GlobalPosition,System.Boolean)"),
    ("Unit", "add_onDisableUnit", "System.Void(System.Action`1<Unit>)"),
    ("Unit", "remove_onDisableUnit", "System.Void(System.Action`1<Unit>)"),
    ("Unit", "Jam", "System.Void(Unit+JamEventArgs)"),
    ("PilotDismounted", "Capture", "System.Void(Unit)"),
    ("Building", "Repair", "System.Void(Unit,System.Single)"),
    ("Building", "NeedsRepair", "System.Boolean()"),
    ("Building", "IsRepairable", "System.Boolean()"),
    ("Rearmer", "ProcessRearmRequest", "System.Boolean(Unit,System.Int32&)"),
    ("Rearmer", "RefillOtherRearmer", "System.Void(Rearmer)"),
    ("Aircraft", "IsLanded", "System.Boolean()"),
    ("Unit", "get_NetworkunitState", "Unit+UnitState()"),
    ("GroundVehicle", "get_UnitCommand", "UnitCommand()"),
    ("GroundVehicle", "SetHoldPosition", "System.Void(System.Boolean)"),
    ("GroundVehicle", "MoveFromDepot", "System.Void()"),
    ("GroundVehicle", "GetHoldPosition", "System.Boolean()"),
    ("GroundVehicle", "GetDestination", "GlobalPosition()"),
    ("GroundVehicle", "IsSlung", "System.Boolean()"),
    ("FactionHQ", "TryGetNearestGroundEnemy", "System.Boolean(GlobalPosition,TrackingInfo&)"),
    ("PathfindingAgent", "RaycastTerrain", "System.Boolean(GlobalPosition,UnityEngine.RaycastHit&)"),
    ("FactionHQ", "RewardPlayer", "System.Void(NuclearOption.Networking.Player,Unit,System.Single,System.Single,FactionHQ+RewardType)"),
    ("FactionHQ", "GetTrackingData", "TrackingInfo(PersistentID)"),
    ("Airbase", "get_AttachedAirbase", "System.Boolean()"),
    ("Airbase", "get_CurrentHQ", "FactionHQ()"),
    ("Airbase", "get_disabled", "System.Boolean()"),
    ("Airbase", "get_capture", "Capture()"),
    ("Airbase", "get_SavedAirbase", "NuclearOption.SavedMission.SavedAirbase()"),
    ("Capture", "get_controlBalance", "System.Single()"),
    ("Capture", "get_capturingHQ", "FactionHQ()"),
    ("LevelInfo", "get_roadNetwork", "RoadPathfinding.RoadNetwork()"),
    ("RoadPathfinder", "TryPathfind", "System.Void(RoadPathfinding.RoadNetwork,GlobalPosition,GlobalPosition,System.Collections.Generic.List`1<RoadPathfinding.Node>,RoadPathfinder+PathfindResult&)"),
    ("RoadPathfinding.Road", "IsBridge", "System.Boolean()"),
    ("GlobalPosition", "AsVector3", "UnityEngine.Vector3()"),
    ("UnitDefinition", "IsAllowed", "System.Boolean(System.Boolean)"),
    ("NuclearOption.Networking.Player", "get_HQ", "FactionHQ()"),
    ("NuclearOption.Networking.Player", "AddScore", "System.Void(System.Single)"),
    ("NuclearOption.Networking.Player", "AddAllocation", "System.Void(System.Single)"),
    ("MissionManager", "get_IsRunning", "System.Boolean()"),
    ("MissionManager", "get_MissionTime", "System.Single()")
    ,("Unit", "RecordDamage", "System.Void(PersistentID,System.Single)")
    ,("Unit", "ReportKilled", "System.Void()")
    ,("Pilot", "ApplyDamage", "System.Void(System.Single,System.Single,System.Single,System.Single)")
    ,("UnitRegistry", "TryGetPersistentUnit", "System.Boolean(PersistentID,PersistentUnit&)")
    ,("FactionHelper", "EmptyOrNoFactionOrNeutral", "System.Boolean(System.String)")
    ,("MissionPosition", "GetAllPositionsResults", "System.Void(FactionHQ,GlobalPosition,System.Boolean,System.Collections.Generic.List`1<MissionPosition+PositionResult>)")
    ,("MissionPosition", "DistanceTo", "System.Boolean(NuclearOption.SavedMission.Objective,GlobalPosition,MissionPosition+PositionResult&)")
    ,("MissionPosition", "TryGetClosestPosition", "System.Boolean(Unit,GlobalPosition&)")
    ,("NuclearOption.SavedMission.SavedObjective", "CreateSavedObjective", "NuclearOption.SavedMission.SavedObjective(NuclearOption.SavedMission.ObjectiveType,System.String)")
    ,("GlobalPosition", ".ctor", "System.Void(System.Single,System.Single,System.Single)")
    ,("NuclearOption.SavedMission.ObjectivePosition", ".ctor", "System.Void(GlobalPosition,System.Nullable`1<System.Single>)")
    ,("GameManager", "GetLocalAircraft", "System.Boolean(Aircraft&)")
    ,("Aircraft", "HasEjected", "System.Boolean()")
    ,("FactionHQ", "AddConvoy", "System.Void(Faction+ConvoyGroup)")
    ,("FactionHQ", "CmdGetDelaySpawnConvoy", "System.Single(System.Byte)")
    ,("FactionHQ", "AddFunds", "System.Void(System.Single)")
    ,("FactionHQ", "get_factionFunds", "System.Single()")
    ,("Faction", "GetConvoyGroups", "System.Collections.Generic.List`1<Faction+ConvoyGroup>()")
    ,("Rearmer", "GetMaxCapacity", "System.Single()")
};
foreach (var seam in operationMethods)
    RequireMetadataSignature(operationAssembly, seam.Type, seam.Method, seam.Signature);

// Fire scorch: the module paints the ash bed straight into the vanilla blast map and sizes
// tree removal with a separate AddBlast, so both calls and the blast payload are seams.
RequireMetadataSignature(operationAssembly, "NuclearOption.Effects.BlastManager", "AddBlast",
    "System.Void(GlobalPosition,System.Single)");
RequireMetadataSignature(operationAssembly, "NuclearOption.Effects.BlastManager", "DrawBlast",
    "System.Void(UnityEngine.Rendering.CommandBuffer,NuclearOption.Effects.BlastManager+DetailBlast)");
RequireMetadataSignature(operationAssembly, "NuclearOption.Effects.BlastManager+DetailBlast", ".ctor",
    "System.Void(GlobalPosition,System.Single)");
RequireMetadataSignature(Path.Combine(managedDir, "Mirage.dll"), "Mirage.ServerObjectManager", "Destroy", "System.Void(UnityEngine.GameObject,System.Boolean)");
(string Type, string Field, string FieldType)[] operationFields =
{
    ("FactionRegistry", "airbaseLookup", "System.Collections.Generic.Dictionary`2<System.String,Airbase>"),
    ("FactionHQ", "factionPlayers", "Mirage.Collections.SyncList`1<NuclearOption.Networking.PlayerRef>"),
    ("TrackingInfo", "lastKnownPosition", "GlobalPosition"),
    ("TrackingInfo", "lastSpottedTime", "System.Single"),
    ("Rearmer", "Unit", "Unit"),
    ("Rearmer", "Capacity", "System.Single"),
    ("Airbase", "center", "UnityEngine.Transform"),
    ("NuclearOption.SavedMission.SavedAirbase", "Capturable", "System.Boolean"),
    ("RoadPathfinding.RoadNetwork", "roads", "System.Collections.Generic.List`1<RoadPathfinding.Road>"),
    ("RoadPathfinding.RoadNetwork", "nodes", "System.Collections.Generic.List`1<RoadPathfinding.Node>"),
    ("RoadPathfinding.Road", "points", "System.Collections.Generic.List`1<GlobalPosition>"),
    ("GameAssets", "terrainMaterial", "UnityEngine.PhysicMaterial"),
    ("PhysicsLayers", "StaticsMask", "UnityEngine.LayerMask"),
    ("Encyclopedia", "vehicles", "System.Collections.Generic.List`1<VehicleDefinition>"),
    ("Encyclopedia", "buildings", "System.Collections.Generic.List`1<BuildingDefinition>"),
    ("Encyclopedia", "scenery", "System.Collections.Generic.List`1<SceneryDefinition>"),
    ("Encyclopedia", "otherUnits", "System.Collections.Generic.List`1<UnitDefinition>")
    ,("PersistentUnit", "player", "NuclearOption.Networking.Player")
    ,("Pilot", "dead", "System.Boolean")
    ,("Pilot", "ejected", "System.Boolean")
    ,("Pilot", "aircraft", "Aircraft")
    ,("Aircraft", "pilots", "Pilot[]")
    ,("FactionHQ", "RearmMissionController", "RearmMissionController")
    ,("RearmMissionController", "Rearmers", "System.Collections.Generic.List`1<Rearmer>")
    ,("RearmMissionController", "UnitsNeedingRearm", "Mirage.Collections.SyncList`1<Unit>")
    ,("Rearmer", "AvailableForMission", "System.Boolean")
};
foreach (var seam in operationFields)
    RequireMetadataField(operationAssembly, seam.Type, seam.Field, seam.FieldType);
// Intel (S1) reads each faction's own tracking through public seams only; it has no patch
// targets. The discover/forget events, the tracking database, a missile's owner and weapon,
// the weapon-station envelope and the radar-carrier test are its whole surface.
(string Type, string Method, string Signature)[] intelMethods =
{
    ("FactionHQ", "add_onDiscoverUnit", "System.Void(System.Action`1<PersistentID>)"),
    ("FactionHQ", "remove_onDiscoverUnit", "System.Void(System.Action`1<PersistentID>)"),
    ("FactionHQ", "add_onForgetUnit", "System.Void(System.Action`1<PersistentID>)"),
    ("FactionHQ", "remove_onForgetUnit", "System.Void(System.Action`1<PersistentID>)"),
    ("FactionRegistry", "GetAllHQs", "System.Collections.Generic.Dictionary`2+ValueCollection<Faction,FactionHQ>()"),
    ("GameManager", "GetLocalHQ", "System.Boolean(FactionHQ&)"),
    ("TrackingInfo", "TryGetUnit", "System.Boolean(Unit&)"),
    ("UnitRegistry", "TryGetUnit", "System.Boolean(System.Nullable`1<PersistentID>,Unit&)"),
    ("Unit", "HasRadarEmission", "System.Boolean()"),
    ("Radar", "IsJammed", "System.Boolean()"),
    ("Unit", "get_NetworkHQ", "FactionHQ()"),
    ("Unit", "get_SavedUnit", "NuclearOption.SavedMission.SavedUnit()"),
    ("Missile", "GetWeaponInfo", "WeaponInfo()"),
    ("GlobalPositionExtensions", "GlobalPosition", "GlobalPosition(Unit)")
};
foreach (var seam in intelMethods)
    RequireMetadataSignature(operationAssembly, seam.Type, seam.Method, seam.Signature);
(string Type, string Field, string FieldType)[] intelFields =
{
    ("FactionHQ", "trackingDatabase", "System.Collections.Generic.Dictionary`2<PersistentID,TrackingInfo>"),
    ("FactionHQ", "factionUnits", "Mirage.Collections.SyncList`1<PersistentID>"),
    ("PersistentID", "Id", "System.UInt32"),
    ("Missile", "ownerID", "PersistentID"),
    ("Unit", "radar", "TargetDetector"),
    ("Unit", "weaponStations", "System.Collections.Generic.List`1<WeaponStation>"),
    ("Unit", "definition", "UnitDefinition"),
    ("Unit", "UniqueName", "System.String"),
    ("WeaponStation", "WeaponInfo", "WeaponInfo"),
    ("WeaponInfo", "effectiveness", "RoleIdentity"),
    ("WeaponInfo", "targetRequirements", "TargetRequirements"),
    ("WeaponInfo", "gun", "System.Boolean"),
    ("WeaponInfo", "jammer", "System.Boolean"),
    ("TargetRequirements", "maxRange", "System.Single"),
    ("TargetRequirements", "minAltitude", "System.Single"),
    ("TargetRequirements", "maxAltitude", "System.Single"),
    ("TargetRequirements", "minIR", "System.Single"),
    ("TargetRequirements", "minRadar", "System.Single"),
    ("RoleIdentity", "antiAir", "System.Single"),
    ("RoleIdentity", "antiSurface", "System.Single"),
    ("UnitDefinition", "typeIdentity", "TypeIdentity"),
    ("UnitDefinition", "roleIdentity", "RoleIdentity"),
    ("UnitDefinition", "code", "System.String"),
    ("TypeIdentity", "radar", "System.Single"),
    ("GlobalPosition", "x", "System.Single"),
    ("GlobalPosition", "z", "System.Single")
};
foreach (var seam in intelFields)
    RequireMetadataField(operationAssembly, seam.Type, seam.Field, seam.FieldType);
// TAC BOARD SYSTEMS page: a helicopter is told by its rotor shaft (DamageWatch).
if (gameAssembly.GetType("RotorShaft", false) == null) throw new TypeLoadException("RotorShaft");
if (gameAssembly.GetType("Radar", true)!.BaseType?.FullName != "TargetDetector")
    throw new TypeLoadException("Radar no longer derives from TargetDetector; Intel reads Unit.radar as a Radar");
foreach (string type in new[] {
    "BoscaliSummer.Core.Contracts.IThreatPicture",
    "BoscaliSummer.Modules.Intel.Runtime.ThreatPictureService" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
if (Convert.ToInt32(Enum.Parse(gameAssembly.GetType("FactionHQ+RewardType", true)!, "None")) != 0)
    throw new InvalidOperationException("Dynamic operations reward category changed");
foreach (string type in new[] {
    "BoscaliSummer.Modules.DynamicOperations.DynamicOperationsModule",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.OperationsManager",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.OperationRewards",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.ContractMarker",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.ContractHud",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.ContractMapTag",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.ContractMapHud",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.OperationZoneHud",
    "BoscaliSummer.Modules.DynamicOperations.Networking.OperationsNet" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Core.Contracts.IHudBoard",
    "BoscaliSummer.Core.Contracts.IHudLine",
    "BoscaliSummer.Core.Contracts.HudLayout",
    "BoscaliSummer.Core.Contracts.ITacBoardSettings",
    "BoscaliSummer.Modules.Hud.Configuration.HudSettings",
    "BoscaliSummer.Modules.Hud.Presentation.HudBoard",
    "BoscaliSummer.Modules.Hud.Presentation.StatusPanel",
    // TAC BOARD (HUD v8): the offline check reaches these by name; the page groups' feeds register through them.
    "BoscaliSummer.Core.Contracts.ScopeFeeds",
    "BoscaliSummer.Core.Contracts.ScopeGauges",
    "BoscaliSummer.Core.Contracts.GaugeSpec",
    "BoscaliSummer.Modules.Hud.Domain.SystemsScope",
    // Corner takeover: the stores header's feed structs are filled by name in the offline fixtures; NativeCorner is the one class that hides native objects.
    "BoscaliSummer.Modules.Hud.Domain.StoresScope",
    "BoscaliSummer.Modules.Hud.Domain.StoreStation",
    "BoscaliSummer.Modules.Hud.Domain.StoreClass",
    "BoscaliSummer.Modules.Hud.Domain.ThreatScope",
    "BoscaliSummer.Modules.Hud.Domain.ThreatMissile",
    "BoscaliSummer.Modules.Hud.Domain.ThreatEmitter",
    "BoscaliSummer.Modules.Hud.Domain.ThreatSeeker",
    "BoscaliSummer.Modules.Hud.Presentation.StoresHeader",
    "BoscaliSummer.Modules.Hud.Presentation.ThreatPage",
    "BoscaliSummer.Modules.Hud.Runtime.StoresFeed",
    "BoscaliSummer.Modules.Hud.Runtime.NativeCorner",
    "BoscaliSummer.Modules.Hud.Presentation.ScopePages",
    "BoscaliSummer.Modules.Hud.Presentation.SystemsPage",
    // APPROACH, WING and DOCK (tb-flight): their feed structs and enums are filled by name in the offline fixtures.
    "BoscaliSummer.Core.Contracts.ApproachScope",
    "BoscaliSummer.Core.Contracts.ApproachStage",
    "BoscaliSummer.Core.Contracts.WingBoardScope",
    "BoscaliSummer.Core.Contracts.WingBoardMember",
    "BoscaliSummer.Core.Contracts.WingBoardAck",
    "BoscaliSummer.Core.Contracts.WingState",
    "BoscaliSummer.Core.Contracts.DockScope",
    "BoscaliSummer.Core.Contracts.DockPhase",
    "BoscaliSummer.Modules.Hud.Presentation.ApproachPage",
    "BoscaliSummer.Modules.Hud.Presentation.WingPage",
    "BoscaliSummer.Modules.Hud.Presentation.DockPage",
    "BoscaliSummer.Modules.Autopilot.Runtime.ApproachScopeFeed",
    "BoscaliSummer.Modules.Wing.Runtime.WingScopeFeed",
    "BoscaliSummer.Modules.Vanguard.Presentation.SkywellScopeFeed",
    // STRIKE and OBJECTIVE (tb-ops): their feed structs and enums are filled by name in the offline fixtures.
    "BoscaliSummer.Core.Contracts.StrikeScope",
    "BoscaliSummer.Core.Contracts.StrikeTrack",
    "BoscaliSummer.Core.Contracts.PerkTile",
    "BoscaliSummer.Core.Contracts.StrikeKind",
    "BoscaliSummer.Core.Contracts.PerkFront",
    "BoscaliSummer.Core.Contracts.PerkPhase",
    "BoscaliSummer.Core.Contracts.ChainNodeKind",
    "BoscaliSummer.Core.Contracts.ObjectiveScope",
    "BoscaliSummer.Modules.Hud.Presentation.StrikePage",
    "BoscaliSummer.Modules.Hud.Presentation.ObjectivePage",
    "BoscaliSummer.Modules.Support.Runtime.StrikeScopeFeed",
    "BoscaliSummer.Modules.DynamicOperations.Runtime.ObjectiveScopeFeed" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Modules.HighCommand.Runtime.HighCommandManager",
    "BoscaliSummer.Modules.HighCommand.Networking.HighCommandNet",
    "BoscaliSummer.Modules.HighCommand.Domain.CommandTree",
    "BoscaliSummer.Modules.HighCommand.Domain.CommandLog",
    // S3 wingposts: the pure command-post rules the HighCommand repair (S8) reads.
    "BoscaliSummer.Modules.HighCommand.Domain.BountyLedger",
    "BoscaliSummer.Modules.HighCommand.Domain.PostIntel",
    "BoscaliSummer.Core.Contracts.IHighCommandView" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Modules.TheaterOps.Runtime.TheaterPriorityService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.TheaterLogisticsService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.GroundFrontService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.LivingFrontService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.NavalFrontService",
    "BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontNet",
    "BoscaliSummer.Modules.TheaterOps.Networking.TheaterOpsNet",
    "BoscaliSummer.Modules.TheaterOps.Domain.PriorityTable",
    // S3 sorties: the pure wave rules the reinforcement desk (S7) reads.
    "BoscaliSummer.Modules.TheaterOps.Domain.WavePolicy",
    "BoscaliSummer.Modules.TheaterOps.Domain.WaveLedger",
    "BoscaliSummer.Core.Contracts.ITheaterWarView",
    "BoscaliSummer.Core.Contracts.ITheaterAirStationView",
    "BoscaliSummer.Core.Contracts.ITheaterWarHost",
    "BoscaliSummer.Core.Contracts.ITheaterCensusSource",
    // S2 freeze: the cross-module doors that later slices implement.
    "BoscaliSummer.Core.Contracts.IOperationFocusSource",
    "BoscaliSummer.Core.Contracts.ISortieOutcomeSource",
    "BoscaliSummer.Core.Contracts.IConvoyPurchase",
    "BoscaliSummer.Core.Contracts.IWingSortieOrders",
    "BoscaliSummer.Core.Contracts.ISortieWingOrderSource" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
Type highCommandSnapshot = pluginAssembly.GetType("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", true)!;
Type commanderWire = pluginAssembly.GetType("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", true)!;
Type commanderLog = pluginAssembly.GetType("BoscaliSummer.Modules.HighCommand.Networking.CommanderLogWire", true)!;
FieldInfo snapshotNodes = highCommandSnapshot.GetField("Nodes", AllMembers) ??
    throw new MissingFieldException(highCommandSnapshot.FullName, "Nodes");
if (snapshotNodes.FieldType != commanderWire.MakeArrayType())
    throw new InvalidOperationException("High command snapshot node array type changed");
FieldInfo snapshotLog = highCommandSnapshot.GetField("Log", AllMembers) ??
    throw new MissingFieldException(highCommandSnapshot.FullName, "Log");
FieldInfo snapshotHostileLog = highCommandSnapshot.GetField("HostileLog", AllMembers) ??
    throw new MissingFieldException(highCommandSnapshot.FullName, "HostileLog");
if (snapshotLog.FieldType != commanderLog.MakeArrayType() || snapshotHostileLog.FieldType != commanderLog.MakeArrayType())
    throw new InvalidOperationException("High command snapshot log array type changed");
Type highCommandNet = pluginAssembly.GetType("BoscaliSummer.Modules.HighCommand.Networking.HighCommandNet", true)!;
    if ((byte)highCommandNet.GetField("ProtocolVersion", AllMembers)!.GetRawConstantValue()! != 5)
    throw new InvalidOperationException("High command protocol changed without updating its probe");
Type weatherNet = pluginAssembly.GetType("BoscaliSummer.Modules.Weather.Networking.WeatherNet", true)!;
// Version 8 carries the whole weather-tape key (seed, story, pin, salt, class, opening base, heading, epoch,
// offset, speed, dynamic, sets, anchor, set origins) and drops the stepping timeline's fields.
if ((byte)weatherNet.GetField("ProtocolVersion", AllMembers)!.GetRawConstantValue()! != 8)
    throw new InvalidOperationException("Weather protocol changed without updating its probe");
Type tapeGolden = pluginAssembly.GetType("BoscaliSummer.Modules.Weather.Domain.TapeGolden", true)!;
uint liveGolden = (uint)tapeGolden.GetMethod("Compute", AllMembers)!.Invoke(null, null)!;
if ((uint)weatherNet.GetField("TapeGoldenVector", AllMembers)!.GetRawConstantValue()! != liveGolden)
    throw new InvalidOperationException("The weather tape tables changed without updating WeatherNet.TapeGoldenVector (and the protocol at release)");
foreach (var contract in new[] {
    ("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "Action:System.Byte", "X:System.Single", "Y:System.Single", "Z:System.Single" }),
    ("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "Action:System.Byte", "Result:System.Byte", "CooldownSeconds:System.Single", "Radius:System.Single", "Duration:System.Single", "Contacts:System.Int32", "X:System.Single", "Y:System.Single", "Z:System.Single" }),
    ("BoscaliSummer.Modules.Support.Networking.CruiseWaypointMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "X:System.Single", "Z:System.Single", "Clear:System.Boolean" }),
    ("BoscaliSummer.Modules.Support.Networking.CruiseLegsMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "OwnerId:System.UInt64", "Result:System.Byte", "FactionName:System.String", "LegCount:System.Byte", "X:System.Single[]", "Z:System.Single[]", "Dive:System.Boolean", "Tti:System.Single" }),
    ("BoscaliSummer.Modules.DynamicOperations.Networking.OperationsQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "OperationId:System.Int32", "Action:System.Byte" }),
    ("BoscaliSummer.Modules.DynamicOperations.Networking.OperationsSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Status:System.String", "Cards:BoscaliSummer.Core.Contracts.SecondaryObjectiveView[]" }),
    ("BoscaliSummer.Modules.Progression.Networking.ProgressionSubmit", new[] { "Protocol:System.Byte", "Perk:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Generation:System.Int32" }),
    ("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", new[] { "Protocol:System.Byte", "PerkMask:System.UInt32", "Score:System.Int32", "EarnedPoints:System.Byte", "Rank:System.Byte", "Result:System.Byte", "Generation:System.Int32", "Scene:System.UInt32", "Token:System.UInt32", "ScorePerPoint:System.Int32", "MaximumPoints:System.Byte", "PlaneId:System.UInt32", "EngineMap:System.Byte" }),
    ("BoscaliSummer.Modules.Progression.Networking.PlaneTuneRequest", new[] { "Protocol:System.Byte", "AircraftId:System.UInt32", "Mode:System.Byte" }),
    ("BoscaliSummer.Modules.Progression.Networking.PlaneTuneState", new[] { "Protocol:System.Byte", "AircraftId:System.UInt32", "Mode:System.Byte", "Accepted:System.Byte" }),
    ("BoscaliSummer.Modules.Progression.Networking.CrestSubmit", new[] { "Protocol:System.Byte", "Shape:System.Byte", "Division:System.Byte", "Charge:System.Byte", "Accent:System.Byte", "Field:System.Byte", "Second:System.Byte", "Ink:System.Byte", "Number:System.Byte", "Paint:System.Byte" }),
    ("BoscaliSummer.Modules.Progression.Networking.CrestState", new[] { "Protocol:System.Byte", "Owner:System.UInt64", "Shape:System.Byte", "Division:System.Byte", "Charge:System.Byte", "Accent:System.Byte", "Field:System.Byte", "Second:System.Byte", "Ink:System.Byte", "Number:System.Byte", "Paint:System.Byte" }),
    ("BoscaliSummer.Modules.Squad.Networking.SquadQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Revision:System.UInt32" }),
    ("BoscaliSummer.Modules.Squad.Networking.SquadSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Event:System.UInt32", "Pilot:BoscaliSummer.Core.Contracts.PilotView", "Hunt:System.Boolean", "Bonus:System.Int32", "Origin:System.Int32", "ActiveIndex:System.Int32", "HuntId:System.Int32", "Status:System.String", "Speaker:System.String", "Chatter:System.String", "Wings:BoscaliSummer.Core.Contracts.EnemyWingView[]", "Revision:System.UInt32", "Unchanged:System.Boolean" }),
    ("BoscaliSummer.Modules.Command.Networking.FactionMoraleChanged", new[] { "Protocol:System.Byte", "FactionHash:System.Int32", "Morale:System.Single" }),
    ("BoscaliSummer.Modules.Events.Networking.ActiveEventChanged", new[] { "Protocol:System.Byte", "CatalogIndex:System.SByte", "TargetFactionHash:System.Int32", "StartedAtMissionTime:System.Single", "EndsAtMissionTime:System.Single", "EffectStrength:System.Single" }),
    ("BoscaliSummer.Modules.Trenches.Networking.TrenchGeometryMessage", new[] { "Protocol:System.Byte", "LineId:System.Int32", "Stage:System.Byte", "OwnerHash:System.Int32", "Curve:UnityEngine.Vector3[]", "Threat:UnityEngine.Vector3[]", "Support:UnityEngine.Vector3[]", "Redoubt:UnityEngine.Vector3[]", "Links:UnityEngine.Vector3[][]", "Spurs:UnityEngine.Vector3[][]" }),
    ("BoscaliSummer.Modules.Trenches.Networking.TrenchStateMessage", new[] { "Protocol:System.Byte", "LineId:System.Int32", "Stage:System.Byte", "Defenders:System.Byte", "Suppressed:System.Boolean", "Overrun:System.Boolean" }),
    ("BoscaliSummer.Modules.Trenches.Networking.TrenchLineRemovedMessage", new[] { "Protocol:System.Byte", "LineId:System.Int32" }),
    ("BoscaliSummer.Modules.Session.Networking.SessionHello", new[] { "Protocol:System.Byte", "Version:System.String" }),
    ("BoscaliSummer.Modules.Session.Networking.HostSettingsMessage", new[] { "Protocol:System.Byte", "Version:System.String", "Flags:System.Byte", "Keys:System.String[]", "Values:System.String[]" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.TheaterPriorityQuery", new[] { "Protocol:System.Byte" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontQuery", new[] { "Protocol:System.Byte", "Session:System.Int32" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontIntent", new[] { "Protocol:System.Byte", "Kind:System.Byte", "Posture:System.Byte", "Id:System.Int32", "Revision:System.Int32", "RequestId:System.Int32", "Session:System.Int32", "HostEpoch:System.Int32" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontResult", new[] { "Protocol:System.Byte", "Faction:System.String", "RequestId:System.Int32", "Code:System.Byte", "Detail:System.String", "LockSeconds:System.Single", "Session:System.Int32", "HostEpoch:System.Int32" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontSnapshot", new[] { "Protocol:System.Byte", "HasSnapshot:System.Boolean", "Session:System.Int32", "HostEpoch:System.Int32", "Faction:System.String", "Body:BoscaliSummer.Core.Contracts.TheaterWarSnapshot" }),
    ("BoscaliSummer.Modules.Comms.Networking.CommsUpMessage", new[] { "Protocol:System.Byte", "Op:System.Byte", "Channel:System.Byte", "Kind:System.Byte", "Style:System.Byte", "Size:System.Byte", "Target:System.UInt32", "Points:System.Int32[]", "Text:System.String", "Items:System.String[]" }),
    ("BoscaliSummer.Modules.Comms.Networking.CommsDownMessage", new[] { "Protocol:System.Byte", "Event:System.Byte", "Id:System.UInt32", "Author:System.UInt64", "AuthorName:System.String", "Faction:System.Int32", "Channel:System.Byte", "Kind:System.Byte", "Style:System.Byte", "Size:System.Byte", "Flags:System.Byte", "Ttl:System.Single", "Points:System.Int32[]", "Text:System.String", "Items:System.String[]", "Values:System.Int32[]", "Players:System.UInt64[]", "Ids:System.UInt32[]" }),
    ("BoscaliSummer.Modules.Weather.Networking.WeatherSyncMessage", new[] { "Protocol:System.Byte", "TargetConditions:System.Single", "TargetCloudHeight:System.Single", "TargetWindX:System.Single", "TargetWindZ:System.Single", "TargetTurbulence:System.Single", "MissionTimeSeconds:System.UInt32", "ForcedRain:System.Single", "FieldSeed:System.UInt32", "FieldStory:System.Byte", "FieldSkyPin:System.Byte", "FieldSalt:System.Byte", "FieldMissionClass:System.Byte", "FieldOpeningBase:System.Single", "FieldHeading:System.Single", "FieldEpoch:System.Single", "FieldOffset:System.Single", "FieldSystemSpeed:System.Single", "FieldDynamic:System.Boolean", "FieldManual:System.Boolean", "FieldSets:System.Byte", "FieldHasAnchor:System.Boolean", "FieldAnchorX:System.Single", "FieldAnchorZ:System.Single", "FieldSetOriginSquall:System.Single", "FieldSetOriginSupercell:System.Single", "FieldSetOriginCell:System.Single" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.TheaterPriorityState", new[] { "Protocol:System.Byte", "Active:System.Byte", "Faction:System.String", "Key:System.String", "Label:System.String", "X:System.Single", "Y:System.Single", "Z:System.Single" }) })
{
    Type type = pluginAssembly.GetType(contract.Item1, true)!;
    string[] actual = type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.MetadataToken)
        .Select(field => field.Name + ":" + field.FieldType.FullName).ToArray();
    if (!actual.SequenceEqual(contract.Item2)) throw new InvalidOperationException(contract.Item1 + " wire fields changed: " + string.Join("|", actual));
}
ProbeOperationSerialization(pluginAssembly, mirageAssembly);
ProbeHighCommandSerialization(pluginAssembly, mirageAssembly);
ProbeSquadSerialization(pluginAssembly, mirageAssembly);
ProbeSupportSerialization(pluginAssembly, mirageAssembly);
ProbeEventsSerialization(pluginAssembly, mirageAssembly);
ProbeFactionMoraleSerialization(pluginAssembly, mirageAssembly);
ProbeTrenchSerialization(pluginAssembly, mirageAssembly);
ProbeSessionSerialization(pluginAssembly, mirageAssembly);
ProbeWeatherSerialization(pluginAssembly, mirageAssembly);
ProbeWeatherConfig(pluginAssembly);
ProbeTacBoardConfig(pluginAssembly);
ProbeTheaterOpsHostSettings(pluginAssembly);
ProbeTheaterOpsSerialization(pluginAssembly, mirageAssembly);
// Wing integration is optional and the supported contract is compiled in-tree. A hard
// external dependency prevents BepInEx from loading the entire plugin when Wing is absent.
foreach (CustomAttributeData dependency in pluginAssembly.GetType("BoscaliSummer.Plugin", true)!.CustomAttributes
    .Where(attribute => attribute.AttributeType.FullName == "BepInEx.BepInDependency" &&
        attribute.ConstructorArguments.Count > 0 &&
        Equals(attribute.ConstructorArguments[0].Value, "com.marci.wingcommand")))
{
    // BepInEx's version constructor implies HardDependency; its flags constructor can
    // explicitly select SoftDependency (2). Named Flags also override that default.
    int flags = dependency.ConstructorArguments.Count > 1 && dependency.ConstructorArguments[1].ArgumentType.IsEnum
        ? Convert.ToInt32(dependency.ConstructorArguments[1].Value) : 1;
    CustomAttributeNamedArgument namedFlags = dependency.NamedArguments.FirstOrDefault(argument => argument.MemberName == "Flags");
    if (namedFlags.MemberInfo != null) flags = Convert.ToInt32(namedFlags.TypedValue.Value);
    if ((flags & 1) != 0) throw new InvalidOperationException("Plugin must load without the optional external Wing Command plugin");
}
if (pluginAssembly.GetReferencedAssemblies().Any(reference =>
    reference.Name?.IndexOf("WingCommand", StringComparison.OrdinalIgnoreCase) >= 0))
    throw new InvalidOperationException("Optional in-tree Wing integration must not require a WingCommand assembly reference");
ProbeClientImmersionComposition(pluginAssembly);
Console.WriteLine($"Patch target probe: game methods/fields, Harmony parameters, {patchTypes.Length} patch classes, {featureTypes.Length + 1} modules, radio assets, wire contracts, dynamic operation reward/road signatures, and Mirage seams resolved.");
return 0;

static void ProbeClientImmersionComposition(Assembly plugin)
{
    // Inspect the real compiled roster without invoking Unity's native Application call
    // from .NET. Client execution still requires the live game; this guards its master-off
    // registration and the batch-mode exclusion in the exact candidate DLL.
    MethodInfo compose = plugin.GetType("BoscaliSummer.Core.BoscaliMod", true)!
        .GetMethod("Compose", BindingFlags.Static | BindingFlags.NonPublic)!;
    byte[] il = compose.GetMethodBody()!.GetILAsByteArray()!;
    Dictionary<short, OpCode> codes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
    var instructions = new List<(int Offset, OpCode Code, MemberInfo Member, int Target)>();
    for (int cursor = 0; cursor < il.Length;)
    {
        int start = cursor;
        short value = il[cursor++] == 0xfe ? (short)(0xfe00 | il[cursor++]) : il[start];
        OpCode code = codes[value];
        MemberInfo member = null;
        int target = -1, size;
        switch (code.OperandType)
        {
            case OperandType.InlineNone: size = 0; break;
            case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
            case OperandType.InlineVar: size = 2; break;
            case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
            case OperandType.InlineSwitch: size = 4 + BitConverter.ToInt32(il, cursor) * 4; break;
            case OperandType.ShortInlineBrTarget: size = 1; target = cursor + size + (sbyte)il[cursor]; break;
            case OperandType.InlineBrTarget: size = 4; target = cursor + size + BitConverter.ToInt32(il, cursor); break;
            case OperandType.InlineMethod: case OperandType.InlineField: case OperandType.InlineTok:
                size = 4; member = compose.Module.ResolveMember(BitConverter.ToInt32(il, cursor)); break;
            default: size = 4; break;
        }
        instructions.Add((start, code, member, target)); cursor += size;
    }
    int immersion = instructions.FindIndex(instruction => instruction.Code == OpCodes.Newobj &&
        instruction.Member?.DeclaringType?.FullName == "BoscaliSummer.Modules.Immersion.ImmersionModule");
    int batch = instructions.FindIndex(instruction => instruction.Member?.DeclaringType?.FullName == "UnityEngine.Application" &&
        instruction.Member.Name == "get_isBatchMode");
    if (immersion < 0 || batch < 0 || immersion <= batch)
        throw new InvalidOperationException("Compiled client roster must include Immersion inside its batch-mode gate");
    var gate = instructions[batch + 1];
    if ((gate.Code != OpCodes.Brtrue && gate.Code != OpCodes.Brtrue_S) || gate.Target <= instructions[immersion].Offset)
        throw new InvalidOperationException("Batch mode must skip client Immersion registration");
    if (instructions.Take(immersion).Any(instruction =>
        instruction.Member?.DeclaringType?.FullName == "BoscaliSummer.Core.Config.ModConfiguration") ||
        instructions.Skip(batch + 2).Take(immersion - batch - 2).Any(instruction => instruction.Target >= 0))
        throw new InvalidOperationException("Client Immersion registration must remain available when its master setting is off");
    var add = instructions.Skip(immersion + 1).FirstOrDefault(instruction =>
        instruction.Member?.Name == "Add" && instruction.Member.DeclaringType?.IsGenericType == true);
    if (add.Member == null || add.Offset >= gate.Target)
        throw new InvalidOperationException("Client Immersion must be added to the compiled module roster");
    Console.WriteLine("  Compiled client Immersion roster: installed before settings gates (master off supported); excluded in batch mode");
}

static bool MetadataHasMethod(string assemblyPath, string typeName, string methodName)
{
    using FileStream stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
    {
        TypeDefinition definition = metadata.GetTypeDefinition(handle);
        if (MetadataTypeName(metadata, handle) != typeName) continue;
        foreach (MethodDefinitionHandle methodHandle in definition.GetMethods())
            if (metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name) == methodName)
                return true;
        return false;
    }
    return false;
}

static bool MetadataHasField(string assemblyPath, string typeName, string fieldName)
{
    using FileStream stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
    {
        TypeDefinition definition = metadata.GetTypeDefinition(handle);
        if (MetadataTypeName(metadata, handle) != typeName) continue;
        foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
            if (metadata.GetString(metadata.GetFieldDefinition(fieldHandle).Name) == fieldName)
                return true;
        return false;
    }
    return false;
}

static string MetadataTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
{
    TypeDefinition definition = metadata.GetTypeDefinition(handle);
    string name = metadata.GetString(definition.Name);
    TypeDefinitionHandle parent = definition.GetDeclaringType();
    if (!parent.IsNil) return MetadataTypeName(metadata, parent) + "+" + name;
    string ns = metadata.GetString(definition.Namespace);
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

static void RequireMetadataSignature(string assemblyPath, string typeName, string methodName, string expected)
{
    using var stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
    {
        if (MetadataTypeName(metadata, handle) != typeName) continue;
        foreach (MethodDefinitionHandle methodHandle in metadata.GetTypeDefinition(handle).GetMethods())
        {
            MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
            if (metadata.GetString(method.Name) != methodName || (method.Attributes & MethodAttributes.Public) == 0) continue;
            var signature = method.DecodeSignature(new ProbeSignatureNames(), (object)null);
            string actual = signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";
            if (actual == expected) return;
        }
    }
    throw new MissingMethodException(typeName, methodName + ": " + expected);
}

static void RequireMetadataField(string assemblyPath, string typeName, string fieldName, string expected)
{
    using var stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
    {
        if (MetadataTypeName(metadata, handle) != typeName) continue;
        foreach (FieldDefinitionHandle fieldHandle in metadata.GetTypeDefinition(handle).GetFields())
        {
            FieldDefinition field = metadata.GetFieldDefinition(fieldHandle);
            if (metadata.GetString(field.Name) == fieldName && (field.Attributes & FieldAttributes.Public) != 0 &&
                field.DecodeSignature(new ProbeSignatureNames(), (object)null) == expected) return;
        }
    }
    throw new MissingFieldException(typeName, fieldName + ": " + expected);
}

static void ProbeOperationSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.DynamicOperations.Networking.OperationsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 5)
        throw new InvalidOperationException("Operations protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    Type queryType = plugin.GetType("BoscaliSummer.Modules.DynamicOperations.Networking.OperationsQuery", true)!;
    Type snapshotType = plugin.GetType("BoscaliSummer.Modules.DynamicOperations.Networking.OperationsSnapshot", true)!;
    Type cardType = plugin.GetType("BoscaliSummer.Core.Contracts.SecondaryObjectiveView", true)!;

    object Encode(Type type, object value)
    {
        object writer = Activator.CreateInstance(writerType, 8192)!;
        Type holder = mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(type);
        ((Delegate)holder.GetProperty("Write", flags)!.GetValue(null)!).DynamicInvoke(writer, value);
        return writer;
    }
    object Decode(Type type, object writer)
    {
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            byte[] bytes = (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            Type holder = mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(type);
            return ((Delegate)holder.GetProperty("Read", flags)!.GetValue(null)!).DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }
    void Reject(object writer)
    {
        try { Decode(snapshotType, writer); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return; }
        throw new InvalidOperationException("Malformed operations snapshot was accepted");
    }
    object Card(float progress, float remaining = 300f, int money = 17, int xp = 29, object link = null) =>
        Activator.CreateInstance(cardType, 123, new string('T', 150), "description", "target", "status", "reward", progress, remaining, money, xp, false,
            false, true, true, 12500f, -6750f, 1500f, "<b>" + new string('P', 50), null, link)!;
    object Snapshot(int count, object card)
    {
        object snapshot = Activator.CreateInstance(snapshotType)!;
        snapshotType.GetField("Protocol")!.SetValue(snapshot, (byte)5);
        snapshotType.GetField("Scene")!.SetValue(snapshot, 345u);
        snapshotType.GetField("Token")!.SetValue(snapshot, 678u);
        snapshotType.GetField("Status")!.SetValue(snapshot, new string('S', 200));
        Array cards = Array.CreateInstance(cardType, count);
        for (int i = 0; i < count; i++) cards.SetValue(card, i);
        snapshotType.GetField("Cards")!.SetValue(snapshot, cards);
        return snapshot;
    }
    object query = Activator.CreateInstance(queryType)!;
    queryType.GetField("Protocol")!.SetValue(query, (byte)5);
    queryType.GetField("OperationId")!.SetValue(query, 123);
    queryType.GetField("Action")!.SetValue(query, (byte)1);
    queryType.GetField("Scene")!.SetValue(query, uint.MaxValue);
    queryType.GetField("Token")!.SetValue(query, 987654u);
    object queryResult = Decode(queryType, Encode(queryType, query));
    foreach (FieldInfo field in queryType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(query), field.GetValue(queryResult))) throw new InvalidOperationException("Operations query roundtrip changed " + field.Name);

    object decoded = Decode(snapshotType, Encode(snapshotType, Snapshot(4, Card(0.75f))));
    Array output = (Array)snapshotType.GetField("Cards")!.GetValue(decoded)!;
    if (output.Length != 3 || (string)snapshotType.GetField("Status")!.GetValue(decoded)! != new string('S', 128) ||
        (uint)snapshotType.GetField("Scene")!.GetValue(decoded)! != 345u || (uint)snapshotType.GetField("Token")!.GetValue(decoded)! != 678u)
        throw new InvalidOperationException("Operations snapshot bounds/header roundtrip failed");
    object cardResult = output.GetValue(0)!;
    object expectedCard = Activator.CreateInstance(cardType, 123, new string('T', 128), "description", "target", "status", "reward", 0.75f, 300f, 17, 29, false,
        false, true, true, 12500f, -6750f, 1500f, "b" + new string('P', 31), null, null)!;
    foreach (PropertyInfo property in cardType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(property.GetValue(expectedCard), property.GetValue(cardResult))) throw new InvalidOperationException("Operations card roundtrip changed " + property.Name);

    object EnumValue(string name, byte value) => Enum.ToObject(plugin.GetType("BoscaliSummer.Core.Contracts." + name, true)!, value);
    Type phaseType = plugin.GetType("BoscaliSummer.Core.Contracts.ObjectivePhase", true)!;
    Type taskingType = plugin.GetType("BoscaliSummer.Core.Contracts.ObjectiveTasking", true)!;
    object Tasking(byte family = 8, float age = 0f, bool duplicatePhase = false, int subject = -1)
    {
        Array phases = Array.CreateInstance(phaseType, 3);
        string[] phaseNames = { "JOIN", "COVER", "TRANSFER" };
        for (int i = 0; i < phases.Length; i++)
            phases.SetValue(Activator.CreateInstance(phaseType, (byte)(duplicatePhase ? 1 : i + 1), phaseNames[i], "Host gate " + i,
                EnumValue("ObjectivePhaseStatus", i == 0 ? (byte)2 : i == 1 ? (byte)1 : (byte)0), i == 0 ? 1f : i == 1 ? .55f : 0f), i);
        return Activator.CreateInstance(taskingType, EnumValue("ObjectiveFamily", family), EnumValue("ObjectiveAsset", 2),
            EnumValue("ObjectiveContact", 2), age, "Cover the truck.", "", "Keep the eastern line supplied.", phases,
            EnumValue("ObjectiveLifecycle", 1), "Ends the sortie without an award.", EnumValue("ObjectiveAllegiance", 1), subject)!;
    }
    object TypedCard(object tasking, object link = null) => Activator.CreateInstance(cardType, 124, "COVER THE SUPPLY RUN", "Host briefing", "Supply truck", "COVER", "Morale +3",
        .55f, 702f, 1400, 125, false, false, true, true, 12500f, -6750f, 1500f, "ROOK 1", tasking, link)!;
    object typedInput = Tasking();
    object typedSnapshot = Decode(snapshotType, Encode(snapshotType, Snapshot(1, TypedCard(typedInput))));
    object typedResult = cardType.GetProperty("Tasking")!.GetValue(((Array)snapshotType.GetField("Cards")!.GetValue(typedSnapshot)!).GetValue(0))!;
    foreach (PropertyInfo property in taskingType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.Name == "Phases") continue;
        if (!Equals(property.GetValue(typedInput), property.GetValue(typedResult))) throw new InvalidOperationException("Operations tasking roundtrip changed " + property.Name);
    }
    Array inputPhases = (Array)taskingType.GetProperty("Phases")!.GetValue(typedInput)!;
    Array resultPhases = (Array)taskingType.GetProperty("Phases")!.GetValue(typedResult)!;
    if (resultPhases.Length != 3) throw new InvalidOperationException("Operations phase gate count changed");
    for (int i = 0; i < inputPhases.Length; i++)
        foreach (PropertyInfo property in phaseType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (!Equals(property.GetValue(inputPhases.GetValue(i)), property.GetValue(resultPhases.GetValue(i))))
                throw new InvalidOperationException("Operations phase roundtrip changed " + property.Name);
    Reject(Encode(snapshotType, Snapshot(1, TypedCard(Tasking(family: 255)))));
    Reject(Encode(snapshotType, Snapshot(1, TypedCard(Tasking(age: 31f)))));
    Reject(Encode(snapshotType, Snapshot(1, TypedCard(Tasking(duplicatePhase: true)))));
    Reject(Encode(snapshotType, Snapshot(1, TypedCard(Tasking(subject: 64)))));
    Reject(Encode(snapshotType, Snapshot(1, TypedCard(Tasking(subject: -2)))));
    // v5: the sortie link and the subject id.
    Type linkType = plugin.GetType("BoscaliSummer.Core.Contracts.SortieLink", true)!;
    object Link(int operation = 7, byte role = 2, string place = null, bool live = true, bool known = true,
        float x = 12500f, float z = -6750f, float radius = 1500f, bool wing = true) =>
        Activator.CreateInstance(linkType, operation, EnumValue("FocusRole", role), place ?? new string('L', 41), live, known, x, z, radius, wing)!;
    object linkRoundtrip = Decode(snapshotType, Encode(snapshotType, Snapshot(1, TypedCard(Tasking(subject: 63), Link()))));
    object linkCard = ((Array)snapshotType.GetField("Cards")!.GetValue(linkRoundtrip)!).GetValue(0)!;
    object linkBack = cardType.GetProperty("Link")!.GetValue(linkCard)!;
    object linkWant = Link(place: new string('L', 40));
    foreach (PropertyInfo property in linkType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(property.GetValue(linkWant), property.GetValue(linkBack)))
            throw new InvalidOperationException("Operations link roundtrip changed " + property.Name);
    if ((int)taskingType.GetProperty("SubjectId")!.GetValue(cardType.GetProperty("Tasking")!.GetValue(linkCard))! != 63)
        throw new InvalidOperationException("Operations tasking subject id changed");
    object plainBack = ((Array)snapshotType.GetField("Cards")!.GetValue(Decode(snapshotType, Encode(snapshotType, Snapshot(1, Card(0.5f)))))!).GetValue(0)!;
    if (cardType.GetProperty("Link")!.GetValue(plainBack) != null) throw new InvalidOperationException("An untied card grew a link");
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(radius: 1501f)))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(role: 4)))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(operation: -1)))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(operation: 1000001)))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(operation: 0, role: 2, place: "", live: false, known: false, x: 0f, z: 0f, radius: 0f)))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(operation: 0, role: 0, place: "", live: true, known: false, x: 0f, z: 0f, radius: 0f)))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(x: float.NaN)))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link(known: false, x: 5f, z: 0f, radius: 0f)))));
    // The flags byte is the 13th from the end of a snapshot whose last field is a link: patch it to 15 (the three real bits plus bit 3).
    // Patching it to 8 would also clear SiteKnown and be refused for the 12500 left in X, so the flags rule would never be tested.
    byte[] flagBytes = (byte[])writerType.GetMethod("ToArray")!.Invoke(Encode(snapshotType, Snapshot(1, Card(0.5f, link: Link()))), null)!;
    flagBytes[flagBytes.Length - 13] = 15;
    object flagReader = Activator.CreateInstance(readerType)!;
    try
    {
        readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(flagReader, new object[] { flagBytes });
        try { ((Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(snapshotType).GetProperty("Read", flags)!.GetValue(null)!).DynamicInvoke(flagReader); throw new InvalidOperationException("Link flags 15 were accepted"); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
    }
    finally { ((IDisposable)flagReader).Dispose(); }
    Reject(Encode(snapshotType, Snapshot(1, Card(float.NaN))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, float.PositiveInfinity))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, 300f, -1))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, 300f, 17, 10001))));
    object excessiveCount = Encode(snapshotType, Snapshot(0, Card(0f)));
    int bitPosition = (int)writerType.GetProperty("BitPosition")!.GetValue(excessiveCount)!;
    writerType.GetField("_bitPosition", flags)!.SetValue(excessiveCount, bitPosition - 8);
    writerType.GetMethod("WriteByte")!.Invoke(excessiveCount, new object[] { (byte)4 });
    Reject(excessiveCount);
    Console.WriteLine("  Dynamic operations serializers v5: query/card/typed-tasking/phase/link/subject roundtrip, 3-card/128-char/32-pilot/40-place bounds, non-finite/range/count/enum/duplicate-phase/link-coupling rejection");
}

static void ProbeHighCommandSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    plugin.GetType("BoscaliSummer.Modules.HighCommand.Networking.HighCommandNet", true)!.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    Type snapshotType = plugin.GetType("BoscaliSummer.Modules.HighCommand.Networking.HighCommandSnapshot", true)!;
    Type nodeType = plugin.GetType("BoscaliSummer.Modules.HighCommand.Networking.CommanderWire", true)!;
    Type logType = plugin.GetType("BoscaliSummer.Modules.HighCommand.Networking.CommanderLogWire", true)!;
    MethodInfo[] extensions = mirage.GetTypes().Where(t => t.IsAbstract && t.IsSealed)
        .SelectMany(t => t.GetMethods(flags)).Where(m => m.IsStatic && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == writerType).ToArray();

    byte[] Encode(object value)
    {
        object writer = Activator.CreateInstance(writerType, 8192)!;
        ((Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(snapshotType).GetProperty("Write", flags)!.GetValue(null)!).DynamicInvoke(writer, value);
        return (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
    }
    object Decode(byte[] bytes)
    {
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            return ((Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(snapshotType).GetProperty("Read", flags)!.GetValue(null)!).DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }
    void Set(object target, string field, object value) => target.GetType().GetField(field, flags)!.SetValue(target, value);
    object Get(object target, string field) => target.GetType().GetField(field, flags)!.GetValue(target)!;
    object Node(int id = 3, byte tier = 2, byte nodeFlags = 1 | 2 | 64, float age = 12f, float x = 100f, int bounty = 3000)
    {
        object node = Activator.CreateInstance(nodeType)!;
        Set(node, "Id", id); Set(node, "ParentId", 1); Set(node, "Tier", tier); Set(node, "Flags", nodeFlags); Set(node, "TraitMask", (byte)0);
        Set(node, "Seed", 77); Set(node, "IntelAge", age); Set(node, "Weight", 0f); Set(node, "X", x); Set(node, "Z", -200f);
        Set(node, "Name", "COL TEST"); Set(node, "Rank", "COL"); Set(node, "Role", "GROUND"); Set(node, "Location", "PORT");
        Set(node, "Site", new string('S', 40)); Set(node, "Bounty", bounty);
        return node;
    }
    object Row(string text)
    {
        object row = Activator.CreateInstance(logType)!;
        Set(row, "TargetId", 3); Set(row, "Tone", (byte)1); Set(row, "Text", text); Set(row, "Age", 5f);
        return row;
    }
    object Snapshot(object node, object row)
    {
        object snapshot = Activator.CreateInstance(snapshotType)!;
        Set(snapshot, "Protocol", (byte)5); Set(snapshot, "Scene", 345u); Set(snapshot, "Token", 678u);
        Set(snapshot, "Status", new string('S', 200)); Set(snapshot, "Signal", ""); Set(snapshot, "Cohesion", .5f);
        Set(snapshot, "Active", 5); Set(snapshot, "Kia", 0);
        Array nodes = Array.CreateInstance(nodeType, node == null ? 0 : 1); if (node != null) nodes.SetValue(node, 0);
        Array log = Array.CreateInstance(logType, row == null ? 0 : 1); if (row != null) log.SetValue(row, 0);
        Set(snapshot, "Nodes", nodes); Set(snapshot, "Log", log); Set(snapshot, "HostileLog", Array.CreateInstance(logType, 0));
        return snapshot;
    }
    void Rejected(byte[] bytes, string what)
    {
        if ((byte)Get(Decode(bytes), "Protocol") != 0) throw new InvalidOperationException("High command accepted " + what);
    }
    // Hand-written wire: the writer clips and caps, so these two cannot come from it.
    byte[] Hand(int nodeCount, string logText)
    {
        object writer = Activator.CreateInstance(writerType, 1024)!;
        void Put(string method, object value)
        {
            MethodInfo own = writerType.GetMethods(flags).FirstOrDefault(m => m.Name == method && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == value.GetType());
            if (own != null) { own.Invoke(writer, new[] { value }); return; }
            extensions.First(m => m.Name == method && m.GetParameters()[1].ParameterType == value.GetType()).Invoke(null, new[] { writer, value });
        }
        Put("WriteByte", (byte)5); Put("WritePackedUInt32", 345u); Put("WritePackedUInt32", 678u);
        Put("WriteString", "S"); Put("WriteString", ""); Put("WriteSingle", .5f); Put("WritePackedInt32", 5); Put("WritePackedInt32", 0);
        Put("WriteByte", (byte)nodeCount);
        if (nodeCount > 0) return (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
        if (logText == null) Put("WriteByte", (byte)0);
        else { Put("WriteByte", (byte)1); Put("WritePackedInt32", 3); Put("WriteByte", (byte)1); Put("WriteString", logText); Put("WriteSingle", 5f); }
        Put("WriteByte", (byte)0);
        return (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
    }

    object decoded = Decode(Encode(Snapshot(Node(), Row(new string('L', 96)))));
    object back = ((Array)Get(decoded, "Nodes")).GetValue(0)!;
    if ((byte)Get(decoded, "Protocol") != 5 || ((string)Get(decoded, "Status")).Length != 96 || ((Array)Get(decoded, "Nodes")).Length != 1 ||
        ((string)Get(back, "Site")).Length != 32 || (int)Get(back, "Bounty") != 3000 || (byte)Get(back, "Flags") != 67 ||
        ((string)Get(((Array)Get(decoded, "Log")).GetValue(0)!, "Text")).Length != 96)
        throw new InvalidOperationException("High command v5 roundtrip changed (Claimed row, Site 40 to 32, status 200 to 96, 96-character log row)");
    Rejected(Encode(Snapshot(Node(bounty: -1), null)), "bounty -1");
    Rejected(Encode(Snapshot(Node(bounty: 100001), null)), "bounty 100001");
    Rejected(Encode(Snapshot(Node(nodeFlags: 0x80), null)), "flag 0x80");
    Rejected(Encode(Snapshot(Node(age: 601f), null)), "intel age 601");
    Rejected(Encode(Snapshot(Node(x: float.NaN), null)), "a NaN coordinate");
    Rejected(Encode(Snapshot(Node(id: 64), null)), "id 64");
    Rejected(Encode(Snapshot(Node(tier: 3), null)), "tier 3");
    Rejected(Hand(33, null), "33 nodes");
    Rejected(Hand(0, new string('L', 97)), "a 97-character log row");
    if ((byte)Get(Decode(Hand(0, new string('L', 96))), "Protocol") != 5) throw new InvalidOperationException("High command hand-written 96-character log row was refused");
    Console.WriteLine("  High command serializers v5: Claimed row, Site 40 to 32, status 200 to 96, 96-character log row, nine rejections");
}

static void ProbeSquadSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Squad.Networking.SquadNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 3)
        throw new InvalidOperationException("Squad protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type progression = plugin.GetType("BoscaliSummer.Modules.Progression.Networking.ProgressionNet", true)!;
    if ((byte)progression.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 6)
        throw new InvalidOperationException("Progression protocol changed without updating its probe");
    progression.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    Type queryType = plugin.GetType("BoscaliSummer.Modules.Squad.Networking.SquadQuery", true)!;
    Type snapshotType = plugin.GetType("BoscaliSummer.Modules.Squad.Networking.SquadSnapshot", true)!;
    Type pilotType = plugin.GetType("BoscaliSummer.Core.Contracts.PilotView", true)!;
    Type wingType = plugin.GetType("BoscaliSummer.Core.Contracts.EnemyWingView", true)!;
    object Encode(Type type, object value)
    {
        object writer = Activator.CreateInstance(writerType, 8192)!;
        Type holder = mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(type);
        ((Delegate)holder.GetProperty("Write", flags)!.GetValue(null)!).DynamicInvoke(writer, value);
        return writer;
    }
    object Decode(Type type, object writer)
    {
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            byte[] bytes = (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            Type holder = mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(type);
            return ((Delegate)holder.GetProperty("Read", flags)!.GetValue(null)!).DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }
    void Set(object value, string field, object data) => value.GetType().GetField(field)!.SetValue(value, data);
    object Get(object value, string field) => value.GetType().GetField(field)!.GetValue(value)!;
    object Wing(int tier = 5, int alive = 3, int members = 4, int returns = 2, int abilities = 15) =>
        Activator.CreateInstance(wingType, "ACE", "Viper", "Cinder", tier, "Veteran", "Hunting", alive, members, "Player", returns, abilities)!;
    object Snapshot(int count, object wing)
    {
        object snapshot = Activator.CreateInstance(snapshotType)!;
        Set(snapshot, "Protocol", (byte)3); Set(snapshot, "Scene", 345u); Set(snapshot, "Token", 678u); Set(snapshot, "Event", uint.MaxValue);
        Set(snapshot, "Revision", 0xDEADBEEFu);
        Set(snapshot, "Pilot", Activator.CreateInstance(pilotType, "Pilot Name", "CALDER", "Alive", true, 4, 5, "background")!);
        Set(snapshot, "Hunt", true); Set(snapshot, "Bonus", 20); Set(snapshot, "Origin", 1000); Set(snapshot, "ActiveIndex", count == 0 ? -1 : 0); Set(snapshot, "HuntId", 41);
        Set(snapshot, "Status", new string('S', 240)); Set(snapshot, "Speaker", "Cinder"); Set(snapshot, "Chatter", "Contact.");
        Array wings = Array.CreateInstance(wingType, count);
        for (int i = 0; i < count; i++) wings.SetValue(wing, i);
        Set(snapshot, "Wings", wings);
        return snapshot;
    }
    // Readers never throw inside a Mirage handler; malformed data decodes as protocol 0, which handlers ignore.
    void Reject(object writer)
    {
        if ((byte)Get(Decode(snapshotType, writer), "Protocol") != 0)
            throw new InvalidOperationException("Malformed Squad snapshot was accepted");
    }
    object query = Activator.CreateInstance(queryType)!;
    Set(query, "Protocol", (byte)3); Set(query, "Scene", uint.MaxValue); Set(query, "Token", 987654u); Set(query, "Revision", 0xCAFEF00Du);
    object queryResult = Decode(queryType, Encode(queryType, query));
    foreach (FieldInfo field in queryType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(query), field.GetValue(queryResult))) throw new InvalidOperationException("Squad query roundtrip changed " + field.Name);
    object decoded = Decode(snapshotType, Encode(snapshotType, Snapshot(9, Wing())));
    Array output = (Array)Get(decoded, "Wings");
    if (output.Length != 8 || (string)Get(decoded, "Status") != new string('S', 192) ||
        (uint)Get(decoded, "Scene") != 345u || (uint)Get(decoded, "Token") != 678u || (uint)Get(decoded, "Event") != uint.MaxValue ||
        (int)Get(decoded, "Bonus") != 20 || (int)Get(decoded, "Origin") != 1000 || (int)Get(decoded, "ActiveIndex") != 0 || (int)Get(decoded, "HuntId") != 41 || !(bool)Get(decoded, "Hunt") ||
        (uint)Get(decoded, "Revision") != 0xDEADBEEFu || (bool)Get(decoded, "Unchanged"))
        throw new InvalidOperationException("Squad snapshot bounds/header roundtrip failed");
    object unchanged = Activator.CreateInstance(snapshotType)!;
    Set(unchanged, "Protocol", (byte)3); Set(unchanged, "Scene", 345u); Set(unchanged, "Token", 678u);
    Set(unchanged, "Revision", 0xDEADBEEFu); Set(unchanged, "Unchanged", true);
    object unchangedEncoded = Encode(snapshotType, unchanged);
    object unchangedDecoded = Decode(snapshotType, unchangedEncoded);
    if ((int)writerType.GetProperty("BitPosition")!.GetValue(unchangedEncoded)! > 12 * 8 ||
        (byte)Get(unchangedDecoded, "Protocol") != 3 || !(bool)Get(unchangedDecoded, "Unchanged") ||
        (uint)Get(unchangedDecoded, "Revision") != 0xDEADBEEFu || (uint)Get(unchangedDecoded, "Token") != 678u)
        throw new InvalidOperationException("Squad unchanged reply must stay a header-only roundtrip");
    object foreign = Snapshot(1, Wing()); Set(foreign, "Protocol", (byte)2);
    if ((byte)Get(Decode(snapshotType, Encode(snapshotType, foreign)), "Protocol") != 2 ||
        Get(Decode(snapshotType, Encode(snapshotType, foreign)), "Wings") != null)
        throw new InvalidOperationException("Squad reader must keep only the header of a foreign protocol");
    Reject(Encode(snapshotType, Snapshot(1, Wing(abilities: 16))));
    Reject(Encode(snapshotType, Snapshot(1, Wing(abilities: -1))));
    object expectedWing = Wing();
    foreach (FieldInfo field in wingType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(expectedWing), field.GetValue(output.GetValue(0)))) throw new InvalidOperationException("Squad wing roundtrip changed " + field.Name);
    object expectedPilot = Get(Snapshot(0, Wing()), "Pilot");
    foreach (FieldInfo field in pilotType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(expectedPilot), field.GetValue(Get(decoded, "Pilot")))) throw new InvalidOperationException("Squad pilot roundtrip changed " + field.Name);
    Reject(Encode(snapshotType, Snapshot(1, Wing(tier: 6))));
    Reject(Encode(snapshotType, Snapshot(1, Wing(alive: 4, members: 3))));
    Reject(Encode(snapshotType, Snapshot(1, Wing(members: 5))));
    Reject(Encode(snapshotType, Snapshot(1, Wing(returns: 4))));
    foreach (var invalid in new[] { ("Bonus", 21), ("Origin", -1), ("ActiveIndex", 8), ("HuntId", -1) })
    {
        object snapshot = Snapshot(1, Wing()); Set(snapshot, invalid.Item1, invalid.Item2);
        Reject(Encode(snapshotType, snapshot));
    }
    object excessiveCount = Encode(snapshotType, Snapshot(0, Wing()));
    int bitPosition = (int)writerType.GetProperty("BitPosition")!.GetValue(excessiveCount)!;
    writerType.GetField("_bitPosition", flags)!.SetValue(excessiveCount, bitPosition - 8);
    writerType.GetMethod("WriteByte")!.Invoke(excessiveCount, new object[] { (byte)9 });
    Reject(excessiveCount);
    Type progressType = plugin.GetType("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", true)!;
    object progress = Activator.CreateInstance(progressType)!;
    Set(progress, "Protocol", (byte)6); Set(progress, "Generation", 10001); Set(progress, "PerkMask", 123u);
    Set(progress, "Score", 70000); Set(progress, "Scene", 456u); Set(progress, "Token", 789u);
    Set(progress, "ScorePerPoint", 10000); Set(progress, "MaximumPoints", (byte)20);
    object progressResult = Decode(progressType, Encode(progressType, progress));
    if ((int)Get(progressResult, "Generation") != 10001 || (uint)Get(progressResult, "PerkMask") != 123u ||
        (int)Get(progressResult, "Score") != 70000 || (uint)Get(progressResult, "Scene") != 456u || (uint)Get(progressResult, "Token") != 789u ||
        (int)Get(progressResult, "ScorePerPoint") != 10000 || (byte)Get(progressResult, "MaximumPoints") != 20)
        throw new InvalidOperationException("Progression generation roundtrip failed");
    Type submitType = plugin.GetType("BoscaliSummer.Modules.Progression.Networking.ProgressionSubmit", true)!;
    object submit = Activator.CreateInstance(submitType)!;
    Set(submit, "Protocol", (byte)6); Set(submit, "Perk", (byte)4); Set(submit, "Scene", 456u); Set(submit, "Token", 789u); Set(submit, "Generation", 10001);
    object submitResult = Decode(submitType, Encode(submitType, submit));
    foreach (FieldInfo field in submitType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(submit), field.GetValue(submitResult))) throw new InvalidOperationException("Progression intent roundtrip changed " + field.Name);
    Set(progress, "Protocol", (byte)4);
    object oldProgress = Decode(progressType, Encode(progressType, progress));
    if ((byte)Get(oldProgress, "Protocol") != 4 || (int)Get(oldProgress, "Generation") != 0)
        throw new InvalidOperationException("Progression reader must keep only the header of a foreign protocol");
    string[] crestLayers = { "Shape", "Division", "Charge", "Accent", "Field", "Second", "Ink", "Number" };
    foreach (string crestType in new[] { "CrestSubmit", "CrestState" })
    {
        Type crest = plugin.GetType("BoscaliSummer.Modules.Progression.Networking." + crestType, true)!;
        object sent = Activator.CreateInstance(crest)!;
        Set(sent, "Protocol", (byte)6); Set(sent, "Paint", (byte)2);
        for (int i = 0; i < crestLayers.Length; i++) Set(sent, crestLayers[i], (byte)(i + 3));
        if (crestType == "CrestState") Set(sent, "Owner", 76561198000000000UL);
        object received = Decode(crest, Encode(crest, sent));
        foreach (FieldInfo field in crest.GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (!Equals(field.GetValue(sent), field.GetValue(received)))
                throw new InvalidOperationException(crestType + " roundtrip changed " + field.Name);
        Set(sent, "Protocol", (byte)5);
        object foreignCrest = Decode(crest, Encode(crest, sent));
        if ((byte)Get(foreignCrest, "Protocol") != 5 || (byte)Get(foreignCrest, "Paint") != 0 || (byte)Get(foreignCrest, "Shape") != 0)
            throw new InvalidOperationException(crestType + " reader must keep only the header of a foreign protocol");
    }
    Console.WriteLine("  Squad protocol-3 serializers: pilot/wing/query/revision roundtrip, header-only unchanged reply, 8-wing/192-char bounds, invalid strength/tier/return/header/count read as protocol 0; progression v6 foreign-header drop, crest submit/state roundtrip");
}

static void ProbeSupportSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Support.Networking.SupportNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 39)
        throw new InvalidOperationException("Support protocol differs from the operations contract");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);

    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;

    void Set(object value, string field, object data) =>
        value.GetType().GetField(field, flags)!.SetValue(value, data);

    object Get(object value, string field) =>
        value.GetType().GetField(field, flags)!.GetValue(value)!;

    byte[] Encode(Type type, object source)
    {
        var write = (Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(type)
            .GetProperty("Write", flags)!.GetValue(null)!;
        // Big enough for a full OPS snapshot; Mirage logs through Unity when it has to grow,
        // which cannot run here.
        object writer = Activator.CreateInstance(writerType, 4096)!;
        write.DynamicInvoke(writer, source);
        return (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
    }

    object Decode(Type type, byte[] bytes)
    {
        var read = (Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(type)
            .GetProperty("Read", flags)!.GetValue(null)!;
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            return read.DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }

    object Roundtrip(Type type, object source, string label)
    {
        object result = Decode(type, Encode(type, source));
        foreach (FieldInfo field in type.GetFields())
        {
            if (field.FieldType.IsArray || (field.FieldType.IsClass && field.FieldType != typeof(string))) continue;
            if (!Equals(field.GetValue(source), field.GetValue(result)))
                throw new InvalidOperationException("Support " + label + " changed " + field.Name);
        }
        return result;
    }

    Type requestType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", true)!;
    object request = Activator.CreateInstance(requestType)!;
    Set(request, "Protocol", (byte)39); Set(request, "RequestId", 7123); Set(request, "Action", (byte)6);
    Set(request, "X", 1234.5f); Set(request, "Y", 2345.5f); Set(request, "Z", -3456.5f);
    Roundtrip(requestType, request, "request");

    Type resultType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", true)!;
    object resultMessage = Activator.CreateInstance(resultType)!;
    Set(resultMessage, "Protocol", (byte)39); Set(resultMessage, "RequestId", 7123);
    Set(resultMessage, "Action", (byte)4); Set(resultMessage, "Result", (byte)1);
    Set(resultMessage, "CooldownSeconds", 30f); Set(resultMessage, "Radius", 6000f);
    Set(resultMessage, "Duration", 10f); Set(resultMessage, "Contacts", 48);
    Set(resultMessage, "X", 1234.5f); Set(resultMessage, "Y", 2345.5f); Set(resultMessage, "Z", -3456.5f);
    object resultBack = Roundtrip(resultType, resultMessage, "result");
    if ((int)Get(resultBack, "Contacts") != 48)
        throw new InvalidOperationException("Support result lost the sweep contact count");

    // Request parsing preserves its header; the host rejects it in Evaluate after decoding.
    Set(request, "Protocol", (byte)27);
    Roundtrip(requestType, request, "retired request header");
    foreach (Type type in new[] { resultType })
    {
        object rejected = Decode(type, new byte[] { 27 });
        if ((byte)Get(rejected, "Protocol") != 27 ||
            type.GetFields().Any(field => field.Name != "Protocol" && !Equals(field.GetValue(rejected), Activator.CreateInstance(field.FieldType))))
            throw new InvalidOperationException("Support did not reject the retired protocol for " + type.Name);
    }

    Type waypointType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.CruiseWaypointMessage", true)!;
    object waypoint = Activator.CreateInstance(waypointType)!;
    Set(waypoint, "Protocol", (byte)39); Set(waypoint, "RequestId", 913);
    Set(waypoint, "X", 1234.5f); Set(waypoint, "Z", -3456.5f); Set(waypoint, "Clear", true);
    object waypointBack = Roundtrip(waypointType, waypoint, "cruise waypoint");
    if ((int)Get(waypointBack, "RequestId") != 913 || !(bool)Get(waypointBack, "Clear"))
        throw new InvalidOperationException("Cruise waypoint intent roundtrip failed");
    if ((byte)Get(Decode(waypointType, new byte[] { 18 }), "Protocol") != 18)
        throw new InvalidOperationException("Cruise waypoint did not leave an old header unread");

    Type legsType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.CruiseLegsMessage", true)!;
    object legs = Activator.CreateInstance(legsType)!;
    Set(legs, "Protocol", (byte)39); Set(legs, "RequestId", 913); Set(legs, "OwnerId", 0x123456789ABCDEF0UL);
    Set(legs, "Result", (byte)1); Set(legs, "FactionName", "BOSCALI"); Set(legs, "LegCount", (byte)2);
    Set(legs, "X", new float[] { 1000f, 2000f, 0f, 0f, 0f, 0f });
    Set(legs, "Z", new float[] { -1000f, -2000f, 0f, 0f, 0f, 0f });
    Set(legs, "Dive", true); Set(legs, "Tti", 24.5f);
    object legsBack = Roundtrip(legsType, legs, "cruise legs");
    if ((ulong)Get(legsBack, "OwnerId") != 0x123456789ABCDEF0UL || (byte)Get(legsBack, "LegCount") != 2 ||
        ((float[])Get(legsBack, "X"))[1] != 2000f || !(bool)Get(legsBack, "Dive") ||
        Math.Abs((float)Get(legsBack, "Tti") - 24.5f) > 0.001f)
        throw new InvalidOperationException("Cruise legs roundtrip failed");
    if ((byte)Get(Decode(legsType, new byte[] { 18 }), "Protocol") != 18)
        throw new InvalidOperationException("Cruise legs did not leave an old header unread");

    Type cyberType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.CyberStateMessage", true)!;
    Type cyberDataType = plugin.GetType("BoscaliSummer.Modules.Support.Domain.Cyber.CyberStateData", true)!;
    object cyberData = Activator.CreateInstance(cyberDataType)!;
    Set(cyberData, "Protocol", (byte)39); Set(cyberData, "Active", true); Set(cyberData, "Seq", 7);
    Set(cyberData, "Now", 12.5f); Set(cyberData, "IntrusionCap", (byte)2); Set(cyberData, "HeldTotal", (byte)1);
    object cyberMessage = Activator.CreateInstance(cyberType)!;
    Set(cyberMessage, "Data", cyberData);
    object cyberBack = Get(Decode(cyberType, Encode(cyberType, cyberMessage)), "Data");
    if ((byte)Get(cyberBack, "Protocol") != 39 || (int)Get(cyberBack, "Seq") != 7 || !(bool)Get(cyberBack, "Active") ||
        Math.Abs((float)Get(cyberBack, "Now") - 12.5f) > 0.001f || (byte)Get(cyberBack, "IntrusionCap") != 2 || (byte)Get(cyberBack, "HeldTotal") != 1)
        throw new InvalidOperationException("CYBER state roundtrip failed");
    object cyberForeign = Get(Decode(cyberType, new byte[] { 27 }), "Data");
    if ((byte)Get(cyberForeign, "Protocol") != 27 || (bool)Get(cyberForeign, "Active"))
        throw new InvalidOperationException("CYBER state did not leave a foreign header unread");
    object cyberEmpty = Get(Decode(cyberType, new byte[0]), "Data");
    if ((byte)Get(cyberEmpty, "Protocol") != 0)
        throw new InvalidOperationException("An empty CYBER message must read inert");

    Type sofType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.SofStateMessage", true)!;
    Type sofDataType = plugin.GetType("BoscaliSummer.Modules.Support.Domain.Sof.SofStateData", true)!;
    object sofData = Activator.CreateInstance(sofDataType)!;
    Set(sofData, "Protocol", (byte)39); Set(sofData, "Active", true); Set(sofData, "Seq", 9);
    Set(sofData, "Now", 20.5f); Set(sofData, "TeamCap", (byte)3); Set(sofData, "TapIntrusions", (byte)2);
    object sofMessage = Activator.CreateInstance(sofType)!;
    Set(sofMessage, "Data", sofData);
    object sofBack = Get(Decode(sofType, Encode(sofType, sofMessage)), "Data");
    if ((byte)Get(sofBack, "Protocol") != 39 || (int)Get(sofBack, "Seq") != 9 || !(bool)Get(sofBack, "Active") ||
        Math.Abs((float)Get(sofBack, "Now") - 20.5f) > 0.001f || (byte)Get(sofBack, "TeamCap") != 3 || (byte)Get(sofBack, "TapIntrusions") != 2)
        throw new InvalidOperationException("SOF state roundtrip failed");
    object sofForeign = Get(Decode(sofType, new byte[] { 33 }), "Data");
    if ((byte)Get(sofForeign, "Protocol") != 33 || (bool)Get(sofForeign, "Active"))
        throw new InvalidOperationException("SOF state did not leave a foreign header unread");
    if ((byte)Get(Get(Decode(sofType, new byte[0]), "Data"), "Protocol") != 0)
        throw new InvalidOperationException("An empty SOF message must read inert");

    Type opsType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.OpsStateMessage", true)!;
    Type opsDataType = plugin.GetType("BoscaliSummer.Modules.Support.Domain.Ops.OpsStateData", true)!;
    object opsData = Activator.CreateInstance(opsDataType)!;
    Set(opsData, "Protocol", (byte)39); Set(opsData, "Active", true); Set(opsData, "CyberOps", true); Set(opsData, "Seq", 11);
    Set(opsData, "Now", 30.5f); Set(opsData, "BirdsDown", (byte)5);
    object opsMessage = Activator.CreateInstance(opsType)!;
    Set(opsMessage, "Data", opsData);
    object opsBack = Get(Decode(opsType, Encode(opsType, opsMessage)), "Data");
    if ((byte)Get(opsBack, "Protocol") != 39 || (int)Get(opsBack, "Seq") != 11 || !(bool)Get(opsBack, "Active") || !(bool)Get(opsBack, "CyberOps") ||
        Math.Abs((float)Get(opsBack, "Now") - 30.5f) > 0.001f || (byte)Get(opsBack, "BirdsDown") != 5)
        throw new InvalidOperationException("OPERATIONS state roundtrip failed");
    object opsForeign = Get(Decode(opsType, new byte[] { 34 }), "Data");
    if ((byte)Get(opsForeign, "Protocol") != 34 || (bool)Get(opsForeign, "Active"))
        throw new InvalidOperationException("OPERATIONS state did not leave a foreign header unread");
    if ((byte)Get(Get(Decode(opsType, new byte[0]), "Data"), "Protocol") != 0)
        throw new InvalidOperationException("An empty OPERATIONS message must read inert");

    Type frontType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.FrontStateMessage", true)!;
    Type frontDataType = plugin.GetType("BoscaliSummer.Modules.Support.Domain.Fronts.FrontStateData", true)!;
    object frontData = Activator.CreateInstance(frontDataType)!;
    Set(frontData, "Protocol", (byte)39); Set(frontData, "Seq", 13); Set(frontData, "Now", 41.5f); Set(frontData, "PriorityBy", "VIPER-2");
    // The reader rejects a directive the front does not offer; a default row is Recon, which CYBER lacks.
    Type directiveType = plugin.GetType("BoscaliSummer.Modules.Support.Domain.Fronts.FrontDirective", true)!;
    Set(((Array)Get(frontData, "Fronts")).GetValue(1)!, "Directive", Enum.ToObject(directiveType, (byte)2));
    object frontMessage = Activator.CreateInstance(frontType)!;
    Set(frontMessage, "Data", frontData);
    object frontBack = Get(Decode(frontType, Encode(frontType, frontMessage)), "Data");
    if ((byte)Get(frontBack, "Protocol") != 39 || (int)Get(frontBack, "Seq") != 13 || Math.Abs((float)Get(frontBack, "Now") - 41.5f) > 0.001f)
        throw new InvalidOperationException("FRONT state roundtrip failed");
    object frontForeign = Get(Decode(frontType, new byte[] { 37 }), "Data");
    if ((byte)Get(frontForeign, "Protocol") != 37)
        throw new InvalidOperationException("FRONT state did not leave a foreign header unread");
    if ((byte)Get(Get(Decode(frontType, new byte[0]), "Data"), "Protocol") != 0)
        throw new InvalidOperationException("An empty FRONT message must read inert");

    Console.WriteLine("  Support protocol-39 serializers: requests, results, cruise waypoints/legs, the faction-only CYBER, SOF, OPERATIONS and FRONT states and retired-header rejection");
}

static void ProbeEventsSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Events.Networking.EventsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 6)
        throw new InvalidOperationException("Events protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);

    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;

    void Set(object value, string field, object data) =>
        value.GetType().GetField(field, flags)!.SetValue(value, data);

    object Get(object value, string field) =>
        value.GetType().GetField(field, flags)!.GetValue(value)!;

    byte[] Encode(Type type, object source)
    {
        var write = (Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(type)
            .GetProperty("Write", flags)!.GetValue(null)!;
        // Big enough for a full OPS snapshot; Mirage logs through Unity when it has to grow,
        // which cannot run here.
        object writer = Activator.CreateInstance(writerType, 4096)!;
        write.DynamicInvoke(writer, source);
        return (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
    }

    object Decode(Type type, byte[] bytes)
    {
        var read = (Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(type)
            .GetProperty("Read", flags)!.GetValue(null)!;
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            return read.DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }

    void Roundtrip(Type type, object source, string label)
    {
        object result = Decode(type, Encode(type, source));
        foreach (FieldInfo field in type.GetFields())
        {
            object before = field.GetValue(source)!, after = field.GetValue(result)!;
            if (!(before is Array a && after is Array b ? a.Cast<object>().SequenceEqual(b.Cast<object>()) : Equals(before, after)))
                throw new InvalidOperationException("Events " + label + " changed " + field.Name);
        }
    }

    Type changedType = plugin.GetType("BoscaliSummer.Modules.Events.Networking.ActiveEventChanged", true)!;
    object changed = Activator.CreateInstance(changedType)!;
    Set(changed, "Protocol", (byte)6); Set(changed, "CatalogIndex", (sbyte)-1);
    Set(changed, "TargetFactionHash", 0);
    Set(changed, "StartedAtMissionTime", 12.5f); Set(changed, "EndsAtMissionTime", 912.5f);
    Set(changed, "EffectStrength", 1.25f);
    Roundtrip(changedType, changed, "state");
    object calm = Decode(changedType, Encode(changedType, changed));
    if ((sbyte)Get(calm, "CatalogIndex") != -1 || (int)Get(calm, "TargetFactionHash") != 0)
        throw new InvalidOperationException("Events state lost its calm sentinel");

    Set(changed, "CatalogIndex", (sbyte)11); Set(changed, "TargetFactionHash", -1234567);
    Roundtrip(changedType, changed, "targeted state");
    if ((int)Get(Decode(changedType, Encode(changedType, changed)), "TargetFactionHash") != -1234567)
        throw new InvalidOperationException("Events state lost its target faction hash");

    if ((byte)Get(Decode(changedType, new byte[] { 5 }), "Protocol") != 5 ||
        (sbyte)Get(Decode(changedType, new byte[] { 5 }), "CatalogIndex") != 0)
        throw new InvalidOperationException("Events did not reject an old state header");

    Console.WriteLine("  Events protocol-6 serializers: state (target + host strength + timestamps) roundtrip, old-header rejection");
}

static void ProbeSessionSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Session.Networking.SessionNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 1)
        throw new InvalidOperationException("Session protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type message = plugin.GetType("BoscaliSummer.Modules.Session.Networking.HostSettingsMessage", true)!;
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    var write = (Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(message)
        .GetProperty("Write", flags)!.GetValue(null)!;
    var read = (Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(message)
        .GetProperty("Read", flags)!.GetValue(null)!;

    object Roundtrip(byte[] bytes)
    {
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            return read.DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }

    object sample = Activator.CreateInstance(message)!;
    message.GetField("Protocol")!.SetValue(sample, (byte)1);
    message.GetField("Version")!.SetValue(sample, "0.1.1");
    message.GetField("Flags")!.SetValue(sample, (byte)3);
    message.GetField("Keys")!.SetValue(sample, new[] { "Support/CostMultiplier", "Squad/PilotLives" });
    message.GetField("Values")!.SetValue(sample, new[] { "1.5", "OneLife" });
    object writer = Activator.CreateInstance(writerType, 256)!;
    write.DynamicInvoke(writer, sample);
    object copy = Roundtrip((byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!);
    string[] keys = (string[])message.GetField("Keys")!.GetValue(copy)!;
    string[] values = (string[])message.GetField("Values")!.GetValue(copy)!;
    if ((byte)message.GetField("Flags")!.GetValue(copy)! != 3 || (string)message.GetField("Version")!.GetValue(copy)! != "0.1.1" ||
        !keys.SequenceEqual(new[] { "Support/CostMultiplier", "Squad/PilotLives" }) || !values.SequenceEqual(new[] { "1.5", "OneLife" }))
        throw new InvalidOperationException("Host settings roundtrip changed");

    object foreign = Roundtrip(new byte[] { 9 });
    if ((byte)message.GetField("Protocol")!.GetValue(foreign)! != 9 || message.GetField("Keys")!.GetValue(foreign) != null)
        throw new InvalidOperationException("Host settings reader must stop at a foreign protocol header");
    Console.WriteLine("  Session protocol-1 host settings roundtrip");
}

static void ProbeWeatherSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Weather.Networking.WeatherNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 8)
        throw new InvalidOperationException("Weather protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type message = plugin.GetType("BoscaliSummer.Modules.Weather.Networking.WeatherSyncMessage", true)!;
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    var write = (Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(message)
        .GetProperty("Write", flags)!.GetValue(null)!;
    var read = (Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(message)
        .GetProperty("Read", flags)!.GetValue(null)!;

    object Roundtrip(byte[] bytes)
    {
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            return read.DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }

    // One distinct value per field, so a swapped or dropped field cannot pass.
    var expected = new (string Name, object Value)[]
    {
        ("Protocol", (byte)8), ("TargetConditions", 0.31f), ("TargetCloudHeight", 1850f), ("TargetWindX", -7.5f),
        ("TargetWindZ", 12.25f), ("TargetTurbulence", 0.4f), ("MissionTimeSeconds", 4321u), ("ForcedRain", 0.65f),
        ("FieldSeed", 4000000007u), ("FieldStory", (byte)3), ("FieldSkyPin", (byte)46), ("FieldSalt", (byte)201),
        ("FieldMissionClass", (byte)5), ("FieldOpeningBase", 2410f), ("FieldHeading", 271.5f), ("FieldEpoch", 123.5f),
        ("FieldOffset", -45000.25f), ("FieldSystemSpeed", 31.5f), ("FieldDynamic", true), ("FieldManual", true),
        ("FieldSets", (byte)37), ("FieldHasAnchor", true), ("FieldAnchorX", 1111f), ("FieldAnchorZ", -2222f),
        ("FieldSetOriginSquall", 3333f), ("FieldSetOriginSupercell", 4444f), ("FieldSetOriginCell", 5555f),
    };
    if (message.GetFields(BindingFlags.Public | BindingFlags.Instance).Length != expected.Length)
        throw new InvalidOperationException("Weather sync probe sample does not cover every message field");
    object sample = Activator.CreateInstance(message)!;
    foreach (var (name, value) in expected) message.GetField(name)!.SetValue(sample, value);
    object writer = Activator.CreateInstance(writerType, 256)!;
    write.DynamicInvoke(writer, sample);
    object copy = Roundtrip((byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!);
    foreach (var (name, value) in expected)
        if (!Equals(message.GetField(name)!.GetValue(copy), value))
            throw new InvalidOperationException("Weather sync field " + name + " changed in the roundtrip: " +
                message.GetField(name)!.GetValue(copy) + " != " + value);
    object foreign = Roundtrip(new byte[] { 7 });
    if ((byte)message.GetField("Protocol")!.GetValue(foreign)! != 7 || (uint)message.GetField("FieldSeed")!.GetValue(foreign)! != 0u)
        throw new InvalidOperationException("Weather sync reader must stop at a foreign protocol header");
    Console.WriteLine("  Weather protocol-8 sync roundtrip: every field, foreign-protocol rejection");
}

static void ProbeWeatherConfig(Assembly plugin)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Assembly bepInEx = Assembly.Load("BepInEx");
    Type configFile = bepInEx.GetType("BepInEx.Configuration.ConfigFile", true)!;
    string path = Path.Combine(Path.GetTempPath(), "boscali-weather-config-probe-" + Environment.ProcessId + ".cfg");
    try
    {
        // A player's file from before the tape: the old switch is off and the old step timing is customised.
        File.WriteAllText(path, "[Weather]\n\nDynamicWeatherEnabled = false\nStateIntervalMinutes = 9\nStateFadeSeconds = 120\n");
        object config = Activator.CreateInstance(configFile, path, true)!;
        Type settingsType = plugin.GetType("BoscaliSummer.Modules.Weather.Configuration.WeatherSettings", true)!;
        object settings = Activator.CreateInstance(settingsType, config)!;
        plugin.GetType("BoscaliSummer.Core.Config.LegacyConfigMigration", true)!
            .GetMethod("RemoveEntries", flags)!.Invoke(null, new[] { config });
        configFile.GetMethod("Save")!.Invoke(config, null);
        string text = File.ReadAllText(path);
        foreach (string gone in new[] { "DynamicWeatherEnabled", "StateIntervalMinutes", "StateFadeSeconds" })
            if (text.Contains(gone + " ="))
                throw new InvalidOperationException("Weather config key " + gone + " survived the migration:\n" + text);
        // The stored false is deliberately not carried over: TapeMoves starts at its default.
        foreach (string kept in new[] { "TapeMoves = true", "Pace = 3", "SeedMode = Random", "FixedSeed = 0" })
            if (!text.Contains(kept))
                throw new InvalidOperationException("Weather config is missing '" + kept + "':\n" + text);
        Console.WriteLine("  Weather config: old keys purged, TapeMoves on, Pace 3, SeedMode Random, FixedSeed 0");
    }
    finally { File.Delete(path); }
}

/// <summary>The settings pages keep no copy of the TAC BOARD defaults: ResetToDefaults on the real config entries must restore every option and save the file once.</summary>
static void ProbeTacBoardConfig(Assembly plugin)
{
    Assembly bepInEx = Assembly.Load("BepInEx");
    Type configFile = bepInEx.GetType("BepInEx.Configuration.ConfigFile", true)!;
    Type settingsType = plugin.GetType("BoscaliSummer.Modules.Hud.Configuration.HudSettings", true)!;
    Type contract = plugin.GetType("BoscaliSummer.Core.Contracts.ITacBoardSettings", true)!;
    PropertyInfo[] options = contract.GetProperties();
    string baseline = Path.Combine(Path.GetTempPath(), "boscali-tacboard-probe-default-" + Environment.ProcessId + ".cfg");
    string path = Path.Combine(Path.GetTempPath(), "boscali-tacboard-probe-" + Environment.ProcessId + ".cfg");
    string isolated = Path.Combine(Path.GetTempPath(), "boscali-tacboard-probe-one-" + Environment.ProcessId + ".cfg");
    string menu = Path.Combine(Path.GetTempPath(), "boscali-tacboard-probe-menu-" + Environment.ProcessId + ".cfg");
    object[] Values(object settings) => options.Select(option => option.GetValue(settings)!).ToArray();
    try
    {
        object[] defaults = Values(Activator.CreateInstance(settingsType, Activator.CreateInstance(configFile, baseline, true))!);
        object file = Activator.CreateInstance(configFile, path, true)!;
        object settings = Activator.CreateInstance(settingsType, file)!;
        foreach (PropertyInfo option in options)
            option.SetValue(settings, option.PropertyType == typeof(bool) ? !(bool)option.GetValue(settings)!
                : option.PropertyType == typeof(float) ? 0.5f
                : Enum.ToObject(option.PropertyType, Enum.GetValues(option.PropertyType).Length - 1));
        object[] changed = Values(settings);
        for (int i = 0; i < options.Length; i++)
            if (Equals(changed[i], defaults[i])) throw new InvalidOperationException("TAC BOARD probe could not move " + options[i].Name + " off its default");
        // the moved values must really be on disk before the reset: a file nobody wrote reads back as the defaults, which would let the "saved" check below pass on its own
        object[] onDisk = Values(Activator.CreateInstance(settingsType, Activator.CreateInstance(configFile, path, true))!);
        for (int i = 0; i < options.Length; i++)
            if (!Equals(onDisk[i], changed[i])) throw new InvalidOperationException("TAC BOARD probe: the moved " + options[i].Name + " was not saved (file has " + onDisk[i] + ", set " + changed[i] + ")");
        // HudSettings.Snapshot is what the board reads every tick: moved alone, each option must reach the BoardOptions field of its own name and no other (a swapped assignment would hide the wrong page).
        Type boardOptions = plugin.GetType("BoscaliSummer.Modules.Hud.Domain.BoardOptions", true)!;
        MethodInfo snapshot = settingsType.GetMethod("Snapshot")!;
        FieldInfo[] boardFields = options.Select(option => boardOptions.GetField(option.Name) ?? throw new InvalidOperationException("BoardOptions has no field for " + option.Name)).ToArray();
        object alone = Activator.CreateInstance(settingsType, Activator.CreateInstance(configFile, isolated, true))!;
        for (int i = 0; i < options.Length; i++)
        {
            options[i].SetValue(alone, changed[i]);
            object[] snapArgs = { Activator.CreateInstance(boardOptions)! };
            snapshot.Invoke(alone, snapArgs);
            for (int j = 0; j < options.Length; j++)
            {
                object want = j == i ? options[j].GetValue(alone)! : defaults[j];
                if (!Equals(boardFields[j].GetValue(snapArgs[0]), want))
                    throw new InvalidOperationException("TAC BOARD Snapshot: moving " + options[i].Name + " left BoardOptions." + boardFields[j].Name + " at " + boardFields[j].GetValue(snapArgs[0]) + ", not " + want);
            }
            contract.GetMethod("ResetToDefaults")!.Invoke(alone, null);
        }
        // The F1 window's plain view lists the master switch (ConfigMenu hints it as no advanced setting) and keeps the rest of the section behind the Advanced checkbox.
        // The whole mod's settings bind under BepInEx's paths: point them at a scratch folder first (the Wing settings make a data folder under the config path).
        string scratch = Path.Combine(Path.GetTempPath(), "boscali-tacboard-probe-root-" + Environment.ProcessId);
        MethodInfo setPaths = bepInEx.GetType("BepInEx.Paths", true)!.GetMethod("SetExecutablePath", AllMembers)!;
        setPaths.Invoke(null, setPaths.GetParameters().Select((parameter, index) => index == 0 ? Path.Combine(scratch, "probe.exe") : parameter.HasDefaultValue ? parameter.DefaultValue : null).ToArray());
        Type modConfiguration = plugin.GetType("BoscaliSummer.Core.Config.ModConfiguration", true)!;
        object whole = Activator.CreateInstance(modConfiguration, Activator.CreateInstance(configFile, menu, true))!;
        object board = modConfiguration.GetProperty("TacBoard")!.GetValue(whole)!;
        bool? Advanced(string entryName)
        {
            object entry = settingsType.GetProperty(entryName)!.GetValue(board)!;
            object description = entry.GetType().GetProperty("Description")!.GetValue(entry)!;
            foreach (object tag in (object[])description.GetType().GetProperty("Tags")!.GetValue(description)!)
            {
                FieldInfo advanced = tag.GetType().GetField("IsAdvanced");
                if (advanced != null) return (bool?)advanced.GetValue(tag);
            }
            return null;
        }
        if (Advanced("Enabled") != false) throw new InvalidOperationException("The F1 window's plain view must list TacBoard.Enabled (ConfigMenu.Switches)");
        if (Advanced("Layout") != true) throw new InvalidOperationException("TacBoard.Layout must sit behind the F1 window's Advanced checkbox");
        contract.GetMethod("ResetToDefaults")!.Invoke(settings, null);
        object[] reset = Values(settings);
        object[] saved = Values(Activator.CreateInstance(settingsType, Activator.CreateInstance(configFile, path, true))!);
        for (int i = 0; i < options.Length; i++)
        {
            if (!Equals(reset[i], defaults[i])) throw new InvalidOperationException("TAC BOARD ResetToDefaults left " + options[i].Name + " at " + reset[i] + ", not " + defaults[i]);
            if (!Equals(saved[i], defaults[i])) throw new InvalidOperationException("TAC BOARD ResetToDefaults did not save " + options[i].Name);
        }
        if (!(bool)configFile.GetProperty("SaveOnConfigSet")!.GetValue(file)!) throw new InvalidOperationException("TAC BOARD ResetToDefaults left the config file's save-on-set off");
        Console.WriteLine("  TAC BOARD config: " + options.Length + " options moved, each alone through Snapshot, listed in the F1 window, reset to their bound defaults and saved");
    }
    finally
    {
        File.Delete(baseline); File.Delete(path); File.Delete(isolated); File.Delete(menu);
        string scratch = Path.Combine(Path.GetTempPath(), "boscali-tacboard-probe-root-" + Environment.ProcessId);
        if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
    }
}

static void ProbeTheaterOpsHostSettings(Assembly plugin)
{
    // The one THEATER OPS table (S4): two rows with distinct ids, AI STAFF CONVOYS off by default (D3), and FRONTLINE TACTICS reads as
    // unavailable, with its reason, exactly when Ground Control RTS owns the orders.
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Assembly bepInEx = Assembly.Load("BepInEx");
    Type configFile = bepInEx.GetType("BepInEx.Configuration.ConfigFile", true)!;
    string path = Path.Combine(Path.GetTempPath(), "boscali-theaterops-settings-probe-" + Environment.ProcessId + ".cfg");
    try
    {
        object config = Activator.CreateInstance(configFile, path, true)!;
        object settings = Activator.CreateInstance(plugin.GetType("BoscaliSummer.Modules.TheaterOps.Configuration.TheaterOpsSettings", true)!, config)!;
        MethodInfo build = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Configuration.TheaterOpsHostSettings", true)!.GetMethod("Build", flags)!;
        bool rts = false;
        object table = build.Invoke(null, new object[] { settings, new Func<bool>(() => rts) })!;
        Type tableType = table.GetType();
        Action refresh = () => tableType.GetMethod("Refresh")!.Invoke(table, null);
        Func<List<object>> rows = () => ((System.Collections.IEnumerable)tableType.GetProperty("Rows")!.GetValue(table)!).Cast<object>().ToList();
        refresh();
        List<object> list = rows();
        Func<object, string, object> get = (row, name) => row.GetType().GetProperty(name)!.GetValue(row);
        if (list.Count != 2 || list.Select(row => (int)get(row, "Id")!).Distinct().Count() != 2)
            throw new InvalidOperationException("THEATER OPS must declare two rows with distinct ids");
        if ((string)tableType.GetProperty("Section")!.GetValue(table) != "THEATER OPS")
            throw new InvalidOperationException("THEATER OPS table has the wrong section name");
        object tactics = list.Single(row => (int)get(row, "Id")! == 1), convoys = list.Single(row => (int)get(row, "Id")! == 2);
        if ((bool)get(convoys, "Value")! || (string)get(convoys, "ValueText") != "OFF")
            throw new InvalidOperationException("AI STAFF CONVOYS must default to OFF (D3)");
        if (!(bool)get(tactics, "Interactive")! || get(tactics, "Reason") != null)
            throw new InvalidOperationException("FRONTLINE TACTICS must be available without an RTS commander");
        rts = true;
        refresh();
        if ((bool)get(tactics, "Interactive")! || (string)get(tactics, "Reason") != "GROUND CONTROL RTS OWNS GROUND ORDERS")
            throw new InvalidOperationException("FRONTLINE TACTICS must say why it is unavailable under an RTS commander");
        tableType.GetMethod("Toggle", new[] { typeof(int) })!.Invoke(table, new object[] { 2 });
        refresh();
        if (!(bool)get(convoys, "Value")! || (string)get(convoys, "ValueText") != "ON")
            throw new InvalidOperationException("AI STAFF CONVOYS must toggle on from the table");
        Console.WriteLine("  THEATER OPS host table: two distinct rows, convoys off by default, tactics unavailable under RTS");
    }
    finally { File.Delete(path); }
}

static void ProbeFactionMoraleSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Command.Networking.FactionMoraleNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 1)
        throw new InvalidOperationException("Faction morale protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type message = plugin.GetType("BoscaliSummer.Modules.Command.Networking.FactionMoraleChanged", true)!;
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    object sample = Activator.CreateInstance(message)!;
    message.GetField("Protocol")!.SetValue(sample, (byte)1);
    message.GetField("FactionHash")!.SetValue(sample, -1234567);
    message.GetField("Morale")!.SetValue(sample, 42.5f);
    var write = (Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(message)
        .GetProperty("Write", flags)!.GetValue(null)!;
    object writer = Activator.CreateInstance(writerType, 64)!;
    write.DynamicInvoke(writer, sample);
    byte[] bytes = (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
    object reader = Activator.CreateInstance(readerType)!;
    try
    {
        readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
        var read = (Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(message)
            .GetProperty("Read", flags)!.GetValue(null)!;
        object copy = read.DynamicInvoke(reader)!;
        if ((byte)message.GetField("Protocol")!.GetValue(copy)! != 1 ||
            (int)message.GetField("FactionHash")!.GetValue(copy)! != -1234567 ||
            (float)message.GetField("Morale")!.GetValue(copy)! != 42.5f)
            throw new InvalidOperationException("Faction morale snapshot roundtrip changed");
    }
    finally { ((IDisposable)reader).Dispose(); }
    Console.WriteLine("  Faction morale protocol-1 snapshot roundtrip");
}

static void ProbeTrenchSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Trenches.Networking.TrenchNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 1)
        throw new InvalidOperationException("Trench protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;

    object Roundtrip(Type message, object sample, string label)
    {
        var write = (Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(message)
            .GetProperty("Write", flags)!.GetValue(null)!;
        object writer = Activator.CreateInstance(writerType, 512)!;
        write.DynamicInvoke(writer, sample);
        byte[] bytes = (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            var read = (Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(message)
                .GetProperty("Read", flags)!.GetValue(null)!;
            return read.DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }

    Type geometryType = plugin.GetType("BoscaliSummer.Modules.Trenches.Networking.TrenchGeometryMessage", true)!;
    object geometry = Activator.CreateInstance(geometryType)!;
    Type v3 = Type.GetType("UnityEngine.Vector3, UnityEngine.CoreModule", true)!;
    object Station(float x, float z) => Activator.CreateInstance(v3, x, 0f, z)!;
    Array curve = Array.CreateInstance(v3, 2);
    curve.SetValue(Station(10f, 20f), 0);
    curve.SetValue(Station(30f, 40f), 1);
    Array threat = Array.CreateInstance(v3, 2);
    threat.SetValue(Station(0f, 1f), 0);
    threat.SetValue(Station(0f, 1f), 1);
    Array link = Array.CreateInstance(v3, 3);
    link.SetValue(Station(10f, 20f), 0);
    link.SetValue(Station(11f, 25f), 1);
    link.SetValue(Station(12f, 30f), 2);
    Array links = Array.CreateInstance(v3.MakeArrayType(), 1);
    links.SetValue(link, 0);
    geometryType.GetField("Protocol")!.SetValue(geometry, (byte)1);
    geometryType.GetField("LineId")!.SetValue(geometry, 7);
    geometryType.GetField("Stage")!.SetValue(geometry, (byte)2);
    geometryType.GetField("OwnerHash")!.SetValue(geometry, -1234567);
    geometryType.GetField("Curve")!.SetValue(geometry, curve);
    geometryType.GetField("Threat")!.SetValue(geometry, threat);
    geometryType.GetField("Support")!.SetValue(geometry, Array.CreateInstance(v3, 0));
    geometryType.GetField("Redoubt")!.SetValue(geometry, null);
    geometryType.GetField("Links")!.SetValue(geometry, links);
    geometryType.GetField("Spurs")!.SetValue(geometry, null);
    object geometryBack = Roundtrip(geometryType, geometry, "geometry");
    Array curveBack = (Array)geometryType.GetField("Curve")!.GetValue(geometryBack)!;
    Array threatBack = (Array)geometryType.GetField("Threat")!.GetValue(geometryBack)!;
    Array supportBack = (Array)geometryType.GetField("Support")!.GetValue(geometryBack)!;
    Array redoubtBack = (Array)geometryType.GetField("Redoubt")!.GetValue(geometryBack)!;
    Array linksBack = (Array)geometryType.GetField("Links")!.GetValue(geometryBack)!;
    Array spursBack = (Array)geometryType.GetField("Spurs")!.GetValue(geometryBack)!;
    float X(object station) => (float)v3.GetField("x")!.GetValue(station)!;
    float Z(object station) => (float)v3.GetField("z")!.GetValue(station)!;
    float Y(object station) => (float)v3.GetField("y")!.GetValue(station)!;
    if ((byte)geometryType.GetField("Protocol")!.GetValue(geometryBack)! != 1 ||
        (int)geometryType.GetField("LineId")!.GetValue(geometryBack)! != 7 ||
        (byte)geometryType.GetField("Stage")!.GetValue(geometryBack)! != 2 ||
        (int)geometryType.GetField("OwnerHash")!.GetValue(geometryBack)! != -1234567 ||
        curveBack.Length != 2 || X(curveBack.GetValue(0)!) != 10f || Z(curveBack.GetValue(1)!) != 40f ||
        Y(curveBack.GetValue(0)!) != 0f || threatBack.Length != 2 || X(threatBack.GetValue(1)!) != 0f ||
        supportBack.Length != 0 || redoubtBack.Length != 0 ||
        linksBack.Length != 1 || ((Array)linksBack.GetValue(0)!).Length != 3 ||
        Z(((Array)linksBack.GetValue(0)!).GetValue(2)!) != 30f || spursBack.Length != 0)
        throw new InvalidOperationException("Trench geometry roundtrip changed");
    Console.WriteLine("  Trench protocol-1 geometry roundtrip");

    Type stateType = plugin.GetType("BoscaliSummer.Modules.Trenches.Networking.TrenchStateMessage", true)!;
    object state = Activator.CreateInstance(stateType)!;
    stateType.GetField("Protocol")!.SetValue(state, (byte)1);
    stateType.GetField("LineId")!.SetValue(state, 7);
    stateType.GetField("Stage")!.SetValue(state, (byte)3);
    stateType.GetField("Defenders")!.SetValue(state, (byte)6);
    stateType.GetField("Suppressed")!.SetValue(state, true);
    stateType.GetField("Overrun")!.SetValue(state, false);
    object stateBack = Roundtrip(stateType, state, "state");
    if ((byte)stateType.GetField("Protocol")!.GetValue(stateBack)! != 1 ||
        (int)stateType.GetField("LineId")!.GetValue(stateBack)! != 7 ||
        (byte)stateType.GetField("Stage")!.GetValue(stateBack)! != 3 ||
        (byte)stateType.GetField("Defenders")!.GetValue(stateBack)! != 6 ||
        (bool)stateType.GetField("Suppressed")!.GetValue(stateBack)! != true ||
        (bool)stateType.GetField("Overrun")!.GetValue(stateBack)! != false)
        throw new InvalidOperationException("Trench state roundtrip changed");
    Console.WriteLine("  Trench protocol-1 state roundtrip");

    Type removedType = plugin.GetType("BoscaliSummer.Modules.Trenches.Networking.TrenchLineRemovedMessage", true)!;
    object removed = Activator.CreateInstance(removedType)!;
    removedType.GetField("Protocol")!.SetValue(removed, (byte)1);
    removedType.GetField("LineId")!.SetValue(removed, 7);
    object removedBack = Roundtrip(removedType, removed, "removed");
    if ((byte)removedType.GetField("Protocol")!.GetValue(removedBack)! != 1 ||
        (int)removedType.GetField("LineId")!.GetValue(removedBack)! != 7)
        throw new InvalidOperationException("Trench removal roundtrip changed");
    Console.WriteLine("  Trench protocol-1 removal roundtrip");
}

static void ProbeTheaterOpsSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.TheaterOpsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 3)
        throw new InvalidOperationException("TheaterOps protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);

    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;

    void Set(object value, string field, object data) =>
        value.GetType().GetField(field, flags)!.SetValue(value, data);

    object Get(object value, string field) =>
        value.GetType().GetField(field, flags)!.GetValue(value)!;

    byte[] Encode(Type type, object source)
    {
        var write = (Delegate)mirage.GetType("Mirage.Serialization.Writer`1", true)!.MakeGenericType(type)
            .GetProperty("Write", flags)!.GetValue(null)!;
        // Big enough for a full OPS snapshot; Mirage logs through Unity when it has to grow,
        // which cannot run here.
        object writer = Activator.CreateInstance(writerType, 4096)!;
        write.DynamicInvoke(writer, source);
        return (byte[])writerType.GetMethod("ToArray")!.Invoke(writer, null)!;
    }

    object Decode(Type type, byte[] bytes)
    {
        var read = (Delegate)mirage.GetType("Mirage.Serialization.Reader`1", true)!.MakeGenericType(type)
            .GetProperty("Read", flags)!.GetValue(null)!;
        object reader = Activator.CreateInstance(readerType)!;
        try
        {
            readerType.GetMethod("Reset", new[] { typeof(byte[]) })!.Invoke(reader, new object[] { bytes });
            return read.DynamicInvoke(reader)!;
        }
        finally { ((IDisposable)reader).Dispose(); }
    }

    void Roundtrip(Type type, object source, string label)
    {
        object result = Decode(type, Encode(type, source));
        foreach (FieldInfo field in type.GetFields())
        {
            if (!Equals(field.GetValue(source), field.GetValue(result)))
                throw new InvalidOperationException("TheaterOps " + label + " changed " + field.Name);
        }
    }

    Type queryType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.TheaterPriorityQuery", true)!;
    object query = Activator.CreateInstance(queryType)!;
    Set(query, "Protocol", (byte)3);
    Roundtrip(queryType, query, "query");

    Type stateType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.TheaterPriorityState", true)!;
    object state = Activator.CreateInstance(stateType)!;
    Set(state, "Protocol", (byte)3); Set(state, "Active", (byte)1);
    Set(state, "Faction", "Coalition"); Set(state, "Key", "obj-north");
    Set(state, "Label", "Northern Corridor");
    Set(state, "X", 1200f); Set(state, "Y", 30f); Set(state, "Z", -800f);
    Roundtrip(stateType, state, "state");

    // A clear carries only the header and the faction; identity fields are deliberately absent.
    object clear = Activator.CreateInstance(stateType)!;
    Set(clear, "Protocol", (byte)3); Set(clear, "Active", (byte)0);
    Set(clear, "Faction", "Coalition");
    object clearResult = Decode(stateType, Encode(stateType, clear));
    if ((byte)Get(clearResult, "Active") != 0 || (string)Get(clearResult, "Faction") != "Coalition" ||
        Get(clearResult, "Key") != null || Get(clearResult, "Label") != null)
        throw new InvalidOperationException("TheaterOps clear state lost its faction or sentinel");

    if ((byte)Get(Decode(stateType, new byte[] { 9 }), "Protocol") != 9 ||
        (byte)Get(Decode(stateType, new byte[] { 9 }), "Active") != 0)
        throw new InvalidOperationException("TheaterOps did not reject an unknown state header");

    // ---- Living Front protocol 3 (spec 6.1): one body, built by reflection so a renamed field fails here.
    Type livingNet = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontNet", true)!;
    if ((byte)livingNet.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 3)
        throw new InvalidOperationException("Living Front protocol changed without updating its probe");
    livingNet.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    string netNs = "BoscaliSummer.Modules.TheaterOps.Networking.";
    Type livingQueryType = plugin.GetType(netNs + "LivingFrontQuery", true)!;
    object livingQuery = Activator.CreateInstance(livingQueryType)!;
    Set(livingQuery, "Protocol", (byte)3); Set(livingQuery, "Session", 5);
    Roundtrip(livingQueryType, livingQuery, "living query");
    Type livingIntentType = plugin.GetType(netNs + "LivingFrontIntent", true)!;
    object livingIntent = Activator.CreateInstance(livingIntentType)!;
    Set(livingIntent, "Protocol", (byte)3); Set(livingIntent, "RequestId", 11); Set(livingIntent, "Session", 5); Set(livingIntent, "HostEpoch", 3); Set(livingIntent, "Kind", (byte)0);
    Set(livingIntent, "Posture", (byte)0); Set(livingIntent, "Id", 7);
    Set(livingIntent, "Revision", 4);
    Roundtrip(livingIntentType, livingIntent, "living intent");

    Type snapshotType = plugin.GetType(netNs + "LivingFrontSnapshot", true)!;
    Type resultType = plugin.GetType(netNs + "LivingFrontResult", true)!;
    Type CT(string name) => plugin.GetType("BoscaliSummer.Core.Contracts." + name, true)!;
    object New(string name) => Activator.CreateInstance(CT(name))!;
    object En(string name, int value) => Enum.ToObject(CT(name), value);
    Array Arr(string name, params object[] items)
    {
        Array array = Array.CreateInstance(CT(name), items.Length);
        for (int i = 0; i < items.Length; i++) array.SetValue(items[i], i);
        return array;
    }
    void SetAll(object target, params object[] pairs) { for (int i = 0; i < pairs.Length; i += 2) Set(target, (string)pairs[i], pairs[i + 1]); }
    void Same(object a, object b, string path)
    {
        if (a == null || b == null) { if (a != b) throw new InvalidOperationException("Living Front " + path + " changed: null"); return; }
        Type t = a.GetType();
        if (t.IsPrimitive || t.IsEnum || t == typeof(string))
        {
            if (!Equals(a, b)) throw new InvalidOperationException("Living Front " + path + " changed: " + a + " -> " + b);
            return;
        }
        if (a is Array xa)
        {
            Array xb = (Array)b;
            if (xa.Length != xb.Length) throw new InvalidOperationException("Living Front " + path + " length changed");
            for (int i = 0; i < xa.Length; i++) Same(xa.GetValue(i), xb.GetValue(i), path + "[" + i + "]");
            return;
        }
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) Same(f.GetValue(a), f.GetValue(b), path + "." + f.Name);
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.GetIndexParameters().Length == 0) Same(p.GetValue(a), p.GetValue(b), path + "." + p.Name);
    }
    object Front(int i, string name = null, float x = float.NaN)
    {
        object f = New("TheaterFrontView");
        SetAll(f, "Key", "sector:" + i, "Name", name ?? "FRONT " + i, "X", float.IsNaN(x) ? 1000f * i : x, "Z", -500f * i, "Pressure", .1f * i, "Trend", .1f * i - .4f,
            "ObservedAgeSeconds", i % 2 == 0 ? 0f : 45f, "Status", En("TheaterFrontStatus", i % 4), "Observed", i % 2 == 0,
            "Objective", (i & 2) != 0, "Held", (i & 4) != 0, "NearFriendly", (byte)i, "NearHostile", (byte)(i + 1), "Carriers", (byte)254);
        return f;
    }
    object Proposal(int i)
    {
        object p = New("TheaterProposalView");
        SetAll(p, "Id", i + 1, "Revision", 3, "Kind", En("TheaterOpKind", i % 3), "Name", "PROPOSAL " + i, "TargetKey", "key:" + i, "X", 100f * i, "Z", -100f * i,
            "Radius", 4000f, "Pressure", .3f * i, "Risk", En("TheaterRisk", i % 3), "Carrier", En("TheaterCarrier", i % 3),
            "FriendlyGround", (byte)(3 + i), "FriendlyOther", (byte)255, "HostileTracked", (byte)i, "Recommended", i == 0);
        return p;
    }
    object Operation(int marks = 8)
    {
        object forces = New("TheaterForcesView");
        object[] list = new object[marks];
        for (int i = 0; i < marks; i++) { list[i] = New("TheaterForceMark"); SetAll(list[i], "X", 100f * i, "Z", -50f * i, "Stage", (byte)(i % 6), "Alive", (byte)(i % 7)); }
        SetAll(forces, "Groups", (byte)4, "Vehicles", (byte)11, "Formed", (byte)12, "Rally", (byte)1, "Line", (byte)1, "Assault", (byte)1, "Contact", (byte)1, "Withdraw", (byte)0,
            "Moving", (byte)5, "Steered", (byte)6, "OnSlot", (byte)3, "Stalled", (byte)192, "EtaSeconds", (ushort)3600, "NearestToFixMeters", 8200f,
            "Marks", Arr("TheaterForceMark", list));
        object op = New("TheaterOperationView");
        SetAll(op, "Id", 7, "Revision", 3, "Kind", En("TheaterOpKind", 1), "TargetKey", "key:op", "Name", "PORT AURELIA", "X", 1200f, "Z", 1700f, "OriginX", -3000f, "OriginZ", 900f,
            "Radius", 4000f, "Phase", En("TheaterPhase", 3), "PhaseNote", En("TheaterPhaseNote", 2), "ElapsedSeconds", 95f, "LimitSeconds", 600f, "Progress", .42f, "Forces", forces,
            "PickedBy", "VIPER 1-1", "PickedById", 76561198000000001UL, "PickedByStaff", true, "LockSeconds", 118f);
        return op;
    }
    object Wave(int ws = 1, int block = 8, int credits = 2, int perWave = 3, int cost = 1850, int pool = 7400, float depotX = 12500f, bool depotKnown = true, int bought = 5, float lastAgo = 120f) =>
        Activator.CreateInstance(CT("TheaterWaveView"), En("WaveState", ws), En("WaveBlock", block), credits, perWave, "TARANTULA COLUMN", cost, pool, 20, bought, lastAgo, depotKnown, depotX, -6750f)!;
    object Outcome(int seq = 4, int credits = 3) => Activator.CreateInstance(CT("TheaterOutcomeView"), seq, "SORTIE COMPLETE", "VIPER 1-1", 4242.5f, -300f, true, false, credits)!;
    object Census()
    {
        object c = New("TheaterCensus");
        SetAll(c, "Known", true, "AirFriendly", (short)12, "AirParked", (short)3, "AirHostileTracked", (short)2, "GroundFriendly", (short)40, "GroundHostileTracked", (short)22,
            "ShipsFriendly", (short)1, "ShipsHostileTracked", (short)0, "BasesFriendly", (short)4, "BasesHostile", (short)3, "BasesNeutral", (short)1, "BasesContested", (short)2, "Radars", (short)6,
            "SectorsFriendly", (short)30, "SectorsHostile", (short)25, "SectorsContested", (short)9, "SectorsNeutral", (short)2, "SortieCap", (short)3, "SortieSead", (short)1,
            "SortieCas", (short)2, "SortieStrike", (short)1, "SortieTransit", (short)0, "FrontMetres", 130000f, "Threat", (byte)3, "ThreatWhy", En("TheaterThreatWhy", 4));
        return c;
    }
    object Body(int fronts = 8, int proposals = 3, bool operation = true, bool wave = true, bool result = true, bool outcome = true, int marks = 8)
    {
        object body = New("TheaterWarSnapshot");
        object[] f = new object[fronts], p = new object[proposals];
        for (int i = 0; i < fronts; i++) f[i] = Front(i, i == 0 ? new string('N', 32) : null, i == 0 ? 1234.5f : float.NaN);
        for (int i = 0; i < proposals; i++) p[i] = Proposal(i);
        object last = New("TheaterResultView");
        SetAll(last, "OperationId", 6, "Kind", En("TheaterOpKind", 0), "Name", "HARBOR", "Outcome", En("TheaterOutcome", 1), "Why", En("TheaterWhy", 11), "AgeSeconds", 300f, "Funds", 150, "Morale", -4);
        SetAll(body, "Faction", "Coalition", "Posture", En("TheaterWarPosture", 2), "Caps", En("TheaterCaps", 0x1FF), "Reason", En("TheaterEmptyReason", 16), "ReasonSeconds", 12f, "SensedAgeSeconds", 3f,
            "StaffPickSeconds", 44f, "GroundReason", En("GroundReason", 7), "Fronts", Arr("TheaterFrontView", f), "Proposals", Arr("TheaterProposalView", p),
            "Operation", operation ? Operation(marks) : null, "Wave", wave ? Wave(depotX: 777.25f) : null, "LastResult", result ? last : null,
            "LastOutcome", outcome ? Outcome() : null, "Census", Census(),
            "Log", new[] { "L1", "L2", "L3", "L4", "L5", "L6", "L7", new string('G', 64) });
        return body;
    }
    object WithWave(object body, object wave) { Set(body, "Wave", wave); return body; }
    object Header(object body, byte protocol = 3, bool has = true)
    {
        object h = Activator.CreateInstance(snapshotType)!;
        SetAll(h, "Protocol", protocol, "HasSnapshot", has, "Session", 5, "HostEpoch", 3, "Faction", "Coalition", "Body", body);
        return h;
    }
    byte[] Wire(object body) => Encode(snapshotType, Header(body));
    bool Accepted(byte[] bytes) => (byte)Get(Decode(snapshotType, bytes), "Protocol") == 3;
    void Refused(byte[] bytes, string what)
    {
        if (Accepted(bytes)) throw new InvalidOperationException("Living Front accepted " + what);
    }
    int Find(byte[] data, byte[] pattern)
    {
        for (int i = 0; i + pattern.Length <= data.Length; i++)
        {
            int k = 0;
            while (k < pattern.Length && data[i + k] == pattern[k]) k++;
            if (k == pattern.Length) return i;
        }
        throw new InvalidOperationException("Living Front probe could not find a marker in the wire");
    }
    int FirstDiff(byte[] a, byte[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return i;
        throw new InvalidOperationException("Living Front probe found no difference");
    }
    byte[] Patched(byte[] data, int index, byte value) { byte[] copy = (byte[])data.Clone(); copy[index] = value; return copy; }
    // A well-formed wire with `times` copies of the one item that `with1` has and `with0` lacks (the count byte is the first difference),
    // so only the count rule can refuse it.
    byte[] Repeated(byte[] with1, byte[] with0, int times)
    {
        int at = FirstDiff(with0, with1), length = with1.Length - with0.Length;
        var output = new System.Collections.Generic.List<byte>(with1.Take(at));
        output.Add((byte)times);
        for (int i = 0; i < times; i++) output.AddRange(with1.Skip(at + 1).Take(length));
        output.AddRange(with1.Skip(at + 1 + length));
        return output.ToArray();
    }
    MethodInfo[] writerExt = mirage.GetTypes().Where(t => t.IsAbstract && t.IsSealed)
        .SelectMany(t => t.GetMethods(flags)).Where(m => m.IsStatic && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == writerType).ToArray();
    byte[] WriteString(string text)
    {
        object w = Activator.CreateInstance(writerType, 256)!;
        MethodInfo own = writerType.GetMethods(flags).FirstOrDefault(m => m.Name == "WriteString" && m.GetParameters().Length == 1);
        if (own != null) own.Invoke(w, new object[] { text });
        else writerExt.First(m => m.Name == "WriteString").Invoke(null, new object[] { w, text });
        return (byte[])writerType.GetMethod("ToArray")!.Invoke(w, null)!;
    }
    // One more character in the string that holds `marker`: a well-formed wire the writer (which clips) could never produce.
    byte[] Lengthen(byte[] data, string marker)
    {
        int w = WriteString(marker).Length - marker.Length;
        long length = 0;
        byte[] enc = WriteString(marker);
        for (int i = w - 1; i >= 0; i--) length = (length << 8) | enc[i];
        int at = Find(data, System.Text.Encoding.UTF8.GetBytes(marker));
        byte[] output = new byte[data.Length + 1];
        Array.Copy(data, output, at);
        length++;
        for (int i = 0; i < w; i++) output[at - w + i] = (byte)(length >> (8 * i));
        output[at + marker.Length] = (byte)marker[0];
        Array.Copy(data, at + marker.Length, output, at + marker.Length + 1, data.Length - at - marker.Length);
        return output;
    }

    // Full snapshot: every enum, every flag, 8 fronts, 3 proposals, an operation with 8 marks, wave, result, outcome, census, 8 log lines.
    object fullBody = Body();
    object fullHeader = Header(fullBody);
    object decodedHeader = Decode(snapshotType, Encode(snapshotType, fullHeader));
    Same(Get(fullHeader, "Body"), Get(decodedHeader, "Body"), "Body");
    if ((string)Get(Get(decodedHeader, "Body"), "Faction") != "Coalition" || (int)Get(decodedHeader, "Session") != 5 || (int)Get(decodedHeader, "HostEpoch") != 3)
        throw new InvalidOperationException("Living Front header did not roundtrip");
    // Informational numbers are clamped, never trusted.
    object clamped = Decode(snapshotType, Wire(WithWave(Body(), Wave(pool: 5000000, cost: -50, bought: 20000, lastAgo: float.NaN))));
    object clampedWave = Get(Get(clamped, "Body"), "Wave");
    if ((int)clampedWave.GetType().GetProperty("Pool")!.GetValue(clampedWave)! != 1000000 || (int)clampedWave.GetType().GetProperty("Cost")!.GetValue(clampedWave)! != -1 ||
        (int)clampedWave.GetType().GetProperty("Bought")!.GetValue(clampedWave)! != 10000 || (float)clampedWave.GetType().GetProperty("LastBoughtAgo")!.GetValue(clampedWave)! != -1f)
        throw new InvalidOperationException("Living Front wave numbers were not clamped");
    // A snapshot nobody has measured: every unknown encoding comes back as the same sentinel.
    object unknownBody = New("TheaterWarSnapshot");
    Set(unknownBody, "Faction", "Coalition");
    Set(unknownBody, "Fronts", Arr("TheaterFrontView", Front(1)));
    object unobserved = ((Array)Get(unknownBody, "Fronts")).GetValue(0)!;
    SetAll(unobserved, "Observed", false, "ObservedAgeSeconds", -1f, "NearHostile", (byte)255, "Pressure", 0f, "Trend", 0f);
    object bare = Operation(0);
    Set(bare, "Forces", New("TheaterForcesView"));
    Set(bare, "Progress", -1f);
    Set(unknownBody, "Operation", bare);
    object unknownBack = Get(Decode(snapshotType, Wire(unknownBody)), "Body");
    Same(unknownBody, unknownBack, "Unknown");
    object unknownForces = Get(Get(unknownBack, "Operation"), "Forces");
    object unknownCensus = Get(unknownBack, "Census");
    if ((byte)Get(unknownForces, "Groups") != 255 || (ushort)Get(unknownForces, "EtaSeconds") != 65535 || (float)Get(unknownForces, "NearestToFixMeters") != -1f ||
        (bool)Get(unknownCensus, "Known") || (short)Get(unknownCensus, "AirFriendly") != -1 || (float)Get(((Array)Get(unknownBack, "Fronts")).GetValue(0)!, "ObservedAgeSeconds") != -1f ||
        (float)Get(unknownBack, "StaffPickSeconds") != -1f || Convert.ToInt32(Get(unknownBack, "GroundReason")) != 255 || (byte)Get(((Array)Get(unknownBack, "Fronts")).GetValue(0)!, "NearHostile") != 255)
        throw new InvalidOperationException("Living Front unknown encodings changed");
    // No snapshot yet: the header alone.
    object empty = Decode(snapshotType, Encode(snapshotType, Header(null, has: false)));
    if ((bool)Get(empty, "HasSnapshot") || Get(empty, "Body") != null || (byte)Get(empty, "Protocol") != 3)
        throw new InvalidOperationException("Living Front empty snapshot changed");
    // Result: every command code.
    for (int code = 0; code <= 9; code++)
    {
        object result = Activator.CreateInstance(resultType)!;
        SetAll(result, "Protocol", (byte)3, "Faction", "Coalition", "RequestId", 11, "Code", (byte)code, "Detail", "VIPER 1-1", "LockSeconds", 30f, "Session", 5, "HostEpoch", 3);
        Roundtrip(resultType, result, "living result " + code);
    }
    object ResultWith(string field, object value)
    {
        object result = Activator.CreateInstance(resultType)!;
        SetAll(result, "Protocol", (byte)3, "Faction", "Coalition", "RequestId", 11, "Code", (byte)1, "Detail", "VIPER 1-1", "LockSeconds", 30f, "Session", 5, "HostEpoch", 3);
        Set(result, field, value);
        return result;
    }
    bool ResultOk(object result) => (byte)Get(Decode(resultType, Encode(resultType, result)), "Protocol") == 3;
    foreach (object bad in new[] { ResultWith("Code", (byte)10), ResultWith("LockSeconds", float.NaN), ResultWith("LockSeconds", -1f), ResultWith("LockSeconds", 121f),
        ResultWith("RequestId", 0), ResultWith("Session", 0), ResultWith("HostEpoch", 0), ResultWith("Faction", "") })
        if (ResultOk(bad)) throw new InvalidOperationException("Living Front accepted a bad result");
    byte[] okLong = Lengthen(Encode(resultType, ResultWith("Detail", new string('D', 20))), new string('D', 20));
    if ((byte)Get(Decode(resultType, okLong), "Protocol") != 3 || ((string)Get(Decode(resultType, okLong), "Detail")).Length != 21)
        throw new InvalidOperationException("Living Front probe cannot lengthen a string");
    if ((byte)Get(Decode(resultType, Lengthen(Encode(resultType, ResultWith("Detail", new string('D', 24))), new string('D', 24))), "Protocol") != 0)
        throw new InvalidOperationException("Living Front accepted a 25-character result detail");

    // Protocol 2 and a truncated payload.
    Refused(Encode(snapshotType, Header(fullBody, protocol: 2)), "a protocol 2 payload");
    byte[] full = Encode(snapshotType, fullHeader);
    if (!Accepted(full)) throw new InvalidOperationException("Living Front probe control failed");
    Refused(full.Take(full.Length / 2).ToArray(), "a truncated payload");
    Refused(full.Take(full.Length - 1).ToArray(), "a payload one byte short");

    // Structural violations written through the object path (the writer passes these bytes as they are).
    void Bad(string what, Action<object> mutate)
    {
        object body = Body();
        mutate(body);
        Refused(Wire(body), what);
    }
    object Front0(object body) => ((Array)Get(body, "Fronts")).GetValue(0)!;
    object Proposal0(object body) => ((Array)Get(body, "Proposals")).GetValue(0)!;
    object Forces(object body) => Get(Get(body, "Operation"), "Forces");
    object Mark0(object body) => ((Array)Get(Forces(body), "Marks")).GetValue(0)!;
    void CensusSet(object body, string field, object value) { object c = Get(body, "Census"); Set(c, field, value); Set(body, "Census", c); }
    Bad("NaN pressure", b => Set(Front0(b), "Pressure", float.NaN));
    Bad("pressure 1.5", b => Set(Front0(b), "Pressure", 1.5f));
    Bad("front status 4", b => Set(Front0(b), "Status", En("TheaterFrontStatus", 4)));
    Bad("an empty front key", b => Set(Front0(b), "Key", ""));
    Bad("a non-finite front coordinate", b => Set(Front0(b), "X", float.PositiveInfinity));
    Bad("carrier 3", b => Set(Proposal0(b), "Carrier", En("TheaterCarrier", 3)));
    Bad("kind 3", b => Set(Proposal0(b), "Kind", En("TheaterOpKind", 3)));
    Bad("risk 3", b => Set(Proposal0(b), "Risk", En("TheaterRisk", 3)));
    Bad("proposal id 0", b => Set(Proposal0(b), "Id", 0));
    Bad("threat 6", b => CensusSet(b, "Threat", (byte)6));
    Bad("threat reason 7", b => CensusSet(b, "ThreatWhy", En("TheaterThreatWhy", 7)));
    Bad("a census count of 16385", b => CensusSet(b, "Radars", (short)16385));
    Bad("reason 17", b => Set(b, "Reason", En("TheaterEmptyReason", 17)));
    Bad("posture 3", b => Set(b, "Posture", En("TheaterWarPosture", 3)));
    Bad("caps 0x200", b => Set(b, "Caps", En("TheaterCaps", 0x200)));
    Bad("why 12", b => Set(Get(b, "LastResult"), "Why", En("TheaterWhy", 12)));
    Bad("outcome 0", b => Set(Get(b, "LastResult"), "Outcome", En("TheaterOutcome", 0)));
    Bad("morale 11", b => Set(Get(b, "LastResult"), "Morale", 11));
    Bad("ground reason 8", b => Set(b, "GroundReason", En("GroundReason", 8)));
    Bad("ground reason 254", b => Set(b, "GroundReason", En("GroundReason", 254)));
    Bad("groups 33", b => Set(Forces(b), "Groups", (byte)33));
    Bad("vehicles 193", b => Set(Forces(b), "Vehicles", (byte)193));
    Bad("stalled 193", b => Set(Forces(b), "Stalled", (byte)193));
    Bad("eta 3601", b => Set(Forces(b), "EtaSeconds", (ushort)3601));
    Bad("stage 6", b => Set(Mark0(b), "Stage", (byte)6));
    Bad("alive 7", b => Set(Mark0(b), "Alive", (byte)7));
    Bad("a NaN distance", b => Set(Forces(b), "NearestToFixMeters", float.NaN));
    Bad("a non-finite mark", b => Set(Mark0(b), "X", float.PositiveInfinity));
    Bad("lock 121", b => Set(Get(b, "Operation"), "LockSeconds", 121f));
    Bad("phase 7", b => Set(Get(b, "Operation"), "Phase", En("TheaterPhase", 7)));
    Bad("a wave state 3", b => Set(b, "Wave", Wave(ws: 3)));
    Bad("a wave block 9", b => Set(b, "Wave", Wave(block: 9)));
    Bad("7 wave credits", b => Set(b, "Wave", Wave(credits: 7)));
    Bad("0 credits per wave", b => Set(b, "Wave", Wave(perWave: 0)));
    Bad("10 credits per wave", b => Set(b, "Wave", Wave(perWave: 10)));
    Bad("a non-finite depot", b => Set(b, "Wave", Wave(depotX: float.NaN)));
    Bad("a wave switched off with a short pool", b => Set(b, "Wave", Wave(ws: 0, block: 8)));
    Bad("a blocked wave with no block", b => Set(b, "Wave", Wave(ws: 2, block: 0)));
    Bad("outcome seq 0", b => Set(b, "LastOutcome", Outcome(seq: 0)));
    Bad("outcome credits 7", b => Set(b, "LastOutcome", Outcome(credits: 7)));
    if (!Accepted(Wire(WithWave(Body(), Wave(ws: 0, block: 1))))) throw new InvalidOperationException("Living Front refused a wave that is switched off");

    // Structural violations that need hand-edited wire.
    byte[] Fronts1() => Wire(Body(fronts: 1, proposals: 0, operation: false, wave: false, result: false, outcome: false));
    byte[] Fronts0() => Wire(Body(fronts: 0, proposals: 0, operation: false, wave: false, result: false, outcome: false));
    if (!Accepted(Repeated(Fronts1(), Fronts0(), 8))) throw new InvalidOperationException("Living Front probe cannot repeat a front");
    Refused(Repeated(Fronts1(), Fronts0(), 9), "a 9th front");
    byte[] Props1() => Wire(Body(fronts: 0, proposals: 1, operation: false, wave: false, result: false, outcome: false));
    if (!Accepted(Repeated(Props1(), Fronts0(), 3))) throw new InvalidOperationException("Living Front probe cannot repeat a proposal");
    Refused(Repeated(Props1(), Fronts0(), 4), "a 4th proposal");
    byte[] Marks1() => Wire(Body(fronts: 0, proposals: 0, operation: true, wave: false, result: false, outcome: false, marks: 1));
    byte[] Marks0() => Wire(Body(fronts: 0, proposals: 0, operation: true, wave: false, result: false, outcome: false, marks: 0));
    if (!Accepted(Repeated(Marks1(), Marks0(), 8))) throw new InvalidOperationException("Living Front probe cannot repeat a mark");
    Refused(Repeated(Marks1(), Marks0(), 9), "a 9th mark");
    int frontX = Find(full, BitConverter.GetBytes(1234.5f));
    Refused(Patched(full, frontX + 21, 0x80), "a front with flag bit 7");
    object shortName = Body();
    Set(Front0(shortName), "Name", new string('M', 30));
    if (!Accepted(Lengthen(Wire(shortName), new string('M', 30)))) throw new InvalidOperationException("Living Front probe cannot lengthen a front name");
    Refused(Lengthen(full, new string('N', 32)), "a 33-character front name");
    byte[] withWave = Wire(Body());
    byte[] withoutWave = Wire(WithWave(Body(), null));
    Refused(Patched(withWave, FirstDiff(withoutWave, withWave), 2), "wave present 2");
    byte[] zeroDepot = Wire(WithWave(Body(), Wave(depotX: 0f)));
    Refused(Patched(zeroDepot, Find(zeroDepot, BitConverter.GetBytes(-6750f)) - 5, 2), "depot known 2");
    Refused(Patched(withWave, Find(withWave, BitConverter.GetBytes(4242.5f)) + 8, 2), "counted 2");
    Console.WriteLine("  TheaterOps protocol-3 serializers: query, active priority, clear sentinel, unknown-header rejection");
    Console.WriteLine("  Living Front protocol-3 serializers: full snapshot, every result code, unknown encodings, clamps, refusals through the object path and hand-edited wire, query, intent");
}

sealed class ProbeSignatureNames : ISignatureTypeProvider<string, object>
{
    public string GetArrayType(string element, ArrayShape shape) => element + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string element) => element + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
    public string GetGenericInstantiation(string type, ImmutableArray<string> arguments) => type + "<" + string.Join(",", arguments) + ">";
    public string GetGenericMethodParameter(object context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object context, int index) => "!" + index;
    public string GetModifiedType(string modifier, string element, bool required) => element;
    public string GetPinnedType(string element) => element;
    public string GetPointerType(string element) => element + "*";
    public string GetPrimitiveType(PrimitiveTypeCode code) => "System." + code;
    public string GetSZArrayType(string element) => element + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);
        return definition.GetDeclaringType().IsNil ? Qualify(reader.GetString(definition.Namespace), name)
            : GetTypeFromDefinition(reader, definition.GetDeclaringType(), kind) + "+" + name;
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind)
    {
        TypeReference reference = reader.GetTypeReference(handle);
        string name = reader.GetString(reference.Name);
        return reference.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, kind) + "+" + name
            : Qualify(reader.GetString(reference.Namespace), name);
    }
    public string GetTypeFromSpecification(MetadataReader reader, object context, TypeSpecificationHandle handle, byte kind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}
