using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
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
    /// "OPS" — the pilot operations console on the maximised map. Perks, tactical support,
    /// observation and the current service record share one screen so Boscali keeps its
    /// four-bezel coexistence contract with Wing Command.
    /// </summary>
    internal sealed class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.15f;

        /// <summary>How far the spine sits inside the panel padding.</summary>
        private const float SpineInset = 14f;

        private const int ChipCount = 3;

        private const int TabPerks = 0;
        private const int TabSupport = 1;
        private const int TabObserve = 2;
        private const int TabStatus = 3;

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

        private sealed class StrikeRow
        {
            public SupportActionDefinition Definition;
            public AvButton Action;
            public Image Rail;
            public Image Background;
            public TMP_Text Code;
            public TMP_Text Name;
            public TMP_Text Status;
            public TMP_Text Cost;
        }

        private SupportManager support;
        private IProgressionView progression;
        private IBaseDefenseAlarmService baseAlarm;
        private ManualLogSource logger;
        private IObservationSource observations;
        private IThirdPersonHud thirdPersonHud;

        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;
        private AvScreen shell;

        private AvStyled.DataBar dataBar;
        private AvStyled.Metric allocMetric;
        private AvStyled.Metric scoreMetric;

        private readonly List<PerkRow> perkRows = new List<PerkRow>();
        private readonly List<StrikeRow> strikeRows = new List<StrikeRow>();
        private byte? perkAwaitingConfirmation;
        private byte? perkRequestId;
        private float perkConfirmationUntil;

        // ---- Observe Page Controls -------------------------------------------------------

        private Image observeStatusRail;
        private TMP_Text observeStatusTitle;
        private TMP_Text observeDetails;
        private AvButton captureMark, callAtMark, clearMark;
        private TMP_Text observePosValue;
        private TMP_Text observeRangeValue;
        private TMP_Text observeAgeValue;
        private TMP_Text observeArmedValue;
        private AvButton hudToggle;

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
            observations = null;
            thirdPersonHud = null;
            perkRows.Clear();
            strikeRows.Clear();
            perkAwaitingConfirmation = null;
            perkRequestId = null;
            perkConfirmationUntil = 0f;

            observeStatusRail = null;
            observeStatusTitle = null;
            observeDetails = null;
            captureMark = callAtMark = clearMark = null;
            observePosValue = observeRangeValue = observeAgeValue = observeArmedValue = null;
            hudToggle = null;

            baseAlarmRail = null;
            baseAlarmLabel = null;
            baseAlarmDetails = null;
            strikeTelemetryRail = null;
            strikeTelemetryLabel = null;
            strikeTelemetryDetails = null;
            allocAccountValue = cooldownStatusValue = armedSummaryValue = null;
            rankValue = missionScoreValue = perkBudgetValue = committedValue = null;

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

            ModServices.TryGet(out observations);
            ModServices.TryGet(out thirdPersonHud);

            shell = AvScreen.Build(
                content, "OPS",
                new[] { "PERKS", "SUPPORT", "OBSERVE", "STATUS" },
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

            BuildPerksPage((RectTransform)shell.CreatePage(TabPerks, "PerksPage").transform, body);
            BuildStrikesPage((RectTransform)shell.CreatePage(TabSupport, "SupportPage").transform, body);
            BuildObservePage((RectTransform)shell.CreatePage(TabObserve, "ObservePage").transform, body);
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

        private static TMP_Text KeyValue(
            RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.58f, 16f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.58f, y, width * 0.42f, 16f),
                                  "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        // ---- Tab 0: PERKS ---------------------------------------------------------------

        private static string PerkCode(PerkView perk) =>
            perk.Group != null &&
            perk.Group.IndexOf("AUTHORIS", StringComparison.OrdinalIgnoreCase) >= 0
                ? "AUT"
                : "PAS";

        private void BuildPerksPage(RectTransform parent, Rect body)
        {
            PerkView[] perks = progression != null ? progression.GetPerks() : Array.Empty<PerkView>();
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
                              "PILOT SYSTEMS", "SCROLL · SELECT ROW, THEN CONFIRM", band: false);

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
            if (progression.UnlockPending ||
                !TryFind(progression.GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            perkAwaitingConfirmation = id;
            perkConfirmationUntil = Time.unscaledTime + 6f;
            nextRefresh = 0f;
        }

        private void CommitSelectedPerk(byte id)
        {
            if (progression == null || progression.UnlockPending ||
                perkAwaitingConfirmation != id || Time.unscaledTime > perkConfirmationUntil ||
                !TryFind(progression.GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            perkAwaitingConfirmation = null;
            perkConfirmationUntil = 0f;
            perkRequestId = id;
            progression.RequestUnlock(id);
            nextRefresh = 0f;
        }

        private static Color HoverFill() =>
            AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised);

        // ---- Tab 1: SUPPORT -------------------------------------------------------------

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

        private void BuildStrikesPage(RectTransform parent, Rect body)
        {
            IReadOnlyList<SupportActionDefinition> actions = support.Actions;

            var descriptions = new List<string>();
            for (int i = 0; i < actions.Count; i++)
            {
                // Measure the actual longest per-action availability copy and reserve the
                // full control stack; the drawn text changes with authority and cooldown.
                string perkName = progression != null
                    ? progression.PerkNameFor(actions[i].Capability)
                    : "COMBAT ENGINEERING";
                descriptions.Add("LOCKED · UNLOCK ON PERKS ('" +
                                 perkName.ToUpperInvariant() + "')\n" + actions[i].Description);
            }

            AvNode page = AvBox.Column("support").Gaps(0f)
                .Add(Section("strikes", descriptions))
                .Add(AvBox.Filler());
            page.Arrange(body);

            if (page.At("strikes").height > body.height)
            {
                parent = AvScreen.Scroll(parent, body, page.At("strikes").height, out Rect scrolled);
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
            strike.Status = AvStyled.Label(parent, row.At("text.desc"), definition.Description, "row-sub");

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
                " Unlocked on OPS / PERKS ('" + perkHint + "').");

            strikeRows.Add(strike);
        }

        // ---- Tab 2: OBSERVE --------------------------------------------------------------

        private void BuildObservePage(RectTransform parent, Rect body)
        {
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            y = DrawSectionTitle(parent, x, y, width, "CAMERA TARGETING", "SURFACE SENSOR MARK", band: false);

            AvStyled.Box(parent, new Rect(x, y, width, 76f), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 14f);
            observeStatusRail = AvStyled.Rail(parent, new Rect(x + 6f, y - 6f, 3f, 64f), "locked");
            observeStatusTitle = AvStyled.Label(parent, new Rect(x + 16f, y - 6f, width - 24f, 16f),
                "NO ACTIVE CAMERA MARK", "section-title");
            observeDetails = AvStyled.Label(parent, new Rect(x + 16f, y - 24f, width - 24f, 44f),
                "Aim cockpit or targeting pod at ground surface and press MARK CAMERA.", "row-sub");

            y -= 88f;

            float buttonWidth = (width - 8f) / 3f;
            captureMark = AvStyled.Button(parent, new Rect(x, y, buttonWidth, 26f),
                "MARK CAMERA", "btn", () => { observations?.Capture(); nextRefresh = 0f; },
                AvButtonStyle.Primary)
                .WithTooltip("Record the ground surface at the center of the live native camera. Lasts 120s.");

            callAtMark = AvStyled.Button(parent, new Rect(x + buttonWidth + 4f, y, buttonWidth, 26f),
                "CALL AT MARK", "btn", () => { if (observations != null) support.RequestAtMark(observations); nextRefresh = 0f; },
                AvButtonStyle.Primary)
                .WithTooltip("Select a call-in on SUPPORT, then execute it at this recorded camera mark.");

            clearMark = AvStyled.Button(parent, new Rect(x + (buttonWidth + 4f) * 2f, y, buttonWidth, 26f),
                "CLEAR MARK", "btn", () => { observations?.Clear(); nextRefresh = 0f; },
                AvButtonStyle.Quiet)
                .WithTooltip("Clear active observation point.");

            y -= 38f;

            y = DrawSectionTitle(parent, x, y, width, "TARGET TELEMETRY", "COORDINATES & RANGE", band: true);

            observePosValue = KeyValue(parent, x, y, width, "COORDINATES (X/Z)");
            y -= 18f;
            observeRangeValue = KeyValue(parent, x, y, width, "SLANT RANGE");
            y -= 18f;
            observeAgeValue = KeyValue(parent, x, y, width, "MARK AGE");
            y -= 18f;
            observeArmedValue = KeyValue(parent, x, y, width, "ARMED CALL-IN");
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "AUXILIARY DISPLAY", "THIRD-PERSON HUD", band: false);
            hudToggle = AvStyled.Button(parent, new Rect(x, y, width, 28f),
                "THIRD-PERSON HUD", "btn", () =>
                {
                    thirdPersonHud?.Toggle();
                    nextRefresh = 0f;
                }, AvButtonStyle.Toggle)
                .WithTooltip("Show or hide Boscali's compact third-person flight overlay.");
        }

        // ---- Tab 3: STATUS ---------------------------------------------------------------

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
                "Tactical support actions draw from allocation earned through combat and service. Single-battle assets only.",
                "row-sub");
            y -= 48f;

            y = DrawSectionTitle(parent, x, y, width, "PILOT RECORD", "THIS MISSION", band: true);
            rankValue = KeyValue(parent, x, y, width, "PILOT RANK");
            y -= 18f;
            missionScoreValue = KeyValue(parent, x, y, width, "MISSION SCORE");
            y -= 18f;
            perkBudgetValue = KeyValue(parent, x, y, width, "PERK POINTS  UNSPENT / EARNED");
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "COMMITTED SYSTEMS", null, band: false);
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
            RefreshPerkRows();
            RefreshStrikeRows(bypass);
            RefreshObservation();
            RefreshStatusPage(bypass);

            UpdateStatusStrip();
        }

        private void RefreshDataBar(bool bypass)
        {
            bool underAttack = baseAlarm != null && baseAlarm.IsBaseUnderAttack;
            bool pending = support.RequestPending;
            bool cooling = support.LocalCooldownRemaining > 0.5f;
            bool marked = observations != null && observations.TryGet(out _);
            bool wingPresent = WingLink.Available;

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
                dataBar.State.text = "DEBUG BYPASS — COSTS IGNORED";
                dataBar.State.color = AvTheme.Warning;
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
            dataBar.SetChip(1, marked ? "CAM MARK" : "NO MARK", marked ? "info" : "inert");
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
            int perPoint = Math.Max(1, progression != null ? progression.ScorePerPoint : 1);
            int intoPoint = score % perPoint;
            int available = progression != null ? progression.AvailablePoints : 0;
            int rank = progression != null ? progression.Rank : 0;

            bool unlockPending = progression != null && progression.UnlockPending;
            scoreMetric.Set(
                bypass ? "FREE" : score.ToString("N0"),
                bypass
                    ? "ALL PERKS UNLOCKED"
                    : unlockPending
                        ? "PERK REQUEST · AWAITING HOST"
                        : available + (available == 1 ? " PT · " : " PTS · ") +
                          (perPoint - intoPoint) + " TO NEXT",
                bypass ? 1f : intoPoint / (float)perPoint,
                bypass ? AvTheme.Warning : unlockPending ? AvTheme.RailInfo : AvTheme.RailReady);
            scoreMetric.Unit.text = bypass ? "BYPASS" : "PTS · RANK " + rank;
        }

        private void RefreshPerkRows()
        {
            if (progression == null) return;
            if (perkAwaitingConfirmation.HasValue && Time.unscaledTime > perkConfirmationUntil)
            {
                perkAwaitingConfirmation = null;
                perkConfirmationUntil = 0f;
            }

            PerkView[] perks = progression.GetPerks();
            bool requestPending = progression.UnlockPending;
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

        private void RefreshStrikeRows(bool bypass)
        {
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;

            for (int i = 0; i < strikeRows.Count; i++)
            {
                StrikeRow row = strikeRows[i];
                float cost = support.Cost(row.Definition);
                bool isAuth = support.IsAuthorised(row.Definition);
                bool isArmed = support.ArmedAction.HasValue &&
                               support.ArmedAction.Value == row.Definition.Id;

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
                        "LOCKED · UNLOCK ON PERKS ('" + perkName.ToUpperInvariant() + "')",
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
                    SetRowState(row, "armed", "ARMED · RIGHT-CLICK MAP OR USE OBSERVE TAB",
                        AvTheme.RailCaution, "ABORT", true, true);
                }
                else
                {
                    string perkName = progression != null ? progression.PerkNameFor(row.Definition.Capability) : "Perk";
                    SetRowState(row, "ready",
                        "AUTH: " + perkName.ToUpperInvariant() + " · CLEARED",
                        AvTheme.RailReady, "CALL IN", true, false);
                }

                row.Cost.color = row.Status.color;
            }
        }

        private static void SetRowState(
            StrikeRow row, string railState, string status, Color statusColor,
            string button, bool ready, bool armed)
        {
            Color rail = RailColour(railState);
            row.Rail.color = rail;
            row.Code.color = rail;

            row.Status.text = status + "\n" + row.Definition.Description;
            row.Status.color = statusColor;

            row.Action.SetText(button);
            row.Action.SetEnabled(ready || armed);
            row.Action.SetLatched(armed);
            row.Action.WithTooltip(status + " — " + row.Definition.Description);
        }

        private static Color RailColour(string state) =>
            AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);

        private void RefreshObservation()
        {
            if (observeStatusTitle == null) return;

            bool hasSource = observations != null;
            ObservationPoint point = default;
            bool marked = hasSource && observations.TryGet(out point);
            bool canCapture = hasSource && observations.CanCapture;
            bool armed = support.ArmedAction.HasValue && MapPicker.IsOwner(MapPicker.Support);

            if (marked)
            {
                observeStatusRail.color = AvTheme.RailReady;
                observeStatusTitle.text = $"{point.Source} SURFACE MARK RECORDED";
                observeStatusTitle.color = AvTheme.RailReady;
                float age = Mathf.Max(0f, Time.unscaledTime - point.RecordedAt);
                observeDetails.text = $"{age:0}s old · range {point.Range / 1000f:0.0} km · Elevation {point.Y:0} m";

                observePosValue.text = $"X {point.X:0}  ·  Z {point.Z:0}";
                observeRangeValue.text = $"{point.Range / 1000f:0.0} km";
                observeAgeValue.text = $"{age:0}s  ·  {(120f - age):0}s EXPIRY";
            }
            else
            {
                observeStatusRail.color = AvTheme.RailInert;
                observeStatusTitle.text = hasSource ? observations.Status.ToUpperInvariant() : "NO SENSOR ATTACHED";
                observeStatusTitle.color = AvTheme.Dim;
                observeDetails.text = "Aim camera at surface and press MARK CAMERA to designate.";

                observePosValue.text = "—";
                observeRangeValue.text = "—";
                observeAgeValue.text = "—";
            }

            observeArmedValue.text = armed
                ? support.ArmedAction.Value.ToString().ToUpperInvariant()
                : "NONE (SELECT ON SUPPORT)";
            observeArmedValue.color = armed ? AvTheme.RailCaution : AvTheme.Dim;

            if (captureMark != null)
            {
                captureMark.SetEnabled(canCapture);
                captureMark.WithTooltip(canCapture
                    ? "Record ground target from native cockpit/pod camera."
                    : "Requires an active native camera view on your aircraft.");
            }

            if (callAtMark != null)
            {
                callAtMark.SetEnabled(marked && armed && !support.RequestPending);
                callAtMark.WithTooltip(!marked ? "Capture a camera mark first."
                    : !armed ? "Select an action on the SUPPORT page first."
                    : support.RequestPending ? "Wait for the host to answer the current request."
                    : "Deliver armed support action directly onto this camera mark.");
            }

            if (clearMark != null)
            {
                clearMark.SetEnabled(marked);
                clearMark.WithTooltip(marked
                    ? "Clear the active observation point."
                    : "No camera mark is available to clear.");
            }

            if (hudToggle != null)
            {
                bool available = thirdPersonHud != null;
                bool enabled = available && thirdPersonHud.IsEnabled;
                hudToggle.SetEnabled(available);
                hudToggle.SetLatched(enabled);
                hudToggle.SetText(!available ? "THIRD-PERSON HUD · UNAVAILABLE"
                                             : enabled ? "THIRD-PERSON HUD · ON"
                                                       : "THIRD-PERSON HUD · OFF");
                hudToggle.WithTooltip(available
                    ? "Show or hide Boscali's compact third-person flight overlay."
                    : "Third-person HUD service is unavailable in this scene.");
            }
        }

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

            armedSummaryValue.text = support.ArmedAction.HasValue
                ? support.ArmedAction.Value.ToString().ToUpperInvariant()
                : "STANDBY";
            armedSummaryValue.color = support.ArmedAction.HasValue ? AvTheme.RailCaution : AvTheme.Dim;

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
                ? "Nothing committed yet. Unlock pilot systems on the PERKS page."
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

        private static bool TryFind(PerkView[] perks, byte id, out PerkView view)
        {
            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].Id != id) continue;
                view = perks[i];
                return true;
            }
            view = default;
            return false;
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
