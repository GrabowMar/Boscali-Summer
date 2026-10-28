using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Presentation vocabulary for the MIS board: vanilla objective types and the
    /// dynamic-operations contract titles, each mapped to a glyph shape and a readable
    /// family label.
    ///
    /// This mapping lives in Command because the glyph language is Command's. The contract
    /// view carries only the player-facing title, so a title the dynamic-operations pool
    /// adds later keeps the generic flag and SECONDARY family instead of breaking the panel.
    /// </summary>
    internal static class MfdMissionLabels
    {

        /// <summary>
        /// Counts objectives by readable tag, most common first, ties in first-seen order so
        /// a live board never reshuffles itself between refreshes. Null or empty input has no
        /// summary to show.
        /// </summary>
        public static string TypeSummary(IEnumerable<string> typeNames)
        {
            if (typeNames == null) return "";
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (string name in typeNames)
            {
                string label = ObjectiveTypeLabel(name);
                if (!counts.TryGetValue(label, out int count)) order.Add(label);
                counts[label] = count + 1;
            }
            if (order.Count == 0) return "";
            // Stable insertion sort, most common first: ties keep first-seen order, so a
            // live board never reshuffles itself between refreshes. No LINQ on the path.
            for (int i = 1; i < order.Count; i++)
            {
                string candidate = order[i];
                int weight = counts[candidate];
                int at = i - 1;
                while (at >= 0 && counts[order[at]] < weight)
                {
                    order[at + 1] = order[at];
                    at--;
                }
                order[at + 1] = candidate;
            }
            var text = new StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) text.Append("   ·   ");
                text.Append(order[i]).Append(' ').Append(counts[order[i]]);
            }
            return text.ToString();
        }

        /// <summary>Maps a vanilla <c>ObjectiveType</c> name to a short readable tag.</summary>
        public static string ObjectiveTypeLabel(string typeName)
        {
            switch (typeName)
            {
                case "DestroyUnits": return "DESTROY";
                case "CrashAircraft": return "AIR LOSS";
                case "CaptureAirbase": return "CAPTURE";
                case "ReachUnits": return "REACH";
                case "ReachWaypoints": return "REACH";
                case "SuccessfulSortie": return "SORTIE";
                case "WaitSeconds": return "HOLD";
                case "CompleteOtherObjective": return "LINKED";
                case "DialogueBox": return "BRIEF";
                case "SpotUnit": return "SURVEIL";
                default: return "OBJECTIVE";
            }
        }
    }
}
