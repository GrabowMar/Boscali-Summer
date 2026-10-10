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

        public static bool Counters(in CmStation s, string seeker) => (s.Mask & SeekerMask(seeker)) != 0;

        public static string TagOf(byte mask, bool chargeable, string name)
        {
            if ((mask & IrMask) != 0) return "IR";
            if ((mask & RadarMask) != 0) return chargeable ? "JAM" : "RDR";
            if (string.IsNullOrEmpty(name)) return "CM";
            string upper = name.Trim().ToUpperInvariant();
            return upper.Length <= 3 ? upper : upper.Substring(0, 3);
        }

        public static CmLevel Level(in CmStation s)
        {
            if (s.Chargeable) return CmLevel.Ok;
            if (s.Ammo <= 0) return CmLevel.Empty;
            return s.Max > 0 && s.Ammo <= (int)Math.Ceiling(s.Max * LowShare) ? CmLevel.Low : CmLevel.Ok;
        }

        /// <summary>"IR 72", "RDR 30"; a jammer is just "JAM".</summary>
        public static string Tile(in CmStation s) => s.Chargeable ? s.Tag : s.Tag + " " + s.Ammo;

        /// <summary>The tile's words: the count, then LOW or EMPTY when it is ("RDR 4 LOW"): words alongside the colour.</summary>
        public static string Words(in CmStation s)
        {
            CmLevel level = Level(s);
            return level == CmLevel.Low ? Tile(s) + " LOW" : level == CmLevel.Empty ? Tile(s) + " EMPTY" : Tile(s);
        }
    }
}
