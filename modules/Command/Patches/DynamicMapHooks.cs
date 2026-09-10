using System;
using HarmonyLib;

namespace BoscaliSummer.Features.Command.Patches
{
    [HarmonyPatch(typeof(DynamicMap), "Maximize")]
    internal static class DynamicMapMaximizePatch
    {
        public static event Action OnMaximized;

        private static void Postfix()
        {
            OnMaximized?.Invoke();
        }
    }

    [HarmonyPatch(typeof(DynamicMap), "Minimize")]
    internal static class DynamicMapMinimizePatch
    {
        public static event Action OnMinimized;

        private static void Postfix()
        {
            OnMinimized?.Invoke();
        }
    }
}
