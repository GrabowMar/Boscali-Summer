using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
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
            private readonly List<Objective> objectives = new List<Objective>();
            private RectTransform[] pages;
            private TMP_Text missionName;
            private TMP_Text missionDescription;
            private UnityEngine.UI.ScrollRect briefingScroll;
            private TMP_Text missionClock;
            private TMP_Text escalation;
            private MfdPagingGrid objectiveGrid;
            private readonly UnityEngine.UI.Image[] escalationSteps = new UnityEngine.UI.Image[3];
            private SecondaryObjectiveCard[] secondaryCards;
            private TMP_Text secondaryEmpty;
            private TMP_Text secondaryPageLabel;
            private AvButton secondaryPrevious;
            private AvButton secondaryNext;
            private int selectedPage;
            private int secondaryPage;
            private int secondaryCount;
            private int secondaryActive;
            private int secondaryFilter;
            private readonly List<SecondaryObjectiveView> filtered = new List<SecondaryObjectiveView>(3);
            private readonly AvButton[] secondaryFilters = new AvButton[3];
            private string secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE";

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                ConfigureTabs(new[] { "MISSION", "OBJECTIVES", "SECONDARY" }, SelectPage);
                pages = new[] { CreatePage("Mission"), CreatePage("Objectives"), CreatePage("Secondary") };
                BuildMissionPage(pages[0]);
                BuildObjectivesPage(pages[1]);
                BuildSecondaryPage(pages[2]);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                Mission mission = MissionManager.CurrentMission;
                MissionManager manager = NetworkSceneSingleton<MissionManager>.i;
                RefreshMissionCopy(mission, manager);
                RefreshObjectives();
                RefreshSecondaryObjectives();

                Shell.DataBar.State.text = selectedPage == 2 ? "SECONDARY MISSIONS" : "MISSION OVERVIEW";
                Shell.DataBar.SetChip(0, objectives.Count + " PRIMARY", objectives.Count > 0);
                Shell.DataBar.SetChip(1, secondaryActive + " SECONDARY", secondaryActive > 0);
                Shell.DataBar.SetChip(2, MissionClock(manager), manager != null);
            }

            protected override string AmbientStatus() =>
                selectedPage == 2 ? secondaryStatus :
                objectives.Count == 0 ? "MISSION STATUS — NO ACTIVE OBJECTIVES" :
                "MISSION STATUS — " + objectives.Count + " ACTIVE OBJECTIVES";

            private void BuildMissionPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "MISSION BRIEF", "SCROLL TO READ BRIEF");
                float briefHeight = Mathf.Clamp(Shell.Body.height - 166f, 244f, 340f);
                AvKit.TacticalCard(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, briefHeight), AvTheme.RailInfo);
                missionName = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 14f, Shell.Body.width - AvTokens.Space5, 58f),
                    "LOADING MISSION", "metric-value");
                missionName.enableWordWrapping = true;
                missionName.enableAutoSizing = true;
                missionName.fontSizeMin = 16f;
                missionName.fontSizeMax = 22f;
                var viewport = new GameObject("BriefingScroll", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.UI.RectMask2D), typeof(UnityEngine.UI.ScrollRect)).GetComponent<RectTransform>();
                viewport.SetParent(page, false);
                AvKit.Place(viewport, new Rect(AvTokens.Space4, y - 76f, Shell.Body.width - AvTokens.Space5, briefHeight - 138f));
                viewport.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                briefingScroll = viewport.GetComponent<UnityEngine.UI.ScrollRect>();
                briefingScroll.viewport = viewport;
                briefingScroll.horizontal = false;
                briefingScroll.vertical = true;
                briefingScroll.inertia = false;
                briefingScroll.scrollSensitivity = 24f;
                briefingScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
                missionDescription = AvStyled.Label(viewport, new Rect(0f, 0f, viewport.rect.width, viewport.rect.height), "", "row-sub");
                briefingScroll.content = missionDescription.rectTransform;
                missionClock = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - briefHeight + 52f, Shell.Body.width - AvTokens.Space5, 18f),
                    "TIME —", "row-main");
                escalation = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - briefHeight + 26f, Shell.Body.width - AvTokens.Space5, 18f),
                    "ESCALATION —", "row-main");
                missionDescription.enableWordWrapping = true;
                missionDescription.fontSize = 13f;
                missionDescription.overflowMode = TextOverflowModes.Overflow;
                missionDescription.color = AvTheme.TextPrimary;
                y -= briefHeight + 20f;
                y = Heading(page, y, Shell.Body.width, "ESCALATION LADDER", "CURRENT THRESHOLD");
                string[] stages = { "CONVENTIONAL", "TACTICAL", "STRATEGIC" };
                float width = (Shell.Body.width-AvTokens.Space3-AvTokens.Gap*2f)/3f;
                for (int i = 0; i < stages.Length; i++)
                {
                    float x = AvTokens.Space3+i*(width+AvTokens.Gap);
                    AvStyled.Label(page, new Rect(x, y, width, 20f), stages[i], "row-sub");
                    escalationSteps[i] = AvKit.Rule(page, new Rect(x, y-24f, width, 5f), AvTheme.SurfaceRaised);
                }
                AvStyled.Button(page, new Rect(AvTokens.Space3, y - 42f, Shell.Body.width - AvTokens.Space3, 44f),
                    "BROWSE OPTIONAL CONTRACTS", "row-main", () => SelectPage(2), AvButtonStyle.Quiet);
            }

            private void BuildObjectivesPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "ACTIVE OBJECTIVES", "LIVE PROGRESS");
                int rows = Mathf.Clamp(Mathf.FloorToInt((Shell.Body.height - 80f) / 64f), 4, 9);
                objectiveGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, rows, readOnly: true, progressBars: true, rowHeight: 56f);
                for (int i = 0; i < rows; i++)
                {
                    TMP_Text label = objectiveGrid.ButtonAt(i).GetComponentInChildren<TMP_Text>();
                    label.richText = false; label.enableWordWrapping = true;
                    label.enableAutoSizing = false; label.fontSize = 14f;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                }
            }

            private void SelectPage(int selected)
            {
                selectedPage = selected;
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                SetSelectedTab(selected);
                RequestRefresh();
            }

            private void BuildSecondaryPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "CONTRACT BOARD", "ACCEPT / EXECUTE / COLLECT");
                string[] filters = { "AVAILABLE", "ACTIVE", "RESULTS" };
                float filterWidth = (Shell.Body.width - AvTokens.Space3 - AvTokens.Gap * 2f) / 3f;
                for (int i = 0; i < filters.Length; i++)
                {
                    int index = i;
                    secondaryFilters[i] = AvStyled.Button(page,
                        new Rect(AvTokens.Space3 + i * (filterWidth + AvTokens.Gap), y, filterWidth, 44f),
                        filters[i], "row-main", () => { secondaryFilter = index; secondaryPage = 0; RequestRefresh(); }, AvButtonStyle.Tab);
                }
                y -= 52f;
                secondaryCards = new SecondaryObjectiveCard[Shell.Body.height >= 600f ? 2 : 1];
                secondaryEmpty = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, 100f),
                    "Waiting for the mission director.", "row-main");
                secondaryEmpty.enableWordWrapping = true;
                secondaryEmpty.richText = false;
                for (int i = 0; i < secondaryCards.Length; i++)
                    secondaryCards[i] = new SecondaryObjectiveCard(page,
                        new Rect(AvTokens.Space3, y - i * (242f + AvTokens.Gap),
                                 Shell.Body.width - AvTokens.Space3, 242f), RequestRefresh);

                AvButton[] pager = AvKit.Stepper(page, AvTokens.Space3,
                    y - secondaryCards.Length * (242f + AvTokens.Gap), Shell.Body.width - AvTokens.Space3,
                    out secondaryPageLabel, () => ChangeSecondaryPage(-1), () => ChangeSecondaryPage(1));
                secondaryPrevious = pager[0];
                secondaryNext = pager[1];
            }

            private void ChangeSecondaryPage(int delta)
            {
                secondaryPage = MfdSecondaryObjectives.ClampPage(secondaryPage + delta, secondaryCount, secondaryCards.Length);
                RequestRefresh();
            }

            private void RefreshSecondaryObjectives()
            {
                IReadOnlyList<SecondaryObjectiveView> entries = null;
                if (ModServices.TryGet(out ISecondaryObjectivesView view))
                {
                    view.Refresh();
                    entries = view.Objectives;
                    secondaryStatus = string.IsNullOrWhiteSpace(view.Status)
                        ? "SECONDARY MISSIONS — WAITING FOR HOST" : view.Status;
                }
                else secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE — DIRECTOR DISABLED OR NOT INSTALLED";

                int available = 0, results = 0;
                filtered.Clear();
                secondaryActive = 0;
                if (entries != null) foreach (SecondaryObjectiveView entry in entries)
                {
                    if (entry == null) continue;
                    if (entry.IsActive) secondaryActive++;
                    else if (entry.IsOffered) available++;
                    else results++;
                    if (secondaryFilter == 0 ? entry.IsOffered : secondaryFilter == 1 ? entry.IsActive : !entry.IsActive && !entry.IsOffered)
                        filtered.Add(entry);
                }
                secondaryFilters[0].SetText("AVAILABLE " + available);
                secondaryFilters[1].SetText("ACTIVE " + secondaryActive + "/2");
                secondaryFilters[2].SetText("RESULTS " + results);
                for (int i = 0; i < secondaryFilters.Length; i++) secondaryFilters[i].SetLatched(i == secondaryFilter);
                entries = filtered;
                secondaryCount = filtered.Count;
                secondaryPage = MfdSecondaryObjectives.ClampPage(secondaryPage, secondaryCount, secondaryCards.Length);
                for (int i = 0; i < secondaryCards.Length; i++)
                {
                    int index = secondaryPage * secondaryCards.Length + i;
                    bool exists = index < secondaryCount;
                    secondaryCards[i].Root.gameObject.SetActive(exists);
                    if (exists) secondaryCards[i].Refresh(entries[index], secondaryActive < 2);
                }

                secondaryEmpty.gameObject.SetActive(secondaryCount == 0);
                secondaryEmpty.text = secondaryFilter == 0 ? "No offers right now. New contracts are drawn from the battlefield every 30 seconds.\n\n" + secondaryStatus :
                    secondaryFilter == 1 ? "No active contracts. Choose AVAILABLE and accept a mission to reveal its map marker." : "No recent results. Finished contracts remain here for 60 seconds.";
                int pageCount = MfdSecondaryObjectives.PageCount(secondaryCount, secondaryCards.Length);
                secondaryPageLabel.text = secondaryCount == 0 ? "NO SECONDARY MISSIONS" :
                    "PAGE " + (secondaryPage + 1) + " / " + pageCount + "  ·  " + secondaryActive + " ACTIVE";
                secondaryPrevious.SetEnabled(secondaryPage > 0);
                secondaryNext.SetEnabled(secondaryPage + 1 < pageCount);
                secondaryPrevious.WithTooltip(secondaryPage > 0 ? "Previous secondary missions" : "First page of secondary missions");
                secondaryNext.WithTooltip(secondaryPage + 1 < pageCount ? "Next secondary missions" : "Last page of secondary missions");
            }

            private sealed class SecondaryObjectiveCard
            {
                public readonly RectTransform Root;
                private readonly TMP_Text title;
                private readonly TMP_Text state;
                private readonly TMP_Text deadline;
                private readonly TMP_Text requirement;
                private readonly TMP_Text target;
                private readonly TMP_Text payment;
                private readonly TMP_Text reward;
                private readonly UnityEngine.UI.Image rail;
                private readonly UnityEngine.UI.Image progress;
                private readonly float progressWidth;
                private readonly AvButton accept;
                private readonly AvButton dismiss;
                private SecondaryObjectiveView current;
                private int confirmCancel;

                public SecondaryObjectiveCard(RectTransform parent, Rect area, System.Action refresh)
                {
                    Root = new GameObject("SecondaryObjective", typeof(RectTransform)).GetComponent<RectTransform>();
                    Root.SetParent(parent, false);
                    AvKit.Place(Root, area);
                    rail = AvKit.TacticalCard(Root, new Rect(0f, 0f, area.width, area.height), AvTheme.RailInfo).Rail;
                    float width = area.width - AvTokens.Space3 * 2f;
                    title = Label(-8f, width, 22f, "row-main");
                    title.fontSize = 17f;
                    state = Label(-34f, width - 108f, 16f, "row-sub");
                    deadline = AvStyled.Label(Root, new Rect(area.width - 116f, -34f, 104f, 16f),
                        "", "row-sub", align: TextAlignmentOptions.MidlineRight);
                    requirement = Label(-56f, width, 44f, "row-sub", wrap: true);
                    target = Label(-104f, width, 18f, "row-sub");
                    progress = AvKit.ProgressBar(Root, new Rect(AvTokens.Space3, -128f, width, 5f), 0f, AvTheme.RailInfo);
                    // The shared bar has no sprite: resize its rectangle instead of relying on fillAmount.
                    progress.type = UnityEngine.UI.Image.Type.Simple;
                    progressWidth = progress.rectTransform.sizeDelta.x;
                    payment = Label(-139f, width, 19f, "row-main");
                    reward = Label(-161f, width, 32f, "row-sub", wrap: true);
                    accept = AvStyled.Button(Root, new Rect(AvTokens.Space3, -196f, (width - AvTokens.Gap) * .6f, 40f),
                        "ACCEPT CONTRACT", "row-main", () =>
                        {
                            if (current != null && ModServices.TryGet(out ISecondaryObjectivesView view)) view.RequestAccept(current.Id);
                            refresh();
                        }, AvButtonStyle.Quiet);
                    dismiss = AvStyled.Button(Root, new Rect(AvTokens.Space3 + width * .6f, -196f, width * .4f, 40f),
                        "DISMISS", "row-main", () =>
                        {
                            if (current == null) return;
                            if (current.IsActive && confirmCancel != current.Id)
                            { confirmCancel = current.Id; dismiss.SetText("CONFIRM ABORT"); return; }
                            if (ModServices.TryGet(out ISecondaryObjectivesView view)) view.RequestCancel(current.Id);
                            confirmCancel = 0; refresh();
                        }, AvButtonStyle.Quiet);
                }

                private TMP_Text Label(float y, float width, float height, string style, bool wrap = false)
                {
                    TMP_Text text = AvStyled.Label(Root, new Rect(AvTokens.Space3, y, width, height), "", style);
                    text.richText = false;
                    text.enableWordWrapping = wrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    text.color = AvTheme.TextPrimary;
                    text.fontSize = 13f;
                    return text;
                }

                public void Refresh(SecondaryObjectiveView objective, bool hasCapacity)
                {
                    if (current?.Id != objective?.Id) confirmCancel = 0;
                    current = objective;
                    accept.SetEnabled(objective != null && objective.IsOffered && hasCapacity);
                    accept.SetText(objective?.IsOffered == true ? hasCapacity ? "ACCEPT CONTRACT" : "2 ACTIVE / LIMIT" : objective?.IsActive == true ? objective.HasMarker ? "TRACKED ON MAP" : "CONTACT LOST" : "CONTRACT ENDED");
                    dismiss.SetEnabled(objective != null && (objective.IsOffered || objective.IsActive));
                    dismiss.SetText(objective?.IsActive == true ? confirmCancel == objective.Id ? "CONFIRM ABORT" : "ABORT" : "DISMISS");
                    if (objective == null)
                    {
                        title.text = "OBJECTIVE LINK LOST";
                        state.text = "WAITING FOR HOST";
                        deadline.text = requirement.text = target.text = payment.text = reward.text = "";
                        progress.fillAmount = 0f;
                        rail.color = AvTheme.Warning;
                        return;
                    }
                    float fraction = MfdChartScale.Fraction(objective.Progress, 1f);
                    Color color = objective.IsComplete ? AvTheme.RailReady :
                        objective.SecondsRemaining <= 0f ? AvTheme.Warning : AvTheme.RailInfo;
                    title.text = "#" + objective.Id + "  " + objective.Title;
                    state.text = objective.IsOffered ? "AWAITING ACCEPTANCE" : objective.Status + "  ·  " + (fraction * 100f).ToString("0") + "%";
                    state.color = color;
                    deadline.text = MfdSecondaryObjectives.TimeLabel(objective.IsComplete, objective.SecondsRemaining).Replace("LEFT", objective.IsOffered ? "OFFER" : "LEFT");
                    requirement.text = objective.Description;
                    target.text = "TARGET  " + objective.Target;
                    payment.text = "REWARD  $" + objective.Money.ToString("N0") + "  +  " + objective.Xp.ToString("N0") + " XP";
                    reward.text = objective.Reward;
                    progress.fillAmount = fraction;
                    progress.rectTransform.sizeDelta = new Vector2(progressWidth * fraction, progress.rectTransform.sizeDelta.y);
                    progress.color = rail.color = color;
                }
            }

            private void RefreshMissionCopy(Mission mission, MissionManager manager)
            {
                if (mission == null)
                {
                    missionName.text = "NO MISSION LOADED";
                    missionDescription.text = "Waiting for mission controller data.";
                }
                else
                {
                    missionName.text = string.IsNullOrEmpty(mission.Name) ? "UNTITLED MISSION" : mission.Name;
                    missionDescription.text = mission.missionSettings == null ? "" :
                        string.IsNullOrWhiteSpace(mission.missionSettings.description) ? "No mission briefing supplied." :
                        mission.missionSettings.description;
                }

                missionClock.text = "MISSION TIME  " + MissionClock(manager);
                missionDescription.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(briefingScroll.viewport.rect.height,
                        missionDescription.GetPreferredValues(missionDescription.text, briefingScroll.viewport.rect.width, 0f).y));
                escalation.text = "ESCALATION    " + EscalationLabel(manager);
                int stage = manager == null ? -1 : manager.currentEscalation >= manager.strategicThreshold ? 2 :
                    manager.currentEscalation >= manager.tacticalThreshold ? 1 : 0;
                for (int i = 0; i < escalationSteps.Length; i++)
                    escalationSteps[i].color = i == stage ? AvTheme.Warning : AvTheme.SurfaceRaised;
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

                objectiveGrid.SetData(objectives.Count,
                    i => ObjectiveLabel(objectives[i]),
                    i => objectives[i] != null && objectives[i].CompletePercent >= 0.999f,
                    null, progress: i => objectives[i] == null ? 0f : objectives[i].CompletePercent);
            }

            private static string ObjectiveLabel(Objective objective)
            {
                if (objective == null) return "OBJECTIVE LINK LOST";
                string prefix = objective.CompletePercent >= 0.999f ? "DONE  " : (Mathf.Clamp01(objective.CompletePercent)*100f).ToString("0") + "%  ";
                    return prefix + MfdSecondaryObjectives.PlainObjective(objective.ToUIString(oneLine: true));
            }

            private static string EscalationLabel(MissionManager manager)
            {
                if (manager == null) return "LINK LOST";
                if (manager.currentEscalation >= manager.strategicThreshold) return "STRATEGIC";
                if (manager.currentEscalation >= manager.tacticalThreshold) return "TACTICAL";
                return "CONVENTIONAL";
            }

            private static string MissionClock(MissionManager manager)
            {
                if (manager == null) return "—";
                int seconds = Mathf.Max(0, Mathf.FloorToInt(manager.MissionTime));
                return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }
        }

        private sealed class UnavailablePresenter : Presenter
        {
            public UnavailablePresenter(MFDScreen screen, VanillaMfdPanelId id) : base(screen, id) { }

            protected override void BuildContent()
            {
                RectTransform page = CreatePage("Unavailable");
                DrawSpine(page);
                AvStyled.SpineTick(page, 3f, -AvTokens.Space3);
                AvStyled.Label(page, new Rect(AvTokens.Space3, -AvTokens.Space3,
                                               Shell.Body.width - AvTokens.Space3, 18f),
                               "NATIVE ADAPTER UNAVAILABLE", "section-title");
                AvStyled.Label(page, new Rect(AvTokens.Space3, -AvTokens.Space6,
                                               Shell.Body.width - AvTokens.Space3, 56f),
                               "This game build changed the controller attached to this MFD. " +
                               "The source panel remains intact and will be restored when the map closes.",
                               "row-sub");
            }

            protected override void RefreshContent()
            {
                Shell.DataBar.State.text = "COMPATIBILITY HOLD";
                Shell.DataBar.SetChip(0, "NATIVE", false);
                Shell.DataBar.SetChip(1, "ADAPTER", false);
                Shell.DataBar.SetChip(2, "CHECK LOG", false);
            }

            protected override string AmbientStatus() => "UPDATE COMPATIBILITY REQUIRED";
        }
    }
}
