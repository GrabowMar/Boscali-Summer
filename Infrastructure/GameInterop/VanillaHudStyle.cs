using System;
using System.Reflection;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Runtime
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
        private static FieldInfo markerPrefabField;
        private static FieldInfo topRightPanelField;
        private static FieldInfo overlayPointerField;
        private static FieldInfo overlayDotField;
        private static FieldInfo overlayRingField;
        private static FieldInfo overlayInfoField;
        private static FieldInfo destroySpriteField;
        private static FieldInfo waypointSpriteField;
        private static FieldInfo captureSpriteField;
        private static FieldInfo reconSpriteField;
        private static CockpitStyle cockpit;
        private static MapStyle map;
        private static bool cockpitReady;
        private static bool mapReady;
        private static bool weaponPanelProbed;
        private static float nextAttempt;

        internal struct CockpitStyle
        {
            public TMP_FontAsset Font;
            public Material FontMaterial;
            public Color Colour;
            public Sprite Pointer;
            public Sprite Dot;
            public Sprite Ring;
            public Vector2 PointerSize;
            public Vector2 DotSize;
            public Vector2 RingSize;

            /// <summary>Vanilla's label is font size 32 scaled 0.5, i.e. 16 px on screen.</summary>
            public float LabelScale;
        }

        internal struct MapStyle
        {
            public Sprite WaypointIcon;
            public Sprite DestroyIcon;
            public Sprite ReconIcon;
            public Sprite CaptureIcon;
            public Font LabelFont;
            public Material LabelMaterial;
            public float LabelSize;
            public Color LabelColour;
            public Sprite Ring;

            /// <summary>Vanilla's map label is 24 px scaled 0.5, i.e. 12 px on screen.</summary>
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

        /// <summary>
        /// The size vanilla's own objective label actually renders at: the player's overlay text
        /// size carried through the label's 0.5 transform scale, i.e. 16 px at the default 32.
        /// Imitating the font size alone is what makes a mod's marker text twice as loud.
        /// </summary>
        internal static float ObjectiveTextSize
        {
            get
            {
                float size = OverlayTextSize;
                float scale = cockpitReady && cockpit.LabelScale > 0.01f ? cockpit.LabelScale : 0.5f;
                return !float.IsNaN(size) && !float.IsInfinity(size) && size >= 1f ? size * scale : 16f;
            }
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
                topRightPanelField = AccessTools.Field(typeof(CombatHUD), "topRightPanel");
                overlayPrefabField = AccessTools.Field(typeof(ObjectiveOverlayManager), "overlayPrefab");
                markerPrefabField = AccessTools.Field(typeof(ObjectiveMarkerManager), "markerPrefab");
                overlayPointerField = AccessTools.Field(typeof(ObjectiveOverlay), "objectivePointer");
                overlayDotField = AccessTools.Field(typeof(ObjectiveOverlay), "objectiveDot");
                overlayRingField = AccessTools.Field(typeof(ObjectiveOverlay), "sizeIndicator");
                overlayInfoField = AccessTools.Field(typeof(ObjectiveOverlay), "objectiveInfo");
                destroySpriteField = AccessTools.Field(typeof(ObjectiveMarker), "destroyObjective");
                waypointSpriteField = AccessTools.Field(typeof(ObjectiveMarker), "waypointObjective");
                captureSpriteField = AccessTools.Field(typeof(ObjectiveMarker), "captureObjective");
                reconSpriteField = AccessTools.Field(typeof(ObjectiveMarker), "reconObjective");
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Vanilla HUD style unavailable: " + e.Message);
            }
        }

        public static void Invalidate()
        {
            cockpitReady = false;
            mapReady = false;
            cockpit = default;
            map = default;
            nextAttempt = 0f;
            weaponPanelProbed = false;
        }

        /// <summary>
        /// The vanilla weapon and capacitor column's own rectangle, for anything that wants to
        /// sit under it and share its edges. Read only: nothing here writes to, re-parents or
        /// disables the column, and a scene without it simply says no. Probed once, so a game
        /// build that renames the field costs one reflection lookup and not one per tick.
        /// </summary>
        internal static bool TryWeaponPanel(out RectTransform panel)
        {
            panel = null;
            if (topRightPanelField == null)
            {
                if (weaponPanelProbed) return false;
                weaponPanelProbed = true;
                Initialise();
                if (topRightPanelField == null) return false;
            }

            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                GameObject column = hud != null ? topRightPanelField.GetValue(hud) as GameObject : null;
                if (column == null) return false;
                panel = column.transform as RectTransform;
                return panel != null;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Vanilla weapon column unavailable: " + e.Message);
                weaponPanelProbed = true;
                return false;
            }
        }

        internal static bool TryCockpit(out CockpitStyle style)
        {
            Resolve();
            style = cockpit;
            return cockpitReady;
        }

        internal static bool TryMap(out MapStyle style)
        {
            Resolve();
            style = map;
            return mapReady;
        }

        /// <summary>
        /// Resolve once per scene, but keep retrying while something is missing: the marker
        /// managers only exist in flight and the map one only once the map has been opened, so
        /// a first attempt that lands too early must not disable the style for the whole scene.
        /// </summary>
        private static void Resolve()
        {
            if (cockpitReady && mapReady) return;
            EnsureReflection();
            if (Time.unscaledTime < nextAttempt) return;
            nextAttempt = Time.unscaledTime + RetrySeconds;
            if (!cockpitReady) cockpitReady = TryReadCockpit(out cockpit);
            if (!mapReady) mapReady = TryReadMap(out map);
        }

        private static void EnsureReflection()
        {
            if (overlayPrefabField != null && markerPrefabField != null && overlayPointerField != null &&
                overlayDotField != null && overlayRingField != null && overlayInfoField != null &&
                destroySpriteField != null && waypointSpriteField != null && captureSpriteField != null &&
                reconSpriteField != null)
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

                // The serialized fields are the game's own names; the fallback scan covers a
                // prefab whose child objects were renamed.
                Image pointer = Typed<Image>(prefab, overlayPointerField) ?? Named<Image>(prefab, "objectivePointer");
                Image dot = Typed<Image>(prefab, overlayDotField) ?? Named<Image>(prefab, "objectiveDot");
                Image ring = Typed<Image>(prefab, overlayRingField) ?? Named<Image>(prefab, "objectiveSizeIndicator");
                TextMeshProUGUI label = Typed<TextMeshProUGUI>(prefab, overlayInfoField) ?? Named<TextMeshProUGUI>(prefab, "ObjectiveInfo");
                if (pointer == null || dot == null || ring == null || label == null) return false;

                style = new CockpitStyle
                {
                    Pointer = pointer.sprite,
                    PointerSize = Size(pointer.rectTransform, 25f),
                    Dot = dot.sprite,
                    DotSize = Size(dot.rectTransform, 10f),
                    Ring = ring.sprite,
                    RingSize = Size(ring.rectTransform, 20f),
                    Font = label.font,
                    FontMaterial = label.fontSharedMaterial,
                    Colour = label.color,
                    LabelScale = Scale(label.rectTransform, 0.5f)
                };
                return style.Pointer != null && style.Dot != null && style.Ring != null && style.Font != null;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Cockpit marker style unavailable: " + e.Message);
                return false;
            }
        }

        private static bool TryReadMap(out MapStyle style)
        {
            style = default;
            try
            {
                ObjectiveMarkerManager manager = UnityEngine.Object.FindObjectOfType<ObjectiveMarkerManager>();
                ObjectiveMarker prefab = manager != null && markerPrefabField != null
                    ? markerPrefabField.GetValue(manager) as ObjectiveMarker
                    : null;
                if (prefab == null) return false;

                Text label = prefab.GetComponentInChildren<Text>(true);
                style = new MapStyle
                {
                    WaypointIcon = Sprite(waypointSpriteField, prefab),
                    DestroyIcon = Sprite(destroySpriteField, prefab),
                    ReconIcon = Sprite(reconSpriteField, prefab),
                    CaptureIcon = Sprite(captureSpriteField, prefab),
                    LabelFont = label != null ? label.font : null,
                    LabelMaterial = label != null ? label.material : null,
                    LabelSize = label != null ? label.fontSize : 24f,
                    LabelColour = label != null ? label.color : Color.white,
                    LabelScale = label != null ? Scale(label.rectTransform, 0.5f) : 0.5f,
                    Ring = ExclusionRing()
                };
                return style.WaypointIcon != null && style.DestroyIcon != null &&
                       style.ReconIcon != null && style.CaptureIcon != null;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Map marker style unavailable: " + e.Message);
                return false;
            }
        }

        /// <summary>The vanilla map radius ring: the exclusion-zone display's own sprite.</summary>
        private static Sprite ExclusionRing()
        {
            GameAssets assets = GameAssets.i;
            if (assets == null || assets.exclusionZoneDisplay == null) return null;
            Image ring = assets.exclusionZoneDisplay.GetComponentInChildren<Image>(true);
            return ring != null ? ring.sprite : null;
        }

        private static Sprite Sprite(FieldInfo field, ObjectiveMarker prefab) =>
            field != null ? field.GetValue(prefab) as Sprite : null;

        private static T Typed<T>(Component prefab, FieldInfo field) where T : Component =>
            field != null ? field.GetValue(prefab) as T : null;

        private static T Named<T>(Component prefab, string name) where T : Component
        {
            foreach (T candidate in prefab.GetComponentsInChildren<T>(true))
                if (string.Equals(candidate.gameObject.name, name, StringComparison.Ordinal))
                    return candidate;
            return null;
        }

        private static Vector2 Size(RectTransform rect, float fallback) =>
            rect != null && rect.sizeDelta.x > 0.5f && rect.sizeDelta.y > 0.5f ? rect.sizeDelta : new Vector2(fallback, fallback);

        /// <summary>
        /// The vanilla labels carry their real size in a 0.5 transform scale, so a mod that
        /// copies the font size alone renders twice as large as the game beside it.
        /// </summary>
        private static float Scale(RectTransform rect, float fallback) =>
            rect != null && rect.localScale.x > 0.01f && rect.localScale.x <= 4f ? rect.localScale.x : fallback;
    }
}
