using System;
using System.Reflection;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Read-only access to the visual style the game itself uses for objective markers, so a
    /// mod can draw look-alike markers without bundling assets. Every value is copied out of a
    /// vanilla prefab, theme or player option; nothing here writes to, re-parents or disables a
    /// vanilla object. Prefab-derived values are resolved once per scene and invalidated on
    /// scene reset; palette, unit system and text size are read live because options and themes
    /// change at runtime.
    /// </summary>
    internal static class VanillaHudStyle
    {
        private static readonly Color VanillaGreen = new Color(0f, 1f, 0f, 1f);
        private static readonly Color VanillaYellow = new Color(1f, 1f, 0f, 1f);
        private static readonly Color VanillaRed = new Color(1f, 0f, 0f, 1f);
        private const float RetrySeconds = 2f;

        private static FieldInfo overlayPrefabField;
        private static FieldInfo overlayInfoField;
        private static CockpitStyle cockpit;
        private static bool cockpitReady;
        private static float nextAttempt;
        private static bool reflectionTried;

        internal struct CockpitStyle
        {
            public TMP_FontAsset Font;
            public Material FontMaterial;

            /// <summary>Vanilla's label is font size 32 scaled 0.5, i.e. 16 px on screen.</summary>
            public float LabelScale;
        }

        internal struct Palette
        {
            public Color AllClear;
            public Color Warning;
            public Color Alert;
        }

        /// <summary>Metric is the game's default; it also decides how every distance prints.</summary>
        internal static bool Metric => PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric;

        /// <summary>The vanilla objective label size, live from the player's options.</summary>
        internal static float OverlayTextSize => PlayerSettings.overlayTextSize;

        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");

        /// <summary>The native HUD's label face (the weapon panel's name text), which the board, the third-person cluster and the top-centre lane all write in. False before the HUD exists.</summary>
        internal static bool TryHudFont(out TMP_FontAsset font, out Material material)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            WeaponStatus status = hud != null ? WeaponStatusField?.GetValue(hud) as WeaponStatus : null;
            TextMeshProUGUI label = status != null ? NameTextField?.GetValue(status) as TextMeshProUGUI : null;
            font = label != null ? label.font : null;
            material = label != null ? label.fontSharedMaterial : null;
            return font != null;
        }

        /// <summary>Vanilla HUD colours, live so a custom theme carries over.</summary>
        internal static Palette Colours
        {
            get
            {
                try
                {
                    ThemeGroup group = ThemeManager.Active ?? ThemeManager.Vanilla;
                    ColorTheme theme = group != null ? group.ColorTheme : null;
                    if (theme != null)
                        return new Palette
                        {
                            AllClear = theme.AllClear,
                            Warning = theme.Warning,
                            Alert = theme.Alert
                        };
                }
                catch (Exception)
                {
                }

                return new Palette
                {
                    AllClear = VanillaGreen,
                    Warning = VanillaYellow,
                    Alert = VanillaRed
                };
            }
        }

        public static void Initialise()
        {
            try
            {
                overlayPrefabField = AccessTools.Field(typeof(ObjectiveOverlayManager), "overlayPrefab");
                overlayInfoField = AccessTools.Field(typeof(ObjectiveOverlay), "objectiveInfo");
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Vanilla HUD style unavailable: " + e.Message);
            }
        }

        public static void Invalidate()
        {
            cockpitReady = false;
            cockpit = default;
            nextAttempt = 0f;
        }

        internal static bool TryCockpit(out CockpitStyle style)
        {
            Resolve();
            style = cockpit;
            return cockpitReady;
        }

        /// <summary>
        /// Resolve once per scene, but keep retrying while something is missing: the
        /// overlay manager only exists in flight, so a first attempt that lands too early must not
        /// disable the style for the whole scene.
        /// </summary>
        private static void Resolve()
        {
            if (cockpitReady) return;
            EnsureReflection();
            float now = Time.unscaledTime;
            if (now >= nextAttempt)
            {
                nextAttempt = now + RetrySeconds;
                if (!cockpitReady) cockpitReady = TryReadCockpit(out cockpit);
            }
        }

        private static void EnsureReflection()
        {
            if (reflectionTried) return;
            reflectionTried = true;
            if (overlayPrefabField != null && overlayInfoField != null)
                return;
            Initialise();
        }

        private static bool TryReadCockpit(out CockpitStyle style)
        {
            style = default;
            try
            {
                ObjectiveOverlayManager manager = UnityEngine.Object.FindObjectOfType<ObjectiveOverlayManager>();
                ObjectiveOverlay prefab = manager != null && overlayPrefabField != null
                    ? overlayPrefabField.GetValue(manager) as ObjectiveOverlay
                    : null;
                if (prefab == null) return false;

                // Only the label is read: the overlay's pointer, dot and ring sprites are white on
                // opaque black and need the game's additive material, so nothing of ours draws them.
                // The serialized field is the game's own name; the fallback scan covers a prefab
                // whose child object was renamed.
                TextMeshProUGUI label = Typed<TextMeshProUGUI>(prefab, overlayInfoField) ?? Named<TextMeshProUGUI>(prefab, "ObjectiveInfo");
                if (label == null) return false;

                style = new CockpitStyle
                {
                    Font = label.font,
                    FontMaterial = label.fontSharedMaterial,
                    LabelScale = Scale(label.rectTransform, 0.5f)
                };
                return style.Font != null;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Cockpit marker style unavailable: " + e.Message);
                return false;
            }
        }

        private static T Typed<T>(Component prefab, FieldInfo field) where T : Component =>
            field != null ? field.GetValue(prefab) as T : null;

        private static T Named<T>(Component prefab, string name) where T : Component
        {
            foreach (T candidate in prefab.GetComponentsInChildren<T>(true))
                if (string.Equals(candidate.gameObject.name, name, StringComparison.Ordinal))
                    return candidate;
            return null;
        }

        /// <summary>
        /// The vanilla labels carry their real size in a 0.5 transform scale, so a mod that
        /// copies the font size alone renders twice as large as the game beside it.
        /// </summary>
        private static float Scale(RectTransform rect, float fallback) =>
            rect != null && rect.localScale.x > 0.01f && rect.localScale.x <= 4f ? rect.localScale.x : fallback;
    }
}
