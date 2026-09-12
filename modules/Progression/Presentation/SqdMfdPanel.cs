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
    /// "SQD" — the pilot identity, ability budget, and enemy ace encounter roster.
    /// Reads encounter snapshots only; Wing Command owns aircraft orders and pilot generation.
    /// </summary>
    internal sealed class SqdMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.20f;
        private const float SpineInset = 14f;

        private const int ChipCount = 3;

        private const int TabPilot = 0;
        private const int TabPerks = 1;
        private const int TabEnemy = 2;

        private const int MaximumBudgetPips = 20;
        private const int EnemyRowsPerPage = 2;

        private sealed class PerkRow
        {
            public byte Id;
            public AvButton Select;
            public AvButton Confirm;
            public Image Rail;
            public Image Background;
            public TMP_Text Code;
            public TMP_Text Name;
        }

        private sealed class EnemyWingRow
        {
            public RectTransform Root;
            public Image Rail;
            public TMP_Text Symbol;
            public TMP_Text Wing;
            public TMP_Text Ace;
            public TMP_Text Skill;
            public TMP_Text Status;
            public TMP_Text Members;
            public TMP_Text Target;
        }

        private ProgressionManager progression;
        private ISquadView squad;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;
        private AvScreen shell;

        private AvStyled.DataBar dataBar;
        private AvStyled.Metric scoreMetric;
        private AvStyled.Metric budgetMetric;

        private byte? perkAwaitingConfirmation;
        private byte? perkRequestId;
        private float perkConfirmationUntil;
        private readonly List<PerkRow> perkRows = new List<PerkRow>();

        // ---- Perks Page ------------------------------------------------------------------

        private TMP_Text earnedValue;
        private TMP_Text spentValue;
        private TMP_Text availableValue;
        private TMP_Text abilityBudgetValue;
        private TMP_Text pilotScoreValue;
        private TMP_Text aceBonusValue;
        private Image[] budgetPips;
        private GameObject[] budgetPipSlots;

        // ---- Enemy roster ---------------------------------------------------------------

        private Image huntRail;
        private TMP_Text huntTitle;
        private TMP_Text huntDetails;
        private readonly List<EnemyWingRow> enemyRows = new List<EnemyWingRow>();
        private TMP_Text rosterPage;
        private AvButton previousWings;
        private AvButton nextWings;
        private int enemyPage;

        // ---- Pilot identity -------------------------------------------------------------

        private TMP_Text pilotCallsign;
        private TMP_Text pilotName;
        private TMP_Text pilotBackground;
        private TMP_Text pilotMode;
        private TMP_Text pilotStatus;
        private TMP_Text pilotDeaths;
        private TMP_Text pilotGeneration;

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

        public void Configure(ProgressionManager manager, ISquadView squadView, ManualLogSource log)
        {
            progression = manager;
            squad = squadView;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Sqd);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            font = null;
            shell = null;
            dataBar = null;
            scoreMetric = null;
            budgetMetric = null;
            perkAwaitingConfirmation = perkRequestId = null;
            perkConfirmationUntil = 0f;
            perkRows.Clear();
            enemyRows.Clear();

            earnedValue = spentValue = availableValue = null;
            abilityBudgetValue = pilotScoreValue = aceBonusValue = null;
            budgetPips = null;
            budgetPipSlots = null;

            huntRail = null;
            huntTitle = huntDetails = rosterPage = null;
            previousWings = nextWings = null;
            enemyPage = 0;
            pilotCallsign = pilotName = pilotBackground = pilotMode = pilotStatus = pilotDeaths = pilotGeneration = null;

            runAirframeValue = runTimeValue = runSortieScoreValue = null;
            runMissionScoreValue = runRankValue = runNextPerkValue = null;
            runFuelValue = runFlightStatusValue = committedSystemsList = null;

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            SetViewOpen(false);
        }

        private void OnDestroy() => ResetForScene();

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

            bool visible = screen.isActive && SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            SetViewOpen(visible);
            if (visible && Time.unscaledTime >= nextRefresh)
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
                if (template == null) { MfdBezel.Release(MfdSlots.Sqd); return; }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Sqd);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    ResetForScene();
                    failed = true;
                    logger.LogWarning("SQD MFD unavailable: bezel changed during installation.");
                    return;
                }
                logger.LogInfo("SQD MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                ResetForScene();
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
            screenRoot = root;
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
            AvKit.ClampIntoCanvas(rootRect);

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
                new[] { "PILOT", "ABILITIES", "ENEMY WINGS" },
                new[]
                {
                    new[] { "PILOT SCORE", "PTS" },
                    new[] { "PERK BUDGET", "BUDGET" },
                },
                ChipCount, Width, height, _ =>
                {
                    perkAwaitingConfirmation = null;
                    perkConfirmationUntil = 0f;
                    nextRefresh = 0f;
                });

            dataBar = shell.DataBar;
            scoreMetric = shell.Metrics[0];
            budgetMetric = shell.Metrics[1];

            Rect body = shell.Body;

            BuildPerksPage((RectTransform)shell.CreatePage(TabPerks, "PerksPage").transform, body);
            BuildEnemyPage((RectTransform)shell.CreatePage(TabEnemy, "EnemyPage").transform, body);
            BuildPilotPage((RectTransform)shell.CreatePage(TabPilot, "PilotPage").transform, body);

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
            shell.SetPage(TabPilot);
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
                .Add(AvBox.Cell("trail").Width(96f).Intrinsic(46f));

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

        // ---- ABILITIES ---------------------------------------------------------------

        private static string PerkCode(PerkView perk) =>
            perk.Group != null &&
            perk.Group.IndexOf("AUTHORIS", StringComparison.OrdinalIgnoreCase) >= 0
                ? "AUT"
                : "PAS";

        private void BuildPerksPage(RectTransform parent, Rect body)
        {
            PerkView[] perks = progression != null ? ((IProgressionView)progression).GetPerks() : Array.Empty<PerkView>();
            if (perks.Length == 0)
            {
                AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
                AvStyled.Label(parent,
                    new Rect(body.x + SpineInset, body.y, body.width - SpineInset, 40f),
                    "No pilot perks are configured on this host.", "row-sub");
                return;
            }

            var descriptions = new List<string>(perks.Length);
            for (int i = 0; i < perks.Length; i++) descriptions.Add(perks[i].Description);

            AvNode page = AvBox.Column("perks").Gaps(0f)
                .Add(AvBox.Cell("budget").Height(38f))
                .Add(Section("list", descriptions))
                .Add(AvBox.Filler());
            page.Arrange(body);

            float contentHeight = page.At("budget").height + page.At("list").height;
            if (contentHeight > body.height)
            {
                parent = AvScreen.Scroll(parent, body, contentHeight, out Rect scrolled);
                page.Arrange(scrolled);
                body = scrolled;
            }

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            Rect budget = page.At("budget");
            abilityBudgetValue = AvStyled.Label(parent,
                new Rect(budget.x + SpineInset, budget.y, budget.width - SpineInset, budget.height),
                "Awaiting current pilot point budget.", "row-main");
            AvNode section = page.Find("list");
            DrawSectionHeader(parent, section, page.At("list"),
                              "PILOT ABILITIES", "SELECT ROW · CONFIRM", band: false);

            for (int i = 0; i < perks.Length; i++)
                AddPerkRow(parent, section.Find("r" + i), perks[i]);
        }

        private void AddPerkRow(RectTransform parent, AvNode row, PerkView view)
        {
            Rect area = row.Rect.ToUnity();
            var perk = new PerkRow { Id = view.Id };

            perk.Background = AvKit.Panel(parent, area, Color.clear);
            RowSeparator(parent, area);
            perk.Rail = AvStyled.Rail(parent, row.At("rail"), "locked");
            perk.Code = AvStyled.Label(parent, row.At("code"), PerkCode(view), "row-sub",
                                       align: TextAlignmentOptions.MidlineLeft);
            perk.Name = AvStyled.Label(parent, row.At("text.name"),
                                       view.Name.ToUpperInvariant(), "row-name");
            AvStyled.Label(parent, row.At("text.desc"), view.Description, "row-sub");

            byte id = view.Id;
            Rect trail = row.At("trail");
            Rect selectArea = new Rect(
                area.x, area.y, Mathf.Max(0f, trail.x - area.x - 2f), area.height);
            perk.Select = AvKit.HitButton(parent, selectArea, () => SelectPerk(id));
            perk.Select.SetRowHighlight(perk.Background, Color.clear, HoverFill());
            perk.Select.WithTooltip(
                view.Name.ToUpperInvariant() + " — costs " + view.Cost +
                (view.Cost == 1 ? " point. " : " points. ") + view.Description +
                " Select this row, then use the separate confirm control.");

            float actionHeight = Mathf.Min(AvTokens.RowHeight, trail.height);
            perk.Confirm = AvStyled.Button(parent,
                new Rect(trail.x, trail.y - Mathf.Max(0f, (trail.height - actionHeight) * 0.5f),
                         trail.width, actionHeight),
                "SELECT", "btn", () => CommitSelectedPerk(id), AvButtonStyle.Primary);
            perk.Confirm.SetEnabled(false);
            perkRows.Add(perk);
        }

        private void SelectPerk(byte id)
        {
            if (progression == null) return;
            if (((IProgressionView)progression).UnlockPending ||
                !TryFind(((IProgressionView)progression).GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            perkAwaitingConfirmation = id;
            perkConfirmationUntil = Time.unscaledTime + 6f;
            nextRefresh = 0f;
        }

        private void CommitSelectedPerk(byte id)
        {
            if (progression == null || ((IProgressionView)progression).UnlockPending ||
                perkAwaitingConfirmation != id || Time.unscaledTime > perkConfirmationUntil ||
                !TryFind(((IProgressionView)progression).GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            perkAwaitingConfirmation = null;
            perkConfirmationUntil = 0f;
            perkRequestId = id;
            ((IProgressionView)progression).RequestUnlock(id);
            nextRefresh = 0f;
        }

        // ---- Enemy wings ----------------------------------------------------------------

        private void BuildEnemyPage(RectTransform parent, Rect body)
        {
            parent = AvScreen.Scroll(parent, body, 418f, out body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            AvStyled.Box(parent, new Rect(x, y, width, 78f), "section band");
            huntRail = AvStyled.Rail(parent, new Rect(x + 6f, y - 8f, 3f, 60f), "ready");
            huntTitle = PlainLabel(parent, new Rect(x + 18f, y - 8f, width - 28f, 18f), "ACE HUNT STANDBY", "section-title");
            huntDetails = PlainLabel(parent, new Rect(x + 18f, y - 30f, width - 28f, 40f), "Awaiting enemy wing reports.", "row-sub");
            y -= 86f;

            previousWings = AvStyled.Button(parent, new Rect(x, y, 78f, 28f), "< PREV", "btn", () =>
            {
                enemyPage = Math.Max(0, enemyPage - 1);
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet);
            previousWings.WithTooltip("Show the previous two enemy wings.");
            rosterPage = PlainLabel(parent, new Rect(x + 84f, y, width - 168f, 28f), "NO WINGS", "kv-value");
            rosterPage.alignment = TextAlignmentOptions.Center;
            nextWings = AvStyled.Button(parent, new Rect(x + width - 78f, y, 78f, 28f), "NEXT >", "btn", () =>
            {
                int count = squad != null ? squad.EnemyWingCount : 0;
                if ((enemyPage + 1) * EnemyRowsPerPage < count) enemyPage++;
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet);
            nextWings.WithTooltip("Show the next two enemy wings, including previous encounters.");
            y -= 38f;

            for (int i = 0; i < EnemyRowsPerPage; i++)
            {
                var rowObject = new GameObject("EnemyWing_" + i, typeof(RectTransform));
                var root = (RectTransform)rowObject.transform;
                root.SetParent(parent, false);
                AvKit.Place(root, new Rect(x, y, width, 110f));
                AvStyled.Box(root, new Rect(0f, 0f, width, 106f), "section");
                var row = new EnemyWingRow
                {
                    Root = root,
                    Rail = AvStyled.Rail(root, new Rect(4f, -8f, 3f, 88f), "locked"),
                    Symbol = PlainLabel(root, new Rect(14f, -8f, 40f, 26f), "", "section-title"),
                    Wing = PlainLabel(root, new Rect(60f, -8f, width - 70f, 18f), "", "row-name"),
                    Ace = PlainLabel(root, new Rect(60f, -30f, width - 70f, 16f), "", "kv-value"),
                    Skill = PlainLabel(root, new Rect(14f, -51f, width - 28f, 16f), "", "row-sub"),
                    Status = PlainLabel(root, new Rect(14f, -71f, width * 0.6f, 16f), "", "kv-value"),
                    Members = PlainLabel(root, new Rect(width * 0.62f, -71f, width * 0.38f - 14f, 16f), "", "kv-value"),
                    Target = PlainLabel(root, new Rect(14f, -89f, width - 28f, 16f), "", "row-sub")
                };
                row.Members.alignment = TextAlignmentOptions.MidlineRight;
                enemyRows.Add(row);
                y -= 116f;
            }

            AvStyled.Label(parent, new Rect(x, y - 6f, width, 50f),
                "ACE KILL: +1 PERK POINT. Downed aces may return stronger.\nFriendly wings and friendly aces: roster not implemented yet. Use WMC for recruited wingmen.", "row-sub");
        }

        private static TMP_Text PlainLabel(RectTransform parent, Rect area, string text, string classes)
        {
            TMP_Text label = AvStyled.Label(parent, area, text, classes, align: TextAlignmentOptions.MidlineLeft);
            label.richText = false;
            return label;
        }

        // ---- Pilot record ---------------------------------------------------------------

        private void BuildPilotPage(RectTransform parent, Rect body)
        {
            parent = AvScreen.Scroll(parent, body, 644f, out body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;
            AvStyled.Box(parent, new Rect(x, y, width, 116f), "section band");
            pilotCallsign = PlainLabel(parent, new Rect(x + 10f, y - 8f, width - 20f, 22f), "PILOT RECORD PENDING", "section-title");
            pilotName = PlainLabel(parent, new Rect(x + 10f, y - 34f, width - 20f, 18f), "Awaiting host pilot record.", "kv-value");
            pilotBackground = PlainLabel(parent, new Rect(x + 10f, y - 60f, width - 20f, 48f), "", "row-sub");
            y -= 126f;
            pilotMode = KeyValue(parent, x, y, width, "PILOT LIFE MODE (F1)");
            y -= 20f;
            pilotStatus = KeyValue(parent, x, y, width, "PILOT STATUS");
            y -= 20f;
            pilotDeaths = KeyValue(parent, x, y, width, "PILOT DEATHS");
            y -= 20f;
            pilotGeneration = KeyValue(parent, x, y, width, "PILOT GENERATION");
            y -= 28f;

            y = DrawSectionTitle(parent, x, y, width, "SORTIE PERFORMANCE", "CURRENT AIRCRAFT", band: false);

            runAirframeValue = KeyValue(parent, x, y, width, "ACTIVE AIRFRAME");
            y -= 18f;
            runTimeValue = KeyValue(parent, x, y, width, "MISSION ELAPSED");
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
            pilotScoreValue = KeyValue(parent, x, y, width, "CURRENT PILOT SCORE");
            y -= 18f;
            aceBonusValue = KeyValue(parent, x, y, width, "ACE BONUS POINTS");
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
            budgetPips = new Image[MaximumBudgetPips];
            budgetPipSlots = new GameObject[MaximumBudgetPips];
            for (int i = 0; i < MaximumBudgetPips; i++)
            {
                var slot = new GameObject("BudgetPoint_" + i, typeof(RectTransform));
                var rect = (RectTransform)slot.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x + i * 15f, y, 12f, 12f));
                AvKit.Outline(rect, new Rect(0f, 0f, 12f, 12f), AvTheme.Hairline);
                budgetPips[i] = AvKit.Panel(rect, new Rect(2f, -2f, 8f, 8f), Color.clear);
                budgetPipSlots[i] = slot;
            }
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "COMMITTED SYSTEMS", null, band: false);
            committedSystemsList = AvStyled.Label(parent, new Rect(x, y, width, 72f),
                "No perks committed yet.", "row-sub");
        }

        // ---- Refresh Logic ---------------------------------------------------------------

        private void Refresh()
        {
            if (scoreMetric == null || progression == null) return;

            bool bypass = progression.BypassRequirements;
            int score = ((IProgressionView)progression).Score;
            int bonus = 0;
            if (squad != null && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                ulong id = PlayerIdentity.Of(local);
                score = Math.Max(0, score - squad.GetScoreOrigin(id));
                bonus = squad.GetBonusPoints(id);
            }

            RefreshDataBar(bypass);
            RefreshMetrics(bypass, score, bonus);
            RefreshPerkRows();
            RefreshEnemyPage();
            RefreshPilotPage(bypass, score, bonus);

            UpdateStatusStrip();
        }

        private void RefreshDataBar(bool bypass)
        {
            IProgressionView view = progression;
            bool hunted = squad != null && squad.HuntActive;
            dataBar.State.text = hunted ? "ACE WING HUNTING YOU" : bypass ? "DEBUG BYPASS — ALL ABILITIES ACTIVE" : "PILOT & ENEMY WING RECORDS";
            dataBar.State.color = hunted ? AvTheme.Alert : bypass ? AvTheme.Warning : AvTheme.RailReady;
            dataBar.SetChip(0, hunted ? "HUNT ACTIVE" : "NO HUNT", hunted);
            dataBar.SetChip(1, "RANK " + view.Rank, true);
            int available = view.AvailablePoints;
            dataBar.SetChip(2, bypass ? "ALL ACTIVE" : available + "P AVAIL", available > 0 || bypass);
        }

        private void RefreshMetrics(bool bypass, int score, int bonus)
        {
            IProgressionView view = progression;
            int perPoint = Math.Max(1, view.ScorePerPoint);
            int intoPoint = score % perPoint;
            int ceiling = Mathf.Max(1, view.MaximumPoints);
            int avail = view.AvailablePoints;
            bool capped = view.EarnedPoints >= ceiling;

            scoreMetric.Set(
                bypass ? "BYPASS" : score.ToString("N0"),
                bypass ? "ALL PERKS ACTIVE" : capped ? "SCORE BUDGET COMPLETE" : (perPoint - intoPoint) + " PTS TO NEXT PERK",
                bypass || capped ? 1f : intoPoint / (float)perPoint,
                bypass ? AvTheme.Warning : AvTheme.RailReady);
            scoreMetric.Unit.text = "PTS · THIS PILOT";

            int earned = view.EarnedPoints;
            budgetMetric.Set(
                bypass ? "FREE" : avail + "P",
                bypass ? "UNLIMITED POINTS" : $"{earned}/{ceiling} EARNED · {bonus} ACE BONUS",
                bypass ? 1f : earned / (float)ceiling,
                avail > 0 ? AvTheme.RailReady : AvTheme.RailInfo);
            budgetMetric.Unit.text = "UNSPENT";
            if (abilityBudgetValue != null)
                abilityBudgetValue.text = bypass ? "DEBUG BYPASS: ALL ABILITIES AVAILABLE" :
                    "PILOT SCORE " + score.ToString("N0") + " · +" + bonus + " ACE BONUS POINTS · " + avail + "P AVAILABLE";
        }

        private void RefreshPerkRows()
        {
            if (progression == null) return;
            if (perkAwaitingConfirmation.HasValue && Time.unscaledTime > perkConfirmationUntil)
            {
                perkAwaitingConfirmation = null;
                perkConfirmationUntil = 0f;
            }

            PerkView[] perks = ((IProgressionView)progression).GetPerks();
            bool requestPending = ((IProgressionView)progression).UnlockPending;
            if (requestPending)
            {
                perkAwaitingConfirmation = null;
                perkConfirmationUntil = 0f;
            }
            else perkRequestId = null;

            for (int i = 0; i < perkRows.Count; i++)
            {
                PerkRow row = perkRows[i];
                if (!TryFind(perks, row.Id, out PerkView view)) continue;

                bool confirming = perkAwaitingConfirmation == row.Id;
                row.Select.SetEnabled(!requestPending && view.Affordable && !view.Unlocked);

                if (view.Unlocked)
                {
                    PaintPerk(row, "ready", AvTheme.TextPrimary);
                    PaintPerkAction(row, "ACTIVE", false, false,
                        view.Name.ToUpperInvariant() + " is active.");
                    row.Select.WithTooltip(view.Name.ToUpperInvariant() + " is active.");
                }
                else if (requestPending && perkRequestId == row.Id)
                {
                    const string pending = "Waiting for the host to accept or deny this perk request.";
                    PaintPerk(row, "cooling", AvTheme.TextPrimary);
                    PaintPerkAction(row, "PENDING", false, true, pending);
                    row.Select.WithTooltip(pending);
                }
                else if (requestPending)
                {
                    const string wait = "Wait for the host to answer the current perk request.";
                    PaintPerk(row, "locked", AvTheme.Dim);
                    PaintPerkAction(row, "WAIT", false, false, wait);
                    row.Select.WithTooltip(wait);
                }
                else if (confirming)
                {
                    string confirm = "Confirm this separate action to commit " + view.Cost +
                                     (view.Cost == 1 ? " perk point." : " perk points.");
                    PaintPerk(row, "armed", AvTheme.TextPrimary);
                    PaintPerkAction(row, "CONFIRM " + view.Cost + "P", true, true, confirm);
                    row.Select.WithTooltip("Selected. Use the separate CONFIRM control to commit it.");
                }
                else if (view.Affordable)
                {
                    const string select = "Select the row first; the separate confirm control will then enable.";
                    PaintPerk(row, "armed", AvTheme.TextPrimary);
                    PaintPerkAction(row, "CONFIRM " + view.Cost + "P", false, false, select);
                    row.Select.WithTooltip(select);
                }
                else
                {
                    string required = "Requires " + view.Cost +
                                      (view.Cost == 1 ? " unspent perk point." : " unspent perk points.");
                    PaintPerk(row, "locked", AvTheme.Dim);
                    PaintPerkAction(row, view.Cost + "P REQ", false, false, required);
                    row.Select.WithTooltip(required);
                }
            }
        }

        private static void PaintPerk(PerkRow row, string railState, Color name)
        {
            Color rail = RailColour(railState);
            row.Rail.color = rail;
            row.Code.color = rail;
            row.Name.color = name;
        }

        private static void PaintPerkAction(
            PerkRow row, string text, bool enabled, bool latched, string tooltip)
        {
            row.Confirm.SetText(text);
            row.Confirm.SetEnabled(enabled);
            row.Confirm.SetLatched(latched);
            row.Confirm.WithTooltip(tooltip);
        }

        private void RefreshEnemyPage()
        {
            if (huntTitle == null) return;
            bool hunted = squad != null && squad.HuntActive;
            huntTitle.text = hunted ? "ACE HUNT ACTIVE — YOU ARE THE TARGET" : "ACE HUNT STANDBY";
            huntTitle.color = huntRail.color = hunted ? AvTheme.Alert : AvTheme.RailInfo;
            huntDetails.text = squad != null ? squad.Status : "Enemy wing reports are unavailable.";
            int count = Math.Max(0, squad != null ? squad.EnemyWingCount : 0);
            enemyPage = Math.Min(enemyPage, Math.Max(0, (count - 1) / EnemyRowsPerPage));
            int first = enemyPage * EnemyRowsPerPage;
            rosterPage.text = count == 0 ? "NO ENEMY WINGS ENCOUNTERED" :
                (first + 1) + "–" + Math.Min(first + EnemyRowsPerPage, count) + " OF " + count + " WINGS";
            previousWings.SetEnabled(enemyPage > 0);
            nextWings.SetEnabled(first + EnemyRowsPerPage < count);
            for (int i = 0; i < enemyRows.Count; i++)
            {
                EnemyWingRow row = enemyRows[i];
                bool visible = first + i < count;
                row.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                EnemyWingView wing = squad.GetEnemyWing(first + i);
                row.Symbol.text = wing.Symbol;
                row.Wing.text = wing.WingName;
                row.Ace.text = "ACE: " + wing.AceName;
                row.Skill.text = "TIER " + wing.Tier + " · SKILL " + wing.Skill +
                    (wing.Returns > 0 ? " · RETURN #" + wing.Returns : " · FIRST ENCOUNTER");
                row.Status.text = wing.Status;
                row.Members.text = wing.MembersAlive + " / " + wing.MemberCount + " ALIVE";
                row.Target.text = wing.MembersAlive <= 0 ? "WING NO LONGER ACTIVE" :
                    string.IsNullOrEmpty(wing.TargetName) ? "TARGET: NORMAL MISSION ORDERS" : "TARGET: " + wing.TargetName;
                Color color = wing.MembersAlive <= 0 ? AvTheme.Dim :
                    !string.IsNullOrEmpty(wing.TargetName) ? AvTheme.Alert : AvTheme.RailInfo;
                row.Status.color = row.Rail.color = row.Symbol.color = color;
            }
        }

        private void RefreshPilotPage(bool bypass, int pilotScore, int bonus)
        {
            if (runAirframeValue == null) return;
            if (squad != null)
            {
                PilotView pilot = squad.Pilot;
                pilotCallsign.text = string.IsNullOrEmpty(pilot.Callsign) ? "PILOT RECORD PENDING" : pilot.Callsign;
                pilotName.text = pilot.Name;
                pilotBackground.text = pilot.Background;
                pilotMode.text = pilot.Respawns ? "RESPAWNING" : "ONE LIFE";
                pilotMode.color = pilot.Respawns ? AvTheme.RailInfo : AvTheme.RailCaution;
                pilotStatus.text = pilot.Status;
                pilotDeaths.text = pilot.Deaths.ToString();
                pilotGeneration.text = pilot.Generation.ToString();
            }

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
            pilotScoreValue.text = pilotScore.ToString("N0");
            aceBonusValue.text = "+" + bonus + "P";

            int perPoint = Math.Max(1, view.ScorePerPoint);
            int toNext = perPoint - (pilotScore % perPoint);
            runNextPerkValue.text = bypass ? "BYPASS ACTIVE" : view.EarnedPoints >= view.MaximumPoints ? "BUDGET COMPLETE" : toNext.ToString("N0");

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
                    budgetPipSlots[i].SetActive(i < ceiling);
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
                ? "No abilities committed yet. Select one in ABILITIES."
                : string.Join("  ·  ", committed.ToArray());
            committedSystemsList.color = committed.Count == 0 ? AvTheme.Dim : AvTheme.TextPrimary;
        }

        private void UpdateStatusStrip()
        {
            if (shell == null) return;
            string baseLine = shell.Page == TabEnemy
                ? (squad != null ? squad.Status : "Enemy wing reports are unavailable.")
                : progression != null ? progression.LastResult : "";
            shell.WriteStatus(null, MapPicker.Prompt, baseLine);
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
