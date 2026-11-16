using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Services;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        internal static System.Action<int> BuildMapDrawer(MfdMapTools.Page layers, MfdMapTools.Page plot, AvFooter footer)
        {
            var presenter = new MapPresenter(null, null);
            presenter.BuildDrawer(layers, plot, footer);
            return presenter.RefreshDrawer;
        }
        /// <summary>
        /// The MAP bezel: every map tool (TOOLS), which map layers and Boscali overlays are drawn
        /// (LAYERS, with the hover readout and symbol scale). TOOLS only surfaces what the map input
        /// already owns (<see cref="MfdMapInteractions"/>, <see cref="ReliefNavigator"/>, the MARK tools)
        /// and COM's shared plot through <see cref="ICommsPlot"/>.
        ///
        /// Every control calls the authority that already owns the value — the native
        /// <see cref="MapOptions"/> for the six stock layers, or Command's own
        /// <see cref="ComMapOverlay"/> for the theater control tint and front line — and the
        /// panel keeps no state of its own. Both pages are selections on the shared control
        /// grid, so they read like the rest of the rebuilt bezel screens instead of a wall of
        /// identical full-width boxes.
        /// </summary>
        private sealed class MapPresenter : Presenter
        {
            private const int NativeLayerCount = 6;
            private const int OverlayLayerCount = 5;
            private const int RoadsLayer = 4;

            private static readonly string[] LayerNames =
                { "OBJECTIVES", "TARGET DETAILS", "JAMMING", "GRID LABELS", "PILOTS", "AIRBASES" };
            private static readonly string[] LayerSubtitles =
                { "Mission markers", "Target markers", "Jam indicators", "Grid coordinates", "Pilot icons", "Base icons" };
            private static readonly string[] OverlayNames =
                { "CONTROL FIELD", "FRONT LINE", "INDUSTRY", "SUPPLY ROUTES", "ROADS" };
            private static readonly string[] OverlaySubtitles =
                { "Sector control", "Control boundary", "Factories, depots, ammo dumps", "Depot lines and trucks", "Road network on the relief" };
            private static readonly string[] HoverNames = { "OFF", "UNIT INFO", "AMMUNITION", "ORDERS" };
            private static readonly string[] HoverNotes =
                { "No hover tooltip", "Unit information", "Weapon and ammunition", "Current unit orders" };
            private static readonly MapOptions.TooltipType[] HoverModes =
            {
                MapOptions.TooltipType.None, MapOptions.TooltipType.Info,
                MapOptions.TooltipType.Ammo, MapOptions.TooltipType.Order,
            };
            private static readonly string[] SizeNames = { "SMALL", "MEDIUM", "LARGE" };
            private static readonly string[] SizeSubtitles = { "COMPACT", "BALANCED", "FULL SIZE" };
            private static readonly string[] ReadoutKeys =
                { "SECTOR CELL", "CONTROL DATA", "INDUSTRY", "CONVOYS" };
            private static readonly string[] LegendLabels =
                { "FRIENDLY GROUND", "HOSTILE GROUND", "CONTESTED", "FRONT LINE", "INDUSTRY SITE", "SUPPLY ROUTE" };
            private static readonly AvIcon[] LayerGlyphs =
                { AvIcon.Flag, AvIcon.Target, AvIcon.WaveSine, AvIcon.Ruler2, AvIcon.User, AvIcon.Plane };
            private static readonly AvIcon[] OverlayGlyphs =
                { AvIcon.Shield, AvIcon.Line, AvIcon.BuildingFactory, AvIcon.Truck, AvIcon.Map2 };
            private static readonly AvIcon[] HoverGlyphs =
                { AvIcon.X, AvIcon.InfoCircle, AvIcon.Bolt, AvIcon.ListDetails };
            private static readonly AvIcon[] SizeGlyphs =
                { AvIcon.Minimize, AvIcon.Circle, AvIcon.Maximize };

            private const int ToolsPage = 0, LayersPage = 1;
            // In MapRightClick order: the first claim wins (rule B).
            private static readonly string[] RuleNames =
                { "ARMED TOOL", "MOVE WING", "WAYPOINT", "A CARD ON THE SPOT" };

            private MapOptions options;
            private AvFooter drawerFooter;
            private int drawerTab = -1;
            private AvSection selectSection, markSection;
            private AvControl[] markTools, viewTools;
            private ICommsPlot plot;
            private AvPart[] plotParts;
            private AvNote plotOff, ruleNote;
            private int lastRule = -1;
            private string lastMarkCaption = "", lastSelCaption = "";

            private ComMapOverlay overlay;
            private LogisticsMapOverlay logistics;
            private RoadMapOverlay roads;
            private MfdPagingGrid layers, overlays, hover, sizes;
            private AvControl presetAll, presetNone, presetDefaults, plotInk, reliefToggle;
            private AvControl[] nativeSwitches, overlaySwitches;
            private AvMetric[] metrics;
            private int selectedPage;
            private int visibleLayers;

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map) { this.options = options; }

            internal void BuildDrawer(MfdMapTools.Page layersPage, MfdMapTools.Page plotPage, AvFooter footer)
            {
                drawerFooter = footer;
                AvFlow native = layersPage.Card("GAME LAYERS"), theater = layersPage.Card("THEATER OVERLAYS"),
                    display = layersPage.Card("DISPLAY"), readouts = layersPage.Card("PRESETS & READOUTS"), key = layersPage.Card("MAP KEY");
                BuildLayers(native, theater, display, key, readouts);
                metrics = new AvMetric[ReadoutKeys.Length];
                for (int i = 0; i < metrics.Length; i++) metrics[i] = new AvMetric(readouts.Content, ReadoutKeys[i]);
                readouts.Row(metrics[0], metrics[1]); readouts.Row(metrics[2], metrics[3]);
                AvFlow marks = plotPage.Card("MARKS & TEXT");
                BuildTools(marks, plotPage.Card("SHARED PLOT"), plotPage.Card("MARKERS & INK"), marks);
            }

            internal void RefreshDrawer(int tab)
            {
                drawerTab = tab;
                if (options == null)
                {
                    VirtualMFD mfd = MapMfdLookup.Resolve(SceneSingleton<DynamicMap>.i?.maximizedMapCanvas);
                    if (mfd != null) options = FindOptions(MapUiAccess.GetLeftScreens(mfd)) ?? FindOptions(MapUiAccess.GetRightScreens(mfd));
                }
                // Plot ownership must survive tab changes/folding; only the visible page repaints.
                plot?.KeepAlive();
                if (tab == 1) { selectedPage = LayersPage; RefreshContent(); }
                else if (tab == 2) { selectedPage = ToolsPage; RefreshTools(); }
                else return;
                // The drawer owns its one feedback/help line; hidden pages never overwrite it.
            }

            private static MapOptions FindOptions(System.Collections.Generic.List<MFDScreen> screens)
            {
                if (screens != null)
                    foreach (MFDScreen screen in screens)
                        if (screen != null && screen.GetComponent<MapOptions>() is MapOptions found) return found;
                return null;
            }

            private new void RequestRefresh()
            {
                if (drawerFooter != null) RefreshDrawer(drawerTab);
                else base.RequestRefresh();
            }

            protected override (AvIcon Icon, string Label)[] TabItems => new[]
            {
                (AvIcon.Ruler2, "TOOLS"),
                (AvIcon.LayersSubtract, "LAYERS"),
            };

            protected override string[] TabTips { get; } = new[]
            {
                "Tools: marks, the shared plot, the relief camera and selection. Right mouse goes to the first claim.",
                "Layers: the game's map layers, the Boscali overlays, hover readout and symbol size, with the colour legend.",
            };

            private bool Available => options != null && SceneSingleton<DynamicMap>.i != null;

            /// <summary>Resolved once and re-resolved after a rollback; never a scene scan.</summary>
            private ComMapOverlay Overlay => Late(ref overlay);
            private LogisticsMapOverlay Logistics => Late(ref logistics);
            private RoadMapOverlay Roads => Late(ref roads);

            private static T Late<T>(ref T cache) where T : Object
            {
                if (cache == null) ModuleServices.TryGet(out cache);
                return cache;
            }

            protected override void BuildContent()
            {
                // The four live readouts are console-level chrome and stay visible on both pages. The three
                // status chips (layer count, tooltip, size) only repeated what the tiles below already show.
                metrics = Console.Metrics(ReadoutKeys);
                string[] metricTips =
                {
                    "Sector cell: the side of one control-field cell in metres. Cells only coarsen on very large theaters.",
                    "Control data: LIVE when ground presence feeds the sector control field, NO DATA before either side has ground units.",
                    "Industry: factories, depots and ammo dumps your side knows of, both sides, and how many friendly ones have a line to the front.",
                    "Convoys: supply trucks your side knows of, both sides."
                };
                for (int i = 0; i < metrics.Length; i++) AvHelpTip.Attach(metrics[i].Rect.gameObject, metricTips[i]);

                BuildTools(CreatePage());
                BuildLayers(CreatePage());

                // The footer shows these one at a time under the status line, most important first.
                Console.Orders(ToolsPage,
                    "Arm a MARK or PLOT tool here, then right-click the map. ESC puts it down.");
                Console.Orders(LayersPage,
                    "Left-click a tile to show or hide that layer. ALL ON and HIDE ALL act on every layer.");
            }
            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                RequestRefresh();
            }

            // -------------------------------------------------------------- tools

            private void BuildTools(AvFlow page, AvFlow sharedTools = null, AvFlow sharedMarkers = null, AvFlow sharedText = null)
            {
                markSection = page.Section(AvIcon.Ruler2, sharedTools != null ? "PRIVATE MARKS" : "MARK",
                    sharedTools != null ? "RMB TO PLACE" : "ARM ONE · RMB PLACES IT · ONLY YOU SEE IT");
                var markSpecs = new[]
                {
                    new AvControl.Spec("RANGE", () => ArmMark(MfdMapInteractions.MarkTool.Range), AvButtonStyle.Toggle, AvIcon.Ruler2),
                    new AvControl.Spec("LANDING ZONE", () => ArmMark(MfdMapInteractions.MarkTool.Zone), AvButtonStyle.Toggle, AvIcon.Parachute),
                    new AvControl.Spec("COPY FIX", () => ArmMark(MfdMapInteractions.MarkTool.Fix), AvButtonStyle.Toggle, AvIcon.Copy),
                    new AvControl.Spec("CLEAR MARKS", () => { MfdMapInteractions.ClearMarks(); RequestRefresh(); }, AvButtonStyle.Default, AvIcon.Eraser)
                };
                if (sharedTools != null)
                {
                    AvControl[] first = page.Buttons(markSpecs[0], markSpecs[1]).Controls;
                    AvControl[] second = page.Buttons(markSpecs[2], markSpecs[3]).Controls;
                    markTools = new[] { first[0], first[1], second[0], second[1] };
                }
                else markTools = page.Buttons(markSpecs).Controls;
                markTools[0].Help = "Range: right-click the map to drop your range mark.";
                markTools[1].Help = "Landing zone: right-click the map; every HALO stick and glider you release aims there.";
                markTools[2].Help = "Copy fix: right-click the map to copy that ground's X and Z to the clipboard.";
                markTools[3].Help = "Clear: remove your range mark and your landing zone.";

                // COM's shared plot, built by Comms on this page; one dim line while Comms is off.
                plotOff = page.Add(new AvNote(page.Content, "PLOT off: COMMS is switched off in F1."));
                if (ModuleServices.TryGet(out plot))
                    plotParts = sharedTools != null ? plot.BuildDrawer(sharedTools, sharedMarkers, sharedText) : plot.Build(page);

                if (sharedTools != null)
                {
                    ruleNote = page.Add(new AvNote(page.Content, ""));
                    return; // VIEW owns the camera and selection controls in the drawer.
                }

                page.Section(AvIcon.Focus2, "VIEW", "THE RELIEF CAMERA");
                viewTools = page.Buttons(
                    new AvControl.Spec("NORTH UP", ReliefNavigator.North, AvButtonStyle.Default, AvIcon.CurrentLocation),
                    new AvControl.Spec("FIT MAP", ReliefNavigator.Fit, AvButtonStyle.Default, AvIcon.Maximize),
                    new AvControl.Spec("FOLLOW", ReliefNavigator.ToggleFollow, AvButtonStyle.Toggle, AvIcon.Plane),
                    new AvControl.Spec("WASD", ReliefNavigator.ToggleFly, AvButtonStyle.Toggle, AvIcon.Eye)).Controls;
                viewTools[0].Help = "North up at the default terrain tilt.";
                viewTools[1].Help = "Fit the whole mission map. Double-click the ground to zoom in there.";
                viewTools[2].Help = "Follow your aircraft, or drag the map to move freely.";
                viewTools[3].Help = "Keyboard camera flight: W/A/S/D moves relative to the view heading while the map is open.";

                selectSection = page.Section(AvIcon.Target, "SELECT", "LEFT MOUSE · SHIFT BOXES IN · CTRL BOXES OUT");
                AvControl[] select = page.Buttons(
                    new AvControl.Spec("FRAME", MfdMapInteractions.FrameSelection, AvButtonStyle.Default, AvIcon.Focus2),
                    new AvControl.Spec("SAME TYPE", MfdMapInteractions.SelectSameType, AvButtonStyle.Default, AvIcon.Filter),
                    new AvControl.Spec("CLEAR", MfdMapInteractions.ClearSelectionTool, AvButtonStyle.Default, AvIcon.Eraser)).Controls;
                select[0].Help = "Frame: centre the map on what is selected.";
                select[1].Help = "Same type: select every visible contact of the first selected one's type and side.";
                select[2].Help = "Clear: empty the selection, and with it your target list while flying.";

                // The one rule line; the footer carries the rest.
                ruleNote = page.Add(new AvNote(page.Content, ""));
            }

            private void ArmMark(MfdMapInteractions.MarkTool tool)
            {
                MfdMapInteractions.ArmMark(tool);
                RequestRefresh();
            }

            private void RefreshTools()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                int rule = map != null ? (int)MfdMapInteractions.RightRule(map) : (int)MapRightClick.Card;
                if (rule != lastRule)
                {
                    lastRule = rule;
                    ruleNote.Set("RIGHT MOUSE NOW: " + RuleNames[Mathf.Clamp(rule, 0, RuleNames.Length - 1)]);
                }

                int sel = map != null && map.selectedIcons != null ? map.selectedIcons.Count : 0;
                string selCaption = sel + " SELECTED";
                if (selCaption != lastSelCaption)
                {
                    lastSelCaption = selCaption;
                    selectSection?.SetCaption(selCaption + " · SHIFT BOXES IN · CTRL BOXES OUT");
                }

                MfdMapInteractions.MarkTool armed = MfdMapInteractions.ArmedMark;
                bool marked = MfdMapInteractions.Mark.HasValue, zoned = MfdMapInteractions.Zoned;
                markTools[0].Latched = armed == MfdMapInteractions.MarkTool.Range;
                markTools[1].Latched = armed == MfdMapInteractions.MarkTool.Zone;
                markTools[2].Latched = armed == MfdMapInteractions.MarkTool.Fix;
                markTools[1].Interactable = MfdMapInteractions.ZoneOffered;
                markTools[3].Interactable = marked || zoned;
                string markCaption = (marked ? "RANGE MARK SET" : "NO RANGE MARK") + " · " + (zoned ? "LZ SET" : "NO LZ");
                if (markCaption != lastMarkCaption)
                {
                    lastMarkCaption = markCaption;
                    markSection.SetCaption(drawerFooter != null
                        ? "RANGE " + (marked ? "SET" : "—") + " · LZ " + (zoned ? "SET" : "—")
                        : markCaption + " · ONLY YOU SEE THEM");
                }

                bool plotOn = plot != null && plot.On;
                plotOff.SetShown(!plotOn);
                for (int i = 0; plotParts != null && i < plotParts.Length; i++) plotParts[i]?.SetShown(plotOn);
                if (plotOn) plot.Refresh();

                bool relief = MfdTerrainRelief.IsDrawing;
                if (viewTools != null)
                {
                    foreach (AvControl c in viewTools) c.Interactable = relief;
                    viewTools[2].Latched = ReliefNavigator.Following;
                    viewTools[3].Latched = ReliefNavigator.Flying;
                }
            }

            // ------------------------------------------------------------- layers

            private void BuildLayers(AvFlow page, AvFlow overlayPage = null, AvFlow displayPage = null, AvFlow keyPage = null, AvFlow presetPage = null)
            {
                bool compact = overlayPage != null;
                if (!compact) page.Section(AvIcon.Filter, "GAME LAYERS", "STOCK MAP OPTIONS");
                if (compact) nativeSwitches = LayerSwitches(page, new[] { "OBJECTIVES", "DETAILS", "JAMMING", "GRID", "PILOTS", "AIRBASES" },
                    i => { if (ToggleReady()) ToggleNativeLayer(i); RequestRefresh(); });
                else
                {
                    layers = new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true);
                    layers.SetGlyphs(i => LayerGlyphs[Mathf.Clamp(i, 0, LayerGlyphs.Length - 1)]);
                    AddGrid(page, layers);
                }

                page = overlayPage ?? page;
                if (!compact) page.Section(AvIcon.Scale, "THEATER OVERLAYS", "BOSCALI PLOT");
                if (compact) overlaySwitches = LayerSwitches(page, new[] { "CONTROL", "FRONT LINE", "INDUSTRY", "SUPPLY", "ROADS" },
                    i => { if (ToggleReady() && OverlayReady(i)) SetOverlayLayer(i, !OverlayLayer(i)); RequestRefresh(); });
                else
                {
                    overlays = new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true);
                    overlays.SetGlyphs(i => OverlayGlyphs[Mathf.Clamp(i, 0, OverlayGlyphs.Length - 1)]);
                    AddGrid(page, overlays);
                }

                page = presetPage ?? page;
                var presetSpecs = new[]
                {
                    new AvControl.Spec("ALL ON", () => SetAllLayers(true), AvButtonStyle.Default, AvIcon.LayersSubtract),
                    new AvControl.Spec("HIDE ALL", () => SetAllLayers(false), AvButtonStyle.Default, AvIcon.X),
                    new AvControl.Spec("DEFAULTS", ApplyDefaults, AvButtonStyle.Default, AvIcon.Refresh),
                    new AvControl.Spec("PLOT INK", () => { if (plot != null && plot.On) plot.ShowOnMap = !plot.ShowOnMap; RequestRefresh(); }, AvButtonStyle.Toggle, AvIcon.Pencil)
                };
                if (compact)
                {
                    AvControl[] first = page.Buttons(presetSpecs[0], presetSpecs[1]).Controls;
                    AvControl[] second = page.Buttons(presetSpecs[2], presetSpecs[3]).Controls;
                    presetAll = first[0]; presetNone = first[1]; presetDefaults = second[0]; plotInk = second[1];
                }
                else
                {
                    AvControl[] controls = page.Buttons(presetSpecs).Controls;
                    presetAll = controls[0]; presetNone = controls[1]; presetDefaults = controls[2]; plotInk = controls[3];
                }
                plotInk.Help = "Plot ink: the pings, drawings and text your net team puts on the map. Tap to show or hide.";

                page = displayPage ?? page;
                if (compact)
                {
                    reliefToggle = page.Buttons(new AvControl.Spec("3D RELIEF", () =>
                    {
                        var setting = Plugin.Settings?.Command?.MapRelief3D;
                        if (setting != null) setting.Value = !setting.Value;
                        RequestRefresh();
                    }, AvButtonStyle.Toggle, AvIcon.Map2)).Controls[0];
                }
                page.Section(AvIcon.Eye, compact ? "HOVER READOUT" : "DISPLAY", compact ? null : "HOVER READOUT · SYMBOL SCALE");
                hover = new MfdPagingGrid(page.Content, compact ? 2 : 4, compact ? 2 : 1, pager: false, rowHeight: compact ? 38f : 56f, tile: true);
                hover.SetGlyphs(i => HoverGlyphs[Mathf.Clamp(i, 0, HoverGlyphs.Length - 1)]);
                AddGrid(page, hover);
                if (compact) page.Section(AvIcon.Maximize, "SYMBOL SCALE");
                sizes = new MfdPagingGrid(page.Content, 3, 1, pager: false, rowHeight: compact ? 42f : 56f, tile: true);
                sizes.SetGlyphs(i => SizeGlyphs[Mathf.Clamp(i, 0, SizeGlyphs.Length - 1)]);
                AddGrid(page, sizes);

                // One colour legend, then the symbol key.
                page = keyPage ?? page;
                page.Add(new LegendCard(page.Content));
                page.Add(new SymbolKey(page.Content));
            }

            private static AvControl[] LayerSwitches(AvFlow flow, string[] labels, System.Action<int> toggle)
            {
                var result = new AvControl[labels.Length];
                for (int i = 0; i < labels.Length; i += 2)
                {
                    int a = i, b = i + 1;
                    var first = new AvControl.Spec(labels[a], () => toggle(a), AvButtonStyle.Toggle);
                    AvControl[] row = b < labels.Length
                        ? flow.Buttons(first, new AvControl.Spec(labels[b], () => toggle(b), AvButtonStyle.Toggle)).Controls
                        : flow.Buttons(first).Controls;
                    result[a] = row[0]; if (b < labels.Length) result[b] = row[1];
                }
                return result;
            }
            private bool NativeLayer(int index)
            {
                switch (index)
                {
                    case 0: return options.showObjectives;
                    case 1: return options.showTargetInfo;
                    case 2: return options.showJamming;
                    case 3: return options.showGridLabels;
                    case 4: return options.showPilotIcons;
                    default: return options.showAirbaseIcon;
                }
            }

            private void ToggleNativeLayer(int index)
            {
                switch (index)
                {
                    case 0: options.ToggleShowObjectives(); break;
                    case 1: options.ToggleShowTargetInfo(); break;
                    case 2: options.ToggleShowJamming(); break;
                    case 3: options.ToggleShowGridLabels(); break;
                    case 4: options.ToggleShowPilotIcons(); break;
                    default: options.ToggleShowAirbaseIcons(); break;
                }
            }

            private bool OverlayLayer(int index)
            {
                if (index == 0) return Overlay != null && Overlay.ControlFieldVisible;
                if (index == 1) return Overlay != null && Overlay.FrontLineVisible;
                if (index == 2) return Logistics != null && Logistics.IndustryVisible;
                if (index == 3) return Logistics != null && Logistics.SupplyVisible;
                if (index == RoadsLayer) return Roads != null && Roads.Visible;
                return false;
            }

            private void SetOverlayLayer(int index, bool visible)
            {
                if (index == 0) Overlay?.SetControlFieldVisible(visible);
                else if (index == 1) Overlay?.SetFrontLineVisible(visible);
                else if (index == 2) Logistics?.SetIndustryVisible(visible);
                else if (index == 3) Logistics?.SetSupplyVisible(visible);
                else if (index == RoadsLayer) Roads?.SetVisible(visible);
            }

            /// <summary>Whether one overlay has the map and the state it needs to draw at all.</summary>
            private bool OverlayReady(int index)
            {
                if (index < 2) return Overlay != null && Overlay.LayersAvailable;
                if (index < 4) return Logistics != null && Logistics.Available;
                if (index == RoadsLayer) return Roads != null && Roads.Available;
                return false;
            }

            /// <summary>Both presets state the whole set; already-satisfied layers are skipped.</summary>
            private void SetAllLayers(bool visible)
            {
                if (!Available)
                {
                    RequestRefresh();
                    return;
                }

                for (int i = 0; i < NativeLayerCount; i++)
                    if (NativeLayer(i) != visible) ToggleNativeLayer(i);

                for (int i = 0; i < OverlayLayerCount; i++)
                    if (OverlayReady(i) && OverlayLayer(i) != visible) SetOverlayLayer(i, visible);

                RequestRefresh();
            }

            /// <summary>The game's own defaults, not a remembered layout.</summary>
            private void ApplyDefaults()
            {
                if (!Available)
                {
                    RequestRefresh();
                    return;
                }

                for (int i = 0; i < NativeLayerCount; i++)
                    if (!NativeLayer(i)) ToggleNativeLayer(i);
                options.SetToolTipType((int)MapOptions.TooltipType.Info);
                options.SetIconSize(2);

                // The game has no road layer of its own, so DEFAULTS leaves ROADS as it is.
                for (int i = 0; i < OverlayLayerCount; i++)
                    if (i != RoadsLayer && OverlayReady(i) && !OverlayLayer(i)) SetOverlayLayer(i, true);

                RequestRefresh();
            }

            /// <summary>The colour a contact side wears on the map: the tints the game gives friendly and
            /// hostile icons, and a neutral grey. Shape carries the same distinction without colour.</summary>
            private static Color SideTint(MapSide side) =>
                side == MapSide.Friendly ? AvTheme.RailInfo
                : side == MapSide.Hostile ? AvTheme.RailDanger
                : AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);

            /// <summary>Builds one contact symbol exactly as the map draws it: a frameless glyph.</summary>
            private static void MakeSymbol(RectTransform parent, MapSide side, MapGlyph glyph,
                out Image plate, out Image mark)
            {
                // The map draws no frame; the plate only keeps the tile's layout and stays hidden.
                plate = NewSymbolImage(parent, "Plate", null, Color.clear);
                plate.enabled = false;
                mark = NewSymbolImage(parent, "Glyph", MapSymbolAtlas.Glyph(glyph), SideTint(side));
            }

            private static Image NewSymbolImage(RectTransform parent, string name, Sprite sprite, Color tint)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                Image image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.color = tint;
                // A spriteless Image draws a solid square; an icon the game has not loaded shows nothing.
                image.enabled = sprite != null;
                image.raycastTarget = false;
                RectTransform rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(.5f, .5f);
                return image;
            }

            private static void PlaceSymbol(Image plate, Image mark, float centreX, float centreY, float size)
            {
                plate.rectTransform.anchoredPosition = mark.rectTransform.anchoredPosition =
                    new Vector2(centreX, -centreY);
                plate.rectTransform.sizeDelta = new Vector2(size, size);
                float inner = size / MapSymbology.PlateRatio;
                mark.rectTransform.sizeDelta = new Vector2(inner, inner);
            }

            /// <summary>Key to every contact symbol: the three frames (allegiance) and the glyph per platform
            /// class. Painted from the same sprites the map draws with.</summary>
            private sealed class SymbolKey : AvPart
            {
                private const float Pitch = 62f, Inset = 12f, Size = 32f;
                private static int Columns(float width) => Mathf.Clamp(Mathf.FloorToInt((width - Inset * 2f) / 74f), 1, 6);
                private static readonly (string Label, MapSide Side, MapGlyph Glyph)[] Entries =
                {
                    ("FRIENDLY", MapSide.Friendly, MapGlyph.Armor), ("HOSTILE", MapSide.Hostile, MapGlyph.Armor),
                    ("UNKNOWN", MapSide.Neutral, MapGlyph.Armor), ("AIRCRAFT", MapSide.Friendly, MapGlyph.Aircraft),
                    ("ARMOUR", MapSide.Friendly, MapGlyph.Armor), ("LIGHT", MapSide.Friendly, MapGlyph.Light),
                    ("SUPPLY", MapSide.Friendly, MapGlyph.Supply), ("ARTILLERY", MapSide.Friendly, MapGlyph.Artillery),
                    ("ANTI-AIR", MapSide.Friendly, MapGlyph.AntiAir), ("SAM", MapSide.Friendly, MapGlyph.Sam),
                    ("RADAR", MapSide.Friendly, MapGlyph.Radar), ("SHIP", MapSide.Friendly, MapGlyph.Hull),
                };
                private readonly AvFrame frame;
                private readonly Image[] plates = new Image[Entries.Length];
                private readonly Image[] marks = new Image[Entries.Length];
                private readonly TMP_Text[] labels = new TMP_Text[Entries.Length];

                public SymbolKey(RectTransform parent)
                {
                    Rect = AvLay.Child(parent, "Symbol Key");
                    frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                    AvLay.Fill(frame.rectTransform);
                    for (int i = 0; i < Entries.Length; i++)
                    {
                        MakeSymbol(Rect, Entries[i].Side, Entries[i].Glyph, out plates[i], out marks[i]);
                        labels[i] = AvText.Make(Rect, "Label " + i, AvTextRole.Micro, Entries[i].Label,
                            TextAlignmentOptions.Center);
                    }
                    Restyle();
                }

                public override float Measure(float width) =>
                    Mathf.CeilToInt(Entries.Length / (float)Columns(width)) * Pitch + Inset * 2f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    int columns = Columns(s.W);
                    float cell = (s.W - Inset * 2f) / columns;
                    // A growing key spreads its rows over the spare height instead of leaving a blank band.
                    int rows = Mathf.CeilToInt(Entries.Length / (float)columns);
                    float pitch = Mathf.Max(Pitch, (s.H - Inset * 2f) / rows);
                    for (int i = 0; i < Entries.Length; i++)
                    {
                        float x = Inset + (i % columns) * cell;
                        float y = Inset + (i / columns) * pitch;
                        PlaceSymbol(plates[i], marks[i], x + cell * .5f, y + Size * .5f, Size);
                        AvLay.Place(labels[i].rectTransform, x, y + Size + 4f, cell, 16f);
                    }
                }
                public override void Restyle()
                {
                    frame.Paint(AvStyleHost.FuiFill("card", AvTheme.SurfaceInert),
                                AvStyleHost.FuiBorder("card", AvTheme.Hairline));
                    Color ink = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                    for (int i = 0; i < Entries.Length; i++)
                    {
                        labels[i].color = ink;
                        plates[i].color = marks[i].color = SideTint(Entries[i].Side);
                    }
                }
            }

            /// <summary>The map's own live ink (sector tints, front-line trace, threat rails) painted as
            /// swatches: genuinely data — it must match what actually renders on the map — never a themed
            /// fill, so this is a local AvPart built from kit primitives (AvFrame + raw <see cref="Image"/>
            /// fills) rather than a styled AvCard.</summary>
            private sealed class LegendCard : AvPart
            {
                private const float Pitch = 24f, BlockW = 10f, BlockH = 12f, Inset = 12f;
                private readonly AvFrame frame;
                private readonly Image[] left, right;
                private readonly TMP_Text[] labels;

                public LegendCard(RectTransform parent)
                {
                    Rect = AvLay.Child(parent, "Legend");
                    frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                    AvLay.Fill(frame.rectTransform);

                    int n = LegendLabels.Length;
                    left = new Image[n];
                    right = new Image[n];
                    labels = new TMP_Text[n];
                    for (int i = 0; i < n; i++)
                    {
                        left[i] = AvLay.Solid(Rect, "Swatch " + i + "a", Color.white);
                        right[i] = AvLay.Solid(Rect, "Swatch " + i + "b", Color.white);
                        labels[i] = AvText.Make(Rect, "Label " + i, AvTextRole.DataSmall, LegendLabels[i]);
                    }
                    Paint();
                    Restyle();
                }

                /// <summary>Live map ink, painted once at build: these are the map's real render
                /// colours, not theme tokens, so they do not move with the palette switcher.</summary>
                private void Paint()
                {
                    Color32 friendly = TacticalSectorGrid.FriendlyTint;
                    Color32 hostile = TacticalSectorGrid.HostileTint;
                    Color32 contestedLeft = new Color32(friendly.r, friendly.g, friendly.b, 150);
                    Color32 contestedRight = new Color32(hostile.r, hostile.g, hostile.b, 150);
                    Color32 front = FrontlineGraphic.Ink;
                    for (int i = 0; i < LegendLabels.Length; i++)
                    {
                        Color32 l = i == 1 ? hostile : i == 2 ? contestedLeft : i == 3 ? front
                            : i == 4 || i == 5 ? (Color32)AvTheme.RailInfo : friendly;
                        Color32 r = i == 2 ? contestedRight : i == 4 ? (Color32)AvTheme.RailDanger : l;
                        left[i].color = l;
                        right[i].color = r;
                    }
                }

                private static int Columns(float width) => width >= 400f ? 2 : 1;

                public override float Measure(float width) =>
                    Mathf.CeilToInt(LegendLabels.Length / (float)Columns(width)) * Pitch + Inset * 2f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    int columns = Columns(s.W);
                    float half = s.W / columns;
                    for (int i = 0; i < LegendLabels.Length; i++)
                    {
                        float colX = Inset + (i % columns) * half;
                        float rowY = Inset + (i / columns) * Pitch;
                        AvLay.Place(left[i].rectTransform, colX, rowY, BlockW, BlockH);
                        AvLay.Place(right[i].rectTransform, colX + BlockW, rowY, BlockW, BlockH);
                        AvLay.Place(labels[i].rectTransform, colX + BlockW * 2f + 8f, rowY - 2f,
                            half - BlockW * 2f - Inset - 8f, 16f);
                    }
                }

                public override void Restyle()
                {
                    Paint();
                    frame.Paint(AvStyleHost.FuiFill("card", AvTheme.SurfaceInert),
                                AvStyleHost.FuiBorder("card", AvTheme.Hairline));
                    Color ink = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                    foreach (TMP_Text t in labels) t.color = ink;
                }
            }

            // ------------------------------------------------------------- refresh

            protected override void RefreshContent()
            {
                bool available = Available;
                ComMapOverlay source = Overlay;
                LogisticsMapOverlay logisticsSource = Logistics;
                bool overlaysReady = available && source != null && source.LayersAvailable;
                bool logisticsReady = available && logisticsSource != null && logisticsSource.Available &&
                    (logisticsSource.IndustryVisible || logisticsSource.SupplyVisible);

                visibleLayers = 0;
                for (int i = 0; i < NativeLayerCount; i++)
                    if (available && NativeLayer(i)) visibleLayers++;
                for (int i = 0; i < OverlayLayerCount; i++)
                    if (OverlayReady(i) && OverlayLayer(i)) visibleLayers++;

                if (nativeSwitches != null)
                    for (int i = 0; i < nativeSwitches.Length; i++)
                    {
                        nativeSwitches[i].Interactable = available;
                        nativeSwitches[i].Latched = available && NativeLayer(i);
                        nativeSwitches[i].Help = available ? LayerNames[i] + ": " + LayerSubtitles[i] + ". Lit = shown." : "Map options are not available yet.";
                    }
                else layers.SetData(NativeLayerCount, i => LayerNames[i], i => available && NativeLayer(i),
                    i => { if (ToggleReady()) ToggleNativeLayer(i); RequestRefresh(); }, i => available,
                    details: i => available ? LayerNames[i] + ": " + LayerSubtitles[i].ToLowerInvariant() + " on the map. Tap to show or hide." :
                        "Map options are not available yet.");
                if (overlaySwitches != null)
                    for (int i = 0; i < overlaySwitches.Length; i++)
                    {
                        bool ready = available && OverlayReady(i);
                        overlaySwitches[i].Interactable = ready;
                        overlaySwitches[i].Latched = ready && OverlayLayer(i);
                        overlaySwitches[i].Help = ready ? OverlayNames[i] + ": " + OverlaySubtitles[i] + ". Lit = shown." : "Waiting for the map and a faction headquarters.";
                    }
                else overlays.SetData(OverlayLayerCount, i => OverlayNames[i], i => OverlayReady(i) && OverlayLayer(i),
                    i => { if (ToggleReady() && OverlayReady(i)) SetOverlayLayer(i, !OverlayLayer(i)); RequestRefresh(); },
                    i => OverlayReady(i),
                    details: i => OverlayReady(i) ? OverlayNames[i] + ": " + OverlaySubtitles[i].ToLowerInvariant() + " drawn over the map. Tap to show or hide." :
                        "Waiting for the map and a faction headquarters.");

                hover.SetData(HoverNames.Length, i => HoverNames[i],
                    i => available && options.tooltipType == HoverModes[i],
                    i => { if (ToggleReady()) options.SetToolTipType((int)HoverModes[i]); RequestRefresh(); },
                    i => available,
                    details: i => available ? "Hover a map unit: " + HoverNotes[i].ToLowerInvariant() + ". Tap to make it the tooltip." :
                        "Map options are not available yet.");
                sizes.SetData(SizeNames.Length, i => SizeNames[i] + " " + AvNum.Percent((60 + 20 * i) / 100f, 0),
                    i => available && Mathf.Approximately(options.iconSize, .6f + .2f * i),
                    i => { if (ToggleReady()) options.SetIconSize(i); RequestRefresh(); }, i => available,
                    details: i => available ? "Draw map symbols at " + AvNum.Percent((60 + 20 * i) / 100f, 0) +
                        " (" + SizeSubtitles[i].ToLowerInvariant() + ")." : "Map options are not available yet.");
                bool anyLayer = available && visibleLayers > 0;
                bool everyLayer = available && visibleLayers == ShownLayerTarget();
                presetAll.Interactable = available && !everyLayer;
                presetNone.Interactable = anyLayer;
                presetAll.Help = !available ? "Map is not available yet." : everyLayer
                    ? "Every layer is already shown." : "Show every game layer and every Boscali overlay.";
                presetNone.Help = !available ? "Map is not available yet." : !anyLayer
                    ? "Every layer is already hidden." : "Hide every map layer. Panels and symbols stay as they are.";
                presetDefaults.Help = available
                    ? "Restore the game's defaults: every layer on, unit tooltips and full-size symbols."
                    : "Map is not available yet.";

                plotInk.Interactable = plot != null && plot.On;
                plotInk.Latched = plot != null && plot.On && plot.ShowOnMap;
                if (reliefToggle != null)
                {
                    var settings = Plugin.Settings?.Command;
                    reliefToggle.Interactable = settings != null;
                    reliefToggle.Latched = settings?.MapRelief3D.Value == true;
                    reliefToggle.Help = "Switch between the flat map and the 3D relief. Terrain image must also be enabled in SET MAP.";
                }

                metrics[0].Set(overlaysReady ? AvNum.Fixed(source.CellMetres, 0) : "—", "M",
                    overlaysReady ? 1f : 0f, overlaysReady ? AvState.Ready : AvState.Inert);
                metrics[1].Set(overlaysReady ? (source.HasControlData ? "LIVE" : "NO DATA") : "—", "",
                    overlaysReady ? 1f : 0f,
                    !overlaysReady ? AvState.Inert : source.HasControlData ? AvState.Ready : AvState.Caution);
                // Counts come from the overlay's last pass, so both layers hidden reads a dash, never a stale zero.
                metrics[2].Set(logisticsReady ? AvNum.Fixed(logisticsSource.SiteCount, 0) : "—",
                    logisticsReady ? AvNum.Fixed(logisticsSource.RouteCount, 0) + " TO FRONT" : "",
                    logisticsReady ? 1f : 0f, logisticsReady ? AvState.Ready : AvState.Inert);
                metrics[3].Set(logisticsReady ? AvNum.Fixed(logisticsSource.TruckCount, 0) : "—",
                    logisticsReady ? "TRUCKS" : "", logisticsReady ? 1f : 0f, logisticsReady ? AvState.Ready : AvState.Inert);

                if (selectedPage == ToolsPage) RefreshTools();
            }

            /// <summary>The whole set is only as large as the overlays that can actually draw.</summary>
            private int ShownLayerTarget()
            {
                int total = NativeLayerCount;
                for (int i = 0; i < OverlayLayerCount; i++)
                    if (OverlayReady(i)) total++;
                return total;
            }

            /// <summary>Checked at click time, not at build time: a scene change can land between.</summary>
            private bool ToggleReady() => options != null && SceneSingleton<DynamicMap>.i != null;

            protected override string AmbientStatus() => !Available
                ? "WAITING FOR MAP"
                : selectedPage == ToolsPage
                    ? MfdMapInteractions.Feedback ?? "ARM A TOOL HERE, THEN RIGHT-CLICK THE MAP"
                    : AvNum.Fixed(visibleLayers, 0) + "/" + AvNum.Fixed(ShownLayerTarget(), 0) + " LAYERS";
        }
    }
}
