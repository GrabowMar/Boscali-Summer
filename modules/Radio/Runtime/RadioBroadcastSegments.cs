using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Util;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>
    /// The three stations air music, adverts and news. Base Broadcast always uses the mercenary set.
    /// Generated audio is unpacked into the local cache once and decoded on demand.
    /// </summary>
    internal static class RadioBroadcastSegments
    {
        private const string Prefix = "BoscaliSummer.RadioAssets.Broadcast.";
        private const int MaxClipBytes = 4 * 1024 * 1024;

        private sealed class Segment
        {
            public string File;
            public string Title;
        }

        private static Dictionary<string, Dictionary<string, List<Segment>>> sets;
        private static readonly Dictionary<string, string> unpacked = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The break that follows song number <paramref name="breakIndex"/>, in play order; empty when the station has no talk.</summary>
        public static List<RadioStationTrack> Break(string stationId, string playerFaction, int breakIndex) =>
            Build(stationId, playerFaction, BreakPlan.Kinds(breakIndex));

        /// <summary>The station's own song number <paramref name="pick"/> (ElevenLabs music, walked in order); null when it has none.</summary>
        public static RadioStationTrack Song(string stationId, string playerFaction, int pick)
        {
            List<RadioStationTrack> one = Build(stationId, playerFaction, new[] { ("song", pick) });
            return one.Count > 0 ? one[0] : null;
        }

        private static List<RadioStationTrack> Build(string stationId, string playerFaction, IEnumerable<(string Kind, int Pick)> plan)
        {
            var result = new List<RadioStationTrack>(3);
            string set = SetFor(stationId, playerFaction);
            if (set == null || !Load().TryGetValue(set, out Dictionary<string, List<Segment>> kinds)) return result;

            foreach ((string kind, int pick) in plan)
            {
                if (!kinds.TryGetValue(kind, out List<Segment> pool) || pool.Count == 0) continue;
                Segment segment = pool[Math.Max(0, pick) % pool.Count];
                string path = Unpack(segment.File);
                if (path != null)
                    result.Add(RadioStationTrack.Local(new RadioTrack(segment.Title, path, ".mp3")));
            }
            return result;
        }

        /// <summary>br / ps for civil stations; bb-pmc for Base Broadcast for every listener.</summary>
        internal static string SetFor(string stationId, string playerFaction)
        {
            switch (stationId)
            {
                case BuiltInStationRules.AgrapolId: return "br";
                case BuiltInStationRules.MarisId: return "ps";
                case BuiltInStationRules.BaseId: return "bb-pmc";
                default: return null;
            }
        }

        private static Dictionary<string, Dictionary<string, List<Segment>>> Load()
        {
            if (sets != null) return sets;
            sets = new Dictionary<string, Dictionary<string, List<Segment>>>(StringComparer.Ordinal);
            byte[] bytes = EmbeddedResources.ReadAll(typeof(RadioBroadcastSegments).Assembly, Prefix + "broadcast.tsv", 256 * 1024);
            if (bytes == null) return sets;

            foreach (string raw in Encoding.UTF8.GetString(bytes).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] cells = line.Split('\t');
                if (cells.Length < 3) continue;
                // file = <set>-<kind>[-<n>].mp3; the set may itself contain a dash (bb-pala).
                string[] parts = Path.GetFileNameWithoutExtension(cells[0]).Split('-');
                int kindAt = Array.FindLastIndex(parts, BreakPlan.IsKind);
                if (kindAt <= 0) continue;
                string set = string.Join("-", parts, 0, kindAt);
                if (!sets.TryGetValue(set, out Dictionary<string, List<Segment>> kinds))
                    sets[set] = kinds = new Dictionary<string, List<Segment>>(StringComparer.Ordinal);
                if (!kinds.TryGetValue(parts[kindAt], out List<Segment> pool))
                    kinds[parts[kindAt]] = pool = new List<Segment>();
                pool.Add(new Segment { File = cells[0], Title = cells[2] });
            }
            return sets;
        }

        /// <summary>The clip as a file Unity can decode (unpacked into the cache once), or null when it is not embedded.</summary>
        private static string Unpack(string file)
        {
            if (unpacked.TryGetValue(file, out string known) && File.Exists(known)) return known;
            try
            {
                string folder = Path.Combine(BepInEx.Paths.CachePath, "BoscaliSummer", "Radio");
                string cached = Path.Combine(folder, file);
                byte[] bytes = EmbeddedResources.ReadAll(typeof(RadioBroadcastSegments).Assembly, Prefix + file, MaxClipBytes);
                if (bytes == null) return null;
                if (File.Exists(cached) && new FileInfo(cached).Length == bytes.Length) { unpacked[file] = cached; return cached; }
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(cached, bytes);
                unpacked[file] = cached;
                return cached;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    /// <summary>
    /// A single advert or news item after each song. Each pool advances before repeating.
    /// </summary>
    internal static class BreakPlan
    {
        private static readonly string[] Rotation = { "ad", "news", "ad", "ad", "news" };

        public static bool IsKind(string value) => value == "ad" || value == "news" || value == "song";

        public static IEnumerable<(string Kind, int Pick)> Kinds(int breakIndex)
        {
            int n = Math.Max(0, breakIndex);
            string main = Rotation[n % Rotation.Length];
            int perCycle = 0, before = 0;
            for (int i = 0; i < Rotation.Length; i++)
            {
                if (Rotation[i] != main) continue;
                perCycle++;
                if (i < n % Rotation.Length) before++;
            }
            yield return (main, n / Rotation.Length * perCycle + before);
        }
    }
}
