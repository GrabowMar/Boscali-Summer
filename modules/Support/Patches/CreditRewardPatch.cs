using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Support.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Patches
{
    /// <summary>Core 6.2: CR rides the vanilla reward path. Postfix only; vanilla allocation and score are unchanged.</summary>
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.RewardPlayer))]
    internal static class CreditRewardPatch
    {
        private static void Postfix(Player player, Unit target, float rewardAllocation, FactionHQ.RewardType missionType)
        {
            try
            {
                SupportManager.Active?.CreditFromReward(player, target, rewardAllocation, missionType);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Support.CreditReward", e);
            }
        }
    }
}
