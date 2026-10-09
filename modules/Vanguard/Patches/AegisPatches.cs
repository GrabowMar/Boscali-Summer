using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // AEGIS-3 SNAP: the AEGIS station's trigger (player input and AI Pilot.Fire both land here, on the simulating peer)
    // is judged by AegisTrigger instead of vanilla's fire-all / salvo branches: one dart per tap at the locked air unit.
    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.Fire))]
    internal static class AegisSnapPatch
    {
        private static bool Prefix(WeaponManager __instance, Aircraft ___aircraft)
        {
            WeaponStation station = __instance.currentWeaponStation;
            if (___aircraft == null || !AegisTrigger.IsAegis(station)) return true;
            var targets = __instance.GetTargetList();
            AegisTrigger.Press(___aircraft, station, targets.Count > 0 ? targets[0] : null);
            return false;
        }
    }

    // LaunchMount runs on every peer (owner, server Cmd, client Rpc replay): the pod's cell covers follow from there.
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.LaunchMount))]
    internal static class AegisDoorsPatch
    {
        private static void Postfix(WeaponStation __instance)
        {
            if (AegisTrigger.IsAegis(__instance)) AegisDoors.Attach(__instance);
        }
    }

    // StartMissile runs on every peer: the released dart's grid fins snap open from its age.
    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class AegisFinsPatch
    {
        private static void Postfix(Missile __instance)
        {
            if (__instance != null && __instance.definition != null) AegisFins.Attach(__instance);
        }
    }
}
