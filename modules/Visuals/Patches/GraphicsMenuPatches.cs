using System;
using BoscaliSummer.Features.Visuals.Presentation;
using HarmonyLib;

namespace BoscaliSummer.Features.Visuals.Patches
{
    [HarmonyPatch(typeof(GraphicsMenu), "Start")]
    internal static class GraphicsMenuStartPatch
    {
        private static void Postfix(GraphicsMenu __instance)
        {
            GraphicsMenuInjector.Inject(__instance);
        }
    }

    [HarmonyPatch(typeof(GraphicsMenu), "RefreshUI")]
    internal static class GraphicsMenuRefreshPatch
    {
        private static void Postfix(GraphicsMenu __instance)
        {
            GraphicsMenuInjector.Refresh(__instance);
        }
    }
}
