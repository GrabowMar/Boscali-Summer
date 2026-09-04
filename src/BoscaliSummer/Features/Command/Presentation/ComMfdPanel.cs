using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal sealed class ComMfdPanel : MonoBehaviour, ISceneService, ITheaterPage
    {
        /// <summary>
        /// The left edge, inset past the spine OPS draws. THEATER is mounted inside the
        /// OPS body, so it shares that body's column rather than the panel's.
        /// </summary>
        private const float Pad = AvTokens.Pad + 14f;
        private const float Gap = AvTokens.Gap;
        private const float TabHeight = AvTokens.TabBarHeight;
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

        private AvButton theaterTab;
        private AvButton doctrineTab;
        private AvButton mapModesTab;

        // Theater SA widgets
        private TMP_Text airRatioLabel;
        private TMP_Text defconLabel;
        private Image airBalanceFill;
        private TMP_Text airbaseCountLabel;
        private TMP_Text sortiesLabel;

        // Doctrine widgets
        private readonly List<AvButton> doctrineButtons = new List<AvButton>();
        private TMP_Text activeDoctrineDesc;
        private TMP_Text priorityTargetsLabel;

        // Map Modes widgets
        private TMP_Text frontlinesToggleLabel;
        private TMP_Text radarToggleLabel;
        private TMP_Text reconToggleLabel;
        private TMP_Text ordersToggleLabel;

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
            airRatioLabel = null;
            defconLabel = null;
            airBalanceFill = null;
            airbaseCountLabel = null;
            sortiesLabel = null;
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

            AvStyled.Spine(host, new Rect(AvTokens.Pad, startY, 3f, Mathf.Abs(startY) + 320f));
            AvStyled.SpineTick(host, AvTokens.Pad + 3f, y - 7f);

            AvStyled.Label(host, new Rect(Pad, y, inner, 14f),
                           "FRIENDLY MISSION AI", "section-title");
            AvStyled.Label(host, new Rect(Pad, y, inner, 14f),
                           "NEVER TASKS YOUR WING", "section-title-note",
                           align: TMPro.TextAlignmentOptions.MidlineRight);
            y -= 20f;

            float tabWidth = (inner - Gap * 2f) / 3f;
            theaterTab = AvKit.Tab(host, "SA", new Rect(Pad, y, tabWidth, TabHeight), ShowTheater);
            doctrineTab = AvKit.Tab(host, "DOCTRINE", new Rect(Pad + tabWidth + Gap, y, tabWidth, TabHeight), ShowDoctrine);
            mapModesTab = AvKit.Tab(host, "MAP", new Rect(Pad + (tabWidth + Gap) * 2f, y, tabWidth, TabHeight), ShowMapModes);
            y -= TabHeight;
            AvKit.Rule(host, new Rect(Pad, y, inner, 1f), AvTheme.Frame);
            y -= AvTokens.Space2;

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
            const float cardH = 92f;
            AvStyled.Box(parent, new Rect(Pad, y, inner, cardH), "section");
            AvStyled.Rail(parent, new Rect(Pad, y, 3f, cardH), "ready");

            AvKit.Label(parent, "FORCE RATIO & AIR DOMINANCE", new Rect(Pad + 12f, y - 4f, inner - 20f, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            airRatioLabel = AvKit.Label(parent, "AIR BALANCE: 50% FRIENDLY", new Rect(Pad + 12f, y - 22f, inner - 20f, 22f),
                                        AvTheme.RailReady, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            // Air-dominance meter: biased left=friendly, right=hostile; fill fills from the left.
            airBalanceFill = AvKit.ProgressBar(parent, new Rect(Pad + 12f, y - 52f, inner - 24f, 8f), 0.5f, AvTheme.RailReady);

            defconLabel = AvKit.Label(parent, "DEFCON 3: CONTESTED THEATER", new Rect(Pad + 12f, y - 66f, inner - 20f, 16f),
                                      AvTheme.RailCaution, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            y -= cardH + 8f;

            const float infraH = 84f;
            AvStyled.Box(parent, new Rect(Pad, y, inner, infraH), "section band");
            AvStyled.Rail(parent, new Rect(Pad, y, 3f, infraH), "info");
            AvKit.Label(parent, "THEATER INFRASTRUCTURE & ACTIVE SORTIES", new Rect(Pad + 12f, y - 4f, inner - 20f, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            airbaseCountLabel = AvKit.Label(parent, "AIRBASES: -- FRIENDLY / -- HOSTILE", new Rect(Pad + 12f, y - 24f, inner - 20f, 18f),
                                            AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            sortiesLabel = AvKit.Label(parent, "SAM SITES: -- | DOCTRINE: --",
                                       new Rect(Pad + 12f, y - 48f, inner - 20f, 18f),
                                       AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        }

        private void BuildDoctrinePage(RectTransform parent, float inner, float startY)
        {
            float y = startY;

            AvKit.Label(parent, "FRIENDLY MISSION-AI BIAS  ·  DOES NOT OVERRIDE WING ORDERS", new Rect(Pad, y, inner, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 16f;

            CommandDoctrine[] doctrines = (CommandDoctrine[])Enum.GetValues(typeof(CommandDoctrine));
            float btnW = (inner - 4f * (doctrines.Length - 1)) / doctrines.Length;

            for (int i = 0; i < doctrines.Length; i++)
            {
                CommandDoctrine doc = doctrines[i];
                float bx = Pad + i * (btnW + 4f);
                AvButton btn = AvKit.Button(parent, doc.ToString().Substring(0, Math.Min(5, doc.ToString().Length)).ToUpperInvariant(),
                    new Rect(bx, y, btnW, 28f), () =>
                    {
                        command?.TrySetDoctrine(doc);
                        Refresh();
                    }, AvTokens.FontSmall, AvButtonStyle.Toggle);
                doctrineButtons.Add(btn);
            }
            y -= 34f;

            activeDoctrineDesc = AvKit.Label(parent, "Balanced: Standard autonomous AI targeting.",
                new Rect(Pad, y, inner, 40f), AvTheme.TextPrimary, AvTokens.FontMicro,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);
            y -= 48f;

            AvKit.Rule(parent, new Rect(Pad, y, inner, 1f), AvTheme.Frame);
            y -= 8f;

            AvKit.Label(parent, "PRIORITY TARGET DESIGNATIONS (REQ RANK 1 SGT)", new Rect(Pad, y, inner, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 16f;

            priorityTargetsLabel = AvKit.Label(parent, "0 / 1 TARGETS DESIGNATED (CLICK TRACKED HOSTILE TO MARK)",
                new Rect(Pad, y, inner, 20f), AvTheme.RailCaution, AvTokens.FontMicro,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        }

        private void BuildMapModesPage(RectTransform parent, float inner, float startY)
        {
            float y = startY;

            AvKit.Label(parent, "TACTICAL MAP OVERLAYS & MODES", new Rect(Pad, y, inner, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 18f;

            float rowH = 34f;

            // 1. Frontlines
            AvKit.Button(parent, "TOGGLE FRONTLINES (AREA OF CONTROL)", new Rect(Pad, y, inner - 90f, rowH),
                () =>
                {
                    if (overlay != null) overlay.ShowFrontlines = !overlay.ShowFrontlines;
                    Refresh();
                }, AvTokens.FontSmall, AvButtonStyle.Default);
            frontlinesToggleLabel = AvKit.Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                AvTheme.RailReady, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 6f;

            // 2. Radar
            AvKit.Button(parent, "TOGGLE RADAR / SAM THREAT ENVELOPES", new Rect(Pad, y, inner - 90f, rowH),
                () =>
                {
                    if (overlay != null) overlay.ShowRadar = !overlay.ShowRadar;
                    Refresh();
                }, AvTokens.FontSmall, AvButtonStyle.Default);
            radarToggleLabel = AvKit.Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                AvTheme.RailInfo, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 6f;

            // 3. AI Orders
            AvKit.Button(parent, "TOGGLE AI ORDER VECTORS (FLIGHT PATHS)", new Rect(Pad, y, inner - 90f, rowH),
                () =>
                {
                    if (overlay != null) overlay.ShowAiOrders = !overlay.ShowAiOrders;
                    Refresh();
                }, AvTokens.FontSmall, AvButtonStyle.Default);
            ordersToggleLabel = AvKit.Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                AvTheme.RailCaution, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 6f;

            // 4. Recon
            AvKit.Button(parent, "TOGGLE VISIBILITY & RECON SURVEILLANCE", new Rect(Pad, y, inner - 90f, rowH),
                () =>
                {
                    if (overlay != null) overlay.ShowRecon = !overlay.ShowRecon;
                    Refresh();
                }, AvTokens.FontSmall, AvButtonStyle.Default);
            reconToggleLabel = AvKit.Label(parent, "ON", new Rect(Pad + inner - 80f, y, 76f, rowH),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        private void ShowTheater()
        {
            theaterPage?.SetActive(true);
            doctrinePage?.SetActive(false);
            mapModesPage?.SetActive(false);
            theaterTab?.SetLatched(true);
            doctrineTab?.SetLatched(false);
            mapModesTab?.SetLatched(false);
            Refresh();
        }

        private void ShowDoctrine()
        {
            theaterPage?.SetActive(false);
            doctrinePage?.SetActive(true);
            mapModesPage?.SetActive(false);
            theaterTab?.SetLatched(false);
            doctrineTab?.SetLatched(true);
            mapModesTab?.SetLatched(false);
            Refresh();
        }

        private void ShowMapModes()
        {
            theaterPage?.SetActive(false);
            doctrinePage?.SetActive(false);
            mapModesPage?.SetActive(true);
            theaterTab?.SetLatched(false);
            doctrineTab?.SetLatched(false);
            mapModesTab?.SetLatched(true);
            Refresh();
        }

        private void Refresh()
        {
            if (command == null) return;

            // Update Telemetry
            DynamicMap dm = SceneSingleton<DynamicMap>.i;
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

            if (airBalanceFill != null)
            {
                airBalanceFill.fillAmount = Mathf.Clamp01(state.AirSuperiorityRatio);
                airBalanceFill.color = state.AirSuperiorityRatio >= 0.5f ? AvTheme.RailReady : AvTheme.RailCaution;
            }

            if (defconLabel != null)
            {
                defconLabel.text = "DEFCON " + state.DefconLevel + ": " + state.PrimaryThreatDescription;
                defconLabel.color = state.DefconLevel <= 2 ? AvTheme.Alert : AvTheme.RailCaution;
            }

            if (airbaseCountLabel != null)
            {
                airbaseCountLabel.text = "AIRBASES: " + state.FriendlyAirbaseCount + " FRIENDLY / " +
                    state.HostileAirbaseCount + " HOSTILE";
            }

            if (sortiesLabel != null)
            {
                sortiesLabel.text = "SAM SITES: " + state.FriendlySamCount +
                    " | DOCTRINE: " + CommandDoctrineHelper.GetName(command.ActiveDoctrine);
            }

            // Doctrine
            if (activeDoctrineDesc != null)
            {
                activeDoctrineDesc.text = CommandDoctrineHelper.GetName(command.ActiveDoctrine) + "\n" +
                    CommandDoctrineHelper.GetDescription(command.ActiveDoctrine);
            }

            CommandDoctrine[] doctrines = (CommandDoctrine[])Enum.GetValues(typeof(CommandDoctrine));
            for (int i = 0; i < doctrineButtons.Count && i < doctrines.Length; i++)
            {
                doctrineButtons[i].SetLatched(command.ActiveDoctrine == doctrines[i]);
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
                    frontlinesToggleLabel.color = overlay.ShowFrontlines ? AvTheme.RailReady : AvTheme.Dim;
                }
                if (radarToggleLabel != null)
                {
                    radarToggleLabel.text = overlay.ShowRadar ? "ON" : "OFF";
                    radarToggleLabel.color = overlay.ShowRadar ? AvTheme.RailInfo : AvTheme.Dim;
                }
                if (ordersToggleLabel != null)
                {
                    ordersToggleLabel.text = overlay.ShowAiOrders ? "ON" : "OFF";
                    ordersToggleLabel.color = overlay.ShowAiOrders ? AvTheme.RailCaution : AvTheme.Dim;
                }
                if (reconToggleLabel != null)
                {
                    reconToggleLabel.text = overlay.ShowRecon ? "ON" : "OFF";
                    reconToggleLabel.color = overlay.ShowRecon ? AvTheme.TextPrimary : AvTheme.Dim;
                }
            }
        }

        private static GameObject CreatePage(RectTransform parent, string name)
        {
            var page = new GameObject(name, typeof(RectTransform));
            RectTransform rect = page.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            AvKit.Stretch(rect);
            return page;
        }
    }
}
