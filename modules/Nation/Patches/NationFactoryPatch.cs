using BoscaliSummer.Modules.Nation.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Nation.Patches
{
    /// <summary>
    /// A factory the nation layer runs produces through <see cref="NationService"/> only. The game's own production loop
    /// (started by <c>Factory.SetFactory</c>, and it cannot be stopped) keeps firing for mission-placed factories; this
    /// prefix swallows those ticks so nothing is produced twice. A factory the layer does not manage is untouched.
    /// </summary>
    [HarmonyPatch(typeof(Factory), "ProduceUnit")]
    internal static class NationFactoryPatch
    {
        private static bool Prefix(Factory __instance) => !NationService.Manages(__instance);
    }

    /// <summary>A convoy takes its vehicles out of the reserve the factories filled (whoever bought it, and free ones too).</summary>
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.AddConvoy))]
    internal static class NationConvoyDrawPatch
    {
        private static void Prefix(FactionHQ __instance, Faction.ConvoyGroup convoy) => NationService.Draw(__instance, convoy);
    }

    /// <summary>
    /// The game's own convoy purchase (the contribute menu and the STR desk send it): refused before any money moves when the
    /// reserve does not hold the group's vehicles, and the pilot is told NOT PRODUCED. The method's name carries a hash the
    /// game's weaver picks, so it is found by its prefix.
    /// </summary>
    [HarmonyPatch]
    internal static class NationConvoyGatePatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            foreach (System.Reflection.MethodInfo method in AccessTools.GetDeclaredMethods(typeof(NuclearOption.Networking.Player)))
                if (method.Name.StartsWith("UserCode_CmdPurchaseConvoy", System.StringComparison.Ordinal)) return method;
            return null;
        }

        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(NuclearOption.Networking.Player __instance, int convoyIndex)
        {
            FactionHQ hq = __instance != null ? __instance.HQ : null;
            if (hq == null || hq.faction == null || !hq.faction.TryGetConvoyGroup(convoyIndex, out Faction.ConvoyGroup group)) return true;
            if (NationService.Covers(hq, group)) return true;
            NationService.Refuse(__instance, BoscaliSummer.Core.Contracts.NationOutcome.NotProduced);
            return false;
        }
    }

    /// <summary>Every kill message (host execution and received alike; the service counts it on the host only) moves war support.</summary>
    [HarmonyPatch]
    internal static class NationKillPatch
    {
        private static System.Reflection.MethodBase TargetMethod() => BoscaliSummer.Core.Game.KillMessageRpc.Target();

        private static void Prefix(PersistentID killerID, PersistentID killedID) => NationService.NoteKill(killerID, killedID);
    }
}
