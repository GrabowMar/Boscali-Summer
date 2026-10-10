using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Patches
{
    /// <summary>The shot tracker's one ledger (client-local, presentation only): launches arrive from
    /// <c>MissileTracker.Launched</c>, outcomes from the detonation postfix below.</summary>
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
    /// Resolves a pip when the weapon detonates on this client: the Mirage RPC body <c>UserCode_RpcDetonate_*</c>
    /// (<c>relativeUnit, useUnit, pos, armed, hitArmor, hitTerrain, normal</c>). The flags are bound by NAME, not position:
    /// the old <c>__1</c> was <c>useUnit</c>, which is only true within 200 m of a target, so dumb bombs always read MISS.
    /// Found by name prefix so a regenerated hash suffix does not break it; tests/BoscaliSummer.PatchProbe pins the names.
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

        private static void Postfix(Missile __instance, bool hitArmor, bool useUnit)
        {
            try
            {
                if (ShotFeed.IsOwn(__instance))
                    ShotFeed.Ledger.Detonate(__instance.GetInstanceID(), ShotRules.Hit(hitArmor, useUnit), Time.unscaledTime);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.ShotDetonate", e);
            }
        }
    }
}
