using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Trenches.Domain
{
    /// <summary>
    /// Pure wire contract for trench replication: protocol version, trace caps and the
    /// validators both the reader and the receiver apply. Unity-free so tests can compile it.
    /// </summary>
    internal static class TrenchWire
    {
        public const byte ProtocolVersion = 1;
        public const int MaximumStations = TrenchTraceMath.MaximumStations;
        public const int MaximumLinkStations = 8;
        public const int MaximumSpurStations = 8;
        public const int MaximumDefenders = 8;

        /// <summary>Faction identity on the wire: hashed name, never a peer-local reference.</summary>
        public static int OwnerHashFor(string factionName)
        {
            int hash = unchecked((int)Deterministic.HashString(factionName));
            return hash == 0 ? 1 : hash;
        }

        public static bool ValidStage(int stage) =>
            stage >= (int)TrenchStage.Scrape && stage <= (int)TrenchStage.Saps;

        /// <summary>Trace counts a geometry message may carry: planner bounds, no trust.</summary>
        public static bool ValidTraceCounts(int curve, int threat, int support, int redoubt,
            int links, int spurs)
        {
            if (curve < 2 || curve > MaximumStations) return false;
            if (threat != curve) return false;
            if (support < 0 || support > MaximumStations) return false;
            if (redoubt < 0 || redoubt > MaximumStations) return false;
            if (links < 0 || links > TrenchTraceMath.MaximumLinkTraces) return false;
            if (spurs < 0 || spurs > TrenchTraceMath.MaximumSpurTraces) return false;
            return true;
        }

        public static bool ValidLinkStations(int stations) =>
            stations >= 0 && stations <= MaximumLinkStations;

        public static bool ValidSpurStations(int stations) =>
            stations >= 0 && stations <= MaximumSpurStations;

        public static bool ValidDefenders(int defenders) =>
            defenders >= 0 && defenders <= MaximumDefenders;

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
