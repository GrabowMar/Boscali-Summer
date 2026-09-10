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
    /// "OPS" — the pilot's own console on the maximised map.
    ///
    /// <para>OPS answers one question: what have you earned, and what may you call in. The
    /// theater picture used to be a third tab here, opening a second row of tabs inside the
    /// first; it is now its own screen, and the two stopped competing for the same 400
    /// pixels. What OPS got back it spent on the perk board, which is why the passive
    /// systems and the strike authorisations are now a page each instead of two sections
    /// fighting over one.</para>
    ///
    /// Built on the shared layout engine. Three earlier changes still hold, all structural
    /// rather than cosmetic, and each one fixing something the previous design could not:
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

        private const int TabPassive = 0;
        private const int TabAuth = 1;
        private const int TabSupport = 2;
        private const int TabRecord = 3;

        /// <summary>Pip slots kept for the perk-point budget. Servers configure the ceiling.</summary>
        private const int MaximumBudgetPips = 16;

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
        private IObservationSource observations;
        private IThirdPersonHud thirdPersonHud;
        private TMP_Text observationText;
        private AvButton captureMark, callAtMark, clearMark;
        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;

        private AvScreen shell;

        private AvStyled.DataBar dataBar;
        private AvStyled.Metric allocMetric;
        private AvStyled.Metric scoreMetric;

        private TMP_Text statusText;
        private string activeHoverTooltip;

        private readonly List<PerkRow> perkRows = new List<PerkRow>();
        private readonly List<StrikeRow> strikeRows = new List<StrikeRow>();

        // ---- Record page ---------------------------------------------------------------

        private TMP_Text rankValue;
        private TMP_Text scoreValue;
        private TMP_Text perPointValue;
        private TMP_Text earnedValue;
        private TMP_Text spentValue;
        private TMP_Text availableValue;
        private TMP_Text unlockedList;
        private Image[] budgetPips;

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
            BezelRegistry.Release(BezelRegistry.Ops);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            font = null;
            shell = null;
            dataBar = null;
            allocMetric = null;
            scoreMetric = null;
            statusText = null;
            observations = null;
            thirdPersonHud = null;
            observationText = null;
            captureMark = callAtMark = clearMark = null;
            activeHoverTooltip = null;
            perkRows.Clear();
            strikeRows.Clear();
            rankValue = scoreValue = perPointValue = null;
            earnedValue = spentValue = availableValue = unlockedList = null;
            budgetPips = null;
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

            // Take the bay the column actually has. It is roughly 900px once the mission
            // clock and the spawn strip are reserved, and this panel used to take 596 of it.
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
                new[] { "PASSIVE", "AUTH", "SUPPORT", "RECORD" },
                new[]
                {
                    new[] { "ALLOCATION", "ALLOC" },
                    new[] { "MISSION SCORE", "PTS" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            dataBar = shell.DataBar;
            allocMetric = shell.Metrics[0];
            scoreMetric = shell.Metrics[1];
            statusText = shell.Status;

            AvKit.HitButton(content, ChipRect(shell.Body, 2), () =>
            {
                thirdPersonHud?.Toggle();
                nextRefresh = 0f;
            }).WithTooltip("Toggle the third-person HUD overlay.");

            Rect body = shell.Body;

            SplitPerks(out List<PerkView> passives, out List<PerkView> auths);

            BuildPerkPage((RectTransform)shell.CreatePage(TabPassive, "PassivePage").transform,
                          body, passives, "PASSIVE SYSTEMS", "ALWAYS ON", PassiveCode, band: false);
            BuildPerkPage((RectTransform)shell.CreatePage(TabAuth, "AuthPage").transform,
                          body, auths, "STRIKE AUTHORISATIONS", "CLEARS A CALL-IN",
                          AuthCode, band: true);

            BuildSupportPage((RectTransform)shell.CreatePage(TabSupport, "SupportPage").transform, body);
            BuildRecordPage((RectTransform)shell.CreatePage(TabRecord, "RecordPage").transform, body);

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
            shell.SetPage(TabSupport);
            return result;
        }

        /// <summary>
        /// Where the nth status chip sits, so a hit target can cover it. The data bar is the
        /// first row of the shell, which puts it at the top padding by construction.
        /// </summary>
        private static Rect ChipRect(Rect body, int index)
        {
            const float barHeight = AvTokens.TitleBarHeight + 2f;
            float chipsWidth = ChipCount * ChipWidth + (ChipCount - 1) * ChipGap;
            float x = body.x + body.width - chipsWidth - 6f + index * (ChipWidth + ChipGap);
            return new Rect(x, -Pad - (barHeight - 16f) * 0.5f, ChipWidth, 16f);
        }

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
        private const string PassiveCode = "PAS";

        private const string AuthCode = "AUT";

        /// <summary>
        /// Split the board on the only distinction that changes what a perk does: a passive
        /// improves a number you already have, an authorisation unlocks a call-in you do
        /// not. They are separate pages because they are separate decisions.
        /// </summary>
        private void SplitPerks(out List<PerkView> passives, out List<PerkView> auths)
        {
            PerkView[] perks = progression.GetPerks();
            passives = new List<PerkView>();
            auths = new List<PerkView>();

            for (int i = 0; i < perks.Length; i++)
            {
                bool isAuth = perks[i].Group != null &&
                              perks[i].Group.IndexOf("AUTHORIS", StringComparison.OrdinalIgnoreCase) >= 0;
                (isAuth ? auths : passives).Add(perks[i]);
            }
        }

        private void BuildPerkPage(
            RectTransform parent, Rect body, List<PerkView> perks,
            string title, string note, string code, bool band)
        {
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            if (perks.Count == 0)
            {
                AvStyled.Label(parent, new Rect(body.x + SpineInset, body.y, body.width - SpineInset, 40f),
                               "No " + title.ToLowerInvariant() + " are configured on this server.",
                               "row-sub");
                return;
            }

            AvNode page = AvBox.Column("perks").Gaps(0f)
                .Add(Section("list", Descriptions(perks)))
                .Add(AvBox.Filler());
            page.Arrange(body);

            AvNode section = page.Find("list");
            Rect area = page.At("list");

            // A page of rows can outgrow even the taller bay once descriptions wrap.
            if (area.height > body.height)
            {
                parent = AvScreen.Scroll(parent, body, area.height, out Rect scrolled);
                page.Arrange(scrolled);
                section = page.Find("list");
                area = page.At("list");
                AvStyled.Spine(parent, new Rect(scrolled.x, scrolled.y, 3f, scrolled.height));
            }

            DrawSectionHeader(parent, section, area, title, note, band);

            for (int i = 0; i < perks.Count; i++)
                AddPerkRow(parent, section.Find("r" + i), perks[i], code);
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
                case SupportActionId.FlareMissile: return "FLR";
                default: return "OPS";
            }
        }

        private void BuildSupportPage(RectTransform parent, Rect body)
        {
            if (observations != null)
            {
                observationText = AvKit.Label(parent, "No camera mark.",
                    new Rect(body.x + SpineInset, body.y, body.width - SpineInset, 32f), AvTheme.Dim, 12f);
                float x = body.x + SpineInset;
                float buttonWidth = (body.width - SpineInset - 8f) / 3f;
                captureMark = AvStyled.Button(parent, new Rect(x, body.y - 36f, buttonWidth, 24f),
                    "MARK CAMERA", "btn", () => { observations.Capture(); nextRefresh = 0f; })
                    .WithTooltip("Record the surface at the centre of the live native camera. One local mark, expires after 120 seconds.");
                callAtMark = AvStyled.Button(parent, new Rect(x + buttonWidth + 4f, body.y - 36f, buttonWidth, 24f),
                    "CALL AT MARK", "btn", () => { support.RequestAtMark(observations); nextRefresh = 0f; })
                    .WithTooltip("Select a support action below first, then confirm execution at this camera mark. Normal cost and host validation apply.");
                clearMark = AvStyled.Button(parent, new Rect(x + (buttonWidth + 4f) * 2f, body.y - 36f, buttonWidth, 24f),
                    "CLEAR MARK", "btn", () => { observations.Clear(); nextRefresh = 0f; });
                body = new Rect(body.x, body.y - 68f, body.width, body.height - 68f);
            }
            IReadOnlyList<SupportActionDefinition> actions = support.Actions;

            var descriptions = new List<string>();
            for (int i = 0; i < actions.Count; i++) descriptions.Add(actions[i].Description);

            AvNode page = AvBox.Column("support").Gaps(0f)
                .Add(Section("strikes", descriptions))
                .Add(AvBox.Filler());
            page.Arrange(body);

            // Keep the mark controls fixed and long action descriptions inside the page.
            if (page.At("strikes").height > body.height)
            {
                parent = AvScreen.Scroll(parent, body, page.At("strikes").height, out Rect scrolled);
                page.Arrange(scrolled);
                body = scrolled;
            }

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            AvNode section = page.Find("strikes");
            DrawSectionHeader(parent, section, page.At("strikes"),
                              "TACTICAL SUPPORT", "SCROLL ACTIONS · RIGHT-CLICK MAP", band: false);

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

        // ---- Record page -----------------------------------------------------------------

        /// <summary>A label/figure pair on one line, the figure right-aligned to the gutter.</summary>
        private static TMP_Text KeyValue(
            RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.62f, 16f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.62f, y, width * 0.38f, 16f),
                                  "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        /// <summary>
        /// The career page: where the points came from and what they went on.
        ///
        /// <para>The metric row above the tabs carries score and the points it has bought,
        /// but a running total cannot say how close the next point is, how many are still
        /// unspent, or what the spent ones bought. Those are the numbers a pilot checks
        /// before deciding whether to hold a point back, so they get a page.</para>
        /// </summary>
        private void BuildRecordPage(RectTransform parent, Rect body)
        {
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            y = RecordHeader(parent, x, y, width, "SERVICE RECORD", "THIS MISSION", band: false);

            rankValue = KeyValue(parent, x, y, width, "PILOT RANK");
            y -= 18f;
            scoreValue = KeyValue(parent, x, y, width, "MISSION SCORE");
            y -= 18f;
            perPointValue = KeyValue(parent, x, y, width, "SCORE PER PERK POINT");
            y -= 26f;

            y = RecordHeader(parent, x, y, width, "PERK BUDGET", "EARNED · SPENT", band: true);

            earnedValue = KeyValue(parent, x, y, width, "POINTS EARNED");
            y -= 18f;
            spentValue = KeyValue(parent, x, y, width, "POINTS COMMITTED");
            y -= 18f;
            availableValue = KeyValue(parent, x, y, width, "POINTS UNSPENT");
            y -= 26f;

            // One pip per point in the ceiling: filled where committed, outlined where the
            // point is banked, dark where it has not been earned yet. A bar cannot show
            // three states of a budget that only ever runs to single digits.
            int ceiling = Mathf.Clamp(progression.MaximumPoints, 1, MaximumBudgetPips);
            budgetPips = new Image[ceiling];
            for (int i = 0; i < ceiling; i++)
            {
                var pip = new Rect(x + i * 15f, y, 12f, 12f);
                AvKit.Outline(parent, pip, AvTheme.Hairline);
                budgetPips[i] = AvKit.Panel(parent, new Rect(pip.x + 2f, pip.y - 2f, 8f, 8f),
                                            Color.clear);
            }
            y -= 26f;

            y = RecordHeader(parent, x, y, width, "COMMITTED SYSTEMS", null, band: false);

            unlockedList = AvStyled.Label(parent, new Rect(x, y, width, 120f), "", "row-sub");
        }

        /// <summary>A section header on the record page's spine. Title and note split the line.</summary>
        private static float RecordHeader(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
        {
            if (band) AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 7f);

            float half = width * 0.5f;
            AvStyled.Label(parent, new Rect(x, y, half, 14f), title, "section-title");
            if (!string.IsNullOrEmpty(note))
            {
                AvStyled.Label(parent, new Rect(x + half, y, width - half, 14f), note,
                               "section-title-note", align: TextAlignmentOptions.MidlineRight);
            }
            return y - 22f;
        }

        private void RefreshRecord(bool bypass)
        {
            if (rankValue == null || progression == null) return;

            int score = progression.Score;
            int perPoint = Math.Max(1, progression.ScorePerPoint);
            int earned = progression.EarnedPoints;
            int available = progression.AvailablePoints;
            int spent = Math.Max(0, earned - available);
            int ceiling = Math.Max(1, progression.MaximumPoints);

            rankValue.text = progression.Rank.ToString();
            scoreValue.text = score.ToString("N0");
            perPointValue.text = perPoint.ToString("N0") + "  ·  " +
                                 (perPoint - score % perPoint) + " TO NEXT";

            earnedValue.text = bypass ? "BYPASS" : earned + " / " + ceiling;
            spentValue.text = bypass ? "—" : spent.ToString();
            availableValue.text = bypass ? "UNLIMITED" : available.ToString();
            availableValue.color = !bypass && available > 0 ? AvTheme.RailReady : AvTheme.TextPrimary;

            for (int i = 0; i < budgetPips.Length; i++)
            {
                budgetPips[i].color = bypass || i < spent ? AvTheme.Accent
                                    : i < earned ? AvTheme.RailReady
                                    : Color.clear;
            }

            PerkView[] perks = progression.GetPerks();
            var committed = new List<string>();
            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].Unlocked) committed.Add(perks[i].Name.ToUpperInvariant());
            }

            unlockedList.text = committed.Count == 0
                ? "Nothing committed yet. Points are spent on the PASSIVE and AUTH pages."
                : string.Join("  ·  ", committed.ToArray());
            unlockedList.color = committed.Count == 0 ? AvTheme.Dim : AvTheme.TextPrimary;
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
            RefreshRecord(bypass);

            UpdateStatusStrip();
        }

        private void RefreshDataBar(bool bypass)
        {
            bool wingPresent = !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid));
            bool hud = thirdPersonHud != null && thirdPersonHud.IsEnabled;
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
            dataBar.SetChip(2, thirdPersonHud == null ? "HUD N/A" : hud ? "3RD HUD" : "HUD OFF", hud);
        }

        private void RefreshObservation()
        {
            if (observationText == null || observations == null) return;
            bool marked = observations.TryGet(out ObservationPoint point);
            observationText.text = marked
                ? $"{point.Source} MARK · X {point.X:0} / Z {point.Z:0}\n{Mathf.Max(0f, Time.unscaledTime - point.RecordedAt):0}s OLD · RANGE {point.Range / 1000f:0.0} km · FIXED POINT"
                : observations.Status;
            bool canCapture = observations.CanCapture;
            captureMark.SetEnabled(canCapture);
            captureMark.WithTooltip(canCapture
                ? "Record a fixed point from the native camera; this does not call support."
                : "Requires a live native camera on your own aircraft, with no pause or spectator view.");
            bool armed = support.ArmedAction.HasValue && MapPicker.IsOwner(MapPicker.Support);
            callAtMark.SetEnabled(marked && armed);
            callAtMark.WithTooltip(!marked ? "Capture a fresh camera mark first."
                : !armed ? "Select a support action below first."
                : "Confirm the selected support at this fixed point. Normal cost and host validation apply.");
            clearMark.SetEnabled(marked);
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
            if (shell == null) return;

            string fire = support != null ? support.FireTelemetry : string.Empty;
            string baseLine = (progression != null ? progression.Status : "") + " · " +
                              (support != null ? support.Status : "");

            shell.WriteStatus(
                baseAlarm != null ? baseAlarm.ActiveAlertTicker : null,
                activeHoverTooltip,
                string.IsNullOrEmpty(fire) ? baseLine : baseLine + " · " + fire);
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
