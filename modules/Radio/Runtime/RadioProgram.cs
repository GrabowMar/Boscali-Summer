using System;
using System.Collections;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace BoscaliSummer.Features.Radio.Runtime
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
        private AudioMixerGroup mixer;
        private int loadGeneration;
        private float volume = 1f;
        private float crossfadeSeconds = 1.5f;

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
                currentSource.volume = volume;
        }

        public void SetCrossfade(float seconds) => crossfadeSeconds = Mathf.Max(0f, seconds);

        /// <summary>Prepare the two sources against the music mixer; false disables playback.</summary>
        public bool Prepare()
        {
            if (currentSource == null) currentSource = CreateSource(name + ".Current");
            if (incomingSource == null) incomingSource = CreateSource(name + ".Incoming");
            try
            {
                AudioMixerGroup group = SoundManager.i == null ? null : SoundManager.i.MusicMixer;
                currentSource.outputAudioMixerGroup = group;
                incomingSource.outputAudioMixerGroup = group;
                mixer = group;
                currentSource.volume = volume;
                return group != null;
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
            AudioType audioType = string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)
                ? AudioType.WAV
                : AudioType.OGGVORBIS;

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
            float target = volume;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float amount = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                incomingSource.volume = target * amount;
                if (currentSource != null) currentSource.volume = target * (1f - amount);
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
            currentSource.volume = target;
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
            source.spatialBlend = 0f;
            source.volume = volume;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        private static void DestroyClip(AudioClip clip, bool owned)
        {
            if (owned && clip != null) UnityEngine.Object.Destroy(clip);
        }
    }
}
