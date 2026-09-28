using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Features.Command.Presentation;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.SavedMission;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ----------------------------------------------------------- mission panel

        private sealed class MissionPresenter : Presenter
        {
            private const int ObjectivePageSize = 6;
            private const int SecondaryPageSize = MfdSecondaryObjectives.MaxCards;

            private readonly List<Objective> objectives = new List<Objective>();
            private readonly List<string> objectiveTypes = new List<string>();
            private int objectiveSummarySignature;
            private bool hasObjectiveSummary;

            private AvChip[] chips;
            private AvFlow missionFlow, objectivesFlow, secondaryFlow;
            private int selectedPage;

            // Mission page.
            private MissionBriefPart briefPart;
            private AvSection ladderSection;
            private AvGauge ladderGauge;
            private readonly AvRow[] ladderRows = new AvRow[3];
            private AvRow contractPreviewRow;

            // Objectives page.
            private AvSection objectivesSection;
            private AvList objectiveList;

            // Secondary/contracts page.
            private AvSection boardSection;
            private AvControl[] secondaryFilterControls;
            private AvRow boardSummaryRow;
            private AvList secondaryList;
            private readonly Dictionary<AvRow, int> secondaryRowIndex = new Dictionary<AvRow, int>();
            private readonly Dictionary<AvRow, (AvControl Accept, AvControl Dismiss)> secondaryControls =
                new Dictionary<AvRow, (AvControl, AvControl)>();
            private readonly List<SecondaryObjectiveView> filtered = new List<SecondaryObjectiveView>(4);
            private MissionContractWindow contractWindow;
            private int secondaryPage;
            private int secondaryCount;
            private int secondaryActive;
            private int secondaryLimit;
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
                (AvIcon.Star, "SECONDARY"),
            };

            protected override void BuildContent()
            {
                chips = Console.Chips(3);
                missionFlow = CreatePage();
                objectivesFlow = CreatePage();
                secondaryFlow = CreatePage();
                BuildMissionPage(missionFlow);
                BuildObjectivesPage(objectivesFlow);
                BuildSecondaryPage(secondaryFlow);
            }

            protected override void RefreshContent()
            {
                Mission mission = MissionManager.CurrentMission;
                MissionManager manager = NetworkSceneSingleton<MissionManager>.i;
                RefreshMissionCopy(mission, manager);
                RefreshObjectives();
                RefreshSecondaryObjectives();

                chips[0].Set(objectives.Count + " PRIMARY", objectives.Count > 0 ? AvState.Ready : AvState.Inert);
                chips[1].Set(MfdSecondaryObjectives.ShortCount(secondaryActive, secondaryLimit) + " SECONDARY",
                    secondaryActive > 0 ? AvState.Ready : AvState.Inert);
                chips[2].Set(MissionClock(manager), manager != null ? AvState.Ready : AvState.Inert);
            }

            protected override string AmbientStatus() =>
                selectedPage == 2 ? secondaryStatus :
                objectives.Count == 0 ? "MISSION STATUS — NO ACTIVE OBJECTIVES" :
                "MISSION STATUS — " + objectives.Count + " ACTIVE OBJECTIVES";

            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                RequestRefresh();
            }

            // ------------------------------------------------------- MISSION page

            private void BuildMissionPage(AvFlow page)
            {
                page.Section(MfdChromeIcon.For("MISSION"), "MISSION BRIEF");
                AvCard card = page.Add(new AvCard(page.Content, page.Ticker, page.Inner));
                briefPart = card.Flow.Add(new MissionBriefPart(card.Flow.Content));

                ladderSection = page.Section(MfdChromeIcon.For("CHART"), "ESCALATION LADDER");
                ladderGauge = page.Add(new AvGauge(page.Content, "NEXT GATE", AvGaugeShape.Segments, 72f));
                ladderRows[0] = new AvRow(page.Content);
                ladderRows[1] = new AvRow(page.Content);
                ladderRows[2] = new AvRow(page.Content);
                // Stacked, not side by side: the rung names ("STRATEGIC NUCLEAR") and thresholds need the full width.
                foreach (AvRow rung in ladderRows) page.Add(rung);

                page.Section(MfdChromeIcon.For("FACTION"), "FACTION CONTRACTS", "SECONDARY MISSIONS");
                contractPreviewRow = page.Add(new AvRow(page.Content, () => SetSelectedTab(2)));
                page.Buttons(new AvControl.Spec("BROWSE CONTRACTS", () => SetSelectedTab(2),
                    AvButtonStyle.Default, AvIcon.ArrowUpRight));
            }

            private void RefreshMissionCopy(Mission mission, MissionManager manager)
            {
                string name, brief;
                bool hasBrief;
                if (mission == null)
                {
                    name = "NO MISSION LOADED";
                    brief = "WAITING FOR MISSION CONTROLLER DATA";
                    hasBrief = false;
                }
                else
                {
                    name = string.IsNullOrEmpty(mission.Name) ? "UNTITLED MISSION" : mission.Name;
                    hasBrief = mission.missionSettings != null &&
                               !string.IsNullOrWhiteSpace(mission.missionSettings.description);
                    brief = hasBrief ? mission.missionSettings.description : "NO BRIEFING FILED";
                }

                string meta = "MISSION TIME " + MissionClock(manager) + "   ·   " + ModeLabel(mission);
                briefPart.Set(name, brief, hasBrief, meta);
                RefreshEscalation(manager);
                missionFlow.RequestRelayout();
            }

            private void RefreshEscalation(MissionManager manager)
            {
                if (manager == null)
                {
                    ladderSection.SetCaption("ESCALATION DATA UNAVAILABLE");
                    ladderGauge.Set(0f, "—", AvState.Inert);
                    for (int i = 0; i < ladderRows.Length; i++)
                        ladderRows[i].Set(MfdMissionOverview.StageName(i), "—", "—", AvState.Inert);
                    return;
                }

                float current = Mathf.Max(0f, manager.currentEscalation);
                float tactical = manager.tacticalThreshold;
                float strategic = manager.strategicThreshold;
                int stage = MfdMissionOverview.Stage(current, tactical, strategic);
                ladderSection.SetCaption(MfdMissionOverview.Caption(current, tactical, strategic));

                float progress = MfdMissionOverview.NextGateProgress(current, tactical, strategic);
                AvState holding = stage == 2 ? AvState.Danger : stage == 1 ? AvState.Caution : AvState.Ready;
                ladderGauge.Set(progress, AvNum.Percent(progress), holding);

                for (int i = 0; i < ladderRows.Length; i++)
                {
                    bool set = i == 0 || (i == 1 ? tactical > 0f : strategic > 0f);
                    bool currentRung = i == stage;
                    AvState state = currentRung ? holding : i < stage ? AvState.Ready : AvState.Inert;
                    ladderRows[i].Set(MfdMissionOverview.StageName(i), MfdMissionOverview.Threshold(i, tactical, strategic),
                        MfdMissionOverview.StageState(i, stage, set), state);
                }
            }

            // ----------------------------------------------------- OBJECTIVES page

            private void BuildObjectivesPage(AvFlow page)
            {
                objectivesSection = page.Section(AvIcon.ListDetails, "ACTIVE OBJECTIVES");
                objectiveList = page.Add(new AvList(page.Content, page.Ticker, ObjectivePageSize, BindObjectiveRow));
            }

            private void RefreshObjectives()
            {
                objectives.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.HQ != null &&
                    MissionPosition.TryGetActiveObjectives(map.HQ, out List<Objective> active) && active != null)
                {
                    objectives.AddRange(active);
                }

                objectiveTypes.Clear();
                for (int i = 0; i < objectives.Count; i++)
                {
                    SavedObjective saved = objectives[i] == null ? null : objectives[i].SavedObjective;
                    objectiveTypes.Add(saved == null ? "" : saved.ObjectiveTypeEnum.ToString());
                }
                // The summary only depends on the type multiset in order: rebuild it when
                // that changes, not on every refresh.
                int signature = HashObjectiveTypes(objectiveTypes);
                if (!hasObjectiveSummary || signature != objectiveSummarySignature)
                {
                    hasObjectiveSummary = true;
                    objectiveSummarySignature = signature;
                    objectivesSection.SetCaption(MfdMissionLabels.TypeSummary(objectiveTypes));
                }

                objectiveList.SetCount(objectives.Count);
                objectivesFlow.RequestRelayout();
            }

            private void BindObjectiveRow(int index, AvRow row)
            {
                Objective objective = index >= 0 && index < objectives.Count ? objectives[index] : null;
                if (objective == null)
                {
                    row.Set("OBJECTIVE LINK LOST", "", "—", AvState.Caution);
                    return;
                }

                SavedObjective saved = objective.SavedObjective;
                string type = saved == null ? "" : saved.ObjectiveTypeEnum.ToString();
                string rawTitle = saved == null ? null : saved.DisplayName;
                string rawSource = saved == null ? "" : saved.UniqueName;
                string title = MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(rawTitle));
                string source = MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(rawSource));
                string detail = MfdMissionLabels.ObjectiveTypeLabel(type);
                if (!string.IsNullOrEmpty(source) && source != title) detail += "  ·  " + source;

                float fraction = MfdChartScale.Fraction(objective.CompletePercent, 1f);
                bool complete = objective.CompletePercent >= 0.999f;
                AvState state = complete ? AvState.Ready : fraction <= 0f ? AvState.Inert : AvState.Info;

                row.Set(AvIcons.Glyph(ObjectiveIcon(type)) + " " + title, detail,
                    complete ? "DONE" : AvNum.Percent(fraction), state);
            }

            // ------------------------------------------------------- SECONDARY page

            private void BuildSecondaryPage(AvFlow page)
            {
                boardSection = page.Section(AvIcon.Star, "CONTRACT BOARD");

                AvButtons filters = page.Buttons(
                    new AvControl.Spec("AVAILABLE", () => { secondaryFilter = MfdSecondaryObjectives.FilterAvailable; RequestRefresh(); }),
                    new AvControl.Spec("ACTIVE", () => { secondaryFilter = MfdSecondaryObjectives.FilterActive; RequestRefresh(); }),
                    new AvControl.Spec("CLOSED", () => { secondaryFilter = MfdSecondaryObjectives.FilterResults; RequestRefresh(); }));
                secondaryFilterControls = filters.Controls;

                boardSummaryRow = page.Add(new AvRow(page.Content));
                boardSummaryRow.AddTrailing(new AvControl.Spec("OPEN DESK", OpenContractDesk,
                    AvButtonStyle.Default, MfdChromeIcon.For("LEDGER")));

                secondaryList = page.Add(new AvList(page.Content, page.Ticker, SecondaryPageSize, BindSecondaryRow));
            }

            private void OpenContractDesk()
            {
                if (contractWindow == null) contractWindow = MissionContractWindow.Create();
                contractWindow.Show();
            }

            private void RefreshSecondaryObjectives()
            {
                bool installed = ModServices.TryGet(out ISecondaryObjectivesView view);
                IReadOnlyList<SecondaryObjectiveView> entries = null;
                bool streamed = false;
                if (installed)
                {
                    view.Refresh();
                    entries = view.Objectives;
                    streamed = !string.IsNullOrWhiteSpace(view.Status);
                    secondaryLimit = Math.Max(0, view.ActiveLimit);
                    secondaryStatus = streamed ? view.Status : "SECONDARY MISSIONS — WAITING FOR HOST";
                }
                else
                {
                    secondaryLimit = 0;
                    secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE — DIRECTOR DISABLED OR NOT INSTALLED";
                }

                int available = 0, results = 0;
                SecondaryObjectiveView leadOffer = null, leadActive = null;
                filtered.Clear();
                secondaryActive = 0;
                if (entries != null) foreach (SecondaryObjectiveView entry in entries)
                {
                    if (entry == null) continue;
                    if (entry.IsActive)
                    {
                        secondaryActive++;
                        if (leadActive == null) leadActive = entry;
                    }
                    else if (entry.IsOffered)
                    {
                        available++;
                        if (leadOffer == null) leadOffer = entry;
                    }
                    else results++;
                    if (secondaryFilter == MfdSecondaryObjectives.FilterAvailable ? entry.IsOffered :
                        secondaryFilter == MfdSecondaryObjectives.FilterActive ? entry.IsActive :
                        !entry.IsActive && !entry.IsOffered)
                        filtered.Add(entry);
                }
                MfdSecondaryObjectives.SortForDisplay(filtered, secondaryFilter);
                secondaryHasCapacity = secondaryLimit <= 0 || secondaryActive < secondaryLimit;

                secondaryFilterControls[0].Label = "AVAILABLE " + available;
                secondaryFilterControls[1].Label = "ACTIVE " + MfdSecondaryObjectives.ShortCount(secondaryActive, secondaryLimit);
                secondaryFilterControls[2].Label = "CLOSED " + results;
                for (int i = 0; i < secondaryFilterControls.Length; i++)
                    secondaryFilterControls[i].Latched = i == secondaryFilter;

                secondaryCount = filtered.Count;
                secondaryPage = MfdSecondaryObjectives.ClampPage(secondaryPage, secondaryCount, SecondaryPageSize);

                boardSection.SetCaption(secondaryCount == 0
                    ? MfdSecondaryObjectives.EmptyMessage(secondaryFilter, EmptyReason(installed, streamed, available, results))
                    : installed && streamed
                        ? (secondaryLimit > 0 ? "CAPACITY " + secondaryLimit + " MAX" : "DIRECTOR ONLINE")
                        : "WAITING FOR HOST");

                boardSummaryRow.Set(MfdSecondaryObjectives.BoardSummary(available, secondaryActive, secondaryLimit, results),
                    null, "", installed && streamed ? AvState.Ready : AvState.Caution);

                SecondaryObjectiveView preview = leadActive ?? leadOffer;
                if (preview != null)
                {
                    contractPreviewRow.Set(MfdSecondaryObjectives.TitleLine(preview.Id, preview.Title),
                        (preview.IsActive ? "IN FIELD" : preview.Target) +
                        (string.IsNullOrWhiteSpace(preview.AcceptedBy) ? "" : "  ·  TAKEN BY " + preview.AcceptedBy),
                        MfdSecondaryObjectives.PayoutLabel(preview), preview.IsActive ? AvState.Ready : AvState.Info);
                }
                else
                {
                    contractPreviewRow.Set(available + secondaryActive > 0 ? "REVIEW CONTRACTS" : "NO ACTIVE CONTRACTS",
                        "Open the contract board to review optional faction missions.", "", AvState.Inert);
                }

                secondaryList.SetCount(secondaryCount);
                secondaryFlow.RequestRelayout();
                missionFlow.RequestRelayout();
            }

            private static BoardEmptyReason EmptyReason(bool installed, bool streamed, int available, int results) =>
                !installed ? BoardEmptyReason.Unavailable
                : !streamed ? BoardEmptyReason.LinkLost
                : available + results == 0 ? BoardEmptyReason.DirectorExhausted
                : BoardEmptyReason.Ready;

            private void BindSecondaryRow(int index, AvRow row)
            {
                secondaryRowIndex[row] = index;
                if (!secondaryControls.ContainsKey(row))
                {
                    AvControl accept = row.AddTrailing(new AvControl.Spec("ACCEPT", () => AcceptSecondary(row),
                        AvButtonStyle.Primary, AvIcon.CircleCheck));
                    AvControl dismiss = row.AddTrailing(new AvControl.Spec("DISMISS", () => DismissSecondary(row),
                        AvButtonStyle.Danger, AvIcon.X));
                    secondaryControls[row] = (accept, dismiss);
                }
                (AvControl accept2, AvControl dismiss2) = secondaryControls[row];

                SecondaryObjectiveView objective = index >= 0 && index < filtered.Count ? filtered[index] : null;
                if (objective == null)
                {
                    row.Set("OBJECTIVE LINK LOST", "", "", AvState.Caution);
                    accept2.Interactable = false; accept2.Label = "ACCEPT";
                    dismiss2.Interactable = false; dismiss2.Label = "DISMISS";
                    return;
                }

                bool complete = objective.IsComplete;
                bool lapsed = !complete && objective.SecondsRemaining <= 0f;
                bool urgent = !complete && !lapsed && objective.SecondsRemaining <= MfdSecondaryObjectives.UrgentSeconds;
                AvState state = complete ? AvState.Ready : lapsed ? AvState.Danger : urgent ? AvState.Caution
                    : objective.IsActive ? AvState.Info : AvState.Inert;

                string family = MfdMissionLabels.ContractFamily(objective.Title);
                if (!string.IsNullOrWhiteSpace(objective.AcceptedBy)) family += "  /  TAKEN BY " + objective.AcceptedBy;
                float fraction = MfdChartScale.Fraction(objective.Progress, 1f);
                string sub = family + "  ·  " + (objective.IsOffered ? "AWAITING ACCEPTANCE" : objective.Status) +
                    "  ·  " + MfdSecondaryObjectives.ChipLabel(objective) +
                    "  ·  " + (complete ? "100%" : AvNum.Percent(fraction)) +
                    "\n" + objective.Description;

                row.Set(AvIcons.Glyph(ContractIcon(objective.Title)) + " " +
                    MfdSecondaryObjectives.TitleLine(objective.Id, objective.Title),
                    sub, complete ? "PAID" : MfdSecondaryObjectives.PayoutLabel(objective), state);

                bool acceptAllowed = MfdSecondaryObjectives.CanAccept(objective, secondaryHasCapacity);
                accept2.Interactable = acceptAllowed;
                accept2.Label = MfdSecondaryObjectives.AcceptLabel(objective, secondaryHasCapacity);
                bool dismissable = objective.IsOffered || objective.IsActive;
                dismiss2.Interactable = dismissable;
                dismiss2.Label = objective.IsActive
                    ? (secondaryConfirmId == objective.Id ? "CONFIRM ABORT" : "ABORT")
                    : "DISMISS";
            }

            private void AcceptSecondary(AvRow row)
            {
                if (!secondaryRowIndex.TryGetValue(row, out int index) || index < 0 || index >= filtered.Count) return;
                SecondaryObjectiveView objective = filtered[index];
                if (objective != null && MfdSecondaryObjectives.CanAccept(objective, secondaryHasCapacity) &&
                    ModServices.TryGet(out ISecondaryObjectivesView view))
                    view.RequestAccept(objective.Id);
                RequestRefresh();
            }

            private void DismissSecondary(AvRow row)
            {
                if (!secondaryRowIndex.TryGetValue(row, out int index) || index < 0 || index >= filtered.Count) return;
                SecondaryObjectiveView objective = filtered[index];
                if (objective == null) return;
                if (objective.IsActive && secondaryConfirmId != objective.Id)
                {
                    secondaryConfirmId = objective.Id;
                    RequestRefresh();
                    return;
                }
                if (ModServices.TryGet(out ISecondaryObjectivesView view)) view.RequestCancel(objective.Id);
                secondaryConfirmId = 0;
                RequestRefresh();
            }

            // ------------------------------------------------------------- helpers

            private static string ModeLabel(Mission mission)
            {
                if (mission == null || mission.missionSettings == null) return "MODE  —";
                switch (mission.missionSettings.playerMode)
                {
                    case PlayerMode.Singleplayer: return "MODE  SINGLEPLAYER";
                    case PlayerMode.Multiplayer: return "MODE  MULTIPLAYER";
                    default: return "MODE  SINGLE / MULTIPLAYER";
                }
            }

            private static string MissionClock(MissionManager manager) =>
                manager == null ? "—" : AvNum.Clock(Mathf.Max(0, Mathf.FloorToInt(manager.MissionTime)));

            private static int HashObjectiveTypes(List<string> types)
            {
                unchecked
                {
                    int hash = types.Count;
                    for (int i = 0; i < types.Count; i++)
                        hash = hash * 31 + (types[i] != null ? types[i].GetHashCode() : 0);
                    return hash;
                }
            }

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

            /// <summary>
            /// The mission brief block: name, brief body (italic when none filed) and a
            /// mission-time/mode meta line. A small local part built from kit v2 text
            /// primitives (AvCard has no mutable title slot for a value that changes at
            /// runtime — kit gap; see the slice report).
            /// </summary>
            private sealed class MissionBriefPart : AvPart
            {
                private readonly TMP_Text name, body, meta;
                private bool hasBrief = true;

                public MissionBriefPart(RectTransform parent)
                {
                    Rect = AvLay.Child(parent, "Brief");
                    name = AvText.Make(Rect, "Name", AvTextRole.Title, "LOADING MISSION", TextAlignmentOptions.TopLeft, true);
                    body = AvText.Make(Rect, "Body", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
                    meta = AvText.Make(Rect, "Meta", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
                    Restyle();
                }

                public void Set(string missionName, string briefText, bool hasBriefFlag, string metaText)
                {
                    name.text = missionName ?? "";
                    body.text = briefText ?? "";
                    body.fontStyle = hasBriefFlag ? FontStyles.Normal : FontStyles.Italic;
                    meta.text = metaText ?? "";
                    if (hasBriefFlag != hasBrief) { hasBrief = hasBriefFlag; Restyle(); }
                }

                public override float Measure(float width) =>
                    AvText.Height(name, width) + 6f + AvText.Height(body, width) + 8f + AvText.Height(meta, width) + 6f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    float nh = AvText.Height(name, s.W);
                    AvLay.Place(name.rectTransform, 0f, 0f, s.W, nh);
                    float bh = AvText.Height(body, s.W);
                    AvLay.Place(body.rectTransform, 0f, nh + 6f, s.W, bh);
                    AvLay.Place(meta.rectTransform, 0f, nh + 6f + bh + 8f, s.W, 16f);
                }

                public override void Restyle()
                {
                    name.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("title").Color, AvTheme.TextPrimary);
                    body.color = hasBrief
                        ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary)
                        : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Disabled);
                    meta.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
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
