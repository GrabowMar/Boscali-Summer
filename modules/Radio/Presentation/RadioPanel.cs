using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Radio.Configuration;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    internal static class RadioPanel
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float Pad = AvTokens.Pad;
        private const float Gap = AvTokens.Gap;
        private const float RowHeight = AvTokens.RowHeight;
        private const float ControlHeight = 32f;
        private const float HeroHeight = 106f;
        private const float DialHeight = 34f;
        private const float ModeHeight = 30f;
        private const float HeaderHeight = 16f;
        private const float PresetHeight = 28f;
        private const float ChannelPitch = RowHeight + 2f;
        private const float TrackLinePitch = 15f;
        private const float TrackLineHeight = 14f;
        private const int MaximumTrackRows = 10;
        private const float SweepSeconds = 0.45f;
        private const int MaximumRows = 8;
        private const int MinimumRows = 3;
        private const int MeterPips = 10;

        private sealed class ChannelRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image SelectionRule;
            public Image BadgeGround;
            public Image Icon;
            public TMP_Text Badge;
            public TMP_Text Label;
            public TMP_Text Frequency;
            public TMP_Text Count;
        }

        private sealed class ProgrammeRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image Rule;
            public TMP_Text Number;
            public TMP_Text Title;
        }

        private static readonly ChannelRow[] rows = new ChannelRow[MaximumRows];
        private static readonly ProgrammeRow[] programmeRows = new ProgrammeRow[MaximumTrackRows];
        private static readonly Image[] meterPips = new Image[MeterPips];
        private static readonly AvButton[] presetButtons = new AvButton[RadioSettings.PresetSlots];
        private static MFDScreen screen;
        private static GameObject screenRoot;
        private static AvScreen shell;
        private static RadioManager manager;
        private static TMP_Text frequencyLabel;
        private static TMP_Text unitLabel;
        private static TMP_Text bandLabel;
        private static TMP_Text signalLabel;
        private static TMP_Text stationNameLabel;
        private static TMP_Text wireLabel;
        private static TMP_Text programLabel;
        private static Image stationIconGround;
        private static Image stationIcon;
        private static TMP_Text stationBadge;
        private static TMP_Text trackLabel;
        private static TMP_Text timeLabel;
        private static TMP_Text pageLabel;
        private static TMP_Text stationsNote;
        private static TMP_Text programmeNote;
        private static TMP_Text programmeEmptyLabel;
        private static AvButton programmePreviousButton;
        private static AvButton programmeNextButton;
        private static AvButton pagePreviousButton;
        private static AvButton pageNextButton;
        private static AvStyled.DataBar dataBar;
        private static TMP_Text channelsEmptyLabel;
        private static Image progressFill;
        private static AvButton seekDownButton;
        private static AvButton tuneDownButton;
        private static AvButton playButton;
        private static AvButton tuneUpButton;
        private static AvButton seekUpButton;
        private static AvButton stopButton;
        private static AvButton shuffleButton;
        private static AvButton repeatButton;
        private static AvButton scanButton;
        private static AvButton bandButton;
        private static AvButton setButton;
        private static TMP_Text volumeLabel;
        private static GameObject dialRoot;
        private static RectTransform pageRoot;
        private static Rect dialArea;
        private static RadioBand dialBand;
        private static Image dialNeedle;
        private static readonly List<Image> dialMarkers = new List<Image>();
        private static readonly List<int> dialMarkerStations = new List<int>();
        private static int rowsPerPage = MinimumRows;
        private static int page;
        private static int programmePage;
        private static int programmeRowsPerPage;
        private static int lastProgrammeTrack = -1;
        private static int iconRevision = -1;
        private static int markerRevision = -1;
        private static RadioDial shownDial;
        private static bool dialInitialized;
        private static bool shownOffStation;
        private static bool sweeping;
        private static float sweepStart;
        private static float sweepFrom;
        private static float sweepTo;
        private static bool presetArmed;
        private static float nextMeterAt;
        private static float nextAttempt;
        private static float nextRefresh;
        private static bool unavailableLogged;
        private static bool gaveUp;

        public static void Tick(RadioManager radio)
        {
            manager = radio;
            if (gaveUp) return;
            if (!GameAccess.MfdAvailable)
            {
                if (!unavailableLogged)
                {
                    unavailableLogged = true;
                    Plugin.Logger.LogWarning("Radio panel unavailable: VirtualMFD access did not resolve.");
                }
                return;
            }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (!screen.isActive) return;
            Animate();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.15f;
            Refresh();
        }

        public static void Reset()
        {
            MfdBezel.Release(MfdSlots.Rad);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            RadioStationIconCache.Clear();
            screenRoot = null;
            screen = null;
            shell = null;
            manager = null;
            frequencyLabel = null;
            unitLabel = null;
            bandLabel = null;
            signalLabel = null;
            stationNameLabel = null;
            wireLabel = null;
            programLabel = null;
            stationIconGround = null;
            stationIcon = null;
            stationBadge = null;
            trackLabel = null;
            timeLabel = null;
            pageLabel = null;
            stationsNote = null;
            pagePreviousButton = null;
            pageNextButton = null;
            dataBar = null;
            channelsEmptyLabel = null;
            progressFill = null;
            seekDownButton = null;
            tuneDownButton = null;
            playButton = null;
            tuneUpButton = null;
            seekUpButton = null;
            stopButton = null;
            shuffleButton = null;
            repeatButton = null;
            scanButton = null;
            bandButton = null;
            setButton = null;
            volumeLabel = null;
            dialRoot = null;
            dialNeedle = null;
            pageRoot = null;
            programmeNote = null;
            programmeEmptyLabel = null;
            programmePreviousButton = null;
            programmeNextButton = null;
            dialMarkers.Clear();
            dialMarkerStations.Clear();
            dialBand = RadioBand.Fm;
            for (int i = 0; i < programmeRows.Length; i++) programmeRows[i] = null;
            for (int i = 0; i < presetButtons.Length; i++) presetButtons[i] = null;
            for (int i = 0; i < meterPips.Length; i++) meterPips[i] = null;
            for (int i = 0; i < rows.Length; i++) rows[i] = null;
            rowsPerPage = MinimumRows;
            page = 0;
            programmePage = 0;
            programmeRowsPerPage = 0;
            lastProgrammeTrack = -1;
            iconRevision = -1;
            markerRevision = -1;
            shownDial = default;
            dialInitialized = false;
            shownOffStation = false;
            sweeping = false;
            presetArmed = false;
            nextMeterAt = 0f;
            nextAttempt = 0f;
            nextRefresh = 0f;
            gaveUp = false;
        }

        private static void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Rad, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    Fail("no free bezel slot");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    Fail("claimed bezel changed before binding");
                    return;
                }
                Plugin.Logger.LogInfo("Radio MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                MfdBezel.Release(MfdSlots.Rad);
                Fail(e.Message);
                Plugin.Logger.LogError("Radio MFD install failed: " + e);
            }
        }

        private static MFDScreen Build(MFDScreen template, Button bezel)
        {
            var root = new GameObject("BoscaliRadio.Screen", typeof(RectTransform), typeof(Image));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);
            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;
            // Position is deliberately not copied. VirtualMFD.showPos is Vector3.zero and
            // MFDScreen.ShowScreen assigns it straight to localPosition, so a screen has no
            // remembered home — it is placed by its parent and anchors, and an
            // anchoredPosition written here is overwritten whenever the panel is opened.
            float height = AvScreen.ResolveHeight(
                templateRect.parent as RectTransform, PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            screenRoot = root;

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
                content, "RAD", new[] { "TUNER" }, null, 3,
                Width, height, _ => nextRefresh = 0f);
            dataBar = shell.DataBar;

            Rect body = shell.Body;
            pageRoot = (RectTransform)shell.CreatePage(0, "TunerPage").transform;
            float y = body.y;

            float bodyBottom = body.y - body.height;
            float bodyX = body.x;
            float bodyW = body.width;

            float cardTop = y;
            AvKit.TacticalCard(pageRoot, new Rect(bodyX, cardTop, bodyW, HeroHeight), AvTheme.RailReady);

            stationIconGround = AvKit.Panel(pageRoot,
                new Rect(bodyX + AvTokens.Space2, cardTop - AvTokens.Space2, 56f, 56f),
                AvTheme.SurfaceInert, AvSprites.Card);
            AvKit.Outline(pageRoot,
                new Rect(bodyX + AvTokens.Space2, cardTop - AvTokens.Space2, 56f, 56f), AvTheme.Frame);
            stationIcon = AvKit.Panel(stationIconGround.rectTransform,
                new Rect(AvTokens.Space1 + 1f, -(AvTokens.Space1 + 1f), 48f, 48f), Color.white);
            stationIcon.preserveAspect = true;
            stationIcon.enabled = false;
            stationBadge = AvKit.Label(stationIconGround.rectTransform, "--",
                new Rect(0f, 0f, 56f, 56f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.Center);

            float infoX = bodyX + AvTokens.Space2 + 56f + AvTokens.Space3;
            frequencyLabel = AvKit.Label(pageRoot, "---.-",
                new Rect(infoX, cardTop - AvTokens.Space2, 128f, 42f),
                AvTheme.TextPrimary, 32f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            unitLabel = AvKit.Label(pageRoot, "MHz",
                new Rect(infoX + 118f, cardTop - AvTokens.Space6, 48f, 16f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            bandLabel = AvStyled.Label(pageRoot,
                new Rect(bodyX + bodyW - AvTokens.Space2 - 130f, cardTop - AvTokens.Space2, 130f, 14f),
                "FM · STEREO", "section-title", align: TextAlignmentOptions.Right);

            float meterX = bodyX + bodyW - AvTokens.Space2 - MeterPips * 11f + 2f;
            for (int i = 0; i < MeterPips; i++)
            {
                meterPips[i] = AvKit.Rule(pageRoot,
                    new Rect(meterX + i * 11f, cardTop - 34f, 8f, 8f), AvTheme.Hairline);
            }
            signalLabel = AvStyled.Label(pageRoot,
                new Rect(bodyX + bodyW - AvTokens.Space2 - 130f, cardTop - 48f, 130f, 12f),
                "NO SIG", "section-title-note", align: TextAlignmentOptions.Right);

            stationNameLabel = AvStyled.Label(pageRoot,
                new Rect(infoX, cardTop - 50f, bodyW - (infoX - bodyX) - 190f, 16f),
                "NO STATIONS", "row-name");
            wireLabel = AvKit.Label(pageRoot, "",
                new Rect(infoX, cardTop - 64f, bodyW - (infoX - bodyX) - 190f, 14f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            programLabel = AvStyled.Label(pageRoot,
                new Rect(bodyX + bodyW - AvTokens.Space2 - 180f, cardTop - 62f, 180f, 12f),
                "", "section-title-note", align: TextAlignmentOptions.Right);

            trackLabel = AvStyled.Label(pageRoot,
                new Rect(bodyX + AvTokens.Space2, cardTop - 78f, bodyW - AvTokens.Space4 - 80f, 16f),
                "NO LOCAL TRACKS", "row-main");
            timeLabel = AvKit.Label(pageRoot, "00:00 / 00:00",
                new Rect(bodyX + bodyW - AvTokens.Space2 - 80f, cardTop - 78f, 80f, 16f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            progressFill = AvKit.ProgressBar(pageRoot,
                new Rect(bodyX + AvTokens.Space2, cardTop - 98f, bodyW - AvTokens.Space4, 4f),
                0f, AvTheme.Accent);

            y = cardTop - HeroHeight - AvTokens.Space2;

            dialArea = new Rect(bodyX, y, bodyW - 94f, DialHeight);
            EnsureDialBand(manager == null ? RadioBand.Fm : manager.TunedDial.Band);
            float volumeX = bodyX + bodyW - 90f;
            AvKit.Button(pageRoot, "-", new Rect(volumeX, y - 16f, 22f, 18f),
                () => manager?.NudgeVolume(-0.1f), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Turn the receiver volume down.");
            volumeLabel = AvKit.Label(pageRoot, "100", new Rect(volumeX + 24f, y - 16f, 42f, 18f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            AvKit.Button(pageRoot, "+", new Rect(volumeX + 68f, y - 16f, 22f, 18f),
                () => manager?.NudgeVolume(0.1f), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Turn the receiver volume up.");
            y -= DialHeight + AvTokens.Space2;

            float keyWidth = (bodyW - Gap * 5f) / 6f;
            seekDownButton = AvKit.Button(pageRoot, "< SEEK", new Rect(bodyX, y, keyWidth, ControlHeight),
                () => manager?.SeekStation(-1), AvTokens.FontSmall, AvButtonStyle.Default)
                .WithTooltip("Seek the next station down the band.");
            tuneDownButton = AvKit.Button(pageRoot, "TUNE -", new Rect(bodyX + (keyWidth + Gap), y, keyWidth, ControlHeight),
                () => manager?.StepDial(-1), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Tune one step down. Between stations is dead air.");
            playButton = AvKit.Button(pageRoot, "PLAY", new Rect(bodyX + (keyWidth + Gap) * 2f, y, keyWidth, ControlHeight),
                () => manager?.TogglePlayback(), AvTokens.FontSmall, AvButtonStyle.Primary)
                .WithTooltip("Play or pause the tuned station. Requires at least one track.");
            tuneUpButton = AvKit.Button(pageRoot, "TUNE +", new Rect(bodyX + (keyWidth + Gap) * 3f, y, keyWidth, ControlHeight),
                () => manager?.StepDial(1), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Tune one step up. Between stations is dead air.");
            seekUpButton = AvKit.Button(pageRoot, "SEEK >", new Rect(bodyX + (keyWidth + Gap) * 4f, y, keyWidth, ControlHeight),
                () => manager?.SeekStation(1), AvTokens.FontSmall, AvButtonStyle.Default)
                .WithTooltip("Seek the next station up the band.");
            stopButton = AvKit.Button(pageRoot, "STOP", new Rect(bodyX + (keyWidth + Gap) * 5f, y, keyWidth, ControlHeight),
                () => manager?.Stop(), AvTokens.FontSmall, AvButtonStyle.Default)
                .WithTooltip("Sign off and return control to the game soundtrack.");
            y -= ControlHeight + Gap * 0.75f;

            float modeWidth = (bodyW - Gap * 6f) / 7f;
            bandButton = AvKit.Button(pageRoot, "BAND", new Rect(bodyX, y, modeWidth, ModeHeight),
                () => manager?.ToggleBand(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Switch between FM and MW. Each band remembers its last frequency.");
            AvKit.Button(pageRoot, "TRK -", new Rect(bodyX + (modeWidth + Gap), y, modeWidth, ModeHeight),
                () => manager?.Previous(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Play the previous track on the tuned station.");
            AvKit.Button(pageRoot, "TRK +", new Rect(bodyX + (modeWidth + Gap) * 2f, y, modeWidth, ModeHeight),
                () => manager?.Next(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Play the next track on the tuned station.");
            scanButton = AvKit.Button(pageRoot, "SCAN", new Rect(bodyX + (modeWidth + Gap) * 3f, y, modeWidth, ModeHeight),
                () => manager?.ToggleScan(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Seek through the stations, holding each one for a few seconds.");
            shuffleButton = AvKit.Button(pageRoot, "SHUF", new Rect(bodyX + (modeWidth + Gap) * 4f, y, modeWidth, ModeHeight),
                () => manager?.ToggleShuffle(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Toggle random track order for the tuned station.");
            repeatButton = AvKit.Button(pageRoot, "REP", new Rect(bodyX + (modeWidth + Gap) * 5f, y, modeWidth, ModeHeight),
                () => manager?.ToggleRepeat(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Toggle repeating the current track.");
            AvKit.Button(pageRoot, "FOLDER", new Rect(bodyX + (modeWidth + Gap) * 6f, y, modeWidth, ModeHeight),
                () => manager?.OpenLibraryFolder(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Open Boscali Summer's local music library folder.");
            y -= ModeHeight + AvTokens.Space2;

            SectionTitle(pageRoot, bodyX, bodyW, y, "STATIONS");
            stationsNote = AvStyled.Label(pageRoot, new Rect(bodyX, y, bodyW - 64f, HeaderHeight),
                "0 FOUND", "section-title-note", align: TextAlignmentOptions.Right);
            AvKit.Button(pageRoot, "RESCAN",
                new Rect(bodyX + bodyW - 60f, y - 2f, 60f, 16f),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Sign off and rescan the local music library.");
            y -= HeaderHeight + 6f;

            // The preset row is pinned to the bottom, and the station list takes what it
            // needs (never more row slots than there are stations).
            float presetTop = bodyBottom + PresetHeight;
            float listRoom = y - (presetTop + AvTokens.Space2 + AvTokens.RowHeight + AvTokens.Space2);
            int fit = Mathf.FloorToInt(listRoom / ChannelPitch);
            int wanted = manager == null
                ? MinimumRows
                : Mathf.Clamp(manager.ChannelCount, MinimumRows, MaximumRows);
            rowsPerPage = Mathf.Clamp(Mathf.Min(fit, wanted), MinimumRows, MaximumRows);

            float channelsBlock = ChannelPitch * rowsPerPage;
            channelsEmptyLabel = AvKit.Label(pageRoot, "",
                new Rect(bodyX + AvTokens.Space4, y, bodyW - AvTokens.Space4 * 2f, channelsBlock),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Italic, TextAlignmentOptions.Center, wrap: true);
            channelsEmptyLabel.gameObject.SetActive(false);

            for (int i = 0; i < rowsPerPage; i++)
                rows[i] = MakeChannelRow(pageRoot, i, y - i * ChannelPitch);
            y -= channelsBlock + AvTokens.Space2;

            AvButton[] pageButtons = AvKit.Stepper(
                pageRoot, bodyX, y, bodyW, out pageLabel, PreviousPage, NextPage);
            pagePreviousButton = pageButtons[0];
            pageNextButton = pageButtons[1];
            pagePreviousButton.WithTooltip("Show the previous page of stations. Disabled on the first page.");
            pageNextButton.WithTooltip("Show the next page of stations. Disabled on the last page.");
            y -= AvTokens.RowHeight + AvTokens.Space2;

            float programmeRoom = y - (presetTop + AvTokens.Space2);
            if (programmeRoom >= HeaderHeight + 6f + TrackLinePitch)
            {
                SectionTitle(pageRoot, bodyX, bodyW, y, "PROGRAMME");
                programmePreviousButton = AvKit.Button(pageRoot, "<",
                    new Rect(bodyX + bodyW - 88f, y - 1f, 22f, 16f),
                    () => NudgeProgrammePage(-1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                    .WithTooltip("Show the previous page of tracks.");
                programmeNote = AvStyled.Label(pageRoot,
                    new Rect(bodyX + bodyW - 64f, y, 40f, HeaderHeight),
                    "1 / 1", "section-title-note", align: TextAlignmentOptions.Center);
                programmeNextButton = AvKit.Button(pageRoot, ">",
                    new Rect(bodyX + bodyW - 22f, y - 1f, 22f, 16f),
                    () => NudgeProgrammePage(1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                    .WithTooltip("Show the next page of tracks.");

                float rowsRoom = programmeRoom - HeaderHeight - 6f;
                programmeRowsPerPage = Mathf.Clamp(
                    Mathf.FloorToInt(rowsRoom / TrackLinePitch), 1, MaximumTrackRows);
                float pitch = Mathf.Clamp(rowsRoom / programmeRowsPerPage, TrackLinePitch, 26f);
                float rowsTop = y - HeaderHeight - 6f;
                for (int i = 0; i < programmeRowsPerPage; i++)
                    programmeRows[i] = MakeProgrammeRow(pageRoot, i, rowsTop - i * pitch);

                programmeEmptyLabel = AvKit.Label(pageRoot, "",
                    new Rect(bodyX, rowsTop, bodyW, Mathf.Max(TrackLineHeight, pitch)),
                    AvTheme.Dim, AvTokens.FontMicro, FontStyles.Italic, TextAlignmentOptions.Left);
                programmeEmptyLabel.gameObject.SetActive(false);
            }

            const float setWidth = 50f;
            float presetWidth = (bodyW - setWidth - Gap * RadioSettings.PresetSlots) /
                                RadioSettings.PresetSlots;
            setButton = AvKit.Button(pageRoot, "SET", new Rect(bodyX, presetTop, setWidth, PresetHeight),
                TogglePresetArming, AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Arm SET, then press a preset to store the tuned station on it.");
            for (int i = 0; i < RadioSettings.PresetSlots; i++)
            {
                int slot = i;
                presetButtons[i] = AvKit.Button(pageRoot, (slot + 1).ToString(),
                    new Rect(bodyX + setWidth + Gap + slot * (presetWidth + Gap), presetTop, presetWidth, PresetHeight),
                    () => OnPreset(slot), AvTokens.FontMicro, AvButtonStyle.Default)
                    .WithTooltip("Preset " + (slot + 1) + ". Press SET first to store the tuned station here.");
            }

            var result = root.AddComponent<MFDScreen>();
            result.shortName = "RAD";
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel == null ? null : bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                screenRoot = null;
                shell = null;
                Fail("bezel label or highlight missing");
                return null;
            }

            shell.SetPage(0);
            Refresh();
            return result;
        }

        private static void EnsureDialBand(RadioBand band)
        {
            if (dialRoot != null && dialBand == band) return;
            if (dialRoot != null) UnityEngine.Object.Destroy(dialRoot);
            dialMarkers.Clear();

            dialRoot = new GameObject("Dial", typeof(RectTransform));
            var rootRect = (RectTransform)dialRoot.transform;
            rootRect.SetParent(pageRoot, false);
            rootRect.SetAsLastSibling();
            AvKit.Place(rootRect, new Rect(0f, 0f, Width, 0f));

            dialBand = band;
            int minorStep = band == RadioBand.Fm ? 500 : 100;
            int labelStep = band == RadioBand.Fm ? 2000 : 200;
            int min = band == RadioBand.Fm ? RadioDial.FmMinKilohertz : RadioDial.MwMinKilohertz;
            int max = band == RadioBand.Fm ? RadioDial.FmMaxKilohertz : RadioDial.MwMaxKilohertz;

            AvKit.Rule(rootRect, new Rect(dialArea.x, dialArea.y - 14f, dialArea.width, 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.5f)));

            for (int khz = min; khz <= max; khz += minorStep)
            {
                bool major = (khz - min) % labelStep == 0;
                float x = DialX((khz - min) / (float)(max - min));
                AvKit.Rule(rootRect, new Rect(x, dialArea.y - 14f, 1f, major ? 7f : 4f),
                    AvTheme.Unity(AvTokens.Hairline.WithAlpha(major ? 0.55f : 0.28f)));
                if (!major) continue;

                string label = band == RadioBand.Fm
                    ? (khz / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    : khz.ToString(System.Globalization.CultureInfo.InvariantCulture);
                AvKit.Label(rootRect, label, new Rect(x - 24f, dialArea.y - 1f, 48f, 12f),
                    AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            }

            dialNeedle = AvKit.Rule(rootRect, new Rect(dialArea.x, dialArea.y - 14f, 2f, 16f), AvTheme.Accent);
            dialNeedle.rectTransform.SetAsLastSibling();
            PlaceNeedle(manager == null || !manager.HasChannels ? 0f : manager.TunedDial.Fraction);
        }

        private static void RefreshDialMarkers()
        {
            if (dialRoot == null || manager == null) return;
            for (int i = 0; i < dialMarkers.Count; i++)
                if (dialMarkers[i] != null) UnityEngine.Object.Destroy(dialMarkers[i].gameObject);
            dialMarkers.Clear();
            dialMarkerStations.Clear();

            var rootRect = (RectTransform)dialRoot.transform;
            int min = dialBand == RadioBand.Fm ? RadioDial.FmMinKilohertz : RadioDial.MwMinKilohertz;
            int max = dialBand == RadioBand.Fm ? RadioDial.FmMaxKilohertz : RadioDial.MwMaxKilohertz;

            for (int i = 0; i < manager.ChannelCount; i++)
            {
                RadioDial dial = manager.GetChannelDial(i);
                if (dial.Band != dialBand) continue;
                float x = DialX((dial.Kilohertz - min) / (float)(max - min));
                Image marker = AvKit.Rule(rootRect,
                    new Rect(x - 2f, dialArea.y - 14f, 4f, 9f), manager.GetChannelColor(i));
                dialMarkers.Add(marker);
                dialMarkerStations.Add(i);
            }

            if (dialNeedle != null) dialNeedle.rectTransform.SetAsLastSibling();
            markerRevision = manager.StationRevision;
        }

        private static void PlaceNeedle(float fraction)
        {
            if (dialNeedle == null) return;
            AvKit.Place(dialNeedle.rectTransform,
                new Rect(DialX(fraction) - 1f, dialArea.y - 14f, 2f, 16f));
        }

        private static float DialX(float fraction) =>
            dialArea.x + Mathf.Clamp01(fraction) * (dialArea.width - 1f);

        private static void Animate()
        {
            if (manager == null || dialRoot == null) return;

            RadioDial dial = manager.TunedDial;
            bool off = manager.IsOffStation;
            if (dialBand != dial.Band)
            {
                EnsureDialBand(dial.Band);
                RefreshDialMarkers();
                sweeping = false;
                PlaceNeedle(dial.Fraction);
                shownDial = dial;
                dialInitialized = true;
                nextRefresh = 0f;
            }
            else if (!dial.Equals(shownDial))
            {
                if (dialInitialized)
                {
                    sweeping = true;
                    sweepStart = Time.unscaledTime;
                    sweepFrom = manager.TunedDial.Fraction;
                    sweepTo = dial.Fraction;
                }
                shownDial = dial;
                dialInitialized = true;
                nextRefresh = 0f;
            }

            if (off != shownOffStation)
            {
                shownOffStation = off;
                nextRefresh = 0f;
            }

            if (sweeping)
            {
                float t = (Time.unscaledTime - sweepStart) / SweepSeconds;
                if (t >= 1f)
                {
                    sweeping = false;
                    PlaceNeedle(sweepTo);
                }
                else
                {
                    float eased = t * t * (3f - 2f * t);
                    float fraction = Mathf.Lerp(sweepFrom, sweepTo, eased);
                    PlaceNeedle(fraction);
                    if (frequencyLabel != null && dialBand == RadioBand.Fm)
                        frequencyLabel.text = SweepFrequencyText(fraction);
                }
            }

            if (Time.unscaledTime < nextMeterAt) return;
            nextMeterAt = Time.unscaledTime + 0.1f;
            UpdateMeter();
        }

        private static string SweepFrequencyText(float fraction)
        {
            int min = RadioDial.FmMinKilohertz;
            int span = RadioDial.FmMaxKilohertz - min;
            int khz = min + Mathf.RoundToInt(fraction * span / RadioDial.FmStepKilohertz) * RadioDial.FmStepKilohertz;
            return (khz / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void UpdateMeter()
        {
            if (manager == null) return;
            float level;
            string signal;
            string token;
            string state = null;

            if (!manager.HasChannels)
            {
                level = 0f;
                signal = "NO SIG";
                token = "NO SIG";
            }
            else if (manager.IsScanning || sweeping)
            {
                level = 0.25f + Mathf.PerlinNoise(Time.unscaledTime * 2.5f, 0.37f) * 0.35f;
                signal = "SEEKING";
                token = "SEEK";
                state = "warn";
            }
            else if (manager.IsOffStation)
            {
                level = 0.1f + Mathf.PerlinNoise(Time.unscaledTime * 3.5f, 0.71f) * 0.2f;
                signal = "NO SIGNAL";
                token = "NO SIG";
                state = "warn";
            }
            else if (manager.IsEngaged && !manager.IsPaused)
            {
                level = Mathf.Max(0.2f, manager.SignalLevel);
                signal = "LOCK " + Mathf.RoundToInt(level * 100f) + "%";
                token = "LOCK";
                state = "live";
            }
            else if (manager.IsEngaged)
            {
                level = 0.55f;
                signal = "CARRIER";
                token = "CARRIER";
            }
            else
            {
                level = 0f;
                signal = "NO SIG";
                token = "NO SIG";
            }

            int lit = Mathf.RoundToInt(Mathf.Clamp01(level) * MeterPips);
            for (int i = 0; i < meterPips.Length; i++)
            {
                if (meterPips[i] == null) continue;
                bool on = i < lit;
                float falloff = on ? 1f - i / (float)MeterPips * 0.35f : 0f;
                meterPips[i].color = on
                    ? new Color(AvTheme.Accent.r * falloff, AvTheme.Accent.g * falloff, AvTheme.Accent.b * falloff)
                    : AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.25f));
            }

            if (signalLabel != null)
            {
                signalLabel.text = signal;
                signalLabel.color = state == "live" ? AvTheme.Accent
                    : state == "warn" ? AvTheme.Warning : AvTheme.Dim;
            }
            if (dataBar != null) dataBar.SetChip(2, token, state ?? "inert");
        }

        private static void SectionTitle(RectTransform parent, float x, float width, float y, string title)
        {
            TMP_Text label = AvStyled.Label(parent, new Rect(x, y, 160f, HeaderHeight), title, "section-title");
            float titleWidth = Mathf.Ceil(label.GetPreferredValues(title).x);
            AvKit.Rule(parent, new Rect(x + titleWidth + AvTokens.Space2, y - 7f,
                Mathf.Max(0f, width - titleWidth - AvTokens.Space2), 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));
        }

        private static ChannelRow MakeChannelRow(RectTransform parent, int row, float y)
        {
            float x = Pad;
            float w = Width - Pad * 2f;

            Image ground = AvKit.Panel(parent, new Rect(x, y, w, RowHeight), Color.clear);
            RectTransform rect = ground.rectTransform;
            AvKit.Rule(rect, new Rect(0f, -RowHeight, w, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
            Image selectionRule = AvKit.Rule(rect, new Rect(0f, 0f, 3f, RowHeight), Color.clear);

            var result = new ChannelRow
            {
                Root = ground.gameObject,
                Ground = ground,
                SelectionRule = selectionRule
            };

            result.BadgeGround = AvKit.Panel(rect, new Rect(10f, -4f, 26f, 22f), AvTheme.SurfaceInert);
            result.Icon = AvKit.Panel(result.BadgeGround.rectTransform, new Rect(1f, -1f, 24f, 24f), Color.white);
            result.Icon.preserveAspect = true;
            result.Icon.enabled = false;
            result.Badge = AvKit.Label(result.BadgeGround.rectTransform, "--", new Rect(0f, 0f, 26f, 22f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            result.Label = AvStyled.Label(rect, new Rect(44f, 0f, w - 44f - 158f, RowHeight), "LOCAL", "row-name");
            result.Frequency = AvStyled.Label(rect, new Rect(w - 150f, 0f, 84f, RowHeight), "--", "row-value");
            result.Count = AvStyled.Label(rect, new Rect(w - 60f, 0f, 56f, RowHeight), "0", "row-sub",
                                          align: TextAlignmentOptions.Right);

            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, w, RowHeight), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && result.Index >= 0) manager.SelectChannel(result.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear,
                AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised));
            button.WithTooltip("Tune the receiver to this station.");
            return result;
        }

        private static ProgrammeRow MakeProgrammeRow(RectTransform parent, int row, float y)
        {
            float x = Pad;
            float w = Width - Pad * 2f;

            Image ground = AvKit.Panel(parent, new Rect(x, y, w, TrackLineHeight), Color.clear);
            RectTransform rect = ground.rectTransform;
            Image rule = AvKit.Rule(rect, new Rect(0f, 0f, 3f, TrackLineHeight), Color.clear);

            var result = new ProgrammeRow
            {
                Root = ground.gameObject,
                Ground = ground,
                Rule = rule
            };

            result.Number = AvKit.Label(rect, "1", new Rect(8f, 0f, 26f, TrackLineHeight),
                AvTheme.Disabled, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            result.Title = AvStyled.Label(rect, new Rect(40f, 0f, w - 48f, TrackLineHeight), "", "row-sub");

            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, w, TrackLineHeight), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && result.Index >= 0) manager.PlayTrack(result.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear, AvTheme.Unity(AvTokens.Wash(
                AvTheme.Accent.ToRgba(), AvTokens.RowHoverScale, AvTokens.RowHoverAlpha)));
            button.WithTooltip("Play this track from the tuned station.");
            return result;
        }

        private static void NudgeProgrammePage(int direction)
        {
            if (manager == null) return;
            int perPage = Math.Max(1, programmeRowsPerPage);
            int pages = Math.Max(1, (manager.TrackCount + perPage - 1) / perPage);
            programmePage = Mathf.Clamp(programmePage + direction, 0, pages - 1);
            nextRefresh = 0f;
        }

        private static void TogglePresetArming()
        {
            presetArmed = !presetArmed;
            setButton?.SetLatched(presetArmed);
            nextRefresh = 0f;
        }

        private static void OnPreset(int slot)
        {
            if (manager == null) return;
            if (presetArmed)
            {
                manager.StorePreset(slot);
                presetArmed = false;
                setButton?.SetLatched(false);
            }
            else
            {
                manager.ApplyPreset(slot);
            }
            nextRefresh = 0f;
        }

        private static void Refresh()
        {
            if (manager == null || frequencyLabel == null) return;
            if (iconRevision != manager.StationRevision)
            {
                RadioStationIconCache.Clear();
                iconRevision = manager.StationRevision;
            }
            if (markerRevision != manager.StationRevision) RefreshDialMarkers();

            bool hasChannels = manager.HasChannels;
            bool offStation = manager.IsOffStation;
            RadioDial dial = manager.TunedDial;
            if (dialBand != dial.Band)
            {
                EnsureDialBand(dial.Band);
                RefreshDialMarkers();
                PlaceNeedle(dial.Fraction);
            }

            if (hasChannels && !offStation)
            {
                frequencyLabel.text = dial.FrequencyText;
                unitLabel.text = dial.UnitText;
                bandLabel.text = dial.BandText + " · " + manager.BroadcastMode;
                stationNameLabel.text = manager.CurrentChannelName;
                wireLabel.text = manager.TickerText;
                programLabel.text = manager.CurrentProgram;
                ApplyStationIcon(
                    stationIcon, stationBadge, manager.GetChannelIconPath(manager.SelectedChannel),
                    manager.CurrentChannelCode);
                stationsNote.text = manager.ChannelCount + " STATIONS";
            }
            else if (hasChannels)
            {
                frequencyLabel.text = dial.FrequencyText;
                unitLabel.text = dial.UnitText;
                bandLabel.text = dial.BandText + " · " + manager.BroadcastMode;
                stationNameLabel.text = "NO SIGNAL";
                wireLabel.text = "Dead air. Tune back to a station.";
                programLabel.text = string.Empty;
                stationBadge.text = "--";
                stationBadge.gameObject.SetActive(true);
                stationIcon.enabled = false;
                stationsNote.text = manager.ChannelCount + " STATIONS";
            }
            else
            {
                frequencyLabel.text = "---.-";
                unitLabel.text = "MHz";
                bandLabel.text = "FM · --";
                stationNameLabel.text = "NO STATIONS";
                wireLabel.text = "Add music folders, then press RESCAN.";
                programLabel.text = string.Empty;
                stationBadge.text = "--";
                stationBadge.gameObject.SetActive(true);
                stationIcon.enabled = false;
                stationsNote.text = "0 FOUND";
            }

            trackLabel.text = offStation ? "DEAD AIR" : manager.CurrentTrackTitle;
            progressFill.fillAmount = offStation ? 0f : manager.Progress;
            timeLabel.text = offStation ? "--:-- / --:--"
                : FormatTime(manager.Elapsed) + " / " + FormatTime(manager.Duration);
            playButton?.SetText(manager.IsPaused ? "RESUME" : manager.IsEngaged ? "PAUSE" : "PLAY");
            shuffleButton?.SetLatched(manager.Shuffle);
            repeatButton?.SetLatched(manager.RepeatTrack);
            scanButton?.SetLatched(manager.IsScanning);
            int selectedTracks = hasChannels ? manager.GetChannelTrackCount(manager.SelectedChannel) : 0;
            bool selectedChannelHasTracks = selectedTracks > 0;
            seekDownButton?.SetEnabled(hasChannels);
            seekUpButton?.SetEnabled(hasChannels);
            tuneDownButton?.SetEnabled(hasChannels);
            tuneUpButton?.SetEnabled(hasChannels);
            bandButton?.SetEnabled(hasChannels);
            playButton?.SetEnabled(selectedChannelHasTracks && !offStation);
            stopButton?.SetEnabled(manager.IsEngaged);
            scanButton?.SetEnabled(manager.ChannelCount > 1);
            volumeLabel.text = Mathf.RoundToInt(manager.VolumeLevel * 100f) + "%";
            shell?.WriteStatus(null, MapPicker.Prompt, manager.Status);

            int pages = Math.Max(1, (manager.ChannelCount + rowsPerPage - 1) / rowsPerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageLabel.text = (page + 1) + " / " + pages;
            pagePreviousButton?.SetEnabled(page > 0);
            pageNextButton?.SetEnabled(page + 1 < pages);

            if (dataBar != null)
            {
                bool playing = manager.IsEngaged && !manager.IsPaused;
                dataBar.State.text = manager.IsScanning ? "SCANNING"
                                    : offStation ? "NO SIGNAL"
                                    : playing ? "ON AIR"
                                    : manager.IsPaused ? "PAUSED"
                                    : hasChannels ? "STANDBY" : "NO LIBRARY";
                dataBar.State.color = manager.IsScanning || offStation ? AvTheme.Warning
                                    : playing ? AvTheme.RailReady
                                    : hasChannels ? AvTheme.Dim : AvTheme.Warning;
                dataBar.SetChip(0, hasChannels ? manager.TunedDial.FullText : "-- MHz", hasChannels ? "live" : "inert");
                dataBar.SetChip(1, hasChannels ? manager.BroadcastMode : "--", hasChannels ? "info" : "inert");
                UpdateMeter();
            }

            if (channelsEmptyLabel != null)
            {
                if (channelsEmptyLabel.gameObject.activeSelf != !hasChannels)
                    channelsEmptyLabel.gameObject.SetActive(!hasChannels);
                if (!hasChannels)
                    channelsEmptyLabel.text =
                        "No music folders found. Press FOLDER to open the library, add " +
                        "subfolders of audio, then press RESCAN.";
            }

            for (int row = 0; row < rowsPerPage; row++)
            {
                int index = page * rowsPerPage + row;
                ChannelRow item = rows[row];
                if (item == null) continue;
                item.Index = index < manager.ChannelCount ? index : -1;
                item.Root.SetActive(item.Index >= 0);
                if (item.Index < 0) continue;
                item.Badge.text = manager.GetChannelCode(index);
                ApplyStationIcon(
                    item.Icon, item.Badge, manager.GetChannelIconPath(index),
                    manager.GetChannelCode(index));
                Color stationColor = manager.GetChannelColor(index);
                item.BadgeGround.color = AvTheme.Unity(AvTokens.Wash(
                    stationColor.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha));
                RadioDial rowDial = manager.GetChannelDial(index);
                item.Label.text = manager.GetChannelName(index);
                item.Frequency.text = rowDial.FrequencyText + " " + rowDial.BandText;
                item.Count.text = manager.GetChannelTrackCount(index) + " TRK";
                bool selected = index == manager.SelectedChannel && !offStation;
                item.Label.color = AvTheme.TextPrimary;
                item.Frequency.color = selected ? AvTheme.Accent : AvTheme.Unity(AvTokens.TextDim);
                item.SelectionRule.color = selected ? AvTheme.Accent : Color.clear;
                item.Ground.color = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear;
            }

            for (int slot = 0; slot < presetButtons.Length; slot++)
            {
                if (presetButtons[slot] == null) continue;
                int stored = manager.GetPreset(slot);
                bool valid = stored >= 0 && stored < manager.ChannelCount;
                presetButtons[slot].SetText(valid ? (slot + 1) + " " + manager.GetChannelCode(stored) : (slot + 1).ToString());
                presetButtons[slot].SetLatched(valid && stored == manager.SelectedChannel && !offStation);
            }

            if (programmeNote != null)
            {
                int trackCount = manager.TrackCount;
                int current = manager.CurrentTrackIndex;
                if (current != lastProgrammeTrack)
                {
                    lastProgrammeTrack = current;
                    if (current >= 0 && programmeRowsPerPage > 0)
                        programmePage = current / programmeRowsPerPage;
                }
                int perPage = Math.Max(1, programmeRowsPerPage);
                int trackPages = Math.Max(1, (trackCount + perPage - 1) / perPage);
                programmePage = Mathf.Clamp(programmePage, 0, trackPages - 1);
                programmeNote.text = trackCount == 0
                    ? "NO LOG"
                    : (programmePage + 1) + " / " + trackPages;
                programmePreviousButton?.SetEnabled(programmePage > 0);
                programmeNextButton?.SetEnabled(programmePage + 1 < trackPages);

                if (programmeEmptyLabel != null)
                {
                    bool empty = trackCount == 0;
                    if (programmeEmptyLabel.gameObject.activeSelf != empty)
                        programmeEmptyLabel.gameObject.SetActive(empty);
                    if (empty)
                        programmeEmptyLabel.text = offStation
                            ? "Tune to a station for its programme log."
                            : "This station carries no local tracks.";
                }

                for (int i = 0; i < programmeRows.Length; i++)
                {
                    ProgrammeRow track = programmeRows[i];
                    if (track == null) continue;
                    int index = programmePage * perPage + i;
                    track.Index = index < trackCount ? index : -1;
                    if (track.Root.activeSelf != (track.Index >= 0))
                        track.Root.SetActive(track.Index >= 0);
                    if (track.Index < 0) continue;
                    bool playing = index == current;
                    track.Number.text = (index + 1).ToString();
                    track.Title.text = manager.GetTrackTitle(index);
                    track.Number.color = playing ? AvTheme.Accent : AvTheme.Disabled;
                    track.Title.color = playing ? AvTheme.Accent : AvTheme.Unity(AvTokens.TextDim);
                    track.Rule.color = playing ? AvTheme.Accent : Color.clear;
                }
            }

            for (int i = 0; i < dialMarkers.Count; i++)
            {
                if (dialMarkers[i] == null) continue;
                int station = dialMarkerStations[i];
                bool tuned = station == manager.SelectedChannel;
                dialMarkers[i].rectTransform.sizeDelta = new Vector2(tuned ? 5f : 4f, tuned ? 12f : 9f);
                dialMarkers[i].color = tuned ? AvTheme.Accent : manager.GetChannelColor(station);
            }
        }

        private static void ApplyStationIcon(
            Image image, TMP_Text fallback, string path, string fallbackText)
        {
            Sprite sprite = RadioStationIconCache.Get(path);
            bool available = sprite != null;
            image.sprite = sprite;
            image.enabled = available;
            fallback.text = fallbackText;
            fallback.gameObject.SetActive(!available);
        }

        private static void PreviousPage()
        {
            if (page > 0) page--;
            nextRefresh = 0f;
        }

        private static void NextPage()
        {
            int pages = manager == null ? 1 : Math.Max(1,
                (manager.ChannelCount + rowsPerPage - 1) / rowsPerPage);
            if (page + 1 < pages) page++;
            nextRefresh = 0f;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        private static string FormatTime(float seconds)
        {
            int value = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (value / 60).ToString("00") + ":" + (value % 60).ToString("00");
        }

        private static void Fail(string reason)
        {
            gaveUp = true;
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            shell = null;
            dataBar = null;
            Plugin.Logger.LogWarning("Radio panel disabled (" + reason + "). Playback remains available through config reload only.");
        }
    }
}
