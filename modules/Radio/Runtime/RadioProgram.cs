using System;
using System.Collections;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>
    /// One audio output with a digital transport: two sources so a track can be crossfaded
    /// in, one in-flight decode at a time, and a small state machine over it.
    ///
    /// <para>Extracted from the receiver because the music deck is a second consumer with
    /// exactly the same problem — load a local OGG/WAV or take a soundtrack clip, blend,
    /// pause, resume, advance. Two copies of this would drift; the caller decides <em>what</em>
    /// plays, this decides how it gets to the mixer.</para>
    /// </summary>
    internal sealed class RadioProgram : IDisposable
    {
        public enum Transport
        {
            Stopped,
            Loading,
            Playing,
            Paused
        }

        private readonly MonoBehaviour runner;
        private readonly GameObject host;
        private readonly string name;
        private readonly ManualLogSource logger;

        private AudioSource currentSource;
        private AudioSource incomingSource;
        private AudioClip currentClip;
        private AudioClip incomingClip;
        private bool currentClipOwned;
        private bool incomingClipOwned;
        private UnityWebRequest pendingRequest;
        private Coroutine pendingCoroutine;
        private int loadGeneration;
        private float volume = 1f;
        private float crossfadeSeconds = 1.5f;
        private float currentGain = 1f, incomingGain = 1f; // loudness levelling per clip (Loudness.Gain)

        public RadioProgram(MonoBehaviour runner, GameObject host, string name, ManualLogSource logger)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.name = name ?? "Program";
            this.logger = logger;
        }

        public Transport State { get; private set; }
        public bool Engaged => State != Transport.Stopped;
        public bool IsPlaying => State == Transport.Playing;
        public bool IsPaused => State == Transport.Paused;
        public AudioSource Source => currentSource;
        public string LastError { get; private set; }
        /// <summary>The levelling gain on the clip on air (1 when it could not be measured).</summary>
        public float Gain => currentGain;
        /// <summary>The measured RMS of the clip on air, or -1 when it could not be read (a streamed soundtrack clip).</summary>
        public float Rms { get; private set; } = -1f;
        private float incomingRms = -1f;

        public float Duration =>
            currentClip != null ? currentClip.length : 0f;

        public float Elapsed =>
            currentSource != null && currentSource.clip != null ? currentSource.time : 0f;

        public float Progress
        {
            get
            {
                float duration = Duration;
                return duration > 0.01f ? Mathf.Clamp01(Elapsed / duration) : 0f;
            }
        }

        /// <summary>True once the clip ran out on its own; the caller advances the log.</summary>
        public bool Finished =>
            State == Transport.Playing && currentClip != null && pendingCoroutine == null &&
            currentSource != null && !currentSource.isPlaying;

        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
            if (pendingCoroutine == null && currentSource != null)
                currentSource.volume = Level(currentGain);
        }

        /// <summary>
        /// Knob × levelling × the game's own Master and Music sliders. The sources sit off the game's
        /// mixer (see <see cref="Prepare"/>), so the sliders are applied here instead.
        /// </summary>
        private float Level(float gain) =>
            volume * gain * AudioMixerVolume.GetPref(AudioMixerVolume.Master) * AudioMixerVolume.GetPref(AudioMixerVolume.Music);

        public void SetCrossfade(float seconds) => crossfadeSeconds = Mathf.Max(0f, seconds);

        /// <summary>
        /// Prepare the two sources; false (the game's audio not up yet) disables playback. They play
        /// straight to the listener, off the game's mixer: on the Music bus the radio was muffled by
        /// the G-LOC master low-pass and sat under every effect, so the set went quiet in a fight.
        /// </summary>
        public bool Prepare()
        {
            if (currentSource == null) currentSource = CreateSource(name + ".Current");
            if (incomingSource == null) incomingSource = CreateSource(name + ".Incoming");
            try
            {
                currentSource.volume = Level(currentGain);
                return SoundManager.i != null;
            }
            catch (Exception e)
            {
                logger?.LogDebug(name + " mixer not ready: " + e.Message);
                return false;
            }
        }

        public bool PlayClip(AudioClip clip, bool owned, float startTime, bool paused)
        {
            if (clip == null || !Prepare())
            {
                LastError = clip == null ? "No clip" : "Mixer unavailable";
                return false;
            }

            CancelPendingLoad();
            State = Transport.Loading;
            StartIncomingClip(clip, owned, startTime, paused);
            return true;
        }

        public bool PlayFile(string path, string extension, string title, float startTime, bool paused)
        {
            if (string.IsNullOrEmpty(path) || !Prepare())
            {
                LastError = "No file";
                return false;
            }

            CancelPendingLoad();
            State = Transport.Loading;
            AudioType audioType =
                string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV :
                string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG :
                AudioType.OGGVORBIS;

            try
            {
                pendingRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(path), audioType);
                var handler = pendingRequest.downloadHandler as DownloadHandlerAudioClip;
                if (handler != null) handler.streamAudio = false;
                int generation = loadGeneration;
                pendingCoroutine = runner.StartCoroutine(
                    LoadTrack(title, startTime, paused, generation, pendingRequest));
                return true;
            }
            catch (Exception e)
            {
                pendingRequest?.Dispose();
                pendingRequest = null;
                pendingCoroutine = null;
                State = currentClip != null ? Transport.Playing : Transport.Stopped;
                LastError = "Could not open " + title;
                logger?.LogWarning(name + " track open failed: " + e.Message);
                return false;
            }
        }

        public void Pause()
        {
            if (State != Transport.Playing || currentSource == null) return;
            currentSource.Pause();
            State = Transport.Paused;
        }

        public void Resume()
        {
            if (State != Transport.Paused || currentSource == null) return;
            currentSource.UnPause();
            State = Transport.Playing;
        }

        public void Stop()
        {
            CancelPendingLoad();
            if (currentSource != null)
            {
                currentSource.Stop();
                currentSource.clip = null;
            }
            DestroyClip(currentClip, currentClipOwned);
            currentClip = null;
            currentClipOwned = false;
            State = Transport.Stopped;
        }

        public void Dispose()
        {
            Stop();
            if (currentSource != null) UnityEngine.Object.Destroy(currentSource);
            if (incomingSource != null) UnityEngine.Object.Destroy(incomingSource);
            currentSource = null;
            incomingSource = null;
        }

        private IEnumerator LoadTrack(
            string title, float startTime, bool paused, int generation, UnityWebRequest request)
        {
            yield return request.SendWebRequest();

            if (generation != loadGeneration || request != pendingRequest) yield break;
            pendingRequest = null;
            pendingCoroutine = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error;
                request.Dispose();
                State = currentClip != null ? Transport.Playing : Transport.Stopped;
                LastError = "Skipped unreadable track";
                logger?.LogWarning(name + " could not decode a local track: " + error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            request.Dispose();
            if (clip == null)
            {
                State = currentClip != null ? Transport.Playing : Transport.Stopped;
                LastError = "Skipped empty track";
                yield break;
            }

            clip.name = title;
            StartIncomingClip(clip, true, startTime, paused);
        }

        private void StartIncomingClip(AudioClip clip, bool owned, float startTime, bool paused)
        {
            incomingClip = clip;
            incomingClipOwned = owned;
            incomingRms = Loudness.Rms(clip);
            incomingGain = Loudness.Gain(incomingRms);
            incomingSource.clip = clip;
            incomingSource.time = startTime <= 0f ? 0f :
                Mathf.Clamp(startTime, 0f, Math.Max(0f, clip.length - 0.05f));
            incomingSource.loop = false;
            incomingSource.volume = 0f;
            incomingSource.Play();
            if (paused) incomingSource.Pause();
            pendingCoroutine = runner.StartCoroutine(CrossFadeToIncoming(paused));
        }

        private IEnumerator CrossFadeToIncoming(bool paused)
        {
            float duration = crossfadeSeconds;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float amount = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                incomingSource.volume = Level(incomingGain) * amount;
                if (currentSource != null) currentSource.volume = Level(currentGain) * (1f - amount);
                yield return null;
            }

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
            currentGain = incomingGain;
            Rms = incomingRms;
            currentSource.volume = Level(currentGain);
            pendingCoroutine = null;
            State = paused ? Transport.Paused : Transport.Playing;
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
                runner.StopCoroutine(pendingCoroutine);
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

        private AudioSource CreateSource(string sourceName)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.name = sourceName;
            source.playOnAwake = false;
            source.loop = false;
            source.ignoreListenerPause = true;
            source.priority = 0; // never the voice Unity steals when engines + pilot grunts fill the real-voice budget
            source.spatialBlend = 0f;
            source.volume = volume;
            return source;
        }

        private static void DestroyClip(AudioClip clip, bool owned)
        {
            if (owned && clip != null) UnityEngine.Object.Destroy(clip);
        }
    }

    /// <summary>
    /// Levels the programme so a shouted advert, a quiet host and a mastered song sit at one
    /// loudness. RMS is sampled from 48 short windows across the clip (no full copy), once per
    /// clip as it starts. Attenuation only: an AudioSource cannot go above 1.
    /// </summary>
    internal static class Loudness
    {
        // ponytail: plain RMS, not LUFS; K-weighting if speech still reads louder than music.
        internal const float Target = 0.12f;   // about -18 dBFS RMS: trims hot ads and mastered songs, leaves speech
        private const int Windows = 48, Frames = 2048;
        private static float[] buffer = new float[0];

        /// <summary>Gain that brings <paramref name="rms"/> to <see cref="Target"/>; 1 when unknown or already quieter.</summary>
        internal static float Gain(float rms) => rms > 1e-4f ? Mathf.Clamp(Target / rms, 0.2f, 1f) : 1f;

        /// <summary>RMS of a decoded clip, or -1 for one Unity cannot read back (streamed or compressed).</summary>
        internal static float Rms(AudioClip clip)
        {
            if (clip == null || clip.loadType != AudioClipLoadType.DecompressOnLoad ||
                clip.loadState != AudioDataLoadState.Loaded || clip.samples <= Frames) return -1f;
            int length = Frames * Math.Max(1, clip.channels);
            if (buffer.Length < length) buffer = new float[length];
            double sum = 0d;
            long count = 0;
            for (int w = 0; w < Windows; w++)
            {
                int offset = (int)((long)(clip.samples - Frames) * (2 * w + 1) / (2 * Windows));
                if (!clip.GetData(buffer, offset)) return -1f;
                for (int i = 0; i < length; i++) sum += buffer[i] * buffer[i];
                count += length;
            }
            return count == 0 ? -1f : (float)Math.Sqrt(sum / count);
        }
    }
}
