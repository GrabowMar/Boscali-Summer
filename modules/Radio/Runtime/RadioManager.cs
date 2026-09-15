using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Features.Radio.Configuration;
using BoscaliSummer.Features.Radio.Presentation;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>
    /// The receiver and the deck behind the two map screens. One manager owns both audio
    /// programs so the vanilla soundtrack is held by whichever of them is live, and so a
    /// measured reception figure can drive the meter, the waterfall and the squelch.
    /// </summary>
    internal sealed class RadioManager : MonoBehaviour, ISceneService
    {
        private const int TerrainLayerMask = 8256;
        private const float BulletinSeconds = 8f;
        private const float HoldSweepSeconds = 0.5f;

        private static RadioManager active;

        private RadioSettings settings;
        private ManualLogSource logger;
        private RadioLibrary localLibrary;
        private RadioStation[] stations = Array.Empty<RadioStation>();
        private RadioDial[] stationDials = Array.Empty<RadioDial>();
        private float[] stationStrengths = Array.Empty<float>();
        private readonly List<RadioSignal> spectrumSignals = new List<RadioSignal>();
        private readonly float[] spectrum = new float[RadioSpectrum.DefaultBins];
        private VanillaSoundtrackCatalog soundtrackCatalog;
        private string libraryPath;
        private RadioProgram receiver;
        private RadioProgram deck;
        private RadioBroadcastFx fx;
        private readonly RadioTransmitterAnchors anchors = new RadioTransmitterAnchors();
        private readonly RadioLinkStub link = new RadioLinkStub();
        private ServiceRegistry services;
        private ISquadView squad;
        private readonly HuntMusicGate huntMusic = new HuntMusicGate();

        // Receiver
        private string status = "Stand by";
        private string programText = string.Empty;
        private string ticker;
        private int selectedChannel;
        private int selectedTrack;
        private int stationRevision;
        private RadioDial tunedDial = RadioDial.Fm(88500);
        private readonly int[] bandPositions = { 88500, 121500, 780 };
        private bool offStation;
        private bool dialResume;
        private bool scanning;
        private readonly VanillaMusicHold hold = new VanillaMusicHold();
        private RadioReception reception = RadioReception.Perfect;
        private float nextScanTime;
        private float nextFilterCheck;
        private float nextLevelSample;
        private float nextProgramRefresh;
        private float nextBulletinAt;
        private float nextSpectrumAt;
        private float nextPropagationAt;
        private int bulletinCursor;
        private string lastProgramName;
        private BroadcastFilterMode appliedFilter;
        private RadioModulation appliedModulation;
        private bool appliedNarrow;
        private bool filterApplied;
        private string lastChatter = string.Empty;

        // Deck
        private string deckStatus = "Deck stopped";
        private int deckFolder;
        private int deckTrack;

        // Vanilla soundtrack ownership
        private bool vanillaHeld;
        private AudioClip interruptedVanillaClip;
        private float interruptedVanillaTime;
        private bool interruptedVanillaLoop;
        private AudioClip deferredVanillaClip;
        private bool deferredVanillaRepeat;
        private float deferredVanillaPriority;
        private bool deferredVanillaCrossfade;
        private float nextVanillaSweep;
        private float nextSoundtrackProbe;
        private bool configured;

        // Hunt override
        private float nextHuntPoll;
        private bool huntOverride;
        private RadioStationTrack huntTrack;
        private int savedChannel;
        private int savedTrack;
        private float savedTime;
        private bool savedPaused;
        private float restoreTime = -1f;
        private bool restorePaused;

        // ------------------------------------------------------------------ receiver face

        public int ChannelCount => stations.Length;
        public bool HasChannels => stations.Length > 0;
        public int SelectedChannel => selectedChannel;
        public int StationRevision => stationRevision;
        public string Status => status;
        public string TickerText => string.IsNullOrEmpty(ticker)
            ? "Receiver on. Tune the dial."
            : ticker;
        public string CurrentProgram => programText;
        public bool IsEngaged => receiver.Engaged;
        public bool IsPaused => receiver.IsPaused;
        public bool IsScanning => scanning;
        public bool IsOffStation => offStation;
        public RadioDial TunedDial => tunedDial;
        public RadioReception Reception => reception;
        public float SignalLevel => fx == null ? 0f : fx.Level;
        public float[] Spectrum => spectrum;
        public RadioLinkStub Link => link;

        /// <summary>
        /// True when the tuned station has a resolved tower and the player has a position, so
        /// the link budget is actually driving the meter. False means the receiver is reading
        /// a local archive (or a map with no authored tower) and everything is full-scale.
        /// </summary>
        public bool ReceptionModelled
        {
            get
            {
                if (anchors.HasListener == false) return false;
                RadioStation station = CurrentChannel();
                return station != null && huntTrack == null &&
                    anchors.TryGet(station.Id, out _, out _);
            }
        }

        public ReceiverMode Mode => settings == null ? ReceiverMode.Auto : settings.Mode.Value;

        public RadioModulation ReceiverModulation
        {
            get
            {
                ReceiverMode mode = Mode;
                if (mode == ReceiverMode.Fm) return RadioModulation.Fm;
                if (mode == ReceiverMode.Am) return RadioModulation.Am;
                return tunedDial.Modulation;
            }
        }

        public string BroadcastMode => ReceiverModulation == RadioModulation.Fm ? "FM STEREO" : "AM";

        public float Squelch => settings == null ? 0.15f : settings.Squelch.Value;
        public bool NarrowBandwidth => settings != null && settings.NarrowBandwidth.Value;
        public bool FineTuning => settings != null && settings.FineTuning.Value;
        public float VolumeLevel => Volume;

        public float Elapsed => receiver.Elapsed;
        public float Duration => receiver.Duration;
        public float Progress => receiver.Progress;

        public int TrackCount => huntTrack != null
            ? 0
            : CurrentChannel() == null ? 0 : CurrentChannel().Tracks.Length;
        public int CurrentTrackIndex => huntTrack != null || TrackCount == 0
            ? -1
            : Mathf.Clamp(selectedTrack, 0, TrackCount - 1);

        public string GetTrackTitle(int index) =>
            index >= 0 && index < TrackCount ? CurrentChannel().Tracks[index].Title : string.Empty;

        public string CurrentChannelName => huntTrack != null ? "HUNT" : ChannelCount == 0
            ? "NO CHANNEL"
            : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)].Name;
        public string CurrentChannelCode => huntTrack != null ? "HT" : ChannelCount == 0
            ? "--"
            : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)].Code;

        public string CurrentTrackTitle
        {
            get
            {
                RadioStationTrack track = CurrentTrack();
                return track == null ? "NO LOCAL TRACKS" : track.Title;
            }
        }

        public bool Shuffle => settings != null && settings.Shuffle.Value;
        public bool RepeatTrack => settings != null && settings.RepeatTrack.Value;

        public string GetChannelName(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Name : string.Empty;

        public int GetChannelTrackCount(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Tracks.Length : 0;

        public string GetChannelCode(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Code : "--";

        public string GetChannelIconPath(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].IconPath : string.Empty;

        public Color GetChannelColor(int index)
        {
            if (index < 0 || index >= ChannelCount) return new Color(0.45f, 0.95f, 0.55f);
            switch (stations[index].Id)
            {
                case "agrapol-fm": return new Color(1f, 0.68f, 0.20f);
                case "maris-network": return new Color(0.20f, 0.82f, 1f);
                case "base-broadcast": return new Color(0.48f, 0.92f, 0.48f);
                default: return new Color(0.45f, 0.95f, 0.55f);
            }
        }

        public RadioDial GetChannelDial(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Dial : RadioDial.Fm(88500);

        /// <summary>Modelled carrier strength per station, for the waterfall and the dial markers.</summary>
        public float GetChannelStrength(int index) =>
            index >= 0 && index < stationStrengths.Length ? stationStrengths[index] : 1f;

        public bool GetChannelHasTransmitter(int index) =>
            index >= 0 && index < ChannelCount && anchors.TryGet(stations[index].Id, out _, out _);

        // ---------------------------------------------------------------------- deck face

        public int DeckFolderCount => localLibrary == null ? 0 : localLibrary.Channels.Length;
        public int DeckFolder => Mathf.Clamp(deckFolder, 0, Mathf.Max(0, DeckFolderCount - 1));
        public int DeckTrackIndex => DeckTrackCount == 0 ? -1 : Mathf.Clamp(deckTrack, 0, DeckTrackCount - 1);
        public int DeckTrackCount => DeckFolderTrackCount(DeckFolder);
        public string DeckStatus => deckStatus;
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

        // ------------------------------------------------------------------- lifecycle

        internal void Configure(RadioSettings radioSettings, ManualLogSource log, ServiceRegistry registry)
        {
            settings = radioSettings ?? throw new ArgumentNullException(nameof(radioSettings));
            logger = log ?? throw new ArgumentNullException(nameof(log));
            services = registry;
            libraryPath = Path.Combine(Paths.PluginPath, "BoscaliSummer", "Music");
            receiver = new RadioProgram(this, gameObject, "BoscaliRadio", logger);
            deck = new RadioProgram(this, gameObject, "BoscaliDeck", logger);
            receiver.SetCrossfade(settings.CrossfadeSeconds.Value);
            deck.SetCrossfade(DeckCrossfadeSeconds);
            fx = new RadioBroadcastFx(gameObject);
            configured = true;
            active = this;
            if (settings.Enabled.Value && !GameManager.IsHeadless)
            {
                RadioStarterLayout.Ensure(libraryPath, logger);
                ScanLibrary();
            }
        }

        public void ResetForScene()
        {
            huntMusic.Reset();
            huntOverride = false;
            huntTrack = null;
            restoreTime = -1f;
            nextHuntPoll = 0f;
            RadioPanel.Reset();
            MusicPanel.Reset();
            scanning = false;
            nextScanTime = 0f;
            nextBulletinAt = 0f;
            nextFilterCheck = 0f;
            nextLevelSample = 0f;
            nextProgramRefresh = 0f;
            nextSpectrumAt = 0f;
            nextPropagationAt = 0f;
            nextVanillaSweep = 0f;
            filterApplied = false;
            bulletinCursor = 0;
            lastProgramName = null;
            lastChatter = string.Empty;
            ticker = null;
            programText = string.Empty;
            offStation = false;
            dialResume = false;
            hold.Reset();
            deckStatus = "Deck stopped";
            deckTrack = 0;
            link.Reset();
            bandPositions[0] = 88500;
            bandPositions[1] = 121500;
            bandPositions[2] = 780;
            tunedDial = RadioDial.Fm(88500);
            reception = RadioReception.Perfect;
            anchors.Reset();
            fx?.Silence();
            receiver?.Stop();
            deck?.Stop();
            if (!configured || !settings.Enabled.Value || GameManager.IsHeadless)
            {
                enabled = false;
                return;
            }

            enabled = true;
            if (vanillaHeld)
            {
                // A map transition invalidates the old map's soundtrack clips. Do not
                // restore one while the new scene is establishing its own music state.
                interruptedVanillaClip = null;
                deferredVanillaClip = null;
                vanillaHeld = false;
            }
            soundtrackCatalog = null;
            nextSoundtrackProbe = 0f;
            if (localLibrary == null) ScanLibrary();
            else BuildStations();
        }

        private void Update()
        {
            if (!configured || GameManager.IsHeadless) return;
            if (!settings.Enabled.Value)
            {
                hold.ReleaseReceiver();
                if (receiver.Engaged) receiver.Stop();
                if (offStation) offStation = false;
                SyncVanillaHold();
                return;
            }

            ProbeSoundtrack();
            PollHunt();
            fx?.Tick();
            ScanTick();
            BulletinTick();
            ProgramTick();
            FilterTick();
            PropagationTick();
            SpectrumTick();

            if (receiver.IsPlaying && Time.unscaledTime >= nextLevelSample)
            {
                nextLevelSample = Time.unscaledTime + 0.066f;
                fx?.Sample(receiver.Source);
            }

            fx?.SetVolume(Volume);
            fx?.SetReception(offStation ? 0f : reception.Quality,
                !offStation && reception.Open(Squelch));
            receiver.SetVolume(Volume * ReceiverGain);
            deck.SetVolume(Volume);

            SyncVanillaHold();
            RadioPanel.Tick(this);
            MusicPanel.Tick(this);

            AdvanceIfDue(receiver, settings.CrossfadeSeconds.Value, AdvanceReceiver);
            AdvanceIfDue(deck, DeckCrossfadeSeconds, AdvanceDeck);
        }

        private const float DeckCrossfadeSeconds = 0.4f;

        private void AdvanceReceiver()
        {
            if (settings.RepeatTrack.Value) PlayCurrent();
            else NextTrack();
        }

        private void AdvanceDeck()
        {
            if (settings.RepeatTrack.Value) DeckPlay(DeckTrackIndex);
            else DeckNext();
        }

        /// <summary>
        /// Roll to the next item when the current one ends — starting the blend just before the
        /// end so the two tracks meet instead of leaving a gap. Only a program in <c>Playing</c>
        /// is considered, so the load that this starts cannot trigger itself again.
        /// </summary>
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

        private void OnDestroy()
        {
            huntOverride = false;
            huntTrack = null;
            RadioPanel.Reset();
            MusicPanel.Reset();
            hold.Reset();
            StopReceiverInternal(true);
            deck?.Stop();
            deck?.Dispose();
            deck = null;
            receiver?.Dispose();
            receiver = null;
            fx?.Dispose();
            fx = null;
            if (vanillaHeld)
            {
                vanillaHeld = false;
                ReleaseVanillaHold();
            }
            if (ReferenceEquals(active, this)) active = null;
        }

        // -------------------------------------------------------------------- receiver

        public void PlayTrack(int index)
        {
            if (huntTrack != null || index < 0 || index >= TrackCount) return;
            StopScanning();
            ManualTransport();
            selectedTrack = index;
            PlayCurrent();
        }

        public void SelectChannel(int index)
        {
            if (index < 0 || index >= ChannelCount) return;
            if (index == selectedChannel && huntTrack == null && !offStation) return;
            StopScanning();
            ManualTransport();
            SetDial(stations[index].Dial);
        }

        public void StepDial(int direction)
        {
            if (direction == 0) return;
            RadioDial next = settings.FineTuning.Value
                ? RadioDialTuning.FineStep(tunedDial, direction, FineStepDivisor)
                : RadioDialTuning.Step(tunedDial, direction);
            SetDial(next);
        }

        private int FineStepDivisor => 5;

        public void SeekStation(int direction)
        {
            if (stationDials.Length == 0)
            {
                status = "No stations to seek";
                return;
            }
            int index = RadioDialTuning.Seek(stationDials, tunedDial, direction);
            if (index < 0)
            {
                status = "No stations to seek";
                return;
            }
            SetDial(stationDials[index]);
        }

        /// <summary>Band knob: FM → VHF → MW, each remembering its last frequency.</summary>
        public void CycleBand()
        {
            RememberBandPosition(tunedDial);
            RadioBand next = RadioBands.Next(tunedDial.Band);
            SetDial(RadioDial.At(next, bandPositions[(int)next]));
        }

        public void CycleMode()
        {
            if (settings == null) return;
            settings.Mode.Value = settings.Mode.Value == ReceiverMode.Auto ? ReceiverMode.Fm
                : settings.Mode.Value == ReceiverMode.Fm ? ReceiverMode.Am
                : ReceiverMode.Auto;
            filterApplied = false;
            BeginTune();
        }

        public void ToggleBandwidth()
        {
            if (settings == null) return;
            settings.NarrowBandwidth.Value = !settings.NarrowBandwidth.Value;
            filterApplied = false;
            status = settings.NarrowBandwidth.Value ? "Narrow bandwidth" : "Wide bandwidth";
        }

        public void ToggleFineTuning()
        {
            if (settings == null) return;
            settings.FineTuning.Value = !settings.FineTuning.Value;
            status = settings.FineTuning.Value ? "Fine tuning step" : "Channel step";
        }

        public void NudgeSquelch(float delta)
        {
            if (settings == null || delta == 0f) return;
            settings.Squelch.Value = Mathf.Clamp01(settings.Squelch.Value + delta);
            status = "Squelch " + Mathf.RoundToInt(settings.Squelch.Value * 100f) + "%";
        }

        public void TogglePlayback()
        {
            if (offStation)
            {
                status = "No signal to monitor";
                return;
            }
            StopScanning();
            ManualTransport(false);
            if (receiver.IsPlaying)
            {
                receiver.Pause();
                status = "Paused";
                return;
            }
            if (receiver.IsPaused)
            {
                receiver.Resume();
                status = "On air";
                return;
            }
            PlayCurrent();
        }

        public void Stop()
        {
            StopScanning();
            ManualTransport();
            offStation = false;
            dialResume = false;
            hold.ReleaseReceiver();
            fx?.SetCarrier(false, Volume);
            StopReceiverInternal(false);
            SyncVanillaHold();
        }

        public void Previous()
        {
            StopScanning();
            ManualTransport();
            RadioStation channel = CurrentChannel();
            if (channel == null || channel.Tracks.Length == 0) return;
            selectedTrack = (selectedTrack - 1 + channel.Tracks.Length) % channel.Tracks.Length;
            PlayCurrent();
        }

        public void Next()
        {
            StopScanning();
            ManualTransport();
            NextTrack();
        }

        public void ToggleScan()
        {
            if (scanning)
            {
                StopScanning();
                status = "Scan stopped";
                return;
            }
            if (ChannelCount < 2)
            {
                status = "Nothing to scan";
                return;
            }
            if (offStation)
            {
                SeekStation(1);
                if (offStation) return;
            }
            if (!receiver.Engaged) PlayCurrent();
            if (!receiver.Engaged) return;
            scanning = true;
            nextScanTime = Time.unscaledTime + settings.ScanDwellSeconds.Value;
            status = "Scanning stations";
        }

        public void ToggleShuffle()
        {
            settings.Shuffle.Value = !settings.Shuffle.Value;
            status = settings.Shuffle.Value ? "Shuffle enabled" : "Shuffle disabled";
            deckStatus = status;
        }

        public void ToggleRepeat()
        {
            settings.RepeatTrack.Value = !settings.RepeatTrack.Value;
            status = settings.RepeatTrack.Value ? "Repeat enabled" : "Repeat disabled";
            deckStatus = status;
        }

        public void Transmit()
        {
            status = RadioLinkStub.TransmitStatus;
        }

        public void ToggleSecure()
        {
            link.ToggleSecure();
            status = RadioLinkStub.SecureStatus;
        }

        public void Rescan()
        {
            StopScanning();
            ManualTransport();
            StopReceiverInternal(false);
            ScanLibrary();
        }

        public void OpenLibraryFolder()
        {
            try
            {
                RadioStarterLayout.Ensure(libraryPath, logger);
                Application.OpenURL(new Uri(libraryPath).AbsoluteUri);
                status = "Opened music folder";
                deckStatus = status;
            }
            catch (Exception e)
            {
                status = "Could not open music folder";
                logger.LogWarning("Radio station folder could not be opened: " + e.Message);
            }
        }

        public void NudgeVolume(float delta)
        {
            if (settings == null || delta == 0f) return;
            settings.Volume.Value = Mathf.Clamp01(settings.Volume.Value + delta);
            status = "Volume " + Mathf.RoundToInt(settings.Volume.Value * 100f) + "%";
        }

        // ------------------------------------------------------------------------ deck

        public void DeckSelectFolder(int index)
        {
            if (index < 0 || index >= DeckFolderCount) return;
            deckFolder = index;
            deckTrack = 0;
            deckStatus = DeckFolderName(index) + " · " + DeckFolderTrackCount(index) + " track(s)";
        }

        public void DeckPlay(int trackIndex)
        {
            RadioTrack track = DeckTrackAt(DeckFolder, trackIndex);
            if (track == null)
            {
                deckStatus = "No track on this folder";
                return;
            }
            deckTrack = trackIndex;
            if (!deck.PlayFile(track.Path, track.Extension, track.Title, 0f, false))
            {
                deckStatus = deck.LastError ?? "Could not open " + track.Title;
                return;
            }
            deckStatus = "Playing " + track.Title;
            ApplyFilters();
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
                deck.Resume();
                deckStatus = "Playing " + DeckCurrentTitle;
                return;
            }
            DeckPlay(deckTrack);
        }

        public void DeckStop()
        {
            deck.Stop();
            deckStatus = "Deck stopped";
            SyncVanillaHold();
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

        public void DeckToggleShuffle() => ToggleShuffle();

        public void DeckToggleRepeat() => ToggleRepeat();

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
                RadioStarterLayout.Ensure(libraryPath, logger);
                localLibrary = RadioLibrary.Scan(libraryPath);
                BuildStations();
                selectedChannel = Mathf.Clamp(selectedChannel, 0, Math.Max(0, ChannelCount - 1));
                selectedTrack = 0;
                deckFolder = Mathf.Clamp(deckFolder, 0, Math.Max(0, DeckFolderCount - 1));
                deckTrack = 0;
                status = localLibrary.TrackCount == 0
                    ? "Built-in stations ready; add OGG/WAV for more"
                    : localLibrary.TrackCount + " local track(s) ready";
                logger.LogInfo("Radio library: " + localLibrary.TrackCount +
                    " local track(s) across " + ChannelCount + " station(s).");
            }
            catch (Exception e)
            {
                localLibrary = null;
                stations = Array.Empty<RadioStation>();
                stationDials = Array.Empty<RadioDial>();
                stationStrengths = Array.Empty<float>();
                status = "Library scan failed";
                logger.LogWarning("Radio library scan failed: " + e.Message);
            }
        }

        private void ProbeSoundtrack()
        {
            if (soundtrackCatalog != null || Time.unscaledTime < nextSoundtrackProbe) return;
            nextSoundtrackProbe = Time.unscaledTime + 1f;
            if (!VanillaSoundtrackCatalog.TryCreate(out VanillaSoundtrackCatalog catalog)) return;
            string selectedName = CurrentChannelName;
            soundtrackCatalog = catalog;
            BuildStations();
            for (int i = 0; i < stations.Length; i++)
                if (string.Equals(stations[i].Name, selectedName, StringComparison.OrdinalIgnoreCase))
                    selectedChannel = i;
            selectedTrack = 0;
            status = "Original soundtrack linked to built-in stations";
            logger.LogInfo("Radio soundtrack adapter: " + catalog.All.Length +
                " installed vanilla clip(s) available.");
        }

        private void BuildStations()
        {
            var result = new List<RadioStation>(RadioLibrary.MaximumChannels);
            var usedFmSlots = new HashSet<int>();
            RadioChannel agrapolLocal = FindLocalChannel("Agrapol FM");
            RadioChannel marisLocal = FindLocalChannel("Maris Network");
            AddBuiltInStation(result, BuiltInStationRules.AgrapolId, "AF", "Agrapol FM",
                soundtrackCatalog?.AgrapolSeed == null ? Array.Empty<AudioClip>() :
                    new[] { soundtrackCatalog.AgrapolSeed }, agrapolLocal,
                BuiltInIcon("agrapol-fm.png"), usedFmSlots);
            AddBuiltInStation(result, BuiltInStationRules.MarisId, "MN", "Maris Network",
                soundtrackCatalog?.MarisSeed == null ? Array.Empty<AudioClip>() :
                    new[] { soundtrackCatalog.MarisSeed }, marisLocal,
                BuiltInIcon("maris-network.png"), usedFmSlots);
            AddBuiltInStation(result, BuiltInStationRules.BaseId, "BB", "Base Broadcast",
                soundtrackCatalog?.All ?? Array.Empty<AudioClip>(), null,
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
            stationDials = new RadioDial[stations.Length];
            stationStrengths = new float[stations.Length];
            for (int i = 0; i < stations.Length; i++)
            {
                stationDials[i] = stations[i].Dial;
                stationStrengths[i] = 1f;
            }
            spectrumSignals.Clear();
            for (int i = 0; i < stations.Length; i++)
                spectrumSignals.Add(new RadioSignal(stations[i].Dial.Kilohertz, 1f));
            if (!offStation)
            {
                tunedDial = ChannelCount == 0
                    ? RadioDial.Fm(88500)
                    : GetChannelDial(Mathf.Clamp(selectedChannel, 0, ChannelCount - 1));
                RememberBandPosition(tunedDial);
            }
            stationRevision++;
            nextPropagationAt = 0f;
            nextSpectrumAt = 0f;
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
            int vanillaCount = BuiltInStationRules.UsesVanillaTracks(id, localCount)
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
            string.Equals(name, "Agrapol FM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Maris Network", StringComparison.OrdinalIgnoreCase) ||
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

        // ------------------------------------------------------------------- tuning

        private void SetDial(RadioDial dial)
        {
            if (dial.Equals(tunedDial) && !offStation) return;
            StopScanning();
            ManualTransport();
            bool wasOnAir = receiver.Engaged && !offStation;
            tunedDial = dial;
            RememberBandPosition(dial);

            int index = RadioDialTuning.IndexAt(stationDials, dial);
            if (index >= 0)
            {
                bool resume = wasOnAir || dialResume || receiver.IsPaused;
                selectedChannel = index;
                selectedTrack = 0;
                offStation = false;
                dialResume = false;
                BeginTune();
                if (resume) PlayCurrent();
                return;
            }

            if (!offStation) dialResume = receiver.Engaged;
            // Dead air while tuning is not the same as the player stopping the receiver:
            // keep holding the vanilla soundtrack silent so it does not cut back in every
            // time the dial sweeps past a gap between stations. It only returns once the
            // player actually presses STOP (or the deck takes over).
            StopReceiverInternal(false);
            offStation = true;
            status = "NO SIGNAL — " + dial.FullText + " " + dial.BandText;
            fx?.SetCarrier(settings.CarrierNoise.Value, Volume);
            nextFilterCheck = 0f;
        }

        private void RememberBandPosition(RadioDial dial)
        {
            int index = Array.IndexOf(RadioBands.All, dial.Band);
            if (index >= 0) bandPositions[index] = dial.Kilohertz;
        }

        private void BeginTune()
        {
            status = "Tuned " + tunedDial.FullText + " " + BroadcastMode +
                " · " + CurrentChannelName;
            fx?.SetCarrier(false, Volume);
            fx?.Tune(CurrentChannelCode, settings.CarrierNoise.Value, settings.StationIdents.Value);
            bulletinCursor = 0;
            nextBulletinAt = Time.unscaledTime + BulletinSeconds;
            nextProgramRefresh = 0f;
            nextFilterCheck = 0f;
            nextPropagationAt = 0f;
            nextSpectrumAt = 0f;
        }

        private void PlayCurrent()
        {
            RadioStationTrack track = CurrentTrack();
            if (track == null)
            {
                status = "No playable track on this channel";
                fx?.CarrierBurst(0.4f);
                RecoverHuntLoadFailure();
                return;
            }

            bool wasPaused = restorePaused;
            bool started = track.IsLocal
                ? receiver.PlayFile(track.LocalPath, track.Extension, track.Title,
                    restoreTime, wasPaused)
                : track.VanillaClip != null && receiver.PlayClip(
                    track.VanillaClip, false, restoreTime, wasPaused);
            restoreTime = -1f;
            restorePaused = false;

            if (!started)
            {
                status = track.IsLocal
                    ? (receiver.LastError ?? "Could not open " + track.Title)
                    : "Original soundtrack is not ready";
                fx?.CarrierBurst(0.4f);
                RecoverHuntLoadFailure();
                return;
            }

            hold.EngageReceiver();
            BeginVanillaHold();
            status = "On air · " + track.Title;
            ApplyFilters();
            SyncVanillaHold();
        }

        private void StopReceiverInternal(bool destroying)
        {
            restoreTime = -1f;
            receiver?.Stop();
            status = destroying ? "Receiver stopped" : "Receiver off";
            fx?.Silence();
        }

        private float Volume => settings == null ? 1f : settings.Volume.Value;

        /// <summary>
        /// Programme gain from the link budget: below squelch the channel is muted and only
        /// the set's own hiss is heard; above it the audio comes up with the signal.
        /// </summary>
        private float ReceiverGain
        {
            get
            {
                if (offStation) return 0f;
                if (!reception.Open(Squelch)) return 0f;
                return Mathf.Lerp(0.35f, 1f, reception.Quality);
            }
        }

        private RadioStation CurrentChannel() => ChannelCount == 0
            ? null
            : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)];

        private RadioStationTrack CurrentTrack()
        {
            if (huntTrack != null) return huntTrack;
            RadioStation channel = CurrentChannel();
            if (channel == null || channel.Tracks.Length == 0) return null;
            selectedTrack = Mathf.Clamp(selectedTrack, 0, channel.Tracks.Length - 1);
            return channel.Tracks[selectedTrack];
        }

        private void NextTrack()
        {
            if (huntTrack != null)
            {
                PlayCurrent();
                return;
            }
            RadioStation channel = CurrentChannel();
            if (channel == null || channel.Tracks.Length == 0) return;
            if (settings.Shuffle.Value && channel.Tracks.Length > 1)
                selectedTrack = ShuffleIndex(channel.Tracks.Length, selectedTrack);
            else
                selectedTrack = (selectedTrack + 1) % channel.Tracks.Length;
            PlayCurrent();
        }

        private static int ShuffleIndex(int count, int current)
        {
            int next = UnityEngine.Random.Range(0, count - 1);
            return next >= current ? next + 1 : next;
        }

        private void ScanTick()
        {
            if (!scanning) return;
            if (!receiver.Engaged || ChannelCount < 2)
            {
                scanning = false;
                return;
            }
            if (Time.unscaledTime < nextScanTime) return;
            nextScanTime = Time.unscaledTime + settings.ScanDwellSeconds.Value;
            ManualTransport(false);
            SelectChannelInternal((selectedChannel + 1) % ChannelCount);
        }

        private void SelectChannelInternal(int index)
        {
            bool resume = receiver.Engaged;
            selectedChannel = index;
            selectedTrack = 0;
            tunedDial = stationDials[index];
            RememberBandPosition(tunedDial);
            offStation = false;
            BeginTune();
            if (resume) PlayCurrent();
        }

        private void StopScanning() => scanning = false;

        // ------------------------------------------------------------------ modes/fx

        private void ApplyFilters()
        {
            BroadcastFilterMode mode = settings.BroadcastFilter.Value;
            RadioModulation modulation = ReceiverModulation;
            bool narrow = NarrowBandwidth;
            appliedFilter = mode;
            appliedModulation = modulation;
            appliedNarrow = narrow;
            filterApplied = true;
            RadioBroadcastFx.ApplyCharacter(receiver.Source, mode, modulation, narrow);
            RadioBroadcastFx.ApplyCharacter(deck.Source, mode, modulation, narrow);
        }

        private void FilterTick()
        {
            if (Time.unscaledTime < nextFilterCheck) return;
            nextFilterCheck = Time.unscaledTime + 1f;
            if (filterApplied && settings.BroadcastFilter.Value == appliedFilter &&
                ReceiverModulation == appliedModulation && NarrowBandwidth == appliedNarrow)
                return;
            ApplyFilters();
        }

        private void ProgramTick()
        {
            if (!string.IsNullOrEmpty(programText) && Time.unscaledTime < nextProgramRefresh) return;
            nextProgramRefresh = Time.unscaledTime + 20f;
            DateTime now = DateTime.Now;
            string name = RadioProgramming.ProgramName(CurrentStationId, now);
            if (!string.Equals(name, lastProgramName, StringComparison.Ordinal))
            {
                lastProgramName = name;
                PushLog("PROGRAM · " + name);
            }
            programText = name;
        }

        private void BulletinTick()
        {
            if (Time.unscaledTime < nextBulletinAt) return;
            nextBulletinAt = Time.unscaledTime + BulletinSeconds;
            if (!HasChannels || offStation) return;
            PushLog(RadioProgramming.Bulletin(CurrentStationId, bulletinCursor++));
        }

        private void PushLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line.Length > 64) line = line.Substring(0, 64);
            ticker = line;
        }

        private string CurrentStationId => huntTrack != null || ChannelCount == 0
            ? string.Empty
            : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)].Id;

        // -------------------------------------------------------------- propagation

        private void PropagationTick()
        {
            if (Time.unscaledTime < nextPropagationAt) return;
            nextPropagationAt = Time.unscaledTime + 0.5f;
            anchors.Tick(Time.unscaledTime);

            bool listener = anchors.HasListener;
            Vector3 listenerPosition = anchors.ListenerPosition;
            float listenerHeight = anchors.ListenerHeight;

            for (int i = 0; i < stations.Length; i++)
            {
                stationStrengths[i] = 1f;
                if (!listener || !anchors.TryGet(stations[i].Id, out Vector3 tx, out float txHeight))
                    continue;
                float distance = Vector3.Distance(listenerPosition, tx) / 1000f;
                stationStrengths[i] = RadioPropagation.Evaluate(
                    distance, txHeight, listenerHeight, true,
                    stations[i].Dial.Modulation, ReceiverModulation).Quality;
            }

            spectrumSignals.Clear();
            for (int i = 0; i < stations.Length; i++)
                spectrumSignals.Add(new RadioSignal(stations[i].Dial.Kilohertz,
                    Mathf.Max(0.08f, stationStrengths[i])));

            if (offStation)
            {
                reception = new RadioReception(0f, 0f, 0f, 0f, RadioPropagation.NoiseFloorDbm);
                return;
            }

            RadioStation station = CurrentChannel();
            if (station == null || huntTrack != null ||
                !anchors.TryGet(station.Id, out Vector3 tower, out float towerHeight) || !listener)
            {
                reception = RadioReception.Perfect;
                return;
            }

            Vector3 towerTop = tower + Vector3.up * towerHeight;
            float km = Vector3.Distance(listenerPosition, towerTop) / 1000f;
            bool lineOfSight = HasLineOfSight(listenerPosition + Vector3.up * 3f, towerTop);
            reception = RadioPropagation.Evaluate(km, towerHeight, listenerHeight, lineOfSight,
                station.Dial.Modulation, ReceiverModulation);
        }

        /// <summary>
        /// One terrain probe per evaluation — three stations' worth at worst, twice a second.
        /// The mask is the game's own ground-collision set, so a hill between aircraft and
        /// transmitter costs the station signal exactly as it would in the air.
        /// </summary>
        private bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            try
            {
                return !Physics.Linecast(from, to, TerrainLayerMask, QueryTriggerInteraction.Ignore);
            }
            catch
            {
                return true;
            }
        }

        private void SpectrumTick()
        {
            if (Time.unscaledTime < nextSpectrumAt) return;
            nextSpectrumAt = Time.unscaledTime + 0.12f;
            RadioSpectrum.Fill(spectrum, spectrumSignals,
                RadioBands.Min(tunedDial.Band), RadioBands.Max(tunedDial.Band),
                0.07f, Time.unscaledTime);
        }

        // ------------------------------------------------------------------- hunts

        private void PollHunt()
        {
            if (Time.unscaledTime < nextHuntPoll) return;
            nextHuntPoll = Time.unscaledTime + 0.5f;
            if (squad == null) services?.TryGet(out squad);
            bool hunting = squad != null && squad.HuntActive;
            string chatter = squad?.LastChatter;
            if (!string.IsNullOrEmpty(chatter) &&
                !string.Equals(chatter, lastChatter, StringComparison.Ordinal))
            {
                lastChatter = chatter;
                PushLog("INTERCEPT · " + chatter);
            }
            // Replacing a debug wing keeps the original pre-hunt music restore point.
            if (huntMusic.Begin(hunting, squad?.ActiveHuntId ?? 0) && !huntOverride) BeginHuntMusic();
            if (!hunting && huntOverride) EndHuntMusic();
        }

        private void BeginHuntMusic()
        {
            int channel = -1;
            for (int i = 0; i < stations.Length; i++)
                if (string.Equals(stations[i].Name, "Hunt", StringComparison.OrdinalIgnoreCase) &&
                    stations[i].Tracks.Length > 0)
                {
                    channel = i;
                    break;
                }

            AudioClip tactical = null;
            if (channel < 0)
            {
                try
                {
                    LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                    if (level != null && level.LoadedMapSettings != null &&
                        GameManager.GetLocalPlayer<Player>(out Player player) && player?.HQ?.faction != null)
                        tactical = level.LoadedMapSettings.GetTacticalMusic(player.HQ.faction);
                }
                catch { }
                if (tactical == null) return;
            }

            savedChannel = selectedChannel;
            savedTrack = selectedTrack;
            savedTime = receiver.Engaged ? Elapsed : 0f;
            savedPaused = receiver.IsPaused;
            restoreTime = -1f;
            huntOverride = true;
            if (channel >= 0)
            {
                selectedChannel = channel;
                selectedTrack = 0;
            }
            else
            {
                huntTrack = RadioStationTrack.Vanilla(tactical);
            }
            tunedDial = ChannelCount == 0
                ? RadioDial.Fm(88500)
                : GetChannelDial(Mathf.Clamp(selectedChannel, 0, ChannelCount - 1));
            offStation = false;
            PlayCurrent();
        }

        private void EndHuntMusic()
        {
            huntOverride = false;
            huntTrack = null;
            selectedChannel = Mathf.Clamp(savedChannel, 0, Math.Max(0, ChannelCount - 1));
            selectedTrack = savedTrack;
            tunedDial = ChannelCount == 0
                ? RadioDial.Fm(88500)
                : GetChannelDial(Mathf.Clamp(selectedChannel, 0, ChannelCount - 1));
            offStation = false;
            if (!receiver.Engaged || CurrentTrack() == null)
            {
                StopReceiverInternal(false);
                return;
            }
            restoreTime = savedTime;
            restorePaused = savedPaused;
            PlayCurrent();
        }

        private void ManualTransport(bool clearTrack = true)
        {
            huntMusic.Suppress(squad?.ActiveHuntId ?? 0);
            huntOverride = false;
            if (clearTrack) huntTrack = null;
            restoreTime = -1f;
            restorePaused = false;
        }

        private void RecoverHuntLoadFailure()
        {
            if (restoreTime >= 0f) StopReceiverInternal(false);
            else if (huntOverride) EndHuntMusic();
        }

        // ------------------------------------------------------- vanilla soundtrack

        /// <summary>
        /// Vanilla music is held silent while either program is live — including dead air
        /// while tuning, which is still the player listening to the receiver. It only comes
        /// back when the receiver is stopped and the deck is not playing.
        /// </summary>
        private void SyncVanillaHold()
        {
            hold.SetDeckEngaged(deck != null && deck.Engaged);
            bool want = hold.Held;
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
                logger.LogDebug("Could not snapshot vanilla music: " + e.Message);
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
                logger?.LogDebug("Could not silence vanilla music: " + e.Message);
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
                logger?.LogDebug("Could not restore vanilla music: " + e.Message);
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
            if (radio == null || !radio.hold.Held) return true;
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
