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
        /// <summary>
        /// The MAP bezel: every map tool (TOOLS), which map layers and Boscali overlays are drawn
        /// (LAYERS) and how its symbols read (READABILITY). TOOLS only surfaces what the map input
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
            private static readonly string[] OverlayBriefs =
            {
                "Who holds each sector. Source: actual ground units, airbases.",
                "Boundary of the two held areas. Source: the control field.",
                "Square: factory. Diamond: depot. Triangle: ammo dump. Side colours; enemy sites only where your side knows them.",
                "Dashed: each friendly factory, depot and ammo dump to the nearest point of the front. Chevron: a friendly supply truck and its heading; small diamond: a known enemy truck.",
                "Relief only: the game's road network drawn on the terrain. Source: the mission's road map.",
            };
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
            // TOOLS: the quick layers are overlay indices (FRONTS, SECTORS, ROADS, INDUSTRY, CONVOYS), then COM's plot ink.
            private static readonly int[] QuickOverlays = { 1, 0, RoadsLayer, 2, 3 };
            private static readonly string[] QuickNames = { "FRONTS", "SECTORS", "ROADS", "INDUSTRY", "CONVOYS", "PLOT" };
            // In MapRightClick order: the first claim wins (rule B).
            private static readonly string[] RuleNames = { "1 · ARMED TOOL", "2 · MOVE WING", "3 · WAYPOINT", "4 · PIN THE SPOT" };
            private static readonly string[] RuleNotes =
            {
                "A MARK, PLOT, support or wing tool you armed places where you click.",
                "A WMC wingman selection flies there.",
                "A friendly unit that takes orders gets the game's own waypoint.",
                "Nothing else claims it: the card opens on that spot.",
            };
            private const int CursorRows = 10;

            private readonly MapOptions options;
            private readonly SymbolTile[] previewTiles = new SymbolTile[3];
            private MfdPagingGrid quick;
            private readonly AvRow[] ruleRows = new AvRow[RuleNames.Length];
            private AvSection selectSection, markSection, cursorSection;
            private AvControl[] markTools, viewTools;
            private ICommsPlot plot;
            private AvPart[] plotParts;
            private AvNote plotOff;
            private readonly AvRow[] cursorRows = new AvRow[CursorRows];
            private readonly List<MapMenuRow> cursorFacts = new List<MapMenuRow>(12);

            private ComMapOverlay overlay;
            private LogisticsMapOverlay logistics;
            private RoadMapOverlay roads;
            private MfdPagingGrid layers, overlays, hover, sizes;
            private AvControl presetAll, presetNone, presetDefaults;
            private AvNote detailSummary, previewCaption;
            private readonly AvRow[] overlayRows = new AvRow[OverlayLayerCount];
            private AvMetric[] metrics;
            private int selectedPage;
            private int visibleLayers;

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map) { this.options = options; }

            protected override (AvIcon Icon, string Label)[] TabItems => new[]
            {
                (AvIcon.Ruler2, "TOOLS"),
                (AvIcon.LayersSubtract, "LAYERS"),
                (AvIcon.Focus2, "READABILITY"),
            };

            protected override string[] TabTips { get; } = new[]
            {
                "Tools: quick layers, what right mouse does, selection, marks, the shared plot, the view and what lies under the cursor.",
                "Layers: show or hide the game's map layers and the Boscali overlays, with the overlay legend beneath them.",
                "Readability: hover tooltip mode, symbol size with a live sample, and the key to every contact symbol.",
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
                BuildReadability(CreatePage());

                // The footer shows these one at a time under the status line, most important first.
                Console.Orders(ToolsPage,
                    "Arm a MARK or PLOT tool here, then right-click the map. ESC puts it down.",
                    "Right mouse goes to the first claim: an armed tool, a wing move, a waypoint, then the card.",
                    "Left mouse selects: click a contact, SHIFT drag boxes in, CTRL drag boxes out.");
                Console.Orders(LayersPage,
                    "Left-click a tile to show or hide that layer.",
                    "ALL ON and HIDE ALL act on the game layers and the Boscali overlays together.",
                    "DEFAULTS restores the game's own settings: every layer on, unit tooltips, full-size symbols.",
                    "An overlay that reads WAIT has no map or faction headquarters to draw from yet.");
                Console.Orders(LayersPage + 1,
                    "Frame shape names the side; the glyph names the platform class.",
                    "Shape carries the side without colour, so a symbol reads on any display scheme.",
                    "Symbol size scales every contact on the map. The sample above shows the change.",
                    "Pick what the hover tooltip shows: unit information, ammunition or current orders.");
            }
            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                RequestRefresh();
            }

            // -------------------------------------------------------------- tools

            private void BuildTools(AvFlow page)
            {
                page.Section(AvIcon.LayersSubtract, "QUICK LAYERS", "EVERY LAYER ON LAYERS");
                quick = AddGrid(page, new MfdPagingGrid(page.Content, QuickNames.Length, 1, pager: false, rowHeight: 56f, tile: true, words: false));
                quick.SetGlyphs(i => i < QuickOverlays.Length ? OverlayGlyphs[QuickOverlays[i]] : AvIcon.Pencil);

                page.Section(AvIcon.ListDetails, "WHAT RIGHT MOUSE DOES", "FIRST ONE THAT FITS WINS");
                for (int i = 0; i < ruleRows.Length; i++)
                {
                    ruleRows[i] = page.Add(new AvRow(page.Content));
                    ruleRows[i].Set(RuleNames[i], RuleNotes[i], "", AvState.Inert);
                }

                selectSection = page.Section(AvIcon.Target, "SELECT", "THE LIST LIVES ON TGT");
                page.Add(new AvRow(page.Content)).Set("LEFT MOUSE",
                    "Click a contact to add or drop it. SHIFT drag boxes contacts in, CTRL drag boxes them out.", "", AvState.Inert);
                AvControl[] select = page.Buttons(
                    new AvControl.Spec("FRAME", MfdMapInteractions.FrameSelection, AvButtonStyle.Default, AvIcon.Focus2),
                    new AvControl.Spec("SAME TYPE", MfdMapInteractions.SelectSameType, AvButtonStyle.Default, AvIcon.Filter),
                    new AvControl.Spec("CLEAR", MfdMapInteractions.ClearSelectionTool, AvButtonStyle.Default, AvIcon.Eraser)).Controls;
                select[0].Help = "Frame: centre the map on what is selected.";
                select[1].Help = "Same type: select every visible contact of the first selected one's type and side.";
                select[2].Help = "Clear: empty the selection, and with it your target list while flying.";

                markSection = page.Section(AvIcon.Ruler2, "MARK", "ARM ONE · RMB PLACES IT · ONLY YOU SEE IT");
                markTools = page.Buttons(
                    new AvControl.Spec("RANGE", () => ArmMark(MfdMapInteractions.MarkTool.Range), AvButtonStyle.Toggle, AvIcon.Ruler2),
                    new AvControl.Spec("LANDING ZONE", () => ArmMark(MfdMapInteractions.MarkTool.Zone), AvButtonStyle.Toggle, AvIcon.Parachute),
                    new AvControl.Spec("COPY FIX", () => ArmMark(MfdMapInteractions.MarkTool.Fix), AvButtonStyle.Toggle, AvIcon.Copy),
                    new AvControl.Spec("CLEAR", () => { MfdMapInteractions.ClearMarks(); RequestRefresh(); }, AvButtonStyle.Default, AvIcon.Eraser)).Controls;
                markTools[0].Help = "Range: right-click the map to drop your range mark; the cursor line and AT THE CURSOR read from it.";
                markTools[1].Help = "Landing zone: right-click the map; every HALO stick and glider you release aims there.";
                markTools[2].Help = "Copy fix: right-click the map to copy that ground's X and Z to the clipboard.";
                markTools[3].Help = "Clear: remove your range mark and your landing zone.";

                // COM's shared plot, built by Comms on this page; one dim line while Comms is off.
                plotOff = page.Add(new AvNote(page.Content, "PLOT off: COMMS is switched off in F1."));
                if (ModuleServices.TryGet(out plot)) plotParts = plot.Build(page);

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

                cursorSection = page.Section(AvIcon.Crosshair, "AT THE CURSOR", "THE LAST GROUND THE POINTER CROSSED");
                for (int i = 0; i < cursorRows.Length; i++)
                {
                    cursorRows[i] = page.Add(new AvRow(page.Content));
                    cursorRows[i].SetShown(false);
                }
                page.Links(("HELD TARGETS", "TGT", "TARGETS"), ("WHO IS ON THE NET", "COM", "NET"));
            }

            private void ArmMark(MfdMapInteractions.MarkTool tool)
            {
                MfdMapInteractions.ArmMark(tool);
                RequestRefresh();
            }

            private bool QuickReady(int i) => i < QuickOverlays.Length ? OverlayReady(QuickOverlays[i]) : plot != null && plot.On;

            private bool QuickOn(int i) => QuickReady(i) && (i < QuickOverlays.Length ? OverlayLayer(QuickOverlays[i]) : plot.ShowOnMap);

            private void ToggleQuick(int i)
            {
                if (!ToggleReady() || !QuickReady(i)) return;
                if (i < QuickOverlays.Length) SetOverlayLayer(QuickOverlays[i], !OverlayLayer(QuickOverlays[i]));
                else plot.ShowOnMap = !plot.ShowOnMap;
            }

            private void RefreshTools()
            {
                quick.SetData(QuickNames.Length, i => QuickNames[i], QuickOn, i => { ToggleQuick(i); RequestRefresh(); }, QuickReady,
                    details: i => i < QuickOverlays.Length
                        ? OverlayNames[QuickOverlays[i]] + ": " + OverlaySubtitles[QuickOverlays[i]].ToLowerInvariant() + ". Tap to show or hide."
                        : "Plot: the pings, drawings and text your net team puts on the map. Tap to show or hide.");

                DynamicMap map = SceneSingleton<DynamicMap>.i;
                int rule = map != null ? (int)MfdMapInteractions.RightRule(map) : (int)MapRightClick.Card;
                for (int i = 0; i < ruleRows.Length; i++)
                    ruleRows[i].Set(RuleNames[i], RuleNotes[i], i == rule ? "NOW" : "", i == rule ? AvState.Caution : AvState.Inert);

                selectSection.SetCaption((map != null && map.selectedIcons != null ? map.selectedIcons.Count : 0) + " SELECTED · THE LIST LIVES ON TGT");

                MfdMapInteractions.MarkTool armed = MfdMapInteractions.ArmedMark;
                bool marked = MfdMapInteractions.Mark.HasValue, zoned = MfdMapInteractions.Zoned;
                markTools[0].Latched = armed == MfdMapInteractions.MarkTool.Range;
                markTools[1].Latched = armed == MfdMapInteractions.MarkTool.Zone;
                markTools[2].Latched = armed == MfdMapInteractions.MarkTool.Fix;
                markTools[1].Interactable = MfdMapInteractions.ZoneOffered;
                markTools[3].Interactable = marked || zoned;
                markSection.SetCaption((marked ? "RANGE MARK SET" : "NO RANGE MARK") + " · " + (zoned ? "LZ SET" : "NO LZ") + " · ONLY YOU SEE THEM");

                bool plotOn = plot != null && plot.On;
                plotOff.SetShown(!plotOn);
                for (int i = 0; plotParts != null && i < plotParts.Length; i++) plotParts[i]?.SetShown(plotOn);
                if (plotOn) plot.Refresh();

                bool relief = MfdTerrainRelief.IsDrawing;
                foreach (AvControl c in viewTools) c.Interactable = relief;
                viewTools[2].Latched = ReliefNavigator.Following;
                viewTools[3].Latched = ReliefNavigator.Flying;

                GlobalPosition? at = MfdMapInteractions.Cursor;
                if (map != null && at.HasValue) MapContextRead.Fill(map, at.Value, MfdMapInteractions.Mark, cursorFacts);
                else cursorFacts.Clear();
                cursorSection.SetCaption(at.HasValue ? "THE LAST GROUND THE POINTER CROSSED" : "MOVE THE POINTER OVER THE MAP");
                for (int i = 0; i < cursorRows.Length; i++)
                {
                    bool shown = i < cursorFacts.Count;
                    cursorRows[i].SetShown(shown);
                    if (shown) cursorRows[i].Set(cursorFacts[i].Key, cursorFacts[i].Value, "", cursorFacts[i].Value == "—" ? AvState.Inert : AvState.Info);
                }
            }

            // ------------------------------------------------------------- layers

            private void BuildLayers(AvFlow page)
            {
                page.Section(AvIcon.Filter, "GAME LAYERS", "STOCK MAP OPTIONS");
                layers = new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true);
                layers.SetGlyphs(i => LayerGlyphs[Mathf.Clamp(i, 0, LayerGlyphs.Length - 1)]);
                AddGrid(page, layers);

                page.Section(AvIcon.Scale, "THEATER OVERLAYS", "BOSCALI PLOT");
                overlays = new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true);
                overlays.SetGlyphs(i => OverlayGlyphs[Mathf.Clamp(i, 0, OverlayGlyphs.Length - 1)]);
                AddGrid(page, overlays);

                // The master switches sit with the tiles they set; the brief and the ink key below can scroll.
                AvButtons presetRow = page.Buttons(
                    new AvControl.Spec("ALL ON", () => SetAllLayers(true), AvButtonStyle.Default, AvIcon.LayersSubtract),
                    new AvControl.Spec("HIDE ALL", () => SetAllLayers(false), AvButtonStyle.Default, AvIcon.X),
                    new AvControl.Spec("DEFAULTS", ApplyDefaults, AvButtonStyle.Default, AvIcon.Refresh));
                presetAll = presetRow.Controls[0];
                presetNone = presetRow.Controls[1];
                presetDefaults = presetRow.Controls[2];

                // What each overlay draws and where its data comes from, with its live state.
                page.Section(AvIcon.InfoCircle, "OVERLAY BRIEF", "WHAT IT DRAWS · WHERE FROM");
                for (int i = 0; i < OverlayLayerCount; i++)
                {
                    overlayRows[i] = page.Add(new AvRow(page.Content));
                    overlayRows[i].Set(OverlayNames[i], OverlayBriefs[i], "—", AvState.Inert);
                }

                // The overlay legend sits under the overlay toggles it explains (it used to live on READABILITY).
                page.Add(new LegendCard(page.Content));
                page.Links(("FRONTS", "STR", "BOARD"), ("TARGETS", "TGT", "TARGETS"));
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

            // -------------------------------------------------------- readability

            private void BuildReadability(AvFlow page)
            {
                page.Section(AvIcon.Focus2, "SYMBOL SAMPLE", "THE MAP'S OWN SPRITES");
                var preview = new AvCard(page.Content, page.Ticker, page.Inner, "UNIT SIGNATURE · SCALE SAMPLE");
                page.Add(preview);
                AvFlow previewFlow = preview.Flow;
                previewTiles[0] = new SymbolTile(previewFlow.Content, "AIR", "AIRCRAFT");
                previewTiles[1] = new SymbolTile(previewFlow.Content, "GND", "GROUND");
                previewTiles[2] = new SymbolTile(previewFlow.Content, "SHP", "SURFACE");
                previewFlow.Row(previewTiles[0], previewTiles[1], previewTiles[2]);
                previewCaption = new AvNote(previewFlow.Content, "");
                previewFlow.Add(previewCaption);

                page.Section(AvIcon.Eye, "HOVER READOUT", "TOOLTIP");
                hover = new MfdPagingGrid(page.Content, 4, 1, pager: false, rowHeight: 56f, tile: true);
                hover.SetGlyphs(i => HoverGlyphs[Mathf.Clamp(i, 0, HoverGlyphs.Length - 1)]);
                AddGrid(page, hover);

                detailSummary = new AvNote(page.Content, "");
                page.Add(detailSummary);

                page.Section(AvIcon.Focus2, "SYMBOL SCALE", "ALL CONTACTS");
                sizes = new MfdPagingGrid(page.Content, 3, 1, pager: false, rowHeight: 56f, tile: true);
                sizes.SetGlyphs(i => SizeGlyphs[Mathf.Clamp(i, 0, SizeGlyphs.Length - 1)]);
                AddGrid(page, sizes);

                page.Section(AvIcon.Focus2, "SYMBOL KEY", "FRAME = SIDE · GLYPH = TYPE");
                page.Add(new SymbolKey(page.Content));
                page.Links(("CONTACT PRIORITY", "TGT", "FILTER"), ("DISPLAY SETTINGS", "SET", null));
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

            /// <summary>One sample of the map's own contact symbols (the real atlas sprites, so the preview
            /// cannot drift from the map) over its label. A local AvPart because the kit has no
            /// symbol-plus-caption tile; scale is applied around the symbol's own centre so the
            /// symbol-size preview grows and shrinks in place instead of drifting.</summary>
            private sealed class SymbolTile : AvPart
            {
                private readonly Image plate, mark;
                private readonly MapSide side;
                private readonly TMP_Text label;

                public SymbolTile(RectTransform parent, string kind, string labelText)
                {
                    Rect = AvLay.Child(parent, "Symbol " + kind);
                    side = kind == "GND" ? MapSide.Hostile : kind == "SHP" ? MapSide.Neutral : MapSide.Friendly;
                    MapGlyph glyph = kind == "GND" ? MapGlyph.Armor : kind == "SHP" ? MapGlyph.Hull : MapGlyph.Aircraft;
                    MakeSymbol(Rect, side, glyph, out plate, out mark);
                    label = AvText.Make(Rect, "Label", AvTextRole.Micro, labelText, TextAlignmentOptions.Center);
                }

                public void SetState(bool available, float scale)
                {
                    plate.enabled = mark.enabled = available;
                    Vector3 grown = Vector3.one * Mathf.Clamp(scale, .1f, 2f);
                    plate.rectTransform.localScale = mark.rectTransform.localScale = grown;
                }

                public override float Measure(float width) => 82f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float size = Mathf.Clamp(s.W * 0.5f, 34f, 52f);
                    PlaceSymbol(plate, mark, s.W * 0.5f, 4f + size * 0.5f, size);
                    AvLay.Place(label.rectTransform, 0f, 4f + size + 8f, s.W, 16f);
                }

                public override void Restyle()
                {
                    label.color = AvStyleHost.FuiInk("section-caption", AvTheme.Dim);
                    plate.color = mark.color = SideTint(side);
                }
            }

            /// <summary>Key to every contact symbol: the three frames (allegiance) and the glyph per platform
            /// class. Painted from the same sprites the map draws with.</summary>
            private sealed class SymbolKey : AvPart
            {
                private const int Columns = 6;
                private const float Pitch = 62f, Inset = 12f, Size = 32f;
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
                    Mathf.CeilToInt(Entries.Length / (float)Columns) * Pitch + Inset * 2f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float cell = (s.W - Inset * 2f) / Columns;
                    // A growing key spreads its rows over the spare height instead of leaving a blank band.
                    int rows = Mathf.CeilToInt(Entries.Length / (float)Columns);
                    float pitch = Mathf.Max(Pitch, (s.H - Inset * 2f) / rows);
                    for (int i = 0; i < Entries.Length; i++)
                    {
                        float x = Inset + (i % Columns) * cell;
                        float y = Inset + (i / Columns) * pitch;
                        PlaceSymbol(plates[i], marks[i], x + cell * .5f, y + Size * .5f, Size);
                        AvLay.Place(labels[i].rectTransform, x, y + Size + 4f, cell, 14f);
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

                public override float Measure(float width) =>
                    Mathf.CeilToInt(LegendLabels.Length / 2f) * Pitch + Inset * 2f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float half = s.W / 2f;
                    for (int i = 0; i < LegendLabels.Length; i++)
                    {
                        float colX = Inset + (i % 2) * half;
                        float rowY = Inset + (i / 2) * Pitch;
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

                layers.SetData(NativeLayerCount, i => LayerNames[i], i => available && NativeLayer(i),
                    i => { if (ToggleReady()) ToggleNativeLayer(i); RequestRefresh(); }, i => available,
                    details: i => available ? LayerNames[i] + ": " + LayerSubtitles[i].ToLowerInvariant() + " on the map. Tap to show or hide." :
                        "Map options are not available yet.");
                overlays.SetData(OverlayLayerCount, i => OverlayNames[i], i => OverlayReady(i) && OverlayLayer(i),
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
                        " (" + SizeSubtitles[i].ToLowerInvariant() + "). The sample above shows the change." : "Map options are not available yet.");
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

                for (int i = 0; i < OverlayLayerCount; i++)
                    SetOverlayRow(i, OverlayReady(i), OverlayReady(i) && OverlayLayer(i), overlaysReady && source.HasControlData);

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

                detailSummary.Set(!available ? "—" : "TOOLTIP " + TooltipName());

                float scale = available && IsUsableSize(options.iconSize)
                    ? Mathf.Clamp(options.iconSize, .1f, 2f) : 1f;
                foreach (SymbolTile tile in previewTiles) tile.SetState(available, scale);
                previewCaption.Set(available ? SizeLabel() : "—");
                if (selectedPage == ToolsPage) RefreshTools();
            }

            /// <summary>One overlay's brief row. The control field also states whether it has ground presence to draw from.</summary>
            private void SetOverlayRow(int index, bool ready, bool shown, bool controlData)
            {
                bool field = index == 0;
                string word = !ready ? "WAIT" : !shown ? "OFF" : field && !controlData ? "NO DATA" : field ? "LIVE" : "ON";
                overlayRows[index].Set(OverlayNames[index], OverlayBriefs[index], word,
                    !shown ? AvState.Inert : field && !controlData ? AvState.Caution : AvState.Ready);
            }

            /// <summary>The whole set is only as large as the overlays that can actually draw.</summary>
            private int ShownLayerTarget()
            {
                int total = NativeLayerCount;
                for (int i = 0; i < OverlayLayerCount; i++)
                    if (OverlayReady(i)) total++;
                return total;
            }

            private static bool IsUsableSize(float value) =>
                float.IsFinite(value);

            /// <summary>Checked at click time, not at build time: a scene change can land between.</summary>
            private bool ToggleReady() => options != null && SceneSingleton<DynamicMap>.i != null;

            private string TooltipName()
            {
                switch (options.tooltipType)
                {
                    case MapOptions.TooltipType.None: return "OFF";
                    case MapOptions.TooltipType.Info: return "INFO";
                    case MapOptions.TooltipType.Ammo: return "AMMO";
                    case MapOptions.TooltipType.Order: return "ORDERS";
                    default: return "UNKNOWN";
                }
            }

            private string SizeLabel()
            {
                if (Mathf.Approximately(options.iconSize, .6f)) return "SMALL 60%";
                if (Mathf.Approximately(options.iconSize, .8f)) return "MEDIUM 80%";
                if (Mathf.Approximately(options.iconSize, 1f)) return "LARGE 100%";
                return "CUSTOM";
            }

            protected override string AmbientStatus() => !Available
                ? "WAITING FOR MAP"
                : selectedPage == ToolsPage
                    ? MfdMapInteractions.Feedback ?? "ARM A TOOL HERE, THEN RIGHT-CLICK THE MAP"
                    : selectedPage == LayersPage
                    ? AvNum.Fixed(visibleLayers, 0) + "/" + AvNum.Fixed(ShownLayerTarget(), 0) + " LAYERS"
                    : "SYMBOLOGY · LIT = SELECTED";
        }
    }
}
