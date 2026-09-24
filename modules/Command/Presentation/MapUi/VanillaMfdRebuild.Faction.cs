using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // The faction, target and mission presenters use the same host and paging primitive
        // below; keeping the controller-specific code here makes a future game API change a
        // local adapter edit instead of another prefab traversal.

        // ----------------------------------------------------------- faction panels

        /// <summary>
        /// The two faction controllers share a single data adapter. The selected source
        /// tells us whose HQ to read; this avoids treating the mutable bezel
        /// short-name as a gameplay identifier and deliberately does not call the stock
        /// airbase switch (which currently switches to players internally).
        /// </summary>
        private sealed class FactionPresenter : Presenter
        {
            private enum LedgerMode { Reserves, Losses, Value, Manpower }
            private enum InfoMode { Airbases, Players }

            private readonly InfoPanel_Faction source;
            private readonly InfoPanel_Faction otherSource;
            private AvButton[] factionTabs;
            private int selectedFaction;
            private readonly List<UnitDefinition> definitions = new List<UnitDefinition>();
            private readonly List<string> infoRows = new List<string>();
            private readonly List<string> infoSubs = new List<string>();
            private readonly List<Sprite> infoIcons = new List<Sprite>();

            private RectTransform[] pages;
            private RectTransform unavailableCard;
            private MfdPagingGrid definitionGrid;
            private MfdPagingGrid infoGrid;
            private AvButton[] definitionTabs;
            private AvButton[] ledgerTabs;
            private AvButton[] infoTabs;
            private TMP_Text factionName;
            private TMP_Text factionSubtitle;
            private Image factionFlag;
            private Image factionLogo;
            private TMP_Text[] forceTotals;
            private AvStyled.Metric[] ledgerMetrics;
            private readonly float[] chartPrimary = new float[4];
            private readonly float[] chartSecondary = new float[4];
            private readonly UnityEngine.UI.Image[] forceBars = new UnityEngine.UI.Image[4];
            private MfdLedgerChart ledgerChart;
            private float forceBarWidth;
            private int definitionGroup;
            private bool definitionsLoaded;
            private LedgerMode ledgerMode;
            private InfoMode infoMode;
            private int selectedPage;
            private int resourceSeries;
            private int renderedResourceSeries = -1;
            private int renderedResourceCount = -1;
            private float renderedResourceTime = float.NaN;
            private static readonly string[] ResourceLabels = { "FUNDS", "WARHEADS", "MANPOWER", "MORALE" };
            private static readonly string[] ResourceGlyphs = { "funds", "missile", "person", "gauge" };
            private static readonly string[] PageLabels = { "ECONOMY", "FORCES", "LEDGER", "POLITICS" };
            private FactionHQ observedHq;
            private MfdResourceHistory resourceHistory;
            private AvStyled.Metric[] resourceMetrics;
            private Image moraleRail;
            private AvButton[] resourceTabs;
            private ResourceHistoryChart resourceChart;
            private TMP_Text economyHeadline;
            private TMP_Text economyEffect;
            private TMP_Text economyContract;
            private TMP_Text mandateHeadline;
            private TMP_Text mandateEffect;
            private Image mandateFlag;
            private Image mandateRail;
            private TMP_Text politicalEvent;
            private TMP_Text politicalEffect;
            private TMP_Text politicalBrief;
            private TMP_Text politicalMission;
            private TMP_Text politicalMissionDetail;

            public FactionPresenter(MFDScreen screen, InfoPanel_Faction source,
                                    InfoPanel_Faction otherSource, VanillaMfdPanelId id)
                : base(screen, id)
            {
                this.source = source;
                this.otherSource = otherSource;
            }

            protected override int TabCount => 4;
            protected override bool PageHasTitle => false;
            protected override float PageTopInset => 42f;

            protected override void BuildContent()
            {
                ConfigureTabs(PageLabels, SelectPage);
                float factionWidth = (Shell.Body.width - 2f * AvTokens.Space3 - AvTokens.Gap) * 0.5f;
                factionTabs = new[]
                {
                    PanelButton(Shell.Content, new Rect(Shell.Body.x + AvTokens.Space3,
                        Shell.Body.y, factionWidth, 34f), "OWN FACTION", "tab",
                        () => SelectFaction(0), AvButtonStyle.Tab),
                    PanelButton(Shell.Content, new Rect(Shell.Body.x + AvTokens.Space3 +
                        factionWidth + AvTokens.Gap, Shell.Body.y, factionWidth, 34f),
                        "OPPOSITION", "tab", () => SelectFaction(1), AvButtonStyle.Tab),
                };
                factionTabs[0].SetLatched(true);
                pages = new[]
                {
                    CreatePage("Economy"),
                    CreatePage("Forces"),
                    CreatePage("Ledger"),
                    CreatePage("Politics"),
                };

                BuildResourcesPage(pages[0]);
                BuildForcesPage(pages[1]);
                BuildLedgerPage(pages[2]);
                BuildStatusPage(pages[3]);
                var unavailable = new GameObject("FactionUnavailable", typeof(RectTransform));
                unavailableCard = unavailable.GetComponent<RectTransform>();
                unavailableCard.SetParent(Shell.Content, false);
                AvKit.Place(unavailableCard, new Rect(Shell.Body.x + AvTokens.Space3,
                    Shell.Body.y - PageTopInset - AvTokens.Space3,
                    Shell.Body.width - 2f * AvTokens.Space3, 104f));
                AvKit.TacticalCard(unavailableCard,
                    new Rect(0f, 0f, Shell.Body.width - 2f * AvTokens.Space3, 104f), AvTheme.Warning);
                AvStyled.Label(unavailableCard, new Rect(14f, -14f, Shell.Body.width - 52f, 22f),
                    "FACTION HQ UNAVAILABLE", "row-main");
                AvStyled.Label(unavailableCard, new Rect(14f, -44f, Shell.Body.width - 52f, 40f),
                    "Waiting for the selected faction and its live mission data.", "row-sub");
                unavailableCard.gameObject.SetActive(false);
                // The state line carries the faction's full name; it shrinks before it cuts.
                Shell.DataBar.State.enableAutoSizing = true;
                Shell.DataBar.State.fontSizeMin = AvTokens.FontMicro;
                SelectDefinitions(0);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                factionTabs[0].SetText(FactionLabel(source, "OWN FACTION"));
                factionTabs[1].SetText(FactionLabel(otherSource, "OPPOSITION"));
                factionTabs[0].SetLatched(selectedFaction == 0);
                factionTabs[1].SetLatched(selectedFaction == 1);
                InfoPanel_Faction selected = selectedFaction == 0 ? source : otherSource;
                FactionHQ hq = selected == null ? null : selected.factionHQ;
                if (hq != observedHq)
                {
                    observedHq = hq;
                    resourceHistory = FactionResourceHistoryStore.For(hq);
                    renderedResourceSeries = -1;
                    renderedResourceCount = -1;
                    renderedResourceTime = float.NaN;
                    definitionGrid.ResetPage();
                    infoGrid.ResetPage();
                }
                if (hq == null)
                {
                    foreach (RectTransform page in pages) page.gameObject.SetActive(false);
                    unavailableCard.gameObject.SetActive(true);
                    Shell.DataBar.State.text = "FACTION / " + PageLabels[selectedPage] + " / NO HQ";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }
                unavailableCard.gameObject.SetActive(false);
                pages[selectedPage].gameObject.SetActive(true);

                string name = hq.faction == null ? "FACTION" : hq.faction.factionName;
                Shell.DataBar.State.text = (name ?? "FACTION").ToUpperInvariant() +
                    "  /  " + PageLabels[selectedPage];
                Shell.DataBar.SetChip(0, "SCORE " + hq.factionScore.ToString("0.0"), true);
                Shell.DataBar.SetChip(1, UnitConverter.ValueReading(hq.factionFunds), true);
                Shell.DataBar.SetChip(2, "WHD " + hq.GetWarheadStockpile(), true);

                factionName.text = name ?? "FACTION";
                string extended = hq.faction == null ? null : hq.faction.factionExtendedName;
                if (factionSubtitle != null)
                {
                    factionSubtitle.text = string.IsNullOrEmpty(extended)
                        ? "LIVE THEATER ORDER OF BATTLE" : extended.ToUpperInvariant();
                }
                Sprite logo = hq.faction == null ? null : hq.faction.factionColorLogo;
                factionLogo.sprite = logo;
                factionLogo.enabled = logo != null;
                // The game's own faction art is the identification a player actually
                // recognises; the roundel above is the fallback for a mission whose faction
                // carries no header sprite.
                Sprite flag = hq.faction == null ? null : hq.faction.factionHeaderSprite;
                if (factionFlag != null)
                {
                    factionFlag.sprite = flag;
                    factionFlag.enabled = flag != null;
                }

                RefreshResources(hq);
                if (selectedPage == 1)
                {
                    SetForceTotals(hq.missionStatsTracker);
                    RefreshDefinitionGrid(hq);
                }
                else if (selectedPage == 2) RefreshLedger(hq);
                else if (selectedPage == 3)
                {
                    RefreshPolitics(hq);
                    RefreshInfo(hq);
                }
                UpdateButtonRows();
            }

            protected override string AmbientStatus()
            {
                if (observedHq == null) return "WAITING FOR FACTION HQ";
                switch (selectedPage)
                {
                    case 0: return "ECONOMY • MORALE SCALES NEW CONTRACT OFFERS";
                    case 1: return "FORCES • CURRENT / LOST UNITS BY CLASS";
                    case 2: return "LEDGER • " + ledgerMode.ToString().ToUpperInvariant() + " BY ASSET CLASS";
                    default: return "POLITICS • MISSION AND EVENT EFFECTS";
                }
            }

            private void BuildResourcesPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float body = PageHeight;
                const float cardHeight = 80f;
                float usable = width - 2f * AvTokens.Space3 - AvTokens.Gap;
                float wide = usable * 0.59f;
                float narrow = usable - wide;

                float y = Heading(page, -AvTokens.Space1, width, "RESOURCE TELEMETRY", "LIVE STOCKPILES");
                SectionGlyph(page, "funds", AvTheme.RailInfo);
                string[] labels = ResourceLabels;
                string[] units = { "", "WHD", "PAX", "/ 100" };
                resourceMetrics = new AvStyled.Metric[4];
                for (int i = 0; i < 4; i++)
                {
                    bool firstRow = i < 2;
                    float leftWidth = firstRow ? wide : narrow;
                    Rect area = new Rect(AvTokens.Space3 + (i % 2 == 0 ? 0f : leftWidth + AvTokens.Gap),
                        y - i / 2 * (cardHeight + AvTokens.Gap), i % 2 == 0 ? leftWidth : usable - leftWidth,
                        cardHeight);
                    AvKit.Panel(page, area, AvTheme.SurfaceInert);
                    AvKit.Rule(page, new Rect(area.x, area.y, area.width, 1f), AvTheme.Hairline);
                    Image rail = AvKit.Rule(page, new Rect(area.x, area.y, 3f, cardHeight),
                        i == 3 ? AvTheme.RailReady : AvTheme.RailInfo);
                    if (i == 3) moraleRail = rail;
                    resourceMetrics[i] = AvStyled.MetricCell(page, area, labels[i], units[i]);
                    CardGlyph(page, area, ResourceGlyphs[i]);
                    // A stock figure shrinks to the micro floor before it is ever cut, so a
                    // large balance or a morale readout never prints as an ellipsis.
                    resourceMetrics[i].Value.enableAutoSizing = true;
                    resourceMetrics[i].Value.fontSizeMin = AvTokens.FontMicro;
                    resourceMetrics[i].Value.fontSizeMax = 22f;
                    resourceMetrics[i].Caption.enableAutoSizing = true;
                    resourceMetrics[i].Caption.fontSizeMin = AvTokens.FontMicro;
                    resourceMetrics[i].Caption.fontSizeMax = 11f;
                    // The unit stops short of the card's corner glyph instead of being
                    // printed under it, which is what hid the morale readout.
                    resourceMetrics[i].Unit.rectTransform.sizeDelta = new Vector2(
                        area.width - 42f, resourceMetrics[i].Unit.rectTransform.sizeDelta.y);
                    resourceMetrics[i].Unit.characterSpacing = 0f;
                    resourceMetrics[i].Unit.overflowMode = TextOverflowModes.Overflow;
                    resourceMetrics[i].Unit.enableWordWrapping = false;
                    resourceMetrics[i].Caption.characterSpacing = 0f;
                    // Only morale has a meaningful maximum. Other resources are absolute stocks.
                    if (i != 3) resourceMetrics[i].Fill.enabled = false;
                }
                y -= 2f * cardHeight + AvTokens.Gap + AvTokens.Space3;

                float economyY = y;
                Rect economyCard = new Rect(AvTokens.Space3, economyY,
                    width - 2f * AvTokens.Space3, 112f);
                AvKit.Panel(page, economyCard, AvTheme.SurfaceInert);
                AvKit.Outline(page, economyCard, AvTheme.Hairline.WithAlpha(0.7f));
                AvKit.Rule(page, new Rect(economyCard.x, economyY, 60f, 2f), AvTheme.RailInfo);
                CardCorners(page, economyCard, AvTheme.RailInfo);
                BriefLabel(page, new Rect(economyCard.x + 12f, economyY - 8f, economyCard.width - 24f, 16f),
                    "WAR ECONOMY / LIVE DIRECTIVE", "metric-key", 11f);
                economyHeadline = BriefLabel(page,
                    new Rect(economyCard.x + 12f, economyY - 27f, economyCard.width - 24f, 25f),
                    "AWAITING DISPATCH", "metric-value", 18f);
                economyHeadline.enableAutoSizing = true;
                economyHeadline.fontSizeMin = AvTokens.FontBody;
                economyEffect = BriefLabel(page,
                    new Rect(economyCard.x + 12f, economyY - 57f, economyCard.width - 24f, 23f),
                    "SUPPORT COST / BASELINE", "metric-cap", 12f);
                AvKit.Rule(page, new Rect(economyCard.x + 12f, economyY - 82f, economyCard.width - 24f, 1f),
                    AvTheme.Hairline.WithAlpha(0.5f));
                economyContract = BriefLabel(page,
                    new Rect(economyCard.x + 12f, economyY - 88f, economyCard.width - 24f, 18f),
                    "NO CONTRACT PAYOUT REPORTED", "metric-cap", 11f);
                y -= economyCard.height + AvTokens.Space3;
                y = Heading(page, y, width, "RESOURCE HISTORY", "LOCAL OBSERVATIONS");
                resourceTabs = CreateButtonRow(page, y, labels, selected =>
                {
                    resourceSeries = selected;
                    RequestRefresh();
                });
                for (int i = 0; i < resourceTabs.Length; i++)
                {
                    resourceTabs[i].WithTooltip(
                        "Plot " + labels[i].ToLowerInvariant() + " over the observed window.");
                }
                y -= AvTokens.RowHeight + AvTokens.Space2;
                resourceChart = new ResourceHistoryChart(page, y, width, body + y - AvTokens.Space1);
            }

            private void RefreshResources(FactionHQ hq)
            {
                float manpower = FactionResourceHistoryStore.Manpower(hq);
                float morale = FactionResourceHistoryStore.Morale(hq);
                float funds = hq.factionFunds;
                int warheads = hq.GetWarheadStockpile();
                resourceMetrics[0].Set(UnitConverter.ValueReading(funds), funds < 0f ? "NEGATIVE BALANCE" : "AVAILABLE FUNDS", 0f, AvTheme.Accent);
                resourceMetrics[0].Value.color = funds < 0f ? AvTheme.Warning : AvTheme.TextPrimary;
                resourceMetrics[1].Set(warheads.ToString(), "STOCKPILE", 0f, AvTheme.Accent);
                resourceMetrics[2].Set(MfdResourceHistory.Finite(manpower) ? manpower.ToString("0") : "—",
                    "IN ACTIVE ASSETS", 0f, AvTheme.Accent);
                resourceMetrics[3].Set(MfdResourceHistory.Finite(morale) ? morale.ToString("0.#") : "—",
                    MfdResourceHistory.Finite(morale) ?
                        "NEW CONTRACTS " + FactionMoraleState.ContractMultiplier(morale).ToString("0.00") + "x" :
                    "HOST DATA UNAVAILABLE",
                    MfdResourceHistory.Finite(morale) ? morale / 100f : 0f,
                    !MfdResourceHistory.Finite(morale) ? AvTheme.Disabled :
                        morale < 50f ? AvTheme.Warning : AvTheme.Accent);
                Color moraleTone = !MfdResourceHistory.Finite(morale) ? AvTheme.Disabled :
                    morale < 50f ? AvTheme.Warning : AvTheme.Accent;
                resourceMetrics[3].Value.color = moraleTone;
                if (moraleRail != null) moraleRail.color = moraleTone;
                ModServices.TryGet(out IActiveEventsView events);
                ActiveEventView current = events?.Current;
                bool applies = current != null && events.AffectsFaction(hq.faction?.factionName);
                string price = current == null ? "NO EFFECT" :
                    events.PriceSummaryForFaction(hq.faction?.factionName);
                economyHeadline.text = current == null ? "SUPPLY LINES HOLDING" :
                    current.Title.ToUpperInvariant();
                economyEffect.text = current == null ? "SUPPORT COST / BASELINE 1.00x" :
                    !current.TargetResolved ? "TARGET LOST / EVENT EFFECT CANCELLED" :
                    applies ? (IsLocalFaction(hq) ? "LOCAL EVENT / " : "BASE EVENT / ") +
                        price + " • " + EventClock(current) :
                    "OTHER SIDE / NO EVENT PRICE CHANGE HERE";
                economyEffect.color = !applies || price == "NO EFFECT" ? AvTheme.TextPrimary :
                    price.StartsWith("+") ? AvTheme.Warning : AvTheme.Accent;
                SecondaryObjectiveView contract = FeaturedContract(hq);
                economyContract.text = contract == null
                    ? IsLocalFaction(hq) ? ContractsDisabled() ? "FIELD CONTRACTS DISABLED BY HOST" :
                        "NO OPEN CONTRACT PAYOUT • CHECK MIS" : "CONTRACT PAYOUTS / LOCAL FACTION ONLY"
                    : "FIELD CONTRACT / " + MfdSecondaryObjectives.PayoutLabel(contract);
                if (resourceHistory == null) resourceHistory = FactionResourceHistoryStore.For(hq);
                int latestCount = resourceHistory != null ? resourceHistory.Count : 0;
                float latest = latestCount > 0 ? resourceHistory.Time(latestCount - 1) : float.NaN;
                bool chartChanged = latestCount != renderedResourceCount ||
                                    (latestCount > 0 && latest != renderedResourceTime) ||
                                    renderedResourceSeries != resourceSeries;
                if (resourceHistory != null && chartChanged)
                {
                    resourceChart.Set(resourceHistory, resourceSeries, ResourceLabels[resourceSeries], FormatResource);
                    renderedResourceTime = latest;
                    renderedResourceCount = latestCount;
                    renderedResourceSeries = resourceSeries;
                }
                SetRow(resourceTabs, resourceSeries);
            }

            private string FormatResource(float value) => resourceSeries == 0
                ? UnitConverter.ValueReading(value) : value.ToString("0.#");

            /// <summary>
            /// The observed resource window: quarter gridlines, the window's own minimum
            /// and maximum at the axis, and an honest empty state until two samples can
            /// draw a line. It pools one segment per history slot and only moves them; the
            /// observed window is padded so a flat series sits inside the plot instead of
            /// hugging its top edge.
            /// </summary>
            private sealed class ResourceHistoryChart
            {
                private const float AxisWidth = 68f;
                private readonly Image[] segments = new Image[MfdResourceHistory.Capacity - 1];
                private readonly TMP_Text summary, upper, lower, window, empty, current;
                private readonly Image marker, reticleH, reticleV, zero;
                private readonly float left, top, width, height;

                public ResourceHistoryChart(RectTransform parent, float y, float panelWidth,
                                            float availableHeight)
                {
                    AvKit.Rule(parent, new Rect(AvTokens.Space3, y - 3f, 2f, 12f),
                        AvTheme.Accent);
                    summary = AvStyled.Label(parent,
                        new Rect(AvTokens.Space3 + 8f, y,
                            panelWidth - 2f * AvTokens.Space3 - 8f,
                            18f), "", "row-main");
                    // The series name and its change are data: the line shrinks to the
                    // micro floor and then overflows rather than being clipped.
                    summary.enableWordWrapping = false;
                    summary.overflowMode = TextOverflowModes.Overflow;
                    summary.enableAutoSizing = true;
                    summary.fontSizeMin = AvTokens.FontMicro;
                    summary.fontSizeMax = summary.fontSize;

                    top = y - 28f;
                    height = Mathf.Max(56f, availableHeight - 74f);
                    left = AvTokens.Space3 + AxisWidth;
                    width = panelWidth - left - AvTokens.Space3;

                    AvKit.Panel(parent, new Rect(left, top, width, height), AvTheme.SurfaceRaised);
                    AvKit.Outline(parent, new Rect(left, top, width, height), AvTheme.Hairline);
                    for (int i = 1; i < 4; i++)
                    {
                        AvKit.Rule(parent, new Rect(left, top - height * i / 4f, width, 1f),
                                   AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));
                        AvKit.Rule(parent, new Rect(left + width * i / 4f, top, 1f, height),
                                   AvTheme.Hairline.WithAlpha(0.2f));
                    }
                    for (int i = 0; i <= 8; i++)
                        AvKit.Rule(parent, new Rect(left + width * i / 8f, top - height,
                            1f, i % 2 == 0 ? 6f : 3f), AvTheme.RailInfo.WithAlpha(0.5f));

                    upper = AxisLabel(parent, top, "—");
                    lower = AxisLabel(parent, top - height + 14f, "—");
                    zero = AvKit.Rule(parent, new Rect(left, top, width, 1f), AvTheme.RailInfo);
                    for (int i = 0; i < segments.Length; i++)
                    {
                        segments[i] = AvKit.Rule(parent, new Rect(left, top, 0f, 2f), AvTheme.Accent);
                        segments[i].rectTransform.pivot = new Vector2(0f, .5f);
                    }
                    reticleH = AvKit.Rule(parent, new Rect(left, top, 14f, 1f), AvTheme.RailInfo);
                    reticleV = AvKit.Rule(parent, new Rect(left, top, 1f, 14f), AvTheme.RailInfo);
                    reticleH.rectTransform.pivot = reticleV.rectTransform.pivot =
                        new Vector2(.5f, .5f);
                    marker = AvKit.Rule(parent, new Rect(left, top, 6f, 6f), AvTheme.TextPrimary);
                    marker.rectTransform.pivot = new Vector2(.5f, .5f);
                    current = AvStyled.Label(parent,
                        new Rect(left + width - 76f, top - 17f, 68f, 14f), "", "row-sub",
                        align: TextAlignmentOptions.MidlineRight);
                    current.enableAutoSizing = true;
                    current.fontSizeMin = AvTokens.FontMicro;
                    current.fontSizeMax = current.fontSize;

                    empty = AvStyled.Label(parent,
                        new Rect(left + 10f, top - height * 0.5f - 20f, width - 20f, 40f),
                        "", "row-sub", align: TextAlignmentOptions.Center);
                    empty.enableWordWrapping = true;
                    empty.overflowMode = TextOverflowModes.Truncate;
                    window = AvStyled.Label(parent,
                        new Rect(AvTokens.Space3, top - height - 18f,
                            panelWidth - 2f * AvTokens.Space3, 16f),
                        "", "row-sub");
                }

                private static TMP_Text AxisLabel(RectTransform parent, float y, string text)
                {
                    TMP_Text label = AvStyled.Label(parent,
                        new Rect(AvTokens.Space3, y, AxisWidth - 6f, 16f), text, "kv-value");
                    label.enableAutoSizing = true;
                    label.fontSizeMin = AvTokens.FontMicro;
                    label.fontSizeMax = AvTokens.FontSmall;
                    return label;
                }

                public void Set(MfdResourceHistory history, int series, string name,
                                Func<float, string> format)
                {
                    int count = history == null ? 0 : history.Count;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = 0; i < count; i++)
                    {
                        float value = history.Value(series, i);
                        if (!MfdResourceHistory.Finite(value)) continue;
                        lo = Mathf.Min(lo, value);
                        hi = Mathf.Max(hi, value);
                    }

                    bool lastKnown = count > 0 && MfdResourceHistory.Finite(history.Value(series, count - 1));
                    if (count < 2 || hi < lo || !lastKnown)
                    {
                        for (int i = 0; i < segments.Length; i++) segments[i].enabled = false;
                        marker.enabled = false;
                        reticleH.enabled = reticleV.enabled = false;
                        current.text = "";
                        zero.enabled = false;
                        upper.text = lower.text = "—";
                        empty.gameObject.SetActive(true);
                        empty.text = count == 0
                            ? "NO SAMPLES YET\nA sample is taken every 5 seconds while this page is open."
                            : "NOT ENOUGH SAMPLES\nA line needs at least two 5-second samples.";
                        summary.text = name + "  •  WAITING FOR SAMPLES";
                        window.text = "HISTORY KEEPS THE LAST " +
                            (MfdResourceHistory.Capacity * MfdResourceHistory.Interval).ToString("0") + " s";
                        return;
                    }

                    // The plot shows the observed window, padded, so a flat series sits
                    // inside the frame rather than along its top edge; the axis labels
                    // state exactly what the window is.
                    float span = hi - lo;
                    if (span < Mathf.Max(1f, Mathf.Abs(hi) * 0.02f))
                    {
                        float centre = (hi + lo) * 0.5f;
                        float pad = Mathf.Max(1f, Mathf.Abs(centre) * 0.05f);
                        lo = centre - pad;
                        hi = centre + pad;
                    }
                    else
                    {
                        float pad = Mathf.Max(1f, span * 0.15f);
                        lo -= pad;
                        hi += pad;
                    }
                    span = hi - lo;

                    float duration = history.Time(count - 1) - history.Time(0);
                    if (duration <= 0f) duration = 1f;
                    int drawn = 0;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        bool valid = i + 1 < count &&
                            MfdResourceHistory.Finite(history.Value(series, i)) &&
                            MfdResourceHistory.Finite(history.Value(series, i + 1));
                        segments[i].enabled = valid;
                        if (!valid) continue;
                        SetSegment(segments[i], history, series, i, lo, span, duration);
                        drawn++;
                    }

                    marker.enabled = true;
                    marker.rectTransform.anchoredPosition =
                        Point(history, series, count - 1, lo, span, duration);
                    reticleH.enabled = reticleV.enabled = true;
                    reticleH.rectTransform.anchoredPosition =
                        reticleV.rectTransform.anchoredPosition = marker.rectTransform.anchoredPosition;
                    current.text = format(history.Value(series, count - 1));
                    zero.enabled = lo <= 0f && hi >= 0f;
                    if (zero.enabled)
                        zero.rectTransform.anchoredPosition =
                            new Vector2(left, top - height * (0f - lo) / span);
                    upper.text = format(hi);
                    lower.text = format(lo);
                    // Samples that are all gaps still draw no line, so the plot says so.
                    empty.gameObject.SetActive(drawn == 0);
                    if (drawn == 0)
                        empty.text = "NOT ENOUGH SAMPLES\nA line needs two consecutive 5-second samples.";
                    float change = history.Value(series, count - 1) - history.Value(series, 0);
                    summary.text = name + "  •  CHANGE " + (MfdResourceHistory.Finite(change)
                        ? (change > 0f ? "+" : "") + format(change) : "—");
                    window.text = "LAST " + duration.ToString("0") + "s   →   NOW  |  5s SAMPLES";
                }

                private void SetSegment(Image image, MfdResourceHistory history, int series,
                                        int index, float lo, float span, float duration)
                {
                    Vector2 a = Point(history, series, index, lo, span, duration);
                    Vector2 b = Point(history, series, index + 1, lo, span, duration);
                    RectTransform rect = image.rectTransform;
                    rect.anchoredPosition = a;
                    rect.sizeDelta = new Vector2((b - a).magnitude, 2f);
                    rect.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                }

                private Vector2 Point(MfdResourceHistory history, int series, int index,
                                      float lo, float span, float duration) =>
                    new Vector2(left + width * (history.Time(index) - history.Time(0)) / duration,
                                top - height + height * (history.Value(series, index) - lo) / span);
            }

            /// <summary>
            /// A resource glyph in the card's top-right corner. The metric key names the
            /// figure; the symbol lets a player who is scanning, not reading, tell funds
            /// from manpower at a glance.
            /// </summary>
            private static void CardGlyph(RectTransform page, Rect area, string kind)
            {
                PlaceGlyph(page, new Rect(area.x + area.width - 34f, area.y - 9f, 20f, 20f),
                    kind, AvTheme.Accent);
            }

            private static string FactionLabel(InfoPanel_Faction controller, string fallback)
            {
                if (controller == null) return fallback;
                MFDScreen screen = controller.GetComponent<MFDScreen>();
                if (screen == null || string.IsNullOrWhiteSpace(screen.shortName)) return fallback;
                return screen.shortName.ToUpperInvariant();
            }

            private void SelectFaction(int faction)
            {
                if (faction == selectedFaction || (faction == 1 && otherSource == null)) return;
                selectedFaction = faction;
                Refresh(force: true);
            }

            private static void PlaceGlyph(RectTransform page, Rect area, string kind, Color tint)
            {
                var go = new GameObject("MetricGlyph", typeof(RectTransform), typeof(MfdGlyph));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(page, worldPositionStays: false);
                AvKit.Place(rt, area);

                MfdGlyph glyph = go.GetComponent<MfdGlyph>();
                glyph.raycastTarget = false;
                glyph.SetKind(kind, tint);
            }

            private static void SectionGlyph(RectTransform page, string kind, Color tint)
            {
                PlaceGlyph(page, new Rect(AvTokens.Space3 + 156f, -AvTokens.Space1 - 1f,
                    14f, 14f), kind, tint);
            }

            private static void CardCorners(RectTransform page, Rect area, Color tint)
            {
                Color quiet = tint.WithAlpha(0.48f);
                float right = area.x + area.width;
                float bottom = area.y - area.height;
                AvKit.Rule(page, new Rect(right - 13f, area.y - 4f, 9f, 1f), quiet);
                AvKit.Rule(page, new Rect(right - 4f, area.y - 4f, 1f, 9f), quiet);
                AvKit.Rule(page, new Rect(area.x + 4f, bottom + 4f, 9f, 1f), quiet);
                AvKit.Rule(page, new Rect(area.x + 4f, bottom + 13f, 1f, 9f), quiet);
            }

            private void BuildForcesPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, PageWidth,
                                  "FORCE INVENTORY", "LIVE ASSETS");
                SectionGlyph(page, "faction", AvTheme.RailInfo);

                // The identity card: roundel, name and the faction's own flag art. The flag
                // keeps the 2:1 aspect it was drawn at — a 456-wide banner would flatten the
                // emblem into a smear, so it takes a fixed panel on the right instead.
                const float heroHeight = 96f;
                const float flagWidth = 112f;
                Rect hero = new Rect(AvTokens.Space3, y, PageWidth - 2f * AvTokens.Space3,
                                     heroHeight);
                AvKit.Panel(page, hero, AvTheme.Surface, AvSprites.Card);
                Rect flagArea = new Rect(hero.x + hero.width - flagWidth - 4f, hero.y - 4f,
                                         flagWidth, heroHeight - 8f);
                factionFlag = AvKit.Panel(page, flagArea, Color.white);
                factionFlag.preserveAspect = true;
                factionFlag.raycastTarget = false;
                AvKit.Rule(page, new Rect(hero.x, hero.y, 3f, heroHeight), AvTheme.RailReady);
                AvKit.Outline(page, hero, AvTheme.Hairline);
                CardCorners(page, hero, AvTheme.RailReady);
                AvKit.Rule(page, new Rect(hero.x + AvTokens.Space3, hero.y - 12f,
                    28f, 1f), AvTheme.RailInfo.WithAlpha(0.65f));

                factionLogo = AvKit.Panel(page, new Rect(hero.x + AvTokens.Space3, hero.y - 28f, 40f, 40f),
                                           Color.white);
                factionLogo.preserveAspect = true;
                factionLogo.raycastTarget = false;
                float textX = hero.x + AvTokens.Space3 + 48f;
                float textWidth = Mathf.Max(0f, flagArea.x - textX - AvTokens.Space2);
                factionName = AvStyled.Label(page,
                    new Rect(textX, hero.y - 22f, textWidth, 38f),
                    "SYNCING FACTION", "metric-value");
                // A long faction name wraps or shrinks; it is the screen's identity and
                // must never be cut.
                factionName.enableWordWrapping = true;
                factionName.overflowMode = TextOverflowModes.Overflow;
                factionName.enableAutoSizing = true;
                factionName.fontSizeMin = AvTokens.FontBody;
                factionName.fontSizeMax = 22f;
                factionSubtitle = AvStyled.Label(page,
                    new Rect(textX, hero.y - 64f, textWidth, 24f),
                    "LIVE THEATER ORDER OF BATTLE", "metric-cap");
                factionSubtitle.enableWordWrapping = true;
                y -= heroHeight + AvTokens.Space3;

                forceTotals = new TMP_Text[4];
                string[] labels = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
                float totalWidth = (PageWidth - 2f * AvTokens.Space3 - AvTokens.Gap * 3f) / 4f;
                for (int i = 0; i < forceTotals.Length; i++)
                {
                    float x = AvTokens.Space3 + i * (totalWidth + AvTokens.Gap);
                    AvKit.Panel(page, new Rect(x, y, totalWidth, 47f), AvTheme.SurfaceInert);
                    AvKit.Rule(page, new Rect(x, y, totalWidth, 1f), AvTheme.Hairline);
                    AvStyled.Label(page, new Rect(x, y, totalWidth, 11f), labels[i], "metric-key",
                                   align: TextAlignmentOptions.Center);
                    PlaceGlyph(page, new Rect(x + totalWidth - 16f, y - 2f, 12f, 12f),
                        new[] { "building", "ground", "ship", "air" }[i],
                        AvTheme.RailInfo.WithAlpha(0.7f));
                    forceTotals[i] = AvStyled.Label(page, new Rect(x, y - 20f, totalWidth, 20f),
                                                     "—", "metric-value",
                                                     align: TextAlignmentOptions.Center);
                }
                for (int i = 0; i < forceBars.Length; i++)
                {
                    float x = AvTokens.Space3 + i * (totalWidth + AvTokens.Gap);
                    AvKit.Rule(page, new Rect(x, y-41f, totalWidth, 3f), AvTheme.SurfaceRaised);
                    forceBars[i] = AvKit.Rule(page, new Rect(x, y-41f, 0f, 3f), AvTheme.Accent);
                }
                forceBarWidth = totalWidth;
                y -= 48f;

                y = Heading(page, y, PageWidth, "ASSET CLASS", "CHOOSE A CLASS");
                definitionTabs = CreateButtonRow(page, y,
                    new[] { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" }, SelectDefinitions);
                y -= AvTokens.RowHeight + AvTokens.Space3;
                y = Heading(page, y, PageWidth, "UNIT READOUT", "CURRENT / LOST");
                // The row math folds the pager in: Space1 and RowHeight are the pager, and
                // Space2 is the margin above the status strip. The pitch then takes the
                // whole remainder, so the list ends within a line of the body bottom
                // instead of stopping 28-52px short of it.
                const float chrome = 42f;
                float readable = PageHeight + y - chrome;
                int rows = Mathf.Clamp(Mathf.FloorToInt(readable / 46f), 2, 8);
                float cell = Mathf.Clamp(readable / rows, 46f, 88f);
                definitionGrid = new MfdPagingGrid(page, y, PageWidth - AvTokens.Space3, 2, rows,
                                                   readOnly: true, rowHeight: cell);
            }

            private void BuildLedgerPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, PageWidth,
                                  "THEATER LEDGER", "MISSION ACCOUNTING");
                SectionGlyph(page, "chart", AvTheme.RailInfo);
                ledgerTabs = CreateButtonRow(page, y,
                    new[] { "RESERVES", "LOSSES", "VALUE", "MANPOWER" }, SelectLedger);
                y -= AvTokens.RowHeight + AvTokens.Space3;

                y = Heading(page, y, PageWidth, "ASSET BREAKDOWN", "LIVE TOTALS");
                ledgerMetrics = new AvStyled.Metric[4];
                string[] labels = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
                float deckWidth = PageWidth - 2f * AvTokens.Space3;
                float cellWidth = deckWidth * 0.5f;
                Rect ledgerDeck = new Rect(AvTokens.Space3, y, deckWidth, 174f);
                AvKit.Panel(page, ledgerDeck, AvTheme.SurfaceInert);
                AvKit.Outline(page, ledgerDeck, AvTheme.Hairline.WithAlpha(0.65f));
                CardCorners(page, ledgerDeck, AvTheme.RailInfo);
                AvKit.Rule(page, new Rect(ledgerDeck.x, y, 52f, 2f), AvTheme.RailInfo);
                AvKit.Rule(page, new Rect(ledgerDeck.x + cellWidth, y - 7f, 1f, 160f),
                    AvTheme.Hairline.WithAlpha(0.45f));
                AvKit.Rule(page, new Rect(ledgerDeck.x + 7f, y - 87f, deckWidth - 14f, 1f),
                    AvTheme.Hairline.WithAlpha(0.45f));
                for (int i = 0; i < ledgerMetrics.Length; i++)
                {
                    int row = i / 2;
                    int column = i % 2;
                    ledgerMetrics[i] = AvStyled.MetricCell(page,
                        new Rect(AvTokens.Space3 + column * cellWidth,
                                 y - row * 87f, cellWidth, 87f), labels[i], "UNIT");
                    PlaceGlyph(page, new Rect(AvTokens.Space3 + (column + 1) * cellWidth - 28f,
                        y - row * 87f - 9f, 16f, 16f),
                        new[] { "building", "ground", "ship", "air" }[i],
                        AvTheme.RailInfo.WithAlpha(0.8f));
                }
                ledgerChart = new MfdLedgerChart(page, y - 184f, PageWidth,
                    PageHeight + y - 184f - AvTokens.Space2);
            }

            private void BuildStatusPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, PageWidth,
                                  "FACTION CABINET", "MANDATE / DIRECTIVES");
                SectionGlyph(page, "faction", AvTheme.RailInfo);
                float x = AvTokens.Space3;
                float width = PageWidth - 2f * x;
                Rect mandate = new Rect(x, y, width, 102f);
                mandateRail = AvKit.TacticalCard(page, mandate, AvTheme.RailReady).Rail;
                CardCorners(page, mandate, AvTheme.RailInfo);
                PlaceGlyph(page, new Rect(x + width - 30f, y - 9f, 16f, 16f),
                    "faction", AvTheme.RailInfo);
                BriefLabel(page, new Rect(x + 12f, y - 9f, width - 48f, 16f),
                    "PUBLIC MANDATE / HOST MORALE", "metric-key", 11f);
                mandateHeadline = BriefLabel(page, new Rect(x + 12f, y - 30f, width - 24f, 28f),
                    "AWAITING HOST", "row-value", 18f);
                mandateHeadline.rectTransform.sizeDelta = new Vector2(width - 78f, 28f);
                mandateFlag = AvKit.Panel(page, new Rect(x + width - 58f, y - 17f, 40f, 40f),
                    Color.white);
                mandateFlag.preserveAspect = true;
                mandateFlag.raycastTarget = false;
                mandateFlag.enabled = false;
                mandateEffect = BriefLabel(page, new Rect(x + 12f, y - 63f, width - 24f, 34f),
                    "CONTRACT EFFECT UNAVAILABLE", "metric-cap", 11f);
                y -= 102f + AvTokens.Space3;

                Rect dispatch = new Rect(x, y, width, 132f);
                AvKit.TacticalCard(page, dispatch, AvTheme.RailInfo);
                CardCorners(page, dispatch, AvTheme.RailInfo);
                PlaceGlyph(page, new Rect(x + width - 30f, y - 9f, 16f, 16f),
                    "radio", AvTheme.RailInfo);
                BriefLabel(page, new Rect(x + 12f, y - 9f, width - 48f, 16f),
                    "WORLD DISPATCH / ACTIVE PRESSURE", "metric-key", 11f);
                politicalEvent = BriefLabel(page, new Rect(x + 12f, y - 30f, width - 24f, 27f),
                    "NO ACTIVE EVENT", "row-value", 16f);
                politicalEffect = BriefLabel(page, new Rect(x + 12f, y - 59f, width - 24f, 23f),
                    "THEATER CALM", "metric-cap", 11f);
                politicalBrief = BriefLabel(page, new Rect(x + 12f, y - 86f, width - 24f, 42f),
                    "AWAITING WORLD NEWS", "metric-cap", 10.5f);
                y -= 132f + AvTokens.Space3;

                Rect directive = new Rect(x, y, width, 100f);
                AvKit.TacticalCard(page, directive, AvTheme.RailReady);
                CardCorners(page, directive, AvTheme.RailInfo);
                PlaceGlyph(page, new Rect(x + width - 30f, y - 9f, 16f, 16f),
                    "flag", AvTheme.RailInfo);
                BriefLabel(page, new Rect(x + 12f, y - 9f, width - 48f, 16f),
                    "FIELD DIRECTIVE / CONTRACT BOARD", "metric-key", 11f);
                politicalMission = BriefLabel(page, new Rect(x + 12f, y - 30f, width - 24f, 25f),
                    "NO DIRECTIVE", "row-value", 15f);
                politicalMissionDetail = BriefLabel(page, new Rect(x + 12f, y - 60f, width - 24f, 35f),
                    "MISSIONS CHANGE MORALE AND FILL THE TREASURY", "metric-cap", 11f);
                y -= 100f + AvTokens.Space3;

                y = Heading(page, y, PageWidth, "THEATER DIRECTORY", "WHO HOLDS THE LINE");
                infoTabs = CreateButtonRow(page, y, new[] { "AIRBASES", "PLAYERS" }, SelectInfo);
                y -= AvTokens.RowHeight + AvTokens.Space3;
                y = Heading(page, y, PageWidth, "ACTIVE ENTRIES", "DIRECTORY");
                const float statusRowHeight = 44f;
                int rows = Mathf.Clamp(Mathf.FloorToInt((PageHeight + y - 44f) / statusRowHeight), 3, 12);
                infoGrid = new MfdPagingGrid(page, y, PageWidth - AvTokens.Space3,
                    1, rows, readOnly: true, rowHeight: statusRowHeight);
            }

            private void RefreshPolitics(FactionHQ hq)
            {
                float morale = FactionResourceHistoryStore.Morale(hq);
                Sprite seal = hq.faction == null ? null : hq.faction.factionColorLogo;
                mandateFlag.sprite = seal;
                mandateFlag.enabled = seal != null;
                bool known = MfdResourceHistory.Finite(morale);
                SecondaryObjectiveView contract = FeaturedContract(hq);
                mandateHeadline.text = known ? MandateName(morale) : "REPORT UNAVAILABLE";
                mandateHeadline.color = !known || morale >= 50f ? AvTheme.TextPrimary : AvTheme.Warning;
                if (mandateRail != null) mandateRail.color = !known ? AvTheme.Disabled :
                    morale < 50f ? AvTheme.Warning : AvTheme.RailReady;
                int percent = known ? Mathf.RoundToInt((FactionMoraleState.ContractMultiplier(morale) - 1f) * 100f) : 0;
                mandateEffect.text = known
                    ? "MORALE " + morale.ToString("0.#") + "/100  •  NEW CONTRACTS " +
                        (percent >= 0 ? "+" : "") + percent + "% MONEY / XP\n" +
                        (contract == null ? "MISSION SUCCESS +3  •  ACTIVE ABORT -1" :
                            (contract.IsActive ? "ACTIVE DIRECTIVE / " : "OFFERED DIRECTIVE / ") +
                            MfdSecondaryObjectives.PlainObjective(contract.Title).ToUpperInvariant())
                    : "AWAITING HOST MORALE • CONTRACT EFFECT UNAVAILABLE";
                ModServices.TryGet(out IActiveEventsView events);
                ActiveEventView current = events?.Current;
                bool applies = current != null && events.AffectsFaction(hq.faction?.factionName);
                string price = current == null ? "NO EFFECT" :
                    events.PriceSummaryForFaction(hq.faction?.factionName);
                politicalEvent.text = current == null ? "NO ACTIVE EVENT" : current.Title.ToUpperInvariant();
                politicalEffect.text = current == null ? "THEATER CALM • SUPPORT PRICES AT BASELINE" :
                    !current.TargetResolved ? "TARGET LOST • EVENT EFFECT CANCELLED" :
                    (applies ? IsLocalFaction(hq) ? "LOCAL EVENT" : "BASE EVENT" : "OTHER SIDE") + " • " + price +
                    " • " + EventClock(current);
                politicalEffect.color = !applies || price == "NO EFFECT" ? AvTheme.TextPrimary :
                    price.StartsWith("+") ? AvTheme.Warning : AvTheme.Accent;
                politicalBrief.text = current == null ? "The cabinet awaits the next world dispatch." :
                    !current.TargetResolved ? "TARGET LOST • FIELD ORDERS CANCELLED" :
                    current.IsSuper ? current.Target + " • " + NextEventBeat(current) :
                    current.Tier + " / " + current.Target + " • " + current.FlavorText;
                politicalMission.text = contract == null ?
                    IsLocalFaction(hq) ? ContractsDisabled() ? "FIELD DIRECTIVES DISABLED" :
                        "NO OPEN FIELD DIRECTIVE" : "FIELD ORDERS CLASSIFIED" :
                    (contract.IsActive ? "ACTIVE / " : "OFFERED / ") +
                    MfdSecondaryObjectives.PlainObjective(contract.Title).ToUpperInvariant();
                politicalMissionDetail.text = contract == null ?
                    IsLocalFaction(hq) ? ContractsDisabled() ? "HOST HAS OPTIONAL CONTRACTS OFF" :
                        "CHECK MIS FOR NEW CONTRACTS • SUCCESS +3 MORALE" :
                    "ONLY YOUR OWN FACTION'S CONTRACTS ARE REPORTED HERE" :
                    MfdSecondaryObjectives.PayoutLabel(contract) + "  •  SUCCESS +3 MORALE";
            }

            private static TMP_Text BriefLabel(RectTransform parent, Rect area, string copy,
                string style, float maxSize)
            {
                TMP_Text label = AvStyled.Label(parent, area, copy, style,
                    align: TextAlignmentOptions.TopLeft);
                label.alignment = TextAlignmentOptions.TopLeft;
                label.enableWordWrapping = true;
                label.enableAutoSizing = true;
                label.fontSizeMin = AvTokens.FontMicro;
                label.fontSizeMax = maxSize;
                return label;
            }

            private static string MandateName(float morale) => morale >= 80f ? "MOBILIZED HOME FRONT" :
                morale >= 50f ? "STEADFAST MANDATE" : morale >= 25f ? "WAR WEARY" : "CABINET IN CRISIS";

            private static string EventClock(ActiveEventView active)
            {
                MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
                if (active == null || mission == null) return "LIVE";
                float left = active.EndsAtMissionTime - mission.MissionTime;
                if (!MfdResourceHistory.Finite(left)) return "LIVE";
                int seconds = Mathf.Max(0, Mathf.CeilToInt(left));
                return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00") + " LEFT";
            }

            private static string NextEventBeat(ActiveEventView active)
            {
                MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
                if (mission == null || active?.Steps == null) return "OPEN EVN FOR FIELD ORDERS";
                float elapsed = mission.MissionTime - active.StartedAtMissionTime;
                for (int i = 0; i < active.Steps.Count; i++)
                {
                    ActiveEventStep step = active.Steps[i];
                    if (step.AtSeconds <= elapsed) continue;
                    int remaining = Mathf.CeilToInt(step.AtSeconds - elapsed);
                    return "NEXT ORDER " + (remaining / 60).ToString("00") + ":" +
                        (remaining % 60).ToString("00") + " / " + step.Label;
                }
                return "FIELD ORDERS ISSUED • OPEN EVN FOR DISPATCH";
            }

            private static bool ContractsDisabled() =>
                ModServices.TryGet(out ISecondaryObjectivesView board) && board?.Status != null &&
                board.Status.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0;

            private static bool IsLocalFaction(FactionHQ hq) =>
                hq != null && GameManager.GetLocalPlayer<Player>(out Player local) &&
                local != null && local.HQ == hq;

            private static SecondaryObjectiveView FeaturedContract(FactionHQ hq)
            {
                if (!IsLocalFaction(hq) || !ModServices.TryGet(out ISecondaryObjectivesView board) ||
                    board?.Objectives == null) return null;
                SecondaryObjectiveView offered = null;
                IReadOnlyList<SecondaryObjectiveView> objectives = board.Objectives;
                for (int i = 0; i < Mathf.Min(16, objectives.Count); i++)
                {
                    SecondaryObjectiveView entry = objectives[i];
                    if (entry == null) continue;
                    if (entry.IsActive) return entry;
                    if (offered == null && entry.IsOffered) offered = entry;
                }
                return offered;
            }

            private AvButton[] CreateButtonRow(RectTransform parent, float y, string[] labels,
                                               Action<int> selected)
            {
                var row = new AvButton[labels.Length];
                float gap = AvTokens.Gap;
                float width = (PageWidth - 2f * AvTokens.Space3 - gap * (labels.Length - 1)) /
                              labels.Length;
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    row[i] = PanelButton(parent,
                        new Rect(AvTokens.Space3 + i * (width + gap), y, width, AvTokens.RowHeight),
                        labels[i], "tab", () => selected(index), AvButtonStyle.Tab);
                }
                return row;
            }

            private void SelectPage(int selected)
            {
                selectedPage = selected;
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SelectDefinitions(int selected)
            {
                definitionGroup = Mathf.Clamp(selected, 0, 3);
                PopulateDefinitions();
                RequestRefresh();
            }

            private void SelectLedger(int selected)
            {
                ledgerMode = (LedgerMode)Mathf.Clamp(selected, 0, 3);
                RequestRefresh();
            }

            private void SelectInfo(int selected)
            {
                // Keep this local. InfoPanel_Faction.SetDisplayAirbases currently selects
                // Players in stock code, exactly the bug this owned view avoids inheriting.
                infoMode = selected == 0 ? InfoMode.Airbases : InfoMode.Players;
                infoGrid.ResetPage();
                RequestRefresh();
            }

            private void PopulateDefinitions()
            {
                definitions.Clear();
                definitionsLoaded = false;
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (encyclopedia == null) return;

                switch (definitionGroup)
                {
                    case 0:
                        if (encyclopedia.buildings == null) return;
                        for (int i = 0; i < encyclopedia.buildings.Count; i++)
                            definitions.Add(encyclopedia.buildings[i]);
                        break;
                    case 1:
                        if (encyclopedia.vehicles == null) return;
                        for (int i = 0; i < encyclopedia.vehicles.Count; i++)
                            definitions.Add(encyclopedia.vehicles[i]);
                        break;
                    case 2:
                        if (encyclopedia.ships == null) return;
                        for (int i = 0; i < encyclopedia.ships.Count; i++)
                            definitions.Add(encyclopedia.ships[i]);
                        break;
                    default:
                        if (encyclopedia.aircraft == null) return;
                        for (int i = 0; i < encyclopedia.aircraft.Count; i++)
                            definitions.Add(encyclopedia.aircraft[i]);
                        break;
                }
                definitionsLoaded = true;
            }

            private void SetForceTotals(MissionStatsTracker tracker)
            {
                if (forceTotals == null) return;
                for (int i = 0; i < forceTotals.Length; i++) forceTotals[i].text = "—";
                if (tracker == null)
                {
                    foreach (Image bar in forceBars) bar.rectTransform.sizeDelta = new Vector2(0f, 3f);
                    return;
                }
                MissionStatsTracker.TypeStat stats = tracker.units;
                forceTotals[0].text = stats.buildings.current.ToString("0");
                forceTotals[1].text = stats.vehicles.current.ToString("0");
                forceTotals[2].text = stats.ships.current.ToString("0");
                forceTotals[3].text = stats.aircraft.current.ToString("0");
                float maximum = Mathf.Max(stats.buildings.current, stats.vehicles.current,
                    stats.ships.current, stats.aircraft.current);
                chartPrimary[0] = stats.buildings.current; chartPrimary[1] = stats.vehicles.current;
                chartPrimary[2] = stats.ships.current; chartPrimary[3] = stats.aircraft.current;
                for (int i = 0; i < 4; i++)
                    forceBars[i].rectTransform.sizeDelta = new Vector2(forceBarWidth *
                        MfdChartScale.Fraction(chartPrimary[i], maximum), 3f);
            }

            private void RefreshDefinitionGrid(FactionHQ hq)
            {
                if (!definitionsLoaded) PopulateDefinitions();

                // Sort active units to the front so players don't have to page past dozens of 0/0 items
                if (hq != null && hq.missionStatsTracker != null)
                {
                    definitions.Sort((a, b) =>
                    {
                        int aCurr = hq.missionStatsTracker.GetCurrentUnits(a);
                        int bCurr = hq.missionStatsTracker.GetCurrentUnits(b);
                        if (aCurr != bCurr) return bCurr.CompareTo(aCurr);

                        int aLost = hq.missionStatsTracker.GetLostUnits(a);
                        int bLost = hq.missionStatsTracker.GetLostUnits(b);
                        if (aLost != bLost) return bLost.CompareTo(aLost);

                        string aName = a != null ? a.unitName ?? a.code : "";
                        string bName = b != null ? b.unitName ?? b.code : "";
                        return string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
                    });
                }

                definitionGrid.SetData(definitions.Count,
                    i => DefinitionLabel(definitions[i]),
                    i => false,
                    null,
                    icons: i => definitions[i] == null ? null : definitions[i].mapIcon,
                    details: i => DefinitionTooltip(definitions[i], hq),
                    subs: i => DefinitionDetail(definitions[i], hq));
            }

            /// <summary>
            /// The unit's own code, never cut: the readout's figures live on the second
            /// line so a long designation cannot push a count off the edge.
            /// </summary>
            private static string DefinitionLabel(UnitDefinition definition)
            {
                if (definition == null) return "UNKNOWN";
                string code = !string.IsNullOrEmpty(definition.code) ? definition.code : definition.unitName;
                return string.IsNullOrEmpty(code) ? "UNIT" : code.ToUpperInvariant();
            }

            private static string DefinitionTooltip(UnitDefinition definition, FactionHQ hq)
            {
                if (definition == null) return "UNIT";
                string full = !string.IsNullOrEmpty(definition.unitName) ? definition.unitName : definition.code;
                return (full ?? "UNIT").ToUpperInvariant() + " • " + DefinitionDetail(definition, hq);
            }

            private static string DefinitionDetail(UnitDefinition definition, FactionHQ hq)
            {
                if (definition == null || hq == null || hq.missionStatsTracker == null)
                    return "NO UNIT ACCOUNTING YET";
                int current = hq.missionStatsTracker.GetCurrentUnits(definition);
                int lost = hq.missionStatsTracker.GetLostUnits(definition);
                return current + " CURRENT  /  " + lost + " LOST";
            }

            private void RefreshLedger(FactionHQ hq)
            {
                if (ledgerMetrics == null) return;
                if (hq.missionStatsTracker == null)
                {
                    foreach (AvStyled.Metric metric in ledgerMetrics)
                        metric.Set("—", "DATA UNAVAILABLE", 0f, AvTheme.Accent);
                    ledgerChart.Clear();
                    return;
                }
                MissionStatsTracker.TypeStat category = LedgerCategory(hq.missionStatsTracker);
                MissionStatsTracker.Stat[] values =
                {
                    category.buildings, category.vehicles, category.ships, category.aircraft,
                };
                string unit = ledgerMode == LedgerMode.Value ? "CR" :
                              ledgerMode == LedgerMode.Manpower ? "PAX" : "UNIT";
                string caption = ledgerMode.ToString().ToUpperInvariant();
                for (int i = 0; i < ledgerMetrics.Length; i++)
                {
                    MissionStatsTracker.Stat stat = values[i];
                    float value = ledgerMode == LedgerMode.Reserves ? ReserveCount(hq, i) :
                                  LedgerValue(stat);
                    float denominator = ledgerMode == LedgerMode.Reserves
                        ? Mathf.Max(1f, value + stat.current)
                        : Mathf.Max(1f, stat.total);
                    float fraction = ledgerMode == LedgerMode.Losses ? stat.lost / denominator :
                                     value / denominator;
                    chartPrimary[i] = value;
                    chartSecondary[i] = ledgerMode == LedgerMode.Reserves || ledgerMode == LedgerMode.Losses
                        ? stat.current : stat.lost;
                    ledgerMetrics[i].Unit.text = unit;
                    ledgerMetrics[i].Set(FormatLedger(value), caption, fraction,
                                         ledgerMode == LedgerMode.Losses ? AvTheme.Warning : AvTheme.Accent);
                }
                ledgerChart.Set(chartPrimary, chartSecondary, caption,
                    ledgerMode == LedgerMode.Reserves || ledgerMode == LedgerMode.Losses ? "CURRENT" : "LOST",
                    unit, FormatLedger);
            }

            private MissionStatsTracker.TypeStat LedgerCategory(MissionStatsTracker tracker)
            {
                switch (ledgerMode)
                {
                    case LedgerMode.Losses:
                    case LedgerMode.Reserves:
                        return tracker.units;
                    case LedgerMode.Value:
                        return tracker.value;
                    default:
                        return tracker.manpower;
                }
            }

            private float LedgerValue(MissionStatsTracker.Stat stat)
            {
                switch (ledgerMode)
                {
                    case LedgerMode.Losses: return stat.lost;
                    case LedgerMode.Value: return stat.current;
                    case LedgerMode.Manpower: return stat.current;
                    default: return stat.current;
                }
            }

            private string FormatLedger(float value)
            {
                return ledgerMode == LedgerMode.Value
                    ? UnitConverter.ValueReading(value)
                    : value.ToString("0");
            }

            private static int ReserveCount(FactionHQ hq, int group)
            {
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (hq == null || encyclopedia == null) return 0;

                int total = 0;
                switch (group)
                {
                    case 0:
                        if (encyclopedia.buildings != null)
                            for (int i = 0; i < encyclopedia.buildings.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.buildings[i]);
                        break;
                    case 1:
                        if (encyclopedia.vehicles != null)
                            for (int i = 0; i < encyclopedia.vehicles.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.vehicles[i]);
                        break;
                    case 2:
                        if (encyclopedia.ships != null)
                            for (int i = 0; i < encyclopedia.ships.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.ships[i]);
                        break;
                    default:
                        if (encyclopedia.aircraft != null)
                            for (int i = 0; i < encyclopedia.aircraft.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.aircraft[i]);
                        break;
                }
                return total;
            }

            private static int SupportedSupply(FactionHQ hq, UnitDefinition definition)
            {
                // FactionHQ stores reserve supply only for mobile definitions. The
                // encyclopedia also exposes buildings through UnitDefinition, but passing
                // one to GetUnitSupply throws in current game builds. Keep the adapter
                // tolerant of mixed/future category lists by filtering on the API contract.
                if (hq == null || definition == null) return 0;
                if (!(definition is AircraftDefinition) && !(definition is VehicleDefinition)) return 0;
                return Mathf.Max(0, hq.GetUnitSupply(definition));
            }

            private void RefreshInfo(FactionHQ hq)
            {
                infoRows.Clear();
                infoSubs.Clear();
                infoIcons.Clear();

                if (infoMode == InfoMode.Airbases)
                {
                    foreach (Airbase airbase in hq.GetAirbases())
                    {
                        if (airbase == null) continue;
                        string title = MfdAirbaseFormatter.Format(airbase);
                        string sub = MfdAirbaseFormatter.OperationalTelemetry(airbase);
                        Sprite icon = null;

                        // Check if airbase has attached unit with an icon (e.g. Carrier)
                        if (airbase.TryGetAttachedUnit(out Unit unit) && unit != null && unit.definition != null)
                        {
                            icon = unit.definition.mapIcon;
                        }

                        infoRows.Add(title.ToUpperInvariant());
                        infoSubs.Add(sub);
                        infoIcons.Add(icon);
                    }
                }
                else
                {
                    foreach (var player in hq.GetPlayers(sortByScore: true))
                    {
                        if (player == null) continue;
                        string callsign = player.ToString();
                        string craft = player.Aircraft != null && player.Aircraft.definition != null
                            ? player.Aircraft.definition.unitName
                            : "NO ACTIVE CRAFT";
                        string score = "SCORE " + player.PlayerScore.ToString("0.0");
                        Sprite icon = player.Aircraft != null && player.Aircraft.definition != null
                            ? player.Aircraft.definition.mapIcon
                            : null;

                        infoRows.Add(callsign.ToUpperInvariant());
                        infoSubs.Add(craft.ToUpperInvariant() + "  ·  " + score);
                        infoIcons.Add(icon);
                    }
                }

                if (infoRows.Count == 0)
                {
                    infoRows.Add(infoMode == InfoMode.Airbases ? "NO ACTIVE AIRBASES" : "NO ACTIVE PLAYERS");
                    infoSubs.Add("THEATER DIRECTORY IS CURRENTLY EMPTY");
                    infoIcons.Add(null);
                }

                infoGrid.SetData(infoRows.Count,
                    i => infoRows[i],
                    i => false,
                    _ => { },
                    icons: i => infoIcons[i],
                    subs: i => infoSubs[i]);
            }

            private void UpdateButtonRows()
            {
                SetRow(definitionTabs, definitionGroup);
                SetRow(ledgerTabs, (int)ledgerMode);
                SetRow(infoTabs, infoMode == InfoMode.Airbases ? 0 : 1);
            }

            private static void SetRow(AvButton[] buttons, int selected)
            {
                if (buttons == null) return;
                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].SetLatched(i == selected);
                    MfdGlyph glyph = buttons[i].GetComponentInChildren<MfdGlyph>(true);
                    if (glyph != null) glyph.SetSelected(i == selected);
                }
            }
        }
    }
}
