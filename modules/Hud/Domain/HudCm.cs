using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>One countermeasure station (flares, chaff, a jammer) as the board's CM tiles show it.</summary>
    internal struct CmStation
    {
        /// <summary>IR, RDR, JAM or the first letters of its name.</summary>
        public string Tag;
        public int Ammo, Max;
        /// <summary>A jammer: it charges instead of counting rounds.</summary>
        public bool Chargeable;
        /// <summary>The threat types it counters (<see cref="HudCm.IrMask"/> | <see cref="HudCm.RadarMask"/>).</summary>
        public byte Mask;
        /// <summary>A jammer only: its capacitor charge 0..1 and whether it is jamming now.</summary>
        public float Charge;
        public bool Active;
        /// <summary>The dispenser the pilot has selected (the game's active countermeasure).</summary>
        public bool Selected;
        /// <summary>The countermeasure's own picture as a handle (0: none; see <see cref="StoreStation.Icon"/>).</summary>
        public int Icon;
    }

    internal enum CmLevel : byte { Ok, Low, Empty }

    /// <summary>
    /// Countermeasure rules for the board (2026-10-10), pure: the tag a station wears, how low is low, and which station
    /// counters which inbound seeker. The board only reads and displays; it never selects a countermeasure (the game's
    /// <c>ChooseCountermeasure</c> would change the pilot's active one).
    /// </summary>
    internal static class HudCm
    {
        public const int MaxStations = 4;
        /// <summary>At or below this share of its full load a station is LOW.</summary>
        public const float LowShare = 0.25f;
        public const byte IrMask = 1, RadarMask = 2;

        /// <summary>The mask for a station's <c>GetThreatTypes()</c>: IR flares counter "IR", chaff and jammers "ARH" and "SARH".</summary>
        public static byte MaskOf(bool ir, bool radar) => (byte)((ir ? IrMask : 0) | (radar ? RadarMask : 0));

        /// <summary>The seeker types a countermeasure class answers: IR seekers fall to flares, radar seekers to chaff and jammers; the rest to nothing.</summary>
        public static byte SeekerMask(string seeker) =>
            seeker == "IR" ? IrMask : seeker == "ARH" || seeker == "SARH" ? RadarMask : (byte)0;

        public static string TagOf(byte mask, bool chargeable, string name)
        {
            if ((mask & IrMask) != 0) return "IR";
            if ((mask & RadarMask) != 0) return chargeable ? "JAM" : "RDR";
            if (string.IsNullOrEmpty(name)) return "CM";
            string upper = name.Trim().ToUpperInvariant();
            return upper.Length <= 3 ? upper : upper.Substring(0, 3);
        }

        /// <summary>
        /// Where the station named <paramref name="name"/> stands in the game's Next Countermeasure cycle (<paramref name="cycle"/>, its first <paramref name="known"/> names): its place, or after every listed
        /// one (the mod's chaff is not in the game's list and is the cycle's last stop).
        /// </summary>
        public static int CycleRank(string name, string[] cycle, int known)
        {
            for (int i = 0; i < known; i++) if (cycle[i] == name) return i;
            return known;
        }

        /// <summary>
        /// Puts the first <paramref name="count"/> station slots in cycle order, stably (ties keep their order): <paramref name="order"/>[k] is the old slot that goes to place k.
        /// </summary>
        public static void CycleOrder(int[] rank, int count, int[] order)
        {
            for (int i = 0; i < count; i++) order[i] = i;
            for (int a = 1; a < count; a++)
                for (int b = a; b > 0 && rank[order[b]] < rank[order[b - 1]]; b--) (order[b], order[b - 1]) = (order[b - 1], order[b]);
        }

        public static CmLevel Level(in CmStation s)
        {
            if (s.Chargeable) return CmLevel.Ok;
            if (s.Ammo <= 0) return CmLevel.Empty;
            return s.Max > 0 && s.Ammo <= (int)Math.Ceiling(s.Max * LowShare) ? CmLevel.Low : CmLevel.Ok;
        }

        /// <summary>The short name on an EXPENDABLES row: FLR for flares, CHF for chaff, else the tag (JAM, or the first letters of the name).</summary>
        public static string Word(in CmStation s) => s.Tag == "IR" ? "FLR" : s.Tag == "RDR" ? "CHF" : s.Tag ?? "CM";
    }
}
