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
            private const int OverlayLayerCount = 3;

            private static readonly string[] LayerNames =
                { "OBJECTIVES", "TARGET DETAILS", "JAMMING", "GRID LABELS", "PILOTS", "AIRBASES" };
            private static readonly string[] LayerNotes =
            {
                "Mission objective markers", "Target information markers", "Jamming indicators",
                "Map grid coordinates", "Dismounted pilot icons", "Airbase icons",
            };
            private static readonly string[] OverlayNames =
                { "CONTROL FIELD", "FRONT LINE", "THREAT HEAT" };
            private static readonly string[] OverlayNotes =
            {
                "Faction sector control tint", "Front line trace over the control field",
                "Hostile sensor heat over tracked emitters",
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
            private static readonly string[] ReadoutKeys =
                { "SECTOR CELL", "CONTROL DATA", "THREAT TRACKS" };
            private static readonly string[] LegendLabels =
                { "FRIENDLY GROUND", "HOSTILE GROUND", "CONTESTED", "FRONT LINE", "RADAR HEAT", "OPTICAL / IR" };

            private readonly MapOptions options;
            private readonly MfdGlyph[] previewSymbols = new MfdGlyph[3];
            private readonly TMP_Text[] readoutValues = new TMP_Text[ReadoutKeys.Length];

            private ComMapOverlay overlay;
            private ThreatMapOverlay threats;
            private MfdPagingGrid layers, overlays, hover, sizes;
            private AvButton presetAll, presetNone, presetDefaults;
            private TMP_Text overlayNote, detailSummary, previewCaption;
            private RectTransform[] pages;
            private int selectedPage;
            private int visibleLayers;

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map) { this.options = options; }

            protected override int TabCount => 2;

            private bool Available => options != null && SceneSingleton<DynamicMap>.i != null;
            private bool OverlaysReady => Available && Overlay != null && Overlay.LayersAvailable;

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
                ConfigureTabs(new[] { "LAYERS", "READABILITY" }, SelectPage);
                pages = new[] { CreatePage("Layers"), CreatePage("Readability") };
                BuildLayers(pages[0]);
                BuildReadability(pages[1]);
                SelectPage(0);
            }

            // ------------------------------------------------------------- layers

            private void BuildLayers(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float gap = AvTokens.Gap;

                // Fixed reading rhythm; compact bays scroll the same controls.
                const float cell = 56f;
                const float readout = 112f;

                float y = Heading(page, 0f, width, "MAP LAYERS", NativeLayerCount + " GAME LAYERS");
                layers = new MfdPagingGrid(page, y, width, 2, 3, pager: false, rowHeight: cell);
                y -= 3f * cell + AvTokens.Space2;

                y = Heading(page, y, width, "BOSCALI OVERLAYS", "THEATER CONTROL");
                overlays = new MfdPagingGrid(page, y, width, 2, 2, pager: false, rowHeight: cell);
                y -= 2f * cell + AvTokens.Space2;

                overlayNote = Hint(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 26f), "");
                y -= 30f;

                float buttonWidth = (width - AvTokens.Space3 - gap * 2f) / 3f;
                presetAll = AvStyled.Button(page, new Rect(AvTokens.Space3, y, buttonWidth, AvTokens.RowHeight),
                    "ALL ON", "btn", () => SetAllLayers(true))
                    .WithTooltip("Show every game layer and every Boscali overlay.");
                presetNone = AvStyled.Button(page, new Rect(AvTokens.Space3 + buttonWidth + gap, y, buttonWidth, AvTokens.RowHeight),
                    "HIDE ALL", "btn", () => SetAllLayers(false))
                    .WithTooltip("Hide every map layer. Panels and symbols stay as they are.");
                presetDefaults = AvStyled.Button(page,
                    new Rect(AvTokens.Space3 + (buttonWidth + gap) * 2f, y, buttonWidth, AvTokens.RowHeight),
                    "DEFAULTS", "btn", ApplyDefaults)
                    .WithTooltip("Restore the game's defaults: every layer on, unit tooltips and full-size symbols.");
                y -= AvTokens.RowHeight + AvTokens.Space1;

                AvKit.TacticalCard(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, readout),
                    AvTheme.RailInfo);
                AvStyled.Label(page, new Rect(AvTokens.Space5, y - 10f, width - AvTokens.Space5 * 2f, 14f),
                    "OVERLAY READOUT", "section-title");
                // The rows spread over the card's whole height: the pitch takes the slack
                // between them instead of leaving it stacked under the last row.
                float pitch = (readout - 54f) / (ReadoutKeys.Length - 1);
                for (int i = 0; i < ReadoutKeys.Length; i++)
                {
                    float rowY = y - 30f - i * pitch;
                    AvStyled.Label(page, new Rect(AvTokens.Space5, rowY, 150f, 16f), ReadoutKeys[i], "kv-key");
                    readoutValues[i] = AvStyled.Label(page,
                        new Rect(AvTokens.Space5 + 150f, rowY, width - AvTokens.Space5 * 2f - 150f, 16f),
                        "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
                    if (i > 0 && pitch >= 20f)
                        AvKit.Rule(page, new Rect(AvTokens.Space5, rowY + (pitch - 16f) * 0.5f,
                                                  width - AvTokens.Space5 * 2f, 1f),
                                   AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));
                }
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
                if (index == 0) return Overlay.ControlFieldVisible;
                if (index == 1) return Overlay.FrontLineVisible;
                return Threats != null && Threats.Visible;
            }

            private void SetOverlayLayer(int index, bool visible)
            {
                if (index == 0) Overlay.SetControlFieldVisible(visible);
                else if (index == 1) Overlay.SetFrontLineVisible(visible);
                else Threats?.SetVisible(visible);
            }

            /// <summary>Whether one overlay has the map and the state it needs to draw at all.</summary>
            private bool OverlayReady(int index)
            {
                if (index < 2) return Overlay != null && Overlay.LayersAvailable;
                return Threats != null && Threats.Available;
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

            private void BuildReadability(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                const float cell = 48f;
                const float legendPitch = 28f;
                const float preview = 120f;

                float y = Heading(page, 0f, width, "HOVER TOOLTIP", "CHOOSE ONE");
                hover = new MfdPagingGrid(page, y, width, 2, 2, pager: false, rowHeight: cell, exclusive: true);
                y -= 2f * cell + AvTokens.Space2;

                detailSummary = Hint(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 26f), "");
                y -= 30f;

                y = Heading(page, y, width, "SYMBOL SIZE", "CHOOSE ONE");
                sizes = new MfdPagingGrid(page, y, width, 3, 1, pager: false, rowHeight: cell, exclusive: true);
                y -= cell + AvTokens.Space2;

                y = Heading(page, y, width, "SYMBOL PREVIEW", "ILLUSTRATIVE");
                BuildPreview(page, y, width, preview);
                y -= preview + AvTokens.Space2;

                y = Heading(page, y, width, "MAP LEGEND", "BOSCALI OVERLAYS");
                BuildLegend(page, y, width, legendPitch);
            }

            private void BuildPreview(RectTransform page, float y, float width, float height)
            {
                AvKit.TacticalCard(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, height),
                    AvTheme.RailInfo);

                string[] symbols = { "AIR", "GND", "SHP" };
                string[] labels = { "AIRCRAFT", "GROUND", "SHIP" };
                float column = (width - AvTokens.Space3) / 3f;
                float glyphSize = Mathf.Clamp(height * 0.34f, 26f, 52f);
                float centreY = y - height * 0.40f;
                for (int i = 0; i < symbols.Length; i++)
                {
                    float x = AvTokens.Space3 + column * i;
                    var go = new GameObject("PreviewSymbol", typeof(RectTransform), typeof(MfdGlyph));
                    go.transform.SetParent(page, false);
                    previewSymbols[i] = go.GetComponent<MfdGlyph>();
                    RectTransform symbol = previewSymbols[i].rectTransform;
                    // Centre pivot so the size choice scales the glyph about its own middle.
                    symbol.pivot = new Vector2(.5f, .5f);
                    AvKit.Place(symbol, new Rect(x + column / 2f, centreY, glyphSize, glyphSize));
                    previewSymbols[i].raycastTarget = false;
                    previewSymbols[i].Set(symbols[i]);
                    AvStyled.Label(page,
                        new Rect(x, centreY - glyphSize * 0.5f - 18f, column, 14f), labels[i],
                        "section-title-note", align: TextAlignmentOptions.Center);
                }

                previewCaption = Hint(page,
                    new Rect(AvTokens.Space5, y - height + 26f, width - AvTokens.Space5 * 2f, 26f), "");
            }

            private static void BuildLegend(RectTransform page, float y, float width, float pitch)
            {
                Color32 friendly = TacticalSectorGrid.FriendlyTint;
                Color32 hostile = TacticalSectorGrid.HostileTint;
                Color32 contestedLeft = new Color32(friendly.r, friendly.g, friendly.b, 150);
                Color32 contestedRight = new Color32(hostile.r, hostile.g, hostile.b, 150);
                Color32 front = FrontlineGraphic.Ink;

                float half = (width - AvTokens.Space3) / 2f;
                float blockHeight = Mathf.Clamp(pitch - 14f, 8f, 20f);
                float blockWidth = Mathf.Max(5f, blockHeight * 0.75f);
                for (int i = 0; i < LegendLabels.Length; i++)
                {
                    float x = AvTokens.Space3 + i % 2 * half;
                    float rowY = y - i / 2 * pitch;
                    Color32 left = i == 1 ? hostile : i == 2 ? contestedLeft : i == 3 ? front
                        : i == 4 ? (Color32)AvTheme.RailDanger : i == 5 ? (Color32)AvTheme.RailCaution : friendly;
                    Color32 right = i == 2 ? contestedRight : left;

                    AvKit.Rule(page, new Rect(x, rowY - 6f, blockWidth, blockHeight), left);
                    AvKit.Rule(page, new Rect(x + blockWidth, rowY - 6f, blockWidth, blockHeight), right);
                    AvStyled.Label(page,
                        new Rect(x + blockWidth * 2f + 8f, rowY - 6f - (blockHeight - 16f) * 0.5f,
                                 half - blockWidth * 2f - 14f, 16f),
                        LegendLabels[i], "kv-key");
                }
            }

            // ------------------------------------------------------------- refresh

            private void SelectPage(int selected)
            {
                selectedPage = selected;
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                SetSelectedTab(selected);
                RequestRefresh();
            }

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
                    details: i => !available ? "Map is not available yet."
                        : LayerNotes[i] + (NativeLayer(i) ? ". Click to hide." : ". Click to show."),
                    subs: i => available ? LayerNotes[i] : null);
                overlays.SetData(OverlayLayerCount, i => OverlayNames[i], i => OverlayReady(i) && OverlayLayer(i),
                    i => { if (ToggleReady() && OverlayReady(i)) SetOverlayLayer(i, !OverlayLayer(i)); RequestRefresh(); },
                    i => OverlayReady(i),
                    details: i => !OverlayReady(i) ? OverlayUnavailable(i)
                        : OverlayNotes[i] + (OverlayLayer(i) ? ". Click to hide." : ". Click to show.") +
                          OverlayDetail(i, threatSource));

                bool tooltipOn = available && options.tooltipType != MapOptions.TooltipType.None;
                hover.SetData(HoverNames.Length, i => HoverNames[i],
                    i => available && options.tooltipType == HoverModes[i],
                    i => { if (ToggleReady()) options.SetToolTipType((int)HoverModes[i]); RequestRefresh(); },
                    i => available,
                    details: i => available ? "Hover a map unit: " + HoverNotes[i].ToLowerInvariant() + "." :
                        "Map options are not available yet.",
                    subs: i => available ? HoverNotes[i] : null);
                sizes.SetData(SizeNames.Length, i => SizeNames[i] + " " + (60 + 20 * i) + "%",
                    i => available && Mathf.Approximately(options.iconSize, .6f + .2f * i),
                    i => { if (ToggleReady()) options.SetIconSize(i); RequestRefresh(); }, i => available,
                    details: i => available ? "Draw map symbols at " + (60 + 20 * i) + " percent." :
                        "Map options are not available yet.",
                    subs: i => available ? "OF STANDARD SIZE" : null);

                bool anyLayer = available && visibleLayers > 0;
                bool everyLayer = available && visibleLayers == ShownLayerTarget();
                presetAll.SetEnabled(available && !everyLayer);
                presetNone.SetEnabled(anyLayer);
                presetAll.WithTooltip(!available ? "Map is not available yet." : everyLayer
                    ? "Every layer is already shown." : "Show every game layer and every Boscali overlay.");
                presetNone.WithTooltip(!available ? "Map is not available yet." : !anyLayer
                    ? "Every layer is already hidden." : "Hide every map layer. Panels and symbols stay as they are.");
                presetDefaults.WithTooltip(available
                    ? "Restore the game's defaults: every layer on, unit tooltips and full-size symbols."
                    : "Map is not available yet.");

                overlayNote.text = overlaysReady
                    ? (source.HasControlData
                        ? "Sector control follows real ground presence; the front is its zero contour."
                        : "No faction ground data on this map yet, so the control field is empty.")
                    : "Command's overlay waits for the map and a faction headquarters.";
                SetReadout(0, overlaysReady ? Mathf.RoundToInt(source.CellMetres) + " m" : "—", overlaysReady);
                SetReadout(1, overlaysReady ? (source.HasControlData ? "LIVE" : "NO DATA") : "—", overlaysReady,
                    source.HasControlData ? null : AvTheme.Warning);
                SetReadout(2, !threatsReady ? "—" : threatSource.TrackedEmitters > 0
                    ? threatSource.TrackedEmitters + " EMITTERS · " +
                      Mathf.RoundToInt(threatSource.WidestEnvelopeKm) + " km"
                    : "NONE",
                    threatsReady);

                Shell.DataBar.State.text = available ? "MAP DISPLAY" : "WAITING FOR MAP";
                Shell.DataBar.SetChip(0, available ? "LAYERS " + visibleLayers + "/" + ShownLayerTarget() : "LAYERS —",
                    !available ? "inert" : visibleLayers == ShownLayerTarget() ? "live" : "info");
                Shell.DataBar.SetChip(1, available ? "TOOLTIP " + TooltipName() : "TOOLTIP —",
                    !available ? "inert" : tooltipOn ? "info" : "inert");
                Shell.DataBar.SetChip(2, available ? SizeLabel() : "SIZE —", available ? "info" : "inert");

                detailSummary.text = !available ? "Map options are not available yet." :
                    options.tooltipType == MapOptions.TooltipType.None
                        ? "Hover tooltips are hidden. Select a mode above to inspect map units."
                        : TooltipName() + " is selected. Hover a map unit to inspect it.";

                float scale = available && IsUsableSize(options.iconSize)
                    ? Mathf.Clamp(options.iconSize, .1f, 2f) : 1f;
                foreach (MfdGlyph symbol in previewSymbols)
                {
                    symbol.enabled = available;
                    symbol.rectTransform.localScale = Vector3.one * scale;
                }
                previewCaption.text = available
                    ? SizeLabel() + " • Actual icons vary by unit; preview is illustrative."
                    : "Symbol size unavailable.";
            }

            private void SetReadout(int index, string text, bool live, Color? color = null)
            {
                readoutValues[index].text = text;
                readoutValues[index].color = color ?? (live ? AvTheme.TextPrimary : AvTheme.Disabled);
            }

            /// <summary>The whole set is only as large as the overlays that can actually draw.</summary>
            private int ShownLayerTarget()
            {
                int total = NativeLayerCount;
                for (int i = 0; i < OverlayLayerCount; i++)
                    if (OverlayReady(i)) total++;
                return total;
            }

            /// <summary>The overlay's live state, on the row that already names the layer.</summary>
            private static string OverlayDetail(int index, ThreatMapOverlay threats)
            {
                if (index < 2 || threats == null) return string.Empty;
                return threats.TrackedEmitters > 0
                    ? " " + threats.TrackedEmitters + " tracked, widest reach " +
                      Mathf.RoundToInt(threats.WidestEnvelopeKm) + " km."
                    : " Nothing tracked.";
            }

            private string OverlayUnavailable(int index) => index < 2
                ? "Command's theater overlay is not running on this map."
                : "Threat heat waits for the map, a faction and your own aircraft.";

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
                    ? visibleLayers + "/" + ShownLayerTarget() +
                      " LAYERS SHOWN • EACH CELL SWITCHES ONE LAYER"
                    : "LIT CELL = SELECTED • TOOLTIP AND SYMBOL SIZE APPLY TO THE MAP";
        }
    }
}
