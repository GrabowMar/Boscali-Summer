using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Cockpit MFD screen for Boscali Operations: the perk board and the support board.
    /// Redesigned as a high-density, dual-column military avionics console with tactile
    /// controls, visual progression meters, and an integrated real-time telemetry terminal.
    /// </summary>
    internal sealed class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = 430f;
        private const float Pad = 12f;
        private const float Gap = 8f;
        private const float PanelHeight = 492f;
        private const float TabHeight = 28f;
        private const float RibbonHeight = 36f;
        private const float TerminalHeight = 66f;
        private const float RefreshInterval = 0.15f;

        private sealed class PerkCard
        {
            public byte Id;
            public GameObject Root;
            public Button Button;
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
            public Button RequestButton;
            public TMP_Text RequestLabel;
            public Image Rail;
            public Image Background;
            public TMP_Text Name;
            public TMP_Text CostChip;
            public TMP_Text AuthStatus;
            public TMP_Text Description;
            public TMP_Text StateLabel;
            public Color Accent;
        }

        internal sealed class OpsHoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action OnEnter;
            public Action OnExit;

            public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
            public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
            private void OnDisable() => OnExit?.Invoke();
        }

        private SupportManager support;
        private IProgressionView progression;
        private ManualLogSource logger;
        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;

        private GameObject perksPage;
        private GameObject supportPage;
        private Button perksTab;
        private Button supportTab;
        private TMP_Text perksTabLabel;
        private TMP_Text supportTabLabel;
        private Image perksUnderline;
        private Image supportUnderline;

        // Perks Page Widgets
        private TMP_Text scoreLabel;
        private TMP_Text rankSubLabel;
        private TMP_Text pointsChipLabel;
        private TMP_Text pointsPipLabel;

        // Support Page Widgets
        private TMP_Text allocationLabel;
        private TMP_Text targetLabel;

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
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            font = null;
            perksPage = null;
            supportPage = null;
            perksTab = null;
            supportTab = null;
            scoreLabel = null;
            rankSubLabel = null;
            pointsChipLabel = null;
            pointsPipLabel = null;
            allocationLabel = null;
            targetLabel = null;
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

                List<Button> buttons = GameAccess.GetLeftMfdButtons(mfd);
                List<MFDScreen> screens = GameAccess.GetLeftMfdScreens(mfd);
                bool right = false;

                if (!TryClaimSlot(buttons, screens, out int slot))
                {
                    buttons = GameAccess.GetRightMfdButtons(mfd);
                    screens = GameAccess.GetRightMfdScreens(mfd);
                    right = true;
                    if (!TryClaimSlot(buttons, screens, out slot))
                    {
                        failed = true;
                        logger.LogWarning("OPS MFD unavailable: no free bezel slot.");
                        return;
                    }
                }

                MFDScreen template = FindTemplate(screens) ??
                    FindTemplate(GameAccess.GetLeftMfdScreens(mfd)) ??
                    FindTemplate(GameAccess.GetRightMfdScreens(mfd));
                if (template == null) return;

                screen = Build(template, buttons[slot]);
                if (screen == null) { failed = true; return; }

                while (screens.Count <= slot) screens.Add(null);
                screens[slot] = screen;
                mfd.SetupButtons();

                Button bezel = buttons[slot];
                bezel.enabled = true;
                bezel.interactable = true;
                if (bezel.onClick.GetPersistentEventCount() == 0)
                {
                    VirtualMFD owner = mfd;
                    bool onRight = right;
                    bezel.onClick.AddListener(() =>
                    {
                        if (onRight) owner.PressRightButton(bezel);
                        else owner.PressLeftButton(bezel);
                    });
                }

                screen.CloseScreen(Screen.width * (right ? Vector3.right : Vector3.left));
                logger.LogInfo("OPS MFD installed on " + (right ? "right" : "left") +
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
            background.color = AvionicsUiPalette.SurfaceScreen;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            Stretch(content);

            float inner = Width - Pad * 2f;
            float y = -Pad;

            // Header Banner
            Label(content, "BOSCALI OPERATIONS", new Rect(Pad, y, inner, 18f),
                Accent(), AvionicsUiPalette.FontTitle, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= 18f;

            Label(content, "THEATER LOGISTICS & AIR-GROUND STRIKE COMMAND", new Rect(Pad, y, inner, 12f),
                AvionicsUiPalette.TextDim, AvionicsUiPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.Center);
            y -= 14f;

            Rule(content, new Rect(Pad, y, inner, 1f), AvionicsUiPalette.Frame);
            y -= AvionicsUiPalette.Space2;

            // Tab Navigation
            float tabWidth = (inner - Gap) * 0.5f;
            perksTab = MakeTabButton(content, "PERKS & REWARDS", new Rect(Pad, y, tabWidth, TabHeight),
                ShowPerks, out perksTabLabel, out perksUnderline);
            supportTab = MakeTabButton(content, "SUPPORT MISSIONS",
                new Rect(Pad + tabWidth + Gap, y, tabWidth, TabHeight),
                ShowSupport, out supportTabLabel, out supportUnderline);
            y -= TabHeight + AvionicsUiPalette.Space2;

            // Pages
            perksPage = CreatePage(content, "PerksPage");
            BuildPerksPage((RectTransform)perksPage.transform, inner, y);

            supportPage = CreatePage(content, "SupportPage");
            BuildSupportPage((RectTransform)supportPage.transform, inner, y);

            // Pinned Telemetry Terminal
            float terminalY = -PanelHeight + TerminalHeight + Pad;
            BuildTelemetryTerminal(content, inner, terminalY);

            // Outer Avionics Border
            Outline(content, new Rect(0f, 0f, Width, PanelHeight), AvionicsUiPalette.Frame);

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

            // Status Ribbon: Score, Rank & Tactical Points Meter
            FramedPanel(parent, new Rect(Pad, y, inner, RibbonHeight),
                AvionicsUiPalette.Frame, AvionicsUiPalette.SurfaceRibbon);

            scoreLabel = Label(parent, "SCORE 0", new Rect(Pad + AvionicsUiPalette.Space2, y - 2f,
                200f, 16f), Friendly(), AvionicsUiPalette.FontSmall, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);

            rankSubLabel = Label(parent, "RANK --",
                new Rect(Pad + AvionicsUiPalette.Space2, y - 18f, 200f, 14f),
                AvionicsUiPalette.TextDim, AvionicsUiPalette.FontNano, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);

            pointsChipLabel = Label(parent, "0 PTS AVAILABLE",
                new Rect(Pad + inner - 180f, y - 2f, 172f, 16f),
                Accent(), AvionicsUiPalette.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            pointsPipLabel = Label(parent, string.Empty,
                new Rect(Pad + inner - 180f, y - 18f, 172f, 14f),
                Friendly(), AvionicsUiPalette.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            y -= RibbonHeight + AvionicsUiPalette.Space2;

            // Dual Column Layout
            float colWidth = (inner - Gap) * 0.5f;
            float col1X = Pad;
            float col2X = Pad + colWidth + Gap;

            // Column 1 Header: Passives
            Label(parent, "PASSIVE FLIGHT & REWARD SYSTEMS", new Rect(col1X, y, colWidth, 14f),
                Friendly(), AvionicsUiPalette.FontNano, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Rule(parent, new Rect(col1X, y - 15f, colWidth, 1f), AvionicsUiPalette.WithAlpha(Friendly(), 0.4f));

            // Column 2 Header: Authorisations
            Label(parent, "TACTICAL SUPPORT AUTHORISATIONS", new Rect(col2X, y, colWidth, 14f),
                AvionicsUiPalette.RailCyan, AvionicsUiPalette.FontNano, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Rule(parent, new Rect(col2X, y - 15f, colWidth, 1f), AvionicsUiPalette.WithAlpha(AvionicsUiPalette.RailCyan, 0.4f));

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

        private void AddPerkCard(
            RectTransform parent, PerkView view, float x, float y, float w, float h)
        {
            var root = new GameObject("PerkCard_" + view.Id, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, new Rect(x, y, w, h));

            Image bg = root.GetComponent<Image>();
            bg.color = AvionicsUiPalette.SurfaceCard;
            Outline(rect, new Rect(0f, 0f, w, h), AvionicsUiPalette.Frame);
            Image rail = Rule(rect, new Rect(0f, 0f, 3f, h), AvionicsUiPalette.RailInert);

            // Title line
            TMP_Text nameLabel = Label(rect, view.Name.ToUpperInvariant(),
                new Rect(AvionicsUiPalette.Space2, -2f, w - 74f, 18f),
                AvionicsUiPalette.TextPrimary, AvionicsUiPalette.FontSmall, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);

            TMP_Text badge = Label(rect, string.Empty,
                new Rect(w - 70f, -2f, 66f, 18f),
                Accent(), AvionicsUiPalette.FontNano, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            // Subtitle / synopsis
            TMP_Text sub = Label(rect, view.Description,
                new Rect(AvionicsUiPalette.Space2, -20f, w - AvionicsUiPalette.Space3, 16f),
                AvionicsUiPalette.TextDim, AvionicsUiPalette.FontNano, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);

            byte id = view.Id;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = bg;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                Deselect(root);
                progression.RequestUnlock(id);
                nextRefresh = 0f;
            });

            string tooltip = "[PERK] " + view.Name.ToUpperInvariant() + " · COST: " + view.Cost +
                (view.Cost == 1 ? " POINT" : " POINTS") + "\n" + view.Description +
                "\nUnlocks via mission score points (1 pt per 500 score).";

            var hover = root.AddComponent<OpsHoverTrigger>();
            hover.OnEnter = () =>
            {
                bg.color = AvionicsUiPalette.SurfaceCardHover;
                SetHoverTooltip(tooltip);
            };
            hover.OnExit = () =>
            {
                bg.color = AvionicsUiPalette.SurfaceCard;
                ClearHoverTooltip();
            };

            perkCards.Add(new PerkCard
            {
                Id = id,
                Root = root,
                Button = button,
                Rail = rail,
                Background = bg,
                Name = nameLabel,
                Badge = badge,
                Subtitle = sub
            });
        }

        // ---- Support Page ----------------------------------------------------------------

        private void BuildSupportPage(RectTransform parent, float inner, float startY)
        {
            float y = startY;

            // Status Ribbon: Allocation Budget & Map Target
            FramedPanel(parent, new Rect(Pad, y, inner, RibbonHeight),
                AvionicsUiPalette.Frame, AvionicsUiPalette.SurfaceRibbon);

            // Allocation section
            Label(parent, "ALLOCATION BUDGET", new Rect(Pad + AvionicsUiPalette.Space2, y - 2f, 150f, 14f),
                AvionicsUiPalette.TextDim, AvionicsUiPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            allocationLabel = Label(parent, "0 ALLOC", new Rect(Pad + AvionicsUiPalette.Space2, y - 16f, 150f, 18f),
                Accent(), AvionicsUiPalette.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            // Target section
            Label(parent, "DESIGNATED STRIKE TARGET", new Rect(Pad + 170f, y - 2f, inner - 178f, 14f),
                AvionicsUiPalette.TextDim, AvionicsUiPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            targetLabel = Label(parent, "NO TARGET — DESIGNATE GRID ON MAXIMISED MAP",
                new Rect(Pad + 170f, y - 16f, inner - 178f, 18f),
                AvionicsUiPalette.TextWarning, AvionicsUiPalette.FontNano, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);

            y -= RibbonHeight + AvionicsUiPalette.Space2;

            // Dual Column Grid (3 rows × 2 cols = 6 cards)
            float colWidth = (inner - Gap) * 0.5f;
            const float cardH = 76f;
            const float pitch = cardH + 6f;

            IReadOnlyList<SupportActionDefinition> actions = support.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float bx = Pad + col * (colWidth + Gap);
                float by = y - row * pitch;

                AddSupportCard(parent, actions[i], AccentFor(actions[i].Id), bx, by, colWidth, cardH);
            }
        }

        private void AddSupportCard(
            RectTransform parent, SupportActionDefinition definition, Color accent,
            float x, float y, float w, float h)
        {
            var root = new GameObject("SupportCard_" + definition.Id, typeof(RectTransform), typeof(Image));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, new Rect(x, y, w, h));

            Image bg = root.GetComponent<Image>();
            bg.color = AvionicsUiPalette.SurfaceCard;
            Outline(rect, new Rect(0f, 0f, w, h), AvionicsUiPalette.Frame);
            Image rail = Rule(rect, new Rect(0f, 0f, 4f, h), accent);

            // Title & Cost
            TMP_Text nameLabel = Label(rect, definition.Name,
                new Rect(AvionicsUiPalette.Space2, -3f, w - 80f, 18f),
                AvionicsUiPalette.TextPrimary, AvionicsUiPalette.FontSmall, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);

            TMP_Text costChip = Label(rect, string.Empty,
                new Rect(w - 76f, -3f, 70f, 18f),
                accent, AvionicsUiPalette.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            // Required Authorisation Status
            TMP_Text authStatus = Label(rect, string.Empty,
                new Rect(AvionicsUiPalette.Space2, -20f, w - AvionicsUiPalette.Space3, 14f),
                AvionicsUiPalette.TextDim, AvionicsUiPalette.FontNano, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);

            // Description
            TMP_Text desc = Label(rect, definition.Description,
                new Rect(AvionicsUiPalette.Space2, -34f, w - AvionicsUiPalette.Space3, 16f),
                AvionicsUiPalette.TextDim, AvionicsUiPalette.FontNano, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);

            // Status readout & Request Button
            TMP_Text stateLabel = Label(rect, string.Empty,
                new Rect(AvionicsUiPalette.Space2, -52f, w - 90f, 20f),
                Friendly(), AvionicsUiPalette.FontNano, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            SupportActionId id = definition.Id;
            Button request = MakeActionButton(rect, "REQUEST",
                new Rect(w - 82f, -52f, 78f, 20f),
                accent, () => { support.Request(id); nextRefresh = 0f; }, out TMP_Text requestLabel);

            var hover = root.AddComponent<OpsHoverTrigger>();
            hover.OnEnter = () =>
            {
                bg.color = AvionicsUiPalette.SurfaceCardHover;
                string tooltip = "[SUPPORT] " + definition.Name + " · " +
                    support.Cost(definition).ToString("0") + " ALLOC\n" + definition.Description +
                    "\nAuthorised by '" + progression.PerkNameFor(definition.Capability) + "' perk.";
                SetHoverTooltip(tooltip);
            };
            hover.OnExit = () =>
            {
                bg.color = AvionicsUiPalette.SurfaceCard;
                ClearHoverTooltip();
            };

            supportCards.Add(new SupportCard
            {
                Definition = definition,
                Root = root,
                RequestButton = request,
                RequestLabel = requestLabel,
                Rail = rail,
                Background = bg,
                Name = nameLabel,
                CostChip = costChip,
                AuthStatus = authStatus,
                Description = desc,
                StateLabel = stateLabel,
                Accent = accent
            });
        }

        // ---- Tactical Telemetry Terminal -------------------------------------------------

        private void BuildTelemetryTerminal(RectTransform parent, float inner, float y)
        {
            FramedPanel(parent, new Rect(Pad, y, inner, TerminalHeight),
                AvionicsUiPalette.Frame, AvionicsUiPalette.SurfaceRibbon);

            // Terminal Header
            Label(parent, "▶ TACTICAL TELEMETRY & OPERATIONS INTEL",
                new Rect(Pad + AvionicsUiPalette.Space2, y - 2f, inner - AvionicsUiPalette.Space4, 14f),
                Accent(), AvionicsUiPalette.FontNano, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            Rule(parent, new Rect(Pad + AvionicsUiPalette.Space2, y - 16f, inner - AvionicsUiPalette.Space4, 1f),
                AvionicsUiPalette.WithAlpha(AvionicsUiPalette.Frame, 0.6f));

            telemetryLabel = Label(parent, string.Empty,
                new Rect(Pad + AvionicsUiPalette.Space2, y - 18f, inner - AvionicsUiPalette.Space4, TerminalHeight - 20f),
                Friendly(), AvionicsUiPalette.FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            telemetryLabel.enableWordWrapping = true;
            telemetryLabel.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void SetHoverTooltip(string tooltip)
        {
            activeHoverTooltip = tooltip;
            UpdateTelemetry();
        }

        private void ClearHoverTooltip()
        {
            activeHoverTooltip = null;
            UpdateTelemetry();
        }

        private void UpdateTelemetry()
        {
            if (telemetryLabel == null) return;
            if (!string.IsNullOrEmpty(activeHoverTooltip))
            {
                telemetryLabel.text = activeHoverTooltip;
                telemetryLabel.color = Friendly();
                return;
            }
            telemetryLabel.text = (progression != null ? progression.Status : "") + "\n" +
                                 (support != null ? support.Status : "");
            telemetryLabel.color = AvionicsUiPalette.TextDim;
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
            scoreLabel.color = bypass ? AvionicsUiPalette.TextWarning : Friendly();
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

            // Tactical pips: [■] available unspent, [▣] spent/unlocked, [□] unearned. Capped so
            // a generous ceiling cannot overrun the ribbon.
            string pips = string.Empty;
            for (int p = 0; p < Math.Min(ceiling, 12); p++)
            {
                if (p < avail) pips += "■ ";
                else if (p < earned) pips += "▣ ";
                else pips += "□ ";
            }
            pointsPipLabel.text = pips.TrimEnd();

            // Refresh individual Perk Cards
            PerkView[] perks = progression.GetPerks();
            for (int i = 0; i < perkCards.Count; i++)
            {
                PerkCard card = perkCards[i];
                if (!TryFind(perks, card.Id, out PerkView view)) continue;

                card.Button.interactable = view.Affordable && !view.Unlocked;

                if (view.Unlocked)
                {
                    card.Background.color = AvionicsUiPalette.SurfaceActive;
                    card.Rail.color = AvionicsUiPalette.RailEmerald;
                    card.Name.color = AvionicsUiPalette.TextPrimary;
                    card.Badge.text = "ACTIVE ✓";
                    card.Badge.color = AvionicsUiPalette.RailEmerald;
                }
                else if (view.Affordable)
                {
                    card.Background.color = AvionicsUiPalette.SurfaceCard;
                    card.Rail.color = AvionicsUiPalette.RailAmber;
                    card.Name.color = Friendly();
                    card.Badge.text = "UNLOCK " + view.Cost + "P";
                    card.Badge.color = AvionicsUiPalette.RailAmber;
                }
                else
                {
                    card.Background.color = AvionicsUiPalette.SurfaceInert;
                    card.Rail.color = AvionicsUiPalette.RailInert;
                    card.Name.color = AvionicsUiPalette.TextDim;
                    card.Badge.text = view.Cost + "P REQ";
                    card.Badge.color = AvionicsUiPalette.TextDim;
                }
            }

            // 2. Refresh Support HUD
            float allocation = support.LocalAllocation;
            allocationLabel.text = allocation.ToString("N0") + " ALLOC";

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
                targetLabel.color = AvionicsUiPalette.RailAmber;
            }
            else
            {
                targetLabel.text = support.DisableCooldowns
                    ? "NO COOLDOWNS · SELECT OPTION, RIGHT-CLICK MAP"
                    : "SELECT SUPPORT OPTION BELOW, THEN RIGHT-CLICK ON MAP";
                targetLabel.color = support.DisableCooldowns ? AvionicsUiPalette.RailAmber : Friendly();
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
                    card.AuthStatus.color = AvionicsUiPalette.RailEmerald;
                }
                else
                {
                    card.AuthStatus.text = "LOCKED: REQ '" + progression.PerkNameFor(card.Definition.Capability).ToUpperInvariant() + "'";
                    card.AuthStatus.color = AvionicsUiPalette.TextWarning;
                }

                bool isArmed = support.ArmedAction.HasValue && support.ArmedAction.Value == card.Definition.Id;

                if (!card.Definition.Enabled)
                {
                    SetActionState(card, "SERVER DISABLED", "OFF", 0.25f, false);
                }
                else if (cost <= 0f)
                {
                    SetActionState(card, "UNAVAILABLE ON MAP", "N/A", 0.25f, false);
                }
                else if (!isAuth)
                {
                    SetActionState(card, "AUTHORISATION REQ", "LOCKED", 0.35f, false);
                }
                else if (cooldown > 0.5f)
                {
                    SetActionState(card, "COOLING DOWN", "WAIT " + Mathf.CeilToInt(cooldown) + "s", 0.6f, false);
                }
                else if (!support.BypassRequirements && allocation + 0.001f < cost)
                {
                    SetActionState(card, "INSUFFICIENT ALLOC", "NO ALLOC", 0.6f, false);
                }
                else if (isArmed)
                {
                    SetActionState(card, "ARMED · RIGHT-CLICK MAP", "ARMED", 1f, true, isArmed: true);
                }
                else
                {
                    SetActionState(card, "CLEARED TO CALL IN", "CALL IN", 1f, true);
                }
            }

            UpdateTelemetry();
        }

        private static void SetActionState(
            SupportCard card, string state, string button, float railAlpha, bool ready, bool isArmed = false)
        {
            Color railColor = isArmed ? AvionicsUiPalette.RailAmber : card.Accent;
            card.Rail.color = AvionicsUiPalette.WithAlpha(railColor, railAlpha);
            card.StateLabel.text = state;
            card.StateLabel.color = isArmed
                ? AvionicsUiPalette.RailAmber
                : (ready ? AvionicsUiPalette.RailEmerald : AvionicsUiPalette.TextWarning);
            card.RequestLabel.text = button;
            card.RequestLabel.color = isArmed
                ? AvionicsUiPalette.RailAmber
                : (ready ? AvionicsUiPalette.TextPrimary : AvionicsUiPalette.TextDim);
            card.CostChip.color = ready ? card.Accent : AvionicsUiPalette.TextDim;
            card.RequestButton.interactable = ready;
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

        // ---- Palette Helpers -------------------------------------------------------------

        private static Color AccentFor(SupportActionId id)
        {
            switch (id)
            {
                case SupportActionId.Artillery: return AvionicsUiPalette.RailAmber;
                case SupportActionId.Fortify: return AvionicsUiPalette.RailEmerald;
                default: return AvionicsUiPalette.RailCyan;
            }
        }

        // ---- Tabs & Navigation -----------------------------------------------------------

        private void ShowPerks()
        {
            perksPage?.SetActive(true);
            supportPage?.SetActive(false);
            SetTabHighlight(true);
            ClearHoverTooltip();
            nextRefresh = 0f;
        }

        private void ShowSupport()
        {
            perksPage?.SetActive(false);
            supportPage?.SetActive(true);
            SetTabHighlight(false);
            ClearHoverTooltip();
            nextRefresh = 0f;
        }

        private void SetTabHighlight(bool perksActive)
        {
            if (perksTab != null)
            {
                perksTab.colors = TabColors(perksActive);
                perksTabLabel.color = perksActive ? AvionicsUiPalette.TextPrimary : Accent();
                if (perksUnderline != null) perksUnderline.color = perksActive ? Accent() : Color.clear;
            }
            if (supportTab != null)
            {
                supportTab.colors = TabColors(!perksActive);
                supportTabLabel.color = perksActive ? Accent() : AvionicsUiPalette.TextPrimary;
                if (supportUnderline != null) supportUnderline.color = perksActive ? Color.clear : Accent();
            }
        }

        private static ColorBlock TabColors(bool active)
        {
            Color accent = Accent();
            return new ColorBlock
            {
                normalColor = active
                    ? AvionicsUiPalette.WithAlpha(accent, 0.30f)
                    : AvionicsUiPalette.SurfaceCard,
                highlightedColor = AvionicsUiPalette.WithAlpha(accent, 0.45f),
                pressedColor = AvionicsUiPalette.WithAlpha(accent, 0.65f),
                selectedColor = active
                    ? AvionicsUiPalette.WithAlpha(accent, 0.30f)
                    : AvionicsUiPalette.SurfaceCard,
                disabledColor = new Color(0.03f, 0.05f, 0.06f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.06f
            };
        }

        private Button MakeTabButton(
            RectTransform parent, string text, Rect area, Action action,
            out TMP_Text label, out Image underline)
        {
            var root = new GameObject(text + "Tab", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, area);

            Image image = root.GetComponent<Image>();
            image.color = AvionicsUiPalette.SurfaceCard;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                Deselect(root);
                action?.Invoke();
            });

            Outline(rect, new Rect(0f, 0f, area.width, area.height),
                AvionicsUiPalette.WithAlpha(Accent(), 0.5f));
            underline = Rule(rect, new Rect(0f, -(area.height - 3f), area.width, 3f), Color.clear);
            label = Label(rect, text, new Rect(0f, 0f, area.width, area.height),
                Accent(), AvionicsUiPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }

        private Button MakeActionButton(
            RectTransform parent, string text, Rect area, Color accent, Action action, out TMP_Text label)
        {
            var root = new GameObject("ActionBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, area);

            Image image = root.GetComponent<Image>();
            image.color = AvionicsUiPalette.WithAlpha(accent, 0.25f);

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = AvionicsUiPalette.WithAlpha(accent, 0.25f),
                highlightedColor = AvionicsUiPalette.WithAlpha(accent, 0.45f),
                pressedColor = AvionicsUiPalette.WithAlpha(accent, 0.65f),
                selectedColor = AvionicsUiPalette.WithAlpha(accent, 0.25f),
                disabledColor = new Color(0.04f, 0.06f, 0.07f, 0.6f),
                colorMultiplier = 1f,
                fadeDuration = 0.06f
            };
            button.onClick.AddListener(() =>
            {
                Deselect(root);
                action?.Invoke();
            });

            Outline(rect, new Rect(0f, 0f, area.width, area.height),
                AvionicsUiPalette.WithAlpha(accent, 0.8f));
            label = Label(rect, text, new Rect(0f, 0f, area.width, area.height),
                AvionicsUiPalette.TextPrimary, AvionicsUiPalette.FontNano, FontStyles.Bold,
                TextAlignmentOptions.Center);
            return button;
        }

        private static void Deselect(GameObject root)
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == root)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private static bool TryClaimSlot(List<Button> buttons, List<MFDScreen> screens, out int slot)
        {
            slot = -1;
            if (buttons == null || screens == null) return false;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] != null && (i >= screens.Count || screens[i] == null))
                {
                    slot = i;
                    return true;
                }
            }
            return false;
        }

        private static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            if (screens == null) return null;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i] != null && screens[i].transform.parent != null) return screens[i];
            return null;
        }

        private static GameObject CreatePage(RectTransform parent, string name)
        {
            var page = new GameObject(name, typeof(RectTransform));
            RectTransform rect = page.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);
            return page;
        }

        private TMP_Text Label(
            RectTransform parent, string text, Rect area, Color color, float size,
            FontStyles style, TextAlignmentOptions alignment)
        {
            var root = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, area);

            TextMeshProUGUI label = root.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = color;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            if (font != null) label.font = font;
            return label;
        }

        private static void Place(RectTransform target, Rect area)
        {
            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = new Vector2(area.x, area.y);
            target.sizeDelta = new Vector2(area.width, area.height);
            target.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform target)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            target.localScale = Vector3.one;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        private static Image Panel(RectTransform parent, Rect area, Color color)
        {
            var root = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, area);
            Image image = root.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void FramedPanel(RectTransform parent, Rect area, Color frameColor, Color fillColor)
        {
            Panel(parent, area, fillColor);
            Outline(parent, area, frameColor);
        }

        private static void Outline(RectTransform parent, Rect area, Color color)
        {
            const float t = 1f;
            Rule(parent, new Rect(area.x, area.y, area.width, t), color);
            Rule(parent, new Rect(area.x, area.y - area.height + t, area.width, t), color);
            Rule(parent, new Rect(area.x, area.y, t, area.height), color);
            Rule(parent, new Rect(area.x + area.width - t, area.y, t, area.height), color);
        }

        private static Image Rule(RectTransform parent, Rect area, Color color) => Panel(parent, area, color);

        private static Color Accent()
        {
            try { return ThemeManager.Active.ColorTheme.AllClear; }
            catch { return new Color(0.30f, 1f, 0.35f); }
        }

        private static Color Friendly()
        {
            try { return ThemeManager.Active.ColorTheme.MapIconFriendly; }
            catch { return new Color(0.45f, 0.95f, 0.55f); }
        }
    }
}
