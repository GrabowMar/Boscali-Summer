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
            private TMP_Text missionClock;
            private TMP_Text escalation;
            private MfdPagingGrid objectiveGrid;
            private readonly UnityEngine.UI.Image[] escalationSteps = new UnityEngine.UI.Image[3];
            private readonly SecondaryObjectiveCard[] secondaryCards =
                new SecondaryObjectiveCard[MfdSecondaryObjectives.CardsPerPage];
            private TMP_Text secondaryEmpty;
            private TMP_Text secondaryPageLabel;
            private AvButton secondaryPrevious;
            private AvButton secondaryNext;
            private int selectedPage;
            private int secondaryPage;
            private int secondaryCount;
            private int secondaryActive;
            private string secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE";

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "MISSION", "OBJECTIVES", "SECONDARY" }, SelectPage);
                pages = new[] { Shell.CreatePage("Mission"), Shell.CreatePage("Objectives"), Shell.CreatePage("Secondary") };
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
                                  "MISSION BRIEF", "LIVE OPERATIONS FEED");
                AvKit.TacticalCard(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, 244f), AvTheme.RailInfo);
                missionName = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 14f, Shell.Body.width - AvTokens.Space5, 58f),
                    "LOADING MISSION", "metric-value");
                missionName.enableWordWrapping = true;
                missionName.enableAutoSizing = true;
                missionName.fontSizeMin = 16f;
                missionName.fontSizeMax = 22f;
                missionDescription = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 80f, Shell.Body.width - AvTokens.Space5, 98f),
                    "", "row-sub");
                missionClock = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 190f, Shell.Body.width - AvTokens.Space5, 18f),
                    "TIME —", "row-main");
                escalation = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 216f, Shell.Body.width - AvTokens.Space5, 18f),
                    "ESCALATION —", "row-main");
                missionDescription.enableWordWrapping = true;
                y -= 264f;
                y = Heading(page, y, Shell.Body.width, "ESCALATION LADDER", "CURRENT THRESHOLD");
                string[] stages = { "CONVENTIONAL", "TACTICAL", "STRATEGIC" };
                float width = (Shell.Body.width-AvTokens.Space3-AvTokens.Gap*2f)/3f;
                for (int i = 0; i < stages.Length; i++)
                {
                    float x = AvTokens.Space3+i*(width+AvTokens.Gap);
                    AvStyled.Label(page, new Rect(x, y, width, 20f), stages[i], "row-sub");
                    escalationSteps[i] = AvKit.Rule(page, new Rect(x, y-24f, width, 5f), AvTheme.SurfaceRaised);
                }
            }

            private void BuildObjectivesPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "ACTIVE OBJECTIVES", "LIVE PROGRESS");
                objectiveGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 9, readOnly: true, progressBars: true);
            }

            private void SelectPage(int selected)
            {
                selectedPage = selected;
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                Shell.SetSelectedTab(selected);
                RequestRefresh();
            }

            private void BuildSecondaryPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "SECONDARY MISSIONS", "FACTION-WIDE / AUTOMATIC");
                secondaryEmpty = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, 100f),
                    "Waiting for the mission director.", "row-main");
                secondaryEmpty.enableWordWrapping = true;
                secondaryEmpty.richText = false;
                for (int i = 0; i < secondaryCards.Length; i++)
                    secondaryCards[i] = new SecondaryObjectiveCard(page,
                        new Rect(AvTokens.Space3, y - i * (172f + AvTokens.Gap),
                                 Shell.Body.width - AvTokens.Space3, 172f));

                AvButton[] pager = AvKit.Stepper(page, AvTokens.Space3,
                    y - secondaryCards.Length * (172f + AvTokens.Gap), Shell.Body.width - AvTokens.Space3,
                    out secondaryPageLabel, () => ChangeSecondaryPage(-1), () => ChangeSecondaryPage(1));
                secondaryPrevious = pager[0];
                secondaryNext = pager[1];
            }

            private void ChangeSecondaryPage(int delta)
            {
                secondaryPage = MfdSecondaryObjectives.ClampPage(secondaryPage + delta, secondaryCount);
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

                secondaryCount = entries?.Count ?? 0;
                secondaryActive = 0;
                for (int i = 0; i < secondaryCount; i++)
                    if (entries[i] != null && !entries[i].IsComplete && entries[i].SecondsRemaining > 0f) secondaryActive++;
                secondaryPage = MfdSecondaryObjectives.ClampPage(secondaryPage, secondaryCount);
                for (int i = 0; i < secondaryCards.Length; i++)
                {
                    int index = secondaryPage * secondaryCards.Length + i;
                    bool exists = index < secondaryCount;
                    secondaryCards[i].Root.gameObject.SetActive(exists);
                    if (exists) secondaryCards[i].Refresh(entries[index]);
                }

                secondaryEmpty.gameObject.SetActive(secondaryCount == 0);
                secondaryEmpty.text = secondaryStatus;
                int pageCount = MfdSecondaryObjectives.PageCount(secondaryCount);
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

                public SecondaryObjectiveCard(RectTransform parent, Rect area)
                {
                    Root = new GameObject("SecondaryObjective", typeof(RectTransform)).GetComponent<RectTransform>();
                    Root.SetParent(parent, false);
                    AvKit.Place(Root, area);
                    rail = AvKit.TacticalCard(Root, new Rect(0f, 0f, area.width, area.height), AvTheme.RailInfo).Rail;
                    float width = area.width - AvTokens.Space3 * 2f;
                    title = Label(-8f, width, 22f, "row-main");
                    title.fontSize = 15f;
                    state = Label(-34f, width - 108f, 16f, "row-sub");
                    deadline = AvStyled.Label(Root, new Rect(area.width - 116f, -34f, 104f, 16f),
                        "", "row-sub", align: TextAlignmentOptions.MidlineRight);
                    requirement = Label(-54f, width, 30f, "row-sub", wrap: true);
                    target = Label(-88f, width, 16f, "row-sub");
                    progress = AvKit.ProgressBar(Root, new Rect(AvTokens.Space3, -109f, width, 5f), 0f, AvTheme.RailInfo);
                    payment = Label(-119f, width, 17f, "row-main");
                    reward = Label(-140f, width, 26f, "row-sub", wrap: true);
                }

                private TMP_Text Label(float y, float width, float height, string style, bool wrap = false)
                {
                    TMP_Text text = AvStyled.Label(Root, new Rect(AvTokens.Space3, y, width, height), "", style);
                    text.richText = false;
                    text.enableWordWrapping = wrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    text.color = AvTheme.TextPrimary;
                    return text;
                }

                public void Refresh(SecondaryObjectiveView objective)
                {
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
                    title.text = objective.Title;
                    state.text = objective.Status + "  ·  " + (fraction * 100f).ToString("0") + "%";
                    state.color = color;
                    deadline.text = MfdSecondaryObjectives.TimeLabel(objective.IsComplete, objective.SecondsRemaining);
                    requirement.text = objective.Description;
                    target.text = "TARGET  " + objective.Target;
                    payment.text = "REWARD  $" + objective.Money.ToString("N0") + "  +  " + objective.Xp.ToString("N0") + " XP";
                    reward.text = objective.Reward;
                    progress.fillAmount = fraction;
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
                return prefix + AvTheme.Truncate(objective.ToUIString(oneLine: true)
                    .Replace("\n", " ").ToUpperInvariant(), 34);
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
                RectTransform page = Shell.CreatePage("Unavailable");
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
