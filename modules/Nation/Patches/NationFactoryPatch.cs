using BoscaliSummer.Modules.Nation.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Nation.Patches
{
    /// <summary>
    /// A factory the nation layer runs produces through <see cref="NationService"/> only. The game's own production loop
    /// (started by <c>Factory.SetFactory</c>, and it cannot be stopped) keeps firing for mission-placed factories; this
    /// prefix swallows those ticks so nothing is produced twice. A factory the layer does not manage is untouched.
    /// </summary>
    [HarmonyPatch(typeof(Factory), "ProduceUnit")]
    internal static class NationFactoryPatch
    {
        private static bool Prefix(Factory __instance) => !NationService.Manages(__instance);
    }
}
