using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
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
    /// "OPS" — the multi-domain operations MFD: SPACE, EW, INFO, SPEC OPS and INTEL.
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
        private const float SectionGap = 10f;
        private const float HeaderHeight = 26f;

        private const int TabSpace = (int)OpsDomain.Space;
        private const int TabEw = (int)OpsDomain.ElectronicWarfare;
        private const int TabInfo = (int)OpsDomain.Information;
        private const int TabSpecOps = (int)OpsDomain.SpecialOperations;
        private const int TabIntel = (int)OpsDomain.Intelligence;

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
            ResetSpacePage();
            ResetEwPage();
            ResetInfoPage();
            ResetProgramPages();

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
                    new[] { "EW", "STN" },
                    new[] { "RESERVE", "INT" }
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            allocationMetric = FitMetric(shell.Metrics[0]);
            orbitMetric = FitMetric(shell.Metrics[1]);
            stationMetric = FitMetric(shell.Metrics[2]);
            reserveMetric = FitMetric(shell.Metrics[3]);

            BuildSpacePage();
            BuildEwPage();
            BuildInfoPage();
            BuildProgramPage(TabSpecOps, OpsReserve.SpecOps);
            BuildProgramPage(TabIntel, OpsReserve.Intel);

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
        /// Four metrics share the bezel, so a wide allocation figure would otherwise ellipsize.
        /// Shrink-to-fit keeps the sheet's size as the ceiling and never drops below body type.
        /// </summary>
        private static AvStyled.Metric FitMetric(AvStyled.Metric metric)
        {
            if (metric?.Value == null) return metric;
            metric.Value.enableAutoSizing = true;
            metric.Value.fontSizeMax = metric.Value.fontSize;
            metric.Value.fontSizeMin = AvTokens.FontLead;
            return metric;
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
            AvStyled.Spine(parent, new Rect(area.x, area.y, 3f, area.height));
            x = area.x + AvScreen.SpineInset;
            y = area.y;
            width = area.width - AvScreen.SpineInset;
            return parent;
        }

        /// <summary>A numbered section heading tied to the spine. Returns its live note.</summary>
        private static TMP_Text Header(RectTransform parent, float x, ref float y, float width,
                                       string title, string note)
        {
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);
            float half = width * 0.5f;
            AvStyled.Label(parent, new Rect(x, y, half, 14f), title, "section-title");
            TMP_Text noteLabel = AvStyled.Label(parent, new Rect(x + half, y, width - half, 14f), note ?? "",
                                                "section-title-note", align: TextAlignmentOptions.MidlineRight);
            AvKit.Rule(parent, new Rect(x, y - 18f, width, 1f), AvTheme.Hairline);
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
        /// The one row shape every page uses: status rail, code, name, a live status line, a
        /// wrapped detail line, and a trailing value with one or two buttons. Fixed height, so
        /// no refresh can make a row taller than the space it was laid out in.
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
            /// <summary>Optional rank strip in the left gutter (base-of-operations doctrine rows).</summary>
            public Image[] Pips;
            public string LastStatus;
            public Tone LastTone = (Tone)255;
        }

        private const float RowHeight = 66f;
        private const float CompactRowHeight = 48f;
        private const float TrailWidth = 104f;

        private static OpsRow Row(
            RectTransform parent, float x, float y, float width, bool compact,
            string code, string name, string detail,
            string primaryText, Action onPrimary,
            string secondaryText = null, Action onSecondary = null,
            AvButtonStyle primaryStyle = AvButtonStyle.Primary, Action onSelect = null)
        {
            float height = compact ? CompactRowHeight : RowHeight;
            var row = new OpsRow();

            row.Background = AvKit.Panel(parent, new Rect(x - 4f, y, width + 4f, height), Color.clear);
            if (onSelect != null)
                AvKit.HitButton(parent, new Rect(x, y, width - TrailWidth - 4f, height), onSelect);

            AvKit.Rule(parent, new Rect(x, y - height + 1f, width, 1f), AvTheme.Hairline.WithAlpha(0.3f));
            row.Rail = AvKit.Rule(parent, new Rect(x, y - 8f, 3f, height - 16f), AvTheme.RailInert);
            row.Code = AvStyled.Label(parent, new Rect(x + 10f, y - 6f, 38f, 16f), code, "row-sub");
            SingleLine(row.Code);

            bool trail = primaryText != null;
            float textX = x + 52f;
            float textWidth = width - 52f - (trail ? TrailWidth + 8f : 0f);

            row.Name = AvStyled.Label(parent, new Rect(textX, y - 6f, textWidth, 16f), name, "row-name");
            row.Status = SingleLine(AvStyled.Label(parent, new Rect(textX, y - 23f, textWidth, 14f), "", "row-sub"));
            if (!compact)
            {
                row.Detail = AvStyled.Label(parent, new Rect(textX, y - 39f, textWidth, 24f), detail ?? "", "row-sub");
                row.Detail.maxVisibleLines = 2;
                row.Detail.color = AvTheme.Disabled;
            }

            if (trail)
            {
                float trailX = x + width - TrailWidth;
                row.Value = AvStyled.Label(parent, new Rect(trailX, y - 6f, TrailWidth, 16f), "",
                                           "row-value", align: TextAlignmentOptions.MidlineRight);
                float buttonY = y - 25f;
                if (secondaryText != null)
                {
                    float half = (TrailWidth - 12f) * 0.5f;
                    row.Secondary = AvStyled.Button(parent, new Rect(trailX + 8f, buttonY, half, 24f),
                                                    secondaryText, "btn", onSecondary, AvButtonStyle.Danger);
                    row.Primary = AvStyled.Button(parent, new Rect(trailX + 12f + half, buttonY, half, 24f),
                                                  primaryText, "btn", onPrimary, primaryStyle);
                }
                else
                {
                    row.Primary = AvStyled.Button(parent, new Rect(trailX + 8f, buttonY, TrailWidth - 8f, 24f),
                                                  primaryText, "btn", onPrimary, primaryStyle);
                }
            }

            return row;
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

        // ---- Support-action rows (shared by SPACE, EW, INFO and SPEC OPS) -----------------

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
            if (action.IsHack) return TabInfo;
            if (SupportManager.OrbitalAbility(action.Id).HasValue) return TabSpace;
            if (action.Id == SupportActionId.FlareMissile) return TabEw;
            return TabSpecOps;
        }

        private int CountActions(int tab)
        {
            int count = 0;
            foreach (SupportActionDefinition action in support.Actions)
                if (HomeTab(action) == tab) count++;
            return count;
        }

        /// <summary>Build every action row this tab hosts; returns the new y.</summary>
        private float BuildActionRows(RectTransform parent, int tab, float x, float y, float width, string verb)
        {
            foreach (SupportActionDefinition action in support.Actions)
            {
                if (HomeTab(action) != tab) continue;
                SupportActionId id = action.Id;
                string code = action.IsHack ? CyberCatalog.Code(action.Hack.Value) : ActionCode(id);
                string detail = action.IsHack ? InfoOperations.Target(action.Hack.Value) : action.Description;
                var row = new ActionRow
                {
                    Definition = action,
                    Tab = tab,
                    Verb = verb,
                    View = Row(parent, x, y, width, false, code, action.Name, detail, verb,
                               () => { support.Arm(id); nextRefresh = 0f; })
                };
                actionRows.Add(row);
                y -= RowHeight;
            }
            return y;
        }

        private static string ActionCode(SupportActionId id)
        {
            switch (id)
            {
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
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;

            for (int i = 0; i < actionRows.Count; i++)
            {
                ActionRow row = actionRows[i];
                if (row.Tab != tab) continue;

                SupportActionDefinition action = row.Definition;
                float cost = support.Cost(action);
                bool armed = support.ArmedAction.HasValue && support.ArmedAction.Value == action.Id;
                row.View.Value.text = cost > 0f ? Figure(cost) : "—";
                if (action.Id == SupportActionId.Fortify)
                {
                    OpsGarrison garrison = support.LocalGarrison;
                    if (garrison != null)
                    {
                        int shells = garrison.FortificationShells;
                        row.View.Detail.text = "Doctrine: each accepted order occupies " + shells +
                                              (shells == 1 ? " defensive position." : " defensive positions.");
                    }
                }

                Tone tone;
                string status;
                bool enabled = false;

                if (!action.Enabled)
                {
                    tone = Tone.Locked;
                    status = "DISABLED IN HOST CONFIG";
                }
                else if (cost <= 0f)
                {
                    tone = Tone.Locked;
                    status = "UNAVAILABLE ON THIS MAP";
                }
                else if (armed)
                {
                    tone = Tone.Armed;
                    status = "ARMED · RIGHT-CLICK MAP OR TGT MARK";
                    enabled = true;
                }
                else if (support.RequestPending)
                {
                    tone = Tone.Pending;
                    status = "REQUEST PENDING · AWAITING HOST";
                }
                else if (!support.IsAuthorised(action))
                {
                    tone = Tone.Locked;
                    status = action.IsHack
                        ? InfoOperations.Explain(InfoGate.FacilityMissing, action.Hack.Value)
                        : action.Id == SupportActionId.FlareMissile
                            ? "LOCKED · SHARES RADAR SCAN PERK"
                            : "LOCKED · UNLOCK IN SQD ABILITIES";
                }
                else if (!Gate(action, out string gate))
                {
                    tone = Tone.Locked;
                    status = gate;
                }
                else if (cooldown > 0.5f)
                {
                    tone = Tone.Armed;
                    status = "NET COOLING · T-" + Mathf.CeilToInt(cooldown) + "s";
                }
                else if (!bypass && allocation + 0.001f < cost)
                {
                    tone = Tone.Danger;
                    status = "INSUFFICIENT ALLOCATION";
                }
                else
                {
                    tone = Tone.Ready;
                    status = gate != null ? "READY · " + gate : "READY · ARM, THEN RIGHT-CLICK MAP";
                    enabled = true;
                }

                row.View.Primary.SetEnabled(enabled);
                row.View.Primary.SetLatched(armed);
                row.View.Primary.SetText(armed ? "ABORT" : row.Verb);
                if (Paint(row.View, tone, status))
                {
                    row.View.Primary.WithTooltip(action.Name + " — " + (cost > 0f ? Figure(cost) + " ALLOC. " : "") +
                        action.Description + " " + status + ".");
                }
            }
        }

        /// <summary>Domain gates the host would refuse (station posture, a satellite of the payload
        /// with a round and power). An open orbital gate still reports its access planning note.</summary>
        private bool Gate(SupportActionDefinition action, out string reason)
        {
            reason = null;
            if (action.IsHack)
            {
                bool station = support.LocalEwAssetState != EwAssetState.None;
                InfoGate gate = InfoOperations.Evaluate(support.LocalInfo, action.Hack.Value, station,
                                                        support.LocalEwPosture);
                // HackAction re-checks facility powers and the station even under debug bypass.
                reason = InfoOperations.Explain(gate, action.Hack.Value);
                return gate == InfoGate.Ready;
            }

            reason = OrbitalGate(action, out bool open);
            return open;
        }

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
                case TabEw: RefreshEw(bypass); break;
                case TabInfo: RefreshInfo(bypass); break;
                case TabSpecOps: RefreshProgramPage(TabSpecOps, bypass); break;
                case TabIntel: RefreshProgramPage(TabIntel, bypass); break;
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
            bool pending = support.RequestPending || support.CommandPending;
            bool armed = support.CommandArmed || support.ArmedAction.HasValue || support.LocalPickArmed;
            float cooldown = support.LocalCooldownRemaining;
            bool fresh = support.OpsStateFresh;
            opsSnapshotSeen |= fresh;

            if (pending)
            {
                bar.State.text = "AWAITING HOST ACK";
                bar.State.color = AvTheme.Warning;
            }
            else if (armed)
            {
                bar.State.text = "ARMED · R-CLICK MAP";
                bar.State.color = AvTheme.RailCaution;
            }
            else if (bypass)
            {
                bar.State.text = "DEBUG BYPASS";
                bar.State.color = AvTheme.Warning;
            }
            else
            {
                bar.State.text = OpsDomains.Title((OpsDomain)Mathf.Clamp(shell.Page, 0, OpsDomains.All.Length - 1));
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
                                : state.InPass ? "LOS " + PlatformWords.Clock(state.TimeToPassEnd)
                                : "AOS " + PlatformWords.Clock(state.TimeToPass),
                                stats.Mass / OrbitalPlatform.MassLimit,
                                platform.Brownout ? AvTheme.RailDanger
                                : holding ? AvTheme.RailInfo
                                : state.InPass ? AvTheme.RailReady : AvTheme.RailCaution);
            }

            bool station = support.LocalEwAssetState != EwAssetState.None;
            EwPostureInfo posture = EwPostures.Info(support.LocalEwPosture);
            stationMetric.Set(station ? "1/1" : "0/1",
                              station ? posture.Code + " · " + posture.Discipline : "NONE",
                              station ? 1f : 0f, station ? AvTheme.RailReady : AvTheme.RailInert);

            OpsProgramLedger programs = support.LocalPrograms;
            int intel = programs != null ? programs.Tokens(OpsReserve.Intel) : 0;
            int specOps = programs != null ? programs.Tokens(OpsReserve.SpecOps) : 0;
            reserveMetric.Set(programs != null ? intel.ToString(Invariant) : "—",
                              "SOF " + specOps,
                              intel / (float)OpsProgramLedger.ReserveCap, AvTheme.RailInfo);
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
