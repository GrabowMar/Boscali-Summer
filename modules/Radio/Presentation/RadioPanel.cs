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
    /// The receiver and the music deck in one set, on two kit v2 pages.
    ///
    /// <para>RECEIVER leads with the tuned frequency (<see cref="AvReadout"/>), the station and
    /// its programme (<see cref="AvRow"/>), an S-meter with a visible squelch gate
    /// (<see cref="SignalMeterPart"/>), then hands the middle of the page to the band scope: a
    /// waterfall, ruler and needle that is also the tuning control (<see cref="BandScopePart"/>
    /// — hover to preview a channel, click to tune to it). The preset list is a fixed pool of
    /// <see cref="AvRow"/> lines with an <see cref="AvStepper"/> pager; the transport, band keys
    /// and setup keys are <see cref="AvControl"/> rows.</para>
    ///
    /// <para>MUSIC is a player: a now-playing row with a real position bar, a transport, one
    /// folder stepper, one paged track list, and an <see cref="AvAlert"/> that says where files
    /// go when the library is empty.</para>
    /// </summary>
    internal static class RadioPanel
    {
        private const int TabReceiver = 0;
        private const int TabDeck = 1;
        private const int PresetRows = 5;
        private const int TrackRows = 12;

        // Tooltips are swapped while a control is disabled, so a greyed key always says why.
        private const string MonitorTip = "Monitor or pause the tuned station's programme.";
        private const string MonitorOffTip = "No programme on this station.";
        private const string ScanTip = "Hold each station in the band for a few seconds as it seeks.";
        private const string ScanOffTip = "Only one station in range.";
        private const string BandFmTip = "FM broadcast, 87.5 \u2013 108 MHz in 100 kHz channels. Keeps its last frequency.";
        private const string BandAirTip = "VHF air band, 118 \u2013 137 MHz in 25 kHz channels, AM. Keeps its last frequency.";
        private const string BandMwTip = "Medium wave, 530 \u2013 1700 kHz in 10 kHz channels, AM. Keeps its last frequency.";
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
        private const string OpenFolderTip = "Open the local music folder. OGG and WAV files only.";

        private static MFDScreen screen;
        private static GameObject screenRoot;
        private static AvConsole console;
        private static RadioManager manager;
        private static AvChip[] chips;

        // ------------------------------------------------------------------ receiver widgets
        private static AvReadout freqReadout;
        private static AvRow stationRow;
        private static SignalMeterPart meterPart;
        private static AvSection scopeSection;
        private static BandScopePart scopePart;
        private static AvSection presetsSection;
        private static readonly AvRow[] stationRows = new AvRow[PresetRows];
        private static AvStepper stationPager;
        private static AvSegmented bandSeg;
        private static AvButtons tuningButtons;
        private static AvButtons playbackButtons;
        private static AvButtons setupButtons;
        private static AvStepper volumeStepper;
        private static AvStepper squelchStepper;

        // ----------------------------------------------------------------------- deck widgets
        private static AvSection nowSection;
        private static AvRow nowRow;
        private static ProgressBarPart deckProgressPart;
        private static AvButtons deckTransport;
        private static AvSection librarySection;
        private static AvStepper folderStepper;
        private static readonly AvRow[] trackRows = new AvRow[TrackRows];
        private static AvStepper trackPager;
        private static AvAlert emptyAlert;
        private static AvButtons utilityButtons;
        private static bool deckHadFolders = true;

        // Per-row hover-help and badge caches, so a row only touches its widgets when its content changes.
        private static readonly string[] stationTipName = new string[PresetRows];
        private static readonly bool[] stationHasBadge = new bool[PresetRows];
        private static readonly string[] trackTipName = new string[TrackRows];
        private static int iconRevision = -1;

        private static int stationPage;
        private static int trackPage;
        private static float nextAttempt;
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
            }

            // The console's own AvTicker (a MonoBehaviour on its Root, inside
            // MFDScreen.displayPanel) drives every readout from here on. Unity stops calling its
            // Update() the moment the bezel shows a different screen on this slot, since
            // displayPanel is deactivated then — the same gate the v1 panel enforced by hand.
        }

        public static void Reset()
        {
            MfdBezel.Release(MfdSlots.Rad);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            RadioStationIconCache.Clear();
            scopePart?.Dispose();

            screenRoot = null;
            screen = null;
            console = null;
            manager = null;
            chips = null;

            freqReadout = null;
            stationRow = null;
            meterPart = null;
            scopeSection = null;
            scopePart = null;
            presetsSection = null;
            Array.Clear(stationRows, 0, stationRows.Length);
            stationPager = null;
            bandSeg = null;
            tuningButtons = null;
            playbackButtons = null;
            setupButtons = null;
            volumeStepper = null;
            squelchStepper = null;

            nowSection = null;
            nowRow = null;
            deckProgressPart = null;
            deckTransport = null;
            librarySection = null;
            folderStepper = null;
            Array.Clear(trackRows, 0, trackRows.Length);
            trackPager = null;
            emptyAlert = null;
            utilityButtons = null;
            deckHadFolders = true;

            Array.Clear(stationTipName, 0, stationTipName.Length);
            Array.Clear(stationHasBadge, 0, stationHasBadge.Length);
            Array.Clear(trackTipName, 0, trackTipName.Length);
            iconRevision = -1;

            stationPage = 0;
            trackPage = 0;
            nextAttempt = 0f;
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

                if (!RadioScreens.TryBuild(template, buttons[slot], "RAD", "BOSCALI / RADIO",
                    new[] { (AvIcon.Radio, "RECEIVER"), (AvIcon.Music, "MUSIC") }, out RadioScreen built))
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    Fail("bezel label or highlight missing");
                    return;
                }

                screen = built.Screen;
                screenRoot = built.Root;
                console = built.Console;
                chips = console.Chips(3);

                BuildReceiver(console.Page(TabReceiver));
                BuildDeck(console.Page(TabDeck));
                console.Finish();
                console.PageChanged += _ => { RefreshReceiver(); RefreshDeck(); };
                RefreshReceiver();
                RefreshDeck();

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Rad);
                    UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    console = null;
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

        private static void Fail(string reason)
        {
            gaveUp = true;
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            console = null;
            Plugin.Logger.LogWarning("Radio panel disabled (" + reason + "). Playback remains available through config reload only.");
        }

        // ------------------------------------------------------------------------ receiver page

        private static void BuildReceiver(AvFlow p)
        {
            freqReadout = p.Add(new AvReadout(p.Content));
            stationRow = p.Add(new AvRow(p.Content));
            meterPart = p.Add(new SignalMeterPart(p.Content));

            scopeSection = p.Section(AvIcon.WaveSine, "BAND SCOPE", "TUNE TO A STATION");
            scopePart = p.Add(new BandScopePart(p.Content));
            scopePart.NoteChanged = scopeSection.SetCaption;

            p.Buttons(new AvControl.Spec("RESCAN", () => manager?.Rescan(), AvButtonStyle.Quiet, AvIcon.Refresh))
                .Controls[0].Help = "Sign off, rescan the local music library and rebuild the dial.";

            presetsSection = p.Section(AvIcon.ListDetails, "STATION PRESETS", "0 FOUND");
            for (int i = 0; i < PresetRows; i++)
            {
                int captured = i;
                stationRows[i] = p.Add(new AvRow(p.Content, () => OnStationRowClick(captured)));
            }
            stationPager = p.Add(new AvStepper(p.Content, "PRESET PAGE",
                () => RangeText(stationPage, PresetRows, manager == null ? 0 : manager.ChannelCount, "NO STATIONS"),
                PreviousStationPage, NextStationPage));

            bandSeg = p.Add(new AvSegmented(p.Content, "BAND", new[] { "FM", "AIR", "MW" }, GetBandIndex, SetBandIndex));
            bandSeg.Options[0].Help = BandFmTip;
            bandSeg.Options[1].Help = BandAirTip;
            bandSeg.Options[2].Help = BandMwTip;

            tuningButtons = p.Buttons(
                new AvControl.Spec("SEEK", () => manager?.SeekStation(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("SEEK", () => manager?.SeekStation(1), AvButtonStyle.Quiet, AvIcon.ChevronRight),
                new AvControl.Spec("TUNE", () => manager?.StepDial(-1), AvButtonStyle.Quiet, AvIcon.ArrowLeft),
                new AvControl.Spec("TUNE", () => manager?.StepDial(1), AvButtonStyle.Quiet, AvIcon.ArrowRight));
            tuningButtons.Controls[0].Help = "Seek the previous station down the band.";
            tuningButtons.Controls[1].Help = "Seek the next station up the band.";
            tuningButtons.Controls[2].Help = "Tune one step down. Between channels is dead air.";
            tuningButtons.Controls[3].Help = "Tune one step up. Between channels is dead air.";

            playbackButtons = p.Buttons(
                new AvControl.Spec("MONITOR", () => manager?.TogglePlayback(), AvButtonStyle.Primary, AvIcon.PlayerPlay),
                new AvControl.Spec("SCAN", () => manager?.ToggleScan(), AvButtonStyle.Toggle, AvIcon.Radar2),
                new AvControl.Spec("STOP", () => manager?.Stop(), AvButtonStyle.Danger, AvIcon.PlayerStop));
            playbackButtons.Controls[0].Help = MonitorTip;
            playbackButtons.Controls[1].Help = ScanTip;
            playbackButtons.Controls[2].Help = "Sign off and hand the soundtrack bus back to the game.";

            setupButtons = p.Buttons(
                new AvControl.Spec("MODE", () => manager?.CycleMode(), AvButtonStyle.Toggle),
                new AvControl.Spec("BANDWIDTH", () => manager?.ToggleBandwidth(), AvButtonStyle.Toggle),
                new AvControl.Spec("STEP", () => manager?.ToggleFineTuning(), AvButtonStyle.Toggle));
            setupButtons.Controls[0].Help = "Cycle AUTO, FM and AM. A forced mode on the wrong station garbles it.";
            setupButtons.Controls[1].Help = "Wide or narrow passband. Narrow trades audio quality for less noise.";
            setupButtons.Controls[2].Help = "Channel step, or a five-times finer tuning step.";

            volumeStepper = p.Add(new AvStepper(p.Content, "VOLUME",
                () => manager == null ? "--" : AvNum.Percent(manager.VolumeLevel),
                () => manager?.NudgeVolume(-0.1f), () => manager?.NudgeVolume(0.1f)));
            volumeStepper.Minus.Help = "Turn the receiver volume down.";
            volumeStepper.Plus.Help = "Turn the receiver volume up.";
            squelchStepper = p.Add(new AvStepper(p.Content, "SQUELCH",
                () => manager == null ? "--" : AvNum.Percent(manager.Squelch),
                () => manager?.NudgeSquelch(-0.05f), () => manager?.NudgeSquelch(0.05f)));
            squelchStepper.Minus.Help = "Lower the squelch: a weaker station can open the audio.";
            squelchStepper.Plus.Help = "Raise the squelch: only a stronger station opens the audio.";

            p.Ticker.Add(TabReceiver, AvTickRate.Fast, RefreshReceiver);
            p.Ticker.Add(TabReceiver, AvTickRate.Fast, scopePart.Animate);
        }

        private static void RefreshReceiver()
        {
            if (manager == null || console == null || freqReadout == null) return;

            bool hasChannels = manager.HasChannels;
            bool offStation = manager.IsOffStation || manager.IsOffAir;
            RadioDial dial = manager.TunedDial;

            string modeLine = hasChannels ? manager.BroadcastMode + " · " + StepText(dial.Band) : "NO LIBRARY";
            freqReadout.Set(dial.FrequencyText, dial.UnitText, modeLine);

            string name, ticker, onAir;
            AvState rowState;
            if (hasChannels && !offStation)
            {
                name = manager.CurrentChannelName;
                ticker = string.IsNullOrEmpty(manager.CurrentProgram)
                    ? manager.TickerText
                    : manager.CurrentProgram + " · " + manager.TickerText;
                bool live = manager.IsEngaged && !manager.IsPaused;
                onAir = live ? "ON AIR · " + manager.CurrentTrackTitle : "STANDBY";
                rowState = live ? AvState.Ready : AvState.Info;
            }
            else if (hasChannels)
            {
                name = manager.IsOffAir ? manager.CurrentChannelName : "NO SIGNAL";
                ticker = manager.IsOffAir
                    ? "Tower lost. The station is off the air on this mission."
                    : "Dead air. Tune back to a station.";
                onAir = manager.IsOffAir ? "OFF AIR" : "DEAD AIR";
                rowState = manager.IsOffAir ? AvState.Danger : AvState.Caution;
            }
            else
            {
                name = "NO STATIONS";
                ticker = "Add music folders, then press RESCAN.";
                onAir = "NO STATIONS";
                rowState = AvState.Inert;
            }
            stationRow.Set(name, ticker, onAir, rowState);
            presetsSection.SetCaption(hasChannels ? AvNum.Fixed(manager.ChannelCount, 0) + " FOUND" : "0 FOUND");

            UpdateMeter();

            volumeStepper.Refresh();
            squelchStepper.Refresh();
            bandSeg.Refresh();
            Gate(bandSeg.Options[0], hasChannels, BandFmTip, BandOffTip);
            Gate(bandSeg.Options[1], hasChannels, BandAirTip, BandOffTip);
            Gate(bandSeg.Options[2], hasChannels, BandMwTip, BandOffTip);

            bool tuneEnabled = hasChannels;
            tuningButtons.Controls[0].Interactable = tuneEnabled;
            tuningButtons.Controls[1].Interactable = tuneEnabled;
            tuningButtons.Controls[2].Interactable = tuneEnabled;
            tuningButtons.Controls[3].Interactable = tuneEnabled;

            int selectedTracks = hasChannels ? manager.GetChannelTrackCount(manager.SelectedChannel) : 0;
            bool monitorEnabled = selectedTracks > 0 && !offStation;
            AvControl monitor = playbackButtons.Controls[0];
            monitor.Label = manager.IsPaused ? "RESUME" : manager.IsEngaged ? "PAUSE" : "MONITOR";
            monitor.Latched = manager.IsEngaged && !manager.IsPaused;
            Gate(monitor, monitorEnabled, MonitorTip, MonitorOffTip);

            AvControl scan = playbackButtons.Controls[1];
            scan.Latched = manager.IsScanning;
            Gate(scan, manager.ChannelCount > 1, ScanTip, ScanOffTip);

            AvControl mode = setupButtons.Controls[0];
            mode.Label = "MODE · " + ModeText();
            mode.Latched = manager.ReceiverModulation != dial.Modulation;
            mode.Interactable = hasChannels;

            AvControl bandwidth = setupButtons.Controls[1];
            bandwidth.Label = manager.NarrowBandwidth ? "BANDWIDTH · NARROW" : "BANDWIDTH · WIDE";
            bandwidth.Latched = manager.NarrowBandwidth;
            bandwidth.Interactable = hasChannels;

            AvControl step = setupButtons.Controls[2];
            step.Label = manager.FineTuning ? "STEP · FINE" : "STEP · " + StepText(dial.Band);
            step.Latched = manager.FineTuning;
            step.Interactable = hasChannels;

            int pages = Math.Max(1, (manager.ChannelCount + PresetRows - 1) / PresetRows);
            stationPage = Mathf.Clamp(stationPage, 0, pages - 1);
            stationPager.Refresh();
            bool previousStation = stationPage > 0;
            Gate(stationPager.Minus, previousStation, StationPreviousTip, FirstPageTip);
            bool nextStation = stationPage + 1 < pages;
            Gate(stationPager.Plus, nextStation, StationNextTip, LastPageTip);

            // A rescan can replace the station art on disk; drop the cached sprites before the rows re-read them.
            if (iconRevision != manager.StationRevision)
            {
                RadioStationIconCache.Clear();
                iconRevision = manager.StationRevision;
            }

            bool badgesChanged = false;
            for (int i = 0; i < PresetRows; i++)
            {
                AvRow row = stationRows[i];
                int index = stationPage * PresetRows + i;
                if (index >= manager.ChannelCount)
                {
                    row.Set("EMPTY PRESET", string.Empty, string.Empty, AvState.Inert);
                    row.Armed = false;
                    row.Interactable = false;
                    badgesChanged |= ApplyStationBadge(row, i, null);
                    if (stationTipName[i] != null) { stationTipName[i] = null; row.Help = null; }
                    continue;
                }

                row.Interactable = true;
                RadioDial rowDial = manager.GetChannelDial(index);
                string channelName = manager.GetChannelName(index);
                string rowName = AvNum.Fixed(index + 1, 0) + ". " + channelName;
                if (stationTipName[i] != channelName)
                {
                    stationTipName[i] = channelName;
                    row.Help = "Tune " + channelName + ". Its programme plays here; tracks are picked on the music tab.";
                }
                badgesChanged |= ApplyStationBadge(row, i, RadioStationIconCache.Get(manager.GetChannelIconPath(index)));
                string status;
                AvState state;
                if (manager.GetChannelOffAir(index)) { status = "OFF AIR"; state = AvState.Danger; }
                else if (!manager.GetChannelHasTransmitter(index))
                {
                    status = AvNum.Fixed(manager.GetChannelTrackCount(index), 0) + " TRK";
                    state = AvState.Info;
                }
                else
                {
                    float strength = Mathf.Clamp01(manager.GetChannelStrength(index));
                    status = strength >= 0.7f ? "STRONG" : strength >= 0.35f ? "FAIR" : "WEAK";
                    state = strength >= 0.7f ? AvState.Ready : strength >= 0.35f ? AvState.Caution : AvState.Danger;
                }

                row.Set(rowName, rowDial.FrequencyText + " " + rowDial.UnitText, status, state);
                row.Armed = index == manager.SelectedChannel && !offStation;
            }
            // A badge changes the row's text width and slot, so the page has to lay the rows out again.
            if (badgesChanged) console.Page(TabReceiver).RequestRelayout();

            string alert = !hasChannels
                ? "NO STATIONS · ADD OGG/WAV FOLDERS, THEN PRESS RESCAN"
                : manager.IsOffAir
                    ? "TOWER LOST · " + manager.CurrentChannelName + " IS OFF AIR ON THIS MISSION"
                    : manager.IsOffStation
                        ? "DEAD AIR · TUNE BACK TO A STATION OR CLICK THE SCOPE"
                        : null;
            if (alert != null)
                console.Footer.Set(alert, hasChannels && !manager.IsOffAir ? AvState.Caution : AvState.Danger);
            else
            {
                string ambient = manager.Status + " · RECEIVE ONLY";
                if (hasChannels && !manager.IsOffAir)
                    ambient += manager.ReceptionModelled
                        ? " · RECEPTION MODELLED: " + AvNum.Fixed(manager.TowerDistanceKm, 0) + " KM · " +
                          (manager.TowerLineOfSight ? "LOS CLEAR" : "TERRAIN BLOCKED")
                        : " · LOCAL ARCHIVE, SIGNAL READS FULL";
                console.Footer.Set(ambient, AvState.Inert);
            }

            string receiverState = manager.IsOffAir ? "OFF AIR"
                : manager.IsScanning ? "SCANNING"
                : manager.IsOffStation ? "DEAD AIR"
                : manager.IsEngaged && !manager.IsPaused ? "ON AIR"
                : manager.IsPaused ? "PAUSED"
                : hasChannels ? "STANDBY" : "NO LIBRARY";
            AvState receiverChipState = manager.IsOffAir ? AvState.Danger
                : manager.IsScanning || manager.IsOffStation ? AvState.Caution
                : manager.IsEngaged && !manager.IsPaused ? AvState.Ready
                : hasChannels ? AvState.Info : AvState.Inert;
            chips[0].Set(receiverState, receiverChipState);
            chips[1].Set(dial.BandText + " " + StepText(dial.Band), hasChannels ? AvState.Info : AvState.Inert);
        }

        /// <summary>The level bar, the squelch gate drawn on it, and the two reports.</summary>
        private static void UpdateMeter()
        {
            if (manager == null || meterPart == null) return;

            float level;
            string report;
            string detail;
            string token;
            AvState state;
            Color color;

            if (!manager.HasChannels)
            {
                level = 0f; report = "S0"; detail = "NO STATIONS"; token = "NO LIB";
                state = AvState.Inert; color = AvTheme.Dim;
            }
            else if (manager.IsScanning)
            {
                level = 0.25f + Mathf.PerlinNoise(Time.unscaledTime * 2.5f, 0.37f) * 0.35f;
                report = "SEEKING"; detail = "SCANNING"; token = "SEEK";
                state = AvState.Caution; color = AvTheme.Warning;
            }
            else if (manager.IsOffStation)
            {
                level = 0.06f + Mathf.PerlinNoise(Time.unscaledTime * 3.5f, 0.71f) * 0.16f;
                report = "S0"; detail = "DEAD AIR"; token = "NO SIG";
                state = AvState.Caution; color = AvTheme.Warning;
            }
            else if (manager.IsOffAir)
            {
                level = 0f; report = "S0"; detail = "OFF AIR"; token = "OFF AIR";
                state = AvState.Danger; color = AvTheme.Alert;
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
                state = open ? (live ? AvState.Ready : AvState.Info) : AvState.Caution;
                color = open ? (live ? AvTheme.RailReady : AvTheme.Dim) : AvTheme.Warning;
            }

            meterPart.Set(report, detail, level, manager.Squelch, color);
            if (console != null && console.CurrentPage == TabReceiver) chips[2].Set(token, state);
        }

        /// <summary>Enable a control and swap its hover help with it, so a greyed key always says why.</summary>
        private static void Gate(AvControl control, bool enabled, string onHelp, string offHelp)
        {
            if (control.Interactable != enabled) control.Interactable = enabled;
            string help = enabled ? onHelp : offHelp;
            if (control.Help != help) control.Help = help;
        }

        /// <summary>Shows the station's logo on its preset row (no-op when unchanged); true when the badge appeared or vanished.</summary>
        private static bool ApplyStationBadge(AvRow row, int slot, Sprite icon)
        {
            bool has = icon != null;
            row.SetBadge(has ? icon.texture : null);
            if (stationHasBadge[slot] == has) return false;
            stationHasBadge[slot] = has;
            return true;
        }

        private static void OnStationRowClick(int slot)
        {
            int index = stationPage * PresetRows + slot;
            if (manager != null && index < manager.ChannelCount) manager.SelectChannel(index);
        }

        private static int GetBandIndex() => manager == null ? 0 : (int)manager.TunedDial.Band;

        private static void SetBandIndex(int index)
        {
            RadioBand band = index == 1 ? RadioBand.Air : index == 2 ? RadioBand.Mw : RadioBand.Fm;
            manager?.SetBand(band);
        }

        private static void PreviousStationPage()
        {
            if (stationPage > 0) stationPage--;
        }

        private static void NextStationPage()
        {
            int pages = manager == null ? 1 : Math.Max(1, (manager.ChannelCount + PresetRows - 1) / PresetRows);
            if (stationPage + 1 < pages) stationPage++;
        }

        // ----------------------------------------------------------------------------- music page

        private static void BuildDeck(AvFlow p)
        {
            nowSection = p.Section(AvIcon.Music, "NOW PLAYING", string.Empty);
            nowRow = p.Add(new AvRow(p.Content));
            deckProgressPart = p.Add(new ProgressBarPart(p.Content));

            deckTransport = p.Buttons(
                new AvControl.Spec("PREVIOUS", () => manager?.DeckPrevious(), AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("PLAY", () => manager?.DeckTogglePlayback(), AvButtonStyle.Primary, AvIcon.PlayerPlay),
                new AvControl.Spec("NEXT", () => manager?.DeckNext(), AvButtonStyle.Quiet, AvIcon.ChevronRight),
                new AvControl.Spec("STOP", () => manager?.DeckStop(), AvButtonStyle.Danger, AvIcon.PlayerStop));
            deckTransport.Controls[0].Help = "Previous track in this folder.";
            deckTransport.Controls[1].Help = PlayTip;
            deckTransport.Controls[2].Help = "Next track in this folder.";
            deckTransport.Controls[3].Help = "Stop the deck. The receiver keeps the soundtrack bus if it is on air.";

            librarySection = p.Section(AvIcon.ListDetails, "MUSIC LIBRARY", "0 TRACKS");
            p.Buttons(new AvControl.Spec("RESCAN", () => manager?.Rescan(), AvButtonStyle.Quiet, AvIcon.Refresh))
                .Controls[0].Help = "Rescan the local music library for new folders and tracks.";

            folderStepper = p.Add(new AvStepper(p.Content, "FOLDER", FolderRangeText,
                () => NudgeFolder(-1), () => NudgeFolder(1)));
            folderStepper.Minus.Help = FolderPreviousTip;
            folderStepper.Plus.Help = FolderNextTip;

            for (int i = 0; i < TrackRows; i++)
            {
                int captured = i;
                trackRows[i] = p.Add(new AvRow(p.Content, () => OnTrackRowClick(captured)));
            }
            trackPager = p.Add(new AvStepper(p.Content, "TRACK PAGE",
                () => RangeText(trackPage, TrackRows, manager == null ? 0 : manager.DeckTrackCount, "NO TRACKS"),
                PreviousTrackPage, NextTrackPage));
            trackPager.Minus.Help = TrackPreviousTip;
            trackPager.Plus.Help = TrackNextTip;

            emptyAlert = p.Add(new AvAlert(p.Content));

            utilityButtons = p.Buttons(
                new AvControl.Spec("SHUFFLE", () => manager?.DeckToggleShuffle(), AvButtonStyle.Toggle),
                new AvControl.Spec("REPEAT", () => manager?.DeckToggleRepeat(), AvButtonStyle.Toggle),
                new AvControl.Spec("OPEN FOLDER", () => manager?.OpenLibraryFolder(), AvButtonStyle.Quiet, AvIcon.Focus2),
                new AvControl.Spec("STOP ALL", () => manager?.StopAll(), AvButtonStyle.Danger));
            utilityButtons.Controls[0].Help = "Random track order, for the music page and the radio programme.";
            utilityButtons.Controls[1].Help = "Repeat the current track instead of advancing.";
            utilityButtons.Controls[2].Help = OpenFolderTip;
            utilityButtons.Controls[3].Help = "Stop the deck and the receiver, and hand the soundtrack bus back to the game.";

            p.Ticker.Add(TabDeck, AvTickRate.Fast, RefreshDeck);
        }

        private static void RefreshDeck()
        {
            if (manager == null || console == null || nowRow == null) return;

            int folders = manager.DeckFolderCount;
            bool hasFolders = folders > 0;
            int folder = manager.DeckFolder;
            int trackCount = manager.DeckTrackCount;
            int track = trackCount == 0 ? 0 : manager.DeckTrackIndex + 1;
            bool playing = manager.DeckEngaged;

            nowSection.SetCaption(hasFolders ? manager.DeckFolderName(folder) : string.Empty);
            string trackTitle = hasFolders ? manager.DeckCurrentTitle : "NO MUSIC FOUND";
            string position = hasFolders && trackCount > 0
                ? "TRACK " + AvNum.Fixed(track, 0) + " / " + AvNum.Fixed(trackCount, 0)
                : string.Empty;
            string timeText = playing
                ? AvNum.Clock(manager.DeckElapsed) + " / " + AvNum.Clock(manager.DeckDuration)
                : "--:-- / --:--";
            nowRow.Set(trackTitle, position, timeText, playing ? AvState.Ready : AvState.Info);
            deckProgressPart.Set(playing ? manager.DeckProgress : 0f);

            bool playEnabled = hasFolders && trackCount > 0;
            deckTransport.Controls[0].Interactable = playEnabled;
            AvControl play = deckTransport.Controls[1];
            play.Label = manager.DeckPaused ? "RESUME" : manager.DeckPlaying ? "PAUSE" : hasFolders ? "PLAY" : "NO LIB";
            Gate(play, playEnabled, PlayTip, hasFolders ? PlayNoTracksTip : PlayNoLibraryTip);
            play.Latched = manager.DeckPlaying && !manager.DeckPaused;
            deckTransport.Controls[2].Interactable = playEnabled;

            librarySection.SetCaption(hasFolders ? AvNum.Fixed(trackCount, 0) + " TRACKS" : "NO TRACKS");

            AvControl shuffle = utilityButtons.Controls[0];
            shuffle.Label = manager.Shuffle ? "SHUFFLE ON" : "SHUFFLE OFF";
            shuffle.Latched = manager.Shuffle;
            AvControl repeat = utilityButtons.Controls[1];
            repeat.Label = manager.RepeatTrack ? "REPEAT ON" : "REPEAT OFF";
            repeat.Latched = manager.RepeatTrack;

            folderStepper.Refresh();
            bool previousFolder = hasFolders && folder > 0;
            Gate(folderStepper.Minus, previousFolder, FolderPreviousTip, hasFolders ? FirstFolderTip : PlayNoTracksTip);
            bool nextFolder = hasFolders && folder + 1 < folders;
            Gate(folderStepper.Plus, nextFolder, FolderNextTip, hasFolders ? LastFolderTip : PlayNoTracksTip);

            int pages = Math.Max(1, (trackCount + TrackRows - 1) / TrackRows);
            trackPage = Mathf.Clamp(trackPage, 0, pages - 1);
            trackPager.Refresh();
            bool previousPage = trackPage > 0;
            Gate(trackPager.Minus, previousPage, TrackPreviousTip, trackCount > 0 ? FirstPageTip : PlayNoTracksTip);
            bool nextPage = trackPage + 1 < pages;
            Gate(trackPager.Plus, nextPage, TrackNextTip, trackCount > 0 ? LastPageTip : PlayNoTracksTip);

            int current = manager.DeckTrackIndex;
            for (int i = 0; i < TrackRows; i++)
            {
                AvRow row = trackRows[i];
                int index = trackPage * TrackRows + i;
                if (index >= trackCount)
                {
                    row.Set(string.Empty, string.Empty, string.Empty, AvState.Inert);
                    row.Armed = false;
                    row.Interactable = false;
                    if (trackTipName[i] != null) { trackTipName[i] = null; row.Help = null; }
                    continue;
                }

                row.Interactable = true;
                bool active = index == current && playing;
                string title = manager.DeckTrackTitle(index);
                if (trackTipName[i] != title)
                {
                    trackTipName[i] = title;
                    row.Help = "Play " + title + ".";
                }
                string number = active ? "▶" : AvNum.Fixed(index + 1, 0);
                string value = active ? AvNum.Percent(manager.DeckProgress) : string.Empty;
                row.Set(number + " " + title, string.Empty, value, active ? AvState.Ready : AvState.Info);
                row.Armed = active;
            }

            if (hasFolders != deckHadFolders)
            {
                deckHadFolders = hasFolders;
                console.Page(TabDeck).RequestRelayout();
            }
            if (!hasFolders)
                emptyAlert.Show(AvIcon.AlertTriangle, "NO MUSIC FOUND",
                    "Add OGG or WAV files to BepInEx/plugins/BoscaliSummer/Music, then press RESCAN.", AvState.Caution);
            else emptyAlert.Hide();

            chips[0].Set("LOCAL", AvState.Info);
            chips[1].Set(AvNum.Fixed(folders, 0) + " FOLDERS", folders > 0 ? AvState.Info : AvState.Inert);
            chips[2].Set(AvNum.Fixed(trackCount, 0) + " TRACKS", trackCount > 0 ? AvState.Ready : AvState.Inert);

            console.Footer.Set(
                hasFolders
                    ? manager.DeckStatus + " · LOCAL LIBRARY ONLY, NOTHING IS DOWNLOADED OR SENT"
                    : "NO FOLDERS YET · PRESS OPEN FOLDER, ADD OGG/WAV TRACKS, THEN RESCAN",
                hasFolders ? AvState.Inert : AvState.Caution);
        }

        private static void OnTrackRowClick(int slot)
        {
            int index = trackPage * TrackRows + slot;
            if (manager != null && index < manager.DeckTrackCount) manager.DeckPlay(index);
        }

        private static void NudgeFolder(int direction)
        {
            if (manager == null || manager.DeckFolderCount == 0) return;
            manager.DeckSelectFolder(Mathf.Clamp(manager.DeckFolder + direction, 0, manager.DeckFolderCount - 1));
            trackPage = 0;
        }

        private static void PreviousTrackPage()
        {
            if (trackPage > 0) trackPage--;
        }

        private static void NextTrackPage()
        {
            int pages = manager == null ? 1 : Math.Max(1, (manager.DeckTrackCount + TrackRows - 1) / TrackRows);
            if (trackPage + 1 < pages) trackPage++;
        }

        private static string FolderRangeText()
        {
            if (manager == null || manager.DeckFolderCount == 0) return "0 / 0";
            return AvNum.Fixed(manager.DeckFolder + 1, 0) + " / " + AvNum.Fixed(manager.DeckFolderCount, 0) +
                " · " + AvNum.Fixed(manager.DeckTrackCount, 0) + " TRK";
        }

        // -------------------------------------------------------------------------- shared text

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
                ? AvNum.Fixed(step / 1000f, step % 1000 == 0 ? 0 : 1) + " MHz"
                : AvNum.Fixed(step, 0) + " kHz";
        }

        /// <summary>"1 - 5 OF 12": a paged list reads as a range even when it is one page.</summary>
        private static string RangeText(int page, int perPage, int count, string empty)
        {
            if (count <= 0) return empty;
            int first = page * perPage + 1;
            int last = Math.Min(count, first + perPage - 1);
            return AvNum.Fixed(first, 0) + " - " + AvNum.Fixed(last, 0) + " OF " + AvNum.Fixed(count, 0);
        }

        private static string BandRangeText()
        {
            if (manager == null || !manager.HasChannels) return "TUNE TO A STATION";
            RadioBand band = manager.TunedDial.Band;
            return RadioBands.Format(band, RadioBands.Min(band)) + " - " + RadioBands.Format(band, RadioBands.Max(band)) +
                " " + RadioBands.UnitText(band) + " · " + RadioBands.BandText(band);
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
                if (!manager.GetChannelHasTransmitter(i)) return AvNum.Fixed(manager.GetChannelTrackCount(i), 0) + " TRK";
                float strength = Mathf.Clamp01(manager.GetChannelStrength(i));
                return strength >= 0.7f ? "STRONG" : strength >= 0.35f ? "FAIR" : "WEAK";
            }
            return null;
        }

        private static int FractionToKhz(float fraction)
        {
            RadioBand band = manager == null ? RadioBand.Fm : manager.TunedDial.Band;
            int min = RadioBands.Min(band);
            int max = RadioBands.Max(band);
            return min + Mathf.RoundToInt(Mathf.Clamp01(fraction) * (max - min));
        }

        // ------------------------------------------------------------------------- local parts

        /// <summary>
        /// Kit gap: v2 has no meter/gauge-with-marker widget. Built from kit primitives
        /// (<see cref="AvText"/>, <see cref="AvGaugeGraphic"/>, <see cref="AvLay"/>): S-report,
        /// detail word, a level bar and the squelch gate drawn on it.
        /// </summary>
        private sealed class SignalMeterPart : AvPart
        {
            private readonly TMP_Text report, detail;
            private readonly AvGaugeGraphic bar;
            private readonly Image gate;

            public SignalMeterPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "Meter");
                report = AvText.Make(Rect, "Report", AvTextRole.DataStrong);
                detail = AvText.Make(Rect, "Detail", AvTextRole.DataSmall, string.Empty, TextAlignmentOptions.MidlineRight);
                var barGo = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
                barGo.transform.SetParent(Rect, false);
                bar = barGo.AddComponent<AvGaugeGraphic>();
                bar.Shape = AvGaugeShape.Bar;
                bar.raycastTarget = false;
                gate = AvLay.Solid(Rect, "Gate", Color.clear);
                Restyle();
            }

            public override float Measure(float width) => 40f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(report.rectTransform, 0f, 0f, 90f, 16f);
                AvLay.Place(detail.rectTransform, s.W - 140f, 0f, 140f, 16f);
                AvLay.Place((RectTransform)bar.transform, 0f, 22f, s.W, 8f);
                AvLay.Place(gate.rectTransform, -1f, 20f, 2f, 12f);
            }

            public void Set(string reportText, string detailText, float level01, float squelch01, Color color)
            {
                report.text = reportText ?? string.Empty;
                report.color = color;
                detail.text = detailText ?? string.Empty;
                bar.FillColor = bar.FillEnd = color;
                bar.Value = level01;
                float x = Mathf.Clamp01(squelch01) * Rect.rect.width;
                AvLay.Place(gate.rectTransform, x - 1f, 20f, 2f, 12f);
            }

            public override void Restyle()
            {
                detail.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                bar.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
                gate.color = AvStyleHost.FuiColor("caution", AvTheme.Warning);
                bar.SetVerticesDirty();
            }
        }

        /// <summary>Kit gap: a bare inline progress bar (the deck's position), from primitives.</summary>
        private sealed class ProgressBarPart : AvPart
        {
            private readonly AvGaugeGraphic bar;

            public ProgressBarPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "Progress");
                var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                bar = go.AddComponent<AvGaugeGraphic>();
                bar.Shape = AvGaugeShape.Bar;
                bar.raycastTarget = false;
                Restyle();
            }

            public override float Measure(float width) => 12f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place((RectTransform)bar.transform, 0f, 2f, s.W, 8f);
            }

            public void Set(float value01) => bar.Value = value01;

            public override void Restyle()
            {
                bar.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
                Color fill = AvStyleHost.FuiColor("select", AvTheme.Accent);
                bar.FillColor = bar.FillEnd = fill;
                bar.SetVerticesDirty();
            }
        }

        /// <summary>
        /// The band scope: the data-viz <see cref="RadioWaterfall"/> hosted inside a kit v2
        /// <see cref="AvPart"/>, plus a ruler, station markers and the needle that is also the
        /// tuning control (hover to preview, click to tune). Kit gap: nothing in v2 combines a
        /// scrolled texture with pointer-driven ruler input, so this is built locally from
        /// primitives (<see cref="AvLay"/>, <see cref="AvText"/>) and the existing data-viz
        /// waterfall, per the module's data-viz allowlist.
        /// </summary>
        private sealed class BandScopePart : AvPart
        {
            private const float WaterfallH = 72f;
            private const float RulerH = 34f;
            private const float Gap = 6f;
            private const float SweepSeconds = 0.45f;

            public Action<string> NoteChanged;

            private RadioWaterfall waterfall;
            private RectTransform tickLayer;
            private RectTransform markerLayer;
            private Image needle;
            private Image hoverCursor;
            private RectTransform hitRect;

            private RadioBand builtBand = (RadioBand)(-1);
            private float width;
            private int hoverKhz = int.MinValue;
            private int markerRevision = -1;
            private string idleNote = string.Empty;
            private RadioDial shownDial;
            private bool dialInitialized;
            private bool sweeping;
            private float sweepStart, sweepFrom, sweepTo;

            private float ScopeHeight => WaterfallH + Gap + RulerH;

            public BandScopePart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "BandScope");
            }

            public override float Measure(float w) => ScopeHeight;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                width = s.W;
                if (waterfall == null)
                    waterfall = new RadioWaterfall(Rect, new Rect(0f, 0f, s.W, WaterfallH), RadioSpectrum.DefaultBins, 48);

                BuildTicks(manager == null ? RadioBand.Fm : manager.TunedDial.Band, s.W);
                RefreshMarkers();
                EnsureChrome();
                PlaceNeedle(manager == null || !manager.HasChannels ? 0f : manager.TunedDial.Fraction);
                shownDial = manager == null ? default : manager.TunedDial;
                dialInitialized = true;
                idleNote = BandRangeText();

                if (hitRect == null)
                {
                    Image hit = AvLay.Solid(Rect, "Hit", Color.clear);
                    hit.raycastTarget = true;
                    hitRect = hit.rectTransform;
                    var input = hit.gameObject.AddComponent<RadioScopeInput>();
                    input.Area = hitRect;
                    input.Moved = OnHover;
                    input.Exited = OnExit;
                    input.Clicked = OnClick;
                }
                AvLay.Place(hitRect, 0f, 0f, s.W, ScopeHeight);
            }

            public void Animate()
            {
                if (manager == null) return;
                RadioDial dial = manager.TunedDial;
                if (dial.Band != builtBand) { BuildTicks(dial.Band, width); RefreshMarkers(); }

                string idle = BandRangeText();
                if (idle != idleNote)
                {
                    idleNote = idle;
                    if (hoverKhz == int.MinValue) NoteChanged?.Invoke(idleNote);
                }

                if (!dial.Equals(shownDial))
                {
                    if (dialInitialized && !manager.FineTuning && dial.Band == shownDial.Band)
                    {
                        sweeping = true;
                        sweepStart = Time.unscaledTime;
                        sweepFrom = shownDial.Fraction;
                        sweepTo = dial.Fraction;
                    }
                    else PlaceNeedle(dial.Fraction);
                    shownDial = dial;
                    dialInitialized = true;
                }

                if (sweeping)
                {
                    float t = (Time.unscaledTime - sweepStart) / SweepSeconds;
                    if (t >= 1f) { sweeping = false; PlaceNeedle(sweepTo); }
                    else PlaceNeedle(Mathf.Lerp(sweepFrom, sweepTo, t * t * (3f - 2f * t)));
                }

                waterfall?.Push(manager.Spectrum);
                if (markerRevision != manager.StationRevision) RefreshMarkers();
            }

            public void Dispose() => waterfall?.Dispose();

            private void EnsureChrome()
            {
                if (needle != null) return;
                needle = AvLay.Solid(Rect, "Needle", AvTheme.Accent);
                hoverCursor = AvLay.Solid(Rect, "HoverCursor", AvTheme.Accent.WithAlpha(0.7f));
                hoverCursor.enabled = false;
            }

            private void BuildTicks(RadioBand band, float w)
            {
                if (tickLayer != null) UnityEngine.Object.Destroy(tickLayer.gameObject);
                if (markerLayer != null) UnityEngine.Object.Destroy(markerLayer.gameObject);
                tickLayer = AvLay.Child(Rect, "Ticks");
                AvLay.Place(tickLayer, 0f, 0f, w, ScopeHeight);
                markerLayer = AvLay.Child(Rect, "Markers");
                AvLay.Place(markerLayer, 0f, 0f, w, ScopeHeight);

                builtBand = band;
                width = w;
                hoverKhz = int.MinValue;
                if (hoverCursor != null) hoverCursor.enabled = false;
                markerRevision = -1;

                int min = RadioBands.Min(band), max = RadioBands.Max(band), step = Math.Max(1, RadioBands.Step(band));
                int span = Math.Max(1, max - min);
                int minorStep = Math.Max(step, span / 60 / step * step);
                int labelStep = band == RadioBand.Mw ? 200 : 2000;
                float rulerTop = WaterfallH + Gap;

                for (int khz = min; khz <= max; khz += minorStep)
                {
                    bool major = (khz - min) % labelStep == 0;
                    float x = TickX(khz, min, max, w);
                    Image tick = AvLay.Solid(tickLayer, "Tick", AvTheme.Hairline.WithAlpha(major ? 0.55f : 0.28f));
                    AvLay.Place(tick.rectTransform, x, rulerTop, 1f, major ? 10f : 6f);
                    if (!major) continue;

                    TMP_Text label = AvText.Make(tickLayer, "Label", AvTextRole.Micro, RadioBands.Format(band, khz));
                    float labelLeft = Mathf.Clamp(x - 26f, 0f, Mathf.Max(0f, w - 52f));
                    AvLay.Place(label.rectTransform, labelLeft, rulerTop + 12f, 52f, 14f);
                    label.alignment = x <= 26f ? TextAlignmentOptions.MidlineLeft
                        : x >= w - 26f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Center;
                    label.color = AvTheme.Dim;
                }
            }

            private void RefreshMarkers()
            {
                if (manager == null || markerLayer == null) return;
                for (int i = markerLayer.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(markerLayer.GetChild(i).gameObject);

                int min = RadioBands.Min(builtBand), max = RadioBands.Max(builtBand);
                float rulerTop = WaterfallH + Gap;
                for (int i = 0; i < manager.ChannelCount; i++)
                {
                    RadioDial dial = manager.GetChannelDial(i);
                    if (dial.Band != builtBand) continue;
                    float x = TickX(dial.Kilohertz, min, max, width);
                    float strength = Mathf.Clamp01(manager.GetChannelStrength(i));
                    Color c = Color.Lerp(AvTheme.RailInert, manager.GetChannelColor(i), 0.25f + 0.75f * strength);
                    Image mark = AvLay.Solid(markerLayer, "Marker", c);
                    AvLay.Place(mark.rectTransform, Mathf.Clamp(x - 3f, 0f, Mathf.Max(0f, width - 6f)), rulerTop, 6f, 12f);
                }
                markerRevision = manager.StationRevision;
            }

            private static float TickX(int khz, int min, int max, float w) =>
                Mathf.Clamp01((khz - min) / (float)Math.Max(1, max - min)) * Mathf.Max(0f, w - 1f);

            private void PlaceNeedle(float fraction)
            {
                if (needle == null) return;
                float x = Mathf.Clamp01(fraction) * Mathf.Max(0f, width - 2f);
                AvLay.Place(needle.rectTransform, x, 0f, 2f, ScopeHeight);
            }

            private void OnHover(float fraction)
            {
                if (manager == null) return;
                RadioBand band = manager.TunedDial.Band;
                int khz = RadioDialTuning.Nearest(band, FractionToKhz(fraction)).Kilohertz;
                if (khz != hoverKhz)
                {
                    hoverKhz = khz;
                    string name = StationNameAt(band, khz);
                    string strength = name == null ? null : StationStrengthAt(band, khz);
                    string note = "> " + RadioBands.Format(band, khz) + " " + RadioBands.UnitText(band) +
                        " · " + (name ?? "NO STATION") + (strength == null ? string.Empty : " · " + strength);
                    NoteChanged?.Invoke(note);
                }
                if (hoverCursor != null)
                {
                    int min = RadioBands.Min(builtBand), max = RadioBands.Max(builtBand);
                    float x = TickX(khz, min, max, width);
                    AvLay.Place(hoverCursor.rectTransform, x - 0.5f, 0f, 1f, ScopeHeight);
                    hoverCursor.enabled = true;
                }
            }

            private void OnExit()
            {
                hoverKhz = int.MinValue;
                if (hoverCursor != null) hoverCursor.enabled = false;
                NoteChanged?.Invoke(idleNote);
            }

            private void OnClick(float fraction)
            {
                manager?.TuneTo(FractionToKhz(fraction));
            }
        }
    }

    /// <summary>
    /// Turns pointer positions over the band scope into a fraction of the ruler, so the scope
    /// itself is the tuning control. A plain pointer handler, not a Selectable, so it never
    /// takes navigation focus from the flight controls.
    /// </summary>
    internal sealed class RadioScopeInput : MonoBehaviour, IPointerMoveHandler, IPointerClickHandler, IPointerExitHandler
    {
        public RectTransform Area;
        public Action<float> Moved;
        public Action<float> Clicked;
        public Action Exited;

        public void OnPointerMove(PointerEventData eventData) =>
            Moved?.Invoke(Fraction(eventData, eventData.enterEventCamera));

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke(Fraction(eventData, eventData.pressEventCamera));
            AvInput.Deselect(gameObject);
        }

        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();

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
