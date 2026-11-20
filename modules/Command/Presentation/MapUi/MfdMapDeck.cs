using NOAvionics;
using BoscaliSummer.Modules.Command.Configuration;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Passive presentation layers for the maximised tactical map.
    ///
    /// <para>The game's map, clock/feed, spawn strip, MFD dock and rail remain in their
    /// native canvases. This class deliberately does not reparent any of them: DynamicMap
    /// owns a mask and its own coordinate/input paths, while the clock and spawn strip use
    /// a different lower-sorting canvas. Instead it supplies two non-interactive layers
    /// behind that live UI:</para>
    ///
    /// <list type="bullet">
    /// <item><description>a lower-sorting full-canvas screen deck which visually gathers the
    /// map controls, top feed and bottom spawn strip into one instrument surface; and</description></item>
    /// <item><description>a darker tray immediately behind the map viewport, so its lower
    /// fade never exposes the cockpit or terrain beneath it.</description></item>
    /// </list>
    ///
    /// <para>Both roots are owned, passive and destroyed when the maximised map is restored.
    /// That keeps the native hierarchy and input behaviour transactionally reversible.</para>
    /// </summary>
    internal static class MfdMapDeck
    {
        private const string BackdropName = "NOAvionics.TacticalBackdrop";
        private const string TrayName = "NOAvionics.MapTray";
        private const float TrayBleed = 10f;

        private static CommandSettings settings;
        private static GameObject backdrop;
        private static GameObject tray;

        private static Image backdropBaseImage;
        private static Image backdropGradientImage;
        private static Image trayBaseImage;
        private static Image trayGradientImage;

        private static Image terrainImage;
        private static bool terrainWasEnabled;
        private static Color terrainColor;
        private static Image nativeBackground;
        private static Color nativeBackgroundColor;

        public static void Configure(CommandSettings config)
        {
            settings = config;
        }

        /// <summary>Apply the matte instrument finish.</summary>
        public static void ApplyAppearance(CommandSettings config = null)
        {
            if (config != null) settings = config;

            if (!DynamicMap.mapMaximized)
            {
                // The backdrop is a root overlay canvas, so any map-close path that skips
                // the minimize postfix leaves it drawing over the cockpit. Own the
                // invariant here: no maximised map, no owned decoration.
                Restore();
                return;
            }

            if (backdrop == null) return;

            float opacity = settings != null ? settings.DeckOpacity.Value : 0.95f;
            float mapTrayOpacity = settings != null ? settings.MapTrayOpacity.Value : 0.15f;

            if (backdropBaseImage != null)
            {
                // With frosted glass the tint is opaque (no sharp world under the blur); MFD OPACITY then sets how much blur bleeds through.
                bool frosted = AvBlurSource.Enabled;
                backdropBaseImage.color = AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(frosted ? 1f : opacity);
                AvFrost frost = backdrop.GetComponentInChildren<AvFrost>(true);
                if (frost != null) frost.Bleed = Mathf.Clamp(1.33f - opacity, .38f, .8f);
            }

            if (backdropGradientImage != null)
            {
                backdropGradientImage.color = Color.white.WithAlpha(Mathf.Clamp01(opacity * 0.75f));
            }

            if (trayBaseImage != null)
            {
                float trayAlpha = mapTrayOpacity;
                trayBaseImage.color = AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(trayAlpha);
            }

            if (trayGradientImage != null)
            {
                float gradAlpha = 0.35f * mapTrayOpacity;
                trayGradientImage.color = Color.white.WithAlpha(gradAlpha);
            }

            // Sync DynamicMap terrain image. Map darkening is the tray behind the viewport;
            // the map bed itself has to stay opaque enough to hide the world camera.
            var dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap != null)
            {
                if (dynamicMap.mapImage != null)
                {
                    Image terrainImg = MfdTerrainRelief.MapImageComponent(dynamicMap.mapImage);
                    if (terrainImg != null)
                    {
                        if (terrainImage != terrainImg)
                        {
                            terrainImage = terrainImg;
                            terrainWasEnabled = terrainImg.enabled;
                            terrainColor = terrainImg.color;
                        }
                        bool showTerrain = settings == null || settings.MapTerrainImage.Value;
                        float terrainAlpha = settings != null ? settings.MapTerrainOpacity.Value : 1f;
                        terrainImg.enabled = showTerrain;
                        Color tc = terrainImg.color;
                        tc.a = showTerrain ? terrainAlpha : 0f;
                        terrainImg.color = tc;
                    }
                }
                if (dynamicMap.mapBackground != null)
                {
                    if (nativeBackground != dynamicMap.mapBackground)
                    {
                        if (nativeBackground != null) nativeBackground.color = nativeBackgroundColor;
                        nativeBackground = dynamicMap.mapBackground;
                        nativeBackgroundColor = nativeBackground.color;
                    }
                    dynamicMap.mapBackground.color = Color.white.WithAlpha(0.68f);
                }
            }
        }

        /// <summary>Build or update the two canvas-level passive layers.</summary>
        public static void Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return;

            // The nested map canvas can report a stale rect on the first open; the backdrop
            // must span the real UI area, so resolve it the same way the layout does.
            Vector2 canvasSize = MfdLayout.CanvasSize(canvas);
            if (canvasSize.x <= 1f || canvasSize.y <= 1f) return;

            RectTransform backdropRect = EnsureBackdrop(canvas);
            RectTransform trayRect = EnsureCanvasRoot(ref tray, TrayName, canvas);
            if (backdropRect == null || trayRect == null) return;

            ConfigureBackdrop(backdropRect);
            ConfigureTray(trayRect, columns);

            // The tray shares the map canvas, where sibling order is draw order. Its root is
            // first, so DynamicMap and every other map-canvas control retain their normal
            // placement above it. The full-screen backdrop is in its own lower-sorting canvas
            // (configured by EnsureBackdrop), which also keeps gameplay-canvas controls above.
            trayRect.SetAsFirstSibling();
        }

        /// <summary>Destroy only the roots this helper owns.</summary>
        public static void Restore()
        {
            MfdTerrainRelief.Restore();
            DestroyOwned(ref backdrop);
            DestroyOwned(ref tray);
            backdropBaseImage = null;
            backdropGradientImage = null;
            trayBaseImage = null;
            trayGradientImage = null;

            if (terrainImage != null)
            {
                terrainImage.enabled = terrainWasEnabled;
                terrainImage.color = terrainColor;
            }
            terrainImage = null;
            if (nativeBackground != null) nativeBackground.color = nativeBackgroundColor;
            nativeBackground = null;
        }

        /// <summary>Mission-end counterpart to <see cref="Restore"/>.</summary>
        public static void Reset()
        {
            Restore();
        }

        /// <summary>
        /// Make the full-screen deck a root overlay canvas below both map and gameplay UI.
        ///
        /// The maximised map canvas has a higher sorting order than the game UI canvas. A
        /// full-screen Image as its child would therefore cover the native clock and spawn
        /// controls even when it was the first sibling. A small, non-interactive root canvas
        /// one order below the gameplay layer is the only way to keep all native surfaces
        /// visible without reparenting their layout-controlled transforms.
        /// </summary>
        private static RectTransform EnsureBackdrop(Canvas source)
        {
            Transform desiredParent = source.transform.parent;
            if (backdrop != null && backdrop.transform.parent != desiredParent)
            {
                Object.Destroy(backdrop);
                backdrop = null;
            }

            if (backdrop == null)
            {
                // Older development builds placed this root inside the map canvas. Clean up
                // only that explicitly owned legacy object before creating the safe root canvas.
                Transform legacy = source.transform.Find(BackdropName);
                if (legacy != null) Object.Destroy(legacy.gameObject);

                backdrop = new GameObject(
                    BackdropName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(Image),
                    typeof(CanvasGroup));
                backdrop.transform.SetParent(desiredParent, worldPositionStays: false);
                backdrop.layer = source.gameObject.layer;
            }

            Canvas deckCanvas = backdrop.GetComponent<Canvas>();
            deckCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            deckCanvas.overrideSorting = true;
            deckCanvas.sortingLayerID = source.sortingLayerID;
            // The known order is GameplayUI=1, MaximizedMap=2. Stay below both; retaining
            // this relative calculation also covers a future game build that shifts them.
            // Nested under SceneEssentials/Canvas Unity can clear overrideSorting; set it
            // after renderMode so the opaque deck still composites over the world camera.
            deckCanvas.sortingOrder = Mathf.Max(0, source.sortingOrder - 2);
            deckCanvas.overrideSorting = true;
            deckCanvas.targetDisplay = source.targetDisplay;

            MfdScreenFinish.CopyScaler(source.GetComponent<CanvasScaler>(), backdrop.GetComponent<CanvasScaler>());

            CanvasGroup group = backdrop.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;

            return backdrop.GetComponent<RectTransform>();
        }

        private static RectTransform EnsureCanvasRoot(ref GameObject root, string name, Canvas canvas)
        {
            if (root != null && root.transform.parent != canvas.transform)
            {
                Object.Destroy(root);
                root = null;
            }

            if (root == null)
            {
                Transform existing = canvas.transform.Find(name);
                root = existing != null ? existing.gameObject : null;
            }

            if (root == null)
            {
                root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                root.GetComponent<RectTransform>().SetParent(canvas.transform, worldPositionStays: false);
            }

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null) group = root.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;

            return root.GetComponent<RectTransform>();
        }

        private static void ConfigureBackdrop(RectTransform root)
        {
            AvLay.Fill(root);

            backdropBaseImage = root.GetComponent<Image>();
            if (backdropBaseImage == null) backdropBaseImage = root.gameObject.AddComponent<Image>();

            backdropBaseImage.sprite = null;
            backdropBaseImage.type = Image.Type.Simple;
            backdropBaseImage.raycastTarget = false;
            backdropBaseImage.enabled = true;
            AvSurfaceGradient.Apply(backdropBaseImage, Color.white, (Color.white * .72f).WithAlpha(1f));

            // Frosted glass: the blurred world over the opaque deck tint, under the screen gradient.
            if (root.GetComponentInChildren<AvFrost>(true) == null) AvFrost.Add(root, "Frost", .38f).Rect.SetAsFirstSibling();

            if (backdropGradientImage == null || backdropGradientImage.transform.parent != root)
                backdropGradientImage = CreateGradient(root, "ScreenGradient", Color.white.WithAlpha(0.38f));

            ApplyAppearance();
        }

        private static void ConfigureTray(RectTransform root, MfdLayout.Columns columns)
        {
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(
                columns.Map.width + TrayBleed * 2f,
                columns.Map.height + TrayBleed * 2f);
            root.anchoredPosition = MfdLayout.TopLeftOf(columns.Map) + new Vector2(-TrayBleed, TrayBleed);
            root.localScale = Vector3.one;

            trayBaseImage = root.GetComponent<Image>();
            if (trayBaseImage == null) trayBaseImage = root.gameObject.AddComponent<Image>();

            trayBaseImage.sprite = null;
            trayBaseImage.type = Image.Type.Simple;
            trayBaseImage.raycastTarget = false;
            trayBaseImage.enabled = true;

            if (root.childCount == 0)
                trayGradientImage = CreateGradient(root, "MapTrayGradient", Color.white.WithAlpha(0.50f));

            ApplyAppearance();
        }

        private static Image CreateGradient(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            AvLay.Fill(rt);

            Image image = go.GetComponent<Image>();
            image.sprite = AvSprites.GroundGradient;
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void DestroyOwned(ref GameObject root)
        {
            if (root != null)
            {
                // EnsureCanvasRoot can run again before Unity drains the destroy queue.
                root.SetActive(false);
                root.name += ".Retired";
                Object.Destroy(root);
            }
            root = null;
        }
    }
}
