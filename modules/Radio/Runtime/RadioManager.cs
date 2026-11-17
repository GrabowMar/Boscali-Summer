using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Radio.Configuration;
using BoscaliSummer.Modules.Radio.Presentation;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>Client-local station programming and local music. Military traffic has its own receive switch;
    /// the channel contract is the seam for future tuned communications, without simulating RF every frame.</summary>
    internal sealed class RadioManager : MonoBehaviour, ISceneService, IRadioRemote, IRadioComms
    {
        private const float HoldSweepSeconds = 0.5f;
        private const float DeckCrossfadeSeconds = 0.4f;
        private static RadioManager active;
        private RadioSettings settings;
        private RadioLibrary localLibrary;
        private RadioStation[] stations = Array.Empty<RadioStation>();
        private VanillaSoundtrackCatalog soundtrackCatalog;
        private string libraryPath;
        private RadioProgram receiver, deck;
        private string status = "Choose a station", programText = string.Empty, ticker;
        private int selectedChannel, selectedTrack, stationRevision;
        private RadioDial tunedDial = RadioDial.Fm(88500);
        private readonly VanillaMusicHold hold = new VanillaMusicHold();
        private string deckStatus = "Deck stopped";
        private int deckFolder, deckTrack;
        private bool configured, vanillaHeld;
        private AudioClip interruptedVanillaClip, deferredVanillaClip;
        private float interruptedVanillaTime, deferredVanillaPriority, nextVanillaSweep, nextSoundtrackProbe;
        private bool interruptedVanillaLoop, deferredVanillaRepeat, deferredVanillaCrossfade;
        private int soundtrackMisses;
        private float restoreTime = -1f;
        private float nextProgramRefresh, duckUntil, duckGain = 1f;
        private AudioSource aftermathStatic;
        private int aftermathRevision;
        private readonly Queue<RadioStationTrack> breakQueue = new Queue<RadioStationTrack>();
        private RadioStationTrack segmentTrack, songTrack;
        private int songIndex, breakIndex;
        private sealed class StationCursor
        {
            public int Track, Song, Break;
            public RadioStationTrack OwnSong;
            public bool Music;
            public float Elapsed, Duration, LeftAt;
        }
        private readonly Dictionary<string, StationCursor> cursors = new Dictionary<string, StationCursor>();

        public int ChannelCount => stations.Length;
        public bool HasChannels => ChannelCount > 0;
        public bool BroadcastEnabled => settings != null && settings.Enabled.Value;
        public int SelectedChannel => selectedChannel;
        public int StationRevision => stationRevision;
        public string Status => RadioAftermath.Active ? "SA-74N · SIGNAL LOST" : status;
        public string TickerText => RadioAftermath.Active ? "No station signal. Only static and scattered survivor transmissions." : ticker ?? "Music, adverts and news. Military traffic uses the military channel.";
        public string CurrentProgram => RadioAftermath.Active ? "COMMUNICATIONS BLACKOUT" : programText;
        public bool IsEngaged => receiver != null && receiver.Engaged;
        public bool IsPaused => receiver != null && receiver.IsPaused;
        public RadioDial TunedDial => tunedDial;
        public float VolumeLevel => settings?.Volume.Value ?? 1f;
        public float Elapsed => receiver?.Elapsed ?? 0f;
        public float Progress => receiver?.Progress ?? 0f;
        public float Duration => receiver?.Duration ?? 0f;
        public string CurrentChannelName => CurrentChannel()?.Name ?? "NO STATION";
        public string CurrentChannelCode => CurrentChannel()?.Code ?? "--";
        public string CurrentTrackTitle => RadioAftermath.Active ? "STATIC / NO BROADCAST" : CurrentTrack()?.Title ?? "NO TRACK";
        public Color CurrentChannelColor => GetChannelColor(selectedChannel);
        public bool IsTalking => !RadioAftermath.Active && segmentTrack != null;
        public string ShowLine => CurrentProgram;
        public bool Shuffle => settings != null && settings.Shuffle.Value;
        public bool RepeatTrack => settings != null && settings.RepeatTrack.Value;
        public bool ReceiveMilitary => settings == null || settings.ReceiveMilitary.Value;
        public bool NuclearBlackout => RadioAftermath.Active;
        public float MilitaryVolume => settings?.MilitaryVolume.Value ?? 0.9f;
        public void ToggleMilitary() { settings.ReceiveMilitary.Value = !ReceiveMilitary; if (!ReceiveMilitary) duckUntil = 0f; }
        public void NudgeMilitaryVolume(float delta) => settings.MilitaryVolume.Value = Mathf.Clamp01(MilitaryVolume + delta);
        public void DuckMilitary(float seconds)
        {
            if (ReceiveMilitary && seconds > 0f && !float.IsNaN(seconds))
                duckUntil = Mathf.Max(duckUntil, Time.unscaledTime + Mathf.Min(seconds, 60f));
        }
        public string GetChannelName(int index) => index >= 0 && index < ChannelCount ? stations[index].Name : string.Empty;
        public int GetChannelTrackCount(int index) => index >= 0 && index < ChannelCount ? stations[index].Tracks.Length : 0;
        public string GetChannelIconPath(int index) => index >= 0 && index < ChannelCount ? stations[index].IconPath : string.Empty;
        public RadioDial GetChannelDial(int index) => index >= 0 && index < ChannelCount ? stations[index].Dial : RadioDial.Fm(88500);
        public string GetChannelSubtitle(int index)
        {
            if (index < 0 || index >= ChannelCount) return string.Empty;
            switch (stations[index].Id)
            {
                case BuiltInStationRules.AgrapolId: return "Republic public service · music / ads / news";
                case BuiltInStationRules.MarisId: return "State broadcast · music / ads / news";
                case BuiltInStationRules.BaseId: return "Mercenary group station · open to every faction";
                default: return "Local music · " + stations[index].Tracks.Length + " tracks";
            }
        }

        public Color GetChannelColor(int index)
        {
            if (index < 0 || index >= ChannelCount) return new Color(0.45f, 0.95f, 0.55f);
            switch (stations[index].Id)
            {
                case "agrapol-fm": return new Color(0.95f, 0.77f, 0.43f);
                case "maris-network": return new Color(0.89f, 0.41f, 0.41f);
                case "base-broadcast": return new Color(0.62f, 0.80f, 0.88f);
                default: return new Color(0.45f, 0.95f, 0.55f);
            }
        }

        public int DeckFolderCount => localLibrary == null ? 0 : localLibrary.Channels.Length;
        public int DeckFolder => Mathf.Clamp(deckFolder, 0, Mathf.Max(0, DeckFolderCount - 1));
        public int DeckTrackIndex => DeckTrackCount == 0 ? -1 : Mathf.Clamp(deckTrack, 0, DeckTrackCount - 1);
        public int DeckTrackCount => DeckFolderTrackCount(DeckFolder);
        public string DeckStatus => RadioAftermath.Active ? "Signal lost · static only" : deckStatus;
        public bool DeckEngaged => deck.Engaged;
        public bool DeckPlaying => deck.IsPlaying;
        public bool DeckPaused => deck.IsPaused;
        public float DeckElapsed => deck.Elapsed;
        public float DeckDuration => deck.Duration;
        public float DeckProgress => deck.Progress;
        public string DeckCurrentTitle
        {
            get
            {
                if (RadioAftermath.Active) return "STATIC / SIGNAL LOST";
                RadioTrack track = DeckTrackAt(DeckFolder, deckTrack);
                return track == null ? "NO TRACK" : track.Title;
            }
        }

        public string DeckFolderName(int index) =>
            index >= 0 && index < DeckFolderCount ? localLibrary.Channels[index].Name : string.Empty;

        public int DeckFolderTrackCount(int index) =>
            index >= 0 && index < DeckFolderCount ? localLibrary.Channels[index].Tracks.Length : 0;

        public string DeckTrackTitle(int index)
        {
            RadioTrack track = DeckTrackAt(DeckFolder, index);
            return track == null ? string.Empty : track.Title;
        }

        internal void Configure(RadioSettings radioSettings, ServiceRegistry registry)
        {
            settings = radioSettings ?? throw new ArgumentNullException(nameof(radioSettings));
            libraryPath = BoscaliSummer.Core.Storage.ModFolders.UserContent("Music", Plugin.Logger);
            receiver = new RadioProgram(this, gameObject, "BoscaliRadio", Plugin.Logger);
            deck = new RadioProgram(this, gameObject, "BoscaliDeck", Plugin.Logger);
            receiver.SetCrossfade(settings.CrossfadeSeconds.Value);
            deck.SetCrossfade(DeckCrossfadeSeconds);
            configured = true;
            active = this;
            if (settings.Enabled.Value && !GameManager.IsHeadless) ScanLibrary();
        }
        public void ResetForScene()
        {
            if (aftermathStatic != null) aftermathStatic.Stop();
            aftermathRevision = -1;
            RadioPanel.Reset();
            receiver?.Stop(); deck?.Stop(); hold.Reset();
            receiver?.SetMuted(false); deck?.SetMuted(false);
            ClearBreak(); cursors.Clear(); songIndex = breakIndex = 0;
            duckUntil = 0f; duckGain = 1f; restoreTime = -1f;
            programText = string.Empty; ticker = null; nextProgramRefresh = 0f;
            // Scene-owned clips cannot be restored on the next map.
            interruptedVanillaClip = deferredVanillaClip = null; vanillaHeld = false;
            soundtrackCatalog = null; soundtrackMisses = 0; nextSoundtrackProbe = nextVanillaSweep = 0f;
            if (!configured || GameManager.IsHeadless) { enabled = false; return; }
            enabled = true;
            if (localLibrary == null && settings.Enabled.Value) ScanLibrary(); else BuildStations();
        }
        private void Update()
        {
            if (!configured || GameManager.IsHeadless) return;
            bool blackout = RadioAftermath.Active;
            receiver.SetMuted(blackout); deck.SetMuted(blackout);
            if (blackout && aftermathRevision != RadioAftermath.Revision)
            {
                aftermathRevision = RadioAftermath.Revision;
                ClearBreak(); cursors.Clear();
            }
            if (settings.Enabled.Value && !blackout)
            {
                ProbeSoundtrack();
                ProgramTick();
                if (receiver.Finished) NextTrack();
                AdvanceIfDue(deck, DeckCrossfadeSeconds, AdvanceDeck);
            }
            else if (!settings.Enabled.Value && (receiver.Engaged || deck.Engaged)) StopAll();
            RadioPanel.Tick(this);
            float target = ReceiveMilitary && MilitaryVolume > 0f && Time.unscaledTime < duckUntil ? 0.22f : 1f;
            duckGain = Mathf.MoveTowards(duckGain, target, Time.unscaledDeltaTime / (target < duckGain ? 0.1f : 0.45f));
            receiver.SetVolume(Volume * duckGain);
            deck.SetVolume(Volume * duckGain);
            UpdateAftermathStatic();
            SyncVanillaHold();
        }
        private void OnDestroy()
        {
            if (aftermathStatic != null) aftermathStatic.Stop();
            RadioPanel.Reset(); hold.Reset();
            receiver?.Dispose(); deck?.Dispose();
            receiver = deck = null;
            if (vanillaHeld) { vanillaHeld = false; ReleaseVanillaHold(); }
            if (ReferenceEquals(active, this)) active = null;
        }
        private void OnDisable() { if (aftermathStatic != null) aftermathStatic.Stop(); }

        private bool NuclearListening => RadioAftermath.Active && ReceiveMilitary && MilitaryVolume > 0f;

        private void UpdateAftermathStatic()
        {
            float station = BroadcastEnabled && (receiver.Engaged && !receiver.IsPaused || deck.Engaged && !deck.IsPaused)
                ? Volume * AudioMixerVolume.GetPref(AudioMixerVolume.Music) : 0f;
            float wanted = RadioAftermath.Active ? Mathf.Max(station, ReceiveMilitary ? MilitaryVolume * .45f : 0f) : 0f;
            if (wanted <= 0f)
            {
                if (aftermathStatic != null) { aftermathStatic.Stop(); aftermathStatic.volume = 0f; }
                return;
            }
            if (SoundManager.i == null) return;
            if (aftermathStatic == null)
            {
                AudioClip clip = SfxClips.Get("nuclear-radio-static");
                if (clip == null) return;
                aftermathStatic = gameObject.AddComponent<AudioSource>();
                aftermathStatic.clip = clip; aftermathStatic.loop = true;
                aftermathStatic.spatialBlend = 0f; aftermathStatic.playOnAwake = false;
                aftermathStatic.priority = 32; aftermathStatic.volume = 0f;
            }
            aftermathStatic.outputAudioMixerGroup = SoundManager.i.InterfaceMixer;
            aftermathStatic.volume = Mathf.MoveTowards(aftermathStatic.volume, wanted * duckGain, Time.unscaledDeltaTime * 4f);
            if (!aftermathStatic.isPlaying) aftermathStatic.Play();
        }

        private void PlayStaticReceiver()
        {
            deck.Stop(); ClearBreak();
            receiver.SetMuted(true); receiver.SetCrossfade(0f);
            if (receiver.PlayClip(SfxClips.Get("nuclear-radio-static"), false, 0f, false)) hold.EngageReceiver();
            SyncVanillaHold();
        }
        public void SelectChannel(int index)
        {
            if (!BroadcastEnabled || index < 0 || index >= ChannelCount) return;
            if (index != selectedChannel) SelectChannelInternal(index);
            if (!receiver.Engaged) PlayOnTune();
        }
        public void SeekStation(int direction)
        {
            if (HasChannels && direction != 0) SelectChannel((selectedChannel + (direction > 0 ? 1 : -1) + ChannelCount) % ChannelCount);
        }
        public void TogglePlayback()
        {
            if (!BroadcastEnabled) return;
            if (receiver.IsPlaying) { receiver.Pause(); status = "Station paused"; }
            else if (receiver.IsPaused) { deck.Stop(); receiver.Resume(); status = "On air"; }
            else PlayOnTune();
        }
        public void Stop()
        {
            SaveCursor(); receiver?.Stop(); hold.ReleaseReceiver();
            status = "Station off"; SyncVanillaHold();
        }
        public void DeckToggleShuffle()
        {
            settings.Shuffle.Value = !settings.Shuffle.Value;
            status = settings.Shuffle.Value ? "Shuffle enabled" : "Shuffle disabled";
            deckStatus = status;
        }

        public void DeckToggleRepeat()
        {
            settings.RepeatTrack.Value = !settings.RepeatTrack.Value;
            status = settings.RepeatTrack.Value ? "Repeat enabled" : "Repeat disabled";
            deckStatus = status;
        }

        public void Rescan() { StopAll(); cursors.Clear(); ScanLibrary(); }
        public void OpenLibraryFolder()
        {
            try
            {
                RadioStarterLayout.Ensure(libraryPath, Plugin.Logger);
                Application.OpenURL(new Uri(libraryPath).AbsoluteUri);
                status = "Opened music folder";
                deckStatus = status;
            }
            catch (Exception e)
            {
                status = "Could not open music folder";
                Plugin.Logger?.LogWarning("Radio station folder could not be opened: " + e.Message);
            }
        }

        public void NudgeVolume(float delta)
        {
            if (settings == null || delta == 0f) return;
            settings.Volume.Value = Mathf.Clamp01(settings.Volume.Value + delta);
            status = "Volume " + Mathf.RoundToInt(settings.Volume.Value * 100f) + "%";
        }

        public void DeckSelectFolder(int index)
        {
            if (index < 0 || index >= DeckFolderCount) return;
            deckFolder = index;
            deckTrack = 0;
            deckStatus = DeckFolderName(index) + " · " + DeckFolderTrackCount(index) + " track(s)";
        }

        public void DeckPlay(int trackIndex)
        {
            if (!BroadcastEnabled) return;
            RadioTrack track = DeckTrackAt(DeckFolder, trackIndex);
            if (track == null)
            {
                deckStatus = "No track on this folder";
                return;
            }
            deckTrack = trackIndex;
            deck.SetMuted(RadioAftermath.Active);
            bool started = RadioAftermath.Active ? deck.PlayClip(SfxClips.Get("nuclear-radio-static"), false, 0f, false)
                : deck.PlayFile(track.Path, track.Extension, track.Title, 0f, false);
            if (!started)
            {
                deckStatus = deck.LastError ?? "Could not open " + track.Title;
                return;
            }
            deckStatus = "Playing " + track.Title;
            Stop();
        }

        public void DeckTogglePlayback()
        {
            if (deck.IsPlaying)
            {
                deck.Pause();
                deckStatus = "Paused";
                return;
            }
            if (deck.IsPaused)
            {
                Stop();
                deck.Resume();
                deckStatus = "Playing " + DeckCurrentTitle;
                return;
            }
            Stop();
            DeckPlay(deckTrack);
        }

        public void DeckStop()
        {
            deck.Stop();
            deckStatus = "Deck stopped";
            SyncVanillaHold();
        }

        /// <summary>Both transports down, soundtrack released: the deck page's STOP ALL.</summary>
        public void StopAll()
        {
            Stop();
            DeckStop();
        }

        public void DeckNext()
        {
            int count = DeckTrackCount;
            if (count == 0) return;
            int next = settings.Shuffle.Value && count > 1
                ? ShuffleIndex(count, DeckTrackIndex)
                : (DeckTrackIndex + 1) % count;
            DeckPlay(next);
        }

        public void DeckPrevious()
        {
            int count = DeckTrackCount;
            if (count == 0) return;
            DeckPlay((DeckTrackIndex - 1 + count) % count);
        }

        // ------------------------------------------------------------------ remote face

        bool IRadioRemote.DeckAvailable => DeckTrackCount > 0;
        bool IRadioRemote.DeckPlaying => DeckPlaying;
        string IRadioRemote.DeckTitle => DeckCurrentTitle;
        void IRadioRemote.DeckTogglePlayback() => DeckTogglePlayback();
        void IRadioRemote.DeckNext() => DeckNext();
        void IRadioRemote.DeckPrevious() => DeckPrevious();
        bool IRadioRemote.ReceiverAvailable => HasChannels;
        bool IRadioRemote.ReceiverOn => IsEngaged;
        string IRadioRemote.ReceiverStation => CurrentChannelName;
        void IRadioRemote.ReceiverSeek(int direction) => SeekStation(direction);
        void IRadioRemote.StopAll() => StopAll();

        void IRadioRemote.ReceiverToggle()
        {
            if (IsEngaged) Stop();
            else TogglePlayback();
        }

        private RadioTrack DeckTrackAt(int folder, int index)
        {
            if (localLibrary == null || folder < 0 || folder >= localLibrary.Channels.Length) return null;
            RadioTrack[] tracks = localLibrary.Channels[folder].Tracks;
            return index < 0 || index >= tracks.Length ? null : tracks[index];
        }

        // ------------------------------------------------------------------- registry

        private void ScanLibrary()
        {
            try
            {
                RadioStarterLayout.Ensure(libraryPath, Plugin.Logger);
                localLibrary = RadioLibrary.Scan(libraryPath);
                BuildStations();
                selectedChannel = Mathf.Clamp(selectedChannel, 0, Math.Max(0, ChannelCount - 1));
                selectedTrack = 0;
                deckFolder = Mathf.Clamp(deckFolder, 0, Math.Max(0, DeckFolderCount - 1));
                deckTrack = 0;
                status = localLibrary.TrackCount == 0
                    ? "Built-in stations ready; add OGG/WAV for more"
                    : localLibrary.TrackCount + " local track(s) ready";
                Plugin.Logger?.LogInfo("Radio library: " + localLibrary.TrackCount +
                    " local track(s) across " + ChannelCount + " station(s).");
            }
            catch (Exception e)
            {
                localLibrary = null;
                stations = Array.Empty<RadioStation>();
                status = "Library scan failed";
                Plugin.Logger?.LogWarning("Radio library scan failed: " + e.Message);
            }
        }

        private void ProbeSoundtrack()
        {
            if (soundtrackCatalog != null || Time.unscaledTime < nextSoundtrackProbe) return;
            nextSoundtrackProbe = Time.unscaledTime + (soundtrackMisses < 5 ? 1f : 10f); // back off once the mission-start window has passed
            if (!VanillaSoundtrackCatalog.TryCreate(out VanillaSoundtrackCatalog catalog)) { soundtrackMisses++; return; }
            soundtrackMisses = 0;
            string selectedName = CurrentChannelName;
            soundtrackCatalog = catalog;
            BuildStations();
            for (int i = 0; i < stations.Length; i++)
                if (string.Equals(stations[i].Name, selectedName, StringComparison.OrdinalIgnoreCase))
                    selectedChannel = i;
            status = "Original soundtrack linked to built-in stations";
            Plugin.Logger?.LogInfo("Radio soundtrack adapter: " + catalog.All.Length +
                " installed vanilla clip(s) available.");
        }

        private void BuildStations()
        {
            var result = new List<RadioStation>(RadioLibrary.MaximumChannels);
            var usedFmSlots = new HashSet<int>();
            RadioChannel agrapolLocal = FindLocalChannel("Boscali Republic Radio");
            RadioChannel marisLocal = FindLocalChannel("PALA State Radio");
            RadioChannel baseLocal = FindLocalChannel("Base Broadcast");
            // Offset the installed soundtrack by station so their rotations differ.
            AddBuiltInStation(result, BuiltInStationRules.AgrapolId, "BR", "Boscali Republic Radio",
                Rotation(soundtrackCatalog?.All, soundtrackCatalog?.AgrapolSeed, 0), agrapolLocal,
                BuiltInIcon("agrapol-fm.png"), usedFmSlots);
            AddBuiltInStation(result, BuiltInStationRules.MarisId, "PS", "PALA State Radio",
                Rotation(soundtrackCatalog?.All, soundtrackCatalog?.MarisSeed, 1), marisLocal,
                BuiltInIcon("maris-network.png"), usedFmSlots);
            AddBuiltInStation(result, BuiltInStationRules.BaseId, "BB", "Base Broadcast",
                Rotation(soundtrackCatalog?.All, null, 2), baseLocal,
                BuiltInIcon("base-broadcast.png"), usedFmSlots);

            if (localLibrary != null)
            {
                for (int i = 0; i < localLibrary.Channels.Length &&
                    result.Count < RadioLibrary.MaximumChannels; i++)
                {
                    RadioChannel channel = localLibrary.Channels[i];
                    if (IsBuiltInName(channel.Name)) continue;
                    var tracks = new RadioStationTrack[channel.Tracks.Length];
                    for (int track = 0; track < tracks.Length; track++)
                        tracks[track] = RadioStationTrack.Local(channel.Tracks[track]);
                    result.Add(new RadioStation(
                        "user-" + channel.Name.ToLowerInvariant(), StationCode(channel.Name),
                        channel.Name, StationIconPath(channel.Name),
                        RadioDialAllocation.Allocate(channel.Name, usedFmSlots), tracks));
                }
            }
            stations = result.ToArray();
            tunedDial = GetChannelDial(selectedChannel);
            stationRevision++;
        }

        private void AddBuiltInStation(
            List<RadioStation> result,
            string id,
            string code,
            string name,
            AudioClip[] vanilla,
            RadioChannel local,
            string iconSource,
            HashSet<int> usedFmSlots)
        {
            int discoveredLocalCount = local?.Tracks.Length ?? 0;
            int localCount = BuiltInStationRules.AcceptsLocalTracks(id) ? discoveredLocalCount : 0;
            int vanillaCount = BuiltInStationRules.UsesVanillaTracks(localCount)
                ? vanilla.Length
                : 0;
            var tracks = new RadioStationTrack[vanillaCount + localCount];
            for (int i = 0; i < vanillaCount; i++)
                tracks[i] = RadioStationTrack.Vanilla(vanilla[i]);
            for (int i = 0; i < localCount; i++)
                tracks[vanillaCount + i] = RadioStationTrack.Local(local.Tracks[i]);

            if (!RadioDialAllocation.TryBuiltIn(id, out RadioDial dial))
                dial = RadioDialAllocation.Allocate(name, usedFmSlots);
            else if (RadioDialAllocation.TryFmSlot(dial, out int slot))
                usedFmSlots.Add(slot);

            result.Add(new RadioStation(id, code, name, iconSource, dial, tracks));
        }

        private static AudioClip[] Rotation(AudioClip[] all, AudioClip seed, int station)
        {
            if (all == null || all.Length == 0) return Array.Empty<AudioClip>();
            int start = seed != null ? Array.IndexOf(all, seed) : -1;
            if (start < 0) start = station * all.Length / 3;
            var result = new AudioClip[all.Length];
            for (int i = 0; i < all.Length; i++) result[i] = all[(start + i) % all.Length];
            return result;
        }

        private static string BuiltInIcon(string fileName) =>
            RadioStationIconCache.EmbeddedPrefix + "BoscaliSummer.RadioAssets." + fileName;

        private string StationIconPath(string stationName) =>
            Path.Combine(libraryPath, stationName, "station.png");

        private RadioChannel FindLocalChannel(string name)
        {
            if (localLibrary == null) return null;
            for (int i = 0; i < localLibrary.Channels.Length; i++)
                if (string.Equals(localLibrary.Channels[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return localLibrary.Channels[i];
            return null;
        }

        private static bool IsBuiltInName(string name) =>
            string.Equals(name, "Boscali Republic Radio", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "PALA State Radio", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Base Broadcast", StringComparison.OrdinalIgnoreCase);

        private static string StationCode(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "--";
            string[] words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
                return char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[1][0]);
            string clean = words.Length == 0 ? name.Trim() : words[0];
            return clean.Length == 1 ? clean.ToUpperInvariant() : clean.Substring(0, 2).ToUpperInvariant();
        }

        private void PlayCurrent()
        {
            if (RadioAftermath.Active) { PlayStaticReceiver(); return; }
            RadioStationTrack track = CurrentTrack();
            if (track == null) { status = "No playable track on this station"; return; }
            deck.Stop();
            // Keep speech boundaries clean: ads and news must never overlap the next item.
            receiver.SetCrossfade(segmentTrack == null ? settings.CrossfadeSeconds.Value : 0.06f);
            bool started = track.IsLocal ? receiver.PlayFile(track.LocalPath, track.Extension, track.Title, restoreTime, false)
                : track.VanillaClip != null && receiver.PlayClip(track.VanillaClip, false, restoreTime, false);
            restoreTime = -1f;
            if (!started) { status = receiver.LastError ?? "Station audio unavailable"; return; }
            hold.EngageReceiver(); BeginVanillaHold();
            status = (segmentTrack == null ? "Music · " : "On air · ") + track.Title;
            ticker = track.Title;
            SyncVanillaHold();
        }
        private float Volume => settings?.Volume.Value ?? 1f;
        private RadioStation CurrentChannel() => ChannelCount == 0 ? null : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)];
        private RadioStationTrack CurrentTrack()
        {
            if (segmentTrack != null) return segmentTrack;
            if (songTrack != null) return songTrack;
            RadioStation channel = CurrentChannel();
            if (channel == null || channel.Tracks.Length == 0) return null;
            selectedTrack = Mathf.Clamp(selectedTrack, 0, channel.Tracks.Length - 1);
            return channel.Tracks[selectedTrack];
        }

        private void NextTrack() => NextTrack(false);

        private void NextTrack(bool skipBreak)
        {
            if (RadioAftermath.Active) { PlayStaticReceiver(); return; }
            // A single advert or news item follows each song.
            if (!skipBreak && breakQueue.Count == 0 && segmentTrack == null && settings.BroadcastTalk.Value)
                foreach (RadioStationTrack segment in RadioBroadcastSegments.Break(
                    CurrentStationId, null, breakIndex++))
                    breakQueue.Enqueue(segment);
            if (breakQueue.Count > 0)
            {
                segmentTrack = breakQueue.Dequeue();
                PlayCurrent();
                return;
            }
            segmentTrack = null;

            // Every other song is the station's own (all of them when there is no soundtrack).
            RadioStation channel = CurrentChannel();
            bool noProgramme = channel == null || channel.Tracks.Length == 0;
            songTrack = (noProgramme || songIndex % 2 == 0)
                ? RadioBroadcastSegments.Song(CurrentStationId, null, noProgramme ? songIndex : songIndex / 2)
                : null;
            songIndex++;
            if (songTrack != null)
            {
                PlayCurrent();
                return;
            }
            if (noProgramme) return;
            selectedTrack = (selectedTrack + 1) % channel.Tracks.Length;
            PlayCurrent();
        }

#if !PUBLIC_RELEASE
        /// <summary>Sim hook: tune a built-in station by id, start it, and on later calls end the current item early.</summary>
        internal static Dictionary<string, object> AutomationStep(string stationId, string faction, bool advance, string action, bool? receive, float? militaryVolume, float? musicVolume)
        {
            RadioManager radio = active;
            if (radio == null || !radio.configured) return null;
            if (receive.HasValue) radio.settings.ReceiveMilitary.Value = receive.Value;
            if (militaryVolume.HasValue) radio.settings.MilitaryVolume.Value = Mathf.Clamp01(militaryVolume.Value);
            if (musicVolume.HasValue) radio.settings.Volume.Value = Mathf.Clamp01(musicVolume.Value);
            if (action == "stop") { radio.StopAll(); return radio.AutomationState(); }
            if (action == "state") return radio.AutomationState();
            if (!string.IsNullOrEmpty(stationId))
                for (int i = 0; i < radio.stations.Length; i++)
                    if (radio.stations[i].Id == stationId && i != radio.selectedChannel) radio.SelectChannelInternal(i);
            if (!radio.receiver.Engaged) radio.PlayOnTune();
            else if (advance) radio.NextTrack();
            return radio.AutomationState();
        }

        internal Dictionary<string, object> AutomationState() => new Dictionary<string, object>
        {
            ["ok"] = true,
            ["receive_military"] = ReceiveMilitary,
            ["military_volume"] = MilitaryVolume,
            ["music_volume"] = Volume,
            ["duck_gain"] = duckGain,
            ["duck_active"] = ReceiveMilitary && MilitaryVolume > 0f && Time.unscaledTime < duckUntil,
            ["deck_engaged"] = DeckEngaged,
            ["source_volume"] = receiver.Source != null ? receiver.Source.volume : 0f,
            ["station"] = CurrentStationId,
            ["track"] = CurrentTrackTitle,
            ["segment"] = segmentTrack != null,
            ["queued"] = breakQueue.Count,
            ["break_index"] = breakIndex,
            ["state"] = receiver.State.ToString(),
            ["duration"] = receiver.Duration,
            ["error"] = receiver.LastError ?? string.Empty,
            ["virtual"] = receiver.Source != null && receiver.Source.isVirtual,
            ["source_playing"] = receiver.Source != null && receiver.Source.isPlaying,
            ["time"] = receiver.Elapsed,
            ["rms"] = receiver.Rms,
            ["gain"] = receiver.Gain,
            ["ticker"] = TickerText,
            ["set"] = RadioBroadcastSegments.SetFor(CurrentStationId, null) ?? string.Empty,
            ["nuclear_blackout"] = RadioAftermath.Active,
            ["program"] = CurrentProgram,
            ["nuclear_static"] = aftermathStatic != null && aftermathStatic.isPlaying,
            ["nuclear_static_volume"] = aftermathStatic != null ? aftermathStatic.volume : 0f,
            ["vanilla_held"] = vanillaHeld,
        };
#endif

        private void PlayOnTune()
        {
            if (RadioAftermath.Active) { PlayStaticReceiver(); return; }
            ClearBreak();
            if (ResumeStation()) return;
            songTrack = RadioBroadcastSegments.Song(CurrentStationId, null, 0);
            songIndex = songTrack != null ? 1 : 0;
            if (songTrack != null) selectedTrack = -1;
            PlayCurrent();
        }
        private void SaveCursor()
        {
            if (!receiver.Engaged || ChannelCount == 0) return;
            cursors[CurrentStationId] = new StationCursor
            {
                Track = selectedTrack, Song = songIndex, Break = breakIndex,
                OwnSong = songTrack, Music = segmentTrack == null,
                Elapsed = receiver.Elapsed, Duration = receiver.Duration, LeftAt = Time.unscaledTime,
            };
        }

        private bool ResumeStation()
        {
            if (!cursors.TryGetValue(CurrentStationId, out StationCursor c))
            {
                songIndex = 0;
                breakIndex = 0;
                return false;
            }
            selectedTrack = c.Track;
            songIndex = c.Song;
            breakIndex = c.Break;
            float at = c.Elapsed + (Time.unscaledTime - c.LeftAt);
            if (c.Music && c.Duration > 0f && at < c.Duration - 3f)
            {
                songTrack = c.OwnSong;
                restoreTime = at;
                PlayCurrent();
            }
            else NextTrack(skipBreak: !c.Music); // left during talk: the break is over, next song
            return true;
        }

        private void ClearBreak()
        {
            breakQueue.Clear();
            segmentTrack = null;
            songTrack = null;
        }

        private static int ShuffleIndex(int count, int current)
        {
            int next = UnityEngine.Random.Range(0, count - 1);
            return next >= current ? next + 1 : next;
        }

        private void SelectChannelInternal(int index)
        {
            bool resume = receiver.Engaged;
            SaveCursor(); receiver.Stop(); selectedChannel = index; selectedTrack = 0;
            tunedDial = stations[index].Dial; ClearBreak(); nextProgramRefresh = 0f;
            status = "Selected " + CurrentChannelName;
            if (resume) PlayOnTune();
        }
        private void ProgramTick()
        {
            if (Time.unscaledTime < nextProgramRefresh) return;
            nextProgramRefresh = Time.unscaledTime + 20f;
            programText = RadioProgramming.ProgramName(CurrentStationId, DateTime.Now);
        }
        public string NextBreakText => !RadioAftermath.Active && settings.BroadcastTalk.Value ? (segmentTrack != null ? "MUSIC" : "ADVERT / NEWS") : string.Empty;
        public float SecondsToBreak => !RadioAftermath.Active && segmentTrack == null && receiver.IsPlaying ? Mathf.Max(0f, Duration - Elapsed) : -1f;
        private string CurrentStationId => CurrentChannel()?.Id ?? string.Empty;
        private void AdvanceDeck()
        {
            if (settings.RepeatTrack.Value) DeckPlay(DeckTrackIndex);
            else DeckNext();
        }

        private static void AdvanceIfDue(RadioProgram program, float crossfade, Action advance)
        {
            if (program == null || !program.IsPlaying) return;
            float remaining = program.Duration - program.Elapsed;
            if (remaining > 0f && crossfade > 0.05f && remaining <= crossfade)
            {
                advance();
                return;
            }
            if (program.Finished) advance();
        }

        private void SyncVanillaHold()
        {
            hold.SetDeckEngaged(deck != null && deck.Engaged);
            bool want = hold.Held || NuclearListening;
            if (want != vanillaHeld)
            {
                if (want) BeginVanillaHold();
                else EndVanillaHold();
                return;
            }
            if (!vanillaHeld || Time.unscaledTime < nextVanillaSweep) return;
            nextVanillaSweep = Time.unscaledTime + HoldSweepSeconds;
            EnforceVanillaSilence();
        }

        private void BeginVanillaHold()
        {
            if (vanillaHeld) return;
            vanillaHeld = true;
            interruptedVanillaClip = null;
            deferredVanillaClip = null;

            try
            {
                MusicManager music = MusicManager.i;
                AudioSource current = GameAccess.GetCurrentMusicSource(music);
                AudioSource fade = GameAccess.GetFadeMusicSource(music);
                AudioSource audible = current;
                if (fade != null && fade.isPlaying &&
                    (audible == null || !audible.isPlaying || fade.volume > audible.volume))
                    audible = fade;

                if (audible != null && audible.clip != null && audible.isPlaying)
                {
                    interruptedVanillaClip = audible.clip;
                    interruptedVanillaTime = audible.time;
                    interruptedVanillaLoop = audible.loop;
                }
                current?.Stop();
                if (fade != current) fade?.Stop();
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogDebug("Could not snapshot vanilla music: " + e.Message);
                try { MusicManager.i.StopMusic(); }
                catch { }
            }
        }

        /// <summary>
        /// The belt to the patches' braces: while the hold is up, nothing vanilla may be
        /// heard, whatever route it took to a source. Unpatched play paths, a crossfade
        /// already in flight, or a queued clip resuming are all silenced here.
        /// </summary>
        private void EnforceVanillaSilence()
        {
            try
            {
                MusicManager music = MusicManager.i;
                if (music == null) return;
                AudioSource current = GameAccess.GetCurrentMusicSource(music);
                AudioSource fade = GameAccess.GetFadeMusicSource(music);
                bool audible = (current != null && current.isPlaying) ||
                               (fade != null && fade.isPlaying);
                if (!audible) return;
                current?.Stop();
                if (fade != current) fade?.Stop();
                music.StopMusic();
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogDebug("Could not silence vanilla music: " + e.Message);
            }
        }

        private void EndVanillaHold()
        {
            if (!vanillaHeld) return;
            vanillaHeld = false;
            ReleaseVanillaHold();
        }

        private void ReleaseVanillaHold()
        {
            try
            {
                MusicManager music = MusicManager.i;
                if (music == null) return;
                if (deferredVanillaClip != null)
                {
                    if (deferredVanillaCrossfade)
                        music.CrossFadeMusic(
                            deferredVanillaClip, 0f, 1f, deferredVanillaRepeat,
                            true, true, deferredVanillaPriority);
                    else
                        music.PlayMusic(deferredVanillaClip, deferredVanillaRepeat);
                }
                else if (interruptedVanillaClip != null)
                {
                    music.PlayMusic(interruptedVanillaClip, interruptedVanillaLoop);
                    AudioSource restored = GameAccess.GetCurrentMusicSource(music);
                    if (restored != null && restored.clip == interruptedVanillaClip)
                        restored.time = Mathf.Clamp(interruptedVanillaTime, 0f,
                            Math.Max(0f, interruptedVanillaClip.length - 0.05f));
                }
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogDebug("Could not restore vanilla music: " + e.Message);
            }
            finally
            {
                interruptedVanillaClip = null;
                deferredVanillaClip = null;
            }
        }

        internal static bool AllowVanillaMusic(
            AudioClip clip, bool repeat, float priority, bool crossfade)
        {
            RadioManager radio = active;
            if (radio == null || (!radio.hold.Held && !radio.NuclearListening)) return true;
            if (clip != null)
            {
                radio.deferredVanillaClip = clip;
                radio.deferredVanillaRepeat = repeat;
                radio.deferredVanillaPriority = priority;
                radio.deferredVanillaCrossfade = crossfade;
            }
            return false;
        }
    }
}
