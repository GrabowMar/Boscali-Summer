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
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Cockpit MFD screen for Boscali Operations: the perk board and the support board.
    /// Redesigned in the unified 5th-gen fighter cockpit avionics language at 470px width,
    /// with SDF chamfered bezels, tactile cards, visual progression meters, and an integrated status strip.
    /// </summary>
    internal sealed class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float Pad = AvTokens.Pad;
        private const float Gap = AvTokens.Gap;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float TabHeight = AvTokens.TabBarHeight;
        private const float RefreshInterval = 0.15f;

        private sealed class PerkCard
        {
            public byte Id;
            public GameObject Root;
            public AvButton Button;
            public Image Rail;
            public Image Background;
            public TMP_Text Name;
            public TMP_Text Badge;
            public TMP_Text Subtitle;
        }

        private sealed class SupportCard
        {
            public SupportActionDefinition Definition;
            public GameObject Root;
            public AvButton RequestButton;
            public Image Rail;
            public Image Background;
            public TMP_Text Name;
            public TMP_Text CostChip;
            public TMP_Text AuthStatus;
            public TMP_Text Description;
            public TMP_Text StateLabel;
        }

        private SupportManager support;
        private IProgressionView progression;
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

        // Perks Page Widgets
        private TMP_Text scoreLabel;
        private TMP_Text rankSubLabel;
        private TMP_Text pointsChipLabel;
        private TMP_Text progressCaptionLabel;
        private RectTransform pipMeterArea;
        private Image scoreBarFill;

        // Support Page Widgets
        private TMP_Text allocationLabel;
        private TMP_Text targetLabel;
        private Image cooldownBarFill;
        private TMP_Text cooldownCaptionLabel;

        // Shared Telemetry Terminal
        private TMP_Text telemetryLabel;
        private string activeHoverTooltip;

        private readonly List<PerkCard> perkCards = new List<PerkCard>();
        private readonly List<SupportCard> supportCards = new List<SupportCard>();

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(SupportManager manager, IProgressionView progressionView, ManualLogSource log)
        {
            support = manager;
            progression = progressionView;
            logger = log;
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
            scoreLabel = null;
            rankSubLabel = null;
            pointsChipLabel = null;
            progressCaptionLabel = null;
            pipMeterArea = null;
            scoreBarFill = null;
            allocationLabel = null;
            targetLabel = null;
            cooldownBarFill = null;
            cooldownCaptionLabel = null;
            telemetryLabel = null;
            activeHoverTooltip = null;
            perkCards.Clear();
            supportCards.Clear();
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
                VirtualMFD mfd = UnityEngine.Object.FindObjectOfType<VirtualMFD>();
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

            var root = new GameObject("BoscaliOperations.Screen", typeof(RectTransform), typeof(Image));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.anchoredPosition = templateRect.anchoredPosition;
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

            float inner = Width - Pad * 2f;
            float y = -Pad;

            // Header Banner (28px Title Bar)
            TMP_Text title = AvKit.Label(content, "BOSCALI OPERATIONS", new Rect(Pad, y, inner, AvTokens.TitleBarHeight),
                                         AvTheme.Accent, AvTokens.FontTitle, FontStyles.Bold, TextAlignmentOptions.Center);
            title.characterSpacing = 0.8f;
            y -= AvTokens.TitleBarHeight + AvTokens.Space1;

            // 18px Chip rail: SYS: LOGISTICS, NET: BOTE-LINK, AUTH: READY
            float chipW = (inner - Gap * 2f) / 3f;
            AvKit.StatusChip(content, "SYS: LOGISTICS", new Rect(Pad, y, chipW, AvTokens.ChipRailHeight),
                             AvTheme.RailReady, AvTheme.TextPrimary, AvTokens.FontMicro);
            AvKit.StatusChip(content, "NET: BOTE-LINK", new Rect(Pad + chipW + Gap, y, chipW, AvTokens.ChipRailHeight),
                             AvTheme.RailInfo, AvTheme.TextPrimary, AvTokens.FontMicro);
            AvKit.StatusChip(content, "AUTH: READY", new Rect(Pad + (chipW + Gap) * 2f, y, chipW, AvTokens.ChipRailHeight),
                             AvTheme.RailReady, AvTheme.TextPrimary, AvTokens.FontMicro);

            y -= AvTokens.ChipRailHeight + AvTokens.Space2;
            AvKit.Rule(content, new Rect(Pad, y, inner, 1f), AvTheme.Hairline);
            y -= AvTokens.Space2;

            ModServices.TryGet(out theater);
            int tabCount = theater != null ? 3 : 2;
            float tabWidth = (inner - Gap * (tabCount - 1)) / tabCount;
            perksTab = AvKit.Tab(content, "PERKS", new Rect(Pad, y, tabWidth, TabHeight), ShowPerks);
            supportTab = AvKit.Tab(content, "SUPPORT", new Rect(Pad + tabWidth + Gap, y, tabWidth, TabHeight), ShowSupport);
            if (theater != null)
            {
                theaterTab = AvKit.Tab(content, "THEATER", new Rect(Pad + (tabWidth + Gap) * 2f, y, tabWidth, TabHeight), ShowTheater);
            }
            y -= TabHeight;
            AvKit.Rule(content, new Rect(Pad, y, inner, 1f), AvTheme.Frame);
            y -= AvTokens.Space2;

            perksPage = CreatePage(content, "PerksPage");
            BuildPerksPage((RectTransform)perksPage.transform, inner, y);

            supportPage = CreatePage(content, "SupportPage");
            BuildSupportPage((RectTransform)supportPage.transform, inner, y);

            if (theater != null)
            {
                theaterPage = CreatePage(content, "TheaterPage");
                theater.Mount((RectTransform)theaterPage.transform, font, inner, y);
            }

            // Pinned Telemetry Terminal (StatusStrip: 56px, 2-line padded)
            float terminalY = -(PanelHeight - Pad - AvTokens.StatusStripHeight);
            telemetryLabel = AvKit.StatusStrip(content, new Rect(Pad, terminalY, inner, AvTokens.StatusStripHeight), AvTheme.RailReady);

            // Chamfer Corner Ticks for panel
            AvKit.CornerTicks(content, new Rect(0f, 0f, Width, PanelHeight), AvTheme.Hairline);

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
            ShowPerks();
            Refresh();
            return result;
        }

        // ---- Perks Page ------------------------------------------------------------------

        private void BuildPerksPage(RectTransform parent, float inner, float startY)
        {
            float y = startY;

            // Status Ribbon: Score, Rank, Tactical Points budget & score-driven progress.
            const float ribbonH = 58f;
            AvKit.Panel(parent, new Rect(Pad, y, inner, ribbonH), AvTheme.SurfaceInert, AvSprites.Card);
            AvKit.Outline(parent, new Rect(Pad, y, inner, ribbonH), AvTheme.Hairline);
            AvKit.CornerTicks(parent, new Rect(Pad, y, inner, ribbonH), AvTheme.Hairline);

            // Left: score readout + rank subtitle
            scoreLabel = AvKit.Label(parent, "SCORE 0",
                                     new Rect(Pad + AvTokens.Space2, y - 2f, 200f, 18f),
                                     AvTheme.Friendly, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            rankSubLabel = AvKit.Label(parent, "RANK --",
                                       new Rect(Pad + AvTokens.Space2, y - 22f, 220f, 14f),
                                       AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            // Right: points-available chip
            pointsChipLabel = AvKit.Label(parent, "0 PTS AVAILABLE",
                                          new Rect(Pad + inner - 176f, y - 2f, 168f, 18f),
                                          AvTheme.Accent, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            var pipGo = new GameObject("PipArea", typeof(RectTransform));
            pipMeterArea = pipGo.GetComponent<RectTransform>();
            pipMeterArea.SetParent(parent, false);
            AvKit.Place(pipMeterArea, new Rect(Pad + inner - 150f, y - 22f, 142f, 10f));

            // Bottom: score-to-next-point progress with caption
            float barX = Pad + AvTokens.Space2;
            float barW = inner - AvTokens.Space2 * 2f;
            progressCaptionLabel = AvKit.Label(parent, "0 / 0 EARNED",
                                               new Rect(barX, y - ribbonH + 4f, 120f, 10f),
                                               AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold,
                                               TextAlignmentOptions.MidlineLeft);
            AvKit.Label(parent, "SCORE TO NEXT POINT",
                        new Rect(barX + 132f, y - ribbonH + 4f, barW - 160f, 10f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            scoreBarFill = AvKit.ProgressBar(parent, new Rect(barX, y - ribbonH + 2f, barW, 6f), 0f, AvTheme.RailReady);

            y -= ribbonH + AvTokens.Space2;

            // Dual Column Layout
            float colWidth = (inner - Gap) * 0.5f;
            float col1X = Pad;
            float col2X = Pad + colWidth + Gap;

            // Column 1 Header: Passives
            AvKit.Label(parent, "PASSIVE FLIGHT & REWARD SYSTEMS", new Rect(col1X, y, colWidth, 14f),
                        AvTheme.Friendly, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            AvKit.Rule(parent, new Rect(col1X, y - 15f, colWidth, 1f), AvTheme.Friendly.WithAlpha(0.4f));

            // Column 2 Header: Authorisations
            AvKit.Label(parent, "TACTICAL SUPPORT AUTHORISATIONS", new Rect(col2X, y, colWidth, 14f),
                        AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            AvKit.Rule(parent, new Rect(col2X, y - 15f, colWidth, 1f), AvTheme.RailInfo.WithAlpha(0.4f));

            y -= 18f;

            PerkView[] perks = progression.GetPerks();
            float col1Y = y;
            float col2Y = y;
            const float cardH = 40f;
            const float pitch = cardH + 4f;

            for (int i = 0; i < perks.Length; i++)
            {
                PerkView perk = perks[i];
                bool isAuth = perk.Group != null && perk.Group.IndexOf("AUTHORIS", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isAuth)
                {
                    AddPerkCard(parent, perk, col1X, col1Y, colWidth, cardH);
                    col1Y -= pitch;
                }
                else
                {
                    AddPerkCard(parent, perk, col2X, col2Y, colWidth, cardH);
                    col2Y -= pitch;
                }
            }
        }

        private static string PerkGlyph(bool isAuth)
        {
            return isAuth ? "◈" : "▣";
        }

        private void AddPerkCard(
            RectTransform parent, PerkView view, float x, float y, float w, float h)
        {
            bool isAuth = view.Group != null && view.Group.IndexOf("AUTHORIS", StringComparison.OrdinalIgnoreCase) >= 0;

            Color groupRail = isAuth ? AvTheme.RailInfo : AvTheme.RailReady;
            var (cardFill, rail) = AvKit.TacticalCard(parent, new Rect(x, y, w, h), AvTheme.RailInert);
            RectTransform rect = cardFill.rectTransform;

            // Category glyph block on the left
            TMP_Text glyph = AvKit.Label(rect, PerkGlyph(isAuth),
                new Rect(AvTokens.Space2, -2f, 20f, 36f),
                groupRail, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            float textX = AvTokens.Space2 + 24f;

            // Title line
            TMP_Text nameLabel = AvKit.Label(rect, view.Name.ToUpperInvariant(),
                new Rect(textX, -2f, w - textX - 74f, 18f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            TMP_Text badge = AvKit.Label(rect, string.Empty,
                new Rect(w - 70f, -2f, 66f, 18f),
                AvTheme.Accent, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            // Subtitle / synopsis
            TMP_Text sub = AvKit.Label(rect, view.Description,
                new Rect(textX, -20f, w - textX - AvTokens.Space3, 16f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            byte id = view.Id;
            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, w, h), () =>
            {
                AvInput.Deselect(cardFill.gameObject);
                progression.RequestUnlock(id);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(cardFill, AvTheme.Surface,
                AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.RowHoverScale, AvTokens.RowHoverAlpha)));

            string tooltip = "[PERK] " + view.Name.ToUpperInvariant() + " · COST: " + view.Cost +
                (view.Cost == 1 ? " POINT" : " POINTS") + "\n" + view.Description +
                "\nUnlocks via mission score points (1 pt per 500 score).";
            button.WithTooltip(tooltip);

            perkCards.Add(new PerkCard
            {
                Id = id,
                Root = cardFill.gameObject,
                Button = button,
                Rail = rail,
                Background = cardFill,
                Name = nameLabel,
                Badge = badge,
                Subtitle = sub
            });
        }

        // ---- Support Page ----------------------------------------------------------------

        private static string ActionGlyph(SupportActionId id)
        {
            switch (id)
            {
                case SupportActionId.Artillery: return "[ARTY]";
                case SupportActionId.Fortify: return "[BASE]";
                case SupportActionId.Recon: return "[RECON]";
                case SupportActionId.Emp: return "[EMP]";
                case SupportActionId.Firebreak: return "[FIRE]";
                case SupportActionId.SmokeMarker: return "[SMOKE]";
                default: return "[OPS]";
            }
        }

        private void BuildSupportPage(RectTransform parent, float inner, float startY)
        {
            float y = startY;

            // Status Ribbon: Allocation Budget & Designated Strike Target.
            const float ribbonH = 64f;
            AvKit.Panel(parent, new Rect(Pad, y, inner, ribbonH), AvTheme.SurfaceInert, AvSprites.Card);
            AvKit.Outline(parent, new Rect(Pad, y, inner, ribbonH), AvTheme.Hairline);
            AvKit.CornerTicks(parent, new Rect(Pad, y, inner, ribbonH), AvTheme.Hairline);

            // Allocation section
            AvKit.Label(parent, "ALLOCATION BUDGET", new Rect(Pad + AvTokens.Space2, y - 2f, 160f, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            allocationLabel = AvKit.Label(parent, "0 ALLOC", new Rect(Pad + AvTokens.Space2, y - 16f, 170f, 18f),
                                          AvTheme.Accent, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            // Target section
            AvKit.Label(parent, "DESIGNATED STRIKE TARGET", new Rect(Pad + 188f, y - 2f, inner - 196f, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            targetLabel = AvKit.Label(parent, "NO TARGET — DESIGNATE GRID ON MAXIMISED MAP",
                                      new Rect(Pad + 188f, y - 16f, inner - 196f, 18f),
                                      AvTheme.Friendly, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            // Bottom: cooldown readiness bar with caption
            float barX = Pad + AvTokens.Space2;
            float barW = inner - AvTokens.Space2 * 2f;
            cooldownCaptionLabel = AvKit.Label(parent, "SUPPORT NET READY",
                                               new Rect(barX, y - ribbonH + 5f, 150f, 10f),
                                               AvTheme.RailReady, AvTokens.FontMicro, FontStyles.Bold,
                                               TextAlignmentOptions.MidlineLeft);
            AvKit.Label(parent, "REQUEST COOLDOWN",
                        new Rect(barX + 158f, y - ribbonH + 5f, barW - 174f, 10f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            cooldownBarFill = AvKit.ProgressBar(parent, new Rect(barX, y - ribbonH + 2f, barW, 6f), 1f, AvTheme.RailReady);

            y -= ribbonH + AvTokens.Space2;

            // Dual Column Grid (3 rows × 2 cols = 6 cards)
            float colWidth = (inner - Gap) * 0.5f;
            const float cardH = 88f;
            const float pitch = cardH + 6f;

            IReadOnlyList<SupportActionDefinition> actions = support.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float bx = Pad + col * (colWidth + Gap);
                float by = y - row * pitch;

                AddSupportCard(parent, actions[i], bx, by, colWidth, cardH);
            }
        }

        private void AddSupportCard(
            RectTransform parent, SupportActionDefinition definition,
            float x, float y, float w, float h)
        {
            var (cardFill, rail) = AvKit.TacticalCard(parent, new Rect(x, y, w, h), AvTheme.RailInert);
            RectTransform rect = cardFill.rectTransform;

            // Title line: action type glyph + name
            string titleText = ActionGlyph(definition.Id) + " " + definition.Name;
            TMP_Text nameLabel = AvKit.Label(rect, titleText,
                new Rect(AvTokens.Space2, -3f, w - 84f, 18f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            // Cost chip: coloured by affordability, right-aligned
            TMP_Text costChip = AvKit.Label(rect, string.Empty,
                new Rect(w - 80f, -4f, 74f, 16f),
                AvTheme.Accent, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            // Required Authorisation Status
            TMP_Text authStatus = AvKit.Label(rect, string.Empty,
                new Rect(AvTokens.Space2, -22f, w - AvTokens.Space3, 14f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            // Description (word wrapping enabled, 2-line flow)
            TMP_Text desc = AvKit.Label(rect, definition.Description,
                new Rect(AvTokens.Space2, -37f, w - AvTokens.Space3, 30f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, wrap: true);

            // Status readout
            TMP_Text stateLabel = AvKit.Label(rect, string.Empty,
                new Rect(AvTokens.Space2, -70f, w - 94f, 18f),
                AvTheme.Friendly, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            SupportActionId id = definition.Id;
            AvButton request = AvKit.Button(rect, "REQUEST",
                new Rect(w - 88f, -69f, 84f, 20f),
                () => { support.Request(id); nextRefresh = 0f; },
                AvTokens.FontMicro, AvButtonStyle.Primary);

            string tooltip = "[SUPPORT] " + definition.Name + " · " +
                support.Cost(definition).ToString("0") + " ALLOC\n" + definition.Description +
                "\nAuthorised by '" + progression.PerkNameFor(definition.Capability) + "' perk.";
            request.WithTooltip(tooltip);

            supportCards.Add(new SupportCard
            {
                Definition = definition,
                Root = cardFill.gameObject,
                RequestButton = request,
                Rail = rail,
                Background = cardFill,
                Name = nameLabel,
                CostChip = costChip,
                AuthStatus = authStatus,
                Description = desc,
                StateLabel = stateLabel,
            });
        }

        private void UpdateTelemetry()
        {
            if (telemetryLabel == null) return;
            string hovered = AvButton.HoveredTooltip;
            if (!string.IsNullOrEmpty(hovered))
            {
                telemetryLabel.text = "> " + hovered;
                telemetryLabel.color = AvTheme.Friendly;
                return;
            }
            if (!string.IsNullOrEmpty(activeHoverTooltip))
            {
                telemetryLabel.text = "> " + activeHoverTooltip;
                telemetryLabel.color = AvTheme.Friendly;
                return;
            }
            string fireTelemetry = support != null ? support.FireTelemetry : string.Empty;
            string baseTelemetry = (progression != null ? progression.Status : "") + " · " +
                                   (support != null ? support.Status : "");
            telemetryLabel.text = "> " + (string.IsNullOrEmpty(fireTelemetry)
                ? baseTelemetry
                : baseTelemetry + " · " + fireTelemetry);
            telemetryLabel.color = AvTheme.Dim;
        }

        // ---- State Refresh ---------------------------------------------------------------

        private void Refresh()
        {
            if (scoreLabel == null) return;

            // 1. Refresh Perks HUD
            int score = progression.Score;
            int rank = progression.Rank;
            int avail = progression.AvailablePoints;
            int earned = progression.EarnedPoints;

            // Debug/BypassRequirements grants every perk free and authorises every action, so
            // the board would otherwise be indistinguishable from a broken one: the point
            // count never moves and every card reads as takeable. Say which mode is on.
            bool bypass = support.BypassRequirements;
            scoreLabel.text = bypass ? "DEBUG BYPASS ACTIVE" : "SCORE " + score.ToString("N0");
            scoreLabel.color = bypass ? AvTheme.Warning : AvTheme.Friendly;
            // The ceiling is configurable, so the readout reads it rather than assuming the
            // shipped default — a server running MaximumPoints=10 used to show "6 OF 6" while
            // the pilot still had points to spend.
            int ceiling = Mathf.Max(1, progression.MaximumPoints);
            rankSubLabel.text = bypass
                ? "PERK COSTS AND AUTHORISATIONS IGNORED"
                : "RANK " + rank + "  ·  " + Math.Min(ceiling, earned) + " OF " + ceiling + " EARNED";
            pointsChipLabel.text = bypass
                ? "FREE"
                : avail + (avail == 1 ? " PT AVAILABLE" : " PTS AVAILABLE");

            // Real pip meter
            if (pipMeterArea != null)
            {
                for (int c = pipMeterArea.childCount - 1; c >= 0; c--)
                    UnityEngine.Object.Destroy(pipMeterArea.GetChild(c).gameObject);
                AvKit.PipMeter(pipMeterArea, new Rect(0f, 0f, 140f, 8f), avail, Math.Min(ceiling, 10), AvTheme.RailReady, AvTheme.Disabled);
            }

            // Score → next point progress bar. Shows fractional progress within the current
            // point so the pilot sees how close the next perk point is without doing the
            // division themselves.
            if (scoreBarFill != null && progressCaptionLabel != null)
            {
                int perPoint = Math.Max(1, progression.ScorePerPoint);
                int intoPoint = score % perPoint;
                scoreBarFill.fillAmount = Mathf.Clamp01(intoPoint / (float)perPoint);
                scoreBarFill.color = bypass ? AvTheme.Warning : AvTheme.RailReady;
                progressCaptionLabel.text = bypass ? "—" : (perPoint - intoPoint) + " SCORE TO NEXT PT";
                progressCaptionLabel.color = bypass ? AvTheme.Warning : AvTheme.TextPrimary;
            }

            // Refresh individual Perk Cards
            PerkView[] perks = progression.GetPerks();
            for (int i = 0; i < perkCards.Count; i++)
            {
                PerkCard card = perkCards[i];
                if (!TryFind(perks, card.Id, out PerkView view)) continue;

                card.Button.SetEnabled(view.Affordable && !view.Unlocked);

                if (view.Unlocked)
                {
                    card.Background.color = AvTheme.SurfaceRaised;
                    card.Rail.color = AvTheme.RailReady;
                    card.Name.color = AvTheme.TextPrimary;
                    card.Badge.text = "ACTIVE ✓";
                    card.Badge.color = AvTheme.RailReady;
                }
                else if (view.Affordable)
                {
                    card.Background.color = AvTheme.Surface;
                    card.Rail.color = AvTheme.RailCaution;
                    card.Name.color = AvTheme.Friendly;
                    card.Badge.text = "UNLOCK " + view.Cost + "P";
                    card.Badge.color = AvTheme.RailCaution;
                }
                else
                {
                    card.Background.color = AvTheme.SurfaceInert;
                    card.Rail.color = AvTheme.RailInert;
                    card.Name.color = AvTheme.Dim;
                    card.Badge.text = view.Cost + "P REQ";
                    card.Badge.color = AvTheme.Dim;
                }
            }

            // 2. Refresh Support HUD
            float allocation = support.LocalAllocation;
            bool wingPresent = !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid));
            allocationLabel.text = allocation.ToString("N0") + " ALLOC" +
                (wingPresent ? "  ·  SHARED WITH WING COMMAND" : "");

            if (support.ArmedAction.HasValue)
            {
                SupportActionDefinition armedDef = null;
                for (int i = 0; i < supportCards.Count; i++)
                {
                    if (supportCards[i].Definition.Id == support.ArmedAction.Value)
                    {
                        armedDef = supportCards[i].Definition;
                        break;
                    }
                }
                string name = armedDef != null ? armedDef.Name : "SUPPORT";
                targetLabel.text = "ARMED: " + name + "  [RIGHT-CLICK MAP TO CALL IN · ESC TO CANCEL]";
                targetLabel.color = AvTheme.RailCaution;
            }
            else
            {
                targetLabel.text = support.DisableCooldowns
                    ? "NO COOLDOWNS · SELECT OPTION, RIGHT-CLICK MAP"
                    : "SELECT SUPPORT OPTION BELOW, THEN RIGHT-CLICK ON MAP";
                targetLabel.color = support.DisableCooldowns ? AvTheme.RailCaution : AvTheme.Friendly;
            }

            // Cooldown readiness bar: fills toward ready as the shared request cooldown drains.
            float netCooldown = support.LocalCooldownRemaining;
            float netTotal = support.LocalCooldownTotal;
            if (cooldownBarFill != null && cooldownCaptionLabel != null)
            {
                if (netCooldown > 0.5f && netTotal > 0f)
                {
                    cooldownBarFill.fillAmount = Mathf.Clamp01(1f - netCooldown / netTotal);
                    cooldownBarFill.color = AvTheme.RailCaution;
                    cooldownCaptionLabel.text = "NET RECHARGING · T-" + Mathf.CeilToInt(netCooldown) + "s";
                    cooldownCaptionLabel.color = AvTheme.RailCaution;
                }
                else
                {
                    cooldownBarFill.fillAmount = 1f;
                    cooldownBarFill.color = support.DisableCooldowns ? AvTheme.RailInfo : AvTheme.RailReady;
                    cooldownCaptionLabel.text = support.DisableCooldowns ? "NO COOLDOWN LIMIT" : "SUPPORT NET READY";
                    cooldownCaptionLabel.color = support.DisableCooldowns ? AvTheme.RailInfo : AvTheme.RailReady;
                }
            }

            // Refresh Support Action Cards
            float cooldown = support.LocalCooldownRemaining;
            for (int i = 0; i < supportCards.Count; i++)
            {
                SupportCard card = supportCards[i];
                float cost = support.Cost(card.Definition);
                card.CostChip.text = cost > 0f ? cost.ToString("N0") + " ALLOC" : "N/A";

                bool isAuth = support.IsAuthorised(card.Definition);
                if (isAuth)
                {
                    card.AuthStatus.text = "AUTH: " + progression.PerkNameFor(card.Definition.Capability).ToUpperInvariant() + " ✓";
                    card.AuthStatus.color = AvTheme.RailReady;
                }
                else
                {
                    card.AuthStatus.text = "LOCKED: REQ '" + progression.PerkNameFor(card.Definition.Capability).ToUpperInvariant() + "'";
                    card.AuthStatus.color = AvTheme.Warning;
                }

                bool isArmed = support.ArmedAction.HasValue && support.ArmedAction.Value == card.Definition.Id;

                if (!card.Definition.Enabled)
                {
                    SetActionState(card, "SERVER DISABLED", "OFF", AvTheme.RailInert, false);
                }
                else if (cost <= 0f)
                {
                    SetActionState(card, "UNAVAILABLE ON MAP", "N/A", AvTheme.RailInert, false);
                }
                else if (!isAuth)
                {
                    SetActionState(card, "AUTHORISATION REQ", "LOCKED", AvTheme.RailCaution, false);
                }
                else if (cooldown > 0.5f)
                {
                    SetActionState(card, "NET COOLING DOWN", "WAIT " + Mathf.CeilToInt(cooldown) + "s", AvTheme.RailCaution, false);
                }
                else if (!support.BypassRequirements && allocation + 0.001f < cost)
                {
                    SetActionState(card, "INSUFFICIENT ALLOC", "NO ALLOC", AvTheme.RailDanger, false);
                }
                else if (isArmed)
                {
                    SetActionState(card, "ARMED · RIGHT-CLICK MAP", "ARMED", AvTheme.RailCaution, true, isArmed: true);
                }
                else
                {
                    SetActionState(card, "CLEARED TO CALL IN", "CALL IN", AvTheme.RailReady, true);
                }

                // Cost chip colour tracks affordability, independent of rail state.
                if (!card.Definition.Enabled || cost <= 0f)
                {
                    card.CostChip.color = AvTheme.RailInert;
                }
                else if (!isAuth)
                {
                    card.CostChip.color = AvTheme.Warning;
                }
                else if (!support.BypassRequirements && allocation + 0.001f < cost)
                {
                    card.CostChip.color = AvTheme.RailDanger;
                }
                else if (isArmed)
                {
                    card.CostChip.color = AvTheme.RailCaution;
                }
                else
                {
                    card.CostChip.color = AvTheme.RailReady;
                }
            }

            if (theaterPage != null && theaterPage.activeSelf)
                theater?.RefreshView();

            UpdateTelemetry();
        }

        private static void SetActionState(
            SupportCard card, string state, string button, Color railColor, bool ready, bool isArmed = false)
        {
            card.Rail.color = railColor;
            card.StateLabel.text = state;
            card.StateLabel.color = isArmed
                ? AvTheme.RailCaution
                : (ready ? AvTheme.RailReady : AvTheme.Warning);
            card.RequestButton.SetText(button);
            card.RequestButton.SetEnabled(ready || isArmed);
            card.RequestButton.SetLatched(isArmed);
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

        // ---- Tabs & Navigation -----------------------------------------------------------

        private void ShowPerks()
        {
            perksPage?.SetActive(true);
            supportPage?.SetActive(false);
            theaterPage?.SetActive(false);
            SetTabHighlight(0);
            activeHoverTooltip = null;
            nextRefresh = 0f;
        }

        private void ShowSupport()
        {
            perksPage?.SetActive(false);
            supportPage?.SetActive(true);
            theaterPage?.SetActive(false);
            SetTabHighlight(1);
            activeHoverTooltip = null;
            nextRefresh = 0f;
        }

        private void ShowTheater()
        {
            perksPage?.SetActive(false);
            supportPage?.SetActive(false);
            theaterPage?.SetActive(true);
            SetTabHighlight(2);
            activeHoverTooltip = null;
            nextRefresh = 0f;
        }

        private void SetTabHighlight(int active)
        {
            perksTab?.SetLatched(active == 0);
            supportTab?.SetLatched(active == 1);
            theaterTab?.SetLatched(active == 2);
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
