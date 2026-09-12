using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// "SQD" — the flight lead, squadron operations, and pilot progression console on the maximised map.
    ///
    /// <para>SQD answers three core questions:
    /// 1. <b>PERKS</b>: What combat traits, fuel discipline, and strike authorisations has your pilot unlocked?
    /// 2. <b>WING LINK</b>: What is the live status of your recruited wingmen and squadron formation (via Wing Command)?
    /// 3. <b>SORTIE</b>: How is your current run and deployed airframe performing in this mission?</para>
    /// </summary>
    internal sealed class SqdMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float Pad = AvTokens.Pad;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.20f;
        private const float SpineInset = 14f;

        private const float ChipWidth = 74f;
        private const float ChipGap = 2f;
        private const int ChipCount = 3;

        private const int TabPerks = 0;
        private const int TabWing = 1;
        private const int TabSortie = 2;

        private const int MaximumBudgetPips = 16;
        private const int MaxWingmanRows = 4;

        private sealed class PerkRow
        {
            public byte Id;
            public AvButton Button;
            public Image Rail;
            public Image Background;
            public TMP_Text Code;
            public TMP_Text Name;
            public TMP_Text Badge;
        }

        private sealed class WingmanRow
        {
            public RectTransform Root;
            public Image Rail;
            public TMP_Text CallSign;
            public TMP_Text Airframe;
            public TMP_Text Status;
            public TMP_Text Distance;
        }

        private ProgressionManager progression;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;
        private AvScreen shell;

        private AvStyled.DataBar dataBar;
        private AvStyled.Metric scoreMetric;
        private AvStyled.Metric budgetMetric;

        private string activeHoverTooltip;
        private readonly List<PerkRow> perkRows = new List<PerkRow>();

        // ---- Perks Page ------------------------------------------------------------------

        private TMP_Text earnedValue;
        private TMP_Text spentValue;
        private TMP_Text availableValue;
        private Image[] budgetPips;

        // ---- Wing Link Page --------------------------------------------------------------

        private Image wingStatusRail;
        private TMP_Text wingStatusTitle;
        private TMP_Text wingStatusSubtitle;
        private readonly List<WingmanRow> wingRows = new List<WingmanRow>();
        private TMP_Text wingCountValue;
        private TMP_Text leadAirframeValue;
        private TMP_Text leadStatusValue;
        private TMP_Text leadSpeedValue;
        private TMP_Text leadAltValue;
        private TMP_Text leadFuelValue;
        private TMP_Text wingTipLabel;

        // ---- Sortie / Run Page -----------------------------------------------------------

        private TMP_Text runAirframeValue;
        private TMP_Text runTimeValue;
        private TMP_Text runSortieScoreValue;
        private TMP_Text runMissionScoreValue;
        private TMP_Text runRankValue;
        private TMP_Text runNextPerkValue;
        private TMP_Text runFuelValue;
        private TMP_Text runFlightStatusValue;
        private TMP_Text committedSystemsList;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(ProgressionManager manager, ManualLogSource log)
        {
            progression = manager;
            logger = log;
        }

        public void ResetForScene()
        {
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            font = null;
            shell = null;
            dataBar = null;
            scoreMetric = null;
            budgetMetric = null;
            activeHoverTooltip = null;
            perkRows.Clear();
            wingRows.Clear();

            earnedValue = spentValue = availableValue = null;
            budgetPips = null;

            wingStatusRail = null;
            wingStatusTitle = wingStatusSubtitle = null;
            wingCountValue = leadAirframeValue = leadStatusValue = null;
            leadSpeedValue = leadAltValue = leadFuelValue = wingTipLabel = null;

            runAirframeValue = runTimeValue = runSortieScoreValue = null;
            runMissionScoreValue = runRankValue = runNextPerkValue = null;
            runFuelValue = runFlightStatusValue = committedSystemsList = null;

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            SetViewOpen(false);
        }

        private void OnDestroy() => SetViewOpen(false);

        private void Update()
        {
            if (failed || progression == null) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            SetViewOpen(screen.isActive);
            if (screen.isActive && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
        }

        private void SetViewOpen(bool open)
        {
            if (viewOpen == open) return;
            viewOpen = open;
            ((IProgressionView)progression)?.SetViewOpen(open);
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Sqd, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger.LogWarning("SQD MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null) return;

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    failed = true;
                    return;
                }

                MfdBezel.Bind(mfd, buttons, screens, slot, left, screen);
                logger.LogInfo("SQD MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                failed = true;
                logger.LogError("SQD MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliSquadron.Screen", typeof(RectTransform), typeof(Image));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvScreen.ResolveHeight(
                templateRect.parent as RectTransform, PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            shell = AvScreen.Build(
                content, "SQD",
                new[] { "PERKS", "WING LINK", "SORTIE" },
                new[]
                {
                    new[] { "MISSION SCORE", "PTS" },
                    new[] { "PERK BUDGET", "BUDGET" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            dataBar = shell.DataBar;
            scoreMetric = shell.Metrics[0];
            budgetMetric = shell.Metrics[1];

            Rect body = shell.Body;

            BuildPerksPage((RectTransform)shell.CreatePage(TabPerks, "PerksPage").transform, body);
            BuildWingPage((RectTransform)shell.CreatePage(TabWing, "WingPage").transform, body);
            BuildSortiePage((RectTransform)shell.CreatePage(TabSortie, "SortiePage").transform, body);

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Sqd;
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            screenRoot = root;
            shell.SetPage(TabPerks);
            return result;
        }

        // ---- Layout Helpers --------------------------------------------------------------

        private static AvNode Section(string name, IList<string> descriptions)
        {
            AvNode section = AvBox.Column(name).Pad(12f, 14f, 14f, 12f).Gaps(0f)
                .Add(AvBox.Cell("title").Height(20f));
            for (int i = 0; i < descriptions.Count; i++)
                section.Add(RowNode("r" + i, descriptions[i]));
            return section;
        }

        private static AvNode RowNode(string name, string description) =>
            AvBox.Row(name).Pad(8f, 0f, 8f, 0f).Gaps(10f)
                .Add(AvBox.Cell("rail").Width(3f))
                .Add(AvBox.Cell("code").Width(30f))
                .Add(AvBox.Column("text").Grow().Gaps(3f)
                    .Add(AvBox.Cell("name").Height(15f))
                    .Add(AvBox.Text("desc", description, "row-sub")))
                .Add(AvBox.Cell("trail").Width(96f));

        private void DrawSectionHeader(RectTransform parent, AvNode node, Rect area,
                                       string title, string note, bool band)
        {
            AvStyled.Box(parent, area, band ? "section band" : "section");
            AvStyled.SpineTick(parent, area.x - SpineInset + 3f, area.y - 16f);

            Rect titleRect = node.At("title");
            float titleWidth = titleRect.width * 0.45f;

            AvStyled.Label(parent, new Rect(titleRect.x, titleRect.y, titleWidth, titleRect.height),
                           title, "section-title");

            if (!string.IsNullOrEmpty(note))
            {
                AvStyled.Label(parent,
                    new Rect(titleRect.x + titleWidth, titleRect.y,
                             titleRect.width - titleWidth, titleRect.height),
                    note, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            }
        }

        private static float DrawSectionTitle(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
        {
            if (band) AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 7f);

            float half = width * 0.50f;
            AvStyled.Label(parent, new Rect(x, y, half, 14f), title, "section-title");
            if (!string.IsNullOrEmpty(note))
            {
                AvStyled.Label(parent, new Rect(x + half, y, width - half, 14f), note,
                               "section-title-note", align: TextAlignmentOptions.MidlineRight);
            }
            return y - 22f;
        }

        private static void RowSeparator(RectTransform parent, Rect area) =>
            AvKit.Rule(parent, new Rect(area.x, area.y - area.height, area.width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        private static TMP_Text KeyValue(
            RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.60f, 16f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.60f, y, width * 0.40f, 16f),
                                  "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        private static Color RailColour(string state) =>
            AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);

        private static Color HoverFill() =>
            AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised);

        // ---- Tab 0: PERKS ----------------------------------------------------------------

        private static string PerkKindCode(PerkDefinition perk)
        {
            if (perk.Capability != null) return "AUT";
            if (perk.Effect == PerkEffect.FuelUse) return "FLT";
            if (perk.Effect == PerkEffect.CombatReward) return "CMB";
            if (perk.Effect == PerkEffect.ServiceReward) return "LOG";
            if (perk.Effect == PerkEffect.ObjectiveReward) return "OBJ";
            if (perk.Effect == PerkEffect.SupportCost) return "SUP";
            return "PAS";
        }

        private void BuildPerksPage(RectTransform parent, Rect body)
        {
            IProgressionView view = progression;
            PerkView[] perks = view.GetPerks();

            var descriptions = new List<string>();
            for (int i = 0; i < perks.Length; i++) descriptions.Add(perks[i].Description);

            AvNode page = AvBox.Column("perks").Gaps(0f)
                .Add(Section("list", descriptions))
                .Add(AvBox.Filler());
            page.Arrange(body);

            if (page.At("list").height > body.height)
            {
                parent = AvScreen.Scroll(parent, body, page.At("list").height, out Rect scrolled);
                page.Arrange(scrolled);
                body = scrolled;
            }

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            AvNode section = page.Find("list");
            DrawSectionHeader(parent, section, page.At("list"),
                              "PILOT & SQUADRON PERKS", "CLICK TO UNLOCK", band: false);

            for (int i = 0; i < perks.Length; i++)
            {
                PerkDefinition def = PerkCatalog.Get(perks[i].Id);
                AddPerkRow(parent, section.Find("r" + i), perks[i], PerkKindCode(def));
            }
        }

        private void AddPerkRow(RectTransform parent, AvNode row, PerkView view, string code)
        {
            Rect area = row.Rect.ToUnity();
            var perk = new PerkRow { Id = view.Id };

            perk.Background = AvKit.Panel(parent, area, Color.clear);
            RowSeparator(parent, area);

            perk.Rail = AvStyled.Rail(parent, row.At("rail"), "locked");
            perk.Code = AvStyled.Label(parent, row.At("code"), code, "row-sub",
                                       align: TextAlignmentOptions.MidlineLeft);
            perk.Name = AvStyled.Label(parent, row.At("text.name"),
                                       view.Name.ToUpperInvariant(), "row-name");
            AvStyled.Label(parent, row.At("text.desc"), view.Description, "row-sub");
            perk.Badge = AvStyled.Label(parent, row.At("trail"), "", "badge",
                                        align: TextAlignmentOptions.MidlineRight);

            byte id = view.Id;
            perk.Button = AvKit.HitButton(parent, area, () =>
            {
                AvInput.Deselect(perk.Background.gameObject);
                ((IProgressionView)progression)?.RequestUnlock(id);
                nextRefresh = 0f;
            });
            perk.Button.SetRowHighlight(perk.Background, Color.clear, HoverFill());
            perk.Button.WithTooltip(
                view.Name.ToUpperInvariant() + " — costs " + view.Cost +
                (view.Cost == 1 ? " point" : " points") + ". " + view.Description);

            perkRows.Add(perk);
        }

        // ---- Tab 1: WING LINK ------------------------------------------------------------

        private void BuildWingPage(RectTransform parent, Rect body)
        {
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            y = DrawSectionTitle(parent, x, y, width, "COMPANION STATUS", "WING COMMAND LINK", band: false);

            AvStyled.Box(parent, new Rect(x, y - 56f, width, 56f), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 14f);
            wingStatusRail = AvStyled.Rail(parent, new Rect(x + 6f, y - 50f, 3f, 44f), "ready");
            wingStatusTitle = AvStyled.Label(parent, new Rect(x + 16f, y - 18f, width - 24f, 16f),
                "WING COMMAND LINK ACTIVE", "section-title");
            wingStatusSubtitle = AvStyled.Label(parent, new Rect(x + 16f, y - 46f, width - 24f, 26f),
                "Squadron telemetry synchronized.", "row-sub");

            y -= 70f;

            y = DrawSectionTitle(parent, x, y, width, "FLIGHT LEAD READOUT", "LOCAL AIRCRAFT", band: true);

            leadAirframeValue = KeyValue(parent, x, y, width, "LEAD AIRFRAME");
            y -= 18f;
            leadStatusValue = KeyValue(parent, x, y, width, "FLIGHT STATUS");
            y -= 18f;
            leadSpeedValue = KeyValue(parent, x, y, width, "AIRSPEED");
            y -= 18f;
            leadAltValue = KeyValue(parent, x, y, width, "RADAR ALTITUDE");
            y -= 18f;
            leadFuelValue = KeyValue(parent, x, y, width, "FUEL RESERVES");
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "SQUADRON ROSTER", "RECRUITED WINGMEN", band: false);

            wingCountValue = KeyValue(parent, x, y, width, "ACTIVE WINGMEN");
            y -= 20f;

            // Up to MaxWingmanRows slots
            wingRows.Clear();
            for (int i = 0; i < MaxWingmanRows; i++)
            {
                var rowObj = new GameObject("WingRow_" + i, typeof(RectTransform));
                RectTransform rt = rowObj.GetComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.anchoredPosition = new Vector2(x, y);
                rt.sizeDelta = new Vector2(width, 24f);

                Image rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, 20f), "ready");
                TMP_Text callsign = AvStyled.Label(rt, new Rect(10f, 2f, 120f, 16f), "WINGMAN " + (i + 1), "row-name");
                TMP_Text airframe = AvStyled.Label(rt, new Rect(135f, 2f, 90f, 16f), "REVOKER", "row-sub");
                TMP_Text status = AvStyled.Label(rt, new Rect(230f, 2f, 90f, 16f), "AIRBORNE", "row-sub");
                TMP_Text dist = AvStyled.Label(rt, new Rect(325f, 2f, width - 325f, 16f), "1.2 km", "kv-value",
                                              align: TextAlignmentOptions.MidlineRight);

                wingRows.Add(new WingmanRow
                {
                    Root = rt,
                    Rail = rail,
                    CallSign = callsign,
                    Airframe = airframe,
                    Status = status,
                    Distance = dist
                });

                y -= 26f;
            }

            y -= 6f;
            wingTipLabel = AvStyled.Label(parent, new Rect(x, y - 48f, width, 48f),
                "Wing Command integrates seamlessly: combat perks amplify wing turnaround, service rewards, and battlefield lethality.",
                "row-sub");
        }

        // ---- Tab 2: SORTIE ---------------------------------------------------------------

        private void BuildSortiePage(RectTransform parent, Rect body)
        {
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            y = DrawSectionTitle(parent, x, y, width, "SORTIE PERFORMANCE", "ACTIVE RUN", band: false);

            runAirframeValue = KeyValue(parent, x, y, width, "ACTIVE AIRFRAME");
            y -= 18f;
            runTimeValue = KeyValue(parent, x, y, width, "SORTIE DURATION");
            y -= 18f;
            runFlightStatusValue = KeyValue(parent, x, y, width, "FLIGHT CONDITION");
            y -= 18f;
            runFuelValue = KeyValue(parent, x, y, width, "FUEL QUANTITY");
            y -= 18f;
            runSortieScoreValue = KeyValue(parent, x, y, width, "SORTIE SCORE");
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "CAREER STANDING", "MISSION TOTALS", band: true);

            runRankValue = KeyValue(parent, x, y, width, "PILOT RANK");
            y -= 18f;
            runMissionScoreValue = KeyValue(parent, x, y, width, "MISSION SCORE");
            y -= 18f;
            runNextPerkValue = KeyValue(parent, x, y, width, "SCORE TO NEXT PERK");
            y -= 18f;
            earnedValue = KeyValue(parent, x, y, width, "POINTS EARNED");
            y -= 18f;
            spentValue = KeyValue(parent, x, y, width, "POINTS COMMITTED");
            y -= 18f;
            availableValue = KeyValue(parent, x, y, width, "POINTS UNSPENT");
            y -= 26f;

            // Budget Pips Row
            int ceiling = Mathf.Clamp(((IProgressionView)progression).MaximumPoints, 1, MaximumBudgetPips);
            budgetPips = new Image[ceiling];
            for (int i = 0; i < ceiling; i++)
            {
                var pip = new Rect(x + i * 15f, y, 12f, 12f);
                AvKit.Outline(parent, pip, AvTheme.Hairline);
                budgetPips[i] = AvKit.Panel(parent, new Rect(pip.x + 2f, pip.y - 2f, 8f, 8f), Color.clear);
            }
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "COMMITTED SYSTEMS", null, band: false);
            committedSystemsList = AvStyled.Label(parent, new Rect(x, y - 64f, width, 64f),
                "No perks committed yet.", "row-sub");
        }

        // ---- Refresh Logic ---------------------------------------------------------------

        private void Refresh()
        {
            if (scoreMetric == null || progression == null) return;

            bool bypass = progression.BypassRequirements;

            RefreshDataBar(bypass);
            RefreshMetrics(bypass);
            RefreshPerkRows();
            RefreshWingPage();
            RefreshSortiePage(bypass);

            UpdateStatusStrip();
        }

        private void RefreshDataBar(bool bypass)
        {
            bool wingPresent = !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid));
            int[] wingIds = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            int wingCount = wingIds != null ? wingIds.Length : 0;
            IProgressionView view = progression;

            if (bypass)
            {
                dataBar.State.text = "DEBUG BYPASS — ALL PERKS UNLOCKED";
                dataBar.State.color = AvTheme.Warning;
            }
            else if (wingPresent)
            {
                dataBar.State.text = "WING COMMAND LINKED · FLIGHT LEAD";
                dataBar.State.color = AvTheme.RailReady;
            }
            else
            {
                dataBar.State.text = "SQUADRON & PILOT CONSOLE";
                dataBar.State.color = AvTheme.Dim;
            }

            dataBar.SetChip(0, wingPresent ? (wingCount > 0 ? $"WING: {wingCount}" : "WING: 0") : "SOLO", wingPresent && wingCount > 0);
            dataBar.SetChip(1, "RANK " + view.Rank, true);
            int avail = view.AvailablePoints;
            dataBar.SetChip(2, bypass ? "ALL UNLOCKED" : avail > 0 ? $"{avail}P AVAIL" : "0P AVAIL", avail > 0 || bypass);
        }

        private void RefreshMetrics(bool bypass)
        {
            IProgressionView view = progression;
            int score = view.Score;
            int perPoint = Math.Max(1, view.ScorePerPoint);
            int intoPoint = score % perPoint;
            int ceiling = Mathf.Max(1, view.MaximumPoints);
            int avail = view.AvailablePoints;

            scoreMetric.Set(
                bypass ? "BYPASS" : score.ToString("N0"),
                bypass ? "ALL PERKS ACTIVE" : (perPoint - intoPoint) + " PTS TO NEXT PERK",
                bypass ? 1f : intoPoint / (float)perPoint,
                bypass ? AvTheme.Warning : AvTheme.RailReady);
            scoreMetric.Unit.text = "PTS · RANK " + view.Rank;

            int earned = view.EarnedPoints;
            budgetMetric.Set(
                bypass ? "FREE" : avail + "P",
                bypass ? "UNLIMITED POINTS" : $"{earned} / {ceiling} EARNED",
                bypass ? 1f : earned / (float)ceiling,
                avail > 0 ? AvTheme.RailReady : AvTheme.RailInfo);
            budgetMetric.Unit.text = "UNSPENT";
        }

        private void RefreshPerkRows()
        {
            IProgressionView view = progression;
            PerkView[] perks = view.GetPerks();

            for (int i = 0; i < perkRows.Count; i++)
            {
                PerkRow row = perkRows[i];
                if (!TryFind(perks, row.Id, out PerkView pView)) continue;

                row.Button.SetEnabled(pView.Affordable && !pView.Unlocked);

                if (pView.Unlocked)
                    PaintPerk(row, "ready", AvTheme.TextPrimary, "ACTIVE", AvTheme.RailReady);
                else if (pView.Affordable)
                    PaintPerk(row, "armed", AvTheme.TextPrimary, "UNLOCK " + pView.Cost + "P", AvTheme.RailCaution);
                else
                    PaintPerk(row, "locked", AvTheme.Dim, pView.Cost + "P REQ", AvTheme.Dim);
            }
        }

        private static void PaintPerk(PerkRow row, string railState, Color name, string badge, Color badgeColor)
        {
            Color rail = RailColour(railState);
            row.Rail.color = rail;
            row.Code.color = rail;
            row.Name.color = name;
            row.Badge.text = badge;
            row.Badge.color = badgeColor;
        }

        private void RefreshWingPage()
        {
            if (wingStatusTitle == null) return;

            string wingGuid = PresenceBoard.GetString(PresenceBoard.WingGuid);
            bool wingPresent = !string.IsNullOrEmpty(wingGuid);
            int[] wingIds = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            int wingCount = wingIds != null ? wingIds.Length : 0;

            if (wingPresent)
            {
                wingStatusRail.color = AvTheme.RailReady;
                wingStatusTitle.text = "WING COMMAND LINK ACTIVE";
                wingStatusTitle.color = AvTheme.RailReady;
                wingStatusSubtitle.text = $"{wingCount} wingmen tracked via PresenceBoard.";
                wingTipLabel.text = "Wing Command synchronizes recruited wingmen, formation stance, and flight orders.";
            }
            else
            {
                wingStatusRail.color = AvTheme.RailCaution;
                wingStatusTitle.text = "STANDALONE SQUADRON MODE";
                wingStatusTitle.color = AvTheme.RailCaution;
                wingStatusSubtitle.text = "Solo flight operations. Wing Command companion mod not detected.";
                wingTipLabel.text = "Install Wing Command to recruit wingmen, issue orders, and view live squadron formation data.";
            }

            wingCountValue.text = wingPresent ? wingCount.ToString() : "SOLO (0)";
            wingCountValue.color = wingCount > 0 ? AvTheme.RailReady : AvTheme.Dim;

            // Player Flight Lead Data
            Aircraft playerAircraft = null;
            if (GameManager.GetLocalPlayer<Player>(out Player player) && player != null)
                playerAircraft = player.Aircraft;

            if (playerAircraft != null)
            {
                string name = playerAircraft.definition != null ? playerAircraft.definition.unitName : playerAircraft.unitName;
                leadAirframeValue.text = string.IsNullOrEmpty(name) ? "ACTIVE AIRCRAFT" : name.ToUpperInvariant();
                leadStatusValue.text = playerAircraft.disabled ? "DISABLED" : playerAircraft.IsLanded() ? "LANDED" : "AIRBORNE";
                leadStatusValue.color = playerAircraft.disabled ? AvTheme.Alert : playerAircraft.IsLanded() ? AvTheme.RailCaution : AvTheme.RailReady;

                leadSpeedValue.text = $"{playerAircraft.speed * 1.94384f:0} kts";
                leadAltValue.text = $"{playerAircraft.radarAlt:0} m AGL";
                leadFuelValue.text = $"{playerAircraft.fuelLevel * 100f:0}%";
            }
            else
            {
                leadAirframeValue.text = "NO AIRCRAFT";
                leadStatusValue.text = "STANDBY";
                leadStatusValue.color = AvTheme.Dim;
                leadSpeedValue.text = "—";
                leadAltValue.text = "—";
                leadFuelValue.text = "—";
            }

            // Wingmen rows resolution
            for (int i = 0; i < wingRows.Count; i++)
            {
                WingmanRow row = wingRows[i];
                if (wingPresent && wingIds != null && i < wingIds.Length)
                {
                    row.Root.gameObject.SetActive(true);
                    int hash = wingIds[i];
                    Aircraft wingAc = FindAircraftByHash(hash);

                    row.CallSign.text = $"WINGMAN {i + 1}";
                    if (wingAc != null)
                    {
                        string acName = wingAc.definition != null ? wingAc.definition.unitName : wingAc.unitName;
                        row.Airframe.text = string.IsNullOrEmpty(acName) ? "AIRCRAFT" : acName.ToUpperInvariant();
                        bool disabled = wingAc.disabled || wingAc.HasEjected();
                        row.Status.text = disabled ? "LOST / EJECTED" : wingAc.IsLanded() ? "LANDED" : "AIRBORNE";
                        row.Status.color = disabled ? AvTheme.Alert : wingAc.IsLanded() ? AvTheme.RailCaution : AvTheme.RailReady;
                        row.Rail.color = disabled ? AvTheme.Alert : AvTheme.RailReady;

                        if (playerAircraft != null)
                        {
                            float distKm = Vector3.Distance(playerAircraft.transform.position, wingAc.transform.position) / 1000f;
                            row.Distance.text = $"{distKm:0.0} km";
                        }
                        else
                        {
                            row.Distance.text = "—";
                        }
                    }
                    else
                    {
                        row.Airframe.text = "EN ROUTE";
                        row.Status.text = "DEPLOYED";
                        row.Status.color = AvTheme.RailInfo;
                        row.Rail.color = AvTheme.RailInfo;
                        row.Distance.text = "—";
                    }
                }
                else
                {
                    row.Root.gameObject.SetActive(false);
                }
            }
        }

        private static Aircraft FindAircraftByHash(int persistentIdHash)
        {
            if (UnitRegistry.allUnits == null) return null;
            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit u = UnitRegistry.allUnits[i];
                if (u != null && u.persistentID.GetHashCode() == persistentIdHash)
                    return u as Aircraft;
            }
            return null;
        }

        private void RefreshSortiePage(bool bypass)
        {
            if (runAirframeValue == null) return;

            IProgressionView view = progression;
            Aircraft playerAircraft = null;
            Player localPlayer = null;
            if (GameManager.GetLocalPlayer<Player>(out localPlayer) && localPlayer != null)
                playerAircraft = localPlayer.Aircraft;

            if (playerAircraft != null)
            {
                string name = playerAircraft.definition != null ? playerAircraft.definition.unitName : playerAircraft.unitName;
                runAirframeValue.text = string.IsNullOrEmpty(name) ? "AIRCRAFT" : name.ToUpperInvariant();
                runSortieScoreValue.text = playerAircraft.sortieScore.ToString("N0");
                runFlightStatusValue.text = playerAircraft.disabled ? "DISABLED" : playerAircraft.IsLanded() ? "LANDED" : "AIRBORNE";
                runFlightStatusValue.color = playerAircraft.disabled ? AvTheme.Alert : AvTheme.RailReady;
                runFuelValue.text = $"{playerAircraft.fuelLevel * 100f:0}%";
            }
            else
            {
                runAirframeValue.text = "NO AIRCRAFT";
                runSortieScoreValue.text = "0";
                runFlightStatusValue.text = "GROUND";
                runFlightStatusValue.color = AvTheme.Dim;
                runFuelValue.text = "—";
            }

            int minutes = Mathf.FloorToInt(Time.timeSinceLevelLoad / 60f);
            int seconds = Mathf.FloorToInt(Time.timeSinceLevelLoad % 60f);
            runTimeValue.text = $"{minutes:00}:{seconds:00}";

            runRankValue.text = view.Rank.ToString();
            runMissionScoreValue.text = view.Score.ToString("N0");

            int perPoint = Math.Max(1, view.ScorePerPoint);
            int toNext = perPoint - (view.Score % perPoint);
            runNextPerkValue.text = bypass ? "BYPASS ACTIVE" : toNext.ToString("N0");

            int earned = view.EarnedPoints;
            int available = view.AvailablePoints;
            int spent = Math.Max(0, earned - available);
            int ceiling = Math.Max(1, view.MaximumPoints);

            earnedValue.text = bypass ? "BYPASS" : $"{earned} / {ceiling}";
            spentValue.text = bypass ? "—" : spent.ToString();
            availableValue.text = bypass ? "UNLIMITED" : available.ToString();
            availableValue.color = !bypass && available > 0 ? AvTheme.RailReady : AvTheme.TextPrimary;

            if (budgetPips != null)
            {
                for (int i = 0; i < budgetPips.Length; i++)
                {
                    budgetPips[i].color = bypass || i < spent ? AvTheme.Accent
                                        : i < earned ? AvTheme.RailReady
                                        : Color.clear;
                }
            }

            PerkView[] perks = view.GetPerks();
            var committed = new List<string>();
            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].Unlocked) committed.Add(perks[i].Name.ToUpperInvariant());
            }

            committedSystemsList.text = committed.Count == 0
                ? "No perks committed yet. Unlock traits in the PERKS tab."
                : string.Join("  ·  ", committed.ToArray());
            committedSystemsList.color = committed.Count == 0 ? AvTheme.Dim : AvTheme.TextPrimary;
        }

        private void UpdateStatusStrip()
        {
            if (shell == null) return;
            string baseLine = progression != null ? progression.LastResult : "";
            shell.WriteStatus(null, activeHoverTooltip, baseLine);
        }

        private static bool TryFind(PerkView[] perks, byte id, out PerkView view)
        {
            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].Id == id)
                {
                    view = perks[i];
                    return true;
                }
            }
            view = default;
            return false;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }
    }
}
