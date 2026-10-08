using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Patches
{
    /// <summary>The shot tracker's one ledger (client-local, presentation only), fed by the two postfixes below.</summary>
    internal static class ShotFeed
    {
        public static readonly ShotLedger Ledger = new ShotLedger();

        internal static bool IsOwn(Missile missile)
        {
            Aircraft own = SceneSingleton<CombatHUD>.i != null ? SceneSingleton<CombatHUD>.i.aircraft : null;
            return own != null && missile != null && missile.owner == own;
        }
    }

    /// <summary>
    /// Records each weapon the local player releases (missile or bomb), the same hook NO_Tactitools' delivery checker
    /// uses. Read-only: never changes the missile.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class ShotLaunchPatch
    {
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (!ShotFeed.IsOwn(__instance)) return;
                WeaponInfo info = __instance.GetWeaponInfo();
                ShotFeed.Ledger.Launch(__instance.GetInstanceID(), info != null && info.bomb, Time.unscaledTime);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.ShotLaunch", e);
            }
        }
    }

    /// <summary>
    /// Resolves a pip when the weapon detonates on this client: the Mirage RPC body <c>UserCode_RpcDetonate_*</c>,
    /// whose second argument says whether it struck armour. Found by name prefix so a regenerated hash suffix does not
    /// break it.
    /// </summary>
    [HarmonyPatch]
    internal static class ShotDetonatePatch
    {
        private static MethodBase TargetMethod()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Missile)))
                if (method.Name.StartsWith("UserCode_RpcDetonate_", StringComparison.Ordinal)) return method;
            return null;
        }

        private static void Postfix(Missile __instance, bool __1)
        {
            try
            {
                if (ShotFeed.IsOwn(__instance)) ShotFeed.Ledger.Detonate(__instance.GetInstanceID(), __1, Time.unscaledTime);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.ShotDetonate", e);
            }
        }
    }
}
