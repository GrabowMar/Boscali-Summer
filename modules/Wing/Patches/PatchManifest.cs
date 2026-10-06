using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Patches
{
    /// <summary>The game methods the Wing patch classes (<c>WingModule.Patches</c>) must patch. Harmony skips
    /// a class without a class-level [HarmonyPatch] silently, so startup compares what was patched with
    /// what is expected and warns on every gap.</summary>
    internal static class PatchManifest
    {
        /// <summary>"DeclaringType.Method" names that must be patched once <c>WingModule.Patches</c> is applied.</summary>
        internal static readonly string[] Expected =
        {
            "RadialMenuAction.AllowedOnAircraft",
            "RadialMenuAction.TriggerAction",
            "RadialMenuAction.Flash",
            "RadialMenuMain.SetupMain",
            "RadialMenuMain.OpenMenu",
            "RadialMenuMain.OnDestroy",
            "SceneSingleton`1.Awake",
            "PilotPlayerState.PlayerAxisControls",
            "PilotPlayerState.PlayerThrottleAxis1Controls",
            "Runway.IsAvailableForTakeoff",
            "Aircraft.StartEjectionSequence",
            "Pilot.SwitchState",
            "AIPilotLandingState.TouchedDown",
            "Pilot.ApplyDamage",
            "Missile.SetAimpoint",
            "FlareEjector.Fire",
            "RadarJammer.Fire",
            "PilotDismounted.OnStartServer",
            "PilotDismounted.UnitDisabled",
            "PilotDismounted.SetPilotState",
            "PilotDismounted.Capture",
            "Aircraft.UserCode_RpcJettisonCanopy_1196305304",
            "GameManager.FinishGame",
            "CombatAI.ChooseHQTarget",
            "DynamicMap.MapControls",
            "UnitMapIcon.ClickIcon",
            "MapIcon.UpdateColor",
            "UnitMapIcon.UnitMapIcon_UpdateColor",
            "UnitMapIcon.SetIcon",
            "UnitMapIcon.UpdateIcon",
            "DynamicMap.ShouldShowAirbase",
            "HUDUnitMarker.UpdateColor",
        };

        /// <summary>The gap-check after BoscaliMod applies <c>WingModule.Patches</c>: read back what the Wing Harmony id patched and warn on every gap.
        /// Only warns; a failed inventory check never disables the wing.</summary>
        internal static void Verify()
        {
            try
            {
                // Same id BoscaliMod builds for this module ("wing" is WingModule's id).
                var harmony = new Harmony(Plugin.PluginGuid + ".module.wing");
                var names = new List<string>();
                foreach (MethodBase m in harmony.GetPatchedMethods())
                {
                    if (m != null) names.Add(m.DeclaringType?.Name + "." + m.Name);
                }
                names.Sort(StringComparer.Ordinal);
                WingLog.Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.PatchesInstalled, names.Count));
                WingLog.Verbose($"Harmony patched {names.Count} method(s) for Wing: {string.Join(", ", names)}");
                foreach (string want in Expected)
                {
                    if (!names.Contains(want)) WingLog.Logger.LogWarning($"Expected Harmony patch missing: {want}");
                }
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Patches] Wing patch inventory check failed: " + e.Message);
            }
        }
    }
}
