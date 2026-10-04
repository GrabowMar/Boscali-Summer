using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BoscaliSummer.Modules.QoL.Patches
{
    // Preserve the game's lead/impact calculation, but stop its stick-input correction.
    [HarmonyPatch(typeof(ControlsFilter), nameof(ControlsFilter.GetAim))]
    internal static class WeaponAimAssistPatch
    {
        private delegate ref bool EnabledRef(object aimAssist);

        // AimAssist is a protected nested type, so the field ref is emitted once over `object`
        // instead of boxing through FieldInfo.GetValue/SetValue every frame.
        private static readonly EnabledRef Enabled = Resolve();

        private static EnabledRef Resolve()
        {
            System.Type type = typeof(ControlsFilter).GetNestedType("AimAssist", BindingFlags.NonPublic);
            FieldInfo field = type?.GetField("Enabled", BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.FieldType != typeof(bool)) return null;
            var method = new DynamicMethod("AimAssistEnabled", typeof(bool).MakeByRefType(),
                new[] { typeof(object) }, typeof(WeaponAimAssistPatch).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Ldflda, field);
            il.Emit(OpCodes.Ret);
            return (EnabledRef)method.CreateDelegate(typeof(EnabledRef));
        }

        private static void Prefix(object ___aimAssist)
        {
            if (___aimAssist == null || Enabled == null) return;
            ref bool enabled = ref Enabled(___aimAssist);
            if (enabled) enabled = false;
        }
    }
}
