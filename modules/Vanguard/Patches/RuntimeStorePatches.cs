using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // A hidden pod (STORMCELL, WATCHTOWER) feeds the aircraft's own systems: no weapon station, no selector entry, no HUD
    // line. The game would make a station for every Weapon in a mount prefab, so attach it directly instead (the pattern
    // the AryxWeaponryPack uses for its flare and targeting pods). Runs on every peer, for every mount spawn.
    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.RegisterWeapon))]
    internal static class HiddenPodRegisterPatch
    {
        private static bool Prefix(Weapon weapon, WeaponMount weaponMount, Hardpoint hardpoint, Aircraft ___aircraft)
        {
            if (!(weapon is IHiddenPod)) return true;
            try
            {
                if (___aircraft != null && hardpoint != null) weapon.AttachToHardpoint(___aircraft, hardpoint, weaponMount);
            }
            catch (Exception error) { PatchGuard.Report("Vanguard.HiddenPodRegister", error); }
            return false;
        }
    }

    // Vanilla AI loadouts roll a random legal option per station (WeaponManager.SelectAIAircraftWeapons, its only caller of this
    // filter): keep the support stores (TORCH, STORMCELL, WATCHTOWER) and the two-station MANTA, whose partner rule the AI path
    // never checks, off them. SUNSPEAR stays: the AI fires lasers.
    [HarmonyPatch(typeof(WeaponChecker), nameof(WeaponChecker.PreferNukesFilter))]
    internal static class AiLoadoutFilterPatch
    {
        private static void Postfix(List<WeaponMount> listToFilter)
        {
            try { listToFilter?.RemoveAll(RuntimeStores.AiSkips); }
            catch (Exception error) { PatchGuard.Report("Vanguard.AiLoadoutFilter", error); }
        }
    }
}
