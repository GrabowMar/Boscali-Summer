using BoscaliSummer.Features.Command.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Features.Command.Patches
{
    [HarmonyPatch(typeof(CombatAI), "AnalyzeTarget")]
    internal static class AiTargetScoringPatch
    {
        private static void Postfix(Unit analyzer, TrackingInfo trackingInfo, ref OpportunityThreat __result)
        {
            CommandManager mgr = CommandManager.Active;
            if (mgr == null || trackingInfo == null || analyzer == null) return;

            if (!trackingInfo.TryGetUnit(out Unit target) || target == null) return;

            float multiplier = mgr.GetTargetScoreMultiplier(analyzer, target);
            if (multiplier != 1f)
            {
                __result = new OpportunityThreat(__result.opportunity * multiplier, __result.threat);
            }
        }
    }
}
