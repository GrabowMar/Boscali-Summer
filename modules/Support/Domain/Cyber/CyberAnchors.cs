using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    internal enum AnchorKind : byte { EwTruck = 0, DataCenter = 1 }
    internal enum AnchorHealth : byte { Live = 0, Damaged = 1, Down = 2 }

    internal static class AnchorRules
    {
        public const float DamagedHealth = 0.5f, GraceSeconds = 120f;
        public const float EwReach = 18000f, DamagedReachFactor = 0.6f;
        public const float RebuildTruck = 100f, RebuildDataCenter = 300f, AutoFundPerMinute = 40f;
        public const float TraceLockSeconds = 90f, TraceRevealSeconds = 120f;
        public const int MaxTrucks = 2, MaxDataCenters = 2;

        public static AnchorHealth Of(float healthFraction, bool down)
        {
            if (down) return AnchorHealth.Down;
            if (float.IsNaN(healthFraction) || float.IsInfinity(healthFraction)) return AnchorHealth.Down;
            return healthFraction <= DamagedHealth ? AnchorHealth.Damaged : AnchorHealth.Live;
        }

        public static float Reach(AnchorHealth health) =>
            health == AnchorHealth.Live ? EwReach : health == AnchorHealth.Damaged ? EwReach * DamagedReachFactor : 0f;

        public static float RebuildGoal(AnchorKind kind) => kind == AnchorKind.EwTruck ? RebuildTruck : RebuildDataCenter;
    }

    /// <summary>Core §7 restore fund: filled by the treasury at up to 40 CR per minute (an AI faction gets a flat seed instead).</summary>
    internal sealed class RebuildBar
    {
        public RebuildBar(float goal) { Goal = goal > 0f && !float.IsNaN(goal) && !float.IsInfinity(goal) ? goal : 1f; }

        public float Goal { get; }
        public float Value { get; private set; }
        public bool Complete => Value >= Goal;
        public float Fraction => Math.Min(1f, Value / Goal);

        /// <summary>Adds <paramref name="amount"/> (a player chip-in or an auto-fund). Returns what the bar actually took.</summary>
        public float Fund(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return 0f;
            float take = Math.Min(amount, Goal - Value);
            Value += take;
            return take;
        }

        /// <summary>Auto-fund for <paramref name="seconds"/>: the treasury pays what the bar takes unless <paramref name="flatSeed"/>. Returns the amount taken from the treasury.</summary>
        public float AutoFund(float seconds, float treasury, bool flatSeed)
        {
            if (float.IsNaN(seconds) || seconds <= 0f || float.IsNaN(treasury)) return 0f;
            float want = AnchorRules.AutoFundPerMinute / 60f * Math.Min(seconds, 60f);
            if (!flatSeed) want = Math.Min(want, Math.Max(0f, treasury));
            float took = Fund(want);
            return flatSeed ? 0f : took;
        }

        public void Reset() { Value = 0f; }
    }

    internal readonly struct SiteCandidate
    {
        public readonly int Id;
        public readonly float X, Z, RearScore, FrontDistance;
        public SiteCandidate(int id, float x, float z, float rearScore, float frontDistance) { Id = id; X = x; Z = z; RearScore = rearScore; FrontDistance = frontDistance; }
    }

    /// <summary>Spec §1.1: anchors sit at least 0.15 x the map diagonal from the front and, when a map allows two, at least that far apart.</summary>
    internal static class CyberPlacement
    {
        public const float FrontFraction = 0.15f, SeparationFraction = 0.15f;
        public const int MaxCandidates = 128;

        /// <summary>
        /// The best <paramref name="max"/> candidates: far enough from the front, best rear score first (ties by id), each at least the
        /// separation from the ones already chosen and at least <paramref name="avoidRadius"/> from every <paramref name="avoid"/> point.
        /// </summary>
        public static int Pick(IReadOnlyList<SiteCandidate> legal, float diagonal, int max, IReadOnlyList<SiteCandidate> avoid, float avoidRadius, List<int> chosen)
        {
            chosen.Clear();
            if (legal == null || legal.Count > MaxCandidates || max <= 0 || float.IsNaN(diagonal) || float.IsInfinity(diagonal) || diagonal <= 0f) return 0;
            double minFront = diagonal * (double)FrontFraction, minSep = diagonal * (double)SeparationFraction;
            var order = new List<SiteCandidate>();
            for (int i = 0; i < legal.Count; i++)
            {
                SiteCandidate c = legal[i];
                if (c.Id < 0 || float.IsNaN(c.X) || float.IsNaN(c.Z) || float.IsInfinity(c.X) || float.IsInfinity(c.Z) || float.IsNaN(c.RearScore) || float.IsNaN(c.FrontDistance)) continue;
                if (c.FrontDistance < minFront) continue;
                order.Add(c);
            }
            order.Sort((a, b) => { int r = b.RearScore.CompareTo(a.RearScore); return r != 0 ? r : a.Id.CompareTo(b.Id); });
            var picked = new List<SiteCandidate>();
            foreach (SiteCandidate c in order)
            {
                if (picked.Count >= max) break;
                bool ok = true;
                for (int k = 0; k < picked.Count && ok; k++) ok = Dist(c, picked[k]) >= minSep;
                for (int k = 0; avoid != null && k < avoid.Count && ok; k++) ok = Dist(c, avoid[k]) >= avoidRadius;
                if (!ok) continue;
                picked.Add(c);
                chosen.Add(c.Id);
            }
            return chosen.Count;
        }

        private static double Dist(in SiteCandidate a, in SiteCandidate b)
        {
            double dx = (double)a.X - b.X, dz = (double)a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>One faction's EW trucks and data centers as the host last measured them (health, position, trace lock). Mission seconds.</summary>
    internal sealed class CyberAnchorSet
    {
        private sealed class Slot
        {
            public AnchorHealth Health = AnchorHealth.Live;
            public float X, Z, DownSince = float.PositiveInfinity, LockUntil, RevealUntil;
            public bool Placed;
        }

        private readonly Slot[][] slots = new Slot[2][];

        public CyberAnchorSet(int trucks, int dataCenters)
        {
            slots[(int)AnchorKind.EwTruck] = Make(Math.Max(0, Math.Min(AnchorRules.MaxTrucks, trucks)));
            slots[(int)AnchorKind.DataCenter] = Make(Math.Max(0, Math.Min(AnchorRules.MaxDataCenters, dataCenters)));
        }

        private static Slot[] Make(int n)
        {
            var s = new Slot[n];
            for (int i = 0; i < n; i++) s[i] = new Slot();
            return s;
        }

        public int Count(AnchorKind kind) => slots[(int)kind].Length;

        public int LiveCount(AnchorKind kind)
        {
            int n = 0;
            foreach (Slot s in slots[(int)kind]) if (s.Health != AnchorHealth.Down) n++;
            return n;
        }

        /// <summary>The faction's data center bonuses apply while any data center stands (Live or Damaged).</summary>
        public bool DataCenterUp => LiveCount(AnchorKind.DataCenter) > 0;

        public AnchorHealth Health(AnchorKind kind, int index) =>
            InRange(kind, index) ? slots[(int)kind][index].Health : AnchorHealth.Down;

        public void SetPosition(AnchorKind kind, int index, float x, float z)
        {
            if (!InRange(kind, index) || float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return;
            Slot s = slots[(int)kind][index];
            s.X = x; s.Z = z; s.Placed = true;
        }

        public void Set(AnchorKind kind, int index, float healthFraction, bool down, float now)
        {
            if (!InRange(kind, index) || float.IsNaN(now) || now < 0f) return;
            Slot s = slots[(int)kind][index];
            AnchorHealth next = AnchorRules.Of(healthFraction, down);
            if (next == AnchorHealth.Down && s.Health != AnchorHealth.Down) s.DownSince = now;
            else if (next != AnchorHealth.Down) s.DownSince = float.PositiveInfinity;
            s.Health = next;
        }

        public float DownSince(AnchorKind kind, int index) => InRange(kind, index) ? slots[(int)kind][index].DownSince : float.PositiveInfinity;

        /// <summary>True once an anchor has been down for the 120 s grace: the rebuild bar may start filling.</summary>
        public bool PastGrace(AnchorKind kind, int index, float now) => now - DownSince(kind, index) >= AnchorRules.GraceSeconds;

        /// <summary>The anchor is standing again after a rebuild: back to Live with a fresh position.</summary>
        public void Restore(AnchorKind kind, int index, float x, float z, float now)
        {
            Set(kind, index, 1f, false, now);
            SetPosition(kind, index, x, z);
        }

        public bool TruckLocked(int index, float now) => InRange(AnchorKind.EwTruck, index) && now < slots[0][index].LockUntil;

        /// <summary>The truck was traced: it cannot start intrusions for 90 s and its position is revealed to the enemy for 120 s.</summary>
        public void Trace(int index, float now)
        {
            if (!InRange(AnchorKind.EwTruck, index)) return;
            slots[0][index].LockUntil = now + AnchorRules.TraceLockSeconds;
            slots[0][index].RevealUntil = now + AnchorRules.TraceRevealSeconds;
        }

        public float LockUntil(int index) => InRange(AnchorKind.EwTruck, index) ? slots[0][index].LockUntil : 0f;
        public float RevealUntil(int index) => InRange(AnchorKind.EwTruck, index) ? slots[0][index].RevealUntil : 0f;

        public bool TryPosition(AnchorKind kind, int index, out float x, out float z)
        {
            x = z = 0f;
            if (!InRange(kind, index) || !slots[(int)kind][index].Placed) return false;
            x = slots[(int)kind][index].X; z = slots[(int)kind][index].Z;
            return true;
        }

        /// <summary>Every truck that is not down, with its health-scaled reach (a locked truck still draws its circle).</summary>
        public int CopyTrucks(List<EwSource> into)
        {
            into.Clear();
            for (int i = 0; i < slots[0].Length; i++)
            {
                Slot s = slots[0][i];
                float reach = AnchorRules.Reach(s.Health);
                if (s.Placed && reach > 0f) into.Add(new EwSource(i, s.X, s.Z, reach));
            }
            return into.Count;
        }

        private bool InRange(AnchorKind kind, int index) => (int)kind < slots.Length && index >= 0 && index < slots[(int)kind].Length;
    }
}
