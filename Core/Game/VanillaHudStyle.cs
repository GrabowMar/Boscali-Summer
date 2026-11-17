using System;
using System.Reflection;
using HarmonyLib;
using BoscaliSummer.Core.Contracts;
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
            material = Glow(label != null ? label.fontSharedMaterial : null);
            return font != null;
        }

        /// <summary>The visor glitch's text shader in the fx bundle (see AvionicsUi/Assets/Source/NOA_TMP_VisorGlitchSDF.shader).</summary>
        internal const string GlitchTextShader = "NOA/TMP/VisorGlitchSDF";

        /// <summary>The sim's pin: every glow material made so far runs on the stock TMP shader (true) or the visor glitch one (false), for a pixel comparison of the two.</summary>
        internal static void UseStockText(bool stock)
        {
            Shader shader = stock ? Shader.Find("TextMeshPro/Distance Field") : NOAvionics.AvBundle.Shader(GlitchTextShader);
            if (shader == null) return;
            foreach (Material m in glows.Values) if (m != null) m.shader = shader;
        }

        private static readonly System.Collections.Generic.Dictionary<long, Material> glows = new System.Collections.Generic.Dictionary<long, Material>();
        /// <summary>Each copy's native material, so a copy handed in again is re-tinted from its source.</summary>
        private static readonly System.Collections.Generic.Dictionary<Material, Material> sourceOf = new System.Collections.Generic.Dictionary<Material, Material>();

        /// <summary>The halo's alpha and spread, and the face's dilate (the native face is thin, -0.19, and frayed on bright cloud once there was no backdrop behind it).</summary>
        internal const float HaloAlpha = 0.3f, HaloDilate = 0.05f, HaloSoftness = 0.3f, FaceDilate = -0.06f;

        /// <summary>
        /// The hair-thin dark edge round every glyph (2026-11-16, the approved mockup's 1 px dark-green rim: small text reads on bright sky and cloud). TMP's own outline, centred on the glyph edge: it
        /// would eat half its width off the face, so the face dilate is raised by <c>width * EdgeFaceBack</c> and the letters keep today's weight. The colour is the mockup's <c>--edge</c> (2,16,10 at 0.92).
        /// Width 0 turns it off; <see cref="SetEdge"/> re-applies it to every material already made.
        /// </summary>
        internal const float EdgeDefault = 0.16f, EdgeMax = 0.25f, EdgeFaceBack = 1.1f;
        private static readonly Color EdgeColour = new Color(HudInk.TextEdge.R, HudInk.TextEdge.G, HudInk.TextEdge.B, HudInk.TextEdge.A);
        internal static float Edge { get; private set; } = EdgeDefault;

        /// <summary>The pilot's <c>TacBoard.TextEdge</c> (0 off, to <see cref="EdgeMax"/>): live, on every glow material made so far and on those made later.</summary>
        internal static void SetEdge(float width)
        {
            width = float.IsNaN(width) ? EdgeDefault : Mathf.Clamp(width, 0f, EdgeMax);
            if (width == Edge) return;
            Edge = width;
            foreach (Material m in glows.Values) if (m != null) ApplyEdge(m);
        }

        private static void ApplyEdge(Material m)
        {
            bool on = Edge > 0.001f;
            if (on) m.EnableKeyword(ShaderUtilities.Keyword_Outline); else m.DisableKeyword(ShaderUtilities.Keyword_Outline);
            m.SetColor(ShaderUtilities.ID_OutlineColor, EdgeColour);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, on ? Edge : 0f);
            m.SetFloat(ShaderUtilities.ID_FaceDilate, FaceDilate + (on ? Edge * EdgeFaceBack : 0f));
            ShaderUtilities.UpdateShaderRatios(m);
        }

        /// <summary>A word in the HUD ink (<see cref="Glow(Material, Color)"/> in <see cref="Colours"/>.AllClear): what a label gets that does not follow its own colour.</summary>
        internal static Material Glow(Material native) => Glow(native, Ink);

        /// <summary>
        /// The mod's HUD words as light on the visor (2026-11-15, user: no dark shadows, "brighter and very slight glow to imitate augmented reality"): a copy of the native label
        /// material on TMP's alpha-blended SDF shader (the native one is additive, so ink vanished on bright cloud), a fuller face, and a soft halo of <paramref name="tint"/>
        /// round each glyph (TMP's underlay, centred: its glow fades out on text this small). The halo stays inside the atlas padding, 1-2 px on screen, and fades with the
        /// text. One copy per native material and tint (rgb in 16 steps, at most 64: past that the ink one), kept for the session; null in, null out; a copy in is re-tinted from its native.
        /// </summary>
        internal static Material Glow(Material native, Color tint)
        {
            if (native == null) return null;
            if (sourceOf.TryGetValue(native, out Material source)) native = source;
            Color32 c = tint;
            long key = ((long)native.GetInstanceID() << 16) ^ ((c.r >> 4) << 8 | (c.g >> 4) << 4 | (c.b >> 4));
            if (glows.TryGetValue(key, out Material m) && m != null) return m;
            if (glows.Count >= 64 && tint != Ink) return Glow(native);
            m = new Material(native) { name = native.name + " (Boscali AR glow)", hideFlags = HideFlags.HideAndDontSave };
            // BROKEN HELMET (2026-11-16): the mod's own copy of TMP's SDF shader when the fx bundle has it (the visor glitch tears, splits and drops out the words with it;
            // identical to the stock shader while no glitch runs), else the stock one.
            Shader sdf = NOAvionics.AvBundle.Shader(GlitchTextShader);
            if (sdf == null) sdf = Shader.Find("TextMeshPro/Distance Field");
            if (sdf != null) m.shader = sdf;
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(c.r / 255f, c.g / 255f, c.b / 255f, HaloAlpha));
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, HaloDilate);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, HaloSoftness);
            ApplyEdge(m);      // face dilate, outline and the shader ratios
            glows[key] = m;
            sourceOf[m] = native;
            return m;
        }

        /// <summary>Vanilla HUD colours, live so a custom theme carries over. AllClear is INK (the theme colour as it looks on sky, HudInk.Ink); Warning is CAUTION (HudInk.Caution) and Alert WARNING (HudInk.Warning).</summary>
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
                            AllClear = Washed(theme.AllClear),
                            Warning = ToColor(HudInk.Caution(ToRgba(theme.Warning))),
                            Alert = ToColor(HudInk.Warning(ToRgba(theme.Alert)))
                        };
                }
                catch (Exception)
                {
                }

                return new Palette
                {
                    AllClear = Washed(VanillaGreen),
                    Warning = ToColor(HudInk.Caution(ToRgba(VanillaYellow))),
                    Alert = ToColor(HudInk.Warning(ToRgba(VanillaRed)))
                };
            }
        }

        /// <summary>
        /// The visor's colour roles (HudStandard): INK is the game's HUD colour as it looks on sky, INK-HI that lifted toward white, INK-DIM an opaque darker tone, CAUTION the theme's amber, WARNING its red. All live (a custom theme
        /// recolours the whole display). INFO (allied, radio, navigation) is INK-HI: the HUD theme has no cool tone.
        /// </summary>
        internal static Color Ink => Colours.AllClear;
        internal static Color InkHi => ToColor(HudInk.Hi(ToRgba(Colours.AllClear)));
        internal static Color InkDim => ToColor(HudInk.Dim(ToRgba(Colours.AllClear)));
        internal static Color Caution => Colours.Warning;
        internal static Color WarningRed => Colours.Alert;

        /// <summary>The theme's all-clear colour as INK (<see cref="HudInk.Ink"/>: the game's additive wash on sky added).</summary>
        private static Color Washed(Color theme) => ToColor(HudInk.Ink(ToRgba(theme)));

        private static NOAvionics.Rgba ToRgba(Color c) => new NOAvionics.Rgba(c.r, c.g, c.b, 1f);
        private static Color ToColor(NOAvionics.Rgba c) => new Color(c.R, c.G, c.B, c.A);

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
                    FontMaterial = Glow(label.fontSharedMaterial),
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
