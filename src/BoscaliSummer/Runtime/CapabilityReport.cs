using System;
using System.Linq;
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
            bool capture = AccessTools.Method(typeof(Airbase), "CaptureFaction") != null;
            bool blast = AccessTools.Method(typeof(BlastManager), "AddBlast") != null;
            Plugin.Logger.LogInfo(
                "Capabilities: " +
                $"BulletImpacts={bullet}, MissileImpacts={missile}, " +
                $"MapBuildingHP={GameAccess.MapBuildingHitPointsAvailable}, " +
                $"ScorchMap={blast}, AirbaseCapture={capture}.");

            try
            {
                if (Encyclopedia.i != null)
                {
                    string defs = string.Join(", ", Encyclopedia.i.buildings
                        .Where(x => x != null && x.buildingType == BuildingType.DEF)
                        .Select(x => x.jsonKey + " (" + x.unitName + ")").ToArray());
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
