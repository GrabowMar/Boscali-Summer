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

            private static readonly string[] ResourceLabels = { "FUNDS", "WARHEADS", "MANPOWER", "MORALE" };
            private static readonly string[] ClassLabels = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
            private static readonly string[] LedgerLabels = { "RESERVES", "LOSSES", "VALUE", "MANPOWER" };

            private readonly InfoPanel_Faction source;
            private readonly InfoPanel_Faction otherSource;
            private readonly List<UnitDefinition> definitions = new List<UnitDefinition>();
            private readonly List<string> infoRows = new List<string>();
            private readonly List<string> infoSubs = new List<string>();
            private readonly List<Sprite> infoIcons = new List<Sprite>();
            // The OWN / OPPOSITION source switch is console-wide; kit v2 has no chrome slot
            // outside the page flows for a control, so every page carries its own copy.
            private readonly List<AvControl> ownFactionButtons = new List<AvControl>(4);
            private readonly List<AvControl> otherFactionButtons = new List<AvControl>(4);
            private readonly float[] chartPrimary = new float[4];
            private readonly float[] chartSecondary = new float[4];
            private readonly float[] resourceBuffer = new float[MfdResourceHistory.Capacity];

            private AvChip[] chips;
            private AvMetric[] resourceMetrics;
            private MfdPagingGrid definitionGrid;
            private MfdPagingGrid infoGrid;
            private ChoiceRow definitionTabs;
            private ChoiceRow ledgerTabs;
            private AvSegmented infoTabs;
            private ChoiceRow resourceTabs;
            private FactionHeaderPart factionHeader;
            private AvGauge[] forceGauges;
            private AvRow[] ledgerRows;
            private LedgerChartPart ledgerChart;
            private AvLineChart resourceChart;
            private ProseNote resourceSummary;
            private TextLine economyHeadline;
            private TextLine economyEffect;
            private TextLine economyContract;
            private ImageTile mandateFlag;
            private TextLine mandateHeadline;
            private TextLine mandateEffect;
            private TextLine politicalEvent;
            private TextLine politicalEffect;
            private TextLine politicalBrief;
            private TextLine politicalMission;
            private TextLine politicalMissionDetail;

            private int selectedFaction;
            private int definitionGroup;
            private bool definitionsLoaded;
            private LedgerMode ledgerMode;
            private InfoMode infoMode;
            private int selectedPage;
            private int resourceSeries;
            private int renderedResourceSeries = -1;
            private int renderedResourceCount = -1;
            private float renderedResourceTime = float.NaN;
            private FactionHQ observedHq;
            private MfdResourceHistory resourceHistory;

            public FactionPresenter(MFDScreen screen, InfoPanel_Faction source,
                                    InfoPanel_Faction otherSource, VanillaMfdPanelId id)
                : base(screen, id)
            {
                this.source = source;
                this.otherSource = otherSource;
            }

            protected override string Title => "FACTION";

            protected override (AvIcon Icon, string Label)[] TabItems { get; } = new[]
            {
                (AvIcon.Coins, "ECONOMY"),
                (AvIcon.Shield, "FORCES"),
                (AvIcon.ChartLine, "LEDGER"),
                (AvIcon.Flag, "POLITICS"),
            };

            protected override void BuildContent()
            {
                chips = Console.Chips(3);
                // Stockpile telemetry is faction-wide, so it rides the console header on every page.
                resourceMetrics = Console.Metrics(ResourceLabels);
                BuildResourcesPage(CreatePage());
                BuildForcesPage(CreatePage());
                BuildLedgerPage(CreatePage());
                BuildStatusPage(CreatePage());
                SelectDefinitions(0);
            }

            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                RequestRefresh();
            }

            protected override void RefreshContent()
            {
                RefreshFactionSelector();
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
                    chips[0].Set("LINK", AvState.Inert);
                    chips[1].Set("DATA", AvState.Inert);
                    chips[2].Set("—", AvState.Inert);
                    for (int i = 0; i < resourceMetrics.Length; i++)
                        resourceMetrics[i].Set("—", "NO HQ", 0f, AvState.Inert);
                    factionHeader.Set("FACTION HQ UNAVAILABLE",
                        "Waiting for the selected faction and its live mission data.");
                    factionHeader.Logo.enabled = false;
                    RefreshSegments();
                    return;
                }

                string name = hq.faction == null ? "FACTION" : hq.faction.factionName;
                chips[0].Set("SCORE " + AvNum.Fixed(hq.factionScore, 1), AvState.Ready);
                chips[1].Set(AvNum.Money(hq.factionFunds), AvState.Ready);
                chips[2].Set("WHD " + AvNum.Fixed(hq.GetWarheadStockpile(), 0), AvState.Ready);

                string extended = hq.faction == null ? null : hq.faction.factionExtendedName;
                factionHeader.Set(name ?? "FACTION", string.IsNullOrEmpty(extended)
                    ? "LIVE THEATER ORDER OF BATTLE" : extended.ToUpperInvariant());
                Sprite logo = hq.faction == null ? null : hq.faction.factionColorLogo;
                factionHeader.Logo.sprite = logo;
                factionHeader.Logo.enabled = logo != null;

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
                RefreshSegments();
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

            // ---------------------------------------------------------- source switch

            private void BuildFactionSelector(AvFlow page)
            {
                AvButtons row = page.Buttons(
                    new AvControl.Spec("OWN FACTION", () => SelectFaction(0), AvButtonStyle.Tab, AvIcon.Shield),
                    new AvControl.Spec("OPPOSITION", () => SelectFaction(1), AvButtonStyle.Tab, AvIcon.Target));
                ownFactionButtons.Add(row.Controls[0]);
                otherFactionButtons.Add(row.Controls[1]);
            }

            private void RefreshFactionSelector()
            {
                string own = FactionLabel(source, "OWN FACTION");
                string other = FactionLabel(otherSource, "OPPOSITION");
                for (int i = 0; i < ownFactionButtons.Count; i++)
                {
                    ownFactionButtons[i].Label = own;
                    ownFactionButtons[i].Latched = selectedFaction == 0;
                }
                for (int i = 0; i < otherFactionButtons.Count; i++)
                {
                    otherFactionButtons[i].Label = other;
                    otherFactionButtons[i].Latched = selectedFaction == 1;
                    otherFactionButtons[i].Interactable = otherSource != null;
                }
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

            private void RefreshSegments()
            {
                definitionTabs.Refresh();
                ledgerTabs.Refresh();
                infoTabs.Refresh();
                resourceTabs.Refresh();
            }

            // ---------------------------------------------------------- ECONOMY page

            private void BuildResourcesPage(AvFlow page)
            {
                BuildFactionSelector(page);
                page.Section(MfdChromeIcon.For("RADIO"), "WAR ECONOMY", "LIVE DIRECTIVE");
                economyHeadline = page.Add(new TextLine(page.Content, AvTextRole.Head, true));
                economyEffect = page.Add(new TextLine(page.Content, AvTextRole.Prose));
                economyContract = page.Add(new TextLine(page.Content, AvTextRole.ProseSmall));
                economyHeadline.Set("AWAITING DISPATCH");
                economyEffect.Set("SUPPORT COST / BASELINE");
                economyContract.Set("NO CONTRACT PAYOUT REPORTED");

                page.Section(MfdChromeIcon.For("CHART"), "RESOURCE HISTORY", "LOCAL OBSERVATIONS");
                resourceTabs = new ChoiceRow(page, ResourceLabels,
                    () => resourceSeries, selected =>
                    {
                        resourceSeries = selected;
                        RequestRefresh();
                    });
                resourceSummary = page.Add(new ProseNote(page.Content, "WAITING FOR SAMPLES"));
                resourceChart = page.Add(new AvLineChart(page.Content, 170f));
                Note(page, "A sample is taken every " + AvNum.Seconds(MfdResourceHistory.Interval, 0) +
                    " while this panel is open; history keeps the last " +
                    AvNum.Seconds(MfdResourceHistory.Capacity * MfdResourceHistory.Interval, 0) + ".");
            }

            private void RefreshResources(FactionHQ hq)
            {
                float manpower = FactionResourceHistoryStore.Manpower(hq);
                float morale = FactionResourceHistoryStore.Morale(hq);
                float funds = hq.factionFunds;
                int warheads = hq.GetWarheadStockpile();
                bool manpowerKnown = MfdResourceHistory.Finite(manpower);
                bool moraleKnown = MfdResourceHistory.Finite(morale);

                resourceMetrics[0].Set(AvNum.Money(funds), funds < 0f ? "NEGATIVE BALANCE" : "AVAILABLE",
                    0f, funds < 0f ? AvState.Caution : AvState.Ready);
                resourceMetrics[1].Set(AvNum.Fixed(warheads, 0), "STOCKPILE", 0f, AvState.Ready);
                resourceMetrics[2].Set(manpowerKnown ? AvNum.Thousands(manpower) : "—",
                    manpowerKnown ? "IN ASSETS" : "UNAVAILABLE", 0f,
                    manpowerKnown ? AvState.Ready : AvState.Inert);
                // Only morale has a meaningful maximum; the other resources are absolute stocks.
                resourceMetrics[3].Set(moraleKnown ? AvNum.Fixed(morale, 1) : "—",
                    moraleKnown
                        ? "CONTRACTS " + AvNum.Fixed(FactionMoraleState.ContractMultiplier(morale), 2) + "x"
                        : "HOST DATA UNAVAILABLE",
                    moraleKnown ? morale / 100f : 0f,
                    !moraleKnown ? AvState.Inert : morale < 50f ? AvState.Caution : AvState.Ready);

                ModServices.TryGet(out IActiveEventsView events);
                ActiveEventView current = events?.Current;
                bool applies = current != null && events.AffectsFaction(hq.faction?.factionName);
                string price = current == null ? "NO EFFECT" :
                    events.PriceSummaryForFaction(hq.faction?.factionName);
                economyHeadline.Set(current == null ? "SUPPLY LINES HOLDING" : current.Title.ToUpperInvariant());
                economyEffect.Set(current == null ? "SUPPORT COST / BASELINE " + AvNum.Fixed(1, 2) + "x" :
                    !current.TargetResolved ? "TARGET LOST / EVENT EFFECT CANCELLED" :
                    applies ? (IsLocalFaction(hq) ? "LOCAL EVENT / " : "BASE EVENT / ") +
                        price + " • " + EventClock(current) :
                    "OTHER SIDE / NO EVENT PRICE CHANGE HERE", PriceState(applies, price));
                SecondaryObjectiveView contract = FeaturedContract(hq);
                economyContract.Set(contract == null
                    ? IsLocalFaction(hq) ? ContractsDisabled() ? "FIELD CONTRACTS DISABLED BY HOST" :
                        "NO OPEN CONTRACT PAYOUT • CHECK MIS" : "CONTRACT PAYOUTS / LOCAL FACTION ONLY"
                    : "FIELD CONTRACT / " + MfdSecondaryObjectives.PayoutLabel(contract));

                if (resourceHistory == null) resourceHistory = FactionResourceHistoryStore.For(hq);
                RefreshResourceChart();
            }

            /// <summary>
            /// Plots the selected series of the observed window. Only the trailing run of
            /// finite samples is drawn (a gap breaks the line), and fewer than two samples
            /// is an honest empty chart rather than a flat line.
            /// </summary>
            private void RefreshResourceChart()
            {
                int count = resourceHistory != null ? resourceHistory.Count : 0;
                float latest = count > 0 ? resourceHistory.Time(count - 1) : float.NaN;
                bool changed = count != renderedResourceCount ||
                               (count > 0 && latest != renderedResourceTime) ||
                               renderedResourceSeries != resourceSeries;
                if (!changed) return;
                renderedResourceCount = count;
                renderedResourceTime = latest;
                renderedResourceSeries = resourceSeries;

                string name = ResourceLabels[resourceSeries];
                int run = 0, runStart = 0;
                for (int i = 0; i < count; i++)
                {
                    float value = resourceHistory.Value(resourceSeries, i);
                    if (!MfdResourceHistory.Finite(value)) { run = 0; runStart = i + 1; continue; }
                    resourceBuffer[run++] = value;
                }
                if (run < 2)
                {
                    resourceChart.SetSeries(resourceBuffer, 0, "—", "—", "—");
                    resourceSummary.Set(name + "  •  " + (count == 0 ? "NO SAMPLES YET" :
                        "NOT ENOUGH SAMPLES — A LINE NEEDS TWO"));
                    return;
                }

                float lo = resourceBuffer[0], hi = resourceBuffer[0];
                for (int i = 1; i < run; i++)
                {
                    lo = Mathf.Min(lo, resourceBuffer[i]);
                    hi = Mathf.Max(hi, resourceBuffer[i]);
                }
                float last = resourceBuffer[run - 1];
                resourceChart.SetSeries(resourceBuffer, run, FormatResource(lo), FormatResource(hi),
                    FormatResource(last));
                float change = last - resourceBuffer[0];
                float duration = resourceHistory.Time(count - 1) - resourceHistory.Time(runStart);
                resourceSummary.Set(name + "  •  CHANGE " + (change > 0f ? "+" : "") + FormatResource(change) +
                    "  •  LAST " + AvNum.Clock(Mathf.Max(0f, duration)));
            }

            private string FormatResource(float value)
            {
                switch (resourceSeries)
                {
                    case 0: return AvNum.Money(value);
                    case 3: return AvNum.Fixed(value, 1);
                    default: return AvNum.Fixed(value, 0);
                }
            }

            private static AvState PriceState(bool applies, string price) =>
                !applies || price == "NO EFFECT" ? AvState.Inert :
                price.StartsWith("+") ? AvState.Caution : AvState.Ready;

            // ----------------------------------------------------------- FORCES page

            private void BuildForcesPage(AvFlow page)
            {
                BuildFactionSelector(page);
                page.Section(MfdChromeIcon.For("FACTION"), "FORCE INVENTORY", "LIVE ASSETS");
                factionHeader = page.Add(new FactionHeaderPart(page.Content));

                forceGauges = new AvGauge[ClassLabels.Length];
                for (int i = 0; i < forceGauges.Length; i++)
                    forceGauges[i] = new AvGauge(page.Content, ClassLabels[i], AvGaugeShape.Bar, 56f);
                page.Row(forceGauges[0], forceGauges[1], forceGauges[2], forceGauges[3]);

                page.Section(MfdChromeIcon.For("FILTER"), "ASSET CLASS", "CHOOSE A CLASS");
                definitionTabs = new ChoiceRow(page, ClassLabels, () => definitionGroup, SelectDefinitions);

                page.Section(AvIcon.ListDetails, "UNIT READOUT", "CURRENT / LOST");
                definitionGrid = new MfdPagingGrid(page.Content, 2, 6, readOnly: true, rowHeight: 46f);
                page.Add(definitionGrid);
            }

            private void SetForceTotals(MissionStatsTracker tracker)
            {
                if (forceGauges == null) return;
                if (tracker == null)
                {
                    foreach (AvGauge gauge in forceGauges) gauge.Set(0f, "—", AvState.Inert);
                    return;
                }
                MissionStatsTracker.TypeStat stats = tracker.units;
                chartPrimary[0] = stats.buildings.current; chartPrimary[1] = stats.vehicles.current;
                chartPrimary[2] = stats.ships.current; chartPrimary[3] = stats.aircraft.current;
                float maximum = Mathf.Max(chartPrimary[0], chartPrimary[1], chartPrimary[2], chartPrimary[3]);
                for (int i = 0; i < forceGauges.Length; i++)
                    forceGauges[i].Set(MfdChartScale.Fraction(chartPrimary[i], maximum),
                        AvNum.Fixed(chartPrimary[i], 0), AvState.Ready);
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
                return AvNum.Fixed(current, 0) + " CURRENT  /  " + AvNum.Fixed(lost, 0) + " LOST";
            }

            private void SelectDefinitions(int selected)
            {
                definitionGroup = Mathf.Clamp(selected, 0, 3);
                PopulateDefinitions();
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

            // ----------------------------------------------------------- LEDGER page

            private void BuildLedgerPage(AvFlow page)
            {
                BuildFactionSelector(page);
                page.Section(MfdChromeIcon.For("LEDGER"), "THEATER LEDGER", "MISSION ACCOUNTING");
                ledgerTabs = new ChoiceRow(page, LedgerLabels, () => (int)ledgerMode, SelectLedger);

                page.Section(AvIcon.LayersSubtract, "ASSET BREAKDOWN", "LIVE TOTALS");
                ledgerRows = new AvRow[ClassLabels.Length];
                for (int i = 0; i < ledgerRows.Length; i++) ledgerRows[i] = new AvRow(page.Content);
                page.Row(ledgerRows[0], ledgerRows[1]);
                page.Row(ledgerRows[2], ledgerRows[3]);

                page.Section(AvIcon.ChartArrows, "COMPARISON", "COMMON SCALE");
                ledgerChart = page.Add(new LedgerChartPart(page.Content, page.Inner));
            }

            private void SelectLedger(int selected)
            {
                ledgerMode = (LedgerMode)Mathf.Clamp(selected, 0, 3);
                RequestRefresh();
            }

            private void RefreshLedger(FactionHQ hq)
            {
                if (ledgerRows == null) return;
                if (hq.missionStatsTracker == null)
                {
                    for (int i = 0; i < ledgerRows.Length; i++)
                        ledgerRows[i].Set(ClassLabels[i], "DATA UNAVAILABLE", "—", AvState.Inert);
                    ledgerChart.Chart.Clear();
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
                for (int i = 0; i < ledgerRows.Length; i++)
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
                    ledgerRows[i].Set(ClassLabels[i],
                        caption + " / " + unit + "  •  " + AvNum.Percent(Mathf.Clamp01(fraction)) + " OF CLASS",
                        FormatLedger(value),
                        ledgerMode == LedgerMode.Losses ? AvState.Caution : AvState.Ready);
                }
                ledgerChart.Chart.Set(chartPrimary, chartSecondary, caption,
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
                return ledgerMode == LedgerMode.Value ? AvNum.Money(value) : AvNum.Fixed(value, 0);
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

            // --------------------------------------------------------- POLITICS page

            private void BuildStatusPage(AvFlow page)
            {
                BuildFactionSelector(page);
                page.Section(MfdChromeIcon.For("FLAG"), "PUBLIC MANDATE", "HOST MORALE");
                mandateFlag = page.Add(new ImageTile(page.Content, 40f));
                mandateHeadline = page.Add(new TextLine(page.Content, AvTextRole.Head, true));
                mandateEffect = page.Add(new TextLine(page.Content, AvTextRole.Prose));
                mandateHeadline.Set("AWAITING HOST");
                mandateEffect.Set("CONTRACT EFFECT UNAVAILABLE");

                page.Section(MfdChromeIcon.For("RADIO"), "WORLD DISPATCH", "ACTIVE PRESSURE");
                politicalEvent = page.Add(new TextLine(page.Content, AvTextRole.Head, true));
                politicalEffect = page.Add(new TextLine(page.Content, AvTextRole.Prose));
                politicalBrief = page.Add(new TextLine(page.Content, AvTextRole.ProseSmall));
                politicalEvent.Set("NO ACTIVE EVENT");
                politicalEffect.Set("THEATER CALM");
                politicalBrief.Set("Awaiting world news.");

                page.Section(MfdChromeIcon.For("MISSION"), "FIELD DIRECTIVE", "CONTRACT BOARD");
                politicalMission = page.Add(new TextLine(page.Content, AvTextRole.Head, true));
                politicalMissionDetail = page.Add(new TextLine(page.Content, AvTextRole.Prose));
                politicalMission.Set("NO DIRECTIVE");
                politicalMissionDetail.Set("Missions change morale and fill the treasury.");

                page.Section(MfdChromeIcon.For("PLAYERS"), "THEATER DIRECTORY", "WHO HOLDS THE LINE");
                infoTabs = page.Add(new AvSegmented(page.Content, "SHOW", new[] { "AIRBASES", "PLAYERS" },
                    () => infoMode == InfoMode.Airbases ? 0 : 1, SelectInfo));
                infoGrid = new MfdPagingGrid(page.Content, 1, 8, readOnly: true, rowHeight: 44f);
                page.Add(infoGrid);
            }

            private void SelectInfo(int selected)
            {
                // Keep this local. InfoPanel_Faction.SetDisplayAirbases currently selects
                // Players in stock code, exactly the bug this owned view avoids inheriting.
                infoMode = selected == 0 ? InfoMode.Airbases : InfoMode.Players;
                infoGrid.ResetPage();
                RequestRefresh();
            }

            private void RefreshPolitics(FactionHQ hq)
            {
                float morale = FactionResourceHistoryStore.Morale(hq);
                Sprite seal = hq.faction == null ? null : hq.faction.factionColorLogo;
                mandateFlag.Image.sprite = seal;
                mandateFlag.Image.enabled = seal != null;
                bool known = MfdResourceHistory.Finite(morale);
                SecondaryObjectiveView contract = FeaturedContract(hq);
                mandateHeadline.Set(known ? MandateName(morale) : "REPORT UNAVAILABLE",
                    !known ? AvState.Inert : morale >= 50f ? AvState.Ready : AvState.Caution);
                int percent = known ? Mathf.RoundToInt((FactionMoraleState.ContractMultiplier(morale) - 1f) * 100f) : 0;
                mandateEffect.Set(known
                    ? "MORALE " + AvNum.Fixed(morale, 1) + "/100  •  NEW CONTRACTS " +
                        AvNum.Signed(percent, 0) + "% MONEY / XP\n" +
                        (contract == null ? "MISSION SUCCESS +3  •  ACTIVE ABORT -1" :
                            (contract.IsActive ? "ACTIVE DIRECTIVE / " : "OFFERED DIRECTIVE / ") +
                            MfdSecondaryObjectives.PlainObjective(contract.Title).ToUpperInvariant())
                    : "AWAITING HOST MORALE • CONTRACT EFFECT UNAVAILABLE");
                ModServices.TryGet(out IActiveEventsView events);
                ActiveEventView current = events?.Current;
                bool applies = current != null && events.AffectsFaction(hq.faction?.factionName);
                string price = current == null ? "NO EFFECT" :
                    events.PriceSummaryForFaction(hq.faction?.factionName);
                politicalEvent.Set(current == null ? "NO ACTIVE EVENT" : current.Title.ToUpperInvariant());
                politicalEffect.Set(current == null ? "THEATER CALM • SUPPORT PRICES AT BASELINE" :
                    !current.TargetResolved ? "TARGET LOST • EVENT EFFECT CANCELLED" :
                    (applies ? IsLocalFaction(hq) ? "LOCAL EVENT" : "BASE EVENT" : "OTHER SIDE") + " • " + price +
                    " • " + EventClock(current), PriceState(applies, price));
                politicalBrief.Set(current == null ? "The cabinet awaits the next world dispatch." :
                    !current.TargetResolved ? "TARGET LOST • FIELD ORDERS CANCELLED" :
                    current.IsSuper ? current.Target + " • " + NextEventBeat(current) :
                    current.Tier + " / " + current.Target + " • " + current.FlavorText);
                politicalMission.Set(contract == null ?
                    IsLocalFaction(hq) ? ContractsDisabled() ? "FIELD DIRECTIVES DISABLED" :
                        "NO OPEN FIELD DIRECTIVE" : "FIELD ORDERS CLASSIFIED" :
                    (contract.IsActive ? "ACTIVE / " : "OFFERED / ") +
                    MfdSecondaryObjectives.PlainObjective(contract.Title).ToUpperInvariant());
                politicalMissionDetail.Set(contract == null ?
                    IsLocalFaction(hq) ? ContractsDisabled() ? "HOST HAS OPTIONAL CONTRACTS OFF" :
                        "CHECK MIS FOR NEW CONTRACTS • SUCCESS +3 MORALE" :
                    "ONLY YOUR OWN FACTION'S CONTRACTS ARE REPORTED HERE" :
                    MfdSecondaryObjectives.PayoutLabel(contract) + "  •  SUCCESS +3 MORALE");
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
                        string score = "SCORE " + AvNum.Fixed(player.PlayerScore, 1);
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

            private static string MandateName(float morale) => morale >= 80f ? "MOBILIZED HOME FRONT" :
                morale >= 50f ? "STEADFAST MANDATE" : morale >= 25f ? "WAR WEARY" : "CABINET IN CRISIS";

            private static string EventClock(ActiveEventView active)
            {
                MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
                if (active == null || mission == null) return "LIVE";
                float left = active.EndsAtMissionTime - mission.MissionTime;
                if (!MfdResourceHistory.Finite(left)) return "LIVE";
                return AvNum.Clock(Mathf.Max(0, Mathf.CeilToInt(left))) + " LEFT";
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
                    return "NEXT ORDER " + AvNum.Clock(remaining) + " / " + step.Label;
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

            // ------------------------------------------------------------ local parts

            /// <summary>
            /// One mutable line of briefing copy in a given type role. A state other than
            /// Inert tints it through the same row-value classes an AvRow uses (R1: the copy
            /// itself always carries the word). Kit v2 has no mutable free-text part.
            /// </summary>
            private sealed class TextLine : AvPart
            {
                private readonly TMP_Text text;
                private readonly bool emphasize;
                private AvState state = AvState.Inert;

                public TextLine(RectTransform parent, AvTextRole role, bool emphasize = false)
                {
                    this.emphasize = emphasize;
                    Rect = AvLay.Child(parent, "Line");
                    text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
                    Restyle();
                }

                public void Set(string body, AvState st = AvState.Inert)
                {
                    string next = body ?? "";
                    if (text.text != next) text.text = next;
                    if (st != state) { state = st; Restyle(); }
                }

                public override float Measure(float width) => AvText.Height(text, width);

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    AvLay.Place(text.rectTransform, 0f, 0f, s.W, s.H);
                }

                public override void Restyle()
                {
                    text.color = state != AvState.Inert
                        ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value " + AvStates.Class(state)).Color, AvTheme.TextPrimary)
                        : emphasize
                            ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary)
                            : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                }
            }

            /// <summary>The faction's own colour roundel (mission data, not chrome), centred.</summary>
            private sealed class ImageTile : AvPart
            {
                private readonly float size;

                public ImageTile(RectTransform parent, float size)
                {
                    this.size = size;
                    Rect = AvLay.Child(parent, "Image");
                    Image = AvLay.Solid(Rect, "Sprite", Color.white);
                    Image.preserveAspect = true;
                    Image.enabled = false;
                }

                public Image Image { get; }

                public override float Measure(float width) => size;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    AvLay.Place(Image.rectTransform, 0f, 0f, size, size);
                }
            }

            /// <summary>
            /// The identity block: faction roundel, name (the screen's identity; it wraps
            /// rather than cuts) and extended name.
            /// </summary>
            private sealed class FactionHeaderPart : AvPart
            {
                private const float LogoSize = 44f, TextX = 56f;
                private readonly TMP_Text name, subtitle;

                public FactionHeaderPart(RectTransform parent)
                {
                    Rect = AvLay.Child(parent, "FactionHeader");
                    Logo = AvLay.Solid(Rect, "Logo", Color.white);
                    Logo.preserveAspect = true;
                    Logo.enabled = false;
                    name = AvText.Make(Rect, "Name", AvTextRole.Title, "SYNCING FACTION", TextAlignmentOptions.TopLeft, true);
                    subtitle = AvText.Make(Rect, "Subtitle", AvTextRole.Micro, "LIVE THEATER ORDER OF BATTLE",
                        TextAlignmentOptions.TopLeft, true);
                    Restyle();
                }

                public Image Logo { get; }

                public void Set(string factionName, string extended)
                {
                    string n = factionName ?? "", s = extended ?? "";
                    if (name.text != n) name.text = n;
                    if (subtitle.text != s) subtitle.text = s;
                }

                public override float Measure(float width)
                {
                    float w = width - TextX;
                    return Mathf.Max(LogoSize, AvText.Height(name, w) + 4f + AvText.Height(subtitle, w));
                }

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float w = s.W - TextX, nh = AvText.Height(name, w);
                    AvLay.Place(Logo.rectTransform, 0f, 0f, LogoSize, LogoSize);
                    AvLay.Place(name.rectTransform, TextX, 0f, w, nh);
                    AvLay.Place(subtitle.rectTransform, TextX, nh + 4f, w, AvText.Height(subtitle, w));
                }

                public override void Restyle()
                {
                    name.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("title").Color, AvTheme.TextPrimary);
                    subtitle.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                }
            }

            /// <summary>
            /// Hosts the reserves-vs-losses bar comparison (<see cref="MfdLedgerChart"/>, data
            /// viz that lays out its own children top-left, y down) at a fixed size in a flow.
            /// </summary>
            private sealed class LedgerChartPart : AvPart
            {
                private const float ChartHeight = 260f;

                // The chart insets itself by its own side margin, so handing it the flow's inner
                // width keeps its bars and value labels clear of the scroll gutter.
                public LedgerChartPart(RectTransform parent, float width)
                {
                    Rect = AvLay.Child(parent, "Ledger");
                    Chart = new MfdLedgerChart(Rect, 0f, width, ChartHeight);
                }

                public MfdLedgerChart Chart { get; }

                public override float Measure(float width) => ChartHeight;

                public override void Place(AvSlot slot) => base.Place(slot);
            }
        }
    }
}
