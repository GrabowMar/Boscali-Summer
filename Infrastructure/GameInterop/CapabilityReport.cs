using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;

namespace BoscaliSummer.Runtime
{
    internal static class CapabilityReport
    {
        public static void Log()
        {
            bool bullet = AccessTools.Method(typeof(BulletSim.Bullet), "TrajectoryTrace") != null;
            bool missile = AccessTools.Method(typeof(Missile), "UserCode_RpcDetonate_897349600") != null;
            bool vehicle = AccessTools.Method(typeof(GroundVehicle), nameof(GroundVehicle.UnitDisabled)) != null;
            bool capture = AccessTools.Method(typeof(Airbase), "CaptureFaction") != null;
            bool blast = AccessTools.Method(typeof(BlastManager), "AddBlast") != null;
            bool blastStamp = AccessTools.Method(typeof(BlastManager), "DrawBlast") != null;
            bool scorchDecal = AccessTools.Field(typeof(GameAssets), "scorchMarkDecal") != null;
            bool musicManager = AccessTools.Method(typeof(MusicManager), nameof(MusicManager.PlayMusic)) != null &&
                AccessTools.Method(typeof(MusicManager), nameof(MusicManager.CrossFadeMusic)) != null;
            bool soundtrackCatalog =
                AccessTools.Method(typeof(MapSettings), nameof(MapSettings.GetStartMusic)) != null &&
                AccessTools.Method(typeof(MapSettings), nameof(MapSettings.GetStrategicMusic)) != null &&
                AccessTools.Method(typeof(MapSettings), nameof(MapSettings.GetTacticalMusic)) != null;
            bool progression = AccessTools.Method(typeof(FactionHQ), nameof(FactionHQ.RewardPlayer)) != null &&
                AccessTools.Method(typeof(Aircraft), nameof(Aircraft.UseFuel)) != null;
            bool supportSpawning = AccessTools.Method(typeof(Spawner), nameof(Spawner.SpawnVehicle)) != null &&
                AccessTools.Method(typeof(Spawner), nameof(Spawner.SpawnBuilding)) != null &&
                AccessTools.Method(typeof(DynamicMap), nameof(DynamicMap.TryGetCursorCoordinates)) != null;
            bool supportRecon = AccessTools.Method(typeof(FactionHQ), "SetTrackingState",
                new[] { typeof(PersistentID), typeof(GlobalPosition), typeof(float) }) != null;
            bool dynamicOperations = AccessTools.Property(typeof(MissionManager), nameof(MissionManager.IsRunning)) != null &&
                AccessTools.Method(typeof(FactionHQ), nameof(FactionHQ.RewardPlayer)) != null &&
                  AccessTools.Method(typeof(FactionHQ), nameof(FactionHQ.GetTrackingData)) != null &&
                  AccessTools.Method(typeof(Unit), nameof(Unit.Jam), new[] { typeof(Unit.JamEventArgs) }) != null && supportSpawning;
            bool operationServices = AccessTools.Method(typeof(PilotDismounted), nameof(PilotDismounted.Capture), new[] { typeof(Unit) }) != null &&
                AccessTools.Method(typeof(Building), nameof(Building.Repair), new[] { typeof(Unit), typeof(float) }) != null &&
                AccessTools.Method(typeof(Rearmer), nameof(Rearmer.ProcessRearmRequest), new[] { typeof(Unit), typeof(int).MakeByRefType() }) != null &&
                AccessTools.Method(typeof(Rearmer), nameof(Rearmer.RefillOtherRearmer), new[] { typeof(Rearmer) }) != null &&
                AccessTools.Method(typeof(Aircraft), nameof(Aircraft.IsLanded)) != null;
            bool autopilotLanding = AccessTools.Method(typeof(Autopilot), nameof(Autopilot.Hover)) != null &&
                AccessTools.Method(typeof(Autopilot), "AutoAim", new[]
                {
                    typeof(GlobalPosition), typeof(bool), typeof(bool), typeof(bool), typeof(float),
                    typeof(float), typeof(bool), typeof(float), typeof(Vector3)
                }) != null &&
                AccessTools.Method(typeof(Airbase), nameof(Airbase.RequestLanding)) != null &&
                AccessTools.Method(typeof(Airbase.Runway), nameof(Airbase.Runway.IsSuitable)) != null &&
                AccessTools.Field(typeof(RadialMenuMain), "actionsMain") != null;
            bool highCommand = supportSpawning &&
                AccessTools.Method(typeof(UnitRegistry), "TryGetPersistentUnit") != null &&
                AccessTools.Method(typeof(FactionHQ), nameof(FactionHQ.AddFunds)) != null &&
                AccessTools.Method(typeof(FactionHQ), nameof(FactionHQ.AddScore)) != null &&
                AccessTools.Method(typeof(Unit), "add_onDisableUnit") != null;
            Plugin.Logger.LogInfo(
                "Capabilities: " +
                $"BulletImpacts={bullet}, MissileImpacts={missile}, VehicleLosses={vehicle}, " +
                $"MapBuildingHP={GameAccess.MapBuildingHitPointsAvailable}, " +
                $"ScorchMap={blast}, ScorchStamps={blastStamp}, FacadeScorch={scorchDecal}, AirbaseCapture={capture}, " +
                $"RadioMFD={GameAccess.MfdAvailable}, MusicMixer={musicManager}, " +
                $"MusicOwnership={GameAccess.MusicSourcesAvailable}, " +
                $"SoundtrackCatalog={soundtrackCatalog}, Progression={progression}, " +
                $"SquadWingCommandApi={WingLink.SquadAvailable}, " +
                $"SupportSpawning={supportSpawning}, SupportRecon={supportRecon}, SupportOrbitalScan={supportRecon}, DynamicOperations={dynamicOperations}, OperationServices={operationServices}, " +
                $"HighCommand={highCommand}, AutopilotLanding={autopilotLanding}.");

            try
            {
                if (Encyclopedia.i != null)
                {
                    var labels = new List<string>();
                    for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
                    {
                        BuildingDefinition definition = Encyclopedia.i.buildings[i];
                        if (definition != null && definition.buildingType == BuildingType.DEF)
                            labels.Add(definition.jsonKey + " (" + definition.unitName + ")");
                    }
                    string defs = string.Join(", ", labels.ToArray());
                    Plugin.Logger.LogInfo("Vanilla DEF building candidates: " + (string.IsNullOrEmpty(defs) ? "none loaded yet" : defs));
                    Plugin.Logger.LogInfo("Trench native defense seams: spawn=" +
                        (AccessTools.Method(typeof(Spawner), nameof(Spawner.SpawnBuilding)) != null) +
                        ", damage polling=" + (AccessTools.Field(typeof(UnitPart), nameof(UnitPart.hitPoints)) != null));
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogDebug("Deferred building inventory probe: " + e.Message);
            }
        }
    }
}
