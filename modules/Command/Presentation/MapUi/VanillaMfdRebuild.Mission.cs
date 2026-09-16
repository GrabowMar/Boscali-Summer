using System;
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
            private TMP_Text missionMode;
            private TMP_Text briefNote;
            private TMP_Text ladderCaption;
            private UnityEngine.UI.Image escalationFill;
            private UnityEngine.UI.Image escalationMarker;
            private UnityEngine.UI.Image escalationTacticalTick;
            private UnityEngine.UI.Image escalationStrategicTick;
            private TMP_Text escalationTacticalValue;
            private TMP_Text escalationStrategicValue;
            private readonly TMP_Text[] escalationStages = new TMP_Text[3];
            private readonly UnityEngine.UI.Image[] escalationStageMarks = new UnityEngine.UI.Image[3];
            private float escalationTrackX;
            private float escalationTrackWidth;
            private float escalationMarkerY;
            private ObjectiveBoard objectiveBoard;
            private TMP_Text objectiveSummary;
            private readonly List<string> objectiveTypes = new List<string>();
            private SecondaryObjectiveCard[] secondaryCards;
            private TMP_Text secondaryEmpty;
            private TMP_Text secondaryPageLabel;
            private TMP_Text boardSummary;
            private TMP_Text boardStatus;
            private AvButton secondaryPrevious;
            private AvButton secondaryNext;
            private int selectedPage;
            private int secondaryPage;
            private int secondaryCount;
            private int secondaryActive;
            private int secondaryLimit;
            private int secondaryFilter;
            private readonly List<SecondaryObjectiveView> filtered = new List<SecondaryObjectiveView>(4);
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
                Shell.DataBar.SetChip(1, MfdSecondaryObjectives.ShortCount(secondaryActive, secondaryLimit) + " SECONDARY", secondaryActive > 0);
                Shell.DataBar.SetChip(2, MissionClock(manager), manager != null);
            }

            protected override string AmbientStatus() =>
                selectedPage == 2 ? secondaryStatus :
                objectives.Count == 0 ? "MISSION STATUS — NO ACTIVE OBJECTIVES" :
                "MISSION STATUS — " + objectives.Count + " ACTIVE OBJECTIVES";

            private void BuildMissionPage(RectTransform page)
            {
                DrawSpine(page);
                float width = Shell.Body.width;
                float y = Heading(page, -AvTokens.Space1, width, "MISSION BRIEF");
                briefNote = AvStyled.Label(page,
                    new Rect(width * 0.52f, -AvTokens.Space1, width * 0.48f, 14f),
                    "SCROLL TO READ BRIEF", "section-title-note", align: TextAlignmentOptions.MidlineRight);

                // The brief takes every pixel the fixed blocks below it do not. The ladder
                // and the contract action are measured up from the page bottom, so the
                // card grows with the dock instead of leaving the lower half empty.
                float bottom = -Shell.Body.height;
                float buttonTop = bottom + AvTokens.Space1 + 44f;
                float captionTop = buttonTop + AvTokens.Space3 + 14f;
                float valueTop = captionTop + AvTokens.Space1 + 12f;
                float trackTop = valueTop + AvTokens.Space1 + 10f;
                float stageTop = trackTop + AvTokens.Space2 + 14f;
                float ladderTop = stageTop + AvTokens.Space5;
                float briefHeight = Mathf.Max(160f, y - ladderTop - AvTokens.Space6);

                AvKit.TacticalCard(page,
                    new Rect(AvTokens.Space3, y, width - AvTokens.Space3, briefHeight), AvTheme.RailInfo);
                missionName = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 14f, width - AvTokens.Space5, 56f),
                    "LOADING MISSION", "metric-value");
                missionName.enableWordWrapping = true;
                missionName.enableAutoSizing = true;
                missionName.fontSizeMin = 15f;
                missionName.fontSizeMax = 22f;
                AvKit.Rule(page, new Rect(AvTokens.Space4, y - 74f, width - AvTokens.Space5, 1f), AvTheme.Hairline);

                var viewport = new GameObject("BriefingScroll", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.UI.RectMask2D), typeof(UnityEngine.UI.ScrollRect)).GetComponent<RectTransform>();
                viewport.SetParent(page, false);
                // The viewport stops above the mission-time footer so a scrolled line is
                // never sliced in half by the footer band underneath it.
                AvKit.Place(viewport, new Rect(AvTokens.Space4, y - 82f, width - AvTokens.Space5,
                    Mathf.Max(40f, briefHeight - 132f)));
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
                missionDescription.enableWordWrapping = true;
                missionDescription.fontSize = 13f;
                missionDescription.overflowMode = TextOverflowModes.Overflow;
                missionDescription.color = AvTheme.TextPrimary;

                float footerTop = y - briefHeight + 26f;
                AvKit.Rule(page, new Rect(AvTokens.Space4, footerTop + 20f, width - AvTokens.Space5, 1f), AvTheme.Hairline);
                missionClock = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, footerTop, width * 0.5f - AvTokens.Space2, 16f),
                    "MISSION TIME  —", "row-sub");
                missionMode = AvStyled.Label(page,
                    new Rect(width * 0.5f, footerTop, width * 0.5f - AvTokens.Space5, 16f),
                    "MODE  —", "row-sub", align: TextAlignmentOptions.MidlineRight);

                Heading(page, ladderTop, width, "ESCALATION LADDER", "CURRENT THRESHOLD");
                string[] stages = { "CONVENTIONAL", "TACTICAL", "STRATEGIC" };
                float third = (width - AvTokens.Space3) / 3f;
                for (int i = 0; i < stages.Length; i++)
                {
                    float x = AvTokens.Space3 + i * third;
                    escalationStages[i] = AvStyled.Label(page, new Rect(x, stageTop, third, 14f),
                        stages[i], "section-title-note", align: TextAlignmentOptions.Center);
                    escalationStageMarks[i] = AvKit.Rule(page,
                        new Rect(x + third * 0.5f - 14f, stageTop - 17f, 28f, 2f), Color.clear);
                }

                var track = new Rect(AvTokens.Space3, trackTop, width - AvTokens.Space3, 10f);
                AvKit.Panel(page, track, AvTheme.SurfaceInert);
                AvKit.Outline(page, track, AvTheme.Frame);
                escalationTrackX = track.x + 1f;
                escalationTrackWidth = track.width - 2f;
                escalationMarkerY = track.y + 1f;
                escalationFill = AvKit.Panel(page,
                    new Rect(escalationTrackX, escalationMarkerY, 0f, 8f), AvTheme.Accent);
                escalationTacticalTick = AvKit.Rule(page,
                    new Rect(track.x + track.width / 3f, track.y, 1f, track.height), AvTheme.Frame);
                escalationStrategicTick = AvKit.Rule(page,
                    new Rect(track.x + track.width * 2f / 3f, track.y, 1f, track.height), AvTheme.Frame);
                escalationMarker = AvKit.Rule(page,
                    new Rect(escalationTrackX, escalationMarkerY, 2f, 12f), AvTheme.TextPrimary);

                escalationTacticalValue = AvStyled.Label(page,
                    new Rect(track.x + track.width / 3f - 40f, valueTop, 80f, 12f), "",
                    "section-title-note", align: TextAlignmentOptions.Center);
                escalationStrategicValue = AvStyled.Label(page,
                    new Rect(track.x + track.width * 2f / 3f - 40f, valueTop, 80f, 12f), "",
                    "section-title-note", align: TextAlignmentOptions.Center);
                ladderCaption = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, captionTop, width - AvTokens.Space3, 14f), "", "section-title-note");

                AvStyled.Button(page,
                    new Rect(AvTokens.Space3, buttonTop, width - AvTokens.Space3, 44f),
                    "BROWSE OPTIONAL CONTRACTS", "row-main", () => SelectPage(2), AvButtonStyle.Primary);
            }

            private void BuildObjectivesPage(RectTransform page)
            {
                DrawSpine(page);
                float width = Shell.Body.width;
                float y = Heading(page, -AvTokens.Space1, width, "ACTIVE OBJECTIVES");
                objectiveSummary = AvStyled.Label(page,
                    new Rect(width * 0.4f, -AvTokens.Space1, width * 0.6f, 14f),
                    "", "section-title-note", align: TextAlignmentOptions.MidlineRight);
                int rows = Mathf.Clamp(Mathf.FloorToInt((Shell.Body.height - 80f) / ObjectiveBoard.Pitch), 4, 11);
                objectiveBoard = new ObjectiveBoard(page, y, width, rows);
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
                float width = Shell.Body.width;
                float height = Shell.Body.height;
                float y = Heading(page, -AvTokens.Space1, width, "CONTRACT BOARD");
                // The heading's own note slot is static; the board writes the host's live
                // status there instead, so a director that is off or unanswered says so on
                // the page rather than only in the status strip.
                boardStatus = AvStyled.Label(page,
                    new Rect(width * 0.34f, -AvTokens.Space1, width * 0.66f - AvTokens.Space3, 14f),
                    "", "section-title-note", align: TextAlignmentOptions.MidlineRight);
                boardStatus.enableWordWrapping = false;
                boardStatus.overflowMode = TextOverflowModes.Ellipsis;

                string[] filters = { "AVAILABLE", "ACTIVE", "CLOSED" };
                float filterWidth = (width - AvTokens.Space3 - AvTokens.Gap * 2f) / 3f;
                for (int i = 0; i < filters.Length; i++)
                {
                    int index = i;
                    secondaryFilters[i] = AvStyled.Button(page,
                        new Rect(AvTokens.Space3 + i * (filterWidth + AvTokens.Gap), y, filterWidth, 40f),
                        filters[i], "row-main", () => { secondaryFilter = index; secondaryPage = 0; RequestRefresh(); }, AvButtonStyle.Tab);
                }
                y -= 48f;

                boardSummary = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 14f), "", "section-title-note");
                y -= 22f;

                // The grid takes the height the panel actually has: a shallow bezel shows
                // one dossier at a time, a tall one shows four, and the pager never moves.
                int rows = MfdSecondaryObjectives.RowsFor(height);
                float cardHeight = MfdSecondaryObjectives.CardHeightFor(height, rows);
                secondaryEmpty = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 100f),
                    "Waiting for the mission director.", "row-main");
                secondaryEmpty.enableWordWrapping = true;
                secondaryEmpty.richText = false;
                secondaryCards = new SecondaryObjectiveCard[rows];
                for (int i = 0; i < rows; i++)
                    secondaryCards[i] = new SecondaryObjectiveCard(page,
                        new Rect(AvTokens.Space3, y - i * MfdSecondaryObjectives.RowPitch,
                                 width - AvTokens.Space3, cardHeight), RequestRefresh);

                float pagerY = -(height - AvTokens.Space2 - AvTokens.RowHeight);
                AvButton[] pager = AvKit.Stepper(page, AvTokens.Space3, pagerY, width - AvTokens.Space3,
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
                bool installed = ModServices.TryGet(out ISecondaryObjectivesView view);
                IReadOnlyList<SecondaryObjectiveView> entries = null;
                if (installed)
                {
                    view.Refresh();
                    entries = view.Objectives;
                    secondaryLimit = Math.Max(0, view.ActiveLimit);
                    secondaryStatus = string.IsNullOrWhiteSpace(view.Status)
                        ? "SECONDARY MISSIONS — WAITING FOR HOST" : view.Status;
                }
                else
                {
                    secondaryLimit = 0;
                    secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE — DIRECTOR DISABLED OR NOT INSTALLED";
                }

                int available = 0, results = 0;
                filtered.Clear();
                secondaryActive = 0;
                if (entries != null) foreach (SecondaryObjectiveView entry in entries)
                {
                    if (entry == null) continue;
                    if (entry.IsActive) secondaryActive++;
                    else if (entry.IsOffered) available++;
                    else results++;
                    if (secondaryFilter == MfdSecondaryObjectives.FilterAvailable ? entry.IsOffered :
                        secondaryFilter == MfdSecondaryObjectives.FilterActive ? entry.IsActive :
                        !entry.IsActive && !entry.IsOffered)
                        filtered.Add(entry);
                }
                MfdSecondaryObjectives.SortForDisplay(filtered, secondaryFilter);

                secondaryFilters[0].SetText("AVAILABLE " + available);
                secondaryFilters[1].SetText("ACTIVE " + MfdSecondaryObjectives.ShortCount(secondaryActive, secondaryLimit));
                secondaryFilters[2].SetText("CLOSED " + results);
                for (int i = 0; i < secondaryFilters.Length; i++) secondaryFilters[i].SetLatched(i == secondaryFilter);

                secondaryCount = filtered.Count;
                secondaryPage = MfdSecondaryObjectives.ClampPage(secondaryPage, secondaryCount, secondaryCards.Length);
                bool hasCapacity = secondaryLimit <= 0 || secondaryActive < secondaryLimit;
                for (int i = 0; i < secondaryCards.Length; i++)
                {
                    int index = secondaryPage * secondaryCards.Length + i;
                    bool exists = index < secondaryCount;
                    secondaryCards[i].Root.gameObject.SetActive(exists);
                    if (exists) secondaryCards[i].Refresh(filtered[index], hasCapacity);
                }

                boardSummary.text = MfdSecondaryObjectives.BoardSummary(available, secondaryActive, secondaryLimit, results);
                boardStatus.text = secondaryStatus;
                boardStatus.color = installed && entries != null ? AvTheme.Dim : AvTheme.Warning;
                secondaryEmpty.gameObject.SetActive(secondaryCount == 0);
                secondaryEmpty.text = MfdSecondaryObjectives.EmptyMessage(secondaryFilter);
                int pageCount = MfdSecondaryObjectives.PageCount(secondaryCount, secondaryCards.Length);
                secondaryPageLabel.text = secondaryCount == 0 ? "NO SECONDARY MISSIONS" :
                    "PAGE " + (secondaryPage + 1) + " / " + pageCount + "  ·  " +
                    MfdSecondaryObjectives.ShortCount(secondaryActive, secondaryLimit) + " ACTIVE";
                secondaryPrevious.SetEnabled(secondaryPage > 0);
                secondaryNext.SetEnabled(secondaryPage + 1 < pageCount);
                secondaryPrevious.WithTooltip(secondaryPage > 0 ? "Previous secondary missions" : "First page of secondary missions");
                secondaryNext.WithTooltip(secondaryPage + 1 < pageCount ? "Next secondary missions" : "Last page of secondary missions");
            }

            /// <summary>
            /// One contract dossier: a family glyph well, the contract number and title,
            /// an urgency chip, the family/state line, a brief, the target and payoff
            /// block, progress, and the action row. Rail and chip carry the state so
            /// colour is never the only signal; the glyph names the contract family.
            ///
            /// The card is measured from its bottom edge up, so a shorter dossier drops
            /// the second brief line instead of pushing the actions off the page.
            /// </summary>
            private sealed class SecondaryObjectiveCard
            {
                private const float ActionHeight = 34f;
                private const float LinePitch = 18f;

                public readonly RectTransform Root;
                private readonly TMP_Text title;
                private readonly TMP_Text family;
                private readonly TMP_Text state;
                private readonly TMP_Text deadline;
                private readonly TMP_Text description;
                private readonly TMP_Text target;
                private readonly TMP_Text pay;
                private readonly TMP_Text effect;
                private readonly TMP_Text percent;
                private readonly MfdGlyph glyph;
                private readonly UnityEngine.UI.Image rail;
                private readonly UnityEngine.UI.Image deadlineFill;
                private readonly UnityEngine.UI.Image[] deadlineFrame;
                private readonly UnityEngine.UI.Image progressFill;
                private readonly float progressWidth;
                private readonly float valueWidth;
                private readonly AvButton accept;
                private readonly AvButton dismiss;
                private SecondaryObjectiveView current;
                private int confirmCancel;

                public SecondaryObjectiveCard(RectTransform parent, Rect area, System.Action refresh)
                {
                    float height = area.height;
                    float actionTop = -(height - 40f);
                    float barTop = -(height - 52f);
                    float effectTop = -(height - 70f);
                    float payTop = effectTop + LinePitch;
                    float targetTop = payTop + LinePitch;
                    float briefHeight = height >= 200f ? 30f : 15f;

                    Root = new GameObject("SecondaryObjective", typeof(RectTransform)).GetComponent<RectTransform>();
                    Root.SetParent(parent, false);
                    AvKit.Place(Root, area);
                    rail = AvKit.TacticalCard(Root, new Rect(0f, 0f, area.width, height), AvTheme.RailInfo).Rail;
                    float inner = area.width - AvTokens.Space3 * 2f;

                    var well = new Rect(AvTokens.Space3, -10f, 30f, 30f);
                    AvKit.Panel(Root, well, AvTheme.SurfaceInert, AvSprites.Control);
                    AvKit.Outline(Root, well, AvTheme.Frame);
                    var glyphGo = new GameObject("Glyph", typeof(RectTransform), typeof(MfdGlyph));
                    glyphGo.transform.SetParent(Root, false);
                    glyph = glyphGo.GetComponent<MfdGlyph>();
                    glyph.raycastTarget = false;
                    AvKit.Place(glyph.rectTransform, new Rect(AvTokens.Space3 + 6.5f, -16.5f, 17f, 17f));

                    title = AvStyled.Label(Root, new Rect(48f, -8f, area.width - 152f, 20f), "", "row-main");
                    title.fontSize = 15f;
                    title.fontStyle = FontStyles.Bold;
                    title.richText = false;
                    title.overflowMode = TextOverflowModes.Ellipsis;

                    var chip = new Rect(area.width - AvTokens.Space3 - 96f, -12f, 96f, 17f);
                    deadlineFill = AvKit.Panel(Root, chip, AvTheme.SurfaceInert, AvSprites.Control);
                    deadlineFrame = AvKit.Outline(Root, chip, AvTheme.Frame);
                    deadline = AvStyled.Label(Root, chip, "", "section-title-note", align: TextAlignmentOptions.Center);
                    deadline.characterSpacing = 2f;

                    family = AvStyled.Label(Root, new Rect(48f, -32f, area.width - 192f, 12f), "", "section-title-note");
                    family.richText = false;
                    state = AvStyled.Label(Root, new Rect(area.width - AvTokens.Space3 - 128f, -32f, 128f, 12f),
                        "", "section-title-note", align: TextAlignmentOptions.MidlineRight);

                    description = AvStyled.Label(Root, new Rect(AvTokens.Space3, -52f, inner, briefHeight), "", "row-sub");
                    description.fontSize = 11.5f;
                    description.richText = false;
                    description.overflowMode = TextOverflowModes.Ellipsis;

                    AvKit.Rule(Root, new Rect(AvTokens.Space3, -48f, inner, 1f), AvTheme.Hairline);

                    Key(targetTop, "TARGET");
                    Key(payTop, "PAY");
                    Key(effectTop, "EFFECT");
                    valueWidth = area.width - 78f;
                    target = Value(targetTop, 14f, 11.5f, AvTheme.TextPrimary);
                    pay = Value(payTop, 15f, 13f, AvTheme.Accent);
                    effect = Value(effectTop, 15f, 10.5f, AvTheme.Dim);

                    var track = new Rect(AvTokens.Space3, barTop, inner - 64f, 6f);
                    AvKit.Panel(Root, track, AvTheme.SurfaceInert);
                    AvKit.Outline(Root, track, AvTheme.Frame);
                    progressWidth = track.width - 2f;
                    progressFill = AvKit.Rule(Root, new Rect(track.x + 1f, track.y - 1f, 0f, 4f), AvTheme.RailInfo);
                    percent = AvStyled.Label(Root, new Rect(area.width - AvTokens.Space3 - 56f, barTop - 1f, 56f, 15f),
                        "", "row-value");

                    accept = AvStyled.Button(Root, new Rect(AvTokens.Space3, actionTop, (inner - AvTokens.Gap) * .6f, ActionHeight),
                        "ACCEPT CONTRACT", "row-main", () =>
                        {
                            if (current != null && ModServices.TryGet(out ISecondaryObjectivesView view)) view.RequestAccept(current.Id);
                            refresh();
                        }, AvButtonStyle.Primary);
                    dismiss = AvStyled.Button(Root, new Rect(AvTokens.Space3 + inner * .6f, actionTop, inner * .4f, ActionHeight),
                        "DISMISS", "row-main", () =>
                        {
                            if (current == null) return;
                            if (current.IsActive && confirmCancel != current.Id)
                            { confirmCancel = current.Id; dismiss.SetText("CONFIRM ABORT"); return; }
                            if (ModServices.TryGet(out ISecondaryObjectivesView view)) view.RequestCancel(current.Id);
                            confirmCancel = 0; refresh();
                        }, AvButtonStyle.Danger);
                }

                private void Key(float y, string text) =>
                    AvStyled.Label(Root, new Rect(AvTokens.Space3, y, 52f, 14f), text, "section-title-note");

                private TMP_Text Value(float y, float height, float size, Color color)
                {
                    TMP_Text label = AvStyled.Label(Root, new Rect(66f, y, valueWidth, height), "", "row-main");
                    label.fontSize = size;
                    label.color = color;
                    label.richText = false;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    label.enableWordWrapping = false;
                    return label;
                }

                public void Refresh(SecondaryObjectiveView objective, bool hasCapacity)
                {
                    if (current?.Id != objective?.Id) confirmCancel = 0;
                    current = objective;
                    accept.SetEnabled(objective != null && objective.IsOffered && hasCapacity);
                    accept.SetText(objective?.IsOffered == true ? hasCapacity ? "ACCEPT CONTRACT" : "ACTIVE LIMIT REACHED" : objective?.IsActive == true ? objective.HasMarker ? "TRACKED ON MAP" : "CONTACT LOST" : "CONTRACT ENDED");
                    dismiss.SetEnabled(objective != null && (objective.IsOffered || objective.IsActive));
                    dismiss.SetText(objective?.IsActive == true ? confirmCancel == objective.Id ? "CONFIRM ABORT" : "ABORT" : "DISMISS");

                    if (objective == null)
                    {
                        title.text = "OBJECTIVE LINK LOST";
                        family.text = "";
                        state.text = "WAITING FOR HOST";
                        state.color = AvTheme.Warning;
                        deadline.text = description.text = target.text = pay.text = effect.text = percent.text = "";
                        glyph.SetKind("flag", AvTheme.Warning);
                        SetTimer(AvTheme.Warning);
                        SetProgress(0f, AvTheme.Warning);
                        rail.color = AvTheme.Warning;
                        return;
                    }

                    float fraction = MfdChartScale.Fraction(objective.Progress, 1f);
                    bool complete = objective.IsComplete;
                    bool lapsed = !complete && objective.SecondsRemaining <= 0f;
                    bool urgent = !complete && !lapsed && objective.SecondsRemaining <= 60f;
                    Color color = complete ? AvTheme.RailReady :
                        lapsed ? AvTheme.Alert :
                        urgent ? AvTheme.Warning : objective.IsActive ? AvTheme.Accent : AvTheme.RailInfo;

                    title.text = "#" + objective.Id + "  " + objective.Title;
                    glyph.SetKind(MfdMissionLabels.ContractGlyph(objective.Title), color);
                    family.text = MfdMissionLabels.ContractFamily(objective.Title);
                    state.text = objective.IsOffered ? "AWAITING ACCEPTANCE" : objective.Status;
                    state.color = color;
                    deadline.text = MfdSecondaryObjectives.ChipLabel(objective);
                    SetTimer(color);
                    description.text = objective.Description;
                    target.text = objective.Target;
                    pay.text = MfdSecondaryObjectives.PayoutLabel(objective);
                    pay.color = color;
                    effect.text = objective.Reward;
                    percent.text = complete ? "100%" : (fraction * 100f).ToString("0") + "%";
                    SetProgress(fraction, color);
                    rail.color = color;
                }

                private void SetTimer(Color color)
                {
                    deadlineFill.color = new Color(color.r * 0.15f, color.g * 0.15f, color.b * 0.15f, 0.85f);
                    for (int i = 0; i < deadlineFrame.Length; i++)
                        deadlineFrame[i].color = new Color(color.r, color.g, color.b, 0.45f);
                    deadline.color = color;
                }

                private void SetProgress(float fraction, Color color)
                {
                    progressFill.color = color;
                    progressFill.rectTransform.sizeDelta = new Vector2(progressWidth * Mathf.Clamp01(fraction), 4f);
                    percent.color = color;
                }
            }

            private void RefreshMissionCopy(Mission mission, MissionManager manager)
            {
                if (mission == null)
                {
                    missionName.text = "NO MISSION LOADED";
                    missionDescription.text = "WAITING FOR MISSION CONTROLLER DATA";
                    missionDescription.fontStyle = FontStyles.Italic;
                    missionDescription.color = AvTheme.Disabled;
                }
                else
                {
                    missionName.text = string.IsNullOrEmpty(mission.Name) ? "UNTITLED MISSION" : mission.Name;
                    bool hasBrief = mission.missionSettings != null &&
                                    !string.IsNullOrWhiteSpace(mission.missionSettings.description);
                    missionDescription.text = hasBrief ? mission.missionSettings.description : "NO BRIEFING FILED";
                    missionDescription.fontStyle = hasBrief ? FontStyles.Normal : FontStyles.Italic;
                    missionDescription.color = hasBrief ? AvTheme.TextPrimary : AvTheme.Disabled;
                }

                missionClock.text = "MISSION TIME  " + MissionClock(manager);
                missionMode.text = ModeLabel(mission);
                float preferred = missionDescription.GetPreferredValues(
                    missionDescription.text, briefingScroll.viewport.rect.width, 0f).y;
                missionDescription.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(briefingScroll.viewport.rect.height, preferred));
                briefNote.text = preferred > briefingScroll.viewport.rect.height + 1f ? "SCROLL TO READ BRIEF" : "";
                RefreshEscalation(manager);
            }

            private void RefreshEscalation(MissionManager manager)
            {
                if (manager == null)
                {
                    ladderCaption.text = "ESCALATION DATA UNAVAILABLE";
                    escalationTacticalValue.text = escalationStrategicValue.text = "—";
                    escalationTacticalTick.enabled = escalationStrategicTick.enabled = false;
                    SetEscalationVisual(0f, AvTheme.RailInert, -1);
                    return;
                }

                float current = Mathf.Max(0f, manager.currentEscalation);
                float tactical = manager.tacticalThreshold;
                float strategic = manager.strategicThreshold;
                bool hasTactical = tactical > 0f;
                bool hasStrategic = strategic > 0f;

                int stage = MfdMissionOverview.Stage(current, tactical, strategic);
                Color color = stage == 2 ? AvTheme.Alert : stage == 1 ? AvTheme.Warning : AvTheme.Accent;
                SetEscalationVisual(MfdMissionOverview.Fraction(current, tactical, strategic), color, stage);

                escalationTacticalTick.enabled = hasTactical;
                escalationStrategicTick.enabled = hasStrategic;
                escalationTacticalValue.text = hasTactical ? tactical.ToString("N0") : "—";
                escalationStrategicValue.text = hasStrategic ? strategic.ToString("N0") : "—";
                ladderCaption.text = MfdMissionOverview.Caption(current, tactical, strategic);
            }

            private void SetEscalationVisual(float fraction, Color color, int stage)
            {
                fraction = Mathf.Clamp01(fraction);
                escalationFill.color = color;
                escalationFill.rectTransform.sizeDelta = new Vector2(fraction * escalationTrackWidth, 8f);
                escalationMarker.color = color;
                escalationMarker.rectTransform.anchoredPosition =
                    new Vector2(escalationTrackX + fraction * escalationTrackWidth - 1f, escalationMarkerY);
                for (int i = 0; i < escalationStages.Length; i++)
                {
                    bool active = i == stage;
                    escalationStages[i].color = active ? color : AvTheme.Dim;
                    escalationStageMarks[i].color = active ? color : Color.clear;
                }
            }

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
                objectiveSummary.text = MfdMissionLabels.TypeSummary(objectiveTypes);

                objectiveBoard.Bind(objectives);
            }

            private static string MissionClock(MissionManager manager)
            {
                if (manager == null) return "—";
                int seconds = Mathf.Max(0, Mathf.FloorToInt(manager.MissionTime));
                return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }

            /// <summary>
            /// Pooled objective rows in the instrument-stack language: a state rail, the
            /// objective-type glyph, the objective name, its source/type detail, a
            /// right-aligned figure and a progress rule. A fixed row count keeps a changing
            /// objective inventory from rebuilding the tree.
            /// </summary>
            private sealed class ObjectiveBoard
            {
                public const float Pitch = 58f;

                private readonly Row[] rows;
                private readonly TMP_Text empty;
                private readonly TMP_Text pageLabel;
                private readonly AvButton previous;
                private readonly AvButton next;
                private readonly RectTransform pagerRoot;
                private readonly int perPage;
                private int page;
                private List<Objective> data;

                public ObjectiveBoard(RectTransform parent, float y, float width, int rowCount)
                {
                    float usable = width - AvTokens.Space3;
                    perPage = Mathf.Max(1, rowCount);
                    rows = new Row[perPage];
                    empty = AvStyled.Label(parent, new Rect(AvTokens.Space3, y, usable, 30f),
                        "NO ACTIVE OBJECTIVES — THE FRONT IS QUIET", "row-sub");
                    for (int i = 0; i < perPage; i++)
                        rows[i] = new Row(parent, y - i * Pitch, usable);

                    float pagerY = y - perPage * Pitch - AvTokens.Space1;
                    var go = new GameObject("ObjectivePager", typeof(RectTransform));
                    pagerRoot = go.GetComponent<RectTransform>();
                    pagerRoot.SetParent(parent, false);
                    AvKit.Place(pagerRoot, new Rect(AvTokens.Space3, pagerY, usable, AvTokens.RowHeight));
                    AvButton[] pager = AvKit.Stepper(pagerRoot, 0f, 0f, usable, out pageLabel, Previous, Next);
                    previous = pager[0];
                    next = pager[1];
                }

                public void Bind(List<Objective> objectives)
                {
                    data = objectives;
                    int count = data == null ? 0 : data.Count;
                    page = Mathf.Clamp(page, 0, PageCount(count) - 1);
                    for (int i = 0; i < rows.Length; i++)
                    {
                        int index = page * perPage + i;
                        if (index >= count) { rows[i].Hide(); continue; }
                        Objective objective = data[index];
                        if (objective == null)
                        {
                            rows[i].Bind("OBJECTIVE LINK LOST", "", "—", 0f, AvTheme.Warning, "flag", false);
                            continue;
                        }

                        SavedObjective saved = objective.SavedObjective;
                        string type = saved == null ? "" : saved.ObjectiveTypeEnum.ToString();
                        string title = MfdSecondaryObjectives.PlainObjective(saved == null ? null : saved.DisplayName);
                        string source = saved == null ? "" : MfdSecondaryObjectives.PlainObjective(saved.UniqueName);
                        string detail = MfdMissionLabels.ObjectiveTypeLabel(type);
                        if (!string.IsNullOrEmpty(source) && source != title) detail += "  ·  " + source;

                        float fraction = MfdChartScale.Fraction(objective.CompletePercent, 1f);
                        bool complete = objective.CompletePercent >= 0.999f;
                        Color color = complete ? AvTheme.RailReady : fraction <= 0f ? AvTheme.RailInert : AvTheme.RailInfo;
                        rows[i].Bind(title, detail, (fraction * 100f).ToString("0") + "%",
                            fraction, color, MfdMissionLabels.ObjectiveGlyph(type), complete);
                    }

                    empty.gameObject.SetActive(count == 0);
                    int pages = PageCount(count);
                    pagerRoot.gameObject.SetActive(pages > 1);
                    pageLabel.text = count == 0 ? "" : (page + 1) + " / " + pages;
                    previous.SetEnabled(page > 0);
                    next.SetEnabled(page + 1 < pages);
                }

                private int PageCount(int count) => count <= 0 ? 1 : 1 + (count - 1) / perPage;

                private void Previous() { page--; Bind(data); }

                private void Next() { page++; Bind(data); }

                private sealed class Row
                {
                    private readonly GameObject root;
                    private readonly UnityEngine.UI.Image rail;
                    private readonly UnityEngine.UI.Image fill;
                    private readonly MfdGlyph glyph;
                    private readonly TMP_Text name;
                    private readonly TMP_Text detail;
                    private readonly TMP_Text value;
                    private readonly AvButton hover;
                    private readonly float trackWidth;

                    public Row(RectTransform parent, float y, float width)
                    {
                        root = new GameObject("ObjectiveRow", typeof(RectTransform));
                        var rect = root.GetComponent<RectTransform>();
                        rect.SetParent(parent, false);
                        AvKit.Place(rect, new Rect(AvTokens.Space3, y, width, Pitch - AvTokens.Gap));

                        rail = AvKit.Rule(rect, new Rect(0f, 3f, 3f, Pitch - AvTokens.Gap - 9f), AvTheme.RailInert);

                        var glyphGo = new GameObject("Glyph", typeof(RectTransform), typeof(MfdGlyph));
                        glyphGo.transform.SetParent(rect, false);
                        glyph = glyphGo.GetComponent<MfdGlyph>();
                        glyph.raycastTarget = false;
                        AvKit.Place(glyph.rectTransform, new Rect(12f, -15f, 17f, 17f));

                        name = AvStyled.Label(rect, new Rect(36f, -6f, width - 120f, 16f), "", "row-name");
                        value = AvStyled.Label(rect, new Rect(width - 78f, -6f, 62f, 16f), "", "row-value");
                        detail = AvStyled.Label(rect, new Rect(36f, -25f, width - 46f, 13f), "", "row-sub");
                        detail.enableWordWrapping = false;
                        detail.overflowMode = TextOverflowModes.Ellipsis;

                        trackWidth = width - 36f;
                        AvKit.Panel(rect, new Rect(36f, -(Pitch - 17f), trackWidth, 3f), AvTheme.SurfaceInert);
                        fill = AvKit.Rule(rect, new Rect(36f, -(Pitch - 17f), 0f, 3f), AvTheme.RailInert);
                        AvKit.Rule(rect, new Rect(0f, -(Pitch - AvTokens.Gap), width, 1f), AvTheme.Hairline);
                        hover = AvKit.HitButton(rect, new Rect(0f, 0f, width, Pitch - AvTokens.Gap), null);
                        root.SetActive(false);
                    }

                    public void Bind(string title, string sub, string figure, float fraction,
                                     Color color, string glyphKind, bool complete)
                    {
                        // The rail, glyph and rule carry state; an objective nothing has
                        // touched still reads its name and source at a readable contrast.
                        Color ink = !complete && fraction <= 0f ? AvTheme.Dim : color;
                        name.text = title ?? "";
                        detail.text = sub ?? "";
                        value.text = complete ? "DONE" : figure ?? "";
                        value.color = ink;
                        rail.color = color;
                        glyph.SetKind(glyphKind, ink);
                        fill.color = color;
                        fill.rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(fraction) * trackWidth, 3f);
                        hover.WithTooltip(string.IsNullOrEmpty(sub) ? title ?? "" : title + "  —  " + sub);
                        if (!root.activeSelf) root.SetActive(true);
                    }

                    public void Hide()
                    {
                        if (root.activeSelf) root.SetActive(false);
                    }
                }
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
