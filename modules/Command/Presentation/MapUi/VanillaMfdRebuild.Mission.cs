using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Presentation;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ----------------------------------------------------------- mission panel

        /// <summary>
        /// The MIS bezel as a briefing board. MISSION is the hero card (name, clock, lead objective,
        /// briefing) over key numbers, the escalation ladder, the lead contract and a live log;
        /// OBJECTIVES is a checklist with state glyphs and distances; CONTRACTS is a stack of cards with the
        /// reward in mono and a primary action. Reads happen in <see cref="Gather"/> into one reusable
        /// <see cref="BoardModel"/>; <see cref="Render"/> only writes it to already-built parts.
        /// </summary>
        private sealed class MissionPresenter : Presenter
        {
            private const int ObjectivePageSize = 10;
            private const int MaxObjectiveLines = 24;
            private const int SecondaryPageSize = MfdSecondaryObjectives.MaxCards;

            private struct ObjectiveLine
            {
                public string Key, Title, Kind, Source, Type;
                public AvIcon Icon;
                public float Fraction;
                /// <summary>Metres to the nearest fix; negative means unknown, hidden or not positioned.</summary>
                public float DistanceM;
                public MissionPhase Phase;
            }

            /// <summary>Everything the three pages show, read once per refresh.</summary>
            private sealed class BoardModel
            {
                public string MissionName = "NO MISSION LOADED", Brief = "WAITING FOR MISSION CONTROLLER DATA", Mode = "", Clock = "—";
                public bool HasBrief, HasHq;
                public float Score;
                public bool HasEscalation;
                public float Current, Tactical, Strategic;
                public readonly List<ObjectiveLine> Objectives = new List<ObjectiveLine>(MaxObjectiveLines);
                public int ObjectivesDone, ObjectivesActive;
                public string ObjectiveSummary = "";
                public bool Installed, Streamed;
                public int Limit, ActiveContracts, Offers, Closed;
                public int AtStake, Offered, Paid;
                public readonly List<SecondaryObjectiveView> Contracts = new List<SecondaryObjectiveView>(8);
                public SecondaryObjectiveView Lead;
            }

            private readonly BoardModel model = new BoardModel();
            private readonly List<string> objectiveTypes = new List<string>(MaxObjectiveLines);
            private readonly List<Objective> activeObjectives = new List<Objective>(MaxObjectiveLines);
            private readonly MissionLog log = new MissionLog();
            private readonly MissionEventTracker tracker = new MissionEventTracker();
            private Mission trackedMission;
            private int objectiveSummarySignature;
            private bool hasObjectiveSummary;

            private AvFlow briefFlow, objectivesFlow, contractsFlow;
            private AvSection trendSection;
            private AvLineChart trendChart;
            private readonly float[] trendBuffer = new float[MfdResourceHistory.Capacity];
            private int selectedPage;

            // MISSION page.
            private MissionHeroPart hero;
            private AvGauge[] briefTiles;
            private AvSection ladderSection;
            private EscalationLadderPart ladder;
            private AvSection leadSection;
            private AvRow leadContractRow;
            private AvControl leadContractOpen;
            private AvSection logSection;
            private AvList logList;
            private const string BrowseHelp = "Open the contract board and browse optional missions.";

            // OBJECTIVES page.
            private ObjectiveTallyPart tally;
            private AvSection checklistSection;
            private PagedPartStack<ChecklistRow> checklist;
            private AvRow objectivesEmpty;

            // CONTRACTS page.
            private AvGauge[] moneyTiles;
            private AvControl[] secondaryFilterControls;
            private AvSection boardSection;
            private PagedPartStack<ContractCard> cardStack;
            private AvRow contractsEmpty;
            private AvControl openDesk;
            private MissionContractWindow contractWindow;
            private int secondaryFilter;
            private int secondaryConfirmId;
            private bool secondaryHasCapacity;
            private string secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE";

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override string Title => "BOSCALI / MISSION";

            protected override (AvIcon Icon, string Label)[] TabItems { get; } = new[]
            {
                (AvIcon.Flag, "MISSION"),
                (AvIcon.ListDetails, "OBJECTIVES"),
                (AvIcon.Star, "CONTRACTS"),
            };

            protected override string[] TabTips { get; } = new[]
            {
                "Mission: the briefing, lead objective, escalation ladder, lead contract, live log and a funds trend.",
                "Objectives: the checklist with progress and distance to each objective, and a map of how far each one has come.",
                "Contracts: browse, accept or abort optional contracts, or open the shared contract desk.",
            };

            protected override void BuildContent()
            {
                // The objective / contract / clock chips repeated the rings and the hero clock, so the header now
                // carries only the tab bar.
                briefFlow = CreatePage();
                objectivesFlow = CreatePage();
                contractsFlow = CreatePage();
                BuildBriefPage(briefFlow);
                BuildObjectivesPage(objectivesFlow);
                BuildContractsPage(contractsFlow);
            }

            protected override void RefreshContent()
            {
                Gather();
                Render();
            }

            protected override string AmbientStatus() =>
                selectedPage == 2 ? secondaryStatus :
                model.ObjectivesActive == 0 ? "NO ACTIVE OBJECTIVES" :
                model.ObjectivesActive + " ACTIVE OBJECTIVES";

            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                RequestRefresh();
            }

            // ------------------------------------------------------------ MISSION page

            private void BuildBriefPage(AvFlow page)
            {
                hero = page.Add(new MissionHeroPart(page.Content));

                briefTiles = new[]
                {
                    new AvGauge(page.Content, "SCORE", AvGaugeShape.Ring, 64f),
                    new AvGauge(page.Content, "OBJECTIVES", AvGaugeShape.Ring, 64f),
                    new AvGauge(page.Content, "AT STAKE", AvGaugeShape.Ring, 64f),
                };
                briefTiles[0].Help = "Score: the faction's mission score. The ring shows progress toward the next escalation gate.";
                briefTiles[1].Help = "Objectives: completed against issued. The OBJECTIVES page lists them with distances.";
                briefTiles[2].Help = "At stake: the reward of contracts currently in the field. The ring shows how full the active-contract limit is.";
                page.Row(briefTiles);

                ladderSection = page.Section(MfdChromeIcon.For("CHART"), "ESCALATION");
                ladder = page.Add(new EscalationLadderPart(page.Content));

                leadSection = page.Section(AvIcon.Star, "LEAD CONTRACT");
                leadContractRow = page.Add(new AvRow(page.Content, () => SetSelectedTab(2)));
                leadContractOpen = leadContractRow.AddTrailing(new AvControl.Spec("OPEN", () => SetSelectedTab(2),
                    AvButtonStyle.Primary));
                if (leadContractOpen != null) leadContractOpen.Help = BrowseHelp;

                logSection = page.Section(AvIcon.Activity, "LOG");
                logList = page.Add(new AvList(page.Content, page.Ticker, MfdMissionBoard.LogShown, BindLogRow));
                logSection.SetShown(false);
                logList.SetShown(false);

                trendSection = page.Section(AvIcon.ChartLine, "FUNDS", "NO SAMPLES");
                trendChart = new AvLineChart(page.Content, 120f);
                trendChart.SetSeries(trendBuffer, 0, "—", "—", "—");
                page.Add(trendChart, 1f);
            }

            /// <summary>The faction's funds over the observed window, fed by the same recorder the FAC history uses.</summary>
            private void RenderTrend()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                FactionHQ hq = map != null ? map.HQ : null;
                MfdResourceHistory history = hq == null ? null : FactionResourceHistoryStore.For(hq);
                int count = history != null ? history.Count : 0;
                int run = 0, runStart = 0;
                for (int i = 0; i < count; i++)
                {
                    float v = history.Value(0, i);
                    if (!MfdResourceHistory.Finite(v)) { run = 0; runStart = i + 1; continue; }
                    trendBuffer[run++] = v;
                }
                if (run < 2)
                {
                    trendChart.SetSeries(trendBuffer, 0, "—", "—", "—");
                    trendSection.SetCaption(count == 0 ? "NO SAMPLES" : "NEEDS 2 SAMPLES");
                    return;
                }
                float lo = trendBuffer[0], hi = trendBuffer[0];
                for (int i = 1; i < run; i++) { lo = Mathf.Min(lo, trendBuffer[i]); hi = Mathf.Max(hi, trendBuffer[i]); }
                trendChart.SetSeries(trendBuffer, run, AvNum.Money(lo), AvNum.Money(hi), AvNum.Money(trendBuffer[run - 1]));
                trendSection.SetCaption("LAST " + AvNum.Clock(Mathf.Max(0f, history.Time(count - 1) - history.Time(runStart))));
            }

            private void BindLogRow(int index, AvRow row)
            {
                MissionLog.Entry entry = log.Newest(index);
                row.Set(entry.Text, null, entry.Stamp, entry.State);
                row.Help = entry.Text;
            }

            private void RenderBrief()
            {
                hero.SetMission(model.MissionName, model.Mode, model.Clock, model.Brief, model.HasBrief);

                int lead = LeadObjective();
                if (lead >= 0)
                {
                    ObjectiveLine line = model.Objectives[lead];
                    hero.SetObjective(line.Title, line.Kind, line.Icon, line.Fraction,
                        line.DistanceM >= 0f ? MfdMissionBoard.Distance(line.DistanceM) : null, AvState.Info);
                }
                else hero.SetObjective(null, null, AvIcon.Flag, 0f, null, AvState.Inert);

                float progress = 0f;
                AvState holding = AvState.Ready;
                if (model.HasEscalation)
                {
                    int stage = MfdMissionOverview.Stage(model.Current, model.Tactical, model.Strategic);
                    holding = stage == 2 ? AvState.Danger : stage == 1 ? AvState.Caution : AvState.Ready;
                    progress = MfdMissionOverview.NextGateProgress(model.Current, model.Tactical, model.Strategic);
                }
                if (!model.HasHq) briefTiles[0].Set(0f, "—", AvState.Inert);
                else briefTiles[0].Set(progress, AvNum.Fixed(model.Score, 1), model.HasEscalation ? holding : AvState.Ready);

                int total = model.ObjectivesDone + model.ObjectivesActive;
                if (total == 0) briefTiles[1].Set(0f, "—", AvState.Inert);
                else briefTiles[1].Set(model.ObjectivesDone / (float)total,
                    AvNum.Fixed(model.ObjectivesDone, 0) + "/" + AvNum.Fixed(total, 0),
                    model.ObjectivesDone == total ? AvState.Ready : AvState.Info);

                if (!model.Installed) briefTiles[2].Set(0f, "—", AvState.Inert);
                else briefTiles[2].Set(model.Limit > 0 ? Mathf.Clamp01(model.ActiveContracts / (float)model.Limit) : 0f,
                    Cash(model.AtStake), model.ActiveContracts > 0 ? AvState.Ready : AvState.Inert);

                RenderLadder();

                SecondaryObjectiveView preview = model.Lead;
                leadSection.SetCaption(!model.Installed ? "UNAVAILABLE" : MfdSecondaryObjectives.BoardSummary(
                    model.Offers, model.ActiveContracts, model.Limit, model.Closed));
                if (preview != null)
                {
                    string where = preview.IsActive ? "IN FIELD" : preview.Target;
                    string taken = string.IsNullOrWhiteSpace(preview.AcceptedBy) ? "" : "  ·  TAKEN BY " + preview.AcceptedBy;
                    leadContractRow.Set(MfdSecondaryObjectives.TitleLine(preview.Id, preview.Title),
                        where + taken, Cash(preview.Money), preview.IsActive ? AvState.Ready : AvState.Info);
                    string help = MfdSecondaryObjectives.TitleLine(preview.Id, preview.Title) + "  ·  " + where + taken;
                    leadContractRow.Help = help;
                    if (leadContractOpen != null) leadContractOpen.Help = help;
                }
                else
                {
                    leadContractRow.Set(model.Offers + model.ActiveContracts > 0 ? "REVIEW CONTRACTS" : "NO CONTRACTS",
                        "", "", AvState.Inert);
                    leadContractRow.Help = BrowseHelp;
                    if (leadContractOpen != null) leadContractOpen.Help = BrowseHelp;
                }
                if (leadContractOpen != null)
                    leadContractOpen.gameObject.SetActive(preview != null || model.Offers + model.ActiveContracts > 0);

                int shown = Math.Min(log.Count, MfdMissionBoard.LogShown);
                logSection.SetShown(shown > 0);
                logList.SetShown(shown > 0);
                logList.SetCount(shown);
                RenderTrend();
            }

            private int LeadObjective()
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < model.Objectives.Count; i++)
                {
                    ObjectiveLine line = model.Objectives[i];
                    if (line.Phase != MissionPhase.Active) continue;
                    if (best < 0) best = i;
                    if (line.DistanceM >= 0f && line.DistanceM < bestDistance)
                    {
                        bestDistance = line.DistanceM;
                        best = i;
                    }
                }
                return best;
            }

            private void RenderLadder()
            {
                // No escalation feed: say nothing rather than draw an empty ladder.
                ladderSection.SetShown(model.HasEscalation);
                ladder.SetShown(model.HasEscalation);
                if (!model.HasEscalation) return;

                float current = Mathf.Max(0f, model.Current);
                float tactical = model.Tactical, strategic = model.Strategic;
                int stage = MfdMissionOverview.Stage(current, tactical, strategic);
                ladderSection.SetCaption(MfdMissionOverview.Caption(current, tactical, strategic));
                AvState holding = stage == 2 ? AvState.Danger : stage == 1 ? AvState.Caution : AvState.Ready;
                for (int i = 0; i < 3; i++)
                {
                    bool set = i == 0 || (i == 1 ? tactical > 0f : strategic > 0f);
                    AvState state = i == stage ? holding : i < stage ? AvState.Ready : AvState.Inert;
                    ladder.SetRung(i, MfdMissionBoard.RungThreshold(i, tactical, strategic),
                        MfdMissionOverview.StageState(i, stage, set), state);
                }
                ladder.SetFill(MfdMissionBoard.LadderFill(stage, MfdMissionOverview.NextGateProgress(current, tactical, strategic)));
            }

            // -------------------------------------------------------- OBJECTIVES page

            private void BuildObjectivesPage(AvFlow page)
            {
                tally = page.Add(new ObjectiveTallyPart(page.Content));
                checklistSection = page.Section(AvIcon.ListDetails, "CHECKLIST");
                checklist = page.Add(new PagedPartStack<ChecklistRow>(page.Content, page.Ticker, ObjectivePageSize, 2f,
                    (parent, slot) => new ChecklistRow(parent), BindObjectiveRow));
                objectivesEmpty = page.Add(new AvRow(page.Content));
                objectivesEmpty.SetShown(false);

            }

            private void RenderObjectives()
            {
                bool any = model.Objectives.Count > 0;
                tally.SetShown(any);
                checklistSection.SetShown(any);
                checklist.SetShown(any);
                objectivesEmpty.SetShown(!any);
                if (!any)
                {
                    objectivesEmpty.Set(model.HasHq ? "NO ACTIVE OBJECTIVES" : "OBJECTIVE FEED OFFLINE",
                        "", "", AvState.Inert);
                    checklist.SetCount(0);
                    return;
                }

                int total = model.ObjectivesDone + model.ObjectivesActive;
                float nearest = -1f;
                for (int i = 0; i < model.Objectives.Count; i++)
                {
                    ObjectiveLine line = model.Objectives[i];
                    if (line.Phase == MissionPhase.Active && line.DistanceM >= 0f && (nearest < 0f || line.DistanceM < nearest))
                        nearest = line.DistanceM;
                }
                tally.Set(model.ObjectivesDone, total, nearest >= 0f ? MfdMissionBoard.Distance(nearest) : null,
                    model.ObjectivesDone == total ? AvState.Ready : AvState.Info);
                checklistSection.SetCaption(model.ObjectiveSummary);
                checklist.SetCount(model.Objectives.Count);
            }

            private void BindObjectiveRow(int index, ChecklistRow row)
            {
                if (index < 0 || index >= model.Objectives.Count)
                {
                    row.Set("OBJECTIVE LINK LOST", "", "—", 0f, false, AvState.Danger);
                    row.Help = null;
                    return;
                }

                ObjectiveLine line = model.Objectives[index];
                bool done = line.Phase == MissionPhase.Done;
                string detail = line.Kind;
                if (!string.IsNullOrEmpty(line.Source) && line.Source != line.Title) detail += "  ·  " + line.Source;
                detail += "  ·  " + (done ? "100%" : AvNum.Percent(line.Fraction));
                string value = done ? "DONE" : line.DistanceM >= 0f ? MfdMissionBoard.Distance(line.DistanceM) : AvNum.Percent(line.Fraction);

                // Surface the objective kind and source beside its progress instead of hiding them behind a heat cell.
                row.Set(line.Title, detail, value, line.Fraction, !done, done ? AvState.Ready : AvState.Info);
                row.Help = line.Title + "  —  " + detail;
            }

            // ---------------------------------------------------------- CONTRACTS page

            private void BuildContractsPage(AvFlow page)
            {
                moneyTiles = new[]
                {
                    new AvGauge(page.Content, "IN FIELD", AvGaugeShape.Ring, 64f),
                    new AvGauge(page.Content, "OFFERED", AvGaugeShape.Ring, 64f),
                    new AvGauge(page.Content, "PAID", AvGaugeShape.Ring, 64f),
                };
                moneyTiles[0].Help = "In field: the money at stake in accepted contracts. The ring fills as active contracts approach the limit.";
                moneyTiles[1].Help = "Offered: the total reward of offers nobody has accepted yet, against everything on the board.";
                moneyTiles[2].Help = "Paid: rewards already earned from completed contracts, against everything on the board.";
                page.Row(moneyTiles);

                boardSection = page.Section(AvIcon.Star, "CONTRACTS");
                // The desk button rides the filter row: one row of four instead of a row and a stray full-width button.
                AvButtons filters = page.Buttons(
                    new AvControl.Spec("AVAILABLE", () => { secondaryFilter = MfdSecondaryObjectives.FilterAvailable; cardStack.SetPage(0); RequestRefresh(); }),
                    new AvControl.Spec("ACTIVE", () => { secondaryFilter = MfdSecondaryObjectives.FilterActive; cardStack.SetPage(0); RequestRefresh(); }),
                    new AvControl.Spec("CLOSED", () => { secondaryFilter = MfdSecondaryObjectives.FilterResults; cardStack.SetPage(0); RequestRefresh(); }),
                    new AvControl.Spec("DESK", OpenContractDesk, AvButtonStyle.Default, MfdChromeIcon.For("LEDGER")));
                secondaryFilterControls = new[] { filters.Controls[0], filters.Controls[1], filters.Controls[2] };
                string[] filterNames = { "available", "active", "closed" };
                string[] filterHelp =
                {
                    "offers the host has not answered yet",
                    "contracts this faction has accepted",
                    "completed, lapsed and aborted contracts",
                };
                for (int i = 0; i < secondaryFilterControls.Length; i++)
                    secondaryFilterControls[i].Help = "Show " + filterNames[i] + " contracts — " + filterHelp[i] + ".";
                openDesk = filters.Controls[3];
                openDesk.Help = "Desk: open the shared faction contract record in its own window.";

                cardStack = page.Add(new PagedPartStack<ContractCard>(page.Content, page.Ticker, SecondaryPageSize, 6f,
                    (parent, slot) => new ContractCard(parent,
                        () => AcceptSecondary(cardStack.ItemIndex(slot)),
                        () => DismissSecondary(cardStack.ItemIndex(slot))),
                    BindContractCard));
                contractsEmpty = page.Add(new AvRow(page.Content));
                contractsEmpty.SetShown(false);

            }

            private void OpenContractDesk()
            {
                if (contractWindow == null) contractWindow = MissionContractWindow.Create();
                contractWindow.Show();
            }

            private void RenderContracts()
            {
                if (!model.Installed)
                    foreach (AvGauge ring in moneyTiles) ring.Set(0f, "—", AvState.Inert);
                else
                {
                    float pool = Mathf.Max(1f, model.AtStake + model.Offered + model.Paid);
                    moneyTiles[0].Set(model.Limit > 0 ? Mathf.Clamp01(model.ActiveContracts / (float)model.Limit) : model.AtStake / pool,
                        Cash(model.AtStake), model.ActiveContracts > 0 ? AvState.Ready : AvState.Inert);
                    moneyTiles[1].Set(model.Offered / pool, Cash(model.Offered), model.Offers > 0 ? AvState.Info : AvState.Inert);
                    moneyTiles[2].Set(model.Paid / pool, Cash(model.Paid), model.Paid > 0 ? AvState.Ready : AvState.Inert);
                }

                secondaryFilterControls[0].Label = "AVAILABLE " + model.Offers;
                secondaryFilterControls[1].Label = "ACTIVE " + MfdSecondaryObjectives.ShortCount(model.ActiveContracts, model.Limit);
                secondaryFilterControls[2].Label = "CLOSED " + model.Closed;
                for (int i = 0; i < secondaryFilterControls.Length; i++)
                    secondaryFilterControls[i].Latched = i == secondaryFilter;

                int count = model.Contracts.Count;
                bool empty = count == 0;
                boardSection.SetCaption(!model.Installed ? "UNAVAILABLE"
                    : !model.Streamed ? "WAITING FOR HOST"
                    : model.Limit > 0 ? "MAX " + model.Limit : "ONLINE");

                cardStack.SetShown(!empty);
                contractsEmpty.SetShown(empty);
                if (empty)
                {
                    string message = MfdSecondaryObjectives.EmptyMessage(secondaryFilter,
                        EmptyReason(model.Installed, model.Streamed, model.Offers, model.Closed));
                    int cut = message.IndexOf('\n');
                    contractsEmpty.Set(cut < 0 ? message : message.Substring(0, cut), "",
                        "", model.Installed && model.Streamed ? AvState.Inert : AvState.Caution);
                }
                cardStack.SetCount(count);
            }

            private static BoardEmptyReason EmptyReason(bool installed, bool streamed, int available, int results) =>
                !installed ? BoardEmptyReason.Unavailable
                : !streamed ? BoardEmptyReason.LinkLost
                : available + results == 0 ? BoardEmptyReason.DirectorExhausted
                : BoardEmptyReason.Ready;

            private void BindContractCard(int index, ContractCard card)
            {
                SecondaryObjectiveView objective = index >= 0 && index < model.Contracts.Count ? model.Contracts[index] : null;
                if (objective == null)
                {
                    card.Set(new ContractCardData
                    {
                        Icon = AvIcon.AlertTriangle, Title = "OBJECTIVE LINK LOST", State = AvState.Caution,
                        AcceptLabel = "ACCEPT", DismissLabel = "DISMISS",
                    });
                    return;
                }

                bool complete = objective.IsComplete;
                bool lapsed = !complete && objective.SecondsRemaining <= 0f;
                bool urgent = !complete && !lapsed && objective.SecondsRemaining <= MfdSecondaryObjectives.UrgentSeconds;
                AvState state = complete ? AvState.Ready : lapsed ? AvState.Danger : urgent ? AvState.Caution
                    : objective.IsActive ? AvState.Info : AvState.Inert;

                string family = MfdMissionLabels.ContractFamily(objective.Title);
                string status = objective.IsOffered ? "AWAITING ACCEPTANCE" : objective.Status;
                string sub = family + "  ·  " + status;
                if (!string.IsNullOrWhiteSpace(objective.AcceptedBy)) sub += "  ·  TAKEN BY " + objective.AcceptedBy;
                float fraction = MfdChartScale.Fraction(objective.Progress, 1f);
                if (complete) fraction = 1f;

                bool dismissable = objective.IsOffered || objective.IsActive;
                card.Set(new ContractCardData
                {
                    Icon = ContractIcon(objective.Title),
                    Title = MfdSecondaryObjectives.TitleLine(objective.Id, objective.Title),
                    Sub = sub,
                    Description = objective.Description,
                    Reward = Cash(objective.Money),
                    Xp = "+" + AvNum.Thousands(Math.Max(0, objective.Xp)) + " XP",
                    Progress = objective.IsOffered ? "OFFER" : complete ? "100%" : AvNum.Percent(fraction),
                    Chip = MfdSecondaryObjectives.ChipLabel(objective),
                    Fraction = fraction,
                    State = state,
                    ShowActions = dismissable,
                    CanAccept = MfdSecondaryObjectives.CanAccept(objective, secondaryHasCapacity),
                    AcceptLabel = MfdSecondaryObjectives.AcceptLabel(objective, secondaryHasCapacity),
                    CanDismiss = dismissable,
                    DismissLabel = objective.IsActive
                        ? (secondaryConfirmId == objective.Id ? "CONFIRM ABORT" : "ABORT")
                        : "DISMISS",
                    Help = MfdSecondaryObjectives.TitleLine(objective.Id, objective.Title) + " · " +
                           objective.Description + " · " + objective.Target + " · " + objective.Reward +
                           (string.IsNullOrWhiteSpace(objective.AcceptedBy) ? "" : " · ACCEPTED BY " + objective.AcceptedBy),
                });
            }

            private void AcceptSecondary(int index)
            {
                if (index < 0 || index >= model.Contracts.Count) return;
                SecondaryObjectiveView objective = model.Contracts[index];
                if (objective != null && MfdSecondaryObjectives.CanAccept(objective, secondaryHasCapacity) &&
                    ModuleServices.TryGet(out ISecondaryObjectivesView view))
                    view.RequestAccept(objective.Id);
                RequestRefresh();
            }

            private void DismissSecondary(int index)
            {
                if (index < 0 || index >= model.Contracts.Count) return;
                SecondaryObjectiveView objective = model.Contracts[index];
                if (objective == null) return;
                if (objective.IsActive && secondaryConfirmId != objective.Id)
                {
                    secondaryConfirmId = objective.Id;
                    RequestRefresh();
                    return;
                }
                if (ModuleServices.TryGet(out ISecondaryObjectivesView view)) view.RequestCancel(objective.Id);
                secondaryConfirmId = 0;
                RequestRefresh();
            }

            // ---------------------------------------------------------------- render

            private void Render()
            {
                RenderBrief();
                RenderObjectives();
                RenderContracts();
            }

            // ---------------------------------------------------------------- gather

            private void Gather()
            {
                Mission mission = MissionManager.CurrentMission;
                MissionManager manager = NetworkSceneSingleton<MissionManager>.i;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                FactionHQ hq = map != null ? map.HQ : null;

                if (!ReferenceEquals(mission, trackedMission))
                {
                    trackedMission = mission;
                    tracker.Reset();
                    log.Clear();
                }

                model.Clock = MissionClock(manager);
                if (mission == null)
                {
                    model.MissionName = "NO MISSION LOADED";
                    model.Brief = "WAITING FOR MISSION CONTROLLER DATA";
                    model.HasBrief = false;
                    model.Mode = "";
                }
                else
                {
                    model.MissionName = string.IsNullOrEmpty(mission.Name) ? "UNTITLED MISSION" : mission.Name;
                    model.HasBrief = mission.missionSettings != null &&
                                     !string.IsNullOrWhiteSpace(mission.missionSettings.description);
                    model.Brief = model.HasBrief ? mission.missionSettings.description : "NO BRIEFING FILED";
                    model.Mode = ModeLabel(mission);
                }

                model.HasHq = hq != null;
                model.Score = hq != null ? hq.factionScore : 0f;
                model.HasEscalation = manager != null;
                if (manager != null)
                {
                    model.Current = Mathf.Max(0f, manager.currentEscalation);
                    model.Tactical = manager.tacticalThreshold;
                    model.Strategic = manager.strategicThreshold;
                }

                GatherObjectives(mission, hq);
                GatherContracts();
                ObserveEvents();
            }

            private void GatherObjectives(Mission mission, FactionHQ hq)
            {
                model.Objectives.Clear();
                objectiveTypes.Clear();
                activeObjectives.Clear();
                model.ObjectivesDone = model.ObjectivesActive = 0;

                GlobalPosition from = Datum.originPosition.ToGlobalPosition();
                if (hq != null && MissionPosition.TryGetActiveObjectives(hq, out List<Objective> active) && active != null)
                {
                    for (int i = 0; i < active.Count && model.Objectives.Count < MaxObjectiveLines; i++)
                    {
                        Objective objective = active[i];
                        if (objective == null || objective.SavedObjective == null) continue;
                        activeObjectives.Add(objective);
                        bool complete = objective.Status == ObjectiveStatus.Complete || objective.CompletePercent >= 0.999f;
                        AddObjectiveLine(objective, complete, from);
                    }
                }

                MissionObjectives runtime = mission == null ? null : mission.RuntimeObjectives;
                if (runtime != null && runtime.AllObjectives != null)
                {
                    List<Objective> all = runtime.AllObjectives;
                    for (int i = 0; i < all.Count && model.Objectives.Count < MaxObjectiveLines; i++)
                    {
                        Objective objective = all[i];
                        if (objective == null || objective == runtime.StartObjective ||
                            objective.Status != ObjectiveStatus.Complete || objective.SavedObjective == null ||
                            objective.SavedObjective.Hidden || activeObjectives.Contains(objective) ||
                            (hq != null && objective.FactionHQ != hq)) continue;
                        AddObjectiveLine(objective, true, from);
                    }
                }

                // The summary only depends on the type multiset in order: rebuild it when that changes.
                int signature = objectiveTypes.Count;
                unchecked
                {
                    for (int i = 0; i < objectiveTypes.Count; i++)
                        signature = signature * 31 + (objectiveTypes[i] != null ? objectiveTypes[i].GetHashCode() : 0);
                }
                if (!hasObjectiveSummary || signature != objectiveSummarySignature)
                {
                    hasObjectiveSummary = true;
                    objectiveSummarySignature = signature;
                    model.ObjectiveSummary = MfdMissionLabels.TypeSummary(objectiveTypes);
                }
            }

            private void AddObjectiveLine(Objective objective, bool complete, GlobalPosition from)
            {
                SavedObjective saved = objective.SavedObjective;
                string type = saved.ObjectiveTypeEnum.ToString();
                string title = MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(saved.DisplayName));
                string source = MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(saved.UniqueName));
                float distance = -1f;
                if (!complete && !saved.Hidden &&
                    MissionPosition.DistanceTo(objective, from, out MissionPosition.PositionResult result))
                    distance = result.Distance;
                model.Objectives.Add(new ObjectiveLine
                {
                    Key = saved.UniqueName, Title = title, Source = source, Type = type,
                    Kind = MfdMissionLabels.ObjectiveTypeLabel(type), Icon = ObjectiveIcon(type),
                    Fraction = complete ? 1f : MfdChartScale.Fraction(objective.CompletePercent, 1f),
                    DistanceM = distance, Phase = complete ? MissionPhase.Done : MissionPhase.Active,
                });
                objectiveTypes.Add(type);
                if (complete) model.ObjectivesDone++;
                else model.ObjectivesActive++;
            }

            private readonly List<SecondaryObjectiveView> allContracts = new List<SecondaryObjectiveView>(16);

            private void GatherContracts()
            {
                bool installed = ModuleServices.TryGet(out ISecondaryObjectivesView view);
                IReadOnlyList<SecondaryObjectiveView> entries = null;
                bool streamed = false;
                model.Installed = installed;
                if (installed)
                {
                    view.Refresh();
                    entries = view.Objectives;
                    streamed = !string.IsNullOrWhiteSpace(view.Status);
                    model.Limit = Math.Max(0, view.ActiveLimit);
                    secondaryStatus = streamed ? view.Status : "SECONDARY MISSIONS — WAITING FOR HOST";
                }
                else
                {
                    model.Limit = 0;
                    secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE — DIRECTOR DISABLED OR NOT INSTALLED";
                }
                model.Streamed = streamed;

                model.Offers = model.Closed = model.ActiveContracts = 0;
                model.AtStake = model.Offered = model.Paid = 0;
                SecondaryObjectiveView leadOffer = null, leadActive = null;
                model.Contracts.Clear();
                allContracts.Clear();
                if (entries != null) foreach (SecondaryObjectiveView entry in entries)
                {
                    if (entry == null) continue;
                    allContracts.Add(entry);
                    int money = Math.Max(0, entry.Money);
                    if (entry.IsActive)
                    {
                        model.ActiveContracts++;
                        model.AtStake += money;
                        if (leadActive == null) leadActive = entry;
                    }
                    else if (entry.IsOffered)
                    {
                        model.Offers++;
                        model.Offered += money;
                        if (leadOffer == null) leadOffer = entry;
                    }
                    else
                    {
                        model.Closed++;
                        if (entry.IsComplete) model.Paid += money;
                    }
                    if (secondaryFilter == MfdSecondaryObjectives.FilterAvailable ? entry.IsOffered :
                        secondaryFilter == MfdSecondaryObjectives.FilterActive ? entry.IsActive :
                        !entry.IsActive && !entry.IsOffered)
                        model.Contracts.Add(entry);
                }
                MfdSecondaryObjectives.SortForDisplay(model.Contracts, secondaryFilter);
                secondaryHasCapacity = model.Limit <= 0 || model.ActiveContracts < model.Limit;
                model.Lead = leadActive ?? leadOffer;
            }

            private void ObserveEvents()
            {
                tracker.Begin();
                for (int i = 0; i < model.Objectives.Count; i++)
                {
                    ObjectiveLine line = model.Objectives[i];
                    tracker.Observe(log, model.Clock, "OBJECTIVE", "O:" + line.Key, line.Title, line.Phase);
                }
                for (int i = 0; i < allContracts.Count; i++)
                {
                    SecondaryObjectiveView entry = allContracts[i];
                    MissionPhase phase = entry.IsComplete ? MissionPhase.Done : entry.IsActive ? MissionPhase.Active
                        : entry.IsOffered ? MissionPhase.Offered : MissionPhase.Closed;
                    tracker.Observe(log, model.Clock, "CONTRACT", "C:" + entry.Id,
                        MfdSecondaryObjectives.TitleLine(entry.Id, entry.Title), phase);
                }
                tracker.End(log, model.Clock);
            }

            // ------------------------------------------------------------- helpers

            private static string ModeLabel(Mission mission)
            {
                if (mission == null || mission.missionSettings == null) return "";
                switch (mission.missionSettings.playerMode)
                {
                    case PlayerMode.Singleplayer: return "SINGLEPLAYER";
                    case PlayerMode.Multiplayer: return "MULTIPLAYER";
                    default: return "SINGLE / MULTIPLAYER";
                }
            }

            /// <summary>A whole-dollar amount in mono, thousands-separated ("$1,500").</summary>
            private static string Cash(int dollars) => "$" + AvNum.Thousands(Math.Max(0, dollars));

            private static string MissionClock(MissionManager manager) =>
                manager == null ? "—" : AvNum.Clock(Mathf.Max(0, Mathf.FloorToInt(manager.MissionTime)));

            /// <summary>Objective-type icon: chrome (a real AvIcon), driven by the vanilla ObjectiveType.</summary>
            private static AvIcon ObjectiveIcon(string typeName)
            {
                switch (typeName)
                {
                    case "DestroyUnits": return AvIcon.Target;
                    case "CrashAircraft": return AvIcon.Plane;
                    case "CaptureAirbase": return AvIcon.Flag;
                    case "ReachUnits": return AvIcon.MapPin;
                    case "ReachWaypoints": return AvIcon.MapPin;
                    case "SuccessfulSortie": return AvIcon.Plane;
                    case "WaitSeconds": return AvIcon.Clock;
                    case "CompleteOtherObjective": return AvIcon.CircleCheck;
                    case "SpotUnit": return AvIcon.Eye;
                    default: return AvIcon.Flag;
                }
            }

            /// <summary>Contract-family icon: chrome, mapped from <see cref="MfdMissionLabels.ContractGlyph"/>'s
            /// v1 glyph-kind strings onto the closest real <see cref="AvIcon"/>.</summary>
            private static AvIcon ContractIcon(string title)
            {
                switch (MfdMissionLabels.ContractGlyph(title))
                {
                    case "shield": return AvIcon.Shield;
                    case "target": return AvIcon.Target;
                    case "air": return AvIcon.Plane;
                    case "eye": return AvIcon.Eye;
                    case "radar": return AvIcon.Radar2;
                    case "person": return AvIcon.User;
                    case "building": return AvIcon.BuildingBank;
                    case "convoy": return AvIcon.Map2;
                    case "repair": return AvIcon.Bolt;
                    default: return AvIcon.Flag;
                }
            }
        }

        private sealed class UnavailablePresenter : Presenter
        {
            public UnavailablePresenter(MFDScreen screen, VanillaMfdPanelId id) : base(screen, id) { }

            protected override string Title => "COMPATIBILITY HOLD";
            protected override (AvIcon Icon, string Label)[] TabItems { get; } = Array.Empty<(AvIcon, string)>();

            protected override void BuildContent()
            {
                AvFlow page = CreatePage();
                page.Section(AvIcon.AlertTriangle, "NATIVE ADAPTER UNAVAILABLE");
                Note(page, "This game build changed the controller attached to this MFD. " +
                           "The source panel remains intact and will be restored when the map closes.");
            }

            protected override void RefreshContent() { }

            protected override string AmbientStatus() => "UPDATE COMPATIBILITY REQUIRED";
        }
    }
}
