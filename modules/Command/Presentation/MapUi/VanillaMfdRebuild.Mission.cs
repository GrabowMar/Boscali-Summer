using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
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
        /// LOCATE and its source's own act. BRIEF introduces the mission and its lead objective. TASKS keeps the checklist directly
        /// accessible even with a long briefing. GAMEMODE keeps the host's match report.
        /// Reads happen in <see cref="Gather"/> into one reusable <see cref="BoardModel"/>; <see cref="Render"/> only writes it to built parts.
        /// </summary>
        private sealed class MissionPresenter : Presenter
        {
            private const int ObjectivePageSize = 6;
            private const int PageJobs = 0, PageBrief = 1, PageMode = 2, PageTasks = 3;
            private const int HudPageSize = 2, OpenPageSize = 4;

            private struct ObjectiveLine
            {
                public string Title, Kind, Source;
                public AvIcon Icon;
                public float Fraction;
                /// <summary>Metres to the nearest fix; negative means unknown, hidden or not positioned.</summary>
                public float DistanceM;
                public MissionPhase Phase;
            }

            /// <summary>Everything the pages show, read once per refresh.</summary>
            private sealed class BoardModel
            {
                public string MissionName = "NO MISSION LOADED", Brief = "STANDING BY FOR MISSION CONTROLLER", Mode = "", Clock = "—";
                public bool HasBrief, HasHq;
                public float Score;
                public bool HasEscalation;
                public float Current, Tactical, Strategic;
                public readonly List<ObjectiveLine> Objectives = new List<ObjectiveLine>(24);
                public int ObjectivesDone, ObjectivesActive;
                public string ObjectiveSummary = "";
                /// <summary>The host's contract cards, for the detail of an open contract or sortie row.</summary>
                public readonly List<SecondaryObjectiveView> Contracts = new List<SecondaryObjectiveView>(8);
                public readonly List<JobRow> Jobs = new List<JobRow>(16);
                public bool Installed, Sources;
                public bool HasModeReport;
                public MissionDashboardStatus ModeReport;
            }

            private readonly BoardModel model = new BoardModel();
            private readonly List<string> objectiveTypes = new List<string>(24);
            private readonly HashSet<Objective> activeObjectives = new HashSet<Objective>();
            private readonly List<Objective> completed = new List<Objective>();
            private int objectiveSummarySignature;
            private bool hasObjectiveSummary;

            private int selectedPage;

            // JOBS page.
            private AvSection hudSection, openSection;
            private AvPagedStack<JobCard> hudJobs, openJobs;
#if !PUBLIC_RELEASE
            private readonly JobCard[] hudCards = new JobCard[HudPageSize], openCards = new JobCard[OpenPageSize];
#endif
            private AvRow openEmpty;
            private MissionTaskPlot taskPlot;
            private MissionTaskActions taskActions;
            private int openSlipId = -2;   // -2 = not chosen yet: the next render opens this pilot's own contract
            private int hudShown, openShown;
            private bool secondaryFresh, actionPending;
            private string actionResult = "";
            private float selfX, selfZ;
            private bool hasSelf;
            private Comparison<JobRow> jobOrder;

            // BRIEF page.
            private AvNote missionName, briefText, missionMeta;
            private AvRow leadObjective;
            private AvMetric scoreTile, taskTile;
            private AvSection ladderSection;
            private EscalationLadderPart ladder;
            private AvSection checklistSection;
            private AvPagedStack<ChecklistRow> checklist;
            private AvRow objectivesEmpty;

            // GAMEMODE page, supplied by the mission owner through the shared report.
            private AvSection modeObjectiveSection, modeRouteSection, modeDetailSection;
            private AvRow modePhase, modeScore, modeTimer;
            private AvNote modeObjective, modeRoute, modeDetail, modeEmpty;

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override (AvIcon Icon, string Label)[] TabItems { get; } = new[]
            {
                (AvIcon.Star, "JOBS"),
                (AvIcon.Flag, "BRIEF"),
                (AvIcon.Target, "GAMEMODE"),
                (AvIcon.ListDetails, "TASKS"),
            };

            protected override string[] TabTips { get; } = new[]
            {
                "Jobs: the one on your HUD, then every open job (contracts, staff sorties, strike packages), nearest first. LOCATE moves the map.",
                "Mission briefing, lead objective, faction score and escalation thresholds.",
                "Gamemode: current phase, team scores, time remaining, route and rules from the mission host.",
                "All mission objectives, with progress and distance. Open tasks appear first.",
            };

            protected override void BuildContent()
            {
                BoscaliSummer.Core.Ui.CountryStrip.Mount(Console);
                BuildJobsPage(CreatePage());
                BuildBriefPage(CreatePage());
                BuildModePage(CreatePage());
                BuildTasksPage(CreatePage());
                Console.Orders(PageJobs, "Select a job title to read its order. LOCATE shows its position on the map.");
                Console.Orders(PageBrief, "TASKS lists individual objectives and their progress.");
                Console.Orders(PageTasks, "Distances are measured to the nearest known objective position.");
                Console.Orders(PageMode, "The host supplies the match phase, scores and rules.");
            }

            protected override void RefreshContent()
            {
                Gather();
                Render();
            }

            protected override string AmbientStatus() =>
                selectedPage == PageMode ? (model.HasModeReport ? model.ModeReport.Ready ? model.ModeReport.Phase : "WAITING FOR HOST" : "NO SPECIAL GAMEMODE") :
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
                hudSection = page.Section(AvIcon.Bookmark, "YOUR JOBS", "ON HUD");
                hudJobs = page.Add(new AvPagedStack<JobCard>(page.Content, page.Ticker, HudPageSize, 6f,
                    (parent, slot) => BuildJob(parent, slot, true), BindJob));
                openSection = page.Section(AvIcon.ListDetails, "AVAILABLE JOBS");
                openJobs = page.Add(new AvPagedStack<JobCard>(page.Content, page.Ticker, OpenPageSize, 6f,
                    (parent, slot) => BuildJob(parent, slot, false), (index, card) => BindJob(hudShown + index, card)));
                openEmpty = page.Add(new AvRow(page.Content));
                // One plot and one action bar, handed to whichever contract is open.
                taskPlot = new MissionTaskPlot(page.Content); taskPlot.Rect.gameObject.SetActive(false);
                taskActions = new MissionTaskActions(page.Content, RequestRefresh); Console.Ticker.Register(taskActions);
                taskActions.Rect.gameObject.SetActive(false);
            }

            private JobCard BuildJob(RectTransform parent, int slot, bool mine)
            {
                var card = new JobCard(parent, Pick, Locate, Act, Release);
#if !PUBLIC_RELEASE
                (mine ? hudCards : openCards)[slot] = card;
#endif
                return card;
            }

            private void BindJob(int index, JobCard card)
            {
                JobRow job = model.Jobs[index];
                SecondaryObjectiveView entry = job.Kind == JobKind.StrikePackage ? null : model.Contracts.Find(c => c.Id == job.Id);
                bool expanded = entry != null && entry.Id == openSlipId;
                card.Set(job, Range(job), entry, secondaryFresh, expanded);
                if (expanded)
                {
                    card.Attach(taskPlot, taskActions);
                    taskActions.Set(entry, secondaryFresh, actionPending, actionResult);
                }
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
                if (selectedPage != PageJobs || index < 0 || index >= hudShown + openShown) return false;
                bool mine = index < hudShown;
                int item = mine ? index : index - hudShown;
                AvPagedStack<JobCard> stack = mine ? hudJobs : openJobs;
                stack.Reveal(item);
                JobCard card = (mine ? hudCards : openCards)[item % stack.PageSize];
                if (act ? string.IsNullOrEmpty(card.Row.Act) || !card.Row.ActLive : !card.Expandable) return false;
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

                hudShown = 0;
                while (hudShown < jobs.Count && jobs[hudShown].Mine) hudShown++;
                openShown = jobs.Count - hudShown;
                hudSection.SetShown(hudShown > 0);
                hudJobs.SetShown(hudShown > 0);
                openJobs.SetShown(openShown > 0);
                hudJobs.SetCount(hudShown);
                openJobs.SetCount(openShown);

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
                openSection.SetCaption(openShown > 0 ? openShown + " OPEN" + (hasSelf ? "  ·  NEAREST FIRST" : "") : "");
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

            // ------------------------------------------------------------- BRIEF page

            private void RenderObjectives()
            {
                bool any = model.Objectives.Count > 0;
                checklist.SetShown(any);
                objectivesEmpty.SetShown(!any);
                if (!any)
                {
                    checklistSection.SetCaption("");
                    objectivesEmpty.Set(model.HasHq ? "NO TASKS ISSUED" : "TASK FEED OFFLINE",
                        "", "", AvState.Inert);
                    checklist.SetCount(0);
                    return;
                }
                checklistSection.SetCaption(model.ObjectiveSummary);
                checklist.SetCount(model.Objectives.Count);
            }

            private void BindObjectiveRow(int index, ChecklistRow row)
            {
                if (index < 0 || index >= model.Objectives.Count)
                {
                    row.Set("OBJECTIVE LINK LOST", "", "—", 0f, false, AvIcon.X, AvState.Danger);
                    row.Help = null;
                    return;
                }

                ObjectiveLine line = model.Objectives[index];
                bool done = line.Phase == MissionPhase.Done;
                string detail = line.Kind;
                if (!string.IsNullOrEmpty(line.Source) && line.Source != line.Title) detail += "  ·  " + line.Source;
                if (!done && line.DistanceM >= 0f) detail += "  ·  " + AvNum.Percent(line.Fraction);
                string value = done ? "DONE" : line.DistanceM >= 0f ? MfdMissionBoard.Distance(line.DistanceM) : AvNum.Percent(line.Fraction);

                row.Set(line.Title, detail, value, line.Fraction, !done, done ? AvIcon.CircleCheck : line.Icon,
                    done ? AvState.Ready : AvState.Info);
                row.Help = line.Title + "  —  " + detail;
            }

            private void BuildBriefPage(AvFlow page)
            {
                var overview = page.Add(new AvCard(page.Content, page.Ticker, page.Inner, brackets: false, variant: "raised"));
                missionName = overview.Flow.Add(new AvNote(overview.Flow.Content, AvTextRole.Headline, emphasize: true));
                missionMeta = overview.Flow.Add(new AvNote(overview.Flow.Content, AvTextRole.DataSmall));
                scoreTile = new AvMetric(page.Content, "FACTION SCORE");
                taskTile = new AvMetric(page.Content, "TASKS COMPLETE");
                AvHelpTip.Attach(scoreTile.Rect.gameObject, "Faction score. The bar tracks progress to the next escalation threshold.");
                page.Row(scoreTile, taskTile);
                page.Section(AvIcon.Flag, "LEAD OBJECTIVE");
                leadObjective = page.Add(new AvRow(page.Content));
                page.Section(AvIcon.Clipboard, "BRIEFING");
                briefText = page.Add(new AvNote(page.Content, AvTextRole.Prose, emphasize: true));

                ladderSection = page.Section(MfdChromeIcon.For("CHART"), "ESCALATION");
                ladder = page.Add(new EscalationLadderPart(page.Content));
            }

            private void BuildTasksPage(AvFlow page)
            {
                checklistSection = page.Section(AvIcon.ListDetails, "MISSION OBJECTIVES");
                checklist = page.Add(new AvPagedStack<ChecklistRow>(page.Content, page.Ticker, ObjectivePageSize, 4f,
                    (parent, slot) => new ChecklistRow(parent), BindObjectiveRow));
                objectivesEmpty = page.Add(new AvRow(page.Content));
                objectivesEmpty.SetShown(false);
            }

            private void RenderBrief()
            {
                missionName.Set(model.MissionName);
                string clock = model.Clock == "—" ? "" : "T+" + model.Clock;
                missionMeta.Set(model.Mode.Length == 0 || clock.Length == 0 ? model.Mode + clock : model.Mode + "   /   " + clock);
                missionMeta.SetShown(missionMeta.Text.Length > 0);
                briefText.Set(model.Brief);

                int lead = LeadObjective();
                if (lead >= 0)
                {
                    ObjectiveLine line = model.Objectives[lead];
                    leadObjective.Set(line.Title, line.Kind + "  ·  " + AvNum.Percent(line.Fraction),
                        line.DistanceM >= 0f ? MfdMissionBoard.Distance(line.DistanceM) : "", AvState.Info);
                }
                else leadObjective.Set("No active objective", "", "", AvState.Inert);

                float progress = 0f;
                AvState holding = AvState.Ready;
                if (model.HasEscalation)
                {
                    int stage = MfdMissionOverview.Stage(model.Current, model.Tactical, model.Strategic);
                    holding = stage == 2 ? AvState.Danger : stage == 1 ? AvState.Caution : AvState.Ready;
                    progress = MfdMissionOverview.NextGateProgress(model.Current, model.Tactical, model.Strategic);
                }
                if (!model.HasHq) scoreTile.Set("—", "", 0f, AvState.Inert);
                else scoreTile.Set(AvNum.Fixed(model.Score, 1), "", progress, model.HasEscalation ? holding : AvState.Ready);
                int total = model.Objectives.Count;
                taskTile.Set(total > 0 ? model.ObjectivesDone + "/" + total : "—", "",
                    total > 0 ? model.ObjectivesDone / (float)total : 0f,
                    total == 0 ? AvState.Inert : model.ObjectivesActive > 0 ? AvState.Info : AvState.Ready);

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

            private void BuildModePage(AvFlow page)
            {
                page.Section(AvIcon.Target, "MATCH STATUS");
                modePhase = page.Add(new AvRow(page.Content));
                modeObjectiveSection = page.Section(AvIcon.Flag, "MATCH OBJECTIVE");
                modeObjective = page.Add(new AvNote(page.Content, AvTextRole.Prose, emphasize: true));
                modeScore = page.Add(new AvRow(page.Content));
                modeTimer = page.Add(new AvRow(page.Content));
                modeRouteSection = page.Section(AvIcon.MapPin, "ROUTE");
                modeRoute = page.Add(new AvNote(page.Content) { CollapseEmpty = true });
                modeDetailSection = page.Section(AvIcon.Clipboard, "MATCH RULES");
                modeDetail = page.Add(new AvNote(page.Content) { CollapseEmpty = true });
                modeEmpty = page.Add(new AvNote(page.Content, "This mission uses its standard briefing and task list. No special gamemode is running."));
            }

            private void RenderMode()
            {
                bool has = model.HasModeReport;
                modePhase.SetShown(has);
                modeEmpty.SetShown(!has);
                MissionDashboardStatus report = model.ModeReport;
                bool objective = has && !string.IsNullOrWhiteSpace(report.Objective);
                bool route = has && !string.IsNullOrWhiteSpace(report.Route);
                bool detail = has && !string.IsNullOrWhiteSpace(report.Detail);
                modeObjectiveSection.SetShown(objective); modeObjective.SetShown(objective);
                modeRouteSection.SetShown(route); modeRoute.SetShown(route);
                modeDetailSection.SetShown(detail); modeDetail.SetShown(detail);
                modeScore.SetShown(has && report.Ready && report.HasScores);
                modeTimer.SetShown(has && report.Ready && report.HasTimer);
                if (!has) return;
                AvState state = report.Ready ? AvState.Info : AvState.Caution;
                modePhase.Set(report.Name, report.Ready ? report.Phase : "WAITING FOR HOST", "", state);
                modeScore.Set("BDF  /  PALA", "TEAM SCORE", AvNum.Fixed(report.Score0, 0) + " : " + AvNum.Fixed(report.Score1, 0), AvState.Info);
                modeTimer.Set("TIME REMAINING", "", AvNum.Clock(report.RemainingSeconds), AvState.Info);
                modeObjective.Set(report.Objective);
                modeRoute.Set(report.Route);
                modeDetail.Set(report.Detail);
            }

            private void Render()
            {
                if (selectedPage == PageJobs) RenderJobs();
                else if (selectedPage == PageBrief) RenderBrief();
                else if (selectedPage == PageTasks) RenderObjectives();
                else RenderMode();
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

                if (selectedPage == PageBrief || selectedPage == PageTasks) GatherObjectives(mission, hq);
                if (selectedPage == PageMode)
                {
                    model.ModeReport = default;
                    model.HasModeReport = ModuleServices.TryGet(out IMissionDashboardView view) && view.TryRead(out model.ModeReport);
                }
                // Only while JOBS is up: reading the strike packages holds the SPACE feed open.
                if (selectedPage == PageJobs) GatherJobs();
            }

            private void GatherObjectives(Mission mission, FactionHQ hq)
            {
                model.Objectives.Clear();
                objectiveTypes.Clear();
                activeObjectives.Clear();
                completed.Clear();
                model.ObjectivesDone = model.ObjectivesActive = 0;

                GlobalPosition from = Datum.originPosition.ToGlobalPosition();
                if (BoscaliSummer.Core.Game.GameAccess.TryGetActiveObjectives(hq, out List<Objective> active))
                {
                    for (int i = 0; i < active.Count; i++)
                    {
                        Objective objective = active[i];
                        if (objective == null || objective.SavedObjective == null) continue;
                        if (!activeObjectives.Add(objective)) continue;
                        bool complete = objective.Status == ObjectiveStatus.Complete || objective.CompletePercent >= 0.999f;
                        if (complete) completed.Add(objective);
                        else AddObjectiveLine(objective, false, from);
                    }
                }
                foreach (Objective objective in completed) AddObjectiveLine(objective, true, from);

                MissionObjectives runtime = mission == null ? null : mission.RuntimeObjectives;
                if (runtime != null && runtime.AllObjectives != null)
                {
                    List<Objective> all = runtime.AllObjectives;
                    for (int i = 0; i < all.Count; i++)
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
                    Title = string.IsNullOrWhiteSpace(title) ? source : title, Source = source,
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
                manager == null || !MissionTimeSource.TryNow(out float time) ? "—" : AvNum.Clock(Mathf.Max(0, Mathf.FloorToInt(time)));

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
                    case "CompleteOtherObjective": return AvIcon.Link;
                    case "SpotUnit": return AvIcon.Eye;
                    default: return AvIcon.Flag;
                }
            }

        }
    }
}
