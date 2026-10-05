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
    ("MountedTroops", "Fire"),
    ("DynamicMap", "Maximize"),
    ("DynamicMap", "Minimize"),
    ("DynamicMap", "CenterMinimizedMap"),
    ("DynamicMap", "MapControls"),
    ("GridLabels", "GridLabels_OnMapChanged"),
    ("GridLabels", "LateUpdate"),
    ("GridLabels", "UpdateMinorGridLabels"),
    ("GridLabels", "Maximize"),
    ("RadialMenuMain", "SetupMain"),
    ("RadialMenuMain", "OpenMenu"),
    ("RadialMenuMain", "OnDestroy"),
    ("RadialMenuAction", "AllowedOnAircraft"),
    ("RadialMenuAction", "TriggerAction"),
    ("PilotPlayerState", "FixedUpdateState"),
    ("NuclearOption.Jobs.DetectorManager", "RequestLoSCheck"),
    ("CameraCockpitState", "UpdateState"),
    ("CameraCockpitState", "LeaveState"),
    ("Gun", "SpawnBullet"),
    ("CameraOrbitState", "UpdateState")
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
    ("CombatHUD", "weaponStatus")
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

    // Third-person HUD: FlightHud.HUDCenter re-projection/levelling (ThirdPersonHudCenter) and
    // native flight-number hiding (NativeFlightNumberHider), all reflected via AccessTools.Field.
    ("FlightHud", "HUDCenter", "UnityEngine.Transform"),
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
Type nativeAimAssist = gameAssembly.GetType("ControlsFilter+AimAssist", true)!;
if (nativeAimAssist.GetField("Enabled", AllMembers)?.FieldType != typeof(bool))
    throw new MissingFieldException("ControlsFilter+AimAssist.Enabled");

// Harmony binds patch parameters by name, so a rename in a game update throws at patch
// time rather than degrading. Neither probe checked these names before.
(string Type, string Method, string[] Parameters)[] parameterNames =
{
    ("ControlsFilter", "SetFlightAssist", new[] { "enabled", "aircraft" }),
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

// The Chimera paratrooper mount registers after vanilla indexing; AfterLoad is overloaded
// (a static Action<Encyclopedia> wrapper), so pin the parameterless instance method.
if (gameAssembly.GetType("Encyclopedia", true)!.GetMethod("AfterLoad", AllMembers, null, Type.EmptyTypes, null) is not { IsStatic: false })
    throw new MissingMethodException("Encyclopedia", "AfterLoad()");
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
    "BoscaliSummer.Garrisons.StrongpointClientGuardPatch",
    "BoscaliSummer.Garrisons.DestroyCommandGuardPatch",
    "BoscaliSummer.Garrisons.ChimeraMountRegistrationPatch",
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
    "BoscaliSummer.Modules.Command.Patches.MapControlsPanelGuardPatch",
    "BoscaliSummer.Modules.Command.Patches.MapCursorPanelGuardPatch",
    "BoscaliSummer.Modules.Command.Patches.GridLabelsPatch",
    "BoscaliSummer.Modules.Support.Patches.SupportMissileDetonatePatch",
    "BoscaliSummer.Modules.Support.Patches.SupportMissileAuthorityPatch",
    "BoscaliSummer.Modules.Support.Patches.SupportMissileDescentPatch",
    "BoscaliSummer.Modules.Support.Patches.CreditRewardPatch",
    "BoscaliSummer.Modules.Support.Patches.CyberLaunchMountPatch",
    "BoscaliSummer.Modules.Support.Patches.CyberLaunchFirePatch",
    "BoscaliSummer.Modules.Support.Patches.CyberDetectScopePatch",
    "BoscaliSummer.Modules.Support.Patches.CyberShareBlockPatch",


    "BoscaliSummer.Modules.QoL.Patches.NightVisionChoicePatch",
    "BoscaliSummer.Modules.QoL.Patches.WeaponAimAssistPatch",
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
};

foreach (string patchType in patchTypes)
    if (pluginAssembly.GetType(patchType, false) == null)
        throw new TypeLoadException("Plugin patch type missing: " + patchType);

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
    ,"BoscaliSummer.Modules.Campaign.CampaignModule"
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
    ,("BoscaliSummer.Modules.Support.Networking.CreditStateMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Modules.Support.Networking.CreditStateMessage", "Balance", typeof(int))
    ,("BoscaliSummer.Modules.Support.Networking.CreditStateMessage", "FrozenSeconds", typeof(int))
    ,("BoscaliSummer.Modules.Support.Networking.CreditStateMessage", "EventFactor", typeof(float))
    ,("BoscaliSummer.Modules.Support.Networking.CreditStateMessage", "SilentFactor", typeof(float))
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
    "BoscaliSummer.Core.Contracts.IHudChannel",
    "BoscaliSummer.Core.Contracts.IHudLine",
    "BoscaliSummer.Core.Contracts.HudLayout",
    "BoscaliSummer.Modules.Hud.Presentation.HudBoard",
    "BoscaliSummer.Modules.Hud.Presentation.StatusPanel" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Modules.HighCommand.Runtime.HighCommandManager",
    "BoscaliSummer.Modules.HighCommand.Networking.HighCommandNet",
    "BoscaliSummer.Modules.HighCommand.Domain.CommandTree",
    "BoscaliSummer.Modules.HighCommand.Domain.CommandLog",
    "BoscaliSummer.Core.Contracts.IHighCommandView" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Modules.TheaterOps.Runtime.TheaterPriorityService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.TheaterLogisticsService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.TheaterOperationsService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.GroundFrontService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.LivingFrontService",
    "BoscaliSummer.Modules.TheaterOps.Runtime.NavalFrontService",
    "BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontNet",
    "BoscaliSummer.Modules.TheaterOps.Networking.TheaterOpsNet",
    "BoscaliSummer.Modules.TheaterOps.Domain.PriorityTable",
    "BoscaliSummer.Modules.TheaterOps.Domain.ReinforcementGatePolicy",
    "BoscaliSummer.Modules.TheaterOps.Domain.OffensiveTable",
    "BoscaliSummer.Modules.TheaterOps.Domain.OffensivePlan",
    "BoscaliSummer.Core.Contracts.ITheaterPriorityView",
    "BoscaliSummer.Core.Contracts.ITheaterLogisticsView",
    "BoscaliSummer.Core.Contracts.ITheaterOperationsView",
    "BoscaliSummer.Core.Contracts.ITheaterWarView",
    "BoscaliSummer.Core.Contracts.ITheaterAirStationView" })
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
    if ((byte)highCommandNet.GetField("ProtocolVersion", AllMembers)!.GetRawConstantValue()! != 4)
    throw new InvalidOperationException("High command protocol changed without updating its probe");
Type weatherNet = pluginAssembly.GetType("BoscaliSummer.Modules.Weather.Networking.WeatherNet", true)!;
// Version 7 retains the wire layout and pins the front-aligned generator/independent cloud ceilings.
if ((byte)weatherNet.GetField("ProtocolVersion", AllMembers)!.GetRawConstantValue()! != 7)
    throw new InvalidOperationException("Weather protocol changed without updating its probe");
foreach (var contract in new[] {
    ("BoscaliSummer.Modules.Support.Networking.SupportRequestMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "Action:System.Byte", "X:System.Single", "Y:System.Single", "Z:System.Single" }),
    ("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "Action:System.Byte", "Result:System.Byte", "CooldownSeconds:System.Single", "Radius:System.Single", "Duration:System.Single", "Contacts:System.Int32", "X:System.Single", "Y:System.Single", "Z:System.Single" }),
    ("BoscaliSummer.Modules.Support.Networking.CreditStateMessage", new[] { "Protocol:System.Byte", "Balance:System.Int32", "FrozenSeconds:System.Int32", "EventFactor:System.Single", "SilentFactor:System.Single" }),
    ("BoscaliSummer.Modules.Support.Networking.CruiseWaypointMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "X:System.Single", "Z:System.Single", "Clear:System.Boolean" }),
    ("BoscaliSummer.Modules.Support.Networking.CruiseLegsMessage", new[] { "Protocol:System.Byte", "RequestId:System.Int32", "OwnerId:System.UInt64", "Result:System.Byte", "FactionName:System.String", "LegCount:System.Byte", "X:System.Single[]", "Z:System.Single[]", "Dive:System.Boolean", "Tti:System.Single" }),
    ("BoscaliSummer.Modules.DynamicOperations.Networking.OperationsQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "OperationId:System.Int32", "Action:System.Byte" }),
    ("BoscaliSummer.Modules.DynamicOperations.Networking.OperationsSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Status:System.String", "Cards:BoscaliSummer.Core.Contracts.SecondaryObjectiveView[]" }),
    ("BoscaliSummer.Modules.Progression.Networking.ProgressionSubmit", new[] { "Protocol:System.Byte", "Perk:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Generation:System.Int32" }),
    ("BoscaliSummer.Modules.Progression.Networking.ProgressionSnapshot", new[] { "Protocol:System.Byte", "PerkMask:System.UInt32", "Score:System.Int32", "EarnedPoints:System.Byte", "Rank:System.Byte", "Result:System.Byte", "Generation:System.Int32", "Scene:System.UInt32", "Token:System.UInt32", "ScorePerPoint:System.Int32", "MaximumPoints:System.Byte", "PlaneId:System.UInt32", "EngineMap:System.Byte" }),
    ("BoscaliSummer.Modules.Progression.Networking.PlaneTuneRequest", new[] { "Protocol:System.Byte", "AircraftId:System.UInt32", "Mode:System.Byte" }),
    ("BoscaliSummer.Modules.Progression.Networking.PlaneTuneState", new[] { "Protocol:System.Byte", "AircraftId:System.UInt32", "Mode:System.Byte", "Accepted:System.Byte" }),
    ("BoscaliSummer.Modules.Squad.Networking.SquadQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Revision:System.UInt32" }),
    ("BoscaliSummer.Modules.Squad.Networking.SquadSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Event:System.UInt32", "Pilot:BoscaliSummer.Core.Contracts.PilotView", "Hunt:System.Boolean", "Bonus:System.Int32", "Origin:System.Int32", "ActiveIndex:System.Int32", "HuntId:System.Int32", "Status:System.String", "Speaker:System.String", "Chatter:System.String", "Wings:BoscaliSummer.Core.Contracts.EnemyWingView[]", "Revision:System.UInt32", "Unchanged:System.Boolean" }),
    ("BoscaliSummer.Modules.Command.Networking.FactionMoraleChanged", new[] { "Protocol:System.Byte", "FactionHash:System.Int32", "Morale:System.Single" }),
    ("BoscaliSummer.Modules.Events.Networking.ActiveEventChanged", new[] { "Protocol:System.Byte", "CatalogIndex:System.SByte", "TargetFactionHash:System.Int32", "StartedAtMissionTime:System.Single", "EndsAtMissionTime:System.Single", "EffectStrength:System.Single", "FactionResponseHashes:System.Int32[]", "FactionResponseKinds:System.Byte[]" }),
    ("BoscaliSummer.Modules.Events.Networking.EventIntent", new[] { "Protocol:System.Byte", "Token:System.UInt32", "Action:System.Byte", "CatalogIndex:System.SByte" }),
    ("BoscaliSummer.Modules.Events.Networking.EventReply", new[] { "Protocol:System.Byte", "Token:System.UInt32", "CatalogIndex:System.SByte", "Result:System.Byte", "Kind:System.Byte", "Cost:System.Int32" }),
    ("BoscaliSummer.Modules.Trenches.Networking.TrenchGeometryMessage", new[] { "Protocol:System.Byte", "LineId:System.Int32", "Stage:System.Byte", "OwnerHash:System.Int32", "Curve:UnityEngine.Vector3[]", "Threat:UnityEngine.Vector3[]", "Support:UnityEngine.Vector3[]", "Redoubt:UnityEngine.Vector3[]", "Links:UnityEngine.Vector3[][]", "Spurs:UnityEngine.Vector3[][]" }),
    ("BoscaliSummer.Modules.Trenches.Networking.TrenchStateMessage", new[] { "Protocol:System.Byte", "LineId:System.Int32", "Stage:System.Byte", "Defenders:System.Byte", "Suppressed:System.Boolean", "Overrun:System.Boolean" }),
    ("BoscaliSummer.Modules.Trenches.Networking.TrenchLineRemovedMessage", new[] { "Protocol:System.Byte", "LineId:System.Int32" }),
    ("BoscaliSummer.Modules.Session.Networking.SessionHello", new[] { "Protocol:System.Byte", "Version:System.String" }),
    ("BoscaliSummer.Modules.Session.Networking.HostSettingsMessage", new[] { "Protocol:System.Byte", "Version:System.String", "Flags:System.Byte", "Keys:System.String[]", "Values:System.String[]" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.TheaterPriorityQuery", new[] { "Protocol:System.Byte" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontQuery", new[] { "Protocol:System.Byte", "Session:System.Int32" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontIntent", new[] { "Protocol:System.Byte", "Kind:System.Byte", "Posture:System.Byte", "Id:System.Int32", "Revision:System.Int32", "RequestId:System.Int32", "Session:System.Int32", "HostEpoch:System.Int32" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontSnapshot", new[] { "Protocol:System.Byte", "Posture:System.Byte", "HasSnapshot:System.Boolean", "Session:System.Int32", "HostEpoch:System.Int32", "Faction:System.String", "Fronts:BoscaliSummer.Core.Contracts.TheaterFrontView[]", "Proposals:BoscaliSummer.Core.Contracts.TheaterProposalView[]", "Operation:BoscaliSummer.Core.Contracts.TheaterLiveOperationView", "Log:System.String[]" }),
    ("BoscaliSummer.Modules.Comms.Networking.CommsUpMessage", new[] { "Protocol:System.Byte", "Op:System.Byte", "Channel:System.Byte", "Kind:System.Byte", "Style:System.Byte", "Size:System.Byte", "Target:System.UInt32", "Points:System.Int32[]", "Text:System.String", "Items:System.String[]" }),
    ("BoscaliSummer.Modules.Comms.Networking.CommsDownMessage", new[] { "Protocol:System.Byte", "Event:System.Byte", "Id:System.UInt32", "Author:System.UInt64", "AuthorName:System.String", "Faction:System.Int32", "Channel:System.Byte", "Kind:System.Byte", "Style:System.Byte", "Size:System.Byte", "Flags:System.Byte", "Ttl:System.Single", "Points:System.Int32[]", "Text:System.String", "Items:System.String[]", "Values:System.Int32[]", "Players:System.UInt64[]", "Ids:System.UInt32[]" }),
    ("BoscaliSummer.Modules.Weather.Networking.WeatherSyncMessage", new[] { "Protocol:System.Byte", "TargetConditions:System.Single", "TargetCloudHeight:System.Single", "TargetWindX:System.Single", "TargetWindZ:System.Single", "TargetTurbulence:System.Single", "TransitionProgress:System.Single", "MissionTimeSeconds:System.UInt32", "ForcedRain:System.Single", "FieldSeed:System.UInt32", "FieldEpoch:System.Single", "FieldStartRegime:System.Byte", "FieldDynamic:System.Boolean", "FieldManual:System.Boolean", "HoldMinutes:System.Single", "BlendMinutes:System.Single", "FieldSets:System.Byte", "FieldSalt:System.Byte", "FieldHasAnchor:System.Boolean", "FieldAnchorX:System.Single", "FieldAnchorZ:System.Single", "FieldFrontTurn:System.Byte" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.TheaterPriorityState", new[] { "Protocol:System.Byte", "Active:System.Byte", "Faction:System.String", "Key:System.String", "Label:System.String", "X:System.Single", "Y:System.Single", "Z:System.Single" }),
    ("BoscaliSummer.Modules.TheaterOps.Networking.TheaterOperationState", new[] { "Protocol:System.Byte", "Count:System.Byte", "Index:System.Byte", "Faction:System.String", "Phase:System.Byte", "Outcome:System.Byte", "Name:System.String", "Target:System.String", "Progress:System.Single", "Budget:System.Single", "Committed:System.Single", "Spent:System.Single", "Duration:System.Single", "Countdown:System.Single", "WavesPlanned:System.Int32", "WavesLaunched:System.Int32", "Holder:System.String" }) })
{
    Type type = pluginAssembly.GetType(contract.Item1, true)!;
    string[] actual = type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.MetadataToken)
        .Select(field => field.Name + ":" + field.FieldType.FullName).ToArray();
    if (!actual.SequenceEqual(contract.Item2)) throw new InvalidOperationException(contract.Item1 + " wire fields changed: " + string.Join("|", actual));
}
ProbeOperationSerialization(pluginAssembly, mirageAssembly);
ProbeSquadSerialization(pluginAssembly, mirageAssembly);
ProbeSupportSerialization(pluginAssembly, mirageAssembly);
ProbeEventsSerialization(pluginAssembly, mirageAssembly);
ProbeFactionMoraleSerialization(pluginAssembly, mirageAssembly);
ProbeTrenchSerialization(pluginAssembly, mirageAssembly);
ProbeSessionSerialization(pluginAssembly, mirageAssembly);
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
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 3)
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
    object Card(float progress, float remaining = 300f, int money = 17, int xp = 29) =>
        Activator.CreateInstance(cardType, 123, new string('T', 150), "description", "target", "status", "reward", progress, remaining, money, xp, false,
            false, true, true, 12500f, -6750f, 1500f, "<b>" + new string('P', 50))!;
    object Snapshot(int count, object card)
    {
        object snapshot = Activator.CreateInstance(snapshotType)!;
        snapshotType.GetField("Protocol")!.SetValue(snapshot, (byte)3);
        snapshotType.GetField("Scene")!.SetValue(snapshot, 345u);
        snapshotType.GetField("Token")!.SetValue(snapshot, 678u);
        snapshotType.GetField("Status")!.SetValue(snapshot, new string('S', 200));
        Array cards = Array.CreateInstance(cardType, count);
        for (int i = 0; i < count; i++) cards.SetValue(card, i);
        snapshotType.GetField("Cards")!.SetValue(snapshot, cards);
        return snapshot;
    }
    object query = Activator.CreateInstance(queryType)!;
    queryType.GetField("Protocol")!.SetValue(query, (byte)3);
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
        false, true, true, 12500f, -6750f, 1500f, "b" + new string('P', 31))!;
    foreach (PropertyInfo property in cardType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(property.GetValue(expectedCard), property.GetValue(cardResult))) throw new InvalidOperationException("Operations card roundtrip changed " + property.Name);
    Reject(Encode(snapshotType, Snapshot(1, Card(float.NaN))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, float.PositiveInfinity))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, 300f, -1))));
    Reject(Encode(snapshotType, Snapshot(1, Card(0.5f, 300f, 17, 10001))));
    object excessiveCount = Encode(snapshotType, Snapshot(0, Card(0f)));
    int bitPosition = (int)writerType.GetProperty("BitPosition")!.GetValue(excessiveCount)!;
    writerType.GetField("_bitPosition", flags)!.SetValue(excessiveCount, bitPosition - 8);
    writerType.GetMethod("WriteByte")!.Invoke(excessiveCount, new object[] { (byte)4 });
    Reject(excessiveCount);
    Console.WriteLine("  Dynamic operations serializers: query/card roundtrip, 3-card/128-char/32-pilot bounds, non-finite/range/count rejection");
}

static void ProbeSquadSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Squad.Networking.SquadNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 3)
        throw new InvalidOperationException("Squad protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type progression = plugin.GetType("BoscaliSummer.Modules.Progression.Networking.ProgressionNet", true)!;
    if ((byte)progression.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 5)
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
    Set(progress, "Protocol", (byte)5); Set(progress, "Generation", 10001); Set(progress, "PerkMask", 123u);
    Set(progress, "Score", 70000); Set(progress, "Scene", 456u); Set(progress, "Token", 789u);
    Set(progress, "ScorePerPoint", 10000); Set(progress, "MaximumPoints", (byte)20);
    object progressResult = Decode(progressType, Encode(progressType, progress));
    if ((int)Get(progressResult, "Generation") != 10001 || (uint)Get(progressResult, "PerkMask") != 123u ||
        (int)Get(progressResult, "Score") != 70000 || (uint)Get(progressResult, "Scene") != 456u || (uint)Get(progressResult, "Token") != 789u ||
        (int)Get(progressResult, "ScorePerPoint") != 10000 || (byte)Get(progressResult, "MaximumPoints") != 20)
        throw new InvalidOperationException("Progression generation roundtrip failed");
    Type submitType = plugin.GetType("BoscaliSummer.Modules.Progression.Networking.ProgressionSubmit", true)!;
    object submit = Activator.CreateInstance(submitType)!;
    Set(submit, "Protocol", (byte)5); Set(submit, "Perk", (byte)4); Set(submit, "Scene", 456u); Set(submit, "Token", 789u); Set(submit, "Generation", 10001);
    object submitResult = Decode(submitType, Encode(submitType, submit));
    foreach (FieldInfo field in submitType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(submit), field.GetValue(submitResult))) throw new InvalidOperationException("Progression intent roundtrip changed " + field.Name);
    Set(progress, "Protocol", (byte)4);
    object oldProgress = Decode(progressType, Encode(progressType, progress));
    if ((byte)Get(oldProgress, "Protocol") != 4 || (int)Get(oldProgress, "Generation") != 0)
        throw new InvalidOperationException("Progression reader must keep only the header of a foreign protocol");
    Console.WriteLine("  Squad protocol-3 serializers: pilot/wing/query/revision roundtrip, header-only unchanged reply, 8-wing/192-char bounds, invalid strength/tier/return/header/count read as protocol 0; progression v5 foreign-header drop");
}

static void ProbeSupportSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Support.Networking.SupportNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 33)
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
    Set(request, "Protocol", (byte)33); Set(request, "RequestId", 7123); Set(request, "Action", (byte)6);
    Set(request, "X", 1234.5f); Set(request, "Y", 2345.5f); Set(request, "Z", -3456.5f);
    Roundtrip(requestType, request, "request");

    Type resultType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.SupportResultMessage", true)!;
    object resultMessage = Activator.CreateInstance(resultType)!;
    Set(resultMessage, "Protocol", (byte)33); Set(resultMessage, "RequestId", 7123);
    Set(resultMessage, "Action", (byte)4); Set(resultMessage, "Result", (byte)1);
    Set(resultMessage, "CooldownSeconds", 30f); Set(resultMessage, "Radius", 6000f);
    Set(resultMessage, "Duration", 10f); Set(resultMessage, "Contacts", 48);
    Set(resultMessage, "X", 1234.5f); Set(resultMessage, "Y", 2345.5f); Set(resultMessage, "Z", -3456.5f);
    object resultBack = Roundtrip(resultType, resultMessage, "result");
    if ((int)Get(resultBack, "Contacts") != 48)
        throw new InvalidOperationException("Support result lost the sweep contact count");

    Type creditType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.CreditStateMessage", true)!;
    object credit = Activator.CreateInstance(creditType)!;
    Set(credit, "Protocol", (byte)33); Set(credit, "Balance", 420);
    Set(credit, "FrozenSeconds", 12); Set(credit, "EventFactor", 1.5f); Set(credit, "SilentFactor", 0.8f);
    Roundtrip(creditType, credit, "owner credit state");
    // Request parsing preserves its header; the host rejects it in Evaluate after decoding.
    Set(request, "Protocol", (byte)27);
    Roundtrip(requestType, request, "retired request header");
    foreach (Type type in new[] { resultType, creditType })
    {
        object rejected = Decode(type, new byte[] { 27 });
        if ((byte)Get(rejected, "Protocol") != 27 ||
            type.GetFields().Any(field => field.Name != "Protocol" && !Equals(field.GetValue(rejected), Activator.CreateInstance(field.FieldType))))
            throw new InvalidOperationException("Support did not reject the retired protocol for " + type.Name);
    }

    Type waypointType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.CruiseWaypointMessage", true)!;
    object waypoint = Activator.CreateInstance(waypointType)!;
    Set(waypoint, "Protocol", (byte)33); Set(waypoint, "RequestId", 913);
    Set(waypoint, "X", 1234.5f); Set(waypoint, "Z", -3456.5f); Set(waypoint, "Clear", true);
    object waypointBack = Roundtrip(waypointType, waypoint, "cruise waypoint");
    if ((int)Get(waypointBack, "RequestId") != 913 || !(bool)Get(waypointBack, "Clear"))
        throw new InvalidOperationException("Cruise waypoint intent roundtrip failed");
    if ((byte)Get(Decode(waypointType, new byte[] { 18 }), "Protocol") != 18)
        throw new InvalidOperationException("Cruise waypoint did not leave an old header unread");

    Type legsType = plugin.GetType("BoscaliSummer.Modules.Support.Networking.CruiseLegsMessage", true)!;
    object legs = Activator.CreateInstance(legsType)!;
    Set(legs, "Protocol", (byte)33); Set(legs, "RequestId", 913); Set(legs, "OwnerId", 0x123456789ABCDEF0UL);
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
    Set(cyberData, "Protocol", (byte)33); Set(cyberData, "Active", true); Set(cyberData, "Seq", 7);
    Set(cyberData, "Now", 12.5f); Set(cyberData, "IntrusionCap", (byte)2); Set(cyberData, "HeldTotal", (byte)1);
    object cyberMessage = Activator.CreateInstance(cyberType)!;
    Set(cyberMessage, "Data", cyberData);
    object cyberBack = Get(Decode(cyberType, Encode(cyberType, cyberMessage)), "Data");
    if ((byte)Get(cyberBack, "Protocol") != 33 || (int)Get(cyberBack, "Seq") != 7 || !(bool)Get(cyberBack, "Active") ||
        Math.Abs((float)Get(cyberBack, "Now") - 12.5f) > 0.001f || (byte)Get(cyberBack, "IntrusionCap") != 2 || (byte)Get(cyberBack, "HeldTotal") != 1)
        throw new InvalidOperationException("CYBER state roundtrip failed");
    object cyberForeign = Get(Decode(cyberType, new byte[] { 27 }), "Data");
    if ((byte)Get(cyberForeign, "Protocol") != 27 || (bool)Get(cyberForeign, "Active"))
        throw new InvalidOperationException("CYBER state did not leave a foreign header unread");
    object cyberEmpty = Get(Decode(cyberType, new byte[0]), "Data");
    if ((byte)Get(cyberEmpty, "Protocol") != 0)
        throw new InvalidOperationException("An empty CYBER message must read inert");

    Console.WriteLine("  Support protocol-33 serializers: requests, results, owner credits, cruise waypoints/legs, the faction-only CYBER state and retired-header rejection");
}

static void ProbeEventsSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Modules.Events.Networking.EventsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 5)
        throw new InvalidOperationException("Events protocol differs from the response contract");
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
    Set(changed, "Protocol", (byte)5); Set(changed, "CatalogIndex", (sbyte)-1);
    Set(changed, "TargetFactionHash", 0);
    Set(changed, "StartedAtMissionTime", 12.5f); Set(changed, "EndsAtMissionTime", 912.5f);
    Set(changed, "EffectStrength", 1.25f);
    Set(changed, "FactionResponseHashes", Array.Empty<int>());
    Set(changed, "FactionResponseKinds", Array.Empty<byte>());
    Roundtrip(changedType, changed, "state");
    object calm = Decode(changedType, Encode(changedType, changed));
    if ((sbyte)Get(calm, "CatalogIndex") != -1 || (int)Get(calm, "TargetFactionHash") != 0)
        throw new InvalidOperationException("Events state lost its calm sentinel");

    Set(changed, "CatalogIndex", (sbyte)11); Set(changed, "TargetFactionHash", -1234567);
    Set(changed, "FactionResponseHashes", new[] { -1234567, 7654321 });
    Set(changed, "FactionResponseKinds", new byte[] { 3, 4 });
    Roundtrip(changedType, changed, "targeted state");
    if ((int)Get(Decode(changedType, Encode(changedType, changed)), "TargetFactionHash") != -1234567)
        throw new InvalidOperationException("Events state lost its target faction hash");

    Type intentType = plugin.GetType("BoscaliSummer.Modules.Events.Networking.EventIntent", true)!;
    object intent = Activator.CreateInstance(intentType)!;
    Set(intent, "Protocol", (byte)5); Set(intent, "Token", 77u);
    Set(intent, "Action", (byte)4); Set(intent, "CatalogIndex", (sbyte)7);
    Roundtrip(intentType, intent, "intent");

    Type replyType = plugin.GetType("BoscaliSummer.Modules.Events.Networking.EventReply", true)!;
    object reply = Activator.CreateInstance(replyType)!;
    Set(reply, "Protocol", (byte)5); Set(reply, "Token", 77u); Set(reply, "CatalogIndex", (sbyte)7);
    Set(reply, "Result", (byte)4); Set(reply, "Kind", (byte)1); Set(reply, "Cost", 700);
    Roundtrip(replyType, reply, "reply");

    if ((byte)Get(Decode(changedType, new byte[] { 4 }), "Protocol") != 4 ||
        (sbyte)Get(Decode(changedType, new byte[] { 4 }), "CatalogIndex") != 0)
        throw new InvalidOperationException("Events did not reject an old state header");

    Console.WriteLine("  Events protocol-5 serializers: state (target + faction responses + host strength + timestamps), decision intent, reply roundtrip, old-header rejection");
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
    if ((byte)net.GetField("InfluenceStance", flags)!.GetRawConstantValue()! != 0 ||
        (byte)net.GetField("InfluenceHold", flags)!.GetRawConstantValue()! != 1 ||
        (byte)net.GetField("InfluenceChest", flags)!.GetRawConstantValue()! != 2 ||
        (byte)net.GetField("InfluenceAxis", flags)!.GetRawConstantValue()! != 3)
        // Retired with RequestClearAxis; a zero-weight axis clears instead. Was: (byte)net.GetField("InfluenceClearAxis", flags)!.GetRawConstantValue()! != 4)
        throw new InvalidOperationException("TheaterOps influence kinds changed without updating its probe");
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

    Type operationType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.TheaterOperationState", true)!;
    object operation = Activator.CreateInstance(operationType)!;
    Set(operation, "Protocol", (byte)3); Set(operation, "Count", (byte)2); Set(operation, "Index", (byte)1);
    Set(operation, "Faction", "Coalition");
    Set(operation, "Phase", (byte)4); Set(operation, "Outcome", (byte)0);
    Set(operation, "Name", "STEEL RAIN"); Set(operation, "Target", "Northern Corridor");
    Set(operation, "Progress", 0.5f); Set(operation, "Budget", 35f); Set(operation, "Committed", 100f);
    Set(operation, "Spent", 65f); Set(operation, "Duration", 252f);
    Set(operation, "Countdown", -1f); Set(operation, "WavesPlanned", 3); Set(operation, "WavesLaunched", 1);
    Set(operation, "Holder", "IRON HAMMER");
    Roundtrip(operationType, operation, "operation");

    // The held-at-H-hour plan carries the effort's owner; the free one carries nothing.
    object unheld = Activator.CreateInstance(operationType)!;
    Set(unheld, "Protocol", (byte)3); Set(unheld, "Count", (byte)1); Set(unheld, "Index", (byte)0);
    Set(unheld, "Faction", "Coalition");
    Set(unheld, "Phase", (byte)3); Set(unheld, "Outcome", (byte)0);
    Set(unheld, "Name", "RED TIDE"); Set(unheld, "Target", "");
    Set(unheld, "Progress", 0f); Set(unheld, "Budget", 70f); Set(unheld, "Committed", 70f);
    Set(unheld, "Spent", 0f); Set(unheld, "Duration", 0f);
    Set(unheld, "Countdown", 0f); Set(unheld, "WavesPlanned", 1); Set(unheld, "WavesLaunched", 0);
    Set(unheld, "Holder", "");
    Roundtrip(operationType, unheld, "unheld operation");

    // A board clear carries the header, the faction and the nil count.
    object operationClear = Activator.CreateInstance(operationType)!;
    Set(operationClear, "Protocol", (byte)3); Set(operationClear, "Count", (byte)0);
    Set(operationClear, "Faction", "Coalition");
    object operationClearResult = Decode(operationType, Encode(operationType, operationClear));
    if ((byte)Get(operationClearResult, "Count") != 0 ||
        (string)Get(operationClearResult, "Faction") != "Coalition" ||
        Get(operationClearResult, "Name") != null || Get(operationClearResult, "Target") != null)
        throw new InvalidOperationException("TheaterOps operation clear lost its faction or sentinel");

    if ((byte)Get(Decode(operationType, new byte[] { 1 }), "Protocol") != 1 ||
        (byte)Get(Decode(operationType, new byte[] { 1 }), "Count") != 0)
        throw new InvalidOperationException("TheaterOps did not reject an old operation header");

    Type intentType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.TheaterInfluenceIntent", true)!;
    object intent = Activator.CreateInstance(intentType)!;
    Set(intent, "Protocol", (byte)3); Set(intent, "Kind", (byte)3);
    Set(intent, "Value", 1f); Set(intent, "Value2", 0f); Set(intent, "Key", "obj-north");
    Roundtrip(intentType, intent, "influence intent");

    Type directorType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.TheaterDirectorState", true)!;
    object director = Activator.CreateInstance(directorType)!;
    Set(director, "Protocol", (byte)3); Set(director, "Faction", "Coalition");
    Set(director, "Stance", 0.7f); Set(director, "Hold", (byte)0);
    Set(director, "MaxEscrow", 15f); Set(director, "Reserve", 3f); Set(director, "Setter", "VIPER-1");
    Set(director, "AxisKey0", "obj-north"); Set(director, "AxisWeight0", 1f);
    Set(director, "AxisKey1", "obj-south"); Set(director, "AxisWeight1", -0.5f);
    Set(director, "AxisKey2", ""); Set(director, "AxisWeight2", 0f);
    Set(director, "AxisKey3", ""); Set(director, "AxisWeight3", 0f);
    Set(director, "Posture", (byte)1); Set(director, "EffortDefense", (byte)0);
    Set(director, "DefenseLabel", ""); Set(director, "ActivePlans", (byte)2);
    Roundtrip(directorType, director, "director state");

    Type directorLogType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.TheaterDirectorLog", true)!;
    object directorLog = Activator.CreateInstance(directorLogType)!;
    Set(directorLog, "Protocol", (byte)3); Set(directorLog, "Faction", "Coalition");
    Set(directorLog, "Line0", "OPENING NORTHERN CORRIDOR: 2 WAVES");
    Set(directorLog, "Line1", "EFFORT AT OBJ-NORTH");
    Set(directorLog, "Line2", ""); Set(directorLog, "Line3", "");
    Set(directorLog, "Line4", ""); Set(directorLog, "Line5", "");
    Set(directorLog, "Line6", ""); Set(directorLog, "Line7", "");
    Roundtrip(directorLogType, directorLog, "director log");

    if ((byte)Get(Decode(intentType, new byte[] { 2 }), "Protocol") != 2 ||
        (byte)Get(Decode(intentType, new byte[] { 2 }), "Kind") != 0)
        throw new InvalidOperationException("TheaterOps did not reject an old intent header");

    Type livingNet = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontNet", true)!;
    if ((byte)livingNet.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 2)
        throw new InvalidOperationException("Living Front protocol changed without updating its probe");
    livingNet.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type livingQueryType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontQuery", true)!;
    object livingQuery = Activator.CreateInstance(livingQueryType)!;
    Set(livingQuery, "Protocol", (byte)2); Set(livingQuery, "Session", 5);
    Roundtrip(livingQueryType, livingQuery, "living query");
    Type livingIntentType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontIntent", true)!;
    object livingIntent = Activator.CreateInstance(livingIntentType)!;
    Set(livingIntent, "Protocol", (byte)2); Set(livingIntent, "RequestId", 11); Set(livingIntent, "Session", 5); Set(livingIntent, "HostEpoch", 3); Set(livingIntent, "Kind", (byte)0);
    Set(livingIntent, "Posture", (byte)0); Set(livingIntent, "Id", 7);
    Set(livingIntent, "Revision", 4);
    Roundtrip(livingIntentType, livingIntent, "living intent");
    Type frontType = plugin.GetType("BoscaliSummer.Core.Contracts.TheaterFrontView", true)!;
    object front = Activator.CreateInstance(frontType, new object[]
        { "sector-1", "NORTH FRONT", 123f, 456f, "IN CONTACT", .6f, .2f, true, 0f })!;
    Array frontArray = Array.CreateInstance(frontType, 1);
    frontArray.SetValue(front, 0);
    Type livingSnapshotType = plugin.GetType("BoscaliSummer.Modules.TheaterOps.Networking.LivingFrontSnapshot", true)!;
    object livingSnapshot = Activator.CreateInstance(livingSnapshotType)!;
    Set(livingSnapshot, "Protocol", (byte)2); Set(livingSnapshot, "HasSnapshot", true); Set(livingSnapshot, "Session", 5); Set(livingSnapshot, "HostEpoch", 3); Set(livingSnapshot, "Posture", (byte)1);
    Set(livingSnapshot, "Faction", "Coalition"); Set(livingSnapshot, "Fronts", frontArray);
    Set(livingSnapshot, "Proposals", Array.CreateInstance(
        plugin.GetType("BoscaliSummer.Core.Contracts.TheaterProposalView", true)!, 0));
    Set(livingSnapshot, "Log", new[] { "CONTACT NORTH" });
    object livingDecoded = Decode(livingSnapshotType, Encode(livingSnapshotType, livingSnapshot));
    if ((string)Get(livingDecoded, "Faction") != "Coalition" ||
        ((Array)Get(livingDecoded, "Fronts")).Length != 1 ||
        ((string[])Get(livingDecoded, "Log"))[0] != "CONTACT NORTH")
        throw new InvalidOperationException("Living Front snapshot did not roundtrip");

    Console.WriteLine("  TheaterOps protocol-3 serializers: query, active priority, clear sentinel, operation board, board clear, old-header rejection, influence intent, director state, director log");
    Console.WriteLine("  Living Front protocol-2 serializers: faction snapshot, front, query, and validated intent roundtrip");
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
