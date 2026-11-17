using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// Shared, borrowed clips from the offline audio catalogue. Physical category folders do not change the stable
    /// BoscaliSummer.Sfx.&lt;id&gt;.wav resource IDs. Decode once on the Unity main thread, inside fixed retained-memory bounds;
    /// unknown IDs allocate no cache entry. Callers never destroy these clips. Clear only after their sources stop.
    /// </summary>
    internal static class SfxClips
    {
        private const int SampleRate = 22050;
        private const string Prefix = "BoscaliSummer.Sfx.";
        internal const int MaxFileBytes = 2 * 1024 * 1024, MaxCachedClips = 256;
        internal const long MaxDecodedBytes = 64L * 1024 * 1024;
        internal const int MaxClipSeconds = 30;

        private readonly struct Cached
        {
            internal readonly AudioClip Clip;
            internal readonly long Bytes;
            internal Cached(AudioClip clip, long bytes) { Clip = clip; Bytes = bytes; }
        }

        private static readonly Dictionary<string, Cached> clips = new Dictionary<string, Cached>(StringComparer.Ordinal);
        private static Dictionary<string, string> resources;
        private static long decodedBytes;
        private static int decodes, failed, rejected;

        internal static int KnownCount => Resources.Count;
        internal static IEnumerable<string> KnownIds => Resources.Keys;
        internal static int CachedCount => clips.Count;
        internal static long DecodedBytes => decodedBytes;
        internal static int DecodeCount => decodes;
        internal static int FailedCount => failed;
        internal static int RejectedCount => rejected;

        private static Dictionary<string, string> Resources
        {
            get
            {
                if (resources != null) return resources;
                resources = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string resource in typeof(SfxClips).Assembly.GetManifestResourceNames())
                {
                    if (!resource.StartsWith(Prefix, StringComparison.Ordinal) || !resource.EndsWith(".wav", StringComparison.Ordinal)) continue;
                    string id = resource.Substring(Prefix.Length, resource.Length - Prefix.Length - 4);
                    if (ValidId(id)) resources[id] = resource;
                }
                return resources;
            }
        }

        public static AudioClip Get(string name)
        {
            if (!ValidId(name) || !Resources.TryGetValue(name, out string resource))
            {
                Increment(ref rejected);
                return null;
            }
            // Unity can destroy a clip while its managed wrapper still exists: forget its accounting before reloading.
            if (clips.TryGetValue(name, out Cached cached))
            {
                if (cached.Clip != null || ReferenceEquals(cached.Clip, null)) return cached.Clip;
                decodedBytes -= cached.Bytes;
                clips.Remove(name);
            }
            if (clips.Count >= MaxCachedClips) { Increment(ref rejected); return null; }

            AudioClip clip = null;
            long retained = 0;
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(SfxClips).Assembly, resource, MaxFileBytes);
                // Conservative preflight avoids allocating a PCM buffer when there is no room to retain it.
                if (bytes != null && decodedBytes + (long)bytes.Length * 2 <= MaxDecodedBytes)
                {
                    float[] samples = PcmWav.Decode(bytes, SampleRate);
                    if (samples != null && samples.Length > 0 && samples.Length <= SampleRate * MaxClipSeconds)
                    {
                        clip = AudioClip.Create("BoscaliSfx." + name, samples.Length, 1, SampleRate, false);
                        if (clip != null && clip.SetData(samples, 0)) retained = (long)samples.Length * sizeof(float);
                        else if (clip != null) { UnityEngine.Object.Destroy(clip); clip = null; }
                    }
                }
            }
            catch (Exception error)
            {
                if (clip != null) UnityEngine.Object.Destroy(clip);
                clip = null;
                Plugin.Logger?.LogWarning("[Audio] Cannot decode " + name + ": " + error.Message);
            }
            if (clip == null) Increment(ref failed); else Increment(ref decodes);
            decodedBytes += retained;
            clips[name] = new Cached(clip, retained); // one failed known resource is checked only once
            return clip;
        }

        internal static void Describe(IDictionary<string, object> state)
        {
            if (state == null) return;
            state["audioKnownClips"] = KnownCount;
            state["audioCachedClips"] = CachedCount;
            state["audioDecodedBytes"] = decodedBytes;
            state["audioDecodedLimit"] = MaxDecodedBytes;
            state["audioDecodes"] = decodes;
            state["audioFailed"] = failed;
            state["audioRejected"] = rejected;
        }

        internal static void Clear()
        {
            foreach (Cached cached in clips.Values) if (cached.Clip != null) UnityEngine.Object.Destroy(cached.Clip);
            clips.Clear();
            decodedBytes = 0;
            decodes = failed = rejected = 0;
        }

        private static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 80) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            return true;
        }

        private static void Increment(ref int value) { if (value < int.MaxValue) value++; }
    }
}
