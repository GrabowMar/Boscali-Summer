using System;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    internal enum ThreatKind : byte { None, Spike, Lock, Missile }

    /// <summary>
    /// One threat as the board's detail rows show it. Names and the seeker are the game's own strings; a number the game
    /// cannot give is NaN. Pure value type, no UnityEngine.
    /// </summary>
    internal struct ThreatContact
    {
        public ThreatKind Kind;
        /// <summary>Emitter unit name (lock, spike); the weapon name for a missile; empty when unknown.</summary>
        public string Name;
        /// <summary>A missile's seeker as <c>Missile.GetSeekerType()</c> spells it: IR, ARH, SARH, ARAD, Laser, Optical, INS.</summary>
        public string Seeker;
        /// <summary>The missile's seeker has locked on to us (<c>seekerMode</c> activeLock) rather than still searching.</summary>
        public bool Locked;
        public float RangeM, ClosureMps, TtiS;
        /// <summary>Seconds since this contact first appeared.</summary>
        public float AgeS;
        /// <summary>Where it is relative to our nose, for the dial: degrees, + right, and elevation.</summary>
        public float BearingDeg, ElevationDeg;
    }

    /// <summary>
    /// Ranking and time-to-impact for the threat detail rows (2026-10-10), pure: inbound missiles first by how soon they
    /// arrive, then radar locks, then search-radar spikes by range. Allocation-free; fills a caller-owned array.
    /// </summary>
    internal static class ThreatRank
    {
        /// <summary>Detail lines the board shows.</summary>
        public const int MaxRows = 2;
        /// <summary>Closing slower than this has no meaningful time to impact.</summary>
        public const float MinClosureMps = 15f;
        /// <summary>A time to impact beyond this is not shown.</summary>
        public const float MaxTtiS = 99f;

        /// <summary>Seconds until the missile arrives at the current closure; NaN when it is not closing or the data is missing.</summary>
        public static float Tti(float rangeM, float closureMps)
        {
            if (float.IsNaN(rangeM) || float.IsNaN(closureMps) || closureMps < MinClosureMps || rangeM < 0f) return float.NaN;
            float t = rangeM / closureMps;
            return t > MaxTtiS ? float.NaN : t;
        }

        /// <summary>Negative when <paramref name="a"/> outranks <paramref name="b"/>.</summary>
        public static int Compare(in ThreatContact a, in ThreatContact b)
        {
            if (a.Kind != b.Kind) return a.Kind > b.Kind ? -1 : 1;
            if (a.Kind == ThreatKind.Missile)
            {
                bool ta = !float.IsNaN(a.TtiS), tb = !float.IsNaN(b.TtiS);
                if (ta != tb) return ta ? -1 : 1;
                if (ta && a.TtiS != b.TtiS) return a.TtiS < b.TtiS ? -1 : 1;
                if (a.Locked != b.Locked) return a.Locked ? -1 : 1;
            }
            bool ra = !float.IsNaN(a.RangeM), rb = !float.IsNaN(b.RangeM);
            if (ra != rb) return ra ? -1 : 1;
            if (ra && a.RangeM != b.RangeM) return a.RangeM < b.RangeM ? -1 : 1;
            return a.AgeS < b.AgeS ? -1 : a.AgeS > b.AgeS ? 1 : 0;
        }

        /// <summary>
        /// Insert <paramref name="c"/> into <paramref name="rows"/> (sorted best first, <paramref name="count"/> filled) and
        /// return the new count. The array's length is the capacity; a contact that ranks below a full array is dropped.
        /// </summary>
        public static int Insert(ThreatContact[] rows, int count, in ThreatContact c)
        {
            int at = 0;
            while (at < count && Compare(rows[at], c) <= 0) at++;
            if (at >= rows.Length) return count;
            int last = Math.Min(count, rows.Length - 1);
            for (int i = last; i > at; i--) rows[i] = rows[i - 1];
            rows[at] = c;
            return Math.Min(rows.Length, count + 1);
        }
    }
}
