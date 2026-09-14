using System.Collections.Generic;
using System.Linq;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
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
        public const string UnknownGlyph = "flag";
        public const string UnknownFamily = "SECONDARY";

        private readonly struct ContractVisual
        {
            public readonly string Glyph;
            public readonly string Family;

            public ContractVisual(string glyph, string family)
            {
                Glyph = glyph;
                Family = family;
            }
        }

        private static readonly Dictionary<string, ContractVisual> Contracts = new Dictionary<string, ContractVisual>
        {
            { "SECURE THE FRONT", new ContractVisual("flag", "CAPTURE") },
            { "HOLD THE LINE", new ContractVisual("shield", "DEFENSE") },
            { "BREAK ENEMY PRESSURE", new ContractVisual("target", "INTERDICTION") },
            { "AIR INTERCEPT", new ContractVisual("air", "AIR DEFENSE") },
            { "WATCH THE APPROACH", new ContractVisual("eye", "PATROL") },
            { "SILENCE THE RADAR", new ContractVisual("radar", "ELECTRONIC WAR") },
            { "ESTABLISH A BEACHHEAD", new ContractVisual("person", "INSERTION") },
            { "ROOFTOP INSERTION", new ContractVisual("building", "INSERTION") },
            { "BRING THEM HOME", new ContractVisual("person", "COMBAT RESCUE") },
            { "RECONNAISSANCE PASS", new ContractVisual("eye", "RECONNAISSANCE") },
            { "CONFIRM THE STRIKE", new ContractVisual("target", "BATTLE DAMAGE") },
            { "COVER THE SUPPLY RUN", new ContractVisual("convoy", "LOGISTICS") },
            { "CUT THE SUPPLY LINE", new ContractVisual("convoy", "LOGISTICS") },
            { "COVER THE ENGINEERS", new ContractVisual("repair", "ENGINEERING") },
            { "HUNT THE JAMMER", new ContractVisual("radar", "ELECTRONIC WAR") },
            { "BRING BACK THE INTELLIGENCE", new ContractVisual("eye", "RECONNAISSANCE") },
            { "SURVEY THE AFTERMATH", new ContractVisual("eye", "RECONNAISSANCE") },
        };

        public static string ContractGlyph(string title) =>
            title != null && Contracts.TryGetValue(title, out ContractVisual visual) ? visual.Glyph : UnknownGlyph;

        public static string ContractFamily(string title) =>
            title != null && Contracts.TryGetValue(title, out ContractVisual visual) ? visual.Family : UnknownFamily;

        /// <summary>Maps a vanilla <c>ObjectiveType</c> name to its glyph shape.</summary>
        public static string ObjectiveGlyph(string typeName)
        {
            switch (typeName)
            {
                case "DestroyUnits": return "target";
                case "CrashAircraft": return "air";
                case "CaptureAirbase": return "flag";
                case "ReachUnits": return "nav";
                case "ReachWaypoints": return "nav";
                case "SuccessfulSortie": return "nav";
                case "WaitSeconds": return "gauge";
                case "CompleteOtherObjective": return "flag";
                case "SpotUnit": return "eye";
                default: return UnknownGlyph;
            }
        }

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
            return string.Join("   ·   ",
                order.OrderByDescending(label => counts[label]).Select(label => label + " " + counts[label]));
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
