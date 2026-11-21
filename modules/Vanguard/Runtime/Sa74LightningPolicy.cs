using System.Reflection;
using BoscaliSummer.Core.Contracts;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>The real nuclear-induced lightning belongs to the fireball, not the winter sky. Ordinary
    /// storm Lightning would keep striking every few seconds once the darkened sky makes it visible, so it
    /// stays silent for the rest of the mission even when the Weather module is disabled.</summary>
    [HarmonyPatch(typeof(Lightning), "Update")]
    internal static class Sa74LightningPatch
    {
        private static readonly FieldInfo Flash = AccessTools.Field(typeof(Lightning), "flashLight");

        private static bool Prefix(Lightning __instance)
        {
            if (!NuclearAftermath.Active) return true;
            if (Flash?.GetValue(__instance) is Light flash && flash.enabled) flash.enabled = false;
            return false;
        }
    }
}
