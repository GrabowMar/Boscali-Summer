using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using CoreGameAccess = BoscaliSummer.Core.Game.GameAccess;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Wing-owned private game access (landing destinations, hangar spawns) plus
    /// the wing's view of the shared seams: the radial wheel, its wedges and the MFD
    /// resolve once in Infrastructure GameAccess, and this class only re-exposes them so
    /// wing call sites keep their shape. Local application policy blocks publicizer
    /// tasks. Resolve at startup and disable unavailable integrations instead of
    /// throwing each frame.</summary>
    internal static partial class GameAccess
    {
        /// <summary>Whether the native radial wheel and wedge seams resolved.</summary>
        public static bool Available { get; private set; }

        public static string UnavailableReason { get; private set; }

        /// <summary>Whether native MFD internals resolved for the WMC screen.</summary>
        public static bool MfdAvailable => CoreGameAccess.MfdAvailable;

        // Read the native landing state's chosen airbase; a guessed nearest base may differ from the
        // actual destination.
        private static AccessTools.FieldRef<AIPilotLandingState, Airbase> landingAirbaseRef;
        private static AccessTools.FieldRef<AIHeloLandingState, Airbase.VerticalLandingPoint>
            heloLandingPointRef;

        /// <summary>Whether native landing destinations are readable.</summary>
        public static bool LandingDestinationAvailable { get; private set; }

        // Read the private spawned prefab immediately after TrySpawnAircraft, before registry
        // discovery.
        private static AccessTools.FieldRef<Hangar, GameObject> hangarSpawnedObjectRef;

        /// <summary>Whether the hangar's spawned object is readable.</summary>
        public static bool HangarSpawnAvailable { get; private set; }

        public static void Initialise()
        {
            Available = CoreGameAccess.RadialAvailable && CoreGameAccess.RadialActionAvailable;
            UnavailableReason = !CoreGameAccess.RadialAvailable
                ? CoreGameAccess.RadialUnavailableReason
                : CoreGameAccess.RadialActionUnavailableReason;
            if (!Available)
            {
                WingLog.Logger.LogWarning(
                    "Native radial menu integration unavailable (" + UnavailableReason +
                    "). Falling back to the standalone wheel; bind Keys/FallbackRadialMenu to use it.");
            }

            // If landing reflection fails, omit the RTB map line.
            try
            {
                landingAirbaseRef = Field<AIPilotLandingState, Airbase>("airbase");
                heloLandingPointRef =
                    Field<AIHeloLandingState, Airbase.VerticalLandingPoint>("landingPoint");
                LandingDestinationAvailable = true;
            }
            catch (Exception landing)
            {
                LandingDestinationAvailable = false;
                WingLog.Logger.LogWarning(
                    "Landing destination unreadable (" + landing.Message +
                    "). RTB will not be drawn on the map.");
            }

            try
            {
                hangarSpawnedObjectRef = Field<Hangar, GameObject>("spawnedObject");
                HangarSpawnAvailable = true;
            }
            catch (Exception hangar)
            {
                HangarSpawnAvailable = false;
                WingLog.Logger.LogWarning(
                    "Hangar spawn unreadable (" + hangar.Message +
                    "). Hangar deliveries will wait for the unit registry.");
            }

            // Optional and independent: flight fields for profiles, wind and the player autopilot.
            InitialiseFlight();
        }

        private static AccessTools.FieldRef<TClass, TField> Field<TClass, TField>(string name)
        {
            FieldInfo info = Require(AccessTools.Field(typeof(TClass), name),
                                     typeof(TClass).Name + "." + name);
            return AccessTools.FieldRefAccess<TClass, TField>(info);
        }

        private static T Require<T>(T member, string description) where T : class
        {
            if (member == null) throw new MissingMemberException("could not resolve " + description);
            return member;
        }

        // Radial accessors (shared seam).

        public static RadialMenuAction[] GetActionsMain(RadialMenuMain menu) =>
            CoreGameAccess.GetRadialActions(menu);

        public static void SetActionsMain(RadialMenuMain menu, RadialMenuAction[] value) =>
            CoreGameAccess.SetRadialActions(menu, value);

        public static Aircraft GetMenuAircraft(RadialMenuMain menu) =>
            CoreGameAccess.GetRadialAircraft(menu);

        public static void SetupMain(RadialMenuMain menu) => CoreGameAccess.InvokeRadialSetupMain(menu);

        // MFD accessors (shared seam).

        public static List<Button> GetLeftButtons(VirtualMFD mfd) => CoreGameAccess.GetLeftMfdButtons(mfd);
        public static List<Button> GetRightButtons(VirtualMFD mfd) => CoreGameAccess.GetRightMfdButtons(mfd);
        public static List<MFDScreen> GetLeftScreens(VirtualMFD mfd) => CoreGameAccess.GetLeftMfdScreens(mfd);
        public static List<MFDScreen> GetRightScreens(VirtualMFD mfd) => CoreGameAccess.GetRightMfdScreens(mfd);

        // Landing accessors.

        /// <summary>The field the game's jet landing state picked (null: none or unreadable).</summary>
        public static bool TryGetLandingAirbase(AIPilotLandingState state, out Airbase airbase)
        {
            airbase = null;
            if (!LandingDestinationAvailable || state == null) return false;
            try { airbase = landingAirbaseRef(state); }
            catch { return false; }
            return airbase != null;
        }

        /// <summary>The pad the game's helicopter landing state picked (null: none or unreadable).</summary>
        public static bool TryGetLandingPad(AIHeloLandingState state, out Airbase.VerticalLandingPoint pad)
        {
            pad = null;
            if (!LandingDestinationAvailable || state == null) return false;
            try { pad = heloLandingPointRef(state); }
            catch { return false; }
            return pad != null;
        }

        public static GameObject GetHangarSpawnedObject(Hangar hangar)
        {
            if (!HangarSpawnAvailable || hangar == null) return null;
            try { return hangarSpawnedObjectRef(hangar); }
            catch { return null; }
        }

        // Radial action accessors (shared seam).

        public static void SetActionType(RadialMenuAction action, RadialMenuAction.ActionType type) =>
            CoreGameAccess.SetRadialActionType(action, type);

        public static Image GetIconImage(RadialMenuAction action) =>
            CoreGameAccess.GetRadialIconImage(action);

        public static void SetIconSprite(RadialMenuAction action, Sprite sprite) =>
            CoreGameAccess.SetRadialIconSprite(action, sprite);

        /// <summary>Copy native sprites and colours; newly created actions otherwise have null sprites and
        /// transparent colours.</summary>
        public static void CopyAppearance(RadialMenuAction target, RadialMenuAction template) =>
            CoreGameAccess.CopyRadialAppearance(target, template);
    }
}
