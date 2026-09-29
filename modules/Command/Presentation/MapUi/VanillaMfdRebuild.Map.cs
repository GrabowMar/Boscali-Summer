using BoscaliSummer.Features.Command.Presentation;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        /// <summary>
        /// The MAP bezel: which map layers and Boscali overlays are drawn and how its
        /// symbols read.
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
            private const int OverlayLayerCount = 4;

            private static readonly string[] LayerNames =
                { "OBJECTIVES", "TARGET DETAILS", "JAMMING", "GRID LABELS", "PILOTS", "AIRBASES" };
            private static readonly string[] LayerSubtitles =
                { "Mission markers", "Target markers", "Jam indicators", "Grid coordinates", "Pilot icons", "Base icons" };
            private static readonly string[] OverlayNames =
                { "CONTROL FIELD", "FRONT LINE", "SENSOR CONTOURS", "SATELLITE MAP" };
            private static readonly string[] OverlaySubtitles =
                { "Sector control", "Control boundary", "Sensor coverage", "Terrain imagery" };
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
                { "SECTOR CELL", "CONTROL DATA", "THREAT TRACKS", "TERRAIN DECK" };
            private static readonly string[] LegendLabels =
                { "FRIENDLY GROUND", "HOSTILE GROUND", "CONTESTED", "FRONT LINE", "RADAR HEAT", "OPTICAL / IR" };

            private readonly MapOptions options;
            private readonly SymbolTile[] previewTiles = new SymbolTile[3];

            private ComMapOverlay overlay;
            private ThreatMapOverlay threats;
            private MfdPagingGrid layers, overlays, hover, sizes;
            private AvControl presetAll, presetNone, presetDefaults;
            private ProseNote overlayNote, detailSummary, previewCaption;
            private AvMetric[] metrics;
            private AvChip[] chips;
            private int selectedPage;
            private int visibleLayers;

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map) { this.options = options; }

            protected override string Title => "MAP";

            protected override (AvIcon Icon, string Label)[] TabItems => new[]
            {
                (AvIcon.LayersSubtract, "LAYERS"),
                (AvIcon.Focus2, "READABILITY"),
            };

            private bool Available => options != null && SceneSingleton<DynamicMap>.i != null;

            /// <summary>Resolved once and re-resolved after a rollback; never a scene scan.</summary>
            private ComMapOverlay Overlay
            {
                get
                {
                    if (overlay == null) ModServices.TryGet(out overlay);
                    return overlay;
                }
            }

            private ThreatMapOverlay Threats
            {
                get
                {
                    if (threats == null) ModServices.TryGet(out threats);
                    return threats;
                }
            }

            protected override void BuildContent()
            {
                // The four live readouts and the three status chips are console-level chrome,
                // not page content — they stay visible on both LAYERS and READABILITY.
                metrics = Console.Metrics(ReadoutKeys);
                chips = Console.Chips(3);

                BuildLayers(CreatePage());
                BuildReadability(CreatePage());
            }

            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                RequestRefresh();
            }

            // ------------------------------------------------------------- layers

            private void BuildLayers(AvFlow page)
            {
                page.Section(AvIcon.Filter, "GAME SYMBOLOGY", NativeLayerCount + " NATIVE LAYERS");
                layers = new MfdPagingGrid(page.Content, 2, 3, pager: false);
                AddGrid(page, layers);

                page.Section(AvIcon.Scale, "THEATER OVERLAYS", "TACTICAL RASTER");
                overlays = new MfdPagingGrid(page.Content, 2, 2, pager: false);
                AddGrid(page, overlays);

                overlayNote = new ProseNote(page.Content, "");
                page.Add(overlayNote);

                AvButtons presetRow = page.Buttons(
                    new AvControl.Spec("ALL ON", () => SetAllLayers(true), AvButtonStyle.Default, AvIcon.LayersSubtract),
                    new AvControl.Spec("HIDE ALL", () => SetAllLayers(false), AvButtonStyle.Default, AvIcon.X),
                    new AvControl.Spec("DEFAULTS", ApplyDefaults, AvButtonStyle.Default, AvIcon.Refresh));
                presetAll = presetRow.Controls[0];
                presetNone = presetRow.Controls[1];
                presetDefaults = presetRow.Controls[2];
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
                if (index == 2) return Threats != null && Threats.Visible;
                return Overlay != null && Overlay.TerrainImageVisible;
            }

            private void SetOverlayLayer(int index, bool visible)
            {
                if (index == 0) Overlay?.SetControlFieldVisible(visible);
                else if (index == 1) Overlay?.SetFrontLineVisible(visible);
                else if (index == 2) Threats?.SetVisible(visible);
                else Overlay?.SetTerrainImageVisible(visible);
            }

            /// <summary>Whether one overlay has the map and the state it needs to draw at all.</summary>
            private bool OverlayReady(int index)
            {
                if (index < 2) return Overlay != null && Overlay.LayersAvailable;
                if (index == 2) return Threats != null && Threats.Available;
                return Overlay != null && Overlay.LayersAvailable;
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

                for (int i = 0; i < OverlayLayerCount; i++)
                    if (OverlayReady(i) && !OverlayLayer(i)) SetOverlayLayer(i, true);

                RequestRefresh();
            }

            // -------------------------------------------------------- readability

            private void BuildReadability(AvFlow page)
            {
                page.Section(AvIcon.Focus2, "SYMBOL DECK", "ILLUSTRATIVE SCALE");
                var preview = new AvCard(page.Content, page.Ticker, page.Inner, "UNIT SIGNATURE  /  SCALE SAMPLE");
                page.Add(preview);
                AvFlow previewFlow = preview.Flow;
                previewTiles[0] = new SymbolTile(previewFlow.Content, "AIR", "AIRCRAFT");
                previewTiles[1] = new SymbolTile(previewFlow.Content, "GND", "GROUND");
                previewTiles[2] = new SymbolTile(previewFlow.Content, "SHP", "SURFACE");
                previewFlow.Row(previewTiles[0], previewTiles[1], previewTiles[2]);
                previewCaption = new ProseNote(previewFlow.Content, "");
                previewFlow.Add(previewCaption);

                page.Section(AvIcon.Eye, "CONTACT HOVER", "CHOOSE ONE");
                hover = new MfdPagingGrid(page.Content, 2, 2, pager: false);
                AddGrid(page, hover);

                detailSummary = new ProseNote(page.Content, "");
                page.Add(detailSummary);

                page.Section(AvIcon.Focus2, "SYMBOL SIZE", "CHOOSE ONE");
                sizes = new MfdPagingGrid(page.Content, 1, 3, pager: false);
                AddGrid(page, sizes);

                page.Section(AvIcon.Map2, "MAP LEGEND", "BOSCALI OVERLAYS");
                page.Add(new LegendCard(page.Content));
                page.Section(AvIcon.Focus2, "CONTACT SYMBOLS", "FRAME = SIDE  /  GLYPH = TYPE");
                page.Add(new SymbolKey(page.Content));
            }

            /// <summary>The colour a contact side wears on the map: the tints the game gives friendly and
            /// hostile icons, and a neutral grey. Shape carries the same distinction without colour.</summary>
            private static Color SideTint(MapSide side) =>
                side == MapSide.Friendly ? AvTheme.RailInfo
                : side == MapSide.Hostile ? AvTheme.RailDanger
                : AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);

            /// <summary>Builds one framed contact symbol exactly as the map draws it: plate, then glyph.</summary>
            private static void MakeSymbol(RectTransform parent, MapSide side, MapGlyph glyph,
                out Image plate, out Image mark)
            {
                plate = NewSymbolImage(parent, "Plate", MapSymbolAtlas.Plate(side), SideTint(side));
                mark = NewSymbolImage(parent, "Glyph", MapSymbolAtlas.Glyph(glyph), SideTint(side));
            }

            private static Image NewSymbolImage(RectTransform parent, string name, Sprite sprite, Color tint)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                Image image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.color = tint;
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
                    label.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Dim);
                    plate.color = mark.color = SideTint(side);
                }
            }

            /// <summary>Key to every contact symbol: the three frames (allegiance) and the glyph per platform
            /// class. Painted from the same sprites the map draws with.</summary>
            private sealed class SymbolKey : AvPart
            {
                private const int Columns = 4;
                private const float Pitch = 64f, Inset = 12f, Size = 32f;
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
                    for (int i = 0; i < Entries.Length; i++)
                    {
                        float x = Inset + (i % Columns) * cell;
                        float y = Inset + (i / Columns) * Pitch;
                        PlaceSymbol(plates[i], marks[i], x + cell * .5f, y + Size * .5f, Size);
                        AvLay.Place(labels[i].rectTransform, x, y + Size + 4f, cell, 14f);
                    }
                }

                public override void Restyle()
                {
                    frame.Paint(AvStyleHost.Resolve(AvStyleHost.FuiStyle("card").Background, AvTheme.SurfaceInert),
                                AvStyleHost.Resolve(AvStyleHost.FuiStyle("card").Border, AvTheme.Hairline));
                    Color ink = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
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
                private const float Pitch = 26f, BlockW = 10f, BlockH = 12f, Inset = 12f;
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
                            : i == 4 ? (Color32)AvTheme.RailDanger : i == 5 ? (Color32)AvTheme.RailCaution : friendly;
                        Color32 r = i == 2 ? contestedRight : l;
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
                    frame.Paint(AvStyleHost.Resolve(AvStyleHost.FuiStyle("card").Background, AvTheme.SurfaceInert),
                                AvStyleHost.Resolve(AvStyleHost.FuiStyle("card").Border, AvTheme.Hairline));
                    Color ink = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                    foreach (TMP_Text t in labels) t.color = ink;
                }
            }

            // ------------------------------------------------------------- refresh

            protected override void RefreshContent()
            {
                bool available = Available;
                ComMapOverlay source = Overlay;
                ThreatMapOverlay threatSource = Threats;
                bool overlaysReady = available && source != null && source.LayersAvailable;
                bool threatsReady = available && threatSource != null && threatSource.Available;

                visibleLayers = 0;
                for (int i = 0; i < NativeLayerCount; i++)
                    if (available && NativeLayer(i)) visibleLayers++;
                for (int i = 0; i < OverlayLayerCount; i++)
                    if (OverlayReady(i) && OverlayLayer(i)) visibleLayers++;

                layers.SetData(NativeLayerCount, i => LayerNames[i], i => available && NativeLayer(i),
                    i => { if (ToggleReady()) ToggleNativeLayer(i); RequestRefresh(); }, i => available,
                    subs: i => available ? LayerSubtitles[i] : null);
                overlays.SetData(OverlayLayerCount, i => OverlayNames[i], i => OverlayReady(i) && OverlayLayer(i),
                    i => { if (ToggleReady() && OverlayReady(i)) SetOverlayLayer(i, !OverlayLayer(i)); RequestRefresh(); },
                    i => OverlayReady(i),
                    subs: i => OverlayReady(i) ? OverlaySubtitles[i] : null);

                bool tooltipOn = available && options.tooltipType != MapOptions.TooltipType.None;
                hover.SetData(HoverNames.Length, i => HoverNames[i],
                    i => available && options.tooltipType == HoverModes[i],
                    i => { if (ToggleReady()) options.SetToolTipType((int)HoverModes[i]); RequestRefresh(); },
                    i => available,
                    details: i => available ? "Hover a map unit: " + HoverNotes[i].ToLowerInvariant() + "." :
                        "Map options are not available yet.",
                    subs: i => available ? HoverNotes[i] : null);
                sizes.SetData(SizeNames.Length, i => SizeNames[i] + " " + AvNum.Percent((60 + 20 * i) / 100f, 0),
                    i => available && Mathf.Approximately(options.iconSize, .6f + .2f * i),
                    i => { if (ToggleReady()) options.SetIconSize(i); RequestRefresh(); }, i => available,
                    details: i => available ? "Draw map symbols at " + AvNum.Percent((60 + 20 * i) / 100f, 0) +
                        " (" + SizeSubtitles[i] + ")." : "Map options are not available yet.",
                    subs: i => available ? SizeSubtitles[i] : null);

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

                overlayNote.Set(overlaysReady
                    ? (source.HasControlData
                        ? "Sector control follows real ground presence; the front is its zero contour."
                        : "No faction ground data on this map yet, so the control field is empty.")
                    : "Command's overlay waits for the map and a faction headquarters.");

                metrics[0].Set(overlaysReady ? AvNum.Fixed(source.CellMetres, 0) : "—", "M",
                    overlaysReady ? 1f : 0f, overlaysReady ? AvState.Ready : AvState.Inert);
                metrics[1].Set(overlaysReady ? (source.HasControlData ? "LIVE" : "NO DATA") : "—", "",
                    overlaysReady ? 1f : 0f,
                    !overlaysReady ? AvState.Inert : source.HasControlData ? AvState.Ready : AvState.Caution);
                metrics[2].Set(!threatsReady ? "—" : AvNum.Fixed(threatSource.TrackedEmitters, 0),
                    !threatsReady ? "" : threatSource.TrackedEmitters > 0
                        ? AvNum.Fixed(threatSource.WidestEnvelopeKm, 0) + " KM" : "NONE",
                    threatsReady ? 1f : 0f,
                    !threatsReady ? AvState.Inert : threatSource.TrackedEmitters > 0 ? AvState.Caution : AvState.Ready);
                metrics[3].Set(overlaysReady ? (source.TerrainImageVisible ? "SAT" : "VEC") : "—",
                    overlaysReady ? (source.TerrainImageVisible ? "SATELLITE" : "VECTOR (OFF)") : "",
                    overlaysReady ? 1f : 0f, overlaysReady ? AvState.Ready : AvState.Inert);

                chips[0].Set(available ? "LAYERS " + AvNum.Fixed(visibleLayers, 0) + "/" + AvNum.Fixed(ShownLayerTarget(), 0) : "LAYERS —",
                    !available ? AvState.Inert : visibleLayers == ShownLayerTarget() ? AvState.Ready : AvState.Info);
                chips[1].Set(available ? "TOOLTIP " + TooltipName() : "TOOLTIP —",
                    !available ? AvState.Inert : tooltipOn ? AvState.Info : AvState.Inert);
                chips[2].Set(available ? SizeLabel() : "SIZE —", available ? AvState.Info : AvState.Inert);

                detailSummary.Set(!available ? "Map options are not available yet." :
                    options.tooltipType == MapOptions.TooltipType.None
                        ? "Hover tooltips are hidden. Select a mode above to inspect map units."
                        : TooltipName() + " is selected. Hover a map unit to inspect it.");

                float scale = available && IsUsableSize(options.iconSize)
                    ? Mathf.Clamp(options.iconSize, .1f, 2f) : 1f;
                foreach (SymbolTile tile in previewTiles) tile.SetState(available, scale);
                previewCaption.Set(available
                    ? SizeLabel() + " • Frame shape marks the side; jets keep the game's own silhouette."
                    : "Symbol size unavailable.");
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
                !float.IsNaN(value) && !float.IsInfinity(value);

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
                ? "MAP CONTROLS UNAVAILABLE — WAITING FOR MAP"
                : selectedPage == 0
                    ? AvNum.Fixed(visibleLayers, 0) + "/" + AvNum.Fixed(ShownLayerTarget(), 0) +
                      " LAYERS SHOWN • EACH CELL SWITCHES ONE LAYER"
                    : "LIT CELL = SELECTED • TOOLTIP AND SYMBOL SIZE APPLY TO THE MAP";
        }
    }
}
