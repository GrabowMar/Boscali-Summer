using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal sealed class ComMfdPanel : MonoBehaviour, ISceneService, ITheaterPage
    {
        private const float Width = 430f;
        private const float Pad = 12f;
        private const float Gap = 8f;
        private const float PanelHeight = 492f;
        private const float TabHeight = 28f;
        private const float RefreshInterval = 0.25f;

        private CommandSettings settings;
        private CommandManager command;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private TMP_FontAsset font;

        private bool mounted;

        private GameObject theaterPage;
        private GameObject doctrinePage;
        private GameObject mapModesPage;

        private Button theaterTab;
        private Button doctrineTab;
        private Button mapModesTab;

        private TMP_Text theaterTabLabel;
        private TMP_Text doctrineTabLabel;
        private TMP_Text mapModesTabLabel;

        private Image theaterUnderline;
        private Image doctrineUnderline;
        private Image mapModesUnderline;

        // Theater SA widgets
        private TMP_Text airRatioLabel;
        private TMP_Text defconLabel;
        private TMP_Text airbaseCountLabel;
        private TMP_Text sortiesLabel;

        // Doctrine widgets
        private readonly List<Button> doctrineButtons = new List<Button>();
        private readonly List<TMP_Text> doctrineLabels = new List<TMP_Text>();
        private TMP_Text activeDoctrineDesc;
        private TMP_Text priorityTargetsLabel;

        // Map Modes widgets
        private TMP_Text frontlinesToggleLabel;
        private TMP_Text radarToggleLabel;
        private TMP_Text reconToggleLabel;
        private TMP_Text ordersToggleLabel;

        // Shared Telemetry Terminal
        private TMP_Text telemetryLabel;

        private float nextRefresh;
        private bool failed;

        public void Configure(
            CommandSettings config, CommandManager manager, ComMapOverlay mapOverlay, ManualLogSource log)
        {
            settings = config;
            command = manager;
            overlay = mapOverlay;
            logger = log;
        }

        public void ResetForScene()
        {
            Unmount();
            theaterPage = null;
            doctrinePage = null;
            mapModesPage = null;
            doctrineButtons.Clear();
            doctrineLabels.Clear();
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        public bool Mount(RectTransform host, TMP_FontAsset panelFont, float width, float height)
        {
            if (host == null || command == null) return false;
            Unmount();
            font = panelFont;
            BuildInto(host, width, height);
            mounted = doctrinePage != null;
            if (mounted) Refresh();
            return mounted;
        }

        public void Unmount()
        {
            mounted = false;
            theaterPage = null;
            doctrinePage = null;
            mapModesPage = null;
            doctrineButtons.Clear();
            doctrineLabels.Clear();
        }

        public void RefreshView() => Refresh();

        private void Update()
        {
            if (!mounted || failed || command == null || !settings.Enabled.Value) return;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
        }

        private void BuildInto(RectTransform host, float inner, float startY)
        {
            float y = startY;
            Label(host, "FRIENDLY MISSION AI  ·  NEVER TASKS YOUR WING", new Rect(Pad, y, inner, 14f),
                ComPalette.TextDim, ComPalette.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 18f;

            float tabWidth = (inner - Gap * 2f) / 3f;
            theaterTab = MakeTabButton(host, "SA", new Rect(Pad, y, tabWidth, TabHeight),
                ShowTheater, out theaterTabLabel, out theaterUnderline);
            doctrineTab = MakeTabButton(host, "DOCTRINE", new Rect(Pad + tabWidth + Gap, y, tabWidth, TabHeight),
                ShowDoctrine, out doctrineTabLabel, out doctrineUnderline);
            mapModesTab = MakeTabButton(host, "MAP", new Rect(Pad + (tabWidth + Gap) * 2f, y, tabWidth, TabHeight),
                ShowMapModes, out mapModesTabLabel, out mapModesUnderline);
            y -= TabHeight + 8f;

            theaterPage = CreatePage(host, "TheaterPage");
            BuildTheaterPage((RectTransform)theaterPage.transform, inner, y);

            doctrinePage = CreatePage(host, "DoctrinePage");
            BuildDoctrinePage((RectTransform)doctrinePage.transform, inner, y);

            mapModesPage = CreatePage(host, "MapModesPage");
            BuildMapModesPage((RectTransform)mapModesPage.transform, inner, y);

            ShowTheater();
        }

        private void BuildTheaterPage(RectTransform parent, float inner, float startY)
        {
            float y = startY;
            TacticalCard(parent, new Rect(Pad, y, inner, 80f), ComPalette.HudEmerald);

            Label(parent, "FORCE RATIO & AIR DOMINANCE", new Rect(Pad + 12f, y - 4f, inner - 20f, 14f),
                ComPalette.TextDim, ComPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            airRatioLabel = Label(parent, "AIR RATIO: 50% FRIENDLY", new Rect(Pad + 12f, y - 20f, inner - 20f, 20f),
                ComPalette.HudEmerald, ComPalette.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            defconLabel = Label(parent, "DEFCON 3: CONTESTED THEATER", new Rect(Pad + 12f, y - 42f, inner - 20f, 16f),
                ComPalette.ThreatAmber, ComPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            y -= 90f;

            TacticalCard(parent, new Rect(Pad, y, inner, 120f), ComPalette.InfoCyan);
            Label(parent, "THEATER INFRASTRUCTURE & ACTIVE SORTIES", new Rect(Pad + 12f, y - 4f, inner - 20f, 14f),
                ComPalette.TextDim, ComPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            airbaseCountLabel = Label(parent, "AIRBASES: -- FRIENDLY / -- HOSTILE", new Rect(Pad + 12f, y - 24f, inner - 20f, 18f),
                ComPalette.TextPrimary, ComPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            sortiesLabel = Label(parent, "ACTIVE SORTIES: -- CAP | -- STRIKE | -- CAS | -- RTB",
                new Rect(Pad + 12f, y - 48f, inner - 20f, 18f),
                ComPalette.InfoCyan, ComPalette.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        }

        private void BuildDoctrinePage(RectTransform parent, float inner, float startY)
        {
            float y = startY;

            Label(parent, "FRIENDLY MISSION-AI BIAS  ·  DOES NOT OVERRIDE WING ORDERS", new Rect(Pad, y, inner, 14f),
                ComPalette.TextDim, ComPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 16f;

            CommandDoctrine[] doctrines = (CommandDoctrine[])Enum.GetValues(typeof(CommandDoctrine));
            float btnW = (inner - 4f * (doctrines.Length - 1)) / doctrines.Length;

            for (int i = 0; i < doctrines.Length; i++)
            {
                CommandDoctrine doc = doctrines[i];
                float bx = Pad + i * (btnW + 4f);
                TMP_Text btnLbl;
                Button btn = MakeActionButton(parent, doc.ToString().Substring(0, Math.Min(5, doc.ToString().Length)).ToUpperInvariant(),
                    new Rect(bx, y, btnW, 28f), ComPalette.HudEmerald, () =>
                    {
                        command?.TrySetDoctrine(doc);
                        Refresh();
                    }, out btnLbl);
                doctrineButtons.Add(btn);
                doctrineLabels.Add(btnLbl);
            }
            y -= 34f;

            activeDoctrineDesc = Label(parent, "Balanced: Standard autonomous AI targeting.",
                new Rect(Pad, y, inner, 40f), ComPalette.TextPrimary, ComPalette.FontMicro,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);
            y -= 48f;

            Rule(parent, new Rect(Pad, y, inner, 1f), ComPalette.Frame);
            y -= 8f;

            Label(parent, "PRIORITY TARGET DESIGNATIONS (REQ RANK 1 SGT)", new Rect(Pad, y, inner, 14f),
                ComPalette.TextDim, ComPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 16f;

            priorityTargetsLabel = Label(parent, "0 / 1 TARGETS DESIGNATED (CLICK TRACKED HOSTILE TO MARK)",
                new Rect(Pad, y, inner, 20f), ComPalette.ThreatAmber, ComPalette.FontMicro,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        }

        private void BuildMapModesPage(RectTransform parent, float inner, float startY)
        {
            float y = startY;

            Label(parent, "TACTICAL MAP OVERLAYS & MODES", new Rect(Pad, y, inner, 14f),
                ComPalette.TextDim, ComPalette.FontNano, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 18f;

            float rowH = 34f;

            // 1. Frontlines
            MakeActionButton(parent, "TOGGLE FRONTLINES (AREA OF CONTROL)", new Rect(Pad, y, inner - 90f, rowH),
                ComPalette.HudEmerald, () =>
                {
                    if (overlay != null) overlay.ShowFrontlines = !overlay.ShowFrontlines;
                    Refresh();
                }, out _);
            frontlinesToggleLabel = Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                ComPalette.HudEmerald, ComPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 6f;

            // 2. Radar
            MakeActionButton(parent, "TOGGLE RADAR / SAM THREAT ENVELOPES", new Rect(Pad, y, inner - 90f, rowH),
                ComPalette.InfoCyan, () =>
                {
                    if (overlay != null) overlay.ShowRadar = !overlay.ShowRadar;
                    Refresh();
                }, out _);
            radarToggleLabel = Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                ComPalette.InfoCyan, ComPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 6f;

            // 3. AI Orders
            MakeActionButton(parent, "TOGGLE AI ORDER VECTORS (FLIGHT PATHS)", new Rect(Pad, y, inner - 90f, rowH),
                ComPalette.ThreatAmber, () =>
                {
                    if (overlay != null) overlay.ShowAiOrders = !overlay.ShowAiOrders;
                    Refresh();
                }, out _);
            ordersToggleLabel = Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                ComPalette.ThreatAmber, ComPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 6f;

            // 4. Recon
            MakeActionButton(parent, "TOGGLE VISIBILITY & RECON SURVEILLANCE", new Rect(Pad, y, inner - 90f, rowH),
                ComPalette.TextPrimary, () =>
                {
                    if (overlay != null) overlay.ShowRecon = !overlay.ShowRecon;
                    Refresh();
                }, out _);
            reconToggleLabel = Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                ComPalette.TextPrimary, ComPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        private void BuildTelemetryRibbon(RectTransform parent, float inner, float y)
        {
            TacticalCard(parent, new Rect(Pad, y, inner, 38f), ComPalette.HudEmerald);
            telemetryLabel = Label(parent, "> TACTICAL LINK ESTABLISHED // OBSERVING THEATER",
                new Rect(Pad + 12f, y - 2f, inner - 20f, 34f), ComPalette.HudEmerald,
                ComPalette.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        }

        private void ShowTheater()
        {
            theaterPage?.SetActive(true);
            doctrinePage?.SetActive(false);
            mapModesPage?.SetActive(false);
            SetTabState(theaterTabLabel, theaterUnderline, true);
            SetTabState(doctrineTabLabel, doctrineUnderline, false);
            SetTabState(mapModesTabLabel, mapModesUnderline, false);
        }

        private void ShowDoctrine()
        {
            theaterPage?.SetActive(false);
            doctrinePage?.SetActive(true);
            mapModesPage?.SetActive(false);
            SetTabState(theaterTabLabel, theaterUnderline, false);
            SetTabState(doctrineTabLabel, doctrineUnderline, true);
            SetTabState(mapModesTabLabel, mapModesUnderline, false);
        }

        private void ShowMapModes()
        {
            theaterPage?.SetActive(false);
            doctrinePage?.SetActive(false);
            mapModesPage?.SetActive(true);
            SetTabState(theaterTabLabel, theaterUnderline, false);
            SetTabState(doctrineTabLabel, doctrineUnderline, false);
            SetTabState(mapModesTabLabel, mapModesUnderline, true);
        }

        private static void SetTabState(TMP_Text label, Image underline, bool active)
        {
            if (label != null) label.color = active ? ComPalette.HudEmerald : ComPalette.TextDim;
            if (underline != null) underline.color = active ? ComPalette.HudEmerald : Color.clear;
        }

        private void Refresh()
        {
            if (command == null) return;

            // Update Telemetry
            DynamicMap dm = UnityEngine.Object.FindObjectOfType<DynamicMap>();
            if (dm != null && dm.HQ != null)
            {
                command.UpdateTelemetry(dm.HQ);
            }

            TacticalTheaterState state = command.TheaterState;

            if (airRatioLabel != null)
            {
                airRatioLabel.text = "AIR BALANCE: " + (state.AirSuperiorityRatio * 100f).ToString("N0") +
                    "% (" + state.FriendlyAircraftCount + " FRIENDLY / " + state.HostileAircraftCount + " HOSTILE)";
            }

            if (defconLabel != null)
            {
                defconLabel.text = "DEFCON " + state.DefconLevel + ": " + state.PrimaryThreatDescription;
                defconLabel.color = state.DefconLevel <= 2 ? ComPalette.AlertRed : ComPalette.ThreatAmber;
            }

            if (airbaseCountLabel != null)
            {
                airbaseCountLabel.text = "AIRBASES: " + state.FriendlyAirbaseCount + " FRIENDLY / " +
                    state.HostileAirbaseCount + " HOSTILE";
            }

            if (sortiesLabel != null)
            {
                sortiesLabel.text = "FRIENDLY SAM SITES: " + state.FriendlySamCount +
                    " | DOCTRINE: " + CommandDoctrineHelper.GetName(command.ActiveDoctrine);
            }

            // Doctrine
            if (activeDoctrineDesc != null)
            {
                activeDoctrineDesc.text = CommandDoctrineHelper.GetName(command.ActiveDoctrine) + "\n" +
                    CommandDoctrineHelper.GetDescription(command.ActiveDoctrine);
            }

            if (priorityTargetsLabel != null)
            {
                int max = CommandDoctrineHelper.MaxPriorityTargets(command.PlayerRank);
                priorityTargetsLabel.text = command.PriorityTargets.Count + " / " + max +
                    " PRIORITY TARGETS (FRIENDLY MISSION AI ONLY)";
            }

            // Map Modes
            if (overlay != null)
            {
                if (frontlinesToggleLabel != null)
                {
                    frontlinesToggleLabel.text = overlay.ShowFrontlines ? "ON" : "OFF";
                    frontlinesToggleLabel.color = overlay.ShowFrontlines ? ComPalette.HudEmerald : ComPalette.TextDim;
                }
                if (radarToggleLabel != null)
                {
                    radarToggleLabel.text = overlay.ShowRadar ? "ON" : "OFF";
                    radarToggleLabel.color = overlay.ShowRadar ? ComPalette.InfoCyan : ComPalette.TextDim;
                }
                if (ordersToggleLabel != null)
                {
                    ordersToggleLabel.text = overlay.ShowAiOrders ? "ON" : "OFF";
                    ordersToggleLabel.color = overlay.ShowAiOrders ? ComPalette.ThreatAmber : ComPalette.TextDim;
                }
                if (reconToggleLabel != null)
                {
                    reconToggleLabel.text = overlay.ShowRecon ? "ON" : "OFF";
                    reconToggleLabel.color = overlay.ShowRecon ? ComPalette.TextPrimary : ComPalette.TextDim;
                }
            }

            if (telemetryLabel != null)
            {
                telemetryLabel.text = "DOCTRINE: " + CommandDoctrineHelper.GetName(command.ActiveDoctrine) +
                    "  ·  WINGMEN EXCLUDED";
            }
        }

        // ---- UI Helpers ----

        private Button MakeTabButton(
            RectTransform parent, string text, Rect area, Action action,
            out TMP_Text label, out Image underline)
        {
            var root = new GameObject("Tab_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, area);

            Image image = root.GetComponent<Image>();
            image.color = ComPalette.SurfaceCard;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                Deselect(root);
                action?.Invoke();
            });

            Outline(rect, new Rect(0f, 0f, area.width, area.height), ComPalette.Frame);
            underline = Rule(rect, new Rect(0f, -(area.height - 3f), area.width, 3f), Color.clear);
            label = Label(rect, text, new Rect(0f, 0f, area.width, area.height),
                ComPalette.TextDim, ComPalette.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
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
            image.color = ComPalette.WithAlpha(accent, 0.25f);

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                Deselect(root);
                action?.Invoke();
            });

            Outline(rect, new Rect(0f, 0f, area.width, area.height), ComPalette.WithAlpha(accent, 0.8f));
            label = Label(rect, text, new Rect(0f, 0f, area.width, area.height),
                ComPalette.TextPrimary, ComPalette.FontNano, FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }

        private static void Deselect(GameObject root)
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == root)
                EventSystem.current.SetSelectedGameObject(null);
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

        private static (Image Background, TMP_Text Label) StatusChip(
            RectTransform parent, string text, Rect rect, Color railColor, Color textColor, float fontSize = ComPalette.FontNano)
        {
            var go = new GameObject("StatusChip", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, rect);

            Image bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            bg.color = new Color(railColor.r * 0.15f, railColor.g * 0.15f, railColor.b * 0.15f, 0.85f);

            Outline(parent, rect, new Color(railColor.r, railColor.g, railColor.b, 0.45f));

            var lblGo = new GameObject("ChipLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lRt = lblGo.GetComponent<RectTransform>();
            lRt.SetParent(parent, false);
            Place(lRt, rect);
            var label = lblGo.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = textColor;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return (bg, label);
        }

        private static Image TacticalCard(RectTransform parent, Rect area, Color railColor)
        {
            FramedPanel(parent, area, ComPalette.BorderSubtle, ComPalette.SurfaceCard);
            return Rule(parent, new Rect(area.x, area.y, 3f, area.height), railColor);
        }
    }
}
