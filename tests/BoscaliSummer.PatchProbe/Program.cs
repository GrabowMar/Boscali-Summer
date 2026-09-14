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
    ("ControlsFilter", "GetAim"),
    ("ControlsFilter", "SetFlightAssist"),
    ("PilotPlayerState", "PlayerAxisControls"),
    ("CameraStateManager", "SwitchState"),
    ("CameraStateManager", "SetFollowingUnit"),
    ("CameraOrbitState", "UpdateState"),
    ("CameraChaseState", "UpdateState"),
    ("GameplayUI", "SelectAircraft"),
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
    ("BulletSim+Bullet", "TrajectoryTrace"),
    ("GroundVehicle", "UnitDisabled"),
    ("MapBuilding", "TakeDamage"),
    ("MapBuilding", "TakeShockwave"),
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
    ("Missile", "Arm"),
    ("Missile", "SetAimpoint"),
    ("FactionHQ", "SetTrackingState"),
    ("FactionHQ", "GetTrackingData"),
    ("UnitRegistry", "RegisterUnit"),
    ("MountedTroops", "Fire"),
    ("LoadoutSelector", "AssignAircraft"),
    ("WeaponManager", "InitializeWeaponManager"),
    ("WeaponSelector", "PopulateOptions"),
    ("WeaponChecker", "GetAvailableWeaponsNonAlloc"),
    ("CombatAI", "AnalyzeTarget"),
    ("DynamicMap", "Maximize"),
    ("DynamicMap", "Minimize"),
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
    ("RoleIdentity", "antiSurface")
    ,("GroundVehicle", "parachuteSystem")
    ,("RadialMenuMain", "actionsMain")
    ,("RadialMenuMain", "aircraft")
    ,("RadialMenuAction", "actionType")
    ,("RadialMenuAction", "iconSprite")
    ,("RadialMenuAction", "backgroundSprite")
    ,("RadialMenuAction", "backgroundColorInactive")
    ,("RadialMenuAction", "backgroundColorActive")
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
    ("PilotBaseState", "pilot", "Pilot"),
    ("PilotPlayerState", "pilotStrength", "System.Single"),
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
MethodInfo gunAim = gameAssembly.GetType("ControlsFilter", true)!.GetMethod("GetAim", AllMembers)!;
var gunAimParameters = gunAim.GetParameters();
Type globalPosition = gameAssembly.GetType("GlobalPosition", true)!;
Type optionalPositionRef = typeof(Nullable<>).MakeGenericType(globalPosition).MakeByRefType();
if (gunAim.ReturnType != typeof(void) || gunAimParameters.Length != 3 ||
    gunAimParameters[0].ParameterType.FullName != "Unit" ||
    gunAimParameters[1].ParameterType != optionalPositionRef || !gunAimParameters[1].IsOut ||
    gunAimParameters[2].ParameterType != optionalPositionRef || !gunAimParameters[2].IsOut)
    throw new InvalidOperationException("ControlsFilter.GetAim gun solution signature changed");
MethodInfo playerAxes = gameAssembly.GetType("PilotPlayerState", true)!.GetMethod("PlayerAxisControls", AllMembers)!;
if (playerAxes.ReturnType != typeof(void) || playerAxes.GetParameters().Length != 0 || playerAxes.IsStatic)
    throw new InvalidOperationException("PilotPlayerState.PlayerAxisControls input signature changed");
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
    ("ControlsFilter", "GetAim", new[] { "target", "aimPoint", "impactPoint" }),
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

// The marker postfix binds this exact overload by parameter name, so pin the overload and its
// names: the type-only probe above cannot disambiguate the Unit/FactionHQ overload pair.
MethodInfo markerPositions = gameAssembly.GetType("MissionPosition", true)!.GetMethods(AllMembers)
    .FirstOrDefault(method => method.Name == "GetAllPositionsResults" &&
        method.GetParameters() is { Length: 4 } parameters && parameters[0].ParameterType.FullName == "FactionHQ")
    ?? throw new MissingMethodException("MissionPosition.GetAllPositionsResults(FactionHQ, GlobalPosition, bool, List<PositionResult>)");
string[] markerPositionParameters = markerPositions.GetParameters().Select(parameter => parameter.Name!).ToArray();
if (!markerPositionParameters.SequenceEqual(new[] { "factionHQ", "from", "includeHidden", "results" }))
    throw new MissingMemberException(
        "MissionPosition.GetAllPositionsResults parameters changed: " + string.Join(", ", markerPositionParameters));

Type levelInfo = gameAssembly.GetType("LevelInfo", true)!;
if (levelInfo.GetProperty("LoadedMapSettings", AllMembers) == null)
    throw new MissingMemberException("LevelInfo.LoadedMapSettings");

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
    "BoscaliSummer.Features.Command.Patches.AiTargetScoringPatch",
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
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonHudPatches",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonOrbitPatch",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonChasePatch",
    "BoscaliSummer.Features.QoL.Patches.GunAimSolutionPatch",
    "BoscaliSummer.Features.QoL.Patches.GunAimInputPatch",
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
    ,"BoscaliSummer.Features.DynamicOperations.Runtime.OperationMarkerPatch"
    ,"BoscaliSummer.Features.HighCommand.Patches.HighCommandDamagePatch"
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
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "SatelliteCount", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "SatelliteIds", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "SatelliteRoles", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "SatelliteAltitudes", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "SatelliteStates", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "SatelliteFuel", typeof(byte[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "StationXs", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "StationZs", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "OriginXs", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "OriginZs", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "TransitLeft", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "TransitTotal", typeof(float[]))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Sigint", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Crypto", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Disrupt", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.OpsStateMessage", "Ew", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Kind", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "FactionName", typeof(string))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "X", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Z", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", "Duration", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "Scene", typeof(uint))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "Token", typeof(uint))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "Action", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandQuery", "TargetId", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Id", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "ParentId", typeof(int))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Tier", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Flags", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Actions", typeof(byte))
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
    ,("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", "Decoration", typeof(string))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Scene", typeof(uint))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Token", typeof(uint))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Status", typeof(string))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Signal", typeof(string))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "Cohesion", typeof(float))
    ,("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", "CommandPoints", typeof(int))
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
    ,("NuclearOption.SavedMission.SavedObjective", "CreateSavedObjective", "NuclearOption.SavedMission.SavedObjective(NuclearOption.SavedMission.ObjectiveType,System.String)")
    ,("GlobalPosition", ".ctor", "System.Void(System.Single,System.Single,System.Single)")
    ,("NuclearOption.SavedMission.ObjectivePosition", ".ctor", "System.Void(GlobalPosition,System.Nullable`1<System.Single>)")
    ,("GameManager", "GetLocalAircraft", "System.Boolean(Aircraft&)")
    ,("Aircraft", "HasEjected", "System.Boolean()")
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
    ("Encyclopedia", "buildings", "System.Collections.Generic.List`1<BuildingDefinition>")
    ,("PersistentUnit", "player", "NuclearOption.Networking.Player")
    ,("Pilot", "dead", "System.Boolean")
    ,("Pilot", "ejected", "System.Boolean")
    ,("Pilot", "aircraft", "Aircraft")
    ,("Aircraft", "pilots", "Pilot[]")
};
foreach (var seam in operationFields)
    RequireMetadataField(operationAssembly, seam.Type, seam.Field, seam.FieldType);
if (Convert.ToInt32(Enum.Parse(gameAssembly.GetType("FactionHQ+RewardType", true)!, "None")) != 0)
    throw new InvalidOperationException("Dynamic operations reward category changed");
foreach (string type in new[] {
    "BoscaliSummer.Features.DynamicOperations.DynamicOperationsFeature",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationsManager",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationRewards",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationObjective",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationMarkerBridge",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationZoneHud",
    "BoscaliSummer.Features.DynamicOperations.Networking.OperationsNet" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (string type in new[] {
    "BoscaliSummer.Features.HighCommand.Runtime.HighCommandManager",
    "BoscaliSummer.Features.HighCommand.Networking.HighCommandNet",
    "BoscaliSummer.Features.HighCommand.Domain.CommandTree",
    "BoscaliSummer.Framework.Contracts.IHighCommandView" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
Type highCommandSnapshot = pluginAssembly.GetType("BoscaliSummer.Features.HighCommand.Networking.HighCommandSnapshot", true)!;
Type commanderWire = pluginAssembly.GetType("BoscaliSummer.Features.HighCommand.Networking.CommanderWire", true)!;
FieldInfo snapshotNodes = highCommandSnapshot.GetField("Nodes", AllMembers) ??
    throw new MissingFieldException(highCommandSnapshot.FullName, "Nodes");
if (snapshotNodes.FieldType != commanderWire.MakeArrayType())
    throw new InvalidOperationException("High command snapshot node array type changed");
foreach (var contract in new[] {
    ("BoscaliSummer.Features.DynamicOperations.Networking.OperationsQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "OperationId:System.Int32", "Action:System.Byte" }),
    ("BoscaliSummer.Features.DynamicOperations.Networking.OperationsSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Status:System.String", "Cards:BoscaliSummer.Framework.Contracts.SecondaryObjectiveView[]" }),
    ("BoscaliSummer.Features.Progression.Networking.ProgressionSubmit", new[] { "Protocol:System.Byte", "Perk:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Generation:System.Int32" }),
    ("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", new[] { "Protocol:System.Byte", "PerkMask:System.UInt32", "Score:System.Int32", "EarnedPoints:System.Byte", "Rank:System.Byte", "Result:System.Byte", "Generation:System.Int32", "Scene:System.UInt32", "Token:System.UInt32", "ScorePerPoint:System.Int32", "MaximumPoints:System.Byte" }),
    ("BoscaliSummer.Features.Squad.Networking.SquadQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32" }),
    ("BoscaliSummer.Features.Squad.Networking.SquadSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Event:System.UInt32", "Pilot:BoscaliSummer.Framework.Contracts.PilotView", "Hunt:System.Boolean", "Bonus:System.Int32", "Origin:System.Int32", "ActiveIndex:System.Int32", "HuntId:System.Int32", "Status:System.String", "Speaker:System.String", "Chatter:System.String", "Wings:BoscaliSummer.Framework.Contracts.EnemyWingView[]" }),
    ("BoscaliSummer.Features.Events.Networking.ActiveEventChanged", new[] { "Protocol:System.Byte", "CatalogIndex:System.SByte", "StartedAtMissionTime:System.Single", "EndsAtMissionTime:System.Single" }),
    ("BoscaliSummer.Features.Events.Networking.EventIntent", new[] { "Protocol:System.Byte", "Token:System.UInt32", "Action:System.Byte", "CatalogIndex:System.SByte" }),
    ("BoscaliSummer.Features.Events.Networking.EventReply", new[] { "Protocol:System.Byte", "Token:System.UInt32", "CatalogIndex:System.SByte", "Result:System.Byte", "Kind:System.Byte", "Cost:System.Int32" }) })
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
CustomAttributeData dependency = pluginAssembly.GetType("BoscaliSummer.Plugin", true)!.CustomAttributes
    .FirstOrDefault(attribute => attribute.AttributeType.FullName == "BepInEx.BepInDependency" &&
        attribute.ConstructorArguments.Count == 2 &&
        Equals(attribute.ConstructorArguments[0].Value, "com.marci.wingcommand"));
if (dependency == null || !Equals(dependency.ConstructorArguments[1].Value, "0.9.2.6"))
    throw new InvalidOperationException("Wing Command minimum hard dependency must be 0.9.2.6");
Console.WriteLine($"Patch target probe: game methods/fields, Harmony parameters, {patchTypes.Length} patch classes, {featureTypes.Length + 1} features, radio assets, wire contracts, dynamic operation reward/road signatures, and Mirage seams resolved.");
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
    if ((byte)progression.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 3)
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
    Set(progress, "Protocol", (byte)3); Set(progress, "Generation", 10001); Set(progress, "PerkMask", 123u);
    Set(progress, "Score", 70000); Set(progress, "Scene", 456u); Set(progress, "Token", 789u);
    Set(progress, "ScorePerPoint", 10000); Set(progress, "MaximumPoints", (byte)20);
    object progressResult = Decode(progressType, Encode(progressType, progress));
    if ((int)Get(progressResult, "Generation") != 10001 || (uint)Get(progressResult, "PerkMask") != 123u ||
        (int)Get(progressResult, "Score") != 70000 || (uint)Get(progressResult, "Scene") != 456u || (uint)Get(progressResult, "Token") != 789u ||
        (int)Get(progressResult, "ScorePerPoint") != 10000 || (byte)Get(progressResult, "MaximumPoints") != 20)
        throw new InvalidOperationException("Progression generation roundtrip failed");
    Type submitType = plugin.GetType("BoscaliSummer.Features.Progression.Networking.ProgressionSubmit", true)!;
    object submit = Activator.CreateInstance(submitType)!;
    Set(submit, "Protocol", (byte)3); Set(submit, "Perk", (byte)4); Set(submit, "Scene", 456u); Set(submit, "Token", 789u); Set(submit, "Generation", 10001);
    object submitResult = Decode(submitType, Encode(submitType, submit));
    foreach (FieldInfo field in submitType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (!Equals(field.GetValue(submit), field.GetValue(submitResult))) throw new InvalidOperationException("Progression intent roundtrip changed " + field.Name);
    Console.WriteLine("  Squad serializers: pilot/wing/query roundtrip, 8-wing/192-char bounds, invalid strength/tier/return/header/count rejection; progression generation v3");
}

static void ProbeSupportSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Features.Support.Networking.SupportNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 7)
        throw new InvalidOperationException("Support protocol differs from the constellation contract");
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
        object writer = Activator.CreateInstance(writerType, 256)!;
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
            if (field.FieldType.IsArray) continue;
            if (!Equals(field.GetValue(source), field.GetValue(result)))
                throw new InvalidOperationException("Support " + label + " changed " + field.Name);
        }
        return result;
    }

    Type requestType = plugin.GetType("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", true)!;
    object request = Activator.CreateInstance(requestType)!;
    Set(request, "Protocol", (byte)7); Set(request, "RequestId", 7123); Set(request, "Action", (byte)6);
    Set(request, "X", 1234.5f); Set(request, "Y", 2345.5f); Set(request, "Z", -3456.5f);
    Roundtrip(requestType, request, "request");

    Type resultType = plugin.GetType("BoscaliSummer.Features.Support.Networking.SupportResultMessage", true)!;
    object resultMessage = Activator.CreateInstance(resultType)!;
    Set(resultMessage, "Protocol", (byte)7); Set(resultMessage, "RequestId", 7123);
    Set(resultMessage, "Action", (byte)4); Set(resultMessage, "Result", (byte)1);
    Set(resultMessage, "CooldownSeconds", 30f); Set(resultMessage, "Radius", 6000f);
    Set(resultMessage, "Duration", 10f); Set(resultMessage, "Contacts", 48);
    Set(resultMessage, "X", 1234.5f); Set(resultMessage, "Y", 2345.5f); Set(resultMessage, "Z", -3456.5f);
    object resultBack = Roundtrip(resultType, resultMessage, "result");
    if ((int)Get(resultBack, "Contacts") != 48)
        throw new InvalidOperationException("Support result lost the sweep contact count");

    Type queryType = plugin.GetType("BoscaliSummer.Features.Support.Networking.OpsQueryMessage", true)!;
    object query = Activator.CreateInstance(queryType)!;
    Set(query, "Protocol", (byte)7);
    Roundtrip(queryType, query, "ops query");

    Type commandType = plugin.GetType("BoscaliSummer.Features.Support.Networking.OpsCommandMessage", true)!;
    object command = Activator.CreateInstance(commandType)!;
    Set(command, "Protocol", (byte)7); Set(command, "RequestId", 91); Set(command, "Command", (byte)1);
    Set(command, "Arg", (byte)2); Set(command, "Arg2", (byte)0); Set(command, "X", 1234.5f); Set(command, "Z", -3456.5f);
    Roundtrip(commandType, command, "ops command");

    Type stateType = plugin.GetType("BoscaliSummer.Features.Support.Networking.OpsStateMessage", true)!;
    object state = Activator.CreateInstance(stateType)!;
    Set(state, "Protocol", (byte)7); Set(state, "RequestId", 91); Set(state, "Result", (byte)1);
    Set(state, "SatelliteCount", (byte)2);
    Set(state, "SatelliteIds", new byte[] { 1, 2, 0, 0 });
    Set(state, "SatelliteRoles", new byte[] { 0, 2, 0, 0 });
    Set(state, "SatelliteAltitudes", new byte[] { 0, 2, 0, 0 });
    Set(state, "SatelliteStates", new byte[] { 0, 1, 0, 0 });
    Set(state, "SatelliteFuel", new byte[] { 100, 40, 0, 0 });
    Set(state, "StationXs", new float[] { 1.5f, -2.5f, 0f, 0f });
    Set(state, "StationZs", new float[] { -1.5f, 2.5f, 0f, 0f });
    Set(state, "OriginXs", new float[] { 0.5f, -0.5f, 0f, 0f });
    Set(state, "OriginZs", new float[] { -0.5f, 0.5f, 0f, 0f });
    Set(state, "TransitLeft", new float[] { 0f, 12.5f, 0f, 0f });
    Set(state, "TransitTotal", new float[] { 0f, 25f, 0f, 0f });
    Set(state, "Sigint", (byte)2); Set(state, "Crypto", (byte)1);
    Set(state, "Disrupt", (byte)1); Set(state, "Ew", (byte)1);
    Set(state, "EwAssetState", (byte)2);
    object stateBack = Roundtrip(stateType, state, "ops state");
    if ((byte)Get(stateBack, "SatelliteCount") != 2 ||
        ((byte[])Get(stateBack, "SatelliteIds"))[1] != 2 ||
        ((byte[])Get(stateBack, "SatelliteRoles"))[1] != 2 ||
        ((byte[])Get(stateBack, "SatelliteAltitudes"))[1] != 2 ||
        Math.Abs(((float[])Get(stateBack, "StationXs"))[1] + 2.5f) > 0.001f ||
        Math.Abs(((float[])Get(stateBack, "TransitLeft"))[1] - 12.5f) > 0.001f ||
        (byte)Get(stateBack, "Ew") != 1 ||
        (byte)Get(stateBack, "EwAssetState") != 2)
        throw new InvalidOperationException("Support ops state bounds/level roundtrip failed");
    if ((byte)Get(Decode(stateType, new byte[] { 2 }), "Protocol") != 2 ||
        (byte)Get(Decode(stateType, new byte[] { 2 }), "SatelliteCount") != 0)
        throw new InvalidOperationException("Support did not reject an old ops state header");

    Type cyberType = plugin.GetType("BoscaliSummer.Features.Support.Networking.CyberEffectMessage", true)!;
    object cyber = Activator.CreateInstance(cyberType)!;
    Set(cyber, "Protocol", (byte)7); Set(cyber, "Kind", (byte)3);
    Set(cyber, "FactionName", "Vulture");
    Set(cyber, "X", 1234.5f); Set(cyber, "Z", -3456.5f); Set(cyber, "Duration", 15f);
    object cyberBack = Roundtrip(cyberType, cyber, "cyber effect");
    if ((string)Get(cyberBack, "FactionName") != "Vulture")
        throw new InvalidOperationException("Support cyber effect lost the attacker faction");

    Console.WriteLine("  Support protocol-7 serializers: request/result (contacts) roundtrip, ops query/command/state (2 satellites, station/transfer state, 4 facilities, EW asset state) roundtrip, cyber effect faction roundtrip, old-header rejection");
}

static void ProbeEventsSerialization(Assembly plugin, Assembly mirage)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    Type net = plugin.GetType("BoscaliSummer.Features.Events.Networking.EventsNet", true)!;
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 2)
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
        object writer = Activator.CreateInstance(writerType, 256)!;
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
    Set(changed, "Protocol", (byte)2); Set(changed, "CatalogIndex", (sbyte)-1);
    Set(changed, "StartedAtMissionTime", 12.5f); Set(changed, "EndsAtMissionTime", 912.5f);
    Roundtrip(changedType, changed, "state");
    if ((sbyte)Get(Decode(changedType, Encode(changedType, changed)), "CatalogIndex") != -1)
        throw new InvalidOperationException("Events state lost its calm sentinel");

    Type intentType = plugin.GetType("BoscaliSummer.Features.Events.Networking.EventIntent", true)!;
    object intent = Activator.CreateInstance(intentType)!;
    Set(intent, "Protocol", (byte)2); Set(intent, "Token", 77u);
    Set(intent, "Action", (byte)1); Set(intent, "CatalogIndex", (sbyte)7);
    Roundtrip(intentType, intent, "intent");

    Type replyType = plugin.GetType("BoscaliSummer.Features.Events.Networking.EventReply", true)!;
    object reply = Activator.CreateInstance(replyType)!;
    Set(reply, "Protocol", (byte)2); Set(reply, "Token", 77u); Set(reply, "CatalogIndex", (sbyte)7);
    Set(reply, "Result", (byte)4); Set(reply, "Kind", (byte)1); Set(reply, "Cost", 700);
    Roundtrip(replyType, reply, "reply");

    if ((byte)Get(Decode(changedType, new byte[] { 1 }), "Protocol") != 1 ||
        (sbyte)Get(Decode(changedType, new byte[] { 1 }), "CatalogIndex") != 0)
        throw new InvalidOperationException("Events did not reject an old state header");

    Console.WriteLine("  Events protocol-2 serializers: state (-1 sentinel + timestamps), query intent, already-responded reply roundtrip, old-header rejection");
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
