using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Kaiju.Networking;
using BoscaliSummer.Modules.Kaiju.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Kaiju
{
    /// <summary>KAIJU: a mountain-sized creature the host summons and every aircraft fights. Spec:
    /// docs/superpowers/specs/2026-10-19-kaiju-design.md; art pipeline: modules/Kaiju/Assets.</summary>
    internal sealed class KaijuModule : IModule
    {
        private static readonly Type[] Patches = { typeof(KaijuPrefabPatch), typeof(KaijuSelfDamagePatch) };

        public ModuleMetadata Metadata { get; } = new ModuleMetadata("kaiju", "Mountain-sized kaiju");
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            context.AddSceneService<KaijuNet>(70);
            KaijuDirector director = context.AddSceneService<KaijuDirector>(71);
            director.Configure(context.Settings.Kaiju);
            context.AddService<IKaijuBoss>(director); // Spooktober's boss menu reaches the kaiju through this
            context.Logger.LogInfo("[Kaiju] Ready: the host summons it from F1 > Kaiju. It rises out of the ground or sea, marches on the nearest airbase, throws heat-seeking spikes, sweeps a lagging mouth beam and claws close aircraft. 21 glowing weak spots are lockable game units; breaking them takes attacks away, and enough of them expose the heart.");
        }
    }

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
