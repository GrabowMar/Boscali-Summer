using System;
using System.Collections;
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
using UnityEngine.Networking;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal sealed class RadioManager : MonoBehaviour, ISceneService
    {
        private enum PlaybackState
        {
            Stopped,
            Loading,
            Playing,
            Paused
        }

        private static RadioManager active;

        private RadioSettings settings;
        private ManualLogSource logger;
        private RadioLibrary localLibrary;
        private RadioStation[] stations = Array.Empty<RadioStation>();
        private VanillaSoundtrackCatalog soundtrackCatalog;
        private string libraryPath;
        private AudioSource currentSource;
        private AudioSource incomingSource;
        private AudioClip currentClip;
        private AudioClip incomingClip;
        private bool currentClipOwned;
        private bool incomingClipOwned;
        private UnityWebRequest pendingRequest;
        private Coroutine pendingCoroutine;
        private PlaybackState state;
        private int selectedChannel;
        private int selectedTrack;
        private int stationRevision;
        private int loadGeneration;
        private bool configured;
        private float nextSoundtrackProbe;
        private bool ownsVanillaMusic;
        private AudioClip interruptedVanillaClip;
        private float interruptedVanillaTime;
        private bool interruptedVanillaLoop;
        private AudioClip deferredVanillaClip;
        private bool deferredVanillaRepeat;
        private float deferredVanillaPriority;
        private bool deferredVanillaCrossfade;
        private string status = "Stand by";
        private ServiceRegistry services;
        private ISquadView squad;
        private readonly HuntMusicGate huntMusic = new HuntMusicGate();
        private float nextHuntPoll;
        private bool huntOverride;
        private RadioStationTrack huntTrack;
        private int savedChannel;
        private int savedTrack;
        private float savedTime;
        private PlaybackState savedState;
        private float restoreTime = -1f;
        private bool restorePaused;
        private RadioBroadcastFx fx;
        private RadioDial tunedDial = RadioDial.Fm(88500);
        private RadioDial[] stationDials = Array.Empty<RadioDial>();
        private bool offStation;
        private bool dialResume;
        private int lastFmKilohertz = 88500;
        private int lastMwKilohertz = 780;
        private string lastChatter = string.Empty;
        private bool scanning;
        private float nextScanTime;
        private float nextFilterCheck;
        private float nextLevelSample;
        private BroadcastFilterMode appliedFilter;
        private bool appliedAmBand;
        private bool filterApplied;
        private int bulletinCursor;
        private float nextBulletinAt;
        private string lastProgramName;
        private const float BulletinSeconds = 8f;
        private string ticker;
        private string programText = string.Empty;
        private float nextProgramRefresh;

        public int ChannelCount => stations.Length;
        public bool HasChannels => stations.Length > 0;
        public int SelectedChannel => selectedChannel;
        public int StationRevision => stationRevision;
        public string Status => status;
        public bool IsEngaged => state != PlaybackState.Stopped;
        public bool IsPaused => state == PlaybackState.Paused;
        public bool IsScanning => scanning;
        public bool IsOffStation => offStation;
        public RadioDial TunedDial => tunedDial;
        public string BroadcastMode => tunedDial.IsFm ? "STEREO" : "MONO";
        public string CurrentProgram => programText;
        public string TickerText => string.IsNullOrEmpty(ticker)
            ? "Receiver on. Tune the dial."
            : ticker;

        public int TrackCount => huntTrack != null
            ? 0
            : CurrentChannel() == null ? 0 : CurrentChannel().Tracks.Length;
        public int CurrentTrackIndex => huntTrack != null || TrackCount == 0
            ? -1
            : Mathf.Clamp(selectedTrack, 0, TrackCount - 1);
        public string GetTrackTitle(int index) =>
            index >= 0 && index < TrackCount ? CurrentChannel().Tracks[index].Title : string.Empty;

        public void PlayTrack(int index)
        {
            if (huntTrack != null || index < 0 || index >= TrackCount) return;
            StopScanning();
            ManualTransport();
            selectedTrack = index;
            PlayCurrent();
        }

        public float SignalLevel => fx == null ? 0f : fx.Level;
        public bool Shuffle => settings != null && settings.Shuffle.Value;
        public bool RepeatTrack => settings != null && settings.RepeatTrack.Value;
        public float Elapsed => currentSource != null && currentSource.clip != null ? currentSource.time : 0f;
        public float Duration => currentSource != null && currentSource.clip != null ? currentSource.clip.length : 0f;
        public float Progress => Duration > 0.01f ? Mathf.Clamp01(Elapsed / Duration) : 0f;

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

        internal void Configure(RadioSettings radioSettings, ManualLogSource log, ServiceRegistry registry)
        {
            settings = radioSettings ?? throw new ArgumentNullException(nameof(radioSettings));
            logger = log ?? throw new ArgumentNullException(nameof(log));
            services = registry;
            libraryPath = System.IO.Path.Combine(Paths.PluginPath, "BoscaliSummer", "Music");
            configured = true;
            fx = new RadioBroadcastFx(gameObject);
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
            scanning = false;
            nextScanTime = 0f;
            nextBulletinAt = 0f;
            nextFilterCheck = 0f;
            nextLevelSample = 0f;
            nextProgramRefresh = 0f;
            filterApplied = false;
            bulletinCursor = 0;
            lastProgramName = null;
            lastChatter = string.Empty;
            ticker = null;
            offStation = false;
            dialResume = false;
            lastFmKilohertz = 88500;
            lastMwKilohertz = 780;
            tunedDial = RadioDial.Fm(88500);
            fx?.Silence();
            if (!configured || !settings.Enabled.Value || GameManager.IsHeadless)
            {
                enabled = false;
                return;
            }

            enabled = true;
            if (IsEngaged)
            {
                // A map transition invalidates the old map's soundtrack clips. Do not
                // restore one while the new scene is establishing its own music state.
                interruptedVanillaClip = null;
                deferredVanillaClip = null;
                StopInternal(false);
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
                if (IsEngaged || offStation) Stop();
                return;
            }
            ProbeSoundtrack();
            PollHunt();
            fx?.Tick();
            ScanTick();
            BulletinTick();
            ProgramTick();
            FilterTick();

            if (state == PlaybackState.Playing && fx != null &&
                Time.unscaledTime >= nextLevelSample)
            {
                nextLevelSample = Time.unscaledTime + 0.066f;
                fx.Sample(currentSource);
            }

            float volume = Volume;
            fx?.SetVolume(volume);
            if (currentSource != null && pendingCoroutine == null &&
                !Mathf.Approximately(currentSource.volume, volume))
            {
                currentSource.volume = volume;
            }

            RadioPanel.Tick(this);

            if (state == PlaybackState.Playing && pendingCoroutine == null &&
                currentClip != null && currentSource != null)
            {
                float remaining = currentClip.length - currentSource.time;
                if (!settings.RepeatTrack.Value && currentSource.isPlaying &&
                    remaining <= Mathf.Max(0.1f, settings.CrossfadeSeconds.Value))
                {
                    NextTrack();
                    return;
                }
                if (currentSource.isPlaying) return;
                if (settings.RepeatTrack.Value) PlayCurrent();
                else NextTrack();
            }
        }

        private void OnDestroy()
        {
            huntOverride = false;
            huntTrack = null;
            RadioPanel.Reset();
            StopInternal(true);
            fx?.Dispose();
            fx = null;
            if (ReferenceEquals(active, this)) active = null;
        }

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

        public void SelectChannel(int index)
        {
            if (index < 0 || index >= ChannelCount) return;
            if (index == selectedChannel && huntTrack == null && !offStation) return;
            StopScanning();
            ManualTransport();
            tunedDial = stations[index].Dial;
            RememberBandPosition(tunedDial);
            offStation = false;
            dialResume = false;
            SelectChannelInternal(index);
        }

        private void SelectChannelInternal(int index)
        {
            bool resume = IsEngaged;
            selectedChannel = index;
            selectedTrack = 0;
            BeginTune();
            if (resume) PlayCurrent();
        }

        private void BeginTune()
        {
            status = "Tuned to " + TunedDial.FullText + " " + BroadcastMode + " · " + CurrentChannelName;
            fx?.SetCarrier(false, Volume);
            fx?.Tune(CurrentChannelCode, settings.CarrierNoise.Value, settings.StationIdents.Value);
            PushLog("TUNED " + TunedDial.FullText + " · " + CurrentChannelName);
            bulletinCursor = 0;
            nextBulletinAt = Time.unscaledTime + BulletinSeconds;
            nextProgramRefresh = 0f;
            nextFilterCheck = 0f;
        }

        public void StepDial(int direction)
        {
            if (direction == 0) return;
            SetDial(RadioDialTuning.Step(tunedDial, direction));
        }

        public void SeekStation(int direction)
        {
            if (stationDials.Length == 0)
            {
                status = "No stations to seek";
                return;
            }
            int index = RadioDialTuning.Seek(stationDials, tunedDial, direction);
            if (index >= 0 && stationDials[index].Equals(tunedDial))
            {
                int other = RadioDialTuning.FirstInBand(
                    stationDials, tunedDial.IsFm ? RadioBand.Mw : RadioBand.Fm);
                if (other >= 0) index = other;
            }
            if (index < 0)
            {
                status = "No stations to seek";
                return;
            }
            SetDial(stationDials[index]);
        }

        public void ToggleBand()
        {
            bool toMw = tunedDial.IsFm;
            RememberBandPosition(tunedDial);
            SetDial(toMw
                ? RadioDial.Mw(lastMwKilohertz)
                : RadioDial.Fm(lastFmKilohertz));
        }

        private float Volume => settings == null ? 1f : settings.Volume.Value;

        public float VolumeLevel => Volume;

        public void NudgeVolume(float delta)
        {
            if (settings == null || delta == 0f) return;
            settings.Volume.Value = Mathf.Clamp01(settings.Volume.Value + delta);
            status = "Volume " + Mathf.RoundToInt(settings.Volume.Value * 100f) + "%";
        }

        private void RememberBandPosition(RadioDial dial)
        {
            if (dial.IsFm) lastFmKilohertz = dial.Kilohertz;
            else lastMwKilohertz = dial.Kilohertz;
        }

        private void SetDial(RadioDial dial)
        {
            if (dial.Equals(tunedDial) && !offStation) return;
            StopScanning();
            ManualTransport();
            bool wasOnAir = IsEngaged && !offStation;
            tunedDial = dial;
            RememberBandPosition(dial);

            int index = RadioDialTuning.IndexAt(stationDials, dial);
            if (index >= 0)
            {
                bool resume = wasOnAir || dialResume;
                selectedChannel = index;
                selectedTrack = 0;
                offStation = false;
                dialResume = false;
                BeginTune();
                if (resume) PlayCurrent();
                return;
            }

            if (!offStation) dialResume = IsEngaged;
            if (IsEngaged) StopInternal(false);
            offStation = true;
            status = "NO SIGNAL — " + dial.FullText + " " + dial.BandText;
            fx?.SetCarrier(settings.CarrierNoise.Value, Volume);
            PushLog("NO SIGNAL · " + dial.FullText);
            nextFilterCheck = 0f;
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
            if (!IsEngaged) PlayCurrent();
            if (!IsEngaged) return;
            scanning = true;
            nextScanTime = Time.unscaledTime + settings.ScanDwellSeconds.Value;
            status = "Scanning stations";
            PushLog("SCAN · seeking stations");
        }

        private void StopScanning() => scanning = false;

        public int GetPreset(int slot) =>
            settings == null || slot < 0 || slot >= RadioSettings.PresetSlots
                ? RadioSettings.NoPreset
                : settings.Presets[slot].Value;

        public void ApplyPreset(int slot)
        {
            int index = GetPreset(slot);
            if (index < 0 || index >= ChannelCount)
            {
                status = "Preset " + (slot + 1) + " is empty";
                return;
            }
            SelectChannel(index);
        }

        public void StorePreset(int slot)
        {
            if (settings == null || slot < 0 || slot >= RadioSettings.PresetSlots) return;
            settings.Presets[slot].Value = selectedChannel;
            status = "Stored " + CurrentChannelName + " on preset " + (slot + 1);
            PushLog("PRESET " + (slot + 1) + " · " + TunedDial.FullText + " " + CurrentChannelName);
        }

        public void TogglePlayback()
        {
            if (offStation) return;
            StopScanning();
            ManualTransport(false);
            if (state == PlaybackState.Loading)
            {
                Stop();
                return;
            }
            if (state == PlaybackState.Playing)
            {
                if (currentSource != null) currentSource.Pause();
                state = PlaybackState.Paused;
                status = "Paused";
                return;
            }
            if (state == PlaybackState.Paused && currentSource != null && currentClip != null)
            {
                currentSource.UnPause();
                state = PlaybackState.Playing;
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
            fx?.SetCarrier(false, Volume);
            StopInternal(false);
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

        private void NextTrack()
        {
            if (huntTrack != null) { PlayCurrent(); return; }
            RadioStation channel = CurrentChannel();
            if (channel == null || channel.Tracks.Length == 0) return;
            if (settings.Shuffle.Value && channel.Tracks.Length > 1)
            {
                int next = UnityEngine.Random.Range(0, channel.Tracks.Length - 1);
                if (next >= selectedTrack) next++;
                selectedTrack = next;
            }
            else
            {
                selectedTrack = (selectedTrack + 1) % channel.Tracks.Length;
            }
            PlayCurrent();
        }

        public void ToggleShuffle()
        {
            settings.Shuffle.Value = !settings.Shuffle.Value;
            status = settings.Shuffle.Value ? "Shuffle enabled" : "Shuffle disabled";
        }

        public void ToggleRepeat()
        {
            settings.RepeatTrack.Value = !settings.RepeatTrack.Value;
            status = settings.RepeatTrack.Value ? "Repeat enabled" : "Repeat disabled";
        }

        public void Rescan()
        {
            StopScanning();
            ManualTransport();
            StopInternal(false);
            ScanLibrary();
        }

        public void OpenLibraryFolder()
        {
            try
            {
                RadioStarterLayout.Ensure(libraryPath, logger);
                Application.OpenURL(new Uri(libraryPath).AbsoluteUri);
                status = "Opened station folder";
            }
            catch (Exception e)
            {
                status = "Could not open station folder";
                logger.LogWarning("Radio station folder could not be opened: " + e.Message);
            }
        }

        private void ScanLibrary()
        {
            try
            {
                RadioStarterLayout.Ensure(libraryPath, logger);
                localLibrary = RadioLibrary.Scan(libraryPath);
                BuildStations();
                selectedChannel = Mathf.Clamp(selectedChannel, 0, Math.Max(0, ChannelCount - 1));
                selectedTrack = 0;
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
            for (int i = 0; i < stations.Length; i++) stationDials[i] = stations[i].Dial;
            if (!offStation)
                tunedDial = ChannelCount == 0
                    ? RadioDial.Fm(88500)
                    : GetChannelDial(Mathf.Clamp(selectedChannel, 0, ChannelCount - 1));
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

            if (!PrepareAudioSources())
            {
                status = "Music mixer is not ready";
                RecoverHuntLoadFailure();
                return;
            }

            BeginVanillaOwnership();
            CancelPendingLoad();
            state = PlaybackState.Loading;
            status = "Loading " + track.Title;
            int generation = ++loadGeneration;
            if (!track.IsLocal)
            {
                if (track.VanillaClip == null)
                {
                    state = currentClip != null ? PlaybackState.Playing : PlaybackState.Stopped;
                    status = "Original soundtrack is not ready";
                    if (currentClip == null) ReleaseVanillaOwnership();
                    RecoverHuntLoadFailure();
                    return;
                }
                StartIncomingClip(track.VanillaClip, false, generation);
                return;
            }

            AudioType audioType = string.Equals(track.Extension, ".wav", StringComparison.OrdinalIgnoreCase)
                ? AudioType.WAV
                : AudioType.OGGVORBIS;

            try
            {
                pendingRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(track.LocalPath), audioType);
                var handler = pendingRequest.downloadHandler as DownloadHandlerAudioClip;
                if (handler != null) handler.streamAudio = false;
                pendingCoroutine = StartCoroutine(LoadTrack(track, generation, pendingRequest));
            }
            catch (Exception e)
            {
                pendingRequest?.Dispose();
                pendingRequest = null;
                pendingCoroutine = null;
                state = currentClip != null ? PlaybackState.Playing : PlaybackState.Stopped;
                status = "Could not open " + track.Title;
                logger.LogWarning("Radio track open failed: " + e.Message);
                if (currentClip == null) ReleaseVanillaOwnership();
                RecoverHuntLoadFailure();
            }
        }

        private void ScanTick()
        {
            if (!scanning) return;
            if (!IsEngaged || ChannelCount < 2)
            {
                scanning = false;
                return;
            }
            if (Time.unscaledTime < nextScanTime) return;
            nextScanTime = Time.unscaledTime + settings.ScanDwellSeconds.Value;
            ManualTransport();
            SelectChannelInternal((selectedChannel + 1) % ChannelCount);
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

        private void FilterTick()
        {
            if (Time.unscaledTime < nextFilterCheck) return;
            nextFilterCheck = Time.unscaledTime + 2f;
            BroadcastFilterMode mode = settings.BroadcastFilter.Value;
            bool amBand = tunedDial.Band == RadioBand.Mw;
            if (filterApplied && mode == appliedFilter && amBand == appliedAmBand) return;
            appliedFilter = mode;
            appliedAmBand = amBand;
            filterApplied = true;
            RadioBroadcastFx.ApplyCharacter(currentSource, mode, amBand);
            RadioBroadcastFx.ApplyCharacter(incomingSource, mode, amBand);
        }

        private void BulletinTick()
        {
            if (Time.unscaledTime < nextBulletinAt) return;
            nextBulletinAt = Time.unscaledTime + BulletinSeconds;
            if (!HasChannels) return;
            PushLog(RadioProgramming.Bulletin(CurrentStationId, bulletinCursor++));
        }

        private void PushLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line.Length > 64) line = line.Substring(0, 64);
            ticker = line;
        }

        private IEnumerator LoadTrack(RadioStationTrack track, int generation, UnityWebRequest request)
        {
            yield return request.SendWebRequest();

            if (generation != loadGeneration || request != pendingRequest) yield break;
            pendingRequest = null;
            pendingCoroutine = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error;
                request.Dispose();
                state = currentClip != null ? PlaybackState.Playing : PlaybackState.Stopped;
                status = "Skipped unreadable track";
                logger.LogWarning("Radio could not decode a local track: " + error);
                if (currentClip == null) ReleaseVanillaOwnership();
                RecoverHuntLoadFailure();
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            request.Dispose();
            if (clip == null)
            {
                state = currentClip != null ? PlaybackState.Playing : PlaybackState.Stopped;
                status = "Skipped empty track";
                if (currentClip == null) ReleaseVanillaOwnership();
                RecoverHuntLoadFailure();
                yield break;
            }

            clip.name = track.Title;
            StartIncomingClip(clip, true, generation);
        }

        private void StartIncomingClip(AudioClip clip, bool owned, int generation)
        {
            incomingClip = clip;
            incomingClipOwned = owned;
            incomingSource.clip = clip;
            bool paused = restoreTime >= 0f && restorePaused;
            incomingSource.time = restoreTime < 0f ? 0f :
                Mathf.Clamp(restoreTime, 0f, Math.Max(0f, clip.length - 0.05f));
            restoreTime = -1f;
            incomingSource.loop = false;
            incomingSource.volume = 0f;
            incomingSource.Play();
            if (paused) incomingSource.Pause();
            pendingCoroutine = StartCoroutine(CrossFadeToIncoming(generation, paused));
        }

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
                    stations[i].Tracks.Length > 0) { channel = i; break; }

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
            if (!PrepareAudioSources()) return;

            savedChannel = selectedChannel;
            savedTrack = selectedTrack;
            savedState = state;
            savedTime = state == PlaybackState.Loading ? 0f : Elapsed;
            restoreTime = -1f;
            huntOverride = true;
            if (channel >= 0) { selectedChannel = channel; selectedTrack = 0; }
            else huntTrack = RadioStationTrack.Vanilla(tactical);
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
            if (savedState == PlaybackState.Stopped || CurrentTrack() == null)
            {
                StopInternal(false);
                return;
            }
            restoreTime = savedTime;
            restorePaused = savedState == PlaybackState.Paused;
            PlayCurrent();
        }

        private void ManualTransport(bool clearTrack = true)
        {
            huntMusic.Suppress(squad?.ActiveHuntId ?? 0);
            huntOverride = false;
            if (clearTrack) huntTrack = null;
            restoreTime = -1f;
        }

        private void RecoverHuntLoadFailure()
        {
            if (restoreTime >= 0f) StopInternal(false);
            else if (huntOverride) EndHuntMusic();
        }

        private IEnumerator CrossFadeToIncoming(int generation, bool paused)
        {
            float duration = settings.CrossfadeSeconds.Value;
            float elapsed = 0f;
            float target = Volume;
            while (generation == loadGeneration && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float amount = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                incomingSource.volume = target * amount;
                if (currentSource != null) currentSource.volume = target * (1f - amount);
                yield return null;
            }

            if (generation != loadGeneration) yield break;
            if (currentSource != null) currentSource.Stop();
            DestroyClip(currentClip, currentClipOwned);

            AudioSource oldSource = currentSource;
            currentSource = incomingSource;
            incomingSource = oldSource;
            currentClip = incomingClip;
            currentClipOwned = incomingClipOwned;
            incomingClip = null;
            incomingClipOwned = false;
            if (incomingSource != null)
            {
                incomingSource.clip = null;
                incomingSource.volume = 0f;
            }
            currentSource.volume = 1f;
            pendingCoroutine = null;
            state = paused ? PlaybackState.Paused : PlaybackState.Playing;
            status = paused ? "Paused" : "On air";
        }

        private bool PrepareAudioSources()
        {
            if (currentSource == null) currentSource = CreateSource("BoscaliRadio.Current");
            if (incomingSource == null) incomingSource = CreateSource("BoscaliRadio.Incoming");
            try
            {
                currentSource.outputAudioMixerGroup = SoundManager.i.MusicMixer;
                incomingSource.outputAudioMixerGroup = SoundManager.i.MusicMixer;
                bool ready = currentSource.outputAudioMixerGroup != null;
                if (ready)
                {
                    fx?.SetMixer(currentSource.outputAudioMixerGroup);
                    BroadcastFilterMode mode = settings.BroadcastFilter.Value;
                    bool amBand = tunedDial.Band == RadioBand.Mw;
                    appliedFilter = mode;
                    appliedAmBand = amBand;
                    filterApplied = true;
                    RadioBroadcastFx.ApplyCharacter(currentSource, mode, amBand);
                    RadioBroadcastFx.ApplyCharacter(incomingSource, mode, amBand);
                    currentSource.volume = Volume;
                }
                return ready;
            }
            catch (Exception e)
            {
                logger.LogDebug("Radio music mixer not ready: " + e.Message);
                return false;
            }
        }

        private AudioSource CreateSource(string sourceName)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.name = sourceName;
            source.playOnAwake = false;
            source.loop = false;
            source.ignoreListenerPause = true;
            source.spatialBlend = 0f;
            source.volume = 1f;
            return source;
        }

        private void StopInternal(bool destroying)
        {
            restoreTime = -1f;
            CancelPendingLoad();
            if (currentSource != null)
            {
                currentSource.Stop();
                currentSource.clip = null;
            }
            if (incomingSource != null)
            {
                incomingSource.Stop();
                incomingSource.clip = null;
            }
            DestroyClip(currentClip, currentClipOwned);
            DestroyClip(incomingClip, incomingClipOwned);
            currentClip = null;
            incomingClip = null;
            currentClipOwned = false;
            incomingClipOwned = false;
            state = PlaybackState.Stopped;
            status = destroying ? "Receiver stopped" : "Receiver off";
            fx?.Silence();
            ReleaseVanillaOwnership();
        }

        private void CancelPendingLoad()
        {
            loadGeneration++;
            if (pendingRequest != null)
            {
                pendingRequest.Abort();
                pendingRequest.Dispose();
                pendingRequest = null;
            }
            if (pendingCoroutine != null)
            {
                StopCoroutine(pendingCoroutine);
                pendingCoroutine = null;
            }
            if (incomingSource != null)
            {
                incomingSource.Stop();
                incomingSource.clip = null;
            }
            DestroyClip(incomingClip, incomingClipOwned);
            incomingClip = null;
            incomingClipOwned = false;
        }

        private static void DestroyClip(AudioClip clip, bool owned)
        {
            if (owned && clip != null) Destroy(clip);
        }

        private RadioStation CurrentChannel() => ChannelCount == 0
            ? null
            : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)];

        private string CurrentStationId => ChannelCount == 0
            ? string.Empty
            : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)].Id;

        private RadioStationTrack CurrentTrack()
        {
            if (huntTrack != null) return huntTrack;
            RadioStation channel = CurrentChannel();
            if (channel == null || channel.Tracks.Length == 0) return null;
            selectedTrack = Mathf.Clamp(selectedTrack, 0, channel.Tracks.Length - 1);
            return channel.Tracks[selectedTrack];
        }

        private void BeginVanillaOwnership()
        {
            if (ownsVanillaMusic) return;
            ownsVanillaMusic = true;
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

        private void ReleaseVanillaOwnership()
        {
            if (!ownsVanillaMusic) return;
            ownsVanillaMusic = false;

            try
            {
                MusicManager music = MusicManager.i;
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
            if (radio == null || !radio.ownsVanillaMusic) return true;
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
