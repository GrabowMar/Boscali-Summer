using System.Collections.Generic;
using HarmonyLib;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// MissionPosition.GetAllPositionsResults is the read path of every objective UI surface
    /// (map markers, cockpit pointer and area ring, MIS objective list). Vanilla AI never
    /// calls it, so appending accepted contracts here gives the native look without putting
    /// a synthetic entry in MissionRunner.activeByFaction.
    /// </summary>
    [HarmonyPatch(typeof(MissionPosition), nameof(MissionPosition.GetAllPositionsResults),
        new[] { typeof(FactionHQ), typeof(GlobalPosition), typeof(bool), typeof(List<MissionPosition.PositionResult>) })]
    internal static class OperationMarkerPatch
    {
        private static void Postfix(FactionHQ factionHQ, GlobalPosition from, List<MissionPosition.PositionResult> results) =>
            OperationMarkerBridge.Append(factionHQ, from, results);
    }
}
