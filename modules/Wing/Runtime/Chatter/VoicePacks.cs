using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Configuration;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Local Yappinator pack adapter. One asynchronous decode at a time; no plugin dependency.</summary>
    internal static class VoicePacks
    {
        public const int MaxPacks = 8, MaxClipsPerPack = 256;
        public const long MaxFileBytes = 16 * 1024 * 1024, MaxDecodedBytes = 128 * 1024 * 1024;
        public const float MaxClipSeconds = 20f;
        private sealed class Pack
        {
            public readonly VoicePackIndex Index = new VoicePackIndex();
            public readonly List<string> Files = new List<string>();
            public readonly Dictionary<string, int> Next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public AudioClip[] Clips;
        }
        private static readonly List<Pack> packs = new List<Pack>();
        private static AudioSource source;
        private static UnityWebRequest request;
        private static int loadingPack, loadingFile;
        private static long decodedBytes;
        private static bool failed;
        public static bool Playing => source != null && source.isPlaying;
        public static void Stop() { if (source != null) source.Stop(); }

        private static IEnumerable<string> Roots()
        {
            yield return Path.Combine(Paths.ConfigPath, "BoscaliSummer", "voicepacks");
            yield return Path.Combine(WingConfig.DataRoot, "voicepacks");
            yield return Path.Combine(Paths.PluginPath, "WSOYappinator", "audio");
            yield return Path.Combine(Paths.PluginPath, "WSO Yappinator", "audio");
        }

        public static void Activate()
        {
            Deactivate();
            failed = false;
            foreach (string raw in (WingSettings.Instance.VoicePacks.Value ?? "").Split(','))
            {
                if (packs.Count >= MaxPacks) break;
                string name = raw.Trim();
                if (name.Length == 0) continue;
                try { Load(name); }
                catch (Exception e) { WingLog.Logger.LogWarning("[Radio] pack unavailable: " + name + ": " + e.Message); }
            }
        }

        private static string Find(string name)
        {
            // A configured name is a single folder, never an arbitrary path.
            if (name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return null;
            foreach (string root in Roots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (string folder in Directory.EnumerateDirectories(root))
                    if (string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase) &&
                        (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) return folder;
            }
            return null;
        }

        private static void Load(string name)
        {
            string folder = Find(name);
            if (folder == null) { WingLog.Logger.LogWarning("[Radio] pack not found: " + name); return; }
            var pack = new Pack();
            // Flat event-tagged files are the upstream format. Metadata is optional for Boscali:
            // eventPriorities.txt cannot promote an idle clip over an emergency on this channel.
            int inspected = 0;
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (++inspected > 4096 || pack.Files.Count >= MaxClipsPerPack) break;
                var info = new FileInfo(file);
                if (TypeOf(file) == AudioType.UNKNOWN || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    info.Length <= 0 || info.Length > MaxFileBytes) continue;
                int before = pack.Index.Count;
                pack.Index.Add(pack.Files.Count, Path.GetFileNameWithoutExtension(file));
                if (pack.Index.Count > before) pack.Files.Add(file);
            }
            pack.Clips = new AudioClip[pack.Files.Count];
            packs.Add(pack); // Preserve configured voice assignment even for an empty but valid pack.
            WingLog.Logger.LogInfo("[Radio] " + name + ": " + pack.Files.Count + " compatible clips");
        }

        private static AudioType TypeOf(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            { case ".wav": return AudioType.WAV; case ".ogg": return AudioType.OGGVORBIS;
              case ".mp3": return AudioType.MPEG; default: return AudioType.UNKNOWN; }
        }

        public static void Deactivate()
        {
            Stop();
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (source != null) { UnityEngine.Object.Destroy(source.gameObject); source = null; }
            foreach (Pack pack in packs)
                foreach (AudioClip clip in pack.Clips) if (clip != null) UnityEngine.Object.Destroy(clip);
            packs.Clear();
            loadingPack = loadingFile = 0;
            decodedBytes = 0;
        }

        public static void Tick()
        {
            if (source != null) source.volume = Mathf.Clamp01(WingSettings.Instance.VoicePackVolume.Value);
            if (request != null)
            {
                if (!request.isDone) return;
                AudioClip clip = null;
                try
                {
                    if (request.result == UnityWebRequest.Result.Success) clip = DownloadHandlerAudioClip.GetContent(request);
                    long bytes = clip == null ? 0 : (long)clip.samples * clip.channels * sizeof(float);
                    if (clip != null && clip.loadState == AudioDataLoadState.Loaded && clip.length > 0f &&
                        clip.length <= MaxClipSeconds && bytes > 0 && decodedBytes + bytes <= MaxDecodedBytes)
                    { packs[loadingPack].Clips[loadingFile] = clip; decodedBytes += bytes; clip = null; }
                }
                catch (Exception e) { WingLog.Logger.LogWarning("[Radio] clip decode failed: " + e.Message); }
                finally
                {
                    if (clip != null) UnityEngine.Object.Destroy(clip);
                    request.Dispose(); request = null; loadingFile++;
                }
            }
            if (decodedBytes >= MaxDecodedBytes) return;
            while (loadingPack < packs.Count)
            {
                Pack pack = packs[loadingPack];
                if (loadingFile >= pack.Files.Count) { loadingPack++; loadingFile = 0; continue; }
                try
                {
                    string file = pack.Files[loadingFile];
                    request = UnityWebRequestMultimedia.GetAudioClip(new Uri(file).AbsoluteUri, TypeOf(file));
                    request.timeout = 10;
                    request.SendWebRequest();
                    return;
                }
                catch (Exception e)
                {
                    request?.Dispose(); request = null; loadingFile++;
                    WingLog.Logger.LogWarning("[Radio] clip load failed: " + e.Message);
                }
            }
        }

        public static bool TryPlay(int number, string call)
        {
            if (failed || packs.Count == 0) return false;
            Pack pack = packs[VoicePackIndex.PackFor(number, packs.Count)];
            foreach (string evt in VoicePackIndex.EventsFor(call))
            {
                IReadOnlyList<int> files = pack.Index.Clips(evt);
                pack.Next.TryGetValue(evt, out int next);
                for (int k = 0; k < files.Count; k++)
                {
                    int index = (int)(((uint)next + (uint)k) % (uint)files.Count);
                    AudioClip clip = pack.Clips[files[index]];
                    if (clip == null) continue;
                    pack.Next[evt] = (index + 1) % files.Count;
                    return Play(clip);
                }
            }
            return false;
        }

        private static bool Play(AudioClip clip)
        {
            try
            {
                if (source == null)
                {
                    var go = new GameObject("BoscaliChatterVoice") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    source = go.AddComponent<AudioSource>();
                    source.spatialBlend = 0f; source.playOnAwake = false;
                }
                source.Stop(); source.volume = Mathf.Clamp01(WingSettings.Instance.VoicePackVolume.Value);
                source.clip = clip; source.Play(); return true;
            }
            catch (Exception e)
            {
                failed = true;
                WingLog.Logger.LogWarning("[Radio] pack playback unavailable this mission: " + e.Message);
                return false;
            }
        }
    }
}
