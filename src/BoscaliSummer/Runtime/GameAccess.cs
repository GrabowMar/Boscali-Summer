using System;
using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Runtime
{
    internal static class GameAccess
    {
        private static FieldInfo mapBuildingHitPoints;

        public static bool MapBuildingHitPointsAvailable { get; private set; }

        public static void Initialise()
        {
            try
            {
                mapBuildingHitPoints = AccessTools.Field(typeof(MapBuilding), "hitPoints");
                MapBuildingHitPointsAvailable = mapBuildingHitPoints != null;
            }
            catch (Exception e)
            {
                MapBuildingHitPointsAvailable = false;
                Plugin.Logger?.LogWarning("Map building HP access unavailable: " + e.Message);
            }
        }

        public static float GetMapBuildingHitPoints(MapBuilding building)
        {
            if (mapBuildingHitPoints?.GetValue(building) is float value) return value;
            return 100f;
        }
    }
}
