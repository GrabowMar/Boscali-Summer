using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Runtime.Loader;
using System.Collections.Immutable;

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
    ("CameraStateManager", "SwitchState"),
    ("CameraStateManager", "SetFollowingUnit"),
    ("CameraOrbitState", "UpdateState"),
    ("CameraChaseState", "UpdateState"),
    ("GameplayUI", "PauseGame"),
    ("GameplayUI", "ResumeGame"),
    ("FlightHud", "EnableCanvas"),
    ("FlightHud", "Update"),
    ("HeadMountedDisplay", "Update"),
    ("CombatHUD", "LateUpdate"),
    ("DynamicMap", "EnableCanvas"),
    ("WeaponManager", "GetTargetList"),
    ("Airbase", "CaptureFaction"),
    ("Building", "OnStartClient"),
    ("Building", "OnStartServer"),
    ("BulletSim+Bullet", "TrajectoryTrace"),
    ("GroundVehicle", "UnitDisabled"),
    ("Aircraft", "UnitDisabled"),
    ("Aircraft", "WaitRemoveAircraft"),
    ("MapBuilding", "TakeDamage"),
    ("Missile", "UserCode_RpcDetonate_897349600"),
    ("Missile", "Detonate"),
    ("Missile", "OnStartClient"),
    ("MusicManager", "PlayMusic"),
    ("MusicManager", "CrossFadeMusic"),
    ("MusicManager", "QueueMusicClip"),
    ("MapSettings", "GetStartMusic"),
    ("MapSettings", "GetStrategicMusic"),
    ("MapSettings", "GetTacticalMusic"),
    ("VirtualMFD", "SetupButtons"),
    ("VirtualMFD", "PressLeftButton"),
    ("VirtualMFD", "PressRightButton"),
    ("MFDScreen", "ShowScreen"),
    ("MFDScreen", "CloseScreen"),
    ("FactionHQ", "RewardPlayer"),
    ("Aircraft", "UseFuel"),
    ("Unit", "RecordDamage"),
    ("Unit", "ReportKilled"),
    ("Pilot", "ApplyDamage"),
    ("DynamicMap", "TryGetCursorCoordinates"),
    ("Spawner", "SpawnVehicle"),
    ("Spawner", "SpawnBuilding"),
    ("Spawner", "SpawnSavedMissile"),
    ("Missile", "GetYield"),
    ("Missile", "SetAimpoint"),
    ("FactionHQ", "SetTrackingState"),
    ("FactionHQ", "GetTrackingData"),
    ("MountedTroops", "Fire"),
    ("LoadoutSelector", "AssignAircraft"),
    ("WeaponManager", "InitializeWeaponManager"),
    ("WeaponSelector", "PopulateOptions"),
    ("WeaponChecker", "GetAvailableWeaponsNonAlloc"),
    ("DynamicMap", "Maximize"),
    ("DynamicMap", "Minimize"),
    ("DynamicMap", "CenterMinimizedMap"),
    ("DynamicMap", "MapControls"),
    ("GridLabels", "GridLabels_OnMapChanged"),
    ("GridLabels", "UpdateMinorGridLabels"),
    ("GridLabels", "Maximize"),
    ("RadialMenuMain", "SetupMain"),
    ("RadialMenuMain", "OpenMenu"),
    ("RadialMenuMain", "OnDestroy"),
    ("RadialMenuAction", "AllowedOnAircraft"),
    ("RadialMenuAction", "TriggerAction"),
    ("PilotPlayerState", "FixedUpdateState")
};

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
    ("TargetCam", "cam"),
    ("Aircraft", "targetCam"),
    ("MapBuilding", "hitPoints"),
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
    ("UnitDefinition", "value"),
    ("UnitDefinition", "roleIdentity"),
    ("RoleIdentity", "antiAir"),
    ("RoleIdentity", "antiSurface"),
    ("GridLabels", "gridToolTip"),
    ("GridLabels", "gridAircraft")
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
    ("FlightHud", "canvas", "UnityEngine.Canvas"),
    ("FlightHud", "pitchCompassCenter", "UnityEngine.GameObject"),
    ("TargetCam", "cam", "UnityEngine.Camera"),
    ("TargetCam", "currentMode", "TargetCam+CamMode"),
    ("TrackingInfo", "lastSpottedTime", "System.Single"),
    ("UnitPart", "hitPoints", "System.Single"),
    ("Unit", "disabled", "System.Boolean"),
    ("CameraOrbitState", "panView", "System.Single"),
    ("CameraOrbitState", "tiltView", "System.Single"),
    ("CameraOrbitState", "viewDistAdjust", "System.Single"),
    ("CameraOrbitState", "lookAtTargetLerp", "System.Single"),
    ("CameraChaseState", "viewDistAdjust", "System.Single"),
    ("CameraChaseState", "currentPos", "CameraChaseState+ChasePos"),
    ("RadialMenuMain", "actionsMain", "RadialMenuAction[]"),
    ("RadialMenuMain", "aircraft", "Aircraft"),
    ("RadialMenuAction", "actionType", "RadialMenuAction+ActionType"),
    ("RadialMenuAction", "iconSprite", "UnityEngine.Sprite"),
    ("RadialMenuAction", "backgroundSprite", "UnityEngine.Sprite"),
    ("RadialMenuAction", "backgroundColorInactive", "UnityEngine.Color"),
    ("RadialMenuAction", "backgroundColorActive", "UnityEngine.Color")
};
foreach (var seam in cameraFields)
{
    FieldInfo field = gameAssembly.GetType(seam.Type, true)!.GetField(seam.Field, AllMembers)
        ?? throw new MissingFieldException(seam.Type, seam.Field);
    if (field.FieldType.FullName != seam.FieldType)
        throw new InvalidOperationException($"Camera seam {seam.Type}.{seam.Field} changed type");
}
Type chasePosition = gameAssembly.GetType("CameraChaseState+ChasePos", true)!;
if (Convert.ToInt32(Enum.Parse(chasePosition, "Back")) != 0)
    throw new InvalidOperationException("Native rear chase preset changed value");
foreach (string cameraState in new[] { "CameraOrbitState", "CameraChaseState" })
{
    MethodInfo update = gameAssembly.GetType(cameraState, true)!.GetMethod("UpdateState", AllMembers)!;
    var parameters = update.GetParameters();
    if (update.ReturnType != typeof(void) || parameters.Length != 1 ||
        parameters[0].ParameterType.FullName != "CameraStateManager" || parameters[0].Name != "cam")
        throw new InvalidOperationException(cameraState + ".UpdateState camera binding changed");
}

// Harmony binds patch parameters by name, so a rename in a game update throws at patch
// time rather than degrading. Neither probe checked these names before.
(string Type, string Method, string[] Parameters)[] parameterNames =
{
    ("ControlsFilter", "SetFlightAssist", new[] { "enabled", "aircraft" }),
    ("Aircraft", "UseFuel", new[] { "fuelDrawn" }),
    ("FactionHQ", "RewardPlayer", new[] { "player", "rewardAllocation", "missionType" })
    ,("Unit", "RecordDamage", new[] { "lastDamagedBy", "damageAmount" })
    ,("PilotDismounted", "Capture", new[] { "capturingUnit" })
    ,("Building", "Repair", new[] { "repairer" })
    ,("Rearmer", "ProcessRearmRequest", new[] { "unitToRearm" })
    ,("Rearmer", "RefillOtherRearmer", new[] { "toRearmer" })
    ,("PilotPlayerState", "FixedUpdateState", new[] { "pilot" })
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
    "BoscaliSummer.Fire.MapBuildingRuinPatch",
    "BoscaliSummer.Fire.AircraftWreckPersistencePatch",
    "BoscaliSummer.Garrisons.AirbaseCapturePatch",
    "BoscaliSummer.Garrisons.GarrisonClientVisualPatch",
    "BoscaliSummer.Garrisons.MountedTroopsFirePatch",
    "BoscaliSummer.Garrisons.ChimeraLoadoutAssignAircraftPatch",
    "BoscaliSummer.Garrisons.ChimeraWeaponManagerInitPatch",
    "BoscaliSummer.Garrisons.ChimeraWeaponSelectorPopulatePatch",
    "BoscaliSummer.Garrisons.ChimeraWeaponCheckerAvailablePatch",
    "BoscaliSummer.Features.Radio.Patches.VanillaPlayMusicPatch",
    "BoscaliSummer.Features.Radio.Patches.VanillaCrossFadeMusicPatch",
    "BoscaliSummer.Features.Radio.Patches.VanillaQueueMusicPatch",
    "BoscaliSummer.Features.Progression.Patches.AircraftFuelUsePatch",
    "BoscaliSummer.Features.Progression.Patches.RewardAllocationPatch",
    "BoscaliSummer.Features.Command.Presentation.MapUi.MfdRailPatch",
    "BoscaliSummer.Features.Command.Presentation.MapUi.MfdScreenChromePatch",
    "BoscaliSummer.Features.Command.Presentation.MapUi.MfdSinglePanelPatch",
    "BoscaliSummer.Features.Command.Patches.DynamicMapMaximizePatch",
    "BoscaliSummer.Features.Command.Patches.DynamicMapMinimizePatch",
    "BoscaliSummer.Features.Command.Patches.MapControlsPanelGuardPatch",
    "BoscaliSummer.Features.Command.Patches.MapCursorPanelGuardPatch",
    "BoscaliSummer.Features.Command.Patches.GridLabelsPatch",
    "BoscaliSummer.Features.Support.Patches.SupportMissileDetonatePatch",
    "BoscaliSummer.Features.Support.Patches.SupportMissileAuthorityPatch",
    "BoscaliSummer.Features.Support.Patches.SupportMissileDescentPatch",
    "BoscaliSummer.Features.Support.Patches.UplinkMapControlsGuardPatch",
    "BoscaliSummer.Features.Support.Patches.UplinkMapCursorGuardPatch",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonHudPatches",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonOrbitPatch",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonChasePatch",
    "BoscaliSummer.Features.Autopilot.Patches.AutopilotLandInputPatch",
    "BoscaliSummer.Features.Autopilot.Patches.FlightAssistReportPatch",
    "BoscaliSummer.Features.Autopilot.Patches.RadialMenuLifecyclePatches",
    "BoscaliSummer.Features.Autopilot.Patches.BoscaliMenuActionPatches"
    ,"BoscaliSummer.Features.Squad.Patches.SquadDamagePatch"
    ,"BoscaliSummer.Features.Squad.Patches.SquadKillPatch"
    ,"BoscaliSummer.Features.Squad.Patches.SquadPilotDeathPatch"
    ,"BoscaliSummer.Features.DynamicOperations.Runtime.OperationJamPatch"
    ,"BoscaliSummer.Features.DynamicOperations.Runtime.OperationRescuePatch"
    ,"BoscaliSummer.Features.DynamicOperations.Runtime.OperationRepairPatch"
    ,"BoscaliSummer.Features.DynamicOperations.Runtime.OperationSupplyPatch"
    ,"BoscaliSummer.Features.DynamicOperations.Runtime.OperationSupplyTransferPatch"
    ,"BoscaliSummer.Features.HighCommand.Patches.HighCommandDamagePatch"
    ,"BoscaliSummer.Features.TheaterOps.Patches.MissionPositionAdvancePriorityPatch"
    ,"BoscaliSummer.Features.TheaterOps.Patches.MissionPositionDeliveryPriorityPatch"
    ,"BoscaliSummer.Features.Trenches.Visuals.TrenchNestClientPatch"
    ,"BoscaliSummer.Features.Trenches.Visuals.TrenchNestServerPatch"
    ,"BoscaliSummer.Features.Comms.Patches.CommsMapControlsPatch"
};

foreach (string patchType in patchTypes)
    if (pluginAssembly.GetType(patchType, false) == null)
        throw new TypeLoadException("Plugin patch type missing: " + patchType);

string[] featureTypes =
{
    "BoscaliSummer.Features.FireAndDestruction.FireAndDestructionFeature",
    "BoscaliSummer.Features.UrbanCombat.UrbanCombatFeature",
    "BoscaliSummer.Features.Radio.RadioFeature",
    "BoscaliSummer.Features.Progression.ProgressionFeature",
    "BoscaliSummer.Features.Support.SupportFeature",
    "BoscaliSummer.Features.QoL.QoLFeature",
    "BoscaliSummer.Features.Autopilot.AutopilotFeature",
    "BoscaliSummer.Features.Command.CommandFeature"
    ,"BoscaliSummer.Features.Squad.SquadFeature"
    ,"BoscaliSummer.Features.HighCommand.HighCommandFeature"
    ,"BoscaliSummer.Features.TheaterOps.TheaterOpsFeature"
    ,"BoscaliSummer.Features.Events.EventsFeature"
    ,"BoscaliSummer.Features.Campaign.CampaignFeature"
    ,"BoscaliSummer.Features.Trenches.TrenchesFeature"
    ,"BoscaliSummer.Features.Hud.HudFeature"
    ,"BoscaliSummer.Features.Comms.CommsFeature"
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

// The campaign mission is the module's whole payload: a missing or empty resource installs
// nothing, so the gate checks the stream the installer actually reads.
const string campaignMissionResource = "BoscaliSummer.Campaign.BoscaliSummerMission.json";
if (!resources.Contains(campaignMissionResource, StringComparer.Ordinal))
    throw new MissingManifestResourceException("Plugin campaign mission missing: " + campaignMissionResource);
using (Stream campaignMission = pluginAssembly.GetManifestResourceStream(campaignMissionResource)!)
{
    if (campaignMission == null || campaignMission.Length < 1024)
        throw new InvalidOperationException("Plugin campaign mission payload is empty or truncated");
}

(string Type, string Field, Type FieldType)[] messageFields =
{
    ("BoscaliSummer.Runtime.FireIgnitedMessage", "X", typeof(float)),
    ("BoscaliSummer.Runtime.FireIgnitedMessage", "Y", typeof(float)),
    ("BoscaliSummer.Runtime.FireIgnitedMessage", "Z", typeof(float)),
    ("BoscaliSummer.Runtime.FireIgnitedMessage", "RemainingLifetime", typeof(float)),
    ("BoscaliSummer.Runtime.FireIgnitedMessage", "ClusterScale", typeof(float)),
    ("BoscaliSummer.Runtime.FireIgnitedMessage", "Forest", typeof(bool)),
    ("BoscaliSummer.Runtime.RuinCreatedMessage", "X", typeof(float)),
    ("BoscaliSummer.Runtime.RuinCreatedMessage", "Y", typeof(float)),
    ("BoscaliSummer.Runtime.RuinCreatedMessage", "Z", typeof(float)),
    ("BoscaliSummer.Runtime.RuinCreatedMessage", "HalfX", typeof(float)),
    ("BoscaliSummer.Runtime.RuinCreatedMessage", "HalfZ", typeof(float)),
    ("BoscaliSummer.Runtime.RuinCreatedMessage", "AgeSeconds", typeof(float))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSubmit", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSubmit", "Perk", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "PerkMask", typeof(uint))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Score", typeof(int))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "EarnedPoints", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Rank", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Result", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Generation", typeof(int))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "ScorePerPoint", typeof(int))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "MaximumPoints", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Action", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "X", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Y", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Z", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Radius", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Duration", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "X", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Y", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Z", typeof(float))

    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Action", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Result", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "CooldownSeconds", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Contacts", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.OpsQueryMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", "Command", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", "Arg", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", "Arg2", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", "X", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", "Z", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Result", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformActive", typeof(bool))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformModules", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformOffline", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformRegime", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformSeed", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformClock", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformHold", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformEnergy", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformFuel", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformRods", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformBrownout", typeof(bool))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformPending", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformPendingCell", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformDockIn", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformRecharge", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformElapsed", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformNotice", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformNoticeCell", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "PlatformNoticeSerial", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "ForeignCount", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "ForeignRegimes", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "ForeignSeeds", typeof(int[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "ForeignClocks", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "ForeignLayouts", typeof(int[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Sigint", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Crypto", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Disrupt", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Ew", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "CyberOriginCount", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "CyberOrigins", typeof(string[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "GarrisonLevels", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Kind", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "FactionName", typeof(string))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "X", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Z", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Duration", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "Scene", typeof(uint))
,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "Token", typeof(uint))
,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Id", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "ParentId", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Tier", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Flags", typeof(byte))
,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "TraitMask", typeof(byte))
,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Seed", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "IntelAge", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Weight", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "X", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Z", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Name", typeof(string))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Rank", typeof(string))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Role", typeof(string))
,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Location", typeof(string))
,("BoscaliSummer.Features.HighCommand.Networking.CommanderLogWire", "TargetId", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderLogWire", "Tone", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderLogWire", "Text", typeof(string))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderLogWire", "Age", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Scene", typeof(uint))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Token", typeof(uint))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Status", typeof(string))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Signal", typeof(string))
,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Cohesion", typeof(float))
,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Active", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Kia", typeof(int))
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
// Support CYBER (CyberDefense): private seeker jamming for the jammer umbrella and the vehicle depot
// spawn bay field trucks leave from, read and written by FieldRefAccess.
foreach (var (type, field, fieldType) in new[] {
    ("Missile", "seeker", "MissileSeeker"),
    ("ARHSeeker", "jamAccumulation", "System.Single"),
    ("ARHSeeker", "jamTolerance", "System.Single"),
    ("SARHSeeker", "jamAccumulation", "System.Single"),
    ("SARHSeeker", "jamTolerance", "System.Single"),
    ("VehicleDepot", "spawnTransform", "UnityEngine.Transform") })
{
    FieldInfo? seam = gameAssembly.GetType(type, true)!.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
    if (seam == null || seam.FieldType.FullName != fieldType)
        throw new MissingFieldException(type, field + ": " + fieldType);
}
foreach (var seam in operationFields)
    RequireMetadataField(operationAssembly, seam.Type, seam.Field, seam.FieldType);
if (Convert.ToInt32(Enum.Parse(gameAssembly.GetType("FactionHQ+RewardType", true)!, "None")) != 0)
    throw new InvalidOperationException("Dynamic operations reward category changed");
foreach (string type in new[] {
    "BoscaliSummer.Features.DynamicOperations.DynamicOperationsFeature",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationsManager",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationRewards",
    "BoscaliSummer.Features.DynamicOperations.Runtime.ContractMarker",
    "BoscaliSummer.Features.DynamicOperations.Runtime.ContractHud",
    "BoscaliSummer.Features.DynamicOperations.Runtime.ContractMapTag",
    "BoscaliSummer.Features.DynamicOperations.Runtime.ContractMapHud",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationZoneHud",
    "BoscaliSummer.Features.DynamicOperations.Networking.OperationsNet" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Framework.Contracts.IHudBoard",
    "BoscaliSummer.Framework.Contracts.IHudChannel",
    "BoscaliSummer.Framework.Contracts.IHudLine",
    "BoscaliSummer.Framework.Contracts.HudLayout",
    "BoscaliSummer.Features.Hud.Presentation.HudBoard" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Features.HighCommand.Runtime.HighCommandManager",
    "BoscaliSummer.Features.HighCommand.Networking.HighCommandNet",
    "BoscaliSummer.Features.HighCommand.Domain.CommandTree",
    "BoscaliSummer.Features.HighCommand.Domain.CommandLog",
    "BoscaliSummer.Framework.Contracts.IHighCommandView" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Features.TheaterOps.Runtime.TheaterPriorityService",
    "BoscaliSummer.Features.TheaterOps.Runtime.TheaterLogisticsService",
    "BoscaliSummer.Features.TheaterOps.Runtime.TheaterEffortMarker",
    "BoscaliSummer.Features.TheaterOps.Networking.TheaterOpsNet",
    "BoscaliSummer.Features.TheaterOps.Domain.PriorityTable",
    "BoscaliSummer.Features.TheaterOps.Domain.ReinforcementGatePolicy",
    "BoscaliSummer.Framework.Contracts.ITheaterPriorityView",
    "BoscaliSummer.Framework.Contracts.ITheaterLogisticsView" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
Type highCommandSnapshot = pluginAssembly.GetType("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", true)!;
Type commanderWire = pluginAssembly.GetType("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", true)!;
Type commanderLog = pluginAssembly.GetType("BoscaliSummer.Features.HighCommand.Networking.CommanderLogWire", true)!;
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
Type highCommandNet = pluginAssembly.GetType("BoscaliSummer.Features.HighCommand.Networking.HighCommandNet", true)!;
    if ((byte)highCommandNet.GetField("ProtocolVersion", AllMembers)!.GetRawConstantValue()! != 4)
    throw new InvalidOperationException("High command protocol changed without updating its probe");
foreach (var contract in new[] {
    ("BoscaliSummer.Features.DynamicOperations.Networking.OperationsQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "OperationId:System.Int32", "Action:System.Byte" }),
    ("BoscaliSummer.Features.DynamicOperations.Networking.OperationsSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Status:System.String", "Cards:BoscaliSummer.Framework.Contracts.SecondaryObjectiveView[]" }),
    ("BoscaliSummer.Features.Progression.Networking.ProgressionSubmit", new[] { "Protocol:System.Byte", "Perk:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Generation:System.Int32" }),
    ("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", new[] { "Protocol:System.Byte", "PerkMask:System.UInt32", "Score:System.Int32", "EarnedPoints:System.Byte", "Rank:System.Byte", "Result:System.Byte", "Generation:System.Int32", "Scene:System.UInt32", "Token:System.UInt32", "ScorePerPoint:System.Int32", "MaximumPoints:System.Byte" }),
    ("BoscaliSummer.Features.Squad.Networking.SquadQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32" }),
    ("BoscaliSummer.Features.Squad.Networking.SquadSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Event:System.UInt32", "Pilot:BoscaliSummer.Framework.Contracts.PilotView", "Hunt:System.Boolean", "Bonus:System.Int32", "Origin:System.Int32", "ActiveIndex:System.Int32", "HuntId:System.Int32", "Status:System.String", "Speaker:System.String", "Chatter:System.String", "Wings:BoscaliSummer.Framework.Contracts.EnemyWingView[]" }),
    ("BoscaliSummer.Features.Events.Networking.ActiveEventChanged", new[] { "Protocol:System.Byte", "CatalogIndex:System.SByte", "TargetFactionHash:System.Int32", "StartedAtMissionTime:System.Single", "EndsAtMissionTime:System.Single" }),
    ("BoscaliSummer.Features.Events.Networking.EventIntent", new[] { "Protocol:System.Byte", "Token:System.UInt32", "Action:System.Byte", "CatalogIndex:System.SByte" }),
    ("BoscaliSummer.Features.Events.Networking.EventReply", new[] { "Protocol:System.Byte", "Token:System.UInt32", "CatalogIndex:System.SByte", "Result:System.Byte", "Kind:System.Byte", "Cost:System.Int32" }),
    ("BoscaliSummer.Features.TheaterOps.Networking.TheaterPriorityQuery", new[] { "Protocol:System.Byte" }),
    ("BoscaliSummer.Features.Comms.Networking.CommsUpMessage", new[] { "Protocol:System.Byte", "Op:System.Byte", "Channel:System.Byte", "Kind:System.Byte", "Style:System.Byte", "Size:System.Byte", "Target:System.UInt32", "Points:System.Int32[]", "Text:System.String", "Items:System.String[]" }),
    ("BoscaliSummer.Features.Comms.Networking.CommsDownMessage", new[] { "Protocol:System.Byte", "Event:System.Byte", "Id:System.UInt32", "Author:System.UInt64", "AuthorName:System.String", "Faction:System.Int32", "Channel:System.Byte", "Kind:System.Byte", "Style:System.Byte", "Size:System.Byte", "Flags:System.Byte", "Ttl:System.Single", "Points:System.Int32[]", "Text:System.String", "Items:System.String[]", "Values:System.Int32[]", "Players:System.UInt64[]", "Ids:System.UInt32[]" }),
    ("BoscaliSummer.Features.TheaterOps.Networking.TheaterPriorityState", new[] { "Protocol:System.Byte", "Active:System.Byte", "Faction:System.String", "Key:System.String", "Label:System.String", "X:System.Single", "Y:System.Single", "Z:System.Single" }) })
{
    Type type = pluginAssembly.GetType(contract.Item1, true)!;
    string[] actual = type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.MetadataToken)
        .Select(field => field.Name + ":" + field.FieldType.FullName).ToArray();
    if (!actual.SequenceEqual(contract.Item2)) throw new InvalidOperationException(contract.Item1 + " wire fields changed");
}
ProbeOperationSerialization(pluginAssembly, mirageAssembly);
ProbeSquadSerialization(pluginAssembly, mirageAssembly);
ProbeSupportSerialization(pluginAssembly, mirageAssembly);
ProbeEventsSerialization(pluginAssembly, mirageAssembly);
ProbeTheaterOpsSerialization(pluginAssembly, mirageAssembly);
CustomAttributeData dependency = pluginAssembly.GetType("BoscaliSummer.Plugin", true)!.CustomAttributes
    .FirstOrDefault(attribute => attribute.AttributeType.FullName == "BepInEx.BepInDependency" &&
        attribute.ConstructorArguments.Count == 2 &&
        Equals(attribute.ConstructorArguments[0].Value, "com.marci.wingcommand"));
if (dependency == null || !Equals(dependency.ConstructorArguments[1].Value, "0.9.2.6"))
    throw new InvalidOperationException("Wing Command minimum hard dependency must be 0.9.2.6");
Console.WriteLine($"Patch target probe: game methods/fields, Harmony parameters, {patchTypes.Length} patch classes, {featureTypes.Length + 1} features, radio assets, campaign mission payload, wire contracts, dynamic operation reward/road signatures, and Mirage seams resolved.");
return 0;

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
    Type net = plugin.GetType("BoscaliSummer.Features.DynamicOperations.Networking.OperationsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 2)
        throw new InvalidOperationException("Operations protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    Type queryType = plugin.GetType("BoscaliSummer.Features.DynamicOperations.Networking.OperationsQuery", true)!;
    Type snapshotType = plugin.GetType("BoscaliSummer.Features.DynamicOperations.Networking.OperationsSnapshot", true)!;
    Type cardType = plugin.GetType("BoscaliSummer.Framework.Contracts.SecondaryObjectiveView", true)!;

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
            false, true, true, 12500f, -6750f, 1500f)!;
    object Snapshot(int count, object card)
    {
        object snapshot = Activator.CreateInstance(snapshotType)!;
        snapshotType.GetField("Protocol")!.SetValue(snapshot, (byte)2);
        snapshotType.GetField("Scene")!.SetValue(snapshot, 345u);
        snapshotType.GetField("Token")!.SetValue(snapshot, 678u);
        snapshotType.GetField("Status")!.SetValue(snapshot, new string('S', 200));
        Array cards = Array.CreateInstance(cardType, count);
        for (int i = 0; i < count; i++) cards.SetValue(card, i);
        snapshotType.GetField("Cards")!.SetValue(snapshot, cards);
        return snapshot;
    }
    object query = Activator.CreateInstance(queryType)!;
    queryType.GetField("Protocol")!.SetValue(query, (byte)2);
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
        false, true, true, 12500f, -6750f, 1500f)!;
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
    Console.WriteLine("  Dynamic operations serializers: query/card roundtrip, 3-card/128-char bounds, non-finite/range/count rejection");
}

static void ProbeSquadSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Features.Squad.Networking.SquadNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 2)
        throw new InvalidOperationException("Squad protocol changed without updating its probe");
    net.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type progression = plugin.GetType("BoscaliSummer.Features.Progression.Networking.ProgressionNet", true)!;
    if ((byte)progression.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 4)
        throw new InvalidOperationException("Progression protocol changed without updating its probe");
    progression.GetMethod("InstallSerializers", flags)!.Invoke(null, null);
    Type writerType = mirage.GetType("Mirage.Serialization.NetworkWriter", true)!;
    Type readerType = mirage.GetType("Mirage.Serialization.NetworkReader", true)!;
    Type queryType = plugin.GetType("BoscaliSummer.Features.Squad.Networking.SquadQuery", true)!;
    Type snapshotType = plugin.GetType("BoscaliSummer.Features.Squad.Networking.SquadSnapshot", true)!;
    Type pilotType = plugin.GetType("BoscaliSummer.Framework.Contracts.PilotView", true)!;
    Type wingType = plugin.GetType("BoscaliSummer.Framework.Contracts.EnemyWingView", true)!;
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
        Set(snapshot, "Protocol", (byte)2); Set(snapshot, "Scene", 345u); Set(snapshot, "Token", 678u); Set(snapshot, "Event", uint.MaxValue);
        Set(snapshot, "Pilot", Activator.CreateInstance(pilotType, "Pilot Name", "CALDER", "Alive", true, 4, 5, "background")!);
        Set(snapshot, "Hunt", true); Set(snapshot, "Bonus", 20); Set(snapshot, "Origin", 1000); Set(snapshot, "ActiveIndex", count == 0 ? -1 : 0); Set(snapshot, "HuntId", 41);
        Set(snapshot, "Status", new string('S', 240)); Set(snapshot, "Speaker", "Cinder"); Set(snapshot, "Chatter", "Contact.");
        Array wings = Array.CreateInstance(wingType, count);
        for (int i = 0; i < count; i++) wings.SetValue(wing, i);
        Set(snapshot, "Wings", wings);
        return snapshot;
    }
    void Reject(object writer)
    {
        try { Decode(snapshotType, writer); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return; }
        throw new InvalidOperationException("Malformed Squad snapshot was accepted");
    }
    object query = Activator.CreateInstance(queryType)!;
    Set(query, "Protocol", (byte)2); Set(query, "Scene", uint.MaxValue); Set(query, "Token", 987654u);
    object queryResult = Decode(queryType, Encode(queryType, query));
    foreach (FieldInfo field in queryType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(query), field.GetValue(queryResult))) throw new InvalidOperationException("Squad query roundtrip changed " + field.Name);
    object decoded = Decode(snapshotType, Encode(snapshotType, Snapshot(9, Wing())));
    Array output = (Array)Get(decoded, "Wings");
    if (output.Length != 8 || (string)Get(decoded, "Status") != new string('S', 192) ||
        (uint)Get(decoded, "Scene") != 345u || (uint)Get(decoded, "Token") != 678u || (uint)Get(decoded, "Event") != uint.MaxValue ||
        (int)Get(decoded, "Bonus") != 20 || (int)Get(decoded, "Origin") != 1000 || (int)Get(decoded, "ActiveIndex") != 0 || (int)Get(decoded, "HuntId") != 41 || !(bool)Get(decoded, "Hunt"))
        throw new InvalidOperationException("Squad snapshot bounds/header roundtrip failed");
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
    Reject(Encode(snapshotType, Snapshot(1, Wing(returns: 3))));
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
    Type progressType = plugin.GetType("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", true)!;
    object progress = Activator.CreateInstance(progressType)!;
    Set(progress, "Protocol", (byte)4); Set(progress, "Generation", 10001); Set(progress, "PerkMask", 123u);
    Set(progress, "Score", 70000); Set(progress, "Scene", 456u); Set(progress, "Token", 789u);
    Set(progress, "ScorePerPoint", 10000); Set(progress, "MaximumPoints", (byte)20);
    object progressResult = Decode(progressType, Encode(progressType, progress));
    if ((int)Get(progressResult, "Generation") != 10001 || (uint)Get(progressResult, "PerkMask") != 123u ||
        (int)Get(progressResult, "Score") != 70000 || (uint)Get(progressResult, "Scene") != 456u || (uint)Get(progressResult, "Token") != 789u ||
        (int)Get(progressResult, "ScorePerPoint") != 10000 || (byte)Get(progressResult, "MaximumPoints") != 20)
        throw new InvalidOperationException("Progression generation roundtrip failed");
    Type submitType = plugin.GetType("BoscaliSummer.Features.Progression.Networking.ProgressionSubmit", true)!;
    object submit = Activator.CreateInstance(submitType)!;
    Set(submit, "Protocol", (byte)4); Set(submit, "Perk", (byte)4); Set(submit, "Scene", 456u); Set(submit, "Token", 789u); Set(submit, "Generation", 10001);
    object submitResult = Decode(submitType, Encode(submitType, submit));
    foreach (FieldInfo field in submitType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(submit), field.GetValue(submitResult))) throw new InvalidOperationException("Progression intent roundtrip changed " + field.Name);
    Console.WriteLine("  Squad serializers: pilot/wing/query roundtrip, 8-wing/192-char bounds, invalid strength/tier/return/header/count rejection; progression generation v3");
}

static void ProbeSupportSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Features.Support.Networking.SupportNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 13)
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

    Type requestType = plugin.GetType("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", true)!;
    object request = Activator.CreateInstance(requestType)!;
    Set(request, "Protocol", (byte)13); Set(request, "RequestId", 7123); Set(request, "Action", (byte)6);
    Set(request, "X", 1234.5f); Set(request, "Y", 2345.5f); Set(request, "Z", -3456.5f);
    Roundtrip(requestType, request, "request");

    Type resultType = plugin.GetType("BoscaliSummer.Features.Support.Networking.SupportResultMessage", true)!;
    object resultMessage = Activator.CreateInstance(resultType)!;
    Set(resultMessage, "Protocol", (byte)13); Set(resultMessage, "RequestId", 7123);
    Set(resultMessage, "Action", (byte)4); Set(resultMessage, "Result", (byte)1);
    Set(resultMessage, "CooldownSeconds", 30f); Set(resultMessage, "Radius", 6000f);
    Set(resultMessage, "Duration", 10f); Set(resultMessage, "Contacts", 48);
    Set(resultMessage, "X", 1234.5f); Set(resultMessage, "Y", 2345.5f); Set(resultMessage, "Z", -3456.5f);
    object resultBack = Roundtrip(resultType, resultMessage, "result");
    if ((int)Get(resultBack, "Contacts") != 48)
        throw new InvalidOperationException("Support result lost the sweep contact count");

    Type queryType = plugin.GetType("BoscaliSummer.Features.Support.Networking.OpsQueryMessage", true)!;
    object query = Activator.CreateInstance(queryType)!;
    Set(query, "Protocol", (byte)13);
    Roundtrip(queryType, query, "ops query");

    Type commandType = plugin.GetType("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", true)!;
    object command = Activator.CreateInstance(commandType)!;
    Set(command, "Protocol", (byte)13); Set(command, "RequestId", 91); Set(command, "Command", (byte)0);
    Set(command, "Arg", (byte)14); Set(command, "Arg2", (byte)9); Set(command, "X", 1234.5f); Set(command, "Z", -3456.5f);
    Roundtrip(commandType, command, "ops module launch command");
    Set(command, "Command", (byte)2); Set(command, "Arg", (byte)7); Set(command, "Arg2", (byte)0);
    Roundtrip(commandType, command, "ops jettison command");
    // Rephase (9), orbit shift (10, band byte) and cargo resupply (11).
    Set(command, "Command", (byte)9); Set(command, "Arg", (byte)0);
    Roundtrip(commandType, command, "ops rephase command");
    Set(command, "Command", (byte)10); Set(command, "Arg", (byte)2);
    Roundtrip(commandType, command, "ops orbit shift command");
    Set(command, "Command", (byte)11); Set(command, "Arg", (byte)0);
    Roundtrip(commandType, command, "ops resupply command");
    // Invest (6, program byte) and GarrisonUpgrade (8, doctrine byte).
    Set(command, "Command", (byte)6); Set(command, "Arg", (byte)4);
    Roundtrip(commandType, command, "ops invest command");
    Set(command, "Command", (byte)8); Set(command, "Arg", (byte)1);
    Roundtrip(commandType, command, "ops garrison upgrade command");
    // CYBER: build (12, kind + mark), scrap (13, slot), verb (14, target + verb), mode (15), move (16).
    Set(command, "Command", (byte)12); Set(command, "Arg", (byte)3); Set(command, "Arg2", (byte)0);
    Roundtrip(commandType, command, "ops cyber build command");
    Set(command, "Command", (byte)13); Set(command, "Arg", (byte)11);
    Roundtrip(commandType, command, "ops cyber scrap command");
    Set(command, "Command", (byte)14); Set(command, "Arg", (byte)5); Set(command, "Arg2", (byte)3);
    Roundtrip(commandType, command, "ops cyber verb command");
    Set(command, "Command", (byte)15); Set(command, "Arg", (byte)2); Set(command, "Arg2", (byte)2);
    Roundtrip(commandType, command, "ops cyber mode command");
    Set(command, "Command", (byte)16); Set(command, "Arg", (byte)4); Set(command, "Arg2", (byte)0);
    Roundtrip(commandType, command, "ops cyber move command");

    Type stateType = plugin.GetType("BoscaliSummer.Features.Support.Networking.OpsStateMessage", true)!;
    object state = Activator.CreateInstance(stateType)!;
    Set(state, "Protocol", (byte)13); Set(state, "RequestId", 91); Set(state, "Result", (byte)1);
    Set(state, "PlatformActive", true);
    Set(state, "PlatformModules", new byte[] { 0, 0, 5, 0, 0, 2, 11, 1, 14, 0, 0, 0, 13, 0, 0 });
    Set(state, "PlatformOffline", new byte[] { 0, 0, 0, 0, 0, 0, 44, 0, 0, 0, 0, 0, 0, 0, 0 });
    Set(state, "PlatformRegime", (byte)2);
    Set(state, "PlatformSeed", -123456789);
    Set(state, "PlatformClock", -12.5f);
    Set(state, "PlatformHold", (byte)2);
    Set(state, "PlatformEnergy", 1234.5f);
    Set(state, "PlatformFuel", 65.25f);
    Set(state, "PlatformRods", (byte)3);
    Set(state, "PlatformBrownout", true);
    Set(state, "PlatformPending", (byte)15);
    Set(state, "PlatformPendingCell", (byte)7);
    Set(state, "PlatformDockIn", 17.5f);
    Set(state, "PlatformRecharge", new float[] { 0f, 44.5f, 0f, 19f, 359.5f, 0f, 0f });
    Set(state, "PlatformElapsed", 1234.25f);
    Set(state, "PlatformNotice", (byte)1);
    Set(state, "PlatformNoticeCell", (byte)6);
    Set(state, "PlatformNoticeSerial", (byte)201);
    Set(state, "ForeignCount", (byte)1);
    Set(state, "ForeignRegimes", new byte[] { 0, 0, 0, 0 });
    Set(state, "ForeignSeeds", new[] { 777, 0, 0, 0 });
    Set(state, "ForeignClocks", new float[] { 300.5f, 0f, 0f, 0f });
    Set(state, "ForeignLayouts", new[] { 0x41c0, 0, 0, 0 });
    Set(state, "Sigint", (byte)2); Set(state, "Crypto", (byte)1);
    Set(state, "Disrupt", (byte)1); Set(state, "Ew", (byte)1);
    Type snapshotType = plugin.GetType("BoscaliSummer.Features.Support.Domain.Cyber.CyberSnapshot", true)!;
    object network = Activator.CreateInstance(snapshotType)!;
    Set(network, "SiteCount", (byte)2);
    ((byte[])Get(network, "Slot"))[1] = 7;
    ((byte[])Get(network, "Kind"))[0] = 1; ((byte[])Get(network, "Kind"))[1] = 3;
    ((float[])Get(network, "X"))[1] = 12345.5f; ((float[])Get(network, "Z"))[1] = -6789.25f;
    ((byte[])Get(network, "Flags"))[0] = 48; // Cyber Command on an airbase whose anchor building is down
    ((byte[])Get(network, "Flags"))[1] = 12; ((byte[])Get(network, "Mode"))[1] = 2;
    ((float[])Get(network, "PatchIn"))[1] = 4.5f; ((float[])Get(network, "BaitIn"))[0] = 33f;
    Set(network, "Bandwidth", 27.75f); Set(network, "Defeated", 5);
    Set(network, "Defended", 4); Set(network, "Breached", 2);
    ((float[])Get(network, "Recharge"))[3] = 18.5f;
    Set(network, "Heat", (byte)71); Set(network, "NextIncidentIn", 64.5f); Set(network, "ExposedIn", 12f);
    Set(network, "IncidentCount", (byte)1);
    ((byte[])Get(network, "IncidentKind"))[0] = 2; ((byte[])Get(network, "IncidentState"))[0] = 0xC0;
    ((byte[])Get(network, "IncidentSite"))[0] = 7; ((byte[])Get(network, "IncidentOrigin"))[0] = 1;
    ((float[])Get(network, "IncidentX"))[0] = 12345.5f; ((float[])Get(network, "IncidentZ"))[0] = -6789.25f;
    ((float[])Get(network, "IncidentAge"))[0] = 22f; ((float[])Get(network, "IncidentLeft"))[0] = 218f;
    ((byte[])Get(network, "IncidentTrace"))[0] = 200;
    ((byte[])Get(network, "Foothold"))[3] = 120;
    Set(network, "NoticeSerial", 77); Set(network, "NoticeCount", (byte)2);
    ((byte[])Get(network, "NoticeKind"))[0] = 7; ((byte[])Get(network, "NoticeSite"))[0] = 7;
    ((byte[])Get(network, "NoticeKind"))[1] = 1; ((byte[])Get(network, "NoticeSite"))[1] = 255;
    Set(state, "Cyber", network);
    Set(state, "CyberOriginCount", (byte)2);
    Set(state, "CyberOrigins", new[] { "BOSCALI", "PRIMEVA", null, null, null, null, null, null });
    Set(state, "ProgramTiers", new byte[] { 1, 2, 3, 0, 1, 2 });
    Set(state, "SpecOpsTokens", (byte)3); Set(state, "IntelTokens", (byte)8);
    Set(state, "SpecOpsProgress", (byte)128); Set(state, "IntelProgress", (byte)0);
    Set(state, "GarrisonLevels", new byte[] { 2, 7 });
    // The snapshot goes to every polling client: keep it inside one datagram.
    int stateBytes = Encode(stateType, state).Length;
    if (stateBytes > 900)
        throw new InvalidOperationException("Support ops state snapshot grew to " + stateBytes +
            " bytes; keep it under 900 so a poll stays in one datagram");
    object stateBack = Roundtrip(stateType, state, "ops state");

    // Worst case a host can send: every slot, every incident, every notice, every name.
    object worst = Activator.CreateInstance(snapshotType)!;
    Set(worst, "SiteCount", (byte)16);
    for (int i = 0; i < 16; i++)
    {
        ((byte[])Get(worst, "Slot"))[i] = (byte)i;
        ((byte[])Get(worst, "Kind"))[i] = 2;
        ((float[])Get(worst, "X"))[i] = 123456.5f;
        ((float[])Get(worst, "Z"))[i] = -123456.5f;
        ((float[])Get(worst, "PatchIn"))[i] = 7.5f;
        ((float[])Get(worst, "BaitIn"))[i] = 55.5f;
    }
    Set(worst, "IncidentCount", (byte)6);
    for (int i = 0; i < 6; i++)
    {
        ((byte[])Get(worst, "IncidentKind"))[i] = 2;
        ((float[])Get(worst, "IncidentX"))[i] = 123456.5f;
        ((float[])Get(worst, "IncidentZ"))[i] = -123456.5f;
        ((float[])Get(worst, "IncidentAge"))[i] = 321.5f;
        ((float[])Get(worst, "IncidentLeft"))[i] = 123.5f;
    }
    Set(worst, "NoticeCount", (byte)6);
    Set(state, "Cyber", worst);
    var longNames = new string[8];
    for (int i = 0; i < longNames.Length; i++) longNames[i] = new string('X', 40);
    Set(state, "CyberOrigins", longNames);
    Set(state, "CyberOriginCount", (byte)8);
    int worstBytes = Encode(stateType, state).Length;
    if (worstBytes > 900)
        throw new InvalidOperationException("Support ops state snapshot worst case is " + worstBytes +
            " bytes; keep it under 900 so a poll stays in one datagram");
    Set(state, "Cyber", network);
    Set(state, "CyberOrigins", new[] { "BOSCALI", "PRIMEVA", null, null, null, null, null, null });
    Set(state, "CyberOriginCount", (byte)2);
    Console.WriteLine("  Support ops state snapshot: " + stateBytes + " bytes typical, " + worstBytes +
        " bytes worst case (16 CYBER sites, 6 incidents, 6 notices, clamped origin names)");
    if (!(bool)Get(stateBack, "PlatformActive") ||
        ((byte[])Get(stateBack, "PlatformModules"))[8] != 14 ||
        ((byte[])Get(stateBack, "PlatformModules"))[12] != 13 ||
        ((byte[])Get(stateBack, "PlatformOffline"))[6] != 44 ||
        (byte)Get(stateBack, "PlatformRegime") != 2 ||
        (int)Get(stateBack, "PlatformSeed") != -123456789 ||
        Math.Abs((float)Get(stateBack, "PlatformClock") + 12.5f) > 0.001f ||
        (byte)Get(stateBack, "PlatformHold") != 2 ||
        Math.Abs((float)Get(stateBack, "PlatformEnergy") - 1234.5f) > 0.001f ||
        (byte)Get(stateBack, "PlatformRods") != 3 ||
        !(bool)Get(stateBack, "PlatformBrownout") ||
        (byte)Get(stateBack, "PlatformPending") != 15 ||
        Math.Abs(((float[])Get(stateBack, "PlatformRecharge"))[4] - 359.5f) > 0.001f ||
        (byte)Get(stateBack, "PlatformNoticeSerial") != 201 ||
        ((int[])Get(stateBack, "ForeignSeeds"))[0] != 777 ||
        ((int[])Get(stateBack, "ForeignLayouts"))[0] != 0x41c0 ||
        Math.Abs(((float[])Get(stateBack, "ForeignClocks"))[0] - 300.5f) > 0.001f ||
        (byte)Get(stateBack, "Ew") != 1 ||
        ((byte[])Get(stateBack, "ProgramTiers")).Length != 6 ||
        ((byte[])Get(stateBack, "ProgramTiers"))[2] != 3 ||
        ((byte[])Get(stateBack, "ProgramTiers"))[5] != 2 ||
        ((byte[])Get(stateBack, "GarrisonLevels")).Length != 2 ||
        ((byte[])Get(stateBack, "GarrisonLevels"))[0] != 2 ||
        ((byte[])Get(stateBack, "GarrisonLevels"))[1] != 7)
        throw new InvalidOperationException("Support ops state station/level/program/doctrine roundtrip failed");
    object networkBack = Get(stateBack, "Cyber");
    if ((byte)Get(networkBack, "SiteCount") != 2 ||
        ((byte[])Get(networkBack, "Slot"))[1] != 7 ||
        ((byte[])Get(networkBack, "Kind"))[1] != 3 ||
        Math.Abs(((float[])Get(networkBack, "X"))[1] - 12345.5f) > 0.001f ||
        ((byte[])Get(networkBack, "Flags"))[0] != 48 ||
        ((byte[])Get(networkBack, "Flags"))[1] != 12 ||
        ((byte[])Get(networkBack, "Mode"))[1] != 2 ||
        Math.Abs(((float[])Get(networkBack, "BaitIn"))[0] - 33f) > 0.001f ||
        Math.Abs((float)Get(networkBack, "Bandwidth") - 27.75f) > 0.001f ||
        (int)Get(networkBack, "Defeated") != 5 ||
        (int)Get(networkBack, "Defended") != 4 ||
        (int)Get(networkBack, "Breached") != 2 ||
        Math.Abs(((float[])Get(networkBack, "Recharge"))[3] - 18.5f) > 0.001f ||
        (byte)Get(networkBack, "Heat") != 71 ||
        (byte)Get(networkBack, "IncidentCount") != 1 ||
        ((byte[])Get(networkBack, "IncidentState"))[0] != 0xC0 ||
        ((byte[])Get(networkBack, "IncidentTrace"))[0] != 200 ||
        Math.Abs(((float[])Get(networkBack, "IncidentLeft"))[0] - 218f) > 0.001f ||
        ((byte[])Get(networkBack, "Foothold"))[3] != 120 ||
        (int)Get(networkBack, "NoticeSerial") != 77 ||
        (byte)Get(networkBack, "NoticeCount") != 2 ||
        ((byte[])Get(networkBack, "NoticeSite"))[1] != 255 ||
        (byte)Get(stateBack, "CyberOriginCount") != 2 ||
        ((string[])Get(stateBack, "CyberOrigins"))[1] != "PRIMEVA")
        throw new InvalidOperationException("Support ops state CYBER network roundtrip failed");
    // A station flag past its bound must stop the reader instead of consuming later fields.
    if ((byte)Get(Decode(stateType, new byte[] { 13, 0, 1, 9 }), "Protocol") != 0)
        throw new InvalidOperationException("Support accepted an out-of-range station flag");
    // An inactive station followed by an over-bound foreign count must be refused too.
    if ((byte)Get(Decode(stateType, new byte[] { 13, 0, 1, 0, 9 }), "Protocol") != 0)
        throw new InvalidOperationException("Support accepted an over-bound foreign station count");
    // No station, no foreign stations, four facility bytes, then seventeen CYBER sites: refused.
    if ((byte)Get(Decode(stateType, new byte[] { 13, 0, 1, 0, 0, 1, 1, 1, 1, 17 }), "Protocol") != 0)
        throw new InvalidOperationException("Support accepted an over-bound CYBER site count");
    if ((byte)Get(Decode(stateType, new byte[] { 2 }), "Protocol") != 2 ||
        (bool)Get(Decode(stateType, new byte[] { 2 }), "PlatformActive"))
        throw new InvalidOperationException("Support did not reject an old ops state header");

    Type cyberType = plugin.GetType("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", true)!;
    object cyber = Activator.CreateInstance(cyberType)!;
    Set(cyber, "Protocol", (byte)13); Set(cyber, "Kind", (byte)3);
    Set(cyber, "FactionName", "Vulture");
    Set(cyber, "X", 1234.5f); Set(cyber, "Z", -3456.5f); Set(cyber, "Duration", 15f);
    object cyberBack = Roundtrip(cyberType, cyber, "cyber effect");
    if ((string)Get(cyberBack, "FactionName") != "Vulture")
        throw new InvalidOperationException("Support cyber effect lost the attacker faction");

    Console.WriteLine("  Support protocol-13 serializers: request/result (contacts) roundtrip, ops query/command (module launch, jettison, rephase, orbit shift, resupply, invest, garrison upgrade, CYBER build/scrap/verb/mode/move)/state (station: 15-cell layout, outages, band/seed/clock/hold, energy/fuel/rods/brownout, cargo in flight, 7 recharge timers, GET, notice; 1 foreign station with layout mask, 4 facilities, CYBER network: sites/flags (incl. airbase static/down)/modes/clocks, bandwidth, ECM tally, defended/breached tally, recharges, heat, incident with trace/held bits, footholds, notices, origin names; 6 program tiers, reserve tokens/progress, 2 base-of-operations ranks) roundtrip, out-of-range flag and over-bound station/site count rejection, cyber effect faction roundtrip, old-header rejection");
}

static void ProbeEventsSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Features.Events.Networking.EventsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 3)
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
            if (!Equals(field.GetValue(source), field.GetValue(result)))
                throw new InvalidOperationException("Events " + label + " changed " + field.Name);
        }
    }

    Type changedType = plugin.GetType("BoscaliSummer.Features.Events.Networking.ActiveEventChanged", true)!;
    object changed = Activator.CreateInstance(changedType)!;
    Set(changed, "Protocol", (byte)3); Set(changed, "CatalogIndex", (sbyte)-1);
    Set(changed, "TargetFactionHash", 0);
    Set(changed, "StartedAtMissionTime", 12.5f); Set(changed, "EndsAtMissionTime", 912.5f);
    Roundtrip(changedType, changed, "state");
    object calm = Decode(changedType, Encode(changedType, changed));
    if ((sbyte)Get(calm, "CatalogIndex") != -1 || (int)Get(calm, "TargetFactionHash") != 0)
        throw new InvalidOperationException("Events state lost its calm sentinel");

    Set(changed, "CatalogIndex", (sbyte)11); Set(changed, "TargetFactionHash", -1234567);
    Roundtrip(changedType, changed, "targeted state");
    if ((int)Get(Decode(changedType, Encode(changedType, changed)), "TargetFactionHash") != -1234567)
        throw new InvalidOperationException("Events state lost its target faction hash");

    Type intentType = plugin.GetType("BoscaliSummer.Features.Events.Networking.EventIntent", true)!;
    object intent = Activator.CreateInstance(intentType)!;
    Set(intent, "Protocol", (byte)3); Set(intent, "Token", 77u);
    Set(intent, "Action", (byte)1); Set(intent, "CatalogIndex", (sbyte)7);
    Roundtrip(intentType, intent, "intent");

    Type replyType = plugin.GetType("BoscaliSummer.Features.Events.Networking.EventReply", true)!;
    object reply = Activator.CreateInstance(replyType)!;
    Set(reply, "Protocol", (byte)3); Set(reply, "Token", 77u); Set(reply, "CatalogIndex", (sbyte)7);
    Set(reply, "Result", (byte)4); Set(reply, "Kind", (byte)1); Set(reply, "Cost", 700);
    Roundtrip(replyType, reply, "reply");

    if ((byte)Get(Decode(changedType, new byte[] { 1 }), "Protocol") != 1 ||
        (sbyte)Get(Decode(changedType, new byte[] { 1 }), "CatalogIndex") != 0)
        throw new InvalidOperationException("Events did not reject an old state header");

    Console.WriteLine("  Events protocol-3 serializers: state (-1 sentinel + target faction hash + timestamps), query intent, already-responded reply roundtrip, old-header rejection");
}

static void ProbeTheaterOpsSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Features.TheaterOps.Networking.TheaterOpsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 1)
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

    Type queryType = plugin.GetType("BoscaliSummer.Features.TheaterOps.Networking.TheaterPriorityQuery", true)!;
    object query = Activator.CreateInstance(queryType)!;
    Set(query, "Protocol", (byte)1);
    Roundtrip(queryType, query, "query");

    Type stateType = plugin.GetType("BoscaliSummer.Features.TheaterOps.Networking.TheaterPriorityState", true)!;
    object state = Activator.CreateInstance(stateType)!;
    Set(state, "Protocol", (byte)1); Set(state, "Active", (byte)1);
    Set(state, "Faction", "Coalition"); Set(state, "Key", "obj-north");
    Set(state, "Label", "Northern Corridor");
    Set(state, "X", 1200f); Set(state, "Y", 30f); Set(state, "Z", -800f);
    Roundtrip(stateType, state, "state");

    // A clear carries only the header and the faction; identity fields are deliberately absent.
    object clear = Activator.CreateInstance(stateType)!;
    Set(clear, "Protocol", (byte)1); Set(clear, "Active", (byte)0);
    Set(clear, "Faction", "Coalition");
    object clearResult = Decode(stateType, Encode(stateType, clear));
    if ((byte)Get(clearResult, "Active") != 0 || (string)Get(clearResult, "Faction") != "Coalition" ||
        Get(clearResult, "Key") != null || Get(clearResult, "Label") != null)
        throw new InvalidOperationException("TheaterOps clear state lost its faction or sentinel");

    if ((byte)Get(Decode(stateType, new byte[] { 9 }), "Protocol") != 9 ||
        (byte)Get(Decode(stateType, new byte[] { 9 }), "Active") != 0)
        throw new InvalidOperationException("TheaterOps did not reject an unknown state header");

    Console.WriteLine("  TheaterOps protocol-1 serializers: query, active state, clear sentinel, unknown-header rejection");
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
