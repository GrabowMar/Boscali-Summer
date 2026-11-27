using NOAvionics;
using System;
using BoscaliSummer.Modules.Radio.Runtime;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Radio.Presentation
{
    /// <summary>One local receiver: station programming and military receive are distinct controls.</summary>
    internal static class RadioPanel
    {
        private const int TabReceiver = 0, TabDeck = 1, PresetRows = 6, TrackRows = 15, MinTrackRows = 1;
        private const float ListRowHeight = 33f;
        private const string PlayTip = "Play or pause the selected track.";
        private const string PlayNoLibraryTip = "Add MP3, OGG or WAV music, then press RESCAN.";
        private const string PlayNoTracksTip = "This folder has no tracks.";
        private const string FolderPreviousTip = "Previous music folder.", FolderNextTip = "Next music folder.";
        private const string FirstFolderTip = "First folder.", LastFolderTip = "Last folder.";
        private const string TrackPreviousTip = "Previous track page.", TrackNextTip = "Next track page.";
        private const string FirstPageTip = "First page.", LastPageTip = "Last page.";
        private const string OpenFolderTip = "Open the local MP3/OGG/WAV music folder.";
        private const string RescanTip = "Stop music and rescan the local library.";
        private static readonly MfdPanelInstaller installer = new MfdPanelInstaller(MfdSlots.Rad, "RAD", "BoscaliRadio.Screen", preferLeft: false)
            { Wrap = true, Builder = BuildScreen };
        private static AvConsole console;
        private static RadioManager manager;
        private static RadioOnAirPart onAirPart;
        private static RadioStripPart dialStrip, deckStrip, militaryStrip;
        private static AvKeyHeader volumeHeader, presetHeader, folderHeader, trackHeader;
        private static readonly AvRow[] stationRows = new AvRow[PresetRows], trackRows = new AvRow[TrackRows];
        private static AvRow nowRow;
        private static AvHazardBar deckBar;
        private static AvAlert emptyAlert;
        private static bool deckHadFolders = true;
        private static int presetShown = PresetRows, trackShown = TrackRows, stationPage, trackPage, iconRevision = -1;
        private static float nextAttempt;
        private static readonly long[] trackKey = new long[TrackRows], stationKey = new long[PresetRows];
        private static readonly string[] trackTipName = new string[TrackRows];
        public static void Tick(RadioManager radio)
        {
            manager = radio;
            if (installer.Failed || !GameAccess.MfdAvailable || installer.Screen != null || Time.unscaledTime < nextAttempt) return;
            nextAttempt = Time.unscaledTime + 1f;
            installer.Log = Plugin.Logger;
            installer.Install();
        }
        public static void Reset()
        {
            installer.Reset(); RadioStationIconCache.Clear();
            console = null; manager = null; onAirPart = null; dialStrip = deckStrip = militaryStrip = null;
            volumeHeader = presetHeader = folderHeader = trackHeader = null;
            nowRow = null; deckBar = null; emptyAlert = null;
            Array.Clear(stationRows, 0, stationRows.Length); Array.Clear(trackRows, 0, trackRows.Length);
            Array.Clear(stationKey, 0, stationKey.Length); Array.Clear(trackKey, 0, trackKey.Length); Array.Clear(trackTipName, 0, trackTipName.Length);
            presetShown = PresetRows; trackShown = TrackRows; stationPage = trackPage = 0; iconRevision = -1; nextAttempt = 0f;
        }
        private static RectTransform BuildScreen(RectTransform display, float height)
        {
            console = AvConsole.Build(display, MfdSlots.Rad, "RADIO", 2, AvTokens.PanelWidth, height);
            console.Tabs((AvIcon.Radio, "BROADCAST"), (AvIcon.Music, "MUSIC"));
            BuildReceiver(console.Page(TabReceiver)); BuildDeck(console.Page(TabDeck));
            console.PageChanged += RefreshPage;
            console.Finish();
            RefreshPage(console.CurrentPage);
            return null;
        }

        private static void RefreshPage(int page)
        {
            if (page == TabReceiver) RefreshReceiver();
            else RefreshDeck();
        }
        private static void BuildReceiver(AvFlow p)
        {
            onAirPart = p.Add(new RadioOnAirPart(p.Content));
            onAirPart.Help = "Station music, adverts and news. Military communications use the independent channel below.";
            dialStrip = p.Add(new RadioStripPart(p.Content,
                new RadioStripItem("PREV", () => manager?.SeekStation(-1), AvButtonStyle.Quiet, AvIcon.None),
                new RadioStripItem("PLAY", () => manager?.TogglePlayback(), AvButtonStyle.Primary, AvIcon.None, 1.4f),
                new RadioStripItem("NEXT", () => manager?.SeekStation(1), AvButtonStyle.Quiet, AvIcon.None),
                new RadioStripItem("STOP", () => manager?.Stop(), AvButtonStyle.Danger, AvIcon.None)));
            dialStrip[0].Help = "Listen to the previous station."; dialStrip[2].Help = "Listen to the next station.";
            dialStrip[1].Help = "Play or pause this station."; dialStrip[3].Help = "Stop station programming. Military receive stays on.";
            volumeHeader = p.Add(new AvKeyHeader(p.Content, AvIcon.Music, "BROADCAST VOLUME",
                new AvControl.Spec("−", () => manager?.NudgeVolume(-.1f), AvButtonStyle.Quiet),
                new AvControl.Spec("+", () => manager?.NudgeVolume(.1f), AvButtonStyle.Quiet)));
            volumeHeader[0].Help = "Lower station and music deck volume."; volumeHeader[1].Help = "Raise station and music deck volume.";
            militaryStrip = p.Add(new RadioStripPart(p.Content,
                new RadioStripItem("MILITARY ON", () => manager?.ToggleMilitary(), AvButtonStyle.Toggle, AvIcon.None, 4f),
                new RadioStripItem("VOL −", () => manager?.NudgeMilitaryVolume(-.1f), AvButtonStyle.Quiet, AvIcon.None),
                new RadioStripItem("VOL +", () => manager?.NudgeMilitaryVolume(.1f), AvButtonStyle.Quiet, AvIcon.None)));
            militaryStrip[0].Help = "Receive military speech independently of station playback. Off stops the current transmission.";
            militaryStrip[1].Help = "Lower military speech volume."; militaryStrip[2].Help = "Raise military speech volume.";
            presetHeader = p.Add(new AvKeyHeader(p.Content, AvIcon.ListDetails, "STATIONS",
                new AvControl.Spec(string.Empty, () => manager?.Rescan(), AvButtonStyle.Quiet, AvIcon.Refresh),
                new AvControl.Spec(string.Empty, () => { if (stationPage > 0) stationPage--; RefreshReceiver(); }, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec(string.Empty, () => { stationPage++; RefreshReceiver(); }, AvButtonStyle.Quiet, AvIcon.ChevronRight)));
            presetHeader[0].Help = RescanTip; presetHeader[1].Help = "Previous station page."; presetHeader[2].Help = "Next station page.";
            for (int i = 0; i < PresetRows; i++)
            {
                int slot = i;
                stationRows[i] = p.Add(new AvRow(p.Content, () => manager?.SelectChannel(stationPage * presetShown + slot)));
                stationRows[i].BadgeSlot = true;
            }
            p.Ticker.Add(TabReceiver, AvTickRate.Slow, RefreshReceiver);
            console.Orders(TabReceiver, "Choose a station to listen. Military receive works with music stopped.");
        }
        private static void RefreshReceiver()
        {
            if (manager == null || console == null || onAirPart == null) return;
            if (iconRevision != manager.StationRevision) { RadioStationIconCache.Clear(); iconRevision = manager.StationRevision; }
            onAirPart.SetCompact(console.Page(TabReceiver).ViewportHeight > 0f && console.Page(TabReceiver).ViewportHeight < 340f);
            bool live = manager.IsEngaged && !manager.IsPaused;
            int selected = manager.SelectedChannel;
            onAirPart.Set(manager.CurrentChannelColor, manager.HasChannels ? RadioStationIconCache.Get(manager.GetChannelIconPath(selected)) : null,
                manager.CurrentChannelCode + " · " + manager.TunedDial.FrequencyText,
                manager.CurrentChannelName, live && manager.NuclearBlackout ? "STATIC" : live ? "ON AIR" : manager.IsPaused ? "PAUSED" : "OFF", live, manager.ShowLine,
                manager.NuclearBlackout ? "SIGNAL LOST" : live ? (manager.IsTalking ? "BROADCAST" : "NOW PLAYING") : "STATION PROGRAMMING",
                manager.IsEngaged ? manager.CurrentTrackTitle : "Choose a station, then play",
                live && !manager.NuclearBlackout ? manager.Progress : 0f,
                live && !manager.NuclearBlackout && manager.Duration > 0f ? AvNum.Clock(manager.Elapsed) + " / " + AvNum.Clock(manager.Duration) : string.Empty,
                live && !manager.NuclearBlackout ? "NEXT  " + manager.NextBreakText : string.Empty, string.Empty);
            volumeHeader.SetCaption(AvNum.Percent(manager.VolumeLevel));
            militaryStrip[0].Label = manager.ReceiveMilitary ? "MILITARY ON · " + AvNum.Percent(manager.MilitaryVolume) : "MILITARY OFF";
            militaryStrip[0].Latched = manager.ReceiveMilitary;
            dialStrip[1].Label = live ? "PAUSE" : manager.IsPaused ? "RESUME" : "PLAY";
            dialStrip[1].Latched = live;
            for (int i = 0; i < 4; i++) dialStrip[i].Interactable = manager.HasChannels && manager.BroadcastEnabled;
            FitPresetRows();
            int pages = Math.Max(1, (manager.ChannelCount + presetShown - 1) / presetShown);
            stationPage = Mathf.Clamp(stationPage, 0, pages - 1);
            presetHeader.SetCaption(RangeText(stationPage, presetShown, manager.ChannelCount, "NO STATIONS"));
            presetHeader[1].Interactable = stationPage > 0; presetHeader[2].Interactable = stationPage + 1 < pages;
            for (int i = 0; i < PresetRows; i++)
            {
                int index = stationPage * presetShown + i;
                AvRow row = stationRows[i]; row.SetShown(i < presetShown && index < manager.ChannelCount);
                if (index >= manager.ChannelCount) continue;
                bool active = index == selected;
                long key = ((long)(manager.StationRevision + 1) * 1000 + index + 1) * 2 + (active ? 1 : 0);
                if (stationKey[i] == key) continue;
                stationKey[i] = key;
                row.Set(manager.GetChannelName(index), manager.GetChannelSubtitle(index), active ? "TUNED" : manager.GetChannelDial(index).FrequencyText,
                    active ? AvState.Ready : AvState.Info);
                row.Armed = active; row.Interactable = true;
                row.Help = "Listen to " + manager.GetChannelName(index) + ".";
                row.SetBadge(RadioStationIconCache.Get(manager.GetChannelIconPath(index))?.texture);
            }
            console.Footer.Set(manager.Status, AvState.Inert);
        }
        private static void FitPresetRows()
        {
            AvFlow p = console.Page(TabReceiver); float width = p.Inner;
            float fixedHeight = 2f * AvGridTokens.Pad + 6f * AvGridTokens.Gap + onAirPart.Measure(width) + dialStrip.Measure(width) +
                volumeHeader.Measure(width) + militaryStrip.Measure(width) + presetHeader.Measure(width);
            float rowHeight = ListRowHeight;
            foreach (AvRow row in stationRows) if (row != null) rowHeight = Mathf.Max(rowHeight, row.Measure(width) + AvGridTokens.Gap);
            int shown = p.ViewportHeight <= 0f ? PresetRows : Mathf.Clamp(Mathf.FloorToInt((p.ViewportHeight - fixedHeight) / rowHeight), 1, PresetRows);
            if (shown == presetShown) return;
            int first = stationPage * presetShown;
            presetShown = shown;
            stationPage = first / shown;
        }
        private static void BuildDeck(AvFlow p)
        {
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

            folderHeader = p.Add(new AvKeyHeader(p.Content, AvIcon.Music, "MUSIC",
                new AvControl.Spec(string.Empty, () => { NudgeFolder(-1); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec(string.Empty, () => { NudgeFolder(1); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronRight),
                new AvControl.Spec(string.Empty, () => manager?.Rescan(), AvButtonStyle.Quiet, AvIcon.Refresh),
                new AvControl.Spec(string.Empty, () => manager?.OpenLibraryFolder(), AvButtonStyle.Quiet, AvIcon.Focus2)));
            folderHeader[0].Help = FolderPreviousTip;
            folderHeader[1].Help = FolderNextTip;
            folderHeader[2].Help = RescanTip;
            folderHeader[3].Help = OpenFolderTip;

            trackHeader = p.Add(new AvKeyHeader(p.Content, AvIcon.ListDetails, "TRACKS",
                new AvControl.Spec(string.Empty, () => { PreviousTrackPage(); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec(string.Empty, () => { NextTrackPage(); RefreshDeck(); }, AvButtonStyle.Quiet, AvIcon.ChevronRight)));

            for (int i = 0; i < TrackRows; i++)
            {
                int captured = i;
                trackRows[i] = p.Add(new AvRow(p.Content, () => OnTrackRowClick(captured)));
            }

            p.Ticker.Add(TabDeck, AvTickRate.Fast, RefreshDeck);

            console.Orders(TabDeck, "Click a track to play it; the deck then steps through its folder in order.");
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
            bool loaded = hasFolders && trackCount > 0;

            string trackTitle = loaded ? manager.DeckCurrentTitle : string.Empty;
            string position = hasFolders && trackCount > 0
                ? "TRACK " + AvNum.Fixed(track, 0) + " / " + AvNum.Fixed(trackCount, 0)
                : string.Empty;
            string timeText = playing
                ? AvNum.Clock(manager.DeckElapsed) + " / " + AvNum.Clock(manager.DeckDuration)
                : "--:-- / --:--";
            nowRow.Set(trackTitle, position, string.Empty, playing ? AvState.Ready : AvState.Info);
            float progress = playing ? Mathf.Clamp01(manager.DeckProgress) : 0f;
            deckBar.Set(progress, timeText, playing ? AvState.Ready : AvState.Inert);

            // Nothing loaded: the one card below says so, and the track line, bar and transport stay out of the way.
            nowRow.SetShown(loaded);
            deckBar.SetShown(loaded);
            deckStrip.SetShown(hasFolders);

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

            folderHeader.SetTitle(hasFolders ? manager.DeckFolderName(folder) : "MUSIC");
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
                    trackKey[i] = 0;
                    row.Set(string.Empty, string.Empty, string.Empty, AvState.Inert);
                    row.Armed = false;
                    row.Interactable = false;
                    if (trackTipName[i] != null) { trackTipName[i] = null; row.Help = null; }
                    continue;
                }

                bool active = index == current && playing;
                int percent = active ? Mathf.RoundToInt(Mathf.Clamp01(manager.DeckProgress) * 100f) : -1;
                long key = ((long)(index + 1) * 200 + percent + 1) * 2 + (active ? 1 : 0);
                string title = manager.DeckTrackTitle(index);
                if (trackKey[i] == key && trackTipName[i] == title) continue;
                trackKey[i] = key;

                row.Interactable = true;
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
                emptyAlert.Show(AvIcon.AlertTriangle, "NO MUSIC ON FILE",
                    "Put MP3, OGG or WAV files in BepInEx/config/BoscaliSummer/Music, then press the rescan key. " +
                    "The folder key opens it.", AvState.Caution);
            else if (trackCount == 0)
                emptyAlert.Show(AvIcon.Music, "FOLDER EMPTY",
                    "Choose another folder with the arrow keys, or add MP3/OGG/WAV files and press RESCAN.", AvState.Info);
            else emptyAlert.Hide();
            console.Footer.Set(hasFolders ? manager.DeckStatus : string.Empty, AvState.Inert);
        }

        /// <summary>The track list shows as many rows as the page can hold, so a short console pages instead of scrolling.</summary>
        private static void FitTrackRows()
        {
            AvFlow page = console.Page(TabDeck);
            float width = page.Inner;
            float fixedHeight = 2f * AvGridTokens.Pad + 4f * AvGridTokens.Gap +
                nowRow.Measure(width) + deckBar.Measure(width) +
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

        private static string RangeText(int page, int perPage, int count, string empty)
        {
            if (count <= 0) return empty;
            int first = page * perPage + 1;
            int last = Math.Min(count, first + perPage - 1);
            return AvNum.Fixed(first, 0) + " - " + AvNum.Fixed(last, 0) + " OF " + AvNum.Fixed(count, 0);
        }

        private static void Gate(AvControl control, bool enabled, string yes, string no)
        { control.Interactable = enabled; SetHelp(control, enabled ? yes : no); }
    }
}
