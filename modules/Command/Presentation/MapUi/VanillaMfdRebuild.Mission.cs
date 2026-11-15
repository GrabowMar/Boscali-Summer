using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ----------------------------------------------------------- mission panel

        /// <summary>
        /// The MIS bezel, home of every paid task. JOBS lists the pilot's own job (on the HUD, or an armed package) and then the open jobs of every
        /// <see cref="IJobSource"/> (contracts and staff sorties from DynamicOperations, strike packages from Support), nearest first, each with
        /// LOCATE and its source's own act; a contract or sortie opens in place into its order, plot and ABORT. OBJECTIVES is a checklist with
        /// state glyphs and distances; BRIEF is the hero card (name, clock, lead objective, briefing) over SCORE, OBJECTIVES and the escalation
        /// ladder. Reads happen in <see cref="Gather"/> into one reusable <see cref="BoardModel"/>; <see cref="Render"/> only writes it to built parts.
        /// </summary>
        private sealed class MissionPresenter : Presenter
        {
            private const int ObjectivePageSize = 10;
            private const int MaxObjectiveLines = 24;
            private const int PageJobs = 0, MaxHudCards = 2, MaxOpenCards = 12;

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
                public string MissionName = "NO MISSION LOADED", Brief = "STANDING BY FOR MISSION CONTROLLER", Mode = "", Clock = "—";
                public bool HasBrief, HasHq;
                public float Score;
                public bool HasEscalation;
                public float Current, Tactical, Strategic;
                public readonly List<ObjectiveLine> Objectives = new List<ObjectiveLine>(MaxObjectiveLines);
                public int ObjectivesDone, ObjectivesActive;
                public string ObjectiveSummary = "";
                /// <summary>The host's contract cards, for the detail of an open contract or sortie row.</summary>
                public readonly List<SecondaryObjectiveView> Contracts = new List<SecondaryObjectiveView>(8);
                public readonly List<JobRow> Jobs = new List<JobRow>(16);
                public bool Installed, Sources;
            }

            private readonly BoardModel model = new BoardModel();
            private readonly List<string> objectiveTypes = new List<string>(MaxObjectiveLines);
            private readonly List<Objective> activeObjectives = new List<Objective>(MaxObjectiveLines);
            private int objectiveSummarySignature;
            private bool hasObjectiveSummary;

            private AvFlow jobsFlow, objectivesFlow, briefFlow;
            private int selectedPage;

            // JOBS page.
            private AvSection openSection;
            private readonly JobCard[] hudCards = new JobCard[MaxHudCards], openCards = new JobCard[MaxOpenCards];
            private AvRow hudEmpty, openEmpty;
            private MissionTaskPlot taskPlot;
            private MissionTaskActions taskActions;
            private int openSlipId = -2;   // -2 = not chosen yet: the next render opens this pilot's own contract
            private int hudShown, openShown;
            private bool secondaryFresh, actionPending;
            private string actionResult = "";
            private float selfX, selfZ;
            private bool hasSelf;
            private Comparison<JobRow> jobOrder;

            // OBJECTIVES page.
            private ObjectiveTallyPart tally;
            private AvSection checklistSection;
            private AvPagedStack<ChecklistRow> checklist;
            private AvRow objectivesEmpty;
            private AvSection typeSection;
            private TaskTypeTally typeTally;

            // BRIEF page.
            private MissionHeroPart hero;
            private AvGauge[] briefTiles;
            private AvSection ladderSection;
            private EscalationLadderPart ladder;

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override (AvIcon Icon, string Label)[] TabItems { get; } = new[]
            {
                (AvIcon.Star, "JOBS"),
                (AvIcon.ListDetails, "OBJECTIVES"),
                (AvIcon.Flag, "BRIEF"),
            };

            protected override string[] TabTips { get; } = new[]
            {
                "Jobs: the one on your HUD, then every open job (contracts, staff sorties, strike packages), nearest first. LOCATE moves the map.",
                "Objectives: the checklist with progress and distance to each objective, and a map of how far each one has come.",
                "Brief: the briefing, the lead objective, the score and the escalation ladder.",
            };

            protected override void BuildContent()
            {
                BoscaliSummer.Core.Ui.CountryStrip.Mount(Console);
                jobsFlow = CreatePage();
                objectivesFlow = CreatePage();
                briefFlow = CreatePage();
                BuildJobsPage(jobsFlow);
                BuildObjectivesPage(objectivesFlow);
                BuildBriefPage(briefFlow);
                Console.Orders(0,
                    "LOCATE moves the map to the job. The act on the right is the source's own: ACCEPT, SELECT, FIRE.",
                    "RELEASE hands your own job back: off your HUD, or a strike package disarmed.",
                    "Click a contract or sortie to open its order, plot and ABORT. ABORT asks for confirmation.");
                Console.Orders(1,
                    "Open tasks lead the list. Completed tasks are filed beneath them.",
                    "The figure on the right is the distance to the task's nearest fix. No fix reads percent complete.",
                    "Percent complete is the host's own figure. The strip under each task mirrors it.",
                    "BY TASK TYPE counts done against issued for each kind of task.");
                Console.Orders(2,
                    "The lead task is the nearest open objective with a known fix.",
                    "The ring under SCORE fills toward the next escalation gate.");
            }

            protected override void RefreshContent()
            {
                Gather();
                Render();
            }

            protected override string AmbientStatus() =>
                selectedPage == PageJobs ? (model.Sources ? openShown + " OPEN · " + hudShown + " ON HUD" : "NO JOB SOURCE RUNNING") :
                model.ObjectivesActive == 0 ? "NO OPEN TASKS" :
                model.ObjectivesActive + " OPEN TASKS";

            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                taskActions?.CancelConfirmation();
                RequestRefresh();
            }

            // -------------------------------------------------------------- JOBS page

            private void BuildJobsPage(AvFlow page)
            {
                page.Section(AvIcon.Target, "ON YOUR HUD", "ONE JOB AT A TIME");
                for (int i = 0; i < MaxHudCards; i++) { hudCards[i] = page.Add(new JobCard(page.Content, Pick, Locate, Act, Release)); hudCards[i].SetShown(false); }
                hudEmpty = page.Add(new AvRow(page.Content));
                openSection = page.Section(AvIcon.ListDetails, "OPEN JOBS", "EVERY SOURCE · NEAREST FIRST");
                for (int i = 0; i < MaxOpenCards; i++) { openCards[i] = page.Add(new JobCard(page.Content, Pick, Locate, Act, Release)); openCards[i].SetShown(false); }
                openEmpty = page.Add(new AvRow(page.Content));
                // One plot and one action bar, handed to whichever contract is open.
                taskPlot = new MissionTaskPlot(page.Content); taskPlot.Rect.gameObject.SetActive(false);
                taskActions = new MissionTaskActions(page.Content, RequestRefresh); Console.Ticker.Register(taskActions);
                taskActions.Rect.gameObject.SetActive(false);
                page.Links(("WAR PLAN", "STR", "BOARD"), ("ALLOCATION", "OPS", "PERKS"));
            }

            private void Pick(JobCard card)
            {
                if (!card.Expandable) return;
                openSlipId = card.Expanded ? -1 : card.Row.Id;
                taskActions.CancelConfirmation();
                RequestRefresh();
            }

#if !PUBLIC_RELEASE
            /// <summary>Clicks the job card at this index (HUD cards first, then the open list), or its live act button; false when there is nothing to click.</summary>
            internal bool PickForAutomation(int index, bool act)
            {
                JobCard card = index < 0 ? null : index < hudShown ? hudCards[index] : index - hudShown < openShown ? openCards[index - hudShown] : null;
                if (card == null || (act ? string.IsNullOrEmpty(card.Row.Act) || !card.Row.ActLive : !card.Expandable)) return false;
                if (act) Act(card); else Pick(card);
                return true;
            }
#endif

            /// <summary>LOCATE: the open map flies to the job's ground point (maximised first when it is not).</summary>
            private static void Locate(JobCard card)
            {
                JobRow job = card.Row;
                if (!job.HasSite) return;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && !DynamicMap.mapMaximized) map.Maximize();
                if (ModuleServices.TryGet(out IMapProjection projection)) projection.Focus(job.X, job.Z);
            }

            private void Act(JobCard card) { SourceOf(card.Row.Kind)?.Act(card.Row.Id); RequestRefresh(); }

            private void Release(JobCard card) { SourceOf(card.Row.Kind)?.Release(card.Row.Id); RequestRefresh(); }

            private static IJobSource SourceOf(JobKind kind) =>
                kind == JobKind.StrikePackage
                    ? ModuleServices.TryGet(out ISupportJobs support) ? support : null
                    : ModuleServices.TryGet(out IOperationJobs operations) ? (IJobSource)operations : null;

            private void RenderJobs()
            {
                List<JobRow> jobs = model.Jobs;
                // The open contract left the board (taken, lapsed or replaced): pick again rather than leave a stale one open.
                if (openSlipId >= 0 && !jobs.Exists(j => j.Kind != JobKind.StrikePackage && j.Id == openSlipId)) openSlipId = -2;
                if (openSlipId == -2)
                {
                    int own = jobs.FindIndex(j => j.Mine && j.Kind != JobKind.StrikePackage);
                    openSlipId = own >= 0 ? jobs[own].Id : -1;
                }

                hudShown = openShown = 0;
                JobCard open = null; SecondaryObjectiveView openEntry = null;
                for (int i = 0; i < jobs.Count; i++)
                {
                    JobRow job = jobs[i];
                    JobCard card = job.Mine && hudShown < MaxHudCards ? hudCards[hudShown++] : !job.Mine && openShown < MaxOpenCards ? openCards[openShown++] : null;
                    if (card == null) continue;
                    SecondaryObjectiveView entry = job.Kind == JobKind.StrikePackage ? null : model.Contracts.Find(c => c.Id == job.Id);
                    bool expanded = entry != null && job.Id == openSlipId;
                    card.Release();
                    card.SetShown(true);
                    card.Set(job, Range(job), entry, secondaryFresh, expanded);
                    if (expanded) { open = card; openEntry = entry; }
                }
                for (int i = hudShown; i < MaxHudCards; i++) { hudCards[i].Release(); hudCards[i].SetShown(false); }
                for (int i = openShown; i < MaxOpenCards; i++) { openCards[i].Release(); openCards[i].SetShown(false); }
                if (open != null) open.Attach(taskPlot, taskActions);
                else { taskPlot.Rect.gameObject.SetActive(false); taskActions.Rect.gameObject.SetActive(false); }
                taskActions.Set(openEntry, secondaryFresh, actionPending, actionResult);

                hudEmpty.SetShown(hudShown == 0);
                if (hudShown == 0) hudEmpty.Set("NO JOB ON YOUR HUD", "ACCEPT OR SELECT ONE BELOW", "", AvState.Inert);
                openEmpty.SetShown(openShown == 0);
                if (openShown == 0)
                {
                    string message = !model.Sources ? "NO JOB SOURCE RUNNING\nDynamic operations and Support are off in this mission." :
                        hudShown > 0 ? "NO OTHER JOBS OPEN\nNew work is drawn from the live battlefield as the front moves." :
                        MfdSecondaryObjectives.EmptyMessage(!model.Installed ? BoardEmptyReason.Unavailable : !secondaryFresh ? BoardEmptyReason.LinkLost : BoardEmptyReason.Ready);
                    int split = message.IndexOf('\n');
                    openEmpty.Set(split < 0 ? message : message.Substring(0, split), split < 0 ? "" : message.Substring(split + 1), "",
                        model.Installed && !secondaryFresh ? AvState.Caution : AvState.Inert);
                }
                openSection.SetCaption("EVERY SOURCE · NEAREST FIRST" + (openShown > 0 ? " · " + openShown + " OPEN" : ""));
            }

            private string Range(in JobRow job) =>
                hasSelf && job.HasSite ? MfdSecondaryObjectives.RangeBearing(selfX, selfZ, job.X, job.Z) : "";

            /// <summary>The pilot's own jobs first, then nearest first (an unknown distance last), then by source and id.</summary>
            private int CompareJobs(JobRow a, JobRow b)
            {
                if (a.Mine != b.Mine) return a.Mine ? -1 : 1;
                int byDistance = Metres(a).CompareTo(Metres(b));
                if (byDistance != 0) return byDistance;
                return a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Id.CompareTo(b.Id);
            }

            private float Metres(in JobRow job)
            {
                if (!hasSelf || !job.HasSite) return float.MaxValue;
                float dx = job.X - selfX, dz = job.Z - selfZ, d = dx * dx + dz * dz;
                return float.IsFinite(d) ? d : float.MaxValue;
            }

            // -------------------------------------------------------- OBJECTIVES page

            private void BuildObjectivesPage(AvFlow page)
            {
                tally = page.Add(new ObjectiveTallyPart(page.Content));
                checklistSection = page.Section(AvIcon.ListDetails, "TASK LIST");
                checklist = page.Add(new AvPagedStack<ChecklistRow>(page.Content, page.Ticker, ObjectivePageSize, 2f,
                    (parent, slot) => new ChecklistRow(parent), BindObjectiveRow));
                objectivesEmpty = page.Add(new AvRow(page.Content));
                objectivesEmpty.SetShown(false);
                typeSection = page.Section(AvIcon.Stack2, "BY TASK TYPE", "DONE / ISSUED");
                typeTally = page.Add(new TaskTypeTally(page.Content));
                page.Links(("FRONTS", "STR", "BOARD"), ("TARGETS", "TGT", "TARGETS"));
            }

            private void RenderObjectives()
            {
                bool any = model.Objectives.Count > 0;
                tally.SetShown(any);
                checklistSection.SetShown(any);
                checklist.SetShown(any);
                objectivesEmpty.SetShown(!any);
                typeSection.SetShown(any);
                typeTally.SetShown(any);
                if (!any)
                {
                    objectivesEmpty.Set(model.HasHq ? "NO TASKS ISSUED" : "TASK FEED OFFLINE",
                        "", "", AvState.Inert);
                    checklist.SetCount(0);
                    return;
                }
                typeTally.Tally(model.Objectives, o => o.Kind, o => o.Phase == MissionPhase.Done);

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
                typeSection.SetCaption(model.ObjectivesDone + " DONE / " + total + " ISSUED");
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

            // ------------------------------------------------------------- BRIEF page

            private void BuildBriefPage(AvFlow page)
            {
                hero = page.Add(new MissionHeroPart(page.Content));

                briefTiles = new[]
                {
                    new AvGauge(page.Content, "SCORE"),
                    new AvGauge(page.Content, "OBJECTIVES"),
                };
                briefTiles[0].Help = "Score: the faction's mission score. The ring shows progress toward the next escalation gate.";
                briefTiles[1].Help = "Objectives: completed against issued. The OBJECTIVES page lists them with distances.";
                page.Row(briefTiles);

                ladderSection = page.Section(MfdChromeIcon.For("CHART"), "ESCALATION");
                ladder = page.Add(new EscalationLadderPart(page.Content));
                page.Links(("WAR PLAN", "STR", "BOARD"), ("FUNDS", "FAC", "ECONOMY"));
            }

            private void RenderBrief()
            {
                hero.SetMission(model.MissionName, model.Mode, model.Clock == "—" ? "—" : "T+" + model.Clock, model.Brief, model.HasBrief);

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

                RenderLadder();
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

            // ---------------------------------------------------------------- render

            private void Render()
            {
                if (selectedPage == PageJobs) RenderJobs();
                RenderObjectives();
                RenderBrief();
            }

            // ---------------------------------------------------------------- gather

            private void Gather()
            {
                Mission mission = MissionManager.CurrentMission;
                MissionManager manager = NetworkSceneSingleton<MissionManager>.i;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                FactionHQ hq = map != null ? map.HQ : null;

                model.Clock = MissionClock(manager);
                if (mission == null)
                {
                    model.MissionName = "NO MISSION LOADED";
                    model.Brief = "STANDING BY FOR MISSION CONTROLLER";
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
                // Only while JOBS is up: reading the strike packages holds the SPACE feed open.
                if (selectedPage == PageJobs) GatherJobs();
            }

            private void GatherObjectives(Mission mission, FactionHQ hq)
            {
                model.Objectives.Clear();
                objectiveTypes.Clear();
                activeObjectives.Clear();
                model.ObjectivesDone = model.ObjectivesActive = 0;

                GlobalPosition from = Datum.originPosition.ToGlobalPosition();
                if (BoscaliSummer.Core.Game.GameAccess.TryGetActiveObjectives(hq, out List<Objective> active))
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

            /// <summary>Every source's jobs, sorted, plus the host's contract cards behind them (for the open card's detail).</summary>
            private void GatherJobs()
            {
                model.Contracts.Clear();
                secondaryFresh = actionPending = false;
                actionResult = "";
                model.Installed = ModuleServices.TryGet(out ISecondaryObjectivesView view);
                if (model.Installed)
                {
                    view.Refresh();
                    secondaryFresh = view.IsFresh;
                    actionPending = view.IsActionPending;
                    actionResult = view.ActionResult;
                    if (view.Objectives != null) foreach (SecondaryObjectiveView e in view.Objectives) if (e != null) model.Contracts.Add(e);
                }

                model.Jobs.Clear();
                bool operations = ModuleServices.TryGet(out IOperationJobs contracts), support = ModuleServices.TryGet(out ISupportJobs packages);
                model.Sources = operations || support;
                if (operations) contracts.CopyJobs(model.Jobs);
                if (support) packages.CopyJobs(model.Jobs);

                hasSelf = GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled && !aircraft.HasEjected();
                if (hasSelf)
                {
                    GlobalPosition self = aircraft.transform.position.ToGlobalPosition();
                    selfX = self.x; selfZ = self.z;
                }
                model.Jobs.Sort(jobOrder ??= CompareJobs);
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

            private static string MissionClock(MissionManager manager) =>
                manager == null ? "—" : AvNum.Clock(Mathf.Max(0, Mathf.FloorToInt(manager.MissionTime)));

            /// <summary>Objective-type icon: chrome (a real AvIcon), driven by the vanilla ObjectiveType.</summary>
            private static AvIcon ObjectiveIcon(string typeName)
            {
                switch (typeName)
                {
                    case "DestroyUnits": return AvIcon.Target;
                    case "CrashAircraft":
                    case "SuccessfulSortie": return AvIcon.Plane;
                    case "CaptureAirbase": return AvIcon.Flag;
                    case "ReachUnits":
                    case "ReachWaypoints": return AvIcon.MapPin;
                    case "WaitSeconds": return AvIcon.Clock;
                    case "CompleteOtherObjective": return AvIcon.CircleCheck;
                    case "SpotUnit": return AvIcon.Eye;
                    default: return AvIcon.Flag;
                }
            }

        }
    }
}
