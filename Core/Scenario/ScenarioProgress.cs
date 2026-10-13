using System;
using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Core.Scenario
{
    /// <summary>
    /// What a pilot's campaigns remember between missions: which scripts they finished and the flags a script asked to keep. Stored as text, one fact a line
    /// (<c>done &lt;script&gt;</c>, <c>flag &lt;campaign&gt; &lt;name&gt;</c>); a line that cannot be read is dropped. Pure.
    /// </summary>
    internal sealed class ScenarioProgress
    {
        private readonly HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> flags = new HashSet<string>(StringComparer.Ordinal);

        public static ScenarioProgress Parse(string text)
        {
            var progress = new ScenarioProgress();
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string[] parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0] == "done" && ScenarioDef.IsId(parts[1])) progress.done.Add(parts[1]);
                else if (parts.Length == 3 && parts[0] == "flag" && ScenarioDef.IsId(parts[1]) && ScenarioDef.IsId(parts[2]))
                    progress.flags.Add(parts[1] + " " + parts[2]);
            }
            return progress;
        }

        public string Format()
        {
            var lines = new List<string>(done.Count + flags.Count);
            foreach (string id in done) lines.Add("done " + id);
            foreach (string flag in flags) lines.Add("flag " + flag);
            lines.Sort(StringComparer.Ordinal);
            var text = new StringBuilder();
            foreach (string line in lines) text.Append(line).Append('\n');
            return text.ToString();
        }

        public bool IsDone(string scenario) => scenario != null && done.Contains(scenario);

        /// <summary>True when this was news.</summary>
        public bool MarkDone(string scenario) => ScenarioDef.IsId(scenario) && done.Add(scenario);

        public bool Has(string saga, string flag) => saga != null && flag != null && flags.Contains(saga + " " + flag);

        /// <summary>True when this was news.</summary>
        public bool Set(string saga, string flag) => ScenarioDef.IsId(saga) && ScenarioDef.IsId(flag) && flags.Add(saga + " " + flag);

        /// <summary>
        /// Whether a pilot may start a script: one outside a campaign and a campaign's first chapter always; a later chapter once every earlier chapter of that
        /// campaign among <paramref name="all"/> is finished.
        /// </summary>
        public bool Unlocked(ScenarioDef script, IEnumerable<ScenarioDef> all)
        {
            if (script == null) return false;
            if (string.IsNullOrEmpty(script.Campaign) || script.Chapter <= 1) return true;
            foreach (ScenarioDef other in all)
                if (other.Campaign == script.Campaign && other.Chapter < script.Chapter && !IsDone(other.Id)) return false;
            return true;
        }
    }
}
