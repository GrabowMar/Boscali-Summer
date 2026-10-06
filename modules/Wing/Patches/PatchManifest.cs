using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
namespace BoscaliSummer.Modules.Wing.Patches
{
    /// <summary>The single list of Harmony patch classes and the game methods they must patch. Harmony skips
    /// a class without a class-level [HarmonyPatch] silently, so startup compares what was patched with
    /// what is expected and warns on every gap. BoscaliMod applies <see cref="PatchTypes"/> through the
    /// Wing Harmony id; <see cref="Verify"/> runs the gap-check afterwards.</summary>
    internal static class PatchManifest
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(WingMenuActionPatches),
            typeof(WingRadialMenuPatches),
            typeof(WingRadialMenuPatches.AwakePatch),
            typeof(PlayerAutopilotPatches),
            typeof(RunwayLockPatch),
            typeof(EjectGuard),
            typeof(SwitchStateGuard),
            typeof(BounceGuard),
            typeof(WingPilotFatalDamagePatch),
            typeof(WingPilotKillerPatch),
            typeof(WingKillMessagePatch),
            typeof(WingLuckPatch),
            typeof(WingFlareReflexPatch),
            typeof(WingEcmSpecialistPatch),
            typeof(WingSurvivorSpawnPatch),
            typeof(WingSurvivorReturnPatch),
            typeof(WingSurvivorDeathPatch),
            typeof(WingSurvivorCapturePatch),
            typeof(WingTakeoverPatches),
            typeof(WingTargetPatch),
            typeof(WingSquad.SurvivorSpawnPatch),
            typeof(WingSquad.SurvivorStatePatch),
            typeof(WingSquad.SurvivorDisabledPatch),
            typeof(WingSquad.SurvivorCapturePatch),
            typeof(WingSquad.AceTargetPatch),
            typeof(WmcMapControlsPatch),
            typeof(WmcMapSelection.ClickIconPatch),
            typeof(WingMapTint.MapIconColorPatch),
            typeof(WingMapTint.ShowAirbasePatch),
            typeof(WingHudTint.UpdateColorPatch),
        };

        /// <summary>"DeclaringType.Method" names that must be patched after <see cref="Apply"/>.</summary>
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

        internal static void Apply(Harmony harmony, ManualLogSource log)
        {
            for (int i = 0; i < PatchTypes.Length; i++)
            {
                try
                {
                    harmony.PatchAll(PatchTypes[i]);
                }
                catch (Exception e)
                {
                    log.LogError($"[Patches] {PatchTypes[i].Name} failed to apply: {e.Message}");
                }
            }

            var names = new List<string>();
            foreach (MethodBase m in harmony.GetPatchedMethods())
            {
                if (m != null) names.Add(m.DeclaringType?.Name + "." + m.Name);
            }
            names.Sort(StringComparer.Ordinal);
            log.LogInfo(new WingDiagnostic(WingDiagnosticEvent.PatchesInstalled, names.Count));
            WingLog.Verbose($"Harmony patched {names.Count} method(s): {string.Join(", ", names)}");

            foreach (string want in Expected)
            {
                if (!names.Contains(want)) log.LogWarning($"Expected Harmony patch missing: {want}");
            }
        }

        /// <summary>The gap-check half of <see cref="Apply"/> for the BoscaliMod flow, which
        /// owns patching: read back what the Wing Harmony id patched and warn on every gap.
        /// Only warns; a failed inventory check never disables the wing.</summary>
        internal static void Verify(ManualLogSource log)
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
                log.LogInfo(new WingDiagnostic(WingDiagnosticEvent.PatchesInstalled, names.Count));
                WingLog.Verbose($"Harmony patched {names.Count} method(s) for Wing: {string.Join(", ", names)}");
                foreach (string want in Expected)
                {
                    if (!names.Contains(want)) log.LogWarning($"Expected Harmony patch missing: {want}");
                }
            }
            catch (Exception e)
            {
                log.LogWarning("[Patches] Wing patch inventory check failed: " + e.Message);
            }
        }
    }
}
