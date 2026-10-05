using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>The words of BEHAVIOUR › STANCES (spec 2026-10-04 §4.1): the plain-language summary under a stance's name, the short
    /// words of its axes, and the stance an element is closest to. Axis order is <c>DoctrineAxis</c>: Guard, Response, Interval, Spread,
    /// Targets, Reach, Weapons, Radar. Fall back / winchester / bingo are the stance's wing-wide follow-ons (-1 = leave as is).</summary>
    internal static class StanceWords
    {
        public const int Guard = 0, Response = 1, Interval = 2, Spread = 3, Targets = 4, Reach = 5, Weapons = 6, Radar = 7;

        private static readonly string[] Names = { "MSL GUARD", "RESPONSE", "INTERVAL", "SPREAD", "TARGETS", "RANGE", "WEAPONS", "RADAR" };
        private static readonly string[][] Short =
        {
            new[] { "OFF", "SELF", "WING", "LEAD" }, new[] { "BREAK", "PRESS" }, new[] { "CLOSE", "STD", "OPEN" }, new[] { "OFF", "ON" },
            new[] { "HOLD", "AIR", "GND", "BOTH", "COVER" }, new[] { "6 KM", "12 KM" }, new[] { "AUTO", "MSL", "GUN", "NO A-G" },
            new[] { "ON", "SILENT", "OFF" },
        };
        private static readonly string[] FallBackWords = { "never", "1.5 : 1", "2 : 1", "3 : 1" };

        public static string AxisName(int axis) => axis >= 0 && axis < Names.Length ? Names[axis] : "?";

        /// <summary>"SILENT", "12 KM": axis <paramref name="axis"/>'s value in a chip or a row; "?" when out of range.</summary>
        public static string AxisWord(int axis, int value) =>
            axis >= 0 && axis < Short.Length && value >= 0 && value < Short[axis].Length ? Short[axis][value] : "?";

        /// <summary>"AIR · 12 KM · SILENT": the three axes a stance is told apart by, for its list line.</summary>
        public static string Brief(byte[] axes)
        {
            if (axes == null || axes.Length < 8) return "";
            return AxisWord(Targets, axes[Targets]) + " · " + AxisWord(Reach, axes[Reach]) + " · " + AxisWord(Radar, axes[Radar]);
        }

        /// <summary>The summary sentence: what the wing does and how it behaves, in the words a pilot would use.</summary>
        public static string Says(byte[] axes, int fallBack, int winchester, int bingo)
        {
            if (axes == null || axes.Length < 8) return "";
            string reach = axes[Reach] == 1 ? "12 km" : "6 km";
            var sb = new StringBuilder();
            switch (axes[Targets])
            {
                case 0: sb.Append("Hold fire unless ordered"); break;
                case 1: sb.Append("Hunt enemy aircraft out to ").Append(reach); break;
                case 2: sb.Append("Hunt ground targets out to ").Append(reach); break;
                case 3: sb.Append("Hunt air and ground targets out to ").Append(reach); break;
                case 4: sb.Append("Cover the protected aircraft against the nearest air threat"); break;
                default: sb.Append("Fly the standing orders"); break;
            }
            if (axes[Targets] != 0)
                switch (axes[Weapons])
                {
                    case 1: sb.Append(", missiles only"); break;
                    case 2: sb.Append(", guns only"); break;
                    case 3: sb.Append(", air targets only (no bombs)"); break;
                }
            switch (axes[Radar])
            {
                case 0: sb.Append(", radar on"); break;
                case 1: sb.Append(", radar silent until we engage"); break;
                default: sb.Append(", radar off"); break;
            }
            sb.Append('.');
            if (axes[Guard] == 0) sb.Append(" No missile guard.");
            else sb.Append(" Guard ").Append(axes[Guard] == 1 ? "yourself" : axes[Guard] == 2 ? "the wing" : "the lead").Append(" against missiles.");
            sb.Append(axes[Response] == 1 ? " Press on against missiles." : " Break from missiles.");
            if (axes[Spread] == 1) sb.Append(" Spread when threatened.");
            sb.Append(axes[Interval] == 0 ? " Fly close." : axes[Interval] == 2 ? " Fly open." : " Fly standard spacing.");
            if (fallBack >= 0 && fallBack < FallBackWords.Length)
                sb.Append(fallBack == 0 ? " Never fall back." : " Fall back at " + FallBackWords[fallBack] + ".");
            if (winchester == 0) sb.Append(" Rejoin at winchester.");
            else if (winchester == 1) sb.Append(" RTB at winchester.");
            else if (winchester == 2) sb.Append(" Refit at winchester.");
            if (bingo == 0) sb.Append(" RTB at bingo.");
            else if (bingo == 1) sb.Append(" Refit at bingo.");
            return sb.ToString();
        }

        /// <summary>The stance whose axes differ least from <paramref name="live"/> (the first on a tie), with the differing axes as a
        /// <see cref="StanceDiff"/> mask; null when there are no stances.</summary>
        public static Stance Closest(IReadOnlyList<Stance> all, byte[] live, out int mask)
        {
            mask = 0;
            Stance best = null;
            int bestCount = int.MaxValue;
            if (all == null) return null;
            foreach (Stance s in all)
            {
                int m = StanceDiff.Mask(live, s.Axes);
                int n = StanceDiff.Count(m);
                if (n >= bestCount) continue;
                best = s;
                bestCount = n;
                mask = m;
            }
            return best;
        }

        /// <summary>"RADAR ON · SPREAD OFF": the axes of <paramref name="mask"/> as <paramref name="live"/> flies them.</summary>
        public static string ChangedWords(int mask, byte[] live)
        {
            if (live == null || live.Length < 8) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                if (!StanceDiff.Changed(mask, i)) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(Names[i]).Append(' ').Append(AxisWord(i, live[i]));
            }
            return sb.ToString();
        }
    }
}
