using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Radio.Configuration;
using BoscaliSummer.Features.Radio.Presentation;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
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

        public int ChannelCount => stations.Length;
        public int SelectedChannel => selectedChannel;
        public int StationRevision => stationRevision;
        public string LibraryPath => libraryPath ?? string.Empty;
        public string Status => status;
        public bool IsEngaged => state != PlaybackState.Stopped;
        public bool IsPaused => state == PlaybackState.Paused;
        public bool Shuffle => settings != null && settings.Shuffle.Value;
        public bool RepeatTrack => settings != null && settings.RepeatTrack.Value;
        public float Volume => settings?.Volume.Value ?? 0f;
        public float Elapsed => currentSource != null && currentSource.clip != null ? currentSource.time : 0f;
        public float Duration => currentSource != null && currentSource.clip != null ? currentSource.clip.length : 0f;
        public float Progress => Duration > 0.01f ? Mathf.Clamp01(Elapsed / Duration) : 0f;

        public string CurrentChannelName => ChannelCount == 0
            ? "NO CHANNEL"
            : stations[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)].Name;
        public string CurrentChannelCode => ChannelCount == 0
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

        internal void Configure(RadioSettings radioSettings, ManualLogSource log)
        {
            settings = radioSettings ?? throw new ArgumentNullException(nameof(radioSettings));
            logger = log ?? throw new ArgumentNullException(nameof(log));
            libraryPath = System.IO.Path.Combine(Paths.PluginPath, "BoscaliSummer", "Music");
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
            RadioPanel.Reset();
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
            if (!configured || !settings.Enabled.Value || GameManager.IsHeadless) return;
            ProbeSoundtrack();
            RadioPanel.Tick(this);

            if (state == PlaybackState.Playing && pendingCoroutine == null &&
                currentClip != null && currentSource != null)
            {
                float remaining = currentClip.length - currentSource.time;
                if (!settings.RepeatTrack.Value && currentSource.isPlaying &&
                    remaining <= Mathf.Max(0.1f, settings.CrossfadeSeconds.Value))
                {
                    Next();
                    return;
                }
                if (currentSource.isPlaying) return;
                if (settings.RepeatTrack.Value) PlayCurrent();
                else Next();
            }
        }

        private void OnDestroy()
        {
            RadioPanel.Reset();
            StopInternal(true);
            if (ReferenceEquals(active, this)) active = null;
        }

        public string GetChannelName(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Name : string.Empty;

        public int GetChannelTrackCount(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Tracks.Length : 0;

        public string GetChannelCode(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Code : "--";

        public string GetChannelSlogan(int index) =>
            index >= 0 && index < ChannelCount ? stations[index].Slogan : string.Empty;

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

        public void SelectChannel(int index)
        {
            if (index < 0 || index >= ChannelCount || index == selectedChannel) return;
            bool resume = IsEngaged;
            selectedChannel = index;
            selectedTrack = 0;
            status = "Tuned to " + CurrentChannelName;
            if (resume) PlayCurrent();
        }

        public void TogglePlayback()
        {
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

        public void Stop() => StopInternal(false);

        public void Previous()
        {
            RadioStation channel = CurrentChannel();
            if (channel == null || channel.Tracks.Length == 0) return;
            selectedTrack = (selectedTrack - 1 + channel.Tracks.Length) % channel.Tracks.Length;
            PlayCurrent();
        }

        public void Next()
        {
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

        public void ChangeVolume(float delta)
        {
            settings.Volume.Value = Mathf.Clamp01(settings.Volume.Value + delta);
            ApplySourceVolumes();
            status = "Volume " + Mathf.RoundToInt(settings.Volume.Value * 100f) + "%";
        }

        public void Rescan()
        {
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
            RadioChannel agrapolLocal = FindLocalChannel("Agrapol FM");
            RadioChannel marisLocal = FindLocalChannel("Maris Network");
            RadioChannel baseLocal = FindLocalChannel("Base Broadcast");

            AddStation(result, "agrapol-fm", "AF", "Agrapol FM", "Fields, flightlines, forward signal",
                soundtrackCatalog?.AgrapolSeed == null ? Array.Empty<AudioClip>() :
                    new[] { soundtrackCatalog.AgrapolSeed }, agrapolLocal);
            AddStation(result, "maris-network", "MN", "Maris Network", "Coastal relay for the contested sky",
                soundtrackCatalog?.MarisSeed == null ? Array.Empty<AudioClip>() :
                    new[] { soundtrackCatalog.MarisSeed }, marisLocal);
            AddStation(result, "base-broadcast", "BB", "Base Broadcast", "Nuclear Option original soundtrack",
                soundtrackCatalog?.All ?? Array.Empty<AudioClip>(), baseLocal);

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
                        channel.Name, "Local station", StationIconPath(channel.Name), tracks));
                }
            }
            stations = result.ToArray();
            stationRevision++;
        }

        private void AddStation(
            List<RadioStation> result,
            string id,
            string code,
            string name,
            string slogan,
            AudioClip[] vanilla,
            RadioChannel local)
        {
            int localCount = local?.Tracks.Length ?? 0;
            var tracks = new RadioStationTrack[vanilla.Length + localCount];
            for (int i = 0; i < vanilla.Length; i++)
                tracks[i] = RadioStationTrack.Vanilla(vanilla[i]);
            for (int i = 0; i < localCount; i++)
                tracks[vanilla.Length + i] = RadioStationTrack.Local(local.Tracks[i]);
            result.Add(new RadioStation(
                id, code, name, slogan,
                StationIconPath(name), tracks));
        }

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
                return;
            }

            if (!PrepareAudioSources())
            {
                status = "Music mixer is not ready";
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
            }
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
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            request.Dispose();
            if (clip == null)
            {
                state = currentClip != null ? PlaybackState.Playing : PlaybackState.Stopped;
                status = "Skipped empty track";
                if (currentClip == null) ReleaseVanillaOwnership();
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
            incomingSource.time = 0f;
            incomingSource.loop = false;
            incomingSource.volume = 0f;
            incomingSource.Play();
            pendingCoroutine = StartCoroutine(CrossFadeToIncoming(generation));
        }

        private IEnumerator CrossFadeToIncoming(int generation)
        {
            float duration = settings.CrossfadeSeconds.Value;
            float elapsed = 0f;
            float target = settings.Volume.Value;
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
            currentSource.volume = settings.Volume.Value;
            pendingCoroutine = null;
            state = PlaybackState.Playing;
            status = "On air";
        }

        private bool PrepareAudioSources()
        {
            if (currentSource == null) currentSource = CreateSource("BoscaliRadio.Current");
            if (incomingSource == null) incomingSource = CreateSource("BoscaliRadio.Incoming");
            try
            {
                currentSource.outputAudioMixerGroup = SoundManager.i.MusicMixer;
                incomingSource.outputAudioMixerGroup = SoundManager.i.MusicMixer;
                return currentSource.outputAudioMixerGroup != null;
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
            source.volume = settings.Volume.Value;
            return source;
        }

        private void ApplySourceVolumes()
        {
            if (currentSource != null) currentSource.volume = settings.Volume.Value;
            if (incomingSource != null && incomingSource.isPlaying)
                incomingSource.volume = Math.Min(incomingSource.volume, settings.Volume.Value);
        }

        private void StopInternal(bool destroying)
        {
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
            status = destroying ? "Stopped" : "Radio off";
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

        private RadioStationTrack CurrentTrack()
        {
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
