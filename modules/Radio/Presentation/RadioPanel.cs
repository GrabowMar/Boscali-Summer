using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Radio.Configuration;
using BoscaliSummer.Modules.Radio.Runtime;
using BoscaliSummer.Core.Game;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Radio.Presentation
{
    /// <summary>
    /// The receiver and the music deck in one set, on two compact kit v2 pages (no chip strip: every
    /// state it used to repeat now lives in the page itself).
    ///
    /// <para>RECEIVER, top to bottom: the head line (frequency, station, programme, status slab), the
    /// signal line with its squelch gate, ONE key row (band keys, seek/tune keys, monitor/scan/stop), the
    /// band scope, which is also the tuning control and is the part that grows to fill the page
    /// (<see cref="BandScopePart"/> — hover to preview a channel, click to tune), the volume and squelch
    /// rings with the receiver switches beside them, and the presets: a header that carries the paging
    /// and rescan keys, and named station rows with frequency and reception reports.</para>
    ///
    /// <para>MUSIC: the equalizer (the growing part), the track on the deck, a position bar, ONE transport
    /// row (transport, shuffle, repeat, stop all), a folder header with folder and library keys, a track
    /// header with paging keys and a track list sized to the page.</para>
    /// </summary>
    internal static class RadioPanel
    {
        private const int TabReceiver = 0;
        private const int TabDeck = 1;
        private const int PresetRows = 6;
        private const int MinPresetRows = 1;
        private const int TrackRows = 15;
        private const int MinTrackRows = 1;
        private const int EqBars = 24;

        private const float ListRowHeight = 33f;

        // Tooltips are swapped while a control is disabled, so a greyed key always says why.
        private const string MonitorTip = "Monitor: listen to the tuned station's programme. Press again to pause it.";
        private const string MonitorPauseTip = "Pause the programme. The station stays tuned and the soundtrack bus stays held.";
        private const string MonitorResumeTip = "Resume the paused programme.";
        private const string MonitorOffTip = "No programme on this station.";
        private const string ScanTip = "Scan: hold each station in the band for a few seconds as it seeks. Press again to stop.";
        private const string ScanOffTip = "Scan needs at least two stations.";
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
        private const string OpenFolderTip = "Open the local music folder. OGG and WAV files only.";
        private const string RescanTip = "Rescan the local music library for new folders and tracks. This signs the receiver off and rebuilds the dial.";

        private static readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Rad, "RAD", "BoscaliRadio.Screen", preferLeft: false)
            { Wrap = true, Builder = BuildScreen };
        private static AvConsole console;
        private static RadioManager manager;

        // ------------------------------------------------------------------ receiver widgets
        private static RadioTunePart tunePart;
        private static RadioSignalPart meterPart;
        private static RadioStripPart dialStrip;
        private static BandScopePart scopePart;
        private static RadioLevelsPart levelsPart;
        private static RadioHeaderPart presetHeader;
        private static readonly AvRow[] stationRows = new AvRow[PresetRows];
        private static int presetShown = PresetRows;

        // ----------------------------------------------------------------------- deck widgets
        private static AvRow nowRow;
        private static AvEqualizer deckEq;
        private static AvHazardBar deckBar;
        private static readonly float[] eqLevels = new float[EqBars];
        private static RadioStripPart deckStrip;
        private static RadioHeaderPart folderHeader;
        private static RadioHeaderPart trackHeader;
        private static readonly AvRow[] trackRows = new AvRow[TrackRows];
        private static AvAlert emptyAlert;
        private static bool deckHadFolders = true;
        private static int trackShown = TrackRows;

        // Per-row hover-help and badge caches, so a row only touches its widgets when its content changes.
        private static readonly string[] stationTipName = new string[PresetRows];
        private static readonly bool[] stationHasBadge = new bool[PresetRows];
        private static readonly string[] trackTipName = new string[TrackRows];
        private static int iconRevision = -1;

        private static int stationPage;
        private static int trackPage;
        private static float nextAttempt;
        private static bool unavailableLogged;

        public static void Tick(RadioManager radio)
        {
            manager = radio;
            if (installer.Failed) return;
            if (!GameAccess.MfdAvailable)
            {
                if (!unavailableLogged)
                {
                    unavailableLogged = true;
                    Plugin.Logger.LogWarning("Radio panel unavailable: VirtualMFD access did not resolve.");
                }
                return;
            }

            if (installer.Screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                installer.Log = Plugin.Logger;
                installer.Install();
                if (installer.Failed)
                {
                    console = null;
                    Plugin.Logger.LogWarning("Radio panel disabled. Playback remains available through config reload only.");
                }
            }

            // The console's own AvTicker (a MonoBehaviour on its Root, inside
            // MFDScreen.displayPanel) drives every readout from here on. Unity stops calling its
            // Update() the moment the bezel shows a different screen on this slot, since
            // displayPanel is deactivated then — the same gate the v1 panel enforced by hand.
        }

        public static void Reset()
        {
            installer.Reset();
            RadioStationIconCache.Clear();
            scopePart?.Dispose();

            console = null;
            manager = null;

            tunePart = null;
            meterPart = null;
            dialStrip = null;
            scopePart = null;
            levelsPart = null;
            presetHeader = null;
            Array.Clear(stationRows, 0, stationRows.Length);
            presetShown = PresetRows;

            nowRow = null;
            deckEq = null;
            deckBar = null;
            Array.Clear(eqLevels, 0, eqLevels.Length);
            deckStrip = null;
            folderHeader = null;
            trackHeader = null;
            Array.Clear(trackRows, 0, trackRows.Length);
            emptyAlert = null;
            deckHadFolders = true;
            trackShown = TrackRows;

            Array.Clear(stationTipName, 0, stationTipName.Length);
            Array.Clear(stationHasBadge, 0, stationHasBadge.Length);
            Array.Clear(trackTipName, 0, trackTipName.Length);
            iconRevision = -1;

            stationPage = 0;
            trackPage = 0;
            nextAttempt = 0f;
        }

        private static RectTransform BuildScreen(RectTransform display, float height)
        {
            console = AvConsole.Build(display, MfdSlots.Rad, "RADIO", 2, AvTokens.PanelWidth, height);
            console.Tabs((AvIcon.Radio, "RECEIVER"), (AvIcon.Music, "MUSIC"));

            BuildReceiver(console.Page(TabReceiver));
            BuildDeck(console.Page(TabDeck));
            console.Finish();
            console.PageChanged += _ => { RefreshReceiver(); RefreshDeck(); };
            RefreshReceiver();
            RefreshDeck();
            return null;
        }

        // ------------------------------------------------------------------------ receiver page

        private static void BuildReceiver(AvFlow p)
        {
            tunePart = p.Add(new RadioTunePart(p.Content));
            tunePart.Help = "Tuned frequency, station and what it is playing. The slab is the receiver state: " +
                "ON AIR, STANDBY, SCANNING, DEAD AIR (nothing on this frequency) or OFF AIR (the station's tower is lost).";

            meterPart = p.Add(new RadioSignalPart(p.Content));
            meterPart.Help = "Signal strength as an S-report and dBm. The amber gate on the bar is the squelch: " +
                "a station weaker than the gate stays silent.";

            dialStrip = p.Add(new RadioStripPart(p.Content,
                new RadioStripItem("FM", () => SetBandIndex(0), AvButtonStyle.Default, AvIcon.None, 1.3f),
                new RadioStripItem("AIR", () => SetBandIndex(1), AvButtonStyle.Default, AvIcon.None, 1.3f),
                new RadioStripItem("MW", () => SetBandIndex(2), AvButtonStyle.Default, AvIcon.None, 1.3f),
                new RadioStripItem(string.Empty, () => manager?.SeekStation(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft, 1f, true),
                new RadioStripItem(string.Empty, () => manager?.StepDial(-1), AvButtonStyle.Quiet, AvIcon.Minus),
                new RadioStripItem(string.Empty, () => manager?.StepDial(1), AvButtonStyle.Quiet, AvIcon.Plus),
                new RadioStripItem(string.Empty, () => manager?.SeekStation(1), AvButtonStyle.Quiet, AvIcon.ChevronRight),
                new RadioStripItem(string.Empty, () => manager?.TogglePlayback(), AvButtonStyle.Primary, AvIcon.Volume, 1f, true),
                new RadioStripItem(string.Empty, () => manager?.ToggleScan(), AvButtonStyle.Toggle, AvIcon.Radar2),
                new RadioStripItem(string.Empty, () => manager?.Stop(), AvButtonStyle.Danger, AvIcon.PlayerStop)));
            dialStrip[0].Help = BandFmTip;
            dialStrip[1].Help = BandAirTip;
            dialStrip[2].Help = BandMwTip;
            dialStrip[3].Help = "Seek: jump to the previous station down the band.";
            dialStrip[4].Help = "Tune one step down. Between channels is dead air.";
            dialStrip[5].Help = "Tune one step up. Between channels is dead air.";
            dialStrip[6].Help = "Seek: jump to the next station up the band.";
            dialStrip[9].Help = "Stop: sign off and hand the soundtrack bus back to the game.";

            // The scope is the part that grows: it takes whatever height the rest of the page leaves over.
            scopePart = p.Add(new BandScopePart(p.Content), 1f);

            levelsPart = p.Add(new RadioLevelsPart(p.Content,
                () => manager?.NudgeVolume(-0.1f), () => manager?.NudgeVolume(0.1f),
                () => manager?.NudgeSquelch(-0.05f), () => manager?.NudgeSquelch(0.05f),
                () => manager?.CycleMode(), () => manager?.ToggleBandwidth(), () => manager?.ToggleFineTuning()));
            levelsPart.Volume.Help = "Receiver volume. Use the + and - keys beside the rectangular level meter.";
            levelsPart.Squelch.Help = "Squelch threshold. Higher means only a stronger station opens the audio; the amber gate on the signal bar shows it.";
            levelsPart.VolumeDown.Help = "Turn the receiver volume down.";
            levelsPart.VolumeUp.Help = "Turn the receiver volume up.";
            levelsPart.SquelchDown.Help = "Lower the squelch: a weaker station can open the audio.";
            levelsPart.SquelchUp.Help = "Raise the squelch: only a stronger station opens the audio.";
            levelsPart.Mode.Help = "Cycle AUTO, FM and AM. A forced mode on the wrong station garbles it.";
            levelsPart.Bandwidth.Help = "Wide or narrow passband. Narrow trades audio quality for less noise.";
            levelsPart.Step.Help = "Channel step, or a five-times finer tuning step.";

            presetHeader = p.Add(new RadioHeaderPart(p.Content, AvIcon.ListDetails, "PRESETS",
                new AvControl.Spec(string.Empty, () => manager?.Rescan(), AvButtonStyle.Quiet, AvIcon.Refresh),
                new AvControl.Spec(string.Empty, () => { PreviousStationPage(); RefreshReceiver(); }, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec(string.Empty, () => { NextStationPage(); RefreshReceiver(); }, AvButtonStyle.Quiet, AvIcon.ChevronRight)));
            presetHeader[0].Help = RescanTip;

            for (int i = 0; i < PresetRows; i++)
            {
                int captured = i;
                stationRows[i] = p.Add(new AvRow(p.Content, () => OnStationRowClick(captured)));
                stationRows[i].BadgeSlot = true;
            }

            p.Ticker.Add(TabReceiver, AvTickRate.Fast, RefreshReceiver);
            p.Ticker.Add(TabReceiver, AvTickRate.Fast, scopePart.Animate);
        }

        private static void RefreshReceiver()
        {
            if (manager == null || console == null || tunePart == null) return;

            bool hasChannels = manager.HasChannels;
            bool offStation = manager.IsOffStation || manager.IsOffAir;
            RadioDial dial = manager.TunedDial;

            string modeLine = hasChannels ? manager.BroadcastMode + " · " + StepText(dial.Band) : "NO LIBRARY";
            string name, programme, trackLine = string.Empty;
            if (hasChannels && !offStation)
            {
                name = manager.CurrentChannelName;
                programme = string.IsNullOrEmpty(manager.CurrentProgram)
                    ? manager.TickerText
                    : manager.CurrentProgram + " · " + manager.TickerText;
                if (manager.IsEngaged && !manager.IsPaused) trackLine = "NOW  " + (manager.CurrentTrackTitle ?? string.Empty);
            }
            else if (hasChannels)
            {
                name = manager.IsOffAir ? manager.CurrentChannelName : "NO SIGNAL";
                programme = manager.IsOffAir
                    ? "TOWER LOST · THIS STATION IS OFF AIR"
                    : "NOTHING HERE · SEEK OR CLICK THE SCOPE";
            }
            else
            {
                name = "NO STATIONS";
                programme = "ADD MUSIC FOLDERS · PRESS RESCAN";
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
            tunePart.Set(dial.FrequencyText, dial.UnitText, modeLine, name, programme, trackLine, receiverState, receiverChipState);
            FitPresetRows();

            UpdateMeter();

            levelsPart.Volume.Set(Mathf.Clamp01(manager.VolumeLevel), AvNum.Percent(manager.VolumeLevel), AvState.Info);
            levelsPart.Squelch.Set(Mathf.Clamp01(manager.Squelch), AvNum.Percent(manager.Squelch),
                manager.Squelch > 0.6f ? AvState.Caution : AvState.Info);

            AvControl mode = levelsPart.Mode;
            mode.Label = "MODE · " + ModeText();
            mode.Latched = manager.ReceiverModulation != dial.Modulation;
            mode.Interactable = hasChannels;

            AvControl bandwidth = levelsPart.Bandwidth;
            bandwidth.Label = manager.NarrowBandwidth ? "BANDWIDTH · NARROW" : "BANDWIDTH · WIDE";
            bandwidth.Latched = manager.NarrowBandwidth;
            bandwidth.Interactable = hasChannels;

            AvControl step = levelsPart.Step;
            step.Label = manager.FineTuning ? "STEP · FINE" : "STEP · " + StepText(dial.Band);
            step.Latched = manager.FineTuning;
            step.Interactable = hasChannels;

            int bandIndex = GetBandIndex();
            for (int i = 0; i < 3; i++) dialStrip[i].Latched = i == bandIndex;
            Gate(dialStrip[0], hasChannels, BandFmTip, BandOffTip);
            Gate(dialStrip[1], hasChannels, BandAirTip, BandOffTip);
            Gate(dialStrip[2], hasChannels, BandMwTip, BandOffTip);
            for (int i = 3; i <= 6; i++) dialStrip[i].Interactable = hasChannels;

            int selectedTracks = hasChannels ? manager.GetChannelTrackCount(manager.SelectedChannel) : 0;
            bool monitorEnabled = selectedTracks > 0 && !offStation;
            AvControl monitor = dialStrip[7];
            monitor.Latched = manager.IsEngaged && !manager.IsPaused;
            Gate(monitor, monitorEnabled,
                manager.IsPaused ? MonitorResumeTip : manager.IsEngaged ? MonitorPauseTip : MonitorTip, MonitorOffTip);

            AvControl scan = dialStrip[8];
            scan.Latched = manager.IsScanning;
            Gate(scan, manager.ChannelCount > 1, ScanTip, ScanOffTip);

            int pages = Math.Max(1, (manager.ChannelCount + presetShown - 1) / presetShown);
            stationPage = Mathf.Clamp(stationPage, 0, pages - 1);
            presetHeader.SetCaption(hasChannels
                ? RangeText(stationPage, presetShown, manager.ChannelCount, "NO STATIONS")
                : "NO STATIONS");
            Gate(presetHeader[1], stationPage > 0, StationPreviousTip, FirstPageTip);
            Gate(presetHeader[2], stationPage + 1 < pages, StationNextTip, LastPageTip);

            // A rescan can replace the station art on disk; drop the cached sprites before the rows re-read them.
            if (iconRevision != manager.StationRevision)
            {
                RadioStationIconCache.Clear();
                iconRevision = manager.StationRevision;
            }

            bool badgesChanged = false;
            for (int i = 0; i < presetShown; i++)
            {
                AvRow row = stationRows[i];
                int index = stationPage * presetShown + i;
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
                string rowName = rowDial.FrequencyText + " " + channelName;
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

                row.Set(rowName, string.Empty, status, state);
                row.Armed = index == manager.SelectedChannel && !offStation;
            }
            // A badge changes the row's text width and slot, so the page has to lay the rows out again.
            if (badgesChanged) console.Page(TabReceiver).RequestRelayout();

            string alert = !hasChannels
                ? "NO STATIONS · ADD FOLDERS, RESCAN"
                : manager.IsOffAir
                    ? "TOWER LOST · " + manager.CurrentChannelName + " OFF AIR"
                    : manager.IsOffStation
                        ? "DEAD AIR · TUNE OR CLICK THE SCOPE"
                        : null;
            if (alert != null)
                console.Footer.Set(alert, hasChannels && !manager.IsOffAir ? AvState.Caution : AvState.Danger);
            else
            {
                string ambient = manager.Status + " · RECEIVE ONLY";
                if (hasChannels && !manager.IsOffAir)
                    ambient += manager.ReceptionModelled
                        ? " · " + AvNum.Fixed(manager.TowerDistanceKm, 0) + " KM · " +
                          (manager.TowerLineOfSight ? "LOS" : "BLOCKED")
                        : " · LOCAL";
                console.Footer.Set(ambient, AvState.Inert);
            }
        }

        /// <summary>The list shows as many preset rows as the page can hold; a short console pages instead of scrolling.</summary>
        private static void FitPresetRows()
        {
            AvFlow page = console.Page(TabReceiver);
            // Measure the same inner width and six fixed lines used by AvFlow. Programme copy can
            // wrap when a tower goes off air, so this runs after the current tune data is applied.
            float width = page.Inner;
            float fixedHeight = 2f * AvGridTokens.Pad + 5f * AvGridTokens.Gap +
                tunePart.Measure(width) + meterPart.Measure(width) + dialStrip.Measure(width) +
                scopePart.Measure(width) + levelsPart.Measure(width) + presetHeader.Measure(width);
            float rowHeight = ListRowHeight;
            foreach (AvRow row in stationRows)
                if (row != null) rowHeight = Mathf.Max(rowHeight, row.Measure(width) + AvGridTokens.Gap);
            int shown = page.ViewportHeight <= 0f ? PresetRows :
                Mathf.Clamp(Mathf.FloorToInt((page.ViewportHeight - fixedHeight) / rowHeight), MinPresetRows, PresetRows);
            if (shown == presetShown) return;
            int first = stationPage * presetShown;
            presetShown = shown;
            stationPage = first / shown;
            for (int i = 0; i < PresetRows; i++) stationRows[i].SetShown(i < shown);
        }

        /// <summary>The level bar, the squelch gate drawn on it, and the two reports.</summary>
        private static void UpdateMeter()
        {
            if (manager == null || meterPart == null) return;

            float level;
            string report;
            string detail;
            Color color;

            if (!manager.HasChannels)
            {
                level = 0f; report = "S0"; detail = "NO STATIONS";
                color = AvTheme.Dim;
            }
            else if (manager.IsScanning)
            {
                level = 0.25f + Mathf.PerlinNoise(Time.unscaledTime * 2.5f, 0.37f) * 0.35f;
                report = "SEEKING"; detail = "SCANNING";
                color = AvTheme.Warning;
            }
            else if (manager.IsOffStation)
            {
                level = 0.06f + Mathf.PerlinNoise(Time.unscaledTime * 3.5f, 0.71f) * 0.16f;
                report = "S0"; detail = "DEAD AIR";
                color = AvTheme.Warning;
            }
            else if (manager.IsOffAir)
            {
                level = 0f; report = "S0"; detail = "OFF AIR";
                color = AvTheme.Alert;
            }
            else
            {
                RadioReception reception = manager.Reception;
                level = Mathf.Max(Mathf.Max(0.04f, reception.Quality * 0.9f), manager.SignalLevel * 0.6f);
                bool open = reception.Open(manager.Squelch);
                bool live = open && manager.IsEngaged && !manager.IsPaused;
                report = reception.SReport;
                detail = open ? reception.DbmReport : "SQL SHUT";
                color = open ? (live ? AvTheme.RailReady : AvTheme.Dim) : AvTheme.Warning;
            }

            meterPart.Set(report, detail, level, manager.Squelch, color);
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
            int index = stationPage * presetShown + slot;
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
            int pages = manager == null ? 1 : Math.Max(1, (manager.ChannelCount + presetShown - 1) / presetShown);
            if (stationPage + 1 < pages) stationPage++;
        }

        // ----------------------------------------------------------------------------- music page

        private static void BuildDeck(AvFlow p)
        {
            // The decorative meter yields space to a track on the compact console and grows
            // into the height that the track list does not need on larger displays.
            deckEq = p.Add(new AvEqualizer(p.Content, "NOW PLAYING", 8f), 1f);
            deckEq.Help = "Level meter. It is decorative, not an audio analysis: the bars move while the deck is playing and settle when it is paused or stopped.";
            nowRow = p.Add(new AvRow(p.Content));
            nowRow.Help = "The track on the deck and its place in the folder. Click a track in the list below to play it.";
            deckBar = p.Add(new AvHazardBar(p.Content, "POSITION"));
            deckBar.Help = "Playback position: elapsed and total time of the current track.";

            deckStrip = p.Add(new RadioStripPart(p.Content,
                new RadioStripItem(string.Empty, () => manager?.DeckPrevious(), AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new RadioStripItem("PLAY", () => manager?.DeckTogglePlayback(), AvButtonStyle.Primary, AvIcon.PlayerPlay, 1.9f),
                new RadioStripItem(string.Empty, () => manager?.DeckNext(), AvButtonStyle.Quiet, AvIcon.ChevronRight),
                new RadioStripItem(string.Empty, () => manager?.DeckStop(), AvButtonStyle.Danger, AvIcon.PlayerStop),
                new RadioStripItem("SHUFFLE", () => manager?.DeckToggleShuffle(), AvButtonStyle.Toggle, AvIcon.None, 1.9f, true),
                new RadioStripItem("REPEAT", () => manager?.DeckToggleRepeat(), AvButtonStyle.Toggle, AvIcon.None, 1.9f),
                new RadioStripItem("STOP ALL", () => manager?.StopAll(), AvButtonStyle.Danger, AvIcon.None, 2.1f, true)));
            deckStrip[0].Help = "Previous track in this folder.";
            deckStrip[1].Help = PlayTip;
            deckStrip[2].Help = "Next track in this folder.";
            deckStrip[3].Help = "Stop the deck. The receiver keeps the soundtrack bus if it is on air.";
            deckStrip[6].Help = "Stop the deck and the receiver, and hand the soundtrack bus back to the game.";

            emptyAlert = p.Add(new AvAlert(p.Content));

            folderHeader = p.Add(new RadioHeaderPart(p.Content, AvIcon.Music, "MUSIC",
                new AvControl.Spec(string.Empty, () => { NudgeFolder(-1); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec(string.Empty, () => { NudgeFolder(1); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronRight),
                new AvControl.Spec(string.Empty, () => manager?.Rescan(), AvButtonStyle.Quiet, AvIcon.Refresh),
                new AvControl.Spec(string.Empty, () => manager?.OpenLibraryFolder(), AvButtonStyle.Quiet, AvIcon.Focus2)));
            folderHeader[0].Help = FolderPreviousTip;
            folderHeader[1].Help = FolderNextTip;
            folderHeader[2].Help = RescanTip;
            folderHeader[3].Help = OpenFolderTip;

            trackHeader = p.Add(new RadioHeaderPart(p.Content, AvIcon.ListDetails, "TRACKS",
                new AvControl.Spec(string.Empty, () => { PreviousTrackPage(); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec(string.Empty, () => { NextTrackPage(); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronRight)));

            for (int i = 0; i < TrackRows; i++)
            {
                int captured = i;
                trackRows[i] = p.Add(new AvRow(p.Content, () => OnTrackRowClick(captured)));
            }

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

            string trackTitle = hasFolders ? manager.DeckCurrentTitle : "NO MUSIC FOUND";
            string position = hasFolders && trackCount > 0
                ? "TRACK " + AvNum.Fixed(track, 0) + " / " + AvNum.Fixed(trackCount, 0)
                : string.Empty;
            string timeText = playing
                ? AvNum.Clock(manager.DeckElapsed) + " / " + AvNum.Clock(manager.DeckDuration)
                : "--:-- / --:--";
            nowRow.Set(trackTitle, position, string.Empty, playing ? AvState.Ready : AvState.Info);
            float progress = playing ? Mathf.Clamp01(manager.DeckProgress) : 0f;
            deckBar.Set(progress, timeText, playing ? AvState.Ready : AvState.Inert);

            // Decorative level bars: not audio analysis, just motion while the deck is live.
            bool live = playing && !manager.DeckPaused;
            float clock = Time.unscaledTime;
            for (int i = 0; i < EqBars; i++)
            {
                float target = live ? 0.15f + 0.85f * Mathf.PerlinNoise(clock * 3.1f + i * 0.61f, 0.23f * i) : 0.04f;
                eqLevels[i] = Mathf.Lerp(eqLevels[i], target, live ? 0.6f : 0.2f);
            }
            deckEq.Set(eqLevels, live ? "ON" : manager.DeckPaused ? "PAUSED" : "IDLE", live ? AvState.Ready : AvState.Inert);

            bool playEnabled = hasFolders && trackCount > 0;
            deckStrip[0].Interactable = playEnabled;
            AvControl play = deckStrip[1];
            play.Label = manager.DeckPaused ? "RESUME" : manager.DeckPlaying ? "PAUSE" : hasFolders ? "PLAY" : "NO LIB";
            play.SetIcon(manager.DeckPlaying && !manager.DeckPaused ? AvIcon.PlayerPause : AvIcon.PlayerPlay);
            Gate(play, playEnabled, PlayTip, hasFolders ? PlayNoTracksTip : PlayNoLibraryTip);
            play.Latched = manager.DeckPlaying && !manager.DeckPaused;
            deckStrip[2].Interactable = playEnabled;

            AvControl shuffle = deckStrip[4];
            shuffle.Latched = manager.Shuffle;
            SetHelp(shuffle, manager.Shuffle
                ? "Shuffle is on: random track order, for the music page and the radio programme. Click to play in order."
                : "Shuffle is off. Click for random track order, on the music page and the radio programme.");
            AvControl repeat = deckStrip[5];
            repeat.Latched = manager.RepeatTrack;
            SetHelp(repeat, manager.RepeatTrack
                ? "Repeat is on: the current track loops. Click to advance to the next track instead."
                : "Repeat is off. Click to loop the current track instead of advancing.");

            folderHeader.SetTitle(hasFolders ? manager.DeckFolderName(folder) : "NO MUSIC FOLDERS");
            folderHeader.SetCaption(FolderRangeText());
            Gate(folderHeader[0], hasFolders && folder > 0, FolderPreviousTip, hasFolders ? FirstFolderTip : PlayNoTracksTip);
            Gate(folderHeader[1], hasFolders && folder + 1 < folders, FolderNextTip, hasFolders ? LastFolderTip : PlayNoTracksTip);

            // Titles can wrap. Fit after applying the current title instead of reserving a fixed
            // height, so every track advertised by the pager fits in the compact console.
            FitTrackRows();

            int pages = Math.Max(1, (trackCount + trackShown - 1) / trackShown);
            trackPage = Mathf.Clamp(trackPage, 0, pages - 1);
            trackHeader.SetCaption(RangeText(trackPage, trackShown, trackCount, "NO TRACKS"));
            Gate(trackHeader[0], trackPage > 0, TrackPreviousTip, trackCount > 0 ? FirstPageTip : PlayNoTracksTip);
            Gate(trackHeader[1], trackPage + 1 < pages, TrackNextTip, trackCount > 0 ? LastPageTip : PlayNoTracksTip);

            int current = manager.DeckTrackIndex;
            for (int i = 0; i < TrackRows; i++)
            {
                AvRow row = trackRows[i];
                int index = trackPage * trackShown + i;
                row.SetShown(hasFolders && i < trackShown && index < trackCount);
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
                string number = active ? "> " : ((index < 9 ? " " : "") + AvNum.Fixed(index + 1, 0));
                string value = active ? AvNum.Percent(manager.DeckProgress) : string.Empty;
                row.Set(number + " " + title, string.Empty, value, active ? AvState.Ready : AvState.Info);
                row.Armed = active;
            }
            trackHeader.SetShown(hasFolders);

            if (hasFolders != deckHadFolders)
            {
                deckHadFolders = hasFolders;
                console.Page(TabDeck).RequestRelayout();
            }
            if (!hasFolders)
                emptyAlert.Show(AvIcon.AlertTriangle, "NO MUSIC FOUND",
                    "Put OGG or WAV files in BepInEx/plugins/BoscaliSummer/Music, then press the rescan key. " +
                    "The folder key opens it.", AvState.Caution);
            else if (trackCount == 0)
                emptyAlert.Show(AvIcon.Music, "EMPTY MUSIC FOLDER",
                    "Choose another folder with the arrow keys, or add OGG/WAV files and press RESCAN.", AvState.Info);
            else emptyAlert.Hide();

            console.Footer.Set(
                hasFolders
                    ? manager.DeckStatus + " · LOCAL ONLY"
                    : "NO FOLDERS · OPEN FOLDER, ADD OGG/WAV, RESCAN",
                hasFolders ? AvState.Inert : AvState.Caution);
        }

        /// <summary>The track list shows as many rows as the page can hold, so a short console pages instead of scrolling.</summary>
        private static void FitTrackRows()
        {
            AvFlow page = console.Page(TabDeck);
            float width = page.Inner;
            float fixedHeight = 2f * AvGridTokens.Pad + 5f * AvGridTokens.Gap +
                deckEq.Measure(width) + nowRow.Measure(width) + deckBar.Measure(width) +
                deckStrip.Measure(width) + folderHeader.Measure(width) + trackHeader.Measure(width);
            float rowHeight = ListRowHeight;
            foreach (AvRow row in trackRows)
                if (row != null) rowHeight = Mathf.Max(rowHeight, row.Measure(width) + AvGridTokens.Gap);
            int shown = page.ViewportHeight <= 0f ? TrackRows : Mathf.Clamp(
                Mathf.FloorToInt((page.ViewportHeight - fixedHeight) / rowHeight), MinTrackRows, TrackRows);
            if (shown == trackShown) return;
            int first = trackPage * trackShown;
            trackShown = shown;
            trackPage = first / shown;
        }

        private static void SetHelp(AvControl control, string help)
        {
            if (control.Help != help) control.Help = help;
        }

        private static void OnTrackRowClick(int slot)
        {
            int index = trackPage * trackShown + slot;
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
            int pages = manager == null ? 1 : Math.Max(1, (manager.DeckTrackCount + trackShown - 1) / trackShown);
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
        /// The band scope: the data-viz <see cref="RadioWaterfall"/> hosted inside a kit v2
        /// <see cref="AvPart"/>, plus a ruler, station markers and the needle that is also the
        /// tuning control (hover to preview, click to tune). It is the growing part of the RECEIVER
        /// page: the waterfall takes whatever height the page leaves over. Kit gap: nothing in v2
        /// combines a scrolled texture with pointer-driven ruler input, so this is built locally from
        /// primitives (<see cref="AvLay"/>, <see cref="AvText"/>) and the existing data-viz
        /// waterfall, per the module's data-viz allowlist.
        /// </summary>
        private sealed class BandScopePart : AvPart
        {
            private const float HeadH = 15f;
            private const float Top = HeadH + 3f;
            private const float WaterfallMin = 44f;
            private const float RulerH = 34f;
            private const float Gap = 6f;
            private const float SweepSeconds = 0.45f;

            private readonly TMP_Text key, note;
            private RadioWaterfall waterfall;
            private RectTransform tickLayer;
            private RectTransform markerLayer;
            private Image needle;
            private Image hoverCursor;
            private RectTransform hitRect;

            private RadioBand builtBand = (RadioBand)(-1);
            private float width;
            private float waterfallH = WaterfallMin;
            private float builtW = -1f, builtH = -1f;
            private int hoverKhz = int.MinValue;
            private int markerRevision = -1;
            private string idleNote = string.Empty;
            private RadioDial shownDial;
            private bool dialInitialized;
            private bool sweeping;
            private float sweepStart, sweepFrom, sweepTo;

            private float RulerTop => Top + waterfallH + Gap;
            private float ScopeHeight => RulerTop + RulerH;

            public BandScopePart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "BandScope");
                key = AvText.Make(Rect, "Key", AvTextRole.Micro, "// BAND SCOPE");
                AvText.Fit(key, false);
                note = AvText.Make(Rect, "Note", AvTextRole.Micro, string.Empty, TextAlignmentOptions.MidlineRight);
                AvText.Fit(note, false);
                Restyle();
            }

            public override float Measure(float w) => Top + WaterfallMin + Gap + RulerH;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                width = s.W;
                waterfallH = Mathf.Max(WaterfallMin, s.H - Top - Gap - RulerH);
                AvLay.Place(key.rectTransform, 0f, 0f, s.W * 0.4f, HeadH);
                AvLay.Place(note.rectTransform, s.W * 0.4f, 0f, s.W * 0.6f, HeadH);

                var area = new Rect(0f, -Top, s.W, waterfallH);
                if (waterfall == null) waterfall = new RadioWaterfall(Rect, area, RadioSpectrum.DefaultBins, 48);
                else if (Mathf.Abs(builtW - s.W) > 0.5f || Mathf.Abs(builtH - waterfallH) > 0.5f) waterfall.Resize(area);

                if (Mathf.Abs(builtW - s.W) > 0.5f || Mathf.Abs(builtH - waterfallH) > 0.5f || tickLayer == null)
                {
                    BuildTicks(manager == null ? RadioBand.Fm : manager.TunedDial.Band, s.W);
                    RefreshMarkers();
                }
                builtW = s.W;
                builtH = waterfallH;
                EnsureChrome();
                PlaceNeedle(manager == null || !manager.HasChannels ? 0f : manager.TunedDial.Fraction);
                shownDial = manager == null ? default : manager.TunedDial;
                dialInitialized = true;
                idleNote = BandRangeText();
                if (hoverKhz == int.MinValue) SetNote(idleNote);

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
                    AvHelpTip.Attach(hit.gameObject, "Band scope: the waterfall of the band, newest signal on top. Hover to preview a channel, " +
                        "click to tune to it. Coloured ticks below the ruler are stations; the bright needle is your dial.");
                }
                AvLay.Place(hitRect, 0f, Top, s.W, ScopeHeight - Top);
            }

            public override void Restyle()
            {
                key.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-key").Color, AvTheme.RailInfo);
                note.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Disabled);
            }

            private void SetNote(string text)
            {
                if (note.text != text) note.text = text ?? string.Empty;
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
                    if (hoverKhz == int.MinValue) SetNote(idleNote);
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
                float rulerTop = RulerTop;

                for (int khz = min; khz <= max; khz += minorStep)
                {
                    bool major = (khz - min) % labelStep == 0;
                    float x = TickX(khz, min, max, w);
                    Image tick = AvLay.Solid(tickLayer, "Tick", AvTheme.Hairline.WithAlpha(major ? 0.55f : 0.28f));
                    AvLay.Place(tick.rectTransform, x, rulerTop, 1f, major ? 10f : 6f);
                    if (!major) continue;

                    TMP_Text label = AvText.Make(tickLayer, "Label", AvTextRole.Micro, RadioBands.Format(band, khz));
                    float labelLeft = Mathf.Clamp(x - 26f, 0f, Mathf.Max(0f, w - 52f));
                    AvLay.Place(label.rectTransform, labelLeft, rulerTop + 12f, 52f, 18f);
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
                float rulerTop = RulerTop;
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
                AvLay.Place(needle.rectTransform, x, Top, 2f, ScopeHeight - Top);
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
                    SetNote("> " + RadioBands.Format(band, khz) + " " + RadioBands.UnitText(band) +
                        " · " + (name ?? "NO STATION") + (strength == null ? string.Empty : " · " + strength));
                }
                if (hoverCursor != null)
                {
                    int min = RadioBands.Min(builtBand), max = RadioBands.Max(builtBand);
                    float x = TickX(khz, min, max, width);
                    AvLay.Place(hoverCursor.rectTransform, x - 0.5f, Top, 1f, ScopeHeight - Top);
                    hoverCursor.enabled = true;
                }
            }

            private void OnExit()
            {
                hoverKhz = int.MinValue;
                if (hoverCursor != null) hoverCursor.enabled = false;
                SetNote(idleNote);
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
