using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The words of the STRIKE and OBJECTIVE pages, pure and allocation-free after warm-up: the strike names, the phase words, the perk and node
    /// words, a clock, the payout. Every string is a constant or cached on first use.
    /// </summary>
    internal static class OpsScopeWords
    {
        private static readonly string[] Names = { "KINETIC ROD", "PRSM STRIKE", "CRUISE SALVO", "EMP STRIKE", "DECOY BARRAGE" };
        private static readonly string[] Shorts = { "ROD", "PRSM", "CRUISE", "EMP", "DECOY" };
        private static readonly string[] Nodes = { "RDR", "C2", "RLY", "UPL", "DC" };
        private static readonly string[] Clocks = new string[3600];
        private static readonly string[,] Legs = new string[8, 8];

        public static string Name(StrikeKind kind) => Names[(int)kind];

        public static string Short(StrikeKind kind) => Shorts[(int)kind];

        /// <summary>The CYBER node's short word on the chain: RDR, C2, RLY, UPL, DC.</summary>
        public static string Node(ChainNodeKind kind) => Nodes[(int)kind];

        private static readonly string[] Hops = { "HOP RDR ", "HOP C2 ", "HOP RLY ", "HOP UPL ", "HOP DC " };
        private static readonly string[] Helds = { "HELD RDR", "HELD C2", "HELD RLY", "HELD UPL", "HELD DC" };

        /// <summary>The words before the hop's seconds ("HOP RDR ") and the word while a node is held ("HELD RDR").</summary>
        public static string Hop(ChainNodeKind kind) => Hops[(int)kind];

        public static string Held(ChainNodeKind kind) => Helds[(int)kind];

        /// <summary>The word under a track's countdown: WARN, FALL, FLIGHT, LEG n/N, IMPACT (SPLASH for PRSM), ACTIVE, DONE.</summary>
        public static string Phase(in StrikeTrack t, StrikePhase phase)
        {
            switch (phase)
            {
                case StrikePhase.Warn: return "WARN";
                case StrikePhase.Fall: return "FALL";
                case StrikePhase.Impact: return t.Kind == StrikeKind.Prsm ? "SPLASH" : "IMPACT";
                case StrikePhase.Effect: return "ACTIVE";
                case StrikePhase.Done: return "DONE";
            }
            if (t.Kind != StrikeKind.Cruise || t.Legs <= 0) return "FLIGHT";
            int leg = StrikeMath.Leg(t), count = StrikeMath.LegCount(t);
            if (leg >= Legs.GetLength(0) || count >= Legs.GetLength(1)) return "FLIGHT";
            return Legs[leg, count] ?? (Legs[leg, count] = "LEG " + leg + "/" + count);
        }

        /// <summary>"4:51" for a number of seconds (rounded up, negative reads 0:00, capped at 59:59).</summary>
        public static string Clock(float seconds)
        {
            int s = float.IsNaN(seconds) ? 0 : (int)Math.Ceiling(Math.Max(0f, Math.Min(3599f, seconds)));
            return Clocks[s] ?? (Clocks[s] = (s / 60) + ":" + (s % 60 < 10 ? "0" : "") + (s % 60));
        }

        /// <summary>A perk's right-hand word: RDY, ARMED or the cooldown clock.</summary>
        public static string Perk(in PerkTile p) => p.State == PerkPhase.Ready ? "RDY" : p.State == PerkPhase.Armed ? "ARMED" : Clock(p.Cooldown);

        /// <summary>"$14.2K" / "$830" for a payout in credits.</summary>
        public static string Money(int credits)
        {
            if (credits < 1000) return "$" + Math.Max(0, credits);
            float k = credits / 1000f;
            return "$" + (k < 100f ? k.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : ((int)k).ToString(System.Globalization.CultureInfo.InvariantCulture)) + "K";
        }

        /// <summary>"x1.15": the world's contract multiplier, nothing when it is not reported or exactly 1.</summary>
        public static string Multiplier(float multiplier) =>
            multiplier <= 0f || Math.Abs(multiplier - 1f) < 0.005f ? null : "x" + multiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
