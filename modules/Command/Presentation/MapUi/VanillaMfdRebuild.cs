using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Owned presentation hosts for the game's six stock map-MFD pages.
    ///
    /// The game controls an <see cref="MFDScreen"/> by toggling only its
    /// <c>displayPanel</c>. Repainting children in that panel made this feature depend on
    /// every stock prefab's layout groups, delayed instantiation, and naming conventions.
    /// Instead, each host keeps the native screen and controller alive, hides the old view
    /// behind a non-interactive <see cref="CanvasGroup"/>, and swaps displayPanel to a
    /// fixed, fully owned avionics surface. Native state and actions therefore remain the
    /// authority, while no native layout participates in the rendered panel.
    ///
    /// Split by concern across partial files: this file holds the attach/detach lifecycle
    /// and the shared Presenter/AvScreen harness. Each vanilla screen's presenter lives in
    /// its own file - see VanillaMfdRebuild.Map.cs, .Hud.cs, .Faction.cs, .Target.cs,
    /// .Mission.cs - and the shared MfdPagingGrid widget lives in .PagingGrid.cs.
    /// </summary>
    internal static partial class VanillaMfdRebuild
    {
        private const float RefreshInterval = 0.15f;

        private static readonly Dictionary<MFDScreen, Binding> bindings =
            new Dictionary<MFDScreen, Binding>();

        /// <summary>
        /// Attach an owned surface when this is one of the six controller-backed stock
        /// pages. Unknown and third-party screens are deliberately left untouched.
        /// </summary>
        public static bool TryApply(MFDScreen screen)
        {
            if (screen == null || bindings.ContainsKey(screen)) return false;
            if (screen.displayPanel == null) return false;

            Presenter presenter = CreatePresenter(screen);
            if (presenter == null) return false;

            Binding binding = null;
            try
            {
                TMP_Text sourceText = screen.GetComponentInChildren<TMP_Text>(true);
                if (sourceText != null && sourceText.font != null) AvFont.Font = sourceText.font;

                binding = new Binding(screen);
                RectTransform view = BuildViewRoot(screen.transform, presenter.Id);
                // Attach before building so the rollback path owns (and can destroy) the
                // partial tree if a stylesheet or game API changes mid-construction.
                binding.Attach(view.gameObject, presenter);
                presenter.Build(view);
                bindings.Add(screen, binding);
                presenter.Refresh(force: true);
                return true;
            }
            catch (Exception e)
            {
                binding?.Restore();
                Plugin.Logger.LogWarning("MFD replacement " + screen.name + " failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Refresh the newly visible surface after VirtualMFD has set isActive.</summary>
        public static void OnShown(MFDScreen screen)
        {
            if (screen != null && bindings.TryGetValue(screen, out Binding binding))
                binding.Presenter.Refresh(force: true);
        }

        /// <summary>Visible-only refresh; no layout rebuilds or prefab traversal.</summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;
            foreach (Binding binding in bindings.Values)
            {
                if (binding.Screen == null || !binding.Screen.isActive) continue;
                binding.Presenter.Refresh(force: false, now);
            }
        }

        public static bool IsHosted(MFDScreen screen) =>
            screen != null && bindings.ContainsKey(screen);

        /// <summary>Restore every native screen exactly before its dock is dismantled.</summary>
        public static void Restore()
        {
            foreach (Binding binding in bindings.Values) binding.Restore();
            bindings.Clear();
        }

        /// <summary>Scene cleanup has the same restoration contract as a settings toggle.</summary>
        public static void Reset() => Restore();

        private static Presenter CreatePresenter(MFDScreen screen)
        {
            // Controller type is the contract. The faction short names are changed by the
            // game after mission setup, so they are not a safe primary key.
            InfoPanel_Faction faction = screen.GetComponent<InfoPanel_Faction>();
            if (faction != null)
            {
                if (faction.selectFaction == InfoPanel_Faction.SelectFaction.Other &&
                    FactionMfdMergePatch.Screen != null) return null;
                return new FactionPresenter(screen, faction, FactionMfdMergePatch.Other,
                    VanillaMfdPanelId.Bdf);
            }

            MapOptions map = screen.GetComponent<MapOptions>();
            if (map != null) return new MapPresenter(screen, map);

            HUDOptions hud = screen.GetComponent<HUDOptions>();
            if (hud != null) return new HudPresenter(screen, hud);

            TargetListSelector target = screen.GetComponent<TargetListSelector>();
            if (target != null) return new TargetPresenter(screen, target);

            ObjectiveInfoList mission = screen.GetComponent<ObjectiveInfoList>();
            if (mission != null) return new MissionPresenter(screen);

            // A game update may leave the stable bezel label but move the controller. Keep
            // the problem actionable instead of blanking the screen or mutating unknown UI.
            VanillaMfdPanelId fallback = VanillaMfdPanelCatalog.FromShortName(screen.shortName);
            return fallback == VanillaMfdPanelId.Unknown ? null : new UnavailablePresenter(screen, fallback);
        }

        private static RectTransform BuildViewRoot(Transform parent, VanillaMfdPanelId id)
        {
            var go = new GameObject("NOAvionics." + VanillaMfdPanelCatalog.Label(id),
                                    typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            float height = AvScreen.ResolveHeight(
                parent.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rt.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            Image background = go.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;
            return rt;
        }

        // ---------------------------------------------------------------- lifecycle

        private sealed class Binding
        {
            private readonly GameObject originalDisplay;
            private readonly CanvasGroup originalGroup;
            private readonly bool addedGroup;
            private readonly float groupAlpha;
            private readonly bool groupInteractable;
            private readonly bool groupBlocksRaycasts;
            private readonly bool groupIgnoreParentGroups;
            private readonly Image rootImage;
            private readonly Color rootImageColor;
            private readonly bool rootImageRaycast;
            private readonly List<BehaviourState> layoutDrivers = new List<BehaviourState>();
            private readonly List<GraphicState> externalGraphics = new List<GraphicState>();
            private readonly List<SelectableState> externalSelectables = new List<SelectableState>();

            public readonly MFDScreen Screen;
            public GameObject View;
            public Presenter Presenter;

            public Binding(MFDScreen screen)
            {
                Screen = screen;
                originalDisplay = screen.displayPanel;

                originalGroup = originalDisplay.GetComponent<CanvasGroup>();
                if (originalGroup == null)
                {
                    originalGroup = originalDisplay.AddComponent<CanvasGroup>();
                    addedGroup = true;
                }
                else
                {
                    groupAlpha = originalGroup.alpha;
                    groupInteractable = originalGroup.interactable;
                    groupBlocksRaycasts = originalGroup.blocksRaycasts;
                    groupIgnoreParentGroups = originalGroup.ignoreParentGroups;
                }

                // The source remains active. Native controller components can keep their
                // lists and text fields current, but it can neither draw nor eat map input.
                originalDisplay.SetActive(true);
                originalGroup.alpha = 0f;
                originalGroup.interactable = false;
                originalGroup.blocksRaycasts = false;

                rootImage = screen.GetComponent<Image>();
                if (rootImage != null)
                {
                    rootImageColor = rootImage.color;
                    rootImageRaycast = rootImage.raycastTarget;
                    // MfdScreenChromePatch later enables this Graphic on ShowScreen, so it
                    // must be transparent rather than merely disabled.
                    rootImage.color = Color.clear;
                    rootImage.raycastTarget = false;
                }

                CaptureAndDisable(screen.GetComponents<ContentSizeFitter>());
                CaptureAndDisable(screen.GetComponents<AspectRatioFitter>());
                // A future stock prefab may replace the faction screen's fitter with a
                // layout group. Neither is allowed to reposition the direct owned host.
                CaptureAndDisable(screen.GetComponents<LayoutGroup>());
                CaptureAndMuteExternalChrome(screen);
            }

            public void Attach(GameObject view, Presenter presenter)
            {
                View = view;
                Presenter = presenter;
                Screen.displayPanel = view;
                view.SetActive(Screen.isActive);
            }

            public void Restore()
            {
                if (Screen != null)
                {
                    // Put the pointer back before destroying the owned object: a late stock
                    // CloseScreen/ShowScreen callback must always see a live native panel.
                    if (Screen.displayPanel == View) Screen.displayPanel = originalDisplay;

                    if (rootImage != null)
                    {
                        rootImage.color = rootImageColor;
                        rootImage.raycastTarget = rootImageRaycast;
                        // Match the live screen state, not the prefab snapshot. A closed
                        // screen can be restored before its final CloseScreen callback; using
                        // the original enabled flag here would flash or strand its border.
                        // MfdScreenChromePatch re-enables it on the next ShowScreen call.
                        rootImage.enabled = Screen.isActive;
                    }

                    for (int i = 0; i < layoutDrivers.Count; i++) layoutDrivers[i].Restore();
                    for (int i = 0; i < externalGraphics.Count; i++) externalGraphics[i].Restore();
                    for (int i = 0; i < externalSelectables.Count; i++) externalSelectables[i].Restore();

                    if (originalGroup != null)
                    {
                        if (addedGroup)
                        {
                            UnityEngine.Object.Destroy(originalGroup);
                        }
                        else
                        {
                            originalGroup.alpha = groupAlpha;
                            originalGroup.interactable = groupInteractable;
                            originalGroup.blocksRaycasts = groupBlocksRaycasts;
                            originalGroup.ignoreParentGroups = groupIgnoreParentGroups;
                        }
                    }

                    if (originalDisplay != null) originalDisplay.SetActive(Screen.isActive);
                }

                if (View != null) UnityEngine.Object.Destroy(View);
                View = null;
            }

            private void CaptureAndDisable(Behaviour[] drivers)
            {
                if (drivers == null) return;
                for (int i = 0; i < drivers.Length; i++)
                {
                    Behaviour driver = drivers[i];
                    if (driver == null || !driver.enabled) continue;
                    layoutDrivers.Add(new BehaviourState(driver));
                    driver.enabled = false;
                }
            }

            private void CaptureAndMuteExternalChrome(MFDScreen screen)
            {
                Transform source = originalDisplay == null ? null : originalDisplay.transform;
                if (source == null) return;

                Graphic[] graphics = screen.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                {
                    Graphic graphic = graphics[i];
                    if (graphic == null || graphic == rootImage || graphic.transform.IsChildOf(source)) continue;
                    externalGraphics.Add(new GraphicState(graphic));
                    graphic.color = Color.clear;
                    graphic.raycastTarget = false;
                }

                Selectable[] selectables = screen.GetComponentsInChildren<Selectable>(true);
                for (int i = 0; i < selectables.Length; i++)
                {
                    Selectable selectable = selectables[i];
                    if (selectable == null || selectable.transform.IsChildOf(source)) continue;
                    externalSelectables.Add(new SelectableState(selectable));
                    selectable.interactable = false;
                }
            }
        }

        private sealed class BehaviourState
        {
            private readonly Behaviour behaviour;
            private readonly bool enabled;

            public BehaviourState(Behaviour behaviour)
            {
                this.behaviour = behaviour;
                enabled = behaviour.enabled;
            }

            public void Restore()
            {
                if (behaviour != null) behaviour.enabled = enabled;
            }
        }

        private sealed class GraphicState
        {
            private readonly Graphic graphic;
            private readonly Color color;
            private readonly bool raycastTarget;

            public GraphicState(Graphic graphic)
            {
                this.graphic = graphic;
                color = graphic.color;
                raycastTarget = graphic.raycastTarget;
            }

            public void Restore()
            {
                if (graphic == null) return;
                graphic.color = color;
                graphic.raycastTarget = raycastTarget;
            }
        }

        private sealed class SelectableState
        {
            private readonly Selectable selectable;
            private readonly bool interactable;

            public SelectableState(Selectable selectable)
            {
                this.selectable = selectable;
                interactable = selectable.interactable;
            }

            public void Restore()
            {
                if (selectable != null) selectable.interactable = interactable;
            }
        }

        private abstract class Presenter
        {
            private float nextRefresh;
            private int nextPage;
            private Action<int> tabHandler;
            private bool selectingPage;

            protected Presenter(MFDScreen screen, VanillaMfdPanelId id)
            {
                Screen = screen;
                Id = id;
            }

            protected readonly MFDScreen Screen;
            public readonly VanillaMfdPanelId Id;
            protected AvScreen Shell;
            // A shallow bay scrolls a readable page instead of squeezing every control.
            // All owned stock presenters use this same content measure.
            // The page's flight-deck title occupies its own strip above the working area.
            protected virtual bool PageHasTitle => true;
            protected virtual float PageTopInset => 0f;
            protected float PageHeight => Mathf.Max(540f, Shell.Body.height - PageTopInset -
                                                       (PageHasTitle ? 48f : 0f));
            protected float PageWidth { get; private set; }

            public void Build(RectTransform root)
            {
                var contentObject = new GameObject("Content", typeof(RectTransform));
                RectTransform content = contentObject.GetComponent<RectTransform>();
                content.SetParent(root, worldPositionStays: false);
                AvKit.Stretch(content);

                string[] tabs = new string[Mathf.Max(0, TabCount)];
                for (int i = 0; i < tabs.Length; i++) tabs[i] = "—";
                Shell = AvScreen.Build(
                    content, VanillaMfdPanelCatalog.Label(Id), tabs, null, 3,
                    root.rect.width, root.rect.height, HandleTabPressed);
                BuildContent();
            }

            public void Refresh(bool force, float now = 0f)
            {
                if (Screen == null) return;
                if (!force)
                {
                    if (now <= 0f) now = Time.unscaledTime;
                    if (now < nextRefresh) return;
                }

                nextRefresh = Time.unscaledTime + RefreshInterval;
                try
                {
                    RefreshContent();
                    UpdateStatus(AmbientStatus());
                    PaintTabGlyphs();
                }
                catch (Exception e)
                {
                    Shell.WriteStatus("NATIVE ADAPTER ERROR — " + e.GetType().Name,
                                      MapPicker.Prompt, null);
                    Plugin.Logger.LogWarning("MFD " + VanillaMfdPanelCatalog.Label(Id) +
                                             " refresh failed: " + e.Message);
                }
            }

            protected virtual int TabCount => 0;
            protected abstract void BuildContent();
            protected abstract void RefreshContent();
            protected abstract string AmbientStatus();

            protected RectTransform CreatePage(string name, float extraHeight = 0f)
            {
                int index = nextPage++;
                GameObject page = Shell.CreatePage(index, name);
                RectTransform pageRect = page.GetComponent<RectTransform>();
                Rect body = Shell.Body;
                AvKit.Place(pageRect, new Rect(body.x, body.y - PageTopInset,
                    body.width, body.height - PageTopInset));
                if (TabCount <= 0) page.SetActive(true);
                RectTransform content = AvScreen.Scroll(pageRect,
                    new Rect(0f, 0f, body.width, body.height - PageTopInset),
                    PageHeight + (PageHasTitle ? 48f : 0f) + extraHeight, out Rect area);
                PageWidth = area.width;

                float width = PageWidth;
                if (PageHasTitle)
                {
                    AvKit.Rule(content, new Rect(AvTokens.Space3, -7f, 18f, 2f), AvTheme.RailInfo);
                    AvStyled.Label(content, new Rect(AvTokens.Space3 + 27f, -3f, width - 52f, 30f),
                        name.ToUpperInvariant(), "page-title");
                    AvKit.Rule(content, new Rect(AvTokens.Space3, -42f,
                        width - 2f * AvTokens.Space3, 1f), AvTheme.Hairline);
                    AvKit.Rule(content, new Rect(AvTokens.Space3, -42f, 46f, 2f), AvTheme.RailInfo);
                }

                var deck = new GameObject("InstrumentDeck", typeof(RectTransform)).GetComponent<RectTransform>();
                deck.SetParent(content, false);
                AvKit.Place(deck, new Rect(0f, PageHasTitle ? -48f : 0f,
                    width, PageHeight + extraHeight));
                return deck;
            }

            protected void ConfigureTabs(string[] labels, Action<int> onTab)
            {
                tabHandler = onTab;
                if (labels == null) return;
                int count = Mathf.Min(Shell.Tabs.Length, labels.Length);
                for (int i = 0; i < count; i++)
                {
                    AvButton tab = Shell.Tabs[i];
                    if (tab == null) continue;
                    tab.SetText(labels[i]);
                    tab.WithTooltip("Open the " + labels[i].ToLowerInvariant() + " page.");
                    DecorateTab(tab, labels[i]);
                }
                PaintTabGlyphs();
            }

            /// <summary>
            /// Give a text-only tab a glyph and room for it. The tab is the one control a
            /// page change starts at, so it carries the same symbol language as the panel
            /// buttons; a short label keeps its full width, the glyph takes a fixed left
            /// column instead of pushing the text off the edge.
            /// </summary>
            private static void DecorateTab(AvButton tab, string label)
            {
                var root = (RectTransform)tab.transform;
                float width = root.sizeDelta.x > 1f ? root.sizeDelta.x : root.rect.width;
                float height = root.sizeDelta.y > 1f ? root.sizeDelta.y : root.rect.height;
                if (width <= 1f) width = 100f;
                if (height <= 1f) height = AvTokens.TabBarHeight;

                var go = new GameObject("TabIcon", typeof(RectTransform), typeof(MfdGlyph));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(root, worldPositionStays: false);
                AvKit.Place(rt, new Rect(7f, -(height - 14f) * 0.5f, 14f, 14f));

                MfdGlyph glyph = go.GetComponent<MfdGlyph>();
                glyph.raycastTarget = false;
                glyph.Set(label);

                TMP_Text text = tab.GetComponentInChildren<TMP_Text>();
                if (text == null) return;
                AvKit.Place(text.rectTransform, new Rect(23f, 0f, Mathf.Max(0f, width - 27f), height));
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.fontSizeMax = text.fontSize;
                text.fontSizeMin = AvTokens.FontMicro;
                text.enableAutoSizing = true;
            }

            /// <summary>Match each tab glyph to the page it opens; hover stays the tab's own.</summary>
            protected void PaintTabGlyphs()
            {
                if (Shell == null || Shell.Tabs == null) return;
                for (int i = 0; i < Shell.Tabs.Length; i++)
                {
                    AvButton tab = Shell.Tabs[i];
                    if (tab == null) continue;
                    MfdGlyph glyph = tab.GetComponentInChildren<MfdGlyph>(true);
                    if (glyph != null) glyph.SetSelected(i == Shell.Page);
                }
            }

            protected void SetSelectedTab(int selected)
            {
                if (Shell.Page == selected) return;
                selectingPage = true;
                try { Shell.SetPage(selected); }
                finally { selectingPage = false; }
            }

            private void HandleTabPressed(int selected)
            {
                if (!selectingPage) tabHandler?.Invoke(selected);
            }

            protected void RequestRefresh()
            {
                nextRefresh = 0f;
                Refresh(force: true);
            }

            protected void UpdateStatus(string ambient)
            {
                Shell.WriteStatus(null, MapPicker.Prompt, ambient ?? "READY");
            }

            protected static void DrawSpine(RectTransform page) =>
                AvStyled.Spine(page, new Rect(0f, 0f, 3f, page.rect.height));

            /// <summary>Distance from a section title's top edge to the content under its rule.</summary>
            protected const float HeadingPitch = 24f;

            /// <summary>
            /// One section head, kept as references so a page whose blocks move as the copy
            /// changes can re-place it. The head is the same everywhere: the spine tick, the
            /// title, an optional right-hand note, and a hairline rule. The old version drew
            /// a lit accent bar under the title, which made every heading look like a latched
            /// tab and was the loudest mark on the page; the sheet's title and note carry the
            /// hierarchy, and the rule only ties them together.
            /// </summary>
            protected sealed class SectionHead
            {
                public Image Tick;
                public TMP_Text Title;
                public TMP_Text Note;
                public Image Rule;

                public void Place(RectTransform parent, float y, float width)
                {
                    AvKit.Place(Tick.rectTransform, new Rect(3f, y - 7f, Tick.rectTransform.sizeDelta.x,
                                                             Tick.rectTransform.sizeDelta.y));
                    AvKit.Place(Title.rectTransform,
                        new Rect(AvTokens.Space3, y, width * 0.55f - AvTokens.Space3, 16f));
                    if (Note != null)
                        AvKit.Place(Note.rectTransform, new Rect(width * 0.57f, y,
                            width * 0.43f - AvTokens.Space3, 16f));
                    AvKit.Place(Rule.rectTransform,
                        new Rect(AvTokens.Space3, y - 16f, width - 2f * AvTokens.Space3, 1f));
                }
            }

            protected static SectionHead Head(RectTransform parent, float y, float width,
                                              string title, string note = null)
            {
                var head = new SectionHead();
                AvStyle tick = AvStyleHost.Style("spine-tick");
                head.Tick = AvKit.Rule(parent, new Rect(3f, y - 7f,
                        tick.HasWidth ? tick.Width : 8f, tick.HasHeight ? tick.Height : 1.5f),
                    AvStyleHost.Resolve(tick.Background, AvTheme.Accent));
                head.Title = AvStyled.Label(parent,
                    new Rect(AvTokens.Space3, y, width * 0.55f - AvTokens.Space3, 16f), title, "section-title");
                if (!string.IsNullOrEmpty(note))
                {
                    head.Note = AvStyled.Label(parent, new Rect(width * 0.57f, y,
                            width * 0.43f - AvTokens.Space3, 16f),
                        note, "section-title-note", align: TextAlignmentOptions.MidlineRight);
                    head.Note.enableWordWrapping = false;
                    head.Note.enableAutoSizing = true;
                    head.Note.fontSizeMin = AvTokens.FontMicro;
                    head.Note.fontSizeMax = head.Note.fontSize;
                    head.Note.overflowMode = TextOverflowModes.Ellipsis;
                }
                head.Rule = AvKit.Rule(parent, new Rect(AvTokens.Space3, y - 16f,
                                                        width - 2f * AvTokens.Space3, 1f),
                                       AvTheme.Unity(AvTokens.Hairline));
                return head;
            }

            protected static float Heading(RectTransform parent, float y, float width,
                                           string title, string note = null)
            {
                Head(parent, y, width, title, note);
                return y - HeadingPitch;
            }

            /// <summary>Explanatory copy under a heading: dim, wrapped, and never ellipsised.</summary>
            protected static TMP_Text Hint(RectTransform parent, Rect area, string text)
            {
                TMP_Text label = AvStyled.Label(parent, area, text, "hint");
                label.enableWordWrapping = true;
                label.overflowMode = TextOverflowModes.Overflow;
                label.fontSizeMin = AvTokens.FontMicro;
                return label;
            }
        }

        private static int CountEnabled(List<HUDOptions_ToggleButton> sources)
        {
            if (sources == null) return 0;
            int count = 0;
            for (int i = 0; i < sources.Count; i++)
                if (sources[i] != null && sources[i].status) count++;
            return count;
        }

        private static string NativeCategoryLabel(HUDOptions_Category category, int fallback)
        {
            if (category != null)
            {
                if (category.listUnitTypes != null && category.listUnitTypes.Count > 0 && category.listUnitTypes[0] != null)
                {
                    switch (category.listUnitTypes[0].GetType().Name)
                    {
                        case "AircraftDefinition": return "AIRCRAFT";
                        case "MissileDefinition": return "MISSILES";
                        case "VehicleDefinition": return "VEHICLES";
                        case "BuildingDefinition": return "BUILDINGS";
                        case "ShipDefinition": return "SHIPS";
                    }
                }
                if (category.context == HUDOptions_Category.ButtonContext.FRIENDLY) return "FRIENDLY";
                if (category.context == HUDOptions_Category.ButtonContext.HOSTILE) return "ENEMY";
                if (category.label != null && !string.IsNullOrEmpty(category.label.text))
                    return category.label.text.ToUpperInvariant();
            }
            return fallback == 0 ? "FRIENDLY" : fallback == 1 ? "ENEMY" : "CATEGORY " + (fallback + 1);
        }

        private static string NativeLabel(MonoBehaviour source, string fallback)
        {
            if (source == null) return fallback;

            TMP_Text[] labels = source.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null && !string.IsNullOrEmpty(labels[i].text))
                    return labels[i].text.Replace("\n", " ").ToUpperInvariant();
            }

            string name = source.gameObject.name;
            const string itemPrefix = "Item_";
            if (name.StartsWith(itemPrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(itemPrefix.Length);
            return string.IsNullOrEmpty(name) ? fallback : name.Replace("_", " ").ToUpperInvariant();
        }
    }
}
