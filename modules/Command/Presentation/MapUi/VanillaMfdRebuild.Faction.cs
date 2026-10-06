using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
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
            private const int AttritionLimit = 32;

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

            private AvMetric[] resourceMetrics;
            private UnitRosterPart definitionGrid;
            private MfdPagingGrid infoGrid;
            private ChoiceRow definitionTabs;
            private ChoiceRow ledgerTabs;
            private AvSegmented infoTabs;
            private HistoryCard history;
            private EventStrip eventStrip;
            private MandateStrip mandateStrip;
            private BriefBlock dispatchBlock;
            private BriefBlock directiveBlock;
            private FactionHeaderPart factionHeader;
            private AvGauge[] ledgerRows;
            private UnitRosterPart attritionRoster;
            private readonly List<UnitDefinition> lossDefinitions = new List<UnitDefinition>(AttritionLimit);
            private TextLine directorySummary;
            private AvHazardBar moraleBar;
            private LedgerChartPart ledgerChart;
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

            protected override string[] TabTips { get; } = new[]
            {
                "Economy: the current event's price effect with its countdown, and four simultaneous compact histories of funds, warheads, manpower and morale.",
                "Forces: live unit counts by class, then every unit type of the chosen class with its live and lost counts, busiest first.",
                "Ledger: reserves, losses, value or manpower per class, plus a named loss ledger of the unit types hit hardest.",
                "Politics: the morale mandate, the current dispatch and directive, and a directory of airbases or players.",
            };

            protected override void BuildContent()
            {
                // Stockpile telemetry is faction-wide, so it rides the console header on every page. The
                // score / funds / warhead chips that used to sit above it repeated the same numbers and are gone.
                resourceMetrics = Console.Metrics(ResourceLabels);
                string[] metricTips =
                {
                    "Funds: the faction's spendable balance. It pays for support call-ins and reinforcements.",
                    "Warheads: stockpiled strategic warheads. Losing airbases and depots drains them.",
                    "Manpower: personnel embodied in the faction's living assets.",
                    "Morale: the host's 0-100 mandate. Below 50 contracts pay less; above it they pay more."
                };
                for (int i = 0; i < resourceMetrics.Length; i++)
                    AvHelpTip.Attach(resourceMetrics[i].Rect.gameObject, metricTips[i]);
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
                    renderedResourceCount = -1;
                    renderedResourceTime = float.NaN;
                    definitionGrid.ResetPage();
                    infoGrid.ResetPage();
                    attritionRoster.ResetPage();
                }
                if (hq == null)
                {
                    for (int i = 0; i < resourceMetrics.Length; i++)
                        resourceMetrics[i].Set("—", "NO HQ", 0f, AvState.Inert);
                    factionHeader.Set("FACTION HQ UNAVAILABLE",
                        "Waiting for the selected faction and its live mission data.");
                    factionHeader.Logo.enabled = false;
                    definitionGrid.Set(Array.Empty<UnitDefinition>(), null, "UNIT MANIFEST", "STATISTICS UNAVAILABLE");
                    attritionRoster.Set(Array.Empty<UnitDefinition>(), null, "ATTRITION / UNIT TYPE", "STATISTICS UNAVAILABLE");
                    SetForceTotals(null);
                    foreach (AvGauge row in ledgerRows) row.Set(0f, "\u2014", AvState.Inert);
                    ledgerChart.Chart.Clear();
                    history.Clear("FACTION HQ UNAVAILABLE");
                    economyHeadline.Set("FACTION HQ UNAVAILABLE");
                    economyEffect.Set("SUPPORT COST / UNAVAILABLE");
                    economyContract.Set("CONTRACT / UNAVAILABLE");
                    eventStrip.Bar.Set(0f, "\u2014", AvState.Inert);
                    mandateHeadline.Set("REPORT UNAVAILABLE");
                    mandateEffect.Set("CONTRACT EFFECT / UNAVAILABLE");
                    mandateFlag.Image.enabled = false;
                    moraleBar.Set(0f, "\u2014", AvState.Inert);
                    politicalEvent.Set("DISPATCH UNAVAILABLE");
                    politicalEffect.Set("\u2014"); politicalBrief.Set("\u2014");
                    politicalMission.Set("DIRECTIVE UNAVAILABLE"); politicalMissionDetail.Set("\u2014");
                    directorySummary.Set("DIRECTORY / UNAVAILABLE");
                    infoGrid.SetData(0, _ => "", _ => false, null);
                    RefreshSegments();
                    return;
                }

                string name = hq.faction == null ? "FACTION" : hq.faction.factionName;

                string extended = hq.faction == null ? null : hq.faction.factionExtendedName;
                factionHeader.Set(name ?? "FACTION", (string.IsNullOrEmpty(extended)
                    ? "LIVE THEATER ORDER OF BATTLE" : extended.ToUpperInvariant()) +
                    "  ·  SCORE " + AvNum.Fixed(hq.factionScore, 1));
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
                    case 0: return "ECONOMY";
                    case 1: return "FORCES • LIVE / LOST";
                    case 2: return "LEDGER • " + ledgerMode.ToString().ToUpperInvariant();
                    default: return "POLITICS";
                }
            }

            // ---------------------------------------------------------- source switch

            private void BuildFactionSelector(AvFlow page)
            {
                AvButtons row = page.Buttons(
                    new AvControl.Spec("OWN FACTION", () => SelectFaction(0), AvButtonStyle.Tab, AvIcon.Shield),
                    new AvControl.Spec("OPPOSITION", () => SelectFaction(1), AvButtonStyle.Tab, AvIcon.Target));
                row.Controls[0].Help = "Show the numbers of your own faction.";
                row.Controls[1].Help = "Show the opposing faction's numbers. It is read from the same host data, so it is only as fresh as the last sync.";
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
            }

            // ---------------------------------------------------------- ECONOMY page

            private void BuildResourcesPage(AvFlow page)
            {
                BuildFactionSelector(page);
                eventStrip = page.Add(new EventStrip(page.Content));
                economyHeadline = eventStrip.Headline;
                economyEffect = eventStrip.Effect;
                economyContract = eventStrip.Contract;
                economyHeadline.Set("AWAITING DISPATCH");
                economyEffect.Set("SUPPORT COST / BASELINE");
                economyContract.Set("—");
                eventStrip.Bar.Help = "Event countdown: the share of the current local event still to run. The clock beside it is the " +
                                      "time left; when it empties, prices return to baseline.";

                history = page.Add(new HistoryCard(page.Content));
                history.Help = "Independent resource scales. One observation every " +
                    AvNum.Seconds(MfdResourceHistory.Interval, 0) + "; up to " +
                    AvNum.Seconds(MfdResourceHistory.Capacity * MfdResourceHistory.Interval, 0) +
                    " retained. Missing observations break each resource trace; no backfill.";
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
                AvHelpTip.Attach(resourceMetrics[3].Rect.gameObject,
                    "Morale: the host's 0-100 mandate. Below 50 contracts pay less; above it they pay more." +
                    (moraleKnown ? " Live contract multiplier " +
                        AvNum.Fixed(FactionMoraleState.ContractMultiplier(morale), 2) + "x." : " Host morale is unavailable."));

                ModuleServices.TryGet(out IActiveEventsView events);
                ActiveEventView current = events?.Current;
                bool applies = current != null && events.AffectsFaction(hq.faction?.factionName);
                string price = current == null ? "NO EFFECT" :
                    events.PriceSummaryForFaction(hq.faction?.factionName);
                economyHeadline.Set(current == null ? "SUPPLY LINES HOLDING" : current.Title.ToUpperInvariant());
                economyEffect.Set(current == null ? "SUPPORT COST / BASELINE " + AvNum.Fixed(1, 2) + "x" :
                    !current.TargetResolved ? "TARGET LOST / EVENT EFFECT CANCELLED" :
                    applies ? (IsLocalFaction(hq) ? "LOCAL EVENT / " : "BASE EVENT / ") + price :
                    "OTHER SIDE / NO EVENT PRICE CHANGE HERE", PriceState(applies, price));
                string eventClock;
                float eventLeft = EventRemaining(current, out eventClock);
                eventStrip.Bar.Set(eventLeft, eventClock,
                    current == null ? AvState.Inert : applies ? AvState.Caution : AvState.Info);
                eventStrip.Tone(current == null ? AvState.Inert : applies ? AvState.Caution : AvState.Info);
                SecondaryObjectiveView contract = FeaturedContract(hq);
                economyContract.Set(contract == null
                    ? IsLocalFaction(hq) ? ContractsDisabled() ? "CONTRACTS OFF" : "—" : "—"
                    : "CONTRACT / " + MfdSecondaryObjectives.PayoutLabel(contract));
                eventStrip.Changed();

                if (resourceHistory == null) resourceHistory = FactionResourceHistoryStore.For(hq);
                RefreshResourceChart();
            }

            /// <summary>Each resource gets its own scale and trailing finite run; gaps never bridge.</summary>
            private void RefreshResourceChart()
            {
                int count = resourceHistory != null ? resourceHistory.Count : 0;
                float latest = count > 0 ? resourceHistory.Time(count - 1) : float.NaN;
                if (count == renderedResourceCount && (count == 0 || latest == renderedResourceTime)) return;
                renderedResourceCount = count;
                renderedResourceTime = latest;

                for (int series = 0; series < ResourceLabels.Length; series++)
                {
                    int run = 0, runStart = 0;
                    for (int i = 0; i < count; i++)
                    {
                        float value = resourceHistory.Value(series, i);
                        if (!MfdResourceHistory.Finite(value)) { run = 0; runStart = i + 1; continue; }
                        resourceBuffer[run++] = value;
                    }
                    ResourceHistoryTile tile = history.Tiles[series];
                    string current = run > 0 ? FormatResource(series, resourceBuffer[run - 1]) : "—";
                    if (run < 2)
                    {
                        tile.Set(resourceBuffer, 0, "—", "—", current,
                            count == 0 ? "NO SAMPLES" : run == 0 ? "UNAVAILABLE" : "NEEDS 2 SAMPLES");
                        continue;
                    }
                    float lo = resourceBuffer[0], hi = resourceBuffer[0];
                    for (int i = 1; i < run; i++)
                    {
                        lo = Mathf.Min(lo, resourceBuffer[i]);
                        hi = Mathf.Max(hi, resourceBuffer[i]);
                    }
                    float change = resourceBuffer[run - 1] - resourceBuffer[0];
                    float duration = Mathf.Max(0f, latest - resourceHistory.Time(runStart));
                    tile.Set(resourceBuffer, run, FormatResource(series, lo), FormatResource(series, hi), current,
                        "CHANGE " + (change > 0f ? "+" : "") + FormatResource(series, change) + "\nWINDOW " + AvNum.Clock(duration));
                }
                history.Changed();
            }

            private static string FormatResource(int series, float value)
            {
                switch (series)
                {
                    case 0: return AvNum.Money(value);
                    case 3: return AvNum.Fixed(value, 1);
                    default: return AvNum.Fixed(value, 0);
                }
            }

            /// <summary>Share of the event still to run (0..1) and its clock text, for the countdown bar.</summary>
            private static float EventRemaining(ActiveEventView active, out string clock)
            {
                clock = "—";
                MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
                if (active == null || mission == null) return 0f;
                float total = active.EndsAtMissionTime - active.StartedAtMissionTime;
                float left = active.EndsAtMissionTime - mission.MissionTime;
                if (!MfdResourceHistory.Finite(left) || !MfdResourceHistory.Finite(total)) { clock = "LIVE"; return 1f; }
                clock = AvNum.Clock(Mathf.Max(0, Mathf.CeilToInt(left))) + " LEFT";
                return total > 0.5f ? Mathf.Clamp01(left / total) : 1f;
            }

            private static AvState PriceState(bool applies, string price) =>
                !applies || price == "NO EFFECT" ? AvState.Inert :
                price.StartsWith("+") ? AvState.Caution : AvState.Ready;

            // ----------------------------------------------------------- FORCES page

            private void BuildForcesPage(AvFlow page)
            {
                BuildFactionSelector(page);
                factionHeader = page.Add(new FactionHeaderPart(page.Content));

                definitionTabs = new ChoiceRow(page, ClassLabels, () => definitionGroup, SelectDefinitions);
                for (int i = 0; i < ClassLabels.Length; i++)
                    definitionTabs[i].Help = "List every " + ClassLabels[i].ToLowerInvariant().TrimEnd('s') + " type with its live and lost " +
                        "counts, busiest first.";

                definitionGrid = page.Add(new UnitRosterPart(page.Content, false), 1f);
            }
            private void SetForceTotals(MissionStatsTracker tracker)
            {
                if (definitionTabs == null) return;
                if (tracker == null)
                {
                    for (int i = 0; i < ClassLabels.Length; i++) definitionTabs[i].Label = ClassLabels[i] + "\n\u2014 LIVE";
                    return;
                }
                MissionStatsTracker.TypeStat stats = tracker.units;
                chartPrimary[0] = stats.buildings.current; chartPrimary[1] = stats.vehicles.current;
                chartPrimary[2] = stats.ships.current; chartPrimary[3] = stats.aircraft.current;
                for (int i = 0; i < ClassLabels.Length; i++)
                    definitionTabs[i].Label = ClassLabels[i] + "\n" + AvNum.Fixed(chartPrimary[i], 0) + " LIVE";
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

                int activeTypes = 0, live = 0;
                if (hq?.missionStatsTracker != null)
                    foreach (UnitDefinition definition in definitions)
                    {
                        if (definition == null) continue;
                        int current = hq.missionStatsTracker.GetCurrentUnits(definition);
                        live += current;
                        if (current > 0) activeTypes++;
                    }
                definitionGrid.Set(definitions, hq, ClassLabels[definitionGroup] + " / MANIFEST",
                    hq?.missionStatsTracker == null ? "STATISTICS UNAVAILABLE" :
                    activeTypes + " ACTIVE TYPES / " + live + " LIVE");
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
                    return "—";
                int current = hq.missionStatsTracker.GetCurrentUnits(definition);
                int lost = hq.missionStatsTracker.GetLostUnits(definition);
                return AvNum.Fixed(current, 0) + " LIVE  ·  " + AvNum.Fixed(lost, 0) + " LOST";
            }

            private void SelectDefinitions(int selected)
            {
                definitionGroup = Mathf.Clamp(selected, 0, 3);
                PopulateDefinitions();
                definitionGrid?.ResetPage();
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
                ledgerTabs = new ChoiceRow(page, LedgerLabels, () => (int)ledgerMode, SelectLedger);
                string[] ledgerTips =
                {
                    "Reserves: what is still alive against what is stockpiled and can be brought in.",
                    "Losses: units destroyed so far, per class.",
                    "Value: the credit worth of what is alive, with the worth already lost beneath it.",
                    "Manpower: personnel alive, with those already lost beneath."
                };
                for (int i = 0; i < ledgerTips.Length; i++) ledgerTabs[i].Help = ledgerTips[i];

                ledgerRows = new AvGauge[ClassLabels.Length];
                for (int i = 0; i < ledgerRows.Length; i++)
                {
                    ledgerRows[i] = new AvGauge(page.Content, ClassLabels[i], AvGaugeShape.Ring, 56f);
                    ledgerRows[i].Help = ClassLabels[i] + " in the chosen ledger view, as a share of everything that class ever fielded. " +
                                         "The bars below give the exact figures.";
                }
                page.Row(ledgerRows[0], ledgerRows[1], ledgerRows[2], ledgerRows[3]);

                ledgerChart = page.Add(new LedgerChartPart(page.Content, page.Inner));

                attritionRoster = page.Add(new UnitRosterPart(page.Content, true), 1f);
            }
            private void SelectLedger(int selected)
            {
                ledgerMode = (LedgerMode)Mathf.Clamp(selected, 0, 3);
                RequestRefresh();
            }

            private void RefreshLedger(FactionHQ hq)
            {
                if (ledgerRows == null) return;
                RefreshAttrition(hq);
                if (hq.missionStatsTracker == null)
                {
                    for (int i = 0; i < ledgerRows.Length; i++)
                        ledgerRows[i].Set(0f, "—", AvState.Inert);
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
                    ledgerRows[i].Set(Mathf.Clamp01(fraction), FormatLedger(value),
                        ledgerMode == LedgerMode.Losses ? AvState.Caution : AvState.Ready);
                }
                ledgerChart.Chart.Set(chartPrimary, chartSecondary, caption,
                    ledgerMode == LedgerMode.Reserves || ledgerMode == LedgerMode.Losses ? "CURRENT" : "LOST",
                    unit, FormatLedger);
            }

            // Keep the highest losses bounded; each definition is scanned once per refresh.
            private void RefreshAttrition(FactionHQ hq)
            {
                lossDefinitions.Clear();
                MissionStatsTracker tracker = hq?.missionStatsTracker;
                Encyclopedia encyclopedia = Encyclopedia.i;
                int lostTotal = 0;
                if (tracker != null && encyclopedia != null)
                {
                    lostTotal += ScanAttrition(tracker, encyclopedia.buildings);
                    lostTotal += ScanAttrition(tracker, encyclopedia.vehicles);
                    lostTotal += ScanAttrition(tracker, encyclopedia.ships);
                    lostTotal += ScanAttrition(tracker, encyclopedia.aircraft);
                }
                attritionRoster.Set(lossDefinitions, hq, "ATTRITION / UNIT TYPE",
                    tracker == null || encyclopedia == null ? "STATISTICS UNAVAILABLE" :
                    lostTotal + " LOST / TOP " + lossDefinitions.Count + " TYPES");
            }

            private int ScanAttrition<T>(MissionStatsTracker tracker, IList<T> list) where T : UnitDefinition
            {
                if (list == null) return 0;
                int lostSum = 0;
                foreach (T definition in list)
                {
                    if (definition == null) continue;
                    int lost = tracker.GetLostUnits(definition);
                    lostSum += lost;
                    if (lost <= 0) continue;
                    int at = 0;
                    while (at < lossDefinitions.Count && tracker.GetLostUnits(lossDefinitions[at]) >= lost) at++;
                    if (at >= AttritionLimit) continue;
                    lossDefinitions.Insert(at, definition);
                    if (lossDefinitions.Count > AttritionLimit) lossDefinitions.RemoveAt(AttritionLimit);
                }
                return lostSum;
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
                mandateStrip = page.Add(new MandateStrip(page.Content));
                mandateFlag = mandateStrip.Flag;
                moraleBar = mandateStrip.Bar;
                mandateHeadline = mandateStrip.Headline;
                mandateEffect = mandateStrip.Effect;
                moraleBar.Set(0f, "—", AvState.Inert);
                mandateHeadline.Set("AWAITING HOST");
                mandateEffect.Set("—");
                moraleBar.Help = "Morale: the host's 0-100 mandate. It starts at 50; authored events and contract results move it, " +
                                 "and it scales contract money and XP.";

                dispatchBlock = new BriefBlock(page.Content, "DISPATCH", AvTextRole.Head, AvTextRole.Prose, AvTextRole.ProseSmall);
                directiveBlock = new BriefBlock(page.Content, "DIRECTIVE", AvTextRole.Head, AvTextRole.Prose);
                politicalEvent = dispatchBlock.Lines[0];
                politicalEffect = dispatchBlock.Lines[1];
                politicalBrief = dispatchBlock.Lines[2];
                politicalMission = directiveBlock.Lines[0];
                politicalMissionDetail = directiveBlock.Lines[1];
                politicalEvent.Set("NO ACTIVE EVENT");
                politicalEffect.Set("THEATER CALM");
                politicalBrief.Set("—");
                politicalMission.Set("NO DIRECTIVE");
                politicalMissionDetail.Set("—");
                page.Row(dispatchBlock, directiveBlock);

                infoTabs = page.Add(AvSegmented.Strip(page.Content, new[] { "AIRBASES", "PLAYERS" },
                    () => infoMode == InfoMode.Airbases ? 0 : 1, SelectInfo));
                infoTabs.Options[0].Help = "List the faction's airbases with their operational state and aircraft count.";
                infoTabs.Options[1].Help = "List the players on this faction with their craft and score.";
                directorySummary = page.Add(new TextLine(page.Content, AvTextRole.DataSmall));
                infoGrid = new MfdPagingGrid(page.Content, 1, 12, readOnly: true, rowHeight: 40f);
                AddGrid(page, infoGrid);
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
                moraleBar.Set(known ? Mathf.Clamp01(morale / 100f) : 0f,
                    known ? AvNum.Fixed(morale, 1) + " / 100" : "—",
                    !known ? AvState.Inert : morale >= 50f ? AvState.Ready : AvState.Caution);
                mandateEffect.Set(known ? "CONTRACTS " + AvNum.Signed(percent, 0) + "% MONEY / XP" : "—");
                ModuleServices.TryGet(out IActiveEventsView events);
                ActiveEventView current = events?.Current;
                bool applies = current != null && events.AffectsFaction(hq.faction?.factionName);
                string price = current == null ? "NO EFFECT" :
                    events.PriceSummaryForFaction(hq.faction?.factionName);
                dispatchBlock.Tone(current == null ? AvState.Inert : applies ? AvState.Caution : AvState.Info);
                directiveBlock.Tone(contract == null ? AvState.Inert : AvState.Info);
                politicalEvent.Set(current == null ? "NO ACTIVE EVENT" : current.Title.ToUpperInvariant());
                politicalEffect.Set(current == null ? "CALM" :
                    !current.TargetResolved ? "TARGET LOST" :
                    (applies ? IsLocalFaction(hq) ? "LOCAL EVENT" : "BASE EVENT" : "OTHER SIDE") + " • " + price +
                    " • " + EventClock(current), PriceState(applies, price));
                politicalBrief.Set(current == null ? "—" :
                    !current.TargetResolved ? "—" :
                    current.IsSuper ? current.Target + " • " + NextEventBeat(current) :
                    current.Tier + " / " + current.Target);
                politicalMission.Set(contract == null ?
                    IsLocalFaction(hq) ? ContractsDisabled() ? "DIRECTIVES OFF" :
                        "NO DIRECTIVE" : "CLASSIFIED" :
                    (contract.IsActive ? "ACTIVE / " : "OFFERED / ") +
                    MfdSecondaryObjectives.PlainObjective(contract.Title).ToUpperInvariant());
                politicalMissionDetail.Set(contract == null ? "—" :
                    MfdSecondaryObjectives.PayoutLabel(contract) + "  •  +3 MORALE");
                dispatchBlock.Changed();
                directiveBlock.Changed();
                mandateStrip.Changed();
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

                directorySummary.Set((infoMode == InfoMode.Airbases ? "BASE NETWORK / " : "PERSONNEL / ") +
                    infoRows.Count + (infoMode == InfoMode.Airbases ? " REGISTERED / OPERATIONS BELOW" : " CONNECTED / SCORE ORDER"));
                directorySummary.Changed();
                if (infoRows.Count == 0)
                {
                    infoRows.Add(infoMode == InfoMode.Airbases ? "NO ACTIVE AIRBASES" : "NO ACTIVE PLAYERS");
                    infoSubs.Add("—");
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
                ModuleServices.TryGet(out ISecondaryObjectivesView board) && board?.Status != null &&
                board.Status.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0;

            private static bool IsLocalFaction(FactionHQ hq) =>
                hq != null && GameManager.GetLocalPlayer<Player>(out Player local) &&
                local != null && local.HQ == hq;

            private static SecondaryObjectiveView FeaturedContract(FactionHQ hq)
            {
                if (!IsLocalFaction(hq) || !ModuleServices.TryGet(out ISecondaryObjectivesView board) ||
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

            /// <summary>
            /// Bordered strip with Portal corner brackets. The tone recolours the frame (amber for a live event,
            /// cyan hairline otherwise); derived strips lay their own children out from the slot.
            /// </summary>
            private abstract class FramedPart : AvPart
            {
                protected const float Inset = 10f;
                private readonly AvFrame frame;
                private AvState tone = AvState.Inert;

                protected FramedPart(RectTransform parent, string name)
                {
                    Rect = AvLay.Child(parent, name);
                    frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                    AvLay.Fill(frame.rectTransform);
                    frame.Bracket = 8f;
                }

                protected void SetTone(AvState next)
                {
                    if (next == tone) return;
                    tone = next;
                    PaintFrame();
                }

                private void PaintFrame()
                {
                    AvStyle c = AvStyleHost.FuiStyle("card");
                    Color line = AvStyleHost.Resolve(c.Border, AvTheme.Hairline);
                    Color bracket = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
                    if (tone == AvState.Caution)
                        line = bracket = AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
                    else if (tone == AvState.Danger)
                        line = bracket = AvStyleHost.FuiColor("danger", AvTheme.RailDanger);
                    frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), line);
                    frame.BracketColor = bracket;
                    frame.SetVerticesDirty();
                }

                public override void Restyle() => PaintFrame();
            }

            /// <summary>Bounded, paged unit ledger; spare height reveals more named records, never anonymous tiles.</summary>
            private sealed class UnitRosterPart : FramedPart
            {
                private const int Capacity = 12;
                private const float RowHeight = 34f, HeaderHeight = 58f, FooterHeight = 28f;
                private readonly bool losses;
                private readonly TMP_Text title, summary, columns, pageText, empty;
                private readonly TMP_Text[] names = new TMP_Text[Capacity], live = new TMP_Text[Capacity],
                    lost = new TMP_Text[Capacity], extra = new TMP_Text[Capacity];
                private readonly Image[] icons = new Image[Capacity];
                private readonly RectTransform[] rows = new RectTransform[Capacity];
                private readonly AvStepProgress[] bars = new AvStepProgress[Capacity];
                private readonly AvControl previous, next;
                private int count;
                private Func<int, string> nameAt, helpAt;
                private Func<int, Sprite> iconAt;
                private Func<int, int> liveAt, lostAt, reserveAt;
                private int page, visible = 4;

                public UnitRosterPart(RectTransform parent, bool losses) : base(parent, "UnitRoster")
                {
                    this.losses = losses;
                    title = AvText.Make(Rect, "Title", AvTextRole.Label);
                    summary = AvText.Make(Rect, "Summary", AvTextRole.Micro);
                    columns = AvText.Make(Rect, "Columns", AvTextRole.Micro, losses ? "TYPE                          LIVE       LOST      LOSS %" : "TYPE                          LIVE       LOST       RSV");
                    // Numeric headings get their own aligned cells below; this line labels the name column only.
                    columns.text = "TYPE / DESIGNATION";
                    for (int i = 0; i < Capacity; i++)
                    {
                        rows[i] = AvLay.Child(Rect, "Record " + i);
                        icons[i] = AvLay.Solid(rows[i], "Symbol", AvTheme.Dim);
                        icons[i].preserveAspect = true;
                        names[i] = AvText.Make(rows[i], "Type", AvTextRole.Label);
                        live[i] = AvText.Make(rows[i], "Live", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                        lost[i] = AvText.Make(rows[i], "Lost", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                        extra[i] = AvText.Make(rows[i], "ReserveOrShare", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                        AvText.Fit(names[i], true);
                        AvText.Fit(live[i], false); AvText.Fit(lost[i], false); AvText.Fit(extra[i], false);
                        bars[i] = new AvStepProgress(rows[i], "RecordedLossShare");
                    }
                    empty = AvText.Make(Rect, "Empty", AvTextRole.ProseSmall);
                    previous = AvControl.Make(Rect, new AvControl.Spec("PREV", () => { page--; Render(); }));
                    next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => { page++; Render(); }));
                    pageText = AvText.Make(Rect, "Page", AvTextRole.Micro, "", TextAlignmentOptions.Midline);
                    Restyle();
                }

                public void ResetPage() { page = 0; }

                public void Set(IList<UnitDefinition> entries, FactionHQ hq, string heading, string detail)
                {
                    bool known = hq?.missionStatsTracker != null && Encyclopedia.i != null;
                    SetRecords(entries.Count, i => DefinitionLabel(entries[i]),
                        i => DefinitionTooltip(entries[i], hq), i => entries[i]?.mapIcon,
                        i => known && entries[i] != null ? hq.missionStatsTracker.GetCurrentUnits(entries[i]) : -1,
                        i => known && entries[i] != null ? hq.missionStatsTracker.GetLostUnits(entries[i]) : -1,
                        i => known && (entries[i] is AircraftDefinition || entries[i] is VehicleDefinition)
                            ? SupportedSupply(hq, entries[i]) : -1, heading, detail);
                }

                public void SetRecords(int count, Func<int, string> name, Func<int, string> help,
                    Func<int, Sprite> icon, Func<int, int> current, Func<int, int> lostCount,
                    Func<int, int> reserves, string heading, string detail)
                {
                    this.count = count; nameAt = name; helpAt = help; iconAt = icon;
                    liveAt = current; lostAt = lostCount; reserveAt = reserves;
                    title.text = heading; summary.text = detail;
                    Render();
                }

                private void Render()
                {
                    int pages = Mathf.Max(1, (count + visible - 1) / visible);
                    page = Mathf.Clamp(page, 0, pages - 1);
                    empty.text = summary.text.Contains("UNAVAILABLE") ? "AWAITING MISSION STATISTICS" :
                        losses ? "NO RECORDED UNIT LOSSES" : "NO UNIT TYPES REGISTERED";
                    empty.gameObject.SetActive(count == 0);
                    for (int row = 0; row < Capacity; row++)
                    {
                        int at = page * visible + row;
                        bool shown = row < visible && at < count;
                        rows[row].gameObject.SetActive(shown);
                        if (!shown) continue;
                        int current = liveAt(at), dead = lostAt(at), reserve = reserveAt(at);
                        bool known = current >= 0 && dead >= 0;
                        names[row].text = nameAt(at);
                        live[row].text = known ? AvNum.Fixed(current, 0) : "\u2014";
                        lost[row].text = known ? AvNum.Fixed(dead, 0) : "\u2014";
                        extra[row].text = !known ? "\u2014" : losses
                            ? AvNum.Fixed(current + dead > 0 ? 100f * dead / (current + dead) : 0f, 0) + "%"
                            : reserve >= 0 ? AvNum.Fixed(reserve, 0) : "\u2014";
                        icons[row].sprite = iconAt(at);
                        icons[row].enabled = icons[row].sprite != null;
                        bars[row].SetProgress(Mathf.Max(0, dead), Mathf.Max(1, current + dead));
                        AvHelpTip.Attach(names[row].gameObject, helpAt(at) +
                            ". Strip: lost / (live + lost), recorded mission accounting. RSV: current mobile reserve supply; dash means not applicable.");
                    }
                    previous.Interactable = page > 0;
                    next.Interactable = page + 1 < pages;
                    pageText.text = "PAGE " + (page + 1) + "/" + pages + "  /  " + count + " TYPES";
                }

                public override float Measure(float width) => HeaderHeight + 4f * RowHeight + FooterHeight;

                public override void Place(AvSlot slot)
                {
                    base.Place(slot);
                    float w = slot.W - 2f * Inset;
                    visible = Mathf.Clamp(Mathf.FloorToInt((slot.H - HeaderHeight - FooterHeight) / RowHeight), 4, Capacity);
                    AvLay.Place(title.rectTransform, Inset, 7f, w, 18f);
                    AvLay.Place(summary.rectTransform, Inset, 25f, w, 14f);
                    AvLay.Place(columns.rectTransform, Inset, 41f, w * .58f, 14f);
                    // Keep headings aligned with the numeric columns at every panel width.
                    EnsureHeadings(w);
                    for (int i = 0; i < Capacity; i++)
                    {
                        AvLay.Place(rows[i], Inset, HeaderHeight + i * RowHeight, w, RowHeight);
                        AvLay.Place(icons[i].rectTransform, 0, 3f, 20f, 20f);
                        AvLay.Place(names[i].rectTransform, 25f, 0, w * .56f - 25f, 27f);
                        AvLay.Place(live[i].rectTransform, w * .57f, 0, w * .12f, 27f);
                        AvLay.Place(lost[i].rectTransform, w * .71f, 0, w * .12f, 27f);
                        AvLay.Place(extra[i].rectTransform, w * .85f, 0, w * .15f, 27f);
                        AvLay.Place(bars[i].Rect, 25f, 29f, w - 25f, 2f);
                    }
                    AvLay.Place(empty.rectTransform, Inset, HeaderHeight + 10f, w, 35f);
                    float y = slot.H - FooterHeight;
                    AvLay.Place(previous.Rect, Inset, y, 54f, 22f);
                    AvLay.Place(next.Rect, slot.W - Inset - 54f, y, 54f, 22f);
                    AvLay.Place(pageText.rectTransform, Inset + 58f, y, w - 116f, 22f);
                    Render();
                }

                private TMP_Text liveHeading, lostHeading, extraHeading;
                private void EnsureHeadings(float w)
                {
                    if (liveHeading == null)
                    {
                        liveHeading = AvText.Make(Rect, "LiveHeading", AvTextRole.Micro, "LIVE", TextAlignmentOptions.MidlineRight);
                        lostHeading = AvText.Make(Rect, "LostHeading", AvTextRole.Micro, "LOST", TextAlignmentOptions.MidlineRight);
                        extraHeading = AvText.Make(Rect, "ExtraHeading", AvTextRole.Micro, losses ? "LOSS %" : "RSV", TextAlignmentOptions.MidlineRight);
                        liveHeading.color = extraHeading.color = AvTheme.Dim; lostHeading.color = AvTheme.Warning;
                    }
                    AvLay.Place(liveHeading.rectTransform, Inset + w * .57f, 41f, w * .12f, 14f);
                    AvLay.Place(lostHeading.rectTransform, Inset + w * .71f, 41f, w * .12f, 14f);
                    AvLay.Place(extraHeading.rectTransform, Inset + w * .85f, 41f, w * .15f, 14f);
                }

                public override void Restyle()
                {
                    base.Restyle();
                    if (title == null) return;
                    title.color = AvTheme.RailInfo;
                    summary.color = columns.color = pageText.color = AvTheme.Dim;
                    empty.color = AvTheme.TextPrimary;
                    for (int i = 0; i < Capacity; i++)
                    {
                        names[i].color = live[i].color = AvTheme.TextPrimary;
                        lost[i].color = AvTheme.Warning; extra[i].color = AvTheme.Dim;
                        bars[i].Paint(AvTheme.Warning, AvTheme.Hairline.WithAlpha(.4f));
                    }
                    previous.Restyle(); next.Restyle();
                }
            }

            /// <summary>The local event: title, price effect, countdown and featured contract.</summary>
            private sealed class EventStrip : FramedPart
            {
                public readonly TextLine Headline, Effect, Contract;
                public readonly AvHazardBar Bar;

                public EventStrip(RectTransform parent) : base(parent, "EventStrip")
                {
                    Headline = new TextLine(Rect, AvTextRole.Head, true);
                    Effect = new TextLine(Rect, AvTextRole.Prose);
                    Bar = new AvHazardBar(Rect, "EVENT");
                    Contract = new TextLine(Rect, AvTextRole.ProseSmall);
                    Restyle();
                }

                public void Tone(AvState state) => SetTone(state);

                public override float Measure(float width)
                {
                    float w = width - 2f * Inset;
                    return 8f + Headline.Measure(w) + 2f + Effect.Measure(w) + 5f + Bar.Measure(w) + 5f + Contract.Measure(w) + 8f;
                }

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float w = s.W - 2f * Inset, y = 8f, h = Headline.Measure(w);
                    Headline.Place(new AvSlot(Inset, y, w, h)); y += h + 2f;
                    h = Effect.Measure(w);
                    Effect.Place(new AvSlot(Inset, y, w, h)); y += h + 5f;
                    h = Bar.Measure(w);
                    Bar.Place(new AvSlot(Inset, y, w, h)); y += h + 5f;
                    Contract.Place(new AvSlot(Inset, y, w, Contract.Measure(w)));
                }

                public override void Restyle()
                {
                    base.Restyle();
                    Headline?.Restyle(); Effect?.Restyle(); Bar?.Restyle(); Contract?.Restyle();
                }
            }

            /// <summary>Four simultaneous plots with bounded height; each retains its own units and scale.</summary>
            private sealed class HistoryCard : AvPart
            {
                private const float Gap = 6f;
                public readonly ResourceHistoryTile[] Tiles = new ResourceHistoryTile[4];

                public HistoryCard(RectTransform parent)
                {
                    Rect = AvLay.Child(parent, "ResourceHistory");
                    string[] units = { "CASH", "STOCK", "PERSONNEL", "0–100" };
                    for (int i = 0; i < Tiles.Length; i++)
                        Tiles[i] = new ResourceHistoryTile(Rect, ResourceLabels[i], units[i]) { Parent = this };
                    Clear("NO SAMPLES");
                }

                public string Help
                {
                    set { foreach (ResourceHistoryTile tile in Tiles) tile.Help = value; }
                }

                public void Clear(string reason)
                {
                    foreach (ResourceHistoryTile tile in Tiles)
                        tile.Set(Array.Empty<float>(), 0, "—", "—", "—", reason);
                    Changed();
                }

                public override float Measure(float width)
                {
                    float w = (width - Gap) * .5f;
                    return Mathf.Max(Tiles[0].Measure(w), Tiles[1].Measure(w)) + Gap +
                        Mathf.Max(Tiles[2].Measure(w), Tiles[3].Measure(w));
                }

                public override void Place(AvSlot slot)
                {
                    base.Place(slot);
                    float w = (slot.W - Gap) * .5f, y = 0f;
                    for (int row = 0; row < 2; row++)
                    {
                        float h = Mathf.Max(Tiles[row * 2].Measure(w), Tiles[row * 2 + 1].Measure(w));
                        for (int col = 0; col < 2; col++)
                            Tiles[row * 2 + col].Place(new AvSlot(col * (w + Gap), y, w, h));
                        y += h + Gap;
                    }
                }

                public override void Restyle()
                {
                    foreach (ResourceHistoryTile tile in Tiles) tile.Restyle();
                }
            }

            private sealed class ResourceHistoryTile : FramedPart
            {
                private const float PlotHeight = 72f;
                private readonly TMP_Text title, current, movement, units;
                public readonly AvLineChart Chart;

                public ResourceHistoryTile(RectTransform parent, string name, string unit)
                    : base(parent, "History " + name)
                {
                    title = AvText.Make(Rect, "Title", AvTextRole.Label, name);
                    current = AvText.Make(Rect, "Current", AvTextRole.DataStrong, "—", TextAlignmentOptions.TopRight);
                    units = AvText.Make(Rect, "Units", AvTextRole.Micro, unit + " / OWN SCALE");
                    movement = AvText.Make(Rect, "Movement", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
                    Chart = new AvLineChart(Rect, PlotHeight) { Parent = this };
                    Restyle();
                }

                public string Help { set => AvHelpTip.Attach(Rect.gameObject, value); }

                public void Set(float[] samples, int count, string min, string max, string value, string change)
                {
                    Chart.SetSeries(samples, count, min, max, "");
                    current.text = value;
                    movement.text = change;
                    Changed();
                }

                public override float Measure(float width) =>
                    8f + 18f + 14f + PlotHeight + 4f + Mathf.Max(16f, AvText.Height(movement, width - 2f * Inset)) + 8f;

                public override void Place(AvSlot slot)
                {
                    base.Place(slot);
                    float w = slot.W - 2f * Inset;
                    AvLay.Place(title.rectTransform, Inset, 8f, w * .58f, 18f);
                    AvLay.Place(current.rectTransform, Inset + w * .58f, 8f, w * .42f, 18f);
                    AvLay.Place(units.rectTransform, Inset, 26f, w, 14f);
                    Chart.Place(new AvSlot(Inset, 40f, w, PlotHeight));
                    AvLay.Place(movement.rectTransform, Inset, 44f + PlotHeight, w, Mathf.Max(16f, AvText.Height(movement, w)));
                }

                public override void Restyle()
                {
                    base.Restyle();
                    if (title == null) return;
                    title.color = AvTheme.RailInfo;
                    current.color = AvTheme.TextPrimary;
                    units.color = movement.color = AvTheme.Dim;
                    Chart?.Restyle();
                }
            }

            /// <summary>Roundel, mandate name and effect on one line, the morale bar under them.</summary>
            private sealed class MandateStrip : FramedPart
            {
                private const float FlagSize = 40f;
                public readonly ImageTile Flag;
                public readonly TextLine Headline, Effect;
                public readonly AvHazardBar Bar;

                public MandateStrip(RectTransform parent) : base(parent, "Mandate")
                {
                    Flag = new ImageTile(Rect, FlagSize);
                    Headline = new TextLine(Rect, AvTextRole.Head, true);
                    Effect = new TextLine(Rect, AvTextRole.Prose);
                    Bar = new AvHazardBar(Rect, "MORALE");
                    Restyle();
                }

                private float RowHeight(float w) =>
                    Mathf.Max(FlagSize, Headline.Measure(w - FlagSize - 8f) + 2f + Effect.Measure(w - FlagSize - 8f));

                public override float Measure(float width)
                {
                    float w = width - 2f * Inset;
                    return 8f + RowHeight(w) + 6f + Bar.Measure(w) + 8f;
                }

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float w = s.W - 2f * Inset, tx = Inset + FlagSize + 8f, tw = w - FlagSize - 8f, row = RowHeight(w);
                    Flag.Place(new AvSlot(Inset, 8f, FlagSize, FlagSize));
                    float hh = Headline.Measure(tw), eh = Effect.Measure(tw);
                    float y = 8f + Mathf.Max(0f, (row - hh - 2f - eh) * 0.5f);
                    Headline.Place(new AvSlot(tx, y, tw, hh));
                    Effect.Place(new AvSlot(tx, y + hh + 2f, tw, eh));
                    Bar.Place(new AvSlot(Inset, 8f + row + 6f, w, Bar.Measure(w)));
                }

                public override void Restyle()
                {
                    base.Restyle();
                    Headline?.Restyle(); Effect?.Restyle(); Bar?.Restyle();
                }
            }

            /// <summary>A small bordered briefing: a "// KEY" micro label over a few text lines.</summary>
            private sealed class BriefBlock : FramedPart
            {
                private readonly TMP_Text key;
                public readonly TextLine[] Lines;

                public BriefBlock(RectTransform parent, string keyText, params AvTextRole[] roles)
                    : base(parent, "Brief " + keyText)
                {
                    key = AvText.Make(Rect, "Key", AvTextRole.Micro, "// " + keyText);
                    AvText.Fit(key, false);
                    Lines = new TextLine[roles.Length];
                    for (int i = 0; i < roles.Length; i++) Lines[i] = new TextLine(Rect, roles[i], i == 0);
                    Restyle();
                }

                public void Tone(AvState state) => SetTone(state);

                public override float Measure(float width)
                {
                    float w = width - 2f * Inset, h = 8f + 15f + 3f;
                    foreach (TextLine l in Lines) h += l.Measure(w) + 2f;
                    return h + 6f;
                }

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float w = s.W - 2f * Inset, y = 8f;
                    AvLay.Place(key.rectTransform, Inset, y, w, 15f);
                    y += 18f;
                    foreach (TextLine l in Lines)
                    {
                        float h = l.Measure(w);
                        l.Place(new AvSlot(Inset, y, w, h));
                        y += h + 2f;
                    }
                }

                public override void Restyle()
                {
                    base.Restyle();
                    if (Lines == null) return;
                    key.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-key").Color, AvTheme.RailInfo);
                    foreach (TextLine l in Lines) l.Restyle();
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
                private const float LogoSize = 26f, TextX = 34f;
                private readonly TMP_Text name, subtitle;

                public FactionHeaderPart(RectTransform parent)
                {
                    Rect = AvLay.Child(parent, "FactionHeader");
                    Logo = AvLay.Solid(Rect, "Logo", Color.white);
                    Logo.preserveAspect = true;
                    Logo.enabled = false;
                    name = AvText.Make(Rect, "Name", AvTextRole.Label, "SYNCING FACTION", TextAlignmentOptions.TopLeft, true);
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
                    return Mathf.Max(LogoSize, AvText.Height(name, width - TextX));
                }

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float w = s.W - TextX, nh = AvText.Height(name, w);
                    AvLay.Place(Logo.rectTransform, 0f, 0f, LogoSize, LogoSize);
                    AvLay.Place(name.rectTransform, TextX, 0f, w, nh);
                    subtitle.gameObject.SetActive(false);
                    AvHelpTip.Attach(name.gameObject, subtitle.text);
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
                private const float ChartHeight = 178f;

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
