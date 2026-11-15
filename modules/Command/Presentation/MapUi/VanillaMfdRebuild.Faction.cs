using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
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
            private enum ForcesView { Live, Reserves, Losses, Value, Manpower }
            private enum InfoMode { Airbases, Players }

            private static readonly string[] ResourceLabels = { "FUNDS", "WARHEADS", "MANPOWER", "MORALE" };
            private static readonly string[] ClassLabels = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
            private static readonly string[] ViewLabels = { "LIVE", "RESERVES", "LOSSES", "VALUE", "MANPOWER" };
            private static readonly string[] ViewTitles = { "ORDER OF BATTLE · LIVE / LOST", "RESERVES BY CLASS", "LOSSES BY CLASS", "VALUE BY CLASS", "MANPOWER BY CLASS" };

            private readonly InfoPanel_Faction source;
            private readonly InfoPanel_Faction otherSource;
            private readonly List<UnitDefinition> definitions = new List<UnitDefinition>();
            private readonly List<UnitDefinition> shownDefinitions = new List<UnitDefinition>(48);
            private readonly float[] ledgerFraction = new float[4], lastFraction = new float[4], lastPrimary = new float[4], lastSecondary = new float[4];
            private readonly int[] shownLive = { -2, -2, -2, -2 };
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

            private UnitRosterPart definitionGrid;
            private MfdPagingGrid infoGrid;
            private AvControl[] definitionTabs;
            private AvButtons classBar;
            private ChoiceRow viewTabs;
            private AvSegmented infoTabs;
            private HistoryCard history;
            private MandateStrip mandateStrip;
            private FactionHeaderPart factionHeader;
            private AvGauge[] ledgerRows;
            private AvNote directorySummary;
            private AvHazardBar moraleBar;
            private LedgerChartPart ledgerChart;
            private AvNote mandateHeadline;
            private AvNote mandateEffect;
            private AvSection historySection;
            // NATION layer: shown instead of the morale mandate while the service reports Active (see RefreshNation).
            private FacPolitics politics;
            private FacStockPart stockPart;
            private FacPlantsPart plantsPart;
            private FacForcePart forcePart;
            private AvNote moneyNote;
            private readonly List<AvPart> nationPoliticsParts = new List<AvPart>(2);
            private readonly List<AvPart> nationEconomyParts = new List<AvPart>(8);
            private readonly List<AvPart> mandateParts = new List<AvPart>(3);
            private int selectedFaction;
            private int definitionGroup;
            private bool definitionsLoaded;
            private ForcesView view;
            private bool definitionsDirty = true, ledgerDirty = true, ledgerShowsData, emptyApplied, nationApplied;
            private bool nationOn, nationOnShown;
            private int definitionSignature;
            private float infoNext;
            private MissionStatsTracker sortStats;
            private Comparison<UnitDefinition> definitionOrder;
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

            protected override (AvIcon Icon, string Label)[] TabItems { get; } = new[]
            {
                (AvIcon.Coins, "ECONOMY"),
                (AvIcon.Shield, "FORCES"),
                (AvIcon.Flag, "POLITICS"),
            };

            protected override string[] TabTips { get; } = new[]
            {
                "Economy: the country's funds, production, plants and policies, your donations and convoy purchases, and the histories of funds, warheads, manpower and morale.",
                "Forces: unit types in service by class, live and lost, busiest first. Switch the view to reserves, losses, value or manpower per class.",
                "Politics: war support and why it moved, the doctrine and its ranks, the decisions and the cabinet minutes (the morale mandate while the nation layer is off), and a directory of airbases or players.",
            };

            private const int PageEconomy = 0, PageForces = 1, PagePolitics = 2;

            protected override void BuildContent()
            {
                // The country strip, as on STR, MIS, OPS and EVN. Warheads, manpower and morale of the selected side are
                // on the TREASURY RETURNS chart (ECONOMY) and the mandate bar (POLITICS).
                BoscaliSummer.Core.Ui.CountryStrip.Mount(Console);
                BuildResourcesPage(CreatePage());
                BuildForcesPage(CreatePage());
                BuildStatusPage(CreatePage());
                SelectDefinitions(0);
                Console.Orders(PageEconomy, "Each trace keeps its own scale; a break is a missed observation, never a zero. OPPOSITION shows the last host sync.");
                Console.Orders(PageForces, "Types run busiest first and idle ones are left out. RSV is mobile reserve supply; a dash means not applicable.");
                Console.Orders(PagePolitics, "Decisions cost war support; the first press wins. The mandate is the host's 0-100 morale, and below 50 contracts pay less.");
            }

            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                infoNext = 0f;
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
                    definitionsDirty = ledgerDirty = true;
                    infoNext = 0f;
                    definitionGrid.ResetPage();
                    infoGrid.ResetPage();
                }
                if (hq == null)
                {
                    if (!emptyApplied) ApplyNoHq();
                    return;
                }
                emptyApplied = false;

                string name = hq.faction == null ? "FACTION" : hq.faction.factionName;
                string extended = hq.faction == null ? null : hq.faction.factionExtendedName;
                factionHeader.Set(name ?? "FACTION", (string.IsNullOrEmpty(extended)
                    ? "FACTION STAFF RETURN" : extended.ToUpperInvariant()) +
                    "  ·  SCORE " + AvNum.Fixed(hq.factionScore, 1));
                factionHeader.SetLogo(hq.faction == null ? null : hq.faction.factionColorLogo);

                RefreshNation(hq);
                if (selectedPage == PageEconomy) RefreshResources(hq);
                else if (selectedPage == PageForces)
                {
                    if (view == ForcesView.Live)
                    {
                        SetForceTotals(hq.missionStatsTracker);
                        RefreshDefinitionGrid(hq);
                    }
                    else RefreshLedger(hq);
                }
                else
                {
                    RefreshPolitics(hq);
                    if (Time.unscaledTime >= infoNext) { infoNext = Time.unscaledTime + 1f; RefreshInfo(hq); }
                }
                RefreshSegments();
            }

            /// <summary>No faction HQ yet: every page shows its empty state once, not on every tick.</summary>
            private void ApplyNoHq()
            {
                emptyApplied = true;
                factionHeader.Set("NO RETURN FROM FACTION HQ", "Standing by for the selected faction and its live mission data.");
                factionHeader.SetLogo(null);
                shownDefinitions.Clear();
                definitionGrid.Set(shownDefinitions, null, ClassLabels[definitionGroup], "STATISTICS UNAVAILABLE");
                SetForceTotals(null);
                foreach (AvGauge row in ledgerRows) row.Set(0f, "—", AvState.Inert);
                ledgerChart.Chart.Clear();
                ledgerShowsData = false;
                history.Clear("FACTION HQ UNAVAILABLE");
                historySection.SetCaption("NO RETURN");
                mandateHeadline.Set("REPORT UNAVAILABLE");
                mandateEffect.Set("CONTRACT EFFECT / UNAVAILABLE");
                mandateStrip.SetSeal(null);
                moraleBar.Set(0f, "—", AvState.Inert);
                directorySummary.Set("UNAVAILABLE");
                infoGrid.SetData(0, _ => "", _ => false, null);
                RefreshSegments();
            }

            protected override string AmbientStatus()
            {
                if (observedHq == null) return "STANDING BY FOR FACTION HQ";
                switch (selectedPage)
                {
                    case PageEconomy: return "TREASURY · OBSERVED RETURNS";
                    case PageForces: return ViewTitles[(int)view];
                    default: return nationOn ? "POLITICS · WAR SUPPORT AND THE CABINET" : "THE MANDATE · MORALE 0-100";
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
                for (int i = 0; i < definitionTabs.Length; i++) definitionTabs[i].Latched = i == definitionGroup;
                viewTabs.Refresh();
                infoTabs.Refresh();
            }

            // ---------------------------------------------------------- ECONOMY page

            // What STR SUPPLY held (panels fold phase 3): the STR hub's own parts, bound here from the hub's model with FAC's own control ids.
            // The nation rows hide with the layer (one dim line instead); DONATE and BUY hide only while STR is off. The opposition shows none.
            private readonly List<StrPart> supplyParts = new List<StrPart>(12), nationSupplyParts = new List<StrPart>(6);
            private readonly StrControls supplyControls = new StrControls();
            private IStrHub supplyHub;
            private StrViewModel supplyBound;
            private int supplyRevision = -1, supplyKey;
            private bool supplyNation;
            private AvSection donateSection, convoySection;
            private AvNote offNote;

            private void AddSupply(AvFlow page, StrPart part, bool nation)
            {
                page.Add(part);
                part.SetShown(false);
                (nation ? nationSupplyParts : supplyParts).Add(part);
            }

            private void BuildResourcesPage(AvFlow page)
            {
                BuildFactionSelector(page);
                supplyHub = Runtime.StrViewHub.Active;

                // NATION blocks sit above the treasury chart and stay out of the flow until the layer is Active.
                nationEconomyParts.Add(page.Section(AvIcon.Coins, "FUNDS", "WHAT THE COUNTRY HAS TO SPEND"));
                moneyNote = page.Add(new AvNote(page.Content, AvTextRole.DataSmall, true));
                nationEconomyParts.Add(moneyNote);
                nationEconomyParts.Add(page.Section(AvIcon.BuildingFactory, "PRODUCTION", "THE STAFF RUNS THE PLANTS · FAVOUR ONE ARM"));
                AddSupply(page, new StrNationReply(page.Content, supplyControls, supplyHub), true);
                for (int i = 0; i < StrNationView.MaxLines; i++) AddSupply(page, new StrLineRowPart(page.Content, supplyControls, supplyHub, i), true);
                nationEconomyParts.Add(page.Section(AvIcon.Tank, "STOCK", "WHAT THE FACTION CAN FIELD NOW"));
                stockPart = page.Add(new FacStockPart(page.Content));
                nationEconomyParts.Add(stockPart);
                nationEconomyParts.Add(page.Section(AvIcon.BuildingFactory, "PLANTS", "FACTORIES AND FREE SLOTS"));
                plantsPart = page.Add(new FacPlantsPart(page.Content));
                nationEconomyParts.Add(plantsPart);
                AddSupply(page, new StrConstructionRow(page.Content, supplyControls, supplyHub), true);
                AddSupply(page, new StrBuildRow(page.Content, supplyControls, supplyHub), true);
                nationEconomyParts.Add(page.Section(AvIcon.Scale, "POLICIES", "HOW HARD THE COUNTRY WORKS"));
                AddSupply(page, new StrPolicyRow(page.Content, supplyControls, supplyHub, StrPolicyRow.Kind.Economy), true);
                nationEconomyParts.Add(page.Section(AvIcon.ListDetails, "IN FORCE", "POLICIES AND OUTPUT"));
                forcePart = page.Add(new FacForcePart(page.Content));
                nationEconomyParts.Add(forcePart);
                foreach (AvPart p in nationEconomyParts) p.SetShown(false);
                offNote = page.Add(new AvNote(page.Content, ""));

                donateSection = page.Section(AvIcon.Coins, "HELP THE COUNTRY", "FROM YOUR ALLOCATION · ONE DONATION A MINUTE");
                AddSupply(page, new StrDonateRow(page.Content, supplyControls, supplyHub), false);
                convoySection = page.Section(AvIcon.Shield, "CONVOYS", "BOUGHT FROM YOUR ALLOCATION · A DEPOT DELIVERS");
                for (int i = 0; i < StrViewModel.MaxConvoys; i++) AddSupply(page, new StrConvoyRow(page.Content, supplyControls, supplyHub, i), false);

                historySection = page.Section(AvIcon.ChartLine, "TREASURY RETURNS", "NO OBSERVATIONS");
                history = page.Add(new HistoryCard(page.Content), 1f);
                history.Help = "Independent resource scales. One observation every " +
                    AvNum.Seconds(MfdResourceHistory.Interval, 0) + "; up to " +
                    AvNum.Seconds(MfdResourceHistory.Capacity * MfdResourceHistory.Interval, 0) +
                    " retained. Missing observations break each resource trace; no backfill.";
            }
            private void RefreshResources(FactionHQ hq)
            {
                if (resourceHistory == null) resourceHistory = FactionResourceHistoryStore.For(hq);
                RefreshResourceChart();
                RefreshSupply(hq);
            }

            /// <summary>Binds the folded STR parts when the hub built something new, the nation moved or the layer turned; the same key STR uses.</summary>
            private void RefreshSupply(FactionHQ hq)
            {
                Runtime.StrViewHub live = Runtime.StrViewHub.Active;
                bool own = selectedFaction == 0;
                bool str = own && live != null && ReferenceEquals(live, supplyHub) && live.Current.Built;
                int key = StrNation.Refresh();
                bool nation = str && IsLocalFaction(hq) && StrNation.Active;
                // One line for whichever layer is off (the opposition shows none).
                bool off = own && (!str || !StrNation.Active);
                offNote.SetShown(off);
                if (off) offNote.Set(!str ? "STRATEGIC COMMAND OFF" : "NATION OFF");
                donateSection.SetShown(str);
                if (!nation) foreach (StrPart p in nationSupplyParts) p.SetShown(false);
                if (!str)
                {
                    foreach (StrPart p in supplyParts) p.SetShown(false);
                    convoySection.SetShown(false);
                    supplyBound = null;
                    return;
                }
                StrViewModel vm = live.Current;
                convoySection.SetShown(vm.Supply.RowCount > 0);
                if (ReferenceEquals(vm, supplyBound) && live.Revision == supplyRevision && key == supplyKey && nation == supplyNation) return;
                supplyBound = vm; supplyRevision = live.Revision; supplyKey = key; supplyNation = nation;
                supplyControls.Clear();
                StrNation.Fill();
                foreach (StrPart p in supplyParts) { p.SetShown(true); p.Bind(vm); }
                if (nation) foreach (StrPart p in nationSupplyParts) { p.SetShown(true); p.Bind(vm); }
            }

            /// <summary>Each resource gets its own scale and trailing finite run; gaps never bridge.</summary>
            private void RefreshResourceChart()
            {
                int count = resourceHistory != null ? resourceHistory.Count : 0;
                float latest = count > 0 ? resourceHistory.Time(count - 1) : float.NaN;
                if (count == renderedResourceCount && (count == 0 || latest == renderedResourceTime)) return;
                renderedResourceCount = count;
                renderedResourceTime = latest;
                float onFile = count > 0 ? latest - resourceHistory.Time(0) : 0f;
                historySection.SetCaption(count == 0 || !float.IsFinite(onFile)
                    ? "NO OBSERVATIONS" : AvNum.Clock(Mathf.Max(0f, onFile)) + " ON FILE");

                for (int series = 0; series < ResourceLabels.Length; series++)
                {
                    int run = resourceHistory.FiniteRun(series, resourceBuffer, out int runStart, out float lo, out float hi);
                    ResourceHistoryTile tile = history.Tiles[series];
                    string current = run > 0 ? FormatResource(series, resourceBuffer[run - 1]) : "—";
                    if (run < 2)
                    {
                        tile.Set(resourceBuffer, 0, "—", "—", current,
                            count == 0 ? "NO SAMPLES" : run == 0 ? "UNAVAILABLE" : "NEEDS 2 SAMPLES");
                        continue;
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

            // ----------------------------------------------------------- FORCES page

            private void BuildForcesPage(AvFlow page)
            {
                BuildFactionSelector(page);
                factionHeader = page.Add(new FactionHeaderPart(page.Content));

                viewTabs = new ChoiceRow(page, ViewLabels, () => (int)view, SelectView);
                string[] viewTips =
                {
                    "Live: every unit type of the chosen class with its live and lost counts.",
                    "Reserves: what is still alive against what is stockpiled and can be brought in, per class.",
                    "Losses: units destroyed so far, per class.",
                    "Value: the credit worth of what is alive, with the worth already lost beneath it.",
                    "Manpower: personnel alive, with those already lost beneath.",
                };
                for (int i = 0; i < viewTips.Length; i++) viewTabs[i].Help = viewTips[i];

                var classSpecs = new AvControl.Spec[ClassLabels.Length];
                for (int i = 0; i < classSpecs.Length; i++)
                {
                    int index = i;
                    classSpecs[i] = new AvControl.Spec(ClassLabels[i], () => SelectDefinitions(index), AvButtonStyle.Tab);
                }
                classBar = page.Buttons(classSpecs);
                definitionTabs = classBar.Controls;
                for (int i = 0; i < ClassLabels.Length; i++)
                    definitionTabs[i].Help = "List every " + ClassLabels[i].ToLowerInvariant().TrimEnd('s') + " type with its live and lost counts, busiest first.";

                definitionGrid = page.Add(new UnitRosterPart(page.Content));

                ledgerRows = new AvGauge[ClassLabels.Length];
                for (int i = 0; i < ledgerRows.Length; i++)
                {
                    ledgerRows[i] = new AvGauge(page.Content, ClassLabels[i]);
                    ledgerRows[i].Help = ClassLabels[i] + " in the chosen view, as a share of everything that class ever fielded. " +
                                         "The bars below give the exact figures.";
                }
                page.Row(ledgerRows[0], ledgerRows[1], ledgerRows[2], ledgerRows[3]);
                ledgerChart = page.Add(new LedgerChartPart(page.Content, page.Inner));
                ApplyView();
            }

            private void SelectView(int selected)
            {
                view = (ForcesView)Mathf.Clamp(selected, 0, 4);
                ledgerDirty = true;
                ApplyView();
                RequestRefresh();
            }

            /// <summary>LIVE shows the class tabs and roster; every other view shows the four class gauges and the bar chart.</summary>
            private void ApplyView()
            {
                bool live = view == ForcesView.Live;
                classBar.SetShown(live);
                definitionGrid.SetShown(live);
                foreach (AvGauge g in ledgerRows) g.SetShown(!live);
                ledgerChart.SetShown(!live);
            }

            private void SetForceTotals(MissionStatsTracker tracker)
            {
                if (definitionTabs == null) return;
                MissionStatsTracker.TypeStat stats = tracker == null ? default : tracker.units;
                for (int i = 0; i < ClassLabels.Length; i++)
                {
                    int n = tracker == null ? -1 :
                        Mathf.RoundToInt(i == 0 ? stats.buildings.current : i == 1 ? stats.vehicles.current : i == 2 ? stats.ships.current : stats.aircraft.current);
                    if (n == shownLive[i]) continue;
                    shownLive[i] = n;
                    definitionTabs[i].Label = ClassLabels[i] + "\n" + (n < 0 ? "—" : AvNum.Fixed(n, 0)) + " LIVE";
                }
            }

            /// <summary>Types with something live or lost, busiest first; rebuilt only when a count moved.</summary>
            private void RefreshDefinitionGrid(FactionHQ hq)
            {
                if (!definitionsLoaded) PopulateDefinitions();
                MissionStatsTracker stats = hq?.missionStatsTracker;
                int signature = definitions.Count;
                if (stats != null)
                    unchecked
                    {
                        for (int i = 0; i < definitions.Count; i++)
                            if (definitions[i] != null) signature = signature * 31 + stats.GetCurrentUnits(definitions[i]) * 7 + stats.GetLostUnits(definitions[i]);
                    }
                if (!definitionsDirty && signature == definitionSignature) return;
                definitionsDirty = false;
                definitionSignature = signature;

                shownDefinitions.Clear();
                int live = 0;
                if (stats != null)
                {
                    for (int i = 0; i < definitions.Count; i++)
                    {
                        UnitDefinition d = definitions[i];
                        if (d == null) continue;
                        int current = stats.GetCurrentUnits(d);
                        live += current;
                        if (current > 0 || stats.GetLostUnits(d) > 0) shownDefinitions.Add(d);
                    }
                    sortStats = stats;
                    shownDefinitions.Sort(definitionOrder ??= CompareDefinitions);
                }
                definitionGrid.Set(shownDefinitions, hq, ClassLabels[definitionGroup] + " · LIVE / LOST",
                    stats == null ? "STATISTICS UNAVAILABLE" : shownDefinitions.Count + " TYPES / " + live + " LIVE");
            }

            private int CompareDefinitions(UnitDefinition a, UnitDefinition b)
            {
                int byLive = sortStats.GetCurrentUnits(b).CompareTo(sortStats.GetCurrentUnits(a));
                if (byLive != 0) return byLive;
                int byLost = sortStats.GetLostUnits(b).CompareTo(sortStats.GetLostUnits(a));
                if (byLost != 0) return byLost;
                return string.Compare(a.unitName ?? a.code, b.unitName ?? b.code, StringComparison.OrdinalIgnoreCase);
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
                return (full ?? "UNIT").ToUpperInvariant() + " · " + DefinitionDetail(definition, hq);
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
                definitionsDirty = true;
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

                IReadOnlyList<UnitDefinition> group = ClassGroup(encyclopedia, definitionGroup);
                if (group == null) return;
                for (int i = 0; i < group.Count; i++) definitions.Add(group[i]);
                definitionsLoaded = true;
            }

            /// <summary>The encyclopedia's list for one class, in <c>ClassLabels</c> order (BUILDINGS, VEHICLES, SHIPS, AIRCRAFT).</summary>
            private static IReadOnlyList<UnitDefinition> ClassGroup(Encyclopedia encyclopedia, int group) =>
                group == 0 ? encyclopedia.buildings : group == 1 ? encyclopedia.vehicles : group == 2 ? encyclopedia.ships :
                (IReadOnlyList<UnitDefinition>)encyclopedia.aircraft;

            // ------------------------------------------------- FORCES page: the other views

            /// <summary>Gauges and chart for the chosen view; written only when a figure moved.</summary>
            private void RefreshLedger(FactionHQ hq)
            {
                if (ledgerRows == null) return;
                if (hq.missionStatsTracker == null)
                {
                    if (ledgerShowsData || ledgerDirty)
                    {
                        for (int i = 0; i < ledgerRows.Length; i++) ledgerRows[i].Set(0f, "—", AvState.Inert);
                        ledgerChart.Chart.Clear();
                    }
                    ledgerShowsData = ledgerDirty = false;
                    return;
                }
                MissionStatsTracker.TypeStat category = LedgerCategory(hq.missionStatsTracker);
                MissionStatsTracker.Stat[] values =
                {
                    category.buildings, category.vehicles, category.ships, category.aircraft,
                };
                bool reserves = view == ForcesView.Reserves, losses = view == ForcesView.Losses;
                bool same = ledgerShowsData && !ledgerDirty;
                for (int i = 0; i < ledgerRows.Length; i++)
                {
                    MissionStatsTracker.Stat stat = values[i];
                    float value = reserves ? ReserveCount(hq, i) : LedgerValue(stat);
                    float denominator = reserves ? Mathf.Max(1f, value + stat.current) : Mathf.Max(1f, stat.total);
                    chartPrimary[i] = value;
                    chartSecondary[i] = reserves || losses ? stat.current : stat.lost;
                    ledgerFraction[i] = Mathf.Clamp01(losses ? stat.lost / denominator : value / denominator);
                    if (chartPrimary[i] != lastPrimary[i] || chartSecondary[i] != lastSecondary[i] || ledgerFraction[i] != lastFraction[i]) same = false;
                }
                if (same) return;
                ledgerShowsData = true;
                ledgerDirty = false;
                for (int i = 0; i < ledgerRows.Length; i++)
                {
                    lastPrimary[i] = chartPrimary[i]; lastSecondary[i] = chartSecondary[i]; lastFraction[i] = ledgerFraction[i];
                    ledgerRows[i].Set(ledgerFraction[i], FormatLedger(chartPrimary[i]), losses ? AvState.Caution : AvState.Ready);
                }
                ledgerChart.Chart.Set(chartPrimary, chartSecondary, ViewLabels[(int)view],
                    reserves || losses ? "CURRENT" : "LOST", view == ForcesView.Value ? "CR" : view == ForcesView.Manpower ? "PAX" : "UNIT", FormatLedger);
            }

            private MissionStatsTracker.TypeStat LedgerCategory(MissionStatsTracker tracker)
            {
                switch (view)
                {
                    case ForcesView.Losses:
                    case ForcesView.Reserves:
                        return tracker.units;
                    case ForcesView.Value:
                        return tracker.value;
                    default:
                        return tracker.manpower;
                }
            }

            private float LedgerValue(MissionStatsTracker.Stat stat) =>
                view == ForcesView.Losses ? stat.lost : stat.current;

            private string FormatLedger(float value)
            {
                return view == ForcesView.Value ? AvNum.Money(value) : AvNum.Fixed(value, 0);
            }

            private static int ReserveCount(FactionHQ hq, int group)
            {
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (hq == null || encyclopedia == null) return 0;

                int total = 0;
                IReadOnlyList<UnitDefinition> list = ClassGroup(encyclopedia, group);
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        total += SupportedSupply(hq, list[i]);
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
                politics = new FacPolitics(page, nationPoliticsParts);
                foreach (AvPart p in nationPoliticsParts) p.SetShown(false);
                mandateStrip = page.Add(new MandateStrip(page.Content));
                mandateParts.Add(mandateStrip);
                moraleBar = mandateStrip.Bar;
                mandateHeadline = mandateStrip.Headline;
                mandateEffect = mandateStrip.Effect;
                moraleBar.Set(0f, "—", AvState.Inert);
                mandateHeadline.Set("AWAITING HOST");
                mandateEffect.Set("—");
                moraleBar.Help = "Morale: the host's 0-100 mandate. It starts at 50; authored events and contract results move it, " +
                                 "and it scales contract money and XP.";

                infoTabs = page.Add(AvSegmented.Strip(page.Content, new[] { "AIRBASES", "PLAYERS" },
                    () => infoMode == InfoMode.Airbases ? 0 : 1, SelectInfo));
                infoTabs.Options[0].Help = "List the faction's airbases with their operational state and aircraft count.";
                infoTabs.Options[1].Help = "List the players on this faction with their craft and score.";
                directorySummary = page.Add(new AvNote(page.Content, AvTextRole.DataSmall));
                infoGrid = new MfdPagingGrid(page.Content, 1, 12, readOnly: true, rowHeight: 40f);
                AddGrid(page, infoGrid);

            }

            private static readonly string[] BandNames = { "MOBILIZED HOME FRONT", "STEADFAST MANDATE", "WAR WEARY", "CABINET IN CRISIS" };

            private static int BandOf(float morale) => morale >= 80f ? 0 : morale >= 50f ? 1 : morale >= 25f ? 2 : 3;

            private void SelectInfo(int selected)
            {
                // Keep this local. InfoPanel_Faction.SetDisplayAirbases currently selects
                // Players in stock code, exactly the bug this owned view avoids inheriting.
                infoMode = selected == 0 ? InfoMode.Airbases : InfoMode.Players;
                infoNext = 0f;
                infoGrid.ResetPage();
                RequestRefresh();
            }

            private void RefreshPolitics(FactionHQ hq)
            {
                float morale = FactionResourceHistoryStore.Morale(hq);
                Sprite seal = hq.faction == null ? null : hq.faction.factionColorLogo;
                mandateStrip.SetSeal(seal);
                bool known = float.IsFinite(morale);
                mandateHeadline.Set(known ? MandateName(morale) : "REPORT UNAVAILABLE",
                    !known ? AvState.Inert : morale >= 50f ? AvState.Ready : AvState.Caution);
                int percent = known ? Mathf.RoundToInt((FactionMoraleState.ContractMultiplier(morale) - 1f) * 100f) : 0;
                moraleBar.Set(known ? Mathf.Clamp01(morale / 100f) : 0f,
                    known ? AvNum.Fixed(morale, 1) + " / 100" : "—",
                    !known ? AvState.Inert : morale >= 50f ? AvState.Ready : AvState.Caution);
                mandateEffect.Set(known ? "CONTRACTS " + AvNum.Signed(percent, 0) + "% MONEY / XP" : "—");
                mandateStrip.Changed();
            }

            // The NATION layer takes the ECONOMY and POLITICS pages for the player's own faction while it runs; otherwise both read as before.
            // Nation installs after Command, so the service is looked up on every refresh rather than cached at build.
            private void RefreshNation(FactionHQ hq)
            {
                INationView nation = null;
                bool on = selectedFaction == 0 && IsLocalFaction(hq) && ModuleServices.TryGet(out nation) && nation.Active && nation.Local != null;
                nationOn = on;
                if (!nationApplied || on != nationOnShown)
                {
                    nationApplied = true; nationOnShown = on;
                    foreach (AvPart p in nationEconomyParts) p.SetShown(on);
                    foreach (AvPart p in nationPoliticsParts) p.SetShown(on);
                    foreach (AvPart p in mandateParts) p.SetShown(!on);
                }
                if (!on) return;
                NationView v = nation.Local;
                if (selectedPage == PageEconomy)
                {
                    moneyNote.Set("FUNDS " + AvNum.Thousands(Mathf.Round(v.Funds)) + "   ·   NEW FACTORY " +
                                  AvNum.Thousands(Mathf.Round(v.FactoryCost)) + "   ·   OUTPUT x" + AvNum.Fixed(v.Output, 2));
                    stockPart.Set(v);
                    plantsPart.Set(v);
                    forcePart.Set(v);
                }
                else if (selectedPage == PagePolitics) politics.Set(nation);
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

                directorySummary.Set(infoRows.Count + (infoMode == InfoMode.Airbases ? " AIRBASES" : " PLAYERS · SCORE ORDER"));
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

            private static string MandateName(float morale) => BandNames[BandOf(morale)];

            private static bool IsLocalFaction(FactionHQ hq) =>
                hq != null && GameManager.GetLocalPlayer<Player>(out Player local) &&
                local != null && local.HQ == hq;

            // ------------------------------------------------------------ local parts

            /// <summary>
            /// Bordered strip with Portal corner brackets; derived strips lay their own children out from the slot.
            /// </summary>
            private abstract class FramedPart : AvPart
            {
                protected const float Inset = 10f;
                private readonly AvFrame frame;

                protected FramedPart(RectTransform parent, string name)
                {
                    Rect = AvLay.Child(parent, name);
                    frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                    AvLay.Fill(frame.rectTransform);
                    frame.Bracket = 8f;
                }

                public override void Restyle()
                {
                    AvStyle c = AvStyleHost.FuiStyle("card");
                    frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                    frame.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
                    frame.SetVerticesDirty();
                }
            }

            /// <summary>
            /// Bounded, paged unit table; spare height reveals more named records, never anonymous tiles. Lines
            /// the records do not fill stay as empty ruled rows, and a total line closes the table.
            /// </summary>
            private sealed class UnitRosterPart : FramedPart
            {
                private const int Capacity = 20;
                private const float RowHeight = 28f, HeaderHeight = 58f, TotalHeight = 26f, FooterHeight = 28f;
                private readonly TMP_Text title, summary, columns, pageText, empty, totalName, totalLive, totalLost, totalExtra;
                private readonly Image totalRule;
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

                public UnitRosterPart(RectTransform parent) : base(parent, "UnitRoster")
                {
                    title = AvText.Make(Rect, "Title", AvTextRole.Label);
                    summary = AvText.Make(Rect, "Summary", AvTextRole.Micro);
                    // Numeric headings get their own aligned cells below; this line labels the name column only.
                    columns = AvText.Make(Rect, "Columns", AvTextRole.Micro, "TYPE / DESIGNATION");
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
                    totalRule = AvLay.Solid(Rect, "TotalRule", AvTheme.Hairline);
                    totalName = AvText.Make(Rect, "TotalName", AvTextRole.Label, "CLASS TOTAL");
                    totalLive = AvText.Make(Rect, "TotalLive", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                    totalLost = AvText.Make(Rect, "TotalLost", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                    totalExtra = AvText.Make(Rect, "TotalExtra", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                    AvText.Fit(totalName, false); AvText.Fit(totalLive, false); AvText.Fit(totalLost, false); AvText.Fit(totalExtra, false);
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
                    bool resized = this.count != count;
                    this.count = count; nameAt = name; helpAt = help; iconAt = icon;
                    liveAt = current; lostAt = lostCount; reserveAt = reserves;
                    title.text = heading; summary.text = detail;
                    Render();
                    if (resized) Changed();
                }

                private void Render()
                {
                    int pages = Mathf.Max(1, (count + visible - 1) / visible);
                    page = Mathf.Clamp(page, 0, pages - 1);
                    empty.text = summary.text.Contains("UNAVAILABLE") ? "AWAITING MISSION STATISTICS" : "NONE FIELDED OR LOST";
                    empty.gameObject.SetActive(count == 0);
                    for (int row = 0; row < Capacity; row++)
                    {
                        int at = page * visible + row;
                        bool shown = row < visible && at < count;
                        rows[row].gameObject.SetActive(shown);
                        if (!shown)
                        {
                            // An unfilled line of the form: the rule only, nothing that could pass for a record.
                            names[row].text = live[row].text = lost[row].text = extra[row].text = "";
                            icons[row].enabled = false;
                            bars[row].SetProgress(0, 1);
                            AvHelpTip.Attach(names[row].gameObject, "");
                            continue;
                        }
                        int current = liveAt(at), dead = lostAt(at), reserve = reserveAt(at);
                        bool known = current >= 0 && dead >= 0;
                        names[row].text = nameAt(at);
                        live[row].text = known ? AvNum.Fixed(current, 0) : "\u2014";
                        lost[row].text = known ? AvNum.Fixed(dead, 0) : "\u2014";
                        extra[row].text = known && reserve >= 0 ? AvNum.Fixed(reserve, 0) : "\u2014";
                        icons[row].sprite = iconAt(at);
                        icons[row].enabled = icons[row].sprite != null;
                        bars[row].SetProgress(Mathf.Max(0, dead), Mathf.Max(1, current + dead));
                        AvHelpTip.Attach(names[row].gameObject, helpAt(at) +
                            ". Strip: lost / (live + lost), recorded mission accounting. RSV: current mobile reserve supply; dash means not applicable.");
                    }
                    RenderTotals();
                    previous.Interactable = page > 0;
                    next.Interactable = page + 1 < pages;
                    pageText.text = "PAGE " + (page + 1) + "/" + pages + " · " + count + " TYPES";
                }

                // Sums over every record of the form, not just the page on show; a type with no figure adds nothing.
                private void RenderTotals()
                {
                    int sumLive = 0, sumLost = 0, sumReserve = 0, known = 0, reserveKnown = 0;
                    for (int at = 0; at < count; at++)
                    {
                        int current = liveAt(at), dead = lostAt(at), reserve = reserveAt(at);
                        if (current < 0 || dead < 0) continue;
                        known++; sumLive += current; sumLost += dead;
                        if (reserve >= 0) { reserveKnown++; sumReserve += reserve; }
                    }
                    bool any = known > 0;
                    totalLive.text = any ? AvNum.Fixed(sumLive, 0) : "\u2014";
                    totalLost.text = any ? AvNum.Fixed(sumLost, 0) : "\u2014";
                    totalExtra.text = any && reserveKnown > 0 ? AvNum.Fixed(sumReserve, 0) : "\u2014";
                    bool shownTotals = count > 0;
                    totalRule.gameObject.SetActive(shownTotals); totalName.gameObject.SetActive(shownTotals);
                    totalLive.gameObject.SetActive(shownTotals); totalLost.gameObject.SetActive(shownTotals);
                    totalExtra.gameObject.SetActive(shownTotals);
                }

                // As tall as its records (four lines at least): no page of empty rules under a short list.
                public override float Measure(float width) => HeaderHeight + Mathf.Clamp(count, 4, Capacity) * RowHeight + TotalHeight + FooterHeight;

                public override void Place(AvSlot slot)
                {
                    base.Place(slot);
                    float w = slot.W - 2f * Inset;
                    float room = slot.H - HeaderHeight - TotalHeight - FooterHeight;
                    visible = Mathf.Clamp(Mathf.FloorToInt(room / RowHeight), 4, Capacity);
                    float pitch = Mathf.Clamp(room / visible, RowHeight, RowHeight * 1.4f);   // the remainder is spread, not left as a gap
                    AvLay.Place(title.rectTransform, Inset, 7f, w, 18f);
                    AvLay.Place(summary.rectTransform, Inset, 25f, w, 14f);
                    AvLay.Place(columns.rectTransform, Inset, 41f, w * .58f, 14f);
                    // Keep headings aligned with the numeric columns at every panel width.
                    EnsureHeadings(w);
                    for (int i = 0; i < Capacity; i++)
                    {
                        float text = pitch - 3f;
                        AvLay.Place(rows[i], Inset, HeaderHeight + i * pitch, w, pitch);
                        AvLay.Place(icons[i].rectTransform, 0, (text - 18f) * .5f, 18f, 18f);
                        AvLay.Place(names[i].rectTransform, 25f, 0, w * .56f - 25f, text);
                        AvLay.Place(live[i].rectTransform, w * .57f, 0, w * .12f, text);
                        AvLay.Place(lost[i].rectTransform, w * .71f, 0, w * .12f, text);
                        AvLay.Place(extra[i].rectTransform, w * .85f, 0, w * .15f, text);
                        AvLay.Place(bars[i].Rect, 25f, pitch - 2f, w - 25f, 2f);
                    }
                    AvLay.Place(empty.rectTransform, Inset + 25f, HeaderHeight, w - 25f, pitch - 3f);
                    float ty = HeaderHeight + visible * pitch + 2f;
                    AvLay.Place(totalRule.rectTransform, Inset, ty, w, 1f);
                    AvLay.Place(totalName.rectTransform, Inset + 25f, ty + 2f, w * .56f - 25f, TotalHeight - 4f);
                    AvLay.Place(totalLive.rectTransform, Inset + w * .57f, ty + 2f, w * .12f, TotalHeight - 4f);
                    AvLay.Place(totalLost.rectTransform, Inset + w * .71f, ty + 2f, w * .12f, TotalHeight - 4f);
                    AvLay.Place(totalExtra.rectTransform, Inset + w * .85f, ty + 2f, w * .15f, TotalHeight - 4f);
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
                        extraHeading = AvText.Make(Rect, "ExtraHeading", AvTextRole.Micro, "RSV", TextAlignmentOptions.MidlineRight);
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
                    totalRule.color = AvTheme.Hairline;
                    totalName.color = totalLive.color = AvTheme.TextPrimary;
                    totalLost.color = AvTheme.Warning; totalExtra.color = AvTheme.Dim;
                    for (int i = 0; i < Capacity; i++)
                    {
                        names[i].color = live[i].color = AvTheme.TextPrimary;
                        lost[i].color = AvTheme.Warning; extra[i].color = AvTheme.Dim;
                        bars[i].Paint(AvTheme.Warning, AvTheme.Hairline.WithAlpha(.4f));
                    }
                    previous.Restyle(); next.Restyle();
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
                    float spare = Mathf.Max(0f, slot.H - Measure(slot.W)) * .5f;   // the page's leftover height deepens both rows
                    for (int row = 0; row < 2; row++)
                    {
                        float h = Mathf.Max(Tiles[row * 2].Measure(w), Tiles[row * 2 + 1].Measure(w)) + spare;
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
                    float note = Mathf.Max(16f, AvText.Height(movement, w));
                    float plot = Mathf.Max(PlotHeight, slot.H - 40f - 4f - note - 8f);
                    Chart.Place(new AvSlot(Inset, 40f, w, plot));
                    AvLay.Place(movement.rectTransform, Inset, 44f + plot, w, note);
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
                public readonly AvNote Headline, Effect;
                public readonly AvHazardBar Bar;

                public MandateStrip(RectTransform parent) : base(parent, "Mandate")
                {
                    Flag = new ImageTile(Rect, FlagSize);
                    Headline = new AvNote(Rect, AvTextRole.Head, true);
                    Effect = new AvNote(Rect, AvTextRole.Prose);
                    Bar = new AvHazardBar(Rect, "MORALE");
                    Restyle();
                }

                private float FlagSpace => Flag.Image.enabled ? FlagSize + 8f : 0f;
                private AvSlot lastSlot;
                private bool placed;

                /// <summary>The faction roundel; no roundel, no blank column. Its arrival moves the text, so the strip re-places itself.</summary>
                public void SetSeal(Sprite sprite)
                {
                    bool on = sprite != null;
                    Flag.Image.sprite = sprite;
                    if (Flag.Image.enabled == on) return;
                    Flag.Image.enabled = on;
                    if (placed) Place(lastSlot);
                    Changed();
                }

                private float RowHeight(float w) =>
                    Mathf.Max(Flag.Image.enabled ? FlagSize : 0f, Headline.Measure(w - FlagSpace) + 2f + Effect.Measure(w - FlagSpace));

                public override float Measure(float width)
                {
                    float w = width - 2f * Inset;
                    return 8f + RowHeight(w) + 6f + Bar.Measure(w) + 8f;
                }

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    lastSlot = s; placed = true;
                    float w = s.W - 2f * Inset, tx = Inset + FlagSpace, tw = w - FlagSpace, row = RowHeight(w);
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

                private AvSlot lastSlot;
                private bool placed;

                /// <summary>The faction roundel; its arrival or loss moves the text, which the flow would not notice by height alone.</summary>
                public void SetLogo(Sprite sprite)
                {
                    bool on = sprite != null;
                    Logo.sprite = sprite;
                    if (Logo.enabled == on) return;
                    Logo.enabled = on;
                    if (placed) Place(lastSlot);
                    Changed();
                }

                public void Set(string factionName, string extended)
                {
                    string n = factionName ?? "", s = extended ?? "";
                    if (name.text != n) { name.text = n; Changed(); }
                    if (subtitle.text != s) { subtitle.text = s; Changed(); }
                }

                private float Indent => Logo.enabled ? TextX : 0f;

                public override float Measure(float width)
                {
                    float w = width - Indent;
                    return Mathf.Max(Logo.enabled ? LogoSize : 0f,
                        AvText.Height(name, w) + (subtitle.text.Length > 0 ? 2f + AvText.Height(subtitle, w) : 0f));
                }

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    lastSlot = s; placed = true;
                    float w = s.W - Indent, nh = AvText.Height(name, w);
                    AvLay.Place(Logo.rectTransform, 0f, 0f, LogoSize, LogoSize);
                    AvLay.Place(name.rectTransform, Indent, 0f, w, nh);
                    subtitle.gameObject.SetActive(subtitle.text.Length > 0);
                    AvLay.Place(subtitle.rectTransform, Indent, nh + 2f, w, AvText.Height(subtitle, w));
                }

                public override void Restyle()
                {
                    name.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
                    subtitle.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
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
            }
        }
    }
}
