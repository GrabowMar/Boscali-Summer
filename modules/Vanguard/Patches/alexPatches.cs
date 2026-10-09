using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // ALE-X: the fire press. The trigger is a held button (WeaponManager.Fire runs every frame it is down) and the pod's
    // last decoy has Ammo 0, so the press is taken here, before the Ready() gate: launch, cut, or swallow the rest of the hold.
    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.Fire))]
    internal static class AlexPressPatch
    {
        private static bool Prefix(WeaponManager __instance, Aircraft ___aircraft) =>
            !AlexFire.Press(___aircraft, __instance.currentWeaponStation);
    }

    // The AI and sim launch the pod through LaunchMount directly; a launch while a decoy is out is a cut there too.
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.LaunchMount))]
    internal static class AlexLaunchPatch
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
