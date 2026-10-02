using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Game
{
    internal static class GameAccess
    {
        private static FieldInfo mapBuildingHitPoints;
        private static AccessTools.FieldRef<MapBuilding, float> mapBuildingHitPointsRef;
        private static FieldInfo mapBuildingSetBuildings;
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> leftMfdButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> rightMfdButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> leftMfdScreensRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> rightMfdScreensRef;
        private static AccessTools.FieldRef<MusicManager, AudioSource> currentMusicSourceRef;
        private static AccessTools.FieldRef<MusicManager, AudioSource> fadeMusicSourceRef;
        private static AccessTools.FieldRef<FactionHQ, List<Radar>> hqRadarsRef;
        private static AccessTools.FieldRef<AIPilotCombatModes, Unit> aiCurrentTargetRef;
        private static AccessTools.FieldRef<AIHeloCombatState, Unit> aiHeloCurrentTargetRef;
        private static AccessTools.FieldRef<Aircraft, Renderer[]> cockpitRenderersRef;
        private static AccessTools.FieldRef<NightVision, bool> nvSelectedRef;
        private static AccessTools.FieldRef<NightVision, bool> nvActiveRef;
        private static AccessTools.FieldRef<RadialMenuMain, RadialMenuAction[]> radialActionsRef;
        private static AccessTools.FieldRef<RadialMenuMain, Aircraft> radialAircraftRef;
        private static Action<RadialMenuMain> radialSetupMain;
        private static FieldInfo radialActionTypeField;
        private static FieldInfo radialIconSpriteField;
        private static FieldInfo radialBackgroundSpriteField;
        private static FieldInfo radialBgInactiveField;
        private static FieldInfo radialBgActiveField;
        private static FieldInfo radialIconImageField;

        public static bool MapBuildingHitPointsAvailable { get; private set; }
        public static bool MapBuildingSetBuildingsAvailable { get; private set; }
        public static bool MfdAvailable { get; private set; }
        public static bool MusicSourcesAvailable { get; private set; }
        public static bool HqSensorsAvailable { get; private set; }
        public static bool AiPilotCombatAvailable { get; private set; }
        public static bool AiHeloCombatAvailable { get; private set; }
        public static bool CockpitRenderersAvailable { get; private set; }
        public static bool NightVisionAvailable { get; private set; }
        public static bool RadialAvailable { get; private set; }
        public static string RadialUnavailableReason { get; private set; }
        public static bool RadialActionAvailable { get; private set; }
        public static string RadialActionUnavailableReason { get; private set; }

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
                mapBuildingSetBuildings = AccessTools.Field(typeof(MapBuildingSet), "mapBuildings");
                MapBuildingSetBuildingsAvailable = mapBuildingSetBuildings != null &&
                    mapBuildingSetBuildings.FieldType == typeof(MapBuilding[]);
            }
            catch (Exception e)
            {
                MapBuildingSetBuildingsAvailable = false;
                Plugin.Logger?.LogWarning("Map building set index access unavailable: " + e.Message);
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

            try
            {
                hqRadarsRef = FieldRef<FactionHQ, List<Radar>>("radars");
                HqSensorsAvailable = true;
            }
            catch (Exception e)
            {
                HqSensorsAvailable = false;
                Plugin.Logger?.LogWarning("HQ sensor arrays access unavailable: " + e.Message);
            }

            try
            {
                aiCurrentTargetRef = FieldRef<AIPilotCombatModes, Unit>("currentTarget");
                AiPilotCombatAvailable = true;
            }
            catch (Exception e)
            {
                AiPilotCombatAvailable = false;
                Plugin.Logger?.LogWarning("AI pilot combat state access unavailable: " + e.Message);
            }

            try
            {
                aiHeloCurrentTargetRef = FieldRef<AIHeloCombatState, Unit>("currentTarget");
                AiHeloCombatAvailable = true;
            }
            catch (Exception e)
            {
                AiHeloCombatAvailable = false;
                Plugin.Logger?.LogWarning("AI helicopter combat state access unavailable: " + e.Message);
            }

            try
            {
                cockpitRenderersRef = FieldRef<Aircraft, Renderer[]>("cockpitRenderers");
                CockpitRenderersAvailable = true;
            }
            catch (Exception e)
            {
                CockpitRenderersAvailable = false;
                Plugin.Logger?.LogWarning("Cockpit renderer access unavailable: " + e.Message);
            }

            try
            {
                nvSelectedRef = FieldRef<NightVision, bool>("nightVisSelected");
                nvActiveRef = FieldRef<NightVision, bool>("nightVisActive");
                NightVisionAvailable = true;
            }
            catch (Exception e)
            {
                NightVisionAvailable = false;
                Plugin.Logger?.LogWarning("Night vision state access unavailable: " + e.Message);
            }

            // The native radial wheel both radial entries (Autopilot's and Wing's) drive:
            // one resolver, before any feature installs.
            try
            {
                radialActionsRef = FieldRef<RadialMenuMain, RadialMenuAction[]>("actionsMain");
                radialAircraftRef = FieldRef<RadialMenuMain, Aircraft>("aircraft");
                MethodInfo setupMain = AccessTools.Method(typeof(RadialMenuMain), "SetupMain") ??
                    throw new MissingMethodException(typeof(RadialMenuMain).FullName, "SetupMain");
                radialSetupMain = AccessTools.MethodDelegate<Action<RadialMenuMain>>(setupMain);
                RadialAvailable = true;
                RadialUnavailableReason = null;
            }
            catch (Exception e)
            {
                RadialAvailable = false;
                RadialUnavailableReason = e.Message;
                Plugin.Logger?.LogWarning("Native radial wheel access unavailable: " + e.Message);
            }

            try
            {
                radialActionTypeField = RequireField(typeof(RadialMenuAction), "actionType");
                radialIconSpriteField = RequireField(typeof(RadialMenuAction), "iconSprite");
                radialBackgroundSpriteField = RequireField(typeof(RadialMenuAction), "backgroundSprite");
                radialBgInactiveField = RequireField(typeof(RadialMenuAction), "backgroundColorInactive");
                radialBgActiveField = RequireField(typeof(RadialMenuAction), "backgroundColorActive");
                radialIconImageField = RequireField(typeof(RadialMenuAction), "iconImage");
                RadialActionAvailable = true;
                RadialActionUnavailableReason = null;
            }
            catch (Exception e)
            {
                RadialActionAvailable = false;
                RadialActionUnavailableReason = e.Message;
                Plugin.Logger?.LogWarning("Native radial wedge access unavailable: " + e.Message);
            }
        }

        public static float GetMapBuildingHitPoints(MapBuilding building)
        {
            if (building != null && mapBuildingHitPointsRef != null)
                return mapBuildingHitPointsRef(building);
            return 100f;
        }

        public static void SetMapBuildingHitPoints(MapBuilding building, float hitPoints)
        {
            if (building != null && mapBuildingHitPointsRef != null)
                mapBuildingHitPointsRef(building) = hitPoints;
        }

        public static bool TryGetMapBuilding(MapBuildingSet set, int index, out MapBuilding building)
        {
            building = null;
            if (set == null || !MapBuildingSetBuildingsAvailable) return false;
            MapBuilding[] buildings;
            try { buildings = mapBuildingSetBuildings.GetValue(set) as MapBuilding[]; }
            catch { return false; }
            if (buildings == null || index < 0 || index >= buildings.Length) return false;
            building = buildings[index];
            return building != null;
        }

        public static List<Button> GetLeftMfdButtons(VirtualMFD mfd) => leftMfdButtonsRef(mfd);
        public static List<Button> GetRightMfdButtons(VirtualMFD mfd) => rightMfdButtonsRef(mfd);
        public static List<MFDScreen> GetLeftMfdScreens(VirtualMFD mfd) => leftMfdScreensRef(mfd);
        public static List<MFDScreen> GetRightMfdScreens(VirtualMFD mfd) => rightMfdScreensRef(mfd);
        public static AudioSource GetCurrentMusicSource(MusicManager music) =>
            currentMusicSourceRef == null || music == null ? null : currentMusicSourceRef(music);
        public static AudioSource GetFadeMusicSource(MusicManager music) =>
            fadeMusicSourceRef == null || music == null ? null : fadeMusicSourceRef(music);

        public static List<Radar> GetHqRadars(FactionHQ hq) =>
            hqRadarsRef == null || hq == null ? null : hqRadarsRef(hq);

        public static Unit GetAiCurrentTarget(AIPilotCombatModes modes) =>
            aiCurrentTargetRef == null || modes == null ? null : aiCurrentTargetRef(modes);

        public static void SetAiCurrentTarget(AIPilotCombatModes modes, Unit target)
        {
            if (aiCurrentTargetRef != null && modes != null) aiCurrentTargetRef(modes) = target;
        }

        public static Unit GetAiHeloCurrentTarget(AIHeloCombatState modes) =>
            aiHeloCurrentTargetRef == null || modes == null ? null : aiHeloCurrentTargetRef(modes);

        public static void SetAiHeloCurrentTarget(AIHeloCombatState modes, Unit target)
        {
            if (aiHeloCurrentTargetRef != null && modes != null) aiHeloCurrentTargetRef(modes) = target;
        }

        public static RadialMenuAction[] GetRadialActions(RadialMenuMain menu) => radialActionsRef(menu);
        public static void SetRadialActions(RadialMenuMain menu, RadialMenuAction[] value) => radialActionsRef(menu) = value;
        public static Aircraft GetRadialAircraft(RadialMenuMain menu) => radialAircraftRef(menu);
        public static void InvokeRadialSetupMain(RadialMenuMain menu) => radialSetupMain(menu);

        /// <summary>Wedge appearance reads and writes. Each fails closed on its own field
        /// so one missing member blanks one copy, never the entry.</summary>
        public static RadialMenuAction.ActionType GetRadialActionType(RadialMenuAction action) =>
            radialActionTypeField == null || action == null ? default :
            (RadialMenuAction.ActionType)radialActionTypeField.GetValue(action);

        public static void SetRadialActionType(RadialMenuAction action, RadialMenuAction.ActionType type)
        {
            radialActionTypeField?.SetValue(action, type);
        }

        public static void SetRadialIconSprite(RadialMenuAction action, Sprite sprite)
        {
            radialIconSpriteField?.SetValue(action, sprite);
        }

        public static Image GetRadialIconImage(RadialMenuAction action) =>
            radialIconImageField == null || action == null ? null : radialIconImageField.GetValue(action) as Image;

        /// <summary>Copy native wedge sprites and colours; newly created actions otherwise
        /// have null sprites and transparent colours. The action type is the caller's.</summary>
        public static void CopyRadialAppearance(RadialMenuAction target, RadialMenuAction template)
        {
            if (target == null || template == null) return;
            if (radialIconSpriteField != null)
                radialIconSpriteField.SetValue(target, radialIconSpriteField.GetValue(template));
            if (radialBackgroundSpriteField != null)
                radialBackgroundSpriteField.SetValue(target, radialBackgroundSpriteField.GetValue(template));
            if (radialBgInactiveField != null)
                radialBgInactiveField.SetValue(target, radialBgInactiveField.GetValue(template));
            if (radialBgActiveField != null)
                radialBgActiveField.SetValue(target, radialBgActiveField.GetValue(template));
        }

        /// <summary>
        /// The prefab-assigned first-person cockpit renderers (toggled by
        /// <c>Aircraft.SetCockpitRenderers</c>), or null when the seam is unavailable.
        /// Readable even while disabled, so the glow can bind outside cockpit view.
        /// </summary>
        public static Renderer[] GetCockpitRenderers(Aircraft aircraft) =>
            cockpitRenderersRef == null || aircraft == null ? null : cockpitRenderersRef(aircraft);

        /// <summary>
        /// Vanilla night-vision state without patching anything: selected is user intent,
        /// active is the faded-in state. Either means goggles are on (covers transitions).
        /// False when the seam is unavailable — callers fail closed to vanilla NV.
        /// </summary>
        public static bool TryGetNightVisionState(out bool selected, out bool active)
        {
            selected = active = false;
            NightVision nv = NightVision.i;
            if (nv == null || nvSelectedRef == null || nvActiveRef == null) return false;
            selected = nvSelectedRef(nv);
            active = nvActiveRef(nv);
            return true;
        }

        public static bool NightVisionOn()
        {
            if (!TryGetNightVisionState(out bool selected, out bool active)) return false;
            return selected || active;
        }

        /// <summary>
        /// Automation only: select vanilla night vision directly, bypassing the cursor-gated
        /// <c>NightVision.Toggle</c> (unattended runs hold UI open). The game's own Update still
        /// owns the fade and volume swap; production code never calls this.
        /// </summary>
        internal static void DebugSelectNightVision(bool selected)
        {
            NightVision nv = NightVision.i;
            if (nv == null || nvSelectedRef == null) return;
            nvSelectedRef(nv) = selected;
        }

        public static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }

        /// <summary>The local player's faction HQ, only once it has a faction assigned.</summary>
        public static bool TryGetLocalFaction(out FactionHQ hq)
        {
            hq = null;
            if (!GameManager.GetLocalHQ(out FactionHQ local) || local == null || local.faction == null)
                return false;
            hq = local;
            return true;
        }

        private static AccessTools.FieldRef<TInstance, TField> FieldRef<TInstance, TField>(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(TInstance), name) ??
                throw new MissingFieldException(typeof(TInstance).FullName, name);
            return AccessTools.FieldRefAccess<TInstance, TField>(field);
        }

        private static FieldInfo RequireField(Type type, string name) =>
            AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
    }
}
