using BoscaliSummer.Features.Progression.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Features.Progression.Patches
{
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UseFuel))]
    internal static class AircraftFuelUsePatch
    {
        private static void Prefix(Aircraft __instance, ref float fuelDrawn)
        {
            Player player = __instance?.Player;
            ProgressionManager manager = ProgressionRuntime.Active;
            if (player != null && manager != null)
                fuelDrawn *= manager.FuelUseMultiplier(ProgressionManager.Identity(player));
        }
    }

    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.RewardPlayer))]
    internal static class RewardAllocationPatch
    {
        private static void Postfix(
            FactionHQ __instance, Player player, float rewardAllocation,
            FactionHQ.RewardType missionType)
        {
            ProgressionManager manager = ProgressionRuntime.Active;
            if (manager == null || player == null || rewardAllocation <= 0f) return;
            float multiplier = manager.RewardAllocationMultiplier(
                ProgressionManager.Identity(player), (byte)missionType);
            if (multiplier <= 1f) return;
            float bonus = rewardAllocation * (1f - __instance.playerTaxRate) * (multiplier - 1f);
            if (bonus > 0f) player.AddAllocation(bonus);
        }
    }
}
