using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Extends battlefield wreckage lifetime so destroyed aircraft smolder, burn,
    /// and smoke with realistic secondary fire sites rather than disappearing after 30 seconds.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), "UnitDisabled")]
    internal static class AircraftWreckPersistencePatch
    {
        internal const float ExtendedDelaySeconds = 180f;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count - 1; i++)
            {
                bool isThirtyLiteral = codes[i].opcode == OpCodes.Ldc_R4
                    && codes[i].operand is float f
                    && f == 30f;

                bool nextCallsWaitRemoveAircraft = (codes[i + 1].opcode == OpCodes.Call || codes[i + 1].opcode == OpCodes.Callvirt)
                    && codes[i + 1].operand is MethodInfo mi
                    && mi.Name == "WaitRemoveAircraft";

                if (isThirtyLiteral && nextCallsWaitRemoveAircraft)
                {
                    codes[i] = new CodeInstruction(OpCodes.Ldc_R4, ExtendedDelaySeconds);
                }
            }

            return codes;
        }
    }
}
