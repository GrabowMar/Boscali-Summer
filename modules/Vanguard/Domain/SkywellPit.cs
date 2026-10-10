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
        public const byte HotBreak = 1;
        public const byte Nothing = 2;
        public const byte FuelStockOut = 4;
        public const byte MunitionStockOut = 8;
        public const byte Capped = 16;     // the plan hit the 30 s / 14 job cap and dropped jobs: fly the box again for the rest
        public const byte BoxLost = 32;    // the receiver left the service box (3 s after the glider let go)
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
    }

    /// <summary>
    /// SKYWELL pit-stop planner: which jobs a receiver in the box gets, in what order, and how long each takes.
    /// Pure and deterministic; the service re-plans from live state at every job boundary.
    /// </summary>
    internal static class PitPlan
    {
        public const float FuelFlowKgS = 400f, FuelPlug = 0.9f, FuelUnplug = 0.6f, FuelMinFlow = 1.2f, FuelMaxFlow = 12f;
        public const float FirstHandoff = 1.7f, NextHandoff = 1.5f, CassetteSwap = 2.4f, FuelFirstBelow = 0.35f;
        public const float MaxSeconds = 30f, HotBreakTti = 7f, HotBreakClosing = 20f, FuelNeedFloorKg = 50f;
        public const int MaxJobs = 14;

        private static readonly List<PitStation> candidates = new List<PitStation>(16);
        private static readonly List<PitJob> munitions = new List<PitJob>(32);

        public static float FuelSeconds(float needKg) =>
            FuelPlug + Mathf.Clamp(needKg / FuelFlowKgS, FuelMinFlow, FuelMaxFlow) + FuelUnplug;

        public static float HandoffSeconds(bool firstOfRun, bool belt) => belt ? CassetteSwap : firstOfRun ? FirstHandoff : NextHandoff;

        public static bool FuelFirst(float fuelFraction) => fuelFraction < FuelFirstBelow;

        /// <summary>Rounds a station can still take from the stock and funds (mass- and funds-limited, like the vanilla rearmer).</summary>
        public static int Take(PitStation s, int want, ref float stockKg, ref float funds, bool free)
        {
            if (s.Skip || want <= 0 || s.MassPerRound <= 0f) return 0;
            int byMass = Mathf.FloorToInt(stockKg / s.MassPerRound);
            int byFunds = free || s.CostPerRound <= 0f ? int.MaxValue : Mathf.FloorToInt(funds / s.CostPerRound);
            int rounds = Mathf.Max(0, Mathf.Min(want, Mathf.Min(byMass, byFunds)));
            stockKg -= rounds * s.MassPerRound;
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
            int priority, float munitionStockKg, float funds, bool free, List<PitJob> into) =>
            Plan(order, fuelNeedKg, fuelFraction, fuelStockKg, stations, priority, munitionStockKg, funds, free, into, out _);

        /// <param name="capped">True when the 30 s / 14 job cap dropped jobs (the pilot flies the box again for them).</param>
        public static int Plan(PitOrder order, float fuelNeedKg, float fuelFraction, float fuelStockKg, IList<PitStation> stations,
            int priority, float munitionStockKg, float funds, bool free, List<PitJob> into, out bool capped)
        {
            into.Clear();
            bool wantFuel = order != PitOrder.Munitions && fuelStockKg >= 1f && fuelNeedKg >= FuelNeedFloorKg;
            var fuel = new PitJob { Kind = PitJobKind.Fuel, Station = -1, Seconds = FuelSeconds(Mathf.Min(fuelNeedKg, fuelStockKg)) };

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
                float stockLeft = munitionStockKg, fundsLeft = funds;
                foreach (PitStation s in candidates)
                {
                    int take = Take(s, s.Missing, ref stockLeft, ref fundsLeft, free);
                    if (take <= 0) continue;
                    if (s.Belt)
                        munitions.Add(new PitJob { Kind = PitJobKind.Cassette, Station = s.Index, Rounds = take, Seconds = CassetteSwap });
                    else
                        for (int k = 0; k < take; k++)
                            munitions.Add(new PitJob { Kind = PitJobKind.Handoff, Station = s.Index, Rounds = 1, Seconds = HandoffSeconds(k == 0, false) });
                }
            }

            if (wantFuel && order == PitOrder.Both && FuelFirst(fuelFraction)) into.Add(fuel);
            into.AddRange(munitions);
            if (wantFuel && !(order == PitOrder.Both && FuelFirst(fuelFraction))) into.Add(fuel);

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

    /// <summary>Where the loader starts a job: Home = arm already down (stop start, after fuel), Again = it just committed a round up at the rail.</summary>
    internal enum PitFrom { Home, Again }

    /// <summary>Client staging timelines, seconds since a job's state change; the visuals drive joints straight from these.</summary>
    internal static class PitStaging
    {
        public const float Slide = 0.7f, Up = 0.6f, Latch = 0.3f, Down = 0.5f;

        private static float S(float x) => Mathf.SmoothStep(0f, 1f, x);

        /// <summary>
        /// Loader lift 0..1 along the straight line from the glider's carousel up to the pylon: a job from Home waits for the glider
        /// to slide under the station (0.7 s), then rises 0.6 s and latches 0.3 s before the commit; a follow-on first lowers the arm
        /// 0.5 s to take the next round, rises 0.6 s and latches 0.3 s.
        /// </summary>
        public static float Reach(float t, PitFrom from)
        {
            if (from == PitFrom.Home) return S(Mathf.Clamp01((t - Slide) / Up));
            return t < Down ? Mathf.Lerp(1f, 0f, S(t / Down)) : S(Mathf.Clamp01((t - Down - 0.05f) / Up));
        }

        /// <summary>Cradle round visibility: a follow-on job regrows it from the belly magazine.</summary>
        public static float RoundScale(float t, bool hiddenAtStart) => hiddenAtStart ? S(Mathf.Clamp01((t - 0.30f) / 0.25f)) : 1f;

        /// <summary>Refuel boom plugged-in weight: plug, hold through the flow, unplug.</summary>
        public static float Connected(float t, float flowSeconds)
        {
            float held = PitPlan.FuelPlug + flowSeconds;
            return t < held ? S(Mathf.Clamp01((t - 0.25f) / 0.65f)) : 1f - S(Mathf.Clamp01((t - held) / 0.40f));
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
    }
}
