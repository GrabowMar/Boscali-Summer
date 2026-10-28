using System;
using HarmonyLib;
using UnityEngine;

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

        /// <summary>The live native HUD and the canvas its icon layer sits on; false while either is missing (no flight scene, or the layer is hidden).</summary>
        public static bool HudCanvas(out CombatHUD hud, out Canvas canvas)
        {
            canvas = null;
            hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null || !hud.iconLayer.gameObject.activeInHierarchy) return false;
            canvas = hud.iconLayer.GetComponentInParent<Canvas>();
            return canvas != null;
        }

        public static F Get<T, F>(AccessTools.FieldRef<T, F> field, T owner) where T : class =>
            field != null && owner != null ? field(owner) : default;
    }
}
