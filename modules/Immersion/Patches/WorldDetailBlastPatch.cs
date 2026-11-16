using BoscaliSummer.Modules.Immersion.Runtime;
using HarmonyLib;
using NuclearOption.Effects;

namespace BoscaliSummer.Modules.Immersion.Patches
{
    [HarmonyPatch(typeof(BlastManager), nameof(BlastManager.AddBlast))]
    internal static class WorldDetailBlastPatch
    {
        private static void Postfix(GlobalPosition position, float radius, float ___radiusMultiplier)
        {
            if (UnityEngine.Application.isBatchMode || GameManager.IsHeadless) return;
            WorldFx world = WorldFx.Live;
            if (world != null) world.RecordDetailBlast(position, radius * ___radiusMultiplier);
        }
    }
}
