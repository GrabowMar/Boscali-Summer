using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>FORMATION's Station Board numbers (spec 2026-10-04 §4.2) from what a snapshot carries: how many members are in their
    /// slot, the RMS of their slot errors, the share in station and the closest pair. Arrays and counts, so the host and a client
    /// feed it the same way and nothing allocates.</summary>
    internal static class StationBoard
    {
        /// <summary>Members whose phase byte reads <see cref="StationPhase.InSlot"/>.</summary>
        public static int InSlot(byte[] phase, int n)
        {
            int k = 0;
            for (int i = 0; i < n && i < phase.Length; i++)
                if (phase[i] == (byte)StationPhase.InSlot) k++;
            return k;
        }

        /// <summary>Root mean square of the slot errors, metres (the snapshot's 10 m steps); -1 for nobody.</summary>
        public static float Rms(byte[] err10, int n)
        {
            if (n <= 0) return -1f;
            double sum = 0;
            int c = 0;
            for (int i = 0; i < n && i < err10.Length; i++)
            {
                double m = StationMath.Error(err10[i]);
                sum += m * m;
                c++;
            }
            return c == 0 ? -1f : (float)Math.Sqrt(sum / c);
        }

        /// <summary>Whole percent of <paramref name="n"/> that is in slot (0 for nobody).</summary>
        public static int Percent(int inSlot, int n) => n <= 0 ? 0 : (int)Math.Round(100.0 * inSlot / n);

        /// <summary>The closest distance between any two of the points (x east, z north), metres; -1 with fewer than two.</summary>
        public static float MinSeparation(float[] x, float[] z, int n)
        {
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float dx = x[i] - x[j], dz = z[i] - z[j];
                    best = Math.Min(best, (float)Math.Sqrt(dx * dx + dz * dz));
                }
            return best == float.MaxValue ? -1f : best;
        }

        /// <summary>"+4", "-1", "0": closure in whole m/s (+ = nearing the slot).</summary>
        public static string Signed(int v) => v > 0 ? "+" + v.ToString(CultureInfo.InvariantCulture)
            : v < 0 ? "-" + (-v).ToString(CultureInfo.InvariantCulture) : "0";

        /// <summary>Slot error as words: "4 m", "310 m", "1.2 km" past a kilometre.</summary>
        public static string ErrorText(float metres) => metres >= 1000f
            ? (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km"
            : ((int)Math.Round(metres)).ToString(CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>ORDERS' stance slots (spec 2026-10-04 §4.1): which slot the scope's live axes match, how far off it is, and saving
    /// the live axes as a new stance in the next free slot.</summary>
    internal static class StanceMatch
    {
        /// <summary>The assigned slot whose stance differs least from <paramref name="live"/> (an exact match wins; ties go to the
        /// lower slot), or -1 when no slot is assigned. <paramref name="mask"/> is the difference (<see cref="StanceDiff"/>).</summary>
        public static int Closest(StanceBook book, byte[] live, out int mask)
        {
            mask = 0;
            int best = -1, bestCount = int.MaxValue;
            for (int i = 0; i < StanceBook.Slots; i++)
            {
                Stance s = book.Slot(i);
                if (s == null) continue;
                int m = StanceDiff.Mask(live, s.Axes), c = StanceDiff.Count(m);
                if (c >= bestCount) continue;
                best = i;
                bestCount = c;
                mask = m;
            }
            return best;
        }

        /// <summary>The first slot with no stance, or -1 when all six are taken.</summary>
        public static int NextFreeSlot(StanceBook book)
        {
            for (int i = 0; i < StanceBook.Slots; i++)
                if (book.Slot(i) == null) return i;
            return -1;
        }

        /// <summary>A user stance from <paramref name="live"/> in the next free slot ("CUSTOM 1", "CUSTOM 2"...); null when the
        /// slots or the book are full or the axes are not 8 long.</summary>
        public static Stance SaveAs(StanceBook book, byte[] live)
        {
            if (live == null || live.Length != Stance.AxisCount) return null;
            int slot = NextFreeSlot(book);
            if (slot < 0 || book.All.Count >= StanceBook.MaxStances) return null;
            for (int n = 1; n <= StanceBook.MaxStances + 1; n++)
            {
                string id = "custom-" + n.ToString(CultureInfo.InvariantCulture);
                if (book.Find(id) != null) continue;
                var s = new Stance { Id = id, Name = "CUSTOM " + n.ToString(CultureInfo.InvariantCulture), Axes = (byte[])live.Clone() };
                if (!book.Add(s)) return null;
                book.Assign(slot, id);
                return s;
            }
            return null;
        }
    }
}
