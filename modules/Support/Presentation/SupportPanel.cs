using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// "OPS" — tactical support, the orbital constellation, cyber infrastructure and battle
    /// status on the maximised map. Pilot abilities and enemy ace records live on SQD; the
    /// ability list stays on SUPPORT, while SPACE and CYBER only command the systems that
    /// enable it.
    /// </summary>
    internal sealed partial class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.15f;

        /// <summary>How far the spine sits inside the panel padding.</summary>
        private const float SpineInset = 14f;

        private const int ChipCount = 3;

        /// <summary>Fixed height for a strike row's dynamic status line, and the hard cap on
        /// how many lines it may ever render. The row's overall height used to be measured
        /// from whatever placeholder string was on hand at build time, then a completely
        /// different (and often longer) status string was drawn into that same space at
        /// refresh — the two disagreed and the live text bled into the row below. Fixing the
        /// height and clamping line count makes that structurally impossible regardless of
        /// what the status string says.</summary>
        private const float StatusRowHeight = 30f;
        private const int StatusMaxLines = 2;

        private const int TabSupport = 0;
        private const int TabSpace = 1;
        private const int TabCyber = 2;
        private const int TabEw = 3;
        private const int TabStatus = 4;

        private sealed class StrikeRow
        {
            public SupportActionDefinition Definition;
            public AvButton Action;
            public Image Rail;
            public Image Background;
            public TMP_Text Code;
            public TMP_Text Name;
            public TMP_Text Status;
            public TMP_Text Status2;
            public TMP_Text Cost;
        }

        private SupportManager support;
        private IProgressionView progression;
        private IBaseDefenseAlarmService baseAlarm;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;
        private AvScreen shell;

        private AvStyled.DataBar dataBar;
        private AvStyled.Metric allocMetric;
        private AvStyled.Metric scoreMetric;

        private readonly List<StrikeRow> strikeRows = new List<StrikeRow>();

        // ---- Battle Page Controls --------------------------------------------------------

        private Image baseAlarmRail;
        private TMP_Text baseAlarmLabel;
        private TMP_Text baseAlarmDetails;
        private Image strikeTelemetryRail;
        private TMP_Text strikeTelemetryLabel;
        private TMP_Text strikeTelemetryDetails;
        private TMP_Text allocAccountValue;
        private TMP_Text cooldownStatusValue;
        private TMP_Text armedSummaryValue;
        private TMP_Text rankValue;
        private TMP_Text missionScoreValue;
        private TMP_Text perkBudgetValue;
        private TMP_Text committedValue;
        private TMP_Text fleetValue;
        private TMP_Text networkValue;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(
            SupportManager manager, IProgressionView progressionView, ManualLogSource log,
            IBaseDefenseAlarmService alarm = null)
        {
            support = manager;
            progression = progressionView;
            logger = log;
            baseAlarm = alarm;
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Ops);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            font = null;
            shell = null;
            dataBar = null;
            allocMetric = null;
            scoreMetric = null;
            strikeRows.Clear();

            baseAlarmRail = null;
            baseAlarmLabel = null;
            baseAlarmDetails = null;
            strikeTelemetryRail = null;
            strikeTelemetryLabel = null;
            strikeTelemetryDetails = null;
            allocAccountValue = cooldownStatusValue = armedSummaryValue = null;
            rankValue = missionScoreValue = perkBudgetValue = committedValue = null;
            fleetValue = networkValue = null;

            ResetSpacePage();
            ResetCyberPage();
            ResetEwPage();

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            SetViewOpen(false);
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || support == null) return;
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
            if (visible)
            {
                UpdateSpaceMotion();
                if (shell.Page == TabCyber) TickScanSweep(ref cyberScan, Time.unscaledDeltaTime);
                else if (shell.Page == TabEw) TickScanSweep(ref ewScan, Time.unscaledDeltaTime);
                if (Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + RefreshInterval;
                    Refresh();
                }
            }
        }

        private void SetViewOpen(bool open)
        {
            if (viewOpen == open) return;
            viewOpen = open;
            progression?.SetViewOpen(open);
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Ops, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger.LogWarning("OPS MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    failed = true;
                    logger.LogWarning("OPS MFD unavailable: claimed bezel changed before binding.");
                    return;
                }
                logger.LogInfo("OPS MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                MfdBezel.Release(MfdSlots.Ops);
                failed = true;
                logger.LogError("OPS MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliOperations.Screen", typeof(RectTransform), typeof(Image));
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
                content, "OPS",
                new[] { "SUPPORT", "SPACE", "CYBER", "EW", "STATUS" },
                new[]
                {
                    new[] { "ALLOCATION", "ALLOC" },
                    new[] { "MISSION SCORE", "PTS" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            dataBar = shell.DataBar;
            allocMetric = shell.Metrics[0];
            scoreMetric = shell.Metrics[1];

            Rect body = shell.Body;

            BuildStrikesPage((RectTransform)shell.CreatePage(TabSupport, "SupportPage").transform, body);
            BuildSpacePage((RectTransform)shell.CreatePage(TabSpace, "SpacePage").transform, body);
            BuildCyberPage((RectTransform)shell.CreatePage(TabCyber, "CyberPage").transform, body);
            BuildEwPage((RectTransform)shell.CreatePage(TabEw, "EwPage").transform, body);
            BuildStatusPage((RectTransform)shell.CreatePage(TabStatus, "StatusPage").transform, body);

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Ops;
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
            shell.SetPage(TabSupport);
            return result;
        }

        // ---- Page scaffolding ------------------------------------------------------------

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
                    .Add(AvBox.Cell("status").Height(StatusRowHeight))
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

            float half = width * 0.48f;
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

        /// <summary>A band-backed section title with a right-hand note and a bright accent
        /// underline, at an AvBox-resolved rect. Shared by SPACE, CYBER and EW, which all hang
        /// numbered bands ("01 / ...") off their spine; lives here so no tab owns a helper the
        /// others depend on. The underline is deliberately drawn here rather than in the
        /// shared ".section.band" style — that class is used by other mods' panels too, and
        /// this accent is specific to Support's own bands.</summary>
        private static TMP_Text DrawBandTitle(RectTransform parent, Rect area, string title, string note)
        {
            AvStyled.Box(parent, new Rect(area.x + SpineInset, area.y + 3f,
                area.width - SpineInset, 15f), "section band");
            AvKit.Rule(parent, new Rect(area.x + SpineInset, area.y - 12f, area.width - SpineInset, 1f),
                AvTheme.RailInfo.WithAlpha(0.45f));
            AvStyled.SpineTick(parent, area.x + SpineInset, area.y + 3f);
            AvStyled.Label(parent, new Rect(area.x + SpineInset + 8f, area.y,
                area.width - SpineInset - 8f, 15f), title, "section-title");
            return AvStyled.Label(parent, new Rect(area.x + area.width * 0.40f, area.y,
                area.width * 0.60f - SpineInset, 15f), note, "section-title-note",
                align: TextAlignmentOptions.MidlineRight);
        }

        // ---- Scan sweep --------------------------------------------------------------------
        //
        // A thin bright line translating down a tab body on a loop: the "this is a live feed"
        // motion cue CYBER and EW need, since neither has SPACE's natural rotating radar sweep.

        private struct ScanSweep
        {
            public Image Bar;
            public Image Glow;
            public float Top;
            public float Bottom;
            public float Y;
        }

        private const float ScanSweepSpeed = 70f;

        private ScanSweep cyberScan;
        private ScanSweep ewScan;

        private static ScanSweep BuildScanSweep(RectTransform parent, Rect area)
        {
            var sweep = new ScanSweep { Top = area.y, Bottom = area.y - area.height, Y = area.y };
            sweep.Glow = AvKit.Panel(parent, new Rect(area.x, area.y, area.width, 14f),
                AvTheme.RailInfo.WithAlpha(0.07f));
            sweep.Glow.raycastTarget = false;
            sweep.Bar = AvKit.Panel(parent, new Rect(area.x, area.y, area.width, 1.5f),
                AvTheme.RailInfo.WithAlpha(0.6f));
            sweep.Bar.raycastTarget = false;
            return sweep;
        }

        private static void TickScanSweep(ref ScanSweep sweep, float deltaTime)
        {
            if (sweep.Bar == null) return;
            sweep.Y -= ScanSweepSpeed * deltaTime;
            if (sweep.Y < sweep.Bottom) sweep.Y = sweep.Top;
            RectTransform barRect = sweep.Bar.rectTransform;
            barRect.anchoredPosition = new Vector2(barRect.anchoredPosition.x, sweep.Y);
            RectTransform glowRect = sweep.Glow.rectTransform;
            glowRect.anchoredPosition = new Vector2(glowRect.anchoredPosition.x, sweep.Y);
        }

        /// <summary>A key/value stat pair: dim label left, bold value right. Shared by SPACE
        /// and CYBER for compact stat readouts inside a card.</summary>
        private static TMP_Text Stat(RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.55f, 14f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, 14f),
                "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        /// <summary>
        /// A faint CRT scanline band tiled over an entire page, drawn first so everything else
        /// sits on top of it. The one cheap trick that actually reads as "phosphor terminal"
        /// rather than "flat dark UI" — reused by SPACE and CYBER, the two tabs asked to be
        /// visually distinctive.
        /// </summary>
        private static void AddScanlineOverlay(RectTransform parent, Rect area)
        {
            var go = new GameObject("Scanlines", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, area);

            Image image = go.GetComponent<Image>();
            image.sprite = SupportTacticalIcons.ScanlineSprite;
            image.type = Image.Type.Tiled;
            image.raycastTarget = false;
        }

        /// <summary>
        /// A soft halo behind a hero card: a slightly larger, low-alpha copy of the card's own
        /// rail colour. Cheap layered-glow, the same "second wider low-alpha copy underneath"
        /// trick <c>MfdResourceChart</c> already uses for its line glow, applied to a card
        /// instead of a line. Must be built BEFORE the card it sits behind.
        /// </summary>
        private static void AddCardGlow(RectTransform parent, Rect cardArea, Color tint, float bleed = 7f)
        {
            Rect glowArea = new Rect(cardArea.x - bleed, cardArea.y + bleed,
                cardArea.width + bleed * 2f, cardArea.height + bleed * 2f);
            AvKit.Panel(parent, glowArea, tint.WithAlpha(0.16f));
        }

        private static TMP_Text KeyValue(
            RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.58f, 16f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.58f, y, width * 0.42f, 16f),
                                  "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        private bool TryCursor(out float x, out float z)
        {
            x = z = 0f;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized) return false;
            if (!map.TryGetCursorCoordinates(out GlobalPosition cursor)) return false;
            x = cursor.x;
            z = cursor.z;
            return true;
        }

        // ---- Tab 1: SUPPORT -------------------------------------------------------------

        private void BuildStrikesPage(RectTransform parent, Rect body)
        {
            var actions = new List<SupportActionDefinition>();
            foreach (var action in support.Actions)
                if (!action.IsHack) actions.Add(action);

            var descriptions = new List<string>();
            for (int i = 0; i < actions.Count; i++)
                descriptions.Add(actions[i].Description);

            AvNode page = AvBox.Column("support").Gaps(0f)
                .Add(Section("strikes", descriptions))
                .Add(AvBox.Filler());
            page.Arrange(body);

            float contentHeight = page.At("strikes").height + 16f;
            if (contentHeight > body.height)
            {
                parent = AvScreen.Scroll(parent, body, contentHeight, out Rect scrolled);
                page.Arrange(scrolled);
                body = scrolled;
            }

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            AvNode section = page.Find("strikes");
            DrawSectionHeader(parent, section, page.At("strikes"),
                              "TACTICAL SUPPORT", "SCROLL · RIGHT-CLICK MAP", band: false);

            for (int i = 0; i < actions.Count; i++)
                AddStrikeRow(parent, section.Find("r" + i), actions[i]);
        }

        private void AddStrikeRow(RectTransform parent, AvNode row, SupportActionDefinition definition)
        {
            Rect area = row.Rect.ToUnity();
            var strike = new StrikeRow { Definition = definition };

            strike.Background = AvKit.Panel(parent, area, Color.clear);
            RowSeparator(parent, area);

            strike.Rail = AvStyled.Rail(parent, row.At("rail"), "locked");
            strike.Code = AvStyled.Label(parent, row.At("code"), ActionCode(definition.Id), "row-sub",
                                         align: TextAlignmentOptions.MidlineLeft);
            strike.Name = AvStyled.Label(parent, row.At("text.name"),
                                         definition.Name.ToUpperInvariant(), "row-name");
            strike.Status = AvStyled.Label(parent, row.At("text.status"), "", "row-sub");
            strike.Status.maxVisibleLines = StatusMaxLines;
            strike.Status2 = AvStyled.Label(parent, row.At("text.desc"), definition.Description, "row-sub");
            strike.Status2.maxVisibleLines = StatusMaxLines;

            Rect trail = row.At("trail");
            strike.Cost = AvStyled.Label(parent, new Rect(trail.x, trail.y, trail.width, 15f),
                                         "", "row-value", align: TextAlignmentOptions.MidlineRight);

            SupportActionId id = definition.Id;
            strike.Action = AvStyled.Button(parent,
                new Rect(trail.x + 10f, trail.y - 20f, trail.width - 10f, 26f),
                "CALL IN", "btn",
                () => { support.Request(id); nextRefresh = 0f; },
                AvButtonStyle.Primary);

            string perkHint = progression != null ? progression.PerkNameFor(definition.Capability) : "Perk";
            strike.Action.WithTooltip(
                definition.Name.ToUpperInvariant() + " — " +
                support.Cost(definition).ToString("0") + " alloc. " + definition.Description +
                " Unlocked in SQD / ABILITIES ('" + perkHint + "').");

            strikeRows.Add(strike);
        }

        private static string ActionCode(SupportActionId id)
        {
            switch (id)
            {
                case SupportActionId.Artillery: return "ART";
                case SupportActionId.Fortify: return "FTF";
                case SupportActionId.Recon: return "SAT";
                case SupportActionId.Emp: return "EMP";
                case SupportActionId.FlareMissile: return "FLR";
                default: return "OPS";
            }
        }

        // ---- Tab 4: STATUS ---------------------------------------------------------------

        private void BuildStatusPage(RectTransform parent, Rect body)
        {
            parent = AvScreen.Scroll(parent, body, 528f, out body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            y = DrawSectionTitle(parent, x, y, width, "AIRBASE INTEGRITY", "LOCAL DEFENSE · SCROLL", band: false);
            AvStyled.Box(parent, new Rect(x, y, width, 56f), "section");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 14f);
            baseAlarmRail = AvStyled.Rail(parent, new Rect(x + 6f, y - 6f, 3f, 44f), "ready");
            baseAlarmLabel = AvStyled.Label(parent, new Rect(x + 16f, y - 6f, width - 24f, 16f),
                "BASE AIRSPACE SECURE", "section-title");
            baseAlarmDetails = AvStyled.Label(parent, new Rect(x + 16f, y - 24f, width - 24f, 28f),
                "No hostile incursions detected near home base.", "row-sub");

            y -= 68f;

            y = DrawSectionTitle(parent, x, y, width, "FIRE SUPPORT TELEMETRY", "IN-FLIGHT ASSETS", band: true);
            AvStyled.Box(parent, new Rect(x, y, width, 76f), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 14f);
            strikeTelemetryRail = AvStyled.Rail(parent, new Rect(x + 6f, y - 6f, 3f, 64f), "ready");
            strikeTelemetryLabel = AvStyled.Label(parent, new Rect(x + 16f, y - 6f, width - 24f, 16f),
                "ORBITAL & ARTILLERY NET", "section-title");
            strikeTelemetryDetails = AvStyled.Label(parent, new Rect(x + 16f, y - 24f, width - 24f, 44f),
                "Idle telemetry.", "row-sub");

            y -= 88f;

            y = DrawSectionTitle(parent, x, y, width, "TACTICAL LOGISTICS", "ALLOCATION STATUS", band: false);
            allocAccountValue = KeyValue(parent, x, y, width, "AVAILABLE ALLOCATION");
            y -= 18f;
            cooldownStatusValue = KeyValue(parent, x, y, width, "NET COOLDOWN REMAINING");
            y -= 18f;
            armedSummaryValue = KeyValue(parent, x, y, width, "ARMED ACTION");
            y -= 26f;

            AvStyled.Label(parent, new Rect(x, y, width, 40f),
                "Tactical support actions draw from allocation earned through combat and service. Satellites and infrastructure are faction assets bought during the mission.",
                "row-sub");
            y -= 48f;

            y = DrawSectionTitle(parent, x, y, width, "PILOT RECORD", "THIS MISSION", band: true);
            rankValue = KeyValue(parent, x, y, width, "PILOT RANK");
            y -= 18f;
            missionScoreValue = KeyValue(parent, x, y, width, "MISSION SCORE");
            y -= 18f;
            perkBudgetValue = KeyValue(parent, x, y, width, "PERK POINTS  UNSPENT / EARNED");
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "SYSTEMS", "SPACE & CYBER", band: false);
            fleetValue = KeyValue(parent, x, y, width, "ORBITAL FLEET");
            y -= 18f;
            networkValue = KeyValue(parent, x, y, width, "CYBER NETWORK");
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "COMMITTED SYSTEMS", null, band: true);
            committedValue = AvStyled.Label(parent, new Rect(x, y, width, 70f),
                                             "NONE", "row-sub");
        }

        // ---- State refresh ---------------------------------------------------------------

        private void Refresh()
        {
            if (allocMetric == null) return;

            bool bypass = support.BypassRequirements;

            RefreshDataBar(bypass);
            RefreshMetrics(bypass);
            RefreshStrikeRows(bypass);
            RefreshSpace(bypass);
            RefreshCyber(bypass);
            RefreshEw(bypass);
            RefreshStatusPage(bypass);

            UpdateStatusStrip();
        }

        private void RefreshDataBar(bool bypass)
        {
            bool underAttack = baseAlarm != null && baseAlarm.IsBaseUnderAttack;
            bool pending = support.RequestPending || support.CommandPending;
            bool cooling = support.LocalCooldownRemaining > 0.5f;
            bool wingPresent = WingLink.Available;
            int satellites = support.LocalConstellation?.Satellites.Count ?? 0;
            int maximum = support.Settings != null ? support.Settings.MaximumSatellites.Value : 4;

            if (underAttack)
            {
                dataBar.State.text = "BASE UNDER ATTACK";
                dataBar.State.color = AvTheme.Alert;
            }
            else if (pending)
            {
                dataBar.State.text = "AWAITING HOST ACKNOWLEDGEMENT";
                dataBar.State.color = AvTheme.Warning;
            }
            else if (bypass)
            {
                dataBar.State.text = "DEBUG BYPASS";
                dataBar.State.color = AvTheme.Warning;
            }
            else if (support.CommandArmed)
            {
                dataBar.State.text = "FLEET COMMAND ARMED · RIGHT-CLICK MAP";
                dataBar.State.color = AvTheme.RailCaution;
            }
            else if (support.ArmedAction.HasValue)
            {
                dataBar.State.text = "ARMED: " + support.ArmedAction.Value.ToString().ToUpperInvariant() + " · RIGHT-CLICK MAP";
                dataBar.State.color = AvTheme.RailCaution;
            }
            else
            {
                dataBar.State.text = "TACTICAL BATTLEFIELD SUPPORT";
                dataBar.State.color = AvTheme.Dim;
            }

            dataBar.SetChip(0, pending ? "PENDING" : cooling ? "NET COOL" : "NET READY",
                            pending || cooling ? "warn" : "live");
            dataBar.SetChip(1, satellites + "/" + maximum + " SATS",
                            satellites > 0 ? "live" : "inert");
            dataBar.SetChip(2, wingPresent ? "WING LINK" : "NO WING",
                            wingPresent ? "info" : "inert");
        }

        private void RefreshMetrics(bool bypass)
        {
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;
            float cooldownTotal = support.LocalCooldownTotal;

            string allocCaption;
            float allocFraction;
            Color allocFill;

            if (support.RequestPending)
            {
                allocCaption = "REQUEST PENDING · AWAITING HOST";
                allocFraction = 1f;
                allocFill = AvTheme.RailInfo;
            }
            else if (cooldown > 0.5f && cooldownTotal > 0f)
            {
                allocCaption = "NET RECHARGING · T-" + Mathf.CeilToInt(cooldown) + "s";
                allocFraction = 1f - cooldown / cooldownTotal;
                allocFill = AvTheme.RailCaution;
            }
            else if (support.DisableCooldowns)
            {
                allocCaption = "NO COOLDOWN LIMIT";
                allocFraction = 1f;
                allocFill = AvTheme.RailInfo;
            }
            else
            {
                allocCaption = "SUPPORT NET READY";
                allocFraction = 1f;
                allocFill = AvTheme.RailReady;
            }

            allocMetric.Set(allocation.ToString("N0"), allocCaption, allocFraction, allocFill);

            int score = progression != null ? progression.Score : 0;
            int available = progression != null ? progression.AvailablePoints : 0;
            int maximum = Math.Max(1, progression != null ? progression.MaximumPoints : 1);
            int rank = progression != null ? progression.Rank : 0;

            bool unlockPending = progression != null && progression.UnlockPending;
            scoreMetric.Set(
                bypass ? "FREE" : score.ToString("N0"),
                bypass
                    ? "ALL PERKS UNLOCKED"
                    : unlockPending
                        ? "PERK REQUEST · AWAITING HOST"
                        : available + "P AVAILABLE · ABILITIES IN SQD",
                bypass ? 1f : available / (float)maximum,
                bypass ? AvTheme.Warning : unlockPending ? AvTheme.RailInfo : AvTheme.RailReady);
            scoreMetric.Unit.text = bypass ? "BYPASS" : "PTS · RANK " + rank;
        }

        private void RefreshStrikeRows(bool bypass)
        {
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;
            bool cursor = TryCursor(out float cursorX, out float cursorZ);

            for (int i = 0; i < strikeRows.Count; i++)
            {
                StrikeRow row = strikeRows[i];
                float cost = support.Cost(row.Definition);
                bool isAuth = support.IsAuthorised(row.Definition);
                bool isArmed = support.ArmedAction.HasValue &&
                               support.ArmedAction.Value == row.Definition.Id;
                SatelliteRole? role = SupportManager.CoverageRole(row.Definition.Id);

                row.Cost.text = cost > 0f ? cost.ToString("N0") : "—";

                if (support.RequestPending)
                {
                    SetRowState(row, "cooling", "REQUEST PENDING · AWAITING HOST",
                        AvTheme.RailInfo, "PENDING", false, false);
                }
                else if (!row.Definition.Enabled)
                {
                    SetRowState(row, "locked", "SERVER DISABLED", AvTheme.Dim, "OFF", false, false);
                }
                else if (cost <= 0f)
                {
                    SetRowState(row, "locked", "UNAVAILABLE ON THIS MAP", AvTheme.Dim, "N/A", false, false);
                }
                else if (!isAuth)
                {
                    string perkName = progression != null ? progression.PerkNameFor(row.Definition.Capability) : "Perk";
                    SetRowState(row, "locked",
                        "LOCKED · UNLOCK IN SQD / ABILITIES ('" + perkName.ToUpperInvariant() + "')",
                        AvTheme.Warning, "LOCKED", false, false);
                }
                else if (cooldown > 0.5f)
                {
                    SetRowState(row, "cooling", "NET COOLING DOWN", AvTheme.RailCaution,
                        "WAIT " + Mathf.CeilToInt(cooldown) + "s", false, false);
                }
                else if (!bypass && allocation + 0.001f < cost)
                {
                    SetRowState(row, "danger", "INSUFFICIENT ALLOCATION", AvTheme.RailDanger,
                        "NO ALLOC", false, false);
                }
                else if (isArmed)
                {
                    SetRowState(row, "armed", "ARMED · RIGHT-CLICK MAP OR CAMERA MARK",
                        AvTheme.RailCaution, "ABORT", true, true);
                }
                else if (role.HasValue && !(cursor && support.CoverageNow(row.Definition.Id, cursorX, cursorZ)))
                {
                    // Coverage is advisory here, not a gate: the player arms the call, then
                    // right-clicks the target, and the host verifies coverage at that point.
                    // Pre-checking the cursor only stopped every out-of-coverage call from
                    // ever being attempted, so the typed denial could never be seen.
                    string coverage = !cursor
                        ? "COVERAGE UNVERIFIED"
                        : CoverageLine(role.Value, cursorX, cursorZ);
                    SetRowState(row, "armed", coverage + " · ARM, THEN RIGHT-CLICK TARGET",
                        cursor ? CoverageColor(role.Value, cursorX, cursorZ) : AvTheme.Warning,
                        "CALL IN", true, false);
                }
                else
                {
                    string status = role.HasValue
                        ? CoverageLine(role.Value, cursorX, cursorZ)
                        : "AUTH: " + ProgressionName(row.Definition);
                    SetRowState(row, "ready", status, AvTheme.RailReady, "CALL IN", true, false);
                }

                row.Cost.color = row.Status.color;
            }
        }

        private string ProgressionName(SupportActionDefinition definition)
        {
            string perkName = progression != null ? progression.PerkNameFor(definition.Capability) : "Perk";
            return perkName.ToUpperInvariant() + " · CLEARED";
        }

        private string CoverageLine(SatelliteRole role, float x, float z)
        {
            Constellation constellation = support.LocalConstellation;
            if (constellation == null) return "THEATER NOT LOADED";
            StationCoverage coverage = constellation.Query(role, x, z);
            if (coverage.Covered)
                return "COVERED BY " + SatelliteName(constellation, coverage.SatelliteId);
            if (!coverage.HasSatellite) return "NO SATELLITE IN THIS ROLE";
            return "NO COVERAGE · NEAREST " + (coverage.NearestGap / 1000f).ToString("0.0") + " KM";
        }

        private Color CoverageColor(SatelliteRole role, float x, float z)
        {
            Constellation constellation = support.LocalConstellation;
            if (constellation == null) return AvTheme.Dim;
            StationCoverage coverage = constellation.Query(role, x, z);
            if (coverage.Covered) return AvTheme.RailReady;
            return coverage.HasSatellite ? AvTheme.RailCaution : AvTheme.RailDanger;
        }

        private static string SatelliteName(Constellation constellation, byte id)
        {
            Satellite satellite = constellation.Find(id);
            return satellite == null ? "SAT-" + id : SatelliteNaming.Callsign(satellite.Role, id);
        }

        private static void SetRowState(
            StrikeRow row, string railState, string status, Color statusColor,
            string button, bool ready, bool armed)
        {
            Color rail = RailColour(railState);
            row.Rail.color = rail;
            row.Code.color = rail;

            row.Status.text = status;
            row.Status.color = statusColor;

            row.Action.SetText(button);
            row.Action.SetEnabled(ready || armed);
            row.Action.SetLatched(armed);
            row.Action.WithTooltip(status + " — " + row.Definition.Description);
        }

        private static Color RailColour(string state) =>
            AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);

        private void RefreshStatusPage(bool bypass)
        {
            if (baseAlarmLabel == null) return;

            bool underAttack = baseAlarm != null && baseAlarm.IsBaseUnderAttack;
            if (underAttack)
            {
                baseAlarmRail.color = AvTheme.Alert;
                baseAlarmLabel.text = "BASE UNDER ATTACK";
                baseAlarmLabel.color = AvTheme.Alert;
                baseAlarmDetails.text = baseAlarm.ActiveAlertTicker ?? "Hostile units engaging base perimeter!";
            }
            else
            {
                baseAlarmRail.color = AvTheme.RailReady;
                baseAlarmLabel.text = "BASE AIRSPACE SECURE";
                baseAlarmLabel.color = AvTheme.RailReady;
                baseAlarmDetails.text = "Perimeter radar clear. No local base assault in progress.";
            }

            string fire = support != null ? support.FireTelemetry : string.Empty;
            if (!string.IsNullOrEmpty(fire))
            {
                strikeTelemetryRail.color = AvTheme.RailCaution;
                strikeTelemetryLabel.text = "ACTIVE FIRE SUPPORT IN FLIGHT";
                strikeTelemetryLabel.color = AvTheme.RailCaution;
                strikeTelemetryDetails.text = fire;
            }
            else
            {
                strikeTelemetryRail.color = AvTheme.RailInert;
                strikeTelemetryLabel.text = "FIRE SUPPORT NET STANDBY";
                strikeTelemetryLabel.color = AvTheme.Dim;
                strikeTelemetryDetails.text = "No active artillery or orbital strikes currently en route.";
            }

            allocAccountValue.text = bypass ? "UNLIMITED (BYPASS)" : support.LocalAllocation.ToString("N0");
            float cooldown = support.LocalCooldownRemaining;
            cooldownStatusValue.text = cooldown > 0.5f ? $"{Mathf.CeilToInt(cooldown)}s" : "READY (0s)";
            cooldownStatusValue.color = cooldown > 0.5f ? AvTheme.RailCaution : AvTheme.RailReady;

            armedSummaryValue.text = support.CommandArmed ? "FLEET COMMAND"
                : support.ArmedAction.HasValue ? support.ArmedAction.Value.ToString().ToUpperInvariant()
                : "STANDBY";
            armedSummaryValue.color = support.ArmedAction.HasValue || support.CommandArmed
                ? AvTheme.RailCaution : AvTheme.Dim;

            if (fleetValue != null)
            {
                Constellation constellation = support.LocalConstellation;
                int maximum = support.Settings != null ? support.Settings.MaximumSatellites.Value : 0;
                fleetValue.text = constellation == null ? "—"
                    : constellation.Satellites.Count + " / " + maximum + " ON ORBIT";
            }
            if (networkValue != null)
            {
                InfoNetwork info = support.LocalInfo;
                networkValue.text = info == null ? "—"
                    : "TIER " + info.Powers.Tier + "  ·  " +
                      info.Level(FacilityId.Sigint) + "/" + info.Level(FacilityId.Crypto) + "/" +
                      info.Level(FacilityId.Disrupt) + "/" + info.Level(FacilityId.Ew);
            }

            if (rankValue == null || progression == null) return;
            rankValue.text = progression.Rank.ToString();
            missionScoreValue.text = progression.Score.ToString("N0");
            perkBudgetValue.text = bypass
                ? "UNLIMITED"
                : progression.AvailablePoints + " / " + progression.EarnedPoints;

            PerkView[] perks = progression.GetPerks();
            var committed = new List<string>();
            for (int i = 0; i < perks.Length; i++)
                if (perks[i].Unlocked) committed.Add(perks[i].Name.ToUpperInvariant());

            committedValue.text = committed.Count == 0
                ? "Nothing committed yet. Unlock pilot abilities in SQD / ABILITIES."
                : string.Join("  ·  ", committed.ToArray());
            committedValue.color = committed.Count == 0 ? AvTheme.Dim : AvTheme.TextPrimary;
        }

        private void UpdateStatusStrip()
        {
            if (shell == null) return;

            string fire = support != null ? support.FireTelemetry : string.Empty;
            string baseLine = (progression != null ? progression.Status : "") + " · " +
                              (support != null ? support.Status : "");

            shell.WriteStatus(
                baseAlarm != null ? baseAlarm.ActiveAlertTicker : null,
                MapPicker.Prompt,
                string.IsNullOrEmpty(fire) ? baseLine : baseLine + " · " + fire);
        }

        // ---- Highlights ------------------------------------------------------------------

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
