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
    /// "OPS" — the operations console on the maximised map's left bezel.
    ///
    /// Rebuilt on the shared layout engine. Three changes, all structural rather than
    /// cosmetic, and each one fixing something the previous design could not:
    ///
    /// 1. <b>The metrics moved out of the tabs.</b> Allocation and score are what a pilot
    ///    checks constantly and both pages need them, so neither page owns them. They sit
    ///    above the tab bar at display size instead of as 12px body text inside whichever
    ///    tab happened to hold them.
    /// 2. <b>Rows size to their own copy.</b> Every card used to be a fixed 40 or 88
    ///    pixels with its description clipped to fit, which is why widening the panel from
    ///    430 to 470 did not stop the ellipses. <c>AvSize.Auto</c> ends that class of bug
    ///    outright — the row is exactly as tall as the wrapped text inside it.
    /// 3. <b>The support grid became a list.</b> A 2×3 grid of 88px cards showed four
    ///    actions and truncated all of them; one row per action shows six and truncates
    ///    none, because a full-width row has room a half-width card never had.
    ///
    /// Colour no longer encodes what an action *is*. The old <c>AccentFor</c> painted
    /// artillery amber and fortify emerald, colliding with the amber and emerald the rails
    /// spend on *state*; two meanings on one channel is what made the board unreadable.
    /// Kind is now a three-letter code, state is the rail.
    /// </summary>
    internal sealed class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float Pad = AvTokens.Pad;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.15f;

        /// <summary>How far the spine sits inside the panel padding.</summary>
        private const float SpineInset = 14f;

        private const float ChipWidth = 74f;
        private const float ChipGap = 2f;
        private const int ChipCount = 3;

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
        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;

        private GameObject perksPage;
        private GameObject supportPage;
        private GameObject theaterPage;
        private AvButton perksTab;
        private AvButton supportTab;
        private AvButton theaterTab;
        private ITheaterPage theater;

        private AvStyled.DataBar dataBar;
        private AvStyled.Metric allocMetric;
        private AvStyled.Metric scoreMetric;

        private TMP_Text statusText;
        private string activeHoverTooltip;

        private readonly List<PerkRow> perkRows = new List<PerkRow>();
        private readonly List<StrikeRow> strikeRows = new List<StrikeRow>();

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
            theater?.Unmount();
            BezelRegistry.Release(BezelRegistry.Ops);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            font = null;
            perksPage = null;
            supportPage = null;
            theaterPage = null;
            perksTab = null;
            supportTab = null;
            theaterTab = null;
            theater = null;
            dataBar = null;
            allocMetric = null;
            scoreMetric = null;
            statusText = null;
            activeHoverTooltip = null;
            perkRows.Clear();
            strikeRows.Clear();
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            SetViewOpen(false);
        }

        private void OnDestroy() => SetViewOpen(false);

        private void Update()
        {
            if (failed || support == null || progression == null) return;
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

                if (!MfdBezel.TryClaim(BezelRegistry.Ops, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger.LogWarning("OPS MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    BezelRegistry.Release(BezelRegistry.Ops);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    BezelRegistry.Release(BezelRegistry.Ops);
                    failed = true;
                    return;
                }

                MfdBezel.Bind(mfd, buttons, screens, slot, left, screen);
                logger.LogInfo("OPS MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
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
            // Position is deliberately not copied. VirtualMFD.showPos is Vector3.zero and
            // MFDScreen.ShowScreen assigns it straight to localPosition, so a screen has no
            // remembered home — it is placed by its parent and anchors, and an
            // anchoredPosition written here is overwritten whenever the panel is opened.
            rootRect.localScale = templateRect.localScale;
            rootRect.sizeDelta = new Vector2(Width, PanelHeight);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            ModServices.TryGet(out theater);
            int tabCount = theater != null ? 3 : 2;

            // The panel shell, declared once. Every rectangle below is read out of this
            // tree rather than accumulated by a running `y -= 34f` cursor.
            AvNode shell = AvBox.Column("ops").Pad(Pad).Gaps(AvTokens.Space2)
                .Add(AvBox.Row("databar").Height(AvTokens.TitleBarHeight + 2f))
                .Add(AvBox.Grid("metrics", 2).Height(58f).Gaps(0f)
                    .Add(AvBox.Cell("alloc"), AvBox.Cell("score")))
                .Add(AvBox.Row("tabs").Height(AvTokens.TabBarHeight).Gaps(1f))
                .Add(AvBox.Cell("body").Grow())
                .Add(AvBox.Cell("status").Height(AvTokens.StatusStripHeight));

            AvNode tabs = shell.Find("tabs");
            for (int i = 0; i < tabCount; i++) tabs.Add(AvBox.Cell("t" + i).Grow());

            shell.Arrange(new Rect(0f, 0f, Width, PanelHeight));

            dataBar = AvStyled.TopBar(content, shell.At("databar"), "OPS", ChipCount);
            AvKit.HitButton(content, ChipRect(shell.At("databar"), 2), () =>
            {
                ThirdPersonHudController.Instance?.Toggle();
                nextRefresh = 0f;
            }).WithTooltip("Toggle the third-person HUD overlay.");

            AvStyled.Box(content, shell.At("metrics"), "metrics");
            allocMetric = AvStyled.MetricCell(content, shell.At("metrics.alloc"), "ALLOCATION", "ALLOC");
            scoreMetric = AvStyled.MetricCell(content, shell.At("metrics.score"), "MISSION SCORE", "PTS");
            AvKit.Rule(content, VerticalDivider(shell.At("metrics")), AvTheme.Hairline);

            perksTab = AvStyled.Button(content, shell.At("tabs.t0"), "PERKS", "tab", ShowPerks, AvButtonStyle.Tab);
            supportTab = AvStyled.Button(content, shell.At("tabs.t1"), "SUPPORT", "tab", ShowSupport, AvButtonStyle.Tab);
            if (theater != null)
                theaterTab = AvStyled.Button(content, shell.At("tabs.t2"), "THEATER", "tab", ShowTheater, AvButtonStyle.Tab);

            Rect body = shell.At("body");

            perksPage = CreatePage(content, "PerksPage");
            BuildPerksPage((RectTransform)perksPage.transform, body);

            supportPage = CreatePage(content, "SupportPage");
            BuildSupportPage((RectTransform)supportPage.transform, body);

            if (theater != null)
            {
                theaterPage = CreatePage(content, "TheaterPage");
                // The theater page still lays itself out against a width and a cursor, so
                // it gets the same inner column the other two pages work inside.
                theater.Mount((RectTransform)theaterPage.transform,
                              font, body.width - SpineInset, body.y);
            }

            statusText = AvStyled.StatusStrip(content, shell.At("status"));

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = "OPS";
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
            SetPage(1);
            return result;
        }

        /// <summary>Where the nth status chip sits in the data bar, so a hit target can cover it.</summary>
        private static Rect ChipRect(Rect bar, int index)
        {
            float chipsWidth = ChipCount * ChipWidth + (ChipCount - 1) * ChipGap;
            float x = bar.x + bar.width - chipsWidth - 6f + index * (ChipWidth + ChipGap);
            return new Rect(x, bar.y - (bar.height - 16f) * 0.5f, ChipWidth, 16f);
        }

        private static Rect VerticalDivider(Rect metrics) =>
            new Rect(metrics.x + metrics.width * 0.5f, metrics.y, 1f, metrics.height);

        // ---- Page scaffolding ------------------------------------------------------------

        /// <summary>
        /// A section: a title line plus one auto-height row per entry.
        ///
        /// Sections are open — a band and a spine tick, no enclosing rectangle. Four
        /// hairlines around every group is what made the old panel read as a wall of
        /// boxes with nothing more important than anything else.
        /// </summary>
        private static AvNode Section(string name, IList<string> descriptions)
        {
            AvNode section = AvBox.Column(name).Pad(12f, 14f, 14f, 12f).Gaps(0f)
                .Add(AvBox.Cell("title").Height(20f));
            for (int i = 0; i < descriptions.Count; i++)
                section.Add(RowNode("r" + i, descriptions[i]));
            return section;
        }

        /// <summary>
        /// One list row: rail, code, a growing text column, and a trailing slot.
        ///
        /// <para>The description is handed to the box <b>as text</b>, not as an empty cell to
        /// be filled in later. That distinction is the whole feature: an empty
        /// <c>Auto()</c> cell measures zero, the row collapses to the height of its one-line
        /// name, and the cost and the action button end up drawn on top of each other. The
        /// box can only size to content it has been given.</para>
        /// </summary>
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

            // Split the line rather than drawing both labels across its whole width: a
            // right-aligned note and a left-aligned title in the same rect collide in the
            // middle, which is how "TACTICAL SUPPORT" and "RIGHT-CLICK MAP TO DESIGNATE"
            // came out overprinted on each other.
            Rect titleRect = node.At("title");
            float titleWidth = titleRect.width * 0.42f;

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

        /// <summary>The thin divider under a row. Rows separate by a line, not by a box.</summary>
        private static void RowSeparator(RectTransform parent, Rect area) =>
            AvKit.Rule(parent, new Rect(area.x, area.y - area.height, area.width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        // ---- Perks page ------------------------------------------------------------------

        /// <summary>What a perk is, as a code. Kind on the code, state on the rail.</summary>
        private static string PerkCode(bool isAuthorisation) => isAuthorisation ? "AUT" : "PAS";

        private void BuildPerksPage(RectTransform parent, Rect body)
        {
            PerkView[] perks = progression.GetPerks();

            var passives = new List<PerkView>();
            var auths = new List<PerkView>();
            for (int i = 0; i < perks.Length; i++)
            {
                bool isAuth = perks[i].Group != null &&
                              perks[i].Group.IndexOf("AUTHORIS", StringComparison.OrdinalIgnoreCase) >= 0;
                (isAuth ? auths : passives).Add(perks[i]);
            }

            AvNode page = AvBox.Column("perks").Gaps(0f)
                .Add(Section("passive", Descriptions(passives)))
                .Add(Section("auth", Descriptions(auths)))
                .Add(AvBox.Filler());
            page.Arrange(body);

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            BuildPerkSection(parent, page.Find("passive"), page.At("passive"),
                             "PASSIVE SYSTEMS", passives, band: false);
            BuildPerkSection(parent, page.Find("auth"), page.At("auth"),
                             "STRIKE AUTHORISATIONS", auths, band: true);
        }

        private void BuildPerkSection(
            RectTransform parent, AvNode node, Rect area, string title,
            List<PerkView> perks, bool band)
        {
            if (perks.Count == 0) return;

            DrawSectionHeader(parent, node, area, title, perks.Count.ToString(), band);

            for (int i = 0; i < perks.Count; i++)
                AddPerkRow(parent, node.Find("r" + i), perks[i], PerkCode(band));
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
                progression.RequestUnlock(id);
                nextRefresh = 0f;
            });
            perk.Button.SetRowHighlight(perk.Background, Color.clear, HoverFill());
            perk.Button.WithTooltip(
                view.Name.ToUpperInvariant() + " — costs " + view.Cost +
                (view.Cost == 1 ? " point" : " points") + ". " + view.Description +
                " One point per " + Math.Max(1, progression.ScorePerPoint) + " mission score.");

            perkRows.Add(perk);
        }

        private static List<string> Descriptions(List<PerkView> perks)
        {
            var text = new List<string>(perks.Count);
            for (int i = 0; i < perks.Count; i++) text.Add(perks[i].Description);
            return text;
        }

        private static Color HoverFill() =>
            AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised);

        // ---- Support page ----------------------------------------------------------------

        /// <summary>What an action is, as a code. Never its state.</summary>
        private static string ActionCode(SupportActionId id)
        {
            switch (id)
            {
                case SupportActionId.Artillery: return "ART";
                case SupportActionId.Fortify: return "FTF";
                case SupportActionId.Recon: return "SAT";
                case SupportActionId.Emp: return "EMP";
                case SupportActionId.Firebreak: return "FIR";
                case SupportActionId.SmokeMarker: return "SMK";
                default: return "OPS";
            }
        }

        private void BuildSupportPage(RectTransform parent, Rect body)
        {
            IReadOnlyList<SupportActionDefinition> actions = support.Actions;

            var descriptions = new List<string>();
            for (int i = 0; i < actions.Count; i++) descriptions.Add(actions[i].Description);

            AvNode page = AvBox.Column("support").Gaps(0f)
                .Add(Section("strikes", descriptions))
                .Add(AvBox.Filler());
            page.Arrange(body);

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            AvNode section = page.Find("strikes");
            DrawSectionHeader(parent, section, page.At("strikes"),
                              "TACTICAL SUPPORT", "RIGHT-CLICK MAP TO DESIGNATE", band: false);

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
                new Rect(trail.x + 10f, trail.y - 19f, trail.width - 10f, 22f),
                "CALL IN", "btn",
                () => { support.Request(id); nextRefresh = 0f; },
                AvButtonStyle.Primary);

            strike.Action.WithTooltip(
                definition.Name.ToUpperInvariant() + " — " +
                support.Cost(definition).ToString("0") + " alloc. " + definition.Description +
                " Authorised by the '" + progression.PerkNameFor(definition.Capability) + "' perk.");

            strikeRows.Add(strike);
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

            if (theaterPage != null && theaterPage.activeSelf) theater?.RefreshView();

            UpdateStatusStrip();
        }

        private void RefreshDataBar(bool bypass)
        {
            bool wingPresent = !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid));
            bool hud = ThirdPersonHudController.Instance != null &&
                       ThirdPersonHudController.Instance.IsEnabled;
            bool underAttack = baseAlarm != null && baseAlarm.IsBaseUnderAttack;

            if (underAttack)
            {
                dataBar.State.text = "BASE UNDER ATTACK";
                dataBar.State.color = AvTheme.Alert;
            }
            else if (bypass)
            {
                dataBar.State.text = "DEBUG BYPASS — COSTS IGNORED";
                dataBar.State.color = AvTheme.Warning;
            }
            else
            {
                dataBar.State.text = "THEATER LOGISTICS";
                dataBar.State.color = AvTheme.Dim;
            }

            dataBar.SetChip(0, "LOGISTICS", true);
            dataBar.SetChip(1, wingPresent ? "WING LINK" : "NO WING", wingPresent);
            dataBar.SetChip(2, hud ? "3RD HUD" : "HUD OFF", hud);
        }

        private void RefreshMetrics(bool bypass)
        {
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;
            float cooldownTotal = support.LocalCooldownTotal;
            bool wingPresent = !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid));

            string allocCaption;
            float allocFraction;
            Color allocFill;

            if (cooldown > 0.5f && cooldownTotal > 0f)
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
                allocCaption = wingPresent ? "SHARED WITH WING COMMAND" : "SUPPORT NET READY";
                allocFraction = 1f;
                allocFill = AvTheme.RailReady;
            }

            allocMetric.Set(allocation.ToString("N0"), allocCaption, allocFraction, allocFill);

            int score = progression.Score;
            int perPoint = Math.Max(1, progression.ScorePerPoint);
            int intoPoint = score % perPoint;
            int ceiling = Mathf.Max(1, progression.MaximumPoints);
            int avail = progression.AvailablePoints;

            scoreMetric.Set(
                bypass ? "FREE" : score.ToString("N0"),
                bypass
                    ? "ALL PERKS UNLOCKED"
                    : avail + (avail == 1 ? " PT · " : " PTS · ") + (perPoint - intoPoint) + " TO NEXT",
                bypass ? 1f : intoPoint / (float)perPoint,
                bypass ? AvTheme.Warning : AvTheme.RailReady);

            scoreMetric.Unit.text = bypass
                ? "BYPASS"
                : "PTS · RANK " + progression.Rank + "/" + ceiling;
        }

        private void RefreshPerkRows()
        {
            PerkView[] perks = progression.GetPerks();

            for (int i = 0; i < perkRows.Count; i++)
            {
                PerkRow row = perkRows[i];
                if (!TryFind(perks, row.Id, out PerkView view)) continue;

                row.Button.SetEnabled(view.Affordable && !view.Unlocked);

                if (view.Unlocked)
                    PaintPerk(row, "ready", AvTheme.TextPrimary, "ACTIVE", AvTheme.RailReady);
                else if (view.Affordable)
                    PaintPerk(row, "armed", AvTheme.TextPrimary, "UNLOCK " + view.Cost + "P", AvTheme.RailCaution);
                else
                    PaintPerk(row, "locked", AvTheme.Dim, view.Cost + "P REQ", AvTheme.Dim);
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

        private static Color RailColour(string state) =>
            AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);

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

                if (!row.Definition.Enabled)
                {
                    SetRowState(row, "locked", "SERVER DISABLED", AvTheme.Dim, "OFF", false, false);
                }
                else if (cost <= 0f)
                {
                    SetRowState(row, "locked", "UNAVAILABLE ON THIS MAP", AvTheme.Dim, "N/A", false, false);
                }
                else if (!isAuth)
                {
                    SetRowState(row, "locked",
                        "LOCKED · REQUIRES '" +
                        progression.PerkNameFor(row.Definition.Capability).ToUpperInvariant() + "'",
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
                    SetRowState(row, "armed", "ARMED · RIGHT-CLICK THE MAP TO DESIGNATE",
                        AvTheme.RailCaution, "ABORT", true, true);
                }
                else
                {
                    SetRowState(row, "ready",
                        "AUTH: " + progression.PerkNameFor(row.Definition.Capability).ToUpperInvariant() +
                        " · CLEARED",
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

            row.Status.text = status;
            row.Status.color = statusColor;

            row.Action.SetText(button);
            row.Action.SetEnabled(ready || armed);
            row.Action.SetLatched(armed);
        }

        /// <summary>
        /// The strip's priority order: a base under attack outranks everything, then the
        /// hovered control's explanation, then the armed prompt, then idle telemetry.
        /// </summary>
        private void UpdateStatusStrip()
        {
            if (statusText == null) return;

            string alert = baseAlarm != null ? baseAlarm.ActiveAlertTicker : string.Empty;
            if (!string.IsNullOrEmpty(alert))
            {
                statusText.text = "> " + alert;
                statusText.color = AvTheme.Alert;
                return;
            }

            string hovered = AvButton.HoveredTooltip;
            if (!string.IsNullOrEmpty(hovered))
            {
                statusText.text = "> " + hovered;
                statusText.color = AvTheme.Friendly;
                return;
            }

            if (!string.IsNullOrEmpty(activeHoverTooltip))
            {
                statusText.text = "> " + activeHoverTooltip;
                statusText.color = AvTheme.Friendly;
                return;
            }

            string fire = support != null ? support.FireTelemetry : string.Empty;
            string baseLine = (progression != null ? progression.Status : "") + " · " +
                              (support != null ? support.Status : "");

            statusText.text = "> " + (string.IsNullOrEmpty(fire) ? baseLine : baseLine + " · " + fire);
            statusText.color = AvTheme.Dim;
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

        // ---- Tabs & navigation -----------------------------------------------------------

        private void ShowPerks() => SetPage(0);
        private void ShowSupport() => SetPage(1);
        private void ShowTheater() => SetPage(2);

        private void SetPage(int index)
        {
            perksPage?.SetActive(index == 0);
            supportPage?.SetActive(index == 1);
            theaterPage?.SetActive(index == 2);

            perksTab?.SetLatched(index == 0);
            supportTab?.SetLatched(index == 1);
            theaterTab?.SetLatched(index == 2);

            activeHoverTooltip = null;
            nextRefresh = 0f;
        }

        private static GameObject CreatePage(RectTransform parent, string name)
        {
            var page = new GameObject(name, typeof(RectTransform));
            RectTransform rect = page.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            AvKit.Stretch(rect);
            return page;
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
