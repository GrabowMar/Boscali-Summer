using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Patches
{
    /// <summary>Water detonation makes a missile kinematic before the native collision code clears its velocity.</summary>
    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    internal static class MissileWaterImpactPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo detonate = AccessTools.Method(typeof(Missile), nameof(Missile.Detonate),
                new[] { typeof(Vector3), typeof(bool), typeof(bool) });
            MethodInfo up = AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.up));
            MethodInfo body = AccessTools.PropertyGetter(typeof(Unit), nameof(Unit.rb));
            MethodInfo zero = AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.zero));
            MethodInfo velocity = AccessTools.PropertySetter(typeof(Rigidbody), nameof(Rigidbody.velocity));
            int matches = 0, at = -1;
            if (detonate != null && up != null && body != null && zero != null && velocity != null)
                for (int i = 8; i < codes.Count; i++)
                    if (codes[i - 8].opcode == OpCodes.Ldarg_0 && codes[i - 7].Calls(up) &&
                        codes[i - 6].opcode == OpCodes.Ldc_I4_0 && codes[i - 5].opcode == OpCodes.Ldc_I4_0 &&
                        codes[i - 4].Calls(detonate) && codes[i - 3].opcode == OpCodes.Ldarg_0 &&
                        codes[i - 2].Calls(body) && codes[i - 1].Calls(zero) && codes[i].Calls(velocity))
                    { matches++; at = i; }
            if (matches != 1)
            {
                Plugin.Logger?.LogWarning("[CombatAdjust] Missile water velocity guard not installed: expected one native call pattern, found " + matches + ".");
                return codes;
            }
            // Change only the call operand/opcode; retain this instruction's labels and exception blocks.
            codes[at].opcode = OpCodes.Call;
            codes[at].operand = AccessTools.Method(typeof(MissileWaterImpactPatch), nameof(SetWaterVelocity));
            return codes;
        }

        private static void SetWaterVelocity(Rigidbody body, Vector3 velocity)
        {
            if (!body.isKinematic) body.velocity = velocity;
        }
    }
}
