using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Scenario
{
    /// <summary>
    /// The mission factions at peace with each other, by faction name. The game itself has no neutrality: every other HQ is an enemy. A scripted mission's
    /// <c>peace</c> lines are set here before its units spawn, and the Pmc module drops every sighting between two sides at peace, so neither ever targets
    /// the other. Empty for every other mission. Read on each sighting, so it is a flat array and allocation-free.
    /// </summary>
    internal static class FactionPeace
    {
        // Pairs, flat: a0, b0, a1, b1...
        private static string[] pairs = Array.Empty<string>();

        public static bool Any => pairs.Length > 0;

        public static void Set(IEnumerable<KeyValuePair<string, string>> peace)
        {
            var list = new List<string>();
            if (peace != null)
                foreach (KeyValuePair<string, string> pair in peace)
                    if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(pair.Value) && pair.Key != pair.Value)
                    {
                        list.Add(pair.Key);
                        list.Add(pair.Value);
                    }
            pairs = list.ToArray();
        }

        public static bool Between(string a, string b)
        {
            string[] p = pairs;
            for (int i = 0; i < p.Length; i += 2)
                if (p[i] == a && p[i + 1] == b || p[i] == b && p[i + 1] == a) return true;
            return false;
        }

        /// <summary>A script's peace as mission faction names.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Of(ScenarioDef def)
        {
            if (def == null) yield break;
            foreach (KeyValuePair<ScenarioSide, ScenarioSide> pair in def.Peace)
                yield return new KeyValuePair<string, string>(SideNames.Of(pair.Key), SideNames.Of(pair.Value));
        }
    }
}
