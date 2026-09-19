using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BoscaliSummer.Features.Command.Configuration;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
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
        private const float GridCell = 64f;
        private const int MajorGridStride = 4;

        public struct WallpaperFileEntry
        {
            public string FileName;
            public string FullPath;
        }

        private static CommandSettings settings;
        private static GameObject backdrop;
        private static GameObject tray;
        private static Vector2 backdropCanvasSize;

        private static Image backdropBaseImage;
        private static Image backdropUserImage;
        private static RawImage backdropCheckerImage;
        private static Image backdropGradientImage;
        private static RectTransform backdropGridTransform;
        private static Image trayBaseImage;
        private static Image trayGradientImage;

        private static Texture2D checkerTexture;
        private static readonly Sprite[] presetSprites = new Sprite[3];
        private static Sprite customWallpaperSprite;
        private static string customWallpaperPath;
        private static string failedWallpaperPath;
        public static string WallpaperStatus { get; private set; } = "Choose CUSTOM on STYLE to use local PNG/JPEG files.";
        private static Image terrainImage;
        private static bool terrainWasEnabled;
        private static Color terrainColor;

        private static readonly List<WallpaperFileEntry> discoveredWallpapers = new List<WallpaperFileEntry>();
        private static bool scannedWallpapers;

        public static void Configure(CommandSettings config)
        {
            settings = config;
        }

        /// <summary>Apply live opacity, grid, checkerboard, and wallpaper appearance.</summary>
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
            bool showGrid = settings == null || settings.DeckGrid.Value;
            bool showChecker = settings != null && settings.CheckerboardOverlay.Value;
            float checkerOpacity = settings != null ? settings.CheckerboardOpacity.Value : 0.08f;
            bool showWallpaper = settings != null && settings.BackgroundImage.Value;
            int wallpaperPreset = settings != null ? settings.BackgroundImagePreset.Value : 0;
            float wallpaperOpacity = settings != null ? settings.BackgroundImageOpacity.Value : 0.25f;
            float mapTrayOpacity = settings != null ? settings.MapTrayOpacity.Value : 0.15f;
            int fitMode = settings != null ? settings.WallpaperFitMode.Value : 0;

            if (backdropBaseImage != null)
            {
                backdropBaseImage.color = AvTheme.Ground.WithAlpha(opacity);
            }

            if (backdropUserImage != null)
            {
                if (showWallpaper)
                {
                    Sprite sprite = GetWallpaperSprite(wallpaperPreset, out bool isTiled);
                    if (sprite != null)
                    {
                        backdropUserImage.gameObject.SetActive(true);
                        backdropUserImage.sprite = sprite;
                        RectTransform imgRt = backdropUserImage.rectTransform;
                        if (isTiled)
                        {
                            AvKit.Stretch(imgRt);
                            backdropUserImage.type = Image.Type.Tiled;
                            backdropUserImage.preserveAspect = false;
                        }
                        else
                        {
                            backdropUserImage.type = Image.Type.Simple;
                            if (fitMode == 0) // Cover (Aspect Fill)
                            {
                                backdropUserImage.preserveAspect = false;
                                ApplyAspectCover(imgRt, sprite, backdropCanvasSize);
                            }
                            else if (fitMode == 1) // Fit (Aspect Fit)
                            {
                                AvKit.Stretch(imgRt);
                                backdropUserImage.preserveAspect = true;
                            }
                            else // Stretch
                            {
                                AvKit.Stretch(imgRt);
                                backdropUserImage.preserveAspect = false;
                            }
                        }
                        backdropUserImage.color = new Color(1f, 1f, 1f, wallpaperOpacity);
                    }
                    else
                    {
                        backdropUserImage.gameObject.SetActive(false);
                    }
                }
                else
                {
                    backdropUserImage.gameObject.SetActive(false);
                }
            }

            if (backdropCheckerImage != null)
            {
                if (showChecker && checkerOpacity > 0.001f)
                {
                    backdropCheckerImage.gameObject.SetActive(true);
                    backdropCheckerImage.color = AvTheme.Frame.WithAlpha(checkerOpacity);
                    float uvW = backdropCanvasSize.x > 0f ? backdropCanvasSize.x / GridCell : 30f;
                    float uvH = backdropCanvasSize.y > 0f ? backdropCanvasSize.y / GridCell : 18f;
                    backdropCheckerImage.uvRect = new Rect(0f, 0f, uvW, uvH);
                }
                else
                {
                    backdropCheckerImage.gameObject.SetActive(false);
                }
            }

            if (backdropGradientImage != null)
            {
                backdropGradientImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity * 0.75f));
            }

            if (backdropGridTransform != null)
            {
                backdropGridTransform.gameObject.SetActive(showGrid);
            }

            if (trayBaseImage != null)
            {
                float trayAlpha = mapTrayOpacity;
                trayBaseImage.color = AvTheme.Ground.WithAlpha(trayAlpha);
            }

            if (trayGradientImage != null)
            {
                float gradAlpha = 0.35f * mapTrayOpacity;
                trayGradientImage.color = new Color(1f, 1f, 1f, gradAlpha);
            }

            // Sync DynamicMap terrain image. Map darkening is the tray behind the viewport;
            // the map bed itself has to stay opaque enough to hide the world camera.
            var dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap != null)
            {
                if (dynamicMap.mapImage != null)
                {
                    Image terrainImg = dynamicMap.mapImage.GetComponent<Image>();
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
                    dynamicMap.mapBackground.color = new Color(1f, 1f, 1f, 0.68f);
            }
        }

        private static void ApplyAspectCover(RectTransform rt, Sprite sprite, Vector2 canvasSize)
        {
            if (rt == null || sprite == null || canvasSize.x <= 1f || canvasSize.y <= 1f) return;
            float canvasAspect = canvasSize.x / canvasSize.y;
            float spriteAspect = (float)sprite.rect.width / Mathf.Max(1f, sprite.rect.height);

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            if (spriteAspect >= canvasAspect)
            {
                float h = canvasSize.y;
                float w = h * spriteAspect;
                rt.sizeDelta = new Vector2(w, h);
            }
            else
            {
                float w = canvasSize.x;
                float h = w / spriteAspect;
                rt.sizeDelta = new Vector2(w, h);
            }
        }

        /// <summary>Build or update the two canvas-level passive layers.</summary>
        public static void Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return;

            // The nested map canvas can report a stale rect on the first open; the backdrop
            // grid must span the real UI area, so resolve it the same way the layout does.
            Vector2 canvasSize = MfdLayout.CanvasSize(canvas);
            if (canvasSize.x <= 1f || canvasSize.y <= 1f) return;

            RectTransform backdropRect = EnsureBackdrop(canvas);
            RectTransform trayRect = EnsureCanvasRoot(ref tray, TrayName, canvas);
            if (backdropRect == null || trayRect == null) return;

            ConfigureBackdrop(backdropRect, canvasSize);
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
            DestroyOwned(ref backdrop);
            DestroyOwned(ref tray);
            backdropBaseImage = null;
            backdropUserImage = null;
            backdropCheckerImage = null;
            backdropGradientImage = null;
            backdropGridTransform = null;
            trayBaseImage = null;
            trayGradientImage = null;
            backdropCanvasSize = Vector2.zero;

            if (terrainImage != null)
            {
                terrainImage.enabled = terrainWasEnabled;
                terrainImage.color = terrainColor;
            }
            terrainImage = null;
        }

        /// <summary>Mission-end counterpart to <see cref="Restore"/>.</summary>
        public static void Reset()
        {
            Restore();
            UnloadCustomWallpaper();
            failedWallpaperPath = null;
            discoveredWallpapers.Clear();
            scannedWallpapers = false;
            for (int i = 0; i < presetSprites.Length; i++)
            {
                if (presetSprites[i] == null) continue;
                Object.Destroy(presetSprites[i].texture);
                Object.Destroy(presetSprites[i]);
                presetSprites[i] = null;
            }
            if (checkerTexture != null) Object.Destroy(checkerTexture);
            checkerTexture = null;
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

            CopyScaler(source.GetComponent<CanvasScaler>(), backdrop.GetComponent<CanvasScaler>());

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

        private static void CopyScaler(CanvasScaler source, CanvasScaler destination)
        {
            if (destination == null) return;

            if (source == null)
            {
                destination.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                destination.referenceResolution = new Vector2(1920f, 1080f);
                destination.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                destination.matchWidthOrHeight = 0.5f;
                return;
            }

            destination.uiScaleMode = source.uiScaleMode;
            destination.scaleFactor = source.scaleFactor;
            destination.referenceResolution = source.referenceResolution;
            destination.screenMatchMode = source.screenMatchMode;
            destination.matchWidthOrHeight = source.matchWidthOrHeight;
            destination.referencePixelsPerUnit = source.referencePixelsPerUnit;
        }

        private static void ConfigureBackdrop(RectTransform root, Vector2 canvasSize)
        {
            AvKit.Stretch(root);

            backdropBaseImage = root.GetComponent<Image>();
            if (backdropBaseImage == null) backdropBaseImage = root.gameObject.AddComponent<Image>();

            backdropBaseImage.sprite = null;
            backdropBaseImage.type = Image.Type.Simple;
            backdropBaseImage.raycastTarget = false;
            backdropBaseImage.enabled = true;

            if (Approximately(backdropCanvasSize, canvasSize) && root.childCount > 0)
            {
                ApplyAppearance();
                return;
            }

            ClearChildren(root);
            backdropCanvasSize = canvasSize;

            backdropUserImage = CreateUserImageLayer(root, "UserImageLayer");
            backdropCheckerImage = CreateCheckerLayer(root, "CheckerboardOverlay");
            backdropGradientImage = CreateGradient(root, "ScreenGradient", new Color(1f, 1f, 1f, 0.38f));
            backdropGridTransform = CreateLayer(root, "DatumGrid");
            BuildDatumGrid(backdropGridTransform, canvasSize);

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
                trayGradientImage = CreateGradient(root, "MapTrayGradient", new Color(1f, 1f, 1f, 0.50f));

            ApplyAppearance();
        }

        private static void BuildDatumGrid(RectTransform grid, Vector2 size)
        {
            float width = Mathf.Ceil(size.x);
            float height = Mathf.Ceil(size.y);
            Color minor = AvTheme.Hairline.WithAlpha(0.09f);
            Color major = AvTheme.Frame.WithAlpha(0.20f);

            for (int x = 0; x <= Mathf.CeilToInt(width); x += (int)GridCell)
            {
                int index = x / (int)GridCell;
                AvKit.Rule(grid, new Rect(x, 0f, 1f, height),
                    index % MajorGridStride == 0 ? major : minor);
            }

            for (int y = 0; y <= Mathf.CeilToInt(height); y += (int)GridCell)
            {
                int index = y / (int)GridCell;
                AvKit.Rule(grid, new Rect(0f, -y, width, 1f),
                    index % MajorGridStride == 0 ? major : minor);
            }

            // One restrained screen boundary makes the surrounding native controls feel
            // intentionally seated on the deck, without competing with MapFrame's bezel.
            var inset = new Rect(8f, -8f, Mathf.Max(0f, width - 16f), Mathf.Max(0f, height - 16f));
            AvKit.Outline(grid, inset, AvTheme.Frame.WithAlpha(0.30f));
        }

        private static Image CreateGradient(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            AvKit.Stretch(rt);

            Image image = go.GetComponent<Image>();
            image.sprite = AvSprites.GroundGradient;
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image CreateUserImageLayer(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            AvKit.Stretch(rt);

            Image image = go.GetComponent<Image>();
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }

        private static RawImage CreateCheckerLayer(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            AvKit.Stretch(rt);

            RawImage raw = go.GetComponent<RawImage>();
            raw.texture = GetCheckerTexture();
            raw.raycastTarget = false;
            go.SetActive(false);
            return raw;
        }

        private static Texture2D GetCheckerTexture()
        {
            if (checkerTexture != null) return checkerTexture;
            checkerTexture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "Avionics_Checkerboard",
                hideFlags = HideFlags.HideAndDontSave
            };
            Color light = new Color(1f, 1f, 1f, 1f);
            Color dark = new Color(0f, 0f, 0f, 0f);
            checkerTexture.SetPixel(0, 0, light);
            checkerTexture.SetPixel(1, 0, dark);
            checkerTexture.SetPixel(0, 1, dark);
            checkerTexture.SetPixel(1, 1, light);
            checkerTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return checkerTexture;
        }

        private static Sprite GetWallpaperSprite(int preset, out bool isTiled)
        {
            isTiled = true;
            if (preset >= 0 && preset < presetSprites.Length)
            {
                if (presetSprites[preset] == null)
                    presetSprites[preset] = CreatePresetSprite(preset);
                return presetSprites[preset];
            }

            if (preset == 3)
            {
                return LoadCustomWallpaper(out isTiled);
            }

            return null;
        }

        private static Sprite CreatePresetSprite(int preset)
        {
            int size = preset == 1 ? 16 : (preset == 2 ? 64 : 32);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                name = "Avionics_WallpaperPreset_" + preset,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color pixel = clear;
                    if (preset == 0) // Hexagon / geometric matrix
                    {
                        int hx = (x + (y % 16 < 8 ? 0 : 8)) % 16;
                        int hy = y % 8;
                        bool edge = hx == 0 || hy == 0 || (hx + hy) == 7 || ((hx - hy + 8) % 8 == 0);
                        if (edge) pixel = new Color(0.25f, 0.70f, 0.45f, 0.45f);
                    }
                    else if (preset == 1) // Carbon micro-weave
                    {
                        bool block = ((x / 4) + (y / 4)) % 2 == 0;
                        bool stripe = (x % 2 == 0);
                        pixel = block ^ stripe
                            ? new Color(0.12f, 0.28f, 0.20f, 0.60f)
                            : new Color(0.04f, 0.08f, 0.06f, 0.45f);
                    }
                    else if (preset == 2) // Radar sweep rings & crosshairs
                    {
                        float dx = x - 31.5f;
                        float dy = y - 31.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        bool ring = Mathf.Abs(d - 10f) < 0.9f || Mathf.Abs(d - 20f) < 0.9f || Mathf.Abs(d - 30f) < 0.9f;
                        bool cross = (Mathf.Abs(dx) < 0.7f && d <= 31f) || (Mathf.Abs(dy) < 0.7f && d <= 31f);
                        if (ring || cross) pixel = new Color(0.20f, 0.85f, 0.50f, 0.50f);
                    }
                    tex.SetPixel(x, y, pixel);
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                32f,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = "Avionics_WallpaperSprite_" + preset;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public static int DiscoveredWallpaperCount
        {
            get
            {
                EnsureWallpapersScanned();
                return discoveredWallpapers.Count;
            }
        }

        public static string GetCurrentWallpaperFileName()
        {
            EnsureWallpapersScanned();
            if (discoveredWallpapers.Count == 0) return "NONE FOUND";
            string current = settings?.CustomWallpaperFile?.Value;
            for (int i = 0; i < discoveredWallpapers.Count; i++)
            {
                if (string.Equals(discoveredWallpapers[i].FileName, current, StringComparison.OrdinalIgnoreCase))
                    return discoveredWallpapers[i].FileName;
            }
            return discoveredWallpapers[0].FileName;
        }

        public static void CycleCustomWallpaper(int delta)
        {
            EnsureWallpapersScanned();
            if (discoveredWallpapers.Count == 0 || settings == null) return;

            string current = settings.CustomWallpaperFile.Value;
            int index = 0;
            for (int i = 0; i < discoveredWallpapers.Count; i++)
            {
                if (string.Equals(discoveredWallpapers[i].FileName, current, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            int newIndex = (index + delta + discoveredWallpapers.Count) % discoveredWallpapers.Count;
            settings.CustomWallpaperFile.Value = discoveredWallpapers[newIndex].FileName;
            UnloadCustomWallpaper();
            ApplyAppearance();
        }

        public static void RescanWallpapers()
        {
            EnsureWallpapersScanned(forceRescan: true);
            UnloadCustomWallpaper();
            failedWallpaperPath = null;
            ApplyAppearance();
        }

        public static void EnsureWallpapersScanned(bool forceRescan = false)
        {
            if (scannedWallpapers && !forceRescan) return;
            scannedWallpapers = true;
            discoveredWallpapers.Clear();

            string primaryDir = Path.Combine(Paths.ConfigPath, "BoscaliSummer", "wallpapers");
            try
            {
                if (!Directory.Exists(primaryDir))
                {
                    Directory.CreateDirectory(primaryDir);
                    string readme = Path.Combine(primaryDir, "README.txt");
                    if (!File.Exists(readme))
                    {
                        File.WriteAllText(readme,
                            "Boscali Summer - Tactical Map Wallpapers\r\n" +
                            "========================================\r\n\r\n" +
                            "Place your custom .png, .jpg, or .jpeg images here.\r\n" +
                            "Recommended resolution: 1920x1080 (or your display resolution).\r\n" +
                            "Select them in-game via the MFD SET (Settings) panel.\r\n");
                    }
                }
            }
            catch { }

            string[] candidateDirs = {
                primaryDir,
                Path.Combine(Paths.ConfigPath, "BoscaliSummer", "Backgrounds"),
                Path.Combine(Paths.ConfigPath, "WingCommand", "Backgrounds"),
                Path.Combine(Paths.ConfigPath, "BoscaliSummer"),
                Path.Combine(Paths.PluginPath, "BoscaliSummer", "wallpapers"),
                Path.Combine(Paths.PluginPath, "BoscaliSummer", "Backgrounds"),
                Path.Combine(Paths.PluginPath, "BoscaliSummer")
            };

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int visited = 0;
            for (int d = 0; d < candidateDirs.Length && visited < 512; d++)
            {
                string dir = candidateDirs[d];
                if (!Directory.Exists(dir)) continue;

                try
                {
                    foreach (string path in Directory.EnumerateFiles(dir))
                    {
                        if (++visited > 512) break;
                        string ext = Path.GetExtension(path)?.ToLowerInvariant();
                        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;

                        string name = Path.GetFileName(path);
                        if (seenNames.Add(name))
                        {
                            discoveredWallpapers.Add(new WallpaperFileEntry
                            {
                                FileName = name,
                                FullPath = path
                            });
                        }
                    }
                }
                catch { }
            }

            discoveredWallpapers.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
        }

        private static void UnloadCustomWallpaper()
        {
            if (customWallpaperSprite != null)
            {
                if (customWallpaperSprite.texture != null)
                    Object.Destroy(customWallpaperSprite.texture);
                Object.Destroy(customWallpaperSprite);
                customWallpaperSprite = null;
                customWallpaperPath = null;
            }
        }

        private static Sprite LoadCustomWallpaper(out bool isTiled)
        {
            isTiled = false;
            EnsureWallpapersScanned();
            if (discoveredWallpapers.Count == 0)
            {
                WallpaperStatus = "No images found. Add PNG/JPEG files to BepInEx/config/BoscaliSummer/wallpapers, then RESCAN.";
                return null;
            }

            string targetName = settings?.CustomWallpaperFile?.Value;
            WallpaperFileEntry selected = default;
            bool found = false;

            if (!string.IsNullOrEmpty(targetName))
            {
                for (int i = 0; i < discoveredWallpapers.Count; i++)
                {
                    if (string.Equals(discoveredWallpapers[i].FileName, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = discoveredWallpapers[i];
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                selected = discoveredWallpapers[0];
                if (settings != null) settings.CustomWallpaperFile.Value = selected.FileName;
            }

            if (customWallpaperSprite != null && customWallpaperPath == selected.FullPath)
                return customWallpaperSprite;

            if (failedWallpaperPath == selected.FullPath) return null;
            UnloadCustomWallpaper();
            failedWallpaperPath = selected.FullPath;
            WallpaperStatus = "Image unavailable or unsupported. Limit: 16 MB, 4096 pixels per side. Replace it and RESCAN.";
            Texture2D tex = null;
            try
            {
                byte[] data;
                using (var stream = File.OpenRead(selected.FullPath))
                {
                    if (stream.Length > 16 * 1024 * 1024) return null;
                    data = new byte[(int)stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int count = stream.Read(data, read, data.Length - read);
                        if (count == 0) return null;
                        read += count;
                    }
                }
                if (!SettingsChoices.SupportedImage(data)) return null;
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!ImageConversion.LoadImage(tex, data, markNonReadable: true))
                {
                    Object.Destroy(tex);
                    return null;
                }
                customWallpaperSprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0u,
                    SpriteMeshType.FullRect);
                customWallpaperSprite.hideFlags = HideFlags.HideAndDontSave;
                customWallpaperPath = selected.FullPath;
                failedWallpaperPath = null;
                WallpaperStatus = "Loaded " + selected.FileName + ". RESCAN reloads replaced files.";
                return customWallpaperSprite;
            }
            catch
            {
                if (tex != null) Object.Destroy(tex);
                UnloadCustomWallpaper();
                return null;
            }
        }

        private static RectTransform CreateLayer(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            AvKit.Stretch(rt);
            return rt;
        }

        private static void ClearChildren(RectTransform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.Destroy(root.GetChild(i).gameObject);
            backdropUserImage = null;
            backdropCheckerImage = null;
            backdropGradientImage = null;
            backdropGridTransform = null;
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y);

        private static void DestroyOwned(ref GameObject root)
        {
            if (root != null) Object.Destroy(root);
            root = null;
        }
    }
}
