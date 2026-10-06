using System.Reflection;
using HarmonyLib;

using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Kill credit from the game kill message, which names the killer (review M5g C1). Each target is
    /// credited once.</summary>
    internal static class WingKillCredit
    {
        /// <summary>Review M5g C1: the game's kill message names the killer — a wing pilot's kill is credited from it
        /// (host only; never a friendly or a missile).</summary>
        public static void Credit(PersistentID killerID, PersistentID killedID)
        {
            if (!UnitRegistry.TryGetPersistentUnit(killerID, out PersistentUnit k) || !(k.unit is Aircraft shooter) || !shooter.IsServer) return;
            // The persistent record outlives the destroyed victim (its type and side stay known).
            if (!UnitRegistry.TryGetPersistentUnit(killedID, out PersistentUnit victim) || victim.unit is Missile ||
                victim.GetHQ() == shooter.NetworkHQ) return;
            string type = victim.definition != null ? victim.definition.unitName : victim.unitName;
            WingPilotRoster.NoteKill(shooter, killedID.Id, type, WingSettings.Instance.PilotProgression.Value);
        }
    }

    // Harmony calls the prefix by name.
#pragma warning disable IDE0051
    /// <summary>Review M5g C1: every kill message (host and received) passes the killer and victim to the kill credit.</summary>
    [HarmonyPatch]
    internal static class WingKillMessagePatch
    {
        private static MethodBase TargetMethod() => AccessTools.FirstMethod(typeof(MessageManager),
            method => method.Name.StartsWith("UserCode_RpcKillMessage_"));

        [HarmonyPrefix]
        private static void Prefix(PersistentID killerID, PersistentID killedID) => WingKillCredit.Credit(killerID, killedID);
    }
#pragma warning restore IDE0051
}
