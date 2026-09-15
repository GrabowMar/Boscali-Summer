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
    /// <summary>
    /// The receiver: a tuner you fly with, not a music browser. A spectrum waterfall, an
    /// S-meter fed by a link budget, squelch, bandwidth and mode keys, and the two lists a
    /// radio operator actually reads — the stations on the dial and the tuned programme.
    ///
    /// <para>The deck lives on its own screen (<see cref="MusicPanel"/>); everything here
    /// assumes the player is operating a set.</para>
    /// </summary>
    internal static class RadioPanel
    {
        private const float Width = AvTokens.PanelWidth;
        private const float Pad = AvTokens.Pad;
        private const float Gap = AvTokens.Gap;
        private const float HeroHeight = 130f;
        private const float WaterfallHeight = 62f;
        private const float DialBlock = 48f;
        private const float ControlHeight = 32f;
        private const float KeyHeight = 30f;
        private const float HeaderHeight = 16f;
        private const float RowHeight = AvTokens.RowHeight;
        private const float ChannelPitch = RowHeight + 2f;
        private const float TrackLinePitch = 15f;
        private const float TrackLineHeight = 14f;
        private const float ContentHeight = 720f;
        private const float SweepSeconds = 0.45f;
        private const int MeterPips = 12;
        private const int MaximumRows = 4;
        private const int MinimumRows = 3;
        private const int MaximumTrackRows = 5;

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
        private static readonly List<Image> dialMarkers = new List<Image>();
        private static readonly List<int> dialMarkerStations = new List<int>();

        private static MFDScreen screen;
        private static GameObject screenRoot;
        private static AvScreen shell;
        private static RadioManager manager;
        private static TMP_Text frequencyLabel;
        private static TMP_Text unitLabel;
        private static TMP_Text modeLabel;
        private static TMP_Text signalLabel;
        private static TMP_Text stationNameLabel;
        private static TMP_Text wireLabel;
        private static TMP_Text programLabel;
        private static TMP_Text trackLabel;
        private static TMP_Text timeLabel;
        private static TMP_Text stationsNote;
        private static TMP_Text pageLabel;
        private static TMP_Text programmeNote;
        private static TMP_Text programmeEmptyLabel;
        private static TMP_Text spectrumNote;
        private static TMP_Text squelchLabel;
        private static TMP_Text footerLabel;
        private static Image stationIconGround;
        private static Image stationIcon;
        private static TMP_Text stationBadge;
        private static AvButton programmePreviousButton;
        private static AvButton programmeNextButton;
        private static AvButton pagePreviousButton;
        private static AvButton pageNextButton;
        private static AvButton seekDownButton;
        private static AvButton tuneDownButton;
        private static AvButton powerButton;
        private static AvButton tuneUpButton;
        private static AvButton seekUpButton;
        private static AvButton stopButton;
        private static AvButton scanButton;
        private static AvButton bandButton;
        private static AvButton modeButton;
        private static AvButton bandwidthButton;
        private static AvButton stepButton;
        private static AvButton squelchDownButton;
        private static AvButton squelchUpButton;
        private static AvButton transmitButton;
        private static AvButton secureButton;
        private static AvStyled.DataBar dataBar;
        private static TMP_Text channelsEmptyLabel;
        private static Image progressFill;
        private static GameObject dialRoot;
        private static RectTransform pageRoot;
        private static Rect dialArea;
        private static RadioBand dialBand;
        private static Image dialNeedle;
        private static RadioWaterfall waterfall;
        private static Image waterfallNeedle;
        private static Rect waterfallArea;
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
        private static float nextMeterAt;
        private static float nextWaterfallAt;
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
            waterfall?.Dispose();
            waterfall = null;
            waterfallNeedle = null;
            screenRoot = null;
            screen = null;
            shell = null;
            manager = null;
            frequencyLabel = null;
            unitLabel = null;
            modeLabel = null;
            signalLabel = null;
            stationNameLabel = null;
            wireLabel = null;
            programLabel = null;
            stationIconGround = null;
            stationIcon = null;
            stationBadge = null;
            trackLabel = null;
            timeLabel = null;
            stationsNote = null;
            pageLabel = null;
            programmeNote = null;
            programmeEmptyLabel = null;
            spectrumNote = null;
            squelchLabel = null;
            footerLabel = null;
            pagePreviousButton = null;
            pageNextButton = null;
            dataBar = null;
            channelsEmptyLabel = null;
            progressFill = null;
            seekDownButton = null;
            tuneDownButton = null;
            powerButton = null;
            tuneUpButton = null;
            seekUpButton = null;
            stopButton = null;
            scanButton = null;
            bandButton = null;
            modeButton = null;
            bandwidthButton = null;
            stepButton = null;
            squelchDownButton = null;
            squelchUpButton = null;
            transmitButton = null;
            secureButton = null;
            dialRoot = null;
            dialNeedle = null;
            pageRoot = null;
            programmePreviousButton = null;
            programmeNextButton = null;
            dialMarkers.Clear();
            dialMarkerStations.Clear();
            dialBand = RadioBand.Fm;
            for (int i = 0; i < programmeRows.Length; i++) programmeRows[i] = null;
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
            nextMeterAt = 0f;
            nextWaterfallAt = 0f;
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

                if (!RadioScreens.TryBuild(template, buttons[slot], MfdSlots.Rad, "BoscaliRadio.Screen",
                    ContentHeight, () => nextRefresh = 0f, out RadioScreen built))
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    Fail("bezel label or highlight missing");
                    return;
                }

                screen = built.Screen;
                screenRoot = built.Root;
                shell = built.Shell;
                pageRoot = built.Page;
                dataBar = shell.DataBar;

                var cursor = new RadioCursor(built.Area);
                BuildHero(cursor.Take(HeroHeight));
                BuildSpectrum(ref cursor);
                BuildDial(ref cursor);
                BuildControls(ref cursor);
                BuildStationList(ref cursor);
                BuildProgrammeLog(ref cursor);
                BuildFooter(ref cursor);

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    shell = null;
                    Fail("claimed bezel changed before binding");
                    return;
                }

                Refresh();
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

        private static void BuildHero(Rect area)
        {
            AvKit.TacticalCard(pageRoot, area, AvTheme.RailReady);

            stationIconGround = AvKit.Panel(pageRoot,
                new Rect(area.x + AvTokens.Space2, area.y - AvTokens.Space2, 56f, 56f),
                AvTheme.SurfaceInert, AvSprites.Card);
            AvKit.Outline(pageRoot,
                new Rect(area.x + AvTokens.Space2, area.y - AvTokens.Space2, 56f, 56f), AvTheme.Frame);
            stationIcon = AvKit.Panel(stationIconGround.rectTransform,
                new Rect(AvTokens.Space1 + 1f, -(AvTokens.Space1 + 1f), 48f, 48f), Color.white);
            stationIcon.preserveAspect = true;
            stationIcon.enabled = false;
            stationBadge = AvKit.Label(stationIconGround.rectTransform, "--",
                new Rect(0f, 0f, 56f, 56f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.Center);

            float infoX = area.x + AvTokens.Space2 + 56f + AvTokens.Space3;
            float infoWidth = area.width - (infoX - area.x) - 186f;
            frequencyLabel = AvKit.Label(pageRoot, "---.-",
                new Rect(infoX, area.y - AvTokens.Space1, 150f, 40f),
                AvTheme.TextPrimary, 32f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            unitLabel = AvKit.Label(pageRoot, "MHz",
                new Rect(infoX + 140f, area.y - AvTokens.Space5, 52f, 16f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            modeLabel = AvStyled.Label(pageRoot,
                new Rect(area.x + area.width - AvTokens.Space2 - 160f, area.y - AvTokens.Space2, 160f, 14f),
                "FM · STEREO", "section-title", align: TextAlignmentOptions.Right);

            float meterX = area.x + area.width - AvTokens.Space2 - MeterPips * 9f;
            for (int i = 0; i < MeterPips; i++)
            {
                meterPips[i] = AvKit.Rule(pageRoot,
                    new Rect(meterX + i * 9f, area.y - 32f, 6f, 8f), AvTheme.Hairline);
            }
            signalLabel = AvStyled.Label(pageRoot,
                new Rect(area.x + area.width - AvTokens.Space2 - 160f, area.y - 46f, 160f, 12f),
                "S0", "section-title-note", align: TextAlignmentOptions.Right);

            stationNameLabel = AvStyled.Label(pageRoot,
                new Rect(infoX, area.y - 58f, infoWidth, 16f),
                "NO STATIONS", "row-name");
            wireLabel = AvKit.Label(pageRoot, "",
                new Rect(infoX, area.y - 72f, infoWidth, 14f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            programLabel = AvStyled.Label(pageRoot,
                new Rect(area.x + area.width - AvTokens.Space2 - 180f, area.y - 70f, 180f, 12f),
                "", "section-title-note", align: TextAlignmentOptions.Right);

            trackLabel = AvStyled.Label(pageRoot,
                new Rect(area.x + AvTokens.Space2, area.y - 90f, area.width - AvTokens.Space4 - 84f, 16f),
                "NO LOCAL TRACKS", "row-main");
            timeLabel = AvKit.Label(pageRoot, "00:00 / 00:00",
                new Rect(area.x + area.width - AvTokens.Space2 - 84f, area.y - 90f, 84f, 16f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            squelchLabel = AvStyled.Label(pageRoot,
                new Rect(area.x + AvTokens.Space2, area.y - 108f, area.width - AvTokens.Space4 - 84f, 12f),
                "SQL 15 · WIDE · STEP 100k", "section-title-note");

            progressFill = AvKit.ProgressBar(pageRoot,
                new Rect(area.x + AvTokens.Space2, area.y - 122f, area.width - AvTokens.Space4, 4f),
                0f, AvTheme.Accent);
        }

        private static void BuildSpectrum(ref RadioCursor cursor)
        {
            Rect header = cursor.Take(HeaderHeight, 2f);
            SectionTitle(pageRoot, header.x, header.width, header.y, "SPECTRUM");
            spectrumNote = AvStyled.Label(pageRoot,
                new Rect(header.x, header.y, header.width, HeaderHeight),
                "", "section-title-note", align: TextAlignmentOptions.Right);

            waterfallArea = cursor.Take(WaterfallHeight, AvTokens.Space2);
            waterfall = new RadioWaterfall(pageRoot, waterfallArea, RadioSpectrum.DefaultBins, 48);
            waterfallNeedle = AvKit.Rule(pageRoot,
                new Rect(waterfallArea.x + 1f, waterfallArea.y - 1f, 2f, waterfallArea.height - 2f),
                AvTheme.Accent);
            waterfallNeedle.rectTransform.SetAsLastSibling();
        }

        private static void BuildDial(ref RadioCursor cursor)
        {
            // The ticks own the left of the strip; the volume keys reset the right of it.
            dialArea = new Rect(cursor.X, cursor.Y, cursor.Width - 94f, DialBlock);
            float volumeX = cursor.X + cursor.Width - 90f;
            float volumeY = cursor.Y - 30f;
            AvKit.Button(pageRoot, "-", new Rect(volumeX, volumeY, 22f, 18f),
                () => manager?.NudgeVolume(-0.1f), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Turn the receiver volume down.");
            AvKit.Label(pageRoot, "AF", new Rect(volumeX + 24f, volumeY, 42f, 18f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            AvKit.Button(pageRoot, "+", new Rect(volumeX + 68f, volumeY, 22f, 18f),
                () => manager?.NudgeVolume(0.1f), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Turn the receiver volume up.");
            cursor.Skip(DialBlock + AvTokens.Space2);
            EnsureDialBand(manager == null ? RadioBand.Fm : manager.TunedDial.Band);
        }

        private static void BuildControls(ref RadioCursor cursor)
        {
            Rect seekRow = cursor.Take(ControlHeight, 6f);
            float keyWidth = (seekRow.width - Gap * 5f) / 6f;
            float x = seekRow.x;
            seekDownButton = AvKit.Button(pageRoot, "SEEK<", new Rect(x, seekRow.y, keyWidth, ControlHeight),
                () => manager?.SeekStation(-1), AvTokens.FontMicro, AvButtonStyle.Default)
                .WithTooltip("Seek the next station down the band.");
            tuneDownButton = AvKit.Button(pageRoot, "TUNE<", new Rect(x += keyWidth + Gap, seekRow.y, keyWidth, ControlHeight),
                () => manager?.StepDial(-1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Tune one step down. Between channels is dead air.");
            powerButton = AvKit.Button(pageRoot, "PWR", new Rect(x += keyWidth + Gap, seekRow.y, keyWidth, ControlHeight),
                () => manager?.TogglePlayback(), AvTokens.FontMicro, AvButtonStyle.Primary)
                .WithTooltip("Monitor or pause the tuned station's programme.");
            tuneUpButton = AvKit.Button(pageRoot, "TUNE>", new Rect(x += keyWidth + Gap, seekRow.y, keyWidth, ControlHeight),
                () => manager?.StepDial(1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Tune one step up. Between channels is dead air.");
            seekUpButton = AvKit.Button(pageRoot, "SEEK>", new Rect(x += keyWidth + Gap, seekRow.y, keyWidth, ControlHeight),
                () => manager?.SeekStation(1), AvTokens.FontMicro, AvButtonStyle.Default)
                .WithTooltip("Seek the next station up the band.");
            scanButton = AvKit.Button(pageRoot, "SCAN", new Rect(x += keyWidth + Gap, seekRow.y, keyWidth, ControlHeight),
                () => manager?.ToggleScan(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Hold each station in the band for a few seconds as it seeks.");

            Rect modeRow = cursor.Take(KeyHeight, AvTokens.Space2);
            float modeWidth = (modeRow.width - Gap * 8f) / 9f;
            x = modeRow.x;
            bandButton = AvKit.Button(pageRoot, "BAND", new Rect(x, modeRow.y, modeWidth, KeyHeight),
                () => manager?.CycleBand(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("FM broadcast, VHF air band, then MW. Each band keeps its last frequency.");
            modeButton = AvKit.Button(pageRoot, "MODE", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.CycleMode(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Auto follows the band. A forced mode on the wrong kind of station garbles it.");
            bandwidthButton = AvKit.Button(pageRoot, "BW", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.ToggleBandwidth(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Wide or narrow passband. Narrow trades audio quality for less noise.");
            stepButton = AvKit.Button(pageRoot, "STEP", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.ToggleFineTuning(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Channel step or a five-times finer tuning step.");
            squelchDownButton = AvKit.Button(pageRoot, "SQ<", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.NudgeSquelch(-0.05f), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Lower the squelch: a weaker station can open the audio.");
            squelchUpButton = AvKit.Button(pageRoot, "SQ>", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.NudgeSquelch(0.05f), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Raise the squelch: only a stronger station opens the audio.");
            transmitButton = AvKit.Button(pageRoot, "TX", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.Transmit(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Transmit key. Voice is not wired yet: this build is receive-only.");
            secureButton = AvKit.Button(pageRoot, "SEC", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.ToggleSecure(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Crypto switch. A placeholder: no channel or key leaves this machine.");
            stopButton = AvKit.Button(pageRoot, "STOP", new Rect(x += modeWidth + Gap, modeRow.y, modeWidth, KeyHeight),
                () => manager?.Stop(), AvTokens.FontMicro, AvButtonStyle.Danger)
                .WithTooltip("Sign off and hand the soundtrack bus back to the game.");
        }

        private static void BuildStationList(ref RadioCursor cursor)
        {
            Rect header = cursor.Take(HeaderHeight, 6f);
            SectionTitle(pageRoot, header.x, header.width, header.y, "STATIONS");
            stationsNote = AvStyled.Label(pageRoot, new Rect(header.x, header.y, header.width - 64f, HeaderHeight),
                "0 FOUND", "section-title-note", align: TextAlignmentOptions.Right);
            AvKit.Button(pageRoot, "RESCAN",
                new Rect(header.x + header.width - 60f, header.y - 2f, 60f, 16f),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Sign off and rescan the local music library.");

            rowsPerPage = MaximumRows;
            float listTop = cursor.Y;
            Rect listFrame = new Rect(cursor.X, listTop, cursor.Width, ChannelPitch);
            channelsEmptyLabel = AvKit.Label(pageRoot, "",
                new Rect(cursor.X + AvTokens.Space4, listTop, cursor.Width - AvTokens.Space4 * 2f,
                    ChannelPitch * MinimumRows),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Italic, TextAlignmentOptions.Center, wrap: true);
            channelsEmptyLabel.gameObject.SetActive(false);

            for (int i = 0; i < rowsPerPage; i++)
                rows[i] = MakeChannelRow(pageRoot, listFrame, listTop - i * ChannelPitch);
            cursor.Skip(ChannelPitch * rowsPerPage + AvTokens.Space2);

            AvButton[] pageButtons = AvKit.Stepper(
                pageRoot, cursor.X, cursor.Y, cursor.Width, out pageLabel,
                PreviousPage, NextPage);
            pagePreviousButton = pageButtons[0];
            pageNextButton = pageButtons[1];
            pagePreviousButton.WithTooltip("Show the previous page of stations. Disabled on the first page.");
            pageNextButton.WithTooltip("Show the next page of stations. Disabled on the last page.");
            cursor.Skip(RowHeight + AvTokens.Space2);
        }

        private static void BuildProgrammeLog(ref RadioCursor cursor)
        {
            Rect header = cursor.Take(HeaderHeight, 6f);
            SectionTitle(pageRoot, header.x, header.width, header.y, "PROGRAMME");
            programmePreviousButton = AvKit.Button(pageRoot, "<",
                new Rect(header.x + header.width - 88f, header.y - 1f, 22f, 16f),
                () => NudgeProgrammePage(-1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Show the previous page of tracks.");
            programmeNote = AvStyled.Label(pageRoot,
                new Rect(header.x + header.width - 64f, header.y, 40f, HeaderHeight),
                "1 / 1", "section-title-note", align: TextAlignmentOptions.Center);
            programmeNextButton = AvKit.Button(pageRoot, ">",
                new Rect(header.x + header.width - 22f, header.y - 1f, 22f, 16f),
                () => NudgeProgrammePage(1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Show the next page of tracks.");

            programmeRowsPerPage = MaximumTrackRows;
            float rowsTop = cursor.Y;
            Rect listFrame = new Rect(cursor.X, rowsTop, cursor.Width, TrackLinePitch);
            for (int i = 0; i < programmeRowsPerPage; i++)
                programmeRows[i] = MakeProgrammeRow(pageRoot, listFrame, rowsTop - i * TrackLinePitch);

            programmeEmptyLabel = AvKit.Label(pageRoot, "",
                new Rect(cursor.X, rowsTop, cursor.Width, TrackLineHeight),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Italic, TextAlignmentOptions.Left);
            programmeEmptyLabel.gameObject.SetActive(false);
            cursor.Skip(TrackLinePitch * programmeRowsPerPage + AvTokens.Space2);
        }

        private static void BuildFooter(ref RadioCursor cursor)
        {
            Rect area = cursor.Take(AvTokens.Space6);
            footerLabel = AvKit.Label(pageRoot,
                "RECEIVE ONLY · NO TRANSMIT · RECEPTION MODELLED FROM RANGE, HORIZON AND TERRAIN",
                new Rect(area.x, area.y, area.width, 20f),
                AvTheme.Disabled, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
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
            int min = RadioBands.Min(band);
            int max = RadioBands.Max(band);
            int step = RadioBands.Step(band);
            int minorStep = Mathf.Max(step, (max - min) / 60 / step * step);
            int labelStep = band == RadioBand.Fm ? 2000 : band == RadioBand.Air ? 2000 : 200;

            AvKit.Rule(rootRect, new Rect(dialArea.x, dialArea.y - 14f, dialArea.width, 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.5f)));

            for (int khz = min; khz <= max; khz += minorStep)
            {
                bool major = (khz - min) % labelStep == 0;
                float x = DialX((khz - min) / (float)(max - min));
                AvKit.Rule(rootRect, new Rect(x, dialArea.y - 14f, 1f, major ? 7f : 4f),
                    AvTheme.Unity(AvTokens.Hairline.WithAlpha(major ? 0.55f : 0.28f)));
                if (!major) continue;

                AvKit.Label(rootRect, RadioBands.Format(band, khz),
                    new Rect(x - 27f, dialArea.y - 33f, 54f, 12f),
                    AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            }

            dialNeedle = AvKit.Rule(rootRect, new Rect(dialArea.x, dialArea.y - 14f, 2f, 16f), AvTheme.Accent);
            dialNeedle.rectTransform.SetAsLastSibling();
            PlaceNeedle(manager == null || !manager.HasChannels ? 0f : manager.TunedDial.Fraction);
            RefreshDialMarkers();
        }

        private static void RefreshDialMarkers()
        {
            if (dialRoot == null || manager == null) return;
            for (int i = 0; i < dialMarkers.Count; i++)
                if (dialMarkers[i] != null) UnityEngine.Object.Destroy(dialMarkers[i].gameObject);
            dialMarkers.Clear();
            dialMarkerStations.Clear();

            var rootRect = (RectTransform)dialRoot.transform;
            int min = RadioBands.Min(dialBand);
            int max = RadioBands.Max(dialBand);

            for (int i = 0; i < manager.ChannelCount; i++)
            {
                RadioDial dial = manager.GetChannelDial(i);
                if (dial.Band != dialBand) continue;
                float x = DialX((dial.Kilohertz - min) / (float)(max - min));
                float strength = Mathf.Clamp01(manager.GetChannelStrength(i));
                Color marker = Color.Lerp(AvTheme.Unity(AvTokens.RailInert), manager.GetChannelColor(i),
                    0.35f + 0.65f * strength);
                Image rule = AvKit.Rule(rootRect,
                    new Rect(x - 2f, dialArea.y - 14f, 4f, 9f), marker);
                dialMarkers.Add(rule);
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
            if (waterfallNeedle != null)
                AvKit.Place(waterfallNeedle.rectTransform,
                    new Rect(waterfallArea.x + 1f + fraction * (waterfallArea.width - 3f),
                        waterfallArea.y - 1f, 2f, waterfallArea.height - 2f));
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
                sweeping = false;
                PlaceNeedle(dial.Fraction);
                shownDial = dial;
                dialInitialized = true;
                nextRefresh = 0f;
            }
            else if (!dial.Equals(shownDial))
            {
                if (dialInitialized && !manager.FineTuning)
                {
                    sweeping = true;
                    sweepStart = Time.unscaledTime;
                    sweepFrom = shownDial.Fraction;
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
                    if (frequencyLabel != null)
                        frequencyLabel.text = SweepFrequencyText(dial.Band, fraction);
                }
            }

            if (Time.unscaledTime >= nextWaterfallAt && waterfall != null && manager.HasChannels)
            {
                nextWaterfallAt = Time.unscaledTime + 0.12f;
                waterfall.Push(manager.Spectrum);
            }

            if (Time.unscaledTime < nextMeterAt) return;
            nextMeterAt = Time.unscaledTime + 0.1f;
            UpdateMeter();
        }

        private static string SweepFrequencyText(RadioBand band, float fraction)
        {
            int min = RadioBands.Min(band);
            int max = RadioBands.Max(band);
            int step = RadioBands.Step(band);
            int khz = min + Mathf.RoundToInt(fraction * (max - min) / step) * step;
            return RadioBands.Format(band, khz);
        }

        private static void UpdateMeter()
        {
            if (manager == null) return;
            float level;
            string signal;
            string token;
            string state = "inert";

            if (!manager.HasChannels)
            {
                level = 0f;
                signal = "S0";
                token = "NO LIB";
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
                level = 0.06f + Mathf.PerlinNoise(Time.unscaledTime * 3.5f, 0.71f) * 0.16f;
                signal = "NO SIGNAL";
                token = "NO SIG";
                state = "warn";
            }
            else
            {
                RadioReception reception = manager.Reception;
                level = Mathf.Max(Mathf.Max(0.04f, reception.Quality * 0.9f),
                    manager.SignalLevel * 0.6f);
                bool open = reception.Open(manager.Squelch);
                signal = reception.SReport + " · " + reception.DbmReport +
                    (open ? "" : " · SQL SHUT");
                token = open ? (manager.IsEngaged && !manager.IsPaused ? "LOCK" : "READY") : "SQL";
                state = open ? (manager.IsEngaged && !manager.IsPaused ? "live" : null) : "warn";
            }

            int lit = Mathf.RoundToInt(Mathf.Clamp01(level) * MeterPips);
            for (int i = 0; i < meterPips.Length; i++)
            {
                if (meterPips[i] == null) continue;
                bool on = i < lit;
                Color colour = i >= MeterPips - 2 && on ? AvTheme.Warning : AvTheme.Accent;
                colour = on ? new Color(colour.r, colour.g, colour.b,
                    Mathf.Lerp(1f, 0.55f, i / (float)MeterPips)) : AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.25f));
                meterPips[i].color = colour;
            }

            if (signalLabel != null)
            {
                signalLabel.text = signal;
                signalLabel.color = state == "live" ? AvTheme.Accent
                    : state == "warn" ? AvTheme.Warning : AvTheme.Dim;
            }
            if (dataBar != null) dataBar.SetChip(2, token, state);
        }

        private static void SectionTitle(RectTransform parent, float x, float width, float y, string title)
        {
            TMP_Text label = AvStyled.Label(parent, new Rect(x, y, 160f, HeaderHeight), title, "section-title");
            float titleWidth = Mathf.Ceil(label.GetPreferredValues(title).x);
            const float tickWidth = 20f;
            AvKit.Rule(parent, new Rect(x + titleWidth + AvTokens.Space2, y - 7f,
                Mathf.Min(tickWidth, Mathf.Max(0f, width - titleWidth - AvTokens.Space2)), 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));
        }

        private static ChannelRow MakeChannelRow(RectTransform parent, Rect frame, float y)
        {
            float x = frame.x;
            float w = frame.width;

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

        private static ProgrammeRow MakeProgrammeRow(RectTransform parent, Rect frame, float y)
        {
            float x = frame.x;
            float w = frame.width;

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
            button.WithTooltip("Monitor this track from the tuned station.");
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
                PlaceNeedle(dial.Fraction);
            }

            if (hasChannels && !offStation)
            {
                frequencyLabel.text = dial.FrequencyText;
                unitLabel.text = dial.UnitText;
                modeLabel.text = dial.BandText + " · " + manager.BroadcastMode;
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
                modeLabel.text = dial.BandText + " · " + manager.BroadcastMode;
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
                modeLabel.text = "FM · --";
                stationNameLabel.text = "NO STATIONS";
                wireLabel.text = "Add music folders, then press RESCAN.";
                programLabel.text = string.Empty;
                stationBadge.text = "--";
                stationBadge.gameObject.SetActive(true);
                stationIcon.enabled = false;
                stationsNote.text = "0 FOUND";
            }

            spectrumNote.text = dial.BandText + " · " + dial.FrequencyText + " " + dial.UnitText;
            trackLabel.text = offStation ? "DEAD AIR" : manager.CurrentTrackTitle;
            progressFill.fillAmount = offStation ? 0f : manager.Progress;
            timeLabel.text = offStation ? "--:-- / --:--"
                : FormatTime(manager.Elapsed) + " / " + FormatTime(manager.Duration);

            powerButton?.SetText(manager.IsPaused ? "RES" : manager.IsEngaged ? "PAUSE" : "PWR");
            powerButton?.SetLatched(manager.IsEngaged && !manager.IsPaused);
            scanButton?.SetLatched(manager.IsScanning);
            bandwidthButton?.SetLatched(manager.NarrowBandwidth);
            stepButton?.SetLatched(manager.FineTuning);
            modeButton?.SetText(ModeText());
            modeButton?.SetLatched(manager.ReceiverModulation != dial.Modulation);

            int selectedTracks = hasChannels ? manager.GetChannelTrackCount(manager.SelectedChannel) : 0;
            bool selectedChannelHasTracks = selectedTracks > 0;
            seekDownButton?.SetEnabled(hasChannels);
            seekUpButton?.SetEnabled(hasChannels);
            tuneDownButton?.SetEnabled(hasChannels);
            tuneUpButton?.SetEnabled(hasChannels);
            bandButton?.SetEnabled(hasChannels);
            powerButton?.SetEnabled(selectedChannelHasTracks && !offStation);
            stopButton?.SetEnabled(manager.IsEngaged || offStation);
            scanButton?.SetEnabled(manager.ChannelCount > 1);
            squelchDownButton?.SetEnabled(manager.Squelch > 0.001f);
            squelchUpButton?.SetEnabled(manager.Squelch < 0.94f);
            transmitButton?.SetEnabled(false);
            secureButton?.SetLatched(manager.Link.Secure);
            squelchLabel.text = "SQL " + Mathf.RoundToInt(manager.Squelch * 100f) +
                " · " + (manager.NarrowBandwidth ? "NARROW" : "WIDE") +
                " · STEP " + StepText(dial.Band);
            footerLabel.text = !manager.ReceptionModelled
                ? "RECEIVE ONLY · LOCAL ARCHIVE · NO TOWER FIX ON THIS MAP, SIGNAL READS FULL"
                : manager.Link.Secure
                    ? "RECEIVE ONLY · NET " + manager.Link.NetName + " · SECURE LATCHED (PLACEHOLDER)"
                    : "RECEIVE ONLY · NO TRANSMIT · RECEPTION MODELLED FROM RANGE, HORIZON AND TERRAIN";
            shell?.WriteStatus(null, MapPicker.Prompt, manager.Status);

            int pages = Math.Max(1, (manager.ChannelCount + rowsPerPage - 1) / rowsPerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            if (pageLabel != null) pageLabel.text = (page + 1) + " / " + pages;
            pagePreviousButton?.SetEnabled(page > 0);
            pageNextButton?.SetEnabled(page + 1 < pages);

            if (dataBar != null)
            {
                bool playing = manager.IsEngaged && !manager.IsPaused;
                dataBar.State.text = manager.IsScanning ? "SCANNING"
                                    : offStation ? "DEAD AIR"
                                    : playing ? "ON AIR"
                                    : manager.IsPaused ? "PAUSED"
                                    : hasChannels ? "STANDBY" : "NO LIBRARY";
                dataBar.State.color = manager.IsScanning || offStation ? AvTheme.Warning
                                    : playing ? AvTheme.RailReady
                                    : hasChannels ? AvTheme.Dim : AvTheme.Warning;
                dataBar.SetChip(0, hasChannels ? dial.FullText : "-- MHz", hasChannels ? "live" : "inert");
                dataBar.SetChip(1, dial.BandText + " " + StepText(dial.Band), hasChannels ? "info" : "inert");
                UpdateMeter();
            }

            if (channelsEmptyLabel != null)
            {
                if (channelsEmptyLabel.gameObject.activeSelf != !hasChannels)
                    channelsEmptyLabel.gameObject.SetActive(!hasChannels);
                if (!hasChannels)
                    channelsEmptyLabel.text =
                        "No music folders found. Press RESCAN after adding OGG/WAV folders, " +
                        "or use the MUS deck screen for the local library.";
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
                bool modelled = manager.GetChannelHasTransmitter(index);
                float strength = Mathf.Clamp01(manager.GetChannelStrength(index));
                item.Count.text = modelled
                    ? Mathf.RoundToInt(strength * 100f) + "%"
                    : manager.GetChannelTrackCount(index) + " TRK";
                bool selected = index == manager.SelectedChannel && !offStation;
                item.Label.color = AvTheme.TextPrimary;
                item.Frequency.color = selected ? AvTheme.Accent
                    : modelled && strength < 0.25f ? AvTheme.Warning
                    : AvTheme.Unity(AvTokens.TextDim);
                item.SelectionRule.color = selected ? AvTheme.Accent : Color.clear;
                item.Ground.color = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear;
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
            }
        }

        private static string ModeText()
        {
            if (manager == null) return "AUTO";
            switch (manager.Mode)
            {
                case ReceiverMode.Fm: return "FM";
                case ReceiverMode.Am: return "AM";
                default: return "AUTO";
            }
        }

        private static string StepText(RadioBand band)
        {
            int step = RadioBands.Step(band);
            if (band == RadioBand.Mw) return step + "k";
            return step >= 1000 ? (step / 1000f).ToString("0.#") + "M" : step + "k";
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
