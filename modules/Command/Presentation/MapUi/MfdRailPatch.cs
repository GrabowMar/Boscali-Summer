using System.Collections.Generic;
using HarmonyLib;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Lays the maximised tactical map out in three columns: mod panels on the left, the map
    /// in the centre, and one thin rail of buttons on the right.
    ///
    /// <para>The stock screen puts a 900×900 map square in the middle of the canvas with a
    /// column of bezel buttons down each side, which leaves roughly 500 units of dead space
    /// on both flanks and no coherent place for a mod panel. The previous version of this
    /// file tried to solve that by turning the two bezel columns into rows above and below
    /// the map; the result read as scattered, and it was answering a question nobody asked —
    /// the columns were never in the map's way. This lays out all three regions instead.</para>
    ///
    /// <para><b>Everything here is reversible.</b> Vanilla state is snapshotted on the first
    /// maximise and restored on minimise, so a player who turns the setting off, or removes
    /// the mod, gets the stock screen back exactly.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class MfdRailPatch
    {
        /// <summary>Extra size the map's background carries over the map viewport, as vanilla does.</summary>
        private const float BackgroundBleed = 20f;

        private sealed class MapSnapshot
        {
            public RectTransform Root;
            public Vector2 RootSize;
            public Vector2 RootAnchoredPosition;
            public Vector2 RootAnchorMin;
            public Vector2 RootAnchorMax;
            public Vector2 RootPivot;

            public RectTransform Background;
            public Vector2 BackgroundSize;
            public Image BackgroundImage;
            public Sprite BackgroundSprite;
            public Image.Type BackgroundType;
            public Color BackgroundColor;

            public void Restore()
            {
                if (Root != null)
                {
                    Root.anchorMin = RootAnchorMin;
                    Root.anchorMax = RootAnchorMax;
                    Root.pivot = RootPivot;
                    Root.sizeDelta = RootSize;
                    Root.anchoredPosition = RootAnchoredPosition;
                }

                if (Background != null) Background.sizeDelta = BackgroundSize;

                if (BackgroundImage != null)
                {
                    BackgroundImage.sprite = BackgroundSprite;
                    BackgroundImage.type = BackgroundType;
                    BackgroundImage.color = BackgroundColor;
                }
            }

            public void RestoreFraming(DynamicMap dynamicMap)
            {
                if (Root != null)
                {
                    Root.anchorMin = new Vector2(0.5f, 0.5f);
                    Root.anchorMax = new Vector2(0.5f, 0.5f);
                    Root.pivot = new Vector2(0.5f, 0.5f);
                    Root.localPosition = Vector3.zero;
                    Root.localRotation = Quaternion.identity;
                    Root.localScale = Vector3.one;
                    if (dynamicMap != null) Root.sizeDelta = Vector2.one * dynamicMap.mapScaleMinimized;
                }

                if (Background != null && dynamicMap != null)
                {
                    Background.sizeDelta = Vector2.one * dynamicMap.mapScaleMinimized + new Vector2(20f, 20f);
                    Background.localPosition = Vector3.zero;
                    Background.localScale = Vector3.one;
                }

                if (BackgroundImage != null)
                {
                    BackgroundImage.sprite = BackgroundSprite;
                    BackgroundImage.type = BackgroundType;
                    BackgroundImage.color = BackgroundColor;
                }

                if (dynamicMap != null)
                {
                    dynamicMap.mapScaleCurrent = dynamicMap.mapScaleMinimized;
                    dynamicMap.mapDisplayFactor = dynamicMap.mapScaleMaximized / dynamicMap.mapDimension;
                    var centerMethod = AccessTools.Method(typeof(DynamicMap), "CenterMinimizedMap");
                    centerMethod?.Invoke(dynamicMap, null);
                }
            }
        }

        private sealed class ButtonSnapshot
        {
            public Button Button;
            public MFDScreen Screen;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public MfdRail.ButtonSkin Skin;

            public void Restore()
            {
                Skin?.Restore();

                if (Button == null) return;

                Transform t = Button.transform;
                t.SetParent(Parent, worldPositionStays: false);
                t.localPosition = LocalPosition;
                t.localRotation = LocalRotation;
                t.localScale = LocalScale;
            }
        }

        /// <summary>
        /// A bezel column's backdrop, hidden once the rail has emptied it.
        ///
        /// LeftButtons and RightButtons each carry their own Image. Reparenting the buttons
        /// out leaves those two panels behind as small dark rectangles floating either side
        /// of the map — the frame of a bezel that no longer has anything in it.
        /// </summary>
        private sealed class ContainerSkin
        {
            public Image Backdrop;
            public bool WasEnabled;

            public void Restore()
            {
                if (Backdrop != null) Backdrop.enabled = WasEnabled;
            }
        }

        private static readonly List<ButtonSnapshot> buttons = new List<ButtonSnapshot>();
        private static readonly List<ContainerSkin> containers = new List<ContainerSkin>();

        /// <summary>Hairline outline and corner ticks around the map viewport, so the three
        /// columns read as one instrument rather than a map dropped between two panels.</summary>
        private static GameObject mapFrame;
        private const string MapFrameName = "NOAvionics.MapFrame";

        /// <summary>
        /// Bezel buttons the rail declined, hidden while it owns the layout.
        ///
        /// The stock bezel has six slots a side with about three filled; a spare is a live
        /// button labelled "-" that does nothing. Leaving them behind put three dark empty
        /// boxes across the map where the old columns used to be. Vanilla re-activates them
        /// on every maximise, so this runs after that and hides them again.
        /// </summary>
        private static readonly List<GameObject> spares = new List<GameObject>();
        private static MapSnapshot map;
        private static bool applied;
        private static Vector2 appliedCanvasSize;
        private static float currentPanelWidth = AvTokens.PanelWidth;

        /// <summary>
        /// The UI area the applied layout was resolved against, exposed so
        /// <see cref="MapUiManager"/> can re-apply when the live canvas size diverges.
        /// </summary>
        public static Vector2 AppliedCanvasSize => appliedCanvasSize;

        public static void Reconcile()
        {
            if (!DynamicMap.mapMaximized)
            {
                MfdScreenFinish.Restore();
                return;
            }
            if (DynamicMap.mapMaximized && applied != MfdPresentation.Expanded)
                MaximizePostfix(SceneSingleton<DynamicMap>.i);
            else if (DynamicMap.mapMaximized && applied)
            {
                var map = SceneSingleton<DynamicMap>.i;
                VirtualMFD mfd = MapMfdLookup.Resolve(map == null ? null : map.maximizedMapCanvas);
                MfdPanelDock.DockModScreens(mfd);
                ReLayoutForActiveScreen(MfdSinglePanelPatch.ActiveScreen);
                RefreshLatched();
            }
            DynamicMap display = SceneSingleton<DynamicMap>.i;
            MfdScreenFinish.Ensure(display == null ? null : display.maximizedMapCanvas);
        }

        public static void Refresh(DynamicMap dynamicMap)
        {
            Restore();
            MaximizePostfix(dynamicMap);
        }

        /// <summary>
        /// Install or update layout when a new MFD screen appears. Does not tear down an
        /// already-applied layout — Restore+rebuild was dropping docked screens back onto
        /// the gameplay canvas.
        ///
        /// <para>The rail is rebuilt here too: a screen can appear or disappear after the
        /// first maximise — a hosted EVN/ADM button, ADM tearing down when a second client
        /// connects, a panel that lost its first race — and its button has to be branded
        /// (or dropped) without waiting for the next map open.</para>
        /// </summary>
        public static void OnStructureChanged(DynamicMap dynamicMap)
        {
            if (dynamicMap == null) return;
            if (!applied)
            {
                MaximizePostfix(dynamicMap);
                return;
            }

            Canvas canvas = dynamicMap.maximizedMapCanvas;
            VirtualMFD mfd = MapMfdLookup.Resolve(canvas);
            if (mfd != null && MfdLayout.TryResolve(canvas, out MfdLayout.Columns columns, currentPanelWidth))
            {
                int activeButtons = MfdRail.Count(MapUiAccess.GetLeftButtons(mfd), MapUiAccess.GetLeftScreens(mfd)) +
                                    MfdRail.Count(MapUiAccess.GetRightButtons(mfd), MapUiAccess.GetRightScreens(mfd));
                if (MfdRail.PrepareCapacity(columns.Rail.height, activeButtons))
                    BuildRail(canvas, columns);
            }

            MfdPanelDock.DockModScreens(mfd);
            ReLayout(currentPanelWidth);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
        public static void MaximizePostfix(DynamicMap __instance)
        {
            if (__instance == null) return;

            if (!MfdPresentation.Expanded)
            {
                // Restoring here happens with the map still maximised, so the map rect does
                // have to be put back — unlike on Minimize, where vanilla has already done it.
                Restore(onMinimize: false);
                // The one faction key still needs its two-HQ selector in stock layout.
                VanillaMfdRebuild.TryApply(FactionMfdMergePatch.Screen);
                MfdScreenFinish.Ensure(__instance.maximizedMapCanvas);
                return;
            }

            Canvas canvas = __instance.maximizedMapCanvas;
            MfdScreenFinish.Ensure(canvas);
            float initialWidth = MfdSinglePanelPatch.ActiveScreen != null && MfdSinglePanelPatch.ActiveScreen.isActive
                ? MfdPanelDock.VisibleWidth(MfdSinglePanelPatch.ActiveScreen)
                : AvTokens.PanelWidth;
            currentPanelWidth = initialWidth;
            if (!MfdLayout.TryResolve(canvas, out MfdLayout.Columns columns, initialWidth)) return;

            VirtualMFD mfd = MapMfdLookup.Resolve(canvas);
            if (mfd == null) return;
            List<Button> leftButtons = MapUiAccess.GetLeftButtons(mfd);
            List<Button> rightButtons = MapUiAccess.GetRightButtons(mfd);
            int activeButtons = MfdRail.Count(leftButtons, MapUiAccess.GetLeftScreens(mfd)) +
                                MfdRail.Count(rightButtons, MapUiAccess.GetRightScreens(mfd));
            if (!MfdRail.PrepareCapacity(columns.Rail.height, activeButtons))
            {
                Plugin.Logger.LogWarning(
                    "Expanded map kept the stock bezel: " + activeButtons +
                    " active MFD buttons do not fit the rail at this canvas height.");
                return;
            }

            MfdPresentation.Capture(mfd);

            ResizeMap(__instance, columns);
            BuildRail(canvas, columns);
            DockPanels(canvas, columns);
            MfdMapDeck.Ensure(canvas, columns);
            EnsureFooter(canvas, columns);
            EnsureNewsTicker(canvas, columns);
            EnsureLogPanel(canvas, columns);
            MfdScreenFinish.Ensure(canvas);

            applied = true;
            appliedCanvasSize = columns.Canvas;
            Plugin.Logger.LogDebug("Boscali tactical map layout installed.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
        public static void MinimizePostfix(DynamicMap __instance)
        {
            // Vanilla hides every screen on this path; enforce the invariant in case a
            // custom screen's close callback is skipped, then restore the layout.
            MfdPanelDock.SyncSurfaceVisibility(__instance == null
                ? null
                : MapMfdLookup.Resolve(__instance.maximizedMapCanvas));
            Restore(onMinimize: true, dynamicMap: __instance);
        }

        // ------------------------------------------------------------------------- map

        /// <summary>
        /// Move the map into the centre column.
        ///
        /// Only the *viewport* changes. The map root carries a <c>RectMask2D</c> over a much
        /// larger content image, so resizing its rect shows more map at the same scale rather
        /// than scaling what is there. That distinction matters: <c>mapScaleMaximized</c> is
        /// public and looks like the obvious lever, but the value 900 is also written as a
        /// literal inside <c>GetCursorCoordinates</c> and <c>LoadMapImage</c>. Changing the
        /// field moves <c>mapDisplayFactor</c> — and therefore every icon — while those two
        /// literals stay put, so icons and right-click waypoints would land at the wrong
        /// world coordinates. Changing the rect leaves every coordinate path consistent.
        ///
        /// Cursor hit-testing and pan clamping both read the background rect, so they follow
        /// this for free.
        /// </summary>
        private static void ResizeMap(DynamicMap dynamicMap, MfdLayout.Columns columns)
        {
            // The private mapRectTransform is this component's own RectTransform, and the
            // private backgroundRectTransform is the public mapBackground's — so the whole
            // relayout needs no reflection.
            var root = dynamicMap.GetComponent<RectTransform>();
            RectTransform background = dynamicMap.mapBackground != null
                ? dynamicMap.mapBackground.rectTransform
                : null;
            if (root == null) return;

            if (map == null)
            {
                map = new MapSnapshot
                {
                    Root = root,
                    RootSize = root.sizeDelta,
                    RootAnchoredPosition = root.anchoredPosition,
                    RootAnchorMin = root.anchorMin,
                    RootAnchorMax = root.anchorMax,
                    RootPivot = root.pivot,
                    Background = background,
                    BackgroundSize = background != null ? background.sizeDelta : Vector2.zero,
                    BackgroundImage = dynamicMap.mapBackground,
                    BackgroundSprite = dynamicMap.mapBackground != null ? dynamicMap.mapBackground.sprite : null,
                    BackgroundType = dynamicMap.mapBackground != null ? dynamicMap.mapBackground.type : Image.Type.Simple,
                    BackgroundColor = dynamicMap.mapBackground != null ? dynamicMap.mapBackground.color : Color.white,
                };
            }

            var size = new Vector2(columns.Map.width, columns.Map.height);

            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = size;
            root.anchoredPosition = MfdLayout.CentreOf(columns.Map);

            if (background != null)
            {
                background.sizeDelta = size + new Vector2(BackgroundBleed, BackgroundBleed);

                // MapFrame owns the viewport bezel, so the map bed needs only the unframed
                // ground fade. The generated sprite already contains theme-ground pixels;
                // tinting it with AvTheme.Ground again would multiply those colours into the
                // muddy blue-green stain the stock screen used to show.
                Image ground = dynamicMap.mapBackground;
                ground.sprite = AvSprites.GroundGradient;
                ground.type = Image.Type.Simple;
                ground.color = new Color(1f, 1f, 1f, 0.68f);
            }

            // Nothing but ClampToMapEdge reads mapScaleCurrent, and it uses it as a radius.
            // The shorter side is the honest answer for a non-square viewport.
            dynamicMap.mapScaleCurrent = Mathf.Min(size.x, size.y);

            EnsureMapFrame(dynamicMap.maximizedMapCanvas, columns);
            Patches.GridLabelsPatch.RepositionCornerReadouts(dynamicMap.gridLabels);
        }

        /// <summary>
        /// Draw (or move) the map viewport's frame: a 1px hairline outline and four corner
        /// ticks, matching the panel column's own frame. A canvas child rather than a child
        /// of the map root, whose <c>RectMask2D</c> would clip it.
        /// </summary>
        private static void EnsureMapFrame(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return;

            if (mapFrame == null)
            {
                Transform existing = canvas.transform.Find(MapFrameName);
                mapFrame = existing != null ? existing.gameObject : null;
            }

            if (mapFrame == null)
            {
                mapFrame = new GameObject(MapFrameName, typeof(RectTransform));
                mapFrame.GetComponent<RectTransform>().SetParent(canvas.transform, worldPositionStays: false);
            }
            else
            {
                for (int i = mapFrame.transform.childCount - 1; i >= 0; i--)
                    Object.Destroy(mapFrame.transform.GetChild(i).gameObject);
            }

            var rt = mapFrame.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(columns.Map.width, columns.Map.height);
            rt.anchoredPosition = MfdLayout.TopLeftOf(columns.Map);
            rt.localScale = Vector3.one;

            var area = new Rect(0f, 0f, columns.Map.width, columns.Map.height);
            AvKit.Outline(rt, area, AvTheme.Hairline);
            AvKit.CornerTicks(rt, area, AvTheme.Hairline);

            rt.SetAsLastSibling();
        }

        // ------------------------------------------------------------------------ rail

        private static void BuildRail(Canvas canvas, MfdLayout.Columns columns)
        {
            VirtualMFD mfd = MapMfdLookup.Resolve(canvas);
            if (mfd == null) return;

            MfdRail.Ensure(canvas, columns);

            List<Button> left = MapUiAccess.GetLeftButtons(mfd);
            List<Button> right = MapUiAccess.GetRightButtons(mfd);

            if (buttons.Count == 0)
            {
                Snapshot(left);
                Snapshot(right);
            }

            // Both vanilla columns become one rail, ordered by the catalog so the faction
            // pair leads it. The stock parents carry a VerticalLayoutGroup that would
            // overwrite any position written to a button while it is still inside them, so
            // reparenting out is what makes placement stick.
            var skins = new List<MfdRail.ButtonSkin>();
            MfdRail.Adopt(left, MapUiAccess.GetLeftScreens(mfd),
                          right, MapUiAccess.GetRightScreens(mfd), skins);

            AttachSkins(skins);
            LinkScreens(left, MapUiAccess.GetLeftScreens(mfd));
            LinkScreens(right, MapUiAccess.GetRightScreens(mfd));
            RefreshLatched();
            HideSpares(left, MapUiAccess.GetLeftScreens(mfd));
            HideSpares(right, MapUiAccess.GetRightScreens(mfd));
            HideEmptiedContainers();
        }

        private static void Snapshot(List<Button> source)
        {
            if (source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                Button button = source[i];
                if (button == null) continue;

                Transform t = button.transform;
                buttons.Add(new ButtonSnapshot
                {
                    Button = button,
                    Parent = t.parent,
                    LocalPosition = t.localPosition,
                    LocalRotation = t.localRotation,
                    LocalScale = t.localScale,
                });
            }
        }

        /// <summary>Hide every bezel button that has no screen behind it.</summary>
        private static void HideSpares(List<Button> source, List<MFDScreen> screens)
        {
            if (source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                Button button = source[i];
                if (button == null) continue;

                bool drivesAScreen = screens != null && i < screens.Count && screens[i] != null;
                if (drivesAScreen) continue;
                if (!button.gameObject.activeSelf) continue;

                if (!spares.Contains(button.gameObject)) spares.Add(button.gameObject);
                button.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Hide the backdrop of any bezel container the rail has emptied.
        ///
        /// Only containers that actually lost every button are hidden, so a column the rail
        /// declined to adopt from keeps its frame.
        /// </summary>
        private static void HideEmptiedContainers()
        {
            if (containers.Count > 0) return;

            for (int i = 0; i < buttons.Count; i++)
            {
                Transform parent = buttons[i].Parent;
                if (parent == null) continue;
                if (StillHoldsAButton(parent)) continue;

                Image backdrop = parent.GetComponent<Image>();
                if (backdrop == null || !backdrop.enabled) continue;
                if (AlreadyTracked(backdrop)) continue;

                containers.Add(new ContainerSkin { Backdrop = backdrop, WasEnabled = true });
                backdrop.enabled = false;
            }
        }

        private static bool StillHoldsAButton(Transform container)
        {
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);

                // Spares left in place but hidden do not count: a container holding only
                // those is empty as far as anything on screen is concerned.
                if (!child.gameObject.activeSelf) continue;
                if (child.GetComponent<Button>() != null) return true;
            }
            return false;
        }

        private static bool AlreadyTracked(Image backdrop)
        {
            for (int i = 0; i < containers.Count; i++)
                if (containers[i].Backdrop == backdrop) return true;
            return false;
        }

        /// <summary>Pair each restyled button with the snapshot that has to undo it.</summary>
        private static void AttachSkins(List<MfdRail.ButtonSkin> skins)
        {
            for (int i = 0; i < skins.Count; i++)
            {
                MfdRail.ButtonSkin skin = skins[i];
                if (skin == null || skin.Background == null) continue;

                for (int j = 0; j < buttons.Count; j++)
                {
                    if (buttons[j].Button != null &&
                        buttons[j].Button.gameObject == skin.Background.gameObject)
                    {
                        buttons[j].Skin = skin;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Remember which screen each adopted button opens, so the rail can show which one
        /// is currently on the panel — vanilla has no state for a button that stays lit.
        /// </summary>
        private static void LinkScreens(List<Button> source, List<MFDScreen> screens)
        {
            if (source == null || screens == null) return;

            for (int i = 0; i < source.Count && i < screens.Count; i++)
            {
                Button button = source[i];
                if (button == null || screens[i] == null) continue;

                for (int j = 0; j < buttons.Count; j++)
                {
                    if (buttons[j].Button != button) continue;
                    buttons[j].Screen = screens[i];
                    break;
                }
            }
        }

        /// <summary>Run the rail's lit state after whatever opened or closed a screen.</summary>
        private static void RefreshLatched()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                ButtonSnapshot snapshot = buttons[i];
                if (snapshot == null || snapshot.Skin == null) continue;

                MFDScreen screen = snapshot.Screen;
                snapshot.Skin.SetLatched(screen != null && screen.isActive);
                // The game rewrites every bezel label whenever a faction panel refreshes
                // (VirtualMFD.SetupButtons); the rail owns its buttons and puts its line back.
                snapshot.Skin.Reassert();
            }
        }

        // ---------------------------------------------------------------------- panels

        private static void DockPanels(Canvas canvas, MfdLayout.Columns columns)
        {
            VirtualMFD mfd = MapMfdLookup.Resolve(canvas);
            if (mfd == null) return;

            MfdPanelDock.Ensure(canvas, columns);
            MfdPanelDock.DockModScreens(mfd);
            MfdSinglePanelPatch.Reconcile(mfd);
        }

        /// <summary>
        /// Adapts column geometry to the active screen's visible width, or the token fallback.
        /// </summary>
        public static void ReLayoutForActiveScreen(MFDScreen screen)
        {
            float targetWidth = screen != null && screen.isActive
                ? MfdPanelDock.VisibleWidth(screen)
                : AvTokens.PanelWidth;
            ReLayoutIfNeeded(targetWidth);
        }

        public static void ReLayoutIfNeeded(float panelWidth)
        {
            if (!applied || map == null || map.Root == null) return;
            if (Mathf.Abs(currentPanelWidth - panelWidth) < 1f) return;
            currentPanelWidth = panelWidth;
            ReLayout(panelWidth);
        }

        /// <summary>
        /// Re-resolve grid and update map, rail and panel dock when the panel column widens.
        /// </summary>
        public static void ReLayout(float panelWidth)
        {
            if (!applied || map == null || map.Root == null) return;

            // The map root's nearest Canvas is the viewport itself (DynamicMap sits on
            // MapCanvas), and this code has already resized that rect to the map column.
            // Resolve against the canvas MaximizePostfix uses, or a relayout divided the
            // already-shrunk viewport again (the panel-click map collapse).
            var dynamicMap = map.Root.GetComponent<DynamicMap>();
            Canvas canvas = dynamicMap != null ? dynamicMap.maximizedMapCanvas : null;
            if (canvas == null) canvas = map.Root.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            if (!MfdLayout.TryResolve(canvas, out MfdLayout.Columns columns, panelWidth)) return;

            if (dynamicMap != null) ResizeMap(dynamicMap, columns);

            MfdRail.Ensure(canvas, columns);
            MfdPanelDock.Ensure(canvas, columns);
            MfdMapDeck.Ensure(canvas, columns);
            EnsureFooter(canvas, columns);
            EnsureNewsTicker(canvas, columns);
            EnsureLogPanel(canvas, columns);
            MfdScreenFinish.Ensure(canvas);

            appliedCanvasSize = columns.Canvas;
        }

        private static void EnsureFooter(Canvas canvas, MfdLayout.Columns columns)
        {
            VirtualMFD mfd = MapMfdLookup.Resolve(canvas);
            if (mfd != null) MfdMapFooter.Ensure(canvas, columns, mfd);
        }

        private static void EnsureLogPanel(Canvas canvas, MfdLayout.Columns columns)
        {
            VirtualMFD mfd = MapMfdLookup.Resolve(canvas);
            if (mfd != null) MfdLogPanel.Ensure(canvas, columns, mfd);
        }

        private static void EnsureNewsTicker(Canvas canvas, MfdLayout.Columns columns)
        {
            MfdNewsTicker.Ensure(canvas, columns, Plugin.Settings?.Command);
        }

        // --------------------------------------------------------------------- restore

        private static void Restore(bool onMinimize = false, DynamicMap dynamicMap = null)
        {
            if (!applied)
            {
                // Maximise can be interrupted while the UI is still coming up. The deck is
                // independently owned, so clean it even if no complete map transaction was
                // captured yet rather than leaving a root overlay canvas behind.
                MfdMapDeck.Restore();
                MfdScreenFinish.Restore();
                return;
            }

            // Backdrops first: the buttons go back into these containers next, and a hidden
            // frame around restored buttons would be its own artifact.
            for (int i = 0; i < containers.Count; i++) containers[i].Restore();
            containers.Clear();

            // Re-activate spare buttons ONLY on the settings-off path while still maximized.
            // On Minimize, vanilla's VirtualMFD_onMapMinimized has already hidden all bezel
            // buttons; unhiding spares here would leave stray '-' buttons floating in the cockpit.
            if (!onMinimize)
            {
                for (int i = 0; i < spares.Count; i++)
                    if (spares[i] != null) spares[i].SetActive(true);
            }
            spares.Clear();

            for (int i = 0; i < buttons.Count; i++) buttons[i].Restore();
            buttons.Clear();

            if (mapFrame != null) { Object.Destroy(mapFrame); mapFrame = null; }
            MfdNewsTicker.Restore();
            MfdLogPanel.Restore();
            MfdMapFooter.Restore();
            MfdMapDeck.Restore();
            MfdScreenFinish.Restore();

            MfdPanelDock.Restore();

            // On Minimize, restore framing and minimap state cleanly back to the HUD anchor
            // without canopy projection or stretched offsets.
            // On the settings-off path (still maximized), Restore() puts everything back.
            if (onMinimize)
            {
                map?.RestoreFraming(dynamicMap);
            }
            else
            {
                map?.Restore();
            }
            map = null;

            applied = false;
            appliedCanvasSize = Vector2.zero;
        }

        /// <summary>Forget captured state at end of mission so the next scene re-snapshots.</summary>
        public static void Reset()
        {
            Restore();
            currentPanelWidth = AvTokens.PanelWidth;
            buttons.Clear();
            containers.Clear();
            spares.Clear();
            if (mapFrame != null) { Object.Destroy(mapFrame); mapFrame = null; }
            MfdNewsTicker.Reset();
            MfdLogPanel.Reset();
            MfdMapFooter.Reset();
            MfdMapDeck.Reset();
            map = null;
            applied = false;
            appliedCanvasSize = Vector2.zero;
            MfdRail.Reset();
            MfdPanelDock.Reset();
            MfdSinglePanelPatch.Reset();
            FactionMfdMergePatch.Reset();
        }
    }
}

