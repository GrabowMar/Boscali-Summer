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
            /// <summary>Fixed card furniture above and below the brief text.</summary>
            private const float BriefCardChrome = 118f;
            /// <summary>Name and footer measured from the card's edges; the rest is viewport.</summary>
            private const float BriefViewportInset = 100f;

            private readonly List<Objective> objectives = new List<Objective>();
            private RectTransform[] pages;
            private RectTransform missionPage;
            private RectTransform briefCard;
            private TMP_Text missionName;
            private TMP_Text missionDescription;
            private UnityEngine.UI.ScrollRect briefingScroll;
            private TMP_Text missionClock;
            private TMP_Text missionMode;
            private TMP_Text briefNote;
            private SectionHead ladderHead;
            private readonly UnityEngine.UI.Image[] ladderCards = new UnityEngine.UI.Image[3];
            private readonly UnityEngine.UI.Image[] ladderRails = new UnityEngine.UI.Image[3];
            private readonly UnityEngine.UI.Image[] ladderMarkers = new UnityEngine.UI.Image[3];
            private readonly TMP_Text[] ladderNames = new TMP_Text[3];
            private readonly TMP_Text[] ladderThresholds = new TMP_Text[3];
            private readonly TMP_Text[] ladderStates = new TMP_Text[3];
            private TMP_Text ladderProgressLabel;
            private readonly UnityEngine.UI.Image[] ladderProgressSegments = new UnityEngine.UI.Image[12];
            private float ladderProgressWidth;
            private SectionHead contractHead;
            private RectTransform contractCard;
            private UnityEngine.UI.Image contractRail;
            private TMP_Text contractSummary;
            private TMP_Text contractDetail;
            private AvButton browseContracts;
            private float briefCardHeight = 170f;
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
            private MissionContractWindow contractWindow;
            private int selectedPage;
            private int secondaryPage;
            private int secondaryCount;
            private int secondaryActive;
            private int secondaryLimit;
            private int secondaryFilter;
            private readonly List<SecondaryObjectiveView> filtered = new List<SecondaryObjectiveView>(4);
            private readonly AvButton[] secondaryFilters = new AvButton[3];
            private string secondaryStatus = "SECONDARY MISSIONS UNAVAILABLE";
            private string previewedOfferCopy;

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override int TabCount => 3;
            protected override bool PageHasTitle => false;

            protected override void BuildContent()
            {
                ConfigureTabs(new[] { "MISSION", "OBJECTIVES", "SECONDARY" }, SelectPage);
                pages = new[] { CreatePage("Mission"), CreatePage("Objectives"), CreatePage("Contracts") };
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

                Shell.DataBar.State.text = selectedPage == 0 ? "THEATER / MISSION" :
                    selectedPage == 1 ? "THEATER / OBJECTIVES" : "THEATER / CONTRACTS";
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
                missionPage = page;
                DrawSpine(page);
                float width = PageWidth;

                Heading(page, -AvTokens.Space1, width, "MISSION BRIEF");
                briefNote = AvStyled.Label(page,
                    new Rect(width * 0.52f, -AvTokens.Space1, width * 0.48f, 14f),
                    "", "section-title-note", align: TextAlignmentOptions.MidlineRight);

                // The brief card is a container: its fill and rail stretch with it, the
                // name and rule are pinned to its top, and the clock/mode footer is pinned
                // to its bottom. The layout pass then sizes the card to its copy, so a
                // two-line brief does not sit in a card with an empty middle.
                var cardGo = new GameObject("BriefCard", typeof(RectTransform));
                briefCard = cardGo.GetComponent<RectTransform>();
                briefCard.SetParent(page, false);
                UnityEngine.UI.Image cardFill = AvKit.Panel(briefCard, new Rect(0f, 0f, 0f, 0f),
                    AvTheme.Unity(AvTokens.Surface), AvSprites.Card);
                AvKit.Stretch(cardFill.rectTransform);
                UnityEngine.UI.Image cardRail = AvKit.Rule(briefCard, new Rect(0f, 0f, 0f, 0f),
                    AvTheme.RailInfo);
                StretchVertical(cardRail.rectTransform, 0f, 3f);
                UnityEngine.UI.Image cardFrame = AvKit.Panel(briefCard, new Rect(0f, 0f, 0f, 0f),
                    AvTheme.Hairline, AvSprites.ControlFrame);
                AvKit.Stretch(cardFrame.rectTransform);

                missionName = AvStyled.Label(briefCard,
                    new Rect(AvTokens.Space4, -12f, width - AvTokens.Space3 - AvTokens.Space4 - AvTokens.Space5, 40f),
                    "LOADING MISSION", "metric-value");
                missionName.enableWordWrapping = true;
                // The identity line shrinks to the micro floor before it is ever cut; the
                // two-line box catches anything the smaller face still cannot fit.
                missionName.overflowMode = TextOverflowModes.Overflow;
                missionName.enableAutoSizing = true;
                missionName.fontSizeMin = AvTokens.FontMicro;
                missionName.fontSizeMax = AvTokens.FontTitle + 4f;
                UnityEngine.UI.Image divider = AvKit.Rule(briefCard,
                    new Rect(AvTokens.Space4, -58f, width - AvTokens.Space3 - AvTokens.Space4 - AvTokens.Space5, 1f), AvTheme.Hairline);
                PinTop(divider.rectTransform, AvTokens.Space4, -58f,
                    width - AvTokens.Space3 - AvTokens.Space4 - AvTokens.Space5);

                var viewport = new GameObject("BriefingScroll", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.UI.RectMask2D), typeof(UnityEngine.UI.ScrollRect)).GetComponent<RectTransform>();
                viewport.SetParent(briefCard, false);
                viewport.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.offsetMin = new Vector2(AvTokens.Space4, 34f);
                viewport.offsetMax = new Vector2(-AvTokens.Space5, -66f);
                briefingScroll = viewport.GetComponent<UnityEngine.UI.ScrollRect>();
                briefingScroll.viewport = viewport;
                briefingScroll.horizontal = false;
                briefingScroll.vertical = true;
                briefingScroll.inertia = false;
                briefingScroll.scrollSensitivity = 24f;
                briefingScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
                // The brief is body copy: a normal measure (~70 characters at 13px), the
                // sheet's normal leading, and no letter tracking borrowed from a label.
                float textWidth = width - AvTokens.Space3 - AvTokens.Space4 - AvTokens.Space5;
                missionDescription = AvStyled.Label(viewport, new Rect(0f, 0f, textWidth, 100f), "", "row-sub");
                briefingScroll.content = missionDescription.rectTransform;
                missionDescription.enableWordWrapping = true;
                missionDescription.fontSize = AvTokens.FontLead;
                missionDescription.characterSpacing = 0f;
                missionDescription.lineSpacing = 0f;
                missionDescription.overflowMode = TextOverflowModes.Overflow;
                missionDescription.color = AvTheme.TextPrimary;

                missionClock = AvStyled.Label(briefCard, new Rect(0f, 0f, 0f, 16f),
                    "MISSION TIME  —", "row-sub");
                PinBottom(missionClock.rectTransform, AvTokens.Space4, 14f, width * 0.5f - AvTokens.Space2, false);
                missionMode = AvStyled.Label(briefCard, new Rect(0f, 0f, 0f, 16f),
                    "MODE  —", "row-sub", align: TextAlignmentOptions.MidlineRight);
                PinBottom(missionMode.rectTransform, AvTokens.Space5, 14f, width * 0.5f - AvTokens.Space5, true);

                browseContracts = AvStyled.Button(page, new Rect(0f, 0f, 0f, 44f),
                    "BROWSE CONTRACTS  >", "btn", () => SelectPage(2), AvButtonStyle.Default)
                    .WithTooltip("Open the contract board and browse optional missions.");

                // Three adjacent escalation stations read like an aircraft system tape.
                // Each still states threshold and state in words.
                ladderHead = Head(page, 0f, width, "ESCALATION LADDER", "CURRENT THRESHOLD");
                ladderProgressLabel = AvStyled.Label(page, new Rect(AvTokens.Space3, 0f, width - AvTokens.Space3, 16f),
                    "SCORE LINK PENDING", "row-sub");
                ladderProgressLabel.enableWordWrapping = false;
                ladderProgressLabel.overflowMode = TextOverflowModes.Ellipsis;
                ladderProgressWidth = width - AvTokens.Space3;
                for (int i = 0; i < ladderProgressSegments.Length; i++)
                    ladderProgressSegments[i] = AvKit.Panel(page, new Rect(0f, 0f, 1f, 5f), AvTheme.SurfaceInert);
                for (int i = 0; i < ladderRails.Length; i++)
                {
                    ladderCards[i] = AvKit.Panel(page, new Rect(0f, 0f, 0f, 0f),
                        AvTheme.SurfaceInert);
                    ladderRails[i] = AvKit.Rule(page, new Rect(0f, 0f, 10f, 3f),
                        AvTheme.RailInert);
                    ladderMarkers[i] = AvKit.Panel(page, new Rect(0f, 0f, 10f, 10f),
                        AvTheme.RailInert, AvSprites.Led);
                    ladderNames[i] = AvStyled.Label(page, new Rect(0f, 0f, 10f, 16f),
                        MfdMissionOverview.StageName(i), "row-name");
                    ladderNames[i].enableWordWrapping = true;
                    ladderNames[i].overflowMode = TextOverflowModes.Truncate;
                    ladderThresholds[i] = AvStyled.Label(page, new Rect(0f, 0f, 10f, 14f), "—", "row-sub");
                    ladderStates[i] = AvStyled.Label(page, new Rect(0f, 0f, 110f, 16f), "—", "row-value",
                        align: TextAlignmentOptions.MidlineLeft);
                }

                contractHead = Head(page, 0f, width, "FACTION CONTRACTS", "SECONDARY MISSIONS");
                var cGo = new GameObject("ContractPreviewCard", typeof(RectTransform));
                contractCard = cGo.GetComponent<RectTransform>();
                contractCard.SetParent(page, false);
                UnityEngine.UI.Image cFill = AvKit.Panel(contractCard, new Rect(0f, 0f, 0f, 0f),
                    AvTheme.Unity(AvTokens.SurfaceInert), AvSprites.Card);
                AvKit.Stretch(cFill.rectTransform);
                UnityEngine.UI.Image cFrame = AvKit.Panel(contractCard, new Rect(0f, 0f, 0f, 0f),
                    AvTheme.Hairline, AvSprites.ControlFrame);
                AvKit.Stretch(cFrame.rectTransform);
                contractRail = AvKit.Rule(contractCard, new Rect(0f, 0f, 3f, 0f), AvTheme.RailInfo);
                StretchVertical(contractRail.rectTransform, 0f, 3f);
                contractSummary = AvStyled.Label(contractCard, new Rect(14f, -6f, width - 40f, 16f), "", "row-main");
                contractDetail = AvStyled.Label(contractCard, new Rect(14f, -24f, width - 40f, 30f), "", "row-sub");
                contractDetail.enableWordWrapping = true;
                contractDetail.overflowMode = TextOverflowModes.Overflow;

                LayoutMissionPage();
            }

            /// <summary>
            /// Places the brief card, the ladder and the contract action for the current
            /// card height. Called at build and again when the measured brief changes size;
            /// it only moves pooled widgets, never rebuilds them.
            /// </summary>
            private void LayoutMissionPage()
            {
                float width = PageWidth;
                float body = PageHeight;
                float cardTop = -HeadingPitch - AvTokens.Space1;
                float cardHeight = Mathf.Clamp(briefCardHeight, 150f, MaxBriefCard());

                AvKit.Place(briefCard, new Rect(AvTokens.Space3, cardTop, width - AvTokens.Space3, cardHeight));

                float ladderHeadingTop = cardTop - cardHeight - AvTokens.Space2;
                ladderHead.Place(missionPage, ladderHeadingTop, width);
                float progressTop = ladderHeadingTop - HeadingPitch;
                AvKit.Place(ladderProgressLabel.rectTransform,
                    new Rect(AvTokens.Space3, progressTop, ladderProgressWidth, 16f));
                // Track and fill share a fixed slot beneath the live score readout.
                float trackY = progressTop - 19f;
                float segmentPitch = ladderProgressWidth / ladderProgressSegments.Length;
                for (int i = 0; i < ladderProgressSegments.Length; i++)
                    AvKit.Place(ladderProgressSegments[i].rectTransform,
                        new Rect(AvTokens.Space3 + i * segmentPitch, trackY,
                            segmentPitch - 3f, 5f));
                float ladderTop = trackY - 12f;
                const float rowHeight = 82f;
                float cardW = width - AvTokens.Space3;
                float stationWidth = (cardW - 2f * AvTokens.Gap) / 3f;

                for (int i = 0; i < ladderRails.Length; i++)
                {
                    float stationX = AvTokens.Space3 + i * (stationWidth + AvTokens.Gap);
                    AvKit.Place(ladderCards[i].rectTransform,
                        new Rect(stationX, ladderTop, stationWidth, rowHeight));
                    AvKit.Place(ladderRails[i].rectTransform,
                        new Rect(stationX, ladderTop - rowHeight + 3f, stationWidth, 3f));
                    AvKit.Place(ladderMarkers[i].rectTransform,
                        new Rect(stationX + 10f, ladderTop - 10f, 8f, 8f));
                    AvKit.Place(ladderNames[i].rectTransform,
                        new Rect(stationX + 22f, ladderTop - 8f, stationWidth - 30f, 31f));
                    AvKit.Place(ladderThresholds[i].rectTransform,
                        new Rect(stationX + 10f, ladderTop - 44f, stationWidth - 20f, 14f));
                    AvKit.Place(ladderStates[i].rectTransform,
                        new Rect(stationX + 10f, ladderTop - 60f, stationWidth - 20f, 16f));
                }

                float ladderBottom = ladderTop - rowHeight - AvTokens.Gap;
                float remainingSpace = body + ladderBottom - AvTokens.Space2;
                bool showContractPreview = remainingSpace >= 138f;

                if (contractHead != null)
                {
                    contractHead.Tick.gameObject.SetActive(showContractPreview);
                    contractHead.Title.gameObject.SetActive(showContractPreview);
                    if (contractHead.Note != null) contractHead.Note.gameObject.SetActive(showContractPreview);
                    contractHead.Rule.gameObject.SetActive(showContractPreview);
                    contractCard.gameObject.SetActive(showContractPreview);
                }

                if (showContractPreview)
                {
                    float contractHeadingTop = ladderBottom - AvTokens.Space1;
                    contractHead.Place(missionPage, contractHeadingTop, width);
                    float contractTop = contractHeadingTop - HeadingPitch;
                    const float previewH = 62f;
                    AvKit.Place(contractCard, new Rect(AvTokens.Space3, contractTop, cardW, previewH));
                    float buttonY = contractTop - previewH - AvTokens.Space2;
                    AvKit.Place((RectTransform)browseContracts.transform,
                        new Rect(AvTokens.Space3, buttonY, cardW, 36f));
                }
                else
                {
                    float buttonY = ladderBottom - AvTokens.Space2;
                    AvKit.Place((RectTransform)browseContracts.transform,
                        new Rect(AvTokens.Space3, buttonY, cardW, 36f));
                }
            }

            /// <summary>
            /// The tallest the brief card may grow: the body's share, but never so tall
            /// that the three ladder rungs and the contract action below it are squeezed.
            /// </summary>
            private float MaxBriefCard() => Mathf.Min(Mathf.Max(200f, PageHeight * 0.44f),
                                                      Mathf.Max(150f, PageHeight - 252f));

            /// <summary>A rule that keeps its width but follows its container's height.</summary>
            private static void StretchVertical(RectTransform rect, float x, float width)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(x, 0f);
                rect.sizeDelta = new Vector2(width, 0f);
            }

            private static void PinTop(RectTransform rect, float x, float y, float width)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            }

            private static void PinBottom(RectTransform rect, float x, float y, float width, bool right)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(right ? 1f : 0f, 0f);
                rect.pivot = new Vector2(right ? 1f : 0f, 0f);
                rect.anchoredPosition = new Vector2(right ? -x : x, y);
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            }

            private void BuildObjectivesPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float y = Heading(page, -AvTokens.Space1, width, "ACTIVE OBJECTIVES");
                objectiveSummary = AvStyled.Label(page,
                    new Rect(width * 0.4f, -AvTokens.Space1, width * 0.6f, 14f),
                    "", "section-title-note", align: TextAlignmentOptions.MidlineRight);
                float usable = PageHeight - 80f;
                int rows = Mathf.Clamp(Mathf.FloorToInt(usable / ObjectiveBoard.Pitch), 4, 12);
                float pitch = Mathf.Clamp(usable / rows, ObjectiveBoard.Pitch, 84f);
                objectiveBoard = new ObjectiveBoard(page, y, width, rows, pitch);
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
                float width = PageWidth;
                float height = PageHeight;
                float y = Heading(page, -AvTokens.Space1, width, "CONTRACT BOARD");
                // The heading's own note slot is static; the board writes the host's live
                // status there instead, so a director that is off or unanswered says so on
                // the page rather than only in the status strip.
                boardStatus = AvStyled.Label(page,
                    new Rect(width * 0.34f, -AvTokens.Space1, width * 0.66f - AvTokens.Space3, 14f),
                    "", "section-title-note", align: TextAlignmentOptions.MidlineRight);
                boardStatus.enableWordWrapping = false;
                // Keep long host status copy inside the heading's measured note slot.
                boardStatus.overflowMode = TextOverflowModes.Ellipsis;
                boardStatus.enableAutoSizing = true;
                boardStatus.fontSizeMin = AvTokens.FontSmall;
                boardStatus.fontSizeMax = boardStatus.fontSize;

                string[] filters = { "AVAILABLE", "ACTIVE", "CLOSED" };
                string[] filterHelp =
                {
                    "offers the host has not answered yet",
                    "contracts this faction has accepted",
                    "completed, lapsed and aborted contracts",
                };
                float filterWidth = (width - AvTokens.Space3 - AvTokens.Gap * 2f) / 3f;
                for (int i = 0; i < filters.Length; i++)
                {
                    int index = i;
                    secondaryFilters[i] = AvStyled.Button(page,
                        new Rect(AvTokens.Space3 + i * (filterWidth + AvTokens.Gap), y, filterWidth, 30f),
                        filters[i], "row-main", () => { secondaryFilter = index; secondaryPage = 0; RequestRefresh(); }, AvButtonStyle.Tab)
                        .WithTooltip("Show " + filters[i].ToLowerInvariant() + " contracts — " + filterHelp[i] + ".");
                }
                y -= 38f;

                boardSummary = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y, width * .66f - AvTokens.Space3, 18f), "", "section-title-note");
                boardSummary.enableWordWrapping = false;
                boardSummary.overflowMode = TextOverflowModes.Ellipsis;
                AvStyled.Button(page,
                    new Rect(width * .68f, y + 3f, width * .32f - AvTokens.Space3, 23f),
                    "OPEN DESK  >", "row-main", () =>
                    {
                        if (contractWindow == null) contractWindow = MissionContractWindow.Create();
                        contractWindow.Show();
                    }, AvButtonStyle.Default).WithTooltip("Open the shared faction contract record.");
                y -= 22f;

                // The grid takes the height the panel actually has: a shallow bezel shows
                // one dossier at a time, a tall one shows three, and the pager never moves.
                int rows = MfdSecondaryObjectives.RowsFor(height);
                float cardHeight = Mathf.Min(MfdSecondaryObjectives.CardHeightFor(height, rows),
                    MfdSecondaryObjectives.RowPitch - AvTokens.Gap);
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
                SecondaryObjectiveView leadOffer = null;
                SecondaryObjectiveView leadActive = null;
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
                boardStatus.text = installed && streamed
                    ? (secondaryLimit > 0 ? "CAPACITY " + secondaryLimit + " MAX" : "DIRECTOR ONLINE")
                    : "WAITING FOR HOST";
                boardStatus.color = installed && streamed ? AvTheme.Dim : AvTheme.Warning;

                if (contractSummary != null)
                {
                    bool hasOffers = available > 0;
                    contractSummary.text = hasOffers
                        ? (available == 1 ? "1 OFFER AVAILABLE" : available + " OFFERS AVAILABLE") + "  ·  " +
                          MfdSecondaryObjectives.ShortCount(secondaryActive, secondaryLimit) + " ACTIVE"
                        : secondaryActive > 0
                        ? MfdSecondaryObjectives.ShortCount(secondaryActive, secondaryLimit) + " ACTIVE CONTRACTS"
                        : "NO ACTIVE CONTRACTS";
                    contractSummary.color = hasOffers ? AvTheme.Accent : secondaryActive > 0 ? AvTheme.RailReady : AvTheme.Dim;
                }
                if (contractDetail != null)
                {
                    SecondaryObjectiveView preview = leadActive ?? leadOffer;
                    if (preview != null)
                    {
                        string copy = MfdSecondaryObjectives.TitleLine(preview.Id, preview.Title) +
                            "  ·  " + (preview.IsActive ? "IN FIELD" : preview.Target) +
                            (string.IsNullOrWhiteSpace(preview.AcceptedBy) ? "" : "  ·  TAKEN BY " + preview.AcceptedBy);
                        if (copy != previewedOfferCopy)
                        {
                            previewedOfferCopy = copy;
                            contractDetail.text = SecondaryObjectiveCard.ClampedCopy(contractDetail, copy,
                                contractDetail.rectTransform.rect.width, 30f);
                            browseContracts.WithTooltip(copy);
                        }
                    }
                    else
                    {
                        previewedOfferCopy = null;
                        contractDetail.text = "Open the contract board to review optional faction missions.";
                        browseContracts.WithTooltip("Open the contract board and browse optional missions.");
                    }
                }
                BoardEmptyReason emptyReason = !installed ? BoardEmptyReason.Unavailable
                    : !streamed ? BoardEmptyReason.LinkLost
                    : !hasCapacity ? BoardEmptyReason.LimitReached
                    : available + secondaryActive + results == 0 ? BoardEmptyReason.DirectorExhausted
                    : BoardEmptyReason.Ready;
                secondaryEmpty.gameObject.SetActive(secondaryCount == 0);
                secondaryEmpty.text = MfdSecondaryObjectives.EmptyMessage(secondaryFilter, emptyReason);
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
                private readonly float briefWidth;
                private readonly float briefHeight;
                private SecondaryObjectiveView current;
                private bool acceptAllowed;
                private int confirmCancel;
                private string briefSource;

                public SecondaryObjectiveCard(RectTransform parent, Rect area, System.Action refresh)
                {
                    float height = area.height;
                    float actionTop = -(height - 40f);
                    float barTop = -(height - 52f);
                    float effectTop = -(height - 70f);
                    float payTop = effectTop + LinePitch;
                    float targetTop = payTop + LinePitch;
                    briefHeight = height >= 170f ? 32f : 16f;
                    briefWidth = area.width - AvTokens.Space3 * 2f;

                    Root = new GameObject("SecondaryObjective", typeof(RectTransform)).GetComponent<RectTransform>();
                    Root.SetParent(parent, false);
                    AvKit.Place(Root, area);
                    rail = AvKit.TacticalCard(Root, new Rect(0f, 0f, area.width, height), AvTheme.RailInfo).Rail;
                    float inner = area.width - AvTokens.Space3 * 2f;

                    var glyphGo = new GameObject("Glyph", typeof(RectTransform), typeof(MfdGlyph));
                    glyphGo.transform.SetParent(Root, false);
                    glyph = glyphGo.GetComponent<MfdGlyph>();
                    glyph.raycastTarget = false;
                    AvKit.Place(glyph.rectTransform, new Rect(AvTokens.Space3 + 6.5f, -16.5f, 17f, 17f));

                    title = AvStyled.Label(Root, new Rect(48f, -8f, area.width - 168f, 20f), "", "row-main");
                    title.fontSize = 15f;
                    title.fontStyle = FontStyles.Bold;
                    title.richText = false;
                    // Long names remain readable and never paint over the deadline chip.
                    title.enableWordWrapping = false;
                    title.overflowMode = TextOverflowModes.Ellipsis;
                    title.enableAutoSizing = true;
                    title.fontSizeMin = 12f;
                    title.fontSizeMax = 15f;

                    var chip = new Rect(area.width - AvTokens.Space3 - 96f, -12f, 96f, 17f);
                    deadlineFill = AvKit.Panel(Root, chip, AvTheme.SurfaceInert, AvSprites.Control);
                    deadlineFrame = AvKit.Outline(Root, chip, AvTheme.Frame);
                    deadline = AvStyled.Label(Root, chip, "", "section-title-note", align: TextAlignmentOptions.Center);
                    deadline.characterSpacing = 0f;

                    family = AvStyled.Label(Root, new Rect(48f, -32f, area.width - 216f, 16f), "", "section-title-note");
                    family.richText = false;
                    state = AvStyled.Label(Root, new Rect(area.width - AvTokens.Space3 - 152f, -32f, 152f, 16f),
                        "", "section-title-note", align: TextAlignmentOptions.MidlineRight);

                    description = AvStyled.Label(Root, new Rect(AvTokens.Space3, -52f, inner, briefHeight), "", "row-sub");
                    description.fontSize = 11.5f;
                    description.richText = false;
                    // The brief wraps to the two lines the card reserved; when the sentence
                    // is longer it is clamped with an explicit "+N MORE" (see ClampedCopy),
                    // never an ellipsis.
                    description.enableWordWrapping = true;
                    description.overflowMode = TextOverflowModes.Overflow;

                    AvKit.Rule(Root, new Rect(AvTokens.Space3, -48f, inner, 1f), AvTheme.Hairline);

                    Key(targetTop, "TARGET");
                    Key(payTop, "PAY");
                    Key(effectTop, "EFFECT");
                    valueWidth = area.width - 78f;
                    target = Value(targetTop, 14f, 11.5f, AvTheme.TextPrimary);
                    pay = Value(payTop, 15f, 13f, AvTheme.Accent);
                    effect = Value(effectTop, 15f, 11.5f, AvTheme.Dim);

                    var track = new Rect(AvTokens.Space3, barTop, inner - 64f, 6f);
                    AvKit.Panel(Root, track, AvTheme.SurfaceInert);
                    AvKit.Outline(Root, track, AvTheme.Frame);
                    progressWidth = track.width - 2f;
                    progressFill = AvKit.Rule(Root, new Rect(track.x + 1f, track.y - 1f, 0f, 4f), AvTheme.RailInfo);
                    percent = AvStyled.Label(Root, new Rect(area.width - AvTokens.Space3 - 56f, barTop + 5f, 56f, 14f),
                        "", "row-value");

                    accept = AvStyled.Button(Root, new Rect(AvTokens.Space3, actionTop, (inner - AvTokens.Gap) * .6f, ActionHeight),
                        "ACCEPT CONTRACT", "row-main", () =>
                        {
                            if (acceptAllowed && ModServices.TryGet(out ISecondaryObjectivesView view)) view.RequestAccept(current.Id);
                            refresh();
                        }, AvButtonStyle.Primary)
                        .WithTooltip("Accept this contract for the faction.");
                    dismiss = AvStyled.Button(Root, new Rect(AvTokens.Space3 + inner * .6f, actionTop, inner * .4f, ActionHeight),
                        "DISMISS", "row-main", () =>
                        {
                            if (current == null) return;
                            if (current.IsActive && confirmCancel != current.Id)
                            { confirmCancel = current.Id; dismiss.SetText("CONFIRM ABORT"); return; }
                            if (ModServices.TryGet(out ISecondaryObjectivesView view)) view.RequestCancel(current.Id);
                            confirmCancel = 0; refresh();
                        }, AvButtonStyle.Danger)
                        .WithTooltip("Dismiss this offer; aborting an active contract asks for confirmation first.");
                }

                private void Key(float y, string text) =>
                    AvStyled.Label(Root, new Rect(AvTokens.Space3, y, 52f, 14f), text, "section-title-note");

                private TMP_Text Value(float y, float height, float size, Color color)
                {
                    // Keep each reading in its column; full contract copy is on hover.
                    TMP_Text label = AvStyled.Label(Root, new Rect(66f, y, valueWidth, height), "", "row-main");
                    label.fontSize = size;
                    label.color = color;
                    label.richText = false;
                    label.enableWordWrapping = false;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    label.enableAutoSizing = true;
                    label.fontSizeMin = AvTokens.FontSmall;
                    label.fontSizeMax = size;
                    return label;
                }

                /// <summary>
                /// Body copy for the dossier's fixed brief slot. It wraps to the two lines
                /// the card reserved and, when the sentence is longer, states how many
                /// words were left out instead of ending in a silent cut. Only re-measured
                /// when the brief's source text changes.
                /// </summary>
                internal static string ClampedCopy(TMP_Text label, string text, float width, float height)
                {
                    if (string.IsNullOrEmpty(text)) return "";
                    if (label.GetPreferredValues(text, width, 0f).y <= height + 1f) return text;
                    string[] words = text.Split(' ');
                    for (int count = words.Length - 1; count >= 1; count--)
                    {
                        string omitted = "  +" + (words.Length - count) + " MORE";
                        string head = string.Join(" ", words, 0, count);
                        if (label.GetPreferredValues(head + omitted, width, 0f).y <= height + 1f)
                            return head + omitted;
                    }
                    return "+" + words.Length + " MORE";
                }

                public void Refresh(SecondaryObjectiveView objective, bool hasCapacity)
                {
                    if (current?.Id != objective?.Id) confirmCancel = 0;
                    current = objective;
                    acceptAllowed = MfdSecondaryObjectives.CanAccept(objective, hasCapacity);
                    accept.SetEnabled(acceptAllowed);
                    accept.SetText(MfdSecondaryObjectives.AcceptLabel(objective, hasCapacity));
                    dismiss.SetEnabled(objective != null && (objective.IsOffered || objective.IsActive));
                    dismiss.SetText(objective?.IsActive == true ? confirmCancel == objective.Id ? "CONFIRM ABORT" : "ABORT" : "DISMISS");

                    if (objective == null)
                    {
                        title.text = "OBJECTIVE LINK LOST";
                        family.text = "";
                        state.text = "WAITING FOR HOST";
                        state.color = AvTheme.Warning;
                        deadline.text = description.text = target.text = pay.text = effect.text = percent.text = "";
                        briefSource = null;
                        glyph.SetKind("flag", AvTheme.Warning);
                        SetTimer(AvTheme.Warning);
                        SetProgress(0f, AvTheme.Warning);
                        rail.color = AvTheme.Warning;
                        return;
                    }

                    float fraction = MfdChartScale.Fraction(objective.Progress, 1f);
                    bool complete = objective.IsComplete;
                    bool lapsed = !complete && objective.SecondsRemaining <= 0f;
                    bool urgent = !complete && !lapsed && objective.SecondsRemaining <= MfdSecondaryObjectives.UrgentSeconds;
                    Color color = complete ? AvTheme.RailReady :
                        lapsed ? AvTheme.Alert :
                        urgent ? AvTheme.Warning : objective.IsActive ? AvTheme.Accent : AvTheme.RailInfo;

                    title.text = MfdSecondaryObjectives.TitleLine(objective.Id, objective.Title);
                    accept.WithTooltip(title.text + " · " + objective.Description + " · " +
                        objective.Target + " · " + objective.Reward +
                        (string.IsNullOrWhiteSpace(objective.AcceptedBy) ? "" : " · ACCEPTED BY " + objective.AcceptedBy));
                    glyph.SetKind(MfdMissionLabels.ContractGlyph(objective.Title), color);
                    family.text = MfdMissionLabels.ContractFamily(objective.Title);
                    if (!string.IsNullOrWhiteSpace(objective.AcceptedBy))
                        family.text += "  /  TAKEN BY " + objective.AcceptedBy;
                    family.enableWordWrapping = false;
                    family.overflowMode = TextOverflowModes.Ellipsis;
                    family.enableAutoSizing = true;
                    family.fontSizeMin = AvTokens.FontMicro;
                    state.text = objective.IsOffered ? "AWAITING ACCEPTANCE" : objective.Status;
                    state.color = color;
                    deadline.text = MfdSecondaryObjectives.ChipLabel(objective);
                    SetTimer(color);
                    if (briefSource != objective.Description)
                    {
                        briefSource = objective.Description;
                        description.text = ClampedCopy(description, briefSource, briefWidth, briefHeight);
                    }
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

                // Size the card to its copy: a short brief leaves the ladder the room, and
                // the brief never sits in a card with an empty middle. Only the pooled
                // widgets move; the measurement runs on the refresh pass it already had.
                float textWidth = PageWidth - AvTokens.Space3 - AvTokens.Space4 - AvTokens.Space5;
                float preferred = missionDescription.GetPreferredValues(
                    missionDescription.text, textWidth, 0f).y;
                float wanted = Mathf.Clamp(Mathf.Round((preferred + BriefCardChrome) * 0.5f) * 2f,
                                           150f, MaxBriefCard());
                if (Mathf.Abs(wanted - briefCardHeight) > 1f)
                {
                    briefCardHeight = wanted;
                    LayoutMissionPage();
                }
                float viewportHeight = Mathf.Max(40f, briefCardHeight - BriefViewportInset);
                missionDescription.rectTransform.sizeDelta =
                    new Vector2(textWidth, Mathf.Max(viewportHeight, preferred));
                briefNote.text = preferred > viewportHeight + 1f ? "SCROLL TO READ BRIEF" : "";
                RefreshEscalation(manager);
            }

            private void RefreshEscalation(MissionManager manager)
            {
                if (manager == null)
                {
                    ladderHead.Note.text = "ESCALATION DATA UNAVAILABLE";
                    ladderProgressLabel.text = "SCORE LINK UNAVAILABLE";
                    for (int i = 0; i < ladderProgressSegments.Length; i++)
                        ladderProgressSegments[i].color = AvTheme.SurfaceInert;
                    for (int i = 0; i < ladderRails.Length; i++)
                        SetLadderRow(i, "—", "—", AvTheme.Disabled,
                                     AvTheme.Unity(AvTokens.RailInert), false);
                    return;
                }

                float current = Mathf.Max(0f, manager.currentEscalation);
                float tactical = manager.tacticalThreshold;
                float strategic = manager.strategicThreshold;
                int stage = MfdMissionOverview.Stage(current, tactical, strategic);
                ladderHead.Note.text = MfdMissionOverview.Caption(current, tactical, strategic);
                ladderProgressLabel.text = MfdMissionOverview.NextGateLabel(current, tactical, strategic);

                Color holding = stage == 2 ? AvTheme.Alert : stage == 1 ? AvTheme.Warning : AvTheme.Accent;
                float progress = MfdMissionOverview.NextGateProgress(current, tactical, strategic);
                for (int i = 0; i < ladderProgressSegments.Length; i++)
                    ladderProgressSegments[i].color = (i + 1f) / ladderProgressSegments.Length <= progress
                        ? holding : AvTheme.SurfaceInert;
                for (int i = 0; i < ladderRails.Length; i++)
                {
                    bool set = i == 0 || (i == 1 ? tactical > 0f : strategic > 0f);
                    bool currentRung = i == stage;
                    Color ink = currentRung ? holding : i < stage ? AvTheme.RailReady : AvTheme.Dim;
                    SetLadderRow(i, MfdMissionOverview.Threshold(i, tactical, strategic),
                        MfdMissionOverview.StageState(i, stage, set), ink,
                        currentRung ? holding : AvTheme.Unity(AvTokens.RailInert), currentRung);
                }
            }

            private void SetLadderRow(int index, string threshold, string state, Color ink,
                                      Color rail, bool currentRung)
            {
                ladderNames[index].color = currentRung ? AvTheme.TextPrimary : AvTheme.Dim;
                ladderThresholds[index].text = threshold;
                ladderThresholds[index].color = ink;
                ladderStates[index].text = state;
                ladderStates[index].color = ink;
                ladderRails[index].color = rail;
                ladderMarkers[index].color = rail;
                if (ladderCards[index] != null)
                {
                    ladderCards[index].color = currentRung
                        ? AvTheme.Unity(AvTokens.Wash(rail.ToRgba(), AvTokens.SelectedScale, 0.15f))
                        : AvTheme.Unity(AvTokens.SurfaceInert);
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

                public ObjectiveBoard(RectTransform parent, float y, float width, int rowCount, float pitch)
                {
                    float usable = width - AvTokens.Space3;
                    perPage = Mathf.Max(1, rowCount);
                    rows = new Row[perPage];
                    empty = AvStyled.Label(parent, new Rect(AvTokens.Space3, y, usable, 30f),
                        "NO ACTIVE OBJECTIVES — THE FRONT IS QUIET", "row-sub");
                    for (int i = 0; i < perPage; i++)
                        rows[i] = new Row(parent, y - i * pitch, usable, pitch);

                    float pagerY = y - perPage * pitch - AvTokens.Space1;
                    var go = new GameObject("ObjectivePager", typeof(RectTransform));
                    pagerRoot = go.GetComponent<RectTransform>();
                    pagerRoot.SetParent(parent, false);
                    AvKit.Place(pagerRoot, new Rect(AvTokens.Space3, pagerY, usable, AvTokens.RowHeight));
                    AvButton[] pager = AvKit.Stepper(pagerRoot, 0f, 0f, usable, out pageLabel,
                        Previous, Next, "Page the objective list");
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
                        string rawTitle = saved == null ? null : saved.DisplayName;
                        string rawSource = saved == null ? "" : saved.UniqueName;
                        string title = MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(rawTitle));
                        string source = MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(rawSource));
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
                    // The pager stays on the page whether or not there is a second one: it
                    // states the position and holds the space the pitch was measured into.
                    pagerRoot.gameObject.SetActive(count > 0);
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

                    public Row(RectTransform parent, float y, float width, float pitch)
                    {
                        float height = pitch - AvTokens.Gap;
                        root = new GameObject("ObjectiveRow", typeof(RectTransform));
                        var rect = root.GetComponent<RectTransform>();
                        rect.SetParent(parent, false);
                        AvKit.Place(rect, new Rect(AvTokens.Space3, y, width, height));

                        rail = AvKit.Rule(rect, new Rect(0f, 3f, 3f, height - 9f), AvTheme.RailInert);

                        var glyphGo = new GameObject("Glyph", typeof(RectTransform), typeof(MfdGlyph));
                        glyphGo.transform.SetParent(rect, false);
                        glyph = glyphGo.GetComponent<MfdGlyph>();
                        glyph.raycastTarget = false;
                        AvKit.Place(glyph.rectTransform, new Rect(12f, -15f, 17f, 17f));

                        name = AvStyled.Label(rect, new Rect(36f, -6f, width - 120f, 16f), "", "row-name");
                        value = AvStyled.Label(rect, new Rect(width - 78f, -6f, 62f, 16f), "", "row-value");
                        detail = AvStyled.Label(rect, new Rect(36f, -25f, width - 46f, 13f), "", "row-sub");
                        // The type/source line is data: it shrinks to the floor and then
                        // overflows rather than being cut to a one-line ellipsis.
                        detail.enableWordWrapping = false;
                        detail.overflowMode = TextOverflowModes.Ellipsis;
                        detail.enableAutoSizing = true;
                        detail.fontSizeMin = AvTokens.FontMicro;
                        detail.fontSizeMax = detail.fontSize;

                        trackWidth = width - 36f;
                        var trackArea = new Rect(36f, -(height - 9f), trackWidth, 3f);
                        AvKit.Panel(rect, trackArea, AvTheme.SurfaceInert);
                        AvKit.Outline(rect, trackArea, AvTheme.Hairline);
                        fill = AvKit.Rule(rect, new Rect(36f, -(height - 9f), 0f, 3f), AvTheme.RailInert);
                        AvKit.Rule(rect, new Rect(0f, -height, width, 1f), AvTheme.Hairline);
                        hover = AvKit.HitButton(rect, new Rect(0f, 0f, width, height), null);
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
                                               PageWidth - AvTokens.Space3, 18f),
                               "NATIVE ADAPTER UNAVAILABLE", "section-title");
                AvStyled.Label(page, new Rect(AvTokens.Space3, -AvTokens.Space6,
                                               PageWidth - AvTokens.Space3, 56f),
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
