using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Autopilot.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Autopilot.Patches
{
    /// <summary>The chaff dispenser is one more stop of the game's Next Countermeasure cycle on the local aircraft (GitHub issue 10).</summary>
    [HarmonyPatch(typeof(CountermeasureManager), nameof(CountermeasureManager.NextCountermeasure))]
    internal static class ChaffNextPatch
    {
        private static bool Prefix(CountermeasureManager __instance)
        {
            try
            {
                ChaffDispenser chaff = ChaffDispenser.Instance;
                return chaff == null || !chaff.TakeNext(__instance);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Autopilot.ChaffNext", e);
                return true;
            }
        }
    }

    /// <summary>While chaff is the selected countermeasure the countermeasure button runs the dispenser; the game's own trigger stays down.</summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Countermeasures))]
    internal static class ChaffTriggerPatch
    {
        private static bool Prefix(Aircraft __instance, bool active)
        {
            try
            {
                ChaffDispenser chaff = ChaffDispenser.Instance;
                return chaff == null || !chaff.TakeTrigger(__instance, active);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Autopilot.ChaffTrigger", e);
                return true;
            }
        }
    }

    /// <summary>Every flare (any aircraft's) burns bigger and stays a few pixels on screen at range: easier to spot (2026-10-27). Looks only.</summary>
    [HarmonyPatch(typeof(IRFlare), nameof(IRFlare.LaunchFlare))]
    internal static class FlareLookPatch
    {
        private static readonly AccessTools.FieldRef<IRFlare, UnityEngine.ParticleSystem> Core =
            AccessTools.FieldRefAccess<IRFlare, UnityEngine.ParticleSystem>("flareParticles");

        private static void Postfix(IRFlare __instance)
        {
            try { ChaffVisuals.BrightenFlare(Core(__instance)); }
            catch (Exception e) { PatchGuard.Report("Autopilot.FlareLook", e); }
        }
    }

    /// <summary>Everything that asks the game which countermeasure is active (its cockpit indicator, the TAC BOARD, the C menu) hears chaff while it is selected.</summary>
    [HarmonyPatch(typeof(CountermeasureManager), nameof(CountermeasureManager.GetActiveCountermeasure))]
    internal static class ChaffActivePatch
    {
        private static void Postfix(CountermeasureManager __instance, ref Countermeasure __result)
        {
            Countermeasure chaff = ChaffDispenser.Instance?.SelectedFor(__instance);
            if (chaff != null) __result = chaff;
        }
    }
}
