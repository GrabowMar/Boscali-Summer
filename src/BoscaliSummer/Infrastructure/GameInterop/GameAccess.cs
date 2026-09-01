using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Runtime
{
    internal static class GameAccess
    {
        private static FieldInfo mapBuildingHitPoints;
        private static AccessTools.FieldRef<MapBuilding, float> mapBuildingHitPointsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> leftMfdButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> rightMfdButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> leftMfdScreensRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> rightMfdScreensRef;
        private static AccessTools.FieldRef<MusicManager, AudioSource> currentMusicSourceRef;
        private static AccessTools.FieldRef<MusicManager, AudioSource> fadeMusicSourceRef;

        public static bool MapBuildingHitPointsAvailable { get; private set; }
        public static bool MfdAvailable { get; private set; }
        public static bool MusicSourcesAvailable { get; private set; }

        public static void Initialise()
        {
            try
            {
                mapBuildingHitPoints = AccessTools.Field(typeof(MapBuilding), "hitPoints");
                if (mapBuildingHitPoints != null)
                    mapBuildingHitPointsRef =
                        AccessTools.FieldRefAccess<MapBuilding, float>(mapBuildingHitPoints);
                MapBuildingHitPointsAvailable = mapBuildingHitPointsRef != null;
            }
            catch (Exception e)
            {
                MapBuildingHitPointsAvailable = false;
                Plugin.Logger?.LogWarning("Map building HP access unavailable: " + e.Message);
            }

            try
            {
                leftMfdButtonsRef = FieldRef<VirtualMFD, List<Button>>("leftButtons");
                rightMfdButtonsRef = FieldRef<VirtualMFD, List<Button>>("rightButtons");
                leftMfdScreensRef = FieldRef<VirtualMFD, List<MFDScreen>>("leftScreens");
                rightMfdScreensRef = FieldRef<VirtualMFD, List<MFDScreen>>("rightScreens");
                MfdAvailable = true;
            }
            catch (Exception e)
            {
                MfdAvailable = false;
                Plugin.Logger?.LogWarning("Radio MFD access unavailable: " + e.Message);
            }

            try
            {
                currentMusicSourceRef = FieldRef<MusicManager, AudioSource>("currentSource");
                fadeMusicSourceRef = FieldRef<MusicManager, AudioSource>("fadeSource");
                MusicSourcesAvailable = true;
            }
            catch (Exception e)
            {
                MusicSourcesAvailable = false;
                Plugin.Logger?.LogWarning("Vanilla music ownership access unavailable: " + e.Message);
            }
        }

        public static float GetMapBuildingHitPoints(MapBuilding building)
        {
            if (building != null && mapBuildingHitPointsRef != null)
                return mapBuildingHitPointsRef(building);
            return 100f;
        }

        public static List<Button> GetLeftMfdButtons(VirtualMFD mfd) => leftMfdButtonsRef(mfd);
        public static List<Button> GetRightMfdButtons(VirtualMFD mfd) => rightMfdButtonsRef(mfd);
        public static List<MFDScreen> GetLeftMfdScreens(VirtualMFD mfd) => leftMfdScreensRef(mfd);
        public static List<MFDScreen> GetRightMfdScreens(VirtualMFD mfd) => rightMfdScreensRef(mfd);
        public static AudioSource GetCurrentMusicSource(MusicManager music) =>
            currentMusicSourceRef == null || music == null ? null : currentMusicSourceRef(music);
        public static AudioSource GetFadeMusicSource(MusicManager music) =>
            fadeMusicSourceRef == null || music == null ? null : fadeMusicSourceRef(music);

        private static AccessTools.FieldRef<TInstance, TField> FieldRef<TInstance, TField>(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(TInstance), name) ??
                throw new MissingFieldException(typeof(TInstance).FullName, name);
            return AccessTools.FieldRefAccess<TInstance, TField>(field);
        }
    }
}
