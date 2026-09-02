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
            Plugin.Logger.LogInfo(
                "Capabilities: " +
                $"BulletImpacts={bullet}, MissileImpacts={missile}, VehicleLosses={vehicle}, " +
                $"MapBuildingHP={GameAccess.MapBuildingHitPointsAvailable}, " +
                $"ScorchMap={blast}, FacadeScorch={scorchDecal}, AirbaseCapture={capture}, " +
                $"RadioMFD={GameAccess.MfdAvailable}, MusicMixer={musicManager}, " +
                $"MusicOwnership={GameAccess.MusicSourcesAvailable}, " +
                $"SoundtrackCatalog={soundtrackCatalog}, Progression={progression}, " +
                $"SupportSpawning={supportSpawning}.");

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
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogDebug("Deferred building inventory probe: " + e.Message);
            }
        }
    }
}
