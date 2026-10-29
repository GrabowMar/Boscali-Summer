using System;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Modules.Progression.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Progression.Patches
{
    /// <summary>Range map limits engine demand after the native control filter.</summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.FilterInputs))]
    internal static class AircraftEngineMapPatch
    {
        private static void Postfix(Aircraft __instance)
        {
            try
            {
                ProgressionManager manager = ProgressionRuntime.Active;
                if (manager == null || !manager.HasTunes || __instance == null) return;
                float limit = PlaneEngineMap.ThrottleCeiling(manager.TuneFor(__instance));
                if (limit >= 1f) return;
                ControlInputs inputs = __instance.GetInputs();
                if (inputs != null && inputs.throttle > limit) inputs.throttle = limit;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Progression.EngineMap", e);
            }
        }
    }
}
