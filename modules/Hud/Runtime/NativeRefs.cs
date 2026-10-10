using System;
using HarmonyLib;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Cached, allocation-free reads of private native fields (<c>AccessTools.FieldRefAccess</c> compiles one delegate per
    /// field; <c>FieldInfo.GetValue</c> boxes every float, vector and struct it returns, every frame). A field the game
    /// renamed gives a null delegate and reads default, never an exception; tests/BoscaliSummer.PatchProbe pins every name.
    /// </summary>
    internal static class NativeRefs
    {
        public static AccessTools.FieldRef<T, F> Field<T, F>(string name) where T : class
        {
            try { return AccessTools.Field(typeof(T), name) != null ? AccessTools.FieldRefAccess<T, F>(name) : null; }
            catch (Exception) { return null; }
        }

        public static F Get<T, F>(AccessTools.FieldRef<T, F> field, T owner) where T : class =>
            field != null && owner != null ? field(owner) : default;
    }
}
