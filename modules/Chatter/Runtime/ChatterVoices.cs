using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Core.Chatter;
using UnityEngine;
using UnityEngine.Networking;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// Recorded voices (docs/CHATTER_PACKS.md, "Voice packs"). A pack is a folder of clips named after the lines or events they serve (<see cref="ChatterVoiceKeys"/>),
    /// with an optional <c>pack.txt</c> saying whose voice it is (<c>persona:</c>, <c>character:</c>, <c>net:</c>). A speaker keeps one pack for the whole mission: the
    /// one recorded for their character, else one for their persona, else a plain pack of their net. A line with no clip remains text only. Clips decode one at a time
    /// after the mission starts, inside fixed limits; a pack that cannot be read is skipped.
    /// </summary>
    internal sealed class ChatterVoices
    {
        private const int MaxPacks = 8, MaxClipsPerPack = 256;
        /// <summary>The built-in station packs (2026-11-15, ElevenLabs; words in modules/Chatter/Assets/Voices/voices.tsv), embedded and unpacked to the BepInEx cache.</summary>
        private const string BuiltInPrefix = "BoscaliSummer.ChatterVoices.";
        private static readonly object BuiltInFoldersGate = new object();
        private static List<string> builtInFolders;
        private const long MaxFileBytes = 16L * 1024 * 1024, MaxDecodedBytes = 128L * 1024 * 1024;
        private const float MaxClipSeconds = 20f;

        private sealed class Pack
        {
            public string Name, Net = "wing", Character;
            /// <summary>Shipped with the mod: kept compressed in memory, and a pilot's own pack for the same net wins over it.</summary>
            public bool BuiltIn;
            public readonly List<string> Personas = new List<string>();
            public readonly List<string> Files = new List<string>();
            public AudioClip[] Clips;
            public readonly Dictionary<string, List<int>> Exact = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<int>> Fallback = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> Turn = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        private readonly List<Pack> packs = new List<Pack>();
        private readonly Dictionary<string, Pack> assigned = new Dictionary<string, Pack>(StringComparer.Ordinal);
        private readonly List<Pack> scratch = new List<Pack>();
        private readonly ManualLogSource log;
        private readonly string dataRoot;
        private AudioSource source;
        private UnityWebRequest request;
        private Task<List<Pack>> preparation;
        private CancellationTokenSource preparationCancellation;
        private int loadingPack, loadingFile;
        private long decodedBytes;

        /// <summary>Clips decoded and ready; lines that were voiced; built-in packs loaded (for the sim and the log).</summary>
        public int ClipsReady { get; private set; }
        public int Voiced { get; private set; }
        public int BuiltInPacks { get { int n = 0; foreach (Pack p in packs) if (p.BuiltIn) n++; return n; } }

        public ChatterVoices(ManualLogSource logger, string dataRoot)
        {
            log = logger;
            this.dataRoot = dataRoot;
        }

        private IEnumerable<string> Roots()
        {
            yield return Path.Combine(Paths.ConfigPath, "BoscaliSummer", "voicepacks");
            yield return Path.Combine(dataRoot, "voicepacks");
            yield return Path.Combine(Paths.PluginPath, "WSOYappinator", "audio");
            yield return Path.Combine(Paths.PluginPath, "WSO Yappinator", "audio");
        }

        /// <summary>A new mission: find the packs the pilot named (comma-separated folder names) and start decoding.</summary>
        public void Activate(string selected, ChatterLibrary library)
        {
            Deactivate();
            preparationCancellation = new CancellationTokenSource();
            CancellationToken cancellation = preparationCancellation.Token;
            preparation = Task.Run(() => Prepare(selected, library, cancellation), cancellation);
        }

        private List<Pack> Prepare(string selected, ChatterLibrary library, CancellationToken cancellation)
        {
            var prepared = new List<Pack>();
            foreach (string folder in BuiltInFolders())
            {
                if (cancellation.IsCancellationRequested) return prepared;
                try { Load(prepared, folder, Path.GetFileName(folder), library, true); }
                catch (Exception e) { log?.LogWarning("[Chatter] built-in voices unavailable: " + e.Message); }
            }
            int builtIn = prepared.Count;
            foreach (string raw in (selected ?? "").Split(','))
            {
                if (cancellation.IsCancellationRequested) return prepared;
                string name = raw.Trim();
                if (name.Length == 0 || prepared.Count - builtIn >= MaxPacks) continue;
                try
                {
                    string folder = Find(name);
                    if (folder == null) log?.LogWarning("[Chatter] voice pack not found: " + name);
                    else Load(prepared, folder, name, library, false);
                }
                catch (Exception e) { log?.LogWarning("[Chatter] voice pack unavailable: " + name + ": " + e.Message); }
            }
            return prepared;
        }

        /// <summary>The built-in packs as folders in the BepInEx cache. One folder for every build: a new build rewrites only the clips whose bytes changed
        /// (and drops clips it no longer ships); the same build only checks sizes.</summary>
        private List<string> BuiltInFolders()
        {
            lock (BuiltInFoldersGate) return builtInFolders ??= UnpackBuiltIn();
        }

        private List<string> UnpackBuiltIn()
        {
            var foldersToLoad = new List<string>();
            try
            {
                var assembly = typeof(ChatterVoices).Assembly;
                string voices = Path.Combine(Paths.CachePath, "BoscaliSummer", "voices"), root = Path.Combine(voices, "built-in");
                // Older builds unpacked into one ~14 MB folder each; nothing reads those again.
                if (Directory.Exists(voices))
                    foreach (string old in Directory.GetDirectories(voices))
                        if (!string.Equals(old, root, StringComparison.OrdinalIgnoreCase))
                            try { Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                string stamp = Path.Combine(root, "build.txt"), build = assembly.ManifestModule.ModuleVersionId.ToString("N");
                bool sameBuild = File.Exists(stamp) && File.ReadAllText(stamp) == build;
                var folders = new SortedSet<string>(StringComparer.Ordinal);
                var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stamp };
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (!name.StartsWith(BuiltInPrefix, StringComparison.Ordinal)) continue;
                    // "<pack>\<file>" (MSBuild RecursiveDir), never deeper.
                    string[] parts = name.Substring(BuiltInPrefix.Length).Split('\\', '/');
                    if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
                    string folder = Path.Combine(root, parts[0]), file = Path.Combine(folder, parts[1]);
                    using (Stream stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null || stream.Length <= 0 || stream.Length > MaxFileBytes) continue;
                        bool sized = File.Exists(file) && new FileInfo(file).Length == stream.Length;
                        if (!sized || !sameBuild)
                        {
                            byte[] bytes = BoscaliSummer.Core.Util.EmbeddedResources.ReadAll(stream, (int)MaxFileBytes);
                            if (bytes == null) continue;
                            if (!sized || !System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(file), bytes))
                            {
                                Directory.CreateDirectory(folder);
                                File.WriteAllBytes(file, bytes);
                            }
                        }
                    }
                    shipped.Add(file);
                    folders.Add(folder);
                }
                if (!sameBuild)
                {
                    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                        if (!shipped.Contains(file)) File.Delete(file);
                    File.WriteAllText(stamp, build);
                }
                foldersToLoad.AddRange(folders);
            }
            catch (Exception e) { log?.LogWarning("[Chatter] could not unpack the built-in voices: " + e.Message); }
            return foldersToLoad;
        }

        /// <summary>A radio voice line is sounding now: the visor's chatter keeps quiet under it (Core/Fx/VisorSound).</summary>
        internal bool Sounding => source != null && source.isPlaying && source.volume > 0f;
        internal bool Loading => preparation != null || decodedBytes < MaxDecodedBytes && (request != null || loadingPack < packs.Count);

        public void Stop() { if (source != null) source.Stop(); }
        public void Volume(float value) { if (source != null) source.volume = Mathf.Clamp01(value); }

        public void Deactivate()
        {
            preparationCancellation?.Cancel();
            preparationCancellation?.Dispose();
            preparationCancellation = null;
            preparation = null;
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (source != null) source.Stop();
            foreach (Pack pack in packs)
                foreach (AudioClip clip in pack.Clips) if (clip != null) UnityEngine.Object.Destroy(clip);
            packs.Clear();
            assigned.Clear();
            loadingPack = loadingFile = 0;
            decodedBytes = 0;
            ClipsReady = 0;
        }

        private string Find(string name)
        {
            // A configured name is a single folder, never a path.
            if (name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            foreach (string root in Roots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (string folder in Directory.EnumerateDirectories(root))
                    if (string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase) &&
                        (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) return folder;
            }
            return null;
        }

        private void Load(List<Pack> prepared, string folder, string name, ChatterLibrary library, bool builtIn)
        {
            var pack = new Pack { Name = name, BuiltIn = builtIn };
            string manifest = Path.Combine(folder, "pack.txt");
            if (File.Exists(manifest) && new FileInfo(manifest).Length < 4096)
                foreach (string row in File.ReadAllLines(manifest))
                {
                    int colon = row.IndexOf(':');
                    if (colon < 1) continue;
                    string key = row.Substring(0, colon).Trim().ToLowerInvariant(), value = row.Substring(colon + 1).Trim().ToLowerInvariant();
                    if (key == "net" && value.Length > 0) pack.Net = value;
                    else if (key == "character" && value.Length > 0) pack.Character = value;
                    else if (key == "persona")
                        foreach (string p in value.Split(',')) if (p.Trim().Length > 0) pack.Personas.Add(p.Trim());
                }
            var exact = new List<string>();
            var fallback = new List<string>();
            Func<string, bool> known = library.Events.ContainsKey;
            int inspected = 0;
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (++inspected > 4096 || pack.Files.Count >= MaxClipsPerPack) break;
                var info = new FileInfo(file);
                if (TypeOf(file) == AudioType.UNKNOWN || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length <= 0 || info.Length > MaxFileBytes) continue;
                exact.Clear();
                fallback.Clear();
                ChatterVoiceKeys.Of(Path.GetFileNameWithoutExtension(file), pack.Net, known, exact, fallback);
                if (exact.Count == 0 && fallback.Count == 0) continue;
                int index = pack.Files.Count;
                pack.Files.Add(file);
                foreach (string key in exact) Index(pack.Exact, key, index);
                foreach (string key in fallback) Index(pack.Fallback, key, index);
            }
            prepared.Add(pack);
            log?.LogInfo("[Chatter] voice pack " + name + ": " + pack.Files.Count + " clips (" + pack.Net +
                (pack.Character != null ? ", character " + pack.Character : pack.Personas.Count > 0 ? ", " + string.Join("/", pack.Personas) : "") + ")");
        }

        private static void Index(Dictionary<string, List<int>> map, string key, int file)
        {
            if (!map.TryGetValue(key, out List<int> list)) map[key] = list = new List<int>();
            list.Add(file);
        }

        private static AudioType TypeOf(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }

        /// <summary>Once a frame: take the clip that finished decoding and start the next.</summary>
        public void Tick()
        {
            if (preparation != null)
            {
                if (!preparation.IsCompleted) return;
                if (preparation.Status == TaskStatus.RanToCompletion)
                    foreach (Pack pack in preparation.Result)
                    {
                        pack.Clips = new AudioClip[pack.Files.Count];
                        packs.Add(pack);
                    }
                else log?.LogWarning("[Chatter] could not prepare the voice packs: " + preparation.Exception?.GetBaseException().Message);
                preparation = null;
                preparationCancellation.Dispose();
                preparationCancellation = null;
            }
            if (packs.Count == 0) return;
            if (request != null)
            {
                if (!request.isDone) return;
                AudioClip clip = null;
                try
                {
                    if (request.result == UnityWebRequest.Result.Success) clip = DownloadHandlerAudioClip.GetContent(request);
                    // A built-in clip stays compressed in memory: count its file, not its samples.
                    long bytes = clip == null ? 0 : packs[loadingPack].BuiltIn ? new FileInfo(packs[loadingPack].Files[loadingFile]).Length
                        : (long)clip.samples * clip.channels * sizeof(float);
                    if (clip != null && clip.loadState == AudioDataLoadState.Loaded && clip.length > 0f && clip.length <= MaxClipSeconds &&
                        bytes > 0 && decodedBytes + bytes <= MaxDecodedBytes)
                    {
                        packs[loadingPack].Clips[loadingFile] = clip;
                        decodedBytes += bytes;
                        ClipsReady++;
                        clip = null;
                    }
                }
                catch (Exception e) { log?.LogWarning("[Chatter] clip decode failed: " + e.Message); }
                finally
                {
                    if (clip != null) UnityEngine.Object.Destroy(clip);
                    request.Dispose();
                    request = null;
                    loadingFile++;
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
                    if (pack.BuiltIn && request.downloadHandler is DownloadHandlerAudioClip handler) handler.compressed = true;
                    request.timeout = 10;
                    request.SendWebRequest();
                    return;
                }
                catch (Exception e)
                {
                    request?.Dispose();
                    request = null;
                    loadingFile++;
                    log?.LogWarning("[Chatter] clip load failed: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Voice a line that just went on air. Returns the clip's length, or 0 when this speaker has no recording for it. A line that cut another stops that one's clip.
        /// Each speaker sits slightly to one side; the actor's original pitch and the mastered radio treatment are preserved.
        /// </summary>
        public float Play(in ChatterAired line, Transform host, float volume)
        {
            if (source != null && line.Cut) source.Stop();
            // Script words have their own duration and narration; generic pack recordings cannot stand in for them.
            if (line.Scripted) return 0f;
            if (packs.Count == 0 || string.IsNullOrEmpty(line.SpeakerId)) return 0f;
            Pack pack = PackFor(line);
            if (pack == null) return 0f;
            AudioClip clip = Clip(pack, pack.Exact, ChatterVoiceKeys.Line(line.LineId ?? "")) ?? Clip(pack, pack.Exact, line.Event) ?? Clip(pack, pack.Fallback, line.Event);
            if (clip == null) return 0f;
            try
            {
                SoundManager sound = SoundManager.i;
                if (sound == null) return 0f;
                if (source == null)
                {
                    var go = new GameObject("BoscaliChatterVoice") { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(host, false);
                    source = go.AddComponent<AudioSource>();
                    source.spatialBlend = 0f;
                    source.playOnAwake = false;
                    source.priority = 16;
                    // Shipped masters are already band-limited and normalized offline. Preserve the actor's delivery.
                }
                uint h = Hash(line.SpeakerId);
                source.outputAudioMixerGroup = sound.InterfaceMixer;
                source.volume = Mathf.Clamp01(volume);
                source.panStereo = ((h & 0xFF) / 255f - 0.5f) * 0.2f;
                source.pitch = 1f;
                source.clip = clip;
                source.Play();
                Voiced++;
                return clip.length / source.pitch;
            }
            catch (Exception e)
            {
                log?.LogWarning("[Chatter] voice playback failed: " + e.Message);
                return 0f;
            }
        }

        private Pack PackFor(in ChatterAired line)
        {
            if (assigned.TryGetValue(line.SpeakerId, out Pack pack)) return pack;
            string net = line.Net.ToString().ToLowerInvariant();
            scratch.Clear();
            if (!string.IsNullOrEmpty(line.Character))
                foreach (Pack p in packs) if (p.Character == line.Character) scratch.Add(p);
            if (scratch.Count == 0 && !string.IsNullOrEmpty(line.Persona))
                foreach (Pack p in packs) if (p.Net == net && p.Character == null && p.Personas.Contains(line.Persona)) scratch.Add(p);
            if (scratch.Count == 0)
                foreach (Pack p in packs) if (p.Net == net && p.Character == null && p.Personas.Count == 0 && !p.BuiltIn) scratch.Add(p);
            if (scratch.Count == 0)
                foreach (Pack p in packs) if (p.Net == net && p.Character == null && p.Personas.Count == 0) scratch.Add(p);
            pack = scratch.Count == 0 ? null : scratch[(int)(Hash(line.SpeakerId) % (uint)scratch.Count)];
            scratch.Clear();
            if (assigned.Count > 256) assigned.Clear();
            assigned[line.SpeakerId] = pack;
            return pack;
        }

        /// <summary>The next decoded clip of a key, in turn, so a pack's takes of one call rotate.</summary>
        private static AudioClip Clip(Pack pack, Dictionary<string, List<int>> map, string key)
        {
            if (string.IsNullOrEmpty(key) || !map.TryGetValue(key, out List<int> files)) return null;
            pack.Turn.TryGetValue(key, out int turn);
            for (int i = 0; i < files.Count; i++)
            {
                AudioClip clip = pack.Clips[files[(turn + i) % files.Count]];
                if (clip == null) continue;
                pack.Turn[key] = (turn + i + 1) % files.Count;
                return clip;
            }
            return null;
        }

        private static uint Hash(string value)
        {
            uint h = 2166136261;
            foreach (char c in value) { h ^= c; h *= 16777619; }
            return h;
        }
    }
}
