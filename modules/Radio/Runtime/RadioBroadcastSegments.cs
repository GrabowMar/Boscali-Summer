using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Util;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>
    /// The voiced station talk between songs: stings, idents, host links, adverts and news, made with
    /// ElevenLabs and embedded with their scripts in <c>Assets/Broadcast/broadcast.tsv</c>. Each
    /// built-in station has one set; Base Broadcast picks its set by the local player's faction, so
    /// a BDF pilot hears forces radio, a PALA pilot the army network, a contractor the PMC net.
    /// Clips are unpacked to the BepInEx cache once and then played like any local track.
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

        /// <summary>The break that follows song number <paramref name="breakIndex"/>, in play order; empty when the station has no talk.</summary>
        public static List<RadioStationTrack> Break(string stationId, string playerFaction, int breakIndex) =>
            Build(stationId, playerFaction, BreakPlan.Kinds(breakIndex));

        /// <summary>What a listener hears on tuning in: the station's sting, then a spoken ident.</summary>
        public static List<RadioStationTrack> Opener(string stationId, string playerFaction, int openerIndex) =>
            Build(stationId, playerFaction, new[] { ("sting", 0), ("id", openerIndex) });

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
                Segment segment = pool[pick % pool.Count];
                string path = Unpack(segment.File);
                if (path != null)
                    result.Add(RadioStationTrack.Local(new RadioTrack(segment.Title, path, ".mp3")));
            }
            return result;
        }

        /// <summary>br / ps for the civil stations; bb-bdf, bb-pala or bb-pmc for Base Broadcast by the listener's side.</summary>
        internal static string SetFor(string stationId, string playerFaction)
        {
            switch (stationId)
            {
                case BuiltInStationRules.AgrapolId: return "br";
                case BuiltInStationRules.MarisId: return "ps";
                case BuiltInStationRules.BaseId:
                    string faction = playerFaction ?? string.Empty;
                    if (faction.IndexOf("Boscali", StringComparison.OrdinalIgnoreCase) >= 0) return "bb-bdf";
                    if (faction.IndexOf("Primeva", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        faction.IndexOf("PALA", StringComparison.OrdinalIgnoreCase) >= 0) return "bb-pala";
                    return faction.Length == 0 ? "bb-bdf" : "bb-pmc";
                default: return null;
            }
        }

        /// <summary>The on-air name of the station's host, for the RECEIVER card; null for a local archive.</summary>
        public static string Host(string stationId, string playerFaction)
        {
            switch (SetFor(stationId, playerFaction))
            {
                case "br": return "Kit Vale";
                case "ps": return "Announcer Dren";
                case "bb-bdf": return "Sgt. Jerry Banks";
                case "bb-pala": return "Commissar Strand";
                case "bb-pmc": return "Dana Quill";
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
            try
            {
                string folder = Path.Combine(BepInEx.Paths.CachePath, "BoscaliSummer", "Radio");
                string cached = Path.Combine(folder, file);
                byte[] bytes = EmbeddedResources.ReadAll(typeof(RadioBroadcastSegments).Assembly, Prefix + file, MaxClipBytes);
                if (bytes == null) return null;
                if (File.Exists(cached) && new FileInfo(cached).Length == bytes.Length) return cached;
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(cached, bytes);
                return cached;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    /// <summary>
    /// Which kinds of talk fill the break after each song. Every break opens on the station's sting;
    /// every third adds a spoken ident; the main item rotates host, ad, host, news, ad, host so a
    /// station never runs two adverts back to back. The pick is a running count per kind, so each
    /// pool is walked in order before anything repeats.
    /// </summary>
    internal static class BreakPlan
    {
        private static readonly string[] Rotation = { "host", "ad", "host", "news", "ad", "host" };

        public static string Word(string kind) => kind == "sting" ? "STING" : kind == "id" ? "STATION ID"
            : kind == "host" ? "HOST" : kind == "ad" ? "ADVERT" : kind == "news" ? "NEWS" : kind.ToUpperInvariant();

        /// <summary>Talk kinds plus <c>song</c>, the station's own music, which plays as a programme item rather than in a break.</summary>
        public static bool IsKind(string value) =>
            value == "sting" || value == "id" || value == "host" || value == "ad" || value == "news" || value == "song";

        public static IEnumerable<(string Kind, int Pick)> Kinds(int breakIndex)
        {
            int n = Math.Max(0, breakIndex);
            yield return ("sting", n); // rotates the station's stings (sound logo, sung jingle)
            if (n % 3 == 0) yield return ("id", n / 3);
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
