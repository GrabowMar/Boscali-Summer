using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Owned presentation hosts for the game's controller-backed stock map-MFD pages.
    ///
    /// The game controls an <see cref="MFDScreen"/> by toggling only its
    /// <c>displayPanel</c>. Repainting children in that panel made this feature depend on
    /// every stock prefab's layout groups, delayed instantiation, and naming conventions.
    /// Instead, each host keeps the native screen and controller alive, hides the old view
    /// behind a non-interactive <see cref="CanvasGroup"/>, and swaps displayPanel to a
    /// fixed, fully owned avionics surface. Native state and actions therefore remain the
    /// authority, while no native layout participates in the rendered panel.
    ///
    /// HUD and mission surfaces attach when first shown, so their card trees cost nothing
    /// while the player stays on other pages.
    /// </summary>
    internal static partial class VanillaMfdRebuild
    {
        private const float RefreshInterval = 0.15f;

        private static readonly Dictionary<MFDScreen, Binding> bindings =
            new Dictionary<MFDScreen, Binding>();

        /// <summary>
        /// Attach an owned surface to a supported stock controller. Unknown and
        /// third-party screens are left untouched.
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
            MissionContractWindow.CloseOpen();
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
            if (hud != null) return screen.isActive ? new HudPresenter(screen, hud) : null;

            TargetListSelector target = screen.GetComponent<TargetListSelector>();
            if (target != null) return new TargetPresenter(screen, target);

            ObjectiveInfoList mission = screen.GetComponent<ObjectiveInfoList>();
            if (mission != null) return screen.isActive ? new MissionPresenter(screen) : null;

            // An unfamiliar stock controller keeps its native view.
            return null;
        }

        private static RectTransform BuildViewRoot(Transform parent, VanillaMfdPanelId id)
        {
            var go = new GameObject("NOAvionics." + VanillaMfdPanelCatalog.Label(id), typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            float height = AvLay.ResolveHeight(
                parent.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rt.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();
            // AvConsole draws its own frame/ground fill; the view root itself stays a bare
            // layout anchor so kit v2 chrome is the only thing that paints.
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

        /// <summary>
        /// Kit v2 base for a stock-screen presenter. Owns one <see cref="AvConsole"/>: its
        /// chamfered chrome, id plate, chips, metrics, icon tab bar and paged, scrolling
        /// <see cref="AvFlow"/> bodies. A derived presenter declares its pages up front
        /// (<see cref="TabItems"/>) and fills each one once from <see cref="BuildContent"/>;
        /// <see cref="RefreshContent"/> only ever mutates already-built parts.
        /// </summary>
        private abstract class Presenter
        {
            private float nextRefresh;
            private int nextPage;

            protected Presenter(MFDScreen screen, VanillaMfdPanelId id)
            {
                Screen = screen;
                Id = id;
            }

            protected readonly MFDScreen Screen;
            public readonly VanillaMfdPanelId Id;
            protected AvConsole Console { get; private set; }

            /// <summary>The console's full header title (the id plate carries the short tag).</summary>
            protected abstract string Title { get; }

            /// <summary>
            /// One (icon, label) per page, in <see cref="CreatePage"/> order. An empty or
            /// single-entry array means no tab bar and exactly one page — the old
            /// <c>TabCount &lt;= 0</c> shape.
            /// </summary>
            protected abstract (AvIcon Icon, string Label)[] TabItems { get; }

            /// <summary>Optional hover tip per tab, in tab order (what the page shows and what it lets you do).</summary>
            protected virtual string[] TabTips => null;

            public void Build(RectTransform root)
            {
                (AvIcon Icon, string Label)[] tabs = TabItems ?? Array.Empty<(AvIcon, string)>();
                int pageCount = Mathf.Max(1, tabs.Length);
                Console = AvConsole.Build(root, VanillaMfdPanelCatalog.Label(Id), Title, pageCount,
                    root.rect.width, root.rect.height);
                if (tabs.Length > 1)
                {
                    AvTabBar tabBar = Console.Tabs(tabs);
                    var hints = new string[tabs.Length];
                    string[] custom = TabTips;
                    for (int i = 0; i < hints.Length; i++)
                        hints[i] = custom != null && i < custom.Length && !string.IsNullOrEmpty(custom[i])
                            ? custom[i] : "Open the " + tabs[i].Label.ToLowerInvariant() + " page.";
                    TabHelp.Apply(tabBar, hints);
                }
                Console.PageChanged += OnPageChanged;
                BuildContent();
                Console.Finish();
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
                }
                catch (Exception e)
                {
                    Console.Footer.Set("NATIVE ADAPTER ERROR — " + e.GetType().Name, AvState.Danger);
                    Plugin.Logger.LogWarning("MFD " + VanillaMfdPanelCatalog.Label(Id) +
                                             " refresh failed: " + e.Message);
                }
            }

            protected abstract void BuildContent();
            protected abstract void RefreshContent();
            protected abstract string AmbientStatus();

            /// <summary>Fires on every page change, programmatic or clicked (spec: tabs latch through AvTabBar).</summary>
            protected virtual void OnPageChanged(int index) { }

            /// <summary>The next page's flow, in the same order <see cref="TabItems"/> declares them.</summary>
            protected AvFlow CreatePage(string name = null) => Console.Page(nextPage++);

            protected void SetSelectedTab(int index) => Console.SetPage(index);

            protected void RequestRefresh()
            {
                nextRefresh = 0f;
                Refresh(force: true);
            }

            /// <summary>
            /// Footer priority: an armed map-gesture prompt outranks ordinary telemetry, same
            /// order the v1 status strip used (hover-tooltip priority has no kit v2 seam yet —
            /// AvControl carries no hovered-tooltip registry; logged as a kit gap).
            /// </summary>
            protected void UpdateStatus(string ambient)
            {
                string prompt = MapPicker.Prompt;
                if (!string.IsNullOrEmpty(prompt)) { Console.Footer.Set(prompt, AvState.Caution); return; }
                Console.Footer.Set(ambient ?? "READY", AvState.Inert);
            }

            /// <summary>Explanatory copy under a heading: dim, wrapped, never truncated.</summary>
            protected static void Note(AvFlow page, string text) => page.Add(new ProseNote(page.Content, text));
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
