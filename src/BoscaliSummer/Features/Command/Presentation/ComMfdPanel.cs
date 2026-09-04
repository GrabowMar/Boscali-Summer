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
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal sealed class ComMfdPanel : MonoBehaviour, ISceneService, ITheaterPage
    {
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
        private TMP_Text defconLabel;
        private TMP_Text airRatioLabel;
        private Image airBalanceFill;

        private TMP_Text sectorControlLabel;
        private Image territoryFill;
        private TMP_Text groundUnitsLabel;

        private TMP_Text earlyWarningLabel;
        private TMP_Text infraLabel;

        // Doctrine widgets
        private readonly List<AvButton> doctrineButtons = new List<AvButton>();
        private TMP_Text activeDoctrineDesc;
        private TMP_Text priorityTargetsLabel;

        // Map Modes widgets
        private TMP_Text sectorsToggleLabel;
        private TMP_Text frontlinesToggleLabel;
        private TMP_Text opacityValueLabel;

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
            defconLabel = null;
            airRatioLabel = null;
            airBalanceFill = null;
            sectorControlLabel = null;
            territoryFill = null;
            groundUnitsLabel = null;
            earlyWarningLabel = null;
            infraLabel = null;
            sectorsToggleLabel = null;
            frontlinesToggleLabel = null;
            opacityValueLabel = null;
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
                           "TACTICAL THEATER COMMAND", "section-title");
            AvStyled.Label(host, new Rect(Pad, y, inner, 14f),
                           "C4ISR OVERVIEW", "section-title-note",
                           align: TextAlignmentOptions.MidlineRight);
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

            // Card 1: Air Dominance & DEFCON Rail
            const float airCardH = 78f;
            AvStyled.Box(parent, new Rect(Pad, y, inner, airCardH), "section");
            AvStyled.Rail(parent, new Rect(Pad, y, 3f, airCardH), "ready");

            defconLabel = AvKit.Label(parent, "DEFCON 3: CONTESTED THEATER",
                new Rect(Pad + 12f, y - 4f, inner - 20f, 15f),
                AvTheme.RailCaution, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            airRatioLabel = AvKit.Label(parent, "AIR DOMINANCE: 50% FRIENDLY (-- / -- AC)",
                new Rect(Pad + 12f, y - 22f, inner - 20f, 18f),
                AvTheme.RailReady, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            airBalanceFill = AvKit.ProgressBar(parent, new Rect(Pad + 12f, y - 48f, inner - 24f, 7f), 0.5f, AvTheme.RailReady);

            y -= airCardH + 6f;

            // Card 2: Ground Territory & Tactical Sectors (FrontlineMap / NOCommander inspiration)
            const float sectorCardH = 82f;
            AvStyled.Box(parent, new Rect(Pad, y, inner, sectorCardH), "section band");
            AvStyled.Rail(parent, new Rect(Pad, y, 3f, sectorCardH), "info");

            AvKit.Label(parent, "TERRITORY & SECTOR CONTROL", new Rect(Pad + 12f, y - 4f, inner - 20f, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            sectorControlLabel = AvKit.Label(parent, "ALLIED: -- | ENEMY: -- | CONTESTED: --",
                new Rect(Pad + 12f, y - 20f, inner - 20f, 18f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            territoryFill = AvKit.ProgressBar(parent, new Rect(Pad + 12f, y - 44f, inner - 24f, 7f), 0.5f, AvTheme.RailInfo);

            groundUnitsLabel = AvKit.Label(parent, "GROUND FORCES: -- ALLIED / -- HOSTILE",
                new Rect(Pad + 12f, y - 58f, inner - 20f, 16f),
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            y -= sectorCardH + 6f;

            // Card 3: Early Warning Threat Board (Alarm System inspiration)
            const float warningCardH = 76f;
            AvStyled.Box(parent, new Rect(Pad, y, inner, warningCardH), "section");
            AvStyled.Rail(parent, new Rect(Pad, y, 3f, warningCardH), "alert");

            AvKit.Label(parent, "EARLY WARNING & INFRASTRUCTURE", new Rect(Pad + 12f, y - 4f, inner - 20f, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            earlyWarningLabel = AvKit.Label(parent, "AIRSPACE NOMINAL",
                new Rect(Pad + 12f, y - 22f, inner - 20f, 18f),
                AvTheme.Alert, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            infraLabel = AvKit.Label(parent, "AIRBASES: -- FRIENDLY / -- HOSTILE | SAMS: --",
                new Rect(Pad + 12f, y - 46f, inner - 20f, 16f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
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

            AvKit.Label(parent, "TACTICAL MAP OVERLAYS & CONTROLS", new Rect(Pad, y, inner, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 18f;

            float rowH = 30f;

            // 1. Sector Grid
            AvKit.Button(parent, "SECTOR CONTROL GRID", new Rect(Pad, y, inner - 80f, rowH),
                () =>
                {
                    if (overlay != null) overlay.ShowSectors = !overlay.ShowSectors;
                    Refresh();
                }, AvTokens.FontSmall, AvButtonStyle.Default);
            sectorsToggleLabel = AvKit.Label(parent, "ON", new Rect(Pad + inner - 72f, y, 70f, rowH),
                AvTheme.RailReady, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 5f;

            // 2. Frontline Barriers
            AvKit.Button(parent, "FRONTLINE BARRIER LINES", new Rect(Pad, y, inner - 80f, rowH),
                () =>
                {
                    if (overlay != null) overlay.ShowFrontlines = !overlay.ShowFrontlines;
                    Refresh();
                }, AvTokens.FontSmall, AvButtonStyle.Default);
            frontlinesToggleLabel = AvKit.Label(parent, "ON", new Rect(Pad + inner - 72f, y, 70f, rowH),
                AvTheme.RailReady, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= rowH + 12f;

            AvKit.Rule(parent, new Rect(Pad, y, inner, 1f), AvTheme.Frame);
            y -= 8f;

            // Opacity quick steps
            AvKit.Label(parent, "OVERLAY OPACITY (PRESETS)", new Rect(Pad, y, inner, 14f),
                        AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            y -= 16f;

            float opW = (inner - 9f) / 4f;
            AvKit.Button(parent, "20%", new Rect(Pad, y, opW, 26f), () => SetOpacity(0.20f), AvTokens.FontMicro, AvButtonStyle.Default);
            AvKit.Button(parent, "35%", new Rect(Pad + (opW + 3f), y, opW, 26f), () => SetOpacity(0.35f), AvTokens.FontMicro, AvButtonStyle.Default);
            AvKit.Button(parent, "50%", new Rect(Pad + (opW + 3f) * 2f, y, opW, 26f), () => SetOpacity(0.50f), AvTokens.FontMicro, AvButtonStyle.Default);
            AvKit.Button(parent, "75%", new Rect(Pad + (opW + 3f) * 3f, y, opW, 26f), () => SetOpacity(0.75f), AvTokens.FontMicro, AvButtonStyle.Default);
            y -= 30f;

            opacityValueLabel = AvKit.Label(parent, "CURRENT OPACITY: 35%", new Rect(Pad, y, inner, 16f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        }

        private void SetOpacity(float val)
        {
            if (settings != null)
            {
                settings.OverlayOpacity.Value = val;
            }
            Refresh();
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

            // 1. Air & DEFCON
            if (defconLabel != null)
            {
                defconLabel.text = "DEFCON " + state.DefconLevel + ": " + state.PrimaryThreatDescription;
                defconLabel.color = state.DefconLevel <= 2 ? AvTheme.Alert :
                    (state.DefconLevel == 3 ? AvTheme.RailCaution : AvTheme.RailReady);
            }

            if (airRatioLabel != null)
            {
                airRatioLabel.text = "AIR DOMINANCE: " + (state.AirSuperiorityRatio * 100f).ToString("N0") +
                    "% (" + state.FriendlyAircraftCount + " ALLIED / " + state.HostileAircraftCount + " HOSTILE)";
            }

            if (airBalanceFill != null)
            {
                airBalanceFill.fillAmount = Mathf.Clamp01(state.AirSuperiorityRatio);
                airBalanceFill.color = state.AirSuperiorityRatio >= 0.5f ? AvTheme.RailReady : AvTheme.RailCaution;
            }

            // 2. Sectors & Territory
            if (sectorControlLabel != null)
            {
                sectorControlLabel.text = "ALLIED: " + state.FriendlySectorCount + " | ENEMY: " +
                    state.HostileSectorCount + " | CONTESTED: " + state.ContestedSectorCount +
                    " (" + (state.TerritoryControlRatio * 100f).ToString("N0") + "% CONTROL)";
            }

            if (territoryFill != null)
            {
                territoryFill.fillAmount = Mathf.Clamp01(state.TerritoryControlRatio);
                territoryFill.color = state.TerritoryControlRatio >= 0.5f ? AvTheme.RailReady : AvTheme.RailCaution;
            }

            if (groundUnitsLabel != null)
            {
                groundUnitsLabel.text = "GROUND FORCES: " + state.FriendlyGroundUnitsCount +
                    " ALLIED / " + state.HostileGroundUnitsCount + " HOSTILE UNITS";
            }

            // 3. Early Warning & Infrastructure
            if (earlyWarningLabel != null)
            {
                earlyWarningLabel.text = state.ActiveThreatWarning;
                earlyWarningLabel.color = state.DefconLevel <= 2 ? AvTheme.Alert : AvTheme.RailInfo;
            }

            if (infraLabel != null)
            {
                infraLabel.text = "AIRBASES: " + state.FriendlyAirbaseCount + " ALLIED / " +
                    state.HostileAirbaseCount + " ENEMY | SAMS: " + state.FriendlySamCount + " ACTIVE";
            }

            // 4. Doctrine
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

            // 5. Map Modes
            if (overlay != null)
            {
                if (sectorsToggleLabel != null)
                {
                    sectorsToggleLabel.text = overlay.ShowSectors ? "ON" : "OFF";
                    sectorsToggleLabel.color = overlay.ShowSectors ? AvTheme.RailReady : AvTheme.Dim;
                }
                if (frontlinesToggleLabel != null)
                {
                    frontlinesToggleLabel.text = overlay.ShowFrontlines ? "ON" : "OFF";
                    frontlinesToggleLabel.color = overlay.ShowFrontlines ? AvTheme.RailReady : AvTheme.Dim;
                }
            }

            if (opacityValueLabel != null && settings != null)
            {
                opacityValueLabel.text = "CURRENT OPACITY: " + Mathf.RoundToInt(settings.OverlayOpacity.Value * 100f) + "%";
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
