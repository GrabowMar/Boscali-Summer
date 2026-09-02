using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Features.Progression.Patches
{
    /// <summary>
    /// Scales fuel draw for the owning player. Harmony binds <c>fuelDrawn</c> by name; the patch
    /// probe asserts that parameter name against the installed assembly.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UseFuel))]
    internal static class AircraftFuelUsePatch
    {
        private static void Prefix(Aircraft __instance, ref float fuelDrawn)
        {
            ProgressionManager manager = ProgressionRuntime.Active;
            Player player = __instance == null ? null : __instance.Player;
            if (manager == null || player == null) return;
            fuelDrawn *= manager.Multiplier(PlayerIdentity.Of(player), PerkEffect.FuelUse);
        }
    }

    /// <summary>
    /// Pays a perk bonus on top of the vanilla reward. Server-only: the base game credits the
    /// player on the server, and paying again client-side would double-count.
    /// </summary>
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.RewardPlayer))]
    internal static class RewardAllocationPatch
    {
        private static void Postfix(
            FactionHQ __instance, Player player, float rewardAllocation,
            FactionHQ.RewardType missionType)
        {
            ProgressionManager manager = ProgressionRuntime.Active;
            if (manager == null || player == null || rewardAllocation <= 0f) return;
            if (__instance == null || !IsServer()) return;
            if (!TryEffect(missionType, out PerkEffect effect)) return;

            float multiplier = manager.Multiplier(PlayerIdentity.Of(player), effect);
            if (multiplier <= 1f) return;
            // Mirrors the vanilla payout so the bonus tracks what the player actually banked.
            float bonus = rewardAllocation * (1f - __instance.playerTaxRate) * (multiplier - 1f);
            if (bonus > 0f) player.AddAllocation(bonus);
        }

        /// <summary>
        /// Maps a vanilla reward category onto a perk effect. Written against the enum members
        /// rather than their ordinals, so a renamed or reordered member is a compile error
        /// instead of a silently wrong bonus.
        /// </summary>
        private static bool TryEffect(FactionHQ.RewardType type, out PerkEffect effect)
        {
            switch (type)
            {
                case FactionHQ.RewardType.Kill:
                case FactionHQ.RewardType.Recon:
                case FactionHQ.RewardType.Jamming:
                    effect = PerkEffect.CombatReward;
                    return true;
                case FactionHQ.RewardType.Supply:
                case FactionHQ.RewardType.Refuel:
                case FactionHQ.RewardType.Repair:
                    effect = PerkEffect.ServiceReward;
                    return true;
                case FactionHQ.RewardType.RescuePilots:
                case FactionHQ.RewardType.CapturePilots:
                case FactionHQ.RewardType.CaptureLocation:
                    effect = PerkEffect.ObjectiveReward;
                    return true;
                default:
                    effect = PerkEffect.FuelUse;
                    return false;
            }
        }

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }
    }
}
