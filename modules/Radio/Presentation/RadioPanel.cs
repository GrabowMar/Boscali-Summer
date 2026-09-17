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
    /// The receiver and the music deck in one set, on two pages.
    ///
    /// <para>RECEIVER is the set: a tuning display, a modelled signal report and the stations on
    /// the dial. You tune a station and hear its programme; you do not pick the track. MUSIC is
    /// the player's own library: one transport, one folder stepper, one track list.</para>
    ///
    /// <para>Layout rule: the figures the page exists for sit in the 25px metric strip, every
    /// control is labelled with the value it changes rather than the key it would press, and the
    /// status strip carries the hovered control's description plus the page's own copy.</para>
    /// </summary>
    internal static class RadioPanel
    {
        private const float Width = AvTokens.PanelWidth;
        private const float MetricHeight = 64f;
        private const float CardHeight = 84f;
        private const float TransportHeight = 36f;
        private const float ControlHeight = 32f;
        private const float HeaderHeight = 26f;
        private const float StepperHeight = AvTokens.RowHeight;
        private const float ChannelPitch = 30f;
        private const float TrackPitch = 26f;
        private const float WaterfallHeight = 54f;
        private const float DialBlock = 46f;
        private const float RadioContentHeight = 672f;
        private const float DeckContentHeight = 544f;
        private const float SweepSeconds = 0.45f;
        private const int StationRows = 5;
        private const int TrackRows = 10;
        private const int TabReceiver = 0;
        private const int TabDeck = 1;

        private sealed class StationRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image SelectionRule;
            public Image BadgeGround;
            public Image Icon;
            public TMP_Text Badge;
            public TMP_Text Name;
            public TMP_Text Frequency;
            public TMP_Text Status;
            public AvButton Button;
        }

        private sealed class TrackRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Rule;
            public TMP_Text Number;
            public TMP_Text Title;
            public AvButton Button;
        }

        private sealed class ReceiverUi
        {
            public AvStyled.Metric Frequency;
            public AvStyled.Metric Signal;
            public Image CardRail;
            public Image IconGround;
            public Image Icon;
            public TMP_Text Badge;
            public TMP_Text Station;
            public TMP_Text Program;
            public TMP_Text Ticker;
            public TMP_Text OnAir;
            public TMP_Text Time;
            public Image Progress;
            public TMP_Text StationsNote;
            public TMP_Text EmptyNote;
            public TMP_Text PageValue;
            public AvButton PagePrevious;
            public AvButton PageNext;
            public AvButton Monitor;
            public AvButton Scan;
            public AvButton Band;
            public AvButton Mode;
            public AvButton Bandwidth;
            public AvButton Step;
            public TMP_Text Volume;
            public TMP_Text Squelch;
            public readonly StationRow[] Rows = new StationRow[StationRows];
        }

        private sealed class DeckUi
        {
            public TMP_Text FolderLabel;
            public TMP_Text TrackLabel;
            public TMP_Text TimeLabel;
            public Image Progress;
            public AvButton Play;
            public AvButton Shuffle;
            public AvButton Repeat;
            public TMP_Text LibraryNote;
            public TMP_Text FolderValue;
            public AvButton FolderPrevious;
            public AvButton FolderNext;
            public TMP_Text PageValue;
            public AvButton PagePrevious;
            public AvButton PageNext;
            public readonly TrackRow[] Rows = new TrackRow[TrackRows];
        }

        private static MFDScreen screen;
        private static GameObject screenRoot;
        private static AvScreen shell;
        private static RadioManager manager;
        private static AvStyled.DataBar dataBar;
        private static ReceiverUi rx;
        private static DeckUi deck;
        private static RectTransform radioPage;

        private static RadioWaterfall waterfall;
        private static Image waterfallNeedle;
        private static Rect waterfallArea;
        private static GameObject dialRoot;
        private static Rect dialArea;
        private static RadioBand dialBand;
        private static Image dialNeedle;
        private static readonly List<Image> dialMarkers = new List<Image>();

        private static int stationPage;
        private static int trackPage;
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

            screenRoot = null;
            screen = null;
            shell = null;
            manager = null;
            dataBar = null;
            rx = null;
            deck = null;
            radioPage = null;
            waterfall = null;
            waterfallNeedle = null;
            dialRoot = null;
            dialNeedle = null;
            dialMarkers.Clear();
            dialBand = RadioBand.Fm;
            stationPage = 0;
            trackPage = 0;
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
                    new[] { "RECEIVER", "MUSIC" }, () => nextRefresh = 0f, out RadioScreen built))
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    Fail("bezel label or highlight missing");
                    return;
                }

                screen = built.Screen;
                screenRoot = built.Root;
                shell = built.Shell;
                dataBar = shell.DataBar;

                RectTransform receiverPage = RadioScreens.ScrollPage(shell, TabReceiver, "ReceiverPage",
                    RadioContentHeight, out Rect receiverArea);
                radioPage = receiverPage;
                BuildReceiver(receiverPage, receiverArea);

                RectTransform musicPage = RadioScreens.ScrollPage(shell, TabDeck, "MusicPage",
                    DeckContentHeight, out Rect deckArea);
                BuildDeck(musicPage, deckArea);

                shell.SetPage(TabReceiver);

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

        // ---------------------------------------------------------------- page scaffolding

        /// <summary>A section header tied to the page spine. Returns the y below it.</summary>
        private static float SectionHeader(
            RectTransform page, float x, float y, float width,
            string title, string note, float noteRight, out TMP_Text noteLabel)
        {
            AvStyled.SpineTick(page, x - AvScreen.SpineInset, y - 7f);
            float half = width * 0.5f;
            AvStyled.Label(page, new Rect(x, y, half, 14f), title, "section-title");
            noteLabel = AvStyled.Label(page,
                new Rect(x + half, y, Mathf.Max(0f, half - noteRight), 14f),
                note ?? string.Empty, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            AvKit.Rule(page, new Rect(x, y - 18f, width, 1f), AvTheme.Hairline);
            return y - HeaderHeight;
        }

        private static void Metrics(
            RectTransform page, Rect area, string leftKey, string leftUnit, string rightKey, string rightUnit,
            out AvStyled.Metric left, out AvStyled.Metric right)
        {
            AvStyled.Box(page, area, "metrics");
            float half = area.width * 0.5f;
            AvKit.Rule(page, new Rect(area.x + half, area.y, 1f, area.height), AvTheme.Hairline);
            left = AvStyled.MetricCell(page, new Rect(area.x, area.y, half, area.height), leftKey, leftUnit);
            right = AvStyled.MetricCell(page, new Rect(area.x + half, area.y, area.width - half, area.height), rightKey, rightUnit);
        }

        private static void NoWrap(TMP_Text label)
        {
            if (label == null) return;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ------------------------------------------------------------------ receiver page

        private static void BuildReceiver(RectTransform page, Rect area)
        {
            rx = new ReceiverUi();
            float x = area.x + AvScreen.SpineInset;
            float w = area.width - AvScreen.SpineInset;
            float y = area.y;

            AvStyled.Spine(page, new Rect(area.x, area.y, 3f, area.height));

            Metrics(page, new Rect(x, y, w, MetricHeight), "FREQUENCY", "MHz", "SIGNAL", string.Empty,
                out rx.Frequency, out rx.Signal);
            y -= MetricHeight + AvTokens.Gap;

            BuildReceiverCard(page, new Rect(x, y, w, CardHeight));
            y -= CardHeight + AvTokens.Gap;

            y = SectionHeader(page, x, y, w, "TUNING", null, 0f, out _);

            waterfallArea = new Rect(x, y, w, WaterfallHeight);
            waterfall = new RadioWaterfall(page, waterfallArea, RadioSpectrum.DefaultBins, 48);
            waterfallNeedle = AvKit.Rule(page,
                new Rect(waterfallArea.x + 1f, waterfallArea.y - 1f, 2f, waterfallArea.height - 2f),
                AvTheme.Accent);
            waterfallNeedle.rectTransform.SetAsLastSibling();
            y -= WaterfallHeight + AvTokens.Gap;

            dialArea = new Rect(x, y, w, DialBlock);
            EnsureDialBand(manager == null ? RadioBand.Fm : manager.TunedDial.Band);
            y -= DialBlock + AvTokens.Gap;

            BuildTunerKeys(page, new Rect(x, y, w, ControlHeight));
            y -= ControlHeight + AvTokens.Gap;

            y = SectionHeader(page, x, y, w, "RECEIVER SETUP", null, 0f, out _);

            BuildSetupKeys(page, new Rect(x, y, w, ControlHeight));
            y -= ControlHeight + AvTokens.Gap;

            BuildSteppers(page, new Rect(x, y, w, StepperHeight));
            y -= StepperHeight + AvTokens.Gap;

            TMP_Text note = null;
            float headerTop = y;
            y = SectionHeader(page, x, y, w, "STATIONS", "0 FOUND", 60f, out note);
            rx.StationsNote = note;
            AvKit.Button(page, "RESCAN", new Rect(x + w - 58f, headerTop - 1f, 58f, 16f),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Sign off, rescan the local music library and rebuild the dial.");

            for (int i = 0; i < StationRows; i++)
                rx.Rows[i] = MakeStationRow(page, x, y - i * ChannelPitch, w);
            rx.EmptyNote = AvStyled.Label(page, new Rect(x, y - ChannelPitch * 2f, w, ChannelPitch * 3f),
                "NO STATIONS · ADD OGG/WAV FOLDERS, THEN PRESS RESCAN",
                "row-sub", align: TextAlignmentOptions.Center);
            rx.EmptyNote.gameObject.SetActive(false);
            y -= ChannelPitch * StationRows + AvTokens.Gap;

            AvButton[] pager = AvKit.Stepper(page, x, y, w, out rx.PageValue, PreviousStationPage, NextStationPage);
            rx.PagePrevious = pager[0];
            rx.PageNext = pager[1];
            rx.PagePrevious.WithTooltip("Show the previous page of stations. Disabled on the first page.");
            rx.PageNext.WithTooltip("Show the next page of stations. Disabled on the last page.");
        }

        private static void BuildReceiverCard(RectTransform page, Rect area)
        {
            (Image _, Image rail) = AvKit.TacticalCard(page, area, AvTheme.RailReady);
            rx.CardRail = rail;

            rx.IconGround = AvKit.Panel(page, new Rect(area.x + AvTokens.Space2, area.y - AvTokens.Space2, 44f, 44f),
                AvTheme.SurfaceInert, AvSprites.Card);
            AvKit.Outline(page, new Rect(area.x + AvTokens.Space2, area.y - AvTokens.Space2, 44f, 44f), AvTheme.Frame);
            rx.Icon = AvKit.Panel(rx.IconGround.rectTransform,
                new Rect(AvTokens.Space1 + 1f, -(AvTokens.Space1 + 1f), 36f, 36f), Color.white);
            rx.Icon.preserveAspect = true;
            rx.Icon.enabled = false;
            rx.Badge = AvKit.Label(rx.IconGround.rectTransform, "--", new Rect(0f, 0f, 44f, 44f),
                AvTheme.TextPrimary, AvTokens.FontTitle, FontStyles.Bold, TextAlignmentOptions.Center);

            float infoX = area.x + 62f;
            rx.Station = AvStyled.Label(page, new Rect(infoX, area.y - 6f, area.width - 62f - 158f, 18f),
                "NO STATIONS", "row-name");
            NoWrap(rx.Station);
            rx.Program = AvStyled.Label(page,
                new Rect(area.x + area.width - AvTokens.Space2 - 150f, area.y - 8f, 150f, 14f),
                string.Empty, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            NoWrap(rx.Program);

            rx.Ticker = AvKit.Label(page, string.Empty,
                new Rect(infoX, area.y - 25f, area.width - 62f - AvTokens.Space2, 13f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            rx.OnAir = AvStyled.Label(page, new Rect(infoX, area.y - 43f, area.width - 62f - 104f, 18f),
                "ON AIR", "row-main");
            NoWrap(rx.OnAir);
            rx.Time = AvKit.Label(page, "00:00 / 00:00",
                new Rect(area.x + area.width - AvTokens.Space2 - 96f, area.y - 43f, 96f, 18f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            rx.Progress = AvKit.ProgressBar(page,
                new Rect(area.x + AvTokens.Space2, area.y - 74f, area.width - AvTokens.Space4, 5f),
                0f, AvTheme.Accent);
        }

        private static void BuildTunerKeys(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float unit = (area.width - gap * 6f) / 7.4f;
            float monitorWidth = unit * 1.4f;
            float packed = area.width - gap * 6f - monitorWidth;
            float keyWidth = packed / 6f;

            float x = area.x;
            AvKit.Button(page, "< SEEK", new Rect(x, area.y, keyWidth, ControlHeight),
                () => manager?.SeekStation(-1), AvTokens.FontMicro, AvButtonStyle.Default)
                .WithTooltip("Seek the next station down the band.");
            AvKit.Button(page, "< TUNE", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.StepDial(-1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Tune one step down. Between channels is dead air.");
            x += keyWidth + gap;
            rx.Monitor = AvKit.Button(page, "MONITOR", new Rect(x, area.y, monitorWidth, ControlHeight),
                () => manager?.TogglePlayback(), AvTokens.FontSmall, AvButtonStyle.Primary)
                .WithTooltip("Monitor or pause the tuned station's programme.");
            AvKit.Button(page, "TUNE >", new Rect(x += monitorWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.StepDial(1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Tune one step up. Between channels is dead air.");
            AvKit.Button(page, "SEEK >", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.SeekStation(1), AvTokens.FontMicro, AvButtonStyle.Default)
                .WithTooltip("Seek the next station up the band.");
            x += keyWidth + gap;
            rx.Scan = AvKit.Button(page, "SCAN", new Rect(x, area.y, keyWidth, ControlHeight),
                () => manager?.ToggleScan(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Hold each station in the band for a few seconds as it seeks.");
            AvKit.Button(page, "STOP", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.Stop(), AvTokens.FontMicro, AvButtonStyle.Danger)
                .WithTooltip("Sign off and hand the soundtrack bus back to the game.");
        }

        private static void BuildSetupKeys(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float keyWidth = (area.width - gap * 3f) / 4f;
            float x = area.x;
            rx.Band = AvKit.Button(page, "BAND · FM", new Rect(x, area.y, keyWidth, ControlHeight),
                () => manager?.CycleBand(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Cycle FM broadcast, VHF air and MW. Each band keeps its last frequency.");
            rx.Mode = AvKit.Button(page, "MODE · AUTO", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.CycleMode(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Cycle AUTO, FM and AM. A forced mode on the wrong station garbles it.");
            rx.Bandwidth = AvKit.Button(page, "BW · WIDE", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.ToggleBandwidth(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Wide or narrow passband. Narrow trades audio quality for less noise.");
            rx.Step = AvKit.Button(page, "STEP · 100k", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.ToggleFineTuning(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Channel step, or a five-times finer tuning step.");
        }

        private static void BuildSteppers(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float half = (area.width - gap) * 0.5f;
            Stepper(page, new Rect(area.x, area.y, half, StepperHeight), "AF 100%",
                () => manager?.NudgeVolume(-0.1f), () => manager?.NudgeVolume(0.1f),
                "Turn the receiver volume down.", "Turn the receiver volume up.", out rx.Volume);
            Stepper(page, new Rect(area.x + half + gap, area.y, half, StepperHeight), "SQL 15",
                () => manager?.NudgeSquelch(-0.05f), () => manager?.NudgeSquelch(0.05f),
                "Lower the squelch: a weaker station can open the audio.",
                "Raise the squelch: only a stronger station opens the audio.", out rx.Squelch);
        }

        private static void Stepper(
            RectTransform page, Rect area, string text, Action down, Action up,
            string downTip, string upTip, out TMP_Text value)
        {
            AvButton[] arrows = AvKit.Stepper(page, area.x, area.y, area.width, out value, down, up);
            arrows[0].SetText("-");
            arrows[0].WithTooltip(downTip);
            arrows[1].SetText("+");
            arrows[1].WithTooltip(upTip);
            value.text = text;
        }

        // --------------------------------------------------------------------- music page

        private static void BuildDeck(RectTransform page, Rect area)
        {
            deck = new DeckUi();
            float x = area.x + AvScreen.SpineInset;
            float w = area.width - AvScreen.SpineInset;
            float y = area.y;

            AvStyled.Spine(page, new Rect(area.x, area.y, 3f, area.height));

            BuildDeckCard(page, new Rect(x, y, w, CardHeight));
            y -= CardHeight + AvTokens.Gap;

            BuildDeckTransport(page, new Rect(x, y, w, TransportHeight));
            y -= TransportHeight + AvTokens.Gap;

            TMP_Text note = null;
            float headerTop = y;
            y = SectionHeader(page, x, y, w, "LIBRARY", "0 TRACKS", 60f, out note);
            deck.LibraryNote = note;
            AvKit.Button(page, "RESCAN", new Rect(x + w - 58f, headerTop - 1f, 58f, 16f),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Rescan the local music library for new folders and tracks.");

            AvButton[] folderArrows = AvKit.Stepper(page, x, y, w, out deck.FolderValue,
                () => NudgeFolder(-1), () => NudgeFolder(1));
            deck.FolderPrevious = folderArrows[0];
            deck.FolderNext = folderArrows[1];
            deck.FolderPrevious.WithTooltip("Browse the previous music folder.");
            deck.FolderNext.WithTooltip("Browse the next music folder.");
            y -= StepperHeight + AvTokens.Gap;

            for (int i = 0; i < TrackRows; i++)
                deck.Rows[i] = MakeTrackRow(page, x, y - i * TrackPitch, w);
            y -= TrackPitch * TrackRows + AvTokens.Gap;

            AvButton[] pager = AvKit.Stepper(page, x, y, w, out deck.PageValue, PreviousTrackPage, NextTrackPage);
            deck.PagePrevious = pager[0];
            deck.PageNext = pager[1];
            deck.PagePrevious.WithTooltip("Show the previous page of tracks.");
            deck.PageNext.WithTooltip("Show the next page of tracks.");
            y -= StepperHeight + AvTokens.Gap;

            BuildDeckUtility(page, new Rect(x, y, w, StepperHeight));
        }

        private static void BuildDeckCard(RectTransform page, Rect area)
        {
            AvKit.TacticalCard(page, area, AvTheme.RailInfo);

            deck.FolderLabel = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 6f, area.width - AvTokens.Space4, 14f),
                "LOCAL MUSIC", "section-title");
            NoWrap(deck.FolderLabel);

            deck.TrackLabel = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 24f, area.width - AvTokens.Space4 - 104f, 20f),
                "NO TRACK", "row-main");
            NoWrap(deck.TrackLabel);
            deck.TimeLabel = AvKit.Label(page, "--:-- / --:--",
                new Rect(area.x + area.width - AvTokens.Space2 - 96f, area.y - 24f, 96f, 20f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            deck.Progress = AvKit.ProgressBar(page,
                new Rect(area.x + AvTokens.Space2, area.y - 74f, area.width - AvTokens.Space4, 6f),
                0f, AvTheme.RailInfo);
        }

        private static void BuildDeckTransport(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float keyWidth = (area.width - gap * 3f) / 4f;
            float x = area.x;
            AvKit.Button(page, "< PREV", new Rect(x, area.y, keyWidth, TransportHeight),
                () => manager?.DeckPrevious(), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Previous track in this folder.");
            deck.Play = AvKit.Button(page, "PLAY", new Rect(x += keyWidth + gap, area.y, keyWidth, TransportHeight),
                () => manager?.DeckTogglePlayback(), AvTokens.FontSmall, AvButtonStyle.Primary)
                .WithTooltip("Play or pause the selected track.");
            AvKit.Button(page, "NEXT >", new Rect(x += keyWidth + gap, area.y, keyWidth, TransportHeight),
                () => manager?.DeckNext(), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Next track in this folder.");
            AvKit.Button(page, "STOP", new Rect(x += keyWidth + gap, area.y, keyWidth, TransportHeight),
                () => manager?.DeckStop(), AvTokens.FontSmall, AvButtonStyle.Danger)
                .WithTooltip("Stop the deck. The receiver keeps the soundtrack bus if it is on air.");
        }

        private static void BuildDeckUtility(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float keyWidth = (area.width - gap * 3f) / 4f;
            float x = area.x;
            deck.Shuffle = AvKit.Button(page, "SHUFFLE", new Rect(x, area.y, keyWidth, StepperHeight),
                () => manager?.DeckToggleShuffle(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Random track order, for the music page and the radio programme.");
            deck.Repeat = AvKit.Button(page, "REPEAT", new Rect(x += keyWidth + gap, area.y, keyWidth, StepperHeight),
                () => manager?.DeckToggleRepeat(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Repeat the current track instead of advancing.");
            AvKit.Button(page, "FOLDER", new Rect(x += keyWidth + gap, area.y, keyWidth, StepperHeight),
                () => manager?.OpenLibraryFolder(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Open the local music folder. OGG and WAV files only.");
            AvKit.Button(page, "STOP ALL", new Rect(x += keyWidth + gap, area.y, keyWidth, StepperHeight),
                () => manager?.StopAll(), AvTokens.FontMicro, AvButtonStyle.Danger)
                .WithTooltip("Stop the deck and the receiver, and hand the soundtrack bus back to the game.");
        }

        // -------------------------------------------------------------------- dial helpers

        private static void EnsureDialBand(RadioBand band)
        {
            if (dialRoot != null && dialBand == band) return;
            if (dialRoot != null) UnityEngine.Object.Destroy(dialRoot);
            dialMarkers.Clear();

            dialRoot = new GameObject("Dial", typeof(RectTransform));
            var rootRect = (RectTransform)dialRoot.transform;
            rootRect.SetParent(radioPage, false);
            rootRect.SetAsLastSibling();
            AvKit.Place(rootRect, new Rect(0f, 0f, Width, 0f));

            dialBand = band;
            int min = RadioBands.Min(band);
            int max = RadioBands.Max(band);
            int step = RadioBands.Step(band);
            int minorStep = Mathf.Max(step, (max - min) / 60 / step * step);
            int labelStep = band == RadioBand.Mw ? 200 : 2000;

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
                    0.25f + 0.75f * strength);
                dialMarkers.Add(AvKit.Rule(rootRect, new Rect(x - 2f, dialArea.y - 14f, 4f, 9f), marker));
            }

            if (dialNeedle != null) dialNeedle.rectTransform.SetAsLastSibling();
            markerRevision = manager.StationRevision;
        }

        private static void PlaceNeedle(float fraction)
        {
            if (dialNeedle == null) return;
            AvKit.Place(dialNeedle.rectTransform, new Rect(DialX(fraction) - 1f, dialArea.y - 14f, 2f, 16f));
            if (waterfallNeedle != null)
                AvKit.Place(waterfallNeedle.rectTransform,
                    new Rect(waterfallArea.x + 1f + fraction * (waterfallArea.width - 3f),
                        waterfallArea.y - 1f, 2f, waterfallArea.height - 2f));
        }

        private static float DialX(float fraction) =>
            dialArea.x + Mathf.Clamp01(fraction) * (dialArea.width - 1f);

        // ------------------------------------------------------------------------ animate

        private static void Animate()
        {
            if (manager == null || dialRoot == null) return;

            RadioDial dial = manager.TunedDial;
            bool off = manager.IsOffStation || manager.IsOffAir;
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
                    if (rx?.Frequency != null)
                        rx.Frequency.Value.text = SweepFrequencyText(dial.Band, fraction);
                }
            }

            if (Time.unscaledTime >= nextWaterfallAt && waterfall != null &&
                shell != null && shell.Page == TabReceiver)
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
            if (manager == null || rx?.Signal == null) return;

            float level;
            string report;
            string unit = string.Empty;
            string caption;
            string token;
            string tokenState = "inert";
            Color colour = AvTheme.Dim;

            if (!manager.HasChannels)
            {
                level = 0f;
                report = "S0";
                caption = "NO STATIONS";
                token = "NO LIB";
            }
            else if (manager.IsScanning || sweeping)
            {
                level = 0.25f + Mathf.PerlinNoise(Time.unscaledTime * 2.5f, 0.37f) * 0.35f;
                report = "SEEKING";
                caption = "SCANNING THE BAND";
                token = "SEEK";
                tokenState = "warn";
                colour = AvTheme.Warning;
            }
            else if (manager.IsOffStation)
            {
                level = 0.06f + Mathf.PerlinNoise(Time.unscaledTime * 3.5f, 0.71f) * 0.16f;
                report = "S0";
                caption = "DEAD AIR";
                token = "NO SIG";
                tokenState = "warn";
                colour = AvTheme.Warning;
            }
            else if (manager.IsOffAir)
            {
                level = 0f;
                report = "S0";
                caption = "OFF AIR";
                token = "OFF AIR";
                tokenState = "danger";
                colour = AvTheme.Alert;
            }
            else
            {
                RadioReception reception = manager.Reception;
                level = Mathf.Max(Mathf.Max(0.04f, reception.Quality * 0.9f), manager.SignalLevel * 0.6f);
                bool open = reception.Open(manager.Squelch);
                bool live = open && manager.IsEngaged && !manager.IsPaused;
                report = reception.SReport;
                unit = reception.DbmReport;
                caption = !BuiltInStationRules.IsBuiltIn(manager.CurrentChannelId)
                    ? "LOCAL ARCHIVE · FULL SCALE"
                    : !manager.ReceptionModelled
                        ? "NO TOWER FIX · FULL SCALE"
                        : Mathf.RoundToInt(manager.TowerDistanceKm) + " km · " +
                          (manager.TowerLineOfSight ? "LOS CLEAR" : "TERRAIN BLOCKED");
                token = open ? (live ? "LOCK" : "READY") : "SQL";
                tokenState = open ? (live ? "live" : "info") : "warn";
                colour = open ? (live ? AvTheme.RailReady : AvTheme.Dim) : AvTheme.Warning;
            }

            rx.Signal.Set(report, caption, level, colour);
            rx.Signal.Unit.text = unit;
            if (dataBar != null && shell != null && shell.Page == TabReceiver)
                dataBar.SetChip(2, token, tokenState);
        }

        // ----------------------------------------------------------------------- row makers

        private static StationRow MakeStationRow(RectTransform page, float x, float y, float width)
        {
            Image ground = AvKit.Panel(page, new Rect(x, y, width, ChannelPitch), Color.clear);
            RectTransform rect = ground.rectTransform;
            AvKit.Rule(rect, new Rect(0f, -ChannelPitch, width, 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
            Image selectionRule = AvKit.Rule(rect, new Rect(0f, 0f, 3f, ChannelPitch), Color.clear);

            var row = new StationRow
            {
                Root = ground.gameObject,
                Ground = ground,
                SelectionRule = selectionRule
            };

            row.BadgeGround = AvKit.Panel(rect, new Rect(10f, -4f, 22f, 22f), AvTheme.SurfaceInert);
            row.Icon = AvKit.Panel(row.BadgeGround.rectTransform, new Rect(1f, -1f, 20f, 20f), Color.white);
            row.Icon.preserveAspect = true;
            row.Icon.enabled = false;
            row.Badge = AvKit.Label(row.BadgeGround.rectTransform, "--", new Rect(0f, 0f, 22f, 22f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            row.Name = AvStyled.Label(rect, new Rect(40f, 0f, width - 40f - 162f, ChannelPitch), "LOCAL", "row-name");
            NoWrap(row.Name);
            row.Frequency = AvStyled.Label(rect, new Rect(width - 162f, 0f, 88f, ChannelPitch), "--", "row-value");
            NoWrap(row.Frequency);
            row.Status = AvStyled.Label(rect, new Rect(width - 72f, 0f, 68f, ChannelPitch), "0", "row-sub",
                align: TextAlignmentOptions.MidlineRight);
            NoWrap(row.Status);

            StationRow captured = row;
            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, width, ChannelPitch), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && captured.Index >= 0) manager.SelectChannel(captured.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear,
                AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised));
            row.Button = button;
            return row;
        }

        private static TrackRow MakeTrackRow(RectTransform page, float x, float y, float width)
        {
            Image ground = AvKit.Panel(page, new Rect(x, y, width, TrackPitch), Color.clear);
            RectTransform rect = ground.rectTransform;
            Image rule = AvKit.Rule(rect, new Rect(0f, 0f, 3f, TrackPitch), Color.clear);

            var row = new TrackRow
            {
                Root = ground.gameObject,
                Rule = rule
            };

            row.Number = AvKit.Label(rect, "1", new Rect(8f, 0f, 26f, TrackPitch),
                AvTheme.Disabled, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            row.Title = AvStyled.Label(rect, new Rect(42f, 0f, width - 50f, TrackPitch), string.Empty, "row-sub");
            NoWrap(row.Title);

            TrackRow captured = row;
            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, width, TrackPitch), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && captured.Index >= 0) manager.DeckPlay(captured.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear, AvTheme.Unity(AvTokens.Wash(
                AvTheme.RailInfo.ToRgba(), AvTokens.RowHoverScale, AvTokens.RowHoverAlpha)));
            row.Button = button;
            return row;
        }        // ------------------------------------------------------------------ page navigation

        private static void PreviousStationPage()
        {
            if (stationPage > 0) stationPage--;
            nextRefresh = 0f;
        }

        private static void NextStationPage()
        {
            int pages = manager == null ? 1 : Math.Max(1,
                (manager.ChannelCount + StationRows - 1) / StationRows);
            if (stationPage + 1 < pages) stationPage++;
            nextRefresh = 0f;
        }

        private static void NudgeFolder(int direction)
        {
            if (manager == null || manager.DeckFolderCount == 0) return;
            manager.DeckSelectFolder(Mathf.Clamp(manager.DeckFolder + direction, 0, manager.DeckFolderCount - 1));
            trackPage = 0;
            nextRefresh = 0f;
        }

        private static void PreviousTrackPage()
        {
            if (trackPage > 0) trackPage--;
            nextRefresh = 0f;
        }

        private static void NextTrackPage()
        {
            int pages = manager == null ? 1 : Math.Max(1,
                (manager.DeckTrackCount + TrackRows - 1) / TrackRows);
            if (trackPage + 1 < pages) trackPage++;
            nextRefresh = 0f;
        }

        // ------------------------------------------------------------------------- refresh

        private static void Refresh()
        {
            if (manager == null || rx == null || deck == null) return;
            if (iconRevision != manager.StationRevision)
            {
                RadioStationIconCache.Clear();
                iconRevision = manager.StationRevision;
            }
            if (markerRevision != manager.StationRevision) RefreshDialMarkers();

            RefreshReceiver();
            RefreshDeck();
            RefreshDataBar();
        }

        private static void RefreshReceiver()
        {
            bool hasChannels = manager.HasChannels;
            bool offStation = manager.IsOffStation || manager.IsOffAir;
            RadioDial dial = manager.TunedDial;
            if (dialBand != dial.Band)
            {
                EnsureDialBand(dial.Band);
                PlaceNeedle(dial.Fraction);
            }

            if (hasChannels && !offStation)
            {
                rx.Frequency.Set(dial.FrequencyText,
                    manager.BroadcastMode + " · STEP " + StepText(dial.Band), 0f, AvTheme.Accent);
                rx.Station.text = manager.CurrentChannelName;
                rx.Ticker.text = manager.TickerText;
                rx.Program.text = manager.CurrentProgram;
                ApplyStationIcon(rx.Icon, rx.Badge, manager.GetChannelIconPath(manager.SelectedChannel),
                    manager.CurrentChannelCode);
                rx.StationsNote.text = manager.ChannelCount + " STATIONS";
                SetCardRail(manager.IsEngaged && !manager.IsPaused ? AvTheme.RailReady : AvTheme.RailInert);
            }
            else if (hasChannels)
            {
                rx.Frequency.Set(dial.FrequencyText, manager.IsOffAir ? "OFF AIR" : "DEAD AIR", 0f, AvTheme.Accent);
                rx.Station.text = manager.IsOffAir ? manager.CurrentChannelName : "NO SIGNAL";
                rx.Ticker.text = manager.IsOffAir
                    ? "Tower lost. The station is off the air on this mission."
                    : "Dead air. Tune back to a station.";
                rx.Program.text = string.Empty;
                rx.Badge.text = "--";
                rx.Badge.gameObject.SetActive(true);
                rx.Icon.enabled = false;
                rx.StationsNote.text = manager.ChannelCount + " STATIONS";
                SetCardRail(manager.IsOffAir ? AvTheme.Alert : AvTheme.Warning);
            }
            else
            {
                rx.Frequency.Set("---.-", "NO STATIONS", 0f, AvTheme.Accent);
                rx.Station.text = "NO STATIONS";
                rx.Ticker.text = "Add music folders, then press RESCAN.";
                rx.Program.text = string.Empty;
                rx.Badge.text = "--";
                rx.Badge.gameObject.SetActive(true);
                rx.Icon.enabled = false;
                rx.StationsNote.text = "0 FOUND";
                SetCardRail(AvTheme.RailInert);
            }

            rx.Frequency.Unit.text = dial.UnitText;
            rx.OnAir.text = !hasChannels ? "NO STATIONS"
                : manager.IsOffAir ? "OFF AIR"
                : offStation ? "DEAD AIR" : "ON AIR · " + manager.CurrentTrackTitle;
            rx.Progress.fillAmount = offStation ? 0f : manager.Progress;
            rx.Time.text = offStation ? "--:-- / --:--"
                : FormatTime(manager.Elapsed) + " / " + FormatTime(manager.Duration);

            rx.Volume.text = "AF " + Mathf.RoundToInt(manager.VolumeLevel * 100f) + "%";
            rx.Squelch.text = "SQL " + Mathf.RoundToInt(manager.Squelch * 100f);

            rx.Monitor.SetText(manager.IsPaused ? "RESUME" : manager.IsEngaged ? "PAUSE" : "MONITOR");
            rx.Monitor.SetLatched(manager.IsEngaged && !manager.IsPaused);
            rx.Scan.SetLatched(manager.IsScanning);
            rx.Bandwidth.SetText(manager.NarrowBandwidth ? "BW · NARROW" : "BW · WIDE");
            rx.Bandwidth.SetLatched(manager.NarrowBandwidth);
            rx.Step.SetText(manager.FineTuning ? "STEP · FINE" : "STEP · " + StepText(dial.Band));
            rx.Step.SetLatched(manager.FineTuning);
            rx.Mode.SetText("MODE · " + ModeText());
            rx.Mode.SetLatched(manager.ReceiverModulation != dial.Modulation);
            rx.Band.SetText("BAND · " + dial.BandText);

            int selectedTracks = hasChannels ? manager.GetChannelTrackCount(manager.SelectedChannel) : 0;
            rx.Monitor.SetEnabled(selectedTracks > 0 && !offStation);
            rx.Scan.SetEnabled(manager.ChannelCount > 1);
            rx.Band.SetEnabled(hasChannels);

            if (rx.EmptyNote != null && rx.EmptyNote.gameObject.activeSelf == hasChannels)
                rx.EmptyNote.gameObject.SetActive(!hasChannels);

            int pages = Math.Max(1, (manager.ChannelCount + StationRows - 1) / StationRows);
            stationPage = Mathf.Clamp(stationPage, 0, pages - 1);
            rx.PageValue.text = (stationPage + 1) + " / " + pages;
            rx.PagePrevious.SetEnabled(stationPage > 0);
            rx.PageNext.SetEnabled(stationPage + 1 < pages);

            for (int i = 0; i < StationRows; i++)
            {
                StationRow row = rx.Rows[i];
                if (row == null) continue;
                int index = stationPage * StationRows + i;
                row.Index = index < manager.ChannelCount ? index : -1;
                if (row.Root.activeSelf != (row.Index >= 0)) row.Root.SetActive(row.Index >= 0);
                if (row.Index < 0) continue;

                row.Badge.text = manager.GetChannelCode(index);
                ApplyStationIcon(row.Icon, row.Badge, manager.GetChannelIconPath(index), manager.GetChannelCode(index));
                row.BadgeGround.color = AvTheme.Unity(AvTokens.Wash(
                    manager.GetChannelColor(index).ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha));

                RadioDial rowDial = manager.GetChannelDial(index);
                string name = manager.GetChannelName(index);
                if (row.Name.text != name)
                {
                    row.Name.text = name;
                    row.Button?.WithTooltip("Tune " + name + ". Its programme plays here; tracks are picked on the music tab.");
                }
                row.Frequency.text = rowDial.FrequencyText + " " + rowDial.BandText;

                float strength = Mathf.Clamp01(manager.GetChannelStrength(index));
                bool modelled = manager.GetChannelHasTransmitter(index);
                bool offAirRow = manager.GetChannelOffAir(index);
                row.Status.text = offAirRow ? "OFF AIR"
                    : modelled ? Mathf.RoundToInt(strength * 100f) + "%"
                    : manager.GetChannelTrackCount(index) + " TRK";
                row.Status.color = offAirRow ? AvTheme.Alert
                    : modelled && strength < 0.25f ? AvTheme.Warning
                    : AvTheme.Dim;

                bool selected = index == manager.SelectedChannel && !offStation;
                row.Name.color = selected ? AvTheme.Accent : AvTheme.TextPrimary;
                row.Frequency.color = selected ? AvTheme.Accent : AvTheme.Dim;
                row.SelectionRule.color = selected ? AvTheme.Accent : Color.clear;
                row.Ground.color = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear;
            }
        }

        private static void SetCardRail(Color colour)
        {
            if (rx?.CardRail != null) rx.CardRail.color = colour;
        }

        private static void RefreshDeck()
        {
            if (deck == null || deck.FolderLabel == null) return;

            int folders = manager.DeckFolderCount;
            bool hasFolders = folders > 0;
            int folder = manager.DeckFolder;
            int trackCount = manager.DeckTrackCount;
            bool playing = manager.DeckEngaged;

            deck.FolderLabel.text = hasFolders
                ? "LOCAL MUSIC · " + manager.DeckFolderName(folder) + " · " + folders + " FOLDERS"
                : "LOCAL MUSIC · NO FOLDERS";
            deck.TrackLabel.text = hasFolders ? manager.DeckCurrentTitle : "NO TRACKS FOUND";
            deck.TimeLabel.text = "--:-- / --:--";
            deck.Progress.fillAmount = 0f;
            if (playing)
            {
                deck.Progress.fillAmount = manager.DeckProgress;
                deck.TimeLabel.text = FormatTime(manager.DeckElapsed) + " / " + FormatTime(manager.DeckDuration);
            }

            deck.Play.SetText(manager.DeckPaused ? "RESUME" : manager.DeckPlaying ? "PAUSE" : hasFolders ? "PLAY" : "NO LIB");
            deck.Play.SetEnabled(hasFolders);
            deck.Shuffle.SetLatched(manager.Shuffle);
            deck.Repeat.SetLatched(manager.RepeatTrack);
            deck.LibraryNote.text = hasFolders ? trackCount + " TRACKS" : "NO TRACKS";

            deck.FolderValue.text = hasFolders
                ? (folder + 1) + " / " + folders + " · " + manager.DeckFolderName(folder) + " · " + trackCount + " TRK"
                : "0 / 0";
            deck.FolderPrevious.SetEnabled(hasFolders && folder > 0);
            deck.FolderNext.SetEnabled(hasFolders && folder + 1 < folders);

            int pages = Math.Max(1, (trackCount + TrackRows - 1) / TrackRows);
            trackPage = Mathf.Clamp(trackPage, 0, pages - 1);
            deck.PageValue.text = trackCount == 0 ? "NO TRACKS" : (trackPage + 1) + " / " + pages;
            deck.PagePrevious.SetEnabled(trackPage > 0);
            deck.PageNext.SetEnabled(trackPage + 1 < pages);

            int current = manager.DeckTrackIndex;
            for (int i = 0; i < TrackRows; i++)
            {
                TrackRow row = deck.Rows[i];
                if (row == null) continue;
                int index = trackPage * TrackRows + i;
                row.Index = index < trackCount ? index : -1;
                if (row.Root.activeSelf != (row.Index >= 0)) row.Root.SetActive(row.Index >= 0);
                if (row.Index < 0) continue;

                bool active = index == current && playing;
                row.Number.text = (index + 1).ToString();
                string title = manager.DeckTrackTitle(index);
                if (row.Title.text != title)
                {
                    row.Title.text = title;
                    row.Button?.WithTooltip("Play " + title + ".");
                }
                row.Number.color = active ? AvTheme.RailInfo : AvTheme.Disabled;
                row.Title.color = active ? AvTheme.RailInfo : AvTheme.Unity(AvTokens.TextDim);
                row.Rule.color = active ? AvTheme.RailInfo : Color.clear;
            }
        }

        private static void RefreshDataBar()
        {
            if (dataBar == null || shell == null || manager == null) return;
            bool receiverTab = shell.Page == TabReceiver;
            bool playing = manager.IsEngaged && !manager.IsPaused;

            if (receiverTab)
            {
                RadioDial dial = manager.TunedDial;
                dataBar.State.text = manager.IsOffAir ? "OFF AIR"
                    : manager.IsScanning ? "SCANNING"
                    : manager.IsOffStation ? "DEAD AIR"
                    : playing ? "ON AIR"
                    : manager.IsPaused ? "PAUSED"
                    : manager.HasChannels ? "STANDBY" : "NO LIBRARY";
                dataBar.State.color = manager.IsOffAir ? AvTheme.Alert
                    : manager.IsScanning || manager.IsOffStation ? AvTheme.Warning
                    : playing ? AvTheme.RailReady
                    : manager.HasChannels ? AvTheme.Dim : AvTheme.Warning;
                dataBar.SetChip(0, manager.HasChannels ? dial.FullText : "-- MHz",
                    manager.HasChannels ? "live" : "inert");
                dataBar.SetChip(1, dial.BandText + " " + StepText(dial.Band),
                    manager.HasChannels ? "info" : "inert");
                UpdateMeter();

                string alert = !manager.HasChannels
                    ? "NO STATIONS · ADD OGG/WAV FOLDERS, THEN PRESS RESCAN"
                    : manager.IsOffAir
                        ? "TOWER LOST · " + manager.CurrentChannelName + " IS OFF AIR ON THIS MISSION"
                        : manager.IsOffStation
                            ? "DEAD AIR · TUNE BACK TO A STATION OR SEEK ACROSS THE BAND"
                            : null;
                shell.WriteStatus(alert, null,
                    manager.Status + " · RECEIVE ONLY · TRACKS ARE PICKED ON THE MUSIC TAB");
                return;
            }

            dataBar.State.text = manager.DeckPlaying ? "PLAYING"
                : manager.DeckPaused ? "PAUSED"
                : manager.DeckFolderCount > 0 ? "READY" : "EMPTY";
            dataBar.State.color = manager.DeckPlaying ? AvTheme.RailInfo
                : manager.DeckFolderCount > 0 ? AvTheme.Dim : AvTheme.Warning;
            dataBar.SetChip(0, "LOCAL", "info");
            dataBar.SetChip(1, manager.DeckFolderCount + " FOLDERS",
                manager.DeckFolderCount > 0 ? "info" : "inert");
            dataBar.SetChip(2, manager.DeckTrackCount + " TRACKS",
                manager.DeckTrackCount > 0 ? "live" : "inert");
            shell.WriteStatus(
                manager.DeckFolderCount > 0 ? null
                    : "NO FOLDERS YET · PRESS FOLDER, ADD OGG/WAV TRACKS, THEN RESCAN",
                null, manager.DeckStatus + " · LOCAL LIBRARY ONLY · NOTHING IS DOWNLOADED OR SENT");
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

        private static void ApplyStationIcon(Image image, TMP_Text fallback, string path, string fallbackText)
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
            rx = null;
            deck = null;
            Plugin.Logger.LogWarning("Radio panel disabled (" + reason + "). Playback remains available through config reload only.");
        }
    }
}
