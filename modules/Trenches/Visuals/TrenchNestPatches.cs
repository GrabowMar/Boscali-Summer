using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Client half of the nest strip: every peer hides the sandbag ring on a Boscali position
    /// in its own scene. The vanilla part stays registered, so hit indices never desync.
    /// </summary>
    [HarmonyPatch]
    internal static class TrenchNestClientPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Building), "OnStartClient");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Building __instance) => TrenchNestVisual.Strip(__instance);
    }

    /// <summary>
    /// Host half of the nest strip, so the listening host's own scene matches its clients.
    /// </summary>
    [HarmonyPatch]
    internal static class TrenchNestServerPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Building), "OnStartServer");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Building __instance) => TrenchNestVisual.Strip(__instance);
    }
}
