using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Support.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Patches
{
    /// <summary>
    /// Kill CR. Vanilla calls <c>ReportKillAction(player, target, factor)</c> once per contributor with
    /// <c>factor</c> = that contributor's damage share, so the postfix pays each contributor their share of the
    /// target value. Postfix only; vanilla allocation and score are unchanged.
    /// </summary>
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.ReportKillAction))]
    internal static class CreditKillPatch
    {
        private static void Postfix(Player player, Unit target, float factor)
        {
            try
            {
                SupportManager.Active?.CreditFromKill(player, target, factor);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Support.CreditKill", e);
            }
        }
    }
}
