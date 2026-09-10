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
    ("PilotPlayerState", "PlayerAxisControls"),
    ("CameraStateManager", "SwitchState"),
    ("CameraStateManager", "SetFollowingUnit"),
    ("CameraOrbitState", "UpdateState"),
    ("CameraChaseState", "UpdateState"),
    ("GameplayUI", "SelectAircraft"),
    ("GameplayUI", "PauseGame"),
    ("GameplayUI", "ResumeGame"),
    ("FlightHud", "EnableCanvas"),
    ("DynamicMap", "EnableCanvas"),
    ("WeaponManager", "GetTargetList"),
    ("Airbase", "CaptureFaction"),
    ("Building", "OnStartClient"),
    ("BulletSim+Bullet", "TrajectoryTrace"),
    ("GroundVehicle", "UnitDisabled"),
    ("MapBuilding", "TakeDamage"),
    ("MapBuilding", "TakeShockwave"),
    ("Missile", "UserCode_RpcDetonate_897349600"),
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
    ("DynamicMap", "Minimize")
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
    ("CameraOrbitState", "panView", "System.Single"),
    ("CameraOrbitState", "tiltView", "System.Single"),
    ("CameraOrbitState", "viewDistAdjust", "System.Single"),
    ("CameraOrbitState", "lookAtTargetLerp", "System.Single"),
    ("CameraChaseState", "viewDistAdjust", "System.Single"),
    ("CameraChaseState", "currentPos", "CameraChaseState+ChasePos")
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
    ("Aircraft", "UseFuel", new[] { "fuelDrawn" }),
    ("FactionHQ", "RewardPlayer", new[] { "player", "rewardAllocation", "missionType" })
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
    "BoscaliSummer.Features.Support.Patches.SupportMissileDetonatePatch",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonHudPatches",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonOrbitPatch",
    "BoscaliSummer.Features.QoL.Patches.ThirdPersonChasePatch",
    "BoscaliSummer.Features.QoL.Patches.GunAimSolutionPatch",
    "BoscaliSummer.Features.QoL.Patches.GunAimInputPatch"
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
    "BoscaliSummer.Features.Command.CommandFeature"
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
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Score", typeof(ushort))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "EarnedPoints", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Rank", typeof(byte))
    ,("BoscaliSummer.Features.Progression.Networking.ProgressionSnapshot", "Result", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Action", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "X", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Y", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportRequestMessage", "Z", typeof(float))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "RequestId", typeof(int))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Protocol", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Action", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "Result", typeof(byte))
    ,("BoscaliSummer.Features.Support.Networking.SupportResultMessage", "CooldownSeconds", typeof(float))
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
    ("UnitDefinition", "IsAllowed", "System.Boolean(System.Boolean)"),
    ("NuclearOption.Networking.Player", "get_HQ", "FactionHQ()"),
    ("NuclearOption.Networking.Player", "AddScore", "System.Void(System.Single)"),
    ("NuclearOption.Networking.Player", "AddAllocation", "System.Void(System.Single)"),
    ("MissionManager", "get_IsRunning", "System.Boolean()"),
    ("MissionManager", "get_MissionTime", "System.Single()")
};
foreach (var seam in operationMethods)
    RequireMetadataSignature(operationAssembly, seam.Type, seam.Method, seam.Signature);
RequireMetadataSignature(Path.Combine(managedDir, "Mirage.dll"), "Mirage.ServerObjectManager", "Destroy", "System.Void(UnityEngine.GameObject,System.Boolean)");
(string Type, string Field, string FieldType)[] operationFields =
{
    ("FactionRegistry", "airbaseLookup", "System.Collections.Generic.Dictionary`2<System.String,Airbase>"),
    ("FactionHQ", "factionPlayers", "Mirage.Collections.SyncList`1<NuclearOption.Networking.PlayerRef>"),
    ("TrackingInfo", "lastKnownPosition", "GlobalPosition"),
    ("TrackingInfo", "lastSpottedTime", "System.Single"),
    ("Airbase", "center", "UnityEngine.Transform"),
    ("NuclearOption.SavedMission.SavedAirbase", "Capturable", "System.Boolean"),
    ("RoadPathfinding.RoadNetwork", "roads", "System.Collections.Generic.List`1<RoadPathfinding.Road>"),
    ("RoadPathfinding.RoadNetwork", "nodes", "System.Collections.Generic.List`1<RoadPathfinding.Node>"),
    ("RoadPathfinding.Road", "points", "System.Collections.Generic.List`1<GlobalPosition>"),
    ("Encyclopedia", "vehicles", "System.Collections.Generic.List`1<VehicleDefinition>"),
    ("Encyclopedia", "buildings", "System.Collections.Generic.List`1<BuildingDefinition>")
};
foreach (var seam in operationFields)
    RequireMetadataField(operationAssembly, seam.Type, seam.Field, seam.FieldType);
if (Convert.ToInt32(Enum.Parse(gameAssembly.GetType("FactionHQ+RewardType", true)!, "None")) != 0)
    throw new InvalidOperationException("Dynamic operations reward category changed");
foreach (string type in new[] {
    "BoscaliSummer.Features.DynamicOperations.DynamicOperationsFeature",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationsManager",
    "BoscaliSummer.Features.DynamicOperations.Runtime.OperationRewards",
    "BoscaliSummer.Features.DynamicOperations.Networking.OperationsNet" })
    if (pluginAssembly.GetType(type, false) == null) throw new TypeLoadException(type);
foreach (var contract in new[] {
    ("BoscaliSummer.Features.DynamicOperations.Networking.OperationsQuery", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32" }),
    ("BoscaliSummer.Features.DynamicOperations.Networking.OperationsSnapshot", new[] { "Protocol:System.Byte", "Scene:System.UInt32", "Token:System.UInt32", "Status:System.String", "Cards:BoscaliSummer.Framework.Contracts.SecondaryObjectiveView[]" }) })
{
    Type type = pluginAssembly.GetType(contract.Item1, true)!;
    string[] actual = type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.MetadataToken)
        .Select(field => field.Name + ":" + field.FieldType.FullName).ToArray();
    if (!actual.SequenceEqual(contract.Item2)) throw new InvalidOperationException(contract.Item1 + " wire fields changed");
}
ProbeOperationSerialization(pluginAssembly, mirageAssembly);
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
    if ((byte)net.GetField("ProtocolVersion", flags)!.GetRawConstantValue()! != 1)
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
        Activator.CreateInstance(cardType, 123, new string('T', 150), "description", "target", "status", "reward", progress, remaining, money, xp, true)!;
    object Snapshot(int count, object card)
    {
        object snapshot = Activator.CreateInstance(snapshotType)!;
        snapshotType.GetField("Protocol")!.SetValue(snapshot, (byte)1);
        snapshotType.GetField("Scene")!.SetValue(snapshot, 345u);
        snapshotType.GetField("Token")!.SetValue(snapshot, 678u);
        snapshotType.GetField("Status")!.SetValue(snapshot, new string('S', 200));
        Array cards = Array.CreateInstance(cardType, count);
        for (int i = 0; i < count; i++) cards.SetValue(card, i);
        snapshotType.GetField("Cards")!.SetValue(snapshot, cards);
        return snapshot;
    }
    object query = Activator.CreateInstance(queryType)!;
    queryType.GetField("Protocol")!.SetValue(query, (byte)1);
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
    object expectedCard = Activator.CreateInstance(cardType, 123, new string('T', 128), "description", "target", "status", "reward", 0.75f, 300f, 17, 29, true)!;
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
