using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Radio.Configuration;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>
    /// The receiver and the music deck in one set, on two pages.
    ///
    /// <para>RECEIVER leads with the set's own face — the tuned frequency at 34px, the station
    /// and its programme, an S-meter with a visible squelch gate — then hands the middle of the
    /// page to the band scope: a waterfall, one ruler and one needle that is also the tuning
    /// control (hover to preview a channel, click to tune to it). The preset list is the only
    /// list; the transport, band keys and setup keys state what they do.</para>
    ///
    /// <para>MUSIC is a player: a now-playing card with a real position bar, a transport, one
    /// folder stepper, one track list, and an empty-state card that says where files go instead
    /// of twelve dead rows.</para>
    /// </summary>
    internal static class RadioPanel
    {
        private const float Width = AvTokens.PanelWidth;
        private const float TunerCard = 124f;
        private const float HeaderHeight = 26f;
        private const float WaterfallHeight = 72f;
        private const float RulerBlock = 34f;
        private const float PresetPitch = 34f;
        private const float PagerHeight = 26f;
        private const float TransportHeight = 34f;
        private const float BandHeight = 30f;
        private const float ControlHeight = 32f;

        /// <summary>
        /// The receiver stack at the base waterfall height, top to bottom. The body is measured
        /// against this and every spare pixel goes to the scope: a tall panel shows a deeper
        /// waterfall rather than a dead band between the presets and the transport.
        /// </summary>
        private const float ReceiverNaturalHeight =
            TunerCard + AvTokens.Gap
            + HeaderHeight + WaterfallHeight + AvTokens.Space2 + RulerBlock + AvTokens.Space2
            + HeaderHeight + PresetPitch * PresetRows + AvTokens.Gap
            + PagerHeight + AvTokens.Gap
            + TransportHeight + AvTokens.Gap
            + BandHeight + AvTokens.Gap
            + ControlHeight + AvTokens.Gap
            + ControlHeight;

        private const float MusicCard = 112f;
        private const float MusicTransport = 44f;
        private const float FolderRow = 26f;
        private const float TrackPitch = 24f;
        private const float EmptyCard = 356f;
        private const float UtilityHeight = 30f;
        private const float RadioContentHeight = ReceiverNaturalHeight;

        /// <summary>
        /// The deck's natural stack at the standard track pitch. The body is measured against
        /// this: slack becomes track spacing and empty-card height, so a taller panel never
        /// leaves a dead band under the utility row.
        /// </summary>
        private const float DeckNaturalHeight =
            MusicCard + AvTokens.Gap
            + MusicTransport + AvTokens.Gap
            + HeaderHeight
            + FolderRow + AvTokens.Gap
            + TrackPitch * TrackRows + AvTokens.Gap
            + PagerHeight + AvTokens.Gap
            + UtilityHeight;

        private const float SweepSeconds = 0.45f;
        private const int PresetRows = 5;
        private const int TrackRows = 12;

        // Tooltips are swapped while a control is disabled, so a greyed key always says why.
        private const string MonitorTip = "Monitor or pause the tuned station's programme.";
        private const string MonitorOffTip = "No programme on this station.";
        private const string ScanTip = "Hold each station in the band for a few seconds as it seeks.";
        private const string ScanOffTip = "Only one station in range.";
        private const string BandFmTip = "FM broadcast, 87.5 – 108 MHz in 100 kHz channels. Keeps its last frequency.";
        private const string BandAirTip = "VHF air band, 118 – 137 MHz in 25 kHz channels, AM. Keeps its last frequency.";
        private const string BandMwTip = "Medium wave, 530 – 1700 kHz in 10 kHz channels, AM. Keeps its last frequency.";
        private const string BandOffTip = "No stations in the library.";
        private const string StationPreviousTip = "Previous page of stations.";
        private const string StationNextTip = "Next page of stations.";
        private const string FirstPageTip = "Already on the first page.";
        private const string LastPageTip = "Already on the last page.";
        private const string PlayTip = "Play or pause the selected track.";
        private const string PlayNoLibraryTip = "No music found. Add OGG or WAV files and press RESCAN.";
        private const string PlayNoTracksTip = "This folder has no tracks.";
        private const string FolderPreviousTip = "Browse the previous music folder.";
        private const string FolderNextTip = "Browse the next music folder.";
        private const string FirstFolderTip = "Already on the first folder.";
        private const string LastFolderTip = "Already on the last folder.";
        private const string TrackPreviousTip = "Previous page of tracks.";
        private const string TrackNextTip = "Next page of tracks.";
        private const int TabReceiver = 0;
        private const int TabDeck = 1;

        /// <summary>
        /// A width-driven fill. Unity's Filled image type needs a sprite to honour
        /// <c>fillAmount</c> and draws a full quad without one, so bars are placed by width.
        /// </summary>
        private sealed class Bar
        {
            public Image Fill;
            public float Full;

            public void Set(float fraction) =>
                Fill.rectTransform.sizeDelta = new Vector2(
                    Mathf.Clamp01(fraction) * Full, Fill.rectTransform.sizeDelta.y);
        }

        private sealed class MeterUi
        {
            public Bar Level;
            public Image Gate;
            public TMP_Text Report;
            public TMP_Text Detail;
            public Rect Bar;
        }

        private sealed class StationRow
        {
            public int Index = -1;
            public Image Ground;
            public Image SelectionRail;
            public Image BadgeGround;
            public Image Icon;
            public TMP_Text Badge;
            public TMP_Text Preset;
            public TMP_Text Name;
            public TMP_Text Frequency;
            public TMP_Text Unit;
            public TMP_Text Status;
            public AvButton Button;
        }

        private sealed class TrackRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image Rule;
            public TMP_Text Number;
            public TMP_Text Title;
            public Bar Position;
            public AvButton Button;
        }

        private sealed class ReceiverUi
        {
            public TMP_Text Frequency;
            public TMP_Text Unit;
            public TMP_Text ModeLine;
            public TMP_Text Station;
            public TMP_Text Program;
            public TMP_Text OnAir;
            public TMP_Text Time;
            public MeterUi Meter;
            public Image CardRail;
            public Bar Progress;
            public TMP_Text ScopeNote;
            public TMP_Text StationsNote;
            public TMP_Text PageValue;
            public AvButton PagePrevious;
            public AvButton PageNext;
            public AvButton Monitor;
            public AvButton Scan;
            public AvButton BandFm;
            public AvButton BandAir;
            public AvButton BandMw;
            public AvButton Mode;
            public AvButton Bandwidth;
            public AvButton Step;
            public TMP_Text Volume;
            public TMP_Text Squelch;
            public readonly StationRow[] Rows = new StationRow[PresetRows];
        }

        private sealed class DeckUi
        {
            public TMP_Text FolderLabel;
            public TMP_Text Position;
            public TMP_Text TrackLabel;
            public TMP_Text Elapsed;
            public TMP_Text Duration;
            public Bar Progress;
            public AvButton Play;
            public AvButton Shuffle;
            public AvButton Repeat;
            public TMP_Text LibraryNote;
            public GameObject ListRoot;
            public GameObject EmptyRoot;
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
        private static Rect waterfallArea;
        private static float scopeBodyHeight = WaterfallHeight + AvTokens.Space2 + RulerBlock;
        private static GameObject dialRoot;
        private static Rect dialArea;
        private static RadioBand dialBand;
        private static Image dialNeedle;
        private static Image scopeCursor;
        private static readonly List<Image> dialMarkers = new List<Image>();

        private static int stationPage;
        private static int trackPage;
        private static int iconRevision = -1;
        private static int markerRevision = -1;
        private static int hoverKhz = int.MinValue;
        private static string idleScopeNote = string.Empty;
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
            scopeBodyHeight = WaterfallHeight + AvTokens.Space2 + RulerBlock;
            dialRoot = null;
            dialNeedle = null;
            scopeCursor = null;
            dialMarkers.Clear();
            dialBand = RadioBand.Fm;
            stationPage = 0;
            trackPage = 0;
            iconRevision = -1;
            markerRevision = -1;
            hoverKhz = int.MinValue;
            idleScopeNote = string.Empty;
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
                    Mathf.Max(DeckNaturalHeight, shell.Body.height), out Rect deckArea);
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

        // ---------------------------------------------------------------- shared widgets

        /// <summary>A quiet section header with one local accent cue. Returns the y below it.</summary>
        private static float SectionHeader(
            RectTransform page, float x, float y, float width,
            string title, string note, float noteRight, out TMP_Text noteLabel)
        {
            AvKit.Rule(page, new Rect(x, y - 1f, 3f, 14f), AvTheme.Accent.WithAlpha(0.75f));
            float half = width * 0.5f;
            AvStyled.Label(page, new Rect(x + 10f, y, half - 10f, 14f), title, "section-title");
            noteLabel = AvStyled.Label(page,
                new Rect(x + half, y, Mathf.Max(0f, half - noteRight), 14f),
                note ?? string.Empty, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            AvKit.Rule(page, new Rect(x, y - 18f, width, 1f), AvTheme.Hairline);
            return y - HeaderHeight;
        }

        private static Bar MakeBar(RectTransform page, Rect area, Color color, bool track = true)
        {
            if (track) AvKit.Panel(page, area, AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.22f)));
            Image fill = AvKit.Panel(page,
                new Rect(area.x + 1f, area.y - 1f, 0f, Mathf.Max(1f, area.height - 2f)), color);
            return new Bar { Fill = fill, Full = area.width - 2f };
        }

        /// <summary>The compact prev/label/next control: the music page uses it for both axes.</summary>
        private static void CompactStepper(
            RectTransform page, Rect area, string text,
            Action previous, Action next, string previousTip, string nextTip,
            out TMP_Text value, out AvButton previousButton, out AvButton nextButton)
        {
            AvKit.Panel(page, area, AvTheme.SurfaceInert, AvSprites.Control);
            AvKit.Outline(page, area, AvTheme.Frame);
            const float arrow = 72f;
            previousButton = AvKit.Button(page, "PREVIOUS", new Rect(area.x + 1f, area.y - 1f, arrow, area.height - 2f),
                previous, AvTokens.FontMicro, AvButtonStyle.Quiet).WithTooltip(previousTip);
            nextButton = AvKit.Button(page, "NEXT", new Rect(area.x + area.width - arrow - 1f, area.y - 1f, arrow, area.height - 2f),
                next, AvTokens.FontMicro, AvButtonStyle.Quiet).WithTooltip(nextTip);
            value = AvStyled.Label(page,
                new Rect(area.x + arrow + AvTokens.Space2, area.y, Mathf.Max(0f, area.width - (arrow + AvTokens.Space2) * 2f), area.height),
                text, "row-sub", align: TextAlignmentOptions.Center);
            NoWrap(value);
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
            float w = area.width - AvScreen.SpineInset - 12f;
            float y = area.y;

            AvStyled.Spine(page, new Rect(area.x, area.y, 3f, area.height));

            // Slack goes to the band scope, never to a gap: the waterfall is the only
            // element that reads better with more room, and the rows below it stay put.
            float waterfallHeight = WaterfallHeight + Mathf.Max(0f, area.height - ReceiverNaturalHeight);

            BuildTunerCard(page, new Rect(x, y, w, TunerCard));
            y -= TunerCard + AvTokens.Gap;

            BuildScope(page, x, ref y, w, waterfallHeight);

            TMP_Text note = null;
            float headerTop = y;
            y = SectionHeader(page, x, y, w, "STATION PRESETS", "0 FOUND", 88f, out note);
            rx.StationsNote = note;
            AvKit.Button(page, "RESCAN", new Rect(x + w - 72f, headerTop - 2f, 72f, 20f),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Sign off, rescan the local music library and rebuild the dial.");

            for (int i = 0; i < PresetRows; i++)
                rx.Rows[i] = MakeStationRow(page, x, y - i * PresetPitch, w);
            y -= PresetPitch * PresetRows + AvTokens.Gap;

            CompactStepper(page, new Rect(x, y, w, PagerHeight), "1 / 1",
                PreviousStationPage, NextStationPage,
                "Previous page of stations.", "Next page of stations.",
                out rx.PageValue, out rx.PagePrevious, out rx.PageNext);
            y -= PagerHeight + AvTokens.Gap;

            BuildTransport(page, new Rect(x, y, w, TransportHeight));
            y -= TransportHeight + AvTokens.Gap;

            BuildBandKeys(page, new Rect(x, y, w, BandHeight));
            y -= BandHeight + AvTokens.Gap;

            BuildSetupKeys(page, new Rect(x, y, w, ControlHeight));
            y -= ControlHeight + AvTokens.Gap;

            BuildSteppers(page, new Rect(x, y, w, ControlHeight));
        }

        private static void BuildTunerCard(RectTransform page, Rect area)
        {
            (Image _, Image rail) = AvKit.TacticalCard(page, area, AvTheme.RailReady);
            rx.CardRail = rail;

            rx.Frequency = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 2f, 140f, 42f),
                "---.-", "readout", align: TextAlignmentOptions.MidlineLeft);
            rx.Unit = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2 + 142f, area.y - 2f, 54f, 42f),
                "MHz", "readout-unit", align: TextAlignmentOptions.MidlineLeft);
            rx.ModeLine = AvStyled.Label(page,
                new Rect(area.x + area.width - AvTokens.Space2 - 226f, area.y - 8f, 226f, 14f),
                "FM STEREO · 100 kHz", "section-title-note", align: TextAlignmentOptions.MidlineRight);
            NoWrap(rx.ModeLine);

            rx.Station = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 48f, area.width - AvTokens.Space4 - 200f, 16f),
                "NO STATIONS", "row-name");
            NoWrap(rx.Station);
            rx.Program = AvStyled.Label(page,
                new Rect(area.x + area.width - AvTokens.Space2 - 196f, area.y - 50f, 196f, 14f),
                string.Empty, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            NoWrap(rx.Program);

            BuildMeter(page, new Rect(area.x + AvTokens.Space2, area.y - 66f, area.width - AvTokens.Space4, 28f));

            rx.OnAir = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 96f, area.width - AvTokens.Space4 - 118f, 16f),
                "ON AIR", "row-main");
            NoWrap(rx.OnAir);
            rx.Time = AvStyled.Label(page,
                new Rect(area.x + area.width - AvTokens.Space2 - 110f, area.y - 96f, 110f, 16f),
                "--:-- / --:--", "row-value", align: TextAlignmentOptions.MidlineRight);
            rx.Progress = MakeBar(page,
                new Rect(area.x + AvTokens.Space2, area.y - 116f, area.width - AvTokens.Space4, 4f),
                AvTheme.Accent);
        }

        /// <summary>Level bar with a scale grid and the squelch gate drawn on it.</summary>
        private static void BuildMeter(RectTransform page, Rect area)
        {
            var meter = new MeterUi();
            const float reportWidth = 112f;
            float barWidth = Mathf.Max(80f, area.width - reportWidth - AvTokens.Space2);
            var barArea = new Rect(area.x, area.y - 8f, barWidth, 8f);

            AvKit.Panel(page, barArea, AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.18f)));
            for (int i = 1; i <= 3; i++)
                AvKit.Rule(page, new Rect(barArea.x + barWidth * i * 0.25f, barArea.y, 1f, 8f),
                    AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.45f)));
            meter.Level = MakeBar(page, barArea, AvTheme.RailReady, track: false);
            meter.Gate = AvKit.Rule(page, new Rect(barArea.x, barArea.y - 2f, 2f, 12f), AvTheme.Warning);
            meter.Bar = barArea;

            meter.Report = AvStyled.Label(page,
                new Rect(area.x + barWidth + AvTokens.Space2, area.y - 4f, 34f, 18f),
                "S0", "row-value", align: TextAlignmentOptions.MidlineLeft);
            meter.Detail = AvStyled.Label(page,
                new Rect(area.x + barWidth + 46f, area.y - 6f, 62f, 16f),
                string.Empty, "row-sub", align: TextAlignmentOptions.MidlineLeft);
            NoWrap(meter.Detail);
            rx.Meter = meter;
        }

        private static void BuildScope(RectTransform page, float x, ref float y, float w, float waterfallHeight)
        {
            scopeBodyHeight = waterfallHeight + AvTokens.Space2 + RulerBlock;
            idleScopeNote = BandRangeText();
            TMP_Text note = null;
            y = SectionHeader(page, x, y, w, "BAND SCOPE", idleScopeNote, 0f, out note);
            rx.ScopeNote = note;

            waterfallArea = new Rect(x, y, w, waterfallHeight);
            waterfall = new RadioWaterfall(page, waterfallArea, RadioSpectrum.DefaultBins, 48);
            y -= waterfallHeight + AvTokens.Space2;

            dialArea = new Rect(x, y, w, RulerBlock);
            EnsureDialBand(manager == null ? RadioBand.Fm : manager.TunedDial.Band);
            y -= RulerBlock + AvTokens.Space2;

            scopeCursor = AvKit.Rule(page,
                new Rect(x, waterfallArea.y, 1f, scopeBodyHeight),
                AvTheme.Accent.WithAlpha(0.75f));
            scopeCursor.enabled = false;
            scopeCursor.rectTransform.SetAsLastSibling();

            Image hit = AvKit.Panel(page, new Rect(x, waterfallArea.y, w, scopeBodyHeight), Color.clear);
            hit.raycastTarget = true;
            var input = hit.gameObject.AddComponent<RadioScopeInput>();
            input.Area = (RectTransform)hit.transform;
            input.Moved = OnScopeMove;
            input.Exited = OnScopeExit;
            input.Clicked = OnScopeClick;
        }

        private static void OnScopeMove(float fraction)
        {
            if (manager == null || scopeCursor == null || rx?.ScopeNote == null) return;
            RadioBand band = manager.TunedDial.Band;
            int khz = RadioDialTuning.Nearest(band, FractionToKhz(fraction)).Kilohertz;
            if (khz != hoverKhz)
            {
                hoverKhz = khz;
                string name = StationNameAt(band, khz);
                // The marker's height and colour carry strength on the ruler; the hover note
                // spells the same reading so the meaning never rides on colour alone.
                string strength = name == null ? null : StationStrengthAt(band, khz);
                rx.ScopeNote.text = "> " + RadioBands.Format(band, khz) + " " + RadioBands.UnitText(band) +
                    " · " + (name ?? "NO STATION") + (strength == null ? string.Empty : " · " + strength);
            }
            AvKit.Place(scopeCursor.rectTransform,
                new Rect(DialXKhz(khz) - 0.5f, waterfallArea.y, 1f, scopeBodyHeight));
            if (!scopeCursor.enabled) scopeCursor.enabled = true;
        }

        private static void OnScopeExit()
        {
            hoverKhz = int.MinValue;
            if (scopeCursor != null) scopeCursor.enabled = false;
            if (rx?.ScopeNote != null) rx.ScopeNote.text = idleScopeNote;
        }

        private static void OnScopeClick(float fraction)
        {
            manager?.TuneTo(FractionToKhz(fraction));
            nextRefresh = 0f;
        }

        private static int FractionToKhz(float fraction)
        {
            RadioBand band = manager == null ? RadioBand.Fm : manager.TunedDial.Band;
            int min = RadioBands.Min(band);
            int max = RadioBands.Max(band);
            return min + Mathf.RoundToInt(Mathf.Clamp01(fraction) * (max - min));
        }

        private static string BandRangeText()
        {
            if (manager == null || !manager.HasChannels) return "TUNE TO A STATION";
            RadioBand band = manager.TunedDial.Band;
            return RadioBands.Format(band, RadioBands.Min(band)) + " - " +
                RadioBands.Format(band, RadioBands.Max(band)) + " " + RadioBands.UnitText(band) +
                " · " + RadioBands.BandText(band);
        }

        private static string StationNameAt(RadioBand band, int kilohertz)
        {
            if (manager == null) return null;
            for (int i = 0; i < manager.ChannelCount; i++)
            {
                RadioDial dial = manager.GetChannelDial(i);
                if (dial.Band == band && dial.Kilohertz == kilohertz) return manager.GetChannelName(i);
            }
            return null;
        }

        /// <summary>The hover note's strength word, matching the preset row's own reading.</summary>
        private static string StationStrengthAt(RadioBand band, int kilohertz)
        {
            if (manager == null) return null;
            for (int i = 0; i < manager.ChannelCount; i++)
            {
                RadioDial dial = manager.GetChannelDial(i);
                if (dial.Band != band || dial.Kilohertz != kilohertz) continue;
                if (manager.GetChannelOffAir(i)) return "OFF AIR";
                if (!manager.GetChannelHasTransmitter(i)) return manager.GetChannelTrackCount(i) + " TRK";
                float strength = Mathf.Clamp01(manager.GetChannelStrength(i));
                return strength >= 0.7f ? "STRONG" : strength >= 0.35f ? "FAIR" : "WEAK";
            }
            return null;
        }

        /// <summary>
        /// One transport row, five equal slots. SEEK and TUNE are rockers, so both directions
        /// live inside one labelled control instead of the old duplicated pair per key; the
        /// band scope still tunes straight to any channel and the presets still jump.
        /// </summary>
        private static void BuildTransport(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float slot = (area.width - gap * 4f) / 5f;
            float x = area.x;

            RockerStepper(page, new Rect(x, area.y, slot, TransportHeight), "SEEK", AvTokens.FontSmall,
                () => manager?.SeekStation(-1), () => manager?.SeekStation(1),
                "Seek the previous station down the band.", "Seek the next station up the band.");
            x += slot + gap;

            RockerStepper(page, new Rect(x, area.y, slot, TransportHeight), "TUNE", AvTokens.FontSmall,
                () => manager?.StepDial(-1), () => manager?.StepDial(1),
                "Tune one step down. Between channels is dead air.",
                "Tune one step up. Between channels is dead air.");
            x += slot + gap;

            rx.Monitor = AvKit.Button(page, "MONITOR", new Rect(x, area.y, slot, TransportHeight),
                () => manager?.TogglePlayback(), AvTokens.FontSmall, AvButtonStyle.Primary)
                .WithTooltip(MonitorTip);
            x += slot + gap;

            rx.Scan = AvKit.Button(page, "SCAN", new Rect(x, area.y, slot, TransportHeight),
                () => manager?.ToggleScan(), AvTokens.FontSmall, AvButtonStyle.Toggle)
                .WithTooltip(ScanTip);
            x += slot + gap;

            AvKit.Button(page, "STOP", new Rect(x, area.y, slot, TransportHeight),
                () => manager?.Stop(), AvTokens.FontSmall, AvButtonStyle.Danger)
                .WithTooltip("Sign off and hand the soundtrack bus back to the game.");
        }

        private static void BuildBandKeys(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float keyWidth = (area.width - gap * 2f) / 3f;
            float x = area.x;
            rx.BandFm = AvKit.Button(page, "FM BROADCAST", new Rect(x, area.y, keyWidth, BandHeight),
                () => manager?.SetBand(RadioBand.Fm), AvTokens.FontSmall, AvButtonStyle.Toggle)
                .WithTooltip(BandFmTip);
            rx.BandAir = AvKit.Button(page, "AIR BAND", new Rect(x += keyWidth + gap, area.y, keyWidth, BandHeight),
                () => manager?.SetBand(RadioBand.Air), AvTokens.FontSmall, AvButtonStyle.Toggle)
                .WithTooltip(BandAirTip);
            rx.BandMw = AvKit.Button(page, "MW BROADCAST", new Rect(x += keyWidth + gap, area.y, keyWidth, BandHeight),
                () => manager?.SetBand(RadioBand.Mw), AvTokens.FontSmall, AvButtonStyle.Toggle)
                .WithTooltip(BandMwTip);
        }

        private static void BuildSetupKeys(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float keyWidth = (area.width - gap * 2f) / 3f;
            float x = area.x;
            rx.Mode = AvKit.Button(page, "MODE · AUTO", new Rect(x, area.y, keyWidth, ControlHeight),
                () => manager?.CycleMode(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Cycle AUTO, FM and AM. A forced mode on the wrong station garbles it.");
            rx.Bandwidth = AvKit.Button(page, "BANDWIDTH · WIDE", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.ToggleBandwidth(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Wide or narrow passband. Narrow trades audio quality for less noise.");
            rx.Step = AvKit.Button(page, "STEP · 100 kHz", new Rect(x += keyWidth + gap, area.y, keyWidth, ControlHeight),
                () => manager?.ToggleFineTuning(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Channel step, or a five-times finer tuning step.");
        }

        private static void BuildSteppers(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float half = (area.width - gap) * 0.5f;
            rx.Volume = RockerStepper(page, new Rect(area.x, area.y, half, ControlHeight), "VOLUME · 100%",
                AvTokens.FontMicro, () => manager?.NudgeVolume(-0.1f), () => manager?.NudgeVolume(0.1f),
                "Turn the receiver volume down.", "Turn the receiver volume up.");
            rx.Squelch = RockerStepper(page, new Rect(area.x + half + gap, area.y, half, ControlHeight), "SQUELCH · 15",
                AvTokens.FontMicro, () => manager?.NudgeSquelch(-0.05f), () => manager?.NudgeSquelch(0.05f),
                "Lower the squelch: a weaker station can open the audio.",
                "Raise the squelch: only a stronger station opens the audio.");
        }

        /// <summary>
        /// A compact rocker: quiet arrows at the edges, one accented value between them. The
        /// transport keys and the AF/SQL steppers are all built from it, so the control rows
        /// share one shape, one slot width and one label treatment.
        /// </summary>
        private static TMP_Text RockerStepper(
            RectTransform page, Rect area, string text, float fontSize,
            Action down, Action up, string downTip, string upTip)
        {
            AvKit.Panel(page, area, AvTheme.SurfaceInert, AvSprites.Control);
            AvKit.Outline(page, area, AvTheme.Frame);
            const float arrow = 24f;
            AvKit.Button(page, "-", new Rect(area.x + 1f, area.y - 1f, arrow, area.height - 2f),
                down, AvTokens.FontMicro, AvButtonStyle.Quiet).WithTooltip(downTip);
            AvKit.Button(page, "+", new Rect(area.x + area.width - arrow - 1f, area.y - 1f, arrow, area.height - 2f),
                up, AvTokens.FontMicro, AvButtonStyle.Quiet).WithTooltip(upTip);

            // Keep the readout in the gap between arrow keys at every bezel size.
            TMP_Text label = AvStyled.Label(page,
                new Rect(area.x + arrow + 2f, area.y,
                    Mathf.Max(0f, area.width - 2f * arrow - 4f), area.height),
                text, "row-main", align: TextAlignmentOptions.Center);
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = AvTheme.Accent;
            label.characterSpacing = 0f;
            NoWrap(label);
            return label;
        }

        // --------------------------------------------------------------------- music page

        private static RectTransform Group(RectTransform page)
        {
            var go = new GameObject("Group", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(page, false);
            AvKit.Place(rect, new Rect(0f, 0f, Width, 0f));
            return rect;
        }

        private static void BuildDeck(RectTransform page, Rect area)
        {
            deck = new DeckUi();
            float x = area.x + AvScreen.SpineInset;
            float w = area.width - AvScreen.SpineInset - 12f;
            float y = area.y;

            // Slack rides the track pitch and the empty card, never a dead band: the list's
            // block and the card are both sized from the pitch, so they end on the last pixel.
            float slack = Mathf.Max(0f, area.height - DeckNaturalHeight);
            float trackPitch = TrackPitch + slack / TrackRows;

            AvStyled.Spine(page, new Rect(area.x, area.y, 3f, area.height));

            BuildDeckCard(page, new Rect(x, y, w, MusicCard));
            y -= MusicCard + AvTokens.Gap;

            BuildDeckTransport(page, new Rect(x, y, w, MusicTransport));
            y -= MusicTransport + AvTokens.Gap;

            TMP_Text note = null;
            float headerTop = y;
            y = SectionHeader(page, x, y, w, "MUSIC LIBRARY", "0 TRACKS", 88f, out note);
            deck.LibraryNote = note;
            AvKit.Button(page, "RESCAN", new Rect(x + w - 72f, headerTop - 2f, 72f, 20f),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Rescan the local music library for new folders and tracks.");

            float listTop = y;
            RectTransform list = Group(page);
            deck.ListRoot = list.gameObject;

            CompactStepper(list, new Rect(x, y, w, FolderRow), "0 / 0",
                () => NudgeFolder(-1), () => NudgeFolder(1),
                FolderPreviousTip, FolderNextTip,
                out deck.FolderValue, out deck.FolderPrevious, out deck.FolderNext);
            y -= FolderRow + AvTokens.Gap;

            for (int i = 0; i < TrackRows; i++)
                deck.Rows[i] = MakeTrackRow(list, x, y - i * trackPitch, w, trackPitch);
            y -= trackPitch * TrackRows + AvTokens.Gap;

            CompactStepper(list, new Rect(x, y, w, PagerHeight), "0 / 0",
                PreviousTrackPage, NextTrackPage,
                TrackPreviousTip, TrackNextTip,
                out deck.PageValue, out deck.PagePrevious, out deck.PageNext);
            y -= PagerHeight + AvTokens.Gap;

            BuildEmptyLibrary(page, new Rect(x, listTop, w, EmptyCard + slack));

            BuildDeckUtility(page, new Rect(x, y, w, UtilityHeight));
        }

        private static void BuildDeckCard(RectTransform page, Rect area)
        {
            AvKit.TacticalCard(page, area, AvTheme.RailInfo);

            deck.FolderLabel = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 8f, area.width - AvTokens.Space4 - 120f, 14f),
                "LOCAL MUSIC", "section-title");
            NoWrap(deck.FolderLabel);
            deck.Position = AvStyled.Label(page,
                new Rect(area.x + area.width - AvTokens.Space2 - 116f, area.y - 10f, 116f, 14f),
                string.Empty, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            NoWrap(deck.Position);

            deck.TrackLabel = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 26f, area.width - AvTokens.Space4, 22f),
                "NO TRACK", "row-name", align: TextAlignmentOptions.MidlineLeft);
            NoWrap(deck.TrackLabel);

            deck.Elapsed = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2, area.y - 52f, 92f, 26f),
                "00:00", "readout", align: TextAlignmentOptions.MidlineLeft);
            deck.Duration = AvStyled.Label(page,
                new Rect(area.x + AvTokens.Space2 + 96f, area.y - 60f, 92f, 18f),
                "/ --:--", "readout-unit", align: TextAlignmentOptions.MidlineLeft);

            deck.Progress = MakeBar(page,
                new Rect(area.x + AvTokens.Space2, area.y - 96f, area.width - AvTokens.Space4, 8f),
                AvTheme.RailInfo);
        }

        private static void BuildDeckTransport(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float keyWidth = (area.width - gap * 3f) / 4f;
            float x = area.x;
            AvKit.Button(page, "PREVIOUS", new Rect(x, area.y, keyWidth, MusicTransport),
                () => manager?.DeckPrevious(), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Previous track in this folder.");
            deck.Play = AvKit.Button(page, "PLAY", new Rect(x += keyWidth + gap, area.y, keyWidth, MusicTransport),
                () => manager?.DeckTogglePlayback(), AvTokens.FontBody, AvButtonStyle.Primary)
                .WithTooltip(PlayTip);
            AvKit.Button(page, "NEXT", new Rect(x += keyWidth + gap, area.y, keyWidth, MusicTransport),
                () => manager?.DeckNext(), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Next track in this folder.");
            AvKit.Button(page, "STOP", new Rect(x += keyWidth + gap, area.y, keyWidth, MusicTransport),
                () => manager?.DeckStop(), AvTokens.FontSmall, AvButtonStyle.Danger)
                .WithTooltip("Stop the deck. The receiver keeps the soundtrack bus if it is on air.");
        }

        private static void BuildEmptyLibrary(RectTransform page, Rect area)
        {
            RectTransform group = Group(page);
            deck.EmptyRoot = group.gameObject;
            AvKit.TacticalCard(group, area, AvTheme.Warning, hasRail: false);
            // The message block keeps its 356px composition and centres in whatever the card
            // grew to, so the space slack added reads as a taller card, not a top-heavy one.
            float shift = -(area.height - EmptyCard) * 0.5f;
            AvStyled.Label(group, new Rect(area.x, area.y - 128f + shift, area.width, 20f),
                "NO MUSIC FOUND", "row-name", align: TextAlignmentOptions.Center);
            AvStyled.Label(group, new Rect(area.x + AvTokens.Space4, area.y - 156f + shift,
                    area.width - AvTokens.Space4 * 2f, 40f),
                "ADD .OGG OR .WAV FILES TO BEPINEX/PLUGINS/BOSCALISUMMER/MUSIC, THEN PRESS RESCAN",
                "row-sub", align: TextAlignmentOptions.Center);

            const float buttonWidth = 200f;
            AvKit.Button(group, "OPEN MUSIC FOLDER",
                new Rect(area.x + (area.width - buttonWidth) * 0.5f, area.y - 212f + shift, buttonWidth, 32f),
                () => manager?.OpenLibraryFolder(), AvTokens.FontMicro, AvButtonStyle.Primary)
                .WithTooltip("Open the local music folder. OGG and WAV files only.");
            group.gameObject.SetActive(false);
        }

        private static void BuildDeckUtility(RectTransform page, Rect area)
        {
            float gap = AvTokens.Gap;
            float keyWidth = (area.width - gap * 3f) / 4f;
            float x = area.x;
            deck.Shuffle = AvKit.Button(page, "SHUFFLE", new Rect(x, area.y, keyWidth, UtilityHeight),
                () => manager?.DeckToggleShuffle(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Random track order, for the music page and the radio programme.");
            deck.Repeat = AvKit.Button(page, "REPEAT", new Rect(x += keyWidth + gap, area.y, keyWidth, UtilityHeight),
                () => manager?.DeckToggleRepeat(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Repeat the current track instead of advancing.");
            AvKit.Button(page, "OPEN FOLDER", new Rect(x += keyWidth + gap, area.y, keyWidth, UtilityHeight),
                () => manager?.OpenLibraryFolder(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Open the local music folder. OGG and WAV files only.");
            AvKit.Button(page, "STOP ALL", new Rect(x += keyWidth + gap, area.y, keyWidth, UtilityHeight),
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
            hoverKhz = int.MinValue;
            if (scopeCursor != null) scopeCursor.enabled = false;
            idleScopeNote = BandRangeText();
            if (rx?.ScopeNote != null) rx.ScopeNote.text = idleScopeNote;
            int min = RadioBands.Min(band);
            int max = RadioBands.Max(band);
            int step = RadioBands.Step(band);
            int minorStep = Mathf.Max(step, (max - min) / 60 / step * step);
            int labelStep = band == RadioBand.Mw ? 200 : 2000;

            AvKit.Rule(rootRect, new Rect(dialArea.x, dialArea.y - 2f, dialArea.width, 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.5f)));

            // Every scope position — ticks, labels, station markers, needle and the hover
            // caret — is DialX over the same band range, so a value printed on the ruler and
            // a caret sitting on that value are the same place by construction. Edge labels
            // anchor to the edge instead of clamping their centre away from their tick.
            for (int khz = min; khz <= max; khz += minorStep)
            {
                bool major = (khz - min) % labelStep == 0;
                float x = DialX((khz - min) / (float)(max - min));
                AvKit.Rule(rootRect, new Rect(x, dialArea.y - 2f, 1f, major ? 8f : 5f),
                    AvTheme.Unity(AvTokens.Hairline.WithAlpha(major ? 0.55f : 0.28f)));
                if (!major) continue;

                float labelLeft = Mathf.Clamp(x - 30f, dialArea.x, dialArea.x + dialArea.width - 60f);
                TextAlignmentOptions align = x <= dialArea.x + 30f ? TextAlignmentOptions.MidlineLeft
                    : x >= dialArea.x + dialArea.width - 30f ? TextAlignmentOptions.MidlineRight
                    : TextAlignmentOptions.Center;
                AvKit.Label(rootRect, RadioBands.Format(band, khz),
                    new Rect(labelLeft, dialArea.y - 20f, 60f, 13f),
                    AvTheme.Dim, AvTokens.FontSmall, FontStyles.Normal, align);
            }

            dialNeedle = AvKit.Rule(rootRect,
                new Rect(dialArea.x, waterfallArea.y, 2f, scopeBodyHeight), AvTheme.Accent);
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
            if (dialBand != manager.TunedDial.Band)
            {
                EnsureDialBand(manager.TunedDial.Band);
                return;
            }

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
                dialMarkers.Add(AvKit.Rule(rootRect,
                    new Rect(Mathf.Clamp(x - 3f, dialArea.x, dialArea.x + dialArea.width - 6f),
                        dialArea.y - 2f, 6f, 12f),
                    marker));
            }

            if (dialNeedle != null) dialNeedle.rectTransform.SetAsLastSibling();
            markerRevision = manager.StationRevision;
        }

        private static void PlaceNeedle(float fraction)
        {
            if (dialNeedle == null) return;
            AvKit.Place(dialNeedle.rectTransform,
                new Rect(DialX(fraction) - 1f, waterfallArea.y, 2f, scopeBodyHeight));
        }

        private static float DialX(float fraction) =>
            dialArea.x + Mathf.Clamp01(fraction) * (dialArea.width - 1f);

        private static float DialXKhz(int kilohertz)
        {
            int min = RadioBands.Min(dialBand);
            int max = RadioBands.Max(dialBand);
            return DialX((kilohertz - min) / (float)Mathf.Max(1, max - min));
        }

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
                        rx.Frequency.text = SweepFrequencyText(dial.Band, fraction);
                }
            }

            if (Time.unscaledTime >= nextWaterfallAt && waterfall != null &&
                shell != null && shell.Page == TabReceiver)
            {
                nextWaterfallAt = Time.unscaledTime + 0.12f;
                waterfall.Push(manager.Spectrum);
            }

            if (shell == null || shell.Page != TabReceiver || Time.unscaledTime < nextMeterAt) return;
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

        /// <summary>The level bar, the squelch gate drawn on it, and the two reports.</summary>
        private static void UpdateMeter()
        {
            if (manager == null || rx?.Meter == null) return;

            float level;
            string report;
            string detail = string.Empty;
            string token;
            string tokenState = "inert";
            Color colour = AvTheme.Dim;

            if (!manager.HasChannels)
            {
                level = 0f;
                report = "S0";
                detail = "NO STATIONS";
                token = "NO LIB";
            }
            else if (manager.IsScanning || sweeping)
            {
                level = 0.25f + Mathf.PerlinNoise(Time.unscaledTime * 2.5f, 0.37f) * 0.35f;
                report = "SEEKING";
                detail = "SCANNING";
                token = "SEEK";
                tokenState = "warn";
                colour = AvTheme.Warning;
            }
            else if (manager.IsOffStation)
            {
                level = 0.06f + Mathf.PerlinNoise(Time.unscaledTime * 3.5f, 0.71f) * 0.16f;
                report = "S0";
                detail = "DEAD AIR";
                token = "NO SIG";
                tokenState = "warn";
                colour = AvTheme.Warning;
            }
            else if (manager.IsOffAir)
            {
                level = 0f;
                report = "S0";
                detail = "OFF AIR";
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
                detail = open ? reception.DbmReport : "SQL SHUT";
                token = open ? (live ? "LOCK" : "READY") : "SQL";
                tokenState = open ? (live ? "live" : "info") : "warn";
                colour = open ? (live ? AvTheme.RailReady : AvTheme.Dim) : AvTheme.Warning;
            }

            rx.Meter.Report.text = report;
            rx.Meter.Report.color = colour;
            rx.Meter.Detail.text = detail;
            rx.Meter.Level.Fill.color = colour;
            rx.Meter.Level.Set(level);
            AvKit.Place(rx.Meter.Gate.rectTransform,
                new Rect(rx.Meter.Bar.x + rx.Meter.Bar.width * Mathf.Clamp01(manager.Squelch) - 1f,
                    rx.Meter.Bar.y + 2f, 2f, 12f));
            if (dataBar != null && shell != null && shell.Page == TabReceiver)
                dataBar.SetChip(2, token, tokenState);
        }

        // ----------------------------------------------------------------------- row makers

        /// <summary>
        /// One station slot on the grid every row shares: selection rail, index, icon, name,
        /// frequency value and unit, strength. The selected row is latched with a wash, the
        /// rail and brighter text — never a solid plate — and the strength keeps its word, so
        /// state is never carried by colour alone. Slots past the last station stay visible as
        /// dim empties, which is what keeps the list block from leaving a dead band above the
        /// transport on a short list.
        /// </summary>
        private static StationRow MakeStationRow(RectTransform page, float x, float y, float width)
        {
            const float railWidth = 3f;
            const float indexWidth = 14f;
            const float iconSize = 24f;
            // Wide enough for the longest band reading, "118.250"; the unit has its own column.
            const float frequencyWidth = 74f;
            const float unitWidth = 34f;
            const float strengthWidth = 88f;

            Image ground = AvKit.Panel(page, new Rect(x, y, width, PresetPitch - 2f), AvTheme.Unity(AvTokens.Surface), AvSprites.Control);
            RectTransform rect = ground.rectTransform;
            Image selectionRail = AvKit.Panel(rect, new Rect(0f, 0f, railWidth, PresetPitch - 2f), Color.clear);

            var row = new StationRow
            {
                Ground = ground,
                SelectionRail = selectionRail
            };

            float indexX = railWidth + AvTokens.Space1;
            float iconX = indexX + indexWidth + AvTokens.Space1;
            float nameX = iconX + iconSize + AvTokens.Space2;
            float strengthX = width - strengthWidth;
            float unitX = strengthX - unitWidth - AvTokens.Space2;
            float frequencyX = unitX - frequencyWidth;

            row.Preset = AvStyled.Label(rect, new Rect(indexX, 0f, indexWidth, PresetPitch),
                "1", "row-value-unit");
            row.BadgeGround = AvKit.Panel(rect, new Rect(iconX, -3f, iconSize, iconSize), AvTheme.SurfaceInert, AvSprites.Slot);
            row.Icon = AvKit.Panel(row.BadgeGround.rectTransform, new Rect(1f, -1f, iconSize - 2f, iconSize - 2f), Color.white);
            row.Icon.preserveAspect = true;
            row.Icon.enabled = false;
            row.Badge = AvStyled.Label(row.BadgeGround.rectTransform,
                new Rect(0f, 0f, iconSize, iconSize), "--", "row-name", align: TextAlignmentOptions.Center);

            row.Name = AvStyled.Label(rect,
                new Rect(nameX, 0f, Mathf.Max(0f, frequencyX - nameX - AvTokens.Space2), PresetPitch),
                "—", "row-name");
            NoWrap(row.Name);
            row.Frequency = AvStyled.Label(rect, new Rect(frequencyX, 0f, frequencyWidth, PresetPitch), "--", "row-value");
            NoWrap(row.Frequency);
            row.Unit = AvStyled.Label(rect, new Rect(unitX, 0f, unitWidth, PresetPitch), string.Empty, "row-value-unit");
            NoWrap(row.Unit);
            row.Status = AvStyled.Label(rect, new Rect(strengthX, 0f, strengthWidth - 8f, PresetPitch),
                string.Empty, "row-value", align: TextAlignmentOptions.MidlineRight);
            NoWrap(row.Status);

            StationRow captured = row;
            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, width, PresetPitch), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && captured.Index >= 0) manager.SelectChannel(captured.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, AvTheme.Unity(AvTokens.Surface), RowHoverWash);
            row.Button = button;
            return row;
        }

        private static Color RowHoverWash =>
            AvTheme.Unity(AvTokens.RowFill(AvTheme.Accent.ToRgba(), selected: false, hover: true));

        private static TrackRow MakeTrackRow(RectTransform page, float x, float y, float width, float pitch)
        {
            Image ground = AvKit.Panel(page, new Rect(x, y, width, pitch), Color.clear);
            RectTransform rect = ground.rectTransform;
            Image rule = AvKit.Rule(rect, new Rect(0f, 0f, 3f, pitch), Color.clear);

            var row = new TrackRow
            {
                Root = ground.gameObject,
                Ground = ground,
                Rule = rule
            };

            row.Number = AvStyled.Label(rect, new Rect(6f, 0f, 24f, pitch), "1", "row-value-unit");
            row.Title = AvStyled.Label(rect, new Rect(38f, 0f, width - 46f, pitch),
                string.Empty, "row-main", align: TextAlignmentOptions.MidlineLeft);
            NoWrap(row.Title);
            row.Position = MakeBar(rect, new Rect(38f, -(pitch - 2f), width - 46f, 2f), AvTheme.RailInfo, track: false);
            row.Position.Fill.enabled = false;

            TrackRow captured = row;
            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, width, pitch), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && captured.Index >= 0) manager.DeckPlay(captured.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear,
                AvTheme.Unity(AvTokens.RowFill(AvTheme.RailInfo.ToRgba(), selected: false, hover: true)));
            row.Button = button;
            return row;
        }

        // ------------------------------------------------------------------ page navigation

        private static void PreviousStationPage()
        {
            if (stationPage > 0) stationPage--;
            nextRefresh = 0f;
        }

        private static void NextStationPage()
        {
            int pages = manager == null ? 1 : Math.Max(1,
                (manager.ChannelCount + PresetRows - 1) / PresetRows);
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
            // Switching tabs forces a refresh, so hidden controls need no 6 Hz text pass.
            if (shell.Page == TabReceiver)
            {
                if (markerRevision != manager.StationRevision) RefreshDialMarkers();
                RefreshReceiver();
            }
            else RefreshDeck();
            RefreshDataBar();
        }

        private static void RefreshReceiver()
        {
            bool hasChannels = manager.HasChannels;
            bool offStation = manager.IsOffStation || manager.IsOffAir;
            RadioDial dial = manager.TunedDial;
            string idle = BandRangeText();
            if (idle != idleScopeNote)
            {
                idleScopeNote = idle;
                if (hoverKhz == int.MinValue && rx.ScopeNote != null) rx.ScopeNote.text = idle;
            }

            rx.Frequency.text = dial.FrequencyText;
            rx.Unit.text = dial.UnitText;
            rx.ModeLine.text = hasChannels
                ? manager.BroadcastMode + " · " + StepText(dial.Band)
                : "NO LIBRARY";

            if (hasChannels && !offStation)
            {
                rx.Station.text = manager.CurrentChannelName;
                rx.Program.text = manager.CurrentProgram;
                SetCardRail(manager.IsEngaged && !manager.IsPaused ? AvTheme.RailReady : AvTheme.RailInert);
            }
            else if (hasChannels)
            {
                rx.Station.text = manager.IsOffAir ? manager.CurrentChannelName : "NO SIGNAL";
                rx.Program.text = string.Empty;
                SetCardRail(manager.IsOffAir ? AvTheme.Alert : AvTheme.Warning);
            }
            else
            {
                rx.Station.text = "NO STATIONS";
                rx.Program.text = string.Empty;
                SetCardRail(AvTheme.RailInert);
            }
            rx.StationsNote.text = hasChannels ? manager.ChannelCount + " FOUND" : "0 FOUND";

            rx.OnAir.text = !hasChannels ? "NO STATIONS"
                : manager.IsOffAir ? "OFF AIR"
                : offStation ? "DEAD AIR" : "ON AIR · " + manager.CurrentTrackTitle;
            rx.Progress.Set(offStation ? 0f : manager.Progress);
            rx.Time.text = offStation ? "--:-- / --:--"
                : FormatTime(manager.Elapsed) + " / " + FormatTime(manager.Duration);

            rx.Volume.text = "VOLUME · " + Mathf.RoundToInt(manager.VolumeLevel * 100f) + "%";
            rx.Squelch.text = "SQUELCH · " + Mathf.RoundToInt(manager.Squelch * 100f);

            rx.Monitor.SetText(manager.IsPaused ? "RESUME" : manager.IsEngaged ? "PAUSE" : "MONITOR");
            rx.Monitor.SetLatched(manager.IsEngaged && !manager.IsPaused);
            rx.Scan.SetLatched(manager.IsScanning);
            rx.BandFm.SetLatched(dial.Band == RadioBand.Fm);
            rx.BandAir.SetLatched(dial.Band == RadioBand.Air);
            rx.BandMw.SetLatched(dial.Band == RadioBand.Mw);
            rx.Bandwidth.SetText(manager.NarrowBandwidth ? "BANDWIDTH · NARROW" : "BANDWIDTH · WIDE");
            rx.Bandwidth.SetLatched(manager.NarrowBandwidth);
            rx.Step.SetText(manager.FineTuning ? "STEP · FINE" : "STEP · " + StepText(dial.Band));
            rx.Step.SetLatched(manager.FineTuning);
            rx.Mode.SetText("MODE · " + ModeText());
            rx.Mode.SetLatched(manager.ReceiverModulation != dial.Modulation);

            int selectedTracks = hasChannels ? manager.GetChannelTrackCount(manager.SelectedChannel) : 0;
            bool monitorEnabled = selectedTracks > 0 && !offStation;
            rx.Monitor.SetEnabled(monitorEnabled);
            rx.Monitor.WithTooltip(monitorEnabled ? MonitorTip : MonitorOffTip);
            bool scanEnabled = manager.ChannelCount > 1;
            rx.Scan.SetEnabled(scanEnabled);
            rx.Scan.WithTooltip(scanEnabled ? ScanTip : ScanOffTip);
            rx.BandFm.SetEnabled(hasChannels);
            rx.BandFm.WithTooltip(hasChannels ? BandFmTip : BandOffTip);
            rx.BandAir.SetEnabled(hasChannels);
            rx.BandAir.WithTooltip(hasChannels ? BandAirTip : BandOffTip);
            rx.BandMw.SetEnabled(hasChannels);
            rx.BandMw.WithTooltip(hasChannels ? BandMwTip : BandOffTip);

            int pages = Math.Max(1, (manager.ChannelCount + PresetRows - 1) / PresetRows);
            stationPage = Mathf.Clamp(stationPage, 0, pages - 1);
            rx.PageValue.text = RangeText(stationPage, PresetRows, manager.ChannelCount, "NO STATIONS");
            bool previousStation = stationPage > 0;
            rx.PagePrevious.SetEnabled(previousStation);
            rx.PagePrevious.WithTooltip(previousStation ? StationPreviousTip : FirstPageTip);
            bool nextStation = stationPage + 1 < pages;
            rx.PageNext.SetEnabled(nextStation);
            rx.PageNext.WithTooltip(nextStation ? StationNextTip : LastPageTip);

            Color rest = AvTheme.Unity(AvTokens.Surface);
            Color selectedWash = AvTheme.Unity(AvTokens.RowFill(AvTheme.Accent.ToRgba(), selected: true));

            for (int i = 0; i < PresetRows; i++)
            {
                StationRow row = rx.Rows[i];
                if (row == null) continue;
                int index = stationPage * PresetRows + i;
                bool filled = index < manager.ChannelCount;
                row.Index = filled ? index : -1;

                if (!filled)
                {
                    row.Preset.text = (index + 1).ToString();
                    row.Name.text = "EMPTY PRESET";
                    row.Frequency.text = string.Empty;
                    row.Unit.text = string.Empty;
                    row.Status.text = string.Empty;
                    row.Name.color = AvTheme.Disabled;
                    row.Frequency.color = AvTheme.Disabled;
                    row.Status.color = AvTheme.Disabled;
                    row.Preset.color = AvTheme.Disabled;
                    row.Badge.text = string.Empty;
                    if (row.Badge.gameObject.activeSelf) row.Badge.gameObject.SetActive(false);
                    row.Icon.enabled = false;
                    row.BadgeGround.color = Color.clear;
                    row.SelectionRail.color = Color.clear;
                    row.Button.SetEnabled(false);
                    row.Button.WithTooltip(null);
                    row.Button.SetRowHighlight(row.Ground, rest, RowHoverWash);
                    continue;
                }

                row.Preset.text = (index + 1).ToString();
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
                row.Frequency.text = rowDial.FrequencyText;
                row.Unit.text = rowDial.UnitText;

                float strength = Mathf.Clamp01(manager.GetChannelStrength(index));
                bool modelled = manager.GetChannelHasTransmitter(index);
                bool offAirRow = manager.GetChannelOffAir(index);
                if (offAirRow)
                {
                    row.Status.text = "OFF AIR";
                    row.Status.color = AvTheme.Alert;
                }
                else if (!modelled)
                {
                    row.Status.text = manager.GetChannelTrackCount(index) + " TRK";
                    row.Status.color = AvTheme.Dim;
                }
                else if (strength >= 0.7f)
                {
                    row.Status.text = "STRONG";
                    row.Status.color = AvTheme.RailReady;
                }
                else if (strength >= 0.35f)
                {
                    row.Status.text = "FAIR";
                    row.Status.color = AvTheme.Warning;
                }
                else
                {
                    row.Status.text = "WEAK";
                    row.Status.color = AvTheme.Alert;
                }

                bool selected = index == manager.SelectedChannel && !offStation;
                row.Name.color = selected ? AvTheme.TextPrimary : AvTheme.Dim;
                row.Frequency.color = selected ? AvTheme.TextPrimary : AvTheme.Dim;
                row.Preset.color = selected ? AvTheme.TextPrimary : AvTheme.Disabled;
                row.SelectionRail.color = selected ? AvTheme.Accent : Color.clear;
                row.Button.SetEnabled(true);
                row.Button.SetRowHighlight(row.Ground, selected ? selectedWash : rest, RowHoverWash);
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
            int track = trackCount == 0 ? 0 : manager.DeckTrackIndex + 1;
            bool playing = manager.DeckEngaged;

            if (deck.ListRoot.activeSelf != hasFolders) deck.ListRoot.SetActive(hasFolders);
            if (deck.EmptyRoot.activeSelf == hasFolders) deck.EmptyRoot.SetActive(!hasFolders);

            deck.FolderLabel.text = hasFolders
                ? "LOCAL MUSIC · " + manager.DeckFolderName(folder)
                : "LOCAL MUSIC";
            deck.Position.text = hasFolders && trackCount > 0
                ? "TRACK " + track + " / " + trackCount
                : string.Empty;
            deck.TrackLabel.text = hasFolders ? manager.DeckCurrentTitle : "NO MUSIC FOUND";
            deck.Elapsed.text = playing ? FormatTime(manager.DeckElapsed) : "00:00";
            deck.Duration.text = "/ " + (playing ? FormatTime(manager.DeckDuration) : "--:--");
            deck.Progress.Set(playing ? manager.DeckProgress : 0f);

            bool playEnabled = hasFolders && trackCount > 0;
            deck.Play.SetText(manager.DeckPaused ? "RESUME" : manager.DeckPlaying ? "PAUSE" : hasFolders ? "PLAY" : "NO LIB");
            deck.Play.SetEnabled(playEnabled);
            deck.Play.WithTooltip(playEnabled ? PlayTip : hasFolders ? PlayNoTracksTip : PlayNoLibraryTip);
            // The word carries the latch; the wash is only its echo.
            deck.Shuffle.SetText(manager.Shuffle ? "SHUFFLE ON" : "SHUFFLE OFF");
            deck.Repeat.SetText(manager.RepeatTrack ? "REPEAT ON" : "REPEAT OFF");
            deck.Shuffle.SetLatched(manager.Shuffle);
            deck.Repeat.SetLatched(manager.RepeatTrack);
            deck.LibraryNote.text = hasFolders ? trackCount + " TRACKS" : "NO TRACKS";

            // The stepper stays a page counter; the unbounded folder name lives on the card header.
            deck.FolderValue.text = hasFolders
                ? (folder + 1) + " / " + folders + " · " + trackCount + " TRK"
                : "0 / 0";
            bool previousFolder = hasFolders && folder > 0;
            deck.FolderPrevious.SetEnabled(previousFolder);
            deck.FolderPrevious.WithTooltip(previousFolder ? FolderPreviousTip
                : hasFolders ? FirstFolderTip : PlayNoTracksTip);
            bool nextFolder = hasFolders && folder + 1 < folders;
            deck.FolderNext.SetEnabled(nextFolder);
            deck.FolderNext.WithTooltip(nextFolder ? FolderNextTip
                : hasFolders ? LastFolderTip : PlayNoTracksTip);

            int pages = Math.Max(1, (trackCount + TrackRows - 1) / TrackRows);
            trackPage = Mathf.Clamp(trackPage, 0, pages - 1);
            deck.PageValue.text = RangeText(trackPage, TrackRows, trackCount, "NO TRACKS");
            bool previousPage = trackPage > 0;
            deck.PagePrevious.SetEnabled(previousPage);
            deck.PagePrevious.WithTooltip(previousPage ? TrackPreviousTip
                : trackCount > 0 ? FirstPageTip : PlayNoTracksTip);
            bool nextPage = trackPage + 1 < pages;
            deck.PageNext.SetEnabled(nextPage);
            deck.PageNext.WithTooltip(nextPage ? TrackNextTip
                : trackCount > 0 ? LastPageTip : PlayNoTracksTip);

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
                row.Number.text = active ? ">" : (index + 1).ToString("00");
                string title = manager.DeckTrackTitle(index);
                if (row.Title.text != title)
                {
                    row.Title.text = title;
                    row.Button?.WithTooltip("Play " + title + ".");
                }
                row.Number.color = active ? AvTheme.RailInfo : AvTheme.Disabled;
                row.Title.color = active ? AvTheme.RailInfo : AvTheme.Unity(AvTokens.TextDim);
                row.Button?.SetRowHighlight(row.Ground,
                    active ? AvTheme.RailInfo.WithAlpha(0.1f) : Color.clear,
                    AvTheme.Unity(AvTokens.RowFill(AvTheme.RailInfo.ToRgba(), active, hover: true)));
                row.Rule.color = active ? AvTheme.RailInfo : Color.clear;
                if (row.Position.Fill.enabled != active) row.Position.Fill.enabled = active;
                if (active) row.Position.Set(manager.DeckProgress);
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
                string receiverState = manager.IsOffAir ? "OFF AIR"
                    : manager.IsScanning ? "SCANNING"
                    : manager.IsOffStation ? "DEAD AIR"
                    : playing ? "ON AIR"
                    : manager.IsPaused ? "PAUSED"
                    : manager.HasChannels ? "STANDBY" : "NO LIBRARY";
                dataBar.State.text = "RECEIVER / " + receiverState;
                dataBar.State.color = manager.IsOffAir ? AvTheme.Alert
                    : manager.IsScanning || manager.IsOffStation ? AvTheme.Warning
                    : playing ? AvTheme.RailReady
                    : manager.HasChannels ? AvTheme.Dim : AvTheme.Warning;
                dataBar.SetChip(0, manager.HasChannels ? manager.Reception.SReport + " " + dial.BandText : "--",
                    manager.HasChannels ? "live" : "inert");
                dataBar.SetChip(1, dial.BandText + " " + StepText(dial.Band),
                    manager.HasChannels ? "info" : "inert");
                UpdateMeter();

                string alert = !manager.HasChannels
                    ? "NO STATIONS · ADD OGG/WAV FOLDERS, THEN PRESS RESCAN"
                    : manager.IsOffAir
                        ? "TOWER LOST · " + manager.CurrentChannelName + " IS OFF AIR ON THIS MISSION"
                        : manager.IsOffStation
                            ? "DEAD AIR · TUNE BACK TO A STATION OR CLICK THE SCOPE"
                            : null;
                string ambient = manager.Status + " · RECEIVE ONLY";
                if (manager.HasChannels && !manager.IsOffAir)
                    ambient += manager.ReceptionModelled
                        ? " · RECEPTION MODELLED: " + Mathf.RoundToInt(manager.TowerDistanceKm) + " KM · " +
                          (manager.TowerLineOfSight ? "LOS CLEAR" : "TERRAIN BLOCKED")
                        : " · LOCAL ARCHIVE, SIGNAL READS FULL";
                shell.WriteStatus(alert, null, ambient);
                return;
            }

            string deckState = manager.DeckPlaying ? "PLAYING"
                : manager.DeckPaused ? "PAUSED"
                : manager.DeckFolderCount > 0 ? "READY" : "EMPTY";
            dataBar.State.text = "MUSIC / " + deckState;
            dataBar.State.color = manager.DeckPlaying ? AvTheme.RailInfo
                : manager.DeckFolderCount > 0 ? AvTheme.Dim : AvTheme.Warning;
            dataBar.SetChip(0, "LOCAL", "info");
            dataBar.SetChip(1, manager.DeckFolderCount + " FOLDERS",
                manager.DeckFolderCount > 0 ? "info" : "inert");
            dataBar.SetChip(2, manager.DeckTrackCount + " TRACKS",
                manager.DeckTrackCount > 0 ? "live" : "inert");
            shell.WriteStatus(
                manager.DeckFolderCount > 0 ? null
                    : "NO FOLDERS YET · PRESS OPEN FOLDER, ADD OGG/WAV TRACKS, THEN RESCAN",
                null, manager.DeckStatus + " · LOCAL LIBRARY ONLY, NOTHING IS DOWNLOADED OR SENT");
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
            return step >= 1000
                ? (step / 1000f).ToString("0.#") + " MHz"
                : step + " kHz";
        }

        private static void ApplyStationIcon(Image image, TMP_Text fallback, string path, string fallbackText)
        {
            Sprite sprite = RadioStationIconCache.Get(path);
            bool available = sprite != null && image != null;
            if (image != null)
            {
                image.sprite = sprite;
                image.enabled = available;
            }
            if (fallback == null) return;
            fallback.text = fallbackText;
            fallback.gameObject.SetActive(!available);
        }

        private static string FormatTime(float seconds)
        {
            int value = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (value / 60).ToString("00") + ":" + (value % 60).ToString("00");
        }

        /// <summary>"1 - 5 OF 12": a paged list reads as a range even when it is one page.</summary>
        private static string RangeText(int page, int perPage, int count, string empty)
        {
            if (count <= 0) return empty;
            int first = page * perPage + 1;
            int last = Math.Min(count, first + perPage - 1);
            return first + " - " + last + " OF " + count;
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

    /// <summary>
    /// Turns pointer positions over the band scope into a fraction of the ruler, so the scope
    /// itself is the tuning control. A plain pointer handler, not a Selectable, so it never
    /// takes navigation focus from the flight controls.
    /// </summary>
    internal sealed class RadioScopeInput : MonoBehaviour, IPointerMoveHandler, IPointerClickHandler,
        IPointerExitHandler, IPointerEnterHandler
    {
        public RectTransform Area;
        public Action<float> Moved;
        public Action<float> Clicked;
        public Action Exited;

        private const string Tip = "Hover to preview a channel, click to tune to it. Stations are on the grid.";

        public void OnPointerMove(PointerEventData eventData) =>
            Moved?.Invoke(Fraction(eventData, eventData.enterEventCamera));

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke(Fraction(eventData, eventData.pressEventCamera));
            AvInput.Deselect(gameObject);
        }

        public void OnPointerEnter(PointerEventData eventData) => AvButton.PublishExternal(Tip, true);

        public void OnPointerExit(PointerEventData eventData)
        {
            AvButton.PublishExternal(Tip, false);
            Exited?.Invoke();
        }

        private float Fraction(PointerEventData eventData, Camera camera)
        {
            if (Area == null) return 0f;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Area, eventData.position, camera, out Vector2 local))
                return 0f;
            float width = Mathf.Max(1f, Area.rect.width);
            return Mathf.Clamp01(local.x / width);
        }
    }
}
