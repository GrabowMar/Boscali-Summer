using System.Reflection;
using BoscaliSummer.Modules.Weather.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Patches
{
    /// <summary>
    /// A heat-seeker cannot see through cloud: while its target aircraft is inside cloud, each native lock check
    /// (four a second) may break the lock the same way terrain does. Radar seekers are untouched.
    /// </summary>
    [HarmonyPatch(typeof(IRSeeker), "IRLockCheck")]
    internal static class CloudSeekerPatch
    {
        // About 40 % per second inside cloud; a two-second dip shakes roughly two missiles in three.
        internal const float BreakChancePerCheck = 0.12f;

        private static readonly MethodInfo loseLock = AccessTools.Method(typeof(IRSeeker), "LoseLock");

        [HarmonyPostfix]
        private static void Postfix(IRSeeker __instance, Missile ___missile, Unit ___targetUnit, ref bool __result)
        {
            if (!__result || ___missile == null || !___missile.IsServer || !___missile.LocalSim ||
                !(___targetUnit is Aircraft aircraft) || aircraft.disabled) return;
            WeatherManager weather = WeatherManager.Live;
            if (weather == null || !weather.InCloudAt(aircraft.GlobalPosition()) ||
                Random.value >= BreakChancePerCheck) return;
            loseLock.Invoke(__instance, null);
            __result = false;   // native Seek then drops the heat source and the missile drifts
        }
    }
}
