using System;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Features.Progression.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Infrastructure.Diagnostics;
using BoscaliSummer.Runtime;
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
            try
            {
                ProgressionManager manager = ProgressionRuntime.Active;
                Player player = __instance == null ? null : __instance.Player;
                if (manager == null || player == null) return;
                fuelDrawn *= manager.Multiplier(PlayerIdentity.Of(player), PerkEffect.FuelUse);
                fuelDrawn *= PlaneEngineMap.FuelFactor(manager.TuneFor(__instance));
            }
            catch (Exception e)
            {
                PatchGuard.Report("Progression.UseFuel", e);
            }
        }
    }

    /// <summary>Range map limits engine demand after the native control filter.</summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.FilterInputs))]
    internal static class AircraftEngineMapPatch
    {
        private static void Postfix(Aircraft __instance)
        {
            try
            {
                ProgressionManager manager = ProgressionRuntime.Active;
                if (manager == null || __instance == null) return;
                float limit = PlaneEngineMap.ThrottleCeiling(manager.TuneFor(__instance));
                if (limit >= 1f) return;
                ControlInputs inputs = __instance.GetInputs();
                if (inputs != null && inputs.throttle > limit) inputs.throttle = limit;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Progression.EngineMap", e);
            }
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
            if (__instance == null || !GameAccess.IsServer()) return;
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
    }
}
