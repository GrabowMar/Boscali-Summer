using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Spooktober.Kaiju.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Spooktober.Kaiju
{
    /// <summary>Registers the hardpoint prefab at the moment the game registers its own, so a client that joins
    /// mid-fight can resolve hardpoints that already exist.</summary>
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "RegisterPrefabs")]
    internal static class KaijuPrefabPatch
    {
        private static void Postfix()
        {
            try { KaijuAssets.EnsureRegistered(force: true); }
            catch (Exception e) { PatchGuard.Report("Kaiju.RegisterPrefabs", e); }
        }
    }

    /// <summary>A kaiju's own spikes and footfalls never damage its weak spots. Hot path: one set lookup when a kaiju
    /// exists, nothing otherwise.</summary>
    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.TakeDamage))]
    internal static class KaijuSelfDamagePatch
    {
        private static bool Prefix(UnitPart __instance, PersistentID dealerID) => !KaijuBody.IsSelfHit(__instance.parentUnit, dealerID);
    }
}
