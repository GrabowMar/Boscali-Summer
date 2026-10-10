using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum PitOrder : byte { Both = 0, Fuel = 1, Munitions = 2 }

    internal enum PitJobKind : byte { None = 0, Fuel = 1, Handoff = 2, Cassette = 3 }

    /// <summary>Bit flags the host publishes with the SKYWELL view.</summary>
    internal static class PitFlags
    {
        public const ushort HotBreak = 1;
        public const ushort Nothing = 2;
        public const ushort FuelStockOut = 4;
        public const ushort MunitionStockOut = 8;  // the magazine is empty
        public const ushort Capped = 16;           // the plan hit the time / job cap and dropped jobs: fly the zone again for the rest
        public const ushort ZoneLost = 32;         // the receiver left the service zone (3 s after the tender let go)
        public const ushort OverSpeed = 64;        // the tanker is faster than the tether limit
        public const ushort Refused = 128;         // a deploy request was refused (too low or too fast)
        public const ushort AutoReel = 256;        // the winch is reeling the tender in by itself (too fast or too low)
    }

    /// <summary>Why the server let a receiver go (published with the kit view, so the receiver's own HUD can say it).</summary>
    internal static class PitRelease
    {
        public const byte None = 0;
        public const byte Served = 1;
        public const byte ZoneLost = 2;
        public const byte HotBreak = 3;
        public const byte Aborted = 4;   // timed out, left the sky, kit stowed, receiver gone
    }

    /// <summary>One weapon station of the receiver as the planner sees it (live ammo, no game types).</summary>
    internal struct PitStation
    {
        public int Index, Missing, Full;
        public float MassPerRound, CostPerRound;
        public bool Skip, Belt;
    }

    internal struct PitJob
    {
        public PitJobKind Kind;
        public int Station, Rounds;
        public float Seconds;
        public float Commit; // seconds after the job starts when it is committed to the receiver
    }

    /// <summary>
    /// SKYWELL pit-stop planner: which jobs a receiver in the zone gets, in what order, and how long each takes. Fuel is the tanker's
    /// stock; munitions come one hand-off at a time from the tender's 6-round magazine (a belt station swaps as one cassette, which
    /// also takes one slot). Pure and deterministic; the service re-plans from live state at every job boundary.
    /// </summary>
    internal static class PitPlan
    {
        public const float FuelFlowKgS = 400f, FuelPlug = 0.9f, FuelUnplug = 0.6f, FuelMinFlow = 1.2f, FuelMaxFlow = 12f, FuelFirstBelow = 0.35f;
        public const float HandoffSeconds = SkywellHandoff.Total;    // 2.5: lift 0.8, latch 0.4, release + lower 0.8, carousel index 0.5
        public const float TravelSeconds = 1.3f;   // the tender flies to another station / the receptacle: lower the arm, fly a few metres, settle
        public const float MaxSeconds = 30f, HotBreakTti = 7f, HotBreakClosing = 20f, FuelNeedFloorKg = 50f;
        public const int MaxJobs = 8;

        private static readonly List<PitStation> candidates = new List<PitStation>(16);
        private static readonly List<PitJob> munitions = new List<PitJob>(16);

        public static float FuelSeconds(float needKg) =>
            FuelPlug + Mathf.Clamp(needKg / FuelFlowKgS, FuelMinFlow, FuelMaxFlow) + FuelUnplug;

        public static bool FuelFirst(float fuelFraction) => fuelFraction < FuelFirstBelow;

        /// <summary>Rounds a station can still be paid for (players pay per round; AI receivers are free), charged to <paramref name="funds"/>.</summary>
        public static int Afford(PitStation s, int want, ref float funds, bool free)
        {
            if (s.Skip || want <= 0 || s.MassPerRound <= 0f) return 0;
            int byFunds = free || s.CostPerRound <= 0f ? int.MaxValue : Mathf.FloorToInt(funds / s.CostPerRound);
            int rounds = Mathf.Max(0, Mathf.Min(want, byFunds));
            if (!free) funds -= rounds * s.CostPerRound;
            return rounds;
        }

        /// <summary>Priority station first, then the emptiest, then the lowest index.</summary>
        private static int Compare(PitStation a, PitStation b, int priority)
        {
            bool pa = a.Index == priority, pb = b.Index == priority;
            if (pa != pb) return pa ? -1 : 1;
            long loadedA = (long)(a.Full - a.Missing) * b.Full, loadedB = (long)(b.Full - b.Missing) * a.Full; // cross-multiplied fractions
            if (a.Full > 0 && b.Full > 0 && loadedA != loadedB) return loadedA < loadedB ? -1 : 1;
            return a.Index.CompareTo(b.Index);
        }

        /// <summary>Fills <paramref name="into"/> with the job list for this order and returns its length.</summary>
        public static int Plan(PitOrder order, float fuelNeedKg, float fuelFraction, float fuelStockKg, IList<PitStation> stations,
            int priority, int magazineRounds, float funds, bool free, List<PitJob> into) =>
            Plan(order, fuelNeedKg, fuelFraction, fuelStockKg, stations, priority, magazineRounds, funds, free, into, out _);

        /// <param name="capped">True when the time / job cap dropped jobs (the pilot flies the zone again for them).</param>
        public static int Plan(PitOrder order, float fuelNeedKg, float fuelFraction, float fuelStockKg, IList<PitStation> stations,
            int priority, int magazineRounds, float funds, bool free, List<PitJob> into, out bool capped)
        {
            into.Clear();
            bool wantFuel = order != PitOrder.Munitions && fuelStockKg >= 1f && fuelNeedKg >= FuelNeedFloorKg;
            float fuelSeconds = FuelSeconds(Mathf.Min(fuelNeedKg, fuelStockKg));
            var fuel = new PitJob { Kind = PitJobKind.Fuel, Station = -1, Seconds = fuelSeconds, Commit = fuelSeconds - FuelUnplug };

            munitions.Clear();
            if (order != PitOrder.Fuel)
            {
                candidates.Clear();
                for (int i = 0; i < stations.Count; i++)
                {
                    PitStation s = stations[i];
                    if (s.Skip || s.Missing <= 0 || s.MassPerRound <= 0f) continue;
                    int at = candidates.Count; // insertion sort: a handful of stations
                    while (at > 0 && Compare(s, candidates[at - 1], priority) < 0) at--;
                    candidates.Insert(at, s);
                }
                int slots = Mathf.Clamp(magazineRounds, 0, SkywellMagazine.Rounds);
                float fundsLeft = funds;
                foreach (PitStation s in candidates)
                {
                    if (slots <= 0) break;
                    if (s.Belt)
                    {
                        int bullets = Afford(s, s.Missing, ref fundsLeft, free);
                        if (bullets <= 0) continue;
                        munitions.Add(new PitJob { Kind = PitJobKind.Cassette, Station = s.Index, Rounds = bullets, Seconds = HandoffSeconds, Commit = SkywellHandoff.CommitAt });
                        slots--;
                        continue;
                    }
                    int take = Afford(s, Mathf.Min(s.Missing, slots), ref fundsLeft, free);
                    for (int k = 0; k < take; k++)
                        munitions.Add(new PitJob { Kind = PitJobKind.Handoff, Station = s.Index, Rounds = 1, Seconds = HandoffSeconds, Commit = SkywellHandoff.CommitAt });
                    slots -= take;
                }
            }

            bool fuelFirst = order == PitOrder.Both && FuelFirst(fuelFraction);
            if (wantFuel && fuelFirst) into.Add(fuel);
            into.AddRange(munitions);
            if (wantFuel && !fuelFirst) into.Add(fuel);
            // The tender is already in place for the first job of a stop (it flew straight to it); every later job at another station,
            // or at the receptacle, starts with a short flight the job has to leave time for.
            for (int i = 1; i < into.Count; i++)
            {
                PitJob previous = into[i - 1], job = into[i];
                bool moves = job.Kind == PitJobKind.Fuel ? previous.Kind != PitJobKind.Fuel : previous.Kind == PitJobKind.Fuel || previous.Station != job.Station;
                if (!moves) continue;
                job.Seconds += TravelSeconds;
                job.Commit += TravelSeconds;
                into[i] = job;
            }

            float total = 0f;
            int keep = 0;
            while (keep < into.Count && keep < MaxJobs && total + into[keep].Seconds <= MaxSeconds) total += into[keep++].Seconds;
            capped = keep < into.Count;
            into.RemoveRange(keep, into.Count - keep);
            return into.Count;
        }

        public static float Total(IList<PitJob> jobs, int from = 0)
        {
            float total = 0f;
            for (int i = from; i < jobs.Count; i++) total += jobs[i].Seconds;
            return total;
        }

        /// <summary>Seconds until the missile reaches the host at its current closing speed; +inf when it is not closing fast.</summary>
        public static float TimeToImpact(Vector3 hostPos, Vector3 hostVel, Vector3 mslPos, Vector3 mslVel)
        {
            Vector3 rel = mslPos - hostPos;
            float range = rel.magnitude;
            if (range < 1e-3f) return 0f;
            float closing = -Vector3.Dot(mslVel - hostVel, rel) / range;
            return closing <= HotBreakClosing ? float.PositiveInfinity : range / closing;
        }
    }

    /// <summary>HUD wording that is worth pinning in a test.</summary>
    internal static class PitCopy
    {
        /// <summary>"[##>--]" for done jobs out of count, five cells.</summary>
        public static string Pips(int done, int count)
        {
            const int cells = 5;
            int filled = count <= 0 ? 0 : Mathf.Clamp(done * cells / count, 0, cells);
            var s = new System.Text.StringBuilder("[", cells + 2);
            for (int i = 0; i < cells; i++) s.Append(i < filled ? '#' : i == filled ? '>' : '-');
            return s.Append(']').ToString();
        }

        public static string OrderLabel(PitOrder order) => order == PitOrder.Fuel ? "FUEL ONLY" : order == PitOrder.Munitions ? "MUNITIONS ONLY" : "BOTH";

        public static string Kg(float kg) => (Mathf.Round(kg / 10f) * 10f).ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>"RDS 4/6": rounds left in the tender's magazine.</summary>
        public static string Rounds(int left) => "RDS " + Mathf.Clamp(left, 0, SkywellMagazine.Rounds) + "/" + SkywellMagazine.Rounds;
    }
}
