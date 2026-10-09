using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // ALE-X: a fire press on the pod while a decoy is on the fiber cuts it instead of launching the next one.
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.LaunchMount))]
    internal static class AlexLaunchPatch
    {
        private static bool Prefix(WeaponStation __instance, Unit owner) => !AlexFire.Intercept(__instance, owner);
    }

    // Stations whose WeaponInfo.fireInterval is 0 fire through WeaponStation.Fire instead of LaunchMount.
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.Fire))]
    internal static class AlexFirePatch
    {
        private static bool Prefix(WeaponStation __instance, Unit owner) => !AlexFire.Intercept(__instance, owner);
    }

    // ALE-X DEFLECT: the radar seekers write their aimpoint every Seek; the postfix moves it toward the decoy.
    [HarmonyPatch(typeof(ARHSeeker), nameof(ARHSeeker.Seek))]
    internal static class AlexDeflectArhPatch
    {
        private static void Postfix(Missile ___missile) => AlexDeflect.Apply(___missile);
    }

    [HarmonyPatch(typeof(SARHSeeker), nameof(SARHSeeker.Seek))]
    internal static class AlexDeflectSarhPatch
    {
        private static void Postfix(Missile ___missile) => AlexDeflect.Apply(___missile);
    }

    // ALE-X SUPPRESS: the only lock-time gate in the game is a turret's timeOnTarget > lockTime before it fires.
    // While a kite is out, time on a towing host accrues at 1/SuppressSlow, so the lock takes 50 % longer.
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    internal static class AlexSuppressPatch
    {
        private static readonly AccessTools.FieldRef<Turret, Unit> Target = AccessTools.FieldRefAccess<Turret, Unit>("target");
        private static readonly AccessTools.FieldRef<Turret, float> TimeOnTarget = AccessTools.FieldRefAccess<Turret, float>("timeOnTarget");

        private static void Postfix(Turret __instance)
        {
            if (AlexSuppress.Count == 0) return;
            Unit target = Target(__instance);
            if (!AlexSuppress.Applies(target)) return;
            ref float held = ref TimeOnTarget(__instance);
            held = Mathf.Max(0f, held - AlexKite.SuppressRefund(Time.deltaTime));
            AlexSuppress.Touch(target);
        }
    }
}
