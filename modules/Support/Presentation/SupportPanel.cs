using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
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
    /// "OPS" — the multi-domain operations MFD: SPACE, CYBER and SPEC OPS.
    ///
    /// <para>The panel owns no policy. Every figure it shows comes from
    /// <see cref="SupportManager"/> (host snapshot or the same pricing the host charges) and
    /// every control is a request the host validates. A row that cannot act says why, in
    /// words on the row and in the status strip, never by colour alone.</para>
    ///
    /// <para>This file is the shell: bezel install, header, refresh cadence and the shared
    /// row components. Each domain page lives in its own partial.</para>
    /// </summary>
    internal sealed partial class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.15f;

        private const int ChipCount = 3;
        private const float SectionGap = 16f;
        private const float HeaderHeight = 38f;

        private const int TabSpace = (int)OpsDomain.Space;
        private const int TabCyber = (int)OpsDomain.Cyber;
        private const int TabSpecOps = (int)OpsDomain.SpecialOperations;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private SupportManager support;
        private IProgressionView progression;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvScreen shell;

        private AvStyled.Metric allocationMetric;
        private AvStyled.Metric orbitMetric;
        private AvStyled.Metric stationMetric;
        private AvStyled.Metric reserveMetric;

        /// <summary>Support-action rows on every page, refreshed by the page that owns them.</summary>
        private readonly List<ActionRow> actionRows = new List<ActionRow>(12);

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(
            SupportManager manager,
            IProgressionView progressionView,
            ManualLogSource log,
            IBaseDefenseAlarmService _ = null)
        {
            support = manager;
            progression = progressionView;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Ops);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            shell = null;
            allocationMetric = orbitMetric = stationMetric = reserveMetric = null;
            actionRows.Clear();
            ResetOpsWindow();
            ResetSpacePage();
            ResetCyberPage();
            ResetSpecOpsPage();

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            opsSnapshotSeen = false;
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

            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            SetViewOpen(visible);
            // The launch count, the voice loop and SAR hand-off keep running with the screen closed.
            TickSpaceBackground();
            TickCyberBackground();
            TickSpecOpsBackground();
            if (!visible || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshInterval;
            try
            {
                support.PollOps();
                Refresh();
            }
            catch (Exception e)
            {
                // A refresh fault must not throw every frame; stop drawing and say so once.
                failed = true;
                logger?.LogError("OPS MFD refresh failed: " + e);
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
                    logger?.LogWarning("OPS MFD unavailable: no free bezel slot.");
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
                    logger?.LogWarning("OPS MFD unavailable: claimed bezel changed before binding.");
                    return;
                }

                logger?.LogInfo("OPS MFD installed on " + (left ? "left" : "right") +
                                " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                MfdBezel.Release(MfdSlots.Ops);
                failed = true;
                logger?.LogError("OPS MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            TMP_FontAsset font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliOperations.Screen", typeof(RectTransform), typeof(Image));
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
                content, "OPS",
                OpsDomains.TabLabels(),
                new[]
                {
                    new[] { "ALLOCATION", "" },
                    new[] { "ORBIT", "MOD" },
                    new[] { "CYBER", "NET" },
                    new[] { "SPEC OPS", "RDY" }
                },
                ChipCount, Width, height, _ =>
                {
                    AvButton.ClearTooltip();
                    nextRefresh = 0f;
                });

            DecorateDomainTabs();

            allocationMetric = FitMetric(shell.Metrics[0]);
            orbitMetric = FitMetric(shell.Metrics[1]);
            stationMetric = FitMetric(shell.Metrics[2]);
            reserveMetric = FitMetric(shell.Metrics[3]);

            BuildSpacePage();
            BuildCyberPage();
            BuildSpecOpsPage();

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Ops;
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                screenRoot = null;
                shell = null;
                return null;
            }

            shell.SetPage(TabSpace);
            return result;
        }

        /// <summary>
        /// Four metrics share the bezel, so a wide allocation figure or caption would otherwise
        /// ellipsize. Shrink-to-fit keeps the sheet's size as the ceiling and never drops below
        /// the 10px floor; values align right so "0/15" fractions share one edge.
        /// </summary>
        private static AvStyled.Metric FitMetric(AvStyled.Metric metric)
        {
            if (metric?.Value == null) return metric;
            metric.Value.enableAutoSizing = true;
            metric.Value.fontSizeMax = metric.Value.fontSize;
            metric.Value.fontSizeMin = AvTokens.FontMicro;
            metric.Value.alignment = TextAlignmentOptions.MidlineRight;
            if (metric.Caption != null)
            {
                metric.Caption.enableAutoSizing = true;
                metric.Caption.fontSizeMax = metric.Caption.fontSize;
                metric.Caption.fontSizeMin = AvTokens.FontMicro;
            }
            if (metric.Unit != null)
            {
                metric.Unit.enableAutoSizing = true;
                metric.Unit.fontSizeMax = metric.Unit.fontSize;
                metric.Unit.fontSizeMin = AvTokens.FontMicro;
            }
            return metric;
        }

        private void DecorateDomainTabs()
        {
            OpsSprites.Ensure();
            int[] marks = { OpsSprites.G.Space, OpsSprites.G.Cyber, OpsSprites.G.SpecOps };
            for (int i = 0; i < shell.Tabs.Length && i < marks.Length; i++)
            {
                RectTransform root = (RectTransform)shell.Tabs[i].transform;
                float width = root.sizeDelta.x > 1f ? root.sizeDelta.x : Width / marks.Length;
                float height = root.sizeDelta.y > 1f ? root.sizeDelta.y : AvTokens.TabBarHeight;
                Image glyph = AvKit.Panel(root, new Rect(10f, -(height - 17f) * 0.5f, 17f, 17f),
                    AvTheme.RailInfo, OpsSprites.Glyph(marks[i]));
                glyph.raycastTarget = false;
                TMP_Text label = shell.Tabs[i].GetComponentInChildren<TMP_Text>();
                if (label == null) continue;
                AvKit.Place(label.rectTransform, new Rect(34f, 0f, width - 40f, height));
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.enableAutoSizing = true;
                label.fontSizeMin = AvTokens.FontMicro;
                label.fontSizeMax = label.fontSize;
            }
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject != button.gameObject) return images[i];
            }
            return button.GetComponent<Image>();
        }

        // ---- Page scaffolding ------------------------------------------------------------

        /// <summary>
        /// A page root with its spine, scrolled when <paramref name="contentHeight"/> exceeds
        /// the body. Returns the transform to build into and the content column beside the spine.
        /// </summary>
        private RectTransform BeginPage(int tab, string name, float contentHeight,
                                        out float x, out float y, out float width)
        {
            var page = (RectTransform)shell.CreatePage(tab, name).transform;
            RectTransform parent = AvScreen.Scroll(page, shell.Body, contentHeight, out Rect area);
            x = area.x + 4f;
            y = area.y;
            width = area.width - 8f;
            return parent;
        }

        /// <summary>A numbered section heading tied to the spine. Returns its live note.</summary>
        private static TMP_Text Header(RectTransform parent, float x, ref float y, float width,
                                       string title, string note)
        {
            AvStyled.Label(parent, new Rect(x, y, width, 16f), title, "section-title");
            TMP_Text noteLabel = AvStyled.Label(parent, new Rect(x, y - 17f, width, 14f), note ?? "",
                                                "section-title-note");
            AvKit.Rule(parent, new Rect(x, y - 33f, width, 1f), AvTheme.Hairline);
            y -= HeaderHeight;
            return noteLabel;
        }

        // ---- Rows ------------------------------------------------------------------------

        private enum Tone : byte
        {
            Locked,
            Ready,
            Armed,
            Pending,
            Danger
        }

        /// <summary>
        /// The one row shape every page uses: name and value, a full-width readiness line,
        /// then explanation and labelled controls. Fixed heights keep refreshes stable;
        /// compact rows omit the explanation but retain a two-line readiness lane.
        /// </summary>
        private sealed class OpsRow
        {
            public Image Background;
            public Image Rail;
            public TMP_Text Code;
            public TMP_Text Name;
            public TMP_Text Status;
            public TMP_Text Detail;
            public TMP_Text Value;
            public AvButton Primary;
            public AvButton Secondary;
            public string LastStatus;
            public Tone LastTone = (Tone)255;
        }

        private const float RowHeight = 100f;
        private const float CompactRowHeight = 68f;
        /// <summary>Value and action columns: a cost figure is never crowded by its button.</summary>
        private const float RowValueWidth = 100f;
        private const float RowActionWidth = 96f;
        private const float RowColumnGap = 8f;

        private static OpsRow Row(
            RectTransform parent, float x, float y, float width, bool compact,
            string code, string name, string detail,
            string primaryText, Action onPrimary,
            string secondaryText = null, Action onSecondary = null,
            AvButtonStyle primaryStyle = AvButtonStyle.Default, Action onSelect = null,
            float height = 0f, string selectTooltip = null, int glyph = OpsSprites.G.SpecOps)
        {
            float baseHeight = compact ? CompactRowHeight : RowHeight;
            float slot = height > baseHeight ? height : baseHeight;
            var row = new OpsRow();

            row.Background = AvKit.Panel(parent, new Rect(x - 4f, y, width + 4f, slot), AvTheme.SurfaceInert);
            AvKit.Rule(parent, new Rect(x, y, width, 1f), AvTheme.RailInfo.WithAlpha(0.65f));
            AvKit.Rule(parent, new Rect(x + 12f, y - slot + 1f, width - 24f, 1f), AvTheme.Hairline);
            AvKit.Rule(parent, new Rect(x + 62f, y - 10f, 1f, 46f), AvTheme.RailInfo.WithAlpha(0.32f));

            bool trail = primaryText != null;
            float actionWidth = secondaryText != null ? 152f : RowActionWidth;
            float actionX = x + width - actionWidth - 8f;
            float textRight = x + width - 8f;

            AvButton select = null;
            if (onSelect != null)
                select = AvKit.HitButton(parent, new Rect(x, y, trail ? actionX - x - RowColumnGap : width, slot), onSelect);
            if (select != null && selectTooltip != null) select.WithTooltip(selectTooltip);

            row.Rail = AvKit.Rule(parent, new Rect(x - 4f, y, 3f, slot), AvTheme.RailInert);
            Image symbol = AvKit.Panel(parent, new Rect(x + 14f, y - 13f, 32f, 32f), AvTheme.RailInfo,
                OpsSprites.Glyph(glyph));
            symbol.raycastTarget = false;
            row.Code = AvKit.Label(parent, code, new Rect(x + 10f, y - 51f, 42f, 16f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            float textX = x + 74f;
            float textWidth = textRight - textX - (trail ? RowValueWidth + RowColumnGap : 0f);

            row.Name = AvStyled.Label(parent, new Rect(textX, y - 12f, textWidth, 20f), name, "row-name");
            row.Status = Wrapped(AvStyled.Label(parent,
                new Rect(textX, y - 40f, compact && trail ? actionX - textX - 8f : width - 86f, 30f), "", "row-sub"));
            if (!compact)
            {
                if (slot > 120f)
                    AvKit.Label(parent, "EFFECT / MAP DELIVERY", new Rect(x + 12f, y - 75f, width - 24f, 12f),
                        AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold);
                row.Detail = Wrapped(AvStyled.Label(parent,
                    new Rect(x + 12f, y - Mathf.Max(66f, slot * 0.58f), trail ? actionX - x - 20f : width - 24f, 34f),
                    detail ?? "", "row-sub"));
                row.Detail.color = AvTheme.Dim;
                row.Detail.enableAutoSizing = true;
                row.Detail.fontSizeMin = AvTokens.FontMicro;
                row.Detail.fontSizeMax = 11f;
            }

            if (trail)
            {
                row.Value = AvStyled.Label(parent, new Rect(textRight - RowValueWidth, y - 12f, RowValueWidth, 18f),
                                           "", "row-value", align: TextAlignmentOptions.MidlineRight);
                float buttonY = y - slot + 36f;
                if (secondaryText != null)
                {
                    float half = (actionWidth - 8f) * 0.5f;
                    row.Secondary = AvStyled.Button(parent, new Rect(actionX, buttonY, half, 28f),
                                                    secondaryText, "btn", onSecondary, AvButtonStyle.Danger);
                    row.Primary = AvStyled.Button(parent, new Rect(actionX + half + 8f, buttonY, half, 28f),
                                                  primaryText, "btn", onPrimary, primaryStyle);
                }
                else
                {
                    row.Primary = AvStyled.Button(parent, new Rect(actionX, buttonY, RowActionWidth, 28f),
                                                  primaryText, "btn", onPrimary, primaryStyle);
                }
                if (selectTooltip != null) row.Primary.WithTooltip(selectTooltip);
            }

            return row;
        }

        private static TMP_Text Wrapped(TMP_Text label)
        {
            label.enableWordWrapping = true;
            label.maxVisibleLines = 2;
            label.fontSize = 11f;
            label.characterSpacing = 0f;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }

        private static TMP_Text SingleLine(TMP_Text label)
        {
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }

        /// <summary>Write a row's state. Text carries the meaning; the rail and colour repeat it.</summary>
        private static bool Paint(OpsRow row, Tone tone, string status)
        {
            if (row.LastTone == tone && row.LastStatus == status) return false;
            row.LastTone = tone;
            row.LastStatus = status;
            row.Rail.color = RailColor(tone);
            row.Status.text = status;
            row.Status.color = StatusColor(tone);
            if (row.Background != null)
            {
                switch (tone)
                {
                    case Tone.Armed:
                        row.Background.color = AvTheme.RailCaution.WithAlpha(0.07f);
                        break;
                    case Tone.Ready:
                        row.Background.color = AvTheme.Unity(AvTokens.Surface);
                        break;
                    case Tone.Danger:
                        row.Background.color = AvTheme.RailDanger.WithAlpha(0.08f);
                        break;
                    default:
                        row.Background.color = AvTheme.Unity(AvTokens.Surface.WithAlpha(0.85f));
                        break;
                }
            }
            return true;
        }

        private static Color RailColor(Tone tone)
        {
            switch (tone)
            {
                case Tone.Ready: return AvTheme.RailReady;
                case Tone.Armed: return AvTheme.RailCaution;
                case Tone.Pending: return AvTheme.RailInfo;
                case Tone.Danger: return AvTheme.RailDanger;
                default: return AvTheme.RailInert;
            }
        }

        private static Color StatusColor(Tone tone)
        {
            switch (tone)
            {
                case Tone.Ready: return AvTheme.RailReady;
                case Tone.Armed: return AvTheme.RailCaution;
                case Tone.Pending: return AvTheme.RailInfo;
                case Tone.Danger: return AvTheme.RailDanger;
                default: return AvTheme.Dim;
            }
        }

        private static void SetSelected(OpsRow row, bool selected) =>
            row.Background.color = selected ? AvTheme.Accent.WithAlpha(0.08f) : Color.clear;

        // ---- Support-action rows (shared by SPACE, CYBER and SPEC OPS) --------------------

        private sealed class ActionRow
        {
            public SupportActionDefinition Definition;
            public OpsRow View;
            public int Tab;
            public string Verb;
        }

        /// <summary>Which domain page hosts an action. Nothing is left without a page.</summary>
        private static int HomeTab(SupportActionDefinition action)
        {
            if (action.IsCyber) return TabCyber;
            if (SupportManager.OrbitalAbility(action.Id).HasValue) return TabSpace;
            if (action.Id == SupportActionId.FlareMissile) return TabCyber;
            return TabSpecOps;
        }

        private int CountActions(int tab)
        {
            int count = 0;
            foreach (SupportActionDefinition action in support.Actions)
                if (HomeTab(action) == tab) count++;
            return count;
        }

        /// <summary>Build every action row this tab hosts, post-gated SPEC OPS abilities first;
        /// returns the new y.</summary>
        private float BuildActionRows(RectTransform parent, int tab, float x, float y, float width, string verb,
                                      float rowHeight = 0f)
        {
            float step = rowHeight > RowHeight ? rowHeight : RowHeight;
            for (int pass = 0; pass < 2; pass++)
            foreach (SupportActionDefinition action in support.Actions)
            {
                if (HomeTab(action) != tab || action.IsField != (pass == 0)) continue;
                SupportActionId id = action.Id;
                string code = action.IsHack ? CyberCatalog.Code(action.Hack.Value)
                    : action.IsCapstone ? Capstones.Code(action.Cap.Value)
                    : action.IsField ? FieldWords.AbilityCode(action.Field.Value) : ActionCode(id);
                string detail = action.IsHack ? CyberCatalog.Description(action.Hack.Value)
                    : action.IsCapstone ? Capstones.Summary(action.Cap.Value) : action.Description;
                var row = new ActionRow
                {
                    Definition = action,
                    Tab = tab,
                    Verb = verb,
                    View = Row(parent, x, y, width, false, code, action.Name, detail, verb,
                               () =>
                               {
                                   support.Arm(id);
                                   nextRefresh = 0f;
                               }, height: rowHeight,
                               glyph: action.Field == FieldAbility.Spot || action.Field == FieldAbility.Skywatch
                                   ? OpsSprites.G.Spot : action.Field == FieldAbility.Eavesdrop
                                   ? OpsSprites.G.SpecOps : action.Field == FieldAbility.Suppress || action.Field == FieldAbility.Hunt
                                   ? OpsSprites.G.Suppress : OpsSprites.G.Fortify)
                };
                actionRows.Add(row);
                y -= step;
            }
            return y;
        }

        private static string ActionCode(SupportActionId id)
        {
            switch (id)
            {
                case SupportActionId.MtiSweep: return "MTI";
                case SupportActionId.Recon: return "SAR";
                case SupportActionId.Artillery: return "ROD";
                case SupportActionId.Emp: return "EMP";
                case SupportActionId.FlareMissile: return "FLR";
                case SupportActionId.Fortify: return "FTF";
                default: return "OPS";
            }
        }

        private void RefreshActionRows(int tab, bool bypass)
        {
            for (int i = 0; i < actionRows.Count; i++)
            {
                ActionRow row = actionRows[i];
                if (row.Tab != tab) continue;

                SupportActionDefinition action = row.Definition;
                bool cyber = action.IsCyber;
                float cost = cyber ? 0f : support.Cost(action);
                float intel = AbilityStatus.Intel(action);
                bool armed = support.ArmedAction.HasValue && support.ArmedAction.Value == action.Id;
                AbilityFacts facts = AbilityStatus.For(support, action, bypass);
                row.View.Value.text = facts.CostText;
                if (action.Id == SupportActionId.Fortify)
                {
                    SpecOpsDetachment detachment = support.LocalDetachment;
                    if (detachment != null)
                    {
                        int shells = detachment.GroundReadiness;
                        row.View.Detail.text = "Owned zones, or near a SPEC OPS safehouse. Each order occupies " + shells +
                                              (shells == 1 ? " position" : " positions") + " (best team " +
                                              FieldWords.Rank(detachment.BestRank) + ").";
                    }
                }

                Tone tone = facts.Tone == AbilityTone.Ready ? Tone.Ready
                    : facts.Tone == AbilityTone.Armed ? Tone.Armed
                    : facts.Tone == AbilityTone.Pending ? Tone.Pending
                    : facts.Tone == AbilityTone.Danger ? Tone.Danger
                    : Tone.Locked;
                string status = facts.Readiness;
                bool enabled = facts.Enabled;

                row.View.Primary.SetEnabled(enabled);
                row.View.Primary.SetLatched(armed);
                row.View.Primary.SetText(armed ? "ABORT" : row.Verb);
                // Armed/ready read from the shared accent wash; no solid plate at rest.
                row.View.Primary.ClearCustomColors();
                if (Paint(row.View, tone, status))
                {
                    row.View.Primary.WithTooltip(action.Name + " — " +
                        (cyber ? Figure(intel) + " INTEL. " : cost > 0f ? Figure(cost) + " ALLOC. " : "") +
                        action.Description + " " + status + ".");
                }
            }
        }

        /// <summary>Domain gates the host would refuse; one copy, in <see cref="AbilityStatus.Gate"/>.</summary>
        private bool Gate(SupportActionDefinition action, out string reason) =>
            AbilityStatus.Gate(support, action, out reason);

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (shell == null || allocationMetric == null) return;
            bool bypass = support.BypassRequirements;

            RefreshDataBar(bypass);
            RefreshMetrics(bypass);

            switch (shell.Page)
            {
                case TabSpace: RefreshSpace(bypass); break;
                case TabCyber: RefreshCyber(bypass); break;
                case TabSpecOps: RefreshSpecOps(bypass); break;
            }
            RefreshActionRows(shell.Page, bypass);

            bool armed = support.CommandArmed || support.ArmedAction.HasValue || support.LocalPickArmed;
            string alert = opsSnapshotSeen && !support.OpsStateFresh
                ? "HOST SNAPSHOT STALE · FIGURES MAY BE OUT OF DATE"
                : null;
            shell.WriteStatus(alert, armed ? support.Status : null,
                              support.Status ?? OpsDomains.Mission((OpsDomain)Mathf.Max(0, shell.Page)));
        }

        private bool opsSnapshotSeen;

        private void RefreshDataBar(bool bypass)
        {
            AvStyled.DataBar bar = shell.DataBar;
            string domain = shell.Page == TabSpace ? "SPACE" : shell.Page == TabCyber ? "CYBER" : "SPEC OPS";
            int sub = shell.Page == TabSpace ? spaceSub : shell.Page == TabCyber ? cyberSub : specSub;
            string location = domain + " / " + (sub == 0 ? "STATUS" : "ACTIONS");
            bool pending = support.RequestPending || support.CommandPending;
            bool armed = support.CommandArmed || support.ArmedAction.HasValue || support.LocalPickArmed;
            float cooldown = support.LocalCooldownRemaining;
            bool fresh = support.OpsStateFresh;
            opsSnapshotSeen |= fresh;

            if (pending)
            {
                bar.State.text = location + " · HOST ACK";
                bar.State.color = AvTheme.Warning;
            }
            else if (armed)
            {
                bar.State.text = location + " · R-CLICK MAP";
                bar.State.color = AvTheme.RailCaution;
            }
            else if (bypass)
            {
                bar.State.text = location + " · DEBUG BYPASS";
                bar.State.color = AvTheme.Warning;
            }
            else
            {
                bar.State.text = location;
                bar.State.color = AvTheme.Dim;
            }

            bar.SetChip(0, pending ? "PENDING" : cooldown > 0.5f ? "NET COOL" : "NET READY",
                        pending || cooldown > 0.5f ? "warn" : "live");
            bar.SetChip(1, fresh ? "LINKED" : opsSnapshotSeen ? "LINK STALE" : "SYNCING",
                        fresh ? "live" : opsSnapshotSeen ? "danger" : "info");
            bar.SetChip(2, armed ? "MAP ARMED" : WingLink.WingMapGestureArmed ? "WING MAP" : "MAP IDLE",
                        armed ? "warn" : WingLink.WingMapGestureArmed ? "info" : "inert");
        }

        private void RefreshMetrics(bool bypass)
        {
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;
            float cooldownTotal = support.LocalCooldownTotal;

            if (support.RequestPending || support.CommandPending)
                allocationMetric.Set(Compact(allocation), "AWAITING HOST", 1f, AvTheme.RailInfo);
            else if (cooldown > 0.5f && cooldownTotal > 0f)
                allocationMetric.Set(Compact(allocation), "NET T-" + Mathf.CeilToInt(cooldown) + "s",
                                     1f - cooldown / cooldownTotal, AvTheme.RailCaution);
            else
                allocationMetric.Set(Compact(allocation), bypass ? "BYPASS" : "NET READY", 1f,
                                     bypass ? AvTheme.Warning : AvTheme.RailReady);

            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            if (platform == null || !platform.Exists)
            {
                orbitMetric.Set("0/" + OrbitalPlatform.CellCount, "NO STATION", 0f, AvTheme.RailInert);
            }
            else
            {
                PlatformStats stats = platform.Stats(now);
                OrbitState state = platform.State(now, support.OrbitClock);
                bool holding = platform.HoldAt(now) != PlatformHold.None;
                orbitMetric.Set(stats.Modules + "/" + OrbitalPlatform.CellCount,
                                holding ? PlatformWords.Hold(platform.HoldAt(now))
                                : "ON STATION",
                                stats.Mass / OrbitalPlatform.MassLimit,
                                platform.Brownout ? AvTheme.RailDanger
                                : holding ? AvTheme.RailInfo
                                : state.InPass ? AvTheme.RailReady : AvTheme.RailCaution);
            }

            CyberNetwork cyber = support.LocalCyber;
            if (cyber == null || !cyber.HasCommand)
            {
                stationMetric.Set("0/0", "NO AIRBASE", 0f, AvTheme.RailInert);
            }
            else
            {
                int infocon = cyber.Infocon;
                CyberStats stats = cyber.Stats();
                // Hacked locations over nodes held: airbase infrastructure plus taken locations.
                float computing = cyber.ComputingCapacity() > 0f ? cyber.Computing / cyber.ComputingCapacity() : 0f;
                stationMetric.Set(stats.Hacked + "/" + stats.Nodes, CyberWords.Infocon(infocon), computing,
                                  infocon >= 5 ? AvTheme.RailReady : infocon >= 3 ? AvTheme.RailCaution : AvTheme.RailDanger);
            }

            SpecOpsDetachment detachment = support.LocalDetachment;
            if (detachment == null || !detachment.Enabled || !support.SpecOpsEnabled)
            {
                reserveMetric.Set("—", detachment == null ? "NO DATA" : "OFF", 0f, AvTheme.RailInert);
            }
            else
            {
                int ready = detachment.Count(TeamState.Ready);
                int posts = detachment.Posts();
                int field = detachment.Count(TeamState.EnRoute) + detachment.Count(TeamState.OnTask);
                reserveMetric.Set(ready + "/" + detachment.Formed,
                                  posts > 0 ? posts + (posts == 1 ? " POST" : " POSTS")
                                  : field > 0 ? field + " OUT" : "STANDBY",
                                  ready / (float)SpecOpsDetachment.TeamCount,
                                  posts > 0 ? AvTheme.RailReady : field > 0 ? AvTheme.RailInfo : AvTheme.RailInert);
            }
        }

        // ---- Formatting ------------------------------------------------------------------

        private static string Figure(float value) => Mathf.Round(value).ToString("N0", Invariant);

        private static string Compact(float value)
        {
            if (value >= 100000f) return (value / 1000f).ToString("0", Invariant) + "K";
            if (value >= 10000f) return (value / 1000f).ToString("0.0", Invariant) + "K";
            return Figure(value);
        }

        private static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (total / 60).ToString(Invariant) + ":" + (total % 60).ToString("00", Invariant);
        }
    }
}
